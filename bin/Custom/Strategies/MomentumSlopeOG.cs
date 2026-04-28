// CC BY-NC 4.0
#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class MomentumSlopeStrategy : Strategy
    {
        // ===== Enums =====
        public enum ExitSlopeMode { None, Simple, Hysteresis }
        public enum ChartPreset { Candle_1m, UniRenko_10_20_40, Custom }
        public enum AnchorMode { None, EMA, VWAP }

        // ===== Orders / contracts =====
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name="Contracts", GroupName="Orders", Order=0)]
        public int Contracts { get; set; } = 1;

        // ===== Presets =====
        [NinjaScriptProperty]
        [Display(Name="Preset", GroupName="Presets", Order=0)]
        public ChartPreset Preset { get; set; } = ChartPreset.Candle_1m;

        [NinjaScriptProperty]
        [Display(Name="Apply Preset Defaults", GroupName="Presets", Order=1)]
        public bool ApplyPresetDefaults { get; set; } = true;

        // ===== Entry Direction =====
        [NinjaScriptProperty]
        [Display(Name="Allow Longs", GroupName="Entry Direction", Order=0)]
        public bool AllowLongs { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Allow Shorts", GroupName="Entry Direction", Order=1)]
        public bool AllowShorts { get; set; } = true;

        // ===== Anchor (optional) =====
        [NinjaScriptProperty]
        [Display(Name="Anchor Mode", GroupName="Anchor", Order=0)]
        public AnchorMode SideAnchor { get; set; } = AnchorMode.None;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name="Anchor EMA Period", GroupName="Anchor", Order=1)]
        public int AnchorEmaPeriod { get; set; } = 50;

        [NinjaScriptProperty]
        [Display(Name="Require Longs Above Anchor", GroupName="Anchor", Order=2)]
        public bool RequireLongsAboveAnchor { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name="Require Shorts Below Anchor", GroupName="Anchor", Order=3)]
        public bool RequireShortsBelowAnchor { get; set; } = false;

        // ===== CI / ADX parameters =====
        [NinjaScriptProperty, Range(2, 200)]
        [Display(Name="CI Period", GroupName="CI/ADX", Order=0)]
        public int CiPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name="ADX Period", GroupName="CI/ADX", Order=1)]
        public int AdxPeriod { get; set; } = 14;

        // ===== Entry gates (toggles) =====
        [NinjaScriptProperty]
        [Display(Name="Use DI Cross Entry", GroupName="Entry", Order=0, Description="If on, DI+ cross above DI- goes long, DI- cross below DI+ goes short.")]
        public bool UseDiCrossEntry { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Require ADX ≥ Threshold", GroupName="Entry", Order=1)]
        public bool UseAdxEntryThreshold { get; set; } = true;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name="ADX Entry Threshold", GroupName="Entry", Order=2)]
        public double AdxEntryThreshold { get; set; } = 18.0;

        [NinjaScriptProperty]
        [Display(Name="Require CI ≤ Threshold", GroupName="Entry", Order=3)]
        public bool UseCiEntryThreshold { get; set; } = false;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name="CI Entry Threshold", GroupName="Entry", Order=4)]
        public double CiEntryThreshold { get; set; } = 60.0;

        // (kept for flexibility; default off)
        [NinjaScriptProperty]
        [Display(Name="Use CI↓ & ADX↑ Slope Gate (Entry)", GroupName="Entry", Order=5)]
        public bool UseSlopeGateOnEntry { get; set; } = false;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name="CI Slope Bars (Entry)", GroupName="Entry", Order=6)]
        public int CiSlopeBarsEntry { get; set; } = 5;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name="ADX Slope Bars (Entry)", GroupName="Entry", Order=7)]
        public int AdxSlopeBarsEntry { get; set; } = 5;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name="Min CI Decrease (Entry)", GroupName="Entry", Order=8)]
        public double CiDecreaseMinEntry { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name="Min ADX Increase (Entry)", GroupName="Entry", Order=9)]
        public double AdxIncreaseMinEntry { get; set; } = 2.0;

        // ===== Exit slope modes =====
        [NinjaScriptProperty]
        [Display(Name="Slope Exit Mode", GroupName="Exit - Slope", Order=0)]
        public ExitSlopeMode SlopeExit { get; set; } = ExitSlopeMode.Simple;

        [NinjaScriptProperty, Range(0, 1000)]
        [Display(Name="Min Hold Bars before slope exit", GroupName="Exit - Slope", Order=1)]
        public int MinHoldBars { get; set; } = 2;

        // Simple exit thresholds (N-bar total change)
        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name="CI Slope Bars (Exit)", GroupName="Exit - Simple", Order=0)]
        public int CiSlopeBarsExit { get; set; } = 5;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name="ADX Slope Bars (Exit)", GroupName="Exit - Simple", Order=1)]
        public int AdxSlopeBarsExit { get; set; } = 5;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name="Min CI Rise (Exit)", GroupName="Exit - Simple", Order=2)]
        public double CiRiseMinExit { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name="Min ADX Drop (Exit)", GroupName="Exit - Simple", Order=3)]
        public double AdxDropMinExit { get; set; } = 2.0;

        // Hysteresis (per-bar change for M bars)
        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name="Hysteresis Window Bars", GroupName="Exit - Hysteresis", Order=0)]
        public int HystWindow { get; set; } = 5;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name="Consecutive Fail Bars (M)", GroupName="Exit - Hysteresis", Order=1)]
        public int HystConsecutive { get; set; } = 2;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name="CI Rise Min (per bar)", GroupName="Exit - Hysteresis", Order=2)]
        public double HystCiRisePerBar { get; set; } = 0.5;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name="ADX Drop Min (per bar)", GroupName="Exit - Hysteresis", Order=3)]
        public double HystAdxDropPerBar { get; set; } = 0.5;

        // ===== ATR bracket (simple) =====
        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name="ATR Stop Mult", GroupName="Stops/Targets", Order=0)]
        public double AtrStopMult { get; set; } = 0.75;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name="ATR Stop Length", GroupName="Stops/Targets", Order=1)]
        public int AtrStopLen { get; set; } = 14;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name="Min Stop (ticks)", GroupName="Stops/Targets", Order=2)]
        public int MinStopTicks { get; set; } = 2;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name="Risk:Reward", GroupName="Stops/Targets", Order=3)]
        public double RiskReward { get; set; } = 1.5;

        // ===== Daily/Session Guardrails =====
        [NinjaScriptProperty]
        [Display(Name="Enable Daily Guards", GroupName="Guards", Order=0)]
        public bool EnableDailyGuards { get; set; } = true;

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name="Profit Target ($)", GroupName="Guards", Order=1)]
        public double DailyProfitTarget { get; set; } = 1000.0;

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name="Loss Limit ($)", GroupName="Guards", Order=2)]
        public double DailyLossLimit { get; set; } = 500.0;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name="Max Wins", GroupName="Guards", Order=3)]
        public int MaxWins { get; set; } = 5;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name="Max Losses", GroupName="Guards", Order=4)]
        public int MaxLosses { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name="Flatten On Trigger", GroupName="Guards", Order=5)]
        public bool FlattenOnTrigger { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Reset On New Session", GroupName="Guards", Order=6)]
        public bool ResetOnNewSession { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Ignore Historical (start guards at enable)", GroupName="Guards", Order=7)]
        public bool GuardsIgnoreHistorical { get; set; } = true;

        // ===== Time Filters =====
		[NinjaScriptProperty]
		[Display(Name="Enable Time Filter", GroupName="Time Filters", Order=0)]
		public bool EnableTimeFilter { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name="Start Time 1 (HHmmss)", GroupName="Time Filters", Order=1)]
		public int StartTime1 { get; set; } = 93000;   // 09:30:00
		
		[NinjaScriptProperty]
		[Display(Name="End Time 1 (HHmmss)", GroupName="Time Filters", Order=2)]
		public int EndTime1 { get; set; } = 120000;    // 12:00:00
		
		[NinjaScriptProperty]
		[Display(Name="Use Block Window 1", GroupName="Time Filters", Order=3)]
		public bool UseBlock1 { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name="Block1 Start (HHmmss)", GroupName="Time Filters", Order=4)]
		public int Block1Start { get; set; } = 95900;  // 09:59:00
		
		[NinjaScriptProperty]
		[Display(Name="Block1 End (HHmmss)", GroupName="Time Filters", Order=5)]
		public int Block1End { get; set; } = 100600;   // 10:06:00
		
		[NinjaScriptProperty]
		[Display(Name="Use Block Window 2", GroupName="Time Filters", Order=6)]
		public bool UseBlock2 { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name="Block2 Start (HHmmss)", GroupName="Time Filters", Order=7)]
		public int Block2Start { get; set; } = 102800; // 10:28:00
		
		[NinjaScriptProperty]
		[Display(Name="Block2 End (HHmmss)", GroupName="Time Filters", Order=8)]
		public int Block2End { get; set; } = 103500;   // 10:35:00
		
		[NinjaScriptProperty]
		[Display(Name="Use Block Window 3", GroupName="Time Filters", Order=9)]
		public bool UseBlock3 { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name="Block3 Start (HHmmss)", GroupName="Time Filters", Order=10)]
		public int Block3Start { get; set; } = 50000;  // 05:00:00
		
		[NinjaScriptProperty]
		[Display(Name="Block3 End (HHmmss)", GroupName="Time Filters", Order=11)]
		public int Block3End { get; set; } = 70000;    // 07:00:00
		
		[NinjaScriptProperty]
		[Display(Name="Flatten Near End", GroupName="Time Filters", Order=12)]
		public bool FlattenNearEnd { get; set; } = false;
		
		[NinjaScriptProperty, Range(0, 1800)]
		[Display(Name="Flatten Seconds Before End", GroupName="Time Filters", Order=13)]
		public int FlattenBufferSeconds { get; set; } = 300; // 5 minutes

        // ===== Internals =====
        private ADX adx;
        private ATR atrStop;
        private EMA anchorEma;
        private Series<double> sessionVWAP;  // internal VWAP
        private double cumPV, cumVol;

        // CI internals
        private Series<double> trSeries;
        private SUM sumTr;
        private MAX maxHigh;
        private MIN minLow;
        private Series<double> ci;

        // DI+/DI- (Wilder smoothing)
        private Series<double> dmPlus, dmMinus, sumDmPlus, sumDmMinus, sumTrDI, diPlusSeries, diMinusSeries;

        // guards
        private double sessionPnLBaseline = 0.0;
        private int lastTradeCount = 0;
        private int winCount = 0;
        private int lossCount = 0;
        private bool tradingHalted = false;

        // hysteresis
        private int hystFailCount = 0;

        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        // ===== Preset loader =====
        private void ApplyPreset()
        {
            if (!ApplyPresetDefaults) return;

            switch (Preset)
            {
                case ChartPreset.Candle_1m:
                    CiPeriod = 14; AdxPeriod = 14;
                    CiSlopeBarsExit = 5; AdxSlopeBarsExit = 5;
                    CiRiseMinExit = 2.0; AdxDropMinExit = 2.0;
                    HystWindow = 5; HystConsecutive = 2;
                    HystCiRisePerBar = 0.5; HystAdxDropPerBar = 0.5;
                    CiEntryThreshold = 60; AdxEntryThreshold = 18;
                    AtrStopMult = 0.75; RiskReward = 1.5;
                    break;

                case ChartPreset.UniRenko_10_20_40:
                    CiPeriod = 14; AdxPeriod = 14;
                    CiSlopeBarsExit = 8; AdxSlopeBarsExit = 8;
                    CiRiseMinExit = 1.0; AdxDropMinExit = 1.0;
                    HystWindow = 8; HystConsecutive = 2;
                    HystCiRisePerBar = 0.3; HystAdxDropPerBar = 0.3;
                    CiEntryThreshold = 60; AdxEntryThreshold = 18;
                    AtrStopMult = 0.8; RiskReward = 1.5;
                    break;

                case ChartPreset.Custom:
                default: break;
            }
        }

        private double AnchorValue()
        {
            switch (SideAnchor)
            {
                case AnchorMode.EMA:  return anchorEma != null ? anchorEma[0] : Close[0];
                case AnchorMode.VWAP: return sessionVWAP != null ? sessionVWAP[0] : Close[0];
                default:              return Close[0];
            }
        }

        // ==== Time helpers ====
        private static int ToSecondsHHmmss(int hhmmss)
        {
            int h = hhmmss / 10000;
            int m = (hhmmss % 10000) / 100;
            int s = hhmmss % 100;
            return h * 3600 + m * 60 + s;
        }

        private bool IsInAllowedWindow()
		{
		    if (!EnableTimeFilter) return true;
		    int t = ToTime(Time[0]);
		
		    bool inMain = (t >= StartTime1 && t <= EndTime1);
		    if (!inMain) return false;
		
		    bool blocked1 = UseBlock1 && (t >= Block1Start && t <= Block1End);
		    bool blocked2 = UseBlock2 && (t >= Block2Start && t <= Block2End);
		    bool blocked3 = UseBlock3 && (t >= Block3Start && t <= Block3End);
		
		    return !(blocked1 || blocked2 || blocked3);
		}

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "MomentumSlopeStrategy v1.2";
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

                if (SideAnchor == AnchorMode.EMA)
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

                // guards baseline (will be reset again at Realtime if GuardsIgnoreHistorical = true)
                sessionPnLBaseline = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                lastTradeCount = SystemPerformance.AllTrades.Count;
            }
            else if (State == State.Realtime)
            {
                // Start counting from enable time to avoid historical PnL/wins tripping guards
                if (GuardsIgnoreHistorical)
                {
                    sessionPnLBaseline = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                    lastTradeCount     = SystemPerformance.AllTrades.Count;
                    winCount = 0;
                    lossCount = 0;
                    tradingHalted = false;
                }
            }
        }

        protected override void OnBarUpdate()
        {
            // Basic early guard
            if (CurrentBar < 2)
            {
                if (Bars.IsFirstBarOfSession)
                {
                    cumPV = 0; cumVol = 0; hystFailCount = 0;
                    if (ResetOnNewSession)
                    {
                        sessionPnLBaseline = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                        winCount = 0; lossCount = 0; tradingHalted = false;
                    }
                }
                double typ0 = (High[0] + Low[0] + Close[0]) / 3.0;
                double vol0 = Math.Max(1.0, Volume[0]);
                cumPV += typ0 * vol0; cumVol += vol0;
                sessionVWAP[0] = (cumVol > 0 ? cumPV / cumVol : Close[0]);
                return;
            }

            // If guards are disabled in the UI, clear any prior halt immediately
            if (!EnableDailyGuards && tradingHalted)
                tradingHalted = false;

            // Unified warm-up: wait until ADX/CI/ATR all have history
            int need = Math.Max(AdxPeriod, Math.Max(CiPeriod, AtrStopLen)) + 2;
            if (CurrentBar < need)
                return;

            // --- session VWAP update ---
            if (Bars.IsFirstBarOfSession) { cumPV = 0; cumVol = 0; }
            double typ = (High[0] + Low[0] + Close[0]) / 3.0;
            double vol = Math.Max(1.0, Volume[0]);
            cumPV  += typ * vol;
            cumVol += vol;
            sessionVWAP[0] = (cumVol > 0 ? cumPV / cumVol : Close[0]);

            // --- CI calc (0..100) ---
            double trBar = Math.Max(High[0] - Low[0], Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));
            trSeries[0] = trBar;

            double range = maxHigh[0] - minLow[0];
            double sumTR = Math.Max(1e-9, sumTr[0]);
            double denom = Math.Log10(Math.Max(2, CiPeriod));
            double numer = (range <= 1e-9) ? 1.0 : (sumTR / Math.Max(1e-9, range));
            double val = 100.0 * Math.Log10(numer) / denom;
            ci[0] = Math.Max(0.0, Math.Min(100.0, val));

            // --- DI+/DI- (Wilder smoothing) ---
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
                sumTrDI[0]      = sumTrDI[1] + tr;
                sumDmPlus[0]    = sumDmPlus[1]  + dmp;
                sumDmMinus[0]   = sumDmMinus[1] + dmn;
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

            // Crosses must be checked AFTER DI series updated
            bool crossUp   = CrossAbove(diPlusSeries, diMinusSeries, 1);
            bool crossDown = CrossBelow(diPlusSeries, diMinusSeries, 1);

            // --- guards update ---
            int tc = SystemPerformance.AllTrades.Count;
            if (tc > lastTradeCount)
            {
                var last = SystemPerformance.AllTrades[tc - 1];
                if (last.ProfitCurrency > 0) winCount++;
                else if (last.ProfitCurrency < 0) lossCount++;
                lastTradeCount = tc;
            }
            double sessionPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionPnLBaseline;

            if (EnableDailyGuards && !tradingHalted)
            {
                bool hitProfit = sessionPnL >= DailyProfitTarget;
                bool hitLoss   = sessionPnL <= -DailyLossLimit;
                bool hitWins   = MaxWins > 0 && winCount >= MaxWins;
                bool hitLosses = MaxLosses > 0 && lossCount >= MaxLosses;

                if (hitProfit || hitLoss || hitWins || hitLosses)
                {
                    tradingHalted = true;
                    if (FlattenOnTrigger && Position.MarketPosition != MarketPosition.Flat)
                    {
                        if (Position.MarketPosition == MarketPosition.Long) ExitLong("GuardFlat", "LE");
                        else ExitShort("GuardFlat", "SE");
                    }
                }
            }

            // ===== Time-based auto-flatten (optional) =====
            if (EnableTimeFilter && FlattenNearEnd && Position.MarketPosition != MarketPosition.Flat)
            {
                int tNow = ToTime(Time[0]);
                int nowSec = ToSecondsHHmmss(tNow);
                int endSec = ToSecondsHHmmss(EndTime1);
                if (nowSec >= Math.Max(0, endSec - Math.Max(0, FlattenBufferSeconds)))
                {
                    if (Position.MarketPosition == MarketPosition.Long) ExitLong("TimeExitL", "LE");
                    else ExitShort("TimeExitS", "SE");
                }
            }

            bool canEnter = !tradingHalted;

            // --- optional slope gate ON ENTRY (default off) ---
            bool slopeEntryOk = true;
            if (UseSlopeGateOnEntry && CurrentBar > Math.Max(CiSlopeBarsEntry, AdxSlopeBarsEntry))
            {
                double ciDrop  = ci[CiSlopeBarsEntry] - ci[0];     // positive if CI fell
                double adxRise = adx[0] - adx[AdxSlopeBarsEntry];  // positive if ADX rose
                slopeEntryOk = (ciDrop >= CiDecreaseMinEntry) && (adxRise >= AdxIncreaseMinEntry);
            }

            // --- thresholds (toggles) ---
            bool adxOk = !UseAdxEntryThreshold || (adx[0] >= AdxEntryThreshold);
            bool ciOk  = !UseCiEntryThreshold  || (ci[0]  <= CiEntryThreshold);

            // --- anchor gating / side bias ---
            double anchor = AnchorValue();
            bool anchorLongOK  = (SideAnchor == AnchorMode.None) || !RequireLongsAboveAnchor  || Close[0] > anchor;
            bool anchorShortOK = (SideAnchor == AnchorMode.None) || !RequireShortsBelowAnchor || Close[0] < anchor;

            // ---- Entries (Flat only) ----
            // Apply time window ONLY to new entries
            bool timeOk = !EnableTimeFilter || IsInAllowedWindow();

            if (timeOk && canEnter && Position.MarketPosition == MarketPosition.Flat && slopeEntryOk && adxOk && ciOk)
            {
                bool doLong  = UseDiCrossEntry ? crossUp   : (SideAnchor == AnchorMode.None ? true : Close[0] >= anchor);
                bool doShort = UseDiCrossEntry ? crossDown : (SideAnchor == AnchorMode.None ? true : Close[0] <= anchor);

                // Added AllowLongs and AllowShorts boolean checks here
                if (doLong && anchorLongOK && AllowLongs)
                    SubmitLongWithStops();
                else if (doShort && anchorShortOK && AllowShorts)
                    SubmitShortWithStops();
            }

            // --- Slope-based exits only ---
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                int bse = (Position.MarketPosition == MarketPosition.Long)
                    ? BarsSinceEntryExecution(0, "LE", 0)
                    : BarsSinceEntryExecution(0, "SE", 0);

                if (bse >= MinHoldBars)
                {
                    bool simpleFail = false;
                    bool hystFail = false;

                    if (SlopeExit == ExitSlopeMode.Simple && CurrentBar > Math.Max(CiSlopeBarsExit, AdxSlopeBarsExit))
                    {
                        double ciRise  = ci[0] - ci[CiSlopeBarsExit];
                        double adxDrop = adx[AdxSlopeBarsExit] - adx[0];
                        simpleFail = (ciRise >= CiRiseMinExit) || (adxDrop >= AdxDropMinExit);
                    }

                    if (SlopeExit == ExitSlopeMode.Hysteresis && CurrentBar > 1)
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
                            ExitLong("SlopeX", "LE");
                        else
                            ExitShort("SlopeX", "SE");

                        hystFailCount = 0;
                    }
                }
            }
        }

        // ===== Orders =====
        private const string LEntry = "LE";
        private const string SEntry = "SE";

        private void SubmitLongWithStops()
        {
            double risk = Math.Max(Math.Max(0.01, AtrStopMult) * atrStop[0], Math.Max(1, MinStopTicks) * TickSize);
            double stp  = RT(Close[0] - risk);
            double tgt  = RT(Close[0] + risk * Math.Max(0.1, RiskReward));
            SetStopLoss(LEntry, CalculationMode.Price, stp, false);
            SetProfitTarget(LEntry, CalculationMode.Price, tgt);
            EnterLong(Contracts, LEntry);
        }

        private void SubmitShortWithStops()
        {
            double risk = Math.Max(Math.Max(0.01, AtrStopMult) * atrStop[0], Math.Max(1, MinStopTicks) * TickSize);
            double stp  = RT(Close[0] + risk);
            double tgt  = RT(Close[0] - risk * Math.Max(0.1, RiskReward));
            SetStopLoss(SEntry, CalculationMode.Price, stp, false);
            SetProfitTarget(SEntry, CalculationMode.Price, tgt);
            EnterShort(Contracts, SEntry);
        }
    }
}