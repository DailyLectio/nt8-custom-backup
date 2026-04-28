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
	public class KeyLevels_V6_CustomGrid : Indicator
	{
		private double currentADR = 0;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "V6 B/R Custom Grid - Clean Edition";
				Name						= "KeyLevels_V6_CustomGrid";
				Calculate					= Calculate.OnBarClose;
				IsOverlay					= true;
				PaintPriceMarkers			= true;

				ManualAnchorPrice			= 24812.00;
				ADRLookback					= 10;
			}
			else if (State == State.Configure)
			{
				// Add the Daily series for ADR calculation
				AddDataSeries(BarsPeriodType.Day, 1);
			}
		}

		protected override void OnBarUpdate()
		{
			// 1. Calculate ADR from the Daily series
			if (BarsInProgress == 1)
			{
				if (CurrentBar < ADRLookback) return;
				double dayRange = High[0] - Low[0];
				currentADR = (currentADR == 0) ? dayRange : ((currentADR * (ADRLookback - 1)) + dayRange) / ADRLookback;
				return;
			}

			// 2. Main Chart Drawing logic
			if (BarsInProgress != 0 || currentADR <= 0 || CurrentBar < 1) return;

			// Logic starts here
			string tag = "V6_" + Time[0].Date.ToString("yyyyMMdd");
			double v = currentADR;
			double poc = ManualAnchorPrice;

			// --- POC ---
			Draw.HorizontalLine(this, tag + "POC", poc, Brushes.Gold, DashStyleHelper.Dash, 2);
			Draw.Text(this, tag + "POCT", "POC", 5, poc, Brushes.Gold);

			// --- UPSIDE (B-Levels) ---
			DrawBRLvl(poc + (v * 0.125), tag + "B1", "B1", Brushes.Gray, 1, DashStyleHelper.Dot);
			DrawBRLvl(poc + (v * 0.250), tag + "B2", "B2 [KEY]", Brushes.Cyan, 3, DashStyleHelper.Solid); 
			DrawBRLvl(poc + (v * 0.382), tag + "B3", "B3", Brushes.Gray, 1, DashStyleHelper.Dot);
			DrawBRLvl(poc + (v * 0.500), tag + "B4", "B4", Brushes.DodgerBlue, 2, DashStyleHelper.Solid);

			// --- DOWNSIDE (R-Levels) ---
			DrawBRLvl(poc - (v * 0.125), tag + "R1", "R1", Brushes.Gray, 1, DashStyleHelper.Dot);
			DrawBRLvl(poc - (v * 0.250), tag + "R2", "R2 [KEY]", Brushes.Cyan, 3, DashStyleHelper.Solid);
			DrawBRLvl(poc - (v * 0.382), tag + "R3", "R3", Brushes.Gray, 1, DashStyleHelper.Dot);
			DrawBRLvl(poc - (v * 0.500), tag + "R4", "R4", Brushes.Red, 2, DashStyleHelper.Solid);
		}

		private void DrawBRLvl(double price, string tag, string label, Brush color, int width, DashStyleHelper style)
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
		private KeyLevels_V6_CustomGrid[] cacheKeyLevels_V6_CustomGrid;
		public KeyLevels_V6_CustomGrid KeyLevels_V6_CustomGrid(double manualAnchorPrice, int aDRLookback)
		{
			return KeyLevels_V6_CustomGrid(Input, manualAnchorPrice, aDRLookback);
		}

		public KeyLevels_V6_CustomGrid KeyLevels_V6_CustomGrid(ISeries<double> input, double manualAnchorPrice, int aDRLookback)
		{
			if (cacheKeyLevels_V6_CustomGrid != null)
				for (int idx = 0; idx < cacheKeyLevels_V6_CustomGrid.Length; idx++)
					if (cacheKeyLevels_V6_CustomGrid[idx] != null && cacheKeyLevels_V6_CustomGrid[idx].ManualAnchorPrice == manualAnchorPrice && cacheKeyLevels_V6_CustomGrid[idx].ADRLookback == aDRLookback && cacheKeyLevels_V6_CustomGrid[idx].EqualsInput(input))
						return cacheKeyLevels_V6_CustomGrid[idx];
			return CacheIndicator<KeyLevels_V6_CustomGrid>(new KeyLevels_V6_CustomGrid(){ ManualAnchorPrice = manualAnchorPrice, ADRLookback = aDRLookback }, input, ref cacheKeyLevels_V6_CustomGrid);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.KeyLevels_V6_CustomGrid KeyLevels_V6_CustomGrid(double manualAnchorPrice, int aDRLookback)
		{
			return indicator.KeyLevels_V6_CustomGrid(Input, manualAnchorPrice, aDRLookback);
		}

		public Indicators.KeyLevels_V6_CustomGrid KeyLevels_V6_CustomGrid(ISeries<double> input , double manualAnchorPrice, int aDRLookback)
		{
			return indicator.KeyLevels_V6_CustomGrid(input, manualAnchorPrice, aDRLookback);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.KeyLevels_V6_CustomGrid KeyLevels_V6_CustomGrid(double manualAnchorPrice, int aDRLookback)
		{
			return indicator.KeyLevels_V6_CustomGrid(Input, manualAnchorPrice, aDRLookback);
		}

		public Indicators.KeyLevels_V6_CustomGrid KeyLevels_V6_CustomGrid(ISeries<double> input , double manualAnchorPrice, int aDRLookback)
		{
			return indicator.KeyLevels_V6_CustomGrid(input, manualAnchorPrice, aDRLookback);
		}
	}
}

#endregion
