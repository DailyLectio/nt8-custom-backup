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
		
		private Svt.SvtMovingAvgAtrBands[] cacheSvtMovingAvgAtrBands;

		
		public Svt.SvtMovingAvgAtrBands SvtMovingAvgAtrBands(SvtRef.Models.InputPrice inputDataSource, NinjaTrader.NinjaScript.Indicators.Svt.SvtMainMovingAvgAtrBandsEnum mainAverageType, NinjaTrader.NinjaScript.Indicators.Svt.SvtMovingAvgAtrBandsEnum averageType, int period, int aemaPeriod, int highLowPeriod, int window, double sigma, double offset, int kamaPeriod, int fastKama, int slowKama, int framaPeriod, int fC, int sC, int atrPeriod, bool isBand1Shown, double multiplier1, bool isBand2Shown, double multiplier2, SvtRef.Models.SmoothingMethod smoothingMethod, double gamma, bool applyShade, int shadeOpacity)
		{
			return SvtMovingAvgAtrBands(Input, inputDataSource, mainAverageType, averageType, period, aemaPeriod, highLowPeriod, window, sigma, offset, kamaPeriod, fastKama, slowKama, framaPeriod, fC, sC, atrPeriod, isBand1Shown, multiplier1, isBand2Shown, multiplier2, smoothingMethod, gamma, applyShade, shadeOpacity);
		}


		
		public Svt.SvtMovingAvgAtrBands SvtMovingAvgAtrBands(ISeries<double> input, SvtRef.Models.InputPrice inputDataSource, NinjaTrader.NinjaScript.Indicators.Svt.SvtMainMovingAvgAtrBandsEnum mainAverageType, NinjaTrader.NinjaScript.Indicators.Svt.SvtMovingAvgAtrBandsEnum averageType, int period, int aemaPeriod, int highLowPeriod, int window, double sigma, double offset, int kamaPeriod, int fastKama, int slowKama, int framaPeriod, int fC, int sC, int atrPeriod, bool isBand1Shown, double multiplier1, bool isBand2Shown, double multiplier2, SvtRef.Models.SmoothingMethod smoothingMethod, double gamma, bool applyShade, int shadeOpacity)
		{
			if (cacheSvtMovingAvgAtrBands != null)
				for (int idx = 0; idx < cacheSvtMovingAvgAtrBands.Length; idx++)
					if (cacheSvtMovingAvgAtrBands[idx].InputDataSource == inputDataSource && cacheSvtMovingAvgAtrBands[idx].MainAverageType == mainAverageType && cacheSvtMovingAvgAtrBands[idx].AverageType == averageType && cacheSvtMovingAvgAtrBands[idx].Period == period && cacheSvtMovingAvgAtrBands[idx].AemaPeriod == aemaPeriod && cacheSvtMovingAvgAtrBands[idx].HighLowPeriod == highLowPeriod && cacheSvtMovingAvgAtrBands[idx].Window == window && cacheSvtMovingAvgAtrBands[idx].Sigma == sigma && cacheSvtMovingAvgAtrBands[idx].Offset == offset && cacheSvtMovingAvgAtrBands[idx].KamaPeriod == kamaPeriod && cacheSvtMovingAvgAtrBands[idx].FastKama == fastKama && cacheSvtMovingAvgAtrBands[idx].SlowKama == slowKama && cacheSvtMovingAvgAtrBands[idx].FramaPeriod == framaPeriod && cacheSvtMovingAvgAtrBands[idx].FC == fC && cacheSvtMovingAvgAtrBands[idx].SC == sC && cacheSvtMovingAvgAtrBands[idx].AtrPeriod == atrPeriod && cacheSvtMovingAvgAtrBands[idx].IsBand1Shown == isBand1Shown && cacheSvtMovingAvgAtrBands[idx].Multiplier1 == multiplier1 && cacheSvtMovingAvgAtrBands[idx].IsBand2Shown == isBand2Shown && cacheSvtMovingAvgAtrBands[idx].Multiplier2 == multiplier2 && cacheSvtMovingAvgAtrBands[idx].SmoothingMethod == smoothingMethod && cacheSvtMovingAvgAtrBands[idx].Gamma == gamma && cacheSvtMovingAvgAtrBands[idx].ApplyShade == applyShade && cacheSvtMovingAvgAtrBands[idx].ShadeOpacity == shadeOpacity && cacheSvtMovingAvgAtrBands[idx].EqualsInput(input))
						return cacheSvtMovingAvgAtrBands[idx];
			return CacheIndicator<Svt.SvtMovingAvgAtrBands>(new Svt.SvtMovingAvgAtrBands(){ InputDataSource = inputDataSource, MainAverageType = mainAverageType, AverageType = averageType, Period = period, AemaPeriod = aemaPeriod, HighLowPeriod = highLowPeriod, Window = window, Sigma = sigma, Offset = offset, KamaPeriod = kamaPeriod, FastKama = fastKama, SlowKama = slowKama, FramaPeriod = framaPeriod, FC = fC, SC = sC, AtrPeriod = atrPeriod, IsBand1Shown = isBand1Shown, Multiplier1 = multiplier1, IsBand2Shown = isBand2Shown, Multiplier2 = multiplier2, SmoothingMethod = smoothingMethod, Gamma = gamma, ApplyShade = applyShade, ShadeOpacity = shadeOpacity }, input, ref cacheSvtMovingAvgAtrBands);
		}

	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		
		public Indicators.Svt.SvtMovingAvgAtrBands SvtMovingAvgAtrBands(SvtRef.Models.InputPrice inputDataSource, NinjaTrader.NinjaScript.Indicators.Svt.SvtMainMovingAvgAtrBandsEnum mainAverageType, NinjaTrader.NinjaScript.Indicators.Svt.SvtMovingAvgAtrBandsEnum averageType, int period, int aemaPeriod, int highLowPeriod, int window, double sigma, double offset, int kamaPeriod, int fastKama, int slowKama, int framaPeriod, int fC, int sC, int atrPeriod, bool isBand1Shown, double multiplier1, bool isBand2Shown, double multiplier2, SvtRef.Models.SmoothingMethod smoothingMethod, double gamma, bool applyShade, int shadeOpacity)
		{
			return indicator.SvtMovingAvgAtrBands(Input, inputDataSource, mainAverageType, averageType, period, aemaPeriod, highLowPeriod, window, sigma, offset, kamaPeriod, fastKama, slowKama, framaPeriod, fC, sC, atrPeriod, isBand1Shown, multiplier1, isBand2Shown, multiplier2, smoothingMethod, gamma, applyShade, shadeOpacity);
		}


		
		public Indicators.Svt.SvtMovingAvgAtrBands SvtMovingAvgAtrBands(ISeries<double> input , SvtRef.Models.InputPrice inputDataSource, NinjaTrader.NinjaScript.Indicators.Svt.SvtMainMovingAvgAtrBandsEnum mainAverageType, NinjaTrader.NinjaScript.Indicators.Svt.SvtMovingAvgAtrBandsEnum averageType, int period, int aemaPeriod, int highLowPeriod, int window, double sigma, double offset, int kamaPeriod, int fastKama, int slowKama, int framaPeriod, int fC, int sC, int atrPeriod, bool isBand1Shown, double multiplier1, bool isBand2Shown, double multiplier2, SvtRef.Models.SmoothingMethod smoothingMethod, double gamma, bool applyShade, int shadeOpacity)
		{
			return indicator.SvtMovingAvgAtrBands(input, inputDataSource, mainAverageType, averageType, period, aemaPeriod, highLowPeriod, window, sigma, offset, kamaPeriod, fastKama, slowKama, framaPeriod, fC, sC, atrPeriod, isBand1Shown, multiplier1, isBand2Shown, multiplier2, smoothingMethod, gamma, applyShade, shadeOpacity);
		}
	
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		
		public Indicators.Svt.SvtMovingAvgAtrBands SvtMovingAvgAtrBands(SvtRef.Models.InputPrice inputDataSource, NinjaTrader.NinjaScript.Indicators.Svt.SvtMainMovingAvgAtrBandsEnum mainAverageType, NinjaTrader.NinjaScript.Indicators.Svt.SvtMovingAvgAtrBandsEnum averageType, int period, int aemaPeriod, int highLowPeriod, int window, double sigma, double offset, int kamaPeriod, int fastKama, int slowKama, int framaPeriod, int fC, int sC, int atrPeriod, bool isBand1Shown, double multiplier1, bool isBand2Shown, double multiplier2, SvtRef.Models.SmoothingMethod smoothingMethod, double gamma, bool applyShade, int shadeOpacity)
		{
			return indicator.SvtMovingAvgAtrBands(Input, inputDataSource, mainAverageType, averageType, period, aemaPeriod, highLowPeriod, window, sigma, offset, kamaPeriod, fastKama, slowKama, framaPeriod, fC, sC, atrPeriod, isBand1Shown, multiplier1, isBand2Shown, multiplier2, smoothingMethod, gamma, applyShade, shadeOpacity);
		}


		
		public Indicators.Svt.SvtMovingAvgAtrBands SvtMovingAvgAtrBands(ISeries<double> input , SvtRef.Models.InputPrice inputDataSource, NinjaTrader.NinjaScript.Indicators.Svt.SvtMainMovingAvgAtrBandsEnum mainAverageType, NinjaTrader.NinjaScript.Indicators.Svt.SvtMovingAvgAtrBandsEnum averageType, int period, int aemaPeriod, int highLowPeriod, int window, double sigma, double offset, int kamaPeriod, int fastKama, int slowKama, int framaPeriod, int fC, int sC, int atrPeriod, bool isBand1Shown, double multiplier1, bool isBand2Shown, double multiplier2, SvtRef.Models.SmoothingMethod smoothingMethod, double gamma, bool applyShade, int shadeOpacity)
		{
			return indicator.SvtMovingAvgAtrBands(input, inputDataSource, mainAverageType, averageType, period, aemaPeriod, highLowPeriod, window, sigma, offset, kamaPeriod, fastKama, slowKama, framaPeriod, fC, sC, atrPeriod, isBand1Shown, multiplier1, isBand2Shown, multiplier2, smoothingMethod, gamma, applyShade, shadeOpacity);
		}

	}
}

#endregion
