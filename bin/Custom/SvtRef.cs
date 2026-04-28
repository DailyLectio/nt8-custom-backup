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
		
		private Svt.SvtAema[] cacheSvtAema;
		private Svt.SvtAlmaTrue[] cacheSvtAlmaTrue;
		private Svt.SvtAma[] cacheSvtAma;
		private Svt.SvtDailyVwap[] cacheSvtDailyVwap;
		private Svt.SvtFrama[] cacheSvtFrama;
		private Svt.SvtKama[] cacheSvtKama;
		private Svt.SvtWilderMa[] cacheSvtWilderMa;

		
		public Svt.SvtAema SvtAema(SvtRef.Models.InputPrice inputDataSource, int period, int highLowPeriod)
		{
			return SvtAema(Input, inputDataSource, period, highLowPeriod);
		}

		public Svt.SvtAlmaTrue SvtAlmaTrue(SvtRef.Models.InputPrice dataInputSource, int window, double sigma, double offset)
		{
			return SvtAlmaTrue(Input, dataInputSource, window, sigma, offset);
		}

		public Svt.SvtAma SvtAma(SvtRef.Models.InputPrice inputDataSource, int fastPeriod, int slowPeriod, int effectiveRatioLength)
		{
			return SvtAma(Input, inputDataSource, fastPeriod, slowPeriod, effectiveRatioLength);
		}

		public Svt.SvtDailyVwap SvtDailyVwap(bool isRollingVwap, bool isEnableStdOne, double stdDevOneValue, bool isEnableStdTwo, double stdDevTwoValue, bool isEnableStdThree, double stdDevThreeValue)
		{
			return SvtDailyVwap(Input, isRollingVwap, isEnableStdOne, stdDevOneValue, isEnableStdTwo, stdDevTwoValue, isEnableStdThree, stdDevThreeValue);
		}

		public Svt.SvtFrama SvtFrama(SvtRef.Models.InputPrice inputPriceType, int period, int fC, int sC)
		{
			return SvtFrama(Input, inputPriceType, period, fC, sC);
		}

		public Svt.SvtKama SvtKama(SvtRef.Models.InputPrice dataInputSource, int period, int fast, int slow)
		{
			return SvtKama(Input, dataInputSource, period, fast, slow);
		}

		public Svt.SvtWilderMa SvtWilderMa(int period, SvtRef.Models.InputPrice inputDataSource)
		{
			return SvtWilderMa(Input, period, inputDataSource);
		}


		
		public Svt.SvtAema SvtAema(ISeries<double> input, SvtRef.Models.InputPrice inputDataSource, int period, int highLowPeriod)
		{
			if (cacheSvtAema != null)
				for (int idx = 0; idx < cacheSvtAema.Length; idx++)
					if (cacheSvtAema[idx].InputDataSource == inputDataSource && cacheSvtAema[idx].Period == period && cacheSvtAema[idx].HighLowPeriod == highLowPeriod && cacheSvtAema[idx].EqualsInput(input))
						return cacheSvtAema[idx];
			return CacheIndicator<Svt.SvtAema>(new Svt.SvtAema(){ InputDataSource = inputDataSource, Period = period, HighLowPeriod = highLowPeriod }, input, ref cacheSvtAema);
		}

		public Svt.SvtAlmaTrue SvtAlmaTrue(ISeries<double> input, SvtRef.Models.InputPrice dataInputSource, int window, double sigma, double offset)
		{
			if (cacheSvtAlmaTrue != null)
				for (int idx = 0; idx < cacheSvtAlmaTrue.Length; idx++)
					if (cacheSvtAlmaTrue[idx].DataInputSource == dataInputSource && cacheSvtAlmaTrue[idx].Window == window && cacheSvtAlmaTrue[idx].Sigma == sigma && cacheSvtAlmaTrue[idx].Offset == offset && cacheSvtAlmaTrue[idx].EqualsInput(input))
						return cacheSvtAlmaTrue[idx];
			return CacheIndicator<Svt.SvtAlmaTrue>(new Svt.SvtAlmaTrue(){ DataInputSource = dataInputSource, Window = window, Sigma = sigma, Offset = offset }, input, ref cacheSvtAlmaTrue);
		}

		public Svt.SvtAma SvtAma(ISeries<double> input, SvtRef.Models.InputPrice inputDataSource, int fastPeriod, int slowPeriod, int effectiveRatioLength)
		{
			if (cacheSvtAma != null)
				for (int idx = 0; idx < cacheSvtAma.Length; idx++)
					if (cacheSvtAma[idx].InputDataSource == inputDataSource && cacheSvtAma[idx].FastPeriod == fastPeriod && cacheSvtAma[idx].SlowPeriod == slowPeriod && cacheSvtAma[idx].EffectiveRatioLength == effectiveRatioLength && cacheSvtAma[idx].EqualsInput(input))
						return cacheSvtAma[idx];
			return CacheIndicator<Svt.SvtAma>(new Svt.SvtAma(){ InputDataSource = inputDataSource, FastPeriod = fastPeriod, SlowPeriod = slowPeriod, EffectiveRatioLength = effectiveRatioLength }, input, ref cacheSvtAma);
		}

		public Svt.SvtDailyVwap SvtDailyVwap(ISeries<double> input, bool isRollingVwap, bool isEnableStdOne, double stdDevOneValue, bool isEnableStdTwo, double stdDevTwoValue, bool isEnableStdThree, double stdDevThreeValue)
		{
			if (cacheSvtDailyVwap != null)
				for (int idx = 0; idx < cacheSvtDailyVwap.Length; idx++)
					if (cacheSvtDailyVwap[idx].IsRollingVwap == isRollingVwap && cacheSvtDailyVwap[idx].IsEnableStdOne == isEnableStdOne && cacheSvtDailyVwap[idx].StdDevOneValue == stdDevOneValue && cacheSvtDailyVwap[idx].IsEnableStdTwo == isEnableStdTwo && cacheSvtDailyVwap[idx].StdDevTwoValue == stdDevTwoValue && cacheSvtDailyVwap[idx].IsEnableStdThree == isEnableStdThree && cacheSvtDailyVwap[idx].StdDevThreeValue == stdDevThreeValue && cacheSvtDailyVwap[idx].EqualsInput(input))
						return cacheSvtDailyVwap[idx];
			return CacheIndicator<Svt.SvtDailyVwap>(new Svt.SvtDailyVwap(){ IsRollingVwap = isRollingVwap, IsEnableStdOne = isEnableStdOne, StdDevOneValue = stdDevOneValue, IsEnableStdTwo = isEnableStdTwo, StdDevTwoValue = stdDevTwoValue, IsEnableStdThree = isEnableStdThree, StdDevThreeValue = stdDevThreeValue }, input, ref cacheSvtDailyVwap);
		}

		public Svt.SvtFrama SvtFrama(ISeries<double> input, SvtRef.Models.InputPrice inputPriceType, int period, int fC, int sC)
		{
			if (cacheSvtFrama != null)
				for (int idx = 0; idx < cacheSvtFrama.Length; idx++)
					if (cacheSvtFrama[idx].InputPriceType == inputPriceType && cacheSvtFrama[idx].Period == period && cacheSvtFrama[idx].FC == fC && cacheSvtFrama[idx].SC == sC && cacheSvtFrama[idx].EqualsInput(input))
						return cacheSvtFrama[idx];
			return CacheIndicator<Svt.SvtFrama>(new Svt.SvtFrama(){ InputPriceType = inputPriceType, Period = period, FC = fC, SC = sC }, input, ref cacheSvtFrama);
		}

		public Svt.SvtKama SvtKama(ISeries<double> input, SvtRef.Models.InputPrice dataInputSource, int period, int fast, int slow)
		{
			if (cacheSvtKama != null)
				for (int idx = 0; idx < cacheSvtKama.Length; idx++)
					if (cacheSvtKama[idx].DataInputSource == dataInputSource && cacheSvtKama[idx].Period == period && cacheSvtKama[idx].Fast == fast && cacheSvtKama[idx].Slow == slow && cacheSvtKama[idx].EqualsInput(input))
						return cacheSvtKama[idx];
			return CacheIndicator<Svt.SvtKama>(new Svt.SvtKama(){ DataInputSource = dataInputSource, Period = period, Fast = fast, Slow = slow }, input, ref cacheSvtKama);
		}

		public Svt.SvtWilderMa SvtWilderMa(ISeries<double> input, int period, SvtRef.Models.InputPrice inputDataSource)
		{
			if (cacheSvtWilderMa != null)
				for (int idx = 0; idx < cacheSvtWilderMa.Length; idx++)
					if (cacheSvtWilderMa[idx].Period == period && cacheSvtWilderMa[idx].InputDataSource == inputDataSource && cacheSvtWilderMa[idx].EqualsInput(input))
						return cacheSvtWilderMa[idx];
			return CacheIndicator<Svt.SvtWilderMa>(new Svt.SvtWilderMa(){ Period = period, InputDataSource = inputDataSource }, input, ref cacheSvtWilderMa);
		}

	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		
		public Indicators.Svt.SvtAema SvtAema(SvtRef.Models.InputPrice inputDataSource, int period, int highLowPeriod)
		{
			return indicator.SvtAema(Input, inputDataSource, period, highLowPeriod);
		}

		public Indicators.Svt.SvtAlmaTrue SvtAlmaTrue(SvtRef.Models.InputPrice dataInputSource, int window, double sigma, double offset)
		{
			return indicator.SvtAlmaTrue(Input, dataInputSource, window, sigma, offset);
		}

		public Indicators.Svt.SvtAma SvtAma(SvtRef.Models.InputPrice inputDataSource, int fastPeriod, int slowPeriod, int effectiveRatioLength)
		{
			return indicator.SvtAma(Input, inputDataSource, fastPeriod, slowPeriod, effectiveRatioLength);
		}

		public Indicators.Svt.SvtDailyVwap SvtDailyVwap(bool isRollingVwap, bool isEnableStdOne, double stdDevOneValue, bool isEnableStdTwo, double stdDevTwoValue, bool isEnableStdThree, double stdDevThreeValue)
		{
			return indicator.SvtDailyVwap(Input, isRollingVwap, isEnableStdOne, stdDevOneValue, isEnableStdTwo, stdDevTwoValue, isEnableStdThree, stdDevThreeValue);
		}

		public Indicators.Svt.SvtFrama SvtFrama(SvtRef.Models.InputPrice inputPriceType, int period, int fC, int sC)
		{
			return indicator.SvtFrama(Input, inputPriceType, period, fC, sC);
		}

		public Indicators.Svt.SvtKama SvtKama(SvtRef.Models.InputPrice dataInputSource, int period, int fast, int slow)
		{
			return indicator.SvtKama(Input, dataInputSource, period, fast, slow);
		}

		public Indicators.Svt.SvtWilderMa SvtWilderMa(int period, SvtRef.Models.InputPrice inputDataSource)
		{
			return indicator.SvtWilderMa(Input, period, inputDataSource);
		}


		
		public Indicators.Svt.SvtAema SvtAema(ISeries<double> input , SvtRef.Models.InputPrice inputDataSource, int period, int highLowPeriod)
		{
			return indicator.SvtAema(input, inputDataSource, period, highLowPeriod);
		}

		public Indicators.Svt.SvtAlmaTrue SvtAlmaTrue(ISeries<double> input , SvtRef.Models.InputPrice dataInputSource, int window, double sigma, double offset)
		{
			return indicator.SvtAlmaTrue(input, dataInputSource, window, sigma, offset);
		}

		public Indicators.Svt.SvtAma SvtAma(ISeries<double> input , SvtRef.Models.InputPrice inputDataSource, int fastPeriod, int slowPeriod, int effectiveRatioLength)
		{
			return indicator.SvtAma(input, inputDataSource, fastPeriod, slowPeriod, effectiveRatioLength);
		}

		public Indicators.Svt.SvtDailyVwap SvtDailyVwap(ISeries<double> input , bool isRollingVwap, bool isEnableStdOne, double stdDevOneValue, bool isEnableStdTwo, double stdDevTwoValue, bool isEnableStdThree, double stdDevThreeValue)
		{
			return indicator.SvtDailyVwap(input, isRollingVwap, isEnableStdOne, stdDevOneValue, isEnableStdTwo, stdDevTwoValue, isEnableStdThree, stdDevThreeValue);
		}

		public Indicators.Svt.SvtFrama SvtFrama(ISeries<double> input , SvtRef.Models.InputPrice inputPriceType, int period, int fC, int sC)
		{
			return indicator.SvtFrama(input, inputPriceType, period, fC, sC);
		}

		public Indicators.Svt.SvtKama SvtKama(ISeries<double> input , SvtRef.Models.InputPrice dataInputSource, int period, int fast, int slow)
		{
			return indicator.SvtKama(input, dataInputSource, period, fast, slow);
		}

		public Indicators.Svt.SvtWilderMa SvtWilderMa(ISeries<double> input , int period, SvtRef.Models.InputPrice inputDataSource)
		{
			return indicator.SvtWilderMa(input, period, inputDataSource);
		}
	
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		
		public Indicators.Svt.SvtAema SvtAema(SvtRef.Models.InputPrice inputDataSource, int period, int highLowPeriod)
		{
			return indicator.SvtAema(Input, inputDataSource, period, highLowPeriod);
		}

		public Indicators.Svt.SvtAlmaTrue SvtAlmaTrue(SvtRef.Models.InputPrice dataInputSource, int window, double sigma, double offset)
		{
			return indicator.SvtAlmaTrue(Input, dataInputSource, window, sigma, offset);
		}

		public Indicators.Svt.SvtAma SvtAma(SvtRef.Models.InputPrice inputDataSource, int fastPeriod, int slowPeriod, int effectiveRatioLength)
		{
			return indicator.SvtAma(Input, inputDataSource, fastPeriod, slowPeriod, effectiveRatioLength);
		}

		public Indicators.Svt.SvtDailyVwap SvtDailyVwap(bool isRollingVwap, bool isEnableStdOne, double stdDevOneValue, bool isEnableStdTwo, double stdDevTwoValue, bool isEnableStdThree, double stdDevThreeValue)
		{
			return indicator.SvtDailyVwap(Input, isRollingVwap, isEnableStdOne, stdDevOneValue, isEnableStdTwo, stdDevTwoValue, isEnableStdThree, stdDevThreeValue);
		}

		public Indicators.Svt.SvtFrama SvtFrama(SvtRef.Models.InputPrice inputPriceType, int period, int fC, int sC)
		{
			return indicator.SvtFrama(Input, inputPriceType, period, fC, sC);
		}

		public Indicators.Svt.SvtKama SvtKama(SvtRef.Models.InputPrice dataInputSource, int period, int fast, int slow)
		{
			return indicator.SvtKama(Input, dataInputSource, period, fast, slow);
		}

		public Indicators.Svt.SvtWilderMa SvtWilderMa(int period, SvtRef.Models.InputPrice inputDataSource)
		{
			return indicator.SvtWilderMa(Input, period, inputDataSource);
		}


		
		public Indicators.Svt.SvtAema SvtAema(ISeries<double> input , SvtRef.Models.InputPrice inputDataSource, int period, int highLowPeriod)
		{
			return indicator.SvtAema(input, inputDataSource, period, highLowPeriod);
		}

		public Indicators.Svt.SvtAlmaTrue SvtAlmaTrue(ISeries<double> input , SvtRef.Models.InputPrice dataInputSource, int window, double sigma, double offset)
		{
			return indicator.SvtAlmaTrue(input, dataInputSource, window, sigma, offset);
		}

		public Indicators.Svt.SvtAma SvtAma(ISeries<double> input , SvtRef.Models.InputPrice inputDataSource, int fastPeriod, int slowPeriod, int effectiveRatioLength)
		{
			return indicator.SvtAma(input, inputDataSource, fastPeriod, slowPeriod, effectiveRatioLength);
		}

		public Indicators.Svt.SvtDailyVwap SvtDailyVwap(ISeries<double> input , bool isRollingVwap, bool isEnableStdOne, double stdDevOneValue, bool isEnableStdTwo, double stdDevTwoValue, bool isEnableStdThree, double stdDevThreeValue)
		{
			return indicator.SvtDailyVwap(input, isRollingVwap, isEnableStdOne, stdDevOneValue, isEnableStdTwo, stdDevTwoValue, isEnableStdThree, stdDevThreeValue);
		}

		public Indicators.Svt.SvtFrama SvtFrama(ISeries<double> input , SvtRef.Models.InputPrice inputPriceType, int period, int fC, int sC)
		{
			return indicator.SvtFrama(input, inputPriceType, period, fC, sC);
		}

		public Indicators.Svt.SvtKama SvtKama(ISeries<double> input , SvtRef.Models.InputPrice dataInputSource, int period, int fast, int slow)
		{
			return indicator.SvtKama(input, dataInputSource, period, fast, slow);
		}

		public Indicators.Svt.SvtWilderMa SvtWilderMa(ISeries<double> input , int period, SvtRef.Models.InputPrice inputDataSource)
		{
			return indicator.SvtWilderMa(input, period, inputDataSource);
		}

	}
}

#endregion
