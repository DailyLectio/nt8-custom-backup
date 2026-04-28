#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.Gui.Tools;                 // [Range]
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// EMA crossover entries + custom session-anchored VWAP exits + trailing stop.
// New in v1.1: option to compute VWAP from the underlying (non-HA) series; daily kill switch.
// Run on a Heiken-Ashi chart for parity with TV tests.
// Analyzer: Order fill resolution = High / 1-tick.

namespace NinjaTrader.NinjaScript.Strategies
{
    public class EMA_VWAP_v1_1 : Strategy
    {
        // -------------------
        // Inputs (Parameters)
        // -------------------
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Fast EMA Length", GroupName = "Parameters", Order = 1)]
        public int FastEMALength { get; set; } = 6;

        [NinjaScriptProperty, Range(2, int.MaxValue)]
        [Display(Name = "Slow EMA Length", GroupName = "Parameters", Order = 2)]
        public int SlowEMALength { get; set; } = 18;

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "Trail (ticks)", GroupName = "Parameters", Order = 3)]
        public int TrailTicks { get; set; } = 16;

        [NinjaScriptProperty, Range(1, 10)]
        [Display(Name = "# Contracts", GroupName = "Parameters", Order = 4)]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Use VWAP Exit (custom session)", GroupName = "Parameters", Order = 5)]
        public bool UseVWAPExit { get; set; } = true;

        // ---- Higher-TF confirmation (single TF like v1.0) ----
        [NinjaScriptProperty]
        [Display(Name = "Use Confirm Filter", GroupName = "Confirmation", Order = 1)]
        public bool UseConfirmFilter { get; set; } = true;

        [NinjaScriptProperty, Range(1, 1440)]
        [Display(Name = "Confirm TF (min)", GroupName = "Confirmation", Order = 2)]
        public int ConfirmTFMinutes { get; set; } = 10;

        // ---- Underlying VWAP option ----
        [NinjaScriptProperty]
        [Display(Name = "Use Underlying (non-HA) for VWAP", GroupName = "VWAP Source", Order = 1)]
        public bool UseUnderlyingForVWAP { get; set; } = true;

        [NinjaScriptProperty, Range(0, 1440)]
        [Display(Name = "Underlying TF (min, 0 = follow primary)", GroupName = "VWAP Source", Order = 2)]
        public int UnderlyingTFMinutes { get; set; } = 0;

        // ---- Trading hours / risk ----
        [NinjaScriptProperty]
        [Display(Name = "Only RTH (0930-1600 ET)", GroupName = "Trading Hours", Order = 1)]
        public bool OnlyRTH { get; set; } = true;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Re-Entry Delay (bars)", GroupName = "Risk", Order = 1)]
        public int ReEntryDelayBars { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Use 1-Tick Series (intrabar)", GroupName = "Advanced", Order = 1)]
        public bool UseTickResolution { get; set; } = true;

        // ---- Daily kill switch ----
        [NinjaScriptProperty]
        [Display(Name = "Use Daily Loss Limit", GroupName = "Kill Switch", Order = 1)]
        public bool UseDailyLossLimit { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Daily Loss Limit ($)", GroupName = "Kill Switch", Order = 2)]
        public double DailyLossLimit { get; set; } = 600.0;

        [NinjaScriptProperty]
        [Display(Name = "Use Daily Profit Cap", GroupName = "Kill Switch", Order = 3)]
        public bool UseDailyProfitCap { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Daily Profit Cap ($)", GroupName = "Kill Switch", Order = 4)]
        public double DailyProfitCap { get; set; } = 1500.0;

        [NinjaScriptProperty]
        [Display(Name = "Flatten on Halt", GroupName = "Kill Switch", Order = 5)]
        public bool FlattenOnHalt { get; set; } = true;

        // -------------
        // Private state
        // -------------
        private EMA emaFast, emaSlow, emaFastConfirm, emaSlowConfirm;
        private int confirmBip = -1;

        // custom session VWAP (optionally from underlying series)
        private int underlyingBip = -1;
        private double cumPV = 0.0, cumV = 0.0;
        private Series<double> vwapSession;

        // re-entry & kill switch
        private int lastFlatBar = -10000;
        private MarketPosition prevPos = MarketPosition.Flat;
        private double sessionStartRealized = 0.0;
        private bool haltedToday = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "EMA_VWAP_v1_1";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.Configure)
            {
                if (UseTickResolution)
                    AddDataSeries(BarsPeriodType.Tick, 1);

                // Add underlying regular minute series for VWAP (non-HA)
                if (UseUnderlyingForVWAP)
                {
                    int mins = UnderlyingTFMinutes;
                    if (mins <= 0 && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute)
                        mins = BarsPeriod.Value;
                    if (mins <= 0) mins = 5; // safe fallback
                    AddDataSeries(BarsPeriodType.Minute, mins);
                    underlyingBip = BarsArray.Length - 1;
                }

                if (UseConfirmFilter && ConfirmTFMinutes > 0)
                {
                    AddDataSeries(BarsPeriodType.Minute, ConfirmTFMinutes);
                    confirmBip = BarsArray.Length - 1;
                }

                SetTrailStop(CalculationMode.Ticks, TrailTicks);
            }
            else if (State == State.DataLoaded)
            {
                emaFast = EMA(Close, FastEMALength);
                emaSlow = EMA(Close, SlowEMALength);

                if (UseConfirmFilter && confirmBip >= 0)
                {
                    emaFastConfirm = EMA(Closes[confirmBip], FastEMALength);
                    emaSlowConfirm = EMA(Closes[confirmBip], SlowEMALength);
                }

                vwapSession = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;
            if (CurrentBar < Math.Max(FastEMALength, SlowEMALength) + 2) return;

            // RTH filter
            if (OnlyRTH)
            {
                int t = ToTime(Time[0]);
                if (t < 93000 || t > 160000) return;
            }

            // Start-of-session bookkeeping
            if (Bars.IsFirstBarOfSession)
            {
                cumPV = 0; cumV = 0;
                sessionStartRealized = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                haltedToday = false;
            }

            // Kill switch check
            double realizedToday = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartRealized;
            if ((UseDailyLossLimit && realizedToday <= -Math.Abs(DailyLossLimit)) ||
                (UseDailyProfitCap && realizedToday >= Math.Abs(DailyProfitCap)))
            {
                haltedToday = true;
            }

            if (haltedToday)
            {
                if (FlattenOnHalt && Position.MarketPosition != MarketPosition.Flat)
                {
                    if (Position.MarketPosition == MarketPosition.Long) ExitLong("HaltExit", "LE");
                    if (Position.MarketPosition == MarketPosition.Short) ExitShort("HaltExit", "SE");
                }
                // still maintain VWAP below so that exits can process if UseVWAPExit changes intrabar
            }

            // Track flat transitions for re-entry spacing
            if (prevPos != Position.MarketPosition && Position.MarketPosition == MarketPosition.Flat)
                lastFlatBar = CurrentBar;
            prevPos = Position.MarketPosition;

            // ----- Session VWAP update (from underlying if available) -----
            bool useUnder = UseUnderlyingForVWAP && underlyingBip >= 0 && CurrentBars[underlyingBip] > 0;
            double typ = useUnder
                ? (Highs[underlyingBip][0] + Lows[underlyingBip][0] + Closes[underlyingBip][0]) / 3.0
                : (High[0] + Low[0] + Close[0]) / 3.0;
            double vol = useUnder ? Volumes[underlyingBip][0] : Volume[0];

            cumPV += typ * vol;
            cumV  += vol;
            vwapSession[0] = cumV > 0 ? cumPV / cumV : (useUnder ? Closes[underlyingBip][0] : Close[0]);

            // ----- Signals & confirm -----
            bool crossUp   = CrossAbove(emaFast, emaSlow, 1);
            bool crossDown = CrossBelow(emaFast, emaSlow, 1);

            bool confirmOKLong = true, confirmOKShort = true;
            if (UseConfirmFilter && confirmBip >= 0 && CurrentBars[confirmBip] > SlowEMALength)
            {
                confirmOKLong  = emaFastConfirm[0] > emaSlowConfirm[0];
                confirmOKShort = emaFastConfirm[0] < emaSlowConfirm[0];
            }

            bool canReEnter = (CurrentBar - lastFlatBar) >= ReEntryDelayBars;
            if (!haltedToday && Position.MarketPosition == MarketPosition.Flat && canReEnter)
            {
                if (crossUp && confirmOKLong)   EnterLong(Contracts, "LE");
                if (crossDown && confirmOKShort) EnterShort(Contracts, "SE");
            }

            // ----- Exits -----
            if (UseVWAPExit)
            {
                double exitVwap = vwapSession[0];
                if (Position.MarketPosition == MarketPosition.Long && Close[0] < exitVwap)
                    ExitLong("VWAPLongExit", "LE");

                if (Position.MarketPosition == MarketPosition.Short && Close[0] > exitVwap)
                    ExitShort("VWAPShortExit", "SE");
            }
        }
    }
}
