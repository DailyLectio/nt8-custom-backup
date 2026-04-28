#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Windows.Media;
using System.Xml.Serialization;
using System.Text;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// ===== Enums must be outside the class so NT's code-gen can see them
public enum LabelBehavior { Repaint, NoRepaint }
public enum CandleColorType { EntireCandle, FillOnly }

namespace NinjaTrader.NinjaScript.Indicators
{
    public class PriceActionCandlesMTF : Indicator
    {
        // ====== Inputs
        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "Price-Action Length (Swing N)", GroupName = "1. Core", Order = 0)]
        public int SwingLen { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name = "Label Type", GroupName = "1. Core", Order = 1)]
        public LabelBehavior Labels { get; set; } = LabelBehavior.Repaint;

        [NinjaScriptProperty]
        [Display(Name = "Candle Color Type", GroupName = "1. Core", Order = 2)]
        public CandleColorType ColorType { get; set; } = CandleColorType.FillOnly;

        // --- Colors / bar painting
        [NinjaScriptProperty]
        [Display(Name = "Paint PAC Bars", GroupName = "2. Colors", Order = -1)]
        public bool PaintBars { get; set; } = false;

        [XmlIgnore]
        [Display(Name = "Bull", GroupName = "2. Colors", Order = 0)]
        public Brush BullBrush { get; set; } = Brushes.Lime;
        [Browsable(false)]
        public string BullBrushSerializable { get { return Serialize.BrushToString(BullBrush); } set { BullBrush = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Bear", GroupName = "2. Colors", Order = 1)]
        public Brush BearBrush { get; set; } = Brushes.Red;
        [Browsable(false)]
        public string BearBrushSerializable { get { return Serialize.BrushToString(BearBrush); } set { BearBrush = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Neutral", GroupName = "2. Colors", Order = 2)]
        public Brush NeutralBrush { get; set; } = Brushes.Gold;
        [Browsable(false)]
        public string NeutralBrushSerializable { get { return Serialize.BrushToString(NeutralBrush); } set { NeutralBrush = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Display(Name = "Show HH/HL/LH/LL Labels", GroupName = "3. Labels", Order = 0)]
        public bool ShowSwingLabels { get; set; } = true;

        // --- MTF HUD
        [NinjaScriptProperty]
        [Display(Name = "Show MTF HUD", GroupName = "4. MTF HUD", Order = 0)]
        public bool ShowHud { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "HUD Corner", GroupName = "4. MTF HUD", Order = 1)]
        public TextPosition HudCorner { get; set; } = TextPosition.BottomRight; // BottomCenter is not valid in NT8

        [NinjaScriptProperty]
        [Display(Name = "Use 1 Minute", GroupName = "4. MTF HUD", Order = 10)]
        public bool UseTF1 { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Use 5 Minutes", GroupName = "4. MTF HUD", Order = 11)]
        public bool UseTF2 { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Use 15 Minutes", GroupName = "4. MTF HUD", Order = 12)]
        public bool UseTF3 { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Use 1 Hour", GroupName = "4. MTF HUD", Order = 13)]
        public bool UseTF4 { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Use 1 Day", GroupName = "4. MTF HUD", Order = 14)]
        public bool UseTF5 { get; set; } = true;

        // ====== Internals
        private int trend;                 // -1 bear, 0 neutral, 1 bull
        private int lastSignal;            // 2=HH,1=HL,-1=LH,-2=LL
        private double currSwingHigh = double.NaN;
        private double prevSwingHigh = double.NaN;
        private double currSwingLow  = double.NaN;
        private double prevSwingLow  = double.NaN;

        // MTF trend states
        private int tf1Trend, tf2Trend, tf3Trend, tf4Trend, tf5Trend;

        // Fixed text tag for HUD
        private const string HudTag = "PAC_MTF_HUD";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                    = "Price-Action Candles (MTF)";
                Calculate               = Calculate.OnBarClose;
                IsOverlay               = true;
                PaintPriceMarkers       = false;
                IsSuspendedWhileInactive= true;
            }
            else if (State == State.Configure)
            {
                if (UseTF1) AddDataSeries(BarsPeriodType.Minute, 1);
                if (UseTF2) AddDataSeries(BarsPeriodType.Minute, 5);
                if (UseTF3) AddDataSeries(BarsPeriodType.Minute, 15);
                if (UseTF4) AddDataSeries(BarsPeriodType.Minute, 60);
                if (UseTF5) AddDataSeries(BarsPeriodType.Day, 1);
            }
        }

        // ==== Pivot detection (symmetrical)
        private bool IsPivotHigh(int barsAgo, int n)
        {
            if (CurrentBar < 2 * n + barsAgo) return false;
            double pv = High[barsAgo];
            for (int i = 1; i <= n; i++)
            {
                if (High[barsAgo + i] >= pv) return false;
                if (High[barsAgo - i] >  pv) return false;
            }
            return true;
        }

        private bool IsPivotLow(int barsAgo, int n)
        {
            if (CurrentBar < 2 * n + barsAgo) return false;
            double pv = Low[barsAgo];
            for (int i = 1; i <= n; i++)
            {
                if (Low[barsAgo + i] <= pv) return false;
                if (Low[barsAgo - i] <  pv) return false;
            }
            return true;
        }

        private void UpdateSwingsOnSeries(int n, out bool hh, out bool lh, out bool ll, out bool hl,
                                          out double csh, out double csl, out double psh, out double psl)
        {
            int off = n;
            bool newSH = IsPivotHigh(off, n);
            bool newSL = IsPivotLow(off, n);

            if (newSH)
            {
                prevSwingHigh = currSwingHigh;
                currSwingHigh = High[off];
            }
            if (newSL)
            {
                prevSwingLow = currSwingLow;
                currSwingLow = Low[off];
            }

            hh = !double.IsNaN(currSwingHigh) && !double.IsNaN(prevSwingHigh) && currSwingHigh >= prevSwingHigh && newSH;
            lh = !double.IsNaN(currSwingHigh) && !double.IsNaN(prevSwingHigh) && currSwingHigh <  prevSwingHigh && newSH;
            ll = !double.IsNaN(currSwingLow)  && !double.IsNaN(prevSwingLow)  && currSwingLow  <= prevSwingLow  && newSL;
            hl = !double.IsNaN(currSwingLow)  && !double.IsNaN(prevSwingLow)  && currSwingLow  >  prevSwingLow  && newSL;

            csh = currSwingHigh; csl = currSwingLow; psh = prevSwingHigh; psl = prevSwingLow;

            if (hh)      lastSignal =  2;
            else if (hl) lastSignal =  1;
            else if (lh) lastSignal = -1;
            else if (ll) lastSignal = -2;

            if (ShowSwingLabels && (newSH || newSL))
            {
                int drawOn = Labels == LabelBehavior.Repaint ? off : 0;
                if (newSH)
                    Draw.Text(this, "PAC_HH_" + CurrentBar, hh ? "HH" : "LH",
                              drawOn, High[drawOn] + TickSize * 2, hh ? BullBrush : NeutralBrush);
                if (newSL)
                    Draw.Text(this, "PAC_LL_" + CurrentBar, ll ? "LL" : "HL",
                              drawOn, Low[drawOn] - TickSize * 2, ll ? BearBrush : NeutralBrush);
            }
        }

        private int ComputeTrendFromSwings(bool hh, bool lh, bool ll, bool hl,
                                           double psh, double psl, double csh, double csl)
        {
            int newTrend = trend;
            bool bull = (hh && !double.IsNaN(psh) && High[0] > psh) || (!double.IsNaN(csh) && Close[0] > csh);
            bool bear = (ll && !double.IsNaN(psl) && Low[0]  < psl) || (!double.IsNaN(csl) && Close[0] < csl);

            if (bull)      newTrend = 1;
            else if (bear) newTrend = -1;
            else if (lh || hl) newTrend = 0;

            return newTrend;
        }

        private void ApplyBarColors(Brush b)
        {
            // If painting is disabled, clear our brushes and exit.
            if (!PaintBars)
            {
                BarBrushes[0]          = null;
                CandleOutlineBrushes[0]= null;
                BarBrush               = null;
                CandleOutlineBrush     = null;
                return;
            }

            if (b == null)
                return;

            // Body (fill)
            BarBrush = b;
            BarBrushes[0] = b;

            if (ColorType == CandleColorType.FillOnly)
            {
                // Leave outlines/wicks at chart defaults
                CandleOutlineBrush = null;
                CandleOutlineBrushes[0] = null;
            }
            else
            {
                // Color entire candle (body + outline)
                CandleOutlineBrush = b;
                CandleOutlineBrushes[0] = b;
            }
        }

        private int ComputeTrendOnBip()
        {
            bool hh, lh, ll, hl;
            double csh, csl, psh, psl;
            UpdateSwingsOnSeries(SwingLen, out hh, out lh, out ll, out hl, out csh, out csl, out psh, out psl);
            return ComputeTrendFromSwings(hh, lh, ll, hl, psh, psl, csh, csl);
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 0)
            {
                bool hh, lh, ll, hl;
                double csh, csl, psh, psl;

                UpdateSwingsOnSeries(SwingLen, out hh, out lh, out ll, out hl, out csh, out csl, out psh, out psl);

                bool green  = (hh && !double.IsNaN(psh) && High[0] > psh) || (!double.IsNaN(csh) && Close[0] > csh);
                bool red    = (ll && !double.IsNaN(psl) && Low[0]  < psl) || (!double.IsNaN(csl) && Close[0] < csl);
                bool yellow = (lh) || (hl);

                Brush useBrush;
                if (green)           { trend = 1;  useBrush = BullBrush;    }
                else if (red)        { trend = -1; useBrush = BearBrush;    }
                else if (yellow)     { trend = 0;  useBrush = NeutralBrush; }
                else
                {
                    if      (trend == 1)  useBrush = BullBrush;
                    else if (trend == -1) useBrush = BearBrush;
                    else                  useBrush = NeutralBrush;
                }

                // This will either paint or clear brushes based on PaintBars
                ApplyBarColors(useBrush);

                // ===== MTF HUD table (Bull / Bear / Chop per TF) =====
                if (ShowHud && IsFirstTickOfBar)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("PAC Trend");
                    if (UseTF1) sb.AppendLine(FormatRow("1m",  tf1Trend));
                    if (UseTF2) sb.AppendLine(FormatRow("5m",  tf2Trend));
                    if (UseTF3) sb.AppendLine(FormatRow("15m", tf3Trend));
                    if (UseTF4) sb.AppendLine(FormatRow("60m", tf4Trend));
                    if (UseTF5) sb.AppendLine(FormatRow("1D",  tf5Trend));

                    Draw.TextFixed(
                        this,
                        HudTag,
                        sb.ToString(),
                        HudCorner,
                        Brushes.White,
                        new SimpleFont("Segoe UI", 12),
                        Brushes.Black,    // background
                        Brushes.DimGray,  // border
                        80);              // opacity
                }
            }
            else
            {
                int t = ComputeTrendOnBip();

                // Map BarsInProgress to the added series indices
                int bip = BarsInProgress;
                int idx = 1;
                if (UseTF1) { if (bip == idx) tf1Trend = t; idx++; }
                if (UseTF2) { if (bip == idx) tf2Trend = t; idx++; }
                if (UseTF3) { if (bip == idx) tf3Trend = t; idx++; }
                if (UseTF4) { if (bip == idx) tf4Trend = t; idx++; }
                if (UseTF5) { if (bip == idx) tf5Trend = t; }
            }
        }

        private string FormatRow(string label, int tr)
        {
            string state;
            if      (tr > 0) state = "Bull";
            else if (tr < 0) state = "Bear";
            else             state = "Chop";

            // Simple padding to keep rows aligned
            return string.Format("{0,-4} {1}", label, state);
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private PriceActionCandlesMTF[] cachePriceActionCandlesMTF;
		public PriceActionCandlesMTF PriceActionCandlesMTF(int swingLen, LabelBehavior labels, CandleColorType colorType, bool paintBars, bool showSwingLabels, bool showHud, TextPosition hudCorner, bool useTF1, bool useTF2, bool useTF3, bool useTF4, bool useTF5)
		{
			return PriceActionCandlesMTF(Input, swingLen, labels, colorType, paintBars, showSwingLabels, showHud, hudCorner, useTF1, useTF2, useTF3, useTF4, useTF5);
		}

		public PriceActionCandlesMTF PriceActionCandlesMTF(ISeries<double> input, int swingLen, LabelBehavior labels, CandleColorType colorType, bool paintBars, bool showSwingLabels, bool showHud, TextPosition hudCorner, bool useTF1, bool useTF2, bool useTF3, bool useTF4, bool useTF5)
		{
			if (cachePriceActionCandlesMTF != null)
				for (int idx = 0; idx < cachePriceActionCandlesMTF.Length; idx++)
					if (cachePriceActionCandlesMTF[idx] != null && cachePriceActionCandlesMTF[idx].SwingLen == swingLen && cachePriceActionCandlesMTF[idx].Labels == labels && cachePriceActionCandlesMTF[idx].ColorType == colorType && cachePriceActionCandlesMTF[idx].PaintBars == paintBars && cachePriceActionCandlesMTF[idx].ShowSwingLabels == showSwingLabels && cachePriceActionCandlesMTF[idx].ShowHud == showHud && cachePriceActionCandlesMTF[idx].HudCorner == hudCorner && cachePriceActionCandlesMTF[idx].UseTF1 == useTF1 && cachePriceActionCandlesMTF[idx].UseTF2 == useTF2 && cachePriceActionCandlesMTF[idx].UseTF3 == useTF3 && cachePriceActionCandlesMTF[idx].UseTF4 == useTF4 && cachePriceActionCandlesMTF[idx].UseTF5 == useTF5 && cachePriceActionCandlesMTF[idx].EqualsInput(input))
						return cachePriceActionCandlesMTF[idx];
			return CacheIndicator<PriceActionCandlesMTF>(new PriceActionCandlesMTF(){ SwingLen = swingLen, Labels = labels, ColorType = colorType, PaintBars = paintBars, ShowSwingLabels = showSwingLabels, ShowHud = showHud, HudCorner = hudCorner, UseTF1 = useTF1, UseTF2 = useTF2, UseTF3 = useTF3, UseTF4 = useTF4, UseTF5 = useTF5 }, input, ref cachePriceActionCandlesMTF);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.PriceActionCandlesMTF PriceActionCandlesMTF(int swingLen, LabelBehavior labels, CandleColorType colorType, bool paintBars, bool showSwingLabels, bool showHud, TextPosition hudCorner, bool useTF1, bool useTF2, bool useTF3, bool useTF4, bool useTF5)
		{
			return indicator.PriceActionCandlesMTF(Input, swingLen, labels, colorType, paintBars, showSwingLabels, showHud, hudCorner, useTF1, useTF2, useTF3, useTF4, useTF5);
		}

		public Indicators.PriceActionCandlesMTF PriceActionCandlesMTF(ISeries<double> input , int swingLen, LabelBehavior labels, CandleColorType colorType, bool paintBars, bool showSwingLabels, bool showHud, TextPosition hudCorner, bool useTF1, bool useTF2, bool useTF3, bool useTF4, bool useTF5)
		{
			return indicator.PriceActionCandlesMTF(input, swingLen, labels, colorType, paintBars, showSwingLabels, showHud, hudCorner, useTF1, useTF2, useTF3, useTF4, useTF5);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.PriceActionCandlesMTF PriceActionCandlesMTF(int swingLen, LabelBehavior labels, CandleColorType colorType, bool paintBars, bool showSwingLabels, bool showHud, TextPosition hudCorner, bool useTF1, bool useTF2, bool useTF3, bool useTF4, bool useTF5)
		{
			return indicator.PriceActionCandlesMTF(Input, swingLen, labels, colorType, paintBars, showSwingLabels, showHud, hudCorner, useTF1, useTF2, useTF3, useTF4, useTF5);
		}

		public Indicators.PriceActionCandlesMTF PriceActionCandlesMTF(ISeries<double> input , int swingLen, LabelBehavior labels, CandleColorType colorType, bool paintBars, bool showSwingLabels, bool showHud, TextPosition hudCorner, bool useTF1, bool useTF2, bool useTF3, bool useTF4, bool useTF5)
		{
			return indicator.PriceActionCandlesMTF(input, swingLen, labels, colorType, paintBars, showSwingLabels, showHud, hudCorner, useTF1, useTF2, useTF3, useTF4, useTF5);
		}
	}
}

#endregion
