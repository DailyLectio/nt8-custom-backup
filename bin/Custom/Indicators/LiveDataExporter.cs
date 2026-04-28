#region Using declarations
using System;
using System.IO;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class LiveDataExporter : Indicator
    {
        private string exportPath = "";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Quietly appends live 1-minute OHLCV data to the Macro Regime export files.";
                Name = "Live Data Exporter (Macro Regime)";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
            }
            else if (State == State.DataLoaded)
            {
                // Auto-detect if this is an ES or NQ chart and set the correct file path
                string symbol = Instrument.MasterInstrument.Name.ToUpper();
                if (symbol.Contains("NQ")) 
                {
                    exportPath = @"C:\Users\Valued Customer\NT8_Regimes\Exports\NQ_1min_export.txt";
                }
                else if (symbol.Contains("ES")) 
                {
                    exportPath = @"C:\Users\Valued Customer\NT8_Regimes\Exports\ES_1min_export.txt";
                }
                else 
                {
                    Print("LiveDataExporter: Unrecognized symbol. Not exporting.");
                }
            }
        }

        protected override void OnBarUpdate()
        {
            // We only want to append new data in real-time. We do not want to rewrite history.
            if (CurrentBar < 1 || State != State.Realtime) return;

            if (string.IsNullOrEmpty(exportPath)) return;

            try
            {
                // Format perfectly matches your Python reader: Timestamp;Open;High;Low;Close;Volume
                string timestamp = Time[0].ToString("yyyyMMdd HHmmss");
                string line = string.Format("{0};{1};{2};{3};{4};{5}", 
                    timestamp, Open[0], High[0], Low[0], Close[0], Volume[0]);

                // StreamWriter with "true" tells it to append to the bottom of the file, not overwrite it
                using (StreamWriter sw = new StreamWriter(exportPath, true)) 
                {
                    sw.WriteLine(line);
                }
            }
            catch { /* Silent catch if the file is locked for a millisecond by the Python reader */ }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private LiveDataExporter[] cacheLiveDataExporter;
		public LiveDataExporter LiveDataExporter()
		{
			return LiveDataExporter(Input);
		}

		public LiveDataExporter LiveDataExporter(ISeries<double> input)
		{
			if (cacheLiveDataExporter != null)
				for (int idx = 0; idx < cacheLiveDataExporter.Length; idx++)
					if (cacheLiveDataExporter[idx] != null &&  cacheLiveDataExporter[idx].EqualsInput(input))
						return cacheLiveDataExporter[idx];
			return CacheIndicator<LiveDataExporter>(new LiveDataExporter(), input, ref cacheLiveDataExporter);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.LiveDataExporter LiveDataExporter()
		{
			return indicator.LiveDataExporter(Input);
		}

		public Indicators.LiveDataExporter LiveDataExporter(ISeries<double> input )
		{
			return indicator.LiveDataExporter(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.LiveDataExporter LiveDataExporter()
		{
			return indicator.LiveDataExporter(Input);
		}

		public Indicators.LiveDataExporter LiveDataExporter(ISeries<double> input )
		{
			return indicator.LiveDataExporter(input);
		}
	}
}

#endregion
