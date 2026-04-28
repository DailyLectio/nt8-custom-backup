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
		
		private aiEnhancedChartTrader[] cacheaiEnhancedChartTrader;

		
		public aiEnhancedChartTrader aiEnhancedChartTrader(int triSize, string mTFBasePeriodType1, int mTFBarsPeriod1, int minZWidth, int minBlockTrade, bool useTimeFilter, string startT, string endT, string startT2, string endT2, string startT3, string endT3, bool useEST)
		{
			return aiEnhancedChartTrader(Input, triSize, mTFBasePeriodType1, mTFBarsPeriod1, minZWidth, minBlockTrade, useTimeFilter, startT, endT, startT2, endT2, startT3, endT3, useEST);
		}


		
		public aiEnhancedChartTrader aiEnhancedChartTrader(ISeries<double> input, int triSize, string mTFBasePeriodType1, int mTFBarsPeriod1, int minZWidth, int minBlockTrade, bool useTimeFilter, string startT, string endT, string startT2, string endT2, string startT3, string endT3, bool useEST)
		{
			if (cacheaiEnhancedChartTrader != null)
				for (int idx = 0; idx < cacheaiEnhancedChartTrader.Length; idx++)
					if (cacheaiEnhancedChartTrader[idx].TriSize == triSize && cacheaiEnhancedChartTrader[idx].MTFBasePeriodType1 == mTFBasePeriodType1 && cacheaiEnhancedChartTrader[idx].MTFBarsPeriod1 == mTFBarsPeriod1 && cacheaiEnhancedChartTrader[idx].MinZWidth == minZWidth && cacheaiEnhancedChartTrader[idx].MinBlockTrade == minBlockTrade && cacheaiEnhancedChartTrader[idx].UseTimeFilter == useTimeFilter && cacheaiEnhancedChartTrader[idx].StartT == startT && cacheaiEnhancedChartTrader[idx].EndT == endT && cacheaiEnhancedChartTrader[idx].StartT2 == startT2 && cacheaiEnhancedChartTrader[idx].EndT2 == endT2 && cacheaiEnhancedChartTrader[idx].StartT3 == startT3 && cacheaiEnhancedChartTrader[idx].EndT3 == endT3 && cacheaiEnhancedChartTrader[idx].UseEST == useEST && cacheaiEnhancedChartTrader[idx].EqualsInput(input))
						return cacheaiEnhancedChartTrader[idx];
			return CacheIndicator<aiEnhancedChartTrader>(new aiEnhancedChartTrader(){ TriSize = triSize, MTFBasePeriodType1 = mTFBasePeriodType1, MTFBarsPeriod1 = mTFBarsPeriod1, MinZWidth = minZWidth, MinBlockTrade = minBlockTrade, UseTimeFilter = useTimeFilter, StartT = startT, EndT = endT, StartT2 = startT2, EndT2 = endT2, StartT3 = startT3, EndT3 = endT3, UseEST = useEST }, input, ref cacheaiEnhancedChartTrader);
		}

	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		
		public Indicators.aiEnhancedChartTrader aiEnhancedChartTrader(int triSize, string mTFBasePeriodType1, int mTFBarsPeriod1, int minZWidth, int minBlockTrade, bool useTimeFilter, string startT, string endT, string startT2, string endT2, string startT3, string endT3, bool useEST)
		{
			return indicator.aiEnhancedChartTrader(Input, triSize, mTFBasePeriodType1, mTFBarsPeriod1, minZWidth, minBlockTrade, useTimeFilter, startT, endT, startT2, endT2, startT3, endT3, useEST);
		}


		
		public Indicators.aiEnhancedChartTrader aiEnhancedChartTrader(ISeries<double> input , int triSize, string mTFBasePeriodType1, int mTFBarsPeriod1, int minZWidth, int minBlockTrade, bool useTimeFilter, string startT, string endT, string startT2, string endT2, string startT3, string endT3, bool useEST)
		{
			return indicator.aiEnhancedChartTrader(input, triSize, mTFBasePeriodType1, mTFBarsPeriod1, minZWidth, minBlockTrade, useTimeFilter, startT, endT, startT2, endT2, startT3, endT3, useEST);
		}
	
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		
		public Indicators.aiEnhancedChartTrader aiEnhancedChartTrader(int triSize, string mTFBasePeriodType1, int mTFBarsPeriod1, int minZWidth, int minBlockTrade, bool useTimeFilter, string startT, string endT, string startT2, string endT2, string startT3, string endT3, bool useEST)
		{
			return indicator.aiEnhancedChartTrader(Input, triSize, mTFBasePeriodType1, mTFBarsPeriod1, minZWidth, minBlockTrade, useTimeFilter, startT, endT, startT2, endT2, startT3, endT3, useEST);
		}


		
		public Indicators.aiEnhancedChartTrader aiEnhancedChartTrader(ISeries<double> input , int triSize, string mTFBasePeriodType1, int mTFBarsPeriod1, int minZWidth, int minBlockTrade, bool useTimeFilter, string startT, string endT, string startT2, string endT2, string startT3, string endT3, bool useEST)
		{
			return indicator.aiEnhancedChartTrader(input, triSize, mTFBasePeriodType1, mTFBarsPeriod1, minZWidth, minBlockTrade, useTimeFilter, startT, endT, startT2, endT2, startT3, endT3, useEST);
		}

	}
}

#endregion
