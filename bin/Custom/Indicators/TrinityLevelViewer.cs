#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using WPFBrushes = System.Windows.Media.Brushes;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class TrinityLevelViewer : Indicator
    {
        // =========================================================
        // PROPERTIES: VISIBILITY & STYLES
        // =========================================================
        [NinjaScriptProperty, Display(Name = "Show Text Labels", GroupName = "1. General Settings", Order = 1)] 
        public bool ShowLabels { get; set; } = true;

        [NinjaScriptProperty, Display(Name = "Dark Theme Text (For Black Charts)", GroupName = "1. General Settings", Order = 2)] 
        public bool UseDarkTheme { get; set; } = true;

        [NinjaScriptProperty, Display(Name = "POC (Dev, yPOC, ON_POC)", GroupName = "2. Line Styles", Order = 1)] 
        public Stroke Stroke_POC { get; set; }

        [NinjaScriptProperty, Display(Name = "Value Area (VAH, VAL)", GroupName = "2. Line Styles", Order = 2)] 
        public Stroke Stroke_ValueArea { get; set; }

        [NinjaScriptProperty, Display(Name = "Prior Day (PDH, PDL, PDC)", GroupName = "2. Line Styles", Order = 3)] 
        public Stroke Stroke_Yesterday { get; set; }

        [NinjaScriptProperty, Display(Name = "Overnight (ONH, ONL)", GroupName = "2. Line Styles", Order = 4)] 
        public Stroke Stroke_Overnight { get; set; }

        [NinjaScriptProperty, Display(Name = "Standard/Mids (IBH, IBL)", GroupName = "2. Line Styles", Order = 5)] 
        public Stroke Stroke_Standard { get; set; }

        // =========================================================
        // DIRECT X / SHARP DX RESOURCES (For Labels)
        // =========================================================
        private SharpDX.DirectWrite.TextFormat dxTextFormat;
        private SharpDX.Direct2D1.SolidColorBrush dxBrushText;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Trinity Level Viewer";
                Description = "A lightweight receiver that reads HUDMessenger and plots Data Bridge levels.";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                IsSuspendedWhileInactive = true;

                // Defaults: Highlight POC and Value Area, fade out the rest.
                Stroke_POC = new Stroke(WPFBrushes.Cyan, DashStyleHelper.Solid, 2);
                Stroke_ValueArea = new Stroke(WPFBrushes.DodgerBlue, DashStyleHelper.Solid, 2);
                
                // Neutral fine lines for the rest
                Stroke_Yesterday = new Stroke(WPFBrushes.DimGray, DashStyleHelper.Dot, 1);
                Stroke_Overnight = new Stroke(WPFBrushes.DarkOrange, DashStyleHelper.Dash, 1);
                Stroke_Standard = new Stroke(WPFBrushes.SlateGray, DashStyleHelper.Dash, 1);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            // Make sure the Bridge has actually pushed data
            if (HUDMessenger.SharedLevelMap == null || HUDMessenger.SharedLevelMap.Count == 0) return;

            DrawLevels();
        }

        private void DrawLevels()
        {
            foreach (var kvp in HUDMessenger.SharedLevelMap)
            {
                string levelName = kvp.Key;
                double price = kvp.Value;

                // Skip uninitialized or zero levels
                if (price == 0 || price == double.MinValue || price == double.MaxValue) 
                {
                    RemoveDrawObject(levelName + "_Line");
                    continue;
                }

                // 1. Semantic Style Routing (Dynamically assigns the stroke)
                Stroke activeStroke = Stroke_Standard;

                if (levelName.Contains("POC")) activeStroke = Stroke_POC;
                else if (levelName.Contains("VAH") || levelName.Contains("VAL")) activeStroke = Stroke_ValueArea;
                else if (levelName.StartsWith("ON")) activeStroke = Stroke_Overnight;
                else if (levelName.StartsWith("PD") || levelName.StartsWith("y")) activeStroke = Stroke_Yesterday;

                // 2. Draw the Line
                var line = Draw.HorizontalLine(this, levelName + "_Line", price, activeStroke.Brush);
                if (line != null)
                {
                    line.Stroke = activeStroke; // Applies thickness and dash style natively
                }
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (!ShowLabels || HUDMessenger.SharedLevelMap == null || ChartBars == null) return;

            // 1. Initialize SharpDX Resources
            if (dxTextFormat == null) 
            {
                dxTextFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Calibri", 
                    SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 11.0f) 
                { 
                    TextAlignment = SharpDX.DirectWrite.TextAlignment.Trailing, // Align to right edge
                    ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center 
                };
            }

            if (dxBrushText == null) 
            {
                dxBrushText = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, 
                    UseDarkTheme ? SharpDX.Color.White : SharpDX.Color.Black); // Auto-adjust to chart theme
            }

            // 2. Draw the Labels near the right edge of the chart
            float rightEdgeX = (float)chartControl.CanvasRight - 10f; 

            foreach (var kvp in HUDMessenger.SharedLevelMap)
            {
                double price = kvp.Value;

                // Only draw text if the level is valid and currently visible on the vertical scale
                if (price > 0 && price >= chartScale.MinValue && price <= chartScale.MaxValue)
                {
                    float yPos = chartScale.GetYByValue(price);
                    
                    // Draw text exactly on the line
                    RenderTarget.DrawText(kvp.Key, dxTextFormat, 
                        new SharpDX.RectangleF(rightEdgeX - 150, yPos - 10, 150, 20), dxBrushText);
                }
            }
        }

        public override void OnRenderTargetChanged()
        {
            // Clean up SharpDX resources when the render target changes (e.g. resizing chart)
            if (dxBrushText != null)
            {
                dxBrushText.Dispose();
                dxBrushText = null;
            }
            if (dxTextFormat != null)
            {
                dxTextFormat.Dispose();
                dxTextFormat = null;
            }
            base.OnRenderTargetChanged();
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TrinityLevelViewer[] cacheTrinityLevelViewer;
		public TrinityLevelViewer TrinityLevelViewer(bool showLabels, bool useDarkTheme, Stroke stroke_POC, Stroke stroke_ValueArea, Stroke stroke_Yesterday, Stroke stroke_Overnight, Stroke stroke_Standard)
		{
			return TrinityLevelViewer(Input, showLabels, useDarkTheme, stroke_POC, stroke_ValueArea, stroke_Yesterday, stroke_Overnight, stroke_Standard);
		}

		public TrinityLevelViewer TrinityLevelViewer(ISeries<double> input, bool showLabels, bool useDarkTheme, Stroke stroke_POC, Stroke stroke_ValueArea, Stroke stroke_Yesterday, Stroke stroke_Overnight, Stroke stroke_Standard)
		{
			if (cacheTrinityLevelViewer != null)
				for (int idx = 0; idx < cacheTrinityLevelViewer.Length; idx++)
					if (cacheTrinityLevelViewer[idx] != null && cacheTrinityLevelViewer[idx].ShowLabels == showLabels && cacheTrinityLevelViewer[idx].UseDarkTheme == useDarkTheme && cacheTrinityLevelViewer[idx].Stroke_POC == stroke_POC && cacheTrinityLevelViewer[idx].Stroke_ValueArea == stroke_ValueArea && cacheTrinityLevelViewer[idx].Stroke_Yesterday == stroke_Yesterday && cacheTrinityLevelViewer[idx].Stroke_Overnight == stroke_Overnight && cacheTrinityLevelViewer[idx].Stroke_Standard == stroke_Standard && cacheTrinityLevelViewer[idx].EqualsInput(input))
						return cacheTrinityLevelViewer[idx];
			return CacheIndicator<TrinityLevelViewer>(new TrinityLevelViewer(){ ShowLabels = showLabels, UseDarkTheme = useDarkTheme, Stroke_POC = stroke_POC, Stroke_ValueArea = stroke_ValueArea, Stroke_Yesterday = stroke_Yesterday, Stroke_Overnight = stroke_Overnight, Stroke_Standard = stroke_Standard }, input, ref cacheTrinityLevelViewer);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TrinityLevelViewer TrinityLevelViewer(bool showLabels, bool useDarkTheme, Stroke stroke_POC, Stroke stroke_ValueArea, Stroke stroke_Yesterday, Stroke stroke_Overnight, Stroke stroke_Standard)
		{
			return indicator.TrinityLevelViewer(Input, showLabels, useDarkTheme, stroke_POC, stroke_ValueArea, stroke_Yesterday, stroke_Overnight, stroke_Standard);
		}

		public Indicators.TrinityLevelViewer TrinityLevelViewer(ISeries<double> input , bool showLabels, bool useDarkTheme, Stroke stroke_POC, Stroke stroke_ValueArea, Stroke stroke_Yesterday, Stroke stroke_Overnight, Stroke stroke_Standard)
		{
			return indicator.TrinityLevelViewer(input, showLabels, useDarkTheme, stroke_POC, stroke_ValueArea, stroke_Yesterday, stroke_Overnight, stroke_Standard);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TrinityLevelViewer TrinityLevelViewer(bool showLabels, bool useDarkTheme, Stroke stroke_POC, Stroke stroke_ValueArea, Stroke stroke_Yesterday, Stroke stroke_Overnight, Stroke stroke_Standard)
		{
			return indicator.TrinityLevelViewer(Input, showLabels, useDarkTheme, stroke_POC, stroke_ValueArea, stroke_Yesterday, stroke_Overnight, stroke_Standard);
		}

		public Indicators.TrinityLevelViewer TrinityLevelViewer(ISeries<double> input , bool showLabels, bool useDarkTheme, Stroke stroke_POC, Stroke stroke_ValueArea, Stroke stroke_Yesterday, Stroke stroke_Overnight, Stroke stroke_Standard)
		{
			return indicator.TrinityLevelViewer(input, showLabels, useDarkTheme, stroke_POC, stroke_ValueArea, stroke_Yesterday, stroke_Overnight, stroke_Standard);
		}
	}
}

#endregion
