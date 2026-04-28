#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;                       // Brushes
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;                      // TextPosition
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;       // Draw.TextFixed
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GoNoBadge : Indicator
    {
        // -------- Inputs --------
        [NinjaScriptProperty, Display(Name = "EMA Fast", Order = 0, GroupName = "Trend")]
        public int EmaFastLen { get; set; } = 8;

        [NinjaScriptProperty, Display(Name = "EMA Slow", Order = 1, GroupName = "Trend")]
        public int EmaSlowLen { get; set; } = 24;

        [NinjaScriptProperty, Display(Name = "ADX Len", Order = 2, GroupName = "Trend")]
        public int AdxLen { get; set; } = 14;

        [NinjaScriptProperty, Display(Name = "ADX Min", Order = 3, GroupName = "Trend")]
        public double AdxMin { get; set; } = 18;

        [NinjaScriptProperty, Display(Name = "ADX Slope Bars", Order = 4, GroupName = "Trend")]
        public int AdxSlopeBars { get; set; } = 3;

        [NinjaScriptProperty, Display(Name = "UD Window (bars)", Order = 0, GroupName = "Participation")]
        public int UdWindow { get; set; } = 45;

        [NinjaScriptProperty, Display(Name = "UD Threshold", Order = 1, GroupName = "Participation")]
        public double UdThresh { get; set; } = 1.8;

        [NinjaScriptProperty, Display(Name = "RVol Window (bars)", Order = 2, GroupName = "Participation")]
        public int RvolWindow { get; set; } = 30;

        [NinjaScriptProperty, Display(Name = "RVol Threshold", Order = 3, GroupName = "Participation")]
        public double RvolThresh { get; set; } = 1.25;

        [NinjaScriptProperty, Display(Name = "Badge Position", Order = 0, GroupName = "Visual")]
        public TextPosition BadgePosition { get; set; } = TextPosition.TopLeft;   // <-- compatible on all builds

        // -------- Internals --------
        private EMA emaFast, emaSlow;
        private ADX adx;
        private SessionIterator sess;
        private DateTime day;

        // simple session VWAP
        private double sumPV, sumV;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name      = "GoNoBadge";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
            }
            else if (State == State.DataLoaded)
            {
                emaFast = EMA(EmaFastLen);
                emaSlow = EMA(EmaSlowLen);
                adx     = ADX(AdxLen);
                sess    = new SessionIterator(Bars);

                sumPV = sumV = 0.0;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(Math.Max(EmaFastLen, EmaSlowLen), AdxLen) + 5)
                return;

            // new session → reset session VWAP
            DateTime d = sess.GetTradingDay(Time[0]);
            if (d != day)
            {
                day  = d;
                sumPV = sumV = 0.0;
            }

            // accumulate session VWAP
            double vol = Volume[0];
            double tp  = (High[0] + Low[0] + Close[0]) / 3.0;
            sumPV += tp * vol;
            sumV  += vol;
            double vwap = (sumV > 0 ? sumPV / sumV : Close[0]);

            // side from EMAs + location vs session vwap
            int side = 0; string sideTxt = "flat";
            if (emaFast[0] > emaSlow[0] && Close[0] > vwap) { side = +1; sideTxt = "long"; }
            else if (emaFast[0] < emaSlow[0] && Close[0] < vwap) { side = -1; sideTxt = "short"; }

            // ADX filter (level + slope)
            bool adxOK = adx[0] >= AdxMin;
            if (adxOK && AdxSlopeBars > 0)
            {
                for (int i = 0; i < AdxSlopeBars; i++)
                    adxOK = adxOK && adx[i] > adx[i + 1];
            }

            // UD participation (up/down closes over UdWindow)
            int up = 0, dn = 0, n = Math.Min(UdWindow, CurrentBar);
            for (int i = 0; i < n; i++)
            {
                if (Close[i] > Close[i + 1]) up++;
                else if (Close[i] < Close[i + 1]) dn++;
            }
            double ud = (dn == 0) ? up : (double)up / dn;
            bool udOK = (side == +1) ? (ud >= UdThresh)
                       : (side == -1) ? (ud <= 1.0 / UdThresh)
                       : false;

            // RVol (current window vs prior window)
            int w = Math.Min(RvolWindow, CurrentBar / 2);
            double cur = 0, prev = 0;
            for (int i = 0; i < w; i++) cur += Volume[i];
            for (int i = w; i < 2 * w; i++) prev += Volume[i];
            double rvol = (prev > 0 ? cur / prev : 1.0);
            bool rvolOK = rvol >= RvolThresh;

            bool go = (side != 0) && adxOK && udOK && rvolOK;

            string text =
                (go ? "GO" : "NO GO") +
                $"  | side={sideTxt}  EMA({EmaFastLen}/{EmaSlowLen})  " +
                $"ADX={adx[0]:F1}{(adxOK ? "↑" : "x")}  UD={ud:F2}  RV={rvol:F2}";

            // Use the 5-argument overload (compatible across builds)
            Draw.TextFixed(this, "gng_badge", text, BadgePosition);
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GoNoBadge[] cacheGoNoBadge;
		public GoNoBadge GoNoBadge(int emaFastLen, int emaSlowLen, int adxLen, double adxMin, int adxSlopeBars, int udWindow, double udThresh, int rvolWindow, double rvolThresh, TextPosition badgePosition)
		{
			return GoNoBadge(Input, emaFastLen, emaSlowLen, adxLen, adxMin, adxSlopeBars, udWindow, udThresh, rvolWindow, rvolThresh, badgePosition);
		}

		public GoNoBadge GoNoBadge(ISeries<double> input, int emaFastLen, int emaSlowLen, int adxLen, double adxMin, int adxSlopeBars, int udWindow, double udThresh, int rvolWindow, double rvolThresh, TextPosition badgePosition)
		{
			if (cacheGoNoBadge != null)
				for (int idx = 0; idx < cacheGoNoBadge.Length; idx++)
					if (cacheGoNoBadge[idx] != null && cacheGoNoBadge[idx].EmaFastLen == emaFastLen && cacheGoNoBadge[idx].EmaSlowLen == emaSlowLen && cacheGoNoBadge[idx].AdxLen == adxLen && cacheGoNoBadge[idx].AdxMin == adxMin && cacheGoNoBadge[idx].AdxSlopeBars == adxSlopeBars && cacheGoNoBadge[idx].UdWindow == udWindow && cacheGoNoBadge[idx].UdThresh == udThresh && cacheGoNoBadge[idx].RvolWindow == rvolWindow && cacheGoNoBadge[idx].RvolThresh == rvolThresh && cacheGoNoBadge[idx].BadgePosition == badgePosition && cacheGoNoBadge[idx].EqualsInput(input))
						return cacheGoNoBadge[idx];
			return CacheIndicator<GoNoBadge>(new GoNoBadge(){ EmaFastLen = emaFastLen, EmaSlowLen = emaSlowLen, AdxLen = adxLen, AdxMin = adxMin, AdxSlopeBars = adxSlopeBars, UdWindow = udWindow, UdThresh = udThresh, RvolWindow = rvolWindow, RvolThresh = rvolThresh, BadgePosition = badgePosition }, input, ref cacheGoNoBadge);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GoNoBadge GoNoBadge(int emaFastLen, int emaSlowLen, int adxLen, double adxMin, int adxSlopeBars, int udWindow, double udThresh, int rvolWindow, double rvolThresh, TextPosition badgePosition)
		{
			return indicator.GoNoBadge(Input, emaFastLen, emaSlowLen, adxLen, adxMin, adxSlopeBars, udWindow, udThresh, rvolWindow, rvolThresh, badgePosition);
		}

		public Indicators.GoNoBadge GoNoBadge(ISeries<double> input , int emaFastLen, int emaSlowLen, int adxLen, double adxMin, int adxSlopeBars, int udWindow, double udThresh, int rvolWindow, double rvolThresh, TextPosition badgePosition)
		{
			return indicator.GoNoBadge(input, emaFastLen, emaSlowLen, adxLen, adxMin, adxSlopeBars, udWindow, udThresh, rvolWindow, rvolThresh, badgePosition);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GoNoBadge GoNoBadge(int emaFastLen, int emaSlowLen, int adxLen, double adxMin, int adxSlopeBars, int udWindow, double udThresh, int rvolWindow, double rvolThresh, TextPosition badgePosition)
		{
			return indicator.GoNoBadge(Input, emaFastLen, emaSlowLen, adxLen, adxMin, adxSlopeBars, udWindow, udThresh, rvolWindow, rvolThresh, badgePosition);
		}

		public Indicators.GoNoBadge GoNoBadge(ISeries<double> input , int emaFastLen, int emaSlowLen, int adxLen, double adxMin, int adxSlopeBars, int udWindow, double udThresh, int rvolWindow, double rvolThresh, TextPosition badgePosition)
		{
			return indicator.GoNoBadge(input, emaFastLen, emaSlowLen, adxLen, adxMin, adxSlopeBars, udWindow, udThresh, rvolWindow, rvolThresh, badgePosition);
		}
	}
}

#endregion
