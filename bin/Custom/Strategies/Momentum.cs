// Momentum.cs — V2 lineage upgrade per Strategy Lane Settings v3 (2026-06-05)
//
// Base lineage (data-producing source):
//   * MomentumSlopeOG_V2.cs (class MomentumSlopeStrategy_V2) — research data source for v3 Momentum NQ
//     (originates from MomentumSlopeStrategy_v1.2 / Original OG)
//
// Preserved from V2 lineage:
//   - DI cross entry (Wilder smoothing of DI+/DI-)
//   - ADX / CI threshold entry gates
//   - Anchor (EMA / VWAP) optional side bias
//   - ATR bracket stops with min-ticks + R:R target
//   - Slope-based exits (Simple / Hysteresis)
//   - V3D Trinity HUD gate (RegimeMatrixHUD_V3D.IsMomoAllowed)
//   - Preset loader (Candle_1m / UniRenko_10_20_40 / Custom)
//
// Added per v3 spec (HANDOFF_Expansion_Momentum_CodingChat_RESOLVED_20260605.md):
//   - 3-window allow-list entry gate with per-window Enabled bool (replaces V2 block-list)
//   - LongsOnly + EnableHMMTrendDownBlock (block when V3D hud.HMMRegime == "TrendDown")
//   - MinConfidenceThreshold (Path B per resolved spec — configured but NOT read live in v1)
//   - MaxTradesPerDay (=9), DailyLossCircuit (=$700), MaxSameDirTrades (=2), MaxConsecutiveLossesIntraday (=3)
//
// Out of scope (per HANDOFF):
//   - No wobble enum (Momentum is single-leg).
//   - No §7 stop-protection correctness fix (not in reconstruction scope).
//
// NQ-only. ES is NO-GO per spec — do not deploy to ES.
// Compile via NT8 F5 with this file in /Documents/NinjaTrader 8/bin/Custom/Strategies/.

#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Momentum : Strategy
    {
        public enum MomentumExitSlopeMode { None, Simple, Hysteresis }
        public enum MomentumChartPreset { Candle_1m, UniRenko_10_20_40, Custom }
        public enum MomentumAnchorMode { None, EMA, VWAP }

        // ===== 0. TRINITY HUD GATE =====
        [NinjaScriptProperty]
        [Display(Name="Enable Trinity Filter", Description="If true, requires V3D HUD IsMomoAllowed for entry.", GroupName="0. Trinity Gate", Order=0)]
        public bool EnableTrinityFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Debug Entry Filters", GroupName="0. Trinity Gate", Order=1)]
        public bool DebugEntryFilters { get; set; } = false;

        // ===== 0b. TRADE EXPORT / TAXONOMY =====
        [NinjaScriptProperty]
        [Display(Name="Enable Trade Export", Description="Writes one CSV row per closed trade using the V4A taxonomy fields below.", GroupName="0b. Trade Export", Order=0)]
        public bool EnableTradeExport { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Account Name Filter", Description="Exact account name or ; separated allow-list. Blank allows all accounts.", GroupName="0b. Trade Export", Order=1)]
        public string AccountNameFilter { get; set; } = "OP-V4A-NQ-MOMO-1A";

        [NinjaScriptProperty]
        [Display(Name="Trade Log Folder", Description="Folder where Momentum V4A trade export CSVs are written.", GroupName="0b. Trade Export", Order=2)]
        public string TradeLogFolder { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V4A\TradeLog";

        [NinjaScriptProperty]
        [Display(Name="Model Version", GroupName="0b. Trade Export", Order=3)]
        public string ModelVersion { get; set; } = "V4A";

        [NinjaScriptProperty]
        [Display(Name="Strategy Taxonomy Name", GroupName="0b. Trade Export", Order=4)]
        public string StrategyTaxonomyName { get; set; } = "Momentum";

        [NinjaScriptProperty]
        [Display(Name="Mode / Variant", Description="A/B/C taxonomy label used by reporting.", GroupName="0b. Trade Export", Order=5)]
        public string ModeVariant { get; set; } = "A";

        [NinjaScriptProperty]
        [Display(Name="Template / Notes", GroupName="0b. Trade Export", Order=6)]
        public string TemplateNotes { get; set; } = "NQ A (V4A designed by Claude; V4 designed by Codex)";

        // ===== 1. RESEARCHED GATES (v3 spec) =====
        [NinjaScriptProperty]
        [Display(Name="Longs Only", Description="v3 spec true. Blocks all short entries regardless of DI cross direction.", GroupName="1. Researched Gates", Order=0)]
        public bool LongsOnly { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Enable HMM TrendDown Block", Description="v3 spec true. Blocks entries when RegimeMatrixHUD_V3D.HMMRegime == 'TrendDown'. Fails OPEN if HUD missing/UNKNOWN.", GroupName="1. Researched Gates", Order=1)]
        public bool EnableHMMTrendDownBlock { get; set; } = true;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name="Min Confidence Threshold (no-op v1)", Description="v3 spec: 0 for Momentum NQ. Path B per resolved HANDOFF §8 — configured but not read live in v1.", GroupName="1. Researched Gates", Order=2)]
        public int MinConfidenceThreshold { get; set; } = 0;

        // ===== 2. ORDERS =====
        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name="Contracts", GroupName="2. Orders", Order=0)]
        public int Contracts { get; set; } = 1;

        // ===== 3. ENTRY TIME WINDOWS (allow-list) =====
        [NinjaScriptProperty]
        [Display(Name="Enable Entry Time Windows", Description="When true, new entries are allowed only within enabled windows.", GroupName="3. Entry Time Windows", Order=0)]
        public bool EnableEntryTimeWindows { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Window 1 Enabled", GroupName="3. Entry Time Windows", Order=1)]
        public bool Window1Enabled { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 1 Start (HHmm)", GroupName="3. Entry Time Windows", Order=2)]
        public int Window1Start { get; set; } = 940;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 1 End (HHmm)", GroupName="3. Entry Time Windows", Order=3)]
        public int Window1End { get; set; } = 1000;

        [NinjaScriptProperty]
        [Display(Name="Window 2 Enabled", GroupName="3. Entry Time Windows", Order=4)]
        public bool Window2Enabled { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 2 Start (HHmm)", GroupName="3. Entry Time Windows", Order=5)]
        public int Window2Start { get; set; } = 1030;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 2 End (HHmm)", GroupName="3. Entry Time Windows", Order=6)]
        public int Window2End { get; set; } = 1115;

        [NinjaScriptProperty]
        [Display(Name="Window 3 Enabled", GroupName="3. Entry Time Windows", Order=7)]
        public bool Window3Enabled { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 3 Start (HHmm)", GroupName="3. Entry Time Windows", Order=8)]
        public int Window3Start { get; set; } = 1200;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 3 End (HHmm)", GroupName="3. Entry Time Windows", Order=9)]
        public int Window3End { get; set; } = 1545;

        // ===== 4. CIRCUITS & CAPS =====
        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name="Max Trades Per Day", Description="v3 spec: 9. 0 = OFF.", GroupName="4. Circuits & Caps", Order=0)]
        public int MaxTradesPerDay { get; set; } = 9;

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name="Daily Loss Circuit ($)", Description="v3 Research/SIM default: 700 (1C Mini NQ). Apex 50K preset: 250. 0 = OFF.", GroupName="4. Circuits & Caps", Order=1)]
        public double DailyLossCircuit { get; set; } = 700.0;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name="Max Same-Direction Trades", Description="v3 spec: 2. 0 = OFF.", GroupName="4. Circuits & Caps", Order=2)]
        public int MaxSameDirTrades { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name="Max Consecutive Losses Intraday", Description="v3 spec: 3. 0 = OFF.", GroupName="4. Circuits & Caps", Order=3)]
        public int MaxConsecutiveLossesIntraday { get; set; } = 3;

        // ===== 5. PRESET =====
        [NinjaScriptProperty]
        [Display(Name="Preset", GroupName="5. Preset", Order=0)]
        public MomentumChartPreset Preset { get; set; } = MomentumChartPreset.Candle_1m;

        [NinjaScriptProperty]
        [Display(Name="Apply Preset Defaults", GroupName="5. Preset", Order=1)]
        public bool ApplyPresetDefaults { get; set; } = true;

        // ===== 6. ANCHOR (optional) =====
        [NinjaScriptProperty]
        [Display(Name="Anchor Mode", GroupName="6. Anchor", Order=0)]
        public MomentumAnchorMode SideAnchor { get; set; } = MomentumAnchorMode.None;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name="Anchor EMA Period", GroupName="6. Anchor", Order=1)]
        public int AnchorEmaPeriod { get; set; } = 50;

        [NinjaScriptProperty]
        [Display(Name="Require Longs Above Anchor", GroupName="6. Anchor", Order=2)]
        public bool RequireLongsAboveAnchor { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name="Require Shorts Below Anchor", GroupName="6. Anchor", Order=3)]
        public bool RequireShortsBelowAnchor { get; set; } = false;

        // ===== 7. CI / ADX =====
        [NinjaScriptProperty, Range(2, 200)]
        [Display(Name="CI Period", GroupName="7. CI/ADX", Order=0)]
        public int CiPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name="ADX Period", GroupName="7. CI/ADX", Order=1)]
        public int AdxPeriod { get; set; } = 14;

        // ===== 8. ENTRY GATES =====
        [NinjaScriptProperty]
        [Display(Name="Use DI Cross Entry", GroupName="8. Entry", Order=0)]
        public bool UseDiCrossEntry { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Require ADX >= Threshold", GroupName="8. Entry", Order=1)]
        public bool UseAdxEntryThreshold { get; set; } = true;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name="ADX Entry Threshold", GroupName="8. Entry", Order=2)]
        public double AdxEntryThreshold { get; set; } = 18.0;

        [NinjaScriptProperty]
        [Display(Name="Require CI <= Threshold", GroupName="8. Entry", Order=3)]
        public bool UseCiEntryThreshold { get; set; } = false;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name="CI Entry Threshold", GroupName="8. Entry", Order=4)]
        public double CiEntryThreshold { get; set; } = 60.0;

        // ===== 9. EXIT SLOPE =====
        [NinjaScriptProperty]
        [Display(Name="Slope Exit Mode", GroupName="9. Exit - Slope", Order=0)]
        public MomentumExitSlopeMode SlopeExit { get; set; } = MomentumExitSlopeMode.Simple;

        [NinjaScriptProperty, Range(0, 1000)]
        [Display(Name="Min Hold Bars before slope exit", GroupName="9. Exit - Slope", Order=1)]
        public int MinHoldBars { get; set; } = 2;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name="CI Slope Bars (Exit)", GroupName="9. Exit - Simple", Order=0)]
        public int CiSlopeBarsExit { get; set; } = 5;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name="ADX Slope Bars (Exit)", GroupName="9. Exit - Simple", Order=1)]
        public int AdxSlopeBarsExit { get; set; } = 5;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name="Min CI Rise (Exit)", GroupName="9. Exit - Simple", Order=2)]
        public double CiRiseMinExit { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name="Min ADX Drop (Exit)", GroupName="9. Exit - Simple", Order=3)]
        public double AdxDropMinExit { get; set; } = 2.0;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name="Hysteresis Window Bars", GroupName="9. Exit - Hysteresis", Order=0)]
        public int HystWindow { get; set; } = 5;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name="Consecutive Fail Bars (M)", GroupName="9. Exit - Hysteresis", Order=1)]
        public int HystConsecutive { get; set; } = 2;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name="CI Rise Min (per bar)", GroupName="9. Exit - Hysteresis", Order=2)]
        public double HystCiRisePerBar { get; set; } = 0.5;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name="ADX Drop Min (per bar)", GroupName="9. Exit - Hysteresis", Order=3)]
        public double HystAdxDropPerBar { get; set; } = 0.5;

        // ===== 10. ATR BRACKET =====
        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name="ATR Stop Mult", GroupName="10. Stops/Targets", Order=0)]
        public double AtrStopMult { get; set; } = 0.75;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name="ATR Stop Length", GroupName="10. Stops/Targets", Order=1)]
        public int AtrStopLen { get; set; } = 14;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name="Min Stop (ticks)", GroupName="10. Stops/Targets", Order=2)]
        public int MinStopTicks { get; set; } = 2;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name="Risk:Reward", GroupName="10. Stops/Targets", Order=3)]
        public double RiskReward { get; set; } = 1.5;

        // ===== INTERNALS =====
        private ADX adx;
        private ATR atrStop;
        private EMA anchorEma;
        private Series<double> sessionVWAP;
        private double cumPV, cumVol;

        private Series<double> trSeries;
        private SUM sumTr;
        private MAX maxHigh;
        private MIN minLow;
        private Series<double> ci;

        private Series<double> dmPlus, dmMinus, sumDmPlus, sumDmMinus, sumTrDI, diPlusSeries, diMinusSeries;

        // Daily counters
        private int _sessionEntryCount = 0;
        private double _sessionPnLBaseline = 0.0;
        private bool _circuitTripped = false;

        // Same-direction cap
        private int  _sameDirCount  = 0;
        private int  _lastEntryDir  = 0;
        private bool _dirRegistered = false;

        // Consecutive loss cap
        private int _sessionLossCount = 0;
        private int _lastProcessedTradeCount = 0;
        private double _cycleStartCumProfit = 0.0;
        private bool _cycleInProgress = false;

        private bool _exportEntryOpen = false;
        private DateTime _exportEntryTime = DateTime.MinValue;
        private double _exportEntryPrice = 0.0;
        private int _exportEntryQty = 0;
        private string _exportDirection = "";
        private string _exportEntrySignal = "";
        private string _exportEntryHMM = "";
        private string _exportEntryRegime = "";
        private double _exportEntryConfidence = 0.0;

        // Hysteresis
        private int hystFailCount = 0;

        private const string LEntry = "LE";
        private const string SEntry = "SE";

        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        private void ApplyPreset()
        {
            if (!ApplyPresetDefaults) return;

            switch (Preset)
            {
                case MomentumChartPreset.Candle_1m:
                    CiPeriod = 14; AdxPeriod = 14;
                    CiSlopeBarsExit = 5; AdxSlopeBarsExit = 5;
                    CiRiseMinExit = 2.0; AdxDropMinExit = 2.0;
                    HystWindow = 5; HystConsecutive = 2;
                    HystCiRisePerBar = 0.5; HystAdxDropPerBar = 0.5;
                    CiEntryThreshold = 60; AdxEntryThreshold = 18;
                    AtrStopMult = 0.75; RiskReward = 1.5;
                    break;

                case MomentumChartPreset.UniRenko_10_20_40:
                    CiPeriod = 14; AdxPeriod = 14;
                    CiSlopeBarsExit = 8; AdxSlopeBarsExit = 8;
                    CiRiseMinExit = 1.0; AdxDropMinExit = 1.0;
                    HystWindow = 8; HystConsecutive = 2;
                    HystCiRisePerBar = 0.3; HystAdxDropPerBar = 0.3;
                    CiEntryThreshold = 60; AdxEntryThreshold = 18;
                    AtrStopMult = 0.8; RiskReward = 1.5;
                    break;

                case MomentumChartPreset.Custom:
                default: break;
            }
        }

        private double AnchorValue()
        {
            switch (SideAnchor)
            {
                case MomentumAnchorMode.EMA:  return anchorEma != null ? anchorEma[0] : Close[0];
                case MomentumAnchorMode.VWAP: return sessionVWAP != null ? sessionVWAP[0] : Close[0];
                default:                      return Close[0];
            }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Momentum";
                Description = "V2 lineage Momentum — researched gates, NQ-only.";
                Calculate = Calculate.OnPriceChange;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                TraceOrders = true;
            }
            else if (State == State.Configure)
            {
                ApplyPreset();
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(AdxPeriod);
                atrStop = ATR(Math.Max(5, AtrStopLen));

                if (SideAnchor == MomentumAnchorMode.EMA)
                    anchorEma = EMA(AnchorEmaPeriod);

                sessionVWAP = new Series<double>(this);

                trSeries = new Series<double>(this);
                sumTr = SUM(trSeries, CiPeriod);
                maxHigh = MAX(High, CiPeriod);
                minLow  = MIN(Low, CiPeriod);
                ci = new Series<double>(this);

                dmPlus       = new Series<double>(this);
                dmMinus      = new Series<double>(this);
                sumDmPlus    = new Series<double>(this);
                sumDmMinus   = new Series<double>(this);
                sumTrDI      = new Series<double>(this);
                diPlusSeries = new Series<double>(this);
                diMinusSeries= new Series<double>(this);

                AddChartIndicator(adx);
                if (anchorEma != null) AddChartIndicator(anchorEma);

                _cycleStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                _sessionPnLBaseline  = _cycleStartCumProfit;
                _lastProcessedTradeCount = SystemPerformance.AllTrades.Count;
            }
            else if (State == State.Realtime)
            {
                _sessionEntryCount = 0;
                _sessionPnLBaseline = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                _circuitTripped = false;
                _sessionLossCount = 0;
                _cycleInProgress = Position.MarketPosition != MarketPosition.Flat;
                _cycleStartCumProfit = _sessionPnLBaseline;
                _lastProcessedTradeCount = SystemPerformance.AllTrades.Count;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
            {
                if (Bars.IsFirstBarOfSession)
                {
                    cumPV = 0; cumVol = 0; hystFailCount = 0;
                    ResetSameDirCounter();
                    ResetSessionLossCounter();
                    ResetDailyCounters();
                }
                double typ0 = (High[0] + Low[0] + Close[0]) / 3.0;
                double vol0 = Math.Max(1.0, Volume[0]);
                cumPV += typ0 * vol0; cumVol += vol0;
                sessionVWAP[0] = (cumVol > 0 ? cumPV / cumVol : Close[0]);
                return;
            }

            if (Bars.IsFirstBarOfSession)
            {
                ResetSameDirCounter();
                ResetSessionLossCounter();
                ResetDailyCounters();
            }

            UpdateSessionLossCounterFromClosedCycle();
            UpdateDailyLossCircuit();

            int need = Math.Max(AdxPeriod, Math.Max(CiPeriod, AtrStopLen)) + 2;
            if (CurrentBar < need) return;

            // Session VWAP
            if (Bars.IsFirstBarOfSession) { cumPV = 0; cumVol = 0; }
            double typ = (High[0] + Low[0] + Close[0]) / 3.0;
            double vol = Math.Max(1.0, Volume[0]);
            cumPV  += typ * vol;
            cumVol += vol;
            sessionVWAP[0] = (cumVol > 0 ? cumPV / cumVol : Close[0]);

            // CI calc (0..100)
            double trBar = Math.Max(High[0] - Low[0], Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));
            trSeries[0] = trBar;

            double range = maxHigh[0] - minLow[0];
            double sumTR = Math.Max(1e-9, sumTr[0]);
            double denom = Math.Log10(Math.Max(2, CiPeriod));
            double numer = (range <= 1e-9) ? 1.0 : (sumTR / Math.Max(1e-9, range));
            double val = 100.0 * Math.Log10(numer) / denom;
            ci[0] = Math.Max(0.0, Math.Min(100.0, val));

            // DI+/DI-
            double high0 = High[0], low0 = Low[0], high1 = High[1], low1 = Low[1], close1 = Close[1];
            double tr = Math.Max(high0 - low0, Math.Max(Math.Abs(high0 - close1), Math.Abs(low0 - close1)));
            double upMove   = high0 - high1;
            double downMove = low1 - low0;

            double dmp = (upMove   > 0 && upMove   > downMove) ? upMove   : 0;
            double dmn = (downMove > 0 && downMove > upMove)   ? downMove : 0;

            if (CurrentBar == 1)
            {
                sumTrDI[0] = tr;
                sumDmPlus[0]  = dmp;
                sumDmMinus[0] = dmn;
            }
            else if (CurrentBar < AdxPeriod)
            {
                sumTrDI[0]    = sumTrDI[1]    + tr;
                sumDmPlus[0]  = sumDmPlus[1]  + dmp;
                sumDmMinus[0] = sumDmMinus[1] + dmn;
            }
            else
            {
                sumTrDI[0]    = sumTrDI[1]    - (sumTrDI[1]    / AdxPeriod) + tr;
                sumDmPlus[0]  = sumDmPlus[1]  - (sumDmPlus[1]  / AdxPeriod) + dmp;
                sumDmMinus[0] = sumDmMinus[1] - (sumDmMinus[1] / AdxPeriod) + dmn;
            }

            double sTr = sumTrDI[0].ApproxCompare(0) == 0 ? 1e-9 : sumTrDI[0];
            diPlusSeries[0]  = 100.0 * (sumDmPlus[0]  / sTr);
            diMinusSeries[0] = 100.0 * (sumDmMinus[0] / sTr);

            bool crossUp   = CrossAbove(diPlusSeries, diMinusSeries, 1);
            bool crossDown = CrossBelow(diPlusSeries, diMinusSeries, 1);

            bool adxOk = !UseAdxEntryThreshold || (adx[0] >= AdxEntryThreshold);
            bool ciOk  = !UseCiEntryThreshold  || (ci[0]  <= CiEntryThreshold);

            double anchor = AnchorValue();
            bool anchorLongOK  = (SideAnchor == MomentumAnchorMode.None) || !RequireLongsAboveAnchor  || Close[0] > anchor;
            bool anchorShortOK = (SideAnchor == MomentumAnchorMode.None) || !RequireShortsBelowAnchor || Close[0] < anchor;

            bool inWindow     = IsInAllowedEntryWindow();
            bool hmmBlocked   = IsHmmTrendDownBlocked();
            bool lossBlocked  = ConsecutiveLossBlocked();
            bool tradesCapped = TradesCapReached();
            bool circuitHit   = _circuitTripped;

            // ===== ENTRIES (flat only) =====
            if (Position.MarketPosition == MarketPosition.Flat
                && inWindow && !hmmBlocked && !lossBlocked && !tradesCapped && !circuitHit
                && adxOk && ciOk)
            {
                bool doLong  = UseDiCrossEntry ? crossUp   : (SideAnchor == MomentumAnchorMode.None ? true : Close[0] >= anchor);
                bool doShort = UseDiCrossEntry ? crossDown : (SideAnchor == MomentumAnchorMode.None ? true : Close[0] <= anchor);

                if (LongsOnly) doShort = false;

                if (doLong && anchorLongOK && !SameDirBlocked(1))
                    SubmitLongWithStops();
                else if (doShort && anchorShortOK && !SameDirBlocked(-1))
                    SubmitShortWithStops();
                else if (DebugEntryFilters && (doLong || doShort))
                {
                    string why = "";
                    if (doLong && !anchorLongOK) why += "anchorLong ";
                    if (doShort && !anchorShortOK) why += "anchorShort ";
                    if (doLong && SameDirBlocked(1)) why += "sameDir ";
                    if (doShort && SameDirBlocked(-1)) why += "sameDir ";
                    PrintDebugEntryBlock(doLong ? "LONG" : "SHORT", why == "" ? "entry_filter" : why.Trim());
                }
            }
            else if (DebugEntryFilters && Position.MarketPosition == MarketPosition.Flat && (crossUp || crossDown))
            {
                string why = "";
                if (!inWindow) why += "window ";
                if (hmmBlocked) why += "hmmTrendDown ";
                if (lossBlocked) why += "consecLosses ";
                if (tradesCapped) why += "maxTradesDay ";
                if (circuitHit) why += "circuit ";
                if (!adxOk) why += "ADX ";
                if (!ciOk) why += "CI ";
                PrintDebugEntryBlock(crossUp ? "LONG" : "SHORT", why == "" ? "entry_filter" : why.Trim());
            }

            // ===== SLOPE-BASED EXIT =====
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                int bse = (Position.MarketPosition == MarketPosition.Long)
                    ? BarsSinceEntryExecution(0, LEntry, 0)
                    : BarsSinceEntryExecution(0, SEntry, 0);

                if (bse >= MinHoldBars)
                {
                    bool simpleFail = false;
                    bool hystFail = false;

                    if (SlopeExit == MomentumExitSlopeMode.Simple && CurrentBar > Math.Max(CiSlopeBarsExit, AdxSlopeBarsExit))
                    {
                        double ciRise  = ci[0] - ci[CiSlopeBarsExit];
                        double adxDrop = adx[AdxSlopeBarsExit] - adx[0];
                        simpleFail = (ciRise >= CiRiseMinExit) || (adxDrop >= AdxDropMinExit);
                    }

                    if (SlopeExit == MomentumExitSlopeMode.Hysteresis && CurrentBar > 1)
                    {
                        double ciRiseBar  = ci[0] - ci[1];
                        double adxDropBar = adx[1] - adx[0];
                        bool thisBarFail = (ciRiseBar >= HystCiRisePerBar) || (adxDropBar >= HystAdxDropPerBar);

                        if (thisBarFail) hystFailCount++;
                        else hystFailCount = Math.Max(0, hystFailCount - 1);

                        hystFail = hystFailCount >= HystConsecutive;
                    }

                    if (simpleFail || hystFail)
                    {
                        if (Position.MarketPosition == MarketPosition.Long)
                            ExitLong("SlopeX", LEntry);
                        else
                            ExitShort("SlopeX", SEntry);

                        hystFailCount = 0;
                    }
                }
            }
        }

        // ===== ENTRY SUBMISSION =====
        private void SubmitLongWithStops()
        {
            if (!IsBotAllowedByTrinity())
            {
                PrintDebugEntryBlock("LONG", "trinity");
                return;
            }

            double risk = Math.Max(Math.Max(0.01, AtrStopMult) * atrStop[0], Math.Max(1, MinStopTicks) * TickSize);
            double stp  = RT(Close[0] - risk);
            double tgt  = RT(Close[0] + risk * Math.Max(0.1, RiskReward));
            SetStopLoss(LEntry, CalculationMode.Price, stp, false);
            SetProfitTarget(LEntry, CalculationMode.Price, tgt);
            EnterLong(Contracts, LEntry);
        }

        private void SubmitShortWithStops()
        {
            if (!IsBotAllowedByTrinity())
            {
                PrintDebugEntryBlock("SHORT", "trinity");
                return;
            }

            double risk = Math.Max(Math.Max(0.01, AtrStopMult) * atrStop[0], Math.Max(1, MinStopTicks) * TickSize);
            double stp  = RT(Close[0] + risk);
            double tgt  = RT(Close[0] - risk * Math.Max(0.1, RiskReward));
            SetStopLoss(SEntry, CalculationMode.Price, stp, false);
            SetProfitTarget(SEntry, CalculationMode.Price, tgt);
            EnterShort(Contracts, SEntry);
        }

        // ===== V3D HUD HELPERS =====
        private Indicators.RegimeMatrixHUD_V3D GetV3DHud()
        {
            string baseSymbol = ResolveTrinitySymbol(Instrument.MasterInstrument.Name);
            Indicators.RegimeMatrixHUD_V3D hud = null;
            Indicators.RegimeMatrixHUD_V3D.InstancesV3D.TryGetValue(baseSymbol, out hud);
            return hud;
        }

        private bool IsBotAllowedByTrinity()
        {
            if (!EnableTrinityFilter) return true;
            var hud = GetV3DHud();
            return hud != null && hud.IsMomoAllowed;
        }

        private bool IsHmmTrendDownBlocked()
        {
            if (!EnableHMMTrendDownBlock) return false;
            var hud = GetV3DHud();
            if (hud == null) return false;            // fail open
            string state = (hud.HMMRegime ?? "").Trim();
            if (string.IsNullOrEmpty(state) || string.Equals(state, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
                return false;                          // fail open
            return string.Equals(state, "TrendDown", StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveTrinitySymbol(string sym)
        {
            if (string.IsNullOrEmpty(sym)) return sym;
            sym = sym.Trim().ToUpper();
            if (sym == "MES") return "ES";
            if (sym == "MNQ") return "NQ";
            if (sym == "MGC") return "GC";
            if (sym == "MCL") return "CL";
            return sym;
        }

        private void PrintDebugEntryBlock(string side, string reason)
        {
            if (!DebugEntryFilters) return;

            var hud = GetV3DHud();
            string lane    = hud != null ? (hud.IsMomoAllowed ? "1" : "0") : "NO_HUD";
            string regime  = hud != null ? hud.FinalRegime : "NO_HUD";
            string hmm     = hud != null ? hud.HMMRegime : "NO_HUD";
            string stale   = hud != null ? hud.StaleDataFlag.ToString() : "NO_HUD";
            string account = Account != null ? Account.Name : "NO_ACCOUNT";

            Print($"{Time[0]} Momentum debug | account={account} symbol={Instrument.FullName} side={side} lane=AllowMomo:{lane} finalRegime={regime} hmm={hmm} stale={stale} decision=BLOCK reason={reason}");
        }

        // ===== TIME WINDOWS (allow-list) =====
        private bool IsInAllowedEntryWindow()
        {
            if (!EnableEntryTimeWindows) return true;
            int hhmm = Time[0].Hour * 100 + Time[0].Minute;
            return InWindow(Window1Enabled, Window1Start, Window1End, hhmm)
                || InWindow(Window2Enabled, Window2Start, Window2End, hhmm)
                || InWindow(Window3Enabled, Window3Start, Window3End, hhmm);
        }

        private bool InWindow(bool enabled, int start, int end, int hhmm)
        {
            if (!enabled) return false;
            if (start == 0 && end == 0) return false;
            if (end > start) return hhmm >= start && hhmm < end;
            if (end < start) return hhmm >= start || hhmm < end;
            return false;
        }

        // ===== SAME-DIRECTION CAP =====
        private bool SameDirBlocked(int dir)
        {
            return MaxSameDirTrades > 0
                && dir == _lastEntryDir
                && _sameDirCount >= MaxSameDirTrades;
        }

        private void RegisterDirEntry(int dir)
        {
            if (dir == _lastEntryDir) _sameDirCount++;
            else { _lastEntryDir = dir; _sameDirCount = 1; }
        }

        private void ResetSameDirCounter()
        {
            _sameDirCount = 0;
            _lastEntryDir = 0;
        }

        // ===== CONSECUTIVE LOSS CAP =====
        private bool ConsecutiveLossBlocked()
        {
            return MaxConsecutiveLossesIntraday > 0
                && _sessionLossCount >= MaxConsecutiveLossesIntraday;
        }

        private void UpdateSessionLossCounterFromClosedCycle()
        {
            if (!_cycleInProgress || Position.MarketPosition != MarketPosition.Flat) return;

            int tradeCount = SystemPerformance.AllTrades.Count;
            if (tradeCount <= _lastProcessedTradeCount) return;

            double cyclePnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - _cycleStartCumProfit;
            if (cyclePnL < 0) _sessionLossCount++;

            _cycleInProgress = false;
            _cycleStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            _lastProcessedTradeCount = tradeCount;
        }

        private void ResetSessionLossCounter()
        {
            _sessionLossCount = 0;
            _cycleInProgress = Position.MarketPosition != MarketPosition.Flat;
            _cycleStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            _lastProcessedTradeCount = SystemPerformance.AllTrades.Count;
        }

        // ===== DAILY COUNTERS =====
        private bool TradesCapReached()
        {
            return MaxTradesPerDay > 0 && _sessionEntryCount >= MaxTradesPerDay;
        }

        private void ResetDailyCounters()
        {
            _sessionEntryCount = 0;
            _sessionPnLBaseline = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            _circuitTripped = false;
        }

        private void UpdateDailyLossCircuit()
        {
            if (DailyLossCircuit <= 0 || _circuitTripped) return;
            double sessionPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - _sessionPnLBaseline;
            if (sessionPnL <= -DailyLossCircuit)
            {
                _circuitTripped = true;
                Print($"[CIRCUIT] {Time[0]} {Name} {Instrument.FullName} daily loss circuit tripped: sessionPnL={sessionPnL:F2} limit=-{DailyLossCircuit:F2}");
            }
        }

        // ===== ON EXECUTION =====
        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId,
            DateTime time)
        {
            if (execution?.Order == null) return;
            var order = execution.Order;

            bool isEntry = order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.SellShort;
            TrackTradeExport(execution, price, quantity, marketPosition, time);

            if (isEntry && !_cycleInProgress)
            {
                _cycleStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                _cycleInProgress = true;
            }

            if (isEntry && !_dirRegistered)
            {
                if (order.OrderAction == OrderAction.Buy)            { RegisterDirEntry(1);  _dirRegistered = true; }
                else if (order.OrderAction == OrderAction.SellShort) { RegisterDirEntry(-1); _dirRegistered = true; }
            }

            if (isEntry &&
                (string.Equals(order.Name, LEntry, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(order.Name, SEntry, StringComparison.OrdinalIgnoreCase)))
                _sessionEntryCount++;

            if (marketPosition == MarketPosition.Flat)
                _dirRegistered = false;
        }

        private void TrackTradeExport(Execution execution, double price, int quantity, MarketPosition marketPosition, DateTime time)
        {
            if (!EnableTradeExport || execution == null || execution.Order == null || quantity <= 0)
                return;

            string accountName = execution.Account != null ? execution.Account.Name : (Account == null ? "" : Account.Name);
            if (!IsExportAccountAllowed(accountName))
                return;

            Order order = execution.Order;
            bool isEntry = order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.SellShort;
            bool isExit = order.OrderAction == OrderAction.Sell || order.OrderAction == OrderAction.BuyToCover;

            if (isEntry)
            {
                string direction = order.OrderAction == OrderAction.Buy ? "LONG" : "SHORT";
                if (!_exportEntryOpen || _exportDirection != direction)
                {
                    _exportEntryOpen = true;
                    _exportEntryTime = time;
                    _exportEntryPrice = price;
                    _exportEntryQty = quantity;
                    _exportDirection = direction;
                    _exportEntrySignal = order.Name ?? "";

                    var hud = GetV3DHud();
                    _exportEntryHMM = hud == null ? "NO_HUD" : (hud.HMMRegime ?? "");
                    _exportEntryRegime = hud == null ? "NO_HUD" : (hud.FinalRegime ?? "");
                    _exportEntryConfidence = hud == null ? 0.0 : hud.RegimeConfidence;
                }
                else
                {
                    double notional = _exportEntryPrice * _exportEntryQty + price * quantity;
                    _exportEntryQty += quantity;
                    _exportEntryPrice = notional / Math.Max(1, _exportEntryQty);
                }
                return;
            }

            if (!isExit || !_exportEntryOpen)
                return;

            if (marketPosition != MarketPosition.Flat && Position.MarketPosition != MarketPosition.Flat)
                return;

            WriteTradeExportRow(accountName, price, time, order.Name ?? "UNKNOWN");
            _exportEntryOpen = false;
            _exportEntryTime = DateTime.MinValue;
            _exportEntryPrice = 0.0;
            _exportEntryQty = 0;
            _exportDirection = "";
            _exportEntrySignal = "";
            _exportEntryHMM = "";
            _exportEntryRegime = "";
            _exportEntryConfidence = 0.0;
        }

        private void WriteTradeExportRow(string accountName, double exitPrice, DateTime exitTime, string exitReason)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(TradeLogFolder))
                    return;

                if (!Directory.Exists(TradeLogFolder))
                    Directory.CreateDirectory(TradeLogFolder);

                string path = Path.Combine(TradeLogFolder, SafeFileName(accountName) + "_TradeLog.csv");
                bool writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;

                string symbol = ResolveTrinitySymbol(Instrument.MasterInstrument.Name);
                double sign = _exportDirection == "LONG" ? 1.0 : -1.0;
                double grossPnl = sign * (exitPrice - _exportEntryPrice) * Math.Max(1, _exportEntryQty) * Instrument.MasterInstrument.PointValue;
                double netPnl = grossPnl;
                double ticks = sign * (exitPrice - _exportEntryPrice) / TickSize;
                string winLoss = netPnl > 0.0 ? "WIN" : (netPnl < 0.0 ? "LOSS" : "SCRATCH");
                var hud = GetV3DHud();
                string exitHMM = hud == null ? "NO_HUD" : (hud.HMMRegime ?? "");
                string exitRegime = hud == null ? "NO_HUD" : (hud.FinalRegime ?? "");
                double exitConfidence = hud == null ? 0.0 : hud.RegimeConfidence;

                if (writeHeader)
                {
                    File.WriteAllText(path,
                        "trade_date,entry_time,exit_time,model_version,account,strategy_name,bot_name,ab_mode,symbol,instrument,direction,contracts,entry_price,exit_price,gross_pnl,net_pnl,ticks,win_loss,exit_reason,entry_signal,entry_hmm,entry_regime,entry_confidence,exit_hmm,exit_regime,exit_confidence,template_notes,export_timestamp" + Environment.NewLine);
                }

                string[] fields = new string[]
                {
                    _exportEntryTime.ToString("yyyy-MM-dd"),
                    _exportEntryTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    exitTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    ModelVersion,
                    accountName,
                    StrategyTaxonomyName,
                    Name ?? "Momentum",
                    ModeVariant,
                    symbol,
                    Instrument.FullName,
                    _exportDirection,
                    _exportEntryQty.ToString(CultureInfo.InvariantCulture),
                    _exportEntryPrice.ToString("F4", CultureInfo.InvariantCulture),
                    exitPrice.ToString("F4", CultureInfo.InvariantCulture),
                    grossPnl.ToString("F2", CultureInfo.InvariantCulture),
                    netPnl.ToString("F2", CultureInfo.InvariantCulture),
                    ticks.ToString("F1", CultureInfo.InvariantCulture),
                    winLoss,
                    exitReason,
                    _exportEntrySignal,
                    _exportEntryHMM,
                    _exportEntryRegime,
                    _exportEntryConfidence.ToString("F0", CultureInfo.InvariantCulture),
                    exitHMM,
                    exitRegime,
                    exitConfidence.ToString("F0", CultureInfo.InvariantCulture),
                    TemplateNotes,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                File.AppendAllText(path, string.Join(",", Array.ConvertAll(fields, SafeCsv)) + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Print("[Momentum TradeExport] Write error: " + ex.Message);
            }
        }

        private bool IsExportAccountAllowed(string accountName)
        {
            if (string.IsNullOrWhiteSpace(AccountNameFilter))
                return true;

            foreach (string raw in AccountNameFilter.Split(new char[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(accountName, raw.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "UNKNOWN";

            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            return value.Replace(" ", "_");
        }

        private static string SafeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }
}
