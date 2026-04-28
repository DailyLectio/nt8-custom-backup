#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// RotationAwareTwoSetupAlerts
// ORB‑P (rotation aware) + VWAP Rejection + CI Trend Monitor
// Per‑filter toggles, CI display string ("Panel","PriceTop","PriceBottom"), debug, and marker suppression.
// Exposes 4 public Series for strategy consumption: SigOrbLong, SigOrbShort, SigRejLong, SigRejShort.
// Apply to a 1‑minute ES/NQ RTH chart. Internally adds 3‑min (volume) and 5‑min (EMA bias) series.

namespace NinjaTrader.NinjaScript.Indicators
{
    public class RotationAwareTwoSetupAlerts : Indicator
    {
        // ---- internal helper enum (not exposed) ----
        private enum CIPos { Panel, PriceTop, PriceBottom }

        // ===== Inputs: Core params =====
        [NinjaScriptProperty, Range(2, 200)]
        [Display(Name = "Choppiness Length", Order = 1, GroupName = "Parameters")]
        public int ChopLength { get; set; } = 14;

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "EMA Fast (1m/5m)", Order = 2, GroupName = "Parameters")]
        public int EmaFast { get; set; } = 8;

        [NinjaScriptProperty, Range(2, 200)]
        [Display(Name = "EMA Slow (1m/5m)", Order = 3, GroupName = "Parameters")]
        public int EmaSlow { get; set; } = 21;

        [NinjaScriptProperty, Range(1.0, 10.0)]
        [Display(Name = "3m Volume Multiplier (x prior)", Order = 4, GroupName = "Parameters")]
        public double VolMult { get; set; } = 1.30;

        [NinjaScriptProperty]
        [Display(Name = "Aggressive Break (9:45–10:00 & no‑wait @10:00)", Order = 5, GroupName = "Parameters")]
        public bool AggressiveBreak { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Require Pullback Hold", Order = 6, GroupName = "Parameters")]
        public bool RequirePullback { get; set; } = true;

        [NinjaScriptProperty, Range(1,10)]
        [Display(Name = "CI Decline Streak (bars)", Order = 7, GroupName = "Parameters")]
        public int CiStreakMin { get; set; } = 2;

        // ===== Inputs: Filter toggles =====
        [NinjaScriptProperty]
        [Display(Name = "Use CI Filter (≤54.5)", Order = 10, GroupName = "Filter Toggles")]
        public bool UseCIFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Use Volume Filter (3m x prior)", Order = 11, GroupName = "Filter Toggles")]
        public bool UseVolumeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Bias (1m+5m)", Order = 12, GroupName = "Filter Toggles")]
        public bool UseEMABiasFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Use IB/Time Logic", Order = 13, GroupName = "Filter Toggles")]
        public bool UseIBLogic { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Use Pullback to IB Gate", Order = 14, GroupName = "Filter Toggles")]
        public bool UsePullbackFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Use Rotation Mode Windows", Order = 15, GroupName = "Filter Toggles")]
        public bool UseRotationMode { get; set; } = true;

        // ===== Inputs: Visual & debug =====
        [NinjaScriptProperty]
        [Display(Name = "Suppress Trade Markers", Order = 20, GroupName = "Visual")]
        public bool SuppressTradeMarkers { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Show IB Lines", Order = 21, GroupName = "Visual")]
        public bool ShowIBLines { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "CI Display (Panel | PriceTop | PriceBottom)", Order = 22, GroupName = "Visual")]
        public string CIPlotMode { get; set; } = "Panel";

        [NinjaScriptProperty]
        [Display(Name = "Show CI Labels (text)", Order = 23, GroupName = "Visual")]
        public bool ShowCILabels { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Play Sounds", Order = 24, GroupName = "Visual")]
        public bool PlaySounds { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Show Debug Reasons", Order = 30, GroupName = "Debug")]
        public bool ShowDebug { get; set; } = false;

        // ===== Working vars =====
        private double ibHigh = double.NaN, ibLow = double.NaN;
        private bool ibLocked, brokeIBUp, brokeIBDn;

        private bool   vol3OK;
        private double ciNow = double.NaN, ciPrev = double.NaN;
        private int    ciDownStreak = 0;

        private EMA ema8_1m, ema21_1m, ema8_5m, ema21_5m;

        // Session VWAP
        private double cumPV = 0.0, cumV = 0.0, vwapVal = double.NaN;

        // Panel plots for CI
        private Series<double> ciPlot, ci55Plot, ci45Plot;

        // ---- 4 public signal series for a strategy to read ----
        private Series<double> sigOrbLong, sigOrbShort, sigRejLong, sigRejShort;

        // helpers
        private CIPos CIPosition
        {
            get
            {
                string v = (CIPlotMode ?? "Panel").Trim().ToLowerInvariant();
                if (v == "pricetop") return CIPos.PriceTop;
                if (v == "pricebottom") return CIPos.PriceBottom;
                return CIPos.Panel;
            }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "RotationAwareTwoSetupAlerts (filters + CI display + signals)";
                Description              = "ORB‑P + VWAP Rejection with per‑filter toggles, CI display, and signal outputs.";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = false; // own panel for CI
                DrawOnPricePanel         = true;
                PaintPriceMarkers        = true;
                IsSuspendedWhileInactive = true;

                AddPlot(Brushes.DodgerBlue, "CI");
                AddPlot(Brushes.LimeGreen, "CI55");
                AddPlot(Brushes.Teal,       "CI45");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 3);
                AddDataSeries(BarsPeriodType.Minute, 5);
            }
            else if (State == State.DataLoaded)
            {
                ema8_1m  = EMA(Close, EmaFast);
                ema21_1m = EMA(Close, EmaSlow);
                ema8_5m  = EMA(Closes[2], EmaFast);
                ema21_5m = EMA(Closes[2], EmaSlow);

                ciPlot  = new Series<double>(this);
                ci55Plot= new Series<double>(this);
                ci45Plot= new Series<double>(this);

                sigOrbLong  = new Series<double>(this);
                sigOrbShort = new Series<double>(this);
                sigRejLong  = new Series<double>(this);
                sigRejShort = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < Math.Max(ChopLength + 5, 50)) return;
            if (BarsInProgress == 1 && CurrentBars[1] < 2) return;
            if (BarsInProgress == 2 && CurrentBars[2] < 2) return;

            // clear signal flags each primary bar
            if (BarsInProgress == 0)
            {
                sigOrbLong[0]  = 0;
                sigOrbShort[0] = 0;
                sigRejLong[0]  = 0;
                sigRejShort[0] = 0;
            }

            // 3m volume updates
            if (BarsInProgress == 1)
            {
                double v0 = Volumes[1][0], v1 = Volumes[1][1];
                vol3OK = v0 >= VolMult * Math.Max(1, v1);
                return;
            }
            if (BarsInProgress != 0) return;

            // Session VWAP
            if (Bars.IsFirstBarOfSession) { cumPV = 0.0; cumV = 0.0; ibHigh = double.NaN; ibLow = double.NaN; ibLocked = false; brokeIBUp=false; brokeIBDn=false; }
            double typ = (High[0] + Low[0] + Close[0]) / 3.0;
            double vol = Math.Max(Volume[0], 1.0);
            cumPV += typ * vol; cumV += vol;
            vwapVal = cumPV / Math.Max(1.0, cumV);

            int t = ToTime(Time[0]);
            bool inIBWindow = t >= 93000 && t < 100000;
            bool before0945 = t >= 93000 && t < 94500;
            bool after1000  = t >= 100000;

            // IB logic
            if (UseIBLogic)
            {
                if (inIBWindow)
                {
                    ibHigh = double.IsNaN(ibHigh) ? High[0] : Math.Max(ibHigh, High[0]);
                    ibLow  = double.IsNaN(ibLow)  ? Low[0]  : Math.Min(ibLow,  Low[0]);
                }
                else if (!inIBWindow && !ibLocked && t >= 100000 && !double.IsNaN(ibHigh) && !double.IsNaN(ibLow))
                {
                    ibLocked = true; brokeIBUp = false; brokeIBDn = false;
                }
                if (ibLocked)
                {
                    if (Close[0] > ibHigh) brokeIBUp = true;
                    if (Close[0] < ibLow)  brokeIBDn = true;
                }
            }
            else ibLocked = true;

            bool trendMode          = UseIBLogic && UseRotationMode && ibLocked && before0945 && (Close[0] > ibHigh || Close[0] < ibLow);
            bool rotationModeActive = UseIBLogic && UseRotationMode && ibLocked && after1000  && !brokeIBUp && !brokeIBDn;

            bool longBiasBase  = Close[0] > vwapVal && ema8_1m[0] > ema21_1m[0] && ema8_5m[0] > ema21_5m[0];
            bool shortBiasBase = Close[0] < vwapVal && ema8_1m[0] < ema21_1m[0] && ema8_5m[0] < ema21_5m[0];

            ciPrev = ciNow;
            ciNow  = Choppiness(ChopLength);

            // CI plot or markers
            if (CIPosition == CIPos.Panel) { Values[0][0]=ciNow; Values[1][0]=55; Values[2][0]=45; }
            else { Values[0][0]=double.NaN; Values[1][0]=double.NaN; Values[2][0]=double.NaN; }

            bool ciBelow60 = ciNow < 60.0, ciFalling = ciNow < ciPrev;
            if (ciBelow60 && ciFalling) ciDownStreak++; else ciDownStreak = 0;

            bool ci55 = ciNow <= 55.0 && ciDownStreak >= CiStreakMin;
            bool ci45 = ciNow <= 45.0 && ciDownStreak >= CiStreakMin;
            bool ciExitRisk = ciNow > ciPrev;

            if (CIPosition == CIPos.PriceBottom && ci55) Draw.Dot(this, "CI55"+CurrentBar, true, 0, Low[0]-3*TickSize, Brushes.LimeGreen);
            if (CIPosition == CIPos.PriceBottom && ci45) Draw.Dot(this, "CI45"+CurrentBar, true, 0, Low[0]-4*TickSize, Brushes.Teal);
            if (CIPosition == CIPos.PriceTop    && ciExitRisk) Draw.Diamond(this, "CIRise"+CurrentBar, false, 0, High[0]+3*TickSize, Brushes.OrangeRed);

            if (ShowCILabels && CIPosition != CIPos.Panel)
            {
                if (ci55) Draw.Text(this, "CI55L"+CurrentBar, "CI≤55", 0, Low[0]-5*TickSize, Brushes.LimeGreen);
                if (ci45) Draw.Text(this, "CI45L"+CurrentBar, "CI≤45", 0, Low[0]-6*TickSize, Brushes.Teal);
                if (ciExitRisk && CIPosition==CIPos.PriceTop) Draw.Text(this, "CIRiseL"+CurrentBar, "CI↑", 0, High[0]+5*TickSize, Brushes.OrangeRed);
            }

            if (ci55) Alert("CI55", Priority.Medium, "CI ≤55 decline — trend favored.", PlaySounds ? "Alert2.wav":null, 0, Brushes.Black, Brushes.LightGreen);
            if (ci45) Alert("CI45", Priority.Medium, "CI ≤45 decline — strong continuation.", PlaySounds ? "Alert2.wav":null, 0, Brushes.Black, Brushes.Teal);
            if (ciExitRisk) Alert("CIRise", Priority.Medium, "CI rising — exit risk.", PlaySounds ? "Alert1.wav":null, 0, Brushes.Black, Brushes.OrangeRed);

            // ORB pieces
            bool brokeUpNow    = UseIBLogic ? (ibLocked && Close[0] > ibHigh) : (Close[0] > Close[1]);
            bool brokeDnNow    = UseIBLogic ? (ibLocked && Close[0] < ibLow)  : (Close[0] < Close[1]);
            bool pullbackLong  = UseIBLogic ? (ibLocked && Low[0]  <= ibHigh && Close[0] > ibHigh) : true;
            bool pullbackShort = UseIBLogic ? (ibLocked && High[0] >= ibLow  && Close[0] < ibLow)  : true;

            bool midWindowBreakOK   = UseIBLogic && AggressiveBreak && (t >= 94500 && t < 100000) && (brokeUpNow || brokeDnNow);
            bool alreadyOutside1000 = UseIBLogic && AggressiveBreak && (t >= 100000) && (Close[0] > ibHigh || Close[0] < ibLow);

            bool ciPass     = !UseCIFilter      || (ciNow <= 54.5);
            bool volPass    = !UseVolumeFilter  || vol3OK;
            bool emaLong    = !UseEMABiasFilter || longBiasBase;
            bool emaShort   = !UseEMABiasFilter || shortBiasBase;

            bool gateLong   = !UsePullbackFilter ? (UseIBLogic ? brokeUpNow : true) : pullbackLong;
            bool gateShort  = !UsePullbackFilter ? (UseIBLogic ? brokeDnNow : true) : pullbackShort;

            bool modeOKUp   = !UseIBLogic ? true
                               : (UseRotationMode ? ((trendMode && brokeUpNow) || (rotationModeActive && brokeUpNow) || midWindowBreakOK || alreadyOutside1000)
                                                  : (brokeUpNow || midWindowBreakOK || alreadyOutside1000));
            bool modeOKDown = !UseIBLogic ? true
                               : (UseRotationMode ? ((trendMode && brokeDnNow) || (rotationModeActive && brokeDnNow) || midWindowBreakOK || alreadyOutside1000)
                                                  : (brokeDnNow || midWindowBreakOK || alreadyOutside1000));

            bool orbLong  = ( (!UseIBLogic) || (gateLong  && modeOKUp) )   && volPass && ciPass && emaLong;
            bool orbShort = ( (!UseIBLogic) || (gateShort && modeOKDown) ) && volPass && ciPass && emaShort;

            bool rejLong  = emaLong  && volPass && (Close[0] > vwapVal) && (Low[0]  <= vwapVal) && (!UseCIFilter || (ciPrev >= 54.5 && ciNow <= 54.5));
            bool rejShort = emaShort && volPass && (Close[0] < vwapVal) && (High[0] >= vwapVal) && (!UseCIFilter || (ciPrev >= 54.5 && ciNow <= 54.5));

            // Debug
            if (ShowDebug && (orbLong || orbShort))
            {
                string dbg = $"ci:{ciPass} vol:{volPass} ema:{(orbLong?emaLong:emaShort)} ib:{UseIBLogic} gate:{(orbLong?gateLong:gateShort)} mode:{(orbLong?modeOKUp:modeOKDown)}";
                Draw.Text(this, "DbgSig"+CurrentBar, dbg, 0, (orbLong? Low[0]-10*TickSize : High[0]+10*TickSize), Brushes.Silver);
            }

            // Fire visuals & set signal flags
            if (orbLong)
            {
                if (!SuppressTradeMarkers) Draw.TriangleUp(this, "ORBL"+CurrentBar, false, 0, Low[0] - 2 * TickSize, Brushes.LimeGreen);
                Alert("ORBLong", Priority.High, "ORB‑P Long", PlaySounds ? "Alert3.wav":null, 0, Brushes.Black, Brushes.LightGreen);
                sigOrbLong[0] = 1;
            }
            if (orbShort)
            {
                if (!SuppressTradeMarkers) Draw.TriangleDown(this, "ORBS"+CurrentBar, false, 0, High[0] + 2 * TickSize, Brushes.OrangeRed);
                Alert("ORBShort", Priority.High, "ORB‑P Short", PlaySounds ? "Alert3.wav":null, 0, Brushes.Black, Brushes.OrangeRed);
                sigOrbShort[0] = 1;
            }
            if (rejLong)
            {
                if (!SuppressTradeMarkers) Draw.ArrowUp(this, "VWAPL"+CurrentBar, false, 0, Low[0] - 3*TickSize, Brushes.LimeGreen);
                Alert("VWAPL", Priority.High, "VWAP Rejection LONG", PlaySounds ? "Alert3.wav":null, 0, Brushes.Black, Brushes.LightGreen);
                sigRejLong[0] = 1;
            }
            if (rejShort)
            {
                if (!SuppressTradeMarkers) Draw.ArrowDown(this, "VWAPS"+CurrentBar, false, 0, High[0] + 3*TickSize, Brushes.OrangeRed);
                Alert("VWAPS", Priority.High, "VWAP Rejection SHORT", PlaySounds ? "Alert3.wav":null, 0, Brushes.Black, Brushes.OrangeRed);
                sigRejShort[0] = 1;
            }

            if (ShowIBLines && UseIBLogic && ibLocked)
            {
                Draw.HorizontalLine(this, "IBH", ibHigh, Brushes.DarkGreen);
                Draw.HorizontalLine(this, "IBL", ibLow,  Brushes.IndianRed);
            }
        }

        private double Choppiness(int len)
        {
            if (CurrentBar < len + 2) return 100.0;

            double sumTR = 0.0, highest = High[0], lowest = Low[0];
            for (int i = 0; i < len; i++)
            {
                double hi = High[i], lo = Low[i], pc = Close[i + 1];
                double tr1 = hi - lo, tr2 = Math.Abs(hi - pc), tr3 = Math.Abs(lo - pc);
                double tr  = Math.Max(tr1, Math.Max(tr2, tr3));
                sumTR += tr;
                if (hi > highest) highest = hi;
                if (lo < lowest)  lowest  = lo;
            }
            double range = Math.Max(highest - lowest, TickSize);
            return 100.0 * (Math.Log(sumTR / range) / Math.Log(len));
        }

        // ---- public signal accessors for strategies ----
        [Browsable(false)] public Series<double> SigOrbLong  { get { return sigOrbLong; } }
        [Browsable(false)] public Series<double> SigOrbShort { get { return sigOrbShort; } }
        [Browsable(false)] public Series<double> SigRejLong  { get { return sigRejLong; } }
        [Browsable(false)] public Series<double> SigRejShort { get { return sigRejShort; } }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RotationAwareTwoSetupAlerts[] cacheRotationAwareTwoSetupAlerts;
		public RotationAwareTwoSetupAlerts RotationAwareTwoSetupAlerts(int chopLength, int emaFast, int emaSlow, double volMult, bool aggressiveBreak, bool requirePullback, int ciStreakMin, bool useCIFilter, bool useVolumeFilter, bool useEMABiasFilter, bool useIBLogic, bool usePullbackFilter, bool useRotationMode, bool suppressTradeMarkers, bool showIBLines, string cIPlotMode, bool showCILabels, bool playSounds, bool showDebug)
		{
			return RotationAwareTwoSetupAlerts(Input, chopLength, emaFast, emaSlow, volMult, aggressiveBreak, requirePullback, ciStreakMin, useCIFilter, useVolumeFilter, useEMABiasFilter, useIBLogic, usePullbackFilter, useRotationMode, suppressTradeMarkers, showIBLines, cIPlotMode, showCILabels, playSounds, showDebug);
		}

		public RotationAwareTwoSetupAlerts RotationAwareTwoSetupAlerts(ISeries<double> input, int chopLength, int emaFast, int emaSlow, double volMult, bool aggressiveBreak, bool requirePullback, int ciStreakMin, bool useCIFilter, bool useVolumeFilter, bool useEMABiasFilter, bool useIBLogic, bool usePullbackFilter, bool useRotationMode, bool suppressTradeMarkers, bool showIBLines, string cIPlotMode, bool showCILabels, bool playSounds, bool showDebug)
		{
			if (cacheRotationAwareTwoSetupAlerts != null)
				for (int idx = 0; idx < cacheRotationAwareTwoSetupAlerts.Length; idx++)
					if (cacheRotationAwareTwoSetupAlerts[idx] != null && cacheRotationAwareTwoSetupAlerts[idx].ChopLength == chopLength && cacheRotationAwareTwoSetupAlerts[idx].EmaFast == emaFast && cacheRotationAwareTwoSetupAlerts[idx].EmaSlow == emaSlow && cacheRotationAwareTwoSetupAlerts[idx].VolMult == volMult && cacheRotationAwareTwoSetupAlerts[idx].AggressiveBreak == aggressiveBreak && cacheRotationAwareTwoSetupAlerts[idx].RequirePullback == requirePullback && cacheRotationAwareTwoSetupAlerts[idx].CiStreakMin == ciStreakMin && cacheRotationAwareTwoSetupAlerts[idx].UseCIFilter == useCIFilter && cacheRotationAwareTwoSetupAlerts[idx].UseVolumeFilter == useVolumeFilter && cacheRotationAwareTwoSetupAlerts[idx].UseEMABiasFilter == useEMABiasFilter && cacheRotationAwareTwoSetupAlerts[idx].UseIBLogic == useIBLogic && cacheRotationAwareTwoSetupAlerts[idx].UsePullbackFilter == usePullbackFilter && cacheRotationAwareTwoSetupAlerts[idx].UseRotationMode == useRotationMode && cacheRotationAwareTwoSetupAlerts[idx].SuppressTradeMarkers == suppressTradeMarkers && cacheRotationAwareTwoSetupAlerts[idx].ShowIBLines == showIBLines && cacheRotationAwareTwoSetupAlerts[idx].CIPlotMode == cIPlotMode && cacheRotationAwareTwoSetupAlerts[idx].ShowCILabels == showCILabels && cacheRotationAwareTwoSetupAlerts[idx].PlaySounds == playSounds && cacheRotationAwareTwoSetupAlerts[idx].ShowDebug == showDebug && cacheRotationAwareTwoSetupAlerts[idx].EqualsInput(input))
						return cacheRotationAwareTwoSetupAlerts[idx];
			return CacheIndicator<RotationAwareTwoSetupAlerts>(new RotationAwareTwoSetupAlerts(){ ChopLength = chopLength, EmaFast = emaFast, EmaSlow = emaSlow, VolMult = volMult, AggressiveBreak = aggressiveBreak, RequirePullback = requirePullback, CiStreakMin = ciStreakMin, UseCIFilter = useCIFilter, UseVolumeFilter = useVolumeFilter, UseEMABiasFilter = useEMABiasFilter, UseIBLogic = useIBLogic, UsePullbackFilter = usePullbackFilter, UseRotationMode = useRotationMode, SuppressTradeMarkers = suppressTradeMarkers, ShowIBLines = showIBLines, CIPlotMode = cIPlotMode, ShowCILabels = showCILabels, PlaySounds = playSounds, ShowDebug = showDebug }, input, ref cacheRotationAwareTwoSetupAlerts);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RotationAwareTwoSetupAlerts RotationAwareTwoSetupAlerts(int chopLength, int emaFast, int emaSlow, double volMult, bool aggressiveBreak, bool requirePullback, int ciStreakMin, bool useCIFilter, bool useVolumeFilter, bool useEMABiasFilter, bool useIBLogic, bool usePullbackFilter, bool useRotationMode, bool suppressTradeMarkers, bool showIBLines, string cIPlotMode, bool showCILabels, bool playSounds, bool showDebug)
		{
			return indicator.RotationAwareTwoSetupAlerts(Input, chopLength, emaFast, emaSlow, volMult, aggressiveBreak, requirePullback, ciStreakMin, useCIFilter, useVolumeFilter, useEMABiasFilter, useIBLogic, usePullbackFilter, useRotationMode, suppressTradeMarkers, showIBLines, cIPlotMode, showCILabels, playSounds, showDebug);
		}

		public Indicators.RotationAwareTwoSetupAlerts RotationAwareTwoSetupAlerts(ISeries<double> input , int chopLength, int emaFast, int emaSlow, double volMult, bool aggressiveBreak, bool requirePullback, int ciStreakMin, bool useCIFilter, bool useVolumeFilter, bool useEMABiasFilter, bool useIBLogic, bool usePullbackFilter, bool useRotationMode, bool suppressTradeMarkers, bool showIBLines, string cIPlotMode, bool showCILabels, bool playSounds, bool showDebug)
		{
			return indicator.RotationAwareTwoSetupAlerts(input, chopLength, emaFast, emaSlow, volMult, aggressiveBreak, requirePullback, ciStreakMin, useCIFilter, useVolumeFilter, useEMABiasFilter, useIBLogic, usePullbackFilter, useRotationMode, suppressTradeMarkers, showIBLines, cIPlotMode, showCILabels, playSounds, showDebug);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RotationAwareTwoSetupAlerts RotationAwareTwoSetupAlerts(int chopLength, int emaFast, int emaSlow, double volMult, bool aggressiveBreak, bool requirePullback, int ciStreakMin, bool useCIFilter, bool useVolumeFilter, bool useEMABiasFilter, bool useIBLogic, bool usePullbackFilter, bool useRotationMode, bool suppressTradeMarkers, bool showIBLines, string cIPlotMode, bool showCILabels, bool playSounds, bool showDebug)
		{
			return indicator.RotationAwareTwoSetupAlerts(Input, chopLength, emaFast, emaSlow, volMult, aggressiveBreak, requirePullback, ciStreakMin, useCIFilter, useVolumeFilter, useEMABiasFilter, useIBLogic, usePullbackFilter, useRotationMode, suppressTradeMarkers, showIBLines, cIPlotMode, showCILabels, playSounds, showDebug);
		}

		public Indicators.RotationAwareTwoSetupAlerts RotationAwareTwoSetupAlerts(ISeries<double> input , int chopLength, int emaFast, int emaSlow, double volMult, bool aggressiveBreak, bool requirePullback, int ciStreakMin, bool useCIFilter, bool useVolumeFilter, bool useEMABiasFilter, bool useIBLogic, bool usePullbackFilter, bool useRotationMode, bool suppressTradeMarkers, bool showIBLines, string cIPlotMode, bool showCILabels, bool playSounds, bool showDebug)
		{
			return indicator.RotationAwareTwoSetupAlerts(input, chopLength, emaFast, emaSlow, volMult, aggressiveBreak, requirePullback, ciStreakMin, useCIFilter, useVolumeFilter, useEMABiasFilter, useIBLogic, usePullbackFilter, useRotationMode, suppressTradeMarkers, showIBLines, cIPlotMode, showCILabels, playSounds, showDebug);
		}
	}
}

#endregion
