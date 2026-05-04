// CC BY-NC 4.0
// KalmanPulse_Fader_V2.cs — Adaptive Kalman Fade Strategy (V2)
// ─────────────────────────────────────────────────────────────────────────
// V2 ADDITIONS:
//   1. Dual-Layer Exit Cooldowns (Bars + Seconds) to prevent machine-gun firing.
//   2. Hard cap on MaxTotalContracts to prevent sizing explosion during tight ATR.
//   3. Retains V1B OrderFlow/Kill Switch logic as optional parameters.
// ─────────────────────────────────────────────────────────────────────────

#region Using declarations
using System;
using System.Globalization;
using System.IO;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class KalmanPulse_Fader_V2 : Strategy
    {
        // =====================================================================
        // PARAMETERS — KALMAN ENGINE
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
        // PARAMETERS — RISK
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

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Max Total Contracts", GroupName = "2. Risk", Order = 3, 
                 Description = "Hard cap on position size to prevent ATR explosion.")]
        public int MaxTotalContracts { get; set; } = 2;

        // =====================================================================
        // PARAMETERS — GUARDS & COOLDOWNS (V2 UPGRADES)
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

        [NinjaScriptProperty, Range(0, 50)]
        [Display(Name = "Exit Cooldown (Bars)", GroupName = "3. Guards", Order = 3, 
                 Description = "Number of bars to wait after any exit before re-entering.")]
        public int ExitCooldownBars { get; set; } = 3;

        [NinjaScriptProperty, Range(0, 300)]
        [Display(Name = "Exit Cooldown (Seconds)", GroupName = "3. Guards", Order = 4, 
                 Description = "Strict time lock after an exit to prevent machine-gun firing on fast charts.")]
        public int ExitCooldownSeconds { get; set; } = 30;

        // =====================================================================
        // PARAMETERS — TIME
        // =====================================================================
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
        // ATR 
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

        private int      lastEntryBar   = -1;
        private int      lastExitBar    = -1;
        private DateTime lastExitTime   = DateTime.MinValue;
        private double   prevTickPrice  = 0;
        private double   lastLeg1Target = 0;
        private double   lastLeg2Target = 0;

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
            return Math.Min(MaxTotalContracts, riskBasedQty); // V2 Hard Cap Enforced
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

        private void RegisterExitCooldown()
        {
            lastExitBar = CurrentBar;
            lastExitTime = Time[0];
        }

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "KalmanPulse_Fader_V2";
                Calculate                    = Calculate.OnEachTick;
                EntriesPerDirection          = 2;
                EntryHandling                = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                RealtimeErrorHandling        = RealtimeErrorHandling.StopCancelClose;
                TraceOrders                  = false;
            }
            else if (State == State.DataLoaded)
            {
                lastTradeCount = SystemPerformance.AllTrades.Count;
            }
        }

        protected override void OnExecutionUpdate(
            Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution != null && execution.Order != null && execution.Order.OrderState == OrderState.Filled)
            {
                OrderAction action = execution.Order.OrderAction;
                bool isExit = action == OrderAction.Sell || action == OrderAction.BuyToCover;
                
                if (isExit)
                {
                    RegisterExitCooldown();
                }
            }

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
                lastEntryBar = -1; lastExitBar = -1; 
                lastExitTime = DateTime.MinValue;
                prevTickPrice = 0;
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

            // ── Emergency envelope breach exit ────────────────────────────────
            if (Position.MarketPosition == MarketPosition.Long && curPrice < lowerEnvelope)
            {
                ExitLong("EnvelopeBreakExit", "");
                RegisterExitCooldown();
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                prevTickPrice = curPrice; return;
            }
            if (Position.MarketPosition == MarketPosition.Short && curPrice > upperEnvelope)
            {
                ExitShort("EnvelopeBreakExit", "");
                RegisterExitCooldown();
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
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

            // ── Entry gates & Strict Cooldowns ────────────────────────────────
            leg1Hit = false; leg1JustHit = false; currentLeg2Qty = 1; activeLeg2 = "";

            if (CurrentBar == lastEntryBar) { prevTickPrice = curPrice; return; }
            if (consecutiveLosers >= MaxConsecutiveLosses) { prevTickPrice = curPrice; return; }
            if (!IsInTime()) { prevTickPrice = curPrice; return; }

            // Dual-Layer Cooldown Logic
            if (CurrentBar - lastExitBar < ExitCooldownBars) { prevTickPrice = curPrice; return; }
            if ((Time[0] - lastExitTime).TotalSeconds < ExitCooldownSeconds) { prevTickPrice = curPrice; return; }

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

            if (fadeLong && tickUp)
            {
                double stopPrice  = RT(curPrice - atrVal * AtrStopMult);
                double leg1Target = RT(baseline);
                double leg2Target = RT(innerUpper);

                if (curPrice <= stopPrice) { prevTickPrice = curPrice; return; }
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

                Print(string.Format("[KPF_V2] LONG | Baseline:{0:F2} | Qty:{1}+{2} | Stop:{3:F2} | T1:{4:F2} | T2:{5:F2}",
                    baseline, l1q, l2q, stopPrice, leg1Target, leg2Target));
            }
            // ── SHORT FADE ────────────────────────────────────────────────────
            else
            {
                bool fadeShort = curPrice >= innerUpper && curPrice < upperEnvelope
                              && baselineSlope <= slopeThreshold;

                if (fadeShort && tickDown)
                {
                    double stopPrice  = RT(curPrice + atrVal * AtrStopMult);
                    double leg1Target = RT(baseline);
                    double leg2Target = RT(innerLower);

                    if (curPrice >= stopPrice) { prevTickPrice = curPrice; return; }
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

                    Print(string.Format("[KPF_V2] SHORT | Baseline:{0:F2} | Qty:{1}+{2} | Stop:{3:F2} | T1:{4:F2} | T2:{5:F2}",
                        baseline, l1q, l2q, stopPrice, leg1Target, leg2Target));
                }
            }

            prevTickPrice = curPrice;
        }
    }
}