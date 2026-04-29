// CC BY-NC 4.0
// KalmanPulse_Fader.cs — Adaptive Kalman Filter Mean-Reversion Fade Strategy
// ─────────────────────────────────────────────────────────────────────────────
// REGIME ENGINE : Gaussian Kernel Smoother → Adaptive Kalman Filter baseline.
//   Kalman process/measurement noise scales with live ATR, making the filter
//   automatically more responsive in volatile markets and smoother in quiet ones.
//   Regime zones derived from ATR envelopes around the Kalman baseline.
//
// TRADE TYPE    : Two-sided mean-reversion fade (both long and short).
//   Long fade:  price touches inner lower band + micro-reversal (tick uptick).
//   Short fade: price touches inner upper band + micro-reversal (tick downtick).
//
// REGIME ZONES
//   TREND_UP:         close > outerUpper AND baselineSlope > 0      → no entries
//   TREND_DOWN:       close < outerLower AND baselineSlope < 0      → no entries
//   RANGE:            price between inner bands, slope flat          → no entries
//   FADE_LONG_ZONE:   price ≤ innerLower AND > outerLower            → long entries
//   FADE_SHORT_ZONE:  price ≥ innerUpper AND < outerUpper            → short entries
//
// TWO-LEG EXIT STRUCTURE
//   Leg1: Kalman baseline (dynamic — re-submitted each tick).
//   Leg2: Opposite inner band (dynamic — re-submitted each tick).
//   After Leg1 fills: Leg2 stop pivots to breakeven + 4 ticks.
//   Emergency: price breaks outer envelope → immediate flat.
//
// INSTRUMENT    : Agnostic. Optimized for NQ/MNQ; set TickValue accordingly.
// CHART TYPE    : 1-minute candles. Calculate.OnEachTick for Kalman precision.
// ─────────────────────────────────────────────────────────────────────────────

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class KalmanPulse_Fader : Strategy
    {
        // =====================================================================
        // PARAMETERS
        // =====================================================================

        // --- Kalman / Kernel ---
        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name = "Kernel Length", GroupName = "1. Kalman Engine", Order = 0,
                 Description = "Bars the Gaussian kernel averages. Longer = steadier baseline. Default 33.")]
        public int KernelLength { get; set; } = 33;

        [NinjaScriptProperty, Range(0.1, 10.0)]
        [Display(Name = "Kernel Alpha (recency bias)", GroupName = "1. Kalman Engine", Order = 1,
                 Description = "Higher = more weight on recent prices. Default 1.0.")]
        public double KernelAlpha { get; set; } = 1.0;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name = "ATR Period", GroupName = "1. Kalman Engine", Order = 2)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.5, 5.0)]
        [Display(Name = "ATR Factor (outer envelope)", GroupName = "1. Kalman Engine", Order = 3,
                 Description = "Outer band = baseline ± ATR × this. Regime boundary. Default 2.0.")]
        public double AtrFactor { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0.5, 4.0)]
        [Display(Name = "Inner Band Multiplier", GroupName = "1. Kalman Engine", Order = 4,
                 Description = "Inner band = baseline ± ATR × this. Entry trigger zone. Default 1.5.")]
        public double InnerMult { get; set; } = 1.5;

        // --- Risk ---
        [NinjaScriptProperty, Range(0.5, 5.0)]
        [Display(Name = "ATR Stop Multiplier", GroupName = "2. Risk", Order = 0)]
        public double AtrStopMult { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Size Pct", GroupName = "2. Risk", Order = 1)]
        public int SizePct { get; set; } = 100;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5.00  ES=12.50  MNQ=0.50  MES=1.25",
                 GroupName = "2. Risk", Order = 2)]
        public double TickValueDollars { get; set; } = 5.00;

        // --- Guards ---
        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "3. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0=off)", GroupName = "3. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0=off)", GroupName = "3. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

        // --- Time ---
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
        // KALMAN STATE (persists across ticks)
        // =====================================================================
        private double kalmanState    = double.NaN;
        private double kalmanVar      = 1.0;
        private double prevKalmanState = double.NaN;

        // Cached Kalman output for this tick
        private double baseline       = 0;
        private double upperEnvelope  = 0;
        private double lowerEnvelope  = 0;
        private double innerUpper     = 0;
        private double innerLower     = 0;
        private double baselineSlope  = 0;

        // =====================================================================
        // ATR (manual rolling, tick-safe)
        // =====================================================================
        private double[] atrBuffer;
        private double   runningAtr   = 0;
        private int      atrInitCount = 0;

        // =====================================================================
        // RUNTIME STATE
        // =====================================================================
        private int    consecutiveLosers  = 0;
        private int    lastTradeCount     = 0;
        private double sessionStartProfit = 0;

        private bool   leg1Hit         = false;
        private bool   leg1JustHit     = false;
        private int    currentLeg2Qty  = 1;
        private string activeLeg2      = "";

        // Per-bar entry guard (only one entry allowed per bar)
        private int    lastEntryBar    = -1;

        // Previous tick price for micro-reversal detection
        private double prevTickPrice   = 0;

        // Track whether position targets need to be resubmitted this tick
        private double lastLeg1Target  = 0;
        private double lastLeg2Target  = 0;

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
            return Math.Max(1, (int)(1500.0 / dollarRisk));
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
        {
            return Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));
        }

        // Compute ATR as Wilder's smoothed TR, tick-safe using close prices
        private double ComputeAtr()
        {
            if (CurrentBar < 1) return TickSize * 10;
            double tr = Math.Max(High[0] - Low[0],
                        Math.Max(Math.Abs(High[0] - Close[1]),
                                 Math.Abs(Low[0] - Close[1])));

            if (atrInitCount < AtrPeriod)
            {
                atrInitCount++;
                runningAtr = runningAtr + (tr - runningAtr) / atrInitCount;
            }
            else
            {
                runningAtr = (runningAtr * (AtrPeriod - 1) + tr) / AtrPeriod;
            }
            return runningAtr > 0 ? runningAtr : TickSize * 10;
        }

        // Gaussian kernel weighted average of last KernelLength closes
        private double ComputeKernelSmoothed()
        {
            int len = Math.Min(KernelLength, CurrentBar + 1);
            double center     = len / 2.0;
            double weightSum  = 0;
            double priceSum   = 0;

            for (int i = 0; i < len; i++)
            {
                double x            = i;
                double dist         = Math.Abs(x - center);
                double recency      = Math.Exp(-KernelAlpha * (len - 1 - i) / len);
                double local        = Math.Exp(-Math.Pow(dist / (len / 3.0), 2));
                double weight       = recency * local;
                priceSum   += Close[len - 1 - i] * weight;
                weightSum  += weight;
            }
            return weightSum > 0 ? priceSum / weightSum : Close[0];
        }

        // Run one Kalman update step
        private void UpdateKalman(double measurement, double atrVol)
        {
            if (double.IsNaN(kalmanState))
            {
                kalmanState = measurement;
                kalmanVar   = 1.0;
                return;
            }

            double measurementNoise = atrVol * 0.10;
            double processNoise     = atrVol * 0.05;
            double predictedVar     = kalmanVar + processNoise;
            double innovation       = measurement - kalmanState;
            double innovationVar    = predictedVar + measurementNoise;
            double kalmanGain       = predictedVar / innovationVar;

            prevKalmanState = kalmanState;
            kalmanState    += kalmanGain * innovation;
            kalmanVar       = (1.0 - kalmanGain) * predictedVar;
        }

        // Recompute all envelope levels from current Kalman state
        private void RefreshEnvelopes(double atrVal)
        {
            baseline      = kalmanState;
            baselineSlope = double.IsNaN(prevKalmanState) ? 0 : kalmanState - prevKalmanState;

            upperEnvelope = baseline + atrVal * AtrFactor;
            lowerEnvelope = baseline - atrVal * AtrFactor;
            innerUpper    = baseline + atrVal * InnerMult;
            innerLower    = baseline - atrVal * InnerMult;
        }

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "KalmanPulse_Fader";
                Calculate                    = Calculate.OnEachTick;
                EntriesPerDirection          = 2;
                EntryHandling                = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                RealtimeErrorHandling        = RealtimeErrorHandling.StopCancelClose;
                TraceOrders                  = false;
            }
        }

        // =====================================================================
        // CONSECUTIVE LOSER TRACKING
        // =====================================================================
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
        // MAIN TICK/BAR UPDATE
        // =====================================================================
        protected override void OnBarUpdate()
        {
            if (CurrentBar < KernelLength + 4) return;

            // Session open reset
            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers  = 0;
                leg1Hit            = false;
                leg1JustHit        = false;
                currentLeg2Qty     = 1;
                activeLeg2         = "";
                lastEntryBar       = -1;
                prevTickPrice      = 0;
                lastLeg1Target     = 0;
                lastLeg2Target     = 0;
                kalmanState        = double.NaN;
                prevKalmanState    = double.NaN;
                kalmanVar          = 1.0;
                runningAtr         = 0;
                atrInitCount       = 0;
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }

            // Update Kalman on every tick
            double atrVal        = ComputeAtr();
            double kernelVal     = ComputeKernelSmoothed();
            UpdateKalman(kernelVal, atrVal);
            RefreshEnvelopes(atrVal);

            double curPrice = Close[0];

            // ==================================================================
            // EMERGENCY EXIT: price broke outer envelope (failed fade)
            // ==================================================================
            if (Position.MarketPosition == MarketPosition.Long && curPrice < lowerEnvelope)
            {
                ExitLong("EnvelopeBreakExit", "");
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                prevTickPrice = curPrice;
                return;
            }
            if (Position.MarketPosition == MarketPosition.Short && curPrice > upperEnvelope)
            {
                ExitShort("EnvelopeBreakExit", "");
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                prevTickPrice = curPrice;
                return;
            }

            // Trend regime emergency exit
            if (Position.MarketPosition == MarketPosition.Long &&
                curPrice > upperEnvelope && baselineSlope > 0)
            {
                ExitLong("TrendBreakExit", "");
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                prevTickPrice = curPrice;
                return;
            }
            if (Position.MarketPosition == MarketPosition.Short &&
                curPrice < lowerEnvelope && baselineSlope < 0)
            {
                ExitShort("TrendBreakExit", "");
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                prevTickPrice = curPrice;
                return;
            }

            // ==================================================================
            // RUNNER MANAGEMENT: dynamic target update on every tick
            // ==================================================================
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                // Detect Leg1 fill
                if (!leg1Hit && Position.Quantity <= currentLeg2Qty && currentLeg2Qty > 0)
                {
                    leg1Hit     = true;
                    leg1JustHit = true;

                    double pivot = Position.MarketPosition == MarketPosition.Long
                        ? RT(Position.AveragePrice + 4 * TickSize)
                        : RT(Position.AveragePrice - 4 * TickSize);

                    if (activeLeg2 == KPL2)
                        SetStopLoss(KPL2, CalculationMode.Price, pivot, false);
                    else if (activeLeg2 == KPS2)
                        SetStopLoss(KPS2, CalculationMode.Price, pivot, false);
                }
                else if (leg1JustHit)
                {
                    leg1JustHit = false;
                }

                // Update dynamic targets (Kalman baseline for Leg1, opposite inner band for Leg2)
                // Only update when targets have moved by at least 1 tick to avoid excessive submissions
                if (!leg1JustHit && activeLeg2.Length > 0)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        double newL1 = RT(baseline);
                        double newL2 = RT(innerUpper);

                        // Update Leg1 target if not yet filled and price has moved enough
                        if (!leg1Hit && Math.Abs(newL1 - lastLeg1Target) >= TickSize)
                        {
                            SetProfitTarget(KPL1, CalculationMode.Price, newL1);
                            lastLeg1Target = newL1;
                        }
                        // Update Leg2 target
                        if (Math.Abs(newL2 - lastLeg2Target) >= TickSize)
                        {
                            SetProfitTarget(KPL2, CalculationMode.Price, newL2);
                            lastLeg2Target = newL2;
                        }
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        double newL1 = RT(baseline);
                        double newL2 = RT(innerLower);

                        if (!leg1Hit && Math.Abs(newL1 - lastLeg1Target) >= TickSize)
                        {
                            SetProfitTarget(KPS1, CalculationMode.Price, newL1);
                            lastLeg1Target = newL1;
                        }
                        if (Math.Abs(newL2 - lastLeg2Target) >= TickSize)
                        {
                            SetProfitTarget(KPS2, CalculationMode.Price, newL2);
                            lastLeg2Target = newL2;
                        }
                    }
                }

                prevTickPrice = curPrice;
                return;
            }

            // ==================================================================
            // ENTRY GATES
            // ==================================================================
            leg1Hit = false; leg1JustHit = false; currentLeg2Qty = 1; activeLeg2 = "";

            // Gate 1: only one entry per bar
            if (CurrentBar == lastEntryBar) { prevTickPrice = curPrice; return; }

            // Gate 2: circuit breaker
            if (consecutiveLosers >= MaxConsecutiveLosses) { prevTickPrice = curPrice; return; }

            // Gate 3: time filter
            if (!IsInTime()) { prevTickPrice = curPrice; return; }

            // Gate 4: daily P&L guard
            if (DailyGoal > 0 || DailyLossLimit > 0)
            {
                double dailyPnL = SystemPerformance.AllTrades
                                      .TradesPerformance.Currency.CumProfit
                                  - sessionStartProfit;
                if (DailyGoal     > 0 && dailyPnL >=  DailyGoal)      { prevTickPrice = curPrice; return; }
                if (DailyLossLimit > 0 && dailyPnL <= -DailyLossLimit) { prevTickPrice = curPrice; return; }
            }

            // Micro-reversal detection (tick-level)
            bool tickUpReversal   = prevTickPrice > 0 && curPrice > prevTickPrice;
            bool tickDownReversal = prevTickPrice > 0 && curPrice < prevTickPrice;

            // Slope magnitude threshold: |slope| < ATR × 0.1 = "flat enough"
            double slopeThreshold = atrVal * 0.1;

            // ==================================================================
            // LONG FADE: price in FADE_LONG_ZONE with tick uptick
            // ==================================================================
            bool fadeLongZone = curPrice <= innerLower && curPrice > lowerEnvelope
                             && baselineSlope >= -slopeThreshold;

            if (fadeLongZone && tickUpReversal)
            {
                double stopPrice  = RT(curPrice - atrVal * AtrStopMult);
                double leg1Target = RT(baseline);
                double leg2Target = RT(innerUpper);

                // Ensure minimum distance
                if ((leg1Target - curPrice) / TickSize < 4) { prevTickPrice = curPrice; return; }

                int maxC    = CalcMaxContracts(atrVal);
                int sz      = ScaleByConfidence(maxC, SizePct);
                int leg1Qty = Math.Max(1, sz / 2);
                int leg2Qty = Math.Max(1, sz - leg1Qty);

                SetStopLoss(KPL1, CalculationMode.Price, stopPrice, false);
                SetStopLoss(KPL2, CalculationMode.Price, stopPrice, false);
                SetProfitTarget(KPL1, CalculationMode.Price, leg1Target);
                SetProfitTarget(KPL2, CalculationMode.Price, leg2Target);
                EnterLong(leg1Qty, KPL1);
                EnterLong(leg2Qty, KPL2);

                currentLeg2Qty = leg2Qty;
                activeLeg2     = KPL2;
                lastLeg1Target = leg1Target;
                lastLeg2Target = leg2Target;
                lastEntryBar   = CurrentBar;

                Print(string.Format(
                    "[KalmanPulse_Fader] LONG | Baseline:{0:F2} | InnerL:{1:F2} | " +
                    "Slope:{2:F4} | Qty:{3}+{4} | Stop:{5:F2} | T1:{6:F2} | T2:{7:F2}",
                    baseline, innerLower, baselineSlope,
                    leg1Qty, leg2Qty, stopPrice, leg1Target, leg2Target));
            }
            // ==================================================================
            // SHORT FADE: price in FADE_SHORT_ZONE with tick downtick
            // ==================================================================
            else
            {
                bool fadeShortZone = curPrice >= innerUpper && curPrice < upperEnvelope
                                  && baselineSlope <= slopeThreshold;

                if (fadeShortZone && tickDownReversal)
                {
                    double stopPrice  = RT(curPrice + atrVal * AtrStopMult);
                    double leg1Target = RT(baseline);
                    double leg2Target = RT(innerLower);

                    if ((curPrice - leg1Target) / TickSize < 4) { prevTickPrice = curPrice; return; }

                    int maxC    = CalcMaxContracts(atrVal);
                    int sz      = ScaleByConfidence(maxC, SizePct);
                    int leg1Qty = Math.Max(1, sz / 2);
                    int leg2Qty = Math.Max(1, sz - leg1Qty);

                    SetStopLoss(KPS1, CalculationMode.Price, stopPrice, false);
                    SetStopLoss(KPS2, CalculationMode.Price, stopPrice, false);
                    SetProfitTarget(KPS1, CalculationMode.Price, leg1Target);
                    SetProfitTarget(KPS2, CalculationMode.Price, leg2Target);
                    EnterShort(leg1Qty, KPS1);
                    EnterShort(leg2Qty, KPS2);

                    currentLeg2Qty = leg2Qty;
                    activeLeg2     = KPS2;
                    lastLeg1Target = leg1Target;
                    lastLeg2Target = leg2Target;
                    lastEntryBar   = CurrentBar;

                    Print(string.Format(
                        "[KalmanPulse_Fader] SHORT | Baseline:{0:F2} | InnerU:{1:F2} | " +
                        "Slope:{2:F4} | Qty:{3}+{4} | Stop:{5:F2} | T1:{6:F2} | T2:{7:F2}",
                        baseline, innerUpper, baselineSlope,
                        leg1Qty, leg2Qty, stopPrice, leg1Target, leg2Target));
                }
            }

            prevTickPrice = curPrice;
        }
    }
}