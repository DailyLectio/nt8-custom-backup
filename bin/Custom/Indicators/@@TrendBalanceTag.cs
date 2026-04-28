#region Using
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;                // TextPosition, SimpleFont
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools; // Draw.TextFixed
using NinjaTrader.NinjaScript.Indicators;
#endregion

// IMPORTANT: keep this namespace exactly as below for NT8 to wire things up.
namespace NinjaTrader.NinjaScript.Indicators
{
    public class TrendBalanceTag : Indicator
    {
        // -------- Parameters (can be adjusted in UI) --------
        [NinjaScriptProperty] public DateTime ScoreTime    { get; set; } = DateTime.Today.AddHours(10).AddMinutes(15);
        [NinjaScriptProperty] public DateTime IBEndTime    { get; set; } = DateTime.Today.AddHours(10);
        [NinjaScriptProperty] public int      HoldMinutes  { get; set; } = 15;
        [NinjaScriptProperty] public double   RVolThresh   { get; set; } = 1.35;
        [NinjaScriptProperty] public double   UDThresh     { get; set; } = 2.0;
        [NinjaScriptProperty] public double   IBBreakATR   { get; set; } = 0.35;
        [NinjaScriptProperty] public int      ADXLen       { get; set; } = 14;
        [NinjaScriptProperty] public int      LookbackDays { get; set; } = 20;
        [NinjaScriptProperty] public bool     WriteCsv     { get; set; } = false;
        [NinjaScriptProperty] public string   CsvName      { get; set; } = "TB_Tag.csv";

        // -------- Outputs --------
        public int    Score      { get; private set; }
        public bool   IsTrendDay { get; private set; }
        public string Reasons    { get; private set; } = "";

        // -------- Internals --------
        private ADX adx;
        private SessionIterator sess;
        private DateTime day, ibStart, ibEnd, scoreAt;
        private double ibHigh, ibLow, firstHourVol;
        private bool scoredToday, ibComputed, heldOutside;
        private readonly Queue<double> firstHourVolHist = new Queue<double>();
        private readonly SimpleFont hudFont = new SimpleFont("Segoe UI", 12) { Bold = true };

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name      = "TrendBalanceTag";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
            }
            else if (State == State.DataLoaded)
            {
                adx  = ADX(ADXLen);
                // AddChartIndicator(adx);   // optional: uncomment if you want to see ADX
                sess = new SessionIterator(Bars);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 10)
                return;

            // New trading day reset
            var currDay = sess.GetTradingDay(Times[0][0]);
            if (currDay != day)
            {
                day         = currDay;
                scoredToday = false; 
                ibComputed  = false; 
                heldOutside = false;

                ibHigh      = double.MinValue; 
                ibLow       = double.MaxValue; 
                firstHourVol= 0;

                ibStart     = new DateTime(day.Year, day.Month, day.Day, 9, 30, 0);
                ibEnd       = new DateTime(day.Year, day.Month, day.Day, IBEndTime.Hour, IBEndTime.Minute, 0);
                scoreAt     = new DateTime(day.Year, day.Month, day.Day, ScoreTime.Hour, ScoreTime.Minute, 0);
            }

            DateTime t = Times[0][0];

            // Build Initial Balance 9:30–10:00
            if (t >= ibStart && t < ibEnd)
            {
                ibHigh = Math.Max(ibHigh, Highs[0][0]);
                ibLow  = Math.Min(ibLow,  Lows[0][0]);
                ibComputed = true;
            }

            // First-hour volume (up to score time)
            if (t >= ibStart && t <= scoreAt)
                firstHourVol += Volumes[0][0];

            // IB break & hold check between IB end and score time
            if (ibComputed && t >= ibEnd && t <= scoreAt)
            {
                double atr10       = ATR(10)[0];
                bool outsideUp     = Closes[0][0] > ibHigh + IBBreakATR * atr10;
                bool outsideDown   = Closes[0][0] < ibLow  - IBBreakATR * atr10;

                if (outsideUp || outsideDown)
                {
                    int bars        = Math.Min(HoldMinutes, CurrentBar);
                    int outsideBars = 0;
                    for (int i = 0; i < bars; i++)
                    {
                        double c   = Closes[0][i];
                        double atr = ATR(10)[i];
                        if (c > ibHigh + IBBreakATR * atr || c < ibLow - IBBreakATR * atr)
                            outsideBars++;
                    }
                    heldOutside = outsideBars >= (int)(0.8 * bars);
                }
            }

            // Score the day at scoreAt (default 10:15)
            if (!scoredToday && t >= scoreAt)
            {
                // Relative Volume (first hour vs N-day average of first hour)
                double rvol = 1.0;
                if (firstHourVolHist.Count >= 5)
                {
                    double avg = 0;
                    foreach (var v in firstHourVolHist) avg += v;
                    avg /= firstHourVolHist.Count;
                    rvol = avg > 0 ? firstHourVol / avg : 1.0;
                }

                // Up/Down bars ratio proxy (last ~45 bars, guard for small counts)
                int upBars = 0, downBars = 0;
                int barsToCheck = Math.Max(0, Math.Min(CurrentBar - 1, 45));
                for (int i = 0; i < barsToCheck; i++)
                {
                    if (Closes[0][i] > Closes[0][i + 1]) upBars++;
                    else if (Closes[0][i] < Closes[0][i + 1]) downBars++;
                }
                double ud  = (downBars == 0) ? upBars : (double)upBars / downBars;

                // ADX rising 3 bars (trend strengthening)
                bool adxUp = adx[0] > adx[1] && adx[1] > adx[2];

                // Score & reasons
                int s = 0; var why = new List<string>();
                if (rvol >= RVolThresh)        { s++; why.Add($"RVol={rvol:F2}"); }
                if (ud   >= UDThresh)          { s++; why.Add($"UD={ud:F2}"); }
                if (ibComputed && heldOutside) { s++; why.Add("IBHold"); }
                if (adxUp)                     { s++; why.Add("ADXUp"); }

                Score      = s;
                IsTrendDay = Score >= 3;
                Reasons    = string.Join("|", why);

                // Keep a rolling history for RVol baseline
                firstHourVolHist.Enqueue(firstHourVol);
                while (firstHourVolHist.Count > LookbackDays)
                    firstHourVolHist.Dequeue();

                // --------------- HUD ---------------
                // NT8 overload requires: owner, tag, text, position, textBrush, font, areaBrush, outlineBrush, opacity
                Draw.TextFixed(this, "tb_tag",
                    $"T/B @10:15  Score={Score}  {(IsTrendDay ? "TREND" : "BALANCE")}  [{Reasons}]",
                    TextPosition.TopLeft, Brushes.Yellow, hudFont, null, null, 0);

                // --------------- CSV ---------------
                if (WriteCsv)
                {
                    try
                    {
                        string path   = Path.Combine(Core.Globals.UserDataDir, CsvName);
                        bool   isNew  = !File.Exists(path);
                        using (var sw = new StreamWriter(path, true))
                        {
                            if (isNew)
                                sw.WriteLine("Date,Score,IsTrend,RVol,UD,IBHold,ADXUp,Reasons");
                            sw.WriteLine($"{day:yyyy-MM-dd},{Score},{IsTrendDay},{rvol:F2},{ud:F2},{heldOutside},{adxUp},{Reasons}");
                        }
                    }
                    catch { /* swallow file I/O errors */ }
                }

                scoredToday = true;
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TrendBalanceTag[] cacheTrendBalanceTag;
		public TrendBalanceTag TrendBalanceTag(DateTime scoreTime, DateTime iBEndTime, int holdMinutes, double rVolThresh, double uDThresh, double iBBreakATR, int aDXLen, int lookbackDays, bool writeCsv, string csvName)
		{
			return TrendBalanceTag(Input, scoreTime, iBEndTime, holdMinutes, rVolThresh, uDThresh, iBBreakATR, aDXLen, lookbackDays, writeCsv, csvName);
		}

		public TrendBalanceTag TrendBalanceTag(ISeries<double> input, DateTime scoreTime, DateTime iBEndTime, int holdMinutes, double rVolThresh, double uDThresh, double iBBreakATR, int aDXLen, int lookbackDays, bool writeCsv, string csvName)
		{
			if (cacheTrendBalanceTag != null)
				for (int idx = 0; idx < cacheTrendBalanceTag.Length; idx++)
					if (cacheTrendBalanceTag[idx] != null && cacheTrendBalanceTag[idx].ScoreTime == scoreTime && cacheTrendBalanceTag[idx].IBEndTime == iBEndTime && cacheTrendBalanceTag[idx].HoldMinutes == holdMinutes && cacheTrendBalanceTag[idx].RVolThresh == rVolThresh && cacheTrendBalanceTag[idx].UDThresh == uDThresh && cacheTrendBalanceTag[idx].IBBreakATR == iBBreakATR && cacheTrendBalanceTag[idx].ADXLen == aDXLen && cacheTrendBalanceTag[idx].LookbackDays == lookbackDays && cacheTrendBalanceTag[idx].WriteCsv == writeCsv && cacheTrendBalanceTag[idx].CsvName == csvName && cacheTrendBalanceTag[idx].EqualsInput(input))
						return cacheTrendBalanceTag[idx];
			return CacheIndicator<TrendBalanceTag>(new TrendBalanceTag(){ ScoreTime = scoreTime, IBEndTime = iBEndTime, HoldMinutes = holdMinutes, RVolThresh = rVolThresh, UDThresh = uDThresh, IBBreakATR = iBBreakATR, ADXLen = aDXLen, LookbackDays = lookbackDays, WriteCsv = writeCsv, CsvName = csvName }, input, ref cacheTrendBalanceTag);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TrendBalanceTag TrendBalanceTag(DateTime scoreTime, DateTime iBEndTime, int holdMinutes, double rVolThresh, double uDThresh, double iBBreakATR, int aDXLen, int lookbackDays, bool writeCsv, string csvName)
		{
			return indicator.TrendBalanceTag(Input, scoreTime, iBEndTime, holdMinutes, rVolThresh, uDThresh, iBBreakATR, aDXLen, lookbackDays, writeCsv, csvName);
		}

		public Indicators.TrendBalanceTag TrendBalanceTag(ISeries<double> input , DateTime scoreTime, DateTime iBEndTime, int holdMinutes, double rVolThresh, double uDThresh, double iBBreakATR, int aDXLen, int lookbackDays, bool writeCsv, string csvName)
		{
			return indicator.TrendBalanceTag(input, scoreTime, iBEndTime, holdMinutes, rVolThresh, uDThresh, iBBreakATR, aDXLen, lookbackDays, writeCsv, csvName);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TrendBalanceTag TrendBalanceTag(DateTime scoreTime, DateTime iBEndTime, int holdMinutes, double rVolThresh, double uDThresh, double iBBreakATR, int aDXLen, int lookbackDays, bool writeCsv, string csvName)
		{
			return indicator.TrendBalanceTag(Input, scoreTime, iBEndTime, holdMinutes, rVolThresh, uDThresh, iBBreakATR, aDXLen, lookbackDays, writeCsv, csvName);
		}

		public Indicators.TrendBalanceTag TrendBalanceTag(ISeries<double> input , DateTime scoreTime, DateTime iBEndTime, int holdMinutes, double rVolThresh, double uDThresh, double iBBreakATR, int aDXLen, int lookbackDays, bool writeCsv, string csvName)
		{
			return indicator.TrendBalanceTag(input, scoreTime, iBEndTime, holdMinutes, rVolThresh, uDThresh, iBBreakATR, aDXLen, lookbackDays, writeCsv, csvName);
		}
	}
}

#endregion
