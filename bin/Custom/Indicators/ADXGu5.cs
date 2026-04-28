#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class ADXGu5 : Indicator
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

        // ===== Internal indicators =====
        private DM  dm;
        private ADX adx;

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

        // Public series access for strategies
        public Series<double> AdxSeries       { get { return Values[PlotAdx]; } }
        public Series<double> DiPlusSeries    { get { return Values[PlotDiPlus]; } }
        public Series<double> DiMinusSeries   { get { return Values[PlotDiMinus]; } }
        public Series<double> ConditionSeries { get { return Values[PlotCondition]; } }
        public Series<double> LongESeries     { get { return Values[PlotLongE]; } }
        public Series<double> ShortESeries    { get { return Values[PlotShortE]; } }
        public Series<double> LongEStrSeries  { get { return Values[PlotLongEStr]; } }
        public Series<double> ShortEStrSeries { get { return Values[PlotShortEStr]; } }
        public Series<double> LongXSeries     { get { return Values[PlotLongX]; } }
        public Series<double> ShortXSeries    { get { return Values[PlotShortX]; } }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name      = "ADXGu5 (Pine-style, NT8 DM/ADX)";
                IsOverlay = false;
                Calculate = Calculate.OnBarClose;

                // Core lines
                AddPlot(Brushes.Orange,    "ADX");
                AddPlot(Brushes.LimeGreen, "DIPlus");
                AddPlot(Brushes.Red,       "DIMinus");
                AddPlot(Brushes.Gray,      "Condition");

                // Event flags (shapes)
                AddPlot(Brushes.LimeGreen, "LongE");      // weak long
                AddPlot(Brushes.Green,     "LongEStr");   // strong long
                AddPlot(Brushes.OrangeRed, "ShortE");     // weak short
                AddPlot(Brushes.Red,       "ShortEStr");  // strong short
                AddPlot(Brushes.Goldenrod, "LongX");      // long exit
                AddPlot(Brushes.Goldenrod, "ShortX");     // short exit

                // Styles
                Plots[PlotAdx].PlotStyle       = PlotStyle.Line;
                Plots[PlotAdx].Width           = 2;
                Plots[PlotDiPlus].PlotStyle    = PlotStyle.Line;
                Plots[PlotDiMinus].PlotStyle   = PlotStyle.Line;
                Plots[PlotCondition].PlotStyle = PlotStyle.Line;

                Plots[PlotLongE].PlotStyle     = PlotStyle.TriangleUp;
                Plots[PlotLongEStr].PlotStyle  = PlotStyle.TriangleUp;
                Plots[PlotShortE].PlotStyle    = PlotStyle.TriangleDown;
                Plots[PlotShortEStr].PlotStyle = PlotStyle.TriangleDown;
                Plots[PlotLongX].PlotStyle     = PlotStyle.Cross;
                Plots[PlotShortX].PlotStyle    = PlotStyle.Cross;

                for (int i = PlotLongE; i <= PlotShortX; i++)
                    Plots[i].Width = 2;
            }
            else if (State == State.DataLoaded)
            {
                // Use NT8's built-in DM and ADX for robustness
                dm  = DM(DiLen);
                adx = ADX(SigLen);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
            {
                // Initialize first couple of bars
                for (int i = 0; i <= PlotShortX; i++)
                    Values[i][0] = 0;
                return;
            }

            // Current + previous values
            double diPlus     = dm.DiPlus[0];
            double diMinus    = dm.DiMinus[0];
            double sig        = adx[0];

            double plusPrev   = dm.DiPlus[1];
            double minusPrev  = dm.DiMinus[1];
            double sigPrev    = adx[1];
            double condPrev   = Values[PlotCondition][1];

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

            // Event flags
            bool longE     = condPrev !=  0.5 && cond ==  0.5;
            bool shortE    = condPrev != -0.5 && cond == -0.5;
            bool longEStr  = condPrev !=  1   && cond ==  1;
            bool shortEStr = condPrev != -1   && cond == -1;
            bool longX     = (condPrev ==  0.5 && cond == 0) ||
                             (condPrev ==  1   && cond == 0);
            bool shortX    = (condPrev == -0.5 && cond == 0) ||
                             (condPrev == -1   && cond == 0);

            // --- Assign plots ---

            Values[PlotAdx][0]       = sig;
            Values[PlotDiPlus][0]    = diPlus;
            Values[PlotDiMinus][0]   = diMinus;
            Values[PlotCondition][0] = cond;

            double alertY = HlTrend + 10;   // same general idea as Pine: above trend line

            Values[PlotLongE][0]     = longE     ? alertY : double.NaN;
            Values[PlotShortE][0]    = shortE    ? alertY : double.NaN;
            Values[PlotLongEStr][0]  = longEStr  ? alertY : double.NaN;
            Values[PlotShortEStr][0] = shortEStr ? alertY : double.NaN;
            Values[PlotLongX][0]     = longX     ? alertY : double.NaN;
            Values[PlotShortX][0]    = shortX    ? alertY : double.NaN;
        }
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ADXGu5[] cacheADXGu5;
		public ADXGu5 ADXGu5(int sigLen, int diLen, int hlRange, int hlTrend)
		{
			return ADXGu5(Input, sigLen, diLen, hlRange, hlTrend);
		}

		public ADXGu5 ADXGu5(ISeries<double> input, int sigLen, int diLen, int hlRange, int hlTrend)
		{
			if (cacheADXGu5 != null)
				for (int idx = 0; idx < cacheADXGu5.Length; idx++)
					if (cacheADXGu5[idx] != null && cacheADXGu5[idx].SigLen == sigLen && cacheADXGu5[idx].DiLen == diLen && cacheADXGu5[idx].HlRange == hlRange && cacheADXGu5[idx].HlTrend == hlTrend && cacheADXGu5[idx].EqualsInput(input))
						return cacheADXGu5[idx];
			return CacheIndicator<ADXGu5>(new ADXGu5(){ SigLen = sigLen, DiLen = diLen, HlRange = hlRange, HlTrend = hlTrend }, input, ref cacheADXGu5);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ADXGu5 ADXGu5(int sigLen, int diLen, int hlRange, int hlTrend)
		{
			return indicator.ADXGu5(Input, sigLen, diLen, hlRange, hlTrend);
		}

		public Indicators.ADXGu5 ADXGu5(ISeries<double> input , int sigLen, int diLen, int hlRange, int hlTrend)
		{
			return indicator.ADXGu5(input, sigLen, diLen, hlRange, hlTrend);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ADXGu5 ADXGu5(int sigLen, int diLen, int hlRange, int hlTrend)
		{
			return indicator.ADXGu5(Input, sigLen, diLen, hlRange, hlTrend);
		}

		public Indicators.ADXGu5 ADXGu5(ISeries<double> input , int sigLen, int diLen, int hlRange, int hlTrend)
		{
			return indicator.ADXGu5(input, sigLen, diLen, hlRange, hlTrend);
		}
	}
}

#endregion
