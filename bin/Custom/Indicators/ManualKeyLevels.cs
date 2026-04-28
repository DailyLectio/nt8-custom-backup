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
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX; 
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class ManualKeyLevels : Indicator
	{
		// DirectX Resources for Text
		private SharpDX.DirectWrite.TextFormat textFormat;
		private SharpDX.Direct2D1.SolidColorBrush dxBrush;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Manually enter key levels with custom labels.";
				Name										= "ManualKeyLevels";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true;
				DisplayInDataBox							= false;
				DrawOnPricePanel							= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				
				// Defaults
				LineColor = System.Windows.Media.Brushes.Cyan;
				LineWidth = 2;
				ResetLevels = false;
			}
			else if (State == State.DataLoaded)
			{
				// RESET LOGIC
				if (ResetLevels)
				{
					ONH = ONL = yVAH = yVAL = yPOC = 0;
					HighestHigh = HighTarget3 = High3 = HighTarget2 = High2 = HighTarget1 = High1 = 0;
					POC = Minus1 = MinusTarget1 = Minus2 = MinusTarget2 = Minus3 = MinusTarget3 = LowestLow = 0;
					ResetLevels = false; 
				}
			}
			else if (State == State.Terminated)
			{
				// Clean up resources to prevent memory leaks
				if (textFormat != null) textFormat.Dispose();
				if (dxBrush != null) dxBrush.Dispose();
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 0) return;

			// We draw the lines using standard NinjaTrader tools so they handle scrolling/scaling automatically.
			// The TEXT is handled in OnRender.
			DrawLine("ONH", ONH);
			DrawLine("ONL", ONL);
			DrawLine("yVAH", yVAH);
			DrawLine("yVAL", yVAL);
			DrawLine("yPOC", yPOC);
			DrawLine("Highest high", HighestHigh);
			DrawLine("High target 3", HighTarget3);
			DrawLine("High3", High3);
			DrawLine("High target 2", HighTarget2);
			DrawLine("High 2", High2);
			DrawLine("High target 1", HighTarget1);
			DrawLine("High 1", High1);
			DrawLine("POC", POC);
			DrawLine("Minus 1", Minus1);
			DrawLine("Minus target 1", MinusTarget1);
			DrawLine("Minus 2", Minus2);
			DrawLine("Minus target 2", MinusTarget2);
			DrawLine("Minus 3", Minus3);
			DrawLine("Minus target 3", MinusTarget3);
			DrawLine("Lowest low", LowestLow);
		}

		private void DrawLine(string tag, double price)
		{
			if (price > 0)
			{
				// Draws the infinite horizontal line
				Draw.HorizontalLine(this, tag + "_Line", price, LineColor, DashStyleHelper.Solid, LineWidth);
			}
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			// Verify we can render
			if (chartControl == null || chartScale == null || RenderTarget == null) return;

			// Initialize Font (Calibri Bold, ~8pt)
			if (textFormat == null)
			{
				// 11.0f is approximately 8pt
				textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Calibri", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 11.0f)
				{
					TextAlignment = SharpDX.DirectWrite.TextAlignment.Center, // Center align horizontally
					ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near // Align to top of the box
				};
			}

			// Initialize/Update Brush
			if (dxBrush == null)
			{
				dxBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, GetDXColor(LineColor));
			}
			dxBrush.Color = GetDXColor(LineColor);

			// Calculate Center X of the visible Chart Area
			float centerX = (float)(chartControl.CanvasRight - chartControl.CanvasLeft) / 2.0f + (float)chartControl.CanvasLeft;

			// Render all labels
			RenderLabel("ONH", ONH, chartScale, centerX);
			RenderLabel("ONL", ONL, chartScale, centerX);
			RenderLabel("yVAH", yVAH, chartScale, centerX);
			RenderLabel("yVAL", yVAL, chartScale, centerX);
			RenderLabel("yPOC", yPOC, chartScale, centerX);
			RenderLabel("Highest high", HighestHigh, chartScale, centerX);
			RenderLabel("High target 3", HighTarget3, chartScale, centerX);
			RenderLabel("High3", High3, chartScale, centerX);
			RenderLabel("High target 2", HighTarget2, chartScale, centerX);
			RenderLabel("High 2", High2, chartScale, centerX);
			RenderLabel("High target 1", HighTarget1, chartScale, centerX);
			RenderLabel("High 1", High1, chartScale, centerX);
			RenderLabel("POC", POC, chartScale, centerX);
			RenderLabel("Minus 1", Minus1, chartScale, centerX);
			RenderLabel("Minus target 1", MinusTarget1, chartScale, centerX);
			RenderLabel("Minus 2", Minus2, chartScale, centerX);
			RenderLabel("Minus target 2", MinusTarget2, chartScale, centerX);
			RenderLabel("Minus 3", Minus3, chartScale, centerX);
			RenderLabel("Minus target 3", MinusTarget3, chartScale, centerX);
			RenderLabel("Lowest low", LowestLow, chartScale, centerX);
		}

		private void RenderLabel(string text, double price, ChartScale chartScale, float centerX)
		{
			if (price <= 0) return;

			// Get the Y pixel coordinate for the price
			float y = (float)chartScale.GetYByValue(price);

			// Define a box 300px wide, centered on the screen
			// We draw it 2 pixels BELOW the line (y + 2)
			SharpDX.RectangleF rect = new SharpDX.RectangleF(centerX - 150, y + 2, 300, 20);

			RenderTarget.DrawText(text, textFormat, rect, dxBrush);
		}

		// Helper to convert WPF Color (Settings) to DirectX Color (Drawing)
		private SharpDX.Color GetDXColor(System.Windows.Media.Brush brush)
		{
			if (brush is System.Windows.Media.SolidColorBrush scb)
			{
				return new SharpDX.Color(scb.Color.R, scb.Color.G, scb.Color.B, scb.Color.A);
			}
			return SharpDX.Color.White;
		}

		#region Properties
		
		[Display(Name="Reset All Levels?", Description="Check this and hit Apply to clear all prices.", GroupName="0. Global Settings", Order=0)]
		public bool ResetLevels { get; set; }

		// Explicitly use System.Windows.Media.Brush to avoid ambiguity
		[XmlIgnore]
		[Display(Name="Line Color", GroupName="0. Global Settings", Order=1)]
		public System.Windows.Media.Brush LineColor { get; set; }

		[Browsable(false)]
		public string LineColorSerializable
		{
			get { return Serialize.BrushToString(LineColor); }
			set { LineColor = Serialize.StringToBrush(value); }
		}

		[Range(1, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(Name="Line Width", GroupName="0. Global Settings", Order=2)]
		public int LineWidth { get; set; }

		// Manual Price Inputs
		[NinjaScriptProperty] [Display(Name="ONH", GroupName="Levels", Order=1)] public double ONH { get; set; }
		[NinjaScriptProperty] [Display(Name="ONL", GroupName="Levels", Order=2)] public double ONL { get; set; }
		[NinjaScriptProperty] [Display(Name="yVAH", GroupName="Levels", Order=3)] public double yVAH { get; set; }
		[NinjaScriptProperty] [Display(Name="yVAL", GroupName="Levels", Order=4)] public double yVAL { get; set; }
		[NinjaScriptProperty] [Display(Name="yPOC", GroupName="Levels", Order=5)] public double yPOC { get; set; }
		[NinjaScriptProperty] [Display(Name="Highest high", GroupName="Levels", Order=6)] public double HighestHigh { get; set; }
		[NinjaScriptProperty] [Display(Name="High target 3", GroupName="Levels", Order=7)] public double HighTarget3 { get; set; }
		[NinjaScriptProperty] [Display(Name="High3", GroupName="Levels", Order=8)] public double High3 { get; set; }
		[NinjaScriptProperty] [Display(Name="High target 2", GroupName="Levels", Order=9)] public double HighTarget2 { get; set; }
		[NinjaScriptProperty] [Display(Name="High 2", GroupName="Levels", Order=10)] public double High2 { get; set; }
		[NinjaScriptProperty] [Display(Name="High target 1", GroupName="Levels", Order=11)] public double HighTarget1 { get; set; }
		[NinjaScriptProperty] [Display(Name="High 1", GroupName="Levels", Order=12)] public double High1 { get; set; }
		[NinjaScriptProperty] [Display(Name="POC", GroupName="Levels", Order=13)] public double POC { get; set; }
		[NinjaScriptProperty] [Display(Name="Minus 1", GroupName="Levels", Order=14)] public double Minus1 { get; set; }
		[NinjaScriptProperty] [Display(Name="Minus target 1", GroupName="Levels", Order=15)] public double MinusTarget1 { get; set; }
		[NinjaScriptProperty] [Display(Name="Minus 2", GroupName="Levels", Order=16)] public double Minus2 { get; set; }
		[NinjaScriptProperty] [Display(Name="Minus target 2", GroupName="Levels", Order=17)] public double MinusTarget2 { get; set; }
		[NinjaScriptProperty] [Display(Name="Minus 3", GroupName="Levels", Order=18)] public double Minus3 { get; set; }
		[NinjaScriptProperty] [Display(Name="Minus target 3", GroupName="Levels", Order=19)] public double MinusTarget3 { get; set; }
		[NinjaScriptProperty] [Display(Name="Lowest low", GroupName="Levels", Order=20)] public double LowestLow { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ManualKeyLevels[] cacheManualKeyLevels;
		public ManualKeyLevels ManualKeyLevels(int lineWidth, double oNH, double oNL, double yVAH, double yVAL, double yPOC, double highestHigh, double highTarget3, double high3, double highTarget2, double high2, double highTarget1, double high1, double pOC, double minus1, double minusTarget1, double minus2, double minusTarget2, double minus3, double minusTarget3, double lowestLow)
		{
			return ManualKeyLevels(Input, lineWidth, oNH, oNL, yVAH, yVAL, yPOC, highestHigh, highTarget3, high3, highTarget2, high2, highTarget1, high1, pOC, minus1, minusTarget1, minus2, minusTarget2, minus3, minusTarget3, lowestLow);
		}

		public ManualKeyLevels ManualKeyLevels(ISeries<double> input, int lineWidth, double oNH, double oNL, double yVAH, double yVAL, double yPOC, double highestHigh, double highTarget3, double high3, double highTarget2, double high2, double highTarget1, double high1, double pOC, double minus1, double minusTarget1, double minus2, double minusTarget2, double minus3, double minusTarget3, double lowestLow)
		{
			if (cacheManualKeyLevels != null)
				for (int idx = 0; idx < cacheManualKeyLevels.Length; idx++)
					if (cacheManualKeyLevels[idx] != null && cacheManualKeyLevels[idx].LineWidth == lineWidth && cacheManualKeyLevels[idx].ONH == oNH && cacheManualKeyLevels[idx].ONL == oNL && cacheManualKeyLevels[idx].yVAH == yVAH && cacheManualKeyLevels[idx].yVAL == yVAL && cacheManualKeyLevels[idx].yPOC == yPOC && cacheManualKeyLevels[idx].HighestHigh == highestHigh && cacheManualKeyLevels[idx].HighTarget3 == highTarget3 && cacheManualKeyLevels[idx].High3 == high3 && cacheManualKeyLevels[idx].HighTarget2 == highTarget2 && cacheManualKeyLevels[idx].High2 == high2 && cacheManualKeyLevels[idx].HighTarget1 == highTarget1 && cacheManualKeyLevels[idx].High1 == high1 && cacheManualKeyLevels[idx].POC == pOC && cacheManualKeyLevels[idx].Minus1 == minus1 && cacheManualKeyLevels[idx].MinusTarget1 == minusTarget1 && cacheManualKeyLevels[idx].Minus2 == minus2 && cacheManualKeyLevels[idx].MinusTarget2 == minusTarget2 && cacheManualKeyLevels[idx].Minus3 == minus3 && cacheManualKeyLevels[idx].MinusTarget3 == minusTarget3 && cacheManualKeyLevels[idx].LowestLow == lowestLow && cacheManualKeyLevels[idx].EqualsInput(input))
						return cacheManualKeyLevels[idx];
			return CacheIndicator<ManualKeyLevels>(new ManualKeyLevels(){ LineWidth = lineWidth, ONH = oNH, ONL = oNL, yVAH = yVAH, yVAL = yVAL, yPOC = yPOC, HighestHigh = highestHigh, HighTarget3 = highTarget3, High3 = high3, HighTarget2 = highTarget2, High2 = high2, HighTarget1 = highTarget1, High1 = high1, POC = pOC, Minus1 = minus1, MinusTarget1 = minusTarget1, Minus2 = minus2, MinusTarget2 = minusTarget2, Minus3 = minus3, MinusTarget3 = minusTarget3, LowestLow = lowestLow }, input, ref cacheManualKeyLevels);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ManualKeyLevels ManualKeyLevels(int lineWidth, double oNH, double oNL, double yVAH, double yVAL, double yPOC, double highestHigh, double highTarget3, double high3, double highTarget2, double high2, double highTarget1, double high1, double pOC, double minus1, double minusTarget1, double minus2, double minusTarget2, double minus3, double minusTarget3, double lowestLow)
		{
			return indicator.ManualKeyLevels(Input, lineWidth, oNH, oNL, yVAH, yVAL, yPOC, highestHigh, highTarget3, high3, highTarget2, high2, highTarget1, high1, pOC, minus1, minusTarget1, minus2, minusTarget2, minus3, minusTarget3, lowestLow);
		}

		public Indicators.ManualKeyLevels ManualKeyLevels(ISeries<double> input , int lineWidth, double oNH, double oNL, double yVAH, double yVAL, double yPOC, double highestHigh, double highTarget3, double high3, double highTarget2, double high2, double highTarget1, double high1, double pOC, double minus1, double minusTarget1, double minus2, double minusTarget2, double minus3, double minusTarget3, double lowestLow)
		{
			return indicator.ManualKeyLevels(input, lineWidth, oNH, oNL, yVAH, yVAL, yPOC, highestHigh, highTarget3, high3, highTarget2, high2, highTarget1, high1, pOC, minus1, minusTarget1, minus2, minusTarget2, minus3, minusTarget3, lowestLow);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ManualKeyLevels ManualKeyLevels(int lineWidth, double oNH, double oNL, double yVAH, double yVAL, double yPOC, double highestHigh, double highTarget3, double high3, double highTarget2, double high2, double highTarget1, double high1, double pOC, double minus1, double minusTarget1, double minus2, double minusTarget2, double minus3, double minusTarget3, double lowestLow)
		{
			return indicator.ManualKeyLevels(Input, lineWidth, oNH, oNL, yVAH, yVAL, yPOC, highestHigh, highTarget3, high3, highTarget2, high2, highTarget1, high1, pOC, minus1, minusTarget1, minus2, minusTarget2, minus3, minusTarget3, lowestLow);
		}

		public Indicators.ManualKeyLevels ManualKeyLevels(ISeries<double> input , int lineWidth, double oNH, double oNL, double yVAH, double yVAL, double yPOC, double highestHigh, double highTarget3, double high3, double highTarget2, double high2, double highTarget1, double high1, double pOC, double minus1, double minusTarget1, double minus2, double minusTarget2, double minus3, double minusTarget3, double lowestLow)
		{
			return indicator.ManualKeyLevels(input, lineWidth, oNH, oNL, yVAH, yVAL, yPOC, highestHigh, highTarget3, high3, highTarget2, high2, highTarget1, high1, pOC, minus1, minusTarget1, minus2, minusTarget2, minus3, minusTarget3, lowestLow);
		}
	}
}

#endregion
