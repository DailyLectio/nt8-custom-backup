#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows.Media;                     // Brushes
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class SurrogateCoreLevels_NDR : Indicator
    {
        // -------- Inputs --------
        [NinjaScriptProperty, Display(Name = "Use Auto NDR (20-day ATR)", Order = 0, GroupName = "NDR")]
        public bool UseAutoNdr { get; set; } = true;

        [NinjaScriptProperty, Display(Name = "Manual NDR (pts, if Auto off)", Order = 1, GroupName = "NDR")]
        public double ManualNdr { get; set; } = 50.0;

        [NinjaScriptProperty, Display(Name = "NDR Lookback (days)", Order = 2, GroupName = "NDR")]
        public int NdrLen { get; set; } = 20;

        [NinjaScriptProperty, Display(Name = "First-Hour End (HHmmss)", Order = 0, GroupName = "Windows")]
        public int FirstHourEnd { get; set; } = 103000;

        [NinjaScriptProperty, Display(Name = "B2/R2 factor (×NDR)", Order = 0, GroupName = "Factors")]
        public double F2 { get; set; } = 0.35;

        [NinjaScriptProperty, Display(Name = "B4/R4 factor (×NDR)", Order = 1, GroupName = "Factors")]
        public double F4 { get; set; } = 1.00;

        // -------- Series for readability (plots already exist via AddPlot) --------
        [Browsable(false), XmlIgnore] public Series<double> Mean;
        [Browsable(false), XmlIgnore] public Series<double> B2;
        [Browsable(false), XmlIgnore] public Series<double> B4;
        [Browsable(false), XmlIgnore] public Series<double> R2;
        [Browsable(false), XmlIgnore] public Series<double> R4;

        // -------- Internals --------
        private SessionIterator sess;
        private bool firstHourDone;
        private DateTime day;
        private double ibHigh, ibLow;
        private double ndrPts;
        private Indicators.ATR atrDaily;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name      = "SurrogateCoreLevels_NDR";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;

                AddPlot(Brushes.DeepSkyBlue, "Mean");
                AddPlot(Brushes.LimeGreen,   "B2");
                AddPlot(Brushes.LimeGreen,   "B4");
                AddPlot(Brushes.OrangeRed,   "R2");
                AddPlot(Brushes.OrangeRed,   "R4");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Day, 1);   // for daily ATR
            }
            else if (State == State.DataLoaded)
            {
                sess     = new SessionIterator(Bars);
                atrDaily = ATR(BarsArray[1], NdrLen);

                Mean = new Series<double>(this);
                B2   = new Series<double>(this);
                B4   = new Series<double>(this);
                R2   = new Series<double>(this);
                R4   = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            // Daily series keeps ATR updated
            if (BarsInProgress == 1)
                return;

            if (CurrentBar < 10) return;

            DateTime t = Times[0][0];
            DateTime d = sess.GetTradingDay(t);
            if (d != day)
            {
                day = d;
                firstHourDone = false;
                ibHigh = double.MinValue;
                ibLow  = double.MaxValue;

                ndrPts = (UseAutoNdr && CurrentBars[1] > NdrLen)
                       ? atrDaily[0]
                       : ManualNdr;
            }

            // Build first-hour range 09:30 → FirstHourEnd
            var start = new DateTime(day.Year, day.Month, day.Day, 9, 30, 0);
            var end   = new DateTime(day.Year, day.Month, day.Day,
                                     FirstHourEnd / 10000,
                                     (FirstHourEnd / 100) % 100,
                                     FirstHourEnd % 100);

            if (t >= start && t < end)
            {
                ibHigh = Math.Max(ibHigh, High[0]);
                ibLow  = Math.Min(ibLow,  Low[0]);
            }
            if (!firstHourDone && t >= end && ibHigh > double.MinValue && ibLow < double.MaxValue)
                firstHourDone = true;

            // If first hour not done yet, hide plots
            if (!firstHourDone)
            {
                for (int i = 0; i < 5; i++) Values[i][0] = double.NaN;
                return;
            }

            double m  = (ibHigh + ibLow) * 0.5;
            double b2 = m + F2 * ndrPts;
            double b4 = m + F4 * ndrPts;
            double r2 = m - F2 * ndrPts;
            double r4 = m - F4 * ndrPts;

            Mean[0] = Values[0][0] = m;
            B2[0]   = Values[1][0] = b2;
            B4[0]   = Values[2][0] = b4;
            R2[0]   = Values[3][0] = r2;
            R4[0]   = Values[4][0] = r4;
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private SurrogateCoreLevels_NDR[] cacheSurrogateCoreLevels_NDR;
		public SurrogateCoreLevels_NDR SurrogateCoreLevels_NDR(bool useAutoNdr, double manualNdr, int ndrLen, int firstHourEnd, double f2, double f4)
		{
			return SurrogateCoreLevels_NDR(Input, useAutoNdr, manualNdr, ndrLen, firstHourEnd, f2, f4);
		}

		public SurrogateCoreLevels_NDR SurrogateCoreLevels_NDR(ISeries<double> input, bool useAutoNdr, double manualNdr, int ndrLen, int firstHourEnd, double f2, double f4)
		{
			if (cacheSurrogateCoreLevels_NDR != null)
				for (int idx = 0; idx < cacheSurrogateCoreLevels_NDR.Length; idx++)
					if (cacheSurrogateCoreLevels_NDR[idx] != null && cacheSurrogateCoreLevels_NDR[idx].UseAutoNdr == useAutoNdr && cacheSurrogateCoreLevels_NDR[idx].ManualNdr == manualNdr && cacheSurrogateCoreLevels_NDR[idx].NdrLen == ndrLen && cacheSurrogateCoreLevels_NDR[idx].FirstHourEnd == firstHourEnd && cacheSurrogateCoreLevels_NDR[idx].F2 == f2 && cacheSurrogateCoreLevels_NDR[idx].F4 == f4 && cacheSurrogateCoreLevels_NDR[idx].EqualsInput(input))
						return cacheSurrogateCoreLevels_NDR[idx];
			return CacheIndicator<SurrogateCoreLevels_NDR>(new SurrogateCoreLevels_NDR(){ UseAutoNdr = useAutoNdr, ManualNdr = manualNdr, NdrLen = ndrLen, FirstHourEnd = firstHourEnd, F2 = f2, F4 = f4 }, input, ref cacheSurrogateCoreLevels_NDR);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.SurrogateCoreLevels_NDR SurrogateCoreLevels_NDR(bool useAutoNdr, double manualNdr, int ndrLen, int firstHourEnd, double f2, double f4)
		{
			return indicator.SurrogateCoreLevels_NDR(Input, useAutoNdr, manualNdr, ndrLen, firstHourEnd, f2, f4);
		}

		public Indicators.SurrogateCoreLevels_NDR SurrogateCoreLevels_NDR(ISeries<double> input , bool useAutoNdr, double manualNdr, int ndrLen, int firstHourEnd, double f2, double f4)
		{
			return indicator.SurrogateCoreLevels_NDR(input, useAutoNdr, manualNdr, ndrLen, firstHourEnd, f2, f4);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.SurrogateCoreLevels_NDR SurrogateCoreLevels_NDR(bool useAutoNdr, double manualNdr, int ndrLen, int firstHourEnd, double f2, double f4)
		{
			return indicator.SurrogateCoreLevels_NDR(Input, useAutoNdr, manualNdr, ndrLen, firstHourEnd, f2, f4);
		}

		public Indicators.SurrogateCoreLevels_NDR SurrogateCoreLevels_NDR(ISeries<double> input , bool useAutoNdr, double manualNdr, int ndrLen, int firstHourEnd, double f2, double f4)
		{
			return indicator.SurrogateCoreLevels_NDR(input, useAutoNdr, manualNdr, ndrLen, firstHourEnd, f2, f4);
		}
	}
}

#endregion
