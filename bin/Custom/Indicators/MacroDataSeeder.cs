#region Using declarations
using System;
using System.IO;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class MacroDataSeeder : Indicator
    {
        private bool isExported = false;
        private DateTime enableTime;
        private string statusText = "INITIALIZING...";
        private Brush statusColor = Brushes.Yellow;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"One-time utility to generate a perfect 30-day continuous historical export. Auto-disables after 61 seconds.";
                Name = "Macro Data Seeder";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
            }
            else if (State == State.DataLoaded)
            {
                // Start the 61-second kill switch timer the moment the indicator loads
                enableTime = DateTime.Now;
            }
        }

        protected override void OnBarUpdate()
        {
            // LAYER 1: The 61-Second Absolute Kill Switch
            if ((DateTime.Now - enableTime).TotalSeconds > 61)
            {
                statusText = "SEEDER KILLED BY 61s TIMEOUT (SAFE)";
                statusColor = Brushes.LimeGreen;
                DrawStatus();
                return;
            }

            // LAYER 2: The "One-and-Done" Lock
            if (isExported)
            {
                statusText = "SEED COMPLETE & AUTO-DISABLED (SAFE)";
                statusColor = Brushes.LimeGreen;
                DrawStatus();
                return;
            }

            // Wait until the chart is fully loaded before trying to export
            if (CurrentBar < Count - 2) 
            {
                statusText = "SEEDING IN PROGRESS... DO NOT CLOSE";
                statusColor = Brushes.Red;
                DrawStatus();
                return;
            }

            string symbol = Instrument.MasterInstrument.Name.ToUpper();
            if (!symbol.Contains("NQ") && !symbol.Contains("ES")) return;

            string exportPath = @"C:\Users\Valued Customer\NT8_Regimes\Exports\" + (symbol.Contains("NQ") ? "NQ" : "ES") + "_1min_export.txt";

            try
            {
                // false = Overwrite any existing file with a fresh, clean slate
                using (StreamWriter sw = new StreamWriter(exportPath, false))
                {
                    // Loop through the chart's bars and format exactly for Python
                    for (int i = CurrentBar; i >= 0; i--)
                    {
                        string timestamp = Time[i].ToString("yyyyMMdd HHmmss");
                        sw.WriteLine(string.Format("{0};{1};{2};{3};{4};{5}", 
                            timestamp, Open[i], High[i], Low[i], Close[i], Volume[i]));
                    }
                }
                
                // Engage the lock
                isExported = true;
                
                statusText = "SEED COMPLETE & AUTO-DISABLED (SAFE)";
                statusColor = Brushes.LimeGreen;
                DrawStatus();
                
                Print("SEEDED: Successfully overwrote 30-day history for " + symbol + " to " + exportPath);
            }
            catch (Exception ex)
            {
                Print("MacroDataSeeder Error: " + ex.Message);
            }
        }

        private void DrawStatus()
        {
            Draw.TextFixed(this, "SeederStatusHUD", statusText, TextPosition.TopLeft, statusColor, 
                new Gui.Tools.SimpleFont("Arial", 14) { Bold = true }, 
                Brushes.Transparent, Brushes.Black, 80);
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MacroDataSeeder[] cacheMacroDataSeeder;
		public MacroDataSeeder MacroDataSeeder()
		{
			return MacroDataSeeder(Input);
		}

		public MacroDataSeeder MacroDataSeeder(ISeries<double> input)
		{
			if (cacheMacroDataSeeder != null)
				for (int idx = 0; idx < cacheMacroDataSeeder.Length; idx++)
					if (cacheMacroDataSeeder[idx] != null &&  cacheMacroDataSeeder[idx].EqualsInput(input))
						return cacheMacroDataSeeder[idx];
			return CacheIndicator<MacroDataSeeder>(new MacroDataSeeder(), input, ref cacheMacroDataSeeder);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MacroDataSeeder MacroDataSeeder()
		{
			return indicator.MacroDataSeeder(Input);
		}

		public Indicators.MacroDataSeeder MacroDataSeeder(ISeries<double> input )
		{
			return indicator.MacroDataSeeder(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MacroDataSeeder MacroDataSeeder()
		{
			return indicator.MacroDataSeeder(Input);
		}

		public Indicators.MacroDataSeeder MacroDataSeeder(ISeries<double> input )
		{
			return indicator.MacroDataSeeder(input);
		}
	}
}

#endregion
