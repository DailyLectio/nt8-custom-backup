using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.DrawingTools;

namespace NinjaTrader.NinjaScript.Indicators
{
	public class KeyLevels_V6_Final : Indicator
	{
		private double currentADR = 0;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "Lean B/R Grid - Confidence Edition Math (B1-B8)";
				Name						= "KeyLevels_V6_Final";
				Calculate					= Calculate.OnBarClose;
				IsOverlay					= true;
				PaintPriceMarkers			= true;

				ManualAnchorPrice			= 24812.00;
				ADRLookback					= 10;
			}
			else if (State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Day, 1);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 1)
			{
				if (CurrentBar < ADRLookback) return;
				double dayRange = High[0] - Low[0];
				currentADR = (currentADR == 0) ? dayRange : ((currentADR * (ADRLookback - 1)) + dayRange) / ADRLookback;
				return;
			}

			if (BarsInProgress != 0 || currentADR <= 0 || CurrentBar < 1) return;

			RenderFinalGrid(ManualAnchorPrice, currentADR);
		}

		private void RenderFinalGrid(double poc, double v)
		{
			string tag = "V6Final_" + Time[0].Date.ToString("yyyyMMdd");

			// --- CENTER POC ---
			Draw.HorizontalLine(this, tag + "POC", poc, Brushes.Gold, DashStyleHelper.Dash, 2);

			// --- UPSIDE (B-Levels) ---
			DrawLvl(poc + (v * 0.125), tag + "B1", "B1", Brushes.SlateGray, DashStyleHelper.Dot, 1);
			DrawLvl(poc + (v * 0.250), tag + "B2", "B2", Brushes.DarkTurquoise, DashStyleHelper.Solid, 2); // Confidence Target
			DrawLvl(poc + (v * 0.382), tag + "B3", "B3", Brushes.SlateGray, DashStyleHelper.Dot, 1);
			DrawLvl(poc + (v * 0.500), tag + "B4", "B4", Brushes.DodgerBlue, DashStyleHelper.Solid, 2); // Major Mid
			
			// --- EXTENSIONS (Clean & Thin) ---
			DrawLvl(poc + (v * 0.618), tag + "B5", "B5", Brushes.Gray, DashStyleHelper.Dash, 1);
			DrawLvl(poc + (v * 1.000), tag + "B6", "B6", Brushes.SpringGreen, DashStyleHelper.Solid, 2);
			DrawLvl(poc + (v * 1.272), tag + "B7", "B7", Brushes.Gray, DashStyleHelper.Dash, 1);
			DrawLvl(poc + (v * 1.618), tag + "B8", "B8", Brushes.ForestGreen, DashStyleHelper.Solid, 2); // Extreme

			// --- DOWNSIDE (R-Levels) ---
			DrawLvl(poc - (v * 0.125), tag + "R1", "R1", Brushes.SlateGray, DashStyleHelper.Dot, 1);
			DrawLvl(poc - (v * 0.250), tag + "R2", "R2", Brushes.Crimson, DashStyleHelper.Solid, 2);
			DrawLvl(poc - (v * 0.382), tag + "R3", "R3", Brushes.SlateGray, DashStyleHelper.Dot, 1);
			DrawLvl(poc - (v * 0.500), tag + "R4", "R4", Brushes.Red, DashStyleHelper.Solid, 2);
			
			// --- EXTENSIONS ---
			DrawLvl(poc - (v * 0.618), tag + "R5", "R5", Brushes.Gray, DashStyleHelper.Dash, 1);
			DrawLvl(poc - (v * 1.000), tag + "R6", "R6", Brushes.DarkRed, DashStyleHelper.Solid, 2);
			DrawLvl(poc - (v * 1.272), tag + "R7", "R7", Brushes.Gray, DashStyleHelper.Dash, 1);
			DrawLvl(poc - (v * 1.618), tag + "R8", "R8", Brushes.Maroon, DashStyleHelper.Solid, 2);
		}

		private void DrawLvl(double price, string tag, string label, Brush color, DashStyleHelper style, int width)
		{
			Draw.HorizontalLine(this, tag, price, color, style, width);
			Draw.Text(this, tag + "T", label, 10, price, color);
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name="Manual Anchor Price", GroupName="Parameters", Order=1)]
		public double ManualAnchorPrice { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name="ADR Lookback", GroupName="Parameters", Order=2)]
		public int ADRLookback { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private KeyLevels_V6_Final[] cacheKeyLevels_V6_Final;
		public KeyLevels_V6_Final KeyLevels_V6_Final(double manualAnchorPrice, int aDRLookback)
		{
			return KeyLevels_V6_Final(Input, manualAnchorPrice, aDRLookback);
		}

		public KeyLevels_V6_Final KeyLevels_V6_Final(ISeries<double> input, double manualAnchorPrice, int aDRLookback)
		{
			if (cacheKeyLevels_V6_Final != null)
				for (int idx = 0; idx < cacheKeyLevels_V6_Final.Length; idx++)
					if (cacheKeyLevels_V6_Final[idx] != null && cacheKeyLevels_V6_Final[idx].ManualAnchorPrice == manualAnchorPrice && cacheKeyLevels_V6_Final[idx].ADRLookback == aDRLookback && cacheKeyLevels_V6_Final[idx].EqualsInput(input))
						return cacheKeyLevels_V6_Final[idx];
			return CacheIndicator<KeyLevels_V6_Final>(new KeyLevels_V6_Final(){ ManualAnchorPrice = manualAnchorPrice, ADRLookback = aDRLookback }, input, ref cacheKeyLevels_V6_Final);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.KeyLevels_V6_Final KeyLevels_V6_Final(double manualAnchorPrice, int aDRLookback)
		{
			return indicator.KeyLevels_V6_Final(Input, manualAnchorPrice, aDRLookback);
		}

		public Indicators.KeyLevels_V6_Final KeyLevels_V6_Final(ISeries<double> input , double manualAnchorPrice, int aDRLookback)
		{
			return indicator.KeyLevels_V6_Final(input, manualAnchorPrice, aDRLookback);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.KeyLevels_V6_Final KeyLevels_V6_Final(double manualAnchorPrice, int aDRLookback)
		{
			return indicator.KeyLevels_V6_Final(Input, manualAnchorPrice, aDRLookback);
		}

		public Indicators.KeyLevels_V6_Final KeyLevels_V6_Final(ISeries<double> input , double manualAnchorPrice, int aDRLookback)
		{
			return indicator.KeyLevels_V6_Final(input, manualAnchorPrice, aDRLookback);
		}
	}
}

#endregion
