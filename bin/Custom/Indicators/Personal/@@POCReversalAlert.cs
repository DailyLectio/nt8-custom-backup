using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Drawing;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Data;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class BWT_POC_Reversal_Alert : Indicator
    {
        // Declare variables
        private BWT_Core_Levels bwtLevels;
        private RSI rsi;
        private SMA volSMA;
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Alerts for POC Reversal setups per BWT strategy";
                Name = "BWT POC Reversal Alert";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                IsAutoScale = false;
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.DataLoaded)
            {
                // Initialize indicators
                bwtLevels = BWT_Core_Levels();
                rsi = RSI(Close, 14);
                volSMA = SMA(Volume, 20);
            }
        }

        protected override void OnBarUpdate()
        {
            // Skip during initialization
            if (CurrentBar < 20 || bwtLevels == null) return;

            // Get BWT levels using official names
            double median = bwtLevels.Median[0];             // BLUE - MEDIAN (MidPoint)
            double blueUpperHotZone = bwtLevels.BlueUpperHotZone[0];
            double blueLowerHotZone = bwtLevels.BlueLowerHotZone[0];
            
            // Volume condition (200% of 20-period avg)
            double volAvg = volSMA[0];
            bool volumeConfirmed = volAvg > 0 && Volume[0] >= 2 * volAvg;

            // Bullish setup
            if (IsBullishReversal(median, volumeConfirmed))
            {
                if (Close[0] > blueUpperHotZone)
                {
                    TriggerAlert("BullishPOCReversal", 
                                 $"BULLISH REVERSAL: Break above Blue Upper Hot Zone ({blueUpperHotZone:F2})",
                                 Brushes.Cyan);  // Using CYAN per BWT color scheme
                    Draw.ArrowUp(this, "BullRev" + CurrentBar, 0, Low[0] - 10 * TickSize, Brushes.Cyan);
                }
            }

            // Bearish setup
            if (IsBearishReversal(median, volumeConfirmed))
            {
                if (Close[0] < blueLowerHotZone)
                {
                    TriggerAlert("BearishPOCReversal", 
                                 $"BEARISH REVERSAL: Break below Blue Lower Hot Zone ({blueLowerHotZone:F2})",
                                 Brushes.Magenta);  // Using MAGENTA per BWT color scheme
                    Draw.ArrowDown(this, "BearRev" + CurrentBar, 0, High[0] + 10 * TickSize, Brushes.Magenta);
                }
            }
        }

        // Helper methods
        private bool IsBullishReversal(double median, bool volumeConfirmed)
        {
            // Price touched median and closed above it
            bool priceCondition = Low[0] <= median && Close[0] > median;
            
            // Bullish candle (close > open)
            bool candleCondition = Close[0] > Open[0];
            
            // RSI divergence: Lower price low + higher RSI low
            bool rsiCondition = CurrentBar >= 2 && 
                                (Low[0] < Low[1]) && 
                                (rsi[0] > rsi[1]);

            return priceCondition && candleCondition && rsiCondition && volumeConfirmed;
        }

        private bool IsBearishReversal(double median, bool volumeConfirmed)
        {
            // Price touched median and closed below it
            bool priceCondition = High[0] >= median && Close[0] < median;
            
            // Bearish candle (close < open)
            bool candleCondition = Close[0] < Open[0];
            
            // RSI divergence: Higher price high + lower RSI high
            bool rsiCondition = CurrentBar >= 2 && 
                                (High[0] > High[1]) && 
                                (rsi[0] < rsi[1]);

            return priceCondition && candleCondition && rsiCondition && volumeConfirmed;
        }

        private void TriggerAlert(string alertName, string message, Brush color)
        {
            Alert(alertName, Priority.High, message, "Alert1.wav", 10, Brushes.Black, color);
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BWT_POC_Reversal_Alert[] cacheBWT_POC_Reversal_Alert;
		public BWT_POC_Reversal_Alert BWT_POC_Reversal_Alert()
		{
			return BWT_POC_Reversal_Alert(Input);
		}

		public BWT_POC_Reversal_Alert BWT_POC_Reversal_Alert(ISeries<double> input)
		{
			if (cacheBWT_POC_Reversal_Alert != null)
				for (int idx = 0; idx < cacheBWT_POC_Reversal_Alert.Length; idx++)
					if (cacheBWT_POC_Reversal_Alert[idx] != null &&  cacheBWT_POC_Reversal_Alert[idx].EqualsInput(input))
						return cacheBWT_POC_Reversal_Alert[idx];
			return CacheIndicator<BWT_POC_Reversal_Alert>(new BWT_POC_Reversal_Alert(), input, ref cacheBWT_POC_Reversal_Alert);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BWT_POC_Reversal_Alert BWT_POC_Reversal_Alert()
		{
			return indicator.BWT_POC_Reversal_Alert(Input);
		}

		public Indicators.BWT_POC_Reversal_Alert BWT_POC_Reversal_Alert(ISeries<double> input )
		{
			return indicator.BWT_POC_Reversal_Alert(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BWT_POC_Reversal_Alert BWT_POC_Reversal_Alert()
		{
			return indicator.BWT_POC_Reversal_Alert(Input);
		}

		public Indicators.BWT_POC_Reversal_Alert BWT_POC_Reversal_Alert(ISeries<double> input )
		{
			return indicator.BWT_POC_Reversal_Alert(input);
		}
	}
}

#endregion
