using System;
using System.Windows.Media;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Gui.Tools;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class EMACrossArrowSimple : Indicator
    {
        private EMA fastEMA;
        private EMA slowEMA;
        private bool lastWasBullish;
        private bool lastWasBearish;

        [Range(1, int.MaxValue)]
        [Display(Name = "Fast EMA Period", Description = "Period for fast EMA", Order = 1)]
        public int FastPeriod { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Slow EMA Period", Description = "Period for slow EMA", Order = 2)]
        public int SlowPeriod { get; set; }

        [Display(Name = "Enable Alerts", Description = "Enable audio/popup alerts", Order = 3)]
        public bool EnableAlerts { get; set; }

        [Display(Name = "Alert Sound File", Description = "Sound file name (e.g., 'Alert1.wav')", Order = 4)]
        public string AlertSound { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"EMA Crossover with visual arrows and alerts";
                Name = "EMACrossArrowSimple";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                FastPeriod = 9;
                SlowPeriod = 21;
                EnableAlerts = true;
                AlertSound = "Alert1.wav";
            }
            else if (State == State.Configure)
            {
                // Initialize alert tracking variables
                lastWasBullish = false;
                lastWasBearish = false;
            }
            else if (State == State.DataLoaded)
            {
                fastEMA = EMA(FastPeriod);
                slowEMA = EMA(SlowPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(FastPeriod, SlowPeriod))
                return;

            bool isBullishCross = CrossAbove(fastEMA, slowEMA, 1);
            bool isBearishCross = CrossBelow(fastEMA, slowEMA, 1);

            // Reset tracking on new bar
            if (CurrentBar != lastBarChecked)
            {
                lastWasBullish = false;
                lastWasBearish = false;
                lastBarChecked = CurrentBar;
            }

            // Bullish crossover
            if (isBullishCross && !lastWasBullish)
            {
                // Draw.Diamond removed. Triangle shifted closer to the bar (2 * TickSize instead of 4).
                Draw.TriangleUp(this, "BullTriangle" + CurrentBar, false, 0, Low[0] - 2 * TickSize, Brushes.LimeGreen);
                
                if (EnableAlerts)
                {
                    // Trigger alert
                    Alert("EMA_BullishCross", Priority.High, 
                        "Bullish EMA crossover detected!", 
                        AlertSound, 
                        10, 
                        Brushes.Green, 
                        Brushes.White);
                }
                
                lastWasBullish = true;
            }
            // Bearish crossover
            else if (isBearishCross && !lastWasBearish)
            {
                // Draw.Diamond removed. Triangle shifted closer to the bar (2 * TickSize instead of 4).
                Draw.TriangleDown(this, "BearTriangle" + CurrentBar, false, 0, High[0] + 2 * TickSize, Brushes.Orange);
                
                if (EnableAlerts)
                {
                    // Trigger alert
                    Alert("EMA_BearishCross", Priority.High, 
                        "Bearish EMA crossover detected!", 
                        AlertSound, 
                        10, 
                        Brushes.Red, 
                        Brushes.White);
                }
                
                lastWasBearish = true;
            }
        }

        // Tracking variables
        private int lastBarChecked = -1;
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EMACrossArrowSimple[] cacheEMACrossArrowSimple;
		public EMACrossArrowSimple EMACrossArrowSimple()
		{
			return EMACrossArrowSimple(Input);
		}

		public EMACrossArrowSimple EMACrossArrowSimple(ISeries<double> input)
		{
			if (cacheEMACrossArrowSimple != null)
				for (int idx = 0; idx < cacheEMACrossArrowSimple.Length; idx++)
					if (cacheEMACrossArrowSimple[idx] != null &&  cacheEMACrossArrowSimple[idx].EqualsInput(input))
						return cacheEMACrossArrowSimple[idx];
			return CacheIndicator<EMACrossArrowSimple>(new EMACrossArrowSimple(), input, ref cacheEMACrossArrowSimple);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EMACrossArrowSimple EMACrossArrowSimple()
		{
			return indicator.EMACrossArrowSimple(Input);
		}

		public Indicators.EMACrossArrowSimple EMACrossArrowSimple(ISeries<double> input )
		{
			return indicator.EMACrossArrowSimple(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EMACrossArrowSimple EMACrossArrowSimple()
		{
			return indicator.EMACrossArrowSimple(Input);
		}

		public Indicators.EMACrossArrowSimple EMACrossArrowSimple(ISeries<double> input )
		{
			return indicator.EMACrossArrowSimple(input);
		}
	}
}

#endregion
