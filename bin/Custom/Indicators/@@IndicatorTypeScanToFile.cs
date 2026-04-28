#region Using
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// Scans ALL loaded Indicators, lists their plot names, and flags those that look like CORE Levels.
// Output: Documents\NinjaTrader 8\IndicatorTypes_Report.txt
namespace NinjaTrader.NinjaScript.Indicators
{
    public class IndicatorTypeScanToFile : Indicator
    {
        private string outPath;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "IndicatorTypeScanToFile";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.DataLoaded)
            {
                var (report, totals, hits) = BuildReport();
                outPath = Path.Combine(Core.Globals.UserDataDir, "IndicatorTypes_Report.txt");
                File.WriteAllText(outPath, report);

                Print($"IndicatorTypeScanToFile wrote: {outPath}");
                Print($"Scanned {totals} indicators; flagged {hits} LIKELY_CORE matches. Open the file for details.");
            }
        }

        protected override void OnBarUpdate() { /* no-op */ }

        private (string report, int total, int coreHits) BuildReport()
        {
            var sb = new StringBuilder();
            int total = 0, coreHits = 0;

            sb.AppendLine("=== Indicator Scan (all loaded Indicator types) ===");
            sb.AppendLine($"Run at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("Format: FullName | Plots | Flags");
            sb.AppendLine();

            foreach (var t in GetAllIndicatorTypes())
            {
                total++;
                string full = t.FullName ?? t.Name;
                string plots = "";
                string flags = "";

                try
                {
                    var inst = Activator.CreateInstance(t) as Indicator;
                    if (inst != null && inst.Plots != null)
                    {
                        // Manually count and collect names (avoid Count/Count())
                        var namesList = new List<string>();
                        try
                        {
                            foreach (var p in inst.Plots)
                            {
                                var nm = (p == null ? "" : (p.Name ?? ""));
                                if (!string.IsNullOrWhiteSpace(nm))
                                    namesList.Add(nm);
                            }
                        }
                        catch { /* some collections may not be enumerable; ignore */ }

                        if (namesList.Count > 0)
                        {
                            plots = string.Join(", ", namesList);

                            // Heuristic for CORE: look for POC and ExpectedHigh/ExpectedLow
                            var set = new HashSet<string>(namesList, StringComparer.OrdinalIgnoreCase);
                            bool hasPOC = set.Contains("POC");
                            bool hasEH  = set.Contains("ExpectedHigh");
                            bool hasEL  = set.Contains("ExpectedLow");

                            if (hasPOC && (hasEH || hasEL))
                            {
                                flags = "LIKELY_CORE";
                                coreHits++;
                            }
                        }
                        else
                        {
                            plots = "(no plots)";
                        }
                    }
                    else
                    {
                        plots = "(no plots)";
                    }
                }
                catch
                {
                    plots = "(failed to instantiate)";
                }

                sb.AppendLine($"{full} | {plots} | {flags}");
            }

            sb.AppendLine();
            sb.AppendLine($"Summary: scanned={total}, LIKELY_CORE={coreHits}");
            sb.AppendLine("=== end ===");
            return (sb.ToString(), total, coreHits);
        }

        private static IEnumerable<Type> GetAllIndicatorTypes()
        {
            var list = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x != null).ToArray(); }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (!typeof(Indicator).IsAssignableFrom(t)) continue;
                    if (t.IsAbstract) continue;
                    list.Add(t);
                }
            }
            return list.OrderBy(t => t.FullName);
        }
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private IndicatorTypeScanToFile[] cacheIndicatorTypeScanToFile;
		public IndicatorTypeScanToFile IndicatorTypeScanToFile()
		{
			return IndicatorTypeScanToFile(Input);
		}

		public IndicatorTypeScanToFile IndicatorTypeScanToFile(ISeries<double> input)
		{
			if (cacheIndicatorTypeScanToFile != null)
				for (int idx = 0; idx < cacheIndicatorTypeScanToFile.Length; idx++)
					if (cacheIndicatorTypeScanToFile[idx] != null &&  cacheIndicatorTypeScanToFile[idx].EqualsInput(input))
						return cacheIndicatorTypeScanToFile[idx];
			return CacheIndicator<IndicatorTypeScanToFile>(new IndicatorTypeScanToFile(), input, ref cacheIndicatorTypeScanToFile);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.IndicatorTypeScanToFile IndicatorTypeScanToFile()
		{
			return indicator.IndicatorTypeScanToFile(Input);
		}

		public Indicators.IndicatorTypeScanToFile IndicatorTypeScanToFile(ISeries<double> input )
		{
			return indicator.IndicatorTypeScanToFile(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.IndicatorTypeScanToFile IndicatorTypeScanToFile()
		{
			return indicator.IndicatorTypeScanToFile(Input);
		}

		public Indicators.IndicatorTypeScanToFile IndicatorTypeScanToFile(ISeries<double> input )
		{
			return indicator.IndicatorTypeScanToFile(input);
		}
	}
}

#endregion
