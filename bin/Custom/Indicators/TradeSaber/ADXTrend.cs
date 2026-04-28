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
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators.TradeSaber
{
	public class ADXTrend : Indicator
	{
		private ADX adx;
		
		private bool FilterOn;
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Indicator here.";
				Name										= "ADXTrend";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= false;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
				ShowTransparentPlotsInDataBox = true;
				
				ADXPeriod						= 14;
				LowerThreshold					= 25;
				UpperThreshold					= 75;
				ADXRisingBars					= 3;
				ADXFallingBars					= 2;
				AddPlot(Brushes.DarkCyan, "ADXValue");
				AddPlot(Brushes.Transparent, "Signal");
				
				AddLine(Brushes.Indigo, 2, "LowerLine"); //0
				AddLine(Brushes.Orange, 2, "UpperLine"); //1
			}
			else if (State == State.Configure)
			{
				Lines[0].Value	= LowerThreshold;
				Lines[1].Value	= UpperThreshold;
			}
			else if (State == State.DataLoaded)
			{
				adx = ADX(ADXPeriod);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 5)
				return;
			
			ADXValue[0] = adx[0];
			
			bool isAbove	= adx[0] >= LowerThreshold;
			
			bool isRising	= true;
			for (int i = 0; i < ADXRisingBars; i++)
			{
				if (adx[i] <= adx[i + 1])
				{
					isRising	= false;
					break;
				}
			}
			
			bool isFalling	= true;
			for (int i = 0; i < ADXFallingBars; i++)
			{
				if (adx[i] >= adx[i + 1])
				{
					isFalling	= false;
					break;
				}
			}
			
			if (isAbove && isRising && !FilterOn)
			{
				Signal[0] = 1;
				Draw.ArrowUp(this, "Up" + CurrentBar, false, 0, adx[0] - 5, Brushes.Lime);
				FilterOn  = true;
			}
			
			if ((!isAbove || isFalling) && FilterOn)
			{
				Signal[0] = -1;
				Draw.ArrowDown(this, "Down" + CurrentBar, false, 0, adx[0] + 5, Brushes.Red);
				FilterOn	= false;
			}
			
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ADXPeriod", Order=1, GroupName="Parameters")]
		public int ADXPeriod
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="LowerThreshold", Order=2, GroupName="Parameters")]
		public int LowerThreshold
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="UpperThreshold", Order=3, GroupName="Parameters")]
		public int UpperThreshold
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ADXRisingBars", Order=4, GroupName="Parameters")]
		public int ADXRisingBars
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ADXFallingBars", Order=5, GroupName="Parameters")]
		public int ADXFallingBars
		{ get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ADXValue
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> Signal
		{
			get { return Values[1]; }
		}


		#endregion

	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TradeSaber.ADXTrend[] cacheADXTrend;
		public TradeSaber.ADXTrend ADXTrend(int aDXPeriod, int lowerThreshold, int upperThreshold, int aDXRisingBars, int aDXFallingBars)
		{
			return ADXTrend(Input, aDXPeriod, lowerThreshold, upperThreshold, aDXRisingBars, aDXFallingBars);
		}

		public TradeSaber.ADXTrend ADXTrend(ISeries<double> input, int aDXPeriod, int lowerThreshold, int upperThreshold, int aDXRisingBars, int aDXFallingBars)
		{
			if (cacheADXTrend != null)
				for (int idx = 0; idx < cacheADXTrend.Length; idx++)
					if (cacheADXTrend[idx] != null && cacheADXTrend[idx].ADXPeriod == aDXPeriod && cacheADXTrend[idx].LowerThreshold == lowerThreshold && cacheADXTrend[idx].UpperThreshold == upperThreshold && cacheADXTrend[idx].ADXRisingBars == aDXRisingBars && cacheADXTrend[idx].ADXFallingBars == aDXFallingBars && cacheADXTrend[idx].EqualsInput(input))
						return cacheADXTrend[idx];
			return CacheIndicator<TradeSaber.ADXTrend>(new TradeSaber.ADXTrend(){ ADXPeriod = aDXPeriod, LowerThreshold = lowerThreshold, UpperThreshold = upperThreshold, ADXRisingBars = aDXRisingBars, ADXFallingBars = aDXFallingBars }, input, ref cacheADXTrend);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TradeSaber.ADXTrend ADXTrend(int aDXPeriod, int lowerThreshold, int upperThreshold, int aDXRisingBars, int aDXFallingBars)
		{
			return indicator.ADXTrend(Input, aDXPeriod, lowerThreshold, upperThreshold, aDXRisingBars, aDXFallingBars);
		}

		public Indicators.TradeSaber.ADXTrend ADXTrend(ISeries<double> input , int aDXPeriod, int lowerThreshold, int upperThreshold, int aDXRisingBars, int aDXFallingBars)
		{
			return indicator.ADXTrend(input, aDXPeriod, lowerThreshold, upperThreshold, aDXRisingBars, aDXFallingBars);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TradeSaber.ADXTrend ADXTrend(int aDXPeriod, int lowerThreshold, int upperThreshold, int aDXRisingBars, int aDXFallingBars)
		{
			return indicator.ADXTrend(Input, aDXPeriod, lowerThreshold, upperThreshold, aDXRisingBars, aDXFallingBars);
		}

		public Indicators.TradeSaber.ADXTrend ADXTrend(ISeries<double> input , int aDXPeriod, int lowerThreshold, int upperThreshold, int aDXRisingBars, int aDXFallingBars)
		{
			return indicator.ADXTrend(input, aDXPeriod, lowerThreshold, upperThreshold, aDXRisingBars, aDXFallingBars);
		}
	}
}

#endregion
