#region Using declarations
using System;
using System.ComponentModel;                               // for [Browsable], etc.
using System.ComponentModel.DataAnnotations;               // for [Range], [Display]
using System.Windows.Media;                                // for Brushes
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

// Put indicators in this namespace
namespace NinjaTrader.NinjaScript.Indicators
{
    public class GoNoGoTrend : Indicator
    {
        private EMA fastEMA;
        private EMA slowEMA;
        private ADX adx;

        // --- Parameters ---
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Fast EMA", Order = 1, GroupName = "Parameters")]
        public int FastPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Slow EMA", Order = 2, GroupName = "Parameters")]
        public int SlowPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ADX Period", Order = 3, GroupName = "Parameters")]
        public int AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ADX Threshold", Order = 4, GroupName = "Parameters")]
        [Range(1, 100)]
        public int AdxThreshold { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                    = "GoNoGoTrend";
                Description             = "Colors price bars by trend direction/strength using EMA cross + ADX.";
                Calculate               = Calculate.OnBarClose;  // change to OnEachTick if you want it faster
                IsOverlay               = true;                  // paint on price panel
                DisplayInDataBox        = true;
                PaintPriceMarkers       = true;

                FastPeriod              = 8;
                SlowPeriod              = 21;
                AdxPeriod               = 14;
                AdxThreshold            = 20;                   // classic ADX “trend is on” line
            }
            else if (State == State.DataLoaded)
            {
                fastEMA = EMA(FastPeriod);
                slowEMA = EMA(SlowPeriod);
                adx     = ADX(AdxPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(FastPeriod, SlowPeriod))
                return;

            double fast   = fastEMA[0];
            double slow   = slowEMA[0];
            double adxVal = adx[0];

            // Strong/weak up vs down using ADX as strength gate
            if (fast > slow && adxVal >= AdxThreshold)
            {
                // Strong Go
                BarBrush = Brushes.DodgerBlue;
                CandleOutlineBrush = Brushes.DodgerBlue;
            }
            else if (fast > slow && adxVal < AdxThreshold)
            {
                // Weak Go
                BarBrush = Brushes.Green;
                CandleOutlineBrush = Brushes.Green;
            }
            else if (fast < slow && adxVal >= AdxThreshold)
            {
                // Strong No Go
                BarBrush = Brushes.Purple;
                CandleOutlineBrush = Brushes.Purple;
            }
            else
            {
                // Weak No Go
                BarBrush = Brushes.HotPink;
                CandleOutlineBrush = Brushes.HotPink;
            }
        }
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GoNoGoTrend[] cacheGoNoGoTrend;
		public GoNoGoTrend GoNoGoTrend(int fastPeriod, int slowPeriod, int adxPeriod, int adxThreshold)
		{
			return GoNoGoTrend(Input, fastPeriod, slowPeriod, adxPeriod, adxThreshold);
		}

		public GoNoGoTrend GoNoGoTrend(ISeries<double> input, int fastPeriod, int slowPeriod, int adxPeriod, int adxThreshold)
		{
			if (cacheGoNoGoTrend != null)
				for (int idx = 0; idx < cacheGoNoGoTrend.Length; idx++)
					if (cacheGoNoGoTrend[idx] != null && cacheGoNoGoTrend[idx].FastPeriod == fastPeriod && cacheGoNoGoTrend[idx].SlowPeriod == slowPeriod && cacheGoNoGoTrend[idx].AdxPeriod == adxPeriod && cacheGoNoGoTrend[idx].AdxThreshold == adxThreshold && cacheGoNoGoTrend[idx].EqualsInput(input))
						return cacheGoNoGoTrend[idx];
			return CacheIndicator<GoNoGoTrend>(new GoNoGoTrend(){ FastPeriod = fastPeriod, SlowPeriod = slowPeriod, AdxPeriod = adxPeriod, AdxThreshold = adxThreshold }, input, ref cacheGoNoGoTrend);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GoNoGoTrend GoNoGoTrend(int fastPeriod, int slowPeriod, int adxPeriod, int adxThreshold)
		{
			return indicator.GoNoGoTrend(Input, fastPeriod, slowPeriod, adxPeriod, adxThreshold);
		}

		public Indicators.GoNoGoTrend GoNoGoTrend(ISeries<double> input , int fastPeriod, int slowPeriod, int adxPeriod, int adxThreshold)
		{
			return indicator.GoNoGoTrend(input, fastPeriod, slowPeriod, adxPeriod, adxThreshold);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GoNoGoTrend GoNoGoTrend(int fastPeriod, int slowPeriod, int adxPeriod, int adxThreshold)
		{
			return indicator.GoNoGoTrend(Input, fastPeriod, slowPeriod, adxPeriod, adxThreshold);
		}

		public Indicators.GoNoGoTrend GoNoGoTrend(ISeries<double> input , int fastPeriod, int slowPeriod, int adxPeriod, int adxThreshold)
		{
			return indicator.GoNoGoTrend(input, fastPeriod, slowPeriod, adxPeriod, adxThreshold);
		}
	}
}

#endregion
