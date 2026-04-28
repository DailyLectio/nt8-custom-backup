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
		
		private Svt.SvtChoppinessIndex[] cacheSvtChoppinessIndex;

		
		public Svt.SvtChoppinessIndex SvtChoppinessIndex(int fractalEnergyPeriod, bool smoothCurve, double choppinessLevel, double trendingLevel, double middleLevel, bool showZeroLevel, double zeroLevel, bool showSignalCurve, int signalPeriod, SvtRef.Models.AverageType signalMovingAverage, bool showCurveColor, bool applyShade, int shadeOpacity)
		{
			return SvtChoppinessIndex(Input, fractalEnergyPeriod, smoothCurve, choppinessLevel, trendingLevel, middleLevel, showZeroLevel, zeroLevel, showSignalCurve, signalPeriod, signalMovingAverage, showCurveColor, applyShade, shadeOpacity);
		}


		
		public Svt.SvtChoppinessIndex SvtChoppinessIndex(ISeries<double> input, int fractalEnergyPeriod, bool smoothCurve, double choppinessLevel, double trendingLevel, double middleLevel, bool showZeroLevel, double zeroLevel, bool showSignalCurve, int signalPeriod, SvtRef.Models.AverageType signalMovingAverage, bool showCurveColor, bool applyShade, int shadeOpacity)
		{
			if (cacheSvtChoppinessIndex != null)
				for (int idx = 0; idx < cacheSvtChoppinessIndex.Length; idx++)
					if (cacheSvtChoppinessIndex[idx].FractalEnergyPeriod == fractalEnergyPeriod && cacheSvtChoppinessIndex[idx].SmoothCurve == smoothCurve && cacheSvtChoppinessIndex[idx].ChoppinessLevel == choppinessLevel && cacheSvtChoppinessIndex[idx].TrendingLevel == trendingLevel && cacheSvtChoppinessIndex[idx].MiddleLevel == middleLevel && cacheSvtChoppinessIndex[idx].ShowZeroLevel == showZeroLevel && cacheSvtChoppinessIndex[idx].ZeroLevel == zeroLevel && cacheSvtChoppinessIndex[idx].ShowSignalCurve == showSignalCurve && cacheSvtChoppinessIndex[idx].SignalPeriod == signalPeriod && cacheSvtChoppinessIndex[idx].SignalMovingAverage == signalMovingAverage && cacheSvtChoppinessIndex[idx].ShowCurveColor == showCurveColor && cacheSvtChoppinessIndex[idx].ApplyShade == applyShade && cacheSvtChoppinessIndex[idx].ShadeOpacity == shadeOpacity && cacheSvtChoppinessIndex[idx].EqualsInput(input))
						return cacheSvtChoppinessIndex[idx];
			return CacheIndicator<Svt.SvtChoppinessIndex>(new Svt.SvtChoppinessIndex(){ FractalEnergyPeriod = fractalEnergyPeriod, SmoothCurve = smoothCurve, ChoppinessLevel = choppinessLevel, TrendingLevel = trendingLevel, MiddleLevel = middleLevel, ShowZeroLevel = showZeroLevel, ZeroLevel = zeroLevel, ShowSignalCurve = showSignalCurve, SignalPeriod = signalPeriod, SignalMovingAverage = signalMovingAverage, ShowCurveColor = showCurveColor, ApplyShade = applyShade, ShadeOpacity = shadeOpacity }, input, ref cacheSvtChoppinessIndex);
		}

	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		
		public Indicators.Svt.SvtChoppinessIndex SvtChoppinessIndex(int fractalEnergyPeriod, bool smoothCurve, double choppinessLevel, double trendingLevel, double middleLevel, bool showZeroLevel, double zeroLevel, bool showSignalCurve, int signalPeriod, SvtRef.Models.AverageType signalMovingAverage, bool showCurveColor, bool applyShade, int shadeOpacity)
		{
			return indicator.SvtChoppinessIndex(Input, fractalEnergyPeriod, smoothCurve, choppinessLevel, trendingLevel, middleLevel, showZeroLevel, zeroLevel, showSignalCurve, signalPeriod, signalMovingAverage, showCurveColor, applyShade, shadeOpacity);
		}


		
		public Indicators.Svt.SvtChoppinessIndex SvtChoppinessIndex(ISeries<double> input , int fractalEnergyPeriod, bool smoothCurve, double choppinessLevel, double trendingLevel, double middleLevel, bool showZeroLevel, double zeroLevel, bool showSignalCurve, int signalPeriod, SvtRef.Models.AverageType signalMovingAverage, bool showCurveColor, bool applyShade, int shadeOpacity)
		{
			return indicator.SvtChoppinessIndex(input, fractalEnergyPeriod, smoothCurve, choppinessLevel, trendingLevel, middleLevel, showZeroLevel, zeroLevel, showSignalCurve, signalPeriod, signalMovingAverage, showCurveColor, applyShade, shadeOpacity);
		}
	
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		
		public Indicators.Svt.SvtChoppinessIndex SvtChoppinessIndex(int fractalEnergyPeriod, bool smoothCurve, double choppinessLevel, double trendingLevel, double middleLevel, bool showZeroLevel, double zeroLevel, bool showSignalCurve, int signalPeriod, SvtRef.Models.AverageType signalMovingAverage, bool showCurveColor, bool applyShade, int shadeOpacity)
		{
			return indicator.SvtChoppinessIndex(Input, fractalEnergyPeriod, smoothCurve, choppinessLevel, trendingLevel, middleLevel, showZeroLevel, zeroLevel, showSignalCurve, signalPeriod, signalMovingAverage, showCurveColor, applyShade, shadeOpacity);
		}


		
		public Indicators.Svt.SvtChoppinessIndex SvtChoppinessIndex(ISeries<double> input , int fractalEnergyPeriod, bool smoothCurve, double choppinessLevel, double trendingLevel, double middleLevel, bool showZeroLevel, double zeroLevel, bool showSignalCurve, int signalPeriod, SvtRef.Models.AverageType signalMovingAverage, bool showCurveColor, bool applyShade, int shadeOpacity)
		{
			return indicator.SvtChoppinessIndex(input, fractalEnergyPeriod, smoothCurve, choppinessLevel, trendingLevel, middleLevel, showZeroLevel, zeroLevel, showSignalCurve, signalPeriod, signalMovingAverage, showCurveColor, applyShade, shadeOpacity);
		}

	}
}

#endregion
