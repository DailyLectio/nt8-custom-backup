#region Using
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// -------------------------------------------------------------
// CoreLevelsBridge_BWT
// - Directly creates/attaches the vendor indicator: CoreLevels
// - Reads level PROPERTIES (not plots) that we discovered
// - Writes one CSV row per session at first bar crossing HHmmss
//   default 10:30:00 ET (use chart's timezone)
// - Works on historical (immediately) and live
// -------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Strategies
{
    public class CoreLevelsBridge_BWT : Strategy
    {
        private CoreLevels core;                 // NOTE: relies on 'CoreLevels' type being public
        private SessionIterator sessionIterator;
        private DateTime lastWritten = Core.Globals.MinDate;
        private string outPath;

        [NinjaScriptProperty]
        [Display(Name = "Lock HMS (HHmmss, chart tz)", Order = 0)]
        public int LockHmsValue { get; set; } = 103000; // 10:30:00

        [NinjaScriptProperty]
        [Display(Name = "Write CSV", Order = 1)]
        public bool WriteCsv { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "CSV File Name", Order = 2)]
        public string FileName { get; set; } = "CoreLevels_ES.csv";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "CoreLevelsBridge_BWT";
                Calculate = Calculate.OnBarClose;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.DataLoaded)
            {
                sessionIterator = new SessionIterator(Bars);

                // 1) Create and attach the vendor indicator by type name
                try
                {
                    // If this line errors in your editor, change the RHS to:
                    // var obj = (Indicator) Activator.CreateInstance(Type.GetType("NinjaTrader.NinjaScript.Indicators.CoreLevels"));
                    // and then cast later. On your machine, CoreLevels appears public, so direct works:
                    core = (CoreLevels) Activator.CreateInstance(typeof(CoreLevels));
                }
                catch (Exception ex)
                {
                    Print("[CoreLevelsBridge_BWT] Failed to create CoreLevels: " + ex.Message);
                    return;
                }

                AddChartIndicator(core);
                Print("[CoreLevelsBridge_BWT] Attached: " + core.GetType().FullName);

                // 2) Prepare CSV header
                outPath = Path.Combine(Core.Globals.UserDataDir, FileName);
                if (WriteCsv && !File.Exists(outPath))
                {
                    using (var sw = new StreamWriter(outPath, false))
                    {
                        sw.WriteLine(string.Join(",",
                            new[]
                            {
                                "Date","Instrument",
                                "POC",
                                "ExpectedHigh","ExpectedHighHotZone",
                                "ExpectedLow","ExpectedLowHotZone",
                                "HiMid1","HiMid1HotZone","HiMid2","HiMid3",
                                "LoMid1","LoMid1HotZone","LoMid2","LoMid3",
                                "ExtendedHigh","ExtendedLow",
                                "ExtremeHigh","ExtremeLow"
                            }));
                    }
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (core == null || CurrentBar < 2) return;
            if (!WriteCsv) return;

            // One row per session at first bar crossing the lock time
            var day = sessionIterator.GetTradingDay(Times[0][0]);
            if (day == lastWritten) return;

            DateTime prev = Times[0][1];
            DateTime curr = Times[0][0];
            int prevHms = prev.Hour * 10000 + prev.Minute * 100 + prev.Second;
            int currHms = curr.Hour * 10000 + curr.Minute * 100 + curr.Second;
            int lockHms = LockHmsValue;

            if (!(prevHms < lockHms && currHms >= lockHms)) return;

            // Read properties safely
            double POC                = Safe(core, () => core.POC);
            double ExpectedHigh       = Safe(core, () => core.ExpectedHigh);
            double ExpectedHighHZ     = Safe(core, () => core.ExpectedHighHotZone);
            double ExpectedLow        = Safe(core, () => core.ExpectedLow);
            double ExpectedLowHZ      = Safe(core, () => core.ExpectedLowHotZone);
            double HiMid1             = Safe(core, () => core.HiMid1);
            double HiMid1HZ           = Safe(core, () => core.HiMid1HotZone);
            double HiMid2             = Safe(core, () => core.HiMid2);
            double HiMid3             = Safe(core, () => core.HiMid3);
            double LoMid1             = Safe(core, () => core.LoMid1);
            double LoMid1HZ           = Safe(core, () => core.LoMid1HotZone);
            double LoMid2             = Safe(core, () => core.LoMid2);
            double LoMid3             = Safe(core, () => core.LoMid3);
            double ExtendedHigh       = Safe(core, () => core.ExtendedHigh);
            double ExtendedLow        = Safe(core, () => core.ExtendedLow);
            double ExtremeHigh        = Safe(core, () => core.ExtremeHigh);
            double ExtremeLow         = Safe(core, () => core.ExtremeLow);

            var row = new List<string>
            {
                day.ToString("yyyy-MM-dd"),
                Instrument.FullName,
                F(POC),
                F(ExpectedHigh), F(ExpectedHighHZ),
                F(ExpectedLow),  F(ExpectedLowHZ),
                F(HiMid1), F(HiMid1HZ), F(HiMid2), F(HiMid3),
                F(LoMid1), F(LoMid1HZ), F(LoMid2), F(LoMid3),
                F(ExtendedHigh), F(ExtendedLow),
                F(ExtremeHigh),  F(ExtremeLow)
            };

            using (var sw = new StreamWriter(outPath, true))
                sw.WriteLine(string.Join(",", row));

            lastWritten = day;
            Print($"[CoreLevelsBridge_BWT] Wrote {Instrument.FullName} {day:yyyy-MM-dd} -> {outPath}");
        }

        // ---- helpers ----
        private static double Safe(object obj, Func<double> getter)
        {
            try { return getter(); } catch { return double.NaN; }
        }

        private static string F(double v) =>
            double.IsNaN(v) ? "" : v.ToString(CultureInfo.InvariantCulture);
    }
}
