#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows.Media; // Brushes

using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools; // SimpleFont
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// HUD_ADX_CI — Minimal state HUD that watches a 1m Heikin-Ashi Choppiness Index (CI)
    /// and an ADX. 
    ///
    /// States:
    ///   YELLOW  => CI > ChopThresh AND ADX < AdxThresh ("setup in play")
    ///   GREEN   => ADX > AdxThresh AND CI < ChopThresh ("go")
    ///              persists while ADX slope up AND CI slope down
    ///   RED     => triggered if either slope goes opposite for OppSlopeBars; 
    ///              remains RED until YELLOW condition returns.
    ///
    /// Notes:
    /// - CI is computed from a secondary 1-minute series using Heikin-Ashi OHLC.
    /// - ADX can be taken from the primary chart (default) or from the 1m series.
    /// - Draws one large box at the bottom of the chart whose color is the current state.
    /// - Fires a single alert when entering YELLOW (setup) with cooldown.
    /// </summary>
    public class HUD_ADX_CI : Indicator
    {
        // =============== Parameters ===============
        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "ADX Period", GroupName = "Parameters", Order = 0)]
        public int AdxPeriod { get; set; }

        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "Chop Period", GroupName = "Parameters", Order = 1)]
        public int ChopPeriod { get; set; }

        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "ADX Threshold", GroupName = "Thresholds", Order = 0)]
        public int AdxThresh { get; set; }

        [NinjaScriptProperty, Range(10, 100)]
        [Display(Name = "Chop Threshold", GroupName = "Thresholds", Order = 1)]
        public int ChopThresh { get; set; }

        [NinjaScriptProperty, Range(1, 10)]
        [Display(Name = "Opp Slope Bars", GroupName = "Behavior", Order = 0, Description = "Bars of opposite slope needed to flip GREEN -> RED")] 
        public int OppSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ADX From Primary Chart", GroupName = "Behavior", Order = 1, Description = "If false, ADX is sourced from the 1m series")] 
        public bool AdxFromPrimary { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Alerts", GroupName = "Alerts", Order = 0)]
        public bool AlertsEnabled { get; set; }

        [NinjaScriptProperty, Range(0, 600)]
        [Display(Name = "Alert Cooldown (sec)", GroupName = "Alerts", Order = 1)]
        public int AlertCooldownSec { get; set; }

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name = "Bars Per Box (width)", GroupName = "HUD", Order = 0)]
        public int BarsPerBox { get; set; }

        [NinjaScriptProperty, Range(5, 400)]
        [Display(Name = "Box Height (ticks)", GroupName = "HUD", Order = 1)]
        public int BoxHeightTicks { get; set; }

        [NinjaScriptProperty, Range(0, 400)]
        [Display(Name = "Box Vertical Pad (ticks)", GroupName = "HUD", Order = 2)]
        public int BoxPadTicks { get; set; }

        // =============== Internals ===============
        private enum HudState { None, Yellow, Green, Red }
        private HudState state = HudState.None;
        private int stateStartBar = -1;
        private int oppSlopeCount = 0;

        private ADX adxPrimary;       // primary chart
        private ADX adxOn1m;          // optional

        // 1m HA buffers
        private Series<double> haOpen1, haClose1, haHigh1, haLow1, tr1, ci1; 
        private double ciLatest = double.NaN, ciPrevAtPrimary = double.NaN;
        private double adxPrevAtPrimary = double.NaN;

        private DateTime lastYellowAlert = Core.Globals.MinDate;
        private const int FillOpacity = 85; // 0-100

        private SimpleFont small = new SimpleFont("Segoe UI", 14);
        private SimpleFont big   = new SimpleFont("Segoe UI Semibold", 18) { Bold = true };

        // =============== Lifecycle ===============
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "HUD_ADX_CI";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = true;
                DisplayInDataBox         = false;
                IsSuspendedWhileInactive = true;

                AdxPeriod       = 14;
                ChopPeriod      = 14;
                AdxThresh       = 18;
                ChopThresh      = 60;
                OppSlopeBars    = 1;
                AdxFromPrimary  = true;

                AlertsEnabled   = true;
                AlertCooldownSec= 60;

                BarsPerBox      = 20;
                BoxHeightTicks  = 50;
                BoxPadTicks     = 20;

                AddPlot(Brushes.Transparent, "CI (1m HA)"); // hidden helper
                AddPlot(Brushes.Transparent, "ADX Used");   // hidden helper
            }
            else if (State == State.Configure)
            {
                // Secondary 1-minute data series for CI (Heikin-Ashi computed locally)
                AddDataSeries(BarsPeriodType.Minute, 1);
            }
            else if (State == State.DataLoaded)
            {
                adxPrimary = ADX(AdxPeriod);
                adxOn1m    = ADX(Inputs[1], AdxPeriod);

                haOpen1  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                haClose1 = new Series<double>(this, MaximumBarsLookBack.Infinite);
                haHigh1  = new Series<double>(this, MaximumBarsLookBack.Infinite);
                haLow1   = new Series<double>(this, MaximumBarsLookBack.Infinite);
                tr1      = new Series<double>(this, MaximumBarsLookBack.Infinite);
                ci1      = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
        }

        // =============== Core ===============
        protected override void OnBarUpdate()
        {
            // -------- Build 1m Heikin-Ashi + CI on BIP=1 --------
            if (BarsInProgress == 1)
            {
                BuildHeikinAshi1m();
                ComputeChop1m();
                // Latest CI value (1m) available to primary
                ciLatest = ci1[0];
                return;
            }

            // -------- Primary series logic --------
            if (CurrentBar < 10) return;

            double adxNow = (AdxFromPrimary ? adxPrimary[0] : adxOn1m[0]);
            Values[1][0]  = adxNow;               // expose for debugging
            Values[0][0]  = ciLatest;             // expose CI (may be NaN very early)

            // Guard if CI not ready
            if (double.IsNaN(ciLatest) || CurrentBars[1] < Math.Max(ChopPeriod, 2))
                return;

            bool yellowCond = ciLatest > ChopThresh && adxNow < AdxThresh;
            bool greenCond  = adxNow > AdxThresh && ciLatest < ChopThresh;

            // Slope checks (based on primary bar-to-bar deltas)
            bool adxSlopeUp = !double.IsNaN(adxPrevAtPrimary) && (adxNow - adxPrevAtPrimary) > 0;
            bool ciSlopeDn  = !double.IsNaN(ciPrevAtPrimary)  && (ciLatest - ciPrevAtPrimary) < 0;

            // State transitions
            switch (state)
            {
                case HudState.None:
                    if (yellowCond) EnterState(HudState.Yellow);
                    else if (greenCond) EnterState(HudState.Green);
                    break;

                case HudState.Yellow:
                    if (greenCond) EnterState(HudState.Green);
                    else if (!yellowCond) { /* remain Yellow until either Green or None? */ }
                    break;

                case HudState.Green:
                    if (adxSlopeUp && ciSlopeDn)
                    {
                        oppSlopeCount = 0; // all good
                    }
                    else
                    {
                        oppSlopeCount++;
                        if (oppSlopeCount >= OppSlopeBars)
                            EnterState(HudState.Red);
                    }
                    break;

                case HudState.Red:
                    if (yellowCond) EnterState(HudState.Yellow);
                    break;
            }

            // Draw HUD box from stateStartBar → now
            DrawHudBox();

            // Update prev trackers at primary cadence
            adxPrevAtPrimary = adxNow;
            ciPrevAtPrimary  = ciLatest;
        }

        // =============== Helpers ===============
        private void EnterState(HudState newState)
        {
            state = newState;
            stateStartBar = CurrentBar;
            oppSlopeCount = 0;

            if (state == HudState.Yellow && AlertsEnabled)
            {
                var since = Time[0] - lastYellowAlert;
                if (since.TotalSeconds >= AlertCooldownSec)
                {
                    Alert("HUD_ADX_CI_YELLOW", Priority.Medium,
                          $"HUD setup in play: CI={ciLatest:F1} (> {ChopThresh}), ADX={(AdxFromPrimary ? adxPrimary[0] : adxOn1m[0]):F1} (< {AdxThresh})",
                          "Alert1.wav", 10, Brushes.Black, Brushes.Gold);
                    lastYellowAlert = Time[0];
                }
            }
        }

        private void DrawHudBox()
        {
            if (state == HudState.None || stateStartBar < 0) return;

            Brush fill = Brushes.DimGray;
            string label = "";
            switch (state)
            {
                case HudState.Yellow: fill = Brushes.Gold;       label = "YELLOW"; break;
                case HudState.Green:  fill = Brushes.LimeGreen;  label = "GREEN";  break;
                case HudState.Red:    fill = Brushes.IndianRed;  label = "RED";    break;
            }

            double baseY = MIN(Low, 50)[0] - BoxPadTicks * TickSize;
            double h     = Math.Max(5 * TickSize, BoxHeightTicks * TickSize);
            int span     = Math.Max(1, BarsPerBox);

            // Draw wide box from stateStartBar to now
            int startAgo = Math.Max(0, CurrentBar - stateStartBar);
            Draw.Rectangle(this, "HUD_ADX_CI_BOX", true,
                startAgo, baseY, 0, baseY + h,
                Brushes.Transparent, fill, FillOpacity);

            // Label
            var t = Draw.Text(this, "HUD_ADX_CI_TEXT",
                $"{label}  |  CI {ciLatest:F1}  |  ADX {(AdxFromPrimary ? adxPrimary[0] : adxOn1m[0]):F1}",
                0, baseY - 0.60 * h, Brushes.White);
            t.Font = big;

            // Slope readout (small)
            string slopeTxt = $"ADX {(double.IsNaN(adxPrevAtPrimary) ? '–' : ((Values[1][0] - adxPrevAtPrimary) > 0 ? '↑' : '↓'))}   CI {(double.IsNaN(ciPrevAtPrimary) ? '–' : ((Values[0][0] - ciPrevAtPrimary) < 0 ? '↓' : '↑'))}";
            var ts = Draw.Text(this, "HUD_ADX_CI_SLOPE", slopeTxt, 0, baseY - 0.95 * h, Brushes.White);
            ts.Font = small;
        }

        private void BuildHeikinAshi1m()
        {
            // Work inside the 1m series context (BarsInProgress == 1)
            double o = Opens[1][0];
            double h = Highs[1][0];
            double l = Lows[1][0];
            double c = Closes[1][0];

            double haC = (o + h + l + c) / 4.0;
            double haO = (CurrentBars[1] > 0) ? (haOpen1[1] + haClose1[1]) / 2.0 : (o + c) / 2.0;
            double haH = Math.Max(h, Math.Max(haO, haC));
            double haL = Math.Min(l, Math.Min(haO, haC));

            haOpen1[0]  = haO;
            haClose1[0] = haC;
            haHigh1[0]  = haH;
            haLow1[0]   = haL;

            // True Range using HA values
            double prevC = (CurrentBars[1] > 0) ? haClose1[1] : haC;
            double tr = Math.Max(haH - haL, Math.Max(Math.Abs(haH - prevC), Math.Abs(haL - prevC)));
            tr1[0] = tr;
        }

        private void ComputeChop1m()
        {
            int n = ChopPeriod;
            if (CurrentBars[1] < n)
            {
                ci1[0] = double.NaN;
                return;
            }

            // Sum TR over n
            double sumTr = 0.0;
            double hh = double.MinValue, ll = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                sumTr += tr1[i];
                hh = Math.Max(hh, haHigh1[i]);
                ll = Math.Min(ll, haLow1[i]);
            }
            double range = Math.Max(TickSize, hh - ll);
            double chop = 100.0 * Math.Log10(sumTr / range) / Math.Log10(n);
            chop = Math.Max(0, Math.Min(100, chop));
            ci1[0] = chop;
        }

        // Expose CI/ADX as plots for optional use
        [Browsable(false), XmlIgnore] public Series<double> CI => Values[0];
        [Browsable(false), XmlIgnore] public Series<double> ADXUsed => Values[1];
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private HUD_ADX_CI[] cacheHUD_ADX_CI;
		public HUD_ADX_CI HUD_ADX_CI(int adxPeriod, int chopPeriod, int adxThresh, int chopThresh, int oppSlopeBars, bool adxFromPrimary, bool alertsEnabled, int alertCooldownSec, int barsPerBox, int boxHeightTicks, int boxPadTicks)
		{
			return HUD_ADX_CI(Input, adxPeriod, chopPeriod, adxThresh, chopThresh, oppSlopeBars, adxFromPrimary, alertsEnabled, alertCooldownSec, barsPerBox, boxHeightTicks, boxPadTicks);
		}

		public HUD_ADX_CI HUD_ADX_CI(ISeries<double> input, int adxPeriod, int chopPeriod, int adxThresh, int chopThresh, int oppSlopeBars, bool adxFromPrimary, bool alertsEnabled, int alertCooldownSec, int barsPerBox, int boxHeightTicks, int boxPadTicks)
		{
			if (cacheHUD_ADX_CI != null)
				for (int idx = 0; idx < cacheHUD_ADX_CI.Length; idx++)
					if (cacheHUD_ADX_CI[idx] != null && cacheHUD_ADX_CI[idx].AdxPeriod == adxPeriod && cacheHUD_ADX_CI[idx].ChopPeriod == chopPeriod && cacheHUD_ADX_CI[idx].AdxThresh == adxThresh && cacheHUD_ADX_CI[idx].ChopThresh == chopThresh && cacheHUD_ADX_CI[idx].OppSlopeBars == oppSlopeBars && cacheHUD_ADX_CI[idx].AdxFromPrimary == adxFromPrimary && cacheHUD_ADX_CI[idx].AlertsEnabled == alertsEnabled && cacheHUD_ADX_CI[idx].AlertCooldownSec == alertCooldownSec && cacheHUD_ADX_CI[idx].BarsPerBox == barsPerBox && cacheHUD_ADX_CI[idx].BoxHeightTicks == boxHeightTicks && cacheHUD_ADX_CI[idx].BoxPadTicks == boxPadTicks && cacheHUD_ADX_CI[idx].EqualsInput(input))
						return cacheHUD_ADX_CI[idx];
			return CacheIndicator<HUD_ADX_CI>(new HUD_ADX_CI(){ AdxPeriod = adxPeriod, ChopPeriod = chopPeriod, AdxThresh = adxThresh, ChopThresh = chopThresh, OppSlopeBars = oppSlopeBars, AdxFromPrimary = adxFromPrimary, AlertsEnabled = alertsEnabled, AlertCooldownSec = alertCooldownSec, BarsPerBox = barsPerBox, BoxHeightTicks = boxHeightTicks, BoxPadTicks = boxPadTicks }, input, ref cacheHUD_ADX_CI);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.HUD_ADX_CI HUD_ADX_CI(int adxPeriod, int chopPeriod, int adxThresh, int chopThresh, int oppSlopeBars, bool adxFromPrimary, bool alertsEnabled, int alertCooldownSec, int barsPerBox, int boxHeightTicks, int boxPadTicks)
		{
			return indicator.HUD_ADX_CI(Input, adxPeriod, chopPeriod, adxThresh, chopThresh, oppSlopeBars, adxFromPrimary, alertsEnabled, alertCooldownSec, barsPerBox, boxHeightTicks, boxPadTicks);
		}

		public Indicators.HUD_ADX_CI HUD_ADX_CI(ISeries<double> input , int adxPeriod, int chopPeriod, int adxThresh, int chopThresh, int oppSlopeBars, bool adxFromPrimary, bool alertsEnabled, int alertCooldownSec, int barsPerBox, int boxHeightTicks, int boxPadTicks)
		{
			return indicator.HUD_ADX_CI(input, adxPeriod, chopPeriod, adxThresh, chopThresh, oppSlopeBars, adxFromPrimary, alertsEnabled, alertCooldownSec, barsPerBox, boxHeightTicks, boxPadTicks);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.HUD_ADX_CI HUD_ADX_CI(int adxPeriod, int chopPeriod, int adxThresh, int chopThresh, int oppSlopeBars, bool adxFromPrimary, bool alertsEnabled, int alertCooldownSec, int barsPerBox, int boxHeightTicks, int boxPadTicks)
		{
			return indicator.HUD_ADX_CI(Input, adxPeriod, chopPeriod, adxThresh, chopThresh, oppSlopeBars, adxFromPrimary, alertsEnabled, alertCooldownSec, barsPerBox, boxHeightTicks, boxPadTicks);
		}

		public Indicators.HUD_ADX_CI HUD_ADX_CI(ISeries<double> input , int adxPeriod, int chopPeriod, int adxThresh, int chopThresh, int oppSlopeBars, bool adxFromPrimary, bool alertsEnabled, int alertCooldownSec, int barsPerBox, int boxHeightTicks, int boxPadTicks)
		{
			return indicator.HUD_ADX_CI(input, adxPeriod, chopPeriod, adxThresh, chopThresh, oppSlopeBars, adxFromPrimary, alertsEnabled, alertCooldownSec, barsPerBox, boxHeightTicks, boxPadTicks);
		}
	}
}

#endregion
