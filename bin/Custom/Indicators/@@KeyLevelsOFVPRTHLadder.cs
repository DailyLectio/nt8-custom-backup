#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Xml.Serialization;
using System.Windows.Media;

using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.BarsTypes;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class KeyLevelsOFVPRTHLadder : Indicator
    {
        private VolumetricBarsType volBars;
        private SessionIterator sessionIterator;

        private DateTime sessionStart = Core.Globals.MinDate;
        private DateTime sessionEnd   = Core.Globals.MinDate;
        private DateTime orEndTime    = Core.Globals.MinDate;
        private DateTime snap1Time    = Core.Globals.MinDate;
        private DateTime snap2Time    = Core.Globals.MinDate;

        private bool snap1Done;
        private bool snap2Done;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "KeyLevelsOFVPRTHLadder";
                Description              = "RTH OR-based POC ladder using Volumetric Bid/Ask. Optional S1 + S2 snapshots. Drawn as horizontal lines (reliable across NT8 builds).";
                Calculate                = Calculate.OnBarClose;

                IsOverlay                = true;
                DrawOnPricePanel         = true;
                DisplayInDataBox         = false;
                PaintPriceMarkers        = false;
                IsSuspendedWhileInactive = true;
                IsAutoScale              = false;

                // ===== OR / snapshots =====
                OpeningRangeMinutes = 60;   // 9:30–10:30
                Snapshot1Minutes    = 1;    // sessionStart + 1 minute
                Snapshot2DelayMins  = 1;    // OR end + 1 minute

                EnableSnapshot1     = true;
                EnableSnapshot2     = true;
                UseOnlySnapshot2    = false;

                // ===== Ladder fractions =====
                FractionsCsv        = "0.25,0.382,0.5,0.618,0.786,1.0";
                RangeMultiplier     = 1.0;

                // ===== Styling =====
                PocWidth              = 3;
                LevelWidth            = 2;
                GrayWidth             = 1;
                ShowGrayIntermediates = true;

                // ===== History behavior =====
                KeepPriorSessions   = false;

                AboveBrush          = Brushes.LimeGreen;
                BelowBrush          = Brushes.Red;
                PocBrush            = Brushes.DodgerBlue;
                GrayBrush           = Brushes.DimGray;

                // Debug overlay
                ShowDebugText       = true;
            }
            else if (State == State.DataLoaded)
            {
                volBars = BarsArray[0].BarsType as VolumetricBarsType;
                sessionIterator = new SessionIterator(Bars);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            if (volBars == null)
                volBars = BarsArray[0].BarsType as VolumetricBarsType;

            if (volBars == null)
                return;

            // --- Session handling: do NOT rely solely on Bars.IsFirstBarOfSession (replay/templates can break it)
            UpdateSessionBoundaries();

            if (ShowDebugText)
            {
                Draw.TextFixed(this, "KLVPRTH_ALIVE",
                    $"KLVPRTH | Time={Time[0]:MM-dd HH:mm} | Sess={sessionStart:HH:mm}-{sessionEnd:HH:mm} | IsFirst={Bars.IsFirstBarOfSession} | volBars={(volBars != null ? "OK" : "NULL")}",
                    TextPosition.TopLeft);
            }

            // Snapshot 1
            if (EnableSnapshot1 && !UseOnlySnapshot2 && !snap1Done && Time[0] >= snap1Time)
            {
                TryBuildSnapshot("S1", sessionStart, snap1Time);
                snap1Done = true;
            }

            // Snapshot 2 (Opening Range)
            if (EnableSnapshot2 && !snap2Done && Time[0] >= snap2Time)
            {
                TryBuildSnapshot("S2", sessionStart, orEndTime);
                snap2Done = true;
            }
        }

        private void UpdateSessionBoundaries()
        {
            // If first run or we've moved outside current session, advance iterator
            if (sessionStart == Core.Globals.MinDate || Time[0] < sessionStart || Time[0] >= sessionEnd)
            {
                // This call pattern is the most common in NT8 indicators
                sessionIterator.GetNextSession(Time[0], true);

                sessionStart = sessionIterator.ActualSessionBegin;
                sessionEnd   = sessionIterator.ActualSessionEnd;

                orEndTime = sessionStart.AddMinutes(OpeningRangeMinutes);
                snap1Time = sessionStart.AddMinutes(Snapshot1Minutes);
                snap2Time = orEndTime.AddMinutes(Snapshot2DelayMins);

                snap1Done = false;
                snap2Done = false;
            }
        }

        private void TryBuildSnapshot(string snapTag, DateTime windowStartTime, DateTime windowEndTime)
        {
            int startIdx = Bars.GetBar(windowStartTime);
            int endIdx   = Bars.GetBar(windowEndTime);

            // Fallback: scan current loaded bars for this session date
            if (startIdx < 0) startIdx = FindFirstIndexForDate(sessionStart.Date);
            if (endIdx   < 0) endIdx   = FindLastIndexForDate(sessionStart.Date);

            startIdx = Math.Max(0, Math.Min(CurrentBar, startIdx));
            endIdx   = Math.Max(0, Math.Min(CurrentBar, endIdx));

            if (endIdx < startIdx)
                return;

            // Validate volumetric availability
            if (volBars.Volumes == null || endIdx >= volBars.Volumes.Length || volBars.Volumes[endIdx] == null)
                return;

            // Window High/Low from price bars (absolute indexing)
            double wHigh = double.MinValue;
            double wLow  = double.MaxValue;

            for (int idx = startIdx; idx <= endIdx; idx++)
            {
                wHigh = Math.Max(wHigh, Highs[0][idx]);
                wLow  = Math.Min(wLow,  Lows[0][idx]);
            }

            if (wHigh <= wLow || wHigh == double.MinValue || wLow == double.MaxValue)
                return;

            double poc = ComputePOC_Volumetric(startIdx, endIdx);
            if (double.IsNaN(poc))
                return;

            DrawSnapshotLadder(snapTag, sessionStart.Date, poc, wHigh, wLow);
        }

        private double ComputePOC_Volumetric(int startIdx, int endIdx)
        {
            double ts = TickSize;
            if (ts <= 0)
                return double.NaN;

            var map = new System.Collections.Generic.Dictionary<double, double>();

            for (int idx = startIdx; idx <= endIdx; idx++)
            {
                var vd = volBars.Volumes[idx];
                if (vd == null)
                    continue;

                double hi = Highs[0][idx];
                double lo = Lows[0][idx];

                for (double p = lo; p <= hi + (ts * 0.5); p += ts)
                {
                    double price = Instrument.MasterInstrument.RoundToTickSize(p);

                    long bid = vd.GetBidVolumeForPrice(price);
                    long ask = vd.GetAskVolumeForPrice(price);
                    double total = bid + ask;

                    if (total <= 0)
                        continue;

                    if (map.ContainsKey(price))
                        map[price] += total;
                    else
                        map[price] = total;
                }
            }

            if (map.Count == 0)
                return double.NaN;

            // Tie-breaker: closest to mid of the *window* (not just current bar)
            double mid = (Highs[0][startIdx] + Lows[0][startIdx]) * 0.5;

            double bestPrice = double.NaN;
            double bestVol   = double.MinValue;

            foreach (var kv in map)
            {
                if (kv.Value > bestVol)
                {
                    bestVol   = kv.Value;
                    bestPrice = kv.Key;
                }
                else if (Math.Abs(kv.Value - bestVol) < 0.0001)
                {
                    if (Math.Abs(kv.Key - mid) < Math.Abs(bestPrice - mid))
                        bestPrice = kv.Key;
                }
            }

            return bestPrice;
        }

        private void DrawSnapshotLadder(string snapTag, DateTime sessionDate, double poc, double wHigh, double wLow)
        {
            double baseRange = (wHigh - wLow) * RangeMultiplier;
            if (baseRange <= TickSize)
                return;

            var fracs = ParseFractions();

            // Tag prefix: overwrite each day if KeepPriorSessions=false
            string prefix = KeepPriorSessions
                ? $"KLVPRTH_{Instrument.FullName}_{sessionDate:yyyyMMdd}_{snapTag}"
                : $"KLVPRTH_{Instrument.FullName}_{snapTag}";

            // POC
            HLine($"{prefix}_POC", poc, PocBrush, PocWidth);

            // Above
            for (int i = 0; i < fracs.Length; i++)
            {
                double up = Instrument.MasterInstrument.RoundToTickSize(poc + baseRange * fracs[i]);
                HLine($"{prefix}_A_{i}", up, AboveBrush, LevelWidth);

                if (ShowGrayIntermediates && i > 0)
                {
                    double prev = poc + baseRange * fracs[i - 1];
                    double mid  = Instrument.MasterInstrument.RoundToTickSize((prev + up) * 0.5);
                    HLine($"{prefix}_AG_{i}", mid, GrayBrush, GrayWidth);
                }
            }

            // Below
            for (int i = 0; i < fracs.Length; i++)
            {
                double dn = Instrument.MasterInstrument.RoundToTickSize(poc - baseRange * fracs[i]);
                HLine($"{prefix}_B_{i}", dn, BelowBrush, LevelWidth);

                if (ShowGrayIntermediates && i > 0)
                {
                    double prev = poc - baseRange * fracs[i - 1];
                    double mid  = Instrument.MasterInstrument.RoundToTickSize((prev + dn) * 0.5);
                    HLine($"{prefix}_BG_{i}", mid, GrayBrush, GrayWidth);
                }
            }
        }

        private void HLine(string tag, double price, Brush brush, int width)
        {
            // This signature is the one that matches your working compiled file
            var h = Draw.HorizontalLine(this, tag, price, brush);
            if (h != null && h.Stroke != null)
                h.Stroke.Width = Math.Max(1, width);
        }

        private double[] ParseFractions()
        {
            try
            {
                return FractionsCsv
                    .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => double.Parse(s, CultureInfo.InvariantCulture))
                    .Where(v => v > 0)
                    .Distinct()
                    .OrderBy(v => v)
                    .ToArray();
            }
            catch
            {
                return new[] { 0.25, 0.382, 0.5, 0.618, 0.786, 1.0 };
            }
        }

        private int FindFirstIndexForDate(DateTime d)
        {
            for (int idx = 0; idx <= CurrentBar; idx++)
                if (Times[0][idx].Date == d)
                    return idx;
            return 0;
        }

        private int FindLastIndexForDate(DateTime d)
        {
            for (int idx = CurrentBar; idx >= 0; idx--)
                if (Times[0][idx].Date == d)
                    return idx;
            return CurrentBar;
        }

        #region Properties

        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(Name = "OpeningRangeMinutes", GroupName = "OR / Snapshots", Order = 0)]
        public int OpeningRangeMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "Snapshot1Minutes (from session start)", GroupName = "OR / Snapshots", Order = 1)]
        public int Snapshot1Minutes { get; set; }

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "Snapshot2DelayMins (after OR end)", GroupName = "OR / Snapshots", Order = 2)]
        public int Snapshot2DelayMins { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EnableSnapshot1", GroupName = "OR / Snapshots", Order = 3)]
        public bool EnableSnapshot1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EnableSnapshot2", GroupName = "OR / Snapshots", Order = 4)]
        public bool EnableSnapshot2 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "UseOnlySnapshot2 (simpler)", GroupName = "OR / Snapshots", Order = 5)]
        public bool UseOnlySnapshot2 { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "RangeMultiplier", GroupName = "Parameters", Order = 10)]
        public double RangeMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "FractionsCsv", GroupName = "Parameters", Order = 11)]
        public string FractionsCsv { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ShowGrayIntermediates", GroupName = "Parameters", Order = 12)]
        public bool ShowGrayIntermediates { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "POC Width", GroupName = "Style", Order = 20)]
        public int PocWidth { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Level Width", GroupName = "Style", Order = 21)]
        public int LevelWidth { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5)]
        [Display(Name = "Gray Width", GroupName = "Style", Order = 22)]
        public int GrayWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "KeepPriorSessions", GroupName = "History", Order = 30)]
        public bool KeepPriorSessions { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ShowDebugText", GroupName = "Debug", Order = 40)]
        public bool ShowDebugText { get; set; }

        [XmlIgnore]
        [Display(Name = "AboveBrush", GroupName = "Style", Order = 50)]
        public Brush AboveBrush { get; set; }

        [XmlIgnore]
        [Display(Name = "BelowBrush", GroupName = "Style", Order = 51)]
        public Brush BelowBrush { get; set; }

        [XmlIgnore]
        [Display(Name = "PocBrush", GroupName = "Style", Order = 52)]
        public Brush PocBrush { get; set; }

        [XmlIgnore]
        [Display(Name = "GrayBrush", GroupName = "Style", Order = 53)]
        public Brush GrayBrush { get; set; }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private KeyLevelsOFVPRTHLadder[] cacheKeyLevelsOFVPRTHLadder;
		public KeyLevelsOFVPRTHLadder KeyLevelsOFVPRTHLadder(int openingRangeMinutes, int snapshot1Minutes, int snapshot2DelayMins, bool enableSnapshot1, bool enableSnapshot2, bool useOnlySnapshot2, double rangeMultiplier, string fractionsCsv, bool showGrayIntermediates, int pocWidth, int levelWidth, int grayWidth, bool keepPriorSessions, bool showDebugText)
		{
			return KeyLevelsOFVPRTHLadder(Input, openingRangeMinutes, snapshot1Minutes, snapshot2DelayMins, enableSnapshot1, enableSnapshot2, useOnlySnapshot2, rangeMultiplier, fractionsCsv, showGrayIntermediates, pocWidth, levelWidth, grayWidth, keepPriorSessions, showDebugText);
		}

		public KeyLevelsOFVPRTHLadder KeyLevelsOFVPRTHLadder(ISeries<double> input, int openingRangeMinutes, int snapshot1Minutes, int snapshot2DelayMins, bool enableSnapshot1, bool enableSnapshot2, bool useOnlySnapshot2, double rangeMultiplier, string fractionsCsv, bool showGrayIntermediates, int pocWidth, int levelWidth, int grayWidth, bool keepPriorSessions, bool showDebugText)
		{
			if (cacheKeyLevelsOFVPRTHLadder != null)
				for (int idx = 0; idx < cacheKeyLevelsOFVPRTHLadder.Length; idx++)
					if (cacheKeyLevelsOFVPRTHLadder[idx] != null && cacheKeyLevelsOFVPRTHLadder[idx].OpeningRangeMinutes == openingRangeMinutes && cacheKeyLevelsOFVPRTHLadder[idx].Snapshot1Minutes == snapshot1Minutes && cacheKeyLevelsOFVPRTHLadder[idx].Snapshot2DelayMins == snapshot2DelayMins && cacheKeyLevelsOFVPRTHLadder[idx].EnableSnapshot1 == enableSnapshot1 && cacheKeyLevelsOFVPRTHLadder[idx].EnableSnapshot2 == enableSnapshot2 && cacheKeyLevelsOFVPRTHLadder[idx].UseOnlySnapshot2 == useOnlySnapshot2 && cacheKeyLevelsOFVPRTHLadder[idx].RangeMultiplier == rangeMultiplier && cacheKeyLevelsOFVPRTHLadder[idx].FractionsCsv == fractionsCsv && cacheKeyLevelsOFVPRTHLadder[idx].ShowGrayIntermediates == showGrayIntermediates && cacheKeyLevelsOFVPRTHLadder[idx].PocWidth == pocWidth && cacheKeyLevelsOFVPRTHLadder[idx].LevelWidth == levelWidth && cacheKeyLevelsOFVPRTHLadder[idx].GrayWidth == grayWidth && cacheKeyLevelsOFVPRTHLadder[idx].KeepPriorSessions == keepPriorSessions && cacheKeyLevelsOFVPRTHLadder[idx].ShowDebugText == showDebugText && cacheKeyLevelsOFVPRTHLadder[idx].EqualsInput(input))
						return cacheKeyLevelsOFVPRTHLadder[idx];
			return CacheIndicator<KeyLevelsOFVPRTHLadder>(new KeyLevelsOFVPRTHLadder(){ OpeningRangeMinutes = openingRangeMinutes, Snapshot1Minutes = snapshot1Minutes, Snapshot2DelayMins = snapshot2DelayMins, EnableSnapshot1 = enableSnapshot1, EnableSnapshot2 = enableSnapshot2, UseOnlySnapshot2 = useOnlySnapshot2, RangeMultiplier = rangeMultiplier, FractionsCsv = fractionsCsv, ShowGrayIntermediates = showGrayIntermediates, PocWidth = pocWidth, LevelWidth = levelWidth, GrayWidth = grayWidth, KeepPriorSessions = keepPriorSessions, ShowDebugText = showDebugText }, input, ref cacheKeyLevelsOFVPRTHLadder);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.KeyLevelsOFVPRTHLadder KeyLevelsOFVPRTHLadder(int openingRangeMinutes, int snapshot1Minutes, int snapshot2DelayMins, bool enableSnapshot1, bool enableSnapshot2, bool useOnlySnapshot2, double rangeMultiplier, string fractionsCsv, bool showGrayIntermediates, int pocWidth, int levelWidth, int grayWidth, bool keepPriorSessions, bool showDebugText)
		{
			return indicator.KeyLevelsOFVPRTHLadder(Input, openingRangeMinutes, snapshot1Minutes, snapshot2DelayMins, enableSnapshot1, enableSnapshot2, useOnlySnapshot2, rangeMultiplier, fractionsCsv, showGrayIntermediates, pocWidth, levelWidth, grayWidth, keepPriorSessions, showDebugText);
		}

		public Indicators.KeyLevelsOFVPRTHLadder KeyLevelsOFVPRTHLadder(ISeries<double> input , int openingRangeMinutes, int snapshot1Minutes, int snapshot2DelayMins, bool enableSnapshot1, bool enableSnapshot2, bool useOnlySnapshot2, double rangeMultiplier, string fractionsCsv, bool showGrayIntermediates, int pocWidth, int levelWidth, int grayWidth, bool keepPriorSessions, bool showDebugText)
		{
			return indicator.KeyLevelsOFVPRTHLadder(input, openingRangeMinutes, snapshot1Minutes, snapshot2DelayMins, enableSnapshot1, enableSnapshot2, useOnlySnapshot2, rangeMultiplier, fractionsCsv, showGrayIntermediates, pocWidth, levelWidth, grayWidth, keepPriorSessions, showDebugText);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.KeyLevelsOFVPRTHLadder KeyLevelsOFVPRTHLadder(int openingRangeMinutes, int snapshot1Minutes, int snapshot2DelayMins, bool enableSnapshot1, bool enableSnapshot2, bool useOnlySnapshot2, double rangeMultiplier, string fractionsCsv, bool showGrayIntermediates, int pocWidth, int levelWidth, int grayWidth, bool keepPriorSessions, bool showDebugText)
		{
			return indicator.KeyLevelsOFVPRTHLadder(Input, openingRangeMinutes, snapshot1Minutes, snapshot2DelayMins, enableSnapshot1, enableSnapshot2, useOnlySnapshot2, rangeMultiplier, fractionsCsv, showGrayIntermediates, pocWidth, levelWidth, grayWidth, keepPriorSessions, showDebugText);
		}

		public Indicators.KeyLevelsOFVPRTHLadder KeyLevelsOFVPRTHLadder(ISeries<double> input , int openingRangeMinutes, int snapshot1Minutes, int snapshot2DelayMins, bool enableSnapshot1, bool enableSnapshot2, bool useOnlySnapshot2, double rangeMultiplier, string fractionsCsv, bool showGrayIntermediates, int pocWidth, int levelWidth, int grayWidth, bool keepPriorSessions, bool showDebugText)
		{
			return indicator.KeyLevelsOFVPRTHLadder(input, openingRangeMinutes, snapshot1Minutes, snapshot2DelayMins, enableSnapshot1, enableSnapshot2, useOnlySnapshot2, rangeMultiplier, fractionsCsv, showGrayIntermediates, pocWidth, levelWidth, grayWidth, keepPriorSessions, showDebugText);
		}
	}
}

#endregion
