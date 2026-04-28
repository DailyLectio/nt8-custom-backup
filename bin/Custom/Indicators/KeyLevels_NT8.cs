// CC BY-NC 4.0 — Re-authored for NinjaTrader 8 — no Pine code copied.
// Key Levels (Open, Premarket, Yesterday) — NT8 v1.4.2

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;         // SimpleFont, TextAlignment
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    [Gui.CategoryOrder("Key Levels", 0)]
    [Gui.CategoryOrder("Market Hours", 1)]
    [Gui.CategoryOrder("Lines & Labels", 2)]
    [Gui.CategoryOrder("MAs / VWAP", 3)]
    [Gui.CategoryOrder("Custom Levels", 4)]
    [Description("Key Levels (Open, Premarket, Yesterday) — NT8 v1.4.2")]
    public class KeyLevels_NT8 : Indicator
    {
        #region Inputs
        [NinjaScriptProperty]
        [Display(Name = "RTH Start (HHmm)", GroupName = "Market Hours", Order = 0)]
        public int RthStart { get; set; } = 930;

        [NinjaScriptProperty]
        [Display(Name = "RTH End (HHmm)", GroupName = "Market Hours", Order = 1)]
        public int RthEnd { get; set; } = 1600;

        [NinjaScriptProperty, Range(1, 120)]
        [Display(Name = "Opening Range Minutes", GroupName = "Key Levels", Order = 0)]
        public int OrMinutes { get; set; } = 15;

        [NinjaScriptProperty]
        [Display(Name = "Plot Today High/Low (RTH)", GroupName = "Key Levels", Order = 1)]
        public bool PlotTodayHL { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Plot Yesterday High/Low (RTH)", GroupName = "Key Levels", Order = 2)]
        public bool PlotYesterdayHL { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Plot Yesterday Close (RTH)", GroupName = "Key Levels", Order = 3)]
        public bool PlotYesterdayClose { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Plot Today Open (RTH)", GroupName = "Key Levels", Order = 4)]
        public bool PlotTodayOpen { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Plot Premarket High/Low", GroupName = "Key Levels", Order = 5)]
        public bool PlotPremarketHL { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Plot Opening Range (HL lines)", GroupName = "Key Levels", Order = 6)]
        public bool PlotOpeningRange { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Plot VWAP (Today)", GroupName = "MAs / VWAP", Order = 0)]
        public bool PlotVwapToday { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Carry Yesterday VWAP", GroupName = "MAs / VWAP", Order = 1)]
        public bool PlotVwapYesterday { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2)]
        [Display(Name = "MA1 Type (0=SMA,1=EMA,2=WMA)", GroupName = "MAs / VWAP", Order = 10)]
        public int Ma1TypeIdx { get; set; } = 1;

        [NinjaScriptProperty, Range(1, 1000)]
        [Display(Name = "MA1 Length", GroupName = "MAs / VWAP", Order = 11)]
        public int Ma1Length { get; set; } = 135;

        [NinjaScriptProperty, Range(0, 2)]
        [Display(Name = "MA2 Type (0=SMA,1=EMA,2=WMA)", GroupName = "MAs / VWAP", Order = 12)]
        public int Ma2TypeIdx { get; set; } = 1;

        [NinjaScriptProperty, Range(1, 1000)]
        [Display(Name = "MA2 Length", GroupName = "MAs / VWAP", Order = 13)]
        public int Ma2Length { get; set; } = 90;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "Show Only When Near (%)", GroupName = "Lines & Labels", Order = 0,
            Description = "Only show a line if price is within this percent band of the level. 0 disables.")]
        public double ProximityPercent { get; set; } = 0.0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Truncate History (bars)", GroupName = "Lines & Labels", Order = 1)]
        public int TruncateLeftBars { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Show Labels", GroupName = "Lines & Labels", Order = 4)]
        public bool ShowLabels { get; set; } = true;

        [NinjaScriptProperty, Range(0, 200)]
        [Display(Name = "Label Bars To Right", GroupName = "Lines & Labels", Order = 5)]
        public int LabelBarsRight { get; set; } = 10;

        [XmlIgnore, Display(Name = "Today HL Color", GroupName = "Lines & Labels", Order = 10)]
        public Brush TodayHLColor { get; set; } = Brushes.Orange;

        [XmlIgnore, Display(Name = "Yesterday HL Color", GroupName = "Lines & Labels", Order = 11)]
        public Brush YesterdayHLColor { get; set; } = Brushes.OrangeRed;

        [XmlIgnore, Display(Name = "Premarket HL Color", GroupName = "Lines & Labels", Order = 12)]
        public Brush PremarketHLColor { get; set; } = Brushes.SteelBlue;

        [XmlIgnore, Display(Name = "OR Color", GroupName = "Lines & Labels", Order = 13)]
        public Brush ORColor { get; set; } = Brushes.MediumAquamarine;

        [XmlIgnore, Display(Name = "VWAP Color (Today)", GroupName = "MAs / VWAP", Order = 20)]
        public Brush VwapTodayColor { get; set; } = Brushes.DodgerBlue;

        [XmlIgnore, Display(Name = "VWAP Color (Yest)", GroupName = "MAs / VWAP", Order = 21)]
        public Brush VwapYestColor { get; set; } = Brushes.LightBlue;

        [XmlIgnore, Display(Name = "MA1 Color", GroupName = "MAs / VWAP", Order = 22)]
        public Brush Ma1Color { get; set; } = Brushes.Fuchsia;

        [XmlIgnore, Display(Name = "MA2 Color", GroupName = "MAs / VWAP", Order = 23)]
        public Brush Ma2Color { get; set; } = Brushes.MediumVioletRed;

        [XmlIgnore, Display(Name = "Label Text", GroupName = "Lines & Labels", Order = 30)]
        public Brush LabelTextBrush { get; set; } = Brushes.White;

        [XmlIgnore, Display(Name = "Label BG", GroupName = "Lines & Labels", Order = 31)]
        public Brush LabelBgBrush { get; set; } = Brushes.Transparent;

        [NinjaScriptProperty]
        [Display(Name = "Custom Level 1 Enabled", GroupName = "Custom Levels", Order = 0)]
        public bool Custom1Enabled { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Custom Level 1 Price", GroupName = "Custom Levels", Order = 1)]
        public double Custom1Price { get; set; } = 0.0;

        [XmlIgnore, Display(Name = "Custom Level 1 Color", GroupName = "Custom Levels", Order = 2)]
        public Brush Custom1Color { get; set; } = Brushes.LimeGreen;

        [NinjaScriptProperty]
        [Display(Name = "Custom Level 2 Enabled", GroupName = "Custom Levels", Order = 4)]
        public bool Custom2Enabled { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Custom Level 2 Price", GroupName = "Custom Levels", Order = 5)]
        public double Custom2Price { get; set; } = 0.0;

        [XmlIgnore, Display(Name = "Custom Level 2 Color", GroupName = "Custom Levels", Order = 6)]
        public Brush Custom2Color { get; set; } = Brushes.LimeGreen;
        #endregion

        #region State/vars
        private int startHHmmss;
        private int endHHmmss;

        private DateTime sessionStartTime;
        private DateTime sessionEndTime;
        private DateTime orEndTime;
        private bool orInitialized;

        private double tdHigh, tdLow, tdOpen;
        private double ydHigh, ydLow, ydClose;
        private bool tdOpenSet;

        private double pmHigh, pmLow;
        private bool pmTrackedToday;

        private double sumPV, sumV;
        private double yestVwapClose;

        private double orHi, orLo;

        private Series<double> ma1, ma2;

        private string Tag(string key) => $"KL_{Instrument?.MasterInstrument?.Name}_{key}";
        private bool IsWithinRTH() { int t = ToTime(Time[0]); return t >= startHHmmss && t < endHHmmss; }
        private bool IsPremarket() { int t = ToTime(Time[0]); return t < startHHmmss; }

        private bool PassesProximity(double level)
        {
            if (ProximityPercent <= 0) return true;
            double pct = ProximityPercent / 100.0;
            double upper = Close[0] * (1 + pct);
            double lower = Close[0] * (1 - pct);
            return level <= upper && level >= lower;
        }
        #endregion

        #region OnStateChange
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "KeyLevels_NT8";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.Configure)
            {
                startHHmmss = (RthStart * 100);
                endHHmmss   = (RthEnd * 100);
                ma1 = new Series<double>(this);
                ma2 = new Series<double>(this);
            }
        }
        #endregion

        #region Helpers
        private void ResetForNewSession()
        {
            tdHigh = double.MinValue; tdLow = double.MaxValue; tdOpen = 0; tdOpenSet = false;
            pmHigh = double.MinValue; pmLow = double.MaxValue; pmTrackedToday = false;
            sumPV = 0; sumV = 0; orInitialized = false;
            orHi = double.MinValue; orLo = double.MaxValue;
        }

        private void FinalizeYesterday()
        {
            if (sumV > 0)
                yestVwapClose = sumPV / sumV;
        }

        private void UpdateMAs()
        {
            if (Ma1Length > 0) ma1[0] = ApplyMA(Ma1TypeIdx, Ma1Length);
            if (Ma2Length > 0) ma2[0] = ApplyMA(Ma2TypeIdx, Ma2Length);
        }

        private double ApplyMA(int idx, int len)
        {
            switch (idx)
            {
                case 0: return SMA(len)[0];
                case 1: return EMA(len)[0];
                case 2: return WMA(len)[0];
                default: return Close[0];
            }
        }

        private DateTime FutureTime(int barsRight)
        {
            try
            {
                var bp = Bars.BarsPeriod;
                int n = Math.Max(0, barsRight);
                if (bp.BarsPeriodType == BarsPeriodType.Second) return Time[0].AddSeconds(bp.Value * n);
                if (bp.BarsPeriodType == BarsPeriodType.Minute) return Time[0].AddMinutes(bp.Value * n);
                if (bp.BarsPeriodType == BarsPeriodType.Day)    return Time[0].AddDays(bp.Value * n);
                if (bp.BarsPeriodType == BarsPeriodType.Week)   return Time[0].AddDays(7 * bp.Value * n);
                if (bp.BarsPeriodType == BarsPeriodType.Month)  return Time[0].AddMonths(bp.Value * n);
            }
            catch { }
            return Time[0];
        }

		        // ---- robust label routine (simple overload + post styling) ----
		
		private void DrawLevelWithLabel(string tag, string labelText, Brush color, double price)
		{
		    if (!PassesProximity(price))
		    {
		        RemoveDrawObject(tag);
		        RemoveDrawObject(tag + "_LBL");
		        return;
		    }
		
		    // draw the line
		    Draw.HorizontalLine(this, tag, price, color);
		
		    if (!ShowLabels)
		    {
		        RemoveDrawObject(tag + "_LBL");
		        return;
		    }
		
		    // place label to the right of the last bar using a negative barsAgo offset
		    int futureBarsAgo = -Math.Max(0, LabelBarsRight);
		
		    // short, bullet-proof overload: (owner, tag, text, barsAgo, y)
		    Draw.Text(this, tag + "_LBL", labelText, futureBarsAgo, price);
		}
        #endregion

        // -------- Core --------
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2) return;

            if (Bars.IsFirstBarOfSession && CurrentBar > 2)
            {
                FinalizeYesterday();
                ydHigh = tdHigh; ydLow = tdLow; ydClose = Close[1];
                ResetForNewSession();
            }

            bool inRTH    = IsWithinRTH();
            bool inPremkt = IsPremarket();

            if (inRTH)
            {
                if (!tdOpenSet)
                {
                    tdOpen   = Open[0];
                    tdOpenSet = true;
                }

                if (PlotTodayHL)
                {
                    tdHigh = Math.Max(tdHigh, High[0]);
                    tdLow  = Math.Min(tdLow,  Low[0]);
                }

                if (PlotVwapToday)
                {
                    double typical = (High[0] + Low[0] + Close[0]) / 3.0;
                    double vol     = Math.Max(1, Volume[0]);
                    sumPV += typical * vol;
                    sumV  += vol;
                    double vwap = sumV > 0 ? sumPV / sumV : typical;
                    DrawLevelWithLabel(Tag("VWAP_Today"), "VWAP", VwapTodayColor, vwap);
                }

                if (PlotOpeningRange)
                {
                    if (!orInitialized)
                    {
                        var orStart = Time[0].Date.AddHours(RthStart / 100).AddMinutes(RthStart % 100);
                        orEndTime   = orStart.AddMinutes(OrMinutes);
                        orInitialized = true;
                        orHi = double.MinValue; orLo = double.MaxValue;
                    }

                    if (Time[0] <= orEndTime)
                    {
                        orHi = Math.Max(orHi, High[0]);
                        orLo = Math.Min(orLo, Low[0]);
                    }

                    if (orHi > double.MinValue && orLo < double.MaxValue)
                    {
                        DrawLevelWithLabel(Tag("OR_H"), "ORH", ORColor, orHi);
                        DrawLevelWithLabel(Tag("OR_L"), "ORL", ORColor, orLo);
                    }
                }
            }

            if (PlotPremarketHL && inPremkt)
            {
                pmHigh = Math.Max(pmHigh, High[0]);
                pmLow  = Math.Min(pmLow,  Low[0]);
                pmTrackedToday = true;
            }

            // Lines + labels
            if (PlotTodayHL && tdHigh > double.MinValue && tdLow < double.MaxValue)
            {
                DrawLevelWithLabel(Tag("TD_H"), "TDH", TodayHLColor, tdHigh);
                DrawLevelWithLabel(Tag("TD_L"), "TDL", TodayHLColor, tdLow);
            }

            if (PlotYesterdayHL && ydHigh != 0 && ydLow != 0)
            {
                DrawLevelWithLabel(Tag("YD_H"), "YDH", YesterdayHLColor, ydHigh);
                DrawLevelWithLabel(Tag("YD_L"), "YDL", YesterdayHLColor, ydLow);
            }

            if (PlotYesterdayClose && ydClose != 0)
                DrawLevelWithLabel(Tag("YD_C"), "YDC", YesterdayHLColor, ydClose);

            if (PlotTodayOpen && tdOpenSet)
                DrawLevelWithLabel(Tag("TD_O"), "TDO", TodayHLColor, tdOpen);

            if (PlotPremarketHL && pmTrackedToday)
            {
                DrawLevelWithLabel(Tag("PM_H"), "PMH", PremarketHLColor, pmHigh);
                DrawLevelWithLabel(Tag("PM_L"), "PML", PremarketHLColor, pmLow);
            }

            if (PlotVwapYesterday && yestVwapClose > 0)
                DrawLevelWithLabel(Tag("VWAP_Yest"), "VWAP-Y", VwapYestColor, yestVwapClose);

            UpdateMAs();
            if (Ma1Length > 0) DrawLevelWithLabel(Tag("MA1"), "MA1", Ma1Color, ma1[0]);
            if (Ma2Length > 0) DrawLevelWithLabel(Tag("MA2"), "MA2", Ma2Color, ma2[0]);

            // Custom levels
            if (Custom1Enabled) DrawLevelWithLabel(Tag("C1"), "C1", Custom1Color, Custom1Price);
            else { RemoveDrawObject(Tag("C1")); RemoveDrawObject(Tag("C1_LBL")); }

            if (Custom2Enabled) DrawLevelWithLabel(Tag("C2"), "C2", Custom2Color, Custom2Price);
            else { RemoveDrawObject(Tag("C2")); RemoveDrawObject(Tag("C2_LBL")); }
        }
    }

    // kept for possible future use
    public static class BrushExtensions
    {
        public static Brush ChangeOpacity(this Brush brush, double opacity)
        {
            if (brush == null) return null;
            var b = brush.Clone();
            b.Opacity = Math.Max(0.0, Math.Min(1.0, opacity));
            return b;
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private KeyLevels_NT8[] cacheKeyLevels_NT8;
		public KeyLevels_NT8 KeyLevels_NT8(int rthStart, int rthEnd, int orMinutes, bool plotTodayHL, bool plotYesterdayHL, bool plotYesterdayClose, bool plotTodayOpen, bool plotPremarketHL, bool plotOpeningRange, bool plotVwapToday, bool plotVwapYesterday, int ma1TypeIdx, int ma1Length, int ma2TypeIdx, int ma2Length, double proximityPercent, int truncateLeftBars, bool showLabels, int labelBarsRight, bool custom1Enabled, double custom1Price, bool custom2Enabled, double custom2Price)
		{
			return KeyLevels_NT8(Input, rthStart, rthEnd, orMinutes, plotTodayHL, plotYesterdayHL, plotYesterdayClose, plotTodayOpen, plotPremarketHL, plotOpeningRange, plotVwapToday, plotVwapYesterday, ma1TypeIdx, ma1Length, ma2TypeIdx, ma2Length, proximityPercent, truncateLeftBars, showLabels, labelBarsRight, custom1Enabled, custom1Price, custom2Enabled, custom2Price);
		}

		public KeyLevels_NT8 KeyLevels_NT8(ISeries<double> input, int rthStart, int rthEnd, int orMinutes, bool plotTodayHL, bool plotYesterdayHL, bool plotYesterdayClose, bool plotTodayOpen, bool plotPremarketHL, bool plotOpeningRange, bool plotVwapToday, bool plotVwapYesterday, int ma1TypeIdx, int ma1Length, int ma2TypeIdx, int ma2Length, double proximityPercent, int truncateLeftBars, bool showLabels, int labelBarsRight, bool custom1Enabled, double custom1Price, bool custom2Enabled, double custom2Price)
		{
			if (cacheKeyLevels_NT8 != null)
				for (int idx = 0; idx < cacheKeyLevels_NT8.Length; idx++)
					if (cacheKeyLevels_NT8[idx] != null && cacheKeyLevels_NT8[idx].RthStart == rthStart && cacheKeyLevels_NT8[idx].RthEnd == rthEnd && cacheKeyLevels_NT8[idx].OrMinutes == orMinutes && cacheKeyLevels_NT8[idx].PlotTodayHL == plotTodayHL && cacheKeyLevels_NT8[idx].PlotYesterdayHL == plotYesterdayHL && cacheKeyLevels_NT8[idx].PlotYesterdayClose == plotYesterdayClose && cacheKeyLevels_NT8[idx].PlotTodayOpen == plotTodayOpen && cacheKeyLevels_NT8[idx].PlotPremarketHL == plotPremarketHL && cacheKeyLevels_NT8[idx].PlotOpeningRange == plotOpeningRange && cacheKeyLevels_NT8[idx].PlotVwapToday == plotVwapToday && cacheKeyLevels_NT8[idx].PlotVwapYesterday == plotVwapYesterday && cacheKeyLevels_NT8[idx].Ma1TypeIdx == ma1TypeIdx && cacheKeyLevels_NT8[idx].Ma1Length == ma1Length && cacheKeyLevels_NT8[idx].Ma2TypeIdx == ma2TypeIdx && cacheKeyLevels_NT8[idx].Ma2Length == ma2Length && cacheKeyLevels_NT8[idx].ProximityPercent == proximityPercent && cacheKeyLevels_NT8[idx].TruncateLeftBars == truncateLeftBars && cacheKeyLevels_NT8[idx].ShowLabels == showLabels && cacheKeyLevels_NT8[idx].LabelBarsRight == labelBarsRight && cacheKeyLevels_NT8[idx].Custom1Enabled == custom1Enabled && cacheKeyLevels_NT8[idx].Custom1Price == custom1Price && cacheKeyLevels_NT8[idx].Custom2Enabled == custom2Enabled && cacheKeyLevels_NT8[idx].Custom2Price == custom2Price && cacheKeyLevels_NT8[idx].EqualsInput(input))
						return cacheKeyLevels_NT8[idx];
			return CacheIndicator<KeyLevels_NT8>(new KeyLevels_NT8(){ RthStart = rthStart, RthEnd = rthEnd, OrMinutes = orMinutes, PlotTodayHL = plotTodayHL, PlotYesterdayHL = plotYesterdayHL, PlotYesterdayClose = plotYesterdayClose, PlotTodayOpen = plotTodayOpen, PlotPremarketHL = plotPremarketHL, PlotOpeningRange = plotOpeningRange, PlotVwapToday = plotVwapToday, PlotVwapYesterday = plotVwapYesterday, Ma1TypeIdx = ma1TypeIdx, Ma1Length = ma1Length, Ma2TypeIdx = ma2TypeIdx, Ma2Length = ma2Length, ProximityPercent = proximityPercent, TruncateLeftBars = truncateLeftBars, ShowLabels = showLabels, LabelBarsRight = labelBarsRight, Custom1Enabled = custom1Enabled, Custom1Price = custom1Price, Custom2Enabled = custom2Enabled, Custom2Price = custom2Price }, input, ref cacheKeyLevels_NT8);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.KeyLevels_NT8 KeyLevels_NT8(int rthStart, int rthEnd, int orMinutes, bool plotTodayHL, bool plotYesterdayHL, bool plotYesterdayClose, bool plotTodayOpen, bool plotPremarketHL, bool plotOpeningRange, bool plotVwapToday, bool plotVwapYesterday, int ma1TypeIdx, int ma1Length, int ma2TypeIdx, int ma2Length, double proximityPercent, int truncateLeftBars, bool showLabels, int labelBarsRight, bool custom1Enabled, double custom1Price, bool custom2Enabled, double custom2Price)
		{
			return indicator.KeyLevels_NT8(Input, rthStart, rthEnd, orMinutes, plotTodayHL, plotYesterdayHL, plotYesterdayClose, plotTodayOpen, plotPremarketHL, plotOpeningRange, plotVwapToday, plotVwapYesterday, ma1TypeIdx, ma1Length, ma2TypeIdx, ma2Length, proximityPercent, truncateLeftBars, showLabels, labelBarsRight, custom1Enabled, custom1Price, custom2Enabled, custom2Price);
		}

		public Indicators.KeyLevels_NT8 KeyLevels_NT8(ISeries<double> input , int rthStart, int rthEnd, int orMinutes, bool plotTodayHL, bool plotYesterdayHL, bool plotYesterdayClose, bool plotTodayOpen, bool plotPremarketHL, bool plotOpeningRange, bool plotVwapToday, bool plotVwapYesterday, int ma1TypeIdx, int ma1Length, int ma2TypeIdx, int ma2Length, double proximityPercent, int truncateLeftBars, bool showLabels, int labelBarsRight, bool custom1Enabled, double custom1Price, bool custom2Enabled, double custom2Price)
		{
			return indicator.KeyLevels_NT8(input, rthStart, rthEnd, orMinutes, plotTodayHL, plotYesterdayHL, plotYesterdayClose, plotTodayOpen, plotPremarketHL, plotOpeningRange, plotVwapToday, plotVwapYesterday, ma1TypeIdx, ma1Length, ma2TypeIdx, ma2Length, proximityPercent, truncateLeftBars, showLabels, labelBarsRight, custom1Enabled, custom1Price, custom2Enabled, custom2Price);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.KeyLevels_NT8 KeyLevels_NT8(int rthStart, int rthEnd, int orMinutes, bool plotTodayHL, bool plotYesterdayHL, bool plotYesterdayClose, bool plotTodayOpen, bool plotPremarketHL, bool plotOpeningRange, bool plotVwapToday, bool plotVwapYesterday, int ma1TypeIdx, int ma1Length, int ma2TypeIdx, int ma2Length, double proximityPercent, int truncateLeftBars, bool showLabels, int labelBarsRight, bool custom1Enabled, double custom1Price, bool custom2Enabled, double custom2Price)
		{
			return indicator.KeyLevels_NT8(Input, rthStart, rthEnd, orMinutes, plotTodayHL, plotYesterdayHL, plotYesterdayClose, plotTodayOpen, plotPremarketHL, plotOpeningRange, plotVwapToday, plotVwapYesterday, ma1TypeIdx, ma1Length, ma2TypeIdx, ma2Length, proximityPercent, truncateLeftBars, showLabels, labelBarsRight, custom1Enabled, custom1Price, custom2Enabled, custom2Price);
		}

		public Indicators.KeyLevels_NT8 KeyLevels_NT8(ISeries<double> input , int rthStart, int rthEnd, int orMinutes, bool plotTodayHL, bool plotYesterdayHL, bool plotYesterdayClose, bool plotTodayOpen, bool plotPremarketHL, bool plotOpeningRange, bool plotVwapToday, bool plotVwapYesterday, int ma1TypeIdx, int ma1Length, int ma2TypeIdx, int ma2Length, double proximityPercent, int truncateLeftBars, bool showLabels, int labelBarsRight, bool custom1Enabled, double custom1Price, bool custom2Enabled, double custom2Price)
		{
			return indicator.KeyLevels_NT8(input, rthStart, rthEnd, orMinutes, plotTodayHL, plotYesterdayHL, plotYesterdayClose, plotTodayOpen, plotPremarketHL, plotOpeningRange, plotVwapToday, plotVwapYesterday, ma1TypeIdx, ma1Length, ma2TypeIdx, ma2Length, proximityPercent, truncateLeftBars, showLabels, labelBarsRight, custom1Enabled, custom1Price, custom2Enabled, custom2Price);
		}
	}
}

#endregion
