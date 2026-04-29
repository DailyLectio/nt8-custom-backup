// CC BY-NC 4.0
// VolState_Fader.cs — ATR Ratio Volatility Phase Fade Strategy
// ─────────────────────────────────────────────────────────────
// REGIME ENGINE : ATR Ratio vs. long-term ATR baseline (SMA/EMA).
//   Classifies market into: COMPRESSION, EXPANSION, HIGH_VOLATILITY, EXHAUSTION.
//   Trades are only permitted in COMPRESSION and EXHAUSTION phases.
//
// TRADE TYPE    : Two-sided range fade (both long and short).
//   Long fade:  price near structural low edge + reversal bar.
//   Short fade: price near structural high edge + reversal bar.
//
// SIGNAL ARCHITECTURE
//   Primary:   Price within EdgeProximityAtr × ATR of highest high / lowest low
//              over configurable SR lookback period.
//   Fallback:  Bollinger band touch when structural edge is too far (> 3× ATR).
//   Reversal:  Prior bar opposing direction + current bar confirming reversal.
//
// TWO-LEG EXIT STRUCTURE
//   Leg1: ATR × Leg1TpMult from entry (configurable, default 1.5×).
//   Leg2: ATR × Leg2TpMult from entry (configurable, default 2.5×).
//   After Leg1 fills: Leg2 stop pivots to breakeven + 4 ticks (free trade).
//
// EMERGENCY EXITS
//   Regime → EXPANSION or HIGH_VOLATILITY while in position → immediate flat.
//   ATR percentile > 80 (extreme vol event) → block new entries only.
//
// INSTRUMENT    : Agnostic. Optimized for NQ/MNQ; set TickValue accordingly.
// CHART TYPE    : 1-minute candles. Calculate.OnBarClose.
// ─────────────────────────────────────────────────────────────

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class VolState_Fader : Strategy
    {
        // =====================================================================
        // PARAMETERS
        // =====================================================================

        // --- ATR / Regime ---
        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "ATR Period (Short)", GroupName = "1. ATR Regime", Order = 0)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(10, 500)]
        [Display(Name = "ATR Baseline Length", GroupName = "1. ATR Regime", Order = 1)]
        public int AtrBaselineLength { get; set; } = 50;

        [NinjaScriptProperty]
        [Display(Name = "Baseline Type (SMA/EMA)", GroupName = "1. ATR Regime", Order = 2)]
        public string BaselineType { get; set; } = "SMA";

        [NinjaScriptProperty, Range(0.3, 0.95)]
        [Display(Name = "Compression Threshold (ratio)", GroupName = "1. ATR Regime", Order = 3,
                 Description = "atrRatio below this = COMPRESSION. Default 0.70.")]
        public double CompressionThreshold { get; set; } = 0.70;

        [NinjaScriptProperty, Range(1.0, 1.5)]
        [Display(Name = "Expansion Threshold (ratio)", GroupName = "1. ATR Regime", Order = 4,
                 Description = "atrRatio above this = EXPANSION (no entries). Default 1.15.")]
        public double ExpansionThreshold { get; set; } = 1.15;

        [NinjaScriptProperty, Range(1.2, 3.0)]
        [Display(Name = "High Vol Threshold (ratio)", GroupName = "1. ATR Regime", Order = 5,
                 Description = "atrRatio above this = HIGH_VOLATILITY (no entries). Default 1.40.")]
        public double HighVolThreshold { get; set; } = 1.40;

        [NinjaScriptProperty, Range(2, 20)]
        [Display(Name = "Exhaustion Lookback (bars)", GroupName = "1. ATR Regime", Order = 6,
                 Description = "Consecutive bars ATR must decline to confirm EXHAUSTION. Default 5.")]
        public int ExhaustionLookback { get; set; } = 5;

        [NinjaScriptProperty, Range(10, 500)]
        [Display(Name = "ATR Percentile Lookback", GroupName = "1. ATR Regime", Order = 7,
                 Description = "Historical bars for ATR percentile calc. >80th = block entries.")]
        public int PercentileLookback { get; set; } = 100;

        // --- Signal ---
        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name = "SR Lookback Bars", GroupName = "2. Signal", Order = 0,
                 Description = "Bars used to compute structural high/low edge. Default 20.")]
        public int SrLookbackBars { get; set; } = 20;

        [NinjaScriptProperty, Range(0.1, 3.0)]
        [Display(Name = "Edge Proximity (ATR mult)", GroupName = "2. Signal", Order = 1,
                 Description = "Enter only within this ATR distance of structural edge. Default 0.5.")]
        public double EdgeProximityAtr { get; set; } = 0.5;

        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name = "Bollinger Period", GroupName = "2. Signal", Order = 2)]
        public int BollingerPeriod { get; set; } = 20;

        [NinjaScriptProperty, Range(0.5, 4.0)]
        [Display(Name = "Bollinger StdDev", GroupName = "2. Signal", Order = 3)]
        public double BollingerDev { get; set; } = 2.0;

        [NinjaScriptProperty, Range(4, 500)]
        [Display(Name = "Min Target Ticks", GroupName = "2. Signal", Order = 4,
                 Description = "Skip entry if structural edge is too close. Default 10.")]
        public int MinTargetTicks { get; set; } = 10;

        // --- Risk ---
        [NinjaScriptProperty, Range(0.5, 5.0)]
        [Display(Name = "ATR Stop Multiplier", GroupName = "3. Risk", Order = 0)]
        public double AtrStopMult { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0.5, 10.0)]
        [Display(Name = "Leg1 TP Multiplier (ATR)", GroupName = "3. Risk", Order = 1,
                 Description = "First profit target = entry ± ATR × this. Default 1.5.")]
        public double Leg1TpMult { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1.0, 15.0)]
        [Display(Name = "Leg2 TP Multiplier (ATR)", GroupName = "3. Risk", Order = 2,
                 Description = "Second profit target (runner) = entry ± ATR × this. Default 2.5.")]
        public double Leg2TpMult { get; set; } = 2.5;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5.00  ES=12.50  MNQ=0.50  MES=1.25",
                 GroupName = "3. Risk", Order = 3)]
        public double TickValueDollars { get; set; } = 5.00;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Exhaustion Size Pct (vs 100 Compression)", GroupName = "3. Risk", Order = 4,
                 Description = "Position size % in EXHAUSTION regime. Default 75.")]
        public int ExhaustionSizePct { get; set; } = 75;

        // --- Guards ---
        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "4. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0=off)", GroupName = "4. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0=off)", GroupName = "4. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

        // --- Time ---
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "5. Time", Order = 0)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Start Time (HHmmss)", GroupName = "5. Time", Order = 1)]
        public int StartTime { get; set; } = 103500;

        [NinjaScriptProperty]
        [Display(Name = "End Time (HHmmss)", GroupName = "5. Time", Order = 2)]
        public int EndTime { get; set; } = 155500;

        // =====================================================================
        // REGIME CONSTANTS
        // =====================================================================
        private const int REGIME_UNKNOWN     = 0;
        private const int REGIME_COMPRESSION = 1;
        private const int REGIME_EXPANSION   = 2;
        private const int REGIME_HIGH_VOL    = 3;
        private const int REGIME_EXHAUSTION  = 4;

        // =====================================================================
        // INDICATORS
        // =====================================================================
        private ATR       atr;
        private Bollinger bb;

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
        private bool   isLongTrade     = false;

        private int    currentRegime   = REGIME_UNKNOWN;

        // =====================================================================
        // ORDER LABELS
        // =====================================================================
        private const string VSL1 = "VSF_Long1";
        private const string VSL2 = "VSF_Long2";
        private const string VSS1 = "VSF_Short1";
        private const string VSS2 = "VSF_Short2";

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

        private int CalcMaxContracts()
        {
            double atrVal = atr[0];
            if (atrVal <= 0) return 1;
            double dollarRisk = (atrVal * AtrStopMult) / TickSize * TickValueDollars;
            if (dollarRisk <= 0) return 1;
            return Math.Max(1, (int)(1500.0 / dollarRisk));
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
        {
            return Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));
        }

        // ATR baseline using SMA or EMA over AtrBaselineLength bars
        private double GetAtrBaseline()
        {
            // Manually compute rolling average of ATR values
            double sum = 0;
            double emaVal = atr[0];
            if (string.Equals(BaselineType, "EMA", StringComparison.OrdinalIgnoreCase))
            {
                double k = 2.0 / (AtrBaselineLength + 1);
                emaVal = atr[0];
                for (int i = 1; i < Math.Min(AtrBaselineLength * 2, CurrentBar); i++)
                    emaVal = atr[i] + k * (emaVal - atr[i]);
                return emaVal;
            }
            else
            {
                int bars = Math.Min(AtrBaselineLength, CurrentBar);
                for (int i = 0; i < bars; i++) sum += atr[i];
                return bars > 0 ? sum / bars : atr[0];
            }
        }

        // ATR percentile: rank of current ATR among last PercentileLookback bars
        private double GetAtrPercentile()
        {
            int lookback = Math.Min(PercentileLookback, CurrentBar);
            if (lookback <= 0) return 50;
            double cur = atr[0];
            int below = 0;
            for (int i = 1; i <= lookback; i++)
                if (atr[i] < cur) below++;
            return (double)below / lookback * 100.0;
        }

        // Check if ATR has been declining for ExhaustionLookback consecutive bars
        private bool IsAtrDeclining()
        {
            int bars = Math.Min(ExhaustionLookback, CurrentBar - 1);
            if (bars < 1) return false;
            for (int i = 0; i < bars; i++)
                if (atr[i] >= atr[i + 1]) return false;
            return true;
        }

        // Check if ATR was >= HighVolThreshold × baseline within last 10 bars
        private bool WasRecentlyHighVol(double baseline)
        {
            if (baseline <= 0) return false;
            int bars = Math.Min(10, CurrentBar);
            for (int i = 0; i < bars; i++)
                if (atr[i] / baseline >= HighVolThreshold) return true;
            return false;
        }

        // Classify current volatility regime
        private int ClassifyRegime()
        {
            double atrVal  = atr[0];
            double baseline = GetAtrBaseline();
            if (baseline <= 0) return REGIME_UNKNOWN;

            double ratio = atrVal / baseline;

            if (ratio >= HighVolThreshold)   return REGIME_HIGH_VOL;
            if (ratio >= ExpansionThreshold) return REGIME_EXPANSION;
            if (ratio <  CompressionThreshold) return REGIME_COMPRESSION;

            // Exhaustion: was recently high vol AND ATR now declining consecutively
            if (WasRecentlyHighVol(baseline) && IsAtrDeclining()) return REGIME_EXHAUSTION;

            return REGIME_EXPANSION; // neutral zone — treat as expansion (no entries)
        }

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "VolState_Fader";
                Calculate                    = Calculate.OnBarClose;
                EntriesPerDirection          = 2;
                EntryHandling                = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                RealtimeErrorHandling        = RealtimeErrorHandling.StopCancelClose;
                TraceOrders                  = false;
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(AtrPeriod);
                bb  = Bollinger(BollingerDev, BollingerPeriod);
                lastTradeCount = SystemPerformance.AllTrades.Count;
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
        // MAIN BAR UPDATE
        // =====================================================================
        protected override void OnBarUpdate()
        {
            // Need enough bars for all indicators
            if (CurrentBar < Math.Max(BollingerPeriod, AtrBaselineLength) + 4) return;

            // Session open reset
            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers  = 0;
                leg1Hit            = false;
                leg1JustHit        = false;
                currentLeg2Qty     = 1;
                activeLeg2         = "";
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }

            // Classify regime every bar
            currentRegime = ClassifyRegime();

            // ==================================================================
            // EMERGENCY EXIT: regime broke into expansion/trend while in position
            // ==================================================================
            if (Position.MarketPosition != MarketPosition.Flat &&
                (currentRegime == REGIME_EXPANSION || currentRegime == REGIME_HIGH_VOL))
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong("RegimeBreakExit", "");
                else
                    ExitShort("RegimeBreakExit", "");
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                return;
            }

            // ==================================================================
            // RUNNER MANAGEMENT (position is open, regime still valid)
            // ==================================================================
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                // Detect Leg1 fill: position quantity has dropped to Leg2 quantity
                if (!leg1Hit && Position.Quantity <= currentLeg2Qty && currentLeg2Qty > 0)
                {
                    leg1Hit     = true;
                    leg1JustHit = true;

                    double pivot = Position.MarketPosition == MarketPosition.Long
                        ? RT(Position.AveragePrice + 4 * TickSize)
                        : RT(Position.AveragePrice - 4 * TickSize);

                    if (activeLeg2 == VSL2)
                        SetStopLoss(VSL2, CalculationMode.Price, pivot, false);
                    else if (activeLeg2 == VSS2)
                        SetStopLoss(VSS2, CalculationMode.Price, pivot, false);
                }
                else if (leg1JustHit)
                {
                    leg1JustHit = false;
                }

                return;
            }

            // ==================================================================
            // ENTRY GATES
            // ==================================================================
            leg1Hit = false; leg1JustHit = false; currentLeg2Qty = 1; activeLeg2 = "";

            // Gate 1: regime must be tradeable
            if (currentRegime != REGIME_COMPRESSION && currentRegime != REGIME_EXHAUSTION) return;

            // Gate 2: ATR percentile protection (extreme vol event)
            if (GetAtrPercentile() > 80) return;

            // Gate 3: circuit breaker
            if (consecutiveLosers >= MaxConsecutiveLosses) return;

            // Gate 4: time filter
            if (!IsInTime()) return;

            // Gate 5: daily P&L guard
            if (DailyGoal > 0 || DailyLossLimit > 0)
            {
                double dailyPnL = SystemPerformance.AllTrades
                                      .TradesPerformance.Currency.CumProfit
                                  - sessionStartProfit;
                if (DailyGoal     > 0 && dailyPnL >=  DailyGoal)      return;
                if (DailyLossLimit > 0 && dailyPnL <= -DailyLossLimit) return;
            }

            // Determine size percentage: full in Compression, reduced in Exhaustion
            int sizePct = (currentRegime == REGIME_EXHAUSTION) ? ExhaustionSizePct : 100;

            double atrVal = atr[0];
            double price  = Close[0];

            bool greenBar = Close[0] > Open[0];
            bool redBar   = Close[0] < Open[0];
            bool wasGreen = Close[1] > Open[1];
            bool wasRed   = Close[1] < Open[1];

            // Structural edges
            double supportEdge    = Lowest(Low,  SrLookbackBars)[0];
            double resistanceEdge = Highest(High, SrLookbackBars)[0];

            double proximity = EdgeProximityAtr * atrVal;
            double fallbackLimit = 3.0 * atrVal;

            // Bollinger fallback conditions
            bool bbLowFallback  = (price - supportEdge > fallbackLimit) &&
                                  (Low[1] <= bb.Lower[1] || Low[2] <= bb.Lower[2]);
            bool bbHighFallback = (resistanceEdge - price > fallbackLimit) &&
                                  (High[1] >= bb.Upper[1] || High[2] >= bb.Upper[2]);

            bool nearLowEdge  = (price - supportEdge    >= 0) && (price - supportEdge    <= proximity);
            bool nearHighEdge = (resistanceEdge - price >= 0) && (resistanceEdge - price <= proximity);

            // ==================================================================
            // LONG FADE: near structural low, reversal bar
            // ==================================================================
            if ((nearLowEdge || bbLowFallback) && wasRed && greenBar)
            {
                double distToEdge = resistanceEdge - price;
                if (distToEdge / TickSize < MinTargetTicks) distToEdge = MinTargetTicks * TickSize;

                double stopPrice  = RT(price - atrVal * AtrStopMult);
                double leg1Target = RT(price + atrVal * Leg1TpMult);
                double leg2Target = RT(price + atrVal * Leg2TpMult);

                int maxC    = CalcMaxContracts();
                int sz      = ScaleByConfidence(maxC, sizePct);
                int leg1Qty = Math.Max(1, sz / 2);
                int leg2Qty = Math.Max(1, sz - leg1Qty);

                SetStopLoss(VSL1, CalculationMode.Price, stopPrice, false);
                SetStopLoss(VSL2, CalculationMode.Price, stopPrice, false);
                SetProfitTarget(VSL1, CalculationMode.Price, leg1Target);
                SetProfitTarget(VSL2, CalculationMode.Price, leg2Target);
                EnterLong(leg1Qty, VSL1);
                EnterLong(leg2Qty, VSL2);

                currentLeg2Qty = leg2Qty;
                activeLeg2     = VSL2;
                isLongTrade    = true;

                string trigger = nearLowEdge
                    ? string.Format("EDGE_LOW:{0:F2}", supportEdge) : "BB_FALLBACK_LOW";
                Print(string.Format(
                    "[VolState_Fader] LONG | Regime:{0} | Trigger:{1} | " +
                    "SizePct:{2} | Qty:{3}+{4} | Stop:{5:F2} | T1:{6:F2} | T2:{7:F2}",
                    currentRegime == REGIME_COMPRESSION ? "COMPRESSION" : "EXHAUSTION",
                    trigger, sizePct, leg1Qty, leg2Qty, stopPrice, leg1Target, leg2Target));
            }
            // ==================================================================
            // SHORT FADE: near structural high, reversal bar
            // ==================================================================
            else if ((nearHighEdge || bbHighFallback) && wasGreen && redBar)
            {
                double stopPrice  = RT(price + atrVal * AtrStopMult);
                double leg1Target = RT(price - atrVal * Leg1TpMult);
                double leg2Target = RT(price - atrVal * Leg2TpMult);

                int maxC    = CalcMaxContracts();
                int sz      = ScaleByConfidence(maxC, sizePct);
                int leg1Qty = Math.Max(1, sz / 2);
                int leg2Qty = Math.Max(1, sz - leg1Qty);

                SetStopLoss(VSS1, CalculationMode.Price, stopPrice, false);
                SetStopLoss(VSS2, CalculationMode.Price, stopPrice, false);
                SetProfitTarget(VSS1, CalculationMode.Price, leg1Target);
                SetProfitTarget(VSS2, CalculationMode.Price, leg2Target);
                EnterShort(leg1Qty, VSS1);
                EnterShort(leg2Qty, VSS2);

                currentLeg2Qty = leg2Qty;
                activeLeg2     = VSS2;
                isLongTrade    = false;

                string trigger = nearHighEdge
                    ? string.Format("EDGE_HIGH:{0:F2}", resistanceEdge) : "BB_FALLBACK_HIGH";
                Print(string.Format(
                    "[VolState_Fader] SHORT | Regime:{0} | Trigger:{1} | " +
                    "SizePct:{2} | Qty:{3}+{4} | Stop:{5:F2} | T1:{6:F2} | T2:{7:F2}",
                    currentRegime == REGIME_COMPRESSION ? "COMPRESSION" : "EXHAUSTION",
                    trigger, sizePct, leg1Qty, leg2Qty, stopPrice, leg1Target, leg2Target));
            }
        }
    }
}