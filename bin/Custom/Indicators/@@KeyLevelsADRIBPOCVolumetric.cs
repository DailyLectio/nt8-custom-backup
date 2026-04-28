#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.BarsTypes;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class KeyLevelsADR_IBPOC_VolumetricChart : Indicator
    {
        // Daily ADR (BIP=1)
        private double adrRma = double.NaN;
        private int adrSamples = 0;

        // IB Profile from Volumetric primary series
        private readonly Dictionary<double, double> ibVolByPrice = new Dictionary<double, double>();
        private double ibPOC = double.NaN;
        private bool levelsLockedThisSession = false;

        // Locked levels
        private double lvlPOC, lvlH1, lvlH2, lvlHT, lvlM1, lvlM2, lvlMT;

        // Volumetric access
        private VolumetricBarsType volBars;
        private bool hasVolumetric = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "IB POC (9:30-10:30) from true Volumetric volume-at-price; levels lock at 10:30. ADR from Daily Wilder/RMA.";
                Name                     = "KeyLevelsADR_IBPOC_VolumetricChart";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = true;
                DisplayInDataBox         = true;
                DrawOnPricePanel         = true;
                PaintPriceMarkers        = true;
                IsSuspendedWhileInactive = true;

                AdrLength  = 10;
                Mult1      = 0.50;
                Mult2      = 1.00;
                MultTarget = 1.618;

                IBStartHour = 9;
                IBStartMinute = 30;
                IBEndHour = 10;
                IBEndMinute = 30;

                AddPlot(Brushes.DodgerBlue, "POC");
                AddPlot(Brushes.Gray,       "High1");
                AddPlot(Brushes.Teal,       "High2");
                AddPlot(Brushes.DeepSkyBlue,"HighTarget");
                AddPlot(Brushes.Gray,       "Minus1");
                AddPlot(Brushes.Purple,     "Minus2");
                AddPlot(Brushes.Magenta,    "MinusTarget");
            }
            else if (State == State.Configure)
            {
                // Daily series for ADR
                AddDataSeries(BarsPeriodType.Day, 1);
            }
            else if (State == State.DataLoaded)
            {
                volBars = BarsArray[0].BarsType as VolumetricBarsType;
                hasVolumetric = (volBars != null);

                if (!hasVolumetric)
                    Print($"{Name}: Primary series is NOT Volumetric. Apply this indicator to a Volumetric chart to compute true IB POC.");

                ResetForNewSession();
                SetAllLevelsNaN();
            }
        }

        private void ResetForNewSession()
        {
            ibVolByPrice.Clear();
            ibPOC = double.NaN;
            levelsLockedThisSession = false;
            SetAllLevelsNaN();
        }

        private void SetAllLevelsNaN()
        {
            lvlPOC = lvlH1 = lvlH2 = lvlHT = lvlM1 = lvlM2 = lvlMT = double.NaN;
        }

        private TimeSpan IBStart => new TimeSpan(IBStartHour, IBStartMinute, 0);
        private TimeSpan IBEnd   => new TimeSpan(IBEndHour,   IBEndMinute,   0);

        private double RoundToTick(double price)
        {
            double ts = TickSize;
            if (ts <= 0) return price;
            return Math.Round(price / ts) * ts;
        }

        private void AddVolumetricBarToProfile(int barsAgo)
        {
            if (!hasVolumetric)
                return;

            // Convert barsAgo -> absolute bar index for VolumetricBarsType.Volumes[]
            int barIndex = CurrentBar - barsAgo;
            if (barIndex < 0)
                return;

            // ✅ FIX: Volumes is an array, use Length not Count
            if (volBars.Volumes == null || barIndex >= volBars.Volumes.Length)
                return;

            double low  = Low[barsAgo];
            double high = High[barsAgo];

            double ts = TickSize;
            if (ts <= 0) return;

            double lo = RoundToTick(low);
            double hi = RoundToTick(high);
            if (hi < lo) { double tmp = lo; lo = hi; hi = tmp; }

            for (double p = lo; p <= hi + (ts * 0.5); p += ts)
            {
                double price = RoundToTick(p);

                long bid = volBars.Volumes[barIndex].GetBidVolumeForPrice(price);
                long ask = volBars.Volumes[barIndex].GetAskVolumeForPrice(price);

                double total = bid + ask;
                if (total <= 0)
                    continue;

                if (!ibVolByPrice.ContainsKey(price))
                    ibVolByPrice[price] = 0;

                ibVolByPrice[price] += total;
            }
        }

        private double ComputePOC()
        {
            double poc = double.NaN;
            double maxVol = double.MinValue;

            foreach (var kv in ibVolByPrice)
            {
                if (kv.Value > maxVol)
                {
                    maxVol = kv.Value;
                    poc = kv.Key;
                }
            }
            return poc;
        }

        private double WilderRmaSeedOrUpdate(double priorRma, double x, int n)
        {
            adrSamples++;

            if (adrSamples <= n)
            {
                if (double.IsNaN(priorRma)) return x;
                return priorRma + (x - priorRma) / adrSamples;
            }

            return (priorRma * (n - 1) + x) / n;
        }

        private void LockLevels()
        {
            if (double.IsNaN(ibPOC) || double.IsNaN(adrRma))
                return;

            lvlPOC = ibPOC;

            lvlH1 = lvlPOC + Mult1      * adrRma;
            lvlH2 = lvlPOC + Mult2      * adrRma;
            lvlHT = lvlPOC + MultTarget * adrRma;

            lvlM1 = lvlPOC - Mult1      * adrRma;
            lvlM2 = lvlPOC - Mult2      * adrRma;
            lvlMT = lvlPOC - MultTarget * adrRma;

            levelsLockedThisSession = true;
        }

        protected override void OnBarUpdate()
        {
            // BIP1: Daily ADR
            if (BarsInProgress == 1)
            {
                if (CurrentBars[1] < 2) return;

                double range = Highs[1][1] - Lows[1][1];
                if (range >= 0)
                    adrRma = WilderRmaSeedOrUpdate(adrRma, range, Math.Max(1, AdrLength));

                return;
            }

            // Primary only
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < 1)
                return;

            if (Bars.IsFirstBarOfSession)
                ResetForNewSession();

            if (!hasVolumetric)
            {
                Values[0][0] = lvlPOC;
                Values[1][0] = lvlH1;
                Values[2][0] = lvlH2;
                Values[3][0] = lvlHT;
                Values[4][0] = lvlM1;
                Values[5][0] = lvlM2;
                Values[6][0] = lvlMT;
                return;
            }

            TimeSpan tod = Time[0].TimeOfDay;
            bool inIB = tod >= IBStart && tod < IBEnd;
            bool pastIB = tod >= IBEnd;

            if (!levelsLockedThisSession && inIB)
                AddVolumetricBarToProfile(0);

            if (!levelsLockedThisSession && pastIB)
            {
                ibPOC = ComputePOC();
                LockLevels();
            }

            Values[0][0] = lvlPOC;
            Values[1][0] = lvlH1;
            Values[2][0] = lvlH2;
            Values[3][0] = lvlHT;
            Values[4][0] = lvlM1;
            Values[5][0] = lvlM2;
            Values[6][0] = lvlMT;
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name="ADR Length (Daily RMA/Wilder)", Order=1, GroupName="Parameters")]
        public int AdrLength { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name="Multiplier 1 (High1/Minus1)", Order=2, GroupName="Parameters")]
        public double Mult1 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name="Multiplier 2 (High2/Minus2)", Order=3, GroupName="Parameters")]
        public double Mult2 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name="Target Multiplier (HighTarget/MinusTarget)", Order=4, GroupName="Parameters")]
        public double MultTarget { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name="IB Start Hour", Order=10, GroupName="IB Window")]
        public int IBStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name="IB Start Minute", Order=11, GroupName="IB Window")]
        public int IBStartMinute { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name="IB End Hour", Order=12, GroupName="IB Window")]
        public int IBEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name="IB End Minute", Order=13, GroupName="IB Window")]
        public int IBEndMinute { get; set; }
        #endregion
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private KeyLevelsADR_IBPOC_VolumetricChart[] cacheKeyLevelsADR_IBPOC_VolumetricChart;
		public KeyLevelsADR_IBPOC_VolumetricChart KeyLevelsADR_IBPOC_VolumetricChart(int adrLength, double mult1, double mult2, double multTarget, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute)
		{
			return KeyLevelsADR_IBPOC_VolumetricChart(Input, adrLength, mult1, mult2, multTarget, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute);
		}

		public KeyLevelsADR_IBPOC_VolumetricChart KeyLevelsADR_IBPOC_VolumetricChart(ISeries<double> input, int adrLength, double mult1, double mult2, double multTarget, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute)
		{
			if (cacheKeyLevelsADR_IBPOC_VolumetricChart != null)
				for (int idx = 0; idx < cacheKeyLevelsADR_IBPOC_VolumetricChart.Length; idx++)
					if (cacheKeyLevelsADR_IBPOC_VolumetricChart[idx] != null && cacheKeyLevelsADR_IBPOC_VolumetricChart[idx].AdrLength == adrLength && cacheKeyLevelsADR_IBPOC_VolumetricChart[idx].Mult1 == mult1 && cacheKeyLevelsADR_IBPOC_VolumetricChart[idx].Mult2 == mult2 && cacheKeyLevelsADR_IBPOC_VolumetricChart[idx].MultTarget == multTarget && cacheKeyLevelsADR_IBPOC_VolumetricChart[idx].IBStartHour == iBStartHour && cacheKeyLevelsADR_IBPOC_VolumetricChart[idx].IBStartMinute == iBStartMinute && cacheKeyLevelsADR_IBPOC_VolumetricChart[idx].IBEndHour == iBEndHour && cacheKeyLevelsADR_IBPOC_VolumetricChart[idx].IBEndMinute == iBEndMinute && cacheKeyLevelsADR_IBPOC_VolumetricChart[idx].EqualsInput(input))
						return cacheKeyLevelsADR_IBPOC_VolumetricChart[idx];
			return CacheIndicator<KeyLevelsADR_IBPOC_VolumetricChart>(new KeyLevelsADR_IBPOC_VolumetricChart(){ AdrLength = adrLength, Mult1 = mult1, Mult2 = mult2, MultTarget = multTarget, IBStartHour = iBStartHour, IBStartMinute = iBStartMinute, IBEndHour = iBEndHour, IBEndMinute = iBEndMinute }, input, ref cacheKeyLevelsADR_IBPOC_VolumetricChart);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.KeyLevelsADR_IBPOC_VolumetricChart KeyLevelsADR_IBPOC_VolumetricChart(int adrLength, double mult1, double mult2, double multTarget, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute)
		{
			return indicator.KeyLevelsADR_IBPOC_VolumetricChart(Input, adrLength, mult1, mult2, multTarget, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute);
		}

		public Indicators.KeyLevelsADR_IBPOC_VolumetricChart KeyLevelsADR_IBPOC_VolumetricChart(ISeries<double> input , int adrLength, double mult1, double mult2, double multTarget, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute)
		{
			return indicator.KeyLevelsADR_IBPOC_VolumetricChart(input, adrLength, mult1, mult2, multTarget, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.KeyLevelsADR_IBPOC_VolumetricChart KeyLevelsADR_IBPOC_VolumetricChart(int adrLength, double mult1, double mult2, double multTarget, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute)
		{
			return indicator.KeyLevelsADR_IBPOC_VolumetricChart(Input, adrLength, mult1, mult2, multTarget, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute);
		}

		public Indicators.KeyLevelsADR_IBPOC_VolumetricChart KeyLevelsADR_IBPOC_VolumetricChart(ISeries<double> input , int adrLength, double mult1, double mult2, double multTarget, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute)
		{
			return indicator.KeyLevelsADR_IBPOC_VolumetricChart(input, adrLength, mult1, mult2, multTarget, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute);
		}
	}
}

#endregion
