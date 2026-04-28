#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui;       // <--- ADD THIS LINE
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class ADXGu5v2 : Indicator
    {
        // ===== Inputs (match Pine names) =====
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ADX Smoothing (sigLen)", GroupName = "Parameters", Order = 0)]
        public int SigLen { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "DI Length (diLen)", GroupName = "Parameters", Order = 1)]
        public int DiLen { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Level Range (hlRange)", GroupName = "Parameters", Order = 2)]
        public int HlRange { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Level Trend (hlTrend)", GroupName = "Parameters", Order = 3)]
        public int HlTrend { get; set; } = 35;

        // ===== Existing StopX slope lookback =====
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "StopX Slope Bars", GroupName = "Parameters", Order = 4)]
        public int StopXSlopeBars { get; set; } = 2;

        // ===== NEW: Choppiness & regime inputs =====
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "CHOP Period", GroupName = "Parameters", Order = 5)]
        public int ChopPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "CHOP Lookback Bars", GroupName = "Parameters", Order = 6)]
        public int ChopLookbackBars { get; set; } = 3;

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "CHOP High Threshold", GroupName = "Parameters", Order = 7)]
        public int ChopHighThreshold { get; set; } = 60;

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Min ADX for GREEN", GroupName = "Parameters", Order = 8)]
        public int MinAdxForGreen { get; set; } = 20;

        // ===== Internal indicators =====
        private DM dm;
        private ADX adx;
        private ChoppinessIndex chop;

        // ===== Plot indices =====
        private const int PlotAdx        = 0;
        private const int PlotDiPlus     = 1;
        private const int PlotDiMinus    = 2;
        private const int PlotCondition  = 3;
        private const int PlotLongE      = 4;
        private const int PlotShortE     = 5;
        private const int PlotLongEStr   = 6;
        private const int PlotShortEStr  = 7;
        private const int PlotLongX      = 8;
        private const int PlotShortX     = 9;
        private const int PlotStopXLong  = 10; // magenta X for long weakening
        private const int PlotStopXShort = 11; // magenta X for short weakening
        private const int PlotRegime     = 12; // traffic light bar (single line)

        // Public series access
        public Series<double> AdxSeries        { get { return Values[PlotAdx]; } }
        public Series<double> DiPlusSeries     { get { return Values[PlotDiPlus]; } }
        public Series<double> DiMinusSeries    { get { return Values[PlotDiMinus]; } }
        public Series<double> ConditionSeries  { get { return Values[PlotCondition]; } }
        public Series<double> LongESeries      { get { return Values[PlotLongE]; } }
        public Series<double> ShortESeries     { get { return Values[PlotShortE]; } }
        public Series<double> LongEStrSeries   { get { return Values[PlotLongEStr]; } }
        public Series<double> ShortEStrSeries  { get { return Values[PlotShortEStr]; } }
        public Series<double> LongXSeries      { get { return Values[PlotLongX]; } }
        public Series<double> ShortXSeries     { get { return Values[PlotShortX]; } }
        public Series<double> StopXLongSeries  { get { return Values[PlotStopXLong]; } }
        public Series<double> StopXShortSeries { get { return Values[PlotStopXShort]; } }
        public Series<double> RegimeSeries     { get { return Values[PlotRegime]; } }

		protected override void OnStateChange()
		{
		    if (State == State.SetDefaults)
		    {
		        Name      = "ADXGu5v2 (Pine-style + StopX + Regime)";
		        IsOverlay = false;
		        Calculate = Calculate.OnBarClose;
		
		        // Core lines
		        AddPlot(Brushes.Orange,    "ADX");
		        AddPlot(Brushes.LimeGreen, "DIPlus");
		        AddPlot(Brushes.Red,       "DIMinus");
		        AddPlot(Brushes.Gray,      "Condition");
		
		        // NEW: Permanent Reference Lines at 20 and 35
		        // Parameters: Brush, Value, Name
		        AddLine(Brushes.White, 20, "Level 20");
		        AddLine(Brushes.White, 35, "Level 35");
		
		        // Event flags (shapes)
		        AddPlot(Brushes.LimeGreen, "LongE");      // weak long
		        AddPlot(Brushes.Green,     "LongEStr");   // strong long
		        AddPlot(Brushes.OrangeRed, "ShortE");     // weak short
		        AddPlot(Brushes.Red,       "ShortEStr");  // strong short
		        AddPlot(Brushes.Goldenrod, "LongX");      // long exit
		        AddPlot(Brushes.Goldenrod, "ShortX");     // short exit
		
		        // StopX X marks
		        AddPlot(Brushes.Magenta, "StopXLong");
		        AddPlot(Brushes.Magenta, "StopXShort");
		
		        // Traffic light bar
		        AddPlot(Brushes.Gray, "Regime");
		
		        // Styles
		        Plots[PlotAdx].PlotStyle       = PlotStyle.Line;
		        Plots[PlotAdx].Width           = 2;
		        Plots[PlotDiPlus].PlotStyle    = PlotStyle.Line;
		        Plots[PlotDiMinus].PlotStyle   = PlotStyle.Line;
		        Plots[PlotCondition].PlotStyle = PlotStyle.Line;
		
		        // Apply Dotted Style and Width to the Lines
		        Lines[0].DashStyleHelper = DashStyleHelper.Dot;
		        Lines[0].Width = 2;
		        Lines[1].DashStyleHelper = DashStyleHelper.Dot;
		        Lines[1].Width = 2;
		
		        Plots[PlotLongE].PlotStyle     = PlotStyle.TriangleUp;
		        Plots[PlotLongEStr].PlotStyle  = PlotStyle.TriangleUp;
		        Plots[PlotShortE].PlotStyle    = PlotStyle.TriangleDown;
		        Plots[PlotShortEStr].PlotStyle = PlotStyle.TriangleDown;
		        Plots[PlotLongX].PlotStyle     = PlotStyle.Cross;
		        Plots[PlotShortX].PlotStyle    = PlotStyle.Cross;
		
		        Plots[PlotStopXLong].PlotStyle  = PlotStyle.Cross;
		        Plots[PlotStopXShort].PlotStyle = PlotStyle.Cross;
		
		        Plots[PlotRegime].PlotStyle = PlotStyle.Line;
		        Plots[PlotRegime].Width     = 6;
		
		        for (int i = PlotLongE; i <= PlotStopXShort; i++)
		            Plots[i].Width = 2;
		    }
		    else if (State == State.DataLoaded)
		    {
		        dm   = DM(DiLen);
		        adx  = ADX(SigLen);
		        chop = ChoppinessIndex(ChopPeriod);
		    }
		}

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
            {
                for (int i = 0; i <= PlotRegime; i++)
                    Values[i][0] = 0;
                return;
            }

            // --- Core DM / ADX values ---
            double diPlus    = dm.DiPlus[0];
            double diMinus   = dm.DiMinus[0];
            double sig       = adx[0];

            double plusPrev  = dm.DiPlus[1];
            double minusPrev = dm.DiMinus[1];
            double sigPrev   = adx[1];
            double condPrev  = Values[PlotCondition][1];

            // --- Gu5-style logic using DM/ADX ---

            bool hlRange     = sig <= HlRange;
            bool hlRangePrev = sigPrev <= HlRange;

            bool diUp      = diPlus  >= diMinus;
            bool diUpPrev  = plusPrev >= minusPrev;
            bool diUpUp    = diPlus  >= HlTrend;

            bool diDn      = diMinus > diPlus;
            bool diDnPrev  = minusPrev > plusPrev;
            bool diDnDn    = diMinus > HlTrend;

            bool sigUp     = sig > sigPrev;

            // Approximate ta.cross(diPlus, diMinus)
            bool crossDi = (diPlus > diMinus && plusPrev <= minusPrev) ||
                           (diPlus < diMinus && plusPrev >= minusPrev);

            // Entries
            bool entryLong = (!hlRange && diUp && sigUp && !diUpPrev) ||
                             (!hlRange && diUp && sigUp && sig > HlRange && hlRangePrev);

            bool entryShort = (!hlRange && diDn && sigUp && !diDnPrev) ||
                              (!hlRange && diDn && sigUp && sig > HlRange && hlRangePrev);

            bool entryLongStr = !hlRange && diUp && sigUp && diUpUp;
            bool entryShortSt = !hlRange && diDn && sigUp && diDnDn;

            // Exits
            bool exitLong  = (crossDi && diUpPrev) ||
                             (hlRange && !hlRangePrev);

            bool exitShort = (crossDi && diDnPrev) ||
                             (hlRange && !hlRangePrev);

            // Condition state machine
            double cond;

            if      (condPrev !=  1   && entryLongStr) cond =  1;
            else if (condPrev != -1   && entryShortSt) cond = -1;
            else if (condPrev !=  0.5 && entryLong)    cond =  0.5;
            else if (condPrev != -0.5 && entryShort)   cond = -0.5;
            else if (condPrev !=  0   && exitLong)     cond =  0;
            else if (condPrev !=  0   && exitShort)    cond =  0;
            else                                       cond = condPrev;

            bool longE     = condPrev !=  0.5 && cond ==  0.5;
            bool shortE    = condPrev != -0.5 && cond == -0.5;
            bool longEStr  = condPrev !=  1   && cond ==  1;
            bool shortEStr = condPrev != -1   && cond == -1;
            bool longX     = (condPrev ==  0.5 && cond == 0) ||
                             (condPrev ==  1   && cond == 0);
            bool shortX    = (condPrev == -0.5 && cond == 0) ||
                             (condPrev == -1   && cond == 0);

            // --- Assign core plots ---
            Values[PlotAdx][0]       = sig;
            Values[PlotDiPlus][0]    = diPlus;
            Values[PlotDiMinus][0]   = diMinus;
            Values[PlotCondition][0] = cond;

            double alertY = HlTrend + 10;   // above trend line for markers

            Values[PlotLongE][0]     = longE     ? alertY : double.NaN;
            Values[PlotShortE][0]    = shortE    ? alertY : double.NaN;
            Values[PlotLongEStr][0]  = longEStr  ? alertY : double.NaN;
            Values[PlotShortEStr][0] = shortEStr ? alertY : double.NaN;
            Values[PlotLongX][0]     = longX     ? alertY : double.NaN;
            Values[PlotShortX][0]    = shortX    ? alertY : double.NaN;

            // ==========================
            // StopX slope checks & alerts
            // ==========================

            Values[PlotStopXLong][0]  = double.NaN;
            Values[PlotStopXShort][0] = double.NaN;

            // We'll still compute regime even if we don't have enough bars for StopX,
            // but StopX itself needs enough history.
            bool canDoStopX = CurrentBar >= StopXSlopeBars;
            bool canDoChop  = CurrentBar >= ChopLookbackBars;

            bool inLongTrend  = diPlus  >= diMinus;
            bool inShortTrend = diMinus >  diPlus;

            bool longWeak  = false;
            bool shortWeak = false;

            if (canDoStopX)
            {
                double diPlusPrevLook  = dm.DiPlus[StopXSlopeBars];
                double diMinusPrevLook = dm.DiMinus[StopXSlopeBars];

                longWeak  = inLongTrend  && (diPlus  < diPlusPrevLook);
                shortWeak = inShortTrend && (diMinus < diMinusPrevLook);

                if (longWeak)
                {
                    Values[PlotStopXLong][0] = alertY;

                    Alert("ADXGu5v2_StopXLong",
                          Priority.High,
                          "Gu5v2: LONG trend weakening (DI+ slope down)",
                          "Alert1.wav",
                          10,
                          Brushes.Magenta,
                          Brushes.Transparent);
                }

                if (shortWeak)
                {
                    Values[PlotStopXShort][0] = alertY;

                    Alert("ADXGu5v2_StopXShort",
                          Priority.High,
                          "Gu5v2: SHORT trend weakening (DI- slope down)",
                          "Alert1.wav",
                          10,
                          Brushes.Magenta,
                          Brushes.Transparent);
                }
            }

            // ==========================
            // NEW: CHOP + traffic light
            // ==========================

            double regimeY = 65.0;  // fixed Y-level for continuous bar
            Values[PlotRegime][0] = regimeY;   // always plotted; color changes by state

            Brush regimeBrush = Brushes.Transparent; // default = invisible

            if (canDoChop)
            {
                double chopNow  = chop[0];
                double chopPrev = chop[1];

                // Has CHOP been high for the last N bars?
                bool chopHighRecent = true;
                for (int i = 0; i < ChopLookbackBars; i++)
                {
                    if (chop[i] <= ChopHighThreshold)
                    {
                        chopHighRecent = false;
                        break;
                    }
                }

                bool adxStrong =
                    sig > MinAdxForGreen &&
                    sig > sigPrev;               // ADX rising

                bool diStrong = false;
                if (inLongTrend && !longWeak && diPlus > plusPrev)
                    diStrong = true;
                else if (inShortTrend && !shortWeak && diMinus > minusPrev)
                    diStrong = true;

                bool chopSupport =
                    (chopNow < ChopHighThreshold) &&
                    (chopNow < chopPrev);        // CHOP falling out of chop

                // GREEN setup
                bool green = !chopHighRecent && adxStrong && diStrong && chopSupport;

                // RED: StopX fired OR CHOP rising back into chop zone
                bool redFromSlope = longWeak || shortWeak;
                bool redFromChop  = (chopNow > chopPrev) && (chopNow >= ChopHighThreshold);
                bool red          = redFromSlope || redFromChop;

                // Priority: RED > YELLOW > GREEN
                if (red)
                    regimeBrush = Brushes.Red;
                else if (chopHighRecent)
                    regimeBrush = Brushes.Yellow;
                else if (green)
                    regimeBrush = Brushes.LimeGreen;
            }

            PlotBrushes[PlotRegime][0] = regimeBrush;
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ADXGu5v2[] cacheADXGu5v2;
		public ADXGu5v2 ADXGu5v2(int sigLen, int diLen, int hlRange, int hlTrend, int stopXSlopeBars, int chopPeriod, int chopLookbackBars, int chopHighThreshold, int minAdxForGreen)
		{
			return ADXGu5v2(Input, sigLen, diLen, hlRange, hlTrend, stopXSlopeBars, chopPeriod, chopLookbackBars, chopHighThreshold, minAdxForGreen);
		}

		public ADXGu5v2 ADXGu5v2(ISeries<double> input, int sigLen, int diLen, int hlRange, int hlTrend, int stopXSlopeBars, int chopPeriod, int chopLookbackBars, int chopHighThreshold, int minAdxForGreen)
		{
			if (cacheADXGu5v2 != null)
				for (int idx = 0; idx < cacheADXGu5v2.Length; idx++)
					if (cacheADXGu5v2[idx] != null && cacheADXGu5v2[idx].SigLen == sigLen && cacheADXGu5v2[idx].DiLen == diLen && cacheADXGu5v2[idx].HlRange == hlRange && cacheADXGu5v2[idx].HlTrend == hlTrend && cacheADXGu5v2[idx].StopXSlopeBars == stopXSlopeBars && cacheADXGu5v2[idx].ChopPeriod == chopPeriod && cacheADXGu5v2[idx].ChopLookbackBars == chopLookbackBars && cacheADXGu5v2[idx].ChopHighThreshold == chopHighThreshold && cacheADXGu5v2[idx].MinAdxForGreen == minAdxForGreen && cacheADXGu5v2[idx].EqualsInput(input))
						return cacheADXGu5v2[idx];
			return CacheIndicator<ADXGu5v2>(new ADXGu5v2(){ SigLen = sigLen, DiLen = diLen, HlRange = hlRange, HlTrend = hlTrend, StopXSlopeBars = stopXSlopeBars, ChopPeriod = chopPeriod, ChopLookbackBars = chopLookbackBars, ChopHighThreshold = chopHighThreshold, MinAdxForGreen = minAdxForGreen }, input, ref cacheADXGu5v2);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ADXGu5v2 ADXGu5v2(int sigLen, int diLen, int hlRange, int hlTrend, int stopXSlopeBars, int chopPeriod, int chopLookbackBars, int chopHighThreshold, int minAdxForGreen)
		{
			return indicator.ADXGu5v2(Input, sigLen, diLen, hlRange, hlTrend, stopXSlopeBars, chopPeriod, chopLookbackBars, chopHighThreshold, minAdxForGreen);
		}

		public Indicators.ADXGu5v2 ADXGu5v2(ISeries<double> input , int sigLen, int diLen, int hlRange, int hlTrend, int stopXSlopeBars, int chopPeriod, int chopLookbackBars, int chopHighThreshold, int minAdxForGreen)
		{
			return indicator.ADXGu5v2(input, sigLen, diLen, hlRange, hlTrend, stopXSlopeBars, chopPeriod, chopLookbackBars, chopHighThreshold, minAdxForGreen);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ADXGu5v2 ADXGu5v2(int sigLen, int diLen, int hlRange, int hlTrend, int stopXSlopeBars, int chopPeriod, int chopLookbackBars, int chopHighThreshold, int minAdxForGreen)
		{
			return indicator.ADXGu5v2(Input, sigLen, diLen, hlRange, hlTrend, stopXSlopeBars, chopPeriod, chopLookbackBars, chopHighThreshold, minAdxForGreen);
		}

		public Indicators.ADXGu5v2 ADXGu5v2(ISeries<double> input , int sigLen, int diLen, int hlRange, int hlTrend, int stopXSlopeBars, int chopPeriod, int chopLookbackBars, int chopHighThreshold, int minAdxForGreen)
		{
			return indicator.ADXGu5v2(input, sigLen, diLen, hlRange, hlTrend, stopXSlopeBars, chopPeriod, chopLookbackBars, chopHighThreshold, minAdxForGreen);
		}
	}
}

#endregion
