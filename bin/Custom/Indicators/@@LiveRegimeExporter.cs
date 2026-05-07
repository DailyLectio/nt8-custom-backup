#region Using declarations
using System;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class LiveRegimeExporter : Indicator
    {
        private string filePath = string.Empty;
        private StringBuilder sb;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Legacy/shared research exporter: writes Live_* 1-minute data for Python HMM tests. Not a V3C/V3D production trade exporter.";
                Name = "LiveRegimeExporter";
                Calculate = Calculate.OnBarClose; // Only runs when the minute finishes
                IsOverlay = true;
            }
            else if (State == State.DataLoaded)
            {
                // This automatically saves to your Documents\NinjaTrader 8 folder
                filePath = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "Live_" + Instrument.MasterInstrument.Name + "_Data.txt");
            }
        }

        protected override void OnBarUpdate()
        {
            // Only process the current day's bars so the file doesn't get massive
            if (Time[0].Date != DateTime.Today) return;

            // Reset the memory block at the start of a new day
            if (CurrentBar == Bars.GetBar(DateTime.Today) || sb == null)
            {
                sb = new StringBuilder(); 
            }

            // Format precisely to match your 90-day base export: 20260406 093000;Open;High;Low;Close;Volume
            string timestamp = Time[0].ToString("yyyyMMdd HHmmss");
            string line = string.Format("{0};{1};{2};{3};{4};{5}",
                timestamp, Open[0], High[0], Low[0], Close[0], Volume[0]);

            sb.AppendLine(line);

            // Overwrite the file with the day's updated data
            try
            {
                File.WriteAllText(filePath, sb.ToString());
            }
            catch (Exception e)
            {
                Print("LiveRegimeExporter Error: " + e.Message);
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private LiveRegimeExporter[] cacheLiveRegimeExporter;
		public LiveRegimeExporter LiveRegimeExporter()
		{
			return LiveRegimeExporter(Input);
		}

		public LiveRegimeExporter LiveRegimeExporter(ISeries<double> input)
		{
			if (cacheLiveRegimeExporter != null)
				for (int idx = 0; idx < cacheLiveRegimeExporter.Length; idx++)
					if (cacheLiveRegimeExporter[idx] != null &&  cacheLiveRegimeExporter[idx].EqualsInput(input))
						return cacheLiveRegimeExporter[idx];
			return CacheIndicator<LiveRegimeExporter>(new LiveRegimeExporter(), input, ref cacheLiveRegimeExporter);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.LiveRegimeExporter LiveRegimeExporter()
		{
			return indicator.LiveRegimeExporter(Input);
		}

		public Indicators.LiveRegimeExporter LiveRegimeExporter(ISeries<double> input )
		{
			return indicator.LiveRegimeExporter(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.LiveRegimeExporter LiveRegimeExporter()
		{
			return indicator.LiveRegimeExporter(Input);
		}

		public Indicators.LiveRegimeExporter LiveRegimeExporter(ISeries<double> input )
		{
			return indicator.LiveRegimeExporter(input);
		}
	}
}

#endregion
