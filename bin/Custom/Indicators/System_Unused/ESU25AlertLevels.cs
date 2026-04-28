#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Gui.Chart;
using System.Windows.Media;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class ESU25AlertLevels : Indicator
    {
        // Price level constants
        private Series<double> fib50, yestHigh, low30, weekOpen, fib38;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ESU25AlertLevels";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
            }
            else if (State == State.DataLoaded)
            {
                fib50 = new Series<double>(this);
                yestHigh = new Series<double>(this);
                low30 = new Series<double>(this);
                weekOpen = new Series<double>(this);
                fib38 = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            // Set price level values each bar
            fib50[0] = 6429.17;
            yestHigh[0] = 6432.54;
            low30[0] = 6426.75;
            weekOpen[0] = 6424.25;
            fib38[0] = 6423.06;

            // Use CrossAbove / CrossBelow with Series<double>
            if (CrossAbove(Close, yestHigh, 1))
                Alert("AlertYestHighUp", Priority.High, "Price crossed ABOVE Yesterday High", @"Alert4.wav", 10, Brushes.Gold, Brushes.Black);

            if (CrossBelow(Close, yestHigh, 1))
                Alert("AlertYestHighDown", Priority.Medium, "Price crossed BELOW Yesterday High", @"Alert2.wav", 10, Brushes.Red, Brushes.White);

            if (CrossAbove(Close, fib50, 1))
                Alert("AlertFib50Up", Priority.Medium, "Price crossed ABOVE 50% Fib", @"Alert3.wav", 10, Brushes.LightBlue, Brushes.Black);

            if (CrossBelow(Close, low30, 1))
                Alert("AlertLow30", Priority.Medium, "Price dropped BELOW 30-min Low", @"Alert2.wav", 10, Brushes.OrangeRed, Brushes.White);

            if (CrossBelow(Close, weekOpen, 1))
                Alert("AlertWeekOpen", Priority.High, "Price dropped BELOW Weekly Open", @"Alert4.wav", 10, Brushes.Red, Brushes.White);

            if (CrossBelow(Close, fib38, 1))
                Alert("AlertFib38", Priority.High, "Price dropped BELOW 38.2% Fib", @"Alert1.wav", 10, Brushes.DarkRed, Brushes.White);
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ESU25AlertLevels[] cacheESU25AlertLevels;
		public ESU25AlertLevels ESU25AlertLevels()
		{
			return ESU25AlertLevels(Input);
		}

		public ESU25AlertLevels ESU25AlertLevels(ISeries<double> input)
		{
			if (cacheESU25AlertLevels != null)
				for (int idx = 0; idx < cacheESU25AlertLevels.Length; idx++)
					if (cacheESU25AlertLevels[idx] != null &&  cacheESU25AlertLevels[idx].EqualsInput(input))
						return cacheESU25AlertLevels[idx];
			return CacheIndicator<ESU25AlertLevels>(new ESU25AlertLevels(), input, ref cacheESU25AlertLevels);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ESU25AlertLevels ESU25AlertLevels()
		{
			return indicator.ESU25AlertLevels(Input);
		}

		public Indicators.ESU25AlertLevels ESU25AlertLevels(ISeries<double> input )
		{
			return indicator.ESU25AlertLevels(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ESU25AlertLevels ESU25AlertLevels()
		{
			return indicator.ESU25AlertLevels(Input);
		}

		public Indicators.ESU25AlertLevels ESU25AlertLevels(ISeries<double> input )
		{
			return indicator.ESU25AlertLevels(input);
		}
	}
}

#endregion
