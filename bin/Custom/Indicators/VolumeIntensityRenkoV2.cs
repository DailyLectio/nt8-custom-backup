#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;

using SDX = SharpDX;
using D2D = SharpDX.Direct2D1;
using DW  = SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class VolumeIntensityRenkoV2 : Indicator
    {
        private Series<double> intensitySeries;
        private SMA avgIntensitySMA;
        
        // DX Dashboard Resources
        private D2D.SolidColorBrush dxText, dxBg;
        private DW.TextFormat dxFormat;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = "V2: Volume Intensity for UniRenko with directional quadrants and dashboard.";
                Name                        = "VolumeIntensityRenkoV2";
                Calculate                   = Calculate.OnBarClose; // Fine for Renko brick closes
                IsOverlay                   = false;
                DisplayInDataBox            = true;
                DrawOnPricePanel            = false;
                
                // Dashboard defaults
                ShowDashboard               = true;
                DashCorner                  = TextPosition.TopRight;
                
                // Thresholds for the Quarters (Multiplier of average intensity)
                Q1_MaxMultiplier            = 0.75; // Below 0.75x = Low Vol/Chop
                Q2_MaxMultiplier            = 1.25; // Normal Vol
                Q3_MaxMultiplier            = 2.00; // Strong Breakout Vol
                // Anything above Q3 is Q4 (Extreme/Climax)
                
                // Colors - Bullish (Green/Blue tones)
                BullQ1 = Brushes.DarkOliveGreen;
                BullQ2 = Brushes.LimeGreen;
                BullQ3 = Brushes.Cyan;
                BullQ4 = Brushes.White; // Climax Up
                
                // Colors - Bearish (Red/Magenta tones)
                BearQ1 = Brushes.Maroon;
                BearQ2 = Brushes.Red;
                BearQ3 = Brushes.Magenta;
                BearQ4 = Brushes.Yellow; // Climax Down

                AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Bar, "IntensityBar");
            }
            else if (State == State.DataLoaded)
            {
                intensitySeries = new Series<double>(this);
                avgIntensitySMA = SMA(intensitySeries, 21); // 21-brick average
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            // 1. Calculate time to build the Renko brick (prevent divide by zero on fast markets)
            double seconds = Math.Max(0.1, (Time[0] - Time[1]).TotalSeconds);
            
            // 2. Volume Intensity = Contracts per second
            double currentIntensity = Volume[0] / seconds;
            intensitySeries[0] = currentIntensity;

            // Plot the raw volume for the bar chart size, but we will color it based on intensity
            Value[0] = Volume[0];

            if (CurrentBar < 21) return; // Wait for SMA to calculate

            // 3. Calculate Relative Intensity (RVol of Intensity)
            double avgInt = Math.Max(0.1, avgIntensitySMA[0]);
            double relIntensity = currentIntensity / avgInt;

            // 4. Directional Logic (Fixing the old script's bug)
            bool isUp = Close[0] >= Open[0]; // UniRenko brick direction

            // 5. Quadrant Coloring
            Brush barColor = Brushes.Gray;

            if (isUp)
            {
                if (relIntensity <= Q1_MaxMultiplier) barColor = BullQ1;
                else if (relIntensity <= Q2_MaxMultiplier) barColor = BullQ2;
                else if (relIntensity <= Q3_MaxMultiplier) barColor = BullQ3;
                else barColor = BullQ4; 
            }
            else
            {
                if (relIntensity <= Q1_MaxMultiplier) barColor = BearQ1;
                else if (relIntensity <= Q2_MaxMultiplier) barColor = BearQ2;
                else if (relIntensity <= Q3_MaxMultiplier) barColor = BearQ3;
                else barColor = BearQ4; 
            }

            PlotBrushes[0][0] = barColor;
        }

        #region Dashboard Rendering
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (!ShowDashboard || RenderTarget == null || CurrentBar < 21) return;

            double currentInt = intensitySeries[0];
            double avgInt = avgIntensitySMA[0];
            double rVolInt = currentInt / Math.Max(0.1, avgInt);

            string dashText = $"Int RVol: {rVolInt:0.00} | Vol: {Volume[0]}";

            using (var tl = new DW.TextLayout(Core.Globals.DirectWriteFactory, dashText, dxFormat, 1000f, 1000f))
            {
                float pad = 6f;
                float w = (float)tl.Metrics.Width + pad * 2;
                float h = (float)tl.Metrics.Height + pad * 2;
                
                float x = ChartPanel.X;
                float y = ChartPanel.Y;

                if (DashCorner == TextPosition.TopRight) { x += ChartPanel.W - w - 10; y += 10; }
                else if (DashCorner == TextPosition.TopLeft) { x += 10; y += 10; }
                // Add bottom corners if needed

                var rect = new SDX.RectangleF(x, y, x + w, y + h);
                RenderTarget.FillRectangle(rect, dxBg);
                RenderTarget.DrawTextLayout(new SDX.Vector2(x + pad, y + pad), tl, dxText);
            }
        }

        public override void OnRenderTargetChanged()
        {
            if (dxText != null) { dxText.Dispose(); dxBg.Dispose(); dxFormat.Dispose(); }
            if (RenderTarget != null)
            {
                dxText = new D2D.SolidColorBrush(RenderTarget, SDX.Color.White);
                dxBg = new D2D.SolidColorBrush(RenderTarget, new SDX.Color4(0.1f, 0.1f, 0.1f, 0.8f));
                dxFormat = new DW.TextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", 14f) { TextAlignment = DW.TextAlignment.Leading };
            }
            base.OnRenderTargetChanged();
        }
        #endregion

        #region Properties
        [NinjaScriptProperty]
        [Display(Name="Show Dashboard", Order=1, GroupName="Dashboard")]
        public bool ShowDashboard { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Dashboard Corner", Order=2, GroupName="Dashboard")]
        public TextPosition DashCorner { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Q1 Max Multiplier", Order=1, GroupName="Intensity Thresholds")]
        public double Q1_MaxMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Q2 Max Multiplier", Order=2, GroupName="Intensity Thresholds")]
        public double Q2_MaxMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Q3 Max Multiplier", Order=3, GroupName="Intensity Thresholds")]
        public double Q3_MaxMultiplier { get; set; }

        // --- Brushes ---
        [XmlIgnore] [Display(Name="Bull Q1 (Weak)", GroupName="Colors Bullish")] public Brush BullQ1 { get; set; }
        [Browsable(false)] public string BullQ1Str { get { return Serialize.BrushToString(BullQ1); } set { BullQ1 = Serialize.StringToBrush(value); } }
        
        [XmlIgnore] [Display(Name="Bull Q2 (Normal)", GroupName="Colors Bullish")] public Brush BullQ2 { get; set; }
        [Browsable(false)] public string BullQ2Str { get { return Serialize.BrushToString(BullQ2); } set { BullQ2 = Serialize.StringToBrush(value); } }

        [XmlIgnore] [Display(Name="Bull Q3 (Strong)", GroupName="Colors Bullish")] public Brush BullQ3 { get; set; }
        [Browsable(false)] public string BullQ3Str { get { return Serialize.BrushToString(BullQ3); } set { BullQ3 = Serialize.StringToBrush(value); } }

        [XmlIgnore] [Display(Name="Bull Q4 (Climax)", GroupName="Colors Bullish")] public Brush BullQ4 { get; set; }
        [Browsable(false)] public string BullQ4Str { get { return Serialize.BrushToString(BullQ4); } set { BullQ4 = Serialize.StringToBrush(value); } }

        [XmlIgnore] [Display(Name="Bear Q1 (Weak)", GroupName="Colors Bearish")] public Brush BearQ1 { get; set; }
        [Browsable(false)] public string BearQ1Str { get { return Serialize.BrushToString(BearQ1); } set { BearQ1 = Serialize.StringToBrush(value); } }

        [XmlIgnore] [Display(Name="Bear Q2 (Normal)", GroupName="Colors Bearish")] public Brush BearQ2 { get; set; }
        [Browsable(false)] public string BearQ2Str { get { return Serialize.BrushToString(BearQ2); } set { BearQ2 = Serialize.StringToBrush(value); } }

        [XmlIgnore] [Display(Name="Bear Q3 (Strong)", GroupName="Colors Bearish")] public Brush BearQ3 { get; set; }
        [Browsable(false)] public string BearQ3Str { get { return Serialize.BrushToString(BearQ3); } set { BearQ3 = Serialize.StringToBrush(value); } }

        [XmlIgnore] [Display(Name="Bear Q4 (Climax)", GroupName="Colors Bearish")] public Brush BearQ4 { get; set; }
        [Browsable(false)] public string BearQ4Str { get { return Serialize.BrushToString(BearQ4); } set { BearQ4 = Serialize.StringToBrush(value); } }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private VolumeIntensityRenkoV2[] cacheVolumeIntensityRenkoV2;
		public VolumeIntensityRenkoV2 VolumeIntensityRenkoV2(bool showDashboard, TextPosition dashCorner, double q1_MaxMultiplier, double q2_MaxMultiplier, double q3_MaxMultiplier)
		{
			return VolumeIntensityRenkoV2(Input, showDashboard, dashCorner, q1_MaxMultiplier, q2_MaxMultiplier, q3_MaxMultiplier);
		}

		public VolumeIntensityRenkoV2 VolumeIntensityRenkoV2(ISeries<double> input, bool showDashboard, TextPosition dashCorner, double q1_MaxMultiplier, double q2_MaxMultiplier, double q3_MaxMultiplier)
		{
			if (cacheVolumeIntensityRenkoV2 != null)
				for (int idx = 0; idx < cacheVolumeIntensityRenkoV2.Length; idx++)
					if (cacheVolumeIntensityRenkoV2[idx] != null && cacheVolumeIntensityRenkoV2[idx].ShowDashboard == showDashboard && cacheVolumeIntensityRenkoV2[idx].DashCorner == dashCorner && cacheVolumeIntensityRenkoV2[idx].Q1_MaxMultiplier == q1_MaxMultiplier && cacheVolumeIntensityRenkoV2[idx].Q2_MaxMultiplier == q2_MaxMultiplier && cacheVolumeIntensityRenkoV2[idx].Q3_MaxMultiplier == q3_MaxMultiplier && cacheVolumeIntensityRenkoV2[idx].EqualsInput(input))
						return cacheVolumeIntensityRenkoV2[idx];
			return CacheIndicator<VolumeIntensityRenkoV2>(new VolumeIntensityRenkoV2(){ ShowDashboard = showDashboard, DashCorner = dashCorner, Q1_MaxMultiplier = q1_MaxMultiplier, Q2_MaxMultiplier = q2_MaxMultiplier, Q3_MaxMultiplier = q3_MaxMultiplier }, input, ref cacheVolumeIntensityRenkoV2);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.VolumeIntensityRenkoV2 VolumeIntensityRenkoV2(bool showDashboard, TextPosition dashCorner, double q1_MaxMultiplier, double q2_MaxMultiplier, double q3_MaxMultiplier)
		{
			return indicator.VolumeIntensityRenkoV2(Input, showDashboard, dashCorner, q1_MaxMultiplier, q2_MaxMultiplier, q3_MaxMultiplier);
		}

		public Indicators.VolumeIntensityRenkoV2 VolumeIntensityRenkoV2(ISeries<double> input , bool showDashboard, TextPosition dashCorner, double q1_MaxMultiplier, double q2_MaxMultiplier, double q3_MaxMultiplier)
		{
			return indicator.VolumeIntensityRenkoV2(input, showDashboard, dashCorner, q1_MaxMultiplier, q2_MaxMultiplier, q3_MaxMultiplier);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.VolumeIntensityRenkoV2 VolumeIntensityRenkoV2(bool showDashboard, TextPosition dashCorner, double q1_MaxMultiplier, double q2_MaxMultiplier, double q3_MaxMultiplier)
		{
			return indicator.VolumeIntensityRenkoV2(Input, showDashboard, dashCorner, q1_MaxMultiplier, q2_MaxMultiplier, q3_MaxMultiplier);
		}

		public Indicators.VolumeIntensityRenkoV2 VolumeIntensityRenkoV2(ISeries<double> input , bool showDashboard, TextPosition dashCorner, double q1_MaxMultiplier, double q2_MaxMultiplier, double q3_MaxMultiplier)
		{
			return indicator.VolumeIntensityRenkoV2(input, showDashboard, dashCorner, q1_MaxMultiplier, q2_MaxMultiplier, q3_MaxMultiplier);
		}
	}
}

#endregion
