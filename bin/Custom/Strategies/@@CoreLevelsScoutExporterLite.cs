#region Using
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// -------------------------------------------------------------
// CoreLevelsScoutExporterLite
// - Attaches vendor indicator "CoreLevels" by reflection
// - Reads level PROPERTIES (not plots) via reflection
// - Writes CSV at first bar crossing two lock times:
//      09:31:00 (open environment) and 10:31:00 (post-lock)
// - Safe on historical and live. Missing props -> blank.
// - Fully-qualified DisplayAttribute to avoid CS0246.
// -------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Strategies
{
    public class CoreLevelsScoutExporterLite : Strategy
    {
        private Indicator core;                        // keep generic to avoid type visibility issues
        private SessionIterator sessionIterator;
        private string outPath;

        // track per-day writes for each lock
        private DateTime lastSession = Core.Globals.MinDate;
        private HashSet<string> locksWrittenForDay = new HashSet<string>();

        // ---------- User inputs ----------
        [NinjaScriptProperty]
        [Display(Name = "Lock #1 HMS (HHmmss)", Order = 0, GroupName = "Export")]
        public int LockHms1 { get; set; } =  93100;    // 09:31:00

        [NinjaScriptProperty]
        [Display(Name = "Lock #2 HMS (HHmmss)", Order = 1, GroupName = "Export")]
        public int LockHms2 { get; set; } = 103100;    // 10:31:00

        [NinjaScriptProperty]
        [Display(Name = "Write CSV", Order = 2, GroupName = "Export")]
        public bool WriteCsv { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "CSV File Name", Order = 3, GroupName = "Export")]
        public string FileName { get; set; } = "CoreLevels_ES.csv";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "CoreLevelsScoutExporterLite";
                Description = "Exports Core Levels once at 09:31 and 10:31 (chart time) for each session.";
                Calculate = Calculate.OnBarClose;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.DataLoaded)
            {
                sessionIterator = new SessionIterator(Bars);

                // Try to create the CoreLevels indicator via reflection
                try
                {
                    // Most installs expose the type as public:
                    var t = Type.GetType("NinjaTrader.NinjaScript.Indicators.CoreLevels, NinjaTrader.Core");
                    if (t == null)
                        t = Type.GetType("NinjaTrader.NinjaScript.Indicators.CoreLevels"); // fallback

                    if (t == null)
                        throw new Exception("Type 'Indicators.CoreLevels' not found. Ensure the vendor indicator is installed.");

                    core = (Indicator)Activator.CreateInstance(t);
                    AddChartIndicator(core);
                    Print("[CoreLevelsScoutExporterLite] Attached CoreLevels by reflection.");
                }
                catch (Exception ex)
                {
                    Print("[CoreLevelsScoutExporterLite] Failed to create/attach CoreLevels: " + ex.Message);
                }

                // Prepare CSV
                outPath = Path.Combine(Core.Globals.UserDataDir, FileName);
                if (WriteCsv && !File.Exists(outPath))
                {
                    using (var sw = new StreamWriter(outPath, false))
                    {
                        sw.WriteLine(string.Join(",",
                            new[]
                            {
                                "Date","Instrument","LockTime",
                                "POC",
                                // Expected / Extended / Extreme
                                "ExpectedHigh","ExpectedHighHotZone","ExpectedLow","ExpectedLowHotZone",
                                "ExtendedHigh","ExtendedLow","ExtremeHigh","ExtremeLow",
                                // High-side mids
                                "HiMid1","HiMid1HotZone","HiMid2","HiMid3",
                                // Low-side mids
                                "LoMid1","LoMid1HotZone","LoMid2","LoMid3",
                                // B/R ladders if exposed by vendor
                                "B1","B2","B3","B4","B5","B6",
                                "R1","R2","R3","R4","R5","R6"
                            }));
                    }
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (!WriteCsv) return;
            if (CurrentBar < 2) return;

            // If indicator missing, keep going (writes blanks)
            var day = sessionIterator.GetTradingDay(Times[0][0]);
            var prev = Times[0][1];
            var curr = Times[0][0];

            int prevHms = ToHms(prev);
            int currHms = ToHms(curr);

            // New session? reset which locks we’ve written
            if (day != lastSession)
            {
                locksWrittenForDay.Clear();
                lastSession = day;
            }

            // Check both locks
            MaybeWriteAtCross(day, prevHms, currHms, LockHms1);
            MaybeWriteAtCross(day, prevHms, currHms, LockHms2);
        }

        // ---------- helpers ----------
        private void MaybeWriteAtCross(DateTime day, int prevHms, int currHms, int lockHms)
        {
            if (!(prevHms < lockHms && currHms >= lockHms)) return;

            string key = day.ToString("yyyyMMdd") + "_" + lockHms;
            if (locksWrittenForDay.Contains(key)) return;

            var row = BuildRow(day, lockHms);
            try
            {
                using (var sw = new StreamWriter(outPath, true))
                    sw.WriteLine(string.Join(",", row));
                locksWrittenForDay.Add(key);
                Print($"[CoreLevelsScoutExporterLite] Wrote {Instrument.FullName} {day:yyyy-MM-dd} {lockHms:000000} -> {outPath}");
            }
            catch (Exception ex)
            {
                Print("[CoreLevelsScoutExporterLite] CSV write failed: " + ex.Message);
            }
        }

        private List<string> BuildRow(DateTime day, int lockHms)
        {
            // read everything via reflection (safe -> blank if missing)
            string poc                = G("POC");
            string expHi              = G("ExpectedHigh");
            string expHiHZ            = G("ExpectedHighHotZone");
            string expLo              = G("ExpectedLow");
            string expLoHZ            = G("ExpectedLowHotZone");

            string extHi              = G("ExtendedHigh");
            string extLo              = G("ExtendedLow");
            string extremeHi          = G("ExtremeHigh");
            string extremeLo          = G("ExtremeLow");

            string hiMid1             = G("HiMid1");
            string hiMid1HZ           = G("HiMid1HotZone");
            string hiMid2             = G("HiMid2");
            string hiMid3             = G("HiMid3");

            string loMid1             = G("LoMid1");
            string loMid1HZ           = G("LoMid1HotZone");
            string loMid2             = G("LoMid2");
            string loMid3             = G("LoMid3");

            // Optional “official” ladders if vendor exposes them
            string b1 = G("B1"), b2 = G("B2"), b3 = G("B3"), b4 = G("B4"), b5 = G("B5"), b6 = G("B6");
            string r1 = G("R1"), r2 = G("R2"), r3 = G("R3"), r4 = G("R4"), r5 = G("R5"), r6 = G("R6");

            var row = new List<string>
            {
                day.ToString("yyyy-MM-dd"),
                Instrument?.FullName ?? "",
                lockHms.ToString("000000", CultureInfo.InvariantCulture),

                poc,
                expHi, expHiHZ, expLo, expLoHZ,
                extHi, extLo, extremeHi, extremeLo,
                hiMid1, hiMid1HZ, hiMid2, hiMid3,
                loMid1, loMid1HZ, loMid2, loMid3,
                b1, b2, b3, b4, b5, b6,
                r1, r2, r3, r4, r5, r6
            };

            return row;
        }

        // Safe getter via reflection -> string (blank if missing/NaN)
        private string G(string propName)
        {
            try
            {
                if (core == null) return "";
                var p = core.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);
                if (p == null) return "";
                object v = p.GetValue(core);
                if (v == null) return "";
                double d;

                if (v is double dv) d = dv;
                else if (v is float  fv) d = fv;
                else if (v is decimal m) d = (double)m;
                else if (v is int     i) d = i;
                else if (v is long    l) d = l;
                else
                {
                    // If property is itself a DataSeries/Indicator value with .Value
                    var vp = v.GetType().GetProperty("Value");
                    if (vp != null && vp.PropertyType == typeof(double))
                    {
                        var vv = vp.GetValue(v);
                        d = vv is double dd ? dd : double.NaN;
                    }
                    else return "";
                }

                return double.IsNaN(d) ? "" : d.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return "";
            }
        }

        private static int ToHms(DateTime t) => t.Hour * 10000 + t.Minute * 100 + t.Second;
    }
}