#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;

#endregion



#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		
		private Svt.SvtAtrTrailingStop[] cacheSvtAtrTrailingStop;

		
		public Svt.SvtAtrTrailingStop SvtAtrTrailingStop(int atrPeriod, double atrFactor, SvtRef.Models.AverageType averageType, SvtRef.Models.TrailType trailType, bool showSignal, double signalDispl)
		{
			return SvtAtrTrailingStop(Input, atrPeriod, atrFactor, averageType, trailType, showSignal, signalDispl);
		}


		
		public Svt.SvtAtrTrailingStop SvtAtrTrailingStop(ISeries<double> input, int atrPeriod, double atrFactor, SvtRef.Models.AverageType averageType, SvtRef.Models.TrailType trailType, bool showSignal, double signalDispl)
		{
			if (cacheSvtAtrTrailingStop != null)
				for (int idx = 0; idx < cacheSvtAtrTrailingStop.Length; idx++)
					if (cacheSvtAtrTrailingStop[idx].AtrPeriod == atrPeriod && cacheSvtAtrTrailingStop[idx].AtrFactor == atrFactor && cacheSvtAtrTrailingStop[idx].AverageType == averageType && cacheSvtAtrTrailingStop[idx].TrailType == trailType && cacheSvtAtrTrailingStop[idx].ShowSignal == showSignal && cacheSvtAtrTrailingStop[idx].SignalDispl == signalDispl && cacheSvtAtrTrailingStop[idx].EqualsInput(input))
						return cacheSvtAtrTrailingStop[idx];
			return CacheIndicator<Svt.SvtAtrTrailingStop>(new Svt.SvtAtrTrailingStop(){ AtrPeriod = atrPeriod, AtrFactor = atrFactor, AverageType = averageType, TrailType = trailType, ShowSignal = showSignal, SignalDispl = signalDispl }, input, ref cacheSvtAtrTrailingStop);
		}

	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		
		public Indicators.Svt.SvtAtrTrailingStop SvtAtrTrailingStop(int atrPeriod, double atrFactor, SvtRef.Models.AverageType averageType, SvtRef.Models.TrailType trailType, bool showSignal, double signalDispl)
		{
			return indicator.SvtAtrTrailingStop(Input, atrPeriod, atrFactor, averageType, trailType, showSignal, signalDispl);
		}


		
		public Indicators.Svt.SvtAtrTrailingStop SvtAtrTrailingStop(ISeries<double> input , int atrPeriod, double atrFactor, SvtRef.Models.AverageType averageType, SvtRef.Models.TrailType trailType, bool showSignal, double signalDispl)
		{
			return indicator.SvtAtrTrailingStop(input, atrPeriod, atrFactor, averageType, trailType, showSignal, signalDispl);
		}
	
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		
		public Indicators.Svt.SvtAtrTrailingStop SvtAtrTrailingStop(int atrPeriod, double atrFactor, SvtRef.Models.AverageType averageType, SvtRef.Models.TrailType trailType, bool showSignal, double signalDispl)
		{
			return indicator.SvtAtrTrailingStop(Input, atrPeriod, atrFactor, averageType, trailType, showSignal, signalDispl);
		}


		
		public Indicators.Svt.SvtAtrTrailingStop SvtAtrTrailingStop(ISeries<double> input , int atrPeriod, double atrFactor, SvtRef.Models.AverageType averageType, SvtRef.Models.TrailType trailType, bool showSignal, double signalDispl)
		{
			return indicator.SvtAtrTrailingStop(input, atrPeriod, atrFactor, averageType, trailType, showSignal, signalDispl);
		}

	}
}

#endregion
