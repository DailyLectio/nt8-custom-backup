// EMA_VWAP_OF_V2.cs
// NinjaTrader 8 Strategy (VWAP trailing + Chop Guard) — class/name set to EMA_VWAP_OF_V2
#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class EMA_VWAP_OF_V2 : Strategy
    {
        #region === Inputs ===
        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "EMA Fast", GroupName = "010-Entries", Order = 10)]
        public int EmaFast { get; set; } = 7;

        [NinjaScriptProperty, Range(2, 400)]
        [Display(Name = "EMA Slow", GroupName = "010-Entries", Order = 20)]
        public int EmaSlow { get; set; } = 21;

        [NinjaScriptProperty]
        [Display(Name = "Only Longs", GroupName = "010-Entries", Order = 30)]
        public bool OnlyLongs { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Only Shorts", GroupName = "010-Entries", Order = 31)]
        public bool OnlyShorts { get; set; } = false;

        public enum CombineMode { VWAPOnly, TicksOnly, Tightest }
        [NinjaScriptProperty]
        [Display(Name = "Trail Combine Mode", GroupName = "401-Stops", Order = 10)]
        public CombineMode TrailCombine { get; set; } = CombineMode.VWAPOnly;

        [NinjaScriptProperty, Range(0, 200)]    // allow 0
        [Display(Name = "TrailingTicks", GroupName = "401-Stops", Order = 11)]
        public int TrailingTicks { get; set; } = 0;

        public enum VWAPTrailActivation { Immediate, AfterCross1SD, AfterCross2SD }
        public enum VWAPBandChoice { ClosestToPrice, OneSD, TwoSD }
        public enum OffsetMode { Ticks, PercentOfSpan }

        [NinjaScriptProperty]
        [Display(Name = "VWAP Activation", GroupName = "402-VWAP Trail", Order = 10)]
        public VWAPTrailActivation VwapActivation { get; set; } = VWAPTrailActivation.AfterCross1SD;

        [NinjaScriptProperty]
        [Display(Name = "VWAP Band Choice", GroupName = "402-VWAP Trail", Order = 20)]
        public VWAPBandChoice VwapBand { get; set; } = VWAPBandChoice.ClosestToPrice;

        [NinjaScriptProperty]
        [Display(Name = "Offset Mode", GroupName = "402-VWAP Trail", Order = 30)]
        public OffsetMode VwapOffsetMode { get; set; } = OffsetMode.PercentOfSpan;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "VWAP Offset Ticks", GroupName = "402-VWAP Trail", Order = 31)]
        public int VWAPOffsetTicks { get; set; } = 0;

        [NinjaScriptProperty, Range(0.0, 1.0)]
        [Display(Name = "VWAP Offset % of Span", GroupName = "402-VWAP Trail", Order = 32)]
        public double VWAPOffsetPct { get; set; } = 0.50;

        [NinjaScriptProperty]
        [Display(Name = "Use Midline Target", GroupName = "402-VWAP Trail", Order = 40)]
        public bool UseMidlineTarget { get; set; } = false;

        // Chop Guard
        [NinjaScriptProperty]
        [Display(Name = "Use Chop Filter", GroupName = "501-Chop Guard", Order = 10)]
        public bool UseChopFilter { get; set; } = true;

        [NinjaScriptProperty, Range(10, 50)]
        [Display(Name = "Chop Length", GroupName = "501-Chop Guard", Order = 20)]
        public int ChopLength { get; set; } = 14;

        [NinjaScriptProperty, Range(30, 80)]
        [Display(Name = "Chop Max (No-Trade over)", GroupName = "501-Chop Guard", Order = 30)]
        public double ChopMax { get; set; } = 60;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name = "ADX Length", GroupName = "501-Chop Guard", Order = 40)]
        public int AdxLen { get; set; } = 14;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name = "ADX Min", GroupName = "501-Chop Guard", Order = 50)]
        public double AdxMin { get; set; } = 18;

        [NinjaScriptProperty, Range(10, 50)]
        [Display(Name = "EMA Len (slope)", GroupName = "501-Chop Guard", Order = 60)]
        public int EmaLen { get; set; } = 21;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name = "Slope Len", GroupName = "501-Chop Guard", Order = 70)]
        public int SlopeLen { get; set; } = 20;

        [NinjaScriptProperty, Range(0.05, 0.50)]
        [Display(Name = "Slope Z Min (|slope|/ATR)", GroupName = "501-Chop Guard", Order = 80)]
        public double SlopeZMin { get; set; } = 0.15;

        [NinjaScriptProperty, Range(0.8, 2.0)]
        [Display(Name = "Trend Sensitivity", GroupName = "501-Chop Guard", Order = 90, Description = "Higher = stricter filter")]
        public double TrendSensitivity { get; set; } = 1.40;
        #endregion

        #region === Private fields ===
        private EMA emaFast, emaSlow;
        private EMA emaSlopeBase;
        private ATR atrSlope;
        private OrderFlowVWAP ofVwap;

        private bool crossed1SD, crossed2SD;
        private double currentStop = double.NaN;
        private int prevQty = 0;
        #endregion

        #region === OnStateChange ===
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "EMA_VWAP_OF_V2";
                Calculate = Calculate.OnPriceChange;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 5;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.DataLoaded)
            {
                emaFast = EMA(EmaFast);
                emaSlow = EMA(EmaSlow);
                AddChartIndicator(emaFast);
                AddChartIndicator(emaSlow);

                emaSlopeBase = EMA(EmaLen);
                atrSlope = ATR(SlopeLen);

                // Proper OrderFlowVWAP construction (Standard, chart trading hours, 2 SD)
                ofVwap = OrderFlowVWAP(VWAPResolution.Standard, Bars.TradingHours, VWAPStandardDeviations.Two, 1, 2, 0);
                AddChartIndicator(ofVwap);
            }
        }
        #endregion

        #region === OnBarUpdate ===
        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(Math.Max(EmaSlow, EmaLen), SlopeLen) + 5)
                return;

            if (Position.Quantity != prevQty)
            {
                if (prevQty == 0 && Position.Quantity != 0)
                    ResetTrailState();
                prevQty = Position.Quantity;
            }

            UpdateCrossFlags();

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                UpdateStops();
                return;
            }

            if (IsChoppy())
                return;

            bool bullish = CrossAbove(emaFast, emaSlow, 1) && Close[0] >= GetVWAPMid();
            bool bearish = CrossBelow(emaFast, emaSlow, 1) && Close[0] <= GetVWAPMid();

            if (!OnlyShorts && bullish)
                EnterLong(1, "LE");
            else if (!OnlyLongs && bearish)
                EnterShort(1, "SE");
        }
        #endregion

        #region === VWAP helpers ===
        private double GetVWAPMid()  { return ofVwap == null ? Close[0] : ofVwap.VWAP[0]; }
        private double GetU1() { return ofVwap.StdDev1Upper[0]; }   // +1SD
        private double GetU2() { return ofVwap.StdDev2Upper[0]; }   // +2SD
        private double GetL1() { return ofVwap.StdDev1Lower[0]; }   // -1SD
        private double GetL2() { return ofVwap.StdDev2Lower[0]; }   // -2SD
        #endregion

        #region === Trail logic ===
        private void ResetTrailState()
        {
            crossed1SD = crossed2SD = false;
            currentStop = double.NaN;
        }

        private void UpdateCrossFlags()
        {
            if (ofVwap == null) return;
            double u1 = GetU1(), u2 = GetU2();
            double l1 = GetL1(), l2 = GetL2();

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (High[0] >= u1) crossed1SD = true;
                if (High[0] >= u2) crossed2SD = true;
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (Low[0] <= l1) crossed1SD = true;
                if (Low[0] <= l2) crossed2SD = true;
            }
        }

        private bool VwapTrailActive()
        {
            switch (VwapActivation)
            {
                case VWAPTrailActivation.Immediate:     return true;
                case VWAPTrailActivation.AfterCross1SD: return crossed1SD;
                case VWAPTrailActivation.AfterCross2SD: return crossed2SD;
                default: return false;
            }
        }

        private double VWAPStopLong()
        {
            double vwap = GetVWAPMid();
            double u1 = GetU1(), u2 = GetU2();
            double band = vwap;

            switch (VwapBand)
            {
                case VWAPBandChoice.OneSD: band = u1; break;
                case VWAPBandChoice.TwoSD: band = u2; break;
                case VWAPBandChoice.ClosestToPrice:
                    band = (Close[0] >= u2) ? u2 : (Close[0] >= u1 ? u1 : vwap);
                    break;
            }

            if (VwapOffsetMode == OffsetMode.Ticks)
                return band - VWAPOffsetTicks * TickSize;

            double baseRef = (band == u2 ? u1 : (band == u1 ? vwap : vwap));
            double span = Math.Max(band - baseRef, TickSize);
            return band - VWAPOffsetPct * span;
        }

        private double VWAPStopShort()
        {
            double vwap = GetVWAPMid();
            double l1 = GetL1(), l2 = GetL2();
            double band = vwap;

            switch (VwapBand)
            {
                case VWAPBandChoice.OneSD: band = l1; break;
                case VWAPBandChoice.TwoSD: band = l2; break;
                case VWAPBandChoice.ClosestToPrice:
                    band = (Close[0] <= l2) ? l2 : (Close[0] <= l1 ? l1 : vwap);
                    break;
            }

            if (VwapOffsetMode == OffsetMode.Ticks)
                return band + VWAPOffsetTicks * TickSize;

            double baseRef = (band == l2 ? l1 : (band == l1 ? vwap : vwap));
            double span = Math.Max(baseRef - band, TickSize);
            return band + VWAPOffsetPct * span;
        }

        private double TickTrailLong()
        {
            return (TrailingTicks > 0) ? Position.AveragePrice - TrailingTicks * TickSize : double.NaN;
        }
        private double TickTrailShort()
        {
            return (TrailingTicks > 0) ? Position.AveragePrice + TrailingTicks * TickSize : double.NaN;
        }

        private void UpdateStops()
        {
            if (Position.MarketPosition == MarketPosition.Long)
            {
                double vwapCand = VwapTrailActive() ? VWAPStopLong() : double.NaN;
                double tickCand = TickTrailLong();

                double desired =
                    TrailCombine == CombineMode.VWAPOnly  ? vwapCand :
                    TrailCombine == CombineMode.TicksOnly ? tickCand :
                    Math.Max(
                        double.IsNaN(currentStop) ? double.MinValue : currentStop,
                        Math.Max(double.IsNaN(vwapCand) ? double.MinValue : vwapCand,
                                 double.IsNaN(tickCand) ? double.MinValue : tickCand));

                if (!double.IsNaN(desired) && (double.IsNaN(currentStop) || desired > currentStop))
                {
                    currentStop = desired;
                    SetStopLoss(CalculationMode.Price, currentStop);
                }

                if (UseMidlineTarget)
                {
                    double tgt = GetVWAPMid();
                    if (tgt > Position.AveragePrice)
                        SetProfitTarget(CalculationMode.Price, tgt);
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double vwapCand = VwapTrailActive() ? VWAPStopShort() : double.NaN;
                double tickCand = TickTrailShort();

                double desired =
                    TrailCombine == CombineMode.VWAPOnly  ? vwapCand :
                    TrailCombine == CombineMode.TicksOnly ? tickCand :
                    Math.Min(
                        double.IsNaN(currentStop) ? double.MaxValue : currentStop,
                        Math.Min(double.IsNaN(vwapCand) ? double.MaxValue : vwapCand,
                                 double.IsNaN(tickCand) ? double.MaxValue : tickCand));

                if (!double.IsNaN(desired) && (double.IsNaN(currentStop) || desired < currentStop))
                {
                    currentStop = desired;
                    SetStopLoss(CalculationMode.Price, currentStop);
                }

                if (UseMidlineTarget)
                {
                    double tgt = GetVWAPMid();
                    if (tgt < Position.AveragePrice)
                        SetProfitTarget(CalculationMode.Price, tgt);
                }
            }
        }
        #endregion

        #region === Chop Guard ===
        private double Choppiness()
        {
            double n = ChopLength;
            double sumTR = ATR(ChopLength)[0] * ChopLength;
            double hh = MAX(High, ChopLength)[0];
            double ll = MIN(Low, ChopLength)[0];
            double denom = Math.Max(hh - ll, TickSize);
            return 100.0 * Math.Log10(sumTR / denom) / Math.Log10(n);
        }

        private bool IsChoppy()
        {
            if (!UseChopFilter) return false;

            double chopMaxEff = ChopMax / TrendSensitivity;
            double adxMinEff  = AdxMin  * TrendSensitivity;
            double slopeMinEff= SlopeZMin * TrendSensitivity;

            bool chopHigh  = Choppiness() > chopMaxEff;
            bool adxLow    = ADX(AdxLen)[0] < adxMinEff;

            double slopeAbs = Math.Abs(Slope(emaSlopeBase, SlopeLen, 0));
            double atr      = Math.Max(atrSlope[0], TickSize);
            double slopeZ   = slopeAbs / atr;
            bool slopeFlat  = slopeZ < slopeMinEff;

            int votes = (chopHigh?1:0) + (adxLow?1:0) + (slopeFlat?1:0);
            return votes >= 2;
        }
        #endregion
    }
}
