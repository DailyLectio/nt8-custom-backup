#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// ... [Existing Using Declarations]

namespace NinjaTrader.NinjaScript.Indicators
{
	public class KeyLevels_Hybrid_V5 : Indicator
	{
		private double currentADR = 0;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "KeyLevels_Hybrid_V5";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				
				ADRLookback = 10;
				ManualAnchorPrice = 24812.00; // Updated to your 10m POC
				ShowScalpLevels = true;      // The new "Mode" toggle
				ShowMajorTargets = true;
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

			if (BarsInProgress != 0 || currentADR <= 0) return;

			PlotHybridLadder(ManualAnchorPrice, currentADR);
		}

		private void PlotHybridLadder(double poc, double v)
		{
			string tag = "Hybrid_" + Time[0].Date.ToString("yyyyMMdd");

			// --- MAJOR TREND LEVELS ---
			if (ShowMajorTargets)
			{
				double hMid1 = poc + (v * 0.5);   double lMid1 = poc - (v * 0.5);
				double hTgt  = poc + (v * 1.0);   double lTgt  = poc - (v * 1.0);
				double hExt  = poc + (v * 1.618); double lExt  = poc - (v * 1.618);

				Draw.HorizontalLine(this, tag+"POC", poc, Brushes.Yellow, DashStyleHelper.Dash, 2);
				
				// Highs
				Draw.HorizontalLine(this, tag+"HM1", hMid1, Brushes.LimeGreen, DashStyleHelper.Solid, 1);
				Draw.HorizontalLine(this, tag+"HT", hTgt, Brushes.SpringGreen, DashStyleHelper.Solid, 2);
				Draw.HorizontalLine(this, tag+"HE", hExt, Brushes.DarkGreen, DashStyleHelper.Solid, 3);
				
				// Lows
				Draw.HorizontalLine(this, tag+"LM1", lMid1, Brushes.Red, DashStyleHelper.Solid, 1);
				Draw.HorizontalLine(this, tag+"LT", lTgt, Brushes.Crimson, DashStyleHelper.Solid, 2);
				Draw.HorizontalLine(this, tag+"LE", lExt, Brushes.DarkRed, DashStyleHelper.Solid, 3);
			}

			// --- SCALP CLUSTERS (The "Gap Fillers") ---
			if (ShowScalpLevels)
			{
				// Fibonacci Micro-Levels
				double[] fibs = { 0.146, 0.236, 0.382 }; 
				foreach (double f in fibs)
				{
					Draw.HorizontalLine(this, tag+"hS"+f, poc + (v * f), Brushes.Gray, DashStyleHelper.Dot, 1);
					Draw.HorizontalLine(this, tag+"lS"+f, poc - (v * f), Brushes.Gray, DashStyleHelper.Dot, 1);
				}
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name="Show Scalp Clusters", GroupName="Modes", Order=1)]
		public bool ShowScalpLevels { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Show Major Targets", GroupName="Modes", Order=2)]
		public bool ShowMajorTargets { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Manual Anchor Price", GroupName="Parameters", Order=3)]
		public double ManualAnchorPrice { get; set; }

		[NinjaScriptProperty]
		[Display(Name="ADR Lookback", GroupName="Parameters", Order=4)]
		public int ADRLookback { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private KeyLevels_Hybrid_V5[] cacheKeyLevels_Hybrid_V5;
		public KeyLevels_Hybrid_V5 KeyLevels_Hybrid_V5(bool showScalpLevels, bool showMajorTargets, double manualAnchorPrice, int aDRLookback)
		{
			return KeyLevels_Hybrid_V5(Input, showScalpLevels, showMajorTargets, manualAnchorPrice, aDRLookback);
		}

		public KeyLevels_Hybrid_V5 KeyLevels_Hybrid_V5(ISeries<double> input, bool showScalpLevels, bool showMajorTargets, double manualAnchorPrice, int aDRLookback)
		{
			if (cacheKeyLevels_Hybrid_V5 != null)
				for (int idx = 0; idx < cacheKeyLevels_Hybrid_V5.Length; idx++)
					if (cacheKeyLevels_Hybrid_V5[idx] != null && cacheKeyLevels_Hybrid_V5[idx].ShowScalpLevels == showScalpLevels && cacheKeyLevels_Hybrid_V5[idx].ShowMajorTargets == showMajorTargets && cacheKeyLevels_Hybrid_V5[idx].ManualAnchorPrice == manualAnchorPrice && cacheKeyLevels_Hybrid_V5[idx].ADRLookback == aDRLookback && cacheKeyLevels_Hybrid_V5[idx].EqualsInput(input))
						return cacheKeyLevels_Hybrid_V5[idx];
			return CacheIndicator<KeyLevels_Hybrid_V5>(new KeyLevels_Hybrid_V5(){ ShowScalpLevels = showScalpLevels, ShowMajorTargets = showMajorTargets, ManualAnchorPrice = manualAnchorPrice, ADRLookback = aDRLookback }, input, ref cacheKeyLevels_Hybrid_V5);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.KeyLevels_Hybrid_V5 KeyLevels_Hybrid_V5(bool showScalpLevels, bool showMajorTargets, double manualAnchorPrice, int aDRLookback)
		{
			return indicator.KeyLevels_Hybrid_V5(Input, showScalpLevels, showMajorTargets, manualAnchorPrice, aDRLookback);
		}

		public Indicators.KeyLevels_Hybrid_V5 KeyLevels_Hybrid_V5(ISeries<double> input , bool showScalpLevels, bool showMajorTargets, double manualAnchorPrice, int aDRLookback)
		{
			return indicator.KeyLevels_Hybrid_V5(input, showScalpLevels, showMajorTargets, manualAnchorPrice, aDRLookback);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.KeyLevels_Hybrid_V5 KeyLevels_Hybrid_V5(bool showScalpLevels, bool showMajorTargets, double manualAnchorPrice, int aDRLookback)
		{
			return indicator.KeyLevels_Hybrid_V5(Input, showScalpLevels, showMajorTargets, manualAnchorPrice, aDRLookback);
		}

		public Indicators.KeyLevels_Hybrid_V5 KeyLevels_Hybrid_V5(ISeries<double> input , bool showScalpLevels, bool showMajorTargets, double manualAnchorPrice, int aDRLookback)
		{
			return indicator.KeyLevels_Hybrid_V5(input, showScalpLevels, showMajorTargets, manualAnchorPrice, aDRLookback);
		}
	}
}

#endregion
