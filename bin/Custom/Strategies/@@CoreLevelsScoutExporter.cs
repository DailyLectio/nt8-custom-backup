#region Using
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;

// NT8
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Gui.Tools;           // <-- fixes Display / DisplayAttribute
#endregion

// -------------------------------------------------------------
// CoreLevelsScoutExporter
// - Attaches vendor "CoreLevels" indicator
// - Exports two prints per session at 09:31:00 and 10:31:00 (chart tz)
// - Columns: raw Core props + working B/R mapping + which print
// - Works on historical and live
// -------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Strategies
{
    public class CoreLevelsScoutExporter : Strategy
    {
        private CoreLevels core;                 // requires CoreLevels class to be public
        private SessionIterator sessionIterator;
        private DateTime lastWritten0931 = Core.Globals.MinDate;
        private DateTime lastWritten1031 = Core.Globals.MinDate;
        private string outPath;

        [NinjaScriptProperty]
        [Display(Name = "Write CSV", Order = 0, GroupName = "Parameters")]
        public bool WriteCsv { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "CSV File Name", Order = 1, GroupName = "Parameters")]
        public string FileName { get; set; } = "CoreLevels_ES.csv";

        [NinjaScriptProperty]
        [Display(Name = "Print A (HHmmss)", Order = 2, GroupName = "Parameters")]
        public int LockHmsA { get; set; } = 93100;   // 09:31:00

        [NinjaScriptProperty]
        [Display(Name = "Print B (HHmmss)", Order = 3, GroupName = "Parameters")]
        public int LockHmsB { get; set; } = 103100;  // 10:31:00

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "CoreLevelsScoutExporter";
                Calculate = Calculate.OnBarClose;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.DataLoaded)
            {
                sessionIterator = new SessionIterator(Bars);

                // attach vendor indicator
                try
                {
                    core = (CoreLevels)Activator.CreateInstance(typeof(CoreLevels));
                    AddChartIndicator(core);
                    Print("[CoreLevelsScoutExporter] Attached: " + core.GetType().FullName);
                }
                catch (Exception ex)
                {
                    Print("[CoreLevelsScoutExporter] Failed to create CoreLevels: " + ex.Message);
                    return;
                }

                // prepare CSV header
                outPath = Path.Combine(Core.Globals.UserDataDir, FileName);
                if (WriteCsv && !File.Exists(outPath))
                {
                    using (var sw = new StreamWriter(outPath, false))
                    {
                        sw.WriteLine(string.Join(",", new[]
                        {
                            "Date","Instrument","PrintTime",
                            // raw properties
                            "POC",
                            "ExpectedHigh","ExpectedHighHotZone",
                            "ExpectedLow","ExpectedLowHotZone",
                            "HiMid1","HiMid1HotZone","HiMid2","HiMid3",
                            "LoMid1","LoMid1HotZone","LoMid2","LoMid3",
                            "ExtendedHigh","ExtendedLow",
                            "ExtremeHigh","ExtremeLow",
                            // working B/R mapping (see notes below)
                            "B1","B2","B3","B4","B5","B6",
                            "R1","R2","R3","R4","R5","R6"
                        }));
                    }
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (core == null || !WriteCsv || CurrentBar < 2) return;

            DateTime curr = Times[0][0];
            DateTime prev = Times[0][1];
            int prevHms = prev.Hour * 10000 + prev.Minute * 100 + prev.Second;
            int currHms = curr.Hour * 10000 + curr.Minute * 100 + curr.Second;

            // Determine the trading day (session) of this bar
            var day = sessionIterator.GetTradingDay(curr);

            // Check each lock time crossing once per session
            MaybeWriteAtLock(day, "093100", LockHmsA, ref lastWritten0931, prevHms, currHms);
            MaybeWriteAtLock(day, "103100", LockHmsB, ref lastWritten1031, prevHms, currHms);
        }

        private void MaybeWriteAtLock(DateTime day, string label, int lockHms, ref DateTime lastWrittenForThisLock, int prevHms, int currHms)
        {
            if (day == lastWrittenForThisLock) return;                 // already wrote this session/lock
            if (!(prevHms < lockHms && currHms >= lockHms)) return;    // first bar crossing lock time

            // ---- read Core properties safely ----
            double POC                = Safe(() => core.POC);
            double ExpectedHigh       = Safe(() => core.ExpectedHigh);
            double ExpectedHighHZ     = Safe(() => core.ExpectedHighHotZone);
            double ExpectedLow        = Safe(() => core.ExpectedLow);
            double ExpectedLowHZ      = Safe(() => core.ExpectedLowHotZone);
            double HiMid1             = Safe(() => core.HiMid1);
            double HiMid1HZ           = Safe(() => core.HiMid1HotZone);
            double HiMid2             = Safe(() => core.HiMid2);
            double HiMid3             = Safe(() => core.HiMid3);
            double LoMid1             = Safe(() => core.LoMid1);
            double LoMid1HZ           = Safe(() => core.LoMid1HotZone);
            double LoMid2             = Safe(() => core.LoMid2);
            double LoMid3             = Safe(() => core.LoMid3);
            double ExtendedHigh       = Safe(() => core.ExtendedHigh);
            double ExtendedLow        = Safe(() => core.ExtendedLow);
            double ExtremeHigh        = Safe(() => core.ExtremeHigh);
            double ExtremeLow         = Safe(() => core.ExtremeLow);

            // ---- working B/R mapping (adjustable) ----
            // Assumption:
            //   B2=HiMid1, B3=HiMid2, B4=ExpectedHigh, B5=ExtendedHigh, B6=ExtremeHigh
            //   R2=LoMid1, R3=LoMid2, R4=ExpectedLow,  R5=ExtendedLow,  R6=ExtremeLow
            //   B1 and R1 treated as the session Mean/POC (single value) for export visibility.
            double B1 = POC, R1 = POC;
            double B2 = HiMid1, B3 = HiMid2, B4 = ExpectedHigh, B5 = ExtendedHigh, B6 = ExtremeHigh;
            double R2 = LoMid1, R3 = LoMid2, R4 = ExpectedLow,  R5 = ExtendedLow,  R6 = ExtremeLow;

            var row = new List<string>
            {
                day.ToString("yyyy-MM-dd"),
                Instrument.FullName,
                label,
                F(POC),
                F(ExpectedHigh), F(ExpectedHighHZ),
                F(ExpectedLow),  F(ExpectedLowHZ),
                F(HiMid1), F(HiMid1HZ), F(HiMid2), F(HiMid3),
                F(LoMid1), F(LoMid1HZ), F(LoMid2), F(LoMid3),
                F(ExtendedHigh), F(ExtendedLow),
                F(ExtremeHigh),  F(ExtremeLow),
                F(B1),F(B2),F(B3),F(B4),F(B5),F(B6),
                F(R1),F(R2),F(R3),F(R4),F(R5),F(R6)
            };

            using (var sw = new StreamWriter(outPath, true))
                sw.WriteLine(string.Join(",", row));

            lastWrittenForThisLock = day;
            Print($"[CoreLevelsScoutExporter] Wrote {Instrument.FullName} {day:yyyy-MM-dd} {label} -> {outPath}");
        }

        // ---- helpers ----
        private static double Safe(Func<double> getter)
        {
            try { return getter(); } catch { return double.NaN; }
        }

        private static string F(double v) =>
            double.IsNaN(v) ? "" : v.ToString(CultureInfo.InvariantCulture);
    }
}