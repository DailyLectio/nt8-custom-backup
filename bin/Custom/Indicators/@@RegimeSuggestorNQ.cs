// CC BY-NC 4.0 — RegimeSuggestor_NQ
#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;      // DisplayAttribute
using System.Windows.Media;                      // Brush, Brushes
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;                      // TextPosition, SimpleFont
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.DrawingTools;       // Draw.TextFixed
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class RegimeSuggestor_NQ : Indicator
    {
        // ---------------- Inputs
        [NinjaScriptProperty, Display(Name="RVOL threshold (slow if <)", Order=0, GroupName="Thresholds")]
        public double RvolThresh { get; set; } = 0.65;

        [NinjaScriptProperty, Display(Name="ATR% threshold (slow if <)", Order=1, GroupName="Thresholds")]
        public double AtrPctThresh { get; set; } = 0.70;

        [NinjaScriptProperty, Display(Name="ADX min for trend", Order=2, GroupName="Thresholds")]
        public double AdxTrendMin { get; set; } = 18;

        [NinjaScriptProperty, Display(Name="ORB30% threshold (slow if <)", Order=3, GroupName="Thresholds")]
        public double OrbPctThresh { get; set; } = 0.35;

        [NinjaScriptProperty, Display(Name="Lookback days for ORB/RVOL avg", Order=4, GroupName="Averages")]
        public int LookbackDays { get; set; } = 20;

        [NinjaScriptProperty, Display(Name="Show debug status box", Order=5, GroupName="Visual")]
        public bool ShowDebug { get; set; } = true;

        // ---------------- Internals
        private ADX adx10;                        // 10-minute ADX(14)
        private Series<double> atr5;              // 5-minute ATR(14)
        private Series<double> atr5Avg;           // rolling avg/proxy

        // First-hour RVOL tracking (09:30–10:30)
        private double firstHourVol, firstHourAvgVol;

        // ORB30 tracking (09:30–10:00)
        private double orbHi, orbLo, orb30Avg;

        private DateTime lastSessionDate = Core.Globals.MinDate;
        private bool snapshot1010Done, snapshot1020Done;
        private string regime = "Unknown";
        private string brickSuggestion = "";
        private string templateSuggestion = "";

        private SimpleFont font;
        private const string tagStatus = "RS_Status";

        // ---------- Helpers
        private static bool IsZero(double v) => Math.Abs(v) < 1e-9;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "RegimeSuggestor_NQ";
                Description = "Morning regime read (RVOL/ATR/ADX/ORB30) + UniRenko brick suggestion.";
                IsOverlay = true;
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.Configure)
            {
                // Add secondary series: 5m (idx=1), 10m (idx=2)
                AddDataSeries(BarsPeriodType.Minute, 5);
                AddDataSeries(BarsPeriodType.Minute, 10);
            }
            else if (State == State.DataLoaded)
            {
                adx10  = ADX(BarsArray[2], 14);
                atr5   = new Series<double>(this, MaximumBarsLookBack.Infinite);
                atr5Avg= new Series<double>(this, MaximumBarsLookBack.Infinite);
                font   = new SimpleFont("Segoe UI", 12) { Size = 12, Bold = false };
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 1 || CurrentBars[1] < 1 || CurrentBars[2] < 1)
                return;

            // -------- Primary series (any) drives session logic & snapshots
            if (BarsInProgress == 0)
            {
                DateTime et = Times[0][0]; // assumes PC on ET; adjust if needed

                // Reset at new RTH session (when we first reach/after 09:30)
                if (et.Date != lastSessionDate && et.TimeOfDay >= new TimeSpan(9, 30, 0))
                {
                    lastSessionDate   = et.Date;
                    snapshot1010Done  = snapshot1020Done = false;

                    firstHourVol = 0;
                    orbHi = double.MinValue;
                    orbLo = double.MaxValue;
                }

                // Take snapshots at 10:10 and 10:20 (decision points)
                if (et.TimeOfDay.Hours == 10)
                {
                    if (et.TimeOfDay.Minutes == 10 && !snapshot1010Done)
                    {
                        EvaluateRegime("10:10");
                        snapshot1010Done = true;
                    }
                    if (et.TimeOfDay.Minutes == 20 && !snapshot1020Done)
                    {
                        EvaluateRegime("10:20");
                        snapshot1020Done = true;
                    }
                }

                // Status panel
                if (ShowDebug)
                {
                    Draw.TextFixed(this, tagStatus, BuildStatusText(), TextPosition.TopLeft,
                        Brushes.White, font, Brushes.Transparent, Brushes.Transparent, 0);
                }
            }

            // -------- 5-minute series (idx=1) handles ATR/RVOL/ORB math
            if (BarsInProgress == 1)
            {
                DateTime et5 = Times[1][0];

                // 5m ATR(14) and a light rolling average proxy
                double a5 = ATR(BarsArray[1], 14)[0];
                atr5[0]   = a5;
                atr5Avg[0]= SMA(atr5, 56)[0]; // 14*4 ~ smooth enough

                // ORB30 high/low between 09:30 and 10:00
                if (et5.TimeOfDay >= new TimeSpan(9, 30, 0) && et5.TimeOfDay < new TimeSpan(10, 0, 0))
                {
                    orbHi = Math.Max(orbHi, Highs[1][0]);
                    orbLo = Math.Min(orbLo, Lows[1][0]);
                }

                // First-hour volume 09:30–10:30
                if (et5.TimeOfDay >= new TimeSpan(9, 30, 0) && et5.TimeOfDay < new TimeSpan(10, 30, 0))
                    firstHourVol += Volumes[1][0];

                // At exactly 10:30, update running averages (EMA-like)
                if (et5.TimeOfDay == new TimeSpan(10, 30, 0))
                {
                    if (IsZero(firstHourAvgVol))
                        firstHourAvgVol = firstHourVol;
                    else
                        firstHourAvgVol = firstHourAvgVol + (firstHourVol - firstHourAvgVol) / Math.Max(1, LookbackDays);

                    double orbToday = (orbHi > double.MinValue && orbLo < double.MaxValue) ? (orbHi - orbLo) : 0.0;
                    if (IsZero(orb30Avg))
                        orb30Avg = orbToday;
                    else
                        orb30Avg = orb30Avg + (orbToday - orb30Avg) / Math.Max(1, LookbackDays);
                }
            }
        }

        private void EvaluateRegime(string label)
        {
            // Pull latest measures
            double a5     = atr5[0];
            double a5Avg  = Math.Max(1e-9, atr5Avg[0]);
            double atrPct = a5 / a5Avg;

            double rv     = (IsZero(firstHourAvgVol)) ? 1.0 : firstHourVol / firstHourAvgVol;

            double a10    = adx10[0];
            bool   a10Up  = adx10[0] > adx10[1];

            double orbToday = (orbHi > double.MinValue && orbLo < double.MaxValue) ? (orbHi - orbLo) : 0.0;
            double orbPct   = (IsZero(orb30Avg)) ? 1.0 : (orbToday / orb30Avg);

            int slowFlags = 0;
            if (rv < RvolThresh)                         slowFlags++;
            if (atrPct < AtrPctThresh)                   slowFlags++;
            if (!(a10 >= AdxTrendMin && a10Up))          slowFlags++;
            if (orbPct < OrbPctThresh)                   slowFlags++;

            regime = (slowFlags >= 3) ? $"Rotational ({label})" : $"Trending ({label})";

            // Brick size calculator (base ≈ 0.12 × ATR5 in ticks; clamp [6..20])
            double tickSize = Instrument?.MasterInstrument?.TickSize ?? 0.25;
            double ticksFromAtr = (a5 > 0 ? (0.12 * a5 / tickSize) : 10.0);
            int baseTicks = Math.Max(6, Math.Min(20, (int)Math.Round(ticksFromAtr, MidpointRounding.AwayFromZero)));
            int b1 = baseTicks, b2 = baseTicks * 2, b4 = baseTicks * 4;

            brickSuggestion = $"{b1}-{b2}-{b4}";

            // Friendly template suggestion
            if (regime.StartsWith("Trending"))
            {
                templateSuggestion = (b1 <= 10) ? "Use Uni 8-16-32 or 10-20-40 • 5m HUD OK"
                                                : "Use Uni 10-20-40 • Add if ADX rises";
            }
            else
            {
                templateSuggestion = (b1 >= 12) ? "Use Uni 12-24-48 or 15-30-60 • Cut size"
                                                : "Consider sitting out until ADX/RVOL improve";
            }
        }

        private string BuildStatusText()
        {
            double a10 = (CurrentBars[2] > 20) ? adx10[0] : double.NaN;
            double atrRatio = (IsZero(atr5Avg[0])) ? double.NaN : atr5[0] / atr5Avg[0];
            double rvolNow  = (IsZero(firstHourAvgVol)) ? double.NaN : firstHourVol / firstHourAvgVol;
            double orbToday = (orbHi > double.MinValue && orbLo < double.MaxValue) ? (orbHi - orbLo) : 0.0;
            double orbRatio = (IsZero(orb30Avg)) ? double.NaN : (orbToday / orb30Avg);

            string line1 = $"Regime: {regime}";
            string line2 = $"Suggest: {templateSuggestion}  |  Brick≈ {brickSuggestion}";
            string line3 = $"RVOL(1h): {(double.IsNaN(rvolNow) ? "n/a" : rvolNow.ToString("0.00"))}  |  ATR5/Avg: {(double.IsNaN(atrRatio) ? "n/a" : atrRatio.ToString("0.00"))}";
            string line4 = $"ADX10: {(double.IsNaN(a10) ? "n/a" : a10.ToString("0.0"))}  |  ORB30/Avg: {(double.IsNaN(orbRatio) ? "n/a" : orbRatio.ToString("0.00"))}";

            return $"{line1}\n{line2}\n{line3}\n{line4}";
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RegimeSuggestor_NQ[] cacheRegimeSuggestor_NQ;
		public RegimeSuggestor_NQ RegimeSuggestor_NQ(double rvolThresh, double atrPctThresh, double adxTrendMin, double orbPctThresh, int lookbackDays, bool showDebug)
		{
			return RegimeSuggestor_NQ(Input, rvolThresh, atrPctThresh, adxTrendMin, orbPctThresh, lookbackDays, showDebug);
		}

		public RegimeSuggestor_NQ RegimeSuggestor_NQ(ISeries<double> input, double rvolThresh, double atrPctThresh, double adxTrendMin, double orbPctThresh, int lookbackDays, bool showDebug)
		{
			if (cacheRegimeSuggestor_NQ != null)
				for (int idx = 0; idx < cacheRegimeSuggestor_NQ.Length; idx++)
					if (cacheRegimeSuggestor_NQ[idx] != null && cacheRegimeSuggestor_NQ[idx].RvolThresh == rvolThresh && cacheRegimeSuggestor_NQ[idx].AtrPctThresh == atrPctThresh && cacheRegimeSuggestor_NQ[idx].AdxTrendMin == adxTrendMin && cacheRegimeSuggestor_NQ[idx].OrbPctThresh == orbPctThresh && cacheRegimeSuggestor_NQ[idx].LookbackDays == lookbackDays && cacheRegimeSuggestor_NQ[idx].ShowDebug == showDebug && cacheRegimeSuggestor_NQ[idx].EqualsInput(input))
						return cacheRegimeSuggestor_NQ[idx];
			return CacheIndicator<RegimeSuggestor_NQ>(new RegimeSuggestor_NQ(){ RvolThresh = rvolThresh, AtrPctThresh = atrPctThresh, AdxTrendMin = adxTrendMin, OrbPctThresh = orbPctThresh, LookbackDays = lookbackDays, ShowDebug = showDebug }, input, ref cacheRegimeSuggestor_NQ);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RegimeSuggestor_NQ RegimeSuggestor_NQ(double rvolThresh, double atrPctThresh, double adxTrendMin, double orbPctThresh, int lookbackDays, bool showDebug)
		{
			return indicator.RegimeSuggestor_NQ(Input, rvolThresh, atrPctThresh, adxTrendMin, orbPctThresh, lookbackDays, showDebug);
		}

		public Indicators.RegimeSuggestor_NQ RegimeSuggestor_NQ(ISeries<double> input , double rvolThresh, double atrPctThresh, double adxTrendMin, double orbPctThresh, int lookbackDays, bool showDebug)
		{
			return indicator.RegimeSuggestor_NQ(input, rvolThresh, atrPctThresh, adxTrendMin, orbPctThresh, lookbackDays, showDebug);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RegimeSuggestor_NQ RegimeSuggestor_NQ(double rvolThresh, double atrPctThresh, double adxTrendMin, double orbPctThresh, int lookbackDays, bool showDebug)
		{
			return indicator.RegimeSuggestor_NQ(Input, rvolThresh, atrPctThresh, adxTrendMin, orbPctThresh, lookbackDays, showDebug);
		}

		public Indicators.RegimeSuggestor_NQ RegimeSuggestor_NQ(ISeries<double> input , double rvolThresh, double atrPctThresh, double adxTrendMin, double orbPctThresh, int lookbackDays, bool showDebug)
		{
			return indicator.RegimeSuggestor_NQ(input, rvolThresh, atrPctThresh, adxTrendMin, orbPctThresh, lookbackDays, showDebug);
		}
	}
}

#endregion
