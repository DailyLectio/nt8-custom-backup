#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class TrinityDataBridge : Indicator
    {
        // Session State Tracking
        private bool inRTH = false;
        private bool inETH = false;

        // Current RTH Tracking
        private double totalRthVol;
        private Dictionary<double, double> volumeAtPriceRTH;
        private double tempRthHigh, tempRthLow;
        private double devVAH, devVAL, devPOC;
        private double currentIBH, currentIBL;

        // Current ETH Tracking
        private double totalEthVol;
        private Dictionary<double, double> volumeAtPriceETH;
        private double tempEthHigh, tempEthLow;

        // Saved Prior Day (RTH) Levels
        private double pdh, pdl, pdc, yVAH, yVAL, yPOC;

        // Saved Overnight (ETH) Levels
        private double onh, onl, on_poc;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Trinity Data Bridge";
                Description = "Calculates all 14 Playbook Levels (Prior Day, Overnight, Developing) and bridges them to HUDMessenger.";
                Calculate = Calculate.OnEachTick; 
                IsOverlay = true;
                IsVisible = false; // Runs invisibly in the background
            }
            else if (State == State.DataLoaded)
            {
                volumeAtPriceRTH = new Dictionary<double, double>();
                volumeAtPriceETH = new Dictionary<double, double>();
                
                // Initialize defaults to avoid errors before 3 days of data load
                ResetRTHTrackers();
                ResetETHTrackers();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            int timeNow = ToTime(Time[0]);
            double price = Instrument.MasterInstrument.RoundToTickSize(Close[0]);
            double vol = Volume[0]; // OnTick, this is the exact tick volume

            // Define explicit session windows
            bool isRTH = timeNow >= 93000 && timeNow < 170000;
            bool isMaint = timeNow >= 170000 && timeNow < 180000; // The 5PM to 6PM halt
            bool isETH = timeNow >= 180000 || timeNow < 93000;

            // ==========================================
            // SESSION TRANSITION LOGIC
            // ==========================================

            // 1. 5:00 PM EST -> RTH CLOSES
            if (isMaint && inRTH)
            {
                // Lock in Prior Day Levels
                pdh = tempRthHigh;
                pdl = tempRthLow;
                pdc = Close[0]; // 17:00 Closing Price
                
                CalculateProfile(volumeAtPriceRTH, totalRthVol, out yPOC, out yVAH, out yVAL);
                inRTH = false; 
            }

            // 2. 6:00 PM EST -> ETH OPENS (Or Sunday Open)
            else if (isETH && !inETH)
            {
                ResetETHTrackers();
                inETH = true;
            }

            // 3. 9:30 AM EST -> RTH OPENS
            else if (isRTH && !inRTH)
            {
                // Lock in Overnight Levels
                onh = tempEthHigh;
                onl = tempEthLow;
                CalculateProfile(volumeAtPriceETH, totalEthVol, out on_poc, out _, out _); // Only need ON_POC

                ResetRTHTrackers();
                inRTH = true;
                inETH = false; 
            }

            // ==========================================
            // DATA ACCUMULATION LOGIC
            // ==========================================
            
            if (isRTH)
            {
                if (!volumeAtPriceRTH.ContainsKey(price)) volumeAtPriceRTH[price] = 0;
                volumeAtPriceRTH[price] += vol;
                totalRthVol += vol;

                tempRthHigh = Math.Max(tempRthHigh, High[0]);
                tempRthLow = Math.Min(tempRthLow, Low[0]);

                // Initial Balance (9:30 - 10:30)
                if (timeNow <= 103000)
                {
                    currentIBH = Math.Max(currentIBH, High[0]);
                    currentIBL = Math.Min(currentIBL, Low[0]);
                }

                // Continuously calculate developing RTH profile
                CalculateProfile(volumeAtPriceRTH, totalRthVol, out devPOC, out devVAH, out devVAL);
            }
            else if (isETH)
            {
                if (!volumeAtPriceETH.ContainsKey(price)) volumeAtPriceETH[price] = 0;
                volumeAtPriceETH[price] += vol;
                totalEthVol += vol;

                tempEthHigh = Math.Max(tempEthHigh, High[0]);
                tempEthLow = Math.Min(tempEthLow, Low[0]);
            }

            // ==========================================
            // BROADCAST ALL 14 LEVELS TO HUD MESSENGER
            // ==========================================
            if (HUDMessenger.SharedLevelMap != null)
            {
                // 1. Developing RTH
                HUDMessenger.SharedLevelMap["Dev_VAH"] = devVAH;
                HUDMessenger.SharedLevelMap["Dev_VAL"] = devVAL;
                HUDMessenger.SharedLevelMap["Dev_POC"] = devPOC;
                HUDMessenger.SharedLevelMap["IBH"] = currentIBH == double.MinValue ? 0 : currentIBH;
                HUDMessenger.SharedLevelMap["IBL"] = currentIBL == double.MaxValue ? 0 : currentIBL;
                
                // 2. Prior Day RTH
                HUDMessenger.SharedLevelMap["PDH"] = pdh == double.MinValue ? 0 : pdh;
                HUDMessenger.SharedLevelMap["PDL"] = pdl == double.MaxValue ? 0 : pdl;
                HUDMessenger.SharedLevelMap["PDC"] = pdc;
                HUDMessenger.SharedLevelMap["yVAH"] = yVAH;
                HUDMessenger.SharedLevelMap["yVAL"] = yVAL;
                HUDMessenger.SharedLevelMap["yPOC"] = yPOC;
                
                // 3. Overnight ETH
                HUDMessenger.SharedLevelMap["ONH"] = onh == double.MinValue ? 0 : onh;
                HUDMessenger.SharedLevelMap["ONL"] = onl == double.MaxValue ? 0 : onl;
                HUDMessenger.SharedLevelMap["ON_POC"] = on_poc;
            }
        }

        // ==========================================
        // HELPER METHODS
        // ==========================================
        private void ResetRTHTrackers()
        {
            volumeAtPriceRTH.Clear();
            totalRthVol = 0;
            tempRthHigh = double.MinValue;
            tempRthLow = double.MaxValue;
            currentIBH = double.MinValue;
            currentIBL = double.MaxValue;
            devVAH = 0; devVAL = 0; devPOC = 0;
        }

        private void ResetETHTrackers()
        {
            volumeAtPriceETH.Clear();
            totalEthVol = 0;
            tempEthHigh = double.MinValue;
            tempEthLow = double.MaxValue;
        }

        private void CalculateProfile(Dictionary<double, double> volMap, double totalVol, out double poc, out double vah, out double val)
        {
            poc = 0; vah = 0; val = 0;
            if (volMap.Count == 0 || totalVol == 0) return;

            // 1. Find the POC
            double maxVol = 0;
            foreach (var kvp in volMap)
            {
                if (kvp.Value > maxVol)
                {
                    maxVol = kvp.Value;
                    poc = kvp.Key;
                }
            }

            // 2. Expand outward to calculate 70% Value Area
            double targetVA_Volume = totalVol * 0.70;
            double currentVA_Volume = maxVol;
            vah = poc;
            val = poc;
            double tick = TickSize;

            while (currentVA_Volume < targetVA_Volume)
            {
                double nextHigh = vah + tick;
                double nextLow = val - tick;

                double volHigh = volMap.ContainsKey(nextHigh) ? volMap[nextHigh] : 0;
                double volLow = volMap.ContainsKey(nextLow) ? volMap[nextLow] : 0;

                if (volHigh == 0 && volLow == 0) break; // Reached extreme edges of profile

                if (volHigh >= volLow)
                {
                    vah = nextHigh;
                    currentVA_Volume += volHigh;
                }
                else
                {
                    val = nextLow;
                    currentVA_Volume += volLow;
                }
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TrinityDataBridge[] cacheTrinityDataBridge;
		public TrinityDataBridge TrinityDataBridge()
		{
			return TrinityDataBridge(Input);
		}

		public TrinityDataBridge TrinityDataBridge(ISeries<double> input)
		{
			if (cacheTrinityDataBridge != null)
				for (int idx = 0; idx < cacheTrinityDataBridge.Length; idx++)
					if (cacheTrinityDataBridge[idx] != null &&  cacheTrinityDataBridge[idx].EqualsInput(input))
						return cacheTrinityDataBridge[idx];
			return CacheIndicator<TrinityDataBridge>(new TrinityDataBridge(), input, ref cacheTrinityDataBridge);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TrinityDataBridge TrinityDataBridge()
		{
			return indicator.TrinityDataBridge(Input);
		}

		public Indicators.TrinityDataBridge TrinityDataBridge(ISeries<double> input )
		{
			return indicator.TrinityDataBridge(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TrinityDataBridge TrinityDataBridge()
		{
			return indicator.TrinityDataBridge(Input);
		}

		public Indicators.TrinityDataBridge TrinityDataBridge(ISeries<double> input )
		{
			return indicator.TrinityDataBridge(input);
		}
	}
}

#endregion
