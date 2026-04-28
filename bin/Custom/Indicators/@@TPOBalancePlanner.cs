// NT8 Indicator: TPO Balance Planner (ESU25) – FULL, COMPILE‑READY
// Removed dependency on NinjaTrader.Gui.Tools.Serialize (uses local BrushConverter helpers).
// Default N (SessionsBack) = 1 day.

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class TPOBalancePlanner : Indicator
    {
        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Sessions back", GroupName = "Parameters", Order = 0)]
        public int SessionsBack { get; set; }

        [NinjaScriptProperty, Range(0, 90)]
        [Display(Name = "Composite days", GroupName = "Parameters", Order = 1)]
        public int CompositeDays { get; set; }

        [NinjaScriptProperty, Range(0.50, 0.99)]
        [Display(Name = "Value area %", GroupName = "Parameters", Order = 2)]
        public double ValueAreaPct { get; set; }

        [NinjaScriptProperty, Range(1, 16)]
        [Display(Name = "TPO tick multiple", GroupName = "Parameters", Order = 3)]
        public int TpoTickMultiple { get; set; }

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name = "Balance lookback (sessions)", GroupName = "Parameters", Order = 4)]
        public int BalanceLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show labels", GroupName = "Visual", Order = 10)]
        public bool ShowLabels { get; set; }

        // --- Brushes with string serialization (no Serialize dependency) ---
        [XmlIgnore] public Brush VahBrush { get; set; }
        [Browsable(false)] public string VahBrushSerializable { get { return BrushToString(VahBrush); } set { VahBrush = StringToBrush(value); } }

        [XmlIgnore] public Brush ValBrush { get; set; }
        [Browsable(false)] public string ValBrushSerializable { get { return BrushToString(ValBrush); } set { ValBrush = StringToBrush(value); } }

        [XmlIgnore] public Brush PocBrush { get; set; }
        [Browsable(false)] public string PocBrushSerializable { get { return BrushToString(PocBrush); } set { PocBrush = StringToBrush(value); } }

        [XmlIgnore] public Brush BalBrush { get; set; }
        [Browsable(false)] public string BalBrushSerializable { get { return BrushToString(BalBrush); } set { BalBrush = StringToBrush(value); } }

        [XmlIgnore] public Brush TextBrush { get; set; }
        [Browsable(false)] public string TextBrushSerializable { get { return BrushToString(TextBrush); } set { TextBrush = StringToBrush(value); } }

        private SimpleFont infoFont;
        private DateTime lastSessionDate = Core.Globals.MinDate;
        private double prevSessionHigh = double.NaN;
        private double prevSessionLow  = double.NaN;
        private double prevSessionPOC  = double.NaN;
        private double prevVAH = double.NaN;
        private double prevVAL = double.NaN;
        private double prevPOC = double.NaN;
        private double balHigh = double.NaN;
        private double balLow = double.NaN;
        private double curHigh = double.NaN;
        private double curLow = double.NaN;
        private List<double> curPrices = new List<double>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "TPO Balance Planner";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                IsSuspendedWhileInactive = true;
                SessionsBack = 1;          // yesterday
                CompositeDays = 0;         // off for now
                ValueAreaPct = 0.70;       // 70% VA default
                TpoTickMultiple = 1;
                BalanceLookback = 10;
                ShowLabels = true;
                VahBrush = Brushes.LimeGreen;
                ValBrush = Brushes.Orange;
                PocBrush = Brushes.DeepSkyBlue;
                BalBrush = Brushes.Goldenrod;
                TextBrush = Brushes.White;
                infoFont = new SimpleFont("Segoe UI", 12);
            }
            else if (State == State.DataLoaded)
            {
                curHigh = double.NaN;
                curLow = double.NaN;
                curPrices.Clear();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            DateTime sessionDate = Time[0].Date;
            bool newSession = sessionDate != lastSessionDate && lastSessionDate != Core.Globals.MinDate;

            if (double.IsNaN(curHigh) || High[0] > curHigh) curHigh = High[0];
            if (double.IsNaN(curLow) || Low[0] < curLow) curLow = Low[0];
            curPrices.Add(Close[0]);

            if (newSession)
            {
                // Capture previous session stats
                prevSessionHigh = curHigh;
                prevSessionLow = curLow;
                prevSessionPOC = curPrices.Count > 0 ? Percentile(curPrices, 50) : (curHigh + curLow) * 0.5;
                prevVAH = prevSessionHigh;
                prevVAL = prevSessionLow;
                prevPOC = prevSessionPOC;

                // Simple balance channel across lookback sessions (seeded with prev session)
                balHigh = double.IsNaN(balHigh) ? prevSessionHigh : Math.Max(balHigh, prevSessionHigh);
                balLow  = double.IsNaN(balLow)  ? prevSessionLow  : Math.Min(balLow,  prevSessionLow);

                // Reset intraday trackers
                curHigh = High[0];
                curLow = Low[0];
                curPrices.Clear();
                curPrices.Add(Close[0]);
            }

            lastSessionDate = sessionDate;
            DrawOrUpdateLines();
        }

        private void DrawOrUpdateLines()
        {
            if (!double.IsNaN(prevVAH)) Draw.HorizontalLine(this, "VAH_prev", prevVAH, VahBrush);
            if (!double.IsNaN(prevVAL)) Draw.HorizontalLine(this, "VAL_prev", prevVAL, ValBrush);
            if (!double.IsNaN(prevPOC)) Draw.HorizontalLine(this, "POC_prev", prevPOC, PocBrush);
            if (!double.IsNaN(balHigh)) Draw.HorizontalLine(this, "BAL_HI", balHigh, BalBrush);
            if (!double.IsNaN(balLow))  Draw.HorizontalLine(this, "BAL_LO", balLow,  BalBrush);

            if (ShowLabels)
            {
string lbl = "Prev/Composite: VAH " + SafeF(prevVAH) + "  VAL " + SafeF(prevVAL) + "  POC " + SafeF(prevPOC)
           + "\nBalance Hi " + SafeF(balHigh) + "  Lo " + SafeF(balLow);
                Draw.TextFixed(this, "tpo_info", lbl, TextPosition.BottomRight, TextBrush, infoFont, TextBrush, TextBrush, 0);
            }
        }

        private static string SafeF(double v) => double.IsNaN(v) ? "—" : v.ToString("0.00", CultureInfo.InvariantCulture);

        private static double Percentile(List<double> data, double p)
        {
            if (data == null || data.Count == 0) return double.NaN;
            var arr = data.OrderBy(x => x).ToArray();
            double idx = (arr.Length - 1) * p / 100.0;
            int lo = (int)Math.Floor(idx);
            int hi = (int)Math.Ceiling(idx);
            if (lo == hi) return arr[lo];
            return arr[lo] + (arr[hi] - arr[lo]) * (idx - lo);
        }

        // ----- Brush <-> string helpers (replace Serialize.*) -----
        private static readonly BrushConverter _bc = new BrushConverter();
        private static string BrushToString(Brush b)
        {
            try { return b == null ? string.Empty : _bc.ConvertToString(null, CultureInfo.InvariantCulture, b); }
            catch { return string.Empty; }
        }
        private static Brush StringToBrush(string s)
        {
            try { return string.IsNullOrEmpty(s) ? null : (Brush)_bc.ConvertFromString(null, CultureInfo.InvariantCulture, s); }
            catch { return Brushes.Transparent; }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TPOBalancePlanner[] cacheTPOBalancePlanner;
		public TPOBalancePlanner TPOBalancePlanner(int sessionsBack, int compositeDays, double valueAreaPct, int tpoTickMultiple, int balanceLookback, bool showLabels)
		{
			return TPOBalancePlanner(Input, sessionsBack, compositeDays, valueAreaPct, tpoTickMultiple, balanceLookback, showLabels);
		}

		public TPOBalancePlanner TPOBalancePlanner(ISeries<double> input, int sessionsBack, int compositeDays, double valueAreaPct, int tpoTickMultiple, int balanceLookback, bool showLabels)
		{
			if (cacheTPOBalancePlanner != null)
				for (int idx = 0; idx < cacheTPOBalancePlanner.Length; idx++)
					if (cacheTPOBalancePlanner[idx] != null && cacheTPOBalancePlanner[idx].SessionsBack == sessionsBack && cacheTPOBalancePlanner[idx].CompositeDays == compositeDays && cacheTPOBalancePlanner[idx].ValueAreaPct == valueAreaPct && cacheTPOBalancePlanner[idx].TpoTickMultiple == tpoTickMultiple && cacheTPOBalancePlanner[idx].BalanceLookback == balanceLookback && cacheTPOBalancePlanner[idx].ShowLabels == showLabels && cacheTPOBalancePlanner[idx].EqualsInput(input))
						return cacheTPOBalancePlanner[idx];
			return CacheIndicator<TPOBalancePlanner>(new TPOBalancePlanner(){ SessionsBack = sessionsBack, CompositeDays = compositeDays, ValueAreaPct = valueAreaPct, TpoTickMultiple = tpoTickMultiple, BalanceLookback = balanceLookback, ShowLabels = showLabels }, input, ref cacheTPOBalancePlanner);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TPOBalancePlanner TPOBalancePlanner(int sessionsBack, int compositeDays, double valueAreaPct, int tpoTickMultiple, int balanceLookback, bool showLabels)
		{
			return indicator.TPOBalancePlanner(Input, sessionsBack, compositeDays, valueAreaPct, tpoTickMultiple, balanceLookback, showLabels);
		}

		public Indicators.TPOBalancePlanner TPOBalancePlanner(ISeries<double> input , int sessionsBack, int compositeDays, double valueAreaPct, int tpoTickMultiple, int balanceLookback, bool showLabels)
		{
			return indicator.TPOBalancePlanner(input, sessionsBack, compositeDays, valueAreaPct, tpoTickMultiple, balanceLookback, showLabels);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TPOBalancePlanner TPOBalancePlanner(int sessionsBack, int compositeDays, double valueAreaPct, int tpoTickMultiple, int balanceLookback, bool showLabels)
		{
			return indicator.TPOBalancePlanner(Input, sessionsBack, compositeDays, valueAreaPct, tpoTickMultiple, balanceLookback, showLabels);
		}

		public Indicators.TPOBalancePlanner TPOBalancePlanner(ISeries<double> input , int sessionsBack, int compositeDays, double valueAreaPct, int tpoTickMultiple, int balanceLookback, bool showLabels)
		{
			return indicator.TPOBalancePlanner(input, sessionsBack, compositeDays, valueAreaPct, tpoTickMultiple, balanceLookback, showLabels);
		}
	}
}

#endregion
