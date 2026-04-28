#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.Data;
#endregion

// Indicator: Apex Scout v6 [Stabilized / NT8 Edition]
// Logic: Relaxed Chop Rule + ADX Sustain Mode (>30)
// Safe to install alongside v4.

namespace NinjaTrader.NinjaScript.Indicators
{
    public class ApexScout_v6 : Indicator
    {
        // Indicators
        private ADX adxInd;
        private DM dmInd;
        private ATR atr;
        private MAX maxHigh;
        private MIN minLow;
        private SMA volSma;
        
        // Internal Series for CI
        private Series<double> ciSeries;

        // Brushes
        private Brush brushBlue, brushGreen, brushRed, brushYellow;
        private Brush brushFuelYellow, brushFuelOrange;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Apex Scout v6 - Stabilized Logic for Higher TF/HA/Uni";
                Name = "Apex Scout v6";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true; 
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;

                // Default Settings
                AdxPeriod = 14;
                ChopPeriod = 14;
                ChopLimit = 60;
                RVolLength = 20;
                RVolThreshold = 1.2;
                
                // Visuals
                Opacity = 40; 
                EnableFuelCandles = true; 
            }
            else if (State == State.Configure)
            {
                // Initialize Brushes with Opacity
                brushBlue = new SolidColorBrush(Color.FromArgb((byte)Opacity, 30, 144, 255)); 
                brushGreen = new SolidColorBrush(Color.FromArgb((byte)Opacity, 0, 255, 0));    
                brushRed = new SolidColorBrush(Color.FromArgb((byte)Opacity, 255, 0, 0));      
                brushYellow = new SolidColorBrush(Color.FromArgb((byte)Opacity, 255, 255, 0));  
                
                brushFuelYellow = Brushes.Yellow;
                brushFuelOrange = Brushes.Orange;
                
                brushBlue.Freeze();
                brushGreen.Freeze();
                brushRed.Freeze();
                brushYellow.Freeze();
            }
            else if (State == State.DataLoaded)
            {
                adxInd = ADX(AdxPeriod);
                dmInd = DM(AdxPeriod);
                atr = ATR(1);
                maxHigh = MAX(High, ChopPeriod);
                minLow = MIN(Low, ChopPeriod);
                volSma = SMA(Volume, RVolLength);

                ciSeries = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(AdxPeriod, ChopPeriod) + 1) return;

            // 1. Calculate Choppiness Index (CI)
            double range = maxHigh[0] - minLow[0];
            double sumAtr = 0;
            for (int i = 0; i < ChopPeriod; i++) sumAtr += atr[i];
            
            double ci = 0;
            if (range > 0 && sumAtr > 0)
                ci = 100 * Math.Log10(sumAtr / range) / Math.Log10(ChopPeriod);
            
            ciSeries[0] = ci;

            // 2. Logic Variables (The v6 Stabilized Rules)
            
            // Chop Rule: Just needs to be below the limit (Removed strict falling requirement)
            bool chopOk = ci < ChopLimit;

            // ADX Rule: Sustain Mode (>30) OR Rising if weak
            double currentAdx = adxInd[0];
            bool adxStrong = currentAdx > 30;
            bool adxRising = currentAdx > adxInd[1] && currentAdx > 20; // Ensure at least > 20 to start
            bool adxOk = adxStrong || adxRising;

            bool bullsCtrl = dmInd.DiPlus[0] > dmInd.DiMinus[0];
            bool bearsCtrl = dmInd.DiMinus[0] > dmInd.DiPlus[0];

            // RVol Logic
            double avgVol = volSma[0];
            double rvol = (avgVol > 0) ? Volume[0] / avgVol : 0;
            bool hasFuel = rvol > RVolThreshold;

            // 3. Background Logic (Traffic Light)
            
            // STATE 1: BLUE (Stop / Chop)
            if (!chopOk) // If CI >= 60
            {
                BackBrush = brushBlue;
            }
            // STATE 2: TRENDING (Green/Red)
            else if (adxOk) // Chop is OK (<60) AND Momentum is active
            {
                if (bullsCtrl) BackBrush = brushGreen;
                else if (bearsCtrl) BackBrush = brushRed;
                else BackBrush = brushYellow; 
            }
            // STATE 3: YELLOW (Caution)
            else
            {
                BackBrush = brushYellow;
            }

            // 4. Fuel Bars
            if (EnableFuelCandles && hasFuel)
            {
                if (Close[0] > Open[0]) BarBrush = brushFuelYellow;
                else BarBrush = brushFuelOrange;
            }
        }

        #region Properties
        [NinjaScriptProperty, Range(1, 100), Display(Name="ADX Period", GroupName="Parameters", Order=1)]
        public int AdxPeriod { get; set; }

        [NinjaScriptProperty, Range(1, 100), Display(Name="Chop Period", GroupName="Parameters", Order=2)]
        public int ChopPeriod { get; set; }

        [NinjaScriptProperty, Range(1, 100), Display(Name="Chop Limit (>Blue)", GroupName="Parameters", Order=3)]
        public double ChopLimit { get; set; }

        [NinjaScriptProperty, Range(1, 100), Display(Name="RVol Length", GroupName="Parameters", Order=4)]
        public int RVolLength { get; set; }

        [NinjaScriptProperty, Range(0.1, 10.0), Display(Name="RVol Threshold", GroupName="Parameters", Order=5)]
        public double RVolThreshold { get; set; }
        
        [NinjaScriptProperty, Range(0, 255), Display(Name="Background Opacity", GroupName="Visuals", Order=6)]
        public int Opacity { get; set; }

        [NinjaScriptProperty, Display(Name="Enable Fuel Candles", GroupName="Visuals", Order=7)]
        public bool EnableFuelCandles { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ApexScout_v6[] cacheApexScout_v6;
		public ApexScout_v6 ApexScout_v6(int adxPeriod, int chopPeriod, double chopLimit, int rVolLength, double rVolThreshold, int opacity, bool enableFuelCandles)
		{
			return ApexScout_v6(Input, adxPeriod, chopPeriod, chopLimit, rVolLength, rVolThreshold, opacity, enableFuelCandles);
		}

		public ApexScout_v6 ApexScout_v6(ISeries<double> input, int adxPeriod, int chopPeriod, double chopLimit, int rVolLength, double rVolThreshold, int opacity, bool enableFuelCandles)
		{
			if (cacheApexScout_v6 != null)
				for (int idx = 0; idx < cacheApexScout_v6.Length; idx++)
					if (cacheApexScout_v6[idx] != null && cacheApexScout_v6[idx].AdxPeriod == adxPeriod && cacheApexScout_v6[idx].ChopPeriod == chopPeriod && cacheApexScout_v6[idx].ChopLimit == chopLimit && cacheApexScout_v6[idx].RVolLength == rVolLength && cacheApexScout_v6[idx].RVolThreshold == rVolThreshold && cacheApexScout_v6[idx].Opacity == opacity && cacheApexScout_v6[idx].EnableFuelCandles == enableFuelCandles && cacheApexScout_v6[idx].EqualsInput(input))
						return cacheApexScout_v6[idx];
			return CacheIndicator<ApexScout_v6>(new ApexScout_v6(){ AdxPeriod = adxPeriod, ChopPeriod = chopPeriod, ChopLimit = chopLimit, RVolLength = rVolLength, RVolThreshold = rVolThreshold, Opacity = opacity, EnableFuelCandles = enableFuelCandles }, input, ref cacheApexScout_v6);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ApexScout_v6 ApexScout_v6(int adxPeriod, int chopPeriod, double chopLimit, int rVolLength, double rVolThreshold, int opacity, bool enableFuelCandles)
		{
			return indicator.ApexScout_v6(Input, adxPeriod, chopPeriod, chopLimit, rVolLength, rVolThreshold, opacity, enableFuelCandles);
		}

		public Indicators.ApexScout_v6 ApexScout_v6(ISeries<double> input , int adxPeriod, int chopPeriod, double chopLimit, int rVolLength, double rVolThreshold, int opacity, bool enableFuelCandles)
		{
			return indicator.ApexScout_v6(input, adxPeriod, chopPeriod, chopLimit, rVolLength, rVolThreshold, opacity, enableFuelCandles);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ApexScout_v6 ApexScout_v6(int adxPeriod, int chopPeriod, double chopLimit, int rVolLength, double rVolThreshold, int opacity, bool enableFuelCandles)
		{
			return indicator.ApexScout_v6(Input, adxPeriod, chopPeriod, chopLimit, rVolLength, rVolThreshold, opacity, enableFuelCandles);
		}

		public Indicators.ApexScout_v6 ApexScout_v6(ISeries<double> input , int adxPeriod, int chopPeriod, double chopLimit, int rVolLength, double rVolThreshold, int opacity, bool enableFuelCandles)
		{
			return indicator.ApexScout_v6(input, adxPeriod, chopPeriod, chopLimit, rVolLength, rVolThreshold, opacity, enableFuelCandles);
		}
	}
}

#endregion
