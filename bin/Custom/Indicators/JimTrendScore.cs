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
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class JimTrendScore : Indicator
	{
		private ADX adx;
		private DM dm;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Measures the mathematical relationship between DI Delta and ADX Slope.";
				Name										= "JimTrendScore";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= false;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				
				// User Inputs
				AdxPeriod 		= 14;
				DeltaThreshold 	= 25;
				AdxThreshold 	= 20;

				AddPlot(new Stroke(Brushes.Gold, 2), PlotStyle.Bar, "TrendScore");
				AddLine(Brushes.Gray, 25, "Baseline");
			}
			else if (State == State.DataLoaded)
			{
				adx = ADX(AdxPeriod);
				dm = DM(AdxPeriod);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < AdxPeriod + 1) return;

			// 1. Calculate the DI Delta (Absolute value)
			double diPlus  = dm.DiPlus[0];
			double diMinus = dm.DiMinus[0];
			double diDelta = Math.Abs(diPlus - diMinus);

			// 2. Check for ADX Slope (Current > Previous)
			bool isAdxRising = adx[0] > adx[1];

			// 3. Apply the "Jim Blindfold" Filters
			if (diDelta >= DeltaThreshold && adx[0] >= AdxThreshold && isAdxRising)
			{
				// The Score is the Delta magnified by the ADX strength
				double score = diDelta * (adx[0] / 20);
				TrendScore[0] = score;
				
				// Visual feedback: Color the bar based on intensity
				if (score > 40)
					PlotBrushes[0][0] = Brushes.Cyan; 
				else
					PlotBrushes[0][0] = Brushes.Gold; 
			}
			else
			{
				TrendScore[0] = 0; 
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ADX Period", Order=1, GroupName="Parameters")]
		public int AdxPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Delta Threshold", Order=2, GroupName="Parameters")]
		public int DeltaThreshold { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ADX Threshold", Order=3, GroupName="Parameters")]
		public int AdxThreshold { get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> TrendScore
		{
			get { return Values[0]; }
		}
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private JimTrendScore[] cacheJimTrendScore;
		public JimTrendScore JimTrendScore(int adxPeriod, int deltaThreshold, int adxThreshold)
		{
			return JimTrendScore(Input, adxPeriod, deltaThreshold, adxThreshold);
		}

		public JimTrendScore JimTrendScore(ISeries<double> input, int adxPeriod, int deltaThreshold, int adxThreshold)
		{
			if (cacheJimTrendScore != null)
				for (int idx = 0; idx < cacheJimTrendScore.Length; idx++)
					if (cacheJimTrendScore[idx] != null && cacheJimTrendScore[idx].AdxPeriod == adxPeriod && cacheJimTrendScore[idx].DeltaThreshold == deltaThreshold && cacheJimTrendScore[idx].AdxThreshold == adxThreshold && cacheJimTrendScore[idx].EqualsInput(input))
						return cacheJimTrendScore[idx];
			return CacheIndicator<JimTrendScore>(new JimTrendScore(){ AdxPeriod = adxPeriod, DeltaThreshold = deltaThreshold, AdxThreshold = adxThreshold }, input, ref cacheJimTrendScore);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.JimTrendScore JimTrendScore(int adxPeriod, int deltaThreshold, int adxThreshold)
		{
			return indicator.JimTrendScore(Input, adxPeriod, deltaThreshold, adxThreshold);
		}

		public Indicators.JimTrendScore JimTrendScore(ISeries<double> input , int adxPeriod, int deltaThreshold, int adxThreshold)
		{
			return indicator.JimTrendScore(input, adxPeriod, deltaThreshold, adxThreshold);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.JimTrendScore JimTrendScore(int adxPeriod, int deltaThreshold, int adxThreshold)
		{
			return indicator.JimTrendScore(Input, adxPeriod, deltaThreshold, adxThreshold);
		}

		public Indicators.JimTrendScore JimTrendScore(ISeries<double> input , int adxPeriod, int deltaThreshold, int adxThreshold)
		{
			return indicator.JimTrendScore(input, adxPeriod, deltaThreshold, adxThreshold);
		}
	}
}

#endregion
