// CC BY-NC 4.0
// ─────────────────────────────────────────────────────────────────────────────
// MomentumExpansion_5m.cs  —  Bot B
// ─────────────────────────────────────────────────────────────────────────────
// THESIS        : HTF expansion runner on 5-minute standard candle bars.
//                 Waits for HMM-confirmed TREND_EXPANSION only.
//                 Leg 1 banks at 2.0R (wider first target for 5m context).
//                 Leg 2+ run purely on hysteresis slope exit — NO fixed target.
//                 ATR-ratio expansion confirmation gate (checkbox).
//
// CHART TYPE    : 5-minute standard candle  (Calculate.OnBarClose)
// INSTRUMENT    : NQ / MNQ (MNQ default — TickValue = $0.50)
// REGIME GATE   : HMM CSV — STRICT: TREND_EXPANSION ONLY.
//                 ATR-ratio gate confirms expansion is structural, not a spike.
//
// LEGS          : Up to 4. Defaults: Leg1=1C (2.0R target), Leg2=1C (slope runner),
//                 Leg3=0, Leg4=0. All counts and targets exposed as settings.
//
// KEY FEATURES  :
//   - OnBarClose (5m bars — price-change firing unnecessary)
//   - Manual CI / Wilder DI — identical engine to Bot A and V3D
//   - Tighter CI ceiling (50) and higher ADX floor (22) for 5m conviction
//   - Higher DI separation gate (8) — 5m cross must be decisive
//   - Hysteresis exit with 5m-tuned parameters (window 8 bars, looser per-bar threshold)
//   - Inline session VWAP — direction enforcement (longs above, shorts below)
//   - VWAP extension filter (3× ATR on 5m)
//   - ATR-ratio expansion confirmation: ATR/SMA(ATR,50) >= 1.10 before entry
//   - Breakeven ratchet: stop → BE+4 ticks when Leg 1 exits at 2.0R
//   - Circuit breaker: DI counter-cross exits all legs immediately
//   - Regime TRANSITION → immediate flatten
//   - Consecutive loss pause (N bars — defaults 3 bars = 15 min on 5m)
//   - Session PnL isolation + optional PnL Shield
//   - All feature gates exposed as toggleable boolean settings for SIM testing
//   - Strict regime filter: Expansion only — COMPRESSION entries are BLOCKED
//
// PARAMETER GROUPS:
//   1. Regime         — CSV path, confidence/conflict gates, ATR-ratio toggle
//   2. Signal         — CI/ADX periods, 5m-tuned thresholds, DI separation
//   3. Legs           — Qty per leg (1-4)
//   4. Targets        — Leg 1 target (2.0R), Leg 3/4 targets, BE ratchet
//   5. Exit           — Hysteresis params (5m tuned), min hold bars
//   6. Filters        — VWAP direction enforcement, extension filter
//   7. Shield         — Optional trigger/lock (0=off)
//   8. Timing         — Start/end, 3 block windows
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
    public class MomentumExpansion_5m : Strategy
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
                 Description = "Primary gate. STRICT: only TREND_EXPANSION from HMM CSV. Compression entries are blocked.")]
        public bool EnableHmmGate { get; set; } = true;

        [NinjaScriptProperty]
        [Range(50, 100)]
        [Display(Name = "Min Regime Confidence", GroupName = "1. Regime", Order = 2,
                 Description = "5m uses tighter confidence floor. Default 70 (vs 65 for Bot A).")]
        public int MinConfidence { get; set; } = 70;

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Max Conflict Score", GroupName = "1. Regime", Order = 3,
                 Description = "5m uses tighter conflict ceiling. Default 0.30 (vs 0.40 for Bot A).")]
        public double MaxConflictScore { get; set; } = 0.30;

        [NinjaScriptProperty]
        [Display(Name = "Enable ATR-Ratio Expansion Confirm", GroupName = "1. Regime", Order = 4,
                 Description = "Toggle for SIM testing. When ON, requires ATR/SMA(ATR,50) >= ExpansionMinRatio before entry — confirms structural expansion, not a spike.")]
        public bool EnableAtrRatioGate { get; set; } = true;

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "ATR Baseline Period", GroupName = "1. Regime", Order = 5)]
        public int AtrBaselinePeriod { get; set; } = 50;

        [NinjaScriptProperty]
        [Range(1.0, 2.5)]
        [Display(Name = "Expansion Min ATR Ratio", GroupName = "1. Regime", Order = 6,
                 Description = "ATR/Baseline must be >= this for expansion confirmation. Default 1.10.")]
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
                 Description = "Tighter than Bot A: 50 default. 5m expansion requires clean CI reading.")]
        public double CiEntryThreshold { get; set; } = 50.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "ADX Entry Threshold (>=)", GroupName = "2. Signal", Order = 3,
                 Description = "Higher floor than Bot A: 22 default for 5m conviction.")]
        public double AdxEntryThreshold { get; set; } = 22.0;

        [NinjaScriptProperty]
        [Display(Name = "Enable DI Separation Gate", GroupName = "2. Signal", Order = 4,
                 Description = "Toggle for SIM testing.")]
        public bool EnableDiSeparationGate { get; set; } = true;

        [NinjaScriptProperty, Range(0.0, 40.0)]
        [Display(Name = "Min DI Separation at Cross", GroupName = "2. Signal", Order = 5,
                 Description = "Higher bar than Bot A: 8.0 default for 5m. 5m cross must be decisive.")]
        public double MinDiSeparation { get; set; } = 8.0;

        [NinjaScriptProperty]
        [Display(Name = "Enable Entry Bar Confirmation", GroupName = "2. Signal", Order = 6,
                 Description = "Toggle. When ON, entry bar close must confirm signal direction.")]
        public bool EnableEntryBarConfirm { get; set; } = true;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period (stops)", GroupName = "2. Signal", Order = 7)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.1, 3.0)]
        [Display(Name = "ATR Stop Multiplier", GroupName = "2. Signal", Order = 8,
                 Description = "Wider than Bot A: 1.0 default for 5m. Trend needs room to breathe.")]
        public double AtrStopMult { get; set; } = 1.0;

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "Min Stop Ticks", GroupName = "2. Signal", Order = 9,
                 Description = "Hard floor. Default 16 ticks on 5m NQ.")]
        public int MinStopTicks { get; set; } = 16;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value $ (NQ=5.00, MNQ=0.50)", GroupName = "2. Signal", Order = 10)]
        public double TickValueDollars { get; set; } = 0.50;

        // =====================================================================
        // 3. LEG CONTRACT QUANTITIES
        // =====================================================================
        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Leg 1 Contracts (2.0R target)", GroupName = "3. Legs", Order = 0)]
        public int Leg1Qty { get; set; } = 1;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Leg 2 Contracts (slope runner)", GroupName = "3. Legs", Order = 1,
                 Description = "Leg 2 has NO fixed target — exits via hysteresis slope only.")]
        public int Leg2Qty { get; set; } = 1;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Leg 3 Contracts", GroupName = "3. Legs", Order = 2)]
        public int Leg3Qty { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Leg 4 Contracts", GroupName = "3. Legs", Order = 3)]
        public int Leg4Qty { get; set; } = 0;

        // =====================================================================
        // 4. TARGETS
        // =====================================================================
        [NinjaScriptProperty, Range(1, 1000)]
        [Display(Name = "Leg 1 Target Ticks (2.0R floor)", GroupName = "4. Targets", Order = 0,
                 Description = "Floor tick target for Leg 1. Runtime target = ATR*mult*2.0 ticks. This is the minimum.")]
        public int Leg1TargetTicks { get; set; } = 40;

        [NinjaScriptProperty, Range(1, 1000)]
        [Display(Name = "Leg 3 Target Ticks", GroupName = "4. Targets", Order = 1)]
        public int Leg3TargetTicks { get; set; } = 80;

        [NinjaScriptProperty, Range(1, 1000)]
        [Display(Name = "Leg 4 Target Ticks", GroupName = "4. Targets", Order = 2)]
        public int Leg4TargetTicks { get; set; } = 120;

        [NinjaScriptProperty, Range(0, 30)]
        [Display(Name = "Breakeven Ratchet Ticks (BE+N)", GroupName = "4. Targets", Order = 3,
                 Description = "After Leg 1 exits at 2.0R, move Leg 2+ stop to entry + this. Default 4 ticks.")]
        public int BeRatchetTicks { get; set; } = 4;

        // =====================================================================
        // 5. EXIT PARAMETERS (5m tuned — slower oscillation than 1m)
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "Slope Exit Mode (Leg 2)", GroupName = "5. Exit", Order = 0,
                 Description = "Hysteresis strongly recommended for 5m. Prevents single-bar noise exits.")]
        public ExitSlopeMode SlopeExit { get; set; } = ExitSlopeMode.Hysteresis;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Min Hold Bars Before Exit", GroupName = "5. Exit", Order = 1,
                 Description = "Default 2 bars = 10 minutes minimum hold on 5m.")]
        public int MinHoldBars { get; set; } = 2;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "CI Slope Bars (Simple exit)", GroupName = "5. Exit", Order = 2,
                 Description = "8 bars = 40 min window on 5m.")]
        public int CiSlopeBarsExit { get; set; } = 8;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "ADX Slope Bars (Simple exit)", GroupName = "5. Exit", Order = 3)]
        public int AdxSlopeBarsExit { get; set; } = 8;

        [NinjaScriptProperty, Range(0.0, 20.0)]
        [Display(Name = "Min CI Rise to Exit (Simple)", GroupName = "5. Exit", Order = 4)]
        public double CiRiseMinExit { get; set; } = 1.5;

        [NinjaScriptProperty, Range(0.0, 20.0)]
        [Display(Name = "Min ADX Drop to Exit (Simple)", GroupName = "5. Exit", Order = 5)]
        public double AdxDropMinExit { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name = "Hysteresis Consecutive Fails (M)", GroupName = "5. Exit", Order = 6,
                 Description = "Default 2 — same as 1m. Each bar is 5 min so 2 bars = 10 min of failing slope.")]
        public int HystConsecutive { get; set; } = 2;

        [NinjaScriptProperty, Range(0.0, 10.0)]
        [Display(Name = "Hysteresis CI Rise/Bar", GroupName = "5. Exit", Order = 7,
                 Description = "Looser than 1m (0.3 vs 0.5) — 5m CI oscillates more slowly.")]
        public double HystCiRisePerBar { get; set; } = 0.3;

        [NinjaScriptProperty, Range(0.0, 10.0)]
        [Display(Name = "Hysteresis ADX Drop/Bar", GroupName = "5. Exit", Order = 8,
                 Description = "Looser than 1m (0.3 vs 0.5).")]
        public double HystAdxDropPerBar { get; set; } = 0.3;

        // =====================================================================
        // 6. FILTERS
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "Enable VWAP Direction Enforcement", GroupName = "6. Filters", Order = 0,
                 Description = "Toggle. When ON: longs only when Close > VWAP, shorts only when Close < VWAP.")]
        public bool EnableVwapDirection { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable VWAP Extension Filter", GroupName = "6. Filters", Order = 1,
                 Description = "Toggle. Blocks entries too far extended from VWAP.")]
        public bool EnableVwapExtFilter { get; set; } = true;

        [NinjaScriptProperty, Range(0.5, 8.0)]
        [Display(Name = "VWAP Extension Max (x ATR)", GroupName = "6. Filters", Order = 2,
                 Description = "3.0 default on 5m (wider than 2.0 on 1m — 5m moves are larger).")]
        public double VwapExtensionMaxAtr { get; set; } = 3.0;

        // =====================================================================
        // 7. SHIELD
        // =====================================================================
        [NinjaScriptProperty, Range(0.0, 100000.0)]
        [Display(Name = "Shield Trigger $ (0=off)", GroupName = "7. Shield", Order = 0)]
        public double ShieldTrigger { get; set; } = 0;

        [NinjaScriptProperty, Range(0.0, 100000.0)]
        [Display(Name = "Shield Lock $ (0=off)", GroupName = "7. Shield", Order = 1)]
        public double ShieldLock { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Consecutive Loss Pause (bars)", GroupName = "7. Shield", Order = 2,
                 Description = "Default 3 bars = 15 min pause on 5m after 2 consecutive losses.")]
        public int ConsecLossPauseBars { get; set; } = 3;

        // =====================================================================
        // 8. TIMING
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "8. Timing", Order = 0)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Start Time (HHmmss)", GroupName = "8. Timing", Order = 1,
                 Description = "Default 9:45 — skip the first 15 min open chaos on 5m.")]
        public int StartTime { get; set; } = 94500;

        [NinjaScriptProperty]
        [Display(Name = "End Time (HHmmss)", GroupName = "8. Timing", Order = 2,
                 Description = "Default 13:00 — closes out before early afternoon thinning.")]
        public int EndTime { get; set; } = 130000;

        [NinjaScriptProperty]
        [Display(Name = "Use Block Window 1", GroupName = "8. Timing", Order = 3)]
        public bool UseBlock1 { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Block 1 Start (HHmmss)", GroupName = "8. Timing", Order = 4,
                 Description = "Default: block 12:00-12:10 lunch thinning.")]
        public int Block1Start { get; set; } = 120000;

        [NinjaScriptProperty]
        [Display(Name = "Block 1 End (HHmmss)", GroupName = "8. Timing", Order = 5)]
        public int Block1End { get; set; } = 121000;

        [NinjaScriptProperty]
        [Display(Name = "Use Block Window 2", GroupName = "8. Timing", Order = 6)]
        public bool UseBlock2 { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Block 2 Start (HHmmss)", GroupName = "8. Timing", Order = 7)]
        public int Block2Start { get; set; } = 100000;

        [NinjaScriptProperty]
        [Display(Name = "Block 2 End (HHmmss)", GroupName = "8. Timing", Order = 8)]
        public int Block2End { get; set; } = 100500;

        [NinjaScriptProperty]
        [Display(Name = "Use Block Window 3", GroupName = "8. Timing", Order = 9)]
        public bool UseBlock3 { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Block 3 Start (HHmmss)", GroupName = "8. Timing", Order = 10)]
        public int Block3Start { get; set; } = 155000;

        [NinjaScriptProperty]
        [Display(Name = "Block 3 End (HHmmss)", GroupName = "8. Timing", Order = 11)]
        public int Block3End { get; set; } = 160000;

        // =====================================================================
        // INDICATORS
        // =====================================================================
        private ATR atrStop;
        private ATR atrBaseline;
        private ADX adx;

        private Series<double> trSeries;
        private SUM  sumTr;
        private MAX  maxHigh;
        private MIN  minLow;
        private Series<double> ci;

        private Series<double> dmPlus, dmMinus;
        private Series<double> sumDmPlus, sumDmMinus, sumTrDI;
        private Series<double> diPlusSeries, diMinusSeries;

        private Series<double> sessionVwap;
        private double cumPV, cumVol;

        // =====================================================================
        // REGIME STATE
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

        private const string L1 = "EXP_L1";
        private const string L2 = "EXP_L2";
        private const string L3 = "EXP_L3";
        private const string L4 = "EXP_L4";
        private const string S1 = "EXP_S1";
        private const string S2 = "EXP_S2";
        private const string S3 = "EXP_S3";
        private const string S4 = "EXP_S4";

        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "MomentumExpansion_5m";
                Description                  = "Bot B — 5m Expansion Runner | 2.0R Banker + Slope Runner | HMM Expansion-Only";
                Calculate                    = Calculate.OnBarClose;
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

                adx         = ADX(AdxPeriod);
                atrStop     = ATR(AtrPeriod);
                atrBaseline = ATR(AtrPeriod);

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

            circuitBreakerFiredThisBar = false;

            // ── Session reset ────────────────────────────────────────────
            if (Bars.IsFirstBarOfSession)
            {
                cumPV = 0; cumVol = 0;
                hystFailCount      = 0;
                consecutiveLosers  = 0;
                pauseBarsRemaining = 0;
                leg1HasExited      = false;
                tradingHalted      = false;
                shieldActive       = false;
                pnlFloor           = double.MinValue;
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }

            // ── VWAP ─────────────────────────────────────────────────────
            double typ = (High[0] + Low[0] + Close[0]) / 3.0;
            double vol = Math.Max(1.0, Volume[0]);
            cumPV  += typ * vol;
            cumVol += vol;
            sessionVwap[0] = cumPV / cumVol;

            // ── CI ───────────────────────────────────────────────────────
            double trBar  = Math.Max(High[0] - Low[0],
                            Math.Max(Math.Abs(High[0] - Close[1]),
                                     Math.Abs(Low[0]  - Close[1])));
            trSeries[0] = trBar;
            double range    = maxHigh[0] - minLow[0];
            double sumTrVal = Math.Max(1e-9, sumTr[0]);
            double denom    = Math.Log10(Math.Max(2, CiPeriod));
            double numer    = (range <= 1e-9) ? 1.0 : (sumTrVal / Math.Max(1e-9, range));
            ci[0] = Math.Max(0.0, Math.Min(100.0, 100.0 * Math.Log10(numer) / denom));

            // ── Wilder DI ────────────────────────────────────────────────
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

            RefreshRegimeState();

            // ── Shield ───────────────────────────────────────────────────
            if (ShieldTrigger > 0 || ShieldLock > 0)
            {
                double dailyPnL = SystemPerformance.AllTrades
                                      .TradesPerformance.Currency.CumProfit
                                  - sessionStartProfit;
                if (!shieldActive && ShieldTrigger > 0 && dailyPnL >= ShieldTrigger)
                { shieldActive = true; pnlFloor = ShieldLock; }
                if (shieldActive && ShieldLock > 0 && dailyPnL <= pnlFloor)
                {
                    tradingHalted = true;
                    if (Position.MarketPosition == MarketPosition.Long)  ExitLong("ShieldHalt",  "");
                    if (Position.MarketPosition == MarketPosition.Short) ExitShort("ShieldHalt", "");
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

            // ── Circuit breaker ───────────────────────────────────────────
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

            // ── BE ratchet ────────────────────────────────────────────────
            if (leg1HasExited && Position.MarketPosition != MarketPosition.Flat)
                ApplyBeRatchet();

            // ── Slope exit (Leg 2+ runners) ──────────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                string ent = Position.MarketPosition == MarketPosition.Long ? L2 : S2;
                int bse = BarsSinceEntryExecution(0, ent, 0);

                if (bse >= MinHoldBars)
                {
                    bool exitNow = false;

                    if (SlopeExit == ExitSlopeMode.Simple &&
                        CurrentBar > Math.Max(CiSlopeBarsExit, AdxSlopeBarsExit))
                    {
                        double ciRise  = ci[0]  - ci[CiSlopeBarsExit];
                        double adxDrop = adx[AdxSlopeBarsExit] - adx[0];
                        exitNow = ciRise >= CiRiseMinExit || adxDrop >= AdxDropMinExit;
                    }
                    else if (SlopeExit == ExitSlopeMode.Hysteresis && CurrentBar > 1)
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
                if (!PassesRegimeGates()) return;

                if (ci[0]  > CiEntryThreshold)  return;
                if (adx[0] < AdxEntryThreshold)  return;

                double diSep = Math.Abs(diPlusSeries[0] - diMinusSeries[0]);
                if (EnableDiSeparationGate && diSep < MinDiSeparation) return;

                // ATR-ratio gate
                if (EnableAtrRatioGate && !PassesAtrRatioGate()) return;

                // VWAP extension filter
                if (EnableVwapExtFilter)
                {
                    double vwapDist = Math.Abs(Close[0] - sessionVwap[0]);
                    if (vwapDist > atrStop[0] * VwapExtensionMaxAtr) return;
                }

                if (crossUp && (allowLong || !EnableHmmGate))
                {
                    // VWAP direction enforcement
                    if (EnableVwapDirection && Close[0] <= sessionVwap[0]) return;
                    if (EnableEntryBarConfirm && Close[0] <= Close[1]) return;
                    SubmitLongEntry();
                }
                else if (crossDown && (allowShort || !EnableHmmGate))
                {
                    if (EnableVwapDirection && Close[0] >= sessionVwap[0]) return;
                    if (EnableEntryBarConfirm && Close[0] >= Close[1]) return;
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
            int    tgt1Tks = Math.Max(Leg1TargetTicks, (int)Math.Round(risk * 2.0 / TickSize));

            if (Leg1Qty > 0)
            {
                SetStopLoss(L1, CalculationMode.Price, stp, false);
                SetProfitTarget(L1, CalculationMode.Ticks, tgt1Tks);
                EnterLong(Leg1Qty, L1);
            }
            if (Leg2Qty > 0)
            {
                SetStopLoss(L2, CalculationMode.Price, stp, false);
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

            Print(string.Format("[Exp_5m] LONG | Regime:{0} | Conf:{1} | Conflict:{2:F2} | " +
                                "CI:{3:F1} | ADX:{4:F1} | DiSep:{5:F1} | ATR:{6:F2} | " +
                                "Stop:{7:F2} | Tgt1_Tks:{8}",
                                finalRegime, regimeConfidence, conflictScore,
                                ci[0], adx[0],
                                Math.Abs(diPlusSeries[0] - diMinusSeries[0]),
                                atrVal, stp, tgt1Tks));
        }

        private void SubmitShortEntry()
        {
            entryPrice    = Close[0];
            leg1HasExited = false;

            double atrVal  = atrStop[0];
            double risk    = Math.Max(atrVal * AtrStopMult, MinStopTicks * TickSize);
            double stp     = RT(entryPrice + risk);
            int    tgt1Tks = Math.Max(Leg1TargetTicks, (int)Math.Round(risk * 2.0 / TickSize));

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

            Print(string.Format("[Exp_5m] SHORT | Regime:{0} | Conf:{1} | Conflict:{2:F2} | " +
                                "CI:{3:F1} | ADX:{4:F1} | DiSep:{5:F1} | ATR:{6:F2} | Stop:{7:F2}",
                                finalRegime, regimeConfidence, conflictScore,
                                ci[0], adx[0],
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

        protected override void OnExecutionUpdate(
            Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.IsEntry) return;
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
            // STRICT: Expansion only — Compression is blocked on Bot B
            if (finalRegime != "TREND_EXPANSION") return false;
            if (regimeConfidence < MinConfidence) return false;
            lock (fileLock) { if (conflictScore >= MaxConflictScore) return false; }
            if (!allowLong && !allowShort) return false;
            return true;
        }

        private bool PassesAtrRatioGate()
        {
            if (CurrentBar < AtrBaselinePeriod + AtrPeriod + 4) return false;
            double atrSum = 0;
            int cnt = Math.Min(AtrBaselinePeriod, CurrentBar);
            for (int i = 0; i < cnt; i++)
                atrSum += atrBaseline[i];
            double baseline = cnt > 0 ? atrSum / cnt : atrStop[0];
            if (baseline <= 0) return false;
            return (atrStop[0] / baseline) >= ExpansionMinRatio;
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
        // HMM CSV FILE READER (identical pattern to Bot A / V3D)
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
