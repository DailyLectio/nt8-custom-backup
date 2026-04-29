// CC BY-NC 4.0
// ─────────────────────────────────────────────────────────────────────────────
// MomentumHA_Scalp_1m.cs  —  Bot A
// ─────────────────────────────────────────────────────────────────────────────
// THESIS        : Compression scalper on 1-minute Heikin Ashi bars.
//                 Enter on DI cross confirmed by CI/ADX gates + HA bar direction.
//                 Bank Leg 1 at fixed 1.5R. Run Leg 2 purely on HA color flip
//                 OR hysteresis slope exit — whichever fires first.
//
// CHART TYPE    : 1-minute Heikin Ashi  (Calculate.OnPriceChange)
// INSTRUMENT    : NQ / MNQ (MNQ default — TickValue = $0.50)
// REGIME GATE   : HMM CSV primary (TREND_COMPRESSION or TREND_EXPANSION).
//                 ATR-ratio secondary gate — checkbox toggle for SIM testing.
//
// LEGS          : Up to 4 per direction. Defaults: Leg1=1C, Leg2=1C, Leg3=0, Leg4=0.
//                 All contract counts and tick targets/stops are exposed as settings.
//
// KEY FEATURES  :
//   - Manual CI series (hand-computed, no NT8 built-in required)
//   - Wilder DI+ / DI- smoothing (matches Pine f_dirMov exactly)
//   - DI Separation quality gate (min separation at cross = filter for weak crosses)
//   - Inline HA calculation (open/close series — no HA indicator DLL needed)
//   - Hysteresis slope exit (per-bar CI rise / ADX drop accumulator)
//   - HA color-flip exit on Leg 2 runner
//   - Breakeven ratchet — stop moves to BE+N ticks when Leg 1 exits
//   - VWAP extension filter — blocks entries too far extended from session VWAP
//   - Consecutive-loss pause (N bars) — softer than hard stop
//   - Session PnL isolation (sessionStartProfit)
//   - PnL Shield (trigger → lock floor) — optional, can be zeroed out
//   - 3-window time block system
//   - HMM CSV file watcher + stale-data detection
//   - ATR-ratio regime gate (checkbox) — toggled independently for SIM testing
//   - All feature toggles exposed as boolean properties in settings
//
// RISK GUARDS   : External account manager handles daily drawdown.
//                 Shield and consecutive-loss pause are included but can be
//                 zeroed / disabled without affecting signal logic.
//
// PARAMETER GROUPS (in NT8 Strategy Properties UI):
//   1. Regime         — CSV path, CSV gates, ATR-ratio gate toggle
//   2. Signal         — CI/ADX periods, thresholds, DI separation gate
//   3. Legs           — Qty and stop ticks per leg (1-4)
//   4. Targets        — Tick target per leg, BE ratchet ticks
//   5. Exit           — Slope mode, hysteresis params, HA flip gate toggle
//   6. Filters        — VWAP extension, anchor mode
//   7. Shield         — Trigger/lock (set to 0 to disable)
//   8. Timing         — Start/End, 3 block windows
// ─────────────────────────────────────────────────────────────────────────────

#region Using
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class MomentumHA_Scalp_1m : Strategy
    {
        // =====================================================================
        // ENUMS
        // =====================================================================
        public enum ExitSlopeMode { None, Simple, Hysteresis }

        // =====================================================================
        // 1. REGIME PARAMETERS
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "HMM Data Folder Path", GroupName = "1. Regime", Order = 0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D";

        [NinjaScriptProperty]
        [Display(Name = "Enable HMM CSV Gate", GroupName = "1. Regime", Order = 1,
                 Description = "Primary regime gate. When ON, requires FinalRegime = TREND_COMPRESSION or TREND_EXPANSION from HMM CSV file.")]
        public bool EnableHmmGate { get; set; } = true;

        [NinjaScriptProperty]
        [Range(50, 100)]
        [Display(Name = "Min Regime Confidence", GroupName = "1. Regime", Order = 2,
                 Description = "HMM CSV: entry blocked if RegimeConfidence below this value.")]
        public int MinConfidence { get; set; } = 65;

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Max Conflict Score", GroupName = "1. Regime", Order = 3,
                 Description = "HMM CSV: entry blocked if ConflictScore >= this threshold. 0.40 = default.")]
        public double MaxConflictScore { get; set; } = 0.40;

        [NinjaScriptProperty]
        [Display(Name = "Enable ATR-Ratio Secondary Gate", GroupName = "1. Regime", Order = 4,
                 Description = "Toggle ON/OFF for SIM testing. When ON, requires current ATR to be within regime-appropriate range before entry.")]
        public bool EnableAtrRatioGate { get; set; } = false;

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "ATR Baseline Period (ratio gate)", GroupName = "1. Regime", Order = 5,
                 Description = "SMA period for ATR baseline used in ATR-ratio secondary gate.")]
        public int AtrBaselinePeriod { get; set; } = 50;

        [NinjaScriptProperty]
        [Range(0.3, 0.95)]
        [Display(Name = "Compression Max Ratio", GroupName = "1. Regime", Order = 6,
                 Description = "ATR/Baseline must be BELOW this to confirm Compression. Default 0.85.")]
        public double CompressionMaxRatio { get; set; } = 0.85;

        [NinjaScriptProperty]
        [Range(1.0, 2.5)]
        [Display(Name = "Expansion Min Ratio", GroupName = "1. Regime", Order = 7,
                 Description = "ATR/Baseline must be ABOVE this to confirm Expansion. Default 1.10.")]
        public double ExpansionMinRatio { get; set; } = 1.10;

        // =====================================================================
        // 2. SIGNAL PARAMETERS
        // =====================================================================
        [NinjaScriptProperty, Range(2, 100)]
        [Display(Name = "CI Period", GroupName = "2. Signal", Order = 0)]
        public int CiPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(2, 100)]
        [Display(Name = "ADX Period", GroupName = "2. Signal", Order = 1)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "CI Entry Threshold (<=)", GroupName = "2. Signal", Order = 2,
                 Description = "CI must be at or below this value for entry. 60 = default for compression.")]
        public double CiEntryThreshold { get; set; } = 60.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "ADX Entry Threshold (>=)", GroupName = "2. Signal", Order = 3,
                 Description = "ADX must be at or above this floor for entry.")]
        public double AdxEntryThreshold { get; set; } = 18.0;

        [NinjaScriptProperty]
        [Display(Name = "Enable DI Separation Gate", GroupName = "2. Signal", Order = 4,
                 Description = "Toggle for SIM testing. When ON, requires minimum DI separation at cross to filter weak crosses.")]
        public bool EnableDiSeparationGate { get; set; } = true;

        [NinjaScriptProperty, Range(0.0, 30.0)]
        [Display(Name = "Min DI Separation at Cross", GroupName = "2. Signal", Order = 5,
                 Description = "Minimum |DI+ - DI-| at cross moment. Filters low-conviction crosses. Default 5.0.")]
        public double MinDiSeparation { get; set; } = 5.0;

        [NinjaScriptProperty]
        [Display(Name = "Enable Entry Bar Confirmation", GroupName = "2. Signal", Order = 6,
                 Description = "Toggle for SIM testing. When ON, the entry bar itself must close in the signal direction.")]
        public bool EnableEntryBarConfirm { get; set; } = true;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period (stops)", GroupName = "2. Signal", Order = 7)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.1, 3.0)]
        [Display(Name = "ATR Stop Multiplier", GroupName = "2. Signal", Order = 8,
                 Description = "Stop distance = ATR * this multiplier. 0.75 default for 1m HA scalp.")]
        public double AtrStopMult { get; set; } = 0.75;

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Min Stop Ticks", GroupName = "2. Signal", Order = 9,
                 Description = "Hard floor on stop distance regardless of ATR. Default 8 ticks.")]
        public int MinStopTicks { get; set; } = 8;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value $ (NQ=5.00, MNQ=0.50)", GroupName = "2. Signal", Order = 10)]
        public double TickValueDollars { get; set; } = 0.50;

        // =====================================================================
        // 3. LEG CONTRACT QUANTITIES
        // =====================================================================
        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Leg 1 Contracts (Banker)", GroupName = "3. Legs", Order = 0,
                 Description = "Contracts for Leg 1. Set to 0 to disable this leg.")]
        public int Leg1Qty { get; set; } = 1;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Leg 2 Contracts (Runner)", GroupName = "3. Legs", Order = 1,
                 Description = "Contracts for Leg 2. Set to 0 to disable this leg.")]
        public int Leg2Qty { get; set; } = 1;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Leg 3 Contracts", GroupName = "3. Legs", Order = 2,
                 Description = "Contracts for Leg 3. Set to 0 to disable (default 0).")]
        public int Leg3Qty { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Leg 4 Contracts", GroupName = "3. Legs", Order = 3,
                 Description = "Contracts for Leg 4. Set to 0 to disable (default 0).")]
        public int Leg4Qty { get; set; } = 0;

        // =====================================================================
        // 4. TARGETS
        // =====================================================================
        [NinjaScriptProperty, Range(1, 500)]
        [Display(Name = "Leg 1 Target Ticks (1.5R fixed)", GroupName = "4. Targets", Order = 0,
                 Description = "Fixed tick target for Leg 1 (Banker). Overridden at runtime by ATR*mult*1.5. This value is the SIM fallback floor.")]
        public int Leg1TargetTicks { get; set; } = 30;

        [NinjaScriptProperty, Range(1, 500)]
        [Display(Name = "Leg 3 Target Ticks", GroupName = "4. Targets", Order = 1,
                 Description = "Fixed tick target for Leg 3 if enabled. Default 60.")]
        public int Leg3TargetTicks { get; set; } = 60;

        [NinjaScriptProperty, Range(1, 500)]
        [Display(Name = "Leg 4 Target Ticks", GroupName = "4. Targets", Order = 2,
                 Description = "Fixed tick target for Leg 4 if enabled. Default 90.")]
        public int Leg4TargetTicks { get; set; } = 90;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Breakeven Ratchet Ticks (BE+N)", GroupName = "4. Targets", Order = 3,
                 Description = "After Leg 1 exits, move remaining legs' stop to entry + this many ticks. 0 = move to exact breakeven.")]
        public int BeRatchetTicks { get; set; } = 2;

        // =====================================================================
        // 5. EXIT PARAMETERS
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "Leg 2 Slope Exit Mode", GroupName = "5. Exit", Order = 0,
                 Description = "Slope exit mode for Leg 2 runner. Hysteresis = most robust on 1m HA.")]
        public ExitSlopeMode SlopeExit { get; set; } = ExitSlopeMode.Hysteresis;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Min Hold Bars Before Slope Exit", GroupName = "5. Exit", Order = 1)]
        public int MinHoldBars { get; set; } = 2;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "CI Slope Bars (Simple exit)", GroupName = "5. Exit", Order = 2)]
        public int CiSlopeBarsExit { get; set; } = 5;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "ADX Slope Bars (Simple exit)", GroupName = "5. Exit", Order = 3)]
        public int AdxSlopeBarsExit { get; set; } = 5;

        [NinjaScriptProperty, Range(0.0, 20.0)]
        [Display(Name = "Min CI Rise to Exit (Simple)", GroupName = "5. Exit", Order = 4)]
        public double CiRiseMinExit { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0.0, 20.0)]
        [Display(Name = "Min ADX Drop to Exit (Simple)", GroupName = "5. Exit", Order = 5)]
        public double AdxDropMinExit { get; set; } = 2.0;

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name = "Hysteresis Consecutive Fails (M)", GroupName = "5. Exit", Order = 6,
                 Description = "Number of consecutive per-bar failures before hysteresis exit fires.")]
        public int HystConsecutive { get; set; } = 2;

        [NinjaScriptProperty, Range(0.0, 10.0)]
        [Display(Name = "Hysteresis CI Rise/Bar", GroupName = "5. Exit", Order = 7)]
        public double HystCiRisePerBar { get; set; } = 0.5;

        [NinjaScriptProperty, Range(0.0, 10.0)]
        [Display(Name = "Hysteresis ADX Drop/Bar", GroupName = "5. Exit", Order = 8)]
        public double HystAdxDropPerBar { get; set; } = 0.5;

        [NinjaScriptProperty]
        [Display(Name = "Enable HA Color-Flip Exit (Leg 2)", GroupName = "5. Exit", Order = 9,
                 Description = "Toggle for SIM testing. When ON, Leg 2 also exits on Heikin Ashi color flip.")]
        public bool EnableHaFlipExit { get; set; } = true;

        // =====================================================================
        // 6. FILTERS
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "Enable VWAP Extension Filter", GroupName = "6. Filters", Order = 0,
                 Description = "Toggle for SIM testing. Blocks entries when price is too far extended from session VWAP.")]
        public bool EnableVwapExtFilter { get; set; } = true;

        [NinjaScriptProperty, Range(0.5, 5.0)]
        [Display(Name = "VWAP Extension Max (x ATR)", GroupName = "6. Filters", Order = 1,
                 Description = "If |Close - VWAP| > ATR * this, block entry. Default 2.0.")]
        public double VwapExtensionMaxAtr { get; set; } = 2.0;

        // =====================================================================
        // 7. SHIELD (set both to 0 to disable entirely)
        // =====================================================================
        [NinjaScriptProperty, Range(0.0, 100000.0)]
        [Display(Name = "Shield Trigger $ (0=off)", GroupName = "7. Shield", Order = 0,
                 Description = "Once daily PnL reaches this, lock floor activates. Set 0 to disable.")]
        public double ShieldTrigger { get; set; } = 0;

        [NinjaScriptProperty, Range(0.0, 100000.0)]
        [Display(Name = "Shield Lock $ (0=off)", GroupName = "7. Shield", Order = 1,
                 Description = "If Shield is active and PnL drops below this, halt for session.")]
        public double ShieldLock { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Consecutive Loss Pause (bars)", GroupName = "7. Shield", Order = 2,
                 Description = "After 2 consecutive losses, pause this many bars before re-arming. 0 = no pause.")]
        public int ConsecLossPauseBars { get; set; } = 10;

        // =====================================================================
        // 8. TIMING
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "8. Timing", Order = 0)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Start Time (HHmmss)", GroupName = "8. Timing", Order = 1)]
        public int StartTime { get; set; } = 93500;

        [NinjaScriptProperty]
        [Display(Name = "End Time (HHmmss)", GroupName = "8. Timing", Order = 2)]
        public int EndTime { get; set; } = 113000;

        [NinjaScriptProperty]
        [Display(Name = "Use Block Window 1", GroupName = "8. Timing", Order = 3)]
        public bool UseBlock1 { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Block 1 Start (HHmmss)", GroupName = "8. Timing", Order = 4)]
        public int Block1Start { get; set; } = 95900;

        [NinjaScriptProperty]
        [Display(Name = "Block 1 End (HHmmss)", GroupName = "8. Timing", Order = 5)]
        public int Block1End { get; set; } = 100600;

        [NinjaScriptProperty]
        [Display(Name = "Use Block Window 2", GroupName = "8. Timing", Order = 6)]
        public bool UseBlock2 { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Block 2 Start (HHmmss)", GroupName = "8. Timing", Order = 7)]
        public int Block2Start { get; set; } = 110000;

        [NinjaScriptProperty]
        [Display(Name = "Block 2 End (HHmmss)", GroupName = "8. Timing", Order = 8)]
        public int Block2End { get; set; } = 110500;

        [NinjaScriptProperty]
        [Display(Name = "Use Block Window 3", GroupName = "8. Timing", Order = 9)]
        public bool UseBlock3 { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Block 3 Start (HHmmss)", GroupName = "8. Timing", Order = 10)]
        public int Block3Start { get; set; } = 120000;

        [NinjaScriptProperty]
        [Display(Name = "Block 3 End (HHmmss)", GroupName = "8. Timing", Order = 11)]
        public int Block3End { get; set; } = 120500;

        // =====================================================================
        // INDICATORS
        // =====================================================================
        private ATR atrStop;
        private ATR atrBaseline;   // for ATR-ratio secondary gate
        private ADX adx;

        // CI internals (manual — no NT8 built-in)
        private Series<double> trSeries;
        private SUM  sumTr;
        private MAX  maxHigh;
        private MIN  minLow;
        private Series<double> ci;

        // Wilder DI internals
        private Series<double> dmPlus, dmMinus;
        private Series<double> sumDmPlus, sumDmMinus, sumTrDI;
        private Series<double> diPlusSeries, diMinusSeries;

        // Session VWAP (internal)
        private Series<double> sessionVwap;
        private double cumPV, cumVol;

        // ATR SMA baseline for ratio gate
        private SMA atrSmaBaseline;

        // =====================================================================
        // HEIKIN ASHI (inline — no indicator DLL needed)
        // =====================================================================
        private double haOpen  = 0;
        private double haClose = 0;

        // =====================================================================
        // REGIME STATE (from HMM CSV)
        // =====================================================================
        private string   matrixFile       = "";
        private DateTime lastFileWriteUtc = DateTime.MinValue;
        private DateTime lastFileCheck    = DateTime.MinValue;
        private const int MinCheckSeconds = 15;
        private FileSystemWatcher regimeWatcher;
        private readonly object   fileLock = new object();

        private volatile string finalRegime      = "UNKNOWN";
        private volatile int    regimeConfidence = 0;
        private          double conflictScore    = 1.0;
        private volatile bool   allowLong        = false;
        private volatile bool   allowShort       = false;
        private volatile bool   staleDataFlag    = true;
        private volatile bool   parseFailed      = true;

        private Dictionary<string, int> headerIdx = new Dictionary<string, int>();

        // =====================================================================
        // RUNTIME STATE
        // =====================================================================
        private double sessionStartProfit = 0;
        private double pnlFloor          = double.MinValue;
        private bool   shieldActive      = false;
        private bool   tradingHalted     = false;

        private int  consecutiveLosers  = 0;
        private int  lastTradeCount     = 0;
        private int  pauseBarsRemaining = 0;

        private int    hystFailCount              = 0;
        private bool   circuitBreakerFiredThisBar = false;
        private bool   leg1HasExited              = false;
        private double entryPrice                 = 0;

        // Entry signal names
        private const string L1 = "HA_L1";
        private const string L2 = "HA_L2";
        private const string L3 = "HA_L3";
        private const string L4 = "HA_L4";
        private const string S1 = "HA_S1";
        private const string S2 = "HA_S2";
        private const string S3 = "HA_S3";
        private const string S4 = "HA_S4";

        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "MomentumHA_Scalp_1m";
                Description                  = "Bot A — 1m HA Compression Scalper | Banker+Runner | HMM Gated";
                Calculate                    = Calculate.OnPriceChange;
                EntriesPerDirection          = 4;
                EntryHandling                = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                RealtimeErrorHandling        = RealtimeErrorHandling.StopCancelClose;
                TraceOrders                  = false;
            }
            else if (State == State.DataLoaded)
            {
                string sym = Instrument.MasterInstrument.Name.Trim().ToUpper();
                if (sym == "MNQ") sym = "NQ";
                if (sym == "MES") sym = "ES";
                matrixFile = Path.Combine(DataFolderPath, sym + "_RegimeMatrix_Latest.csv");

                adx        = ADX(AdxPeriod);
                atrStop    = ATR(AtrPeriod);
                atrBaseline= ATR(AtrPeriod);

                trSeries = new Series<double>(this);
                sumTr    = SUM(trSeries, CiPeriod);
                maxHigh  = MAX(High, CiPeriod);
                minLow   = MIN(Low,  CiPeriod);
                ci       = new Series<double>(this);

                dmPlus        = new Series<double>(this);
                dmMinus       = new Series<double>(this);
                sumDmPlus     = new Series<double>(this);
                sumDmMinus    = new Series<double>(this);
                sumTrDI       = new Series<double>(this);
                diPlusSeries  = new Series<double>(this);
                diMinusSeries = new Series<double>(this);

                sessionVwap = new Series<double>(this);

                SetupFileWatcher();
                lastTradeCount = SystemPerformance.AllTrades.Count;
            }
            else if (State == State.Terminated)
            {
                TeardownFileWatcher();
            }
        }

        // =====================================================================
        // MAIN BAR UPDATE
        // =====================================================================
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2) return;
            int warmup = Math.Max(AdxPeriod, Math.Max(CiPeriod, AtrPeriod)) + 4;
            if (CurrentBar < warmup) return;

            // Reset per-bar flags
            circuitBreakerFiredThisBar = false;

            // ── Session reset ────────────────────────────────────────────
            if (Bars.IsFirstBarOfSession)
            {
                cumPV = 0; cumVol = 0;
                hystFailCount     = 0;
                consecutiveLosers = 0;
                pauseBarsRemaining = 0;
                leg1HasExited     = false;
                tradingHalted     = false;
                shieldActive      = false;
                pnlFloor          = double.MinValue;
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }

            // ── VWAP update ──────────────────────────────────────────────
            double typ = (High[0] + Low[0] + Close[0]) / 3.0;
            double vol = Math.Max(1.0, Volume[0]);
            cumPV  += typ * vol;
            cumVol += vol;
            sessionVwap[0] = cumPV / cumVol;

            // ── Heikin Ashi (inline, based on true OHLC) ─────────────────
            haClose = (Open[0] + High[0] + Low[0] + Close[0]) * 0.25;
            haOpen  = CurrentBar == 0 ? Open[0] : (haOpen + haClose) * 0.5;
            bool haIsBull = haClose >= haOpen;

            // ── CI computation ───────────────────────────────────────────
            double trBar  = Math.Max(High[0] - Low[0],
                            Math.Max(Math.Abs(High[0] - Close[1]),
                                     Math.Abs(Low[0]  - Close[1])));
            trSeries[0] = trBar;
            double range    = maxHigh[0] - minLow[0];
            double sumTrVal = Math.Max(1e-9, sumTr[0]);
            double denom    = Math.Log10(Math.Max(2, CiPeriod));
            double numer    = (range <= 1e-9) ? 1.0 : (sumTrVal / Math.Max(1e-9, range));
            ci[0] = Math.Max(0.0, Math.Min(100.0, 100.0 * Math.Log10(numer) / denom));

            // ── Wilder DI computation ────────────────────────────────────
            double h0 = High[0], l0 = Low[0], h1 = High[1], l1 = Low[1], c1 = Close[1];
            double tr2  = Math.Max(h0 - l0, Math.Max(Math.Abs(h0 - c1), Math.Abs(l0 - c1)));
            double up   = h0 - h1;
            double down = l1 - l0;
            double dmp  = (up   > 0 && up   > down) ? up   : 0;
            double dmn  = (down > 0 && down > up)   ? down : 0;

            if (CurrentBar < AdxPeriod)
            {
                sumTrDI[0]    = sumTrDI[1]    + tr2;
                sumDmPlus[0]  = sumDmPlus[1]  + dmp;
                sumDmMinus[0] = sumDmMinus[1] + dmn;
            }
            else
            {
                sumTrDI[0]    = sumTrDI[1]    - (sumTrDI[1]    / AdxPeriod) + tr2;
                sumDmPlus[0]  = sumDmPlus[1]  - (sumDmPlus[1]  / AdxPeriod) + dmp;
                sumDmMinus[0] = sumDmMinus[1] - (sumDmMinus[1] / AdxPeriod) + dmn;
            }
            double sTr       = sumTrDI[0].ApproxCompare(0) == 0 ? 1e-9 : sumTrDI[0];
            diPlusSeries[0]  = 100.0 * (sumDmPlus[0]  / sTr);
            diMinusSeries[0] = 100.0 * (sumDmMinus[0] / sTr);

            bool crossUp   = CrossAbove(diPlusSeries, diMinusSeries, 1);
            bool crossDown = CrossBelow(diPlusSeries, diMinusSeries, 1);

            // ── Refresh regime from CSV ──────────────────────────────────
            RefreshRegimeState();

            // ── Shield check ─────────────────────────────────────────────
            if (ShieldTrigger > 0 || ShieldLock > 0)
            {
                double dailyPnL = SystemPerformance.AllTrades
                                      .TradesPerformance.Currency.CumProfit
                                  - sessionStartProfit;
                if (!shieldActive && ShieldTrigger > 0 && dailyPnL >= ShieldTrigger)
                {
                    shieldActive = true;
                    pnlFloor     = ShieldLock;
                }
                if (shieldActive && ShieldLock > 0 && dailyPnL <= pnlFloor)
                {
                    tradingHalted = true;
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong("ShieldHalt", "");
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort("ShieldHalt", "");
                }
            }

            // ── Consecutive loser tracking ────────────────────────────────
            int tc = SystemPerformance.AllTrades.Count;
            if (tc > lastTradeCount)
            {
                var last = SystemPerformance.AllTrades[tc - 1];
                if (last.ProfitCurrency < 0)
                {
                    consecutiveLosers++;
                    if (consecutiveLosers >= 2 && ConsecLossPauseBars > 0)
                        pauseBarsRemaining = ConsecLossPauseBars;
                }
                else
                    consecutiveLosers = 0;
                lastTradeCount = tc;
            }
            if (pauseBarsRemaining > 0) pauseBarsRemaining--;

            // ── Circuit breaker exit (DI counter-cross) ──────────────────
            if (Position.MarketPosition == MarketPosition.Long && crossDown)
            {
                ExitLong("CBLong", ""); ExitLong("CBLong", L1);
                ExitLong("CBLong", L2); ExitLong("CBLong", L3); ExitLong("CBLong", L4);
                hystFailCount = 0; circuitBreakerFiredThisBar = true;
            }
            else if (Position.MarketPosition == MarketPosition.Short && crossUp)
            {
                ExitShort("CBShort", ""); ExitShort("CBShort", S1);
                ExitShort("CBShort", S2); ExitShort("CBShort", S3); ExitShort("CBShort", S4);
                hystFailCount = 0; circuitBreakerFiredThisBar = true;
            }

            // ── Regime TRANSITION: immediate flat ────────────────────────
            if (finalRegime == "TRANSITION" && Position.MarketPosition != MarketPosition.Flat)
            {
                ExitLong("TransitionExit", ""); ExitShort("TransitionExit", "");
                return;
            }

            // ── Breakeven ratchet (after Leg 1 exits) ────────────────────
            if (leg1HasExited && Position.MarketPosition != MarketPosition.Flat)
                ApplyBeRatchet();

            // ── Slope exit (Leg 2 runner) ────────────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                string ent = Position.MarketPosition == MarketPosition.Long ? L2 : S2;
                int bse = BarsSinceEntryExecution(0, ent, 0);

                if (bse >= MinHoldBars)
                {
                    bool exitNow = false;

                    // HA color-flip exit (if enabled)
                    if (EnableHaFlipExit)
                    {
                        if (Position.MarketPosition == MarketPosition.Long  && !haIsBull) exitNow = true;
                        if (Position.MarketPosition == MarketPosition.Short &&  haIsBull) exitNow = true;
                    }

                    // Slope exit
                    if (!exitNow && SlopeExit == ExitSlopeMode.Simple &&
                        CurrentBar > Math.Max(CiSlopeBarsExit, AdxSlopeBarsExit))
                    {
                        double ciRise  = ci[0]  - ci[CiSlopeBarsExit];
                        double adxDrop = adx[AdxSlopeBarsExit] - adx[0];
                        exitNow = ciRise >= CiRiseMinExit || adxDrop >= AdxDropMinExit;
                    }
                    else if (!exitNow && SlopeExit == ExitSlopeMode.Hysteresis && CurrentBar > 1)
                    {
                        double ciBar   = ci[0]  - ci[1];
                        double adxBar  = adx[1] - adx[0];
                        bool   thisFail = ciBar >= HystCiRisePerBar || adxBar >= HystAdxDropPerBar;
                        if (thisFail) hystFailCount++;
                        else          hystFailCount = Math.Max(0, hystFailCount - 1);
                        exitNow = hystFailCount >= HystConsecutive;
                    }

                    if (exitNow)
                    {
                        if (Position.MarketPosition == MarketPosition.Long)
                        { ExitLong("SlopeX_L2", L2); ExitLong("SlopeX_L3", L3); ExitLong("SlopeX_L4", L4); }
                        else
                        { ExitShort("SlopeX_S2", S2); ExitShort("SlopeX_S3", S3); ExitShort("SlopeX_S4", S4); }
                        hystFailCount = 0;
                    }
                }
            }

            // ── Entry logic ──────────────────────────────────────────────
            if (Position.MarketPosition == MarketPosition.Flat
                && !tradingHalted
                && !circuitBreakerFiredThisBar
                && pauseBarsRemaining <= 0
                && IsInTime())
            {
                // Regime gates
                if (!PassesRegimeGates()) return;

                // CI / ADX gates
                if (ci[0]  > CiEntryThreshold)   return;
                if (adx[0] < AdxEntryThreshold)   return;

                // DI separation quality gate
                double diSep = Math.Abs(diPlusSeries[0] - diMinusSeries[0]);
                if (EnableDiSeparationGate && diSep < MinDiSeparation) return;

                // VWAP extension filter
                if (EnableVwapExtFilter)
                {
                    double vwapDist = Math.Abs(Close[0] - sessionVwap[0]);
                    if (vwapDist > atrStop[0] * VwapExtensionMaxAtr) return;
                }

                // ATR-ratio secondary gate
                if (EnableAtrRatioGate && !PassesAtrRatioGate()) return;

                if (crossUp && (allowLong || !EnableHmmGate))
                {
                    // Entry bar confirmation: HA must be bullish
                    if (EnableEntryBarConfirm && !haIsBull) return;
                    SubmitLongEntry();
                }
                else if (crossDown && (allowShort || !EnableHmmGate))
                {
                    if (EnableEntryBarConfirm && haIsBull) return;
                    SubmitShortEntry();
                }
            }
        }

        // =====================================================================
        // ENTRY SUBMISSION
        // =====================================================================
        private void SubmitLongEntry()
        {
            entryPrice    = Close[0];
            leg1HasExited = false;

            double atrVal  = atrStop[0];
            double risk    = Math.Max(atrVal * AtrStopMult, MinStopTicks * TickSize);
            double stp     = RT(entryPrice - risk);
            double tgt1    = RT(entryPrice + risk * 1.5);
            int    tgt1Tks = Math.Max(Leg1TargetTicks, (int)Math.Round(risk * 1.5 / TickSize));

            if (Leg1Qty > 0)
            {
                SetStopLoss(L1, CalculationMode.Price, stp, false);
                SetProfitTarget(L1, CalculationMode.Ticks, tgt1Tks);
                EnterLong(Leg1Qty, L1);
            }
            if (Leg2Qty > 0)
            {
                SetStopLoss(L2, CalculationMode.Price, stp, false);
                // Leg 2: no fixed target — managed by slope/HA exit
                EnterLong(Leg2Qty, L2);
            }
            if (Leg3Qty > 0)
            {
                SetStopLoss(L3, CalculationMode.Price, stp, false);
                SetProfitTarget(L3, CalculationMode.Ticks, Leg3TargetTicks);
                EnterLong(Leg3Qty, L3);
            }
            if (Leg4Qty > 0)
            {
                SetStopLoss(L4, CalculationMode.Price, stp, false);
                SetProfitTarget(L4, CalculationMode.Ticks, Leg4TargetTicks);
                EnterLong(Leg4Qty, L4);
            }

            Print(string.Format("[HA_Scalp] LONG | Regime:{0} | CI:{1:F1} | ADX:{2:F1} | " +
                                "DiSep:{3:F1} | ATR:{4:F2} | Stop:{5:F2} | Tgt1:{6:F2}",
                                finalRegime, ci[0], adx[0],
                                Math.Abs(diPlusSeries[0] - diMinusSeries[0]),
                                atrVal, stp, tgt1));
        }

        private void SubmitShortEntry()
        {
            entryPrice    = Close[0];
            leg1HasExited = false;

            double atrVal  = atrStop[0];
            double risk    = Math.Max(atrVal * AtrStopMult, MinStopTicks * TickSize);
            double stp     = RT(entryPrice + risk);
            int    tgt1Tks = Math.Max(Leg1TargetTicks, (int)Math.Round(risk * 1.5 / TickSize));

            if (Leg1Qty > 0)
            {
                SetStopLoss(S1, CalculationMode.Price, stp, false);
                SetProfitTarget(S1, CalculationMode.Ticks, tgt1Tks);
                EnterShort(Leg1Qty, S1);
            }
            if (Leg2Qty > 0)
            {
                SetStopLoss(S2, CalculationMode.Price, stp, false);
                EnterShort(Leg2Qty, S2);
            }
            if (Leg3Qty > 0)
            {
                SetStopLoss(S3, CalculationMode.Price, stp, false);
                SetProfitTarget(S3, CalculationMode.Ticks, Leg3TargetTicks);
                EnterShort(Leg3Qty, S3);
            }
            if (Leg4Qty > 0)
            {
                SetStopLoss(S4, CalculationMode.Price, stp, false);
                SetProfitTarget(S4, CalculationMode.Ticks, Leg4TargetTicks);
                EnterShort(Leg4Qty, S4);
            }

            Print(string.Format("[HA_Scalp] SHORT | Regime:{0} | CI:{1:F1} | ADX:{2:F1} | " +
                                "DiSep:{3:F1} | ATR:{4:F2} | Stop:{5:F2}",
                                finalRegime, ci[0], adx[0],
                                Math.Abs(diPlusSeries[0] - diMinusSeries[0]),
                                atrVal, stp));
        }

        // =====================================================================
        // BREAKEVEN RATCHET
        // =====================================================================
        private void ApplyBeRatchet()
        {
            if (entryPrice <= 0) return;
            double bePrice = Position.MarketPosition == MarketPosition.Long
                ? RT(entryPrice + BeRatchetTicks * TickSize)
                : RT(entryPrice - BeRatchetTicks * TickSize);

            // Only move stop if it improves position (never loosen)
            if (Position.MarketPosition == MarketPosition.Long)
            {
                SetStopLoss(L2, CalculationMode.Price, bePrice, false);
                if (Leg3Qty > 0) SetStopLoss(L3, CalculationMode.Price, bePrice, false);
                if (Leg4Qty > 0) SetStopLoss(L4, CalculationMode.Price, bePrice, false);
            }
            else
            {
                SetStopLoss(S2, CalculationMode.Price, bePrice, false);
                if (Leg3Qty > 0) SetStopLoss(S3, CalculationMode.Price, bePrice, false);
                if (Leg4Qty > 0) SetStopLoss(S4, CalculationMode.Price, bePrice, false);
            }
        }

        // =====================================================================
        // EXECUTION UPDATE — track Leg 1 exit to trigger BE ratchet
        // =====================================================================
        protected override void OnExecutionUpdate(
            Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.IsEntry) return;

            // If Leg 1 exits by any means, arm the BE ratchet
            string name = execution.Order != null ? execution.Order.Name : "";
            if (name == L1 || name == S1)
                leg1HasExited = true;
        }

        // =====================================================================
        // GATE HELPERS
        // =====================================================================
        private bool PassesRegimeGates()
        {
            if (!EnableHmmGate) return true;
            if (parseFailed || staleDataFlag) return false;
            if (finalRegime != "TREND_COMPRESSION" && finalRegime != "TREND_EXPANSION") return false;
            if (regimeConfidence < MinConfidence) return false;
            lock (fileLock) { if (conflictScore >= MaxConflictScore) return false; }
            if (!allowLong && !allowShort) return false;
            return true;
        }

        private bool PassesAtrRatioGate()
        {
            if (CurrentBar < AtrBaselinePeriod + AtrPeriod + 4) return false;

            // Compute rolling SMA of ATR manually
            double atrSum = 0;
            int cnt = Math.Min(AtrBaselinePeriod, CurrentBar);
            for (int i = 0; i < cnt; i++)
                atrSum += atrBaseline[i];
            double baseline = cnt > 0 ? atrSum / cnt : atrStop[0];
            if (baseline <= 0) return false;

            double ratio = atrStop[0] / baseline;

            // In compression regime, want ATR compressed (below baseline)
            if (finalRegime == "TREND_COMPRESSION" || !EnableHmmGate)
                return ratio <= CompressionMaxRatio;

            // In expansion regime, want ATR expanded (above baseline)
            if (finalRegime == "TREND_EXPANSION")
                return ratio >= ExpansionMinRatio;

            return true;
        }

        private bool IsInTime()
        {
            if (!EnableTimeFilter) return true;
            int t = ToTime(Time[0]);
            if (t < StartTime || t > EndTime) return false;
            if (UseBlock1 && t >= Block1Start && t <= Block1End) return false;
            if (UseBlock2 && t >= Block2Start && t <= Block2End) return false;
            if (UseBlock3 && t >= Block3Start && t <= Block3End) return false;
            return true;
        }

        // =====================================================================
        // HMM CSV FILE READER
        // =====================================================================
        private void RefreshRegimeState()
        {
            if ((DateTime.Now - lastFileCheck).TotalSeconds < MinCheckSeconds) return;
            try
            {
                DateTime wt = File.GetLastWriteTimeUtc(matrixFile);
                if (wt <= lastFileWriteUtc) { lastFileCheck = DateTime.Now; return; }
                ReadLatestRow();
                lastFileWriteUtc = wt;
                lastFileCheck    = DateTime.Now;
            }
            catch { }
        }

        private void ReadLatestRow()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    string[] lines;
                    lock (fileLock)
                    {
                        using (var fs = new FileStream(matrixFile, FileMode.Open,
                                                       FileAccess.Read, FileShare.ReadWrite))
                        using (var sr = new StreamReader(fs))
                            lines = sr.ReadToEnd()
                                      .Split(new[] { '\r', '\n' },
                                             StringSplitOptions.RemoveEmptyEntries);
                    }
                    if (lines.Length < 2) { parseFailed = true; return; }

                    if (headerIdx.Count == 0)
                    {
                        string[] hdr = lines[0].Split(',');
                        headerIdx.Clear();
                        for (int i = 0; i < hdr.Length; i++)
                            headerIdx[hdr[i].Trim()] = i;
                    }

                    string[] row = lines[lines.Length - 1].Split(',');
                    finalRegime      = Get(row, "FinalRegime");
                    regimeConfidence = (int)GetD(row, "RegimeConfidence");
                    allowLong        = GetI(row, "AllowLong")  == 1;
                    allowShort       = GetI(row, "AllowShort") == 1;
                    staleDataFlag    = GetI(row, "StaleDataFlag") == 1;
                    lock (fileLock) { conflictScore = GetD(row, "ConflictScore"); }
                    parseFailed = false;
                    return;
                }
                catch { Thread.Sleep(20); }
            }
            parseFailed = true;
        }

        private void SetupFileWatcher()
        {
            try
            {
                string dir  = Path.GetDirectoryName(matrixFile);
                string file = Path.GetFileName(matrixFile);
                if (!Directory.Exists(dir)) return;
                regimeWatcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter        = NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                regimeWatcher.Changed += (s, e) => ThreadPool.QueueUserWorkItem(_ => ReadLatestRow());
                ReadLatestRow();
            }
            catch { }
        }

        private void TeardownFileWatcher()
        {
            try
            {
                if (regimeWatcher != null)
                {
                    regimeWatcher.EnableRaisingEvents = false;
                    regimeWatcher.Dispose();
                    regimeWatcher = null;
                }
            }
            catch { }
        }

        private string Get(string[] row, string col)
        { int i; return headerIdx.TryGetValue(col, out i) && i < row.Length ? row[i].Trim() : ""; }
        private double GetD(string[] row, string col)
        { double v; return double.TryParse(Get(row, col), out v) ? v : 0.0; }
        private int GetI(string[] row, string col)
        { int v; return int.TryParse(Get(row, col), out v) ? v : 0; }
    }
}
