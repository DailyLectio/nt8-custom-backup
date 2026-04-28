#region Using declarations
using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;

using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// Indicator: GlobexBiasTableExporter
// - Captures reference prices: 03:00 close, 08:30 close, 09:30 open
// - Calculates Opening Range (09:30 to 09:30+ORMinutes)
// - Classifies bias: Bearish/Bullish/Rotational per your rules
// - Computes daily ATR(10) + daily Avg Volume(10)
// - Optional: ADX(14) + Stoch(14,3,3) on 1-minute series
// - Draws a fixed "table" on chart and exports one CSV row per session at Snapshot #2 time

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GlobexBiasTableExporter : Indicator
    {
        // ===== User parameters =====

        [NinjaScriptProperty]
        [Display(Name="Snapshot 1 Time (HHmm)", Order=1, GroupName="Snapshots")]
        public int Snapshot1HHmm { get; set; } = 935;   // 09:35 by default

        [NinjaScriptProperty]
        [Display(Name="Snapshot 2 Time (HHmm)", Order=2, GroupName="Snapshots")]
        public int Snapshot2HHmm { get; set; } = 1000;  // 10:00 by default

        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name="Opening Range Minutes", Order=3, GroupName="Opening Range")]
        public int ORMinutes { get; set; } = 5;

        [NinjaScriptProperty]
        [Display(Name="Export CSV", Order=4, GroupName="Export")]
        public bool ExportCsv { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="CSV Folder Name (under Documents\\NinjaTrader 8)", Order=5, GroupName="Export")]
        public string CsvFolder { get; set; } = "GlobexBiasExports";

        [NinjaScriptProperty]
        [Display(Name="Include ADX(14) + Stoch(14,3,3) (1-min)", Order=6, GroupName="Risk Metrics")]
        public bool IncludeAdxStoch { get; set; } = true;

        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name="Daily ATR Lookback (days)", Order=7, GroupName="Risk Metrics")]
        public int AtrDays { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name="Daily Volume Avg Lookback (days)", Order=8, GroupName="Risk Metrics")]
        public int VolDays { get; set; } = 10;

        // ===== Internal series indexes =====
        private const int BIP_MIN1  = 1; // 1-minute
        private const int BIP_DAILY = 2; // daily

        // ===== Indicators =====
        private ATR atrDaily;
        private SMA volSmaDaily;

        private ADX adxMin1;
        private Stochastics stochMin1;

        // ===== Snapshot times (ToTime format) =====
        private int snapshot1Time; // e.g., 093500
        private int snapshot2Time; // e.g., 100000

        // ===== Per-session tracked values =====
        private double ref3amClose = double.NaN;   // A
        private double ref830Close = double.NaN;   // B
        private double ref930Open  = double.NaN;   // C

        private double orHigh = double.NaN;
        private double orLow  = double.NaN;
        private double orMid  = double.NaN;
        private double orSize = double.NaN;

        private bool orActive = false;
        private DateTime orStart;
        private DateTime orEnd;

        // Daily risk metrics (from daily series)
        private double atrN  = double.NaN;
        private double volN  = double.NaN;

        // Session bookkeeping
        private DateTime currentSessionDate = Core.Globals.MinDate;
        private bool wroteCsvForSession = false;

        // CSV output
        private string csvPath;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                    = "GlobexBiasTableExporter";
                Description             = "Globex→RTH reference prices + Opening Range + Bias + ATR/Volume + CSV export.";
                Calculate               = Calculate.OnBarClose;
                IsOverlay               = true;
                DisplayInDataBox        = false;
                PaintPriceMarkers       = false;
                IsSuspendedWhileInactive= true;
            }
            else if (State == State.Configure)
            {
                // Add internal series
                AddDataSeries(BarsPeriodType.Minute, 1);
                AddDataSeries(BarsPeriodType.Day, 1);

                // Convert HHmm -> ToTime int (HHmmss)
                // Example: 935 -> 093500, 1000 -> 100000
                snapshot1Time = Snapshot1HHmm * 100;
                snapshot2Time = Snapshot2HHmm * 100;
            }
            else if (State == State.DataLoaded)
            {
                // Daily metrics (BIP_DAILY)
                atrDaily   = ATR(BarsArray[BIP_DAILY], AtrDays);
                volSmaDaily = SMA(Volumes[BIP_DAILY], VolDays);

                // Intraday metrics (BIP_MIN1)
                if (IncludeAdxStoch)
                {
                    adxMin1   = ADX(BarsArray[BIP_MIN1], 14);
                    stochMin1 = Stochastics(BarsArray[BIP_MIN1], 14, 3, 3);
                }

                // CSV path
                string folder = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, CsvFolder);
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                csvPath = Path.Combine(folder, "GlobexBias_" + Instrument.MasterInstrument.Name + ".csv");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 1 || CurrentBars[BIP_MIN1] < 1 || CurrentBars[BIP_DAILY] < Math.Max(AtrDays, VolDays) + 1)
                return;

            // Update daily metrics
            if (BarsInProgress == BIP_DAILY)
            {
                atrN = atrDaily[0];
                volN = volSmaDaily[0];
                return;
            }

            // Main logic on 1-minute series
            if (BarsInProgress != BIP_MIN1)
                return;

            DateTime t = Times[BIP_MIN1][0];
            DateTime sessionDate = t.Date;

            // Reset once per new date
            if (sessionDate != currentSessionDate)
            {
                currentSessionDate = sessionDate;
                ResetSessionVars();
            }

            int tt = ToTime(t);

            // Capture anchors
            if (tt == 30000)
                ref3amClose = Closes[BIP_MIN1][0];

            if (tt == 83000)
                ref830Close = Closes[BIP_MIN1][0];

            if (tt == 93000)
            {
                ref930Open = Opens[BIP_MIN1][0];

                // Start OR tracking
                orActive = true;
                orStart  = t;
                orEnd    = t.AddMinutes(ORMinutes);

                orHigh = Highs[BIP_MIN1][0];
                orLow  = Lows[BIP_MIN1][0];
            }

            // Track OR High/Low
            if (orActive)
            {
                if (t >= orStart && t < orEnd)
                {
                    orHigh = Math.Max(orHigh, Highs[BIP_MIN1][0]);
                    orLow  = Math.Min(orLow,  Lows[BIP_MIN1][0]);
                }
                else if (t >= orEnd)
                {
                    orActive = false;
                    if (!double.IsNaN(orHigh) && !double.IsNaN(orLow))
                    {
                        orMid  = (orHigh + orLow) / 2.0;
                        orSize = (orHigh - orLow);
                    }
                }
            }

            // Snapshot updates
            if (tt == snapshot1Time || tt == snapshot2Time)
            {
                string bias = ComputeBias(ref3amClose, ref830Close, ref930Open);
                string risk = ComputeRiskHint(orSize, atrN, volN);

                DrawTable(bias, risk, tt);

                // Export once per session at snapshot2 time
                if (ExportCsv && tt == snapshot2Time && !wroteCsvForSession)
                {
                    ExportRowToCsv(bias, risk);
                    wroteCsvForSession = true;
                }
            }
        }

        private void ResetSessionVars()
        {
            ref3amClose = double.NaN;
            ref830Close = double.NaN;
            ref930Open  = double.NaN;

            orHigh = double.NaN;
            orLow  = double.NaN;
            orMid  = double.NaN;
            orSize = double.NaN;

            orActive = false;
            wroteCsvForSession = false;
        }

        private string ComputeBias(double a3, double b830, double c930)
        {
            if (double.IsNaN(a3) || double.IsNaN(b830) || double.IsNaN(c930))
                return "INSUFFICIENT DATA (waiting for 03:00 / 08:30 / 09:30)";

            if (a3 >= c930)
                return "BEARISH (A>=C) | Favor R2–R4 sell setups / fades";

            if (c930 > b830 && b830 > a3)
                return "BULLISH (C>B>A) | Favor B2–B4 buy setups / pullbacks";

            if (b830 > c930 && c930 > a3)
                return "ROTATIONAL (B>C>A) | Expect B2↔R2 rotations / mid-to-mid";

            return "MIXED/TRANSITION | Require A+ confirmation or stand down";
        }

        private string ComputeRiskHint(double orRange, double atr, double volAvg)
        {
            if (double.IsNaN(orRange) || double.IsNaN(atr) || atr <= 0)
                return "Risk: waiting for OR/ATR";

            double orVsAtrPct = (orRange / atr) * 100.0;

            if (orVsAtrPct >= 20.0)
                return $"Risk: HIGH (OR≈{orVsAtrPct:0.0}% of daily ATR) | Reduce size / widen expectations";

            if (orVsAtrPct <= 10.0)
                return $"Risk: LOW (OR≈{orVsAtrPct:0.0}% of daily ATR) | Tight targets, beware chop";

            return $"Risk: NORMAL (OR≈{orVsAtrPct:0.0}% of daily ATR) | Standard sizing";
        }

        private void DrawTable(string bias, string risk, int snapshotTime)
        {
            // Optional intraday metrics
            string adxStr = "n/a";
            string stochStr = "n/a";

            if (IncludeAdxStoch && adxMin1 != null && stochMin1 != null)
            {
                adxStr = adxMin1[0].ToString("0.0", CultureInfo.InvariantCulture);
                stochStr = $"{stochMin1.K[0]:0.0}/{stochMin1.D[0]:0.0}";
            }

            string snapLabel = (snapshotTime == snapshot1Time)
                ? $"Snapshot @ {Snapshot1HHmm:0000}"
                : $"Snapshot @ {Snapshot2HHmm:0000}";

            var sb = new StringBuilder();
            sb.AppendLine($"Globex Bias Table — {Instrument.MasterInstrument.Name} — {currentSessionDate:yyyy-MM-dd}");
            sb.AppendLine(snapLabel);
            sb.AppendLine("");

            sb.AppendLine($"A) 03:00 close: {Fmt(ref3amClose)}");
            sb.AppendLine($"B) 08:30 close: {Fmt(ref830Close)}");
            sb.AppendLine($"C) 09:30 open : {Fmt(ref930Open)}");
            sb.AppendLine("");

            sb.AppendLine($"OR ({ORMinutes}m) High: {Fmt(orHigh)}  Low: {Fmt(orLow)}  Mid: {Fmt(orMid)}  Size: {Fmt(orSize)}");
            sb.AppendLine("");

            sb.AppendLine($"Bias: {bias}");
            sb.AppendLine(risk);
            sb.AppendLine("");

            sb.AppendLine($"ATR({AtrDays}) daily: {Fmt(atrN)} | VolAvg({VolDays}) daily: {Fmt(volN)}");
            if (IncludeAdxStoch)
                sb.AppendLine($"ADX(14) 1m: {adxStr} | Stoch(14,3,3) 1m: {stochStr}");

            // Draw fixed text table
            Draw.TextFixed(this, "GB_TABLE", sb.ToString(), TextPosition.TopLeft);
        }

        private void ExportRowToCsv(string bias, string risk)
        {
            bool fileExists = File.Exists(csvPath);

            using (var sw = new StreamWriter(csvPath, true))
            {
                if (!fileExists)
                {
                    sw.WriteLine("Date,Instrument,Snapshot2HHmm,Ref_03_00,Ref_08_30,Open_09_30,OR_High,OR_Low,OR_Mid,OR_Size,Bias,RiskHint,ATR_Daily,VolAvg_Daily,ADX_1m,StochK_1m,StochD_1m");
                }

                string adx = "";
                string k = "";
                string d = "";
                if (IncludeAdxStoch && adxMin1 != null && stochMin1 != null)
                {
                    adx = adxMin1[0].ToString("0.0", CultureInfo.InvariantCulture);
                    k   = stochMin1.K[0].ToString("0.0", CultureInfo.InvariantCulture);
                    d   = stochMin1.D[0].ToString("0.0", CultureInfo.InvariantCulture);
                }

                sw.WriteLine(
                    $"{currentSessionDate:yyyy-MM-dd}," +
                    $"{Instrument.MasterInstrument.Name}," +
                    $"{Snapshot2HHmm:0000}," +
                    $"{FmtRaw(ref3amClose)}," +
                    $"{FmtRaw(ref830Close)}," +
                    $"{FmtRaw(ref930Open)}," +
                    $"{FmtRaw(orHigh)}," +
                    $"{FmtRaw(orLow)}," +
                    $"{FmtRaw(orMid)}," +
                    $"{FmtRaw(orSize)}," +
                    $"{EscapeCsv(bias)}," +
                    $"{EscapeCsv(risk)}," +
                    $"{FmtRaw(atrN)}," +
                    $"{FmtRaw(volN)}," +
                    $"{adx}," +
                    $"{k}," +
                    $"{d}"
                );
            }
        }

        private string EscapeCsv(string s)
        {
            if (s == null) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }

        private string Fmt(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "—";
            return v.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private string FmtRaw(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "";
            return v.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GlobexBiasTableExporter[] cacheGlobexBiasTableExporter;
		public GlobexBiasTableExporter GlobexBiasTableExporter(int snapshot1HHmm, int snapshot2HHmm, int oRMinutes, bool exportCsv, string csvFolder, bool includeAdxStoch, int atrDays, int volDays)
		{
			return GlobexBiasTableExporter(Input, snapshot1HHmm, snapshot2HHmm, oRMinutes, exportCsv, csvFolder, includeAdxStoch, atrDays, volDays);
		}

		public GlobexBiasTableExporter GlobexBiasTableExporter(ISeries<double> input, int snapshot1HHmm, int snapshot2HHmm, int oRMinutes, bool exportCsv, string csvFolder, bool includeAdxStoch, int atrDays, int volDays)
		{
			if (cacheGlobexBiasTableExporter != null)
				for (int idx = 0; idx < cacheGlobexBiasTableExporter.Length; idx++)
					if (cacheGlobexBiasTableExporter[idx] != null && cacheGlobexBiasTableExporter[idx].Snapshot1HHmm == snapshot1HHmm && cacheGlobexBiasTableExporter[idx].Snapshot2HHmm == snapshot2HHmm && cacheGlobexBiasTableExporter[idx].ORMinutes == oRMinutes && cacheGlobexBiasTableExporter[idx].ExportCsv == exportCsv && cacheGlobexBiasTableExporter[idx].CsvFolder == csvFolder && cacheGlobexBiasTableExporter[idx].IncludeAdxStoch == includeAdxStoch && cacheGlobexBiasTableExporter[idx].AtrDays == atrDays && cacheGlobexBiasTableExporter[idx].VolDays == volDays && cacheGlobexBiasTableExporter[idx].EqualsInput(input))
						return cacheGlobexBiasTableExporter[idx];
			return CacheIndicator<GlobexBiasTableExporter>(new GlobexBiasTableExporter(){ Snapshot1HHmm = snapshot1HHmm, Snapshot2HHmm = snapshot2HHmm, ORMinutes = oRMinutes, ExportCsv = exportCsv, CsvFolder = csvFolder, IncludeAdxStoch = includeAdxStoch, AtrDays = atrDays, VolDays = volDays }, input, ref cacheGlobexBiasTableExporter);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GlobexBiasTableExporter GlobexBiasTableExporter(int snapshot1HHmm, int snapshot2HHmm, int oRMinutes, bool exportCsv, string csvFolder, bool includeAdxStoch, int atrDays, int volDays)
		{
			return indicator.GlobexBiasTableExporter(Input, snapshot1HHmm, snapshot2HHmm, oRMinutes, exportCsv, csvFolder, includeAdxStoch, atrDays, volDays);
		}

		public Indicators.GlobexBiasTableExporter GlobexBiasTableExporter(ISeries<double> input , int snapshot1HHmm, int snapshot2HHmm, int oRMinutes, bool exportCsv, string csvFolder, bool includeAdxStoch, int atrDays, int volDays)
		{
			return indicator.GlobexBiasTableExporter(input, snapshot1HHmm, snapshot2HHmm, oRMinutes, exportCsv, csvFolder, includeAdxStoch, atrDays, volDays);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GlobexBiasTableExporter GlobexBiasTableExporter(int snapshot1HHmm, int snapshot2HHmm, int oRMinutes, bool exportCsv, string csvFolder, bool includeAdxStoch, int atrDays, int volDays)
		{
			return indicator.GlobexBiasTableExporter(Input, snapshot1HHmm, snapshot2HHmm, oRMinutes, exportCsv, csvFolder, includeAdxStoch, atrDays, volDays);
		}

		public Indicators.GlobexBiasTableExporter GlobexBiasTableExporter(ISeries<double> input , int snapshot1HHmm, int snapshot2HHmm, int oRMinutes, bool exportCsv, string csvFolder, bool includeAdxStoch, int atrDays, int volDays)
		{
			return indicator.GlobexBiasTableExporter(input, snapshot1HHmm, snapshot2HHmm, oRMinutes, exportCsv, csvFolder, includeAdxStoch, atrDays, volDays);
		}
	}
}

#endregion
