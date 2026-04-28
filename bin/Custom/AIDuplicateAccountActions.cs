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
		
		private aiDuplicateAccountActions[] cacheaiDuplicateAccountActions;

		
		public aiDuplicateAccountActions aiDuplicateAccountActions(double rithmic1Seconds, double rithmic2Seconds)
		{
			return aiDuplicateAccountActions(Input, rithmic1Seconds, rithmic2Seconds);
		}


		
		public aiDuplicateAccountActions aiDuplicateAccountActions(ISeries<double> input, double rithmic1Seconds, double rithmic2Seconds)
		{
			if (cacheaiDuplicateAccountActions != null)
				for (int idx = 0; idx < cacheaiDuplicateAccountActions.Length; idx++)
					if (cacheaiDuplicateAccountActions[idx].Rithmic1Seconds == rithmic1Seconds && cacheaiDuplicateAccountActions[idx].Rithmic2Seconds == rithmic2Seconds && cacheaiDuplicateAccountActions[idx].EqualsInput(input))
						return cacheaiDuplicateAccountActions[idx];
			return CacheIndicator<aiDuplicateAccountActions>(new aiDuplicateAccountActions(){ Rithmic1Seconds = rithmic1Seconds, Rithmic2Seconds = rithmic2Seconds }, input, ref cacheaiDuplicateAccountActions);
		}

	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		
		public Indicators.aiDuplicateAccountActions aiDuplicateAccountActions(double rithmic1Seconds, double rithmic2Seconds)
		{
			return indicator.aiDuplicateAccountActions(Input, rithmic1Seconds, rithmic2Seconds);
		}


		
		public Indicators.aiDuplicateAccountActions aiDuplicateAccountActions(ISeries<double> input , double rithmic1Seconds, double rithmic2Seconds)
		{
			return indicator.aiDuplicateAccountActions(input, rithmic1Seconds, rithmic2Seconds);
		}
	
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		
		public Indicators.aiDuplicateAccountActions aiDuplicateAccountActions(double rithmic1Seconds, double rithmic2Seconds)
		{
			return indicator.aiDuplicateAccountActions(Input, rithmic1Seconds, rithmic2Seconds);
		}


		
		public Indicators.aiDuplicateAccountActions aiDuplicateAccountActions(ISeries<double> input , double rithmic1Seconds, double rithmic2Seconds)
		{
			return indicator.aiDuplicateAccountActions(input, rithmic1Seconds, rithmic2Seconds);
		}

	}
}

#endregion
