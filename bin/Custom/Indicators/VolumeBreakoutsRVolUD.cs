#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using SWM = System.Windows.Media;               // WPF media alias
using NinjaTrader.Gui.Tools;
using NinjaTrader.Gui.Chart;                   // ChartControl / ChartScale
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;

// SharpDX aliases (avoid name collisions with WPF)
using SDX = SharpDX;                           // Vector2, RectangleF, Color4
using D2D = SharpDX.Direct2D1;                 // SolidColorBrush
using DW  = SharpDX.DirectWrite;               // TextFormat, TextLayout
#endregion

// VolumeBreakoutsRVolUD (compat build: no Serialize/Stroke/DrawTextOptions)
// - RVol with two thresholds (1.3/1.5 default-ready)
// - EMA(volume) line (orange by default)
// - U/D ratio over lookback
// - Optional strong/weak close gating (60% / 40%)
// - Movable dashboard: corner OR custom pixel X/Y (updates per tick if Calculate=OnEachTick)

namespace NinjaTrader.NinjaScript.Indicators
{
    internal static class BrushSerializer
    {
        public static string BrushToString(SWM.Brush b)
        {
            var scb = b as SWM.SolidColorBrush;
            var c   = scb?.Color ?? SWM.Colors.Gray;
            return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        public static SWM.Brush StringToBrush(string s)
        {
            try
            {
                var c  = (SWM.Color)SWM.ColorConverter.ConvertFromString(s);
                var br = new SWM.SolidColorBrush(c);
                if (br.CanFreeze) br.Freeze();
                return br;
            }
            catch
            {
                var br = new SWM.SolidColorBrush(SWM.Colors.Gray);
                if (br.CanFreeze) br.Freeze();
                return br;
            }
        }
    }

    public class VolumeBreakoutsRVolUD : Indicator
    {
        // --- Core calc
        private EMA volEma;
        private Series<double> upVol, dnVol;

        // --- Cached WPF brushes for bars
        private SWM.Brush upStrongBrush, upMedBrush, dnStrongBrush, dnMedBrush, neutralBrush;

        // --- Dashboard DX resources
        private D2D.SolidColorBrush dxText, dxRvolBg, dxUdBg;
        private DW.TextFormat       dxFormat;

        // ================== Inputs ==================
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name="EMA Length (Volume)", GroupName="RVol", Order=0)]
        public int EmaLength { get; set; } = 21;

        [NinjaScriptProperty, Range(1.0, 10.0)]
        [Display(Name="RVol Multiplier 1", GroupName="RVol", Order=1)]
        public double Multiplier1 { get; set; } = 1.30;

        [NinjaScriptProperty, Range(1.0, 10.0)]
        [Display(Name="RVol Multiplier 2", GroupName="RVol", Order=2)]
        public double Multiplier2 { get; set; } = 1.50;

        [NinjaScriptProperty, Range(1, 500)]
        [Display(Name="U/D Period", GroupName="U/D Ratio", Order=0)]
        public int UdPeriod { get; set; } = 21;

        [NinjaScriptProperty]
        [Display(Name="Use Strong/Weak Close Filter", GroupName="Bar Strength", Order=0)]
        public bool UseCloseStrength { get; set; } = false;

        [NinjaScriptProperty, Range(0.50, 0.95)]
        [Display(Name="Up Strong Threshold (0-1)", GroupName="Bar Strength", Order=1)]
        public double UpStrongThreshold { get; set; } = 0.60;

        [NinjaScriptProperty, Range(0.05, 0.50)]
        [Display(Name="Down Weak Threshold (0-1)", GroupName="Bar Strength", Order=2)]
        public double DownWeakThreshold { get; set; } = 0.40;

        // ----- Bar/line colors -----
        [XmlIgnore, Display(Name="Up Strong", GroupName="Colors", Order=0)]
        public SWM.Brush UpStrongColor { get; set; } = SWM.Brushes.LimeGreen;
        [Browsable(false)] public string UpStrongColorSerializable { get => BrushSerializer.BrushToString(UpStrongColor); set => UpStrongColor = BrushSerializer.StringToBrush(value); }

        [XmlIgnore, Display(Name="Up Medium", GroupName="Colors", Order=1)]
        public SWM.Brush UpMediumColor { get; set; } = SWM.Brushes.SteelBlue;
        [Browsable(false)] public string UpMediumColorSerializable { get => BrushSerializer.BrushToString(UpMediumColor); set => UpMediumColor = BrushSerializer.StringToBrush(value); }

        [XmlIgnore, Display(Name="Down Strong", GroupName="Colors", Order=2)]
        public SWM.Brush DownStrongColor { get; set; } = SWM.Brushes.Red;
        [Browsable(false)] public string DownStrongColorSerializable { get => BrushSerializer.BrushToString(DownStrongColor); set => DownStrongColor = BrushSerializer.StringToBrush(value); }

        [XmlIgnore, Display(Name="Down Medium", GroupName="Colors", Order=3)]
        public SWM.Brush DownMediumColor { get; set; } = SWM.Brushes.DeepPink;
        [Browsable(false)] public string DownMediumColorSerializable { get => BrushSerializer.BrushToString(DownMediumColor); set => DownMediumColor = BrushSerializer.StringToBrush(value); }

        [XmlIgnore, Display(Name="Neutral/Low", GroupName="Colors", Order=4)]
        public SWM.Brush NeutralColor { get; set; } = SWM.Brushes.Gray;
        [Browsable(false)] public string NeutralColorSerializable { get => BrushSerializer.BrushToString(NeutralColor); set => NeutralColor = BrushSerializer.StringToBrush(value); }

        [XmlIgnore, Display(Name="EMA Color", GroupName="Colors", Order=5)]
        public SWM.Brush EmaColor { get; set; } = SWM.Brushes.Orange;
        [Browsable(false)] public string EmaColorSerializable { get => BrushSerializer.BrushToString(EmaColor); set => EmaColor = BrushSerializer.StringToBrush(value); }

        // ----- Dashboard options -----
        [NinjaScriptProperty]
        [Display(Name="Show Dashboard", GroupName="Dashboard", Order=0)]
        public bool ShowDashboard { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Use Custom XY (else Corner)", GroupName="Dashboard", Order=1)]
        public bool UseCustomXY { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name="Corner", GroupName="Dashboard", Order=2)]
        public TextPosition Corner { get; set; } = TextPosition.TopLeft;

        [NinjaScriptProperty, Range(-5000, 5000)]
        [Display(Name="X Offset (px)", GroupName="Dashboard", Order=3)]
        public int DashX { get; set; } = 12;

        [NinjaScriptProperty, Range(-5000, 5000)]
        [Display(Name="Y Offset (px)", GroupName="Dashboard", Order=4)]
        public int DashY { get; set; } = 8;

        [XmlIgnore, Display(Name="Text Color", GroupName="Dashboard", Order=5)]
        public SWM.Brush DashText { get; set; } = SWM.Brushes.White;
        [Browsable(false)] public string DashTextSerializable { get => BrushSerializer.BrushToString(DashText); set => DashText = BrushSerializer.StringToBrush(value); }

        [XmlIgnore, Display(Name="RVol Box BG", GroupName="Dashboard", Order=6)]
        public SWM.Brush RvolBg { get; set; } = new SWM.SolidColorBrush(SWM.Color.FromArgb(190, 255, 140, 0));
        [Browsable(false)] public string RvolBgSerializable { get => BrushSerializer.BrushToString(RvolBg); set => RvolBg = BrushSerializer.StringToBrush(value); }

        [XmlIgnore, Display(Name="U/D Box BG", GroupName="Dashboard", Order=7)]
        public SWM.Brush UdBg { get; set; } = new SWM.SolidColorBrush(SWM.Color.FromArgb(190, 0, 130, 0));
        [Browsable(false)] public string UdBgSerializable { get => BrushSerializer.BrushToString(UdBg); set => UdBg = BrushSerializer.StringToBrush(value); }

        // =============== Exposed values ===============
        [Browsable(false), XmlIgnore] public Series<double> RVolSeries { get; private set; }
        [Browsable(false)] public double UDRatio { get; private set; }

        // ================= Lifecycle =================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Relative Volume + U/D Ratio with colored volume bars, EMA(volume), and a movable dashboard.";
                Name = "VolumeBreakoutsRVolUD";
                Calculate = Calculate.OnBarClose;         // set to On each tick in UI for per-tick updates
                IsOverlay = false;
                DrawOnPricePanel = false;
                IsSuspendedWhileInactive = true;

                AddPlot(SWM.Brushes.Gray,   "Volume");
                AddPlot(SWM.Brushes.Orange, "VolEMA");
                Plots[0].PlotStyle = PlotStyle.Bar;  Plots[0].Width = 2;
                Plots[1].PlotStyle = PlotStyle.Line; Plots[1].Width = 2;
            }
            else if (State == State.DataLoaded)
            {
                volEma  = EMA(VOL(), EmaLength);
                upVol   = new Series<double>(this);
                dnVol   = new Series<double>(this);
                RVolSeries = new Series<double>(this);

                // cache WPF brushes
                upStrongBrush = UpStrongColor.Clone();  if (upStrongBrush.CanFreeze) upStrongBrush.Freeze();
                upMedBrush    = UpMediumColor.Clone();  if (upMedBrush.CanFreeze)    upMedBrush.Freeze();
                dnStrongBrush = DownStrongColor.Clone();if (dnStrongBrush.CanFreeze) dnStrongBrush.Freeze();
                dnMedBrush    = DownMediumColor.Clone();if (dnMedBrush.CanFreeze)    dnMedBrush.Freeze();
                neutralBrush  = NeutralColor.Clone();   if (neutralBrush.CanFreeze)  neutralBrush.Freeze();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
            {
                Values[0][0] = VOL()[0];
                Values[1][0] = VOL()[0];
                return;
            }

            if (volEma == null || volEma.Period != EmaLength)
                volEma = EMA(VOL(), EmaLength);

            double vol = VOL()[0];
            double ema = volEma[0];

            Values[0][0] = vol;
            Values[1][0] = ema;

            double denom = Math.Abs(ema) < 1e-9 ? 1e-9 : ema;
            double rvol  = vol / denom;
            RVolSeries[0] = rvol;

            bool isUp = Close[0] >= Close[1];

            bool allowStrong = true;
            if (UseCloseStrength && High[0] > Low[0])
            {
                double pos = (Close[0] - Low[0]) / Math.Max(High[0] - Low[0], TickSize);
                if (isUp  && pos < UpStrongThreshold) allowStrong = false;
                if (!isUp && pos > DownWeakThreshold) allowStrong = false;
            }

            SWM.Brush barBrush = neutralBrush;
            if (rvol >= Multiplier2 && allowStrong) barBrush = isUp ? upStrongBrush : dnStrongBrush;
            else if (rvol >= Multiplier1)           barBrush = isUp ? upMedBrush    : dnMedBrush;

            PlotBrushes[0][0] = barBrush;
            PlotBrushes[1][0] = EmaColor;

            upVol[0] = isUp ? vol : 0.0;
            dnVol[0] = isUp ? 0.0 : vol;

            double sumUp = SUM(upVol, UdPeriod)[0];
            double sumDn = SUM(dnVol, UdPeriod)[0];
            UDRatio = sumDn <= 0 ? double.NaN : sumUp / sumDn;
        }

        // ================= Custom Rendering =================
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (!ShowDashboard || RenderTarget == null || ChartPanel == null)
                return;

            string s1, s2;
            if (CurrentBar < 1)
            {
                s1 = "RVol: --";
                s2 = "U/D: --";
            }
            else
            {
                double r = double.IsNaN(RVolSeries[0]) ? 0.0 : RVolSeries[0];
                double u = double.IsNaN(UDRatio) ? 0.0 : UDRatio;
                s1 = $"RVol: {r:0.00}";
                s2 = $"U/D: {u:0.00}";
            }

            using (var tl1 = new DW.TextLayout(Core.Globals.DirectWriteFactory, s1, dxFormat, 1000f, 1000f))
            using (var tl2 = new DW.TextLayout(Core.Globals.DirectWriteFactory, s2, dxFormat, 1000f, 1000f))
            {
                float padX = 8f, padY = 5f, gap = 6f;
                float h  = (float)Math.Max(tl1.Metrics.Height, tl2.Metrics.Height) + padY * 2f;
                float w1 = (float)tl1.Metrics.Width + padX * 2f;
                float w2 = (float)tl2.Metrics.Width + padX * 2f;
                float totalW = w1 + gap + w2;

                float x = ChartPanel.X, y = ChartPanel.Y;

                if (UseCustomXY)
                {
                    x += DashX; y += DashY;
                }
                else
                {
                    switch (Corner)
                    {
                        case TextPosition.TopLeft:     x += DashX; y += DashY; break;
                        case TextPosition.TopRight:    x += ChartPanel.W - totalW - Math.Abs(DashX); y += DashY; break;
                        case TextPosition.BottomLeft:  x += DashX; y += ChartPanel.H - h - Math.Abs(DashY); break;
                        case TextPosition.BottomRight: x += ChartPanel.W - totalW - Math.Abs(DashX); y += ChartPanel.H - h - Math.Abs(DashY); break;
                    }
                }

                var r1 = new SDX.RectangleF(x, y, x + w1, y + h);
                var r2 = new SDX.RectangleF(x + w1 + gap, y, x + w1 + gap + w2, y + h);

                RenderTarget.FillRectangle(r1, dxRvolBg);
                RenderTarget.FillRectangle(r2, dxUdBg);

                RenderTarget.DrawTextLayout(new SDX.Vector2(x + padX, y + padY), tl1, dxText);
                RenderTarget.DrawTextLayout(new SDX.Vector2(x + w1 + gap + padX, y + padY), tl2, dxText);
            }
        }

        // Match access level of your NT8 base (public on some builds)
        public override void OnRenderTargetChanged()
        {
            DisposeDx();
            if (RenderTarget != null)
            {
                dxText   = DxBrushFromWpf(DashText);
                dxRvolBg = DxBrushFromWpf(RvolBg);
                dxUdBg   = DxBrushFromWpf(UdBg);
                dxFormat = new DW.TextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", 13f);
            }
            base.OnRenderTargetChanged();
        }

        private D2D.SolidColorBrush DxBrushFromWpf(SWM.Brush b)
        {
            var c = (b as SWM.SolidColorBrush)?.Color ?? SWM.Colors.White;
            return new D2D.SolidColorBrush(RenderTarget, new SDX.Color4(c.ScR, c.ScG, c.ScB, c.ScA));
        }

        private void DisposeDx()
        {
            dxText?.Dispose();   dxText = null;
            dxRvolBg?.Dispose(); dxRvolBg = null;
            dxUdBg?.Dispose();   dxUdBg = null;
            dxFormat?.Dispose(); dxFormat = null;
        }

        #region Exposed series
        [Browsable(false), XmlIgnore] public Series<double> VolumePlot    => Values[0];
        [Browsable(false), XmlIgnore] public Series<double> VolumeEmaPlot => Values[1];
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private VolumeBreakoutsRVolUD[] cacheVolumeBreakoutsRVolUD;
		public VolumeBreakoutsRVolUD VolumeBreakoutsRVolUD(int emaLength, double multiplier1, double multiplier2, int udPeriod, bool useCloseStrength, double upStrongThreshold, double downWeakThreshold, bool showDashboard, bool useCustomXY, TextPosition corner, int dashX, int dashY)
		{
			return VolumeBreakoutsRVolUD(Input, emaLength, multiplier1, multiplier2, udPeriod, useCloseStrength, upStrongThreshold, downWeakThreshold, showDashboard, useCustomXY, corner, dashX, dashY);
		}

		public VolumeBreakoutsRVolUD VolumeBreakoutsRVolUD(ISeries<double> input, int emaLength, double multiplier1, double multiplier2, int udPeriod, bool useCloseStrength, double upStrongThreshold, double downWeakThreshold, bool showDashboard, bool useCustomXY, TextPosition corner, int dashX, int dashY)
		{
			if (cacheVolumeBreakoutsRVolUD != null)
				for (int idx = 0; idx < cacheVolumeBreakoutsRVolUD.Length; idx++)
					if (cacheVolumeBreakoutsRVolUD[idx] != null && cacheVolumeBreakoutsRVolUD[idx].EmaLength == emaLength && cacheVolumeBreakoutsRVolUD[idx].Multiplier1 == multiplier1 && cacheVolumeBreakoutsRVolUD[idx].Multiplier2 == multiplier2 && cacheVolumeBreakoutsRVolUD[idx].UdPeriod == udPeriod && cacheVolumeBreakoutsRVolUD[idx].UseCloseStrength == useCloseStrength && cacheVolumeBreakoutsRVolUD[idx].UpStrongThreshold == upStrongThreshold && cacheVolumeBreakoutsRVolUD[idx].DownWeakThreshold == downWeakThreshold && cacheVolumeBreakoutsRVolUD[idx].ShowDashboard == showDashboard && cacheVolumeBreakoutsRVolUD[idx].UseCustomXY == useCustomXY && cacheVolumeBreakoutsRVolUD[idx].Corner == corner && cacheVolumeBreakoutsRVolUD[idx].DashX == dashX && cacheVolumeBreakoutsRVolUD[idx].DashY == dashY && cacheVolumeBreakoutsRVolUD[idx].EqualsInput(input))
						return cacheVolumeBreakoutsRVolUD[idx];
			return CacheIndicator<VolumeBreakoutsRVolUD>(new VolumeBreakoutsRVolUD(){ EmaLength = emaLength, Multiplier1 = multiplier1, Multiplier2 = multiplier2, UdPeriod = udPeriod, UseCloseStrength = useCloseStrength, UpStrongThreshold = upStrongThreshold, DownWeakThreshold = downWeakThreshold, ShowDashboard = showDashboard, UseCustomXY = useCustomXY, Corner = corner, DashX = dashX, DashY = dashY }, input, ref cacheVolumeBreakoutsRVolUD);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.VolumeBreakoutsRVolUD VolumeBreakoutsRVolUD(int emaLength, double multiplier1, double multiplier2, int udPeriod, bool useCloseStrength, double upStrongThreshold, double downWeakThreshold, bool showDashboard, bool useCustomXY, TextPosition corner, int dashX, int dashY)
		{
			return indicator.VolumeBreakoutsRVolUD(Input, emaLength, multiplier1, multiplier2, udPeriod, useCloseStrength, upStrongThreshold, downWeakThreshold, showDashboard, useCustomXY, corner, dashX, dashY);
		}

		public Indicators.VolumeBreakoutsRVolUD VolumeBreakoutsRVolUD(ISeries<double> input , int emaLength, double multiplier1, double multiplier2, int udPeriod, bool useCloseStrength, double upStrongThreshold, double downWeakThreshold, bool showDashboard, bool useCustomXY, TextPosition corner, int dashX, int dashY)
		{
			return indicator.VolumeBreakoutsRVolUD(input, emaLength, multiplier1, multiplier2, udPeriod, useCloseStrength, upStrongThreshold, downWeakThreshold, showDashboard, useCustomXY, corner, dashX, dashY);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.VolumeBreakoutsRVolUD VolumeBreakoutsRVolUD(int emaLength, double multiplier1, double multiplier2, int udPeriod, bool useCloseStrength, double upStrongThreshold, double downWeakThreshold, bool showDashboard, bool useCustomXY, TextPosition corner, int dashX, int dashY)
		{
			return indicator.VolumeBreakoutsRVolUD(Input, emaLength, multiplier1, multiplier2, udPeriod, useCloseStrength, upStrongThreshold, downWeakThreshold, showDashboard, useCustomXY, corner, dashX, dashY);
		}

		public Indicators.VolumeBreakoutsRVolUD VolumeBreakoutsRVolUD(ISeries<double> input , int emaLength, double multiplier1, double multiplier2, int udPeriod, bool useCloseStrength, double upStrongThreshold, double downWeakThreshold, bool showDashboard, bool useCustomXY, TextPosition corner, int dashX, int dashY)
		{
			return indicator.VolumeBreakoutsRVolUD(input, emaLength, multiplier1, multiplier2, udPeriod, useCloseStrength, upStrongThreshold, downWeakThreshold, showDashboard, useCustomXY, corner, dashX, dashY);
		}
	}
}

#endregion
