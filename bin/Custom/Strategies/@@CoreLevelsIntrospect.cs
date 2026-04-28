#region Using
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// CoreLevelsIntrospect
// - Creates NinjaTrader.NinjaScript.Indicators.CoreLevels
// - Adds it to the chart
// - Reflects public properties/fields and any Series/ISeries<double>
// - Writes names and most-recent values to a text file so we can map ExpectedHigh/Low/POC, etc.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class CoreLevelsIntrospect : Strategy
    {
        private Indicator core;
        private bool wroteHeader;
        private string outPath;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "CoreLevelsIntrospect";
                Calculate = Calculate.OnBarClose;
            }
            else if (State == State.DataLoaded)
            {
                // 1) Try to instantiate the vendor indicator by known name
                var t = Type.GetType("NinjaTrader.NinjaScript.Indicators.CoreLevels");
                if (t == null)
                {
                    Print("[CoreLevelsIntrospect] Could not find type NinjaTrader.NinjaScript.Indicators.CoreLevels");
                    return;
                }
                try
                {
                    core = Activator.CreateInstance(t) as Indicator;
                }
                catch (Exception ex)
                {
                    Print("[CoreLevelsIntrospect] CreateInstance failed: " + ex.Message);
                    return;
                }

                if (core == null)
                {
                    Print("[CoreLevelsIntrospect] Instance was null.");
                    return;
                }

                // 2) Add to chart so it actually computes
                AddChartIndicator(core);
                Print("[CoreLevelsIntrospect] Attached: " + core.GetType().FullName);

                // 3) Prepare output file
                outPath = Path.Combine(Core.Globals.UserDataDir, "CoreLevels_Introspection.txt");
                using (var sw = new StreamWriter(outPath, false))
                {
                    sw.WriteLine("=== CoreLevels Introspection ===");
                    sw.WriteLine("Run at: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    sw.WriteLine("Instrument: " + Instrument.FullName);
                    sw.WriteLine();
                }
                wroteHeader = false;
            }
        }

        protected override void OnBarUpdate()
        {
            if (core == null || CurrentBar < 2) return;

            // Only dump once (after enough bars load so series have values)
            if (wroteHeader) return;

            var sb = new StringBuilder();
            sb.AppendLine($"Session: {Times[0][0]:yyyy-MM-dd}  Bars: {CurrentBar+1}");
            sb.AppendLine();

            // 4) List public properties (simple scalars)
            var props = core.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            sb.AppendLine("[Properties]");
            foreach (var p in props.OrderBy(x => x.Name))
            {
                try
                {
                    object val = p.CanRead ? p.GetValue(core, null) : null;
                    string printable = FormatValue(val);
                    // Only print interesting types
                    if (val == null) continue;

                    if (val is double || val is int || val is bool || val is string || val is decimal)
                        sb.AppendLine($"{p.Name} = {printable}");
                }
                catch { /* ignore */ }
            }
            sb.AppendLine();

            // 5) List public fields (simple scalars)
            var fields = core.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            sb.AppendLine("[Fields]");
            foreach (var f in fields.OrderBy(x => x.Name))
            {
                try
                {
                    object val = f.GetValue(core);
                    if (val == null) continue;
                    if (val is double || val is int || val is bool || val is string || val is decimal)
                        sb.AppendLine($"{f.Name} = {FormatValue(val)}");
                }
                catch { }
            }
            sb.AppendLine();

            // 6) Try to find any Series / ISeries<double> and read their latest values
            sb.AppendLine("[Series]");
            DumpSeriesFromMembers(props, core, sb);
            DumpSeriesFromMembers(fields, core, sb);

            // 7) Write to file
            using (var sw = new StreamWriter(outPath, true))
            {
                sw.WriteLine(sb.ToString());
                sw.WriteLine("=== end of dump ===");
            }

            Print("[CoreLevelsIntrospect] Wrote -> " + outPath);
            wroteHeader = true; // only once
        }

        private void DumpSeriesFromMembers(IEnumerable<MemberInfo> members, object instance, StringBuilder sb)
        {
            foreach (var m in members)
            {
                Type mt;
                Func<object> getter;

                if (m is PropertyInfo pi && pi.CanRead)
                {
                    mt = pi.PropertyType;
                    getter = () => { try { return pi.GetValue(instance, null); } catch { return null; } };
                }
                else if (m is FieldInfo fi)
                {
                    mt = fi.FieldType;
                    getter = () => { try { return fi.GetValue(instance); } catch { return null; } };
                }
                else continue;

                try
                {
                    object obj = getter();
                    if (obj == null) continue;

                    // Direct Series<double> ?
                    if (IsSeriesDouble(mt))
                    {
                        double? val = TryGetSeriesValue(obj, 0);
                        sb.AppendLine($"{m.Name} [Series<double>] = {(val.HasValue ? val.Value.ToString("G") : "(n/a)")}");
                        continue;
                    }

                    // ISeries<double> ?
                    if (ImplementsISeriesDouble(mt))
                    {
                        double? val = TryGetSeriesValue(obj, 0);
                        sb.AppendLine($"{m.Name} [ISeries<double>] = {(val.HasValue ? val.Value.ToString("G") : "(n/a)")}");
                        continue;
                    }

                    // Collections of series?
                    if (typeof(System.Collections.IEnumerable).IsAssignableFrom(mt) && !(obj is string))
                    {
                        int idx = 0;
                        foreach (var item in (System.Collections.IEnumerable)obj)
                        {
                            if (item == null) { idx++; continue; }
                            var it = item.GetType();
                            if (IsSeriesDouble(it) || ImplementsISeriesDouble(it))
                            {
                                double? val = TryGetSeriesValue(item, 0);
                                sb.AppendLine($"{m.Name}[{idx}] [{it.Name}] = {(val.HasValue ? val.Value.ToString("G") : "(n/a)")}");
                            }
                            idx++;
                        }
                    }
                }
                catch
                {
                    // ignore a member we can't read
                }
            }
        }

        private static bool IsSeriesDouble(Type t)
        {
            // Match generic Series<double> types
            if (t == null) return false;
            if (t.IsGenericType && t.GetGenericTypeDefinition().Name.StartsWith("Series"))
            {
                var args = t.GetGenericArguments();
                return args.Length == 1 && (args[0] == typeof(double));
            }
            return false;
        }

        private static bool ImplementsISeriesDouble(Type t)
        {
            if (t == null) return false;
            foreach (var i in t.GetInterfaces())
            {
                if (i.IsGenericType && i.Name.StartsWith("ISeries"))
                {
                    var args = i.GetGenericArguments();
                    if (args.Length == 1 && (args[0] == typeof(double)))
                        return true;
                }
            }
            return false;
        }

        private double? TryGetSeriesValue(object seriesObj, int barsAgo)
        {
            try
            {
                // Try common "GetValueAt(int)" pattern
                var meth = seriesObj.GetType().GetMethod("GetValueAt", new Type[] { typeof(int) });
                if (meth != null)
                {
                    var v = meth.Invoke(seriesObj, new object[] { barsAgo });
                    if (v is double d) return d;
                }

                // Try indexer get_Item(int)
                var idx = seriesObj.GetType().GetProperty("Item", new Type[] { typeof(int) });
                if (idx != null)
                {
                    var v = idx.GetValue(seriesObj, new object[] { barsAgo });
                    if (v is double d) return d;
                }
            }
            catch { }
            return null;
        }

        private string FormatValue(object v)
        {
            if (v == null) return "";
            if (v is double d) return d.ToString("G");
            if (v is decimal m) return m.ToString("G");
            return v.ToString();
        }
    }
}
