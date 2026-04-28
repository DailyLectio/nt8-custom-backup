// DonchianStopLine.cs (fixed for NT8)
// Plots Donchian-based stop lines for manual ATM trailing on any Input series (e.g., 3m or 5m).
// LongStop = LowerDonchian + offset; ShortStop = UpperDonchian - offset.
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
    public class DonchianStopLine : Indicator
    {
        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "Period", GroupName = "Parameters", Order = 0)]
        public int Period { get; set; } = 20;

        [NinjaScriptProperty, Range(0, 200)]
        [Display(Name = "Offset Ticks", GroupName = "Parameters", Order = 1)]
        public int OffsetTicks { get; set; } = 4;

        [NinjaScriptProperty]
        [Display(Name = "Calculate On Bar Close", GroupName = "Parameters", Order = 2,
                 Description = "If true, updates only on close; if false, updates intrabar.")]
        public bool CalcOnClose { get; set; } = true;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "DonchianStopLine";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                AddPlot(Brushes.ForestGreen, "LongStop");
                AddPlot(Brushes.Firebrick, "ShortStop");
            }
            else if (State == State.Configure)
            {
                Calculate = CalcOnClose ? Calculate.OnBarClose : Calculate.OnPriceChange;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Period)
            {
                Values[0][0] = double.NaN;
                Values[1][0] = double.NaN;
                return;
            }

            double upper = MAX(High, Period)[0];
            double lower = MIN(Low, Period)[0];

            Values[0][0] = lower + OffsetTicks * TickSize; // LongStop
            Values[1][0] = upper - OffsetTicks * TickSize; // ShortStop
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private DonchianStopLine[] cacheDonchianStopLine;
		public DonchianStopLine DonchianStopLine(int period, int offsetTicks, bool calcOnClose)
		{
			return DonchianStopLine(Input, period, offsetTicks, calcOnClose);
		}

		public DonchianStopLine DonchianStopLine(ISeries<double> input, int period, int offsetTicks, bool calcOnClose)
		{
			if (cacheDonchianStopLine != null)
				for (int idx = 0; idx < cacheDonchianStopLine.Length; idx++)
					if (cacheDonchianStopLine[idx] != null && cacheDonchianStopLine[idx].Period == period && cacheDonchianStopLine[idx].OffsetTicks == offsetTicks && cacheDonchianStopLine[idx].CalcOnClose == calcOnClose && cacheDonchianStopLine[idx].EqualsInput(input))
						return cacheDonchianStopLine[idx];
			return CacheIndicator<DonchianStopLine>(new DonchianStopLine(){ Period = period, OffsetTicks = offsetTicks, CalcOnClose = calcOnClose }, input, ref cacheDonchianStopLine);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.DonchianStopLine DonchianStopLine(int period, int offsetTicks, bool calcOnClose)
		{
			return indicator.DonchianStopLine(Input, period, offsetTicks, calcOnClose);
		}

		public Indicators.DonchianStopLine DonchianStopLine(ISeries<double> input , int period, int offsetTicks, bool calcOnClose)
		{
			return indicator.DonchianStopLine(input, period, offsetTicks, calcOnClose);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.DonchianStopLine DonchianStopLine(int period, int offsetTicks, bool calcOnClose)
		{
			return indicator.DonchianStopLine(Input, period, offsetTicks, calcOnClose);
		}

		public Indicators.DonchianStopLine DonchianStopLine(ISeries<double> input , int period, int offsetTicks, bool calcOnClose)
		{
			return indicator.DonchianStopLine(input, period, offsetTicks, calcOnClose);
		}
	}
}

#endregion
