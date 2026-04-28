#region Using
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

// CoreLevelsExporter_Lite – two snapshots/day (09:31, 10:31)
// No vendor dependencies. Build your Core-like ladder from price.
// Columns include: B1..B6, R1..R6, EH/EL, XH/XL, Mean, ADR, POC_Proxy.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class CoreLevelsExporter_Lite2 : Strategy
    {
        private SessionIterator si;
        private string outPath;
        private HashSet<int> lockTimes;
        private HashSet<string> writtenKeys = new HashSet<string>(); // Date+TimeLabel de-dupe

        [NinjaScriptProperty] [Display(Name="CSV File Name", Order=0)]
        public string FileName { get; set; } = "CoreLevels_LITE_ES.csv";

        [NinjaScriptProperty] [Display(Name="ADR Lookback (days)", Order=1)]
        public int AdrLookback { get; set; } = 10;

        [NinjaScriptProperty] [Display(Name="Expected Multiplier (K)", Order=2)]
        public double KExpected { get; set; } = 0.50;

        // Define the ladder in ADR fractions (upper for B1..B6, lower for R1..R6)
        // By default, B4/R4 will align to KExpected (0.50). Adjust steps if your canonical spacing differs.
        [NinjaScriptProperty] [Display(Name="Upper Steps (ADR fractions)", Order=3)]
        public string UpperStepsCsv { get; set; } = "0.15,0.25,0.35,0.50,0.65,0.85";

        [NinjaScriptProperty] [Display(Name="Lower Steps (ADR fractions)", Order=4)]
        public string LowerStepsCsv { get; set; } = "0.15,0.25,0.35,0.50,0.65,0.85";

        [NinjaScriptProperty] [Display(Name="Extended Multiplier (×KExpected)", Order=5)]
        public double ExtendedFactor { get; set; } = 2.0; // XH/XL = Mean ± (ExtendedFactor * KExpected * ADR)

        [NinjaScriptProperty] [Display(Name="Snapshot Times (HHmmss; comma)", Order=6)]
        public string SnapshotTimes { get; set; } = "93100,103100";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "CoreLevelsExporter_Lite2";
                Calculate = Calculate.OnBarClose;
            }
            else if (State == State.DataLoaded)
            {
                si = new SessionIterator(Bars);

                outPath = Path.Combine(Core.Globals.UserDataDir, FileName);
                if (!File.Exists(outPath))
                {
                    using (var sw = new StreamWriter(outPath, false))
                    {
                        var cols = new List<string>{
                            "Date","TimeLabel","Instrument",
                            "H1","L1","Mean","ADR",
                            "ExpectedHigh","ExpectedLow","ExtendedHigh","ExtendedLow",
                            "B1","B2","B3","B4","B5","B6",
                            "R1","R2","R3","R4","R5","R6",
                            "POC_Proxy"
                        };
                        sw.WriteLine(string.Join(",", cols));
                    }
                }

                lockTimes = new HashSet<int>(SnapshotTimes.Split(',').Select(s => int.Parse(s.Trim())));
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2) return;

            var day = si.GetTradingDay(Times[0][0]);
            DateTime prev = Times[0][1];
            DateTime curr = Times[0][0];

            int prevHms = prev.Hour*10000 + prev.Minute*100 + prev.Second;
            int currHms = curr.Hour*10000 + curr.Minute*100 + curr.Second;

            foreach (var lockHms in lockTimes)
            {
                if (!(prevHms < lockHms && currHms >= lockHms)) continue;

                string label = (lockHms == 93100 ? "0931" : (lockHms == 103100 ? "1031" : lockHms.ToString()));
                string key = day.ToString("yyyy-MM-dd") + "|" + label;
                if (writtenKeys.Contains(key)) continue;

                // First hour window (09:30–10:30)
                DateTime fhStart = new DateTime(day.Year, day.Month, day.Day, 9, 30, 0, curr.Kind);
                DateTime fhEnd   = new DateTime(day.Year, day.Month, day.Day, 10,30, 0, curr.Kind);

                double H1 = double.MinValue, L1 = double.MaxValue;
                for (int i = 0; i <= CurrentBar; i++)
                {
                    DateTime t = Times[0][i];
                    if (t < fhStart || t > fhEnd) continue;
                    H1 = Math.Max(H1, Highs[0][i]);
                    L1 = Math.Min(L1, Lows[0][i]);
                }
                if (H1 == double.MinValue || L1 == double.MaxValue) return;

                double mean = (H1 + L1) / 2.0;

                // ADR over prior N sessions (RTH only)
                int daysCounted = 0; double adrSum = 0.0;
                DateTime probe = day.AddDays(-1);
                while (daysCounted < AdrLookback)
                {
                    double dH = double.MinValue, dL = double.MaxValue;
                    for (int i = 0; i <= CurrentBar; i++)
                    {
                        var tt = Times[0][i];
                        if (tt.Date != probe.Date) continue;
                        if (tt.TimeOfDay < new TimeSpan(9,30,0) || tt.TimeOfDay > new TimeSpan(16,0,0)) continue;
                        dH = Math.Max(dH, Highs[0][i]);
                        dL = Math.Min(dL, Lows[0][i]);
                    }
                    if (dH != double.MinValue && dL != double.MaxValue)
                    {
                        adrSum += (dH - dL);
                        daysCounted++;
                    }
                    probe = probe.AddDays(-1);
                }
                double ADR = (daysCounted > 0 ? adrSum / daysCounted : 0.0);
                if (ADR <= 0) return;

                // Build ladders
                var upSteps = ParseSteps(UpperStepsCsv);
                var dnSteps = ParseSteps(LowerStepsCsv);

                double EH = mean + KExpected * ADR; // “B4”
                double EL = mean - KExpected * ADR; // “R4”
                double XH = mean + (ExtendedFactor * KExpected) * ADR;
                double XL = mean - (ExtendedFactor * KExpected) * ADR;

                var B = new double[6];
                var R = new double[6];
                for (int i = 0; i < 6; i++)
                {
                    B[i] = mean + upSteps[i] * ADR;  // B1..B6
                    R[i] = mean - dnSteps[i] * ADR;  // R1..R6
                }

                // POC proxy – use Mean (first-hour midpoint) as a stable placeholder
                double pocProxy = mean;

                using (var sw = new StreamWriter(outPath, true))
                {
                    sw.WriteLine(string.Join(",",
                        day.ToString("yyyy-MM-dd"), label, Instrument.FullName,
                        F(H1), F(L1), F(mean), F(ADR),
                        F(EH), F(EL), F(XH), F(XL),
                        F(B[0]),F(B[1]),F(B[2]),F(B[3]),F(B[4]),F(B[5]),
                        F(R[0]),F(R[1]),F(R[2]),F(R[3]),F(R[4]),F(R[5]),
                        F(pocProxy)
                    ));
                }
                Print($"[CoreLite] Wrote {Instrument.FullName} {day:yyyy-MM-dd} @{label} -> {outPath}");
                writtenKeys.Add(key);
            }
        }

        private static string F(double v) => v.ToString(CultureInfo.InvariantCulture);

        private static double[] ParseSteps(string csv)
        {
            // Expect 6 numbers
            var arr = csv.Split(',').Select(s => double.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
            if (arr.Length != 6) throw new ArgumentException("Steps CSV must have 6 values");
            return arr;
        }
    }
}