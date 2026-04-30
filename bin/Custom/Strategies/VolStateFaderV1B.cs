// CC BY-NC 4.0
// VolState_Fader_V1B.cs — ATR Ratio Volatility Phase Fade Strategy (V1B)
// ─────────────────────────────────────────────────────────────────────────
// V1B ADDITIONS over V1A:
//   1. A/B Mode switch (RequireFootprintConfirmation)
//      false = V1A: bar-reversal entry only (original behavior)
//      true  = V1B: entry also requires a fresh ABS, DD, or TF signal
//              from OrderFlowSetupScanner via HUDMessengerV1B.
//   2. Daily Bias Filter (EnableDailyBiasFilter)
//      Reads HUDMessengerV1B.CurrentDailyBias set by TradeHUD button:
//        D = both sides (rotation)
//        P = long fades only (bull trend exhaustion)
//        b = short fades only (bear trend exhaustion)
//        B = NO entries (breakout day, no fade edge)
//   3. Kill Switch (EnableKillSwitch)
//      A DEIA or EEMDF signal within the last bar immediately flattens
//      any open runner (Leg2) regardless of A/B mode.
//      Requires Scanner broadcast patch (see V1B_Architecture_Notes.md).
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
    public class VolState_Fader_V1B : Strategy
    {
        // =====================================================================
        // PARAMETERS — ATR REGIME (unchanged from V1A)
        // =====================================================================
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
        [Display(Name = "Compression Threshold", GroupName = "1. ATR Regime", Order = 3)]
        public double CompressionThreshold { get; set; } = 0.70;

        [NinjaScriptProperty, Range(1.0, 1.5)]
        [Display(Name = "Expansion Threshold", GroupName = "1. ATR Regime", Order = 4)]
        public double ExpansionThreshold { get; set; } = 1.15;

        [NinjaScriptProperty, Range(1.2, 3.0)]
        [Display(Name = "High Vol Threshold", GroupName = "1. ATR Regime", Order = 5)]
        public double HighVolThreshold { get; set; } = 1.40;

        [NinjaScriptProperty, Range(2, 20)]
        [Display(Name = "Exhaustion Lookback (bars)", GroupName = "1. ATR Regime", Order = 6)]
        public int ExhaustionLookback { get; set; } = 5;

        [NinjaScriptProperty, Range(10, 500)]
        [Display(Name = "ATR Percentile Lookback", GroupName = "1. ATR Regime", Order = 7)]
        public int PercentileLookback { get; set; } = 100;

        // =====================================================================
        // PARAMETERS — SIGNAL (unchanged)
        // =====================================================================
        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name = "SR Lookback Bars", GroupName = "2. Signal", Order = 0)]
        public int SrLookbackBars { get; set; } = 20;

        [NinjaScriptProperty, Range(0.1, 3.0)]
        [Display(Name = "Edge Proximity (ATR mult)", GroupName = "2. Signal", Order = 1)]
        public double EdgeProximityAtr { get; set; } = 0.5;

        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name = "Bollinger Period", GroupName = "2. Signal", Order = 2)]
        public int BollingerPeriod { get; set; } = 20;

        [NinjaScriptProperty, Range(0.5, 4.0)]
        [Display(Name = "Bollinger StdDev", GroupName = "2. Signal", Order = 3)]
        public double BollingerDev { get; set; } = 2.0;

        [NinjaScriptProperty, Range(4, 500)]
        [Display(Name = "Min Target Ticks", GroupName = "2. Signal", Order = 4)]
        public int MinTargetTicks { get; set; } = 10;

        // =====================================================================
        // PARAMETERS — RISK (unchanged)
        // =====================================================================
        [NinjaScriptProperty, Range(0.5, 5.0)]
        [Display(Name = "ATR Stop Multiplier", GroupName = "3. Risk", Order = 0)]
        public double AtrStopMult { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0.5, 10.0)]
        [Display(Name = "Leg1 TP Multiplier (ATR)", GroupName = "3. Risk", Order = 1)]
        public double Leg1TpMult { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1.0, 15.0)]
        [Display(Name = "Leg2 TP Multiplier (ATR)", GroupName = "3. Risk", Order = 2)]
        public double Leg2TpMult { get; set; } = 2.5;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5  ES=12.50  MNQ=0.50  MES=1.25", GroupName = "3. Risk", Order = 3)]
        public double TickValueDollars { get; set; } = 5.00;

        [NinjaScriptProperty, Range(2, 100)]
        [Display(Name = "Max Total Contracts", GroupName = "3. Risk", Order = 4)]
        public int MaxTotalContracts { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Exhaustion Size Pct", GroupName = "3. Risk", Order = 5)]
        public int ExhaustionSizePct { get; set; } = 75;

        // =====================================================================
        // PARAMETERS — GUARDS (unchanged)
        // =====================================================================
        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "4. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0=off)", GroupName = "4. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0=off)", GroupName = "4. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

        // =====================================================================
        // PARAMETERS — TIME (unchanged)
        // =====================================================================
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
        // PARAMETERS — V1B ORDERFLOW LAYER (NEW)
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "A/B: Require Footprint Confirmation",
                 GroupName = "6. OrderFlow (V1B)", Order = 0,
                 Description = "false=V1A (bar reversal only). true=V1B (also needs ABS/DD/TF signal).")]
        public bool RequireFootprintConfirmation { get; set; } = false;

        [NinjaScriptProperty, Range(1, 15)]
        [Display(Name = "Signal Valid Window (minutes)",
                 GroupName = "6. OrderFlow (V1B)", Order = 1)]
        public int FootprintValidMinutes { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Entry: Accept ABS (Absorption)",
                 GroupName = "6. OrderFlow (V1B)", Order = 2)]
        public bool UseAbsEntry { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Entry: Accept DD (Delta Divergence)",
                 GroupName = "6. OrderFlow (V1B)", Order = 3)]
        public bool UseDdEntry { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Entry: Accept TF (Trapped Flow)",
                 GroupName = "6. OrderFlow (V1B)", Order = 4)]
        public bool UseTfEntry { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable Kill Switch (DEIA / EEMDF / DT)",
                 GroupName = "6. OrderFlow (V1B)", Order = 5,
                 Description = "Flattens Leg2 runner immediately on exhaustion signal.")]
        public bool EnableKillSwitch { get; set; } = true;

        [NinjaScriptProperty, Range(0, 5)]
        [Display(Name = "Kill Switch Signal Age (minutes, 0=same bar only)",
                 GroupName = "6. OrderFlow (V1B)", Order = 6)]
        public int KillSwitchMaxMinutes { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Enable Daily Bias Filter (TradeHUD)",
                 GroupName = "6. OrderFlow (V1B)", Order = 7,
                 Description = "D=both, P=long only, b=short only, B=blocked.")]
        public bool EnableDailyBiasFilter { get; set; } = true;

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
            int riskBasedQty = Math.Max(1, (int)(1500.0 / dollarRisk));
            return Math.Min(MaxTotalContracts, riskBasedQty);
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
        {
            return Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));
        }

        private double GetAtrBaseline()
        {
            if (string.Equals(BaselineType, "EMA", StringComparison.OrdinalIgnoreCase))
            {
                double k = 2.0 / (AtrBaselineLength + 1);
                double emaVal = atr[0];
                for (int i = 1; i < Math.Min(AtrBaselineLength * 2, CurrentBar); i++)
                    emaVal = atr[i] + k * (emaVal - atr[i]);
                return emaVal;
            }
            double sum = 0;
            int bars = Math.Min(AtrBaselineLength, CurrentBar);
            for (int i = 0; i < bars; i++) sum += atr[i];
            return bars > 0 ? sum / bars : atr[0];
        }

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

        private bool IsAtrDeclining()
        {
            int bars = Math.Min(ExhaustionLookback, CurrentBar - 1);
            if (bars < 1) return false;
            for (int i = 0; i < bars; i++)
                if (atr[i] >= atr[i + 1]) return false;
            return true;
        }

        private bool WasRecentlyHighVol(double baseline)
        {
            if (baseline <= 0) return false;
            int bars = Math.Min(10, CurrentBar);
            for (int i = 0; i < bars; i++)
                if (atr[i] / baseline >= HighVolThreshold) return true;
            return false;
        }

        private int ClassifyRegime()
        {
            double atrVal  = atr[0];
            double baseline = GetAtrBaseline();
            if (baseline <= 0) return REGIME_UNKNOWN;
            double ratio = atrVal / baseline;

            if (ratio >= HighVolThreshold)   return REGIME_HIGH_VOL;
            if (ratio >= ExpansionThreshold) return REGIME_EXPANSION;
            if (ratio <  CompressionThreshold) return REGIME_COMPRESSION;
            if (WasRecentlyHighVol(baseline) && IsAtrDeclining()) return REGIME_EXHAUSTION;
            return REGIME_EXPANSION;
        }

        // ── V1B: Footprint entry confirmation ────────────────────────────────
        private bool IsFootprintEntryConfirmed(bool isLong)
        {
            // V1A mode: always pass
            if (!RequireFootprintConfirmation) return true;

            DateTime now = Time[0];

            if (UseAbsEntry && HUDMessengerV1B.IsSignalFresh("Scanner_ABS", now, FootprintValidMinutes))
                return true;
            if (UseDdEntry  && HUDMessengerV1B.IsSignalFresh("Scanner_DD",  now, FootprintValidMinutes))
                return true;
            if (UseTfEntry  && HUDMessengerV1B.IsSignalFresh("Scanner_TF",  now, FootprintValidMinutes))
                return true;

            return false;
        }

        // ── V1B: Kill switch — DEIA, EEMDF, or DT within kill window ─────────
        private bool IsKillSwitchActive()
        {
            if (!EnableKillSwitch) return false;
            DateTime now = Time[0];
            double window = Math.Max(0.0, KillSwitchMaxMinutes);
            // 0 minutes = only fire on the exact same bar-close (age <= 0.5 min buffer)
            double effectiveWindow = window < 0.5 ? 0.5 : window;

            return HUDMessengerV1B.IsSignalFresh("Scanner_DEIA",  now, effectiveWindow)
                || HUDMessengerV1B.IsSignalFresh("Scanner_EEMDF", now, effectiveWindow)
                || HUDMessengerV1B.IsSignalFresh("Scanner_DT",    now, effectiveWindow);
        }

        // ── V1B: Daily bias filter ────────────────────────────────────────────
        // Returns true if the proposed trade direction is compatible with today's shape.
        private bool IsBiasCompatible(bool isLong)
        {
            if (!EnableDailyBiasFilter) return true;
            string bias = HUDMessengerV1B.CurrentDailyBias;

            if (string.IsNullOrEmpty(bias)) return true;

            switch (bias)
            {
                case "D": return true;          // Rotation: both fades allowed
                case "P": return isLong;         // Bull trend: only long fades (exhaustion test)
                case "b": return !isLong;        // Bear trend: only short fades
                case "B": return false;          // Breakout expansion: no fades
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
                Name                         = "VolState_Fader_V1B";
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
            if (CurrentBar < Math.Max(BollingerPeriod, AtrBaselineLength) + 4) return;

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

            currentRegime = ClassifyRegime();

            // ── Emergency regime exit ─────────────────────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat &&
                (currentRegime == REGIME_EXPANSION || currentRegime == REGIME_HIGH_VOL))
            {
                if (Position.MarketPosition == MarketPosition.Long) ExitLong("RegimeBreakExit", "");
                else ExitShort("RegimeBreakExit", "");
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                return;
            }

            // ── V1B Kill Switch: flat Leg2 runner on exhaustion signal ─────────
            if (Position.MarketPosition != MarketPosition.Flat && leg1Hit && IsKillSwitchActive())
            {
                string ks_source = Position.MarketPosition == MarketPosition.Long
                    ? VSL2 : VSS2;
                if (Position.MarketPosition == MarketPosition.Long) ExitLong("KillSwitch", ks_source);
                else ExitShort("KillSwitch", ks_source);
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                Print(string.Format("[VSF_V1B] KILL SWITCH fired at {0:F2}", Close[0]));
                return;
            }

            // ── Runner management ─────────────────────────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (!leg1Hit && Position.Quantity <= currentLeg2Qty && currentLeg2Qty > 0)
                {
                    leg1Hit     = true;
                    leg1JustHit = true;
                    double pivot = Position.MarketPosition == MarketPosition.Long
                        ? RT(Position.AveragePrice + 4 * TickSize)
                        : RT(Position.AveragePrice - 4 * TickSize);
                    if (activeLeg2 == VSL2) SetStopLoss(VSL2, CalculationMode.Price, pivot, false);
                    else if (activeLeg2 == VSS2) SetStopLoss(VSS2, CalculationMode.Price, pivot, false);
                }
                else if (leg1JustHit) leg1JustHit = false;
                return;
            }

            // ── Entry gates ───────────────────────────────────────────────────
            leg1Hit = false; leg1JustHit = false; currentLeg2Qty = 1; activeLeg2 = "";

            if (currentRegime != REGIME_COMPRESSION && currentRegime != REGIME_EXHAUSTION) return;
            if (GetAtrPercentile() > 80) return;
            if (consecutiveLosers >= MaxConsecutiveLosses) return;
            if (!IsInTime()) return;

            if (DailyGoal > 0 || DailyLossLimit > 0)
            {
                double dailyPnL = SystemPerformance.AllTrades
                                      .TradesPerformance.Currency.CumProfit - sessionStartProfit;
                if (DailyGoal     > 0 && dailyPnL >=  DailyGoal)      return;
                if (DailyLossLimit > 0 && dailyPnL <= -DailyLossLimit) return;
            }

            int sizePct    = (currentRegime == REGIME_EXHAUSTION) ? ExhaustionSizePct : 100;
            double atrVal  = atr[0];
            double price   = Close[0];
            bool greenBar  = Close[0] > Open[0];
            bool redBar    = Close[0] < Open[0];
            bool wasGreen  = Close[1] > Open[1];
            bool wasRed    = Close[1] < Open[1];

            double supportEdge    = MIN(Low,  SrLookbackBars)[0];
            double resistanceEdge = MAX(High, SrLookbackBars)[0];
            double proximity      = EdgeProximityAtr * atrVal;
            double fallbackLimit  = 3.0 * atrVal;

            bool bbLowFallback  = (price - supportEdge > fallbackLimit) &&
                                  (Low[1] <= bb.Lower[1] || Low[2] <= bb.Lower[2]);
            bool bbHighFallback = (resistanceEdge - price > fallbackLimit) &&
                                  (High[1] >= bb.Upper[1] || High[2] >= bb.Upper[2]);
            bool nearLowEdge    = price >= supportEdge    && (price - supportEdge)    <= proximity;
            bool nearHighEdge   = price <= resistanceEdge && (resistanceEdge - price) <= proximity;

            // ── LONG FADE ─────────────────────────────────────────────────────
            if ((nearLowEdge || bbLowFallback) && wasRed && greenBar)
            {
                // V1B: bias and footprint checks
                if (!IsBiasCompatible(true)) return;
                if (!IsFootprintEntryConfirmed(true)) return;

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

                Print(string.Format("[VSF_V1B] LONG | Regime:{0} | Bias:{1} | FP:{2} | " +
                    "Qty:{3}+{4} | Stop:{5:F2} | T1:{6:F2} | T2:{7:F2}",
                    currentRegime == REGIME_COMPRESSION ? "COMP" : "EXHST",
                    HUDMessengerV1B.CurrentDailyBias,
                    RequireFootprintConfirmation ? "ON" : "OFF",
                    leg1Qty, leg2Qty, stopPrice, leg1Target, leg2Target));
            }
            // ── SHORT FADE ────────────────────────────────────────────────────
            else if ((nearHighEdge || bbHighFallback) && wasGreen && redBar)
            {
                if (!IsBiasCompatible(false)) return;
                if (!IsFootprintEntryConfirmed(false)) return;

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

                Print(string.Format("[VSF_V1B] SHORT | Regime:{0} | Bias:{1} | FP:{2} | " +
                    "Qty:{3}+{4} | Stop:{5:F2} | T1:{6:F2} | T2:{7:F2}",
                    currentRegime == REGIME_COMPRESSION ? "COMP" : "EXHST",
                    HUDMessengerV1B.CurrentDailyBias,
                    RequireFootprintConfirmation ? "ON" : "OFF",
                    leg1Qty, leg2Qty, stopPrice, leg1Target, leg2Target));
            }
        }
    }
}
