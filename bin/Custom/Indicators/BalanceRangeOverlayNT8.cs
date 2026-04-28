#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;   // Draw.*
using System.Windows.Media;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class BalanceRangeOverlayNT8 : Indicator
    {
        // -------- Inputs: Sessions --------
        [NinjaScriptProperty]
        [Display(Name = "RTH Open (HH:mm)", Order = 0, GroupName = "Sessions")]
        public string RthOpenStr { get; set; } = "09:30";

        [NinjaScriptProperty]
        [Display(Name = "RTH Close (HH:mm)", Order = 1, GroupName = "Sessions")]
        public string RthCloseStr { get; set; } = "16:00";

        [NinjaScriptProperty]
        [Display(Name = "Overnight Start (HH:mm)", Order = 2, GroupName = "Sessions")]
        public string OnStartStr { get; set; } = "18:00";

        [NinjaScriptProperty]
        [Range(30, 90)]
        [Display(Name = "IB Minutes", Order = 3, GroupName = "Sessions")]
        public int IBMinutes { get; set; } = 60;

        // -------- Inputs: Style --------
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Line Width", Order = 0, GroupName = "Style")]
        public int LineWidthPx { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "IB Color", Order = 1, GroupName = "Style")]
        public Brush IBColor { get; set; } = Brushes.Gray;

        [NinjaScriptProperty]
        [Display(Name = "ON Color", Order = 2, GroupName = "Style")]
        public Brush ONColor { get; set; } = Brushes.Teal;

        [NinjaScriptProperty]
        [Display(Name = "Yesterday OHLC Color", Order = 3, GroupName = "Style")]
        public Brush YDayColor { get; set; } = Brushes.Orange;

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "IB Fill Opacity (0-100)", Order = 4, GroupName = "Style")]
        public int IBFillOpacity { get; set; } = 15;

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ON Fill Opacity (0-100)", Order = 5, GroupName = "Style")]
        public int ONFillOpacity { get; set; } = 10;

        // -------- Inputs: Toggles --------
        [NinjaScriptProperty]
        [Display(Name = "Show IB Range", Order = 0, GroupName = "Toggles")]
        public bool ShowIB { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Show Overnight Range", Order = 1, GroupName = "Toggles")]
        public bool ShowON { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Show Yesterday OHLC", Order = 2, GroupName = "Toggles")]
        public bool ShowYestOHLC { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Show Right-Edge Labels", Order = 3, GroupName = "Toggles")]
        public bool ShowLabels { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Label Offset (ticks)", Order = 4, GroupName = "Toggles")]
        public int LabelTickOffset { get; set; } = 4;

        // -------- Internals --------
        private Series<double> ibHighSeries, ibLowSeries;
        private Series<double> onHighSeries, onLowSeries;
        private Series<double> yHighSeries, yLowSeries, yCloseSeries;

        private double ibHigh = double.NaN;
        private double ibLow  = double.NaN;
        private bool ibLocked = false;

        private double onHigh = double.NaN;
        private double onLow  = double.NaN;

        private int rthOpenInt;
        private int rthCloseInt;
        private int onStartInt;

        private DateTime rthStartDateTime;
        private DateTime ibEndDateTime;
        private DateTime onStartDateTime;
        private DateTime onEndDateTime;

        private bool rthDayInitialized = false;
        private DateTime currentTradingDay;

        private bool dailySeriesReady = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Balance Range Overlay — IB / ON / YD (NT8)";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = true;

                // Plots: 0 IBH, 1 IBL, 2 ONH, 3 ONL, 4 YH, 5 YL, 6 YC
                AddPlot(IBColor,   "IB High");
                AddPlot(IBColor,   "IB Low");
                AddPlot(ONColor,   "ON High");
                AddPlot(ONColor,   "ON Low");
                AddPlot(YDayColor, "Y High");
                AddPlot(YDayColor, "Y Low");
                AddPlot(YDayColor, "Y Close");
            }
            else if (State == State.Configure)
            {
                // Secondary daily series for YD OHLC
                AddDataSeries(BarsPeriodType.Day, 1);
            }
            else if (State == State.DataLoaded)
            {
                ibHighSeries = new Series<double>(this, MaximumBarsLookBack.Infinite);
                ibLowSeries  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                onHighSeries = new Series<double>(this, MaximumBarsLookBack.Infinite);
                onLowSeries  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                yHighSeries  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                yLowSeries   = new Series<double>(this, MaximumBarsLookBack.Infinite);
                yCloseSeries = new Series<double>(this, MaximumBarsLookBack.Infinite);

                rthOpenInt  = ParseToTimeInt(RthOpenStr);
                rthCloseInt = ParseToTimeInt(RthCloseStr);
                onStartInt  = ParseToTimeInt(OnStartStr);

                SetPlotStyles();
            }
        }

        public override string DisplayName =>
            $"{Name} [IB={IBMinutes}m, RTH {RthOpenStr}-{RthCloseStr}, ON {OnStartStr}-Open]";

        protected override void OnBarUpdate()
        {
            // Handle the daily series for YD OHLC
            if (BarsInProgress == 1)
            {
                if (CurrentBars[1] > 1)
                {
                    yHighSeries[0]  = Highs[1][1];
                    yLowSeries[0]   = Lows[1][1];
                    yCloseSeries[0] = Closes[1][1];
                    dailySeriesReady = true;
                }
                return;
            }

            if (CurrentBar < 2)
                return;

            SetPlotWidths();

            bool isAfterRthOpen = ToTime(Time[0]) >= rthOpenInt;
            DateTime dayAnchor  = isAfterRthOpen ? Time[0].Date : Time[0].Date.AddDays(-1);

            if (!rthDayInitialized || dayAnchor != currentTradingDay)
            {
                currentTradingDay = dayAnchor;
                rthStartDateTime  = currentTradingDay.Add(ParseToTimeSpan(RthOpenStr));
                ibEndDateTime     = rthStartDateTime.AddMinutes(IBMinutes);

                onStartDateTime   = currentTradingDay.AddDays(-1).Add(ParseToTimeSpan(OnStartStr));
                onEndDateTime     = rthStartDateTime;

                ibHigh   = double.NaN;
                ibLow    = double.NaN;
                ibLocked = false;

                onHigh = double.NaN;
                onLow  = double.NaN;

                rthDayInitialized = true;
            }

            DateTime t = Time[0];

            // --- Overnight accumulation ---
            bool inON = t >= onStartDateTime && t < onEndDateTime;
            if (inON && ShowON)
            {
                if (double.IsNaN(onHigh) || High[0] > onHigh) onHigh = High[0];
                if (double.IsNaN(onLow)  || Low[0]  < onLow)  onLow  = Low[0];
            }

            onHighSeries[0] = (!double.IsNaN(onHigh) && ShowON) ? onHigh : double.NaN;
            onLowSeries[0]  = (!double.IsNaN(onLow)  && ShowON) ? onLow  : double.NaN;

            // --- IB accumulation ---
            bool inIBWindow = t >= rthStartDateTime && t < ibEndDateTime;

            if (ShowIB)
            {
                if (inIBWindow && !ibLocked)
                {
                    if (double.IsNaN(ibHigh) || High[0] > ibHigh) ibHigh = High[0];
                    if (double.IsNaN(ibLow)  || Low[0]  < ibLow)  ibLow  = Low[0];
                    if (t >= ibEndDateTime) ibLocked = true;
                }

                ibHighSeries[0] = !double.IsNaN(ibHigh) ? ibHigh : double.NaN;
                ibLowSeries[0]  = !double.IsNaN(ibLow)  ? ibLow  : double.NaN;
            }
            else
            {
                ibHighSeries[0] = double.NaN;
                ibLowSeries[0]  = double.NaN;
            }

            // --- Yesterday OHLC flat lines ---
            if (ShowYestOHLC && dailySeriesReady)
            {
                Values[4][0] = yHighSeries[0];
                Values[5][0] = yLowSeries[0];
                Values[6][0] = yCloseSeries[0];
            }
            else
            {
                Values[4][0] = Values[5][0] = Values[6][0] = double.NaN;
            }

            // --- Brushes (explicit) ---
            PlotBrushes[0][0] = ShowIB ? IBColor : null;
            PlotBrushes[1][0] = ShowIB ? IBColor : null;
            PlotBrushes[2][0] = ShowON ? ONColor : null;
            PlotBrushes[3][0] = ShowON ? ONColor : null;
            PlotBrushes[4][0] = ShowYestOHLC ? YDayColor : null;
            PlotBrushes[5][0] = ShowYestOHLC ? YDayColor : null;
            PlotBrushes[6][0] = ShowYestOHLC ? YDayColor : null;

            // --- Region fills (your build’s overload uses int opacity) ---
            if (ShowIB && !double.IsNaN(ibHigh) && !double.IsNaN(ibLow))
            {
                string tagIB = "IBFill-" + currentTradingDay.ToString("yyyyMMdd");
                Draw.Region(this, tagIB, CurrentBar, 0, ibHighSeries, ibLowSeries, null, IBColor, IBFillOpacity);
            }
            else RemoveDrawObject("IBFill-" + currentTradingDay.ToString("yyyyMMdd"));

            if (ShowON && !double.IsNaN(onHigh) && !double.IsNaN(onLow))
            {
                string tagON = "ONFill-" + currentTradingDay.ToString("yyyyMMdd");
                Draw.Region(this, tagON, CurrentBar, 0, onHighSeries, onLowSeries, null, ONColor, ONFillOpacity);
            }
            else RemoveDrawObject("ONFill-" + currentTradingDay.ToString("yyyyMMdd"));

            // --- Push series to plot values ---
            Values[0][0] = (ShowIB && !double.IsNaN(ibHigh)) ? ibHighSeries[0] : double.NaN;
            Values[1][0] = (ShowIB && !double.IsNaN(ibLow))  ? ibLowSeries[0]  : double.NaN;
            Values[2][0] = (ShowON && !double.IsNaN(onHigh)) ? onHighSeries[0] : double.NaN;
            Values[3][0] = (ShowON && !double.IsNaN(onLow))  ? onLowSeries[0]  : double.NaN;

            // --- Right-edge labels at last bar (short overload) ---
            if (ShowLabels)
            {
                double off = Instrument.MasterInstrument.TickSize * LabelTickOffset;

                DrawPriceLabel("LBL-IBH-" + currentTradingDay.ToString("yyyyMMdd"), "IBH",
                    (ShowIB && !double.IsNaN(ibHigh)) ? ibHigh - off : double.NaN, IBColor);

                DrawPriceLabel("LBL-IBL-" + currentTradingDay.ToString("yyyyMMdd"), "IBL",
                    (ShowIB && !double.IsNaN(ibLow))  ? ibLow  - off : double.NaN, IBColor);

                DrawPriceLabel("LBL-ONH-" + currentTradingDay.ToString("yyyyMMdd"), "ONH",
                    (ShowON && !double.IsNaN(onHigh)) ? onHigh - off : double.NaN, ONColor);

                DrawPriceLabel("LBL-ONL-" + currentTradingDay.ToString("yyyyMMdd"), "ONL",
                    (ShowON && !double.IsNaN(onLow))  ? onLow  - off : double.NaN, ONColor);

                DrawPriceLabel("LBL-YH-"  + currentTradingDay.ToString("yyyyMMdd"), "yHigh",
                    (ShowYestOHLC && dailySeriesReady) ? yHighSeries[0]  - off : double.NaN, YDayColor);

                DrawPriceLabel("LBL-YL-"  + currentTradingDay.ToString("yyyyMMdd"), "yLow",
                    (ShowYestOHLC && dailySeriesReady) ? yLowSeries[0]   - off : double.NaN, YDayColor);

                DrawPriceLabel("LBL-YC-"  + currentTradingDay.ToString("yyyyMMdd"), "yClose",
                    (ShowYestOHLC && dailySeriesReady) ? yCloseSeries[0] - off : double.NaN, YDayColor);
            }
            else
            {
                RemoveAllLabelsForDay();
            }
        }

        // ---- Helpers ----
        private int ParseToTimeInt(string hhmm)
        {
            var ts = ParseToTimeSpan(hhmm);
            return ts.Hours * 10000 + ts.Minutes * 100;
        }

        private TimeSpan ParseToTimeSpan(string hhmm)
        {
            if (TimeSpan.TryParse(hhmm, out var ts)) return ts;
            return new TimeSpan(9, 30, 0);
        }

        private void SetPlotStyles()
        {
            if (Plots != null && Plots.Length >= 7)
            {
                Plots[0].Width = LineWidthPx; Plots[1].Width = LineWidthPx;
                Plots[2].Width = LineWidthPx; Plots[3].Width = LineWidthPx;
                Plots[4].Width = LineWidthPx; Plots[5].Width = LineWidthPx;
                Plots[6].Width = LineWidthPx;

                Plots[0].Brush = IBColor; Plots[1].Brush = IBColor;
                Plots[2].Brush = ONColor; Plots[3].Brush = ONColor;
                Plots[4].Brush = YDayColor; Plots[5].Brush = YDayColor; Plots[6].Brush = YDayColor;
            }
        }

        private void SetPlotWidths()
        {
            if (Plots != null && Plots.Length >= 7)
            {
                Plots[0].Width = LineWidthPx; Plots[1].Width = LineWidthPx;
                Plots[2].Width = LineWidthPx; Plots[3].Width = LineWidthPx;
                Plots[4].Width = LineWidthPx; Plots[5].Width = LineWidthPx;
                Plots[6].Width = LineWidthPx;
            }
        }

        private void DrawPriceLabel(string tag, string text, double price, Brush col)
        {
            if (double.IsNaN(price))
            {
                RemoveDrawObject(tag);
                return;
            }

            // Short, portable overload in your build:
            // Draw.Text(owner, tag, text, barsAgo, y, textBrush)
            Draw.Text(this, tag, text, 0, price, col);
        }

        private void RemoveAllLabelsForDay()
        {
            string d = currentTradingDay.ToString("yyyyMMdd");
            RemoveDrawObject("LBL-IBH-" + d);
            RemoveDrawObject("LBL-IBL-" + d);
            RemoveDrawObject("LBL-ONH-" + d);
            RemoveDrawObject("LBL-ONL-" + d);
            RemoveDrawObject("LBL-YH-"  + d);
            RemoveDrawObject("LBL-YL-"  + d);
            RemoveDrawObject("LBL-YC-"  + d);
        }

        // Expose series (optional)
        [Browsable(false), XmlIgnore] public Series<double> IBHigh  => ibHighSeries;
        [Browsable(false), XmlIgnore] public Series<double> IBLow   => ibLowSeries;
        [Browsable(false), XmlIgnore] public Series<double> ONHigh  => onHighSeries;
        [Browsable(false), XmlIgnore] public Series<double> ONLow   => onLowSeries;
        [Browsable(false), XmlIgnore] public Series<double> YHigh   => yHighSeries;
        [Browsable(false), XmlIgnore] public Series<double> YLow    => yLowSeries;
        [Browsable(false), XmlIgnore] public Series<double> YClose  => yCloseSeries;
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BalanceRangeOverlayNT8[] cacheBalanceRangeOverlayNT8;
		public BalanceRangeOverlayNT8 BalanceRangeOverlayNT8(string rthOpenStr, string rthCloseStr, string onStartStr, int iBMinutes, int lineWidthPx, Brush iBColor, Brush oNColor, Brush yDayColor, int iBFillOpacity, int oNFillOpacity, bool showIB, bool showON, bool showYestOHLC, bool showLabels, int labelTickOffset)
		{
			return BalanceRangeOverlayNT8(Input, rthOpenStr, rthCloseStr, onStartStr, iBMinutes, lineWidthPx, iBColor, oNColor, yDayColor, iBFillOpacity, oNFillOpacity, showIB, showON, showYestOHLC, showLabels, labelTickOffset);
		}

		public BalanceRangeOverlayNT8 BalanceRangeOverlayNT8(ISeries<double> input, string rthOpenStr, string rthCloseStr, string onStartStr, int iBMinutes, int lineWidthPx, Brush iBColor, Brush oNColor, Brush yDayColor, int iBFillOpacity, int oNFillOpacity, bool showIB, bool showON, bool showYestOHLC, bool showLabels, int labelTickOffset)
		{
			if (cacheBalanceRangeOverlayNT8 != null)
				for (int idx = 0; idx < cacheBalanceRangeOverlayNT8.Length; idx++)
					if (cacheBalanceRangeOverlayNT8[idx] != null && cacheBalanceRangeOverlayNT8[idx].RthOpenStr == rthOpenStr && cacheBalanceRangeOverlayNT8[idx].RthCloseStr == rthCloseStr && cacheBalanceRangeOverlayNT8[idx].OnStartStr == onStartStr && cacheBalanceRangeOverlayNT8[idx].IBMinutes == iBMinutes && cacheBalanceRangeOverlayNT8[idx].LineWidthPx == lineWidthPx && cacheBalanceRangeOverlayNT8[idx].IBColor == iBColor && cacheBalanceRangeOverlayNT8[idx].ONColor == oNColor && cacheBalanceRangeOverlayNT8[idx].YDayColor == yDayColor && cacheBalanceRangeOverlayNT8[idx].IBFillOpacity == iBFillOpacity && cacheBalanceRangeOverlayNT8[idx].ONFillOpacity == oNFillOpacity && cacheBalanceRangeOverlayNT8[idx].ShowIB == showIB && cacheBalanceRangeOverlayNT8[idx].ShowON == showON && cacheBalanceRangeOverlayNT8[idx].ShowYestOHLC == showYestOHLC && cacheBalanceRangeOverlayNT8[idx].ShowLabels == showLabels && cacheBalanceRangeOverlayNT8[idx].LabelTickOffset == labelTickOffset && cacheBalanceRangeOverlayNT8[idx].EqualsInput(input))
						return cacheBalanceRangeOverlayNT8[idx];
			return CacheIndicator<BalanceRangeOverlayNT8>(new BalanceRangeOverlayNT8(){ RthOpenStr = rthOpenStr, RthCloseStr = rthCloseStr, OnStartStr = onStartStr, IBMinutes = iBMinutes, LineWidthPx = lineWidthPx, IBColor = iBColor, ONColor = oNColor, YDayColor = yDayColor, IBFillOpacity = iBFillOpacity, ONFillOpacity = oNFillOpacity, ShowIB = showIB, ShowON = showON, ShowYestOHLC = showYestOHLC, ShowLabels = showLabels, LabelTickOffset = labelTickOffset }, input, ref cacheBalanceRangeOverlayNT8);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BalanceRangeOverlayNT8 BalanceRangeOverlayNT8(string rthOpenStr, string rthCloseStr, string onStartStr, int iBMinutes, int lineWidthPx, Brush iBColor, Brush oNColor, Brush yDayColor, int iBFillOpacity, int oNFillOpacity, bool showIB, bool showON, bool showYestOHLC, bool showLabels, int labelTickOffset)
		{
			return indicator.BalanceRangeOverlayNT8(Input, rthOpenStr, rthCloseStr, onStartStr, iBMinutes, lineWidthPx, iBColor, oNColor, yDayColor, iBFillOpacity, oNFillOpacity, showIB, showON, showYestOHLC, showLabels, labelTickOffset);
		}

		public Indicators.BalanceRangeOverlayNT8 BalanceRangeOverlayNT8(ISeries<double> input , string rthOpenStr, string rthCloseStr, string onStartStr, int iBMinutes, int lineWidthPx, Brush iBColor, Brush oNColor, Brush yDayColor, int iBFillOpacity, int oNFillOpacity, bool showIB, bool showON, bool showYestOHLC, bool showLabels, int labelTickOffset)
		{
			return indicator.BalanceRangeOverlayNT8(input, rthOpenStr, rthCloseStr, onStartStr, iBMinutes, lineWidthPx, iBColor, oNColor, yDayColor, iBFillOpacity, oNFillOpacity, showIB, showON, showYestOHLC, showLabels, labelTickOffset);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BalanceRangeOverlayNT8 BalanceRangeOverlayNT8(string rthOpenStr, string rthCloseStr, string onStartStr, int iBMinutes, int lineWidthPx, Brush iBColor, Brush oNColor, Brush yDayColor, int iBFillOpacity, int oNFillOpacity, bool showIB, bool showON, bool showYestOHLC, bool showLabels, int labelTickOffset)
		{
			return indicator.BalanceRangeOverlayNT8(Input, rthOpenStr, rthCloseStr, onStartStr, iBMinutes, lineWidthPx, iBColor, oNColor, yDayColor, iBFillOpacity, oNFillOpacity, showIB, showON, showYestOHLC, showLabels, labelTickOffset);
		}

		public Indicators.BalanceRangeOverlayNT8 BalanceRangeOverlayNT8(ISeries<double> input , string rthOpenStr, string rthCloseStr, string onStartStr, int iBMinutes, int lineWidthPx, Brush iBColor, Brush oNColor, Brush yDayColor, int iBFillOpacity, int oNFillOpacity, bool showIB, bool showON, bool showYestOHLC, bool showLabels, int labelTickOffset)
		{
			return indicator.BalanceRangeOverlayNT8(input, rthOpenStr, rthCloseStr, onStartStr, iBMinutes, lineWidthPx, iBColor, oNColor, yDayColor, iBFillOpacity, oNFillOpacity, showIB, showON, showYestOHLC, showLabels, labelTickOffset);
		}
	}
}

#endregion
