// CC BY-NC 4.0
// KalmanPulse_Fader_V1B.cs — Adaptive Kalman Fade Strategy (V1B)
// ─────────────────────────────────────────────────────────────────────────
// V1B ADDITIONS over V1A:
//   1. A/B Mode switch (RequireFootprintConfirmation)
//      false = V1A: tick micro-reversal at inner band only
//      true  = V1B: also requires fresh ABS, DD, or TF within window
//   2. Daily Bias Filter — same D/P/b/B routing as VolState
//   3. Kill Switch — DEIA, EEMDF, or DT immediately flattens Leg2 runner
// ─────────────────────────────────────────────────────────────────────────

#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class KalmanPulse_Fader_V1B : Strategy
    {
        // =====================================================================
        // PARAMETERS — KALMAN ENGINE (unchanged)
        // =====================================================================
        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name = "Kernel Length", GroupName = "1. Kalman Engine", Order = 0)]
        public int KernelLength { get; set; } = 33;

        [NinjaScriptProperty, Range(0.1, 10.0)]
        [Display(Name = "Kernel Alpha", GroupName = "1. Kalman Engine", Order = 1)]
        public double KernelAlpha { get; set; } = 1.0;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name = "ATR Period", GroupName = "1. Kalman Engine", Order = 2)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.5, 5.0)]
        [Display(Name = "ATR Factor (outer envelope)", GroupName = "1. Kalman Engine", Order = 3)]
        public double AtrFactor { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0.5, 4.0)]
        [Display(Name = "Inner Band Multiplier", GroupName = "1. Kalman Engine", Order = 4)]
        public double InnerMult { get; set; } = 1.5;

        // =====================================================================
        // PARAMETERS — RISK (unchanged)
        // =====================================================================
        [NinjaScriptProperty, Range(0.5, 5.0)]
        [Display(Name = "ATR Stop Multiplier", GroupName = "2. Risk", Order = 0)]
        public double AtrStopMult { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Size Pct", GroupName = "2. Risk", Order = 1)]
        public int SizePct { get; set; } = 100;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5  ES=12.50  MNQ=0.50  MES=1.25", GroupName = "2. Risk", Order = 2)]
        public double TickValueDollars { get; set; } = 5.00;

        [NinjaScriptProperty, Range(2, 100)]
        [Display(Name = "Max Total Contracts", GroupName = "2. Risk", Order = 3)]
        public int MaxTotalContracts { get; set; } = 2;

        // =====================================================================
        // PARAMETERS — GUARDS / TIME (unchanged)
        // =====================================================================
        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "3. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0=off)", GroupName = "3. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0=off)", GroupName = "3. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "4. Time", Order = 0)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Start Time (HHmmss)", GroupName = "4. Time", Order = 1)]
        public int StartTime { get; set; } = 103500;

        [NinjaScriptProperty]
        [Display(Name = "End Time (HHmmss)", GroupName = "4. Time", Order = 2)]
        public int EndTime { get; set; } = 155500;

        // =====================================================================
        // PARAMETERS — V1B ORDERFLOW LAYER (NEW)
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "A/B: Require Footprint Confirmation",
                 GroupName = "5. OrderFlow (V1B)", Order = 0,
                 Description = "false=V1A (tick reversal only). true=V1B (also needs ABS/DD/TF).")]
        public bool RequireFootprintConfirmation { get; set; } = false;

        [NinjaScriptProperty, Range(1, 15)]
        [Display(Name = "Signal Valid Window (minutes)", GroupName = "5. OrderFlow (V1B)", Order = 1)]
        public int FootprintValidMinutes { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Entry: Accept ABS", GroupName = "5. OrderFlow (V1B)", Order = 2)]
        public bool UseAbsEntry { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Entry: Accept DD", GroupName = "5. OrderFlow (V1B)", Order = 3)]
        public bool UseDdEntry { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Entry: Accept TF", GroupName = "5. OrderFlow (V1B)", Order = 4)]
        public bool UseTfEntry { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable Kill Switch (DEIA / EEMDF / DT)",
                 GroupName = "5. OrderFlow (V1B)", Order = 5)]
        public bool EnableKillSwitch { get; set; } = true;

        [NinjaScriptProperty, Range(0, 5)]
        [Display(Name = "Kill Switch Signal Age (minutes)", GroupName = "5. OrderFlow (V1B)", Order = 6)]
        public int KillSwitchMaxMinutes { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Enable Daily Bias Filter (TradeHUD)",
                 GroupName = "5. OrderFlow (V1B)", Order = 7)]
        public bool EnableDailyBiasFilter { get; set; } = true;

        // =====================================================================
        // KALMAN STATE
        // =====================================================================
        private double kalmanState     = double.NaN;
        private double kalmanVar       = 1.0;
        private double prevKalmanState = double.NaN;

        private double baseline      = 0;
        private double upperEnvelope = 0;
        private double lowerEnvelope = 0;
        private double innerUpper    = 0;
        private double innerLower    = 0;
        private double baselineSlope = 0;

        // =====================================================================
        // ATR (manual Wilder, tick-safe)
        // =====================================================================
        private double runningAtr   = 0;
        private int    atrInitCount = 0;

        // =====================================================================
        // RUNTIME STATE
        // =====================================================================
        private int    consecutiveLosers  = 0;
        private int    lastTradeCount     = 0;
        private double sessionStartProfit = 0;

        private bool   leg1Hit        = false;
        private bool   leg1JustHit    = false;
        private int    currentLeg2Qty = 1;
        private string activeLeg2     = "";

        private int    lastEntryBar   = -1;
        private double prevTickPrice  = 0;
        private double lastLeg1Target = 0;
        private double lastLeg2Target = 0;

        // =====================================================================
        // ORDER LABELS
        // =====================================================================
        private const string KPL1 = "KPF_Long1";
        private const string KPL2 = "KPF_Long2";
        private const string KPS1 = "KPF_Short1";
        private const string KPS2 = "KPF_Short2";

        // =====================================================================
        // HELPERS
        // =====================================================================
        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        private bool IsInTime()
        {
            if (!EnableTimeFilter) return true;
            int t = ToTime(Time[0]);
            return t >= StartTime && t <= EndTime;
        }

        private int CalcMaxContracts(double atrVal)
        {
            if (atrVal <= 0) return 1;
            double dollarRisk = (atrVal * AtrStopMult) / TickSize * TickValueDollars;
            if (dollarRisk <= 0) return 1;
            int riskBasedQty = Math.Max(1, (int)(1500.0 / dollarRisk));
            return Math.Min(MaxTotalContracts, riskBasedQty);
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
            => Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));

        private double ComputeAtr()
        {
            if (CurrentBar < 1) return TickSize * 10;
            double tr = Math.Max(High[0] - Low[0],
                        Math.Max(Math.Abs(High[0] - Close[1]),
                                 Math.Abs(Low[0] - Close[1])));
            atrInitCount++;
            if (atrInitCount <= AtrPeriod)
                runningAtr = runningAtr + (tr - runningAtr) / atrInitCount;
            else
                runningAtr = (runningAtr * (AtrPeriod - 1) + tr) / AtrPeriod;
            return runningAtr > 0 ? runningAtr : TickSize * 10;
        }

        private double ComputeKernelSmoothed()
        {
            int len = Math.Min(KernelLength, CurrentBar + 1);
            double center = len / 2.0;
            double ws = 0, ps = 0;
            for (int i = 0; i < len; i++)
            {
                double dist = Math.Abs(i - center);
                double w = Math.Exp(-KernelAlpha * (len - 1 - i) / len)
                         * Math.Exp(-Math.Pow(dist / (len / 3.0), 2));
                ps += Close[len - 1 - i] * w;
                ws += w;
            }
            return ws > 0 ? ps / ws : Close[0];
        }

        private void UpdateKalman(double measurement, double atrVol)
        {
            if (double.IsNaN(kalmanState)) { kalmanState = measurement; kalmanVar = 1.0; return; }
            double predictedVar = kalmanVar + atrVol * 0.05;
            double innovationVar = predictedVar + atrVol * 0.10;
            double gain = predictedVar / innovationVar;
            prevKalmanState = kalmanState;
            kalmanState    += gain * (measurement - kalmanState);
            kalmanVar       = (1.0 - gain) * predictedVar;
        }

        private void RefreshEnvelopes(double atrVal)
        {
            baseline      = kalmanState;
            baselineSlope = double.IsNaN(prevKalmanState) ? 0 : kalmanState - prevKalmanState;
            upperEnvelope = baseline + atrVal * AtrFactor;
            lowerEnvelope = baseline - atrVal * AtrFactor;
            innerUpper    = baseline + atrVal * InnerMult;
            innerLower    = baseline - atrVal * InnerMult;
        }

        // ── V1B helpers ───────────────────────────────────────────────────────
        private bool IsFootprintEntryConfirmed()
        {
            if (!RequireFootprintConfirmation) return true;
            DateTime now = Time[0];
            if (UseAbsEntry && HUDMessengerV1B.IsSignalFresh("Scanner_ABS", now, FootprintValidMinutes)) return true;
            if (UseDdEntry  && HUDMessengerV1B.IsSignalFresh("Scanner_DD",  now, FootprintValidMinutes)) return true;
            if (UseTfEntry  && HUDMessengerV1B.IsSignalFresh("Scanner_TF",  now, FootprintValidMinutes)) return true;
            return false;
        }

        private bool IsKillSwitchActive()
        {
            if (!EnableKillSwitch) return false;
            DateTime now = Time[0];
            double w = Math.Max(0.5, KillSwitchMaxMinutes);
            return HUDMessengerV1B.IsSignalFresh("Scanner_DEIA",  now, w)
                || HUDMessengerV1B.IsSignalFresh("Scanner_EEMDF", now, w)
                || HUDMessengerV1B.IsSignalFresh("Scanner_DT",    now, w);
        }

        private bool IsBiasCompatible(bool isLong)
        {
            if (!EnableDailyBiasFilter) return true;
            string bias = HUDMessengerV1B.CurrentDailyBias;
            if (string.IsNullOrEmpty(bias)) return true;
            switch (bias)
            {
                case "D": return true;
                case "P": return isLong;
                case "b": return !isLong;
                case "B": return false;
                default:  return true;
            }
        }

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "KalmanPulse_Fader_V1B";
                Calculate                    = Calculate.OnEachTick;
                EntriesPerDirection          = 2;
                EntryHandling                = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                RealtimeErrorHandling        = RealtimeErrorHandling.StopCancelClose;
                TraceOrders                  = false;
            }
        }

        protected override void OnExecutionUpdate(
            Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            int tc = SystemPerformance.AllTrades.Count;
            if (tc > lastTradeCount)
            {
                var last = SystemPerformance.AllTrades[tc - 1];
                if (last.ProfitCurrency < 0) consecutiveLosers++;
                else                         consecutiveLosers = 0;
                lastTradeCount = tc;
            }
        }

        // =====================================================================
        // MAIN TICK UPDATE
        // =====================================================================
        protected override void OnBarUpdate()
        {
            if (CurrentBar < KernelLength + 4) return;

            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers  = 0;
                leg1Hit = false; leg1JustHit = false;
                currentLeg2Qty = 1; activeLeg2 = "";
                lastEntryBar = -1; prevTickPrice = 0;
                lastLeg1Target = 0; lastLeg2Target = 0;
                kalmanState = double.NaN; prevKalmanState = double.NaN;
                kalmanVar = 1.0; runningAtr = 0; atrInitCount = 0;
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }

            double atrVal    = ComputeAtr();
            double kernelVal = ComputeKernelSmoothed();
            UpdateKalman(kernelVal, atrVal);
            RefreshEnvelopes(atrVal);

            double curPrice = Close[0];

            // ── Envelope breach exit ──────────────────────────────────────────
            if (Position.MarketPosition == MarketPosition.Long && curPrice < lowerEnvelope)
            {
                ExitLong("EnvelopeBreakExit", "");
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                prevTickPrice = curPrice; return;
            }
            if (Position.MarketPosition == MarketPosition.Short && curPrice > upperEnvelope)
            {
                ExitShort("EnvelopeBreakExit", "");
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                prevTickPrice = curPrice; return;
            }

            // ── V1B Kill Switch ───────────────────────────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat && leg1Hit && IsKillSwitchActive())
            {
                if (Position.MarketPosition == MarketPosition.Long) ExitLong("KillSwitch", KPL2);
                else ExitShort("KillSwitch", KPS2);
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                Print(string.Format("[KPF_V1B] KILL SWITCH at {0:F2}", curPrice));
                prevTickPrice = curPrice; return;
            }

            // ── Runner management + dynamic target update ─────────────────────
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (!leg1Hit && Position.Quantity <= currentLeg2Qty && currentLeg2Qty > 0)
                {
                    leg1Hit = true; leg1JustHit = true;
                    double pivot = Position.MarketPosition == MarketPosition.Long
                        ? RT(Position.AveragePrice + 4 * TickSize)
                        : RT(Position.AveragePrice - 4 * TickSize);
                    if (activeLeg2 == KPL2) SetStopLoss(KPL2, CalculationMode.Price, pivot, false);
                    else if (activeLeg2 == KPS2) SetStopLoss(KPS2, CalculationMode.Price, pivot, false);
                }
                else if (leg1JustHit) leg1JustHit = false;

                if (!leg1JustHit && activeLeg2.Length > 0)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        double newL1 = RT(baseline); double newL2 = RT(innerUpper);
                        if (!leg1Hit && Math.Abs(newL1 - lastLeg1Target) >= TickSize)
                        { SetProfitTarget(KPL1, CalculationMode.Price, newL1); lastLeg1Target = newL1; }
                        if (Math.Abs(newL2 - lastLeg2Target) >= TickSize)
                        { SetProfitTarget(KPL2, CalculationMode.Price, newL2); lastLeg2Target = newL2; }
                    }
                    else
                    {
                        double newL1 = RT(baseline); double newL2 = RT(innerLower);
                        if (!leg1Hit && Math.Abs(newL1 - lastLeg1Target) >= TickSize)
                        { SetProfitTarget(KPS1, CalculationMode.Price, newL1); lastLeg1Target = newL1; }
                        if (Math.Abs(newL2 - lastLeg2Target) >= TickSize)
                        { SetProfitTarget(KPS2, CalculationMode.Price, newL2); lastLeg2Target = newL2; }
                    }
                }

                prevTickPrice = curPrice; return;
            }

            // ── Entry gates ───────────────────────────────────────────────────
            leg1Hit = false; leg1JustHit = false; currentLeg2Qty = 1; activeLeg2 = "";

            if (CurrentBar == lastEntryBar) { prevTickPrice = curPrice; return; }
            if (consecutiveLosers >= MaxConsecutiveLosses) { prevTickPrice = curPrice; return; }
            if (!IsInTime()) { prevTickPrice = curPrice; return; }

            if (DailyGoal > 0 || DailyLossLimit > 0)
            {
                double pnl = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit
                             - sessionStartProfit;
                if (DailyGoal > 0 && pnl >= DailyGoal) { prevTickPrice = curPrice; return; }
                if (DailyLossLimit > 0 && pnl <= -DailyLossLimit) { prevTickPrice = curPrice; return; }
            }

            bool tickUp   = prevTickPrice > 0 && curPrice > prevTickPrice;
            bool tickDown = prevTickPrice > 0 && curPrice < prevTickPrice;
            double slopeThreshold = atrVal * 0.1;

            // ── LONG FADE ─────────────────────────────────────────────────────
            bool fadeLong = curPrice <= innerLower && curPrice > lowerEnvelope
                         && baselineSlope >= -slopeThreshold;

            if (fadeLong && tickUp && IsBiasCompatible(true) && IsFootprintEntryConfirmed())
            {
                double stopPrice  = RT(curPrice - atrVal * AtrStopMult);
                double leg1Target = RT(baseline);
                double leg2Target = RT(innerUpper);

                if ((leg1Target - curPrice) / TickSize < 4) { prevTickPrice = curPrice; return; }

                int maxC = CalcMaxContracts(atrVal);
                int sz   = ScaleByConfidence(maxC, SizePct);
                int l1q  = Math.Max(1, sz / 2);
                int l2q  = Math.Max(1, sz - l1q);

                SetStopLoss(KPL1, CalculationMode.Price, stopPrice, false);
                SetStopLoss(KPL2, CalculationMode.Price, stopPrice, false);
                SetProfitTarget(KPL1, CalculationMode.Price, leg1Target);
                SetProfitTarget(KPL2, CalculationMode.Price, leg2Target);
                EnterLong(l1q, KPL1); EnterLong(l2q, KPL2);

                currentLeg2Qty = l2q; activeLeg2 = KPL2;
                lastLeg1Target = leg1Target; lastLeg2Target = leg2Target;
                lastEntryBar   = CurrentBar;

                Print(string.Format("[KPF_V1B] LONG | Baseline:{0:F2} | Bias:{1} | FP:{2} | " +
                    "Qty:{3}+{4} | Stop:{5:F2} | T1:{6:F2} | T2:{7:F2}",
                    baseline, HUDMessengerV1B.CurrentDailyBias,
                    RequireFootprintConfirmation ? "ON" : "OFF",
                    l1q, l2q, stopPrice, leg1Target, leg2Target));
            }
            // ── SHORT FADE ────────────────────────────────────────────────────
            else
            {
                bool fadeShort = curPrice >= innerUpper && curPrice < upperEnvelope
                              && baselineSlope <= slopeThreshold;

                if (fadeShort && tickDown && IsBiasCompatible(false) && IsFootprintEntryConfirmed())
                {
                    double stopPrice  = RT(curPrice + atrVal * AtrStopMult);
                    double leg1Target = RT(baseline);
                    double leg2Target = RT(innerLower);

                    if ((curPrice - leg1Target) / TickSize < 4) { prevTickPrice = curPrice; return; }

                    int maxC = CalcMaxContracts(atrVal);
                    int sz   = ScaleByConfidence(maxC, SizePct);
                    int l1q  = Math.Max(1, sz / 2);
                    int l2q  = Math.Max(1, sz - l1q);

                    SetStopLoss(KPS1, CalculationMode.Price, stopPrice, false);
                    SetStopLoss(KPS2, CalculationMode.Price, stopPrice, false);
                    SetProfitTarget(KPS1, CalculationMode.Price, leg1Target);
                    SetProfitTarget(KPS2, CalculationMode.Price, leg2Target);
                    EnterShort(l1q, KPS1); EnterShort(l2q, KPS2);

                    currentLeg2Qty = l2q; activeLeg2 = KPS2;
                    lastLeg1Target = leg1Target; lastLeg2Target = leg2Target;
                    lastEntryBar   = CurrentBar;

                    Print(string.Format("[KPF_V1B] SHORT | Baseline:{0:F2} | Bias:{1} | FP:{2} | " +
                        "Qty:{3}+{4} | Stop:{5:F2} | T1:{6:F2} | T2:{7:F2}",
                        baseline, HUDMessengerV1B.CurrentDailyBias,
                        RequireFootprintConfirmation ? "ON" : "OFF",
                        l1q, l2q, stopPrice, leg1Target, leg2Target));
                }
            }

            prevTickPrice = curPrice;
        }
    }
}
