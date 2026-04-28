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
using NinjaTrader.NinjaScript.Indicators;   // OrderFlowVWAP, VWAPResolution, VWAPStandardDeviations
#endregion

// EMA crossover entries with delayed-entry filter + trailing stop + selectable VWAP exit.
// - Exit VWAP source: Custom session VWAP OR Order Flow VWAP
// - Optional VWAP computed from underlying (non-HA) minute series while chart stays HA
// - Two higher-TF confirmations (10m/15m) with AND/OR
// - Daily kill switch (loss/profit)
// - New: SignalConfirmBars = wait N bars AFTER the cross; entry only if conditions still valid

namespace NinjaTrader.NinjaScript.Strategies
{
    public class EMA_VWAP_Delayed_v1_2 : Strategy
    {
        // -------- Exit mode --------
        public enum ExitVWAPMode
        {
            CustomSessionVWAP = 0,
            OrderFlowVWAP     = 1
        }

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
        [Display(Name = "Use VWAP Exit", GroupName = "Parameters", Order = 5)]
        public bool UseVWAPExit { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "VWAP Exit Mode", GroupName = "Parameters", Order = 6)]
        public ExitVWAPMode VwapExitMode { get; set; } = ExitVWAPMode.OrderFlowVWAP;

        // ---- Delayed entry ----
        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Signal Confirm Bars (wait N bars after cross)", GroupName = "Entry Delay", Order = 1)]
        public int SignalConfirmBars { get; set; } = 1; // 0 = immediate entry (v1.1 behavior)

        [NinjaScriptProperty]
        [Display(Name = "Cancel if opposite cross during wait", GroupName = "Entry Delay", Order = 2)]
        public bool CancelOnOppositeCross { get; set; } = true;

        // ---- Order Flow VWAP settings ----
        [NinjaScriptProperty]
        [Display(Name = "OF: Use Tick Resolution", GroupName = "Order Flow VWAP", Order = 1)]
        public bool OFUseTickResolution { get; set; } = false;  // false = Standard

        [NinjaScriptProperty, Range(1, 3)]
        [Display(Name = "OF: # StdDevs (1-3)", GroupName = "Order Flow VWAP", Order = 2)]
        public int OFStdDevs { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "OF: SD1 Multiplier", GroupName = "Order Flow VWAP", Order = 3)]
        public double OFSD1 { get; set; } = 1.0;

        [NinjaScriptProperty]
        [Display(Name = "OF: SD2 Multiplier", GroupName = "Order Flow VWAP", Order = 4)]
        public double OFSD2 { get; set; } = 2.0;

        [NinjaScriptProperty]
        [Display(Name = "OF: SD3 Multiplier", GroupName = "Order Flow VWAP", Order = 5)]
        public double OFSD3 { get; set; } = 3.0;

        [NinjaScriptProperty]
        [Display(Name = "OF: Trading Hours Name", GroupName = "Order Flow VWAP", Order = 6)]
        public string OFTradingHoursName { get; set; } = "CME US Index Futures RTH";

        // ---- Underlying (non-HA) input for VWAP ----
        [NinjaScriptProperty]
        [Display(Name = "Use Underlying (non-HA) for VWAP", GroupName = "VWAP Source", Order = 1)]
        public bool UseUnderlyingForVWAP { get; set; } = true;

        [NinjaScriptProperty, Range(0, 1440)]
        [Display(Name = "Underlying TF (min, 0 = follow primary)", GroupName = "VWAP Source", Order = 2)]
        public int UnderlyingTFMinutes { get; set; } = 0;

        // ---- Confirmations (two TFs) ----
        [NinjaScriptProperty]
        [Display(Name = "Use Confirm 1", GroupName = "Confirmation", Order = 1)]
        public bool UseConfirm1 { get; set; } = true;

        [NinjaScriptProperty, Range(1, 1440)]
        [Display(Name = "Confirm 1 TF (min)", GroupName = "Confirmation", Order = 2)]
        public int ConfirmTFMinutes1 { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name = "Use Confirm 2", GroupName = "Confirmation", Order = 3)]
        public bool UseConfirm2 { get; set; } = true;

        [NinjaScriptProperty, Range(1, 1440)]
        [Display(Name = "Confirm 2 TF (min)", GroupName = "Confirmation", Order = 4)]
        public int ConfirmTFMinutes2 { get; set; } = 15;

        [NinjaScriptProperty]
        [Display(Name = "Require BOTH confirmations (AND). If false = OR", GroupName = "Confirmation", Order = 5)]
        public bool RequireBothConfirmations { get; set; } = false;

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
        private EMA emaFast, emaSlow;
        private int underlyingBip = -1;

        // confirm series
        private EMA emaFastC1, emaSlowC1, emaFastC2, emaSlowC2;
        private int confirmBip1 = -1, confirmBip2 = -1;

        // OF VWAP
        private OrderFlowVWAP ofVWAP;
        private ISeries<double> ofVwapSeries;

        // Custom session VWAP
        private double cumPV = 0.0, cumV = 0.0;
        private Series<double> vwapSession;

        // re-entry & kill switch
        private int lastFlatBar = -10000;
        private MarketPosition prevPos = MarketPosition.Flat;
        private double sessionStartRealized = 0.0;
        private bool haltedToday = false;

        // delayed entry state
        private int pendingStartBar = int.MinValue;
        private int pendingDir = 0; // +1 = long, -1 = short, 0 = none

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "EMA_VWAP_Delayed_v1_2";
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

                // underlying (non-HA) minute series for VWAP
                if (UseUnderlyingForVWAP)
                {
                    int mins = UnderlyingTFMinutes;
                    if (mins <= 0 && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute)
                        mins = BarsPeriod.Value;
                    if (mins <= 0) mins = 5;
                    AddDataSeries(BarsPeriodType.Minute, mins);
                    underlyingBip = BarsArray.Length - 1;
                }

                if (UseConfirm1 && ConfirmTFMinutes1 > 0)
                {
                    AddDataSeries(BarsPeriodType.Minute, ConfirmTFMinutes1);
                    confirmBip1 = BarsArray.Length - 1;
                }
                if (UseConfirm2 && ConfirmTFMinutes2 > 0)
                {
                    AddDataSeries(BarsPeriodType.Minute, ConfirmTFMinutes2);
                    confirmBip2 = BarsArray.Length - 1;
                }

                SetTrailStop(CalculationMode.Ticks, TrailTicks);
            }
            else if (State == State.DataLoaded)
            {
                emaFast = EMA(Close, FastEMALength);
                emaSlow = EMA(Close, SlowEMALength);

                if (confirmBip1 >= 0)
                {
                    emaFastC1 = EMA(Closes[confirmBip1], FastEMALength);
                    emaSlowC1 = EMA(Closes[confirmBip1], SlowEMALength);
                }
                if (confirmBip2 >= 0)
                {
                    emaFastC2 = EMA(Closes[confirmBip2], FastEMALength);
                    emaSlowC2 = EMA(Closes[confirmBip2], SlowEMALength);
                }

                // OF VWAP (we will reference only if chosen)
                if (VwapExitMode == ExitVWAPMode.OrderFlowVWAP)
                {
                    VWAPResolution res = OFUseTickResolution ? VWAPResolution.Tick : VWAPResolution.Standard;
                    VWAPStandardDeviations sdEnum =
                        (OFStdDevs <= 1) ? VWAPStandardDeviations.One
                      : (OFStdDevs == 2) ? VWAPStandardDeviations.Two
                                         : VWAPStandardDeviations.Three;
                    TradingHours th = TradingHours.Get(OFTradingHoursName);
                    if (th == null) th = Bars.TradingHours;
                    ISeries<double> inputSeries = (UseUnderlyingForVWAP && underlyingBip >= 0) ? Closes[underlyingBip] : Close;
                    ofVWAP = OrderFlowVWAP(inputSeries, res, th, sdEnum, OFSD1, OFSD2, OFSD3);
                    ofVwapSeries = ofVWAP.VWAP;
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

            // Start-of-session resets
            if (Bars.IsFirstBarOfSession)
            {
                cumPV = 0; cumV = 0;              // for custom VWAP
                sessionStartRealized = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                haltedToday = false;
                pendingDir = 0;
                pendingStartBar = int.MinValue;
            }

            // Kill switch
            double realizedToday = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartRealized;
            if ((UseDailyLossLimit && realizedToday <= -Math.Abs(DailyLossLimit)) ||
                (UseDailyProfitCap && realizedToday >= Math.Abs(DailyProfitCap)))
            {
                haltedToday = true;
            }
            if (haltedToday && FlattenOnHalt && Position.MarketPosition != MarketPosition.Flat)
            {
                if (Position.MarketPosition == MarketPosition.Long) ExitLong("HaltExit", "LE");
                if (Position.MarketPosition == MarketPosition.Short) ExitShort("HaltExit", "SE");
            }

            // Track flat transitions for re-entry spacing
            if (prevPos != Position.MarketPosition && Position.MarketPosition == MarketPosition.Flat)
                lastFlatBar = CurrentBar;
            prevPos = Position.MarketPosition;

            // --- Update custom session VWAP (from underlying if available) ---
            if (VwapExitMode == ExitVWAPMode.CustomSessionVWAP)
            {
                bool useUnder = UseUnderlyingForVWAP && underlyingBip >= 0 && CurrentBars[underlyingBip] > 0;
                double typ = useUnder
                    ? (Highs[underlyingBip][0] + Lows[underlyingBip][0] + Closes[underlyingBip][0]) / 3.0
                    : (High[0] + Low[0] + Close[0]) / 3.0;
                double vol = useUnder ? Volumes[underlyingBip][0] : Volume[0];
                cumPV += typ * vol;
                cumV  += vol;
                vwapSession[0] = cumV > 0 ? cumPV / cumV : (useUnder ? Closes[underlyingBip][0] : Close[0]);
            }

            // --- Signals on the primary (HA) series ---
            bool crossUp   = CrossAbove(emaFast, emaSlow, 1);
            bool crossDown = CrossBelow(emaFast, emaSlow, 1);

            // Confirmations
            bool c1Long = true, c1Short = true, c2Long = true, c2Short = true;
            if (confirmBip1 >= 0 && CurrentBars[confirmBip1] > SlowEMALength)
            {
                c1Long  = emaFastC1[0] > emaSlowC1[0];
                c1Short = emaFastC1[0] < emaSlowC1[0];
            }
            if (confirmBip2 >= 0 && CurrentBars[confirmBip2] > SlowEMALength)
            {
                c2Long  = emaFastC2[0] > emaSlowC2[0];
                c2Short = emaFastC2[0] < emaSlowC2[0];
            }
            bool okLong, okShort;
            if (RequireBothConfirmations)
            {
                okLong  = (!UseConfirm1 || c1Long)  && (!UseConfirm2 || c2Long);
                okShort = (!UseConfirm1 || c1Short) && (!UseConfirm2 || c2Short);
            }
            else
            {
                okLong  = (!UseConfirm1 && !UseConfirm2) || (UseConfirm1 && c1Long)  || (UseConfirm2 && c2Long);
                okShort = (!UseConfirm1 && !UseConfirm2) || (UseConfirm1 && c1Short) || (UseConfirm2 && c2Short);
            }

            // --- Delayed entry state machine ---
            // Arm a pending signal on cross
            if (SignalConfirmBars > 0)
            {
                if (crossUp && okLong)
                {
                    pendingDir = +1;
                    pendingStartBar = CurrentBar;
                }
                else if (crossDown && okShort)
                {
                    pendingDir = -1;
                    pendingStartBar = CurrentBar;
                }

                // Optional cancellation if opposite cross appears during wait
                if (CancelOnOppositeCross && pendingDir != 0)
                {
                    if ((pendingDir == +1 && crossDown) || (pendingDir == -1 && crossUp))
                    {
                        pendingDir = 0;
                        pendingStartBar = int.MinValue;
                    }
                }
            }

            // Are we allowed to re-enter yet?
            bool canReEnter = (CurrentBar - lastFlatBar) >= ReEntryDelayBars;
            bool allowNewEntry = !haltedToday && Position.MarketPosition == MarketPosition.Flat && canReEnter;

            // Execute entry
            if (allowNewEntry)
            {
                if (SignalConfirmBars <= 0)
                {
                    // Immediate mode (legacy)
                    if (crossUp && okLong)   EnterLong(Contracts, "LE");
                    if (crossDown && okShort) EnterShort(Contracts, "SE");
                }
                else if (pendingDir != 0 && (CurrentBar - pendingStartBar) >= SignalConfirmBars)
                {
                    // Confirmed-after-wait: re-check baseline conditions still valid
                    if (pendingDir == +1 && emaFast[0] > emaSlow[0] && okLong)
                        EnterLong(Contracts, "LE");
                    else if (pendingDir == -1 && emaFast[0] < emaSlow[0] && okShort)
                        EnterShort(Contracts, "SE");

                    // consume the pending signal regardless
                    pendingDir = 0;
                    pendingStartBar = int.MinValue;
                }
            }

            // --- Exits via VWAP + trailing stop ---
            if (UseVWAPExit)
            {
                double exitVwap =
                    (VwapExitMode == ExitVWAPMode.OrderFlowVWAP && ofVwapSeries != null)
                        ? ofVwapSeries[0]
                        : vwapSession[0];

                if (Position.MarketPosition == MarketPosition.Long && Close[0] < exitVwap)
                    ExitLong("VWAPLongExit", "LE");

                if (Position.MarketPosition == MarketPosition.Short && Close[0] > exitVwap)
                    ExitShort("VWAPShortExit", "SE");
            }
        }
    }
}
