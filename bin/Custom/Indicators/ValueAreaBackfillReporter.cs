// ValueAreaBackfillReporter.cs
// Historical ES/NQ value-area seed builder for NT8 charts.
//
// Runs on a loaded intraday chart, preferably 1-minute ETH, and appends missing
// daily rows to:
//   C:\Users\Valued Customer\NT8_Regimes\Exports\ValueArea_ES.csv
//   C:\Users\Valued Customer\NT8_Regimes\Exports\ValueArea_NQ.csv
//
// Output format:
//   Date,Symbol,POC,VAH,VAL,DailyVolume,ONHigh,ONLow

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class ValueAreaBackfillReporter : Indicator
    {
        private const string HEADER = "Date,Symbol,POC,VAH,VAL,DailyVolume,ONHigh,ONLow";

        private readonly Dictionary<DateTime, SessionProfile> sessions = new Dictionary<DateTime, SessionProfile>();
        private readonly HashSet<DateTime> existingDates = new HashSet<DateTime>();

        private string exportPath = "";
        private string rootSymbol = "";
        private int rowsWritten = 0;

        [NinjaScriptProperty]
        [Display(Name = "Export Folder", GroupName = "Value Area Backfill", Order = 0)]
        public string ExportFolder { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\Exports";

        [NinjaScriptProperty]
        [Range(50, 95)]
        [Display(Name = "Value Area Percent", GroupName = "Value Area Backfill", Order = 1)]
        public int ValueAreaPercent { get; set; } = 70;

        [NinjaScriptProperty]
        [Range(1, 390)]
        [Display(Name = "Minimum RTH Bars", GroupName = "Value Area Backfill", Order = 2)]
        public int MinimumRthBars { get; set; } = 120;

        [NinjaScriptProperty]
        [Display(Name = "Include Current Partial Day", GroupName = "Value Area Backfill", Order = 3)]
        public bool IncludeCurrentPartialDay { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Debug Prints", GroupName = "Value Area Backfill", Order = 4)]
        public bool DebugPrints { get; set; } = true;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Backfills ES/NQ ValueArea CSV rows from loaded intraday NT8 chart history.";
                Name = "Value Area Backfill Reporter";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = false;
            }
            else if (State == State.DataLoaded)
            {
                rootSymbol = ResolveRootSymbol();
                if (string.IsNullOrEmpty(rootSymbol))
                {
                    Print("ValueAreaBackfillReporter: chart symbol is not ES/NQ. No export will be written.");
                    return;
                }

                exportPath = Path.Combine(ExportFolder, "ValueArea_" + rootSymbol + ".csv");
                EnsureHeader();
                LoadExistingDates();

                if (DebugPrints)
                    Print("ValueAreaBackfillReporter [" + rootSymbol + "]: harvesting loaded chart history into " + exportPath);
            }
            else if (State == State.Transition)
            {
                FlushCompletedSessions();
                if (DebugPrints)
                    Print("ValueAreaBackfillReporter [" + rootSymbol + "]: historical pass complete. Rows written: " + rowsWritten);
            }
            else if (State == State.Terminated)
            {
                FlushCompletedSessions();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || string.IsNullOrEmpty(exportPath))
                return;

            DateTime barTime = Time[0];
            TimeSpan tod = barTime.TimeOfDay;

            bool isRth = tod >= new TimeSpan(9, 30, 0) && tod <= new TimeSpan(16, 0, 0);
            bool isOvernight = tod >= new TimeSpan(18, 0, 0) || tod < new TimeSpan(9, 30, 0);

            if (!isRth && !isOvernight)
                return;

            DateTime sessionDate = tod >= new TimeSpan(18, 0, 0)
                ? barTime.Date.AddDays(1)
                : barTime.Date;

            SessionProfile profile;
            if (!sessions.TryGetValue(sessionDate, out profile))
            {
                profile = new SessionProfile(sessionDate);
                sessions[sessionDate] = profile;
            }

            if (isRth)
                profile.AddRthBar(High[0], Low[0], Close[0], Volume[0], TickSize);
            else
                profile.AddOvernightBar(High[0], Low[0]);

            if (State == State.Realtime)
                FlushCompletedSessions();
        }

        private void FlushCompletedSessions()
        {
            if (string.IsNullOrEmpty(exportPath))
                return;

            List<DateTime> dates = sessions.Keys.OrderBy(d => d).ToList();
            foreach (DateTime date in dates)
            {
                if (!IncludeCurrentPartialDay && date >= DateTime.Now.Date)
                    continue;
                if (existingDates.Contains(date))
                    continue;

                SessionProfile profile = sessions[date];
                if (profile.RthBars < MinimumRthBars || profile.TotalRthVolume <= 0)
                    continue;

                ValueAreaResult va = profile.CalculateValueArea(ValueAreaPercent / 100.0, TickSize);
                double onHigh = profile.HasOvernight ? profile.OvernightHigh : profile.RthHigh;
                double onLow = profile.HasOvernight ? profile.OvernightLow : profile.RthLow;

                string line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6},{7}",
                    date.ToString("yyyy-MM-dd"),
                    rootSymbol,
                    FormatPrice(va.Poc),
                    FormatPrice(va.Vah),
                    FormatPrice(va.Val),
                    Math.Round(profile.TotalRthVolume).ToString("F0", CultureInfo.InvariantCulture),
                    FormatPrice(onHigh),
                    FormatPrice(onLow));

                using (StreamWriter sw = new StreamWriter(exportPath, true))
                    sw.WriteLine(line);

                existingDates.Add(date);
                rowsWritten++;

                if (DebugPrints)
                    Print("ValueAreaBackfillReporter [" + rootSymbol + "]: wrote " + line);
            }
        }

        private string ResolveRootSymbol()
        {
            string sym = Instrument.MasterInstrument.Name.ToUpperInvariant();
            if (sym.Contains("NQ"))
                return "NQ";
            if (sym.Contains("ES"))
                return "ES";
            return "";
        }

        private string FormatPrice(double price)
        {
            double rounded = Math.Round(price / TickSize) * TickSize;
            return rounded.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void EnsureHeader()
        {
            Directory.CreateDirectory(ExportFolder);
            if (!File.Exists(exportPath))
            {
                using (StreamWriter sw = new StreamWriter(exportPath, false))
                    sw.WriteLine(HEADER);
                return;
            }

            string firstLine = "";
            using (StreamReader sr = new StreamReader(exportPath))
                firstLine = sr.ReadLine() ?? "";

            if (!firstLine.StartsWith("Date,", StringComparison.OrdinalIgnoreCase))
            {
                string original = File.ReadAllText(exportPath);
                File.WriteAllText(exportPath, HEADER + Environment.NewLine + original);
            }
        }

        private void LoadExistingDates()
        {
            existingDates.Clear();
            if (!File.Exists(exportPath))
                return;

            using (StreamReader sr = new StreamReader(exportPath))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Date,", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string[] parts = line.Split(',');
                    DateTime date;
                    if (parts.Length > 0 && DateTime.TryParse(parts[0], out date))
                        existingDates.Add(date.Date);
                }
            }
        }

        private class SessionProfile
        {
            private readonly Dictionary<double, double> volumeByPrice = new Dictionary<double, double>();

            public DateTime Date { get; private set; }
            public int RthBars { get; private set; }
            public double TotalRthVolume { get; private set; }
            public double RthHigh { get; private set; }
            public double RthLow { get; private set; }
            public double OvernightHigh { get; private set; }
            public double OvernightLow { get; private set; }
            public bool HasOvernight { get; private set; }

            public SessionProfile(DateTime date)
            {
                Date = date;
                RthHigh = double.MinValue;
                RthLow = double.MaxValue;
                OvernightHigh = double.MinValue;
                OvernightLow = double.MaxValue;
            }

            public void AddRthBar(double high, double low, double close, double volume, double tickSize)
            {
                RthBars++;
                TotalRthVolume += volume;
                RthHigh = Math.Max(RthHigh, high);
                RthLow = Math.Min(RthLow, low);

                double start = RoundToTick(low, tickSize);
                double end = RoundToTick(high, tickSize);
                if (end < start)
                {
                    double tmp = start;
                    start = end;
                    end = tmp;
                }

                int steps = Math.Max(1, (int)Math.Round((end - start) / tickSize) + 1);
                double volPerPrice = volume / steps;
                for (int i = 0; i < steps; i++)
                {
                    double price = RoundToTick(start + i * tickSize, tickSize);
                    if (!volumeByPrice.ContainsKey(price))
                        volumeByPrice[price] = 0;
                    volumeByPrice[price] += volPerPrice;
                }
            }

            public void AddOvernightBar(double high, double low)
            {
                HasOvernight = true;
                OvernightHigh = Math.Max(OvernightHigh, high);
                OvernightLow = Math.Min(OvernightLow, low);
            }

            public ValueAreaResult CalculateValueArea(double valueAreaPct, double tickSize)
            {
                if (volumeByPrice.Count == 0)
                    return new ValueAreaResult { Poc = 0, Vah = 0, Val = 0 };

                List<double> prices = volumeByPrice.Keys.OrderBy(p => p).ToList();
                int pocIndex = 0;
                for (int i = 1; i < prices.Count; i++)
                    if (volumeByPrice[prices[i]] > volumeByPrice[prices[pocIndex]])
                        pocIndex = i;

                double targetVolume = TotalRthVolume * valueAreaPct;
                double accumulated = volumeByPrice[prices[pocIndex]];
                int lower = pocIndex;
                int upper = pocIndex;

                while (accumulated < targetVolume && (lower > 0 || upper < prices.Count - 1))
                {
                    double lowerVol = lower > 0 ? volumeByPrice[prices[lower - 1]] : -1;
                    double upperVol = upper < prices.Count - 1 ? volumeByPrice[prices[upper + 1]] : -1;

                    if (upperVol >= lowerVol)
                    {
                        upper++;
                        accumulated += volumeByPrice[prices[upper]];
                    }
                    else
                    {
                        lower--;
                        accumulated += volumeByPrice[prices[lower]];
                    }
                }

                return new ValueAreaResult
                {
                    Poc = prices[pocIndex],
                    Vah = prices[upper],
                    Val = prices[lower]
                };
            }

            private static double RoundToTick(double price, double tickSize)
            {
                return Math.Round(price / tickSize) * tickSize;
            }
        }

        private class ValueAreaResult
        {
            public double Poc { get; set; }
            public double Vah { get; set; }
            public double Val { get; set; }
        }
    }
}
