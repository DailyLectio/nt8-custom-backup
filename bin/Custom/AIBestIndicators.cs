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
		
		private aiBestADX[] cacheaiBestADX;
		private aiBestATR[] cacheaiBestATR;
		private aiBestCCI[] cacheaiBestCCI;
		private aiBestMACD[] cacheaiBestMACD;
		private aiBestMAs[] cacheaiBestMAs;
		private aiBestMAsTrend[] cacheaiBestMAsTrend;
		private aiBestRSI[] cacheaiBestRSI;
		private aiBestStochastics[] cacheaiBestStochastics;

		
		public aiBestADX aiBestADX(int mainPeriod, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return aiBestADX(Input, mainPeriod, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public aiBestATR aiBestATR(bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1)
		{
			return aiBestATR(Input, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1);
		}

		public aiBestCCI aiBestCCI(int period, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return aiBestCCI(Input, period, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public aiBestMACD aiBestMACD(int fastEMA, int slowEMA, int smooth, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return aiBestMACD(Input, fastEMA, slowEMA, smooth, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public aiBestMAs aiBestMAs(bool mTFEnabled, string instrumentString, string mTFBasePeriodType1, int mTFBarsPeriod1, string thisMAType, int eMA1Fast)
		{
			return aiBestMAs(Input, mTFEnabled, instrumentString, mTFBasePeriodType1, mTFBarsPeriod1, thisMAType, eMA1Fast);
		}

		public aiBestMAsTrend aiBestMAsTrend(string mTFBasePeriodType1, int mTFBarsPeriod1, string thisMAType, int eMA1Fast, int eMA1Slow)
		{
			return aiBestMAsTrend(Input, mTFBasePeriodType1, mTFBarsPeriod1, thisMAType, eMA1Fast, eMA1Slow);
		}

		public aiBestRSI aiBestRSI(int period, int smooth, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return aiBestRSI(Input, period, smooth, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public aiBestStochastics aiBestStochastics(int periodDFast, int periodKFast, int periodD, int periodK, int smooth, string stochasticsType, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return aiBestStochastics(Input, periodDFast, periodKFast, periodD, periodK, smooth, stochasticsType, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}


		
		public aiBestADX aiBestADX(ISeries<double> input, int mainPeriod, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			if (cacheaiBestADX != null)
				for (int idx = 0; idx < cacheaiBestADX.Length; idx++)
					if (cacheaiBestADX[idx].MainPeriod == mainPeriod && cacheaiBestADX[idx].MTFEnabled == mTFEnabled && cacheaiBestADX[idx].MTFBasePeriodType1 == mTFBasePeriodType1 && cacheaiBestADX[idx].MTFBarsPeriod1 == mTFBarsPeriod1 && cacheaiBestADX[idx].SignalType == signalType && cacheaiBestADX[idx].ReverseEnabled == reverseEnabled && cacheaiBestADX[idx].EqualsInput(input))
						return cacheaiBestADX[idx];
			return CacheIndicator<aiBestADX>(new aiBestADX(){ MainPeriod = mainPeriod, MTFEnabled = mTFEnabled, MTFBasePeriodType1 = mTFBasePeriodType1, MTFBarsPeriod1 = mTFBarsPeriod1, SignalType = signalType, ReverseEnabled = reverseEnabled }, input, ref cacheaiBestADX);
		}

		public aiBestATR aiBestATR(ISeries<double> input, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1)
		{
			if (cacheaiBestATR != null)
				for (int idx = 0; idx < cacheaiBestATR.Length; idx++)
					if (cacheaiBestATR[idx].MTFEnabled == mTFEnabled && cacheaiBestATR[idx].MTFBasePeriodType1 == mTFBasePeriodType1 && cacheaiBestATR[idx].MTFBarsPeriod1 == mTFBarsPeriod1 && cacheaiBestATR[idx].EqualsInput(input))
						return cacheaiBestATR[idx];
			return CacheIndicator<aiBestATR>(new aiBestATR(){ MTFEnabled = mTFEnabled, MTFBasePeriodType1 = mTFBasePeriodType1, MTFBarsPeriod1 = mTFBarsPeriod1 }, input, ref cacheaiBestATR);
		}

		public aiBestCCI aiBestCCI(ISeries<double> input, int period, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			if (cacheaiBestCCI != null)
				for (int idx = 0; idx < cacheaiBestCCI.Length; idx++)
					if (cacheaiBestCCI[idx].Period == period && cacheaiBestCCI[idx].MTFEnabled == mTFEnabled && cacheaiBestCCI[idx].MTFBasePeriodType1 == mTFBasePeriodType1 && cacheaiBestCCI[idx].MTFBarsPeriod1 == mTFBarsPeriod1 && cacheaiBestCCI[idx].SignalType == signalType && cacheaiBestCCI[idx].ReverseEnabled == reverseEnabled && cacheaiBestCCI[idx].EqualsInput(input))
						return cacheaiBestCCI[idx];
			return CacheIndicator<aiBestCCI>(new aiBestCCI(){ Period = period, MTFEnabled = mTFEnabled, MTFBasePeriodType1 = mTFBasePeriodType1, MTFBarsPeriod1 = mTFBarsPeriod1, SignalType = signalType, ReverseEnabled = reverseEnabled }, input, ref cacheaiBestCCI);
		}

		public aiBestMACD aiBestMACD(ISeries<double> input, int fastEMA, int slowEMA, int smooth, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			if (cacheaiBestMACD != null)
				for (int idx = 0; idx < cacheaiBestMACD.Length; idx++)
					if (cacheaiBestMACD[idx].FastEMA == fastEMA && cacheaiBestMACD[idx].SlowEMA == slowEMA && cacheaiBestMACD[idx].Smooth == smooth && cacheaiBestMACD[idx].MTFEnabled == mTFEnabled && cacheaiBestMACD[idx].MTFBasePeriodType1 == mTFBasePeriodType1 && cacheaiBestMACD[idx].MTFBarsPeriod1 == mTFBarsPeriod1 && cacheaiBestMACD[idx].SignalType == signalType && cacheaiBestMACD[idx].ReverseEnabled == reverseEnabled && cacheaiBestMACD[idx].EqualsInput(input))
						return cacheaiBestMACD[idx];
			return CacheIndicator<aiBestMACD>(new aiBestMACD(){ FastEMA = fastEMA, SlowEMA = slowEMA, Smooth = smooth, MTFEnabled = mTFEnabled, MTFBasePeriodType1 = mTFBasePeriodType1, MTFBarsPeriod1 = mTFBarsPeriod1, SignalType = signalType, ReverseEnabled = reverseEnabled }, input, ref cacheaiBestMACD);
		}

		public aiBestMAs aiBestMAs(ISeries<double> input, bool mTFEnabled, string instrumentString, string mTFBasePeriodType1, int mTFBarsPeriod1, string thisMAType, int eMA1Fast)
		{
			if (cacheaiBestMAs != null)
				for (int idx = 0; idx < cacheaiBestMAs.Length; idx++)
					if (cacheaiBestMAs[idx].MTFEnabled == mTFEnabled && cacheaiBestMAs[idx].InstrumentString == instrumentString && cacheaiBestMAs[idx].MTFBasePeriodType1 == mTFBasePeriodType1 && cacheaiBestMAs[idx].MTFBarsPeriod1 == mTFBarsPeriod1 && cacheaiBestMAs[idx].ThisMAType == thisMAType && cacheaiBestMAs[idx].EMA1Fast == eMA1Fast && cacheaiBestMAs[idx].EqualsInput(input))
						return cacheaiBestMAs[idx];
			return CacheIndicator<aiBestMAs>(new aiBestMAs(){ MTFEnabled = mTFEnabled, InstrumentString = instrumentString, MTFBasePeriodType1 = mTFBasePeriodType1, MTFBarsPeriod1 = mTFBarsPeriod1, ThisMAType = thisMAType, EMA1Fast = eMA1Fast }, input, ref cacheaiBestMAs);
		}

		public aiBestMAsTrend aiBestMAsTrend(ISeries<double> input, string mTFBasePeriodType1, int mTFBarsPeriod1, string thisMAType, int eMA1Fast, int eMA1Slow)
		{
			if (cacheaiBestMAsTrend != null)
				for (int idx = 0; idx < cacheaiBestMAsTrend.Length; idx++)
					if (cacheaiBestMAsTrend[idx].MTFBasePeriodType1 == mTFBasePeriodType1 && cacheaiBestMAsTrend[idx].MTFBarsPeriod1 == mTFBarsPeriod1 && cacheaiBestMAsTrend[idx].ThisMAType == thisMAType && cacheaiBestMAsTrend[idx].EMA1Fast == eMA1Fast && cacheaiBestMAsTrend[idx].EMA1Slow == eMA1Slow && cacheaiBestMAsTrend[idx].EqualsInput(input))
						return cacheaiBestMAsTrend[idx];
			return CacheIndicator<aiBestMAsTrend>(new aiBestMAsTrend(){ MTFBasePeriodType1 = mTFBasePeriodType1, MTFBarsPeriod1 = mTFBarsPeriod1, ThisMAType = thisMAType, EMA1Fast = eMA1Fast, EMA1Slow = eMA1Slow }, input, ref cacheaiBestMAsTrend);
		}

		public aiBestRSI aiBestRSI(ISeries<double> input, int period, int smooth, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			if (cacheaiBestRSI != null)
				for (int idx = 0; idx < cacheaiBestRSI.Length; idx++)
					if (cacheaiBestRSI[idx].Period == period && cacheaiBestRSI[idx].Smooth == smooth && cacheaiBestRSI[idx].MTFEnabled == mTFEnabled && cacheaiBestRSI[idx].MTFBasePeriodType1 == mTFBasePeriodType1 && cacheaiBestRSI[idx].MTFBarsPeriod1 == mTFBarsPeriod1 && cacheaiBestRSI[idx].SignalType == signalType && cacheaiBestRSI[idx].ReverseEnabled == reverseEnabled && cacheaiBestRSI[idx].EqualsInput(input))
						return cacheaiBestRSI[idx];
			return CacheIndicator<aiBestRSI>(new aiBestRSI(){ Period = period, Smooth = smooth, MTFEnabled = mTFEnabled, MTFBasePeriodType1 = mTFBasePeriodType1, MTFBarsPeriod1 = mTFBarsPeriod1, SignalType = signalType, ReverseEnabled = reverseEnabled }, input, ref cacheaiBestRSI);
		}

		public aiBestStochastics aiBestStochastics(ISeries<double> input, int periodDFast, int periodKFast, int periodD, int periodK, int smooth, string stochasticsType, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			if (cacheaiBestStochastics != null)
				for (int idx = 0; idx < cacheaiBestStochastics.Length; idx++)
					if (cacheaiBestStochastics[idx].PeriodDFast == periodDFast && cacheaiBestStochastics[idx].PeriodKFast == periodKFast && cacheaiBestStochastics[idx].PeriodD == periodD && cacheaiBestStochastics[idx].PeriodK == periodK && cacheaiBestStochastics[idx].Smooth == smooth && cacheaiBestStochastics[idx].StochasticsType == stochasticsType && cacheaiBestStochastics[idx].MTFEnabled == mTFEnabled && cacheaiBestStochastics[idx].MTFBasePeriodType1 == mTFBasePeriodType1 && cacheaiBestStochastics[idx].MTFBarsPeriod1 == mTFBarsPeriod1 && cacheaiBestStochastics[idx].SignalType == signalType && cacheaiBestStochastics[idx].ReverseEnabled == reverseEnabled && cacheaiBestStochastics[idx].EqualsInput(input))
						return cacheaiBestStochastics[idx];
			return CacheIndicator<aiBestStochastics>(new aiBestStochastics(){ PeriodDFast = periodDFast, PeriodKFast = periodKFast, PeriodD = periodD, PeriodK = periodK, Smooth = smooth, StochasticsType = stochasticsType, MTFEnabled = mTFEnabled, MTFBasePeriodType1 = mTFBasePeriodType1, MTFBarsPeriod1 = mTFBarsPeriod1, SignalType = signalType, ReverseEnabled = reverseEnabled }, input, ref cacheaiBestStochastics);
		}

	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		
		public Indicators.aiBestADX aiBestADX(int mainPeriod, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestADX(Input, mainPeriod, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestATR aiBestATR(bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1)
		{
			return indicator.aiBestATR(Input, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1);
		}

		public Indicators.aiBestCCI aiBestCCI(int period, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestCCI(Input, period, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestMACD aiBestMACD(int fastEMA, int slowEMA, int smooth, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestMACD(Input, fastEMA, slowEMA, smooth, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestMAs aiBestMAs(bool mTFEnabled, string instrumentString, string mTFBasePeriodType1, int mTFBarsPeriod1, string thisMAType, int eMA1Fast)
		{
			return indicator.aiBestMAs(Input, mTFEnabled, instrumentString, mTFBasePeriodType1, mTFBarsPeriod1, thisMAType, eMA1Fast);
		}

		public Indicators.aiBestMAsTrend aiBestMAsTrend(string mTFBasePeriodType1, int mTFBarsPeriod1, string thisMAType, int eMA1Fast, int eMA1Slow)
		{
			return indicator.aiBestMAsTrend(Input, mTFBasePeriodType1, mTFBarsPeriod1, thisMAType, eMA1Fast, eMA1Slow);
		}

		public Indicators.aiBestRSI aiBestRSI(int period, int smooth, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestRSI(Input, period, smooth, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestStochastics aiBestStochastics(int periodDFast, int periodKFast, int periodD, int periodK, int smooth, string stochasticsType, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestStochastics(Input, periodDFast, periodKFast, periodD, periodK, smooth, stochasticsType, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}


		
		public Indicators.aiBestADX aiBestADX(ISeries<double> input , int mainPeriod, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestADX(input, mainPeriod, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestATR aiBestATR(ISeries<double> input , bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1)
		{
			return indicator.aiBestATR(input, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1);
		}

		public Indicators.aiBestCCI aiBestCCI(ISeries<double> input , int period, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestCCI(input, period, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestMACD aiBestMACD(ISeries<double> input , int fastEMA, int slowEMA, int smooth, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestMACD(input, fastEMA, slowEMA, smooth, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestMAs aiBestMAs(ISeries<double> input , bool mTFEnabled, string instrumentString, string mTFBasePeriodType1, int mTFBarsPeriod1, string thisMAType, int eMA1Fast)
		{
			return indicator.aiBestMAs(input, mTFEnabled, instrumentString, mTFBasePeriodType1, mTFBarsPeriod1, thisMAType, eMA1Fast);
		}

		public Indicators.aiBestMAsTrend aiBestMAsTrend(ISeries<double> input , string mTFBasePeriodType1, int mTFBarsPeriod1, string thisMAType, int eMA1Fast, int eMA1Slow)
		{
			return indicator.aiBestMAsTrend(input, mTFBasePeriodType1, mTFBarsPeriod1, thisMAType, eMA1Fast, eMA1Slow);
		}

		public Indicators.aiBestRSI aiBestRSI(ISeries<double> input , int period, int smooth, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestRSI(input, period, smooth, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestStochastics aiBestStochastics(ISeries<double> input , int periodDFast, int periodKFast, int periodD, int periodK, int smooth, string stochasticsType, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestStochastics(input, periodDFast, periodKFast, periodD, periodK, smooth, stochasticsType, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}
	
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		
		public Indicators.aiBestADX aiBestADX(int mainPeriod, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestADX(Input, mainPeriod, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestATR aiBestATR(bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1)
		{
			return indicator.aiBestATR(Input, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1);
		}

		public Indicators.aiBestCCI aiBestCCI(int period, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestCCI(Input, period, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestMACD aiBestMACD(int fastEMA, int slowEMA, int smooth, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestMACD(Input, fastEMA, slowEMA, smooth, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestMAs aiBestMAs(bool mTFEnabled, string instrumentString, string mTFBasePeriodType1, int mTFBarsPeriod1, string thisMAType, int eMA1Fast)
		{
			return indicator.aiBestMAs(Input, mTFEnabled, instrumentString, mTFBasePeriodType1, mTFBarsPeriod1, thisMAType, eMA1Fast);
		}

		public Indicators.aiBestMAsTrend aiBestMAsTrend(string mTFBasePeriodType1, int mTFBarsPeriod1, string thisMAType, int eMA1Fast, int eMA1Slow)
		{
			return indicator.aiBestMAsTrend(Input, mTFBasePeriodType1, mTFBarsPeriod1, thisMAType, eMA1Fast, eMA1Slow);
		}

		public Indicators.aiBestRSI aiBestRSI(int period, int smooth, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestRSI(Input, period, smooth, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestStochastics aiBestStochastics(int periodDFast, int periodKFast, int periodD, int periodK, int smooth, string stochasticsType, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestStochastics(Input, periodDFast, periodKFast, periodD, periodK, smooth, stochasticsType, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}


		
		public Indicators.aiBestADX aiBestADX(ISeries<double> input , int mainPeriod, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestADX(input, mainPeriod, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestATR aiBestATR(ISeries<double> input , bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1)
		{
			return indicator.aiBestATR(input, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1);
		}

		public Indicators.aiBestCCI aiBestCCI(ISeries<double> input , int period, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestCCI(input, period, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestMACD aiBestMACD(ISeries<double> input , int fastEMA, int slowEMA, int smooth, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestMACD(input, fastEMA, slowEMA, smooth, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestMAs aiBestMAs(ISeries<double> input , bool mTFEnabled, string instrumentString, string mTFBasePeriodType1, int mTFBarsPeriod1, string thisMAType, int eMA1Fast)
		{
			return indicator.aiBestMAs(input, mTFEnabled, instrumentString, mTFBasePeriodType1, mTFBarsPeriod1, thisMAType, eMA1Fast);
		}

		public Indicators.aiBestMAsTrend aiBestMAsTrend(ISeries<double> input , string mTFBasePeriodType1, int mTFBarsPeriod1, string thisMAType, int eMA1Fast, int eMA1Slow)
		{
			return indicator.aiBestMAsTrend(input, mTFBasePeriodType1, mTFBarsPeriod1, thisMAType, eMA1Fast, eMA1Slow);
		}

		public Indicators.aiBestRSI aiBestRSI(ISeries<double> input , int period, int smooth, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestRSI(input, period, smooth, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

		public Indicators.aiBestStochastics aiBestStochastics(ISeries<double> input , int periodDFast, int periodKFast, int periodD, int periodK, int smooth, string stochasticsType, bool mTFEnabled, string mTFBasePeriodType1, int mTFBarsPeriod1, string signalType, bool reverseEnabled)
		{
			return indicator.aiBestStochastics(input, periodDFast, periodKFast, periodD, periodK, smooth, stochasticsType, mTFEnabled, mTFBasePeriodType1, mTFBarsPeriod1, signalType, reverseEnabled);
		}

	}
}

#endregion
