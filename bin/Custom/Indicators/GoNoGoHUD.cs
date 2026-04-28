#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;

using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;

using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;

using System.Windows.Media;                         // WPF Brushes
using NinjaTrader.NinjaScript.DrawingTools;         // Draw.Text, Draw.VerticalLine
using NinjaTrader.Gui.Tools;                        // SimpleFont
#endregion

// Avoid WPF/SharpDX brush ambiguity
using DXSolidColorBrush = SharpDX.Direct2D1.SolidColorBrush;

// Put enum in parent namespace so all wrappers can see it
namespace NinjaTrader.NinjaScript
{
    public enum GoHudDockSide { Top, Bottom }
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GoNoGoHUD : Indicator
    {
        // ==============================
        // USER PARAMETERS
        // ==============================
        [NinjaScriptProperty, Range(0,100)]
        [Display(Name="Go Threshold %", GroupName="GO/NO-GO", Order=0)]
        public int GoThreshold { get; set; }

        [NinjaScriptProperty, Range(0,100)]
        [Display(Name="Yellow Floor %", GroupName="GO/NO-GO", Order=1)]
        public int YellowFloor { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Use First-Hour Micro (09:30–09:45)", GroupName="Windows", Order=0)]
        public bool UseFirstHourMicro { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Restrict To Windows (IB/Balance/Primary)", GroupName="Windows", Order=1)]
        public bool RestrictToWindows { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Require ADX Gate", GroupName="Gates", Order=0)]
        public bool RequireAdxGate { get; set; }

        [NinjaScriptProperty, Range(5,100)]
        [Display(Name="ADX Gate", GroupName="Gates", Order=1)]
        public int AdxGate { get; set; }

        [NinjaScriptProperty, Range(5,100)]
        [Display(Name="Chop N", GroupName="Features", Order=0)]
        public int ChopN { get; set; }

        [NinjaScriptProperty, Range(5,200)]
        [Display(Name="RVol Lookback", GroupName="Features", Order=1)]
        public int RVolLookback { get; set; }

        [NinjaScriptProperty] [Display(Name="Enable VWAP Alignment", GroupName="Features", Order=2)]
        public bool UseVWAP { get; set; }

        [NinjaScriptProperty] [Display(Name="Enable RVol", GroupName="Features", Order=3)]
        public bool UseRVol { get; set; }

        [NinjaScriptProperty] [Display(Name="Enable U/D Volume Proxy", GroupName="Features", Order=4)]
        public bool UseUD { get; set; }

        [NinjaScriptProperty] [Display(Name="Enable ADX/Slope", GroupName="Features", Order=5)]
        public bool UseAdxSlope { get; set; }

        [NinjaScriptProperty] [Display(Name="Enable Chop Index", GroupName="Features", Order=6)]
        public bool UseChop { get; set; }

        [NinjaScriptProperty] [Display(Name="Enable Regime (Trend/Balance)", GroupName="Features", Order=7)]
        public bool UseRegime { get; set; }

        [NinjaScriptProperty] [Display(Name="Enable Levels (placeholder)", GroupName="Features", Order=8)]
        public bool UseLevels { get; set; }

        // Bias selector: -1 = ShortOnly, 0 = Auto, +1 = LongOnly
        [NinjaScriptProperty, Range(-1, 1)]
        [Display(Name="Bias Mode (-1 Short, 0 Auto, +1 Long)", GroupName="GO/NO-GO", Order=2)]
        public int BiasMode { get; set; }

        [NinjaScriptProperty, Range(0, 300)]
        [Display(Name="Alert Cooldown (sec)", GroupName="GO/NO-GO", Order=3)]
        public int AlertCooldownSec { get; set; }

        // HUD appearance
        [NinjaScriptProperty]
        [Display(Name="Dock", GroupName="HUD", Order=0, Description="Dock the ribbon at top or bottom of panel")]
        public GoHudDockSide Dock { get; set; }

        [NinjaScriptProperty, Range(24, 140)]
        [Display(Name="HUD Height (px)", GroupName="HUD", Order=1)]
        public int HudHeightPx { get; set; }

        [NinjaScriptProperty, Range(0.50, 1.00)]
        [Display(Name="HUD Opacity (0.50-1.00)", GroupName="HUD", Order=2)]
        public double HudOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Show Debug Text", GroupName="HUD", Order=3)]
        public bool ShowDebugText { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Use Fallback Only (no SharpDX)", GroupName="HUD", Order=4)]
        public bool UseFallbackOnly { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Show Beacon (debug)", GroupName="HUD", Order=5)]
        public bool ShowBeacon { get; set; }

        // Weights (v0 defaults — calibrate later)
        [NinjaScriptProperty] [Display(Name="w_RVol",   GroupName="Weights", Order=0)] public double W_RVol   { get; set; }
        [NinjaScriptProperty] [Display(Name="w_UD",     GroupName="Weights", Order=1)] public double W_UD     { get; set; }
        [NinjaScriptProperty] [Display(Name="w_ADXSL",  GroupName="Weights", Order=2)] public double W_ADXSL  { get; set; }
        [NinjaScriptProperty] [Display(Name="w_CHOP",   GroupName="Weights", Order=3)] public double W_CHOP   { get; set; }
        [NinjaScriptProperty] [Display(Name="w_VWAP",   GroupName="Weights", Order=4)] public double W_VWAP   { get; set; }
        [NinjaScriptProperty] [Display(Name="w_Regime", GroupName="Weights", Order=5)] public double W_Regime { get; set; }
        [NinjaScriptProperty] [Display(Name="Bias",     GroupName="Weights", Order=6)] public double Bias     { get; set; }

        // ==============================
        // INTERNALS
        // ==============================
        private ADX adx;
        private EMA emaFast, emaSlow;
        private SMA volSma;
        private Series<double> chopSeries;

        // session VWAP we roll ourselves
        private Series<double> vwapSeries;
        private double cumPV, cumVol;

        private DateTime lastAlert = Core.Globals.MinDate;

        // DX render resources
        private DXSolidColorBrush dxGreen, dxYellow, dxRed, dxBack, dxBorder, dxText;
        private TextFormat tfLarge, tfSmall;

        // Font for fallback Draw.Text
        private SimpleFont wpfFont = new SimpleFont("Segoe UI Semibold", 18) { Bold = true };

        // Layout
        private float hudHeight = 60f; // set from HudHeightPx
        private readonly float tileW = 28f;
        private readonly float tileGap = 10f;
        private readonly float pad = 12f;

        // ==============================
        // LIFECYCLE
        // ==============================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "GoNoGoHUD";
                Calculate                = Calculate.OnEachTick;
                IsOverlay                = true;
                DisplayInDataBox         = false;
                IsSuspendedWhileInactive = true;

                GoThreshold      = 70;
                YellowFloor      = 50;
                UseFirstHourMicro= true;
                RestrictToWindows= false;
                RequireAdxGate   = false;
                AdxGate          = 18;

                ChopN        = 14;
                RVolLookback = 50;

                UseVWAP   = true; UseRVol = true; UseUD = true;
                UseAdxSlope = true; UseChop = true; UseRegime = true; UseLevels = false;

                BiasMode = 0; // Auto
                AlertCooldownSec = 90;

                Dock = GoHudDockSide.Top;
                HudHeightPx = 80;
                HudOpacity  = 0.95;
                ShowDebugText = false;
                UseFallbackOnly = false;
                ShowBeacon = true;

                W_RVol = 0.8; W_UD = 0.5; W_ADXSL = 1.0; W_CHOP = 0.6; W_VWAP = 0.9; W_Regime = 0.8; Bias = -0.5;

                AddPlot(Brushes.Transparent, "Probability");
            }
            else if (State == State.DataLoaded)
            {
                adx        = ADX(14);
                emaFast    = EMA(8);
                emaSlow    = EMA(24);
                volSma     = SMA(Volume, RVolLookback);
                chopSeries = new Series<double>(this);
                vwapSeries = new Series<double>(this);
                cumPV = 0; cumVol = 0;

                hudHeight = Math.Max(24, Math.Min(140, HudHeightPx));
            }
            else if (State == State.Terminated)
            {
                DisposeDx();
            }
        }

        // ==============================
        // CORE UPDATE
        // ==============================
        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(ChopN + 5, Math.Max(RVolLookback + 5, 25)))
            {
                Values[0][0] = 0;
                return;
            }

            // Session-reset VWAP (typical price × volume)
            if (Bars.IsFirstBarOfSession)
            {
                cumPV = 0; cumVol = 0;
            }
            double tp = (High[0] + Low[0] + Close[0]) / 3.0;
            cumPV  += tp * Volume[0];
            cumVol += Math.Max(1.0, Volume[0]);
            vwapSeries[0] = cumPV / cumVol;

            // features
            double sRvol   = UseRVol    ? ScoreRVol()     : 0.0;
            double sUD     = UseUD      ? ScoreUD()       : 0.0;
            double sAdxSl  = UseAdxSlope? ScoreAdxSlope() : 0.0;
            double sChop   = UseChop    ? ScoreChop()     : 0.0;
            double sVwap   = UseVWAP    ? ScoreVWAP()     : 0.0;
            double sRegime = UseRegime  ? ScoreRegime()   : 0.0;

            bool inWindow = InTradingWindows(Time[0]);
            bool windowOk = !RestrictToWindows || inWindow;
            bool adxOk    = !RequireAdxGate || adx[0] >= AdxGate;

            double z = Bias
                     + W_RVol   * sRvol
                     + W_UD     * sUD
                     + W_ADXSL  * sAdxSl
                     + W_CHOP   * sChop
                     + W_VWAP   * sVwap
                     + W_Regime * sRegime;

            if (inWindow) z += 0.2;

            double p = 100.0 * (1.0 / (1.0 + Math.Exp(-z)));
            Values[0][0] = p;

            bool gatesOk = windowOk && adxOk;
            bool goNow   = p >= GoThreshold && gatesOk;

            // ---- ALWAYS-ON FALLBACK (visible even if SharpDX doesn't render) ----
            string gateWin = inWindow ? "WIN:ON" : "WIN:OFF";
            string gateAdx = adxOk   ? "ADX:OK" : "ADX:NO";
            string txt = string.Format("GO {0:0}%  |  {1}  |  {2}", p, gateWin, gateAdx);

            // Anchor at last bar; autoscale so it is always in view even on blank charts
            var hint = Draw.Text(this, "GoNoGoHUD_fallback", txt, 0, Close[0], Brushes.White);
			hint.Font        = wpfFont;
			hint.IsAutoScale = true;


            if (ShowBeacon)
                Draw.VerticalLine(this, "GoNoGoHUD_beacon", 0, Brushes.Lime);

            // ---- Alert when GO ----
            if (goNow && (Time[0] - lastAlert).TotalSeconds >= AlertCooldownSec)
            {
                string msg = string.Format("GO: p={0:F0}%, ADX={1:F1}, Regime={2}, Window={3}",
                                           p, adx[0], CurrentRegime(), inWindow ? "ON" : "OFF");
                Alert("GoNoGoHUD_GO", Priority.High, msg, "Alert1.wav", 10,
                      Brushes.White, Brushes.DarkGreen);
                lastAlert = Time[0];
            }
        }

        // ==============================
        // FEATURE SCORING
        // ==============================
        private double ScoreRVol()
        {
            double avg = Math.Max(1.0, volSma[0]);
            double rv  = Volume[0] / avg;
            double s   = (rv - 0.5) / 1.5;                 // 0.5x→0, 2.0x→1
            s = Math.Max(0, Math.Min(1, s));
            return 2 * s - 1;                              // [-1,1]
        }

        private double ScoreUD()
        {
            double sign = Close[0] > Open[0] ? 1 : (Close[0] < Open[0] ? -1 : 0);
            double avg  = Math.Max(1.0, volSma[0]);
            double volN = Math.Min(2.0, Volume[0] / avg);
            double s    = sign * Math.Min(1.0, (volN - 0.5) / 0.5);
            return s;                                      // [-1,1]
        }

        // Direction from EMA(8/24); strength from ADX
        private double ScoreAdxSlope()
        {
            double dir = DirectionSign();                  // -1..+1
            double adxFac = Math.Min(1.0, adx[0] / 40.0);  // 0..1
            return dir * adxFac;                           // [-1,1]
        }

        private double ScoreChop()
        {
            int n = ChopN;
            double hh = MAX(High, n)[0];
            double ll = MIN(Low,  n)[0];
            double range = Math.Max(TickSize, hh - ll);
            double sumTr = ATR(n)[0] * n;                  // approximation

            double chop = 100.0 * Math.Log10(sumTr / range) / Math.Log10(n);
            chop = Math.Max(0, Math.Min(100, chop));
            chopSeries[0] = chop;

            double s = 1.0 - (chop - 10.0) / 90.0;         // lower chop → better trend
            s = Math.Max(-1, Math.Min(1, s * 2 - 1));
            return s;
        }

        private double ScoreVWAP()
        {
            double distTicks = (Close[0] - vwapSeries[0]) / TickSize;
            double aligned = DirectionSign() * distTicks;  // positive = aligned
            return Math.Max(-1, Math.Min(1, aligned / 10.0));
        }

        private double ScoreRegime()
        {
            double s = (adx[0] - 15.0) / 15.0;
            return Math.Max(-1, Math.Min(1, s));
        }

        // -1..+1 based on bias + EMA slope/position
        private int HardBias()
        {
            if (BiasMode > 0) return  1;
            if (BiasMode < 0) return -1;
            return 0; // auto
        }

        private double DirectionSign()
        {
            int hb = HardBias();
            if (hb != 0) return hb;

            double slope = emaFast[0] - emaFast[1];
            double pos   = emaFast[0] - emaSlow[0];
            double s = 0.6 * Math.Sign(pos) + 0.4 * Math.Sign(slope);
            if (s > 0) return 1;
            if (s < 0) return -1;
            return 0;
        }

        private string CurrentRegime() => adx[0] >= 20 ? "TREND" : "BAL";

        private bool InTradingWindows(DateTime t)
        {
            var tod = t.TimeOfDay;
            bool firstMicro = UseFirstHourMicro && tod >= new TimeSpan(9,30,0) && tod < new TimeSpan(9,45,0);
            bool ibBuild    =                      tod >= new TimeSpan(9,30,0) && tod < new TimeSpan(10,00,0);
            bool balance    =                      tod >= new TimeSpan(10,00,0) && tod < new TimeSpan(10,15,0);
            bool primary    =                      tod >= new TimeSpan(10,15,0) && tod < new TimeSpan(11,45,0);
            return firstMicro || ibBuild || balance || primary;
        }

        // ==============================
        // RENDERING (SharpDX ribbon)
        // ==============================
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (UseFallbackOnly) return; // user-chosen: skip DX drawing

            if (chartControl == null || ChartPanel == null || BarsArray[0] == null)
                return;

            EnsureDx();
            if (dxBack == null || tfLarge == null || RenderTarget == null)
                return;

            double p = (CurrentBar > 0) ? Values[0][0] : 0.0;

            // panel-relative
            float px = ChartPanel.X;
            float pw = ChartPanel.W;
            float py = (Dock == GoHudDockSide.Top)
                ? ChartPanel.Y + 1f
                : ChartPanel.Y + ChartPanel.H - hudHeight - 1f;

            // background ribbon + border
            RenderTarget.FillRectangle(new RectangleF(px, py, pw, hudHeight), dxBack);
            RenderTarget.DrawRectangle(new RectangleF(px + 0.5f, py + 0.5f, pw - 1f, hudHeight - 1f), dxBorder, 1.0f);

            // headline color
            var pColor = p >= GoThreshold ? dxGreen : (p >= YellowFloor ? dxYellow : dxRed);

            // LEFT: big "GO xx%"
            RenderTarget.DrawText($"GO {p,3:0}%", tfLarge, new RectangleF(px + pad, py, 190f, hudHeight), pColor);

            // RIGHT: tiles (participation columns)
            float cursor = px + pw - pad;

            Action<double> tile = (val) =>
            {
                cursor -= tileW;
                var c = val >= 0.5 ? dxGreen : (val >= 0.0 ? dxYellow : dxRed);
                RenderTarget.FillRectangle(new RectangleF(cursor, py + (hudHeight - tileW) / 2f, tileW, tileW), c);
                cursor -= tileGap;
            };

            // Scores normalized to [0..1] for coloring
            double nRVol   = UseRVol    ? 0.5 * (ScoreRVol()     + 1) : -1;
            double nUD     = UseUD      ? 0.5 * (ScoreUD()       + 1) : -1;
            double nAdxSl  = UseAdxSlope? 0.5 * (ScoreAdxSlope() + 1) : -1;
            double nChop   = UseChop    ? 0.5 * (ScoreChop()     + 1) : -1;
            double nVWAP   = UseVWAP    ? 0.5 * (ScoreVWAP()     + 1) : -1;
            double nRegime = UseRegime  ? 0.5 * (ScoreRegime()   + 1) : -1;

            if (UseRegime)   tile(nRegime);
            if (UseVWAP)     tile(nVWAP);
            if (UseChop)     tile(nChop);
            if (UseAdxSlope) tile(nAdxSl);
            if (UseUD)       tile(nUD);
            if (UseRVol)     tile(nRVol);

            // GATE bars + final GO
            bool inWin = InTradingWindows(Time[0]);
            bool adxOk = !RequireAdxGate || adx[0] >= AdxGate;

            DrawGateBar(cursor -= (tileGap + 6f), py, inWin ? dxGreen : dxRed);
            DrawGateBar(cursor -= (tileGap + 6f), py, adxOk ? dxGreen : dxRed);

            cursor -= (tileGap + tileW + 6f);
            var goBrush = (p >= GoThreshold && inWin && adxOk) ? dxGreen : (p >= YellowFloor ? dxYellow : dxRed);
            float goW = tileW + 8f;
            RenderTarget.FillRectangle(new RectangleF(cursor, py + (hudHeight - goW) / 2f, goW, goW), goBrush);

            // Optional debug text
            if (ShowDebugText && tfSmall != null && dxText != null)
            {
                string dbg = $"ADX {adx[0]:0.0} | Reg {CurrentRegime()} | Win {(inWin ? "ON" : "OFF")}";
                RenderTarget.DrawText(dbg, tfSmall, new RectangleF(px + pw - 360f, py + 2f, 350f, hudHeight - 4f), dxText);
            }
        }

        private void DrawGateBar(float x, float py, DXSolidColorBrush color)
        {
            float w = 6f, h = hudHeight - 12f;
            RenderTarget.FillRectangle(new RectangleF(x, py + 6f, w, h), color);
        }

        public override void OnRenderTargetChanged()
        {
            DisposeDx();
            base.OnRenderTargetChanged();
        }

        private void EnsureDx()
        {
            if (RenderTarget == null || dxGreen != null) return;

            float a = (float)Math.Max(0.5, Math.Min(1.0, HudOpacity));

            dxGreen  = new DXSolidColorBrush(RenderTarget, new Color4(0.10f, 0.70f, 0.10f, a));
            dxYellow = new DXSolidColorBrush(RenderTarget, new Color4(0.95f, 0.80f, 0.10f, a));
            dxRed    = new DXSolidColorBrush(RenderTarget, new Color4(0.85f, 0.22f, 0.22f, a));
            dxBack   = new DXSolidColorBrush(RenderTarget, new Color4(0.22f, 0.22f, 0.26f, a));
            dxBorder = new DXSolidColorBrush(RenderTarget, new Color4(0.95f, 0.95f, 0.95f, Math.Min(1f, a)));
            dxText   = new DXSolidColorBrush(RenderTarget, new Color4(0.95f, 0.95f, 0.95f, 1f));

            tfLarge  = new TextFormat(Core.Globals.DirectWriteFactory, "Segoe UI Semibold", 22);
            tfSmall  = new TextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", 14);

            hudHeight = Math.Max(24, Math.Min(140, HudHeightPx));
        }

        private void DisposeDx()
        {
            tfLarge?.Dispose(); tfLarge = null;
            tfSmall?.Dispose(); tfSmall = null;

            dxGreen?.Dispose();  dxGreen = null;
            dxYellow?.Dispose(); dxYellow = null;
            dxRed?.Dispose();    dxRed = null;
            dxBack?.Dispose();   dxBack = null;
            dxBorder?.Dispose(); dxBorder = null;
            dxText?.Dispose();   dxText = null;
        }

        // ==============================
        // EXPOSED SERIES
        // ==============================
        [Browsable(false), XmlIgnore] public Series<double> Probability => Values[0];
        [Browsable(false), XmlIgnore] public Series<double> ChopValue  => chopSeries;
        [Browsable(false), XmlIgnore] public Series<double> VWAPValue  => vwapSeries;
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GoNoGoHUD[] cacheGoNoGoHUD;
		public GoNoGoHUD GoNoGoHUD(int goThreshold, int yellowFloor, bool useFirstHourMicro, bool restrictToWindows, bool requireAdxGate, int adxGate, int chopN, int rVolLookback, bool useVWAP, bool useRVol, bool useUD, bool useAdxSlope, bool useChop, bool useRegime, bool useLevels, int biasMode, int alertCooldownSec, GoHudDockSide dock, int hudHeightPx, double hudOpacity, bool showDebugText, bool useFallbackOnly, bool showBeacon, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias)
		{
			return GoNoGoHUD(Input, goThreshold, yellowFloor, useFirstHourMicro, restrictToWindows, requireAdxGate, adxGate, chopN, rVolLookback, useVWAP, useRVol, useUD, useAdxSlope, useChop, useRegime, useLevels, biasMode, alertCooldownSec, dock, hudHeightPx, hudOpacity, showDebugText, useFallbackOnly, showBeacon, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias);
		}

		public GoNoGoHUD GoNoGoHUD(ISeries<double> input, int goThreshold, int yellowFloor, bool useFirstHourMicro, bool restrictToWindows, bool requireAdxGate, int adxGate, int chopN, int rVolLookback, bool useVWAP, bool useRVol, bool useUD, bool useAdxSlope, bool useChop, bool useRegime, bool useLevels, int biasMode, int alertCooldownSec, GoHudDockSide dock, int hudHeightPx, double hudOpacity, bool showDebugText, bool useFallbackOnly, bool showBeacon, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias)
		{
			if (cacheGoNoGoHUD != null)
				for (int idx = 0; idx < cacheGoNoGoHUD.Length; idx++)
					if (cacheGoNoGoHUD[idx] != null && cacheGoNoGoHUD[idx].GoThreshold == goThreshold && cacheGoNoGoHUD[idx].YellowFloor == yellowFloor && cacheGoNoGoHUD[idx].UseFirstHourMicro == useFirstHourMicro && cacheGoNoGoHUD[idx].RestrictToWindows == restrictToWindows && cacheGoNoGoHUD[idx].RequireAdxGate == requireAdxGate && cacheGoNoGoHUD[idx].AdxGate == adxGate && cacheGoNoGoHUD[idx].ChopN == chopN && cacheGoNoGoHUD[idx].RVolLookback == rVolLookback && cacheGoNoGoHUD[idx].UseVWAP == useVWAP && cacheGoNoGoHUD[idx].UseRVol == useRVol && cacheGoNoGoHUD[idx].UseUD == useUD && cacheGoNoGoHUD[idx].UseAdxSlope == useAdxSlope && cacheGoNoGoHUD[idx].UseChop == useChop && cacheGoNoGoHUD[idx].UseRegime == useRegime && cacheGoNoGoHUD[idx].UseLevels == useLevels && cacheGoNoGoHUD[idx].BiasMode == biasMode && cacheGoNoGoHUD[idx].AlertCooldownSec == alertCooldownSec && cacheGoNoGoHUD[idx].Dock == dock && cacheGoNoGoHUD[idx].HudHeightPx == hudHeightPx && cacheGoNoGoHUD[idx].HudOpacity == hudOpacity && cacheGoNoGoHUD[idx].ShowDebugText == showDebugText && cacheGoNoGoHUD[idx].UseFallbackOnly == useFallbackOnly && cacheGoNoGoHUD[idx].ShowBeacon == showBeacon && cacheGoNoGoHUD[idx].W_RVol == w_RVol && cacheGoNoGoHUD[idx].W_UD == w_UD && cacheGoNoGoHUD[idx].W_ADXSL == w_ADXSL && cacheGoNoGoHUD[idx].W_CHOP == w_CHOP && cacheGoNoGoHUD[idx].W_VWAP == w_VWAP && cacheGoNoGoHUD[idx].W_Regime == w_Regime && cacheGoNoGoHUD[idx].Bias == bias && cacheGoNoGoHUD[idx].EqualsInput(input))
						return cacheGoNoGoHUD[idx];
			return CacheIndicator<GoNoGoHUD>(new GoNoGoHUD(){ GoThreshold = goThreshold, YellowFloor = yellowFloor, UseFirstHourMicro = useFirstHourMicro, RestrictToWindows = restrictToWindows, RequireAdxGate = requireAdxGate, AdxGate = adxGate, ChopN = chopN, RVolLookback = rVolLookback, UseVWAP = useVWAP, UseRVol = useRVol, UseUD = useUD, UseAdxSlope = useAdxSlope, UseChop = useChop, UseRegime = useRegime, UseLevels = useLevels, BiasMode = biasMode, AlertCooldownSec = alertCooldownSec, Dock = dock, HudHeightPx = hudHeightPx, HudOpacity = hudOpacity, ShowDebugText = showDebugText, UseFallbackOnly = useFallbackOnly, ShowBeacon = showBeacon, W_RVol = w_RVol, W_UD = w_UD, W_ADXSL = w_ADXSL, W_CHOP = w_CHOP, W_VWAP = w_VWAP, W_Regime = w_Regime, Bias = bias }, input, ref cacheGoNoGoHUD);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GoNoGoHUD GoNoGoHUD(int goThreshold, int yellowFloor, bool useFirstHourMicro, bool restrictToWindows, bool requireAdxGate, int adxGate, int chopN, int rVolLookback, bool useVWAP, bool useRVol, bool useUD, bool useAdxSlope, bool useChop, bool useRegime, bool useLevels, int biasMode, int alertCooldownSec, GoHudDockSide dock, int hudHeightPx, double hudOpacity, bool showDebugText, bool useFallbackOnly, bool showBeacon, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias)
		{
			return indicator.GoNoGoHUD(Input, goThreshold, yellowFloor, useFirstHourMicro, restrictToWindows, requireAdxGate, adxGate, chopN, rVolLookback, useVWAP, useRVol, useUD, useAdxSlope, useChop, useRegime, useLevels, biasMode, alertCooldownSec, dock, hudHeightPx, hudOpacity, showDebugText, useFallbackOnly, showBeacon, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias);
		}

		public Indicators.GoNoGoHUD GoNoGoHUD(ISeries<double> input , int goThreshold, int yellowFloor, bool useFirstHourMicro, bool restrictToWindows, bool requireAdxGate, int adxGate, int chopN, int rVolLookback, bool useVWAP, bool useRVol, bool useUD, bool useAdxSlope, bool useChop, bool useRegime, bool useLevels, int biasMode, int alertCooldownSec, GoHudDockSide dock, int hudHeightPx, double hudOpacity, bool showDebugText, bool useFallbackOnly, bool showBeacon, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias)
		{
			return indicator.GoNoGoHUD(input, goThreshold, yellowFloor, useFirstHourMicro, restrictToWindows, requireAdxGate, adxGate, chopN, rVolLookback, useVWAP, useRVol, useUD, useAdxSlope, useChop, useRegime, useLevels, biasMode, alertCooldownSec, dock, hudHeightPx, hudOpacity, showDebugText, useFallbackOnly, showBeacon, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GoNoGoHUD GoNoGoHUD(int goThreshold, int yellowFloor, bool useFirstHourMicro, bool restrictToWindows, bool requireAdxGate, int adxGate, int chopN, int rVolLookback, bool useVWAP, bool useRVol, bool useUD, bool useAdxSlope, bool useChop, bool useRegime, bool useLevels, int biasMode, int alertCooldownSec, GoHudDockSide dock, int hudHeightPx, double hudOpacity, bool showDebugText, bool useFallbackOnly, bool showBeacon, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias)
		{
			return indicator.GoNoGoHUD(Input, goThreshold, yellowFloor, useFirstHourMicro, restrictToWindows, requireAdxGate, adxGate, chopN, rVolLookback, useVWAP, useRVol, useUD, useAdxSlope, useChop, useRegime, useLevels, biasMode, alertCooldownSec, dock, hudHeightPx, hudOpacity, showDebugText, useFallbackOnly, showBeacon, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias);
		}

		public Indicators.GoNoGoHUD GoNoGoHUD(ISeries<double> input , int goThreshold, int yellowFloor, bool useFirstHourMicro, bool restrictToWindows, bool requireAdxGate, int adxGate, int chopN, int rVolLookback, bool useVWAP, bool useRVol, bool useUD, bool useAdxSlope, bool useChop, bool useRegime, bool useLevels, int biasMode, int alertCooldownSec, GoHudDockSide dock, int hudHeightPx, double hudOpacity, bool showDebugText, bool useFallbackOnly, bool showBeacon, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias)
		{
			return indicator.GoNoGoHUD(input, goThreshold, yellowFloor, useFirstHourMicro, restrictToWindows, requireAdxGate, adxGate, chopN, rVolLookback, useVWAP, useRVol, useUD, useAdxSlope, useChop, useRegime, useLevels, biasMode, alertCooldownSec, dock, hudHeightPx, hudOpacity, showDebugText, useFallbackOnly, showBeacon, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias);
		}
	}
}

#endregion
