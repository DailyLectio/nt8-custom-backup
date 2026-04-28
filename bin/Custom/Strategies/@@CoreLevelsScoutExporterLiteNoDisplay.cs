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
// CoreLevelsScoutExporterLite
// - Re-instantiates CoreLevels at the start of EACH trading day
// - Writes two rows per session: 09:31 and 10:31 (chart timezone)
// - Works historically and live, no charts/graphics required
// -------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Strategies
{
    public class CoreLevelsScoutExporterLite : Strategy
    {
        // --- vendor indicator instance (created per day) ---
        private object core; // keep as object (vendor sometimes uses internal visibility)
        private Type coreType;
        private SessionIterator sess;
        private DateTime currentDay = Core.Globals.MinDate;
        private HashSet<string> locksWrittenForDay;   // e.g., "093100" and/or "103100"
        private string outPath;

        // ---------- user inputs ----------
        [NinjaScriptProperty]
        [Display(Name = "Lock #1 HMS (HHmmss)", Order = 0, GroupName = "Export")]
        public int LockHms1 { get; set; } =  93100;  // 09:31:00

        [NinjaScriptProperty]
        [Display(Name = "Lock #2 HMS (HHmmss)", Order = 1, GroupName = "Export")]
        public int LockHms2 { get; set; } = 103100;  // 10:31:00

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
                Calculate = Calculate.OnBarClose;     // safe for daily locks
                IsUnmanaged = false;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.Configure)
            {
                // nothing here
            }
            else if (State == State.DataLoaded)
            {
                sess = new SessionIterator(Bars);
                locksWrittenForDay = new HashSet<string>();
                outPath = Path.Combine(Core.Globals.UserDataDir, FileName);

                // ensure header
                if (WriteCsv && !File.Exists(outPath))
                {
                    using (var sw = new StreamWriter(outPath, false))
                    {
                        sw.WriteLine(string.Join(",",
                            new[]{
                                "Date","Instrument",
                                "POC",
                                "HiMid1HotZone","HiMid1","HiMid2","ExpectedHighHotZone","ExpectedHigh","ExtendedHigh",
                                "LoMid1HotZone","LoMid1","LoMid2","ExpectedLowHotZone","ExpectedLow","ExtendedLow",
                                "ExtremeHigh","ExtremeLow","Session1","Session2"
                            }));
                    }
                }

                // resolve vendor indicator type once
                coreType = Type.GetType("NinjaTrader.NinjaScript.Indicators.CoreLevels");
                if (coreType == null)
                    Print("[CoreLevelsScoutExporterLite] Could not locate Indicators.CoreLevels type. Make sure the indicator is installed & visible.");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2 || coreType == null) return;

            // detect trading day changes
            var day = sess.GetTradingDay(Times[0][0]);
            if (day != currentDay)
            {
                // new session -> reset locks & create a fresh indicator instance
                currentDay = day;
                locksWrittenForDay.Clear();
                RecreateCoreForNewDay();
            }

            // try to export at each lock crossing
            TryLockExport(LockHms1);
            TryLockExport(LockHms2);
        }

        private void RecreateCoreForNewDay()
        {
            try
            {
                // Dispose old instance if it exposes IDisposable (play nice)
                var disp = core as IDisposable;
                if (disp != null) { try { disp.Dispose(); } catch { } }

                // Recreate a fresh instance for THIS session.
                // Using Activator keeps us independent of constructor signature.
                core = Activator.CreateInstance(coreType);

                // Attach to the chart so NT8 drives its calculation path reliably.
                // If you hate labels on the chart, set the indicator's label/font off in its params,
                // or comment the next line (it will still calculate when referenced).
                AddChartIndicator((Indicator)core);

                // allow a couple of bars to flow before we read values (historical will have them)
                // nothing else required here: NT binds Input automatically for Indicators created this way
            }
            catch (Exception ex)
            {
                Print("[CoreLevelsScoutExporterLite] Failed to create CoreLevels for new day: " + ex.Message);
            }
        }

        private void TryLockExport(int lockHms)
        {
            if (!WriteCsv || core == null) return;

            DateTime prev = Times[0][1];
            DateTime curr = Times[0][0];
            int p = ToHms(prev);
            int c = ToHms(curr);

            if (!(p < lockHms && c >= lockHms)) return;   // first bar that crosses the lock time

            string key = lockHms.ToString(CultureInfo.InvariantCulture);
            if (locksWrittenForDay.Contains(key)) return;  // already wrote this lock for the day

            // read from CoreLevels *after* a fresh per-day instance has been allowed to calculate
            var rec = ReadCoreSnapshot(core);
            if (rec == null) return;

            // write row
            var row = new List<string>
            {
                currentDay.ToString("yyyy-MM-dd"),
                Instrument.FullName,
                F(rec.POC),
                F(rec.HiMid1HotZone), F(rec.HiMid1), F(rec.HiMid2), F(rec.ExpectedHighHotZone), F(rec.ExpectedHigh), F(rec.ExtendedHigh),
                F(rec.LoMid1HotZone), F(rec.LoMid1), F(rec.LoMid2), F(rec.ExpectedLowHotZone),  F(rec.ExpectedLow),  F(rec.ExtendedLow),
                F(rec.ExtremeHigh), F(rec.ExtremeLow), rec.Session1.ToString(), rec.Session2.ToString()
            };

            try
            {
                using (var sw = new StreamWriter(outPath, true))
                    sw.WriteLine(string.Join(",", row));
                locksWrittenForDay.Add(key);
                Print($"[CoreLevelsScoutExporterLite] {Instrument.FullName} {currentDay:yyyy-MM-dd} {key} -> {outPath}");
            }
            catch (Exception ex)
            {
                Print("[CoreLevelsScoutExporterLite] Write failed: " + ex.Message);
            }
        }

        // --- snapshot reader (safe, tolerant of vendor hiding) ---
        private CoreSnapshot ReadCoreSnapshot(object obj)
        {
            try
            {
                return new CoreSnapshot
                {
                    // bulls
                    ExpectedHigh        = GetDoubleProp(obj, "ExpectedHigh"),
                    ExpectedHighHotZone = GetDoubleProp(obj, "ExpectedHighHotZone"),
                    ExtendedHigh        = GetDoubleProp(obj, "ExtendedHigh"),
                    HiMid1              = GetDoubleProp(obj, "HiMid1"),
                    HiMid1HotZone       = GetDoubleProp(obj, "HiMid1HotZone"),
                    HiMid2              = GetDoubleProp(obj, "HiMid2"),
                    // bears
                    ExpectedLow         = GetDoubleProp(obj, "ExpectedLow"),
                    ExpectedLowHotZone  = GetDoubleProp(obj, "ExpectedLowHotZone"),
                    ExtendedLow         = GetDoubleProp(obj, "ExtendedLow"),
                    LoMid1              = GetDoubleProp(obj, "LoMid1"),
                    LoMid1HotZone       = GetDoubleProp(obj, "LoMid1HotZone"),
                    LoMid2              = GetDoubleProp(obj, "LoMid2"),
                    // extremes & extras
                    ExtremeHigh         = GetDoubleProp(obj, "ExtremeHigh"),
                    ExtremeLow          = GetDoubleProp(obj, "ExtremeLow"),
                    POC                 = GetDoubleProp(obj, "POC"),
                    Session1            = (int)Math.Round(GetDoubleProp(obj, "Session1")),
                    Session2            = (int)Math.Round(GetDoubleProp(obj, "Session2")),
                };
            }
            catch
            {
                return null;
            }
        }

        // reflection helpers (no hard compile dependency)
        private static double GetDoubleProp(object o, string name)
        {
            try
            {
                var p = o.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (p == null) return double.NaN;
                var v = p.GetValue(o);
                if (v is double d) return d;
                if (v is float  f) return (double)f;
                if (v is decimal m) return (double)m;
                if (v is int i) return i;
                if (v is long l) return l;
                // try change type
                return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            }
            catch { return double.NaN; }
        }

        private static string F(double v) => double.IsNaN(v) ? "" : v.ToString(CultureInfo.InvariantCulture);
        private static int ToHms(DateTime t) => t.Hour * 10000 + t.Minute * 100 + t.Second;

        private class CoreSnapshot
        {
            public double POC;
            public double HiMid1HotZone, HiMid1, HiMid2, ExpectedHighHotZone, ExpectedHigh, ExtendedHigh;
            public double LoMid1HotZone, LoMid1, LoMid2, ExpectedLowHotZone,  ExpectedLow,  ExtendedLow;
            public double ExtremeHigh, ExtremeLow;
            public int    Session1, Session2;
        }
    }
}