#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;

using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;

using SWM = System.Windows.Media;              // WPF brushes for Alert()
using SharpDX;                                 // Color4, RectangleF
using SharpDX.Direct2D1;                       // D2D brushes
using SharpDX.DirectWrite;                     // TextFormat
using RectangleF = SharpDX.RectangleF;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// HUD_ADX_CI_Panel — compact, fixed-position HUD card with explicit rules:
    ///   GREEN  when (ADX > AdxThresh AND slope ↑) AND (CI < ChopThresh AND slope ↓)
    ///          requires ConfirmBarsGreen consecutive bars to enter; stays GREEN until
    ///          either slope is opposite for OppSlopeBars.
    ///   YELLOW when (CI ≥ ChopThresh AND slope ↓) AND (ADX ≤ AdxThresh AND slope ↑).
    ///   RED    when in GREEN and slopes violate for OppSlopeBars; persists until GREEN or YELLOW.
    /// CI source: 1m Heikin‑Ashi or 1m Raw bars (toggle). ADX source: Primary chart or 1m series.
    /// Renders in OnRender (pixel-anchored) so it shows on any timeframe without covering price.
    /// </summary>
    public class HUD_ADX_CI_Panel : Indicator
    {
        // ========== Parameters (no external enums to avoid compile issues) ==========
        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "ADX Period", GroupName = "Parameters", Order = 0)]
        public int AdxPeriod { get; set; }

        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "CI Period", GroupName = "Parameters", Order = 1)]
        public int ChopPeriod { get; set; }

        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "ADX Threshold", GroupName = "Thresholds", Order = 0)]
        public int AdxThresh { get; set; }

        [NinjaScriptProperty, Range(10, 100)]
        [Display(Name = "CI Threshold", GroupName = "Thresholds", Order = 1)]
        public int ChopThresh { get; set; }

        [NinjaScriptProperty, Range(1, 10)]
        [Display(Name = "Confirm Bars GREEN", GroupName = "Behavior", Order = 0)]
        public int ConfirmBarsGreen { get; set; }

        [NinjaScriptProperty, Range(1, 10)]
        [Display(Name = "Opp Slope Bars", GroupName = "Behavior", Order = 1)]
        public int OppSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ADX From Primary", GroupName = "Behavior", Order = 2)]
        public bool AdxFromPrimary { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "CI Use Heikin‑Ashi (1m)", GroupName = "Behavior", Order = 3)]
        public bool CIUseHeikinAshi { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alerts Enabled", GroupName = "Alerts", Order = 0)]
        public bool AlertsEnabled { get; set; }

        [NinjaScriptProperty, Range(0, 600)]
        [Display(Name = "Alert Cooldown (sec)", GroupName = "Alerts", Order = 1)]
        public int AlertCooldownSec { get; set; }

        // Panel (corner index: 0 = TL, 1 = TR, 2 = BL, 3 = BR)
        [NinjaScriptProperty, Range(0, 3)]
        [Display(Name = "Panel Corner (0 TL,1 TR,2 BL,3 BR)", GroupName = "HUD Panel", Order = 0)]
        public int PanelCornerIndex { get; set; }

        [NinjaScriptProperty, Range(120, 600)]
        [Display(Name = "Panel Width (px)", GroupName = "HUD Panel", Order = 1)]
        public int PanelWidth { get; set; }

        [NinjaScriptProperty, Range(40, 300)]
        [Display(Name = "Panel Height (px)", GroupName = "HUD Panel", Order = 2)]
        public int PanelHeight { get; set; }

        [NinjaScriptProperty, Range(0, 400)]
        [Display(Name = "Margin X (px)", GroupName = "HUD Panel", Order = 3)]
        public int MarginX { get; set; }

        [NinjaScriptProperty, Range(0, 400)]
        [Display(Name = "Margin Y (px)", GroupName = "HUD Panel", Order = 4)]
        public int MarginY { get; set; }

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Panel Opacity %", GroupName = "HUD Panel", Order = 5)]
        public int PanelOpacityPct { get; set; }

        // ================= Internals =================
        private enum HudState { None, Yellow, Green, Red }
        private HudState state = HudState.None;
        private int greenConfirmCount = 0;
        private int oppSlopeCount = 0;
        private DateTime lastYellowAlert = Core.Globals.MinDate;

        // Indicators
        private ADX adxPrimary;
        private ADX adxOn1m;

        // 1m buffers
        private Series<double> haOpen1, haClose1, haHigh1, haLow1; // HA
        private Series<double> tr1, ci1;                            // TR & CI

        // Latest values for render
        private double ciLatest = double.NaN;
        private double adxLatest = double.NaN;
        private bool adxSlopeUp = false, ciSlopeDn = false;
        private double adxPrevPri = double.NaN, ciPrevPri = double.NaN;

        // DirectWrite
        private TextFormat tfBig, tfSmall;

        // ================= Lifecycle =================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "HUD_ADX_CI_Panel";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = true;
                DisplayInDataBox         = false;
                IsSuspendedWhileInactive = true;

                AdxPeriod       = 14;
                ChopPeriod      = 14;
                AdxThresh       = 18;
                ChopThresh      = 60;
                ConfirmBarsGreen= 1;
                OppSlopeBars    = 1;

                AdxFromPrimary  = true;
                CIUseHeikinAshi = false;   // default to raw-price CI to match NT CI

                AlertsEnabled   = true;
                AlertCooldownSec= 60;

                PanelCornerIndex= 1;       // TopRight
                PanelWidth      = 260;
                PanelHeight     = 68;
                MarginX         = 12;
                MarginY         = 12;
                PanelOpacityPct = 70;

                AddPlot(SWM.Brushes.Transparent, "CI (1m)");
                AddPlot(SWM.Brushes.Transparent, "ADX Used");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 1); // CI baseline
            }
            else if (State == State.DataLoaded)
            {
                adxPrimary = ADX(AdxPeriod);
                adxOn1m    = ADX(Inputs[1], AdxPeriod);

                haOpen1  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                haClose1 = new Series<double>(this, MaximumBarsLookBack.Infinite);
                haHigh1  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                haLow1   = new Series<double>(this, MaximumBarsLookBack.Infinite);
                tr1      = new Series<double>(this, MaximumBarsLookBack.Infinite);
                ci1      = new Series<double>(this, MaximumBarsLookBack.Infinite);

                tfBig   = new TextFormat(Core.Globals.DirectWriteFactory, "Segoe UI Semibold", 16f);
                tfSmall = new TextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", 13f);
            }
            else if (State == State.Terminated)
            {
                tfBig?.Dispose();
                tfSmall?.Dispose();
            }
        }

        // ================= Core =================
        protected override void OnBarUpdate()
        {
            // Build 1m CI on its own series
            if (BarsInProgress == 1)
            {
                if (CIUseHeikinAshi) BuildHeikinAshi1m();
                ComputeTrAndChop1m();
                ciLatest = ci1[0];
                return;
            }

            if (CurrentBar < 10) return;

            double adxNow = (AdxFromPrimary ? adxPrimary[0] : adxOn1m[0]);
            adxLatest     = adxNow;            // for render
            Values[1][0]  = adxNow;
            Values[0][0]  = ciLatest;

            if (double.IsNaN(ciLatest) || CurrentBars[1] < Math.Max(ChopPeriod, 2))
                return;

            // Slopes vs prior primary bar
            adxSlopeUp = !double.IsNaN(adxPrevPri) && (adxNow - adxPrevPri) > 0;
            ciSlopeDn  = !double.IsNaN(ciPrevPri)  && (ciLatest - ciPrevPri) < 0;

            // Conditions
            bool condGreen  = (adxNow > AdxThresh && adxSlopeUp) && (ciLatest < ChopThresh && ciSlopeDn);
            bool condYellow = (ciLatest >= ChopThresh && ciSlopeDn) && (adxNow <= AdxThresh && adxSlopeUp);

            // --- State machine ---
            switch (state)
            {
                case HudState.None:
                    if (condGreen)
                    {
                        greenConfirmCount++;
                        if (greenConfirmCount >= ConfirmBarsGreen)
                        { state = HudState.Green; greenConfirmCount = 0; oppSlopeCount = 0; }
                    }
                    else if (condYellow)
                    {
                        state = HudState.Yellow; greenConfirmCount = 0; oppSlopeCount = 0; MaybeAlertYellow(adxNow);
                    }
                    else
                        greenConfirmCount = 0;
                    break;

                case HudState.Yellow:
                    if (condGreen)
                    {
                        greenConfirmCount++;
                        if (greenConfirmCount >= ConfirmBarsGreen)
                        { state = HudState.Green; greenConfirmCount = 0; oppSlopeCount = 0; }
                    }
                    else if (!condYellow)
                    {
                        greenConfirmCount = 0; // stay Yellow until clear shift
                    }
                    break;

                case HudState.Green:
                    if (condGreen)
                    {
                        oppSlopeCount = 0; // good
                    }
                    else
                    {
                        if (condYellow) { state = HudState.Yellow; oppSlopeCount = 0; greenConfirmCount = 0; MaybeAlertYellow(adxNow); }
                        else
                        {
                            oppSlopeCount++;
                            if (oppSlopeCount >= OppSlopeBars)
                            { state = HudState.Red; oppSlopeCount = 0; greenConfirmCount = 0; }
                        }
                    }
                    break;

                case HudState.Red:
                    if (condGreen)
                    {
                        greenConfirmCount++;
                        if (greenConfirmCount >= ConfirmBarsGreen)
                        { state = HudState.Green; greenConfirmCount = 0; oppSlopeCount = 0; }
                    }
                    else if (condYellow)
                    {
                        state = HudState.Yellow; greenConfirmCount = 0; oppSlopeCount = 0; MaybeAlertYellow(adxNow);
                    }
                    else
                    {
                        greenConfirmCount = 0; // remain Red
                    }
                    break;
            }

            adxPrevPri = adxNow;
            ciPrevPri  = ciLatest;
        }

        private void MaybeAlertYellow(double adxNow)
        {
            if (!AlertsEnabled) return;
            var since = Time[0] - lastYellowAlert;
            if (since.TotalSeconds >= AlertCooldownSec)
            {
                Alert("HUD_ADX_CI_YELLOW", Priority.Medium,
                      $"Setup: CI {ciLatest:F1} (⇓ to < {ChopThresh}) | ADX {adxNow:F1} (⇑ to > {AdxThresh})",
                      "Alert1.wav", 10, SWM.Brushes.White, SWM.Brushes.Goldenrod);
                lastYellowAlert = Time[0];
            }
        }

        // ================= Rendering (pixel anchored) =================
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (ChartPanel == null || RenderTarget == null)
                return;

            float w = PanelWidth, h = PanelHeight;
            float x, y;
            int corner = Math.Max(0, Math.Min(3, PanelCornerIndex));
            if (corner == 1) { // TopRight
                x = ChartPanel.X + ChartPanel.W - w - MarginX; y = ChartPanel.Y + MarginY;
            } else if (corner == 2) { // BottomLeft
                x = ChartPanel.X + MarginX; y = ChartPanel.Y + ChartPanel.H - h - MarginY;
            } else if (corner == 3) { // BottomRight
                x = ChartPanel.X + ChartPanel.W - w - MarginX; y = ChartPanel.Y + ChartPanel.H - h - MarginY;
            } else { // TopLeft
                x = ChartPanel.X + MarginX; y = ChartPanel.Y + MarginY;
            }

            var backCol = new Color4(0f, 0f, 0f, Math.Min(1f, PanelOpacityPct / 100f));
            var white   = new Color4(1f, 1f, 1f, 1f);
            var green   = new Color4(0.0f, 0.8f, 0.0f, 1f);
            var yellow  = new Color4(0.93f, 0.77f, 0.19f, 1f);
            var red     = new Color4(0.80f, 0.24f, 0.24f, 1f);
            var dim     = new Color4(0.75f, 0.75f, 0.75f, 1f);

            Color4 stateCol = dim; string stateTxt = "--";
            switch (state)
            {
                case HudState.Yellow: stateCol = yellow; stateTxt = "YELLOW"; break;
                case HudState.Green:  stateCol = green;  stateTxt = "GREEN";  break;
                case HudState.Red:    stateCol = red;    stateTxt = "RED";    break;
            }

            using (var bBack = new SolidColorBrush(RenderTarget, backCol))
            using (var bState = new SolidColorBrush(RenderTarget, stateCol))
            using (var bText = new SolidColorBrush(RenderTarget, white))
            {
                var rect = new RectangleF(x, y, w, h);
                RenderTarget.FillRectangle(rect, bBack);

                float chip = Math.Min(18f, h - 16f);
                var chipRect = new RectangleF(x + 10f, y + (h - chip)/2f, chip, chip);
                RenderTarget.FillRectangle(chipRect, bState);

                string line1 = $"{stateTxt}  |  CI {ciLatest.ToString("0.0")} {(ciSlopeDn ? "↓" : "↑")}  |  ADX {adxLatest.ToString("0.0")} {(adxSlopeUp ? "↑" : "↓")}";
                string line2 = $"Rules: CI<{ChopThresh}↓ & ADX>{AdxThresh}↑  •  Conf={ConfirmBarsGreen}  •  Exit={OppSlopeBars}";

                var r1 = new RectangleF(x + 10f + chip + 8f, y + 8f, w - chip - 28f, (h/2f) - 6f);
                var r2 = new RectangleF(x + 10f + chip + 8f, y + (h/2f) - 2f, w - chip - 28f, (h/2f));

                RenderTarget.DrawText(line1, tfBig, r1, bText, DrawTextOptions.Clip);
                RenderTarget.DrawText(line2, tfSmall, r2, bText, DrawTextOptions.Clip);
            }
        }

        // ================= Helpers =================
        private void BuildHeikinAshi1m()
        {
            double o = Opens[1][0];
            double h = Highs[1][0];
            double l = Lows[1][0];
            double c = Closes[1][0];

            double haC = (o + h + l + c) / 4.0;
            double haO = (CurrentBars[1] > 0) ? (haOpen1[1] + haClose1[1]) / 2.0 : (o + c) / 2.0;
            double haH = Math.Max(h, Math.Max(haO, haC));
            double haL = Math.Min(l, Math.Min(haO, haC));

            haOpen1[0]  = haO;
            haClose1[0] = haC;
            haHigh1[0]  = haH;
            haLow1[0]   = haL;
        }

        private void ComputeTrAndChop1m()
        {
            double o = Opens[1][0];
            double h = Highs[1][0];
            double l = Lows[1][0];
            double c = Closes[1][0];

            double srcH, srcL, prevClose;
            if (CIUseHeikinAshi)
            {
                double prevC = (CurrentBars[1] > 0) ? haClose1[1] : (o + h + l + c) / 4.0;
                srcH = haHigh1[0]; srcL = haLow1[0]; prevClose = prevC;
            }
            else
            {
                double prevC = (CurrentBars[1] > 0) ? Closes[1][1] : c;
                srcH = h; srcL = l; prevClose = prevC;
            }

            double tr = Math.Max(srcH - srcL, Math.Max(Math.Abs(srcH - prevClose), Math.Abs(srcL - prevClose)));
            if (tr1 == null) tr1 = new Series<double>(this, MaximumBarsLookBack.Infinite);
            if (ci1 == null) ci1 = new Series<double>(this, MaximumBarsLookBack.Infinite);
            tr1[0] = tr;

            int n = ChopPeriod;
            if (CurrentBars[1] < n)
            {
                ci1[0] = double.NaN;
                return;
            }

            double sumTr = 0.0;
            double hh = double.MinValue, ll = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                sumTr += tr1[i];
                if (CIUseHeikinAshi)
                {
                    hh = Math.Max(hh, haHigh1[i]);
                    ll = Math.Min(ll, haLow1[i]);
                }
                else
                {
                    hh = Math.Max(hh, Highs[1][i]);
                    ll = Math.Min(ll, Lows[1][i]);
                }
            }
            double range = Math.Max(Instrument.MasterInstrument.TickSize, hh - ll);
            double chop = 100.0 * Math.Log10(sumTr / range) / Math.Log10(n);
            chop = Math.Max(0, Math.Min(100, chop));
            ci1[0] = chop;
        }

        // Plots for optional inspection
        [Browsable(false), XmlIgnore] public Series<double> CI => Values[0];
        [Browsable(false), XmlIgnore] public Series<double> ADXUsed => Values[1];
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private HUD_ADX_CI_Panel[] cacheHUD_ADX_CI_Panel;
		public HUD_ADX_CI_Panel HUD_ADX_CI_Panel(int adxPeriod, int chopPeriod, int adxThresh, int chopThresh, int confirmBarsGreen, int oppSlopeBars, bool adxFromPrimary, bool cIUseHeikinAshi, bool alertsEnabled, int alertCooldownSec, int panelCornerIndex, int panelWidth, int panelHeight, int marginX, int marginY, int panelOpacityPct)
		{
			return HUD_ADX_CI_Panel(Input, adxPeriod, chopPeriod, adxThresh, chopThresh, confirmBarsGreen, oppSlopeBars, adxFromPrimary, cIUseHeikinAshi, alertsEnabled, alertCooldownSec, panelCornerIndex, panelWidth, panelHeight, marginX, marginY, panelOpacityPct);
		}

		public HUD_ADX_CI_Panel HUD_ADX_CI_Panel(ISeries<double> input, int adxPeriod, int chopPeriod, int adxThresh, int chopThresh, int confirmBarsGreen, int oppSlopeBars, bool adxFromPrimary, bool cIUseHeikinAshi, bool alertsEnabled, int alertCooldownSec, int panelCornerIndex, int panelWidth, int panelHeight, int marginX, int marginY, int panelOpacityPct)
		{
			if (cacheHUD_ADX_CI_Panel != null)
				for (int idx = 0; idx < cacheHUD_ADX_CI_Panel.Length; idx++)
					if (cacheHUD_ADX_CI_Panel[idx] != null && cacheHUD_ADX_CI_Panel[idx].AdxPeriod == adxPeriod && cacheHUD_ADX_CI_Panel[idx].ChopPeriod == chopPeriod && cacheHUD_ADX_CI_Panel[idx].AdxThresh == adxThresh && cacheHUD_ADX_CI_Panel[idx].ChopThresh == chopThresh && cacheHUD_ADX_CI_Panel[idx].ConfirmBarsGreen == confirmBarsGreen && cacheHUD_ADX_CI_Panel[idx].OppSlopeBars == oppSlopeBars && cacheHUD_ADX_CI_Panel[idx].AdxFromPrimary == adxFromPrimary && cacheHUD_ADX_CI_Panel[idx].CIUseHeikinAshi == cIUseHeikinAshi && cacheHUD_ADX_CI_Panel[idx].AlertsEnabled == alertsEnabled && cacheHUD_ADX_CI_Panel[idx].AlertCooldownSec == alertCooldownSec && cacheHUD_ADX_CI_Panel[idx].PanelCornerIndex == panelCornerIndex && cacheHUD_ADX_CI_Panel[idx].PanelWidth == panelWidth && cacheHUD_ADX_CI_Panel[idx].PanelHeight == panelHeight && cacheHUD_ADX_CI_Panel[idx].MarginX == marginX && cacheHUD_ADX_CI_Panel[idx].MarginY == marginY && cacheHUD_ADX_CI_Panel[idx].PanelOpacityPct == panelOpacityPct && cacheHUD_ADX_CI_Panel[idx].EqualsInput(input))
						return cacheHUD_ADX_CI_Panel[idx];
			return CacheIndicator<HUD_ADX_CI_Panel>(new HUD_ADX_CI_Panel(){ AdxPeriod = adxPeriod, ChopPeriod = chopPeriod, AdxThresh = adxThresh, ChopThresh = chopThresh, ConfirmBarsGreen = confirmBarsGreen, OppSlopeBars = oppSlopeBars, AdxFromPrimary = adxFromPrimary, CIUseHeikinAshi = cIUseHeikinAshi, AlertsEnabled = alertsEnabled, AlertCooldownSec = alertCooldownSec, PanelCornerIndex = panelCornerIndex, PanelWidth = panelWidth, PanelHeight = panelHeight, MarginX = marginX, MarginY = marginY, PanelOpacityPct = panelOpacityPct }, input, ref cacheHUD_ADX_CI_Panel);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.HUD_ADX_CI_Panel HUD_ADX_CI_Panel(int adxPeriod, int chopPeriod, int adxThresh, int chopThresh, int confirmBarsGreen, int oppSlopeBars, bool adxFromPrimary, bool cIUseHeikinAshi, bool alertsEnabled, int alertCooldownSec, int panelCornerIndex, int panelWidth, int panelHeight, int marginX, int marginY, int panelOpacityPct)
		{
			return indicator.HUD_ADX_CI_Panel(Input, adxPeriod, chopPeriod, adxThresh, chopThresh, confirmBarsGreen, oppSlopeBars, adxFromPrimary, cIUseHeikinAshi, alertsEnabled, alertCooldownSec, panelCornerIndex, panelWidth, panelHeight, marginX, marginY, panelOpacityPct);
		}

		public Indicators.HUD_ADX_CI_Panel HUD_ADX_CI_Panel(ISeries<double> input , int adxPeriod, int chopPeriod, int adxThresh, int chopThresh, int confirmBarsGreen, int oppSlopeBars, bool adxFromPrimary, bool cIUseHeikinAshi, bool alertsEnabled, int alertCooldownSec, int panelCornerIndex, int panelWidth, int panelHeight, int marginX, int marginY, int panelOpacityPct)
		{
			return indicator.HUD_ADX_CI_Panel(input, adxPeriod, chopPeriod, adxThresh, chopThresh, confirmBarsGreen, oppSlopeBars, adxFromPrimary, cIUseHeikinAshi, alertsEnabled, alertCooldownSec, panelCornerIndex, panelWidth, panelHeight, marginX, marginY, panelOpacityPct);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.HUD_ADX_CI_Panel HUD_ADX_CI_Panel(int adxPeriod, int chopPeriod, int adxThresh, int chopThresh, int confirmBarsGreen, int oppSlopeBars, bool adxFromPrimary, bool cIUseHeikinAshi, bool alertsEnabled, int alertCooldownSec, int panelCornerIndex, int panelWidth, int panelHeight, int marginX, int marginY, int panelOpacityPct)
		{
			return indicator.HUD_ADX_CI_Panel(Input, adxPeriod, chopPeriod, adxThresh, chopThresh, confirmBarsGreen, oppSlopeBars, adxFromPrimary, cIUseHeikinAshi, alertsEnabled, alertCooldownSec, panelCornerIndex, panelWidth, panelHeight, marginX, marginY, panelOpacityPct);
		}

		public Indicators.HUD_ADX_CI_Panel HUD_ADX_CI_Panel(ISeries<double> input , int adxPeriod, int chopPeriod, int adxThresh, int chopThresh, int confirmBarsGreen, int oppSlopeBars, bool adxFromPrimary, bool cIUseHeikinAshi, bool alertsEnabled, int alertCooldownSec, int panelCornerIndex, int panelWidth, int panelHeight, int marginX, int marginY, int panelOpacityPct)
		{
			return indicator.HUD_ADX_CI_Panel(input, adxPeriod, chopPeriod, adxThresh, chopThresh, confirmBarsGreen, oppSlopeBars, adxFromPrimary, cIUseHeikinAshi, alertsEnabled, alertCooldownSec, panelCornerIndex, panelWidth, panelHeight, marginX, marginY, panelOpacityPct);
		}
	}
}

#endregion
