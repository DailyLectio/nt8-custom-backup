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
		
		private T3000.MGI.T3000_LagDetector[] cacheT3000_LagDetector;
		private T3000.MGI.T3000_MGI_Daily[] cacheT3000_MGI_Daily;
		private T3000.MGI.T3000_MGI_Monthly[] cacheT3000_MGI_Monthly;
		private T3000.MGI.T3000_MGI_Statistics[] cacheT3000_MGI_Statistics;
		private T3000.MGI.T3000_MGI_Weekly[] cacheT3000_MGI_Weekly;

		
		public T3000.MGI.T3000_LagDetector T3000_LagDetector()
		{
			return T3000_LagDetector(Input);
		}

		public T3000.MGI.T3000_MGI_Daily T3000_MGI_Daily()
		{
			return T3000_MGI_Daily(Input);
		}

		public T3000.MGI.T3000_MGI_Monthly T3000_MGI_Monthly()
		{
			return T3000_MGI_Monthly(Input);
		}

		public T3000.MGI.T3000_MGI_Statistics T3000_MGI_Statistics()
		{
			return T3000_MGI_Statistics(Input);
		}

		public T3000.MGI.T3000_MGI_Weekly T3000_MGI_Weekly()
		{
			return T3000_MGI_Weekly(Input);
		}


		
		public T3000.MGI.T3000_LagDetector T3000_LagDetector(ISeries<double> input)
		{
			if (cacheT3000_LagDetector != null)
				for (int idx = 0; idx < cacheT3000_LagDetector.Length; idx++)
					if ( cacheT3000_LagDetector[idx].EqualsInput(input))
						return cacheT3000_LagDetector[idx];
			return CacheIndicator<T3000.MGI.T3000_LagDetector>(new T3000.MGI.T3000_LagDetector(), input, ref cacheT3000_LagDetector);
		}

		public T3000.MGI.T3000_MGI_Daily T3000_MGI_Daily(ISeries<double> input)
		{
			if (cacheT3000_MGI_Daily != null)
				for (int idx = 0; idx < cacheT3000_MGI_Daily.Length; idx++)
					if ( cacheT3000_MGI_Daily[idx].EqualsInput(input))
						return cacheT3000_MGI_Daily[idx];
			return CacheIndicator<T3000.MGI.T3000_MGI_Daily>(new T3000.MGI.T3000_MGI_Daily(), input, ref cacheT3000_MGI_Daily);
		}

		public T3000.MGI.T3000_MGI_Monthly T3000_MGI_Monthly(ISeries<double> input)
		{
			if (cacheT3000_MGI_Monthly != null)
				for (int idx = 0; idx < cacheT3000_MGI_Monthly.Length; idx++)
					if ( cacheT3000_MGI_Monthly[idx].EqualsInput(input))
						return cacheT3000_MGI_Monthly[idx];
			return CacheIndicator<T3000.MGI.T3000_MGI_Monthly>(new T3000.MGI.T3000_MGI_Monthly(), input, ref cacheT3000_MGI_Monthly);
		}

		public T3000.MGI.T3000_MGI_Statistics T3000_MGI_Statistics(ISeries<double> input)
		{
			if (cacheT3000_MGI_Statistics != null)
				for (int idx = 0; idx < cacheT3000_MGI_Statistics.Length; idx++)
					if ( cacheT3000_MGI_Statistics[idx].EqualsInput(input))
						return cacheT3000_MGI_Statistics[idx];
			return CacheIndicator<T3000.MGI.T3000_MGI_Statistics>(new T3000.MGI.T3000_MGI_Statistics(), input, ref cacheT3000_MGI_Statistics);
		}

		public T3000.MGI.T3000_MGI_Weekly T3000_MGI_Weekly(ISeries<double> input)
		{
			if (cacheT3000_MGI_Weekly != null)
				for (int idx = 0; idx < cacheT3000_MGI_Weekly.Length; idx++)
					if ( cacheT3000_MGI_Weekly[idx].EqualsInput(input))
						return cacheT3000_MGI_Weekly[idx];
			return CacheIndicator<T3000.MGI.T3000_MGI_Weekly>(new T3000.MGI.T3000_MGI_Weekly(), input, ref cacheT3000_MGI_Weekly);
		}

	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		
		public Indicators.T3000.MGI.T3000_LagDetector T3000_LagDetector()
		{
			return indicator.T3000_LagDetector(Input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Daily T3000_MGI_Daily()
		{
			return indicator.T3000_MGI_Daily(Input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Monthly T3000_MGI_Monthly()
		{
			return indicator.T3000_MGI_Monthly(Input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Statistics T3000_MGI_Statistics()
		{
			return indicator.T3000_MGI_Statistics(Input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Weekly T3000_MGI_Weekly()
		{
			return indicator.T3000_MGI_Weekly(Input);
		}


		
		public Indicators.T3000.MGI.T3000_LagDetector T3000_LagDetector(ISeries<double> input )
		{
			return indicator.T3000_LagDetector(input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Daily T3000_MGI_Daily(ISeries<double> input )
		{
			return indicator.T3000_MGI_Daily(input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Monthly T3000_MGI_Monthly(ISeries<double> input )
		{
			return indicator.T3000_MGI_Monthly(input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Statistics T3000_MGI_Statistics(ISeries<double> input )
		{
			return indicator.T3000_MGI_Statistics(input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Weekly T3000_MGI_Weekly(ISeries<double> input )
		{
			return indicator.T3000_MGI_Weekly(input);
		}
	
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		
		public Indicators.T3000.MGI.T3000_LagDetector T3000_LagDetector()
		{
			return indicator.T3000_LagDetector(Input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Daily T3000_MGI_Daily()
		{
			return indicator.T3000_MGI_Daily(Input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Monthly T3000_MGI_Monthly()
		{
			return indicator.T3000_MGI_Monthly(Input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Statistics T3000_MGI_Statistics()
		{
			return indicator.T3000_MGI_Statistics(Input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Weekly T3000_MGI_Weekly()
		{
			return indicator.T3000_MGI_Weekly(Input);
		}


		
		public Indicators.T3000.MGI.T3000_LagDetector T3000_LagDetector(ISeries<double> input )
		{
			return indicator.T3000_LagDetector(input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Daily T3000_MGI_Daily(ISeries<double> input )
		{
			return indicator.T3000_MGI_Daily(input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Monthly T3000_MGI_Monthly(ISeries<double> input )
		{
			return indicator.T3000_MGI_Monthly(input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Statistics T3000_MGI_Statistics(ISeries<double> input )
		{
			return indicator.T3000_MGI_Statistics(input);
		}

		public Indicators.T3000.MGI.T3000_MGI_Weekly T3000_MGI_Weekly(ISeries<double> input )
		{
			return indicator.T3000_MGI_Weekly(input);
		}

	}
}

#endregion
