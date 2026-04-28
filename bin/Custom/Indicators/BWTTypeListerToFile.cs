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

// Lists every loaded Indicator whose name contains "BWT".
// Writes the results to: Documents\NinjaTrader 8\BWT_IndicatorTypes.txt
namespace NinjaTrader.NinjaScript.Indicators
{
    public class BWTTypeListerToFile : Indicator
    {
        private string outPath;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "BWTTypeListerToFile";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.DataLoaded)
            {
                // Build list once when the indicator is loaded
                var sb = new StringBuilder();
                sb.AppendLine("=== Indicators containing 'BWT' ===");

                foreach (var t in GetAllIndicatorTypes())
                {
                    if (t.FullName != null &&
                        t.FullName.Contains("NinjaTrader.NinjaScript.Indicators") &&
                        t.Name.IndexOf("BWT", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        sb.AppendLine(t.FullName); // e.g. NinjaTrader.NinjaScript.Indicators.BWTCoreLevels
                    }
                }

                sb.AppendLine("=== end ===");

                outPath = Path.Combine(Core.Globals.UserDataDir, "BWT_IndicatorTypes.txt");
                File.WriteAllText(outPath, sb.ToString());

                Print("BWTTypeListerToFile wrote: " + outPath);
            }
        }

        protected override void OnBarUpdate()
        {
            // nothing needed here
        }

        private static IEnumerable<Type> GetAllIndicatorTypes()
        {
            var list = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(x => x != null).ToArray();
                }

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
		private BWTTypeListerToFile[] cacheBWTTypeListerToFile;
		public BWTTypeListerToFile BWTTypeListerToFile()
		{
			return BWTTypeListerToFile(Input);
		}

		public BWTTypeListerToFile BWTTypeListerToFile(ISeries<double> input)
		{
			if (cacheBWTTypeListerToFile != null)
				for (int idx = 0; idx < cacheBWTTypeListerToFile.Length; idx++)
					if (cacheBWTTypeListerToFile[idx] != null &&  cacheBWTTypeListerToFile[idx].EqualsInput(input))
						return cacheBWTTypeListerToFile[idx];
			return CacheIndicator<BWTTypeListerToFile>(new BWTTypeListerToFile(), input, ref cacheBWTTypeListerToFile);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BWTTypeListerToFile BWTTypeListerToFile()
		{
			return indicator.BWTTypeListerToFile(Input);
		}

		public Indicators.BWTTypeListerToFile BWTTypeListerToFile(ISeries<double> input )
		{
			return indicator.BWTTypeListerToFile(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BWTTypeListerToFile BWTTypeListerToFile()
		{
			return indicator.BWTTypeListerToFile(Input);
		}

		public Indicators.BWTTypeListerToFile BWTTypeListerToFile(ISeries<double> input )
		{
			return indicator.BWTTypeListerToFile(input);
		}
	}
}

#endregion
