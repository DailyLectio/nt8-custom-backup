// PriorBarHL.cs (fixed for NT8)
// Plots the prior bar's High and Low for the selected Input series (e.g., 3m or 5m).
// Attach your ATM stop to PrevLow (longs) or PrevHigh (shorts).
#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;                 // Brushes
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class PriorBarHL : Indicator
    {
        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "Bars Ago (prior = 1)", GroupName = "Parameters", Order = 0)]
        public int BarsAgoRef { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Calculate On Bar Close", GroupName = "Parameters", Order = 1,
                 Description = "If true, updates only when the input bar closes. If false, updates intrabar.")]
        public bool CalcOnClose { get; set; } = true;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "PriorBarHL";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                AddPlot(Brushes.DodgerBlue, "PrevHigh");
                AddPlot(Brushes.IndianRed, "PrevLow");
            }
            else if (State == State.Configure)
            {
                Calculate = CalcOnClose ? Calculate.OnBarClose : Calculate.OnPriceChange;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsAgoRef)
            {
                Values[0][0] = double.NaN;
                Values[1][0] = double.NaN;
                return;
            }

            Values[0][0] = High[BarsAgoRef]; // PrevHigh from chosen Input series
            Values[1][0] = Low[BarsAgoRef];  // PrevLow from chosen Input series
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private PriorBarHL[] cachePriorBarHL;
		public PriorBarHL PriorBarHL(int barsAgoRef, bool calcOnClose)
		{
			return PriorBarHL(Input, barsAgoRef, calcOnClose);
		}

		public PriorBarHL PriorBarHL(ISeries<double> input, int barsAgoRef, bool calcOnClose)
		{
			if (cachePriorBarHL != null)
				for (int idx = 0; idx < cachePriorBarHL.Length; idx++)
					if (cachePriorBarHL[idx] != null && cachePriorBarHL[idx].BarsAgoRef == barsAgoRef && cachePriorBarHL[idx].CalcOnClose == calcOnClose && cachePriorBarHL[idx].EqualsInput(input))
						return cachePriorBarHL[idx];
			return CacheIndicator<PriorBarHL>(new PriorBarHL(){ BarsAgoRef = barsAgoRef, CalcOnClose = calcOnClose }, input, ref cachePriorBarHL);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.PriorBarHL PriorBarHL(int barsAgoRef, bool calcOnClose)
		{
			return indicator.PriorBarHL(Input, barsAgoRef, calcOnClose);
		}

		public Indicators.PriorBarHL PriorBarHL(ISeries<double> input , int barsAgoRef, bool calcOnClose)
		{
			return indicator.PriorBarHL(input, barsAgoRef, calcOnClose);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.PriorBarHL PriorBarHL(int barsAgoRef, bool calcOnClose)
		{
			return indicator.PriorBarHL(Input, barsAgoRef, calcOnClose);
		}

		public Indicators.PriorBarHL PriorBarHL(ISeries<double> input , int barsAgoRef, bool calcOnClose)
		{
			return indicator.PriorBarHL(input, barsAgoRef, calcOnClose);
		}
	}
}

#endregion
