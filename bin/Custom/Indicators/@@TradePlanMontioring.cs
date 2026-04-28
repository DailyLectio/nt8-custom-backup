#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;
using System.Collections.Generic;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    [Category("Trade Plan Monitoring")]
    public class TradePlanMonitor : Indicator
    {
        // Manual calculation buffers
        private double fastEmaValue;
        private double slowEmaValue;
        private Queue<double> volumeQueue = new Queue<double>();
        private double volumeSum;
        private double rsiValue;
        private double rsiGain;
        private double rsiLoss;
        
        #region Core Levels
[NinjaScriptProperty]
[Display(Name = "B0 (POC)", Description = "Point of Control/Median", GroupName = "Core Levels", Order = 1)]
public double B0 { get; set; }

[NinjaScriptProperty]
[Display(Name = "B2 (Upper Midpoint)", Description = "High midpoint for breakouts", GroupName = "Core Levels", Order = 2)]
public double B2 { get; set; }

[NinjaScriptProperty]
[Display(Name = "B4 (Expected High)", Description = "Expected high target", GroupName = "Core Levels", Order = 3)]
public double B4 { get; set; }

// CORRECTED B6 DECLARATION
[NinjaScriptProperty]
[Display(Name = "B6 (Extended High)", Description = "Extended high target", GroupName = "Core Levels", Order = 4)]
public double B6 { get; set; } // Fixed property name

[NinjaScriptProperty]
[Display(Name = "R2 (Lower Midpoint)", Description = "Low midpoint for breakdowns", GroupName = "Core Levels", Order = 5)]
public double R2 { get; set; }

[NinjaScriptProperty]
[Display(Name = "R4 (Expected Low)", Description = "Expected low target", GroupName = "Core Levels", Order = 6)]
public double R4 { get; set; }

[NinjaScriptProperty]
[Display(Name = "R6 (Extended Low)", Description = "Extended low target", GroupName = "Core Levels", Order = 7)]
public double R6 { get; set; }
#endregion

        #region Indicator Settings
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Fast EMA Period", Description = "Fast EMA period", GroupName = "Indicator Settings", Order = 1)]
        public int FastEmaPeriod { get; set; } = 9;

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Slow EMA Period", Description = "Slow EMA period", GroupName = "Indicator Settings", Order = 2)]
        public int SlowEmaPeriod { get; set; } = 21;

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "Volume SMA Period", Description = "Volume SMA period", GroupName = "Indicator Settings", Order = 3)]
        public int VolSmaPeriod { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(5, 30)]
        [Display(Name = "RSI Period", Description = "RSI period", GroupName = "Indicator Settings", Order = 4)]
        public int RsiPeriod { get; set; } = 14;
        #endregion

        #region Alert Settings
        [NinjaScriptProperty]
        [Display(Name = "Enable Breakout Alerts", Description = "Enable breakout alerts", GroupName = "Alert Settings", Order = 1)]
        public bool EnableBreakoutAlerts { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable Reversal Alerts", Description = "Enable reversal alerts", GroupName = "Alert Settings", Order = 2)]
        public bool EnableReversalAlerts { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable Volume Alerts", Description = "Enable volume spike alerts", GroupName = "Alert Settings", Order = 3)]
        public bool EnableVolumeAlerts { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable RSI Alerts", Description = "Enable RSI alerts", GroupName = "Alert Settings", Order = 4)]
        public bool EnableRsiAlerts { get; set; } = true;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Monitors trade plan conditions and triggers alerts for breakouts and reversals";
                Name = "TradePlanMonitor";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                IsAutoScale = false;
                IsSuspendedWhileInactive = true;
            }
        }

        protected override void OnBarUpdate()
        {
            // Initialize volume SMA queue
            if (volumeQueue == null)
                volumeQueue = new Queue<double>(VolSmaPeriod);
            
            // Add current volume to queue and sum
            double currentVolume = Volume[0];
            volumeSum += currentVolume;
            volumeQueue.Enqueue(currentVolume);
            
            // Maintain queue size
            if (volumeQueue.Count > VolSmaPeriod)
            {
                volumeSum -= volumeQueue.Dequeue();
            }
            
            // Calculate volume average
            double volumeAverage = (volumeQueue.Count > 0) ? volumeSum / volumeQueue.Count : 0;
            
            // Calculate EMA manually
            if (CurrentBar == 0)
            {
                fastEmaValue = Close[0];
                slowEmaValue = Close[0];
            }
            else
            {
                double fastAlpha = 2.0 / (1 + FastEmaPeriod);
                double slowAlpha = 2.0 / (1 + SlowEmaPeriod);
                fastEmaValue = (Close[0] - fastEmaValue) * fastAlpha + fastEmaValue;
                slowEmaValue = (Close[0] - slowEmaValue) * slowAlpha + slowEmaValue;
            }
            
            // Calculate RSI manually
            if (CurrentBar > 0)
            {
                double delta = Close[0] - Close[1];
                double gain = delta > 0 ? delta : 0;
                double loss = delta < 0 ? -delta : 0;
                
                if (CurrentBar == 1)
                {
                    rsiGain = gain;
                    rsiLoss = loss;
                }
                else
                {
                    rsiGain = (gain + (RsiPeriod - 1) * rsiGain) / RsiPeriod;
                    rsiLoss = (loss + (RsiPeriod - 1) * rsiLoss) / RsiPeriod;
                }
                
                rsiValue = (rsiLoss == 0) ? 100 : 100 - (100 / (1 + rsiGain / rsiLoss));
            }
            
            // Skip until enough data
            int minBars = Math.Max(Math.Max(FastEmaPeriod, SlowEmaPeriod), Math.Max(VolSmaPeriod, RsiPeriod)) + 5;
            if (CurrentBar < minBars) 
                return;

            double currentClose = Close[0];
            bool volumeConfirmed = currentVolume >= 1.5 * volumeAverage;
            bool rsiBullFilter = rsiValue < 70;
            bool rsiBearFilter = rsiValue > 30;
            bool rsiOverbought = rsiValue >= 70;
            bool rsiOversold = rsiValue <= 30;

            bool bullishDivergence = false;
            bool bearishDivergence = false;
            
            if (CurrentBar >= 2)
            {
                // Simplified divergence detection
                bullishDivergence = (Low[0] < Low[1] && rsiValue > rsiValue);
                bearishDivergence = (High[0] > High[1] && rsiValue < rsiValue);
            }

            // 1. Bullish Breakout Alert (B2 Break)
            if (EnableBreakoutAlerts && 
                currentClose > B2 && 
                Close[1] <= B2 && 
                volumeConfirmed && 
                rsiBullFilter)
            {
                Alert("BullishBreakout", Priority.High, 
                    "BULLISH BREAKOUT: Price broke above B2 (" + B2.ToString("F2") + ") with volume confirmation",
                    NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav",
                    10, Brushes.Black, Brushes.Lime);
            }

            // 2. Bearish Breakdown Alert (R2 Break)
            if (EnableBreakoutAlerts && 
                currentClose < R2 && 
                Close[1] >= R2 && 
                volumeConfirmed && 
                rsiBearFilter)
            {
                Alert("BearishBreakdown", Priority.High, 
                    "BEARISH BREAKDOWN: Price broke below R2 (" + R2.ToString("F2") + ") with volume confirmation",
                    NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert2.wav",
                    10, Brushes.Black, Brushes.Pink);
            }

            // 3. Bullish Reversal Alert (R6 Touch)
            if (EnableReversalAlerts && 
                Low[0] <= R6 && 
                bullishDivergence)
            {
                Alert("BullishReversal", Priority.High, 
                    "BULLISH REVERSAL: Price touched R6 (" + R6.ToString("F2") + ") with RSI divergence",
                    NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert3.wav",
                    10, Brushes.Black, Brushes.Cyan);
            }

            // 4. Bearish Reversal Alert (B6 Touch)
            if (EnableReversalAlerts && 
                High[0] >= B6 && 
                bearishDivergence)
            {
                Alert("BearishReversal", Priority.High, 
                    "BEARISH REVERSAL: Price touched B6 (" + B6.ToString("F2") + ") with R极 divergence",
                    NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert4.wav",
                    10, Brushes.Black, Brushes.Orange);
            }

            // 5. Volume Spike Alert
            if (EnableVolumeAlerts && volumeConfirmed)
            {
                Alert("VolumeSpike", Priority.Medium, 
                    "VOLUME SPIKE: Current volume " + currentVolume.ToString("F0") + " is 150%+ of average",
                    NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav",
                    5, Brushes.Black, Brushes.Yellow);
            }

            // 6. RSI Extreme Alerts
            if (EnableRsiAlerts && rsiOverbought)
            {
                Alert("RSIOverBought", Priority.Medium, 
                    "RSI OVERBOUGHT: RSI above 70 (" + rsiValue.ToString("F1") + ")",
                    NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert2.wav",
                    5, Brushes.Black, Brushes.Red);
            }

            if (EnableRsiAlerts && rsiOversold)
            {
                Alert("RSIOverSold", Priority.Medium, 
                    "RSI OVERSOLD: RSI below 30 (" + rsiValue.ToString("F1") + ")",
                    NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert3.wav",
                    5, Brushes.Black, Brushes.Green);
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TradePlanMonitor[] cacheTradePlanMonitor;
		public TradePlanMonitor TradePlanMonitor(double b0, double b2, double b4, double b6, double r2, double r4, double r6, int fastEmaPeriod, int slowEmaPeriod, int volSmaPeriod, int rsiPeriod, bool enableBreakoutAlerts, bool enableReversalAlerts, bool enableVolumeAlerts, bool enableRsiAlerts)
		{
			return TradePlanMonitor(Input, b0, b2, b4, b6, r2, r4, r6, fastEmaPeriod, slowEmaPeriod, volSmaPeriod, rsiPeriod, enableBreakoutAlerts, enableReversalAlerts, enableVolumeAlerts, enableRsiAlerts);
		}

		public TradePlanMonitor TradePlanMonitor(ISeries<double> input, double b0, double b2, double b4, double b6, double r2, double r4, double r6, int fastEmaPeriod, int slowEmaPeriod, int volSmaPeriod, int rsiPeriod, bool enableBreakoutAlerts, bool enableReversalAlerts, bool enableVolumeAlerts, bool enableRsiAlerts)
		{
			if (cacheTradePlanMonitor != null)
				for (int idx = 0; idx < cacheTradePlanMonitor.Length; idx++)
					if (cacheTradePlanMonitor[idx] != null && cacheTradePlanMonitor[idx].B0 == b0 && cacheTradePlanMonitor[idx].B2 == b2 && cacheTradePlanMonitor[idx].B4 == b4 && cacheTradePlanMonitor[idx].B6 == b6 && cacheTradePlanMonitor[idx].R2 == r2 && cacheTradePlanMonitor[idx].R4 == r4 && cacheTradePlanMonitor[idx].R6 == r6 && cacheTradePlanMonitor[idx].FastEmaPeriod == fastEmaPeriod && cacheTradePlanMonitor[idx].SlowEmaPeriod == slowEmaPeriod && cacheTradePlanMonitor[idx].VolSmaPeriod == volSmaPeriod && cacheTradePlanMonitor[idx].RsiPeriod == rsiPeriod && cacheTradePlanMonitor[idx].EnableBreakoutAlerts == enableBreakoutAlerts && cacheTradePlanMonitor[idx].EnableReversalAlerts == enableReversalAlerts && cacheTradePlanMonitor[idx].EnableVolumeAlerts == enableVolumeAlerts && cacheTradePlanMonitor[idx].EnableRsiAlerts == enableRsiAlerts && cacheTradePlanMonitor[idx].EqualsInput(input))
						return cacheTradePlanMonitor[idx];
			return CacheIndicator<TradePlanMonitor>(new TradePlanMonitor(){ B0 = b0, B2 = b2, B4 = b4, B6 = b6, R2 = r2, R4 = r4, R6 = r6, FastEmaPeriod = fastEmaPeriod, SlowEmaPeriod = slowEmaPeriod, VolSmaPeriod = volSmaPeriod, RsiPeriod = rsiPeriod, EnableBreakoutAlerts = enableBreakoutAlerts, EnableReversalAlerts = enableReversalAlerts, EnableVolumeAlerts = enableVolumeAlerts, EnableRsiAlerts = enableRsiAlerts }, input, ref cacheTradePlanMonitor);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TradePlanMonitor TradePlanMonitor(double b0, double b2, double b4, double b6, double r2, double r4, double r6, int fastEmaPeriod, int slowEmaPeriod, int volSmaPeriod, int rsiPeriod, bool enableBreakoutAlerts, bool enableReversalAlerts, bool enableVolumeAlerts, bool enableRsiAlerts)
		{
			return indicator.TradePlanMonitor(Input, b0, b2, b4, b6, r2, r4, r6, fastEmaPeriod, slowEmaPeriod, volSmaPeriod, rsiPeriod, enableBreakoutAlerts, enableReversalAlerts, enableVolumeAlerts, enableRsiAlerts);
		}

		public Indicators.TradePlanMonitor TradePlanMonitor(ISeries<double> input , double b0, double b2, double b4, double b6, double r2, double r4, double r6, int fastEmaPeriod, int slowEmaPeriod, int volSmaPeriod, int rsiPeriod, bool enableBreakoutAlerts, bool enableReversalAlerts, bool enableVolumeAlerts, bool enableRsiAlerts)
		{
			return indicator.TradePlanMonitor(input, b0, b2, b4, b6, r2, r4, r6, fastEmaPeriod, slowEmaPeriod, volSmaPeriod, rsiPeriod, enableBreakoutAlerts, enableReversalAlerts, enableVolumeAlerts, enableRsiAlerts);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TradePlanMonitor TradePlanMonitor(double b0, double b2, double b4, double b6, double r2, double r4, double r6, int fastEmaPeriod, int slowEmaPeriod, int volSmaPeriod, int rsiPeriod, bool enableBreakoutAlerts, bool enableReversalAlerts, bool enableVolumeAlerts, bool enableRsiAlerts)
		{
			return indicator.TradePlanMonitor(Input, b0, b2, b4, b6, r2, r4, r6, fastEmaPeriod, slowEmaPeriod, volSmaPeriod, rsiPeriod, enableBreakoutAlerts, enableReversalAlerts, enableVolumeAlerts, enableRsiAlerts);
		}

		public Indicators.TradePlanMonitor TradePlanMonitor(ISeries<double> input , double b0, double b2, double b4, double b6, double r2, double r4, double r6, int fastEmaPeriod, int slowEmaPeriod, int volSmaPeriod, int rsiPeriod, bool enableBreakoutAlerts, bool enableReversalAlerts, bool enableVolumeAlerts, bool enableRsiAlerts)
		{
			return indicator.TradePlanMonitor(input, b0, b2, b4, b6, r2, r4, r6, fastEmaPeriod, slowEmaPeriod, volSmaPeriod, rsiPeriod, enableBreakoutAlerts, enableReversalAlerts, enableVolumeAlerts, enableRsiAlerts);
		}
	}
}

#endregion
