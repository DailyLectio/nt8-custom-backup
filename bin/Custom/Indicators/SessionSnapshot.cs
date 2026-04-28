#region Using
using System;
using System.Collections.Generic;
using System.IO;
// using System.Windows.Media;            // not needed for 4-arg TextFixed
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class SessionSnapshot : Indicator
    {
        // --- Inputs ---
        [NinjaScriptProperty] public DateTime ScoreTime    { get; set; } = DateTime.Today.AddHours(10).AddMinutes(16);
        [NinjaScriptProperty] public DateTime IBEndTime    { get; set; } = DateTime.Today.AddHours(10);
        [NinjaScriptProperty] public int      UDMinutes    { get; set; } = 45;
        [NinjaScriptProperty] public int      ADXLen       { get; set; } = 14;
        [NinjaScriptProperty] public int      EMAFast      { get; set; } = 8;
        [NinjaScriptProperty] public int      EMASlow      { get; set; } = 24;
        [NinjaScriptProperty] public int      RVolLookback { get; set; } = 20;
        [NinjaScriptProperty] public bool     WriteCsv     { get; set; } = true;
        [NinjaScriptProperty] public string   CsvName      { get; set; } = "Session_Snapshot.csv";
        [NinjaScriptProperty] public TextPosition LabelCorner { get; set; } = TextPosition.TopRight;

        // --- Internals ---
        private SessionIterator sess;
        private ADX adx;
        private EMA emaF, emaS;

        private DateTime day, ibStart, ibEnd, scoreAt;
        private double ibHigh, ibLow, firstHourVol, prevClose, openPrice;

        // Manual session VWAP accumulator
        private double cumPV, cumVol, vwapAtOpen;
        private bool   vwapCapturedAtOpen;

        private bool ibComputed, scoredToday, capturedOpen;

        private readonly Queue<double> firstHourVolHist = new Queue<double>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name      = "SessionSnapshot";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
            }
            else if (State == State.DataLoaded)
            {
                sess  = new SessionIterator(Bars);
                adx   = ADX(ADXLen);
                emaF  = EMA(EMAFast);
                emaS  = EMA(EMASlow);
                // AddChartIndicator(adx);
                // AddChartIndicator(emaF);
                // AddChartIndicator(emaS);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 10) return;

            // Detect new trading day
            DateTime currDay = sess.GetTradingDay(Times[0][0]);
            if (currDay != day)
            {
                prevClose          = (CurrentBar > 0) ? Closes[0][1] : double.NaN;

                day                = currDay;
                ibComputed         = false;
                scoredToday        = false;
                capturedOpen       = false;

                ibHigh             = double.MinValue;
                ibLow              = double.MaxValue;
                firstHourVol       = 0;
                openPrice          = double.NaN;

                // reset manual VWAP accumulators
                cumPV              = 0;
                cumVol             = 0;
                vwapAtOpen         = double.NaN;
                vwapCapturedAtOpen = false;

                ibStart            = new DateTime(day.Year, day.Month, day.Day, 9, 30, 0);
                ibEnd              = new DateTime(day.Year, day.Month, day.Day, IBEndTime.Hour, IBEndTime.Minute, 0);
                scoreAt            = new DateTime(day.Year, day.Month, day.Day, ScoreTime.Hour, ScoreTime.Minute, 0);
            }

            DateTime t = Times[0][0];

            // Capture RTH open price (first 9:30 bar)
            if (!capturedOpen && t >= ibStart && t < ibStart.AddMinutes(1))
            {
                openPrice    = Opens[0][0];
                capturedOpen = true;
            }

            // Manual session VWAP accumulation (typical price * volume) during RTH
            if (t >= ibStart)
            {
                double typical = (Highs[0][0] + Lows[0][0] + Closes[0][0]) / 3.0;
                double vol     = Volumes[0][0];
                cumPV  += typical * vol;
                cumVol += vol;

                if (capturedOpen && !vwapCapturedAtOpen)
                {
                    vwapAtOpen = (cumVol > 0 ? cumPV / cumVol : double.NaN);
                    vwapCapturedAtOpen = true;
                }
            }

            // Build IB (9:30-10:00)
            if (t >= ibStart && t < ibEnd)
            {
                ibHigh     = Math.Max(ibHigh, Highs[0][0]);
                ibLow      = Math.Min(ibLow,  Lows[0][0]);
                ibComputed = true;
            }

            // First-hour volume up to ScoreTime
            if (t >= ibStart && t <= scoreAt)
                firstHourVol += Volumes[0][0];

            // Score & log after ScoreTime
            if (!scoredToday && t >= scoreAt)
            {
                // rVol vs history
                double rvol = 1.0;
                if (firstHourVolHist.Count >= Math.Max(5, RVolLookback / 2))
                {
                    double avg = 0;
                    foreach (var v in firstHourVolHist) avg += v;
                    avg = avg / firstHourVolHist.Count;
                    rvol = avg > 0 ? firstHourVol / avg : 1.0;
                }
                firstHourVolHist.Enqueue(firstHourVol);
                while (firstHourVolHist.Count > RVolLookback)
                    firstHourVolHist.Dequeue();

                // UD ratio over UDMinutes
                int lookBars = Math.Min(UDMinutes, CurrentBar - 1);
                int upBars = 0, dnBars = 0;
                for (int i = 0; i < lookBars; i++)
                {
                    if (Closes[0][i] > Closes[0][i + 1]) upBars++;
                    else if (Closes[0][i] < Closes[0][i + 1]) dnBars++;
                }
                double ud = (dnBars == 0) ? upBars : (double)upBars / dnBars;

                // EMA & ADX context
                double emaFv     = emaF[0];
                double emaSv     = emaS[0];
                bool   emaAbove  = emaFv > emaSv;
                double emaFSlope = emaF[0] - emaF[1];
                double emaSSlope = emaS[0] - emaS[1];

                double adxVal    = adx[0];
                bool   adxUp     = adx[0] > adx[1] && adx[1] > adx[2];

                // Snapshot fields
                double ibRangeTicks   = (ibComputed ? (ibHigh - ibLow) / TickSize : double.NaN);
                double rangeTo10Ticks = ibRangeTicks; // same window
                double gapTicks       = (!double.IsNaN(prevClose) && !double.IsNaN(openPrice)) ? (openPrice - prevClose) / TickSize : double.NaN;

                string openVsVWAP     = (!double.IsNaN(openPrice) && !double.IsNaN(vwapAtOpen))
                    ? (openPrice > vwapAtOpen ? "above" : "below")
                    : "";

                // HUD (4-arg TextFixed to match your NT8)
                string hud = $"SNAP {day:MM-dd}  IB:{ibRangeTicks:F0}t  rVol:{rvol:F2}  UD:{ud:F2}  EMA{EMAFast}>{EMASlow}:{(emaAbove?1:0)}  ADX:{adxVal:F1}";
                Draw.TextFixed(this, "snap_hud", hud, LabelCorner);

                // CSV
                if (WriteCsv)
                {
                    try
                    {
                        string path  = Path.Combine(Core.Globals.UserDataDir, CsvName);
                        bool   isNew = !File.Exists(path);
                        using (var sw = new StreamWriter(path, true))
                        {
                            if (isNew)
                                sw.WriteLine("date,openPrice,gapTicks,sessionVWAPatOpen,openVsVWAP,ibHigh,ibLow,ibRangeTicks,rangeTo10amTicks,firstHourVol,rVol_60m,ud_lookback,adx14,adxSlopeUp,emaFast,emaSlow,emaFast_gt_emaSlow,emaFastSlope,emaSlowSlope");

                            sw.WriteLine($"{day:yyyy-MM-dd},{Fmt(openPrice)},{Fmt(gapTicks)},{Fmt(vwapAtOpen)},{openVsVWAP},{Fmt(ibHigh)},{Fmt(ibLow)},{Fmt(ibRangeTicks)},{Fmt(rangeTo10Ticks)},{firstHourVol:F0},{rvol:F2},{ud:F2},{adxVal:F2},{(adxUp?1:0)},{Fmt(emaFv)},{Fmt(emaSv)},{(emaAbove?1:0)},{emaFSlope:F4},{emaSSlope:F4}");
                        }
                    }
                    catch { /* ignore IO errors */ }
                }

                scoredToday = true;
            }
        }

        private string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("0.#####");
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private SessionSnapshot[] cacheSessionSnapshot;
		public SessionSnapshot SessionSnapshot(DateTime scoreTime, DateTime iBEndTime, int uDMinutes, int aDXLen, int eMAFast, int eMASlow, int rVolLookback, bool writeCsv, string csvName, TextPosition labelCorner)
		{
			return SessionSnapshot(Input, scoreTime, iBEndTime, uDMinutes, aDXLen, eMAFast, eMASlow, rVolLookback, writeCsv, csvName, labelCorner);
		}

		public SessionSnapshot SessionSnapshot(ISeries<double> input, DateTime scoreTime, DateTime iBEndTime, int uDMinutes, int aDXLen, int eMAFast, int eMASlow, int rVolLookback, bool writeCsv, string csvName, TextPosition labelCorner)
		{
			if (cacheSessionSnapshot != null)
				for (int idx = 0; idx < cacheSessionSnapshot.Length; idx++)
					if (cacheSessionSnapshot[idx] != null && cacheSessionSnapshot[idx].ScoreTime == scoreTime && cacheSessionSnapshot[idx].IBEndTime == iBEndTime && cacheSessionSnapshot[idx].UDMinutes == uDMinutes && cacheSessionSnapshot[idx].ADXLen == aDXLen && cacheSessionSnapshot[idx].EMAFast == eMAFast && cacheSessionSnapshot[idx].EMASlow == eMASlow && cacheSessionSnapshot[idx].RVolLookback == rVolLookback && cacheSessionSnapshot[idx].WriteCsv == writeCsv && cacheSessionSnapshot[idx].CsvName == csvName && cacheSessionSnapshot[idx].LabelCorner == labelCorner && cacheSessionSnapshot[idx].EqualsInput(input))
						return cacheSessionSnapshot[idx];
			return CacheIndicator<SessionSnapshot>(new SessionSnapshot(){ ScoreTime = scoreTime, IBEndTime = iBEndTime, UDMinutes = uDMinutes, ADXLen = aDXLen, EMAFast = eMAFast, EMASlow = eMASlow, RVolLookback = rVolLookback, WriteCsv = writeCsv, CsvName = csvName, LabelCorner = labelCorner }, input, ref cacheSessionSnapshot);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.SessionSnapshot SessionSnapshot(DateTime scoreTime, DateTime iBEndTime, int uDMinutes, int aDXLen, int eMAFast, int eMASlow, int rVolLookback, bool writeCsv, string csvName, TextPosition labelCorner)
		{
			return indicator.SessionSnapshot(Input, scoreTime, iBEndTime, uDMinutes, aDXLen, eMAFast, eMASlow, rVolLookback, writeCsv, csvName, labelCorner);
		}

		public Indicators.SessionSnapshot SessionSnapshot(ISeries<double> input , DateTime scoreTime, DateTime iBEndTime, int uDMinutes, int aDXLen, int eMAFast, int eMASlow, int rVolLookback, bool writeCsv, string csvName, TextPosition labelCorner)
		{
			return indicator.SessionSnapshot(input, scoreTime, iBEndTime, uDMinutes, aDXLen, eMAFast, eMASlow, rVolLookback, writeCsv, csvName, labelCorner);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.SessionSnapshot SessionSnapshot(DateTime scoreTime, DateTime iBEndTime, int uDMinutes, int aDXLen, int eMAFast, int eMASlow, int rVolLookback, bool writeCsv, string csvName, TextPosition labelCorner)
		{
			return indicator.SessionSnapshot(Input, scoreTime, iBEndTime, uDMinutes, aDXLen, eMAFast, eMASlow, rVolLookback, writeCsv, csvName, labelCorner);
		}

		public Indicators.SessionSnapshot SessionSnapshot(ISeries<double> input , DateTime scoreTime, DateTime iBEndTime, int uDMinutes, int aDXLen, int eMAFast, int eMASlow, int rVolLookback, bool writeCsv, string csvName, TextPosition labelCorner)
		{
			return indicator.SessionSnapshot(input, scoreTime, iBEndTime, uDMinutes, aDXLen, eMAFast, eMASlow, rVolLookback, writeCsv, csvName, labelCorner);
		}
	}
}

#endregion
