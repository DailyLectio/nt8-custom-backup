// CC BY-NC 4.0
// ─────────────────────────────────────────────────────────────────────────────
// MomentumRange_RangeBar.cs  —  Bot C
// ─────────────────────────────────────────────────────────────────────────────
// THESIS        : Versatile range bar strategy exploiting the unique property
//                 that range bars SELF-ENCODE volatility in their formation speed.
//                 In compression: bars form slowly — CI stays high, few builds.
//                 In expansion: bars form fast — CI drops, DI separates quickly.
//                 This natural bar-speed dynamic means the CI/ADX signal is
//                 CLEANER on range bars than on time-based bars.
//
//                 Unlike time charts, range bars DO NOT have a reliable ATR
//                 reading (every bar IS the range). Therefore:
//                   - Stops are TICK-BASED (not ATR multiplier)
//                   - Stop ticks are exposed per-leg in settings
//                   - A secondary ATR-like measure (High-Low of last N bars) is
//                     available as an optional stop-scaling mechanism
//
// CHART TYPE    : Range bars — recommended 15–20 pt for NQ, 10–15 pt for MNQ
//                 (Calculate.OnPriceChange — essential for tick-based trailing)
// INSTRUMENT    : NQ / MNQ (MNQ default — TickValue = $0.50)
//
// REGIME GATE   : HMM CSV primary (COMPRESSION or EXPANSION — both allowed,
//                 unlike Bot B which blocks compression).
//                 ATR-ratio secondary gate — checkbox toggle.
//                 Range-bar-specific "bar velocity" gate — optional checkbox:
//                 measures how fast bars are forming (bars/minute proxy) as
//                 an additional regime confirmation.
//
// LEGS          : Up to 4 per direction.
//                 Defaults: Leg1=1C, Leg2=1C, Leg3=0, Leg4=0.
//                 Each leg has its own:
//                   - Contract count
//                   - Stop ticks (independent — can widen per leg)
//                   - Target ticks (Leg 1 fixed, Leg 2 slope-only runner)
//
// KEY FEATURES (range-bar specific):
//   - Tick-based stops as PRIMARY mechanism (ATR fallback optional)
//   - BarVelocity gate: requires minimum bars formed in last N seconds
//     (ensures bars are forming — i.e., market is actually moving)
//   - Range compression detect: if price range over last N bars < threshold,
//     skip entry (bars forming but no directional pressure)
//   - Identical CI/ADX/DI signal engine as Bots A and B
//   - CI on range bars is naturally smoother — hysteresis thresholds are
//     looser than 1m (0.3/bar vs 0.5/bar)
//   - All feature gates exposed as checkbox toggles for SIM testing
//   - Both COMPRESSION and EXPANSION regimes supported (versatile)
//   - Per-regime CI ceiling: tighter in expansion (50), looser in compression (62)
//   - Per-regime stop width: wider in expansion, tighter in compression
//
// PARAMETER GROUPS:
//   1. Regime         — CSV path, HMM gate, ATR-ratio toggle, bar velocity toggle
//   2. Signal         — CI/ADX periods, per-regime CI ceilings, ADX floors
//   3. Legs           — Qty per leg (1-4)
//   4. Stops          — Stop ticks per leg (L1 tighter, L2 wider for runner)
//   5. Targets        — Target ticks per leg, BE ratchet
//   6. Exit           — Slope mode, hysteresis (range-bar tuned), HA-flip optional
//   7. RangeBar Gates — Bar velocity gate, range compression detect
//   8. Filters        — VWAP extension, VWAP direction enforcement
//   9. Shield         — Optional trigger/lock (0=off)
//  10. Timing         — Start/end, 3 block windows
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
    public class MomentumRange_RangeBar : Strategy
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
                 Description = "Primary gate. Both TREND_COMPRESSION and TREND_EXPANSION are allowed on Bot C.")]
        public bool EnableHmmGate { get; set; } = true;

        [NinjaScriptProperty]
        [Range(50, 100)]
        [Display(Name = "Min Regime Confidence", GroupName = "1. Regime", Order = 2)]
        public int MinConfidence { get; set; } = 65;

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Max Conflict Score", GroupName = "1. Regime", Order = 3)]
        public double MaxConflictScore { get; set; } = 0.40;

        [NinjaScriptProperty]
        [Display(Name = "Enable ATR-Ratio Secondary Gate", GroupName = "1. Regime", Order = 4,
                 Description = "Toggle for SIM testing. NOTE: on range bars, ATR is quasi-constant (bar=range). This gate uses a N-bar rolling range average instead.")]
        public bool EnableAtrRatioGate { get; set; } = false;

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "Range Baseline Bars (ratio gate)", GroupName = "1. Regime", Order = 5,
                 Description = "Number of bars for rolling range average baseline in ATR-ratio gate.")]
        public int RangeBaselineBars { get; set; } = 50;

        [NinjaScriptProperty]
        [Range(0.3, 0.95)]
        [Display(Name = "Compression Max Ratio", GroupName = "1. Regime", Order = 6)]
        public double CompressionMaxRatio { get; set; } = 0.85;

        [NinjaScriptProperty]
        [Range(1.0, 2.5)]
        [Display(Name = "Expansion Min Ratio", GroupName = "1. Regime", Order = 7)]
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
        [Display(Name = "CI Ceiling — Compression", GroupName = "2. Signal", Order = 2,
                 Description = "When regime = COMPRESSION, CI must be <= this. 62 allows slightly more chop — range bars in compression produce slightly higher CI.")]
        public double CiMaxCompression { get; set; } = 62.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "CI Ceiling — Expansion", GroupName = "2. Signal", Order = 3,
                 Description = "When regime = EXPANSION, CI must be <= this. Tighter — expansion on range bars is decisive.")]
        public double CiMaxExpansion { get; set; } = 52.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "ADX Floor — Compression", GroupName = "2. Signal", Order = 4)]
        public double AdxMinCompression { get; set; } = 18.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "ADX Floor — Expansion", GroupName = "2. Signal", Order = 5,
                 Description = "Higher floor in expansion — expect stronger ADX on range bars during trend.")]
        public double AdxMinExpansion { get; set; } = 22.0;

        [NinjaScriptProperty]
        [Display(Name = "Enable DI Separation Gate", GroupName = "2. Signal", Order = 6,
                 Description = "Toggle for SIM testing.")]
        public bool EnableDiSeparationGate { get; set; } = true;

        [NinjaScriptProperty, Range(0.0, 30.0)]
        [Display(Name = "Min DI Separation at Cross", GroupName = "2. Signal", Order = 7,
                 Description = "Default 6.0 on range bars — slightly higher than 1m to filter range-bar noise.")]
        public double MinDiSeparation { get; set; } = 6.0;

        [NinjaScriptProperty]
        [Display(Name = "Enable Entry Bar Confirmation", GroupName = "2. Signal", Order = 8,
                 Description = "Toggle. When ON, entry bar close must confirm signal direction.")]
        public bool EnableEntryBarConfirm { get; set; } = true;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value $ (NQ=5.00, MNQ=0.50)", GroupName = "2. Signal", Order = 9)]
        public double TickValueDollars { get; set; } = 0.50;

        // =====================================================================
        // 3. LEG CONTRACT QUANTITIES
        // =====================================================================
        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Leg 1 Contracts (Banker)", GroupName = "3. Legs", Order = 0)]
        public int Leg1Qty { get; set; } = 1;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Leg 2 Contracts (Runner)", GroupName = "3. Legs", Order = 1,
                 Description = "Leg 2 has NO fixed target — exits via hysteresis slope only.")]
        public int Leg2Qty { get; set; } = 1;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Leg 3 Contracts", GroupName = "3. Legs", Order = 2)]
        public int Leg3Qty { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Leg 4 Contracts", GroupName = "3. Legs", Order = 3)]
        public int Leg4Qty { get; set; } = 0;

        // =====================================================================
        // 4. STOPS (TICK-BASED — primary mechanism for range bars)
        // =====================================================================
        [NinjaScriptProperty, Range(1, 500)]
        [Display(Name = "Leg 1 Stop Ticks", GroupName = "4. Stops", Order = 0,
                 Description = "Primary stop for Leg 1 (Banker). On NQ 15pt range bars: 20 ticks = 5 pts. Start here.")]
        public int Leg1StopTicks { get; set; } = 20;

        [NinjaScriptProperty, Range(1, 500)]
        [Display(Name = "Leg 2 Stop Ticks", GroupName = "4. Stops", Order = 1,
                 Description = "Can be wider than Leg 1 to give the runner more room. Default same as Leg 1.")]
        public int Leg2StopTicks { get; set; } = 20;

        [NinjaScriptProperty, Range(1, 500)]
        [Display(Name = "Leg 3 Stop Ticks", GroupName = "4. Stops", Order = 2)]
        public int Leg3StopTicks { get; set; } = 20;

        [NinjaScriptProperty, Range(1, 500)]
        [Display(Name = "Leg 4 Stop Ticks", GroupName = "4. Stops", Order = 3)]
        public int Leg4StopTicks { get; set; } = 25;

        [NinjaScriptProperty]
        [Display(Name = "Use Regime-Adaptive Stop Scaling", GroupName = "4. Stops", Order = 4,
                 Description = "Toggle for SIM testing. When ON, in EXPANSION regime all stop ticks are multiplied by ExpansionStopScale (wider stops for running trend).")]
        public bool EnableRegimeStopScale { get; set; } = false;

        [NinjaScriptProperty, Range(1.0, 3.0)]
        [Display(Name = "Expansion Stop Scale Factor", GroupName = "4. Stops", Order = 5,
                 Description = "In Expansion regime, multiply all stop ticks by this factor. Default 1.25 = 25% wider stops in expansion.")]
        public double ExpansionStopScale { get; set; } = 1.25;

        // =====================================================================
        // 5. TARGETS
        // =====================================================================
        [NinjaScriptProperty, Range(1, 1000)]
        [Display(Name = "Leg 1 Target Ticks (Banker)", GroupName = "5. Targets", Order = 0,
                 Description = "Fixed tick target for Leg 1 on NQ range bars. Default 40 = 10 pts. Adjust to match range bar size (2-3 bars away).")]
        public int Leg1TargetTicks { get; set; } = 40;

        [NinjaScriptProperty, Range(1, 1000)]
        [Display(Name = "Leg 3 Target Ticks", GroupName = "5. Targets", Order = 1)]
        public int Leg3TargetTicks { get; set; } = 60;

        [NinjaScriptProperty, Range(1, 1000)]
        [Display(Name = "Leg 4 Target Ticks", GroupName = "5. Targets", Order = 2)]
        public int Leg4TargetTicks { get; set; } = 80;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Breakeven Ratchet Ticks (BE+N)", GroupName = "5. Targets", Order = 3,
                 Description = "After Leg 1 exits, move Leg 2+ stop to entry + N ticks. Default 2.")]
        public int BeRatchetTicks { get; set; } = 2;

        // =====================================================================
        // 6. EXIT PARAMETERS (range-bar tuned)
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "Leg 2 Slope Exit Mode", GroupName = "6. Exit", Order = 0,
                 Description = "Hysteresis recommended. Range bars produce smooth CI — looser per-bar thresholds than 1m.")]
        public ExitSlopeMode SlopeExit { get; set; } = ExitSlopeMode.Hysteresis;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Min Hold Bars Before Exit", GroupName = "6. Exit", Order = 1,
                 Description = "Default 2. On range bars, this is 2 full range-bar moves minimum.")]
        public int MinHoldBars { get; set; } = 2;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "CI Slope Bars (Simple)", GroupName = "6. Exit", Order = 2,
                 Description = "8 bars on range = equivalent to UniRenko preset.")]
        public int CiSlopeBarsExit { get; set; } = 8;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "ADX Slope Bars (Simple)", GroupName = "6. Exit", Order = 3)]
        public int AdxSlopeBarsExit { get; set; } = 8;

        [NinjaScriptProperty, Range(0.0, 20.0)]
        [Display(Name = "Min CI Rise to Exit (Simple)", GroupName = "6. Exit", Order = 4)]
        public double CiRiseMinExit { get; set; } = 1.0;

        [NinjaScriptProperty, Range(0.0, 20.0)]
        [Display(Name = "Min ADX Drop to Exit (Simple)", GroupName = "6. Exit", Order = 5)]
        public double AdxDropMinExit { get; set; } = 1.0;

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name = "Hysteresis Consecutive Fails (M)", GroupName = "6. Exit", Order = 6)]
        public int HystConsecutive { get; set; } = 2;

        [NinjaScriptProperty, Range(0.0, 10.0)]
        [Display(Name = "Hysteresis CI Rise/Bar", GroupName = "6. Exit", Order = 7,
                 Description = "Looser than 1m: 0.3 default on range bars. CI on range bars oscillates less per bar.")]
        public double HystCiRisePerBar { get; set; } = 0.3;

        [NinjaScriptProperty, Range(0.0, 10.0)]
        [Display(Name = "Hysteresis ADX Drop/Bar", GroupName = "6. Exit", Order = 8,
                 Description = "Looser than 1m: 0.3 default.")]
        public double HystAdxDropPerBar { get; set; } = 0.3;

        // =====================================================================
        // 7. RANGE BAR SPECIFIC GATES
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "Enable Bar Velocity Gate", GroupName = "7. Range Bar Gates", Order = 0,
                 Description = "RANGE BAR SPECIFIC — Toggle for SIM testing. When ON, requires minimum bars formed in last N real-time seconds. Ensures market is actually moving before entry.")]
        public bool EnableBarVelocityGate { get; set; } = false;

        [NinjaScriptProperty, Range(1, 300)]
        [Display(Name = "Velocity Window Seconds", GroupName = "7. Range Bar Gates", Order = 1,
                 Description = "Time window to count bars formed. Default 60 seconds.")]
        public int VelocityWindowSeconds { get; set; } = 60;

        [NinjaScriptProperty, Range(1, 30)]
        [Display(Name = "Min Bars in Velocity Window", GroupName = "7. Range Bar Gates", Order = 2,
                 Description = "Minimum range bars that must have formed in VelocityWindowSeconds. Default 3 bars = meaningful price movement.")]
        public int MinBarsInVelocityWindow { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Enable Range Compression Detect", GroupName = "7. Range Bar Gates", Order = 3,
                 Description = "Toggle for SIM testing. Blocks entry if price has been forming bars but within a very narrow overall range (bars forming but no direction).")]
        public bool EnableRangeCompressionDetect { get; set; } = false;

        [NinjaScriptProperty, Range(2, 30)]
        [Display(Name = "Range Compression Lookback Bars", GroupName = "7. Range Bar Gates", Order = 4,
                 Description = "Number of bars to measure for tight range detection.")]
        public int RangeCompressionLookback { get; set; } = 8;

        [NinjaScriptProperty, Range(0.5, 5.0)]
        [Display(Name = "Range Compression Threshold (bars)", GroupName = "7. Range Bar Gates", Order = 5,
                 Description = "If High-Low over last N bars < BarRangeSize * this multiplier, market is too compressed to enter. Default 1.5.")]
        public double RangeCompressionThreshold { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1, 500)]
        [Display(Name = "Bar Range Size (ticks)", GroupName = "7. Range Bar Gates", Order = 6,
                 Description = "Your range bar setting in ticks. NQ 15pt range = 60 ticks. NQ 20pt range = 80 ticks. Used for range compression threshold calculation.")]
        public int BarRangeSizeTicks { get; set; } = 60;

        // =====================================================================
        // 8. FILTERS
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "Enable VWAP Direction Enforcement", GroupName = "8. Filters", Order = 0,
                 Description = "Toggle. Longs only above VWAP, shorts only below.")]
        public bool EnableVwapDirection { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Enable VWAP Extension Filter", GroupName = "8. Filters", Order = 1,
                 Description = "Toggle. Blocks entries too far from VWAP (measured in bar range multiples, not ATR multiples).")]
        public bool EnableVwapExtFilter { get; set; } = false;

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name = "VWAP Extension Max (x bar range)", GroupName = "8. Filters", Order = 2,
                 Description = "If |Close - VWAP| > BarRangeSizeTicks * TickSize * this value, block entry. Default 4 (= 4 full range bars from VWAP).")]
        public int VwapExtensionMaxBars { get; set; } = 4;

        // =====================================================================
        // 9. SHIELD
        // =====================================================================
        [NinjaScriptProperty, Range(0.0, 100000.0)]
        [Display(Name = "Shield Trigger $ (0=off)", GroupName = "9. Shield", Order = 0)]
        public double ShieldTrigger { get; set; } = 0;

        [NinjaScriptProperty, Range(0.0, 100000.0)]
        [Display(Name = "Shield Lock $ (0=off)", GroupName = "9. Shield", Order = 1)]
        public double ShieldLock { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 30)]
        [Display(Name = "Consecutive Loss Pause (bars)", GroupName = "9. Shield", Order = 2,
                 Description = "Default 8 bars on range = meaningful pause. Range bars accumulate quickly during active sessions.")]
        public int ConsecLossPauseBars { get; set; } = 8;

        // =====================================================================
        // 10. TIMING
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "10. Timing", Order = 0)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Start Time (HHmmss)", GroupName = "10. Timing", Order = 1,
                 Description = "Default 9:35 — range bars need a few minutes to warm up after open.")]
        public int StartTime { get; set; } = 93500;

        [NinjaScriptProperty]
        [Display(Name = "End Time (HHmmss)", GroupName = "10. Timing", Order = 2)]
        public int EndTime { get; set; } = 120000;

        [NinjaScriptProperty]
        [Display(Name = "Use Block Window 1", GroupName = "10. Timing", Order = 3)]
        public bool UseBlock1 { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Block 1 Start (HHmmss)", GroupName = "10. Timing", Order = 4,
                 Description = "Default: block 9:59-10:06 open rotation.")]
        public int Block1Start { get; set; } = 95900;

        [NinjaScriptProperty]
        [Display(Name = "Block 1 End (HHmmss)", GroupName = "10. Timing", Order = 5)]
        public int Block1End { get; set; } = 100600;

        [NinjaScriptProperty]
        [Display(Name = "Use Block Window 2", GroupName = "10. Timing", Order = 6)]
        public bool UseBlock2 { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Block 2 Start (HHmmss)", GroupName = "10. Timing", Order = 7)]
        public int Block2Start { get; set; } = 102800;

        [NinjaScriptProperty]
        [Display(Name = "Block 2 End (HHmmss)", GroupName = "10. Timing", Order = 8)]
        public int Block2End { get; set; } = 103500;

        [NinjaScriptProperty]
        [Display(Name = "Use Block Window 3", GroupName = "10. Timing", Order = 9)]
        public bool UseBlock3 { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Block 3 Start (HHmmss)", GroupName = "10. Timing", Order = 10)]
        public int Block3Start { get; set; } = 120000;

        [NinjaScriptProperty]
        [Display(Name = "Block 3 End (HHmmss)", GroupName = "10. Timing", Order = 11)]
        public int Block3End { get; set; } = 120500;

        // =====================================================================
        // INDICATORS
        // =====================================================================
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

        private const string L1 = "RB_L1";
        private const string L2 = "RB_L2";
        private const string L3 = "RB_L3";
        private const string L4 = "RB_L4";
        private const string S1 = "RB_S1";
        private const string S2 = "RB_S2";
        private const string S3 = "RB_S3";
        private const string S4 = "RB_S4";

        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "MomentumRange_RangeBar";
                Description                  = "Bot C — Range Bar Versatile | Tick-Stop Legs | HMM Gated | Both Regimes";
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

                adx = ADX(AdxPeriod);

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
            int warmup = Math.Max(AdxPeriod, CiPeriod) + 4;
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
            // NOTE: On range bars, High-Low per bar is nearly constant (the range).
            // CI therefore measures the spread of that range vs the larger window.
            // When bars stack directionally (trend), CI drops. When bars chop within
            // a tight band, CI stays high. This is cleaner than on time bars.
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

                // Per-regime adaptive CI ceiling and ADX floor
                bool isExpansion = finalRegime == "TREND_EXPANSION";
                double ciCeiling = isExpansion ? CiMaxExpansion   : CiMaxCompression;
                double adxFloor  = isExpansion ? AdxMinExpansion  : AdxMinCompression;

                if (ci[0]  > ciCeiling) return;
                if (adx[0] < adxFloor)  return;

                double diSep = Math.Abs(diPlusSeries[0] - diMinusSeries[0]);
                if (EnableDiSeparationGate && diSep < MinDiSeparation) return;

                // Range-bar specific gates
                if (EnableAtrRatioGate      && !PassesRangeRatioGate(isExpansion)) return;
                if (EnableBarVelocityGate   && !PassesBarVelocityGate())           return;
                if (EnableRangeCompressionDetect && !PassesRangeCompressionGate()) return;

                // VWAP filters
                if (EnableVwapExtFilter)
                {
                    double barRangePoints = BarRangeSizeTicks * TickSize;
                    double vwapDist       = Math.Abs(Close[0] - sessionVwap[0]);
                    if (vwapDist > barRangePoints * VwapExtensionMaxBars) return;
                }

                if (crossUp && (allowLong || !EnableHmmGate))
                {
                    if (EnableVwapDirection && Close[0] <= sessionVwap[0]) return;
                    if (EnableEntryBarConfirm && Close[0] <= Close[1]) return;
                    SubmitLongEntry(isExpansion);
                }
                else if (crossDown && (allowShort || !EnableHmmGate))
                {
                    if (EnableVwapDirection && Close[0] >= sessionVwap[0]) return;
                    if (EnableEntryBarConfirm && Close[0] >= Close[1]) return;
                    SubmitShortEntry(isExpansion);
                }
            }
        }

        // =====================================================================
        // ENTRY SUBMISSION — TICK-BASED STOPS
        // =====================================================================
        private void SubmitLongEntry(bool isExpansion)
        {
            entryPrice    = Close[0];
            leg1HasExited = false;

            double scale = (EnableRegimeStopScale && isExpansion) ? ExpansionStopScale : 1.0;
            int s1 = Math.Max(1, (int)(Leg1StopTicks * scale));
            int s2 = Math.Max(1, (int)(Leg2StopTicks * scale));
            int s3 = Math.Max(1, (int)(Leg3StopTicks * scale));
            int s4 = Math.Max(1, (int)(Leg4StopTicks * scale));

            double stp1 = RT(entryPrice - s1 * TickSize);
            double stp2 = RT(entryPrice - s2 * TickSize);
            double stp3 = RT(entryPrice - s3 * TickSize);
            double stp4 = RT(entryPrice - s4 * TickSize);

            if (Leg1Qty > 0)
            {
                SetStopLoss(L1, CalculationMode.Price, stp1, false);
                SetProfitTarget(L1, CalculationMode.Ticks, Leg1TargetTicks);
                EnterLong(Leg1Qty, L1);
            }
            if (Leg2Qty > 0)
            {
                SetStopLoss(L2, CalculationMode.Price, stp2, false);
                // No fixed target on Leg 2 — slope exit manages it
                EnterLong(Leg2Qty, L2);
            }
            if (Leg3Qty > 0)
            {
                SetStopLoss(L3, CalculationMode.Price, stp3, false);
                SetProfitTarget(L3, CalculationMode.Ticks, Leg3TargetTicks);
                EnterLong(Leg3Qty, L3);
            }
            if (Leg4Qty > 0)
            {
                SetStopLoss(L4, CalculationMode.Price, stp4, false);
                SetProfitTarget(L4, CalculationMode.Ticks, Leg4TargetTicks);
                EnterLong(Leg4Qty, L4);
            }

            Print(string.Format("[RangeBar] LONG | Regime:{0} | CI:{1:F1} | ADX:{2:F1} | " +
                                "DiSep:{3:F1} | StopTks_L1:{4} | TargetTks:{5} | Scale:{6:F2}",
                                finalRegime, ci[0], adx[0],
                                Math.Abs(diPlusSeries[0] - diMinusSeries[0]),
                                s1, Leg1TargetTicks, scale));
        }

        private void SubmitShortEntry(bool isExpansion)
        {
            entryPrice    = Close[0];
            leg1HasExited = false;

            double scale = (EnableRegimeStopScale && isExpansion) ? ExpansionStopScale : 1.0;
            int s1 = Math.Max(1, (int)(Leg1StopTicks * scale));
            int s2 = Math.Max(1, (int)(Leg2StopTicks * scale));
            int s3 = Math.Max(1, (int)(Leg3StopTicks * scale));
            int s4 = Math.Max(1, (int)(Leg4StopTicks * scale));

            double stp1 = RT(entryPrice + s1 * TickSize);
            double stp2 = RT(entryPrice + s2 * TickSize);
            double stp3 = RT(entryPrice + s3 * TickSize);
            double stp4 = RT(entryPrice + s4 * TickSize);

            if (Leg1Qty > 0)
            {
                SetStopLoss(S1, CalculationMode.Price, stp1, false);
                SetProfitTarget(S1, CalculationMode.Ticks, Leg1TargetTicks);
                EnterShort(Leg1Qty, S1);
            }
            if (Leg2Qty > 0)
            {
                SetStopLoss(S2, CalculationMode.Price, stp2, false);
                EnterShort(Leg2Qty, S2);
            }
            if (Leg3Qty > 0)
            {
                SetStopLoss(S3, CalculationMode.Price, stp3, false);
                SetProfitTarget(S3, CalculationMode.Ticks, Leg3TargetTicks);
                EnterShort(Leg3Qty, S3);
            }
            if (Leg4Qty > 0)
            {
                SetStopLoss(S4, CalculationMode.Price, stp4, false);
                SetProfitTarget(S4, CalculationMode.Ticks, Leg4TargetTicks);
                EnterShort(Leg4Qty, S4);
            }

            Print(string.Format("[RangeBar] SHORT | Regime:{0} | CI:{1:F1} | ADX:{2:F1} | " +
                                "DiSep:{3:F1} | StopTks_S1:{4} | TargetTks:{5} | Scale:{6:F2}",
                                finalRegime, ci[0], adx[0],
                                Math.Abs(diPlusSeries[0] - diMinusSeries[0]),
                                s1, Leg1TargetTicks, scale));
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
            if (finalRegime != "TREND_COMPRESSION" && finalRegime != "TREND_EXPANSION") return false;
            if (regimeConfidence < MinConfidence) return false;
            lock (fileLock) { if (conflictScore >= MaxConflictScore) return false; }
            if (!allowLong && !allowShort) return false;
            return true;
        }

        private bool PassesRangeRatioGate(bool isExpansion)
        {
            // On range bars: ATR ≈ bar range. We instead use recent bar speed
            // (close-to-close range distribution) via the N-bar high-low span.
            if (CurrentBar < RangeBaselineBars + 4) return false;

            // Current N-bar range
            double curRange = maxHigh[0] - minLow[0];

            // Baseline: average range over further lookback
            double baseSum = 0;
            int bCnt = Math.Min(RangeBaselineBars, CurrentBar);
            for (int i = 0; i < bCnt; i++)
                baseSum += (MAX(High, CiPeriod)[i] - MIN(Low, CiPeriod)[i]);
            double baseline = bCnt > 0 ? baseSum / bCnt : curRange;
            if (baseline <= 0) return false;

            double ratio = curRange / baseline;

            if (isExpansion) return ratio >= ExpansionMinRatio;
            else             return ratio <= CompressionMaxRatio;
        }

        private bool PassesBarVelocityGate()
        {
            // Count how many bars formed within the last VelocityWindowSeconds
            DateTime cutoff = Time[0].AddSeconds(-VelocityWindowSeconds);
            int cnt = 0;
            int maxLook = Math.Min(100, CurrentBar);
            for (int i = 0; i < maxLook; i++)
            {
                if (Time[i] < cutoff) break;
                cnt++;
            }
            return cnt >= MinBarsInVelocityWindow;
        }

        private bool PassesRangeCompressionGate()
        {
            // If N-bar span < BarRangeSize * threshold, the bars are chopping in place
            int lookback = Math.Min(RangeCompressionLookback, CurrentBar);
            double hiN = double.MinValue, loN = double.MaxValue;
            for (int i = 0; i < lookback; i++)
            {
                hiN = Math.Max(hiN, High[i]);
                loN = Math.Min(loN, Low[i]);
            }
            double span          = hiN - loN;
            double minRequiredRange = BarRangeSizeTicks * TickSize * RangeCompressionThreshold;
            return span >= minRequiredRange;  // returns true = OK to trade, false = too compressed
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
