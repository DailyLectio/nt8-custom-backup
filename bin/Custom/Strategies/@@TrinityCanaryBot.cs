// CC BY-NC 4.0
#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Trinity_Canary_Bot : Strategy
    {
        // ===== Parameters =====

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Scout Size (Initial)", GroupName = "1. Position Sizing", Order = 0)]
        public int ScoutSize { get; set; } = 2;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Add-On Size (Strong)", GroupName = "1. Position Sizing", Order = 1)]
        public int AddOnSize { get; set; } = 2;

        // --- Momentum Gates ---
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ADX Period", GroupName = "2. Trinity Logic", Order = 0)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Trend Strength (DI Value)", GroupName = "2. Trinity Logic", Order = 1)]
        public double TrendStrength { get; set; } = 35.0;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Min ADX Value", GroupName = "2. Trinity Logic", Order = 2)]
        public double MinAdxValue { get; set; } = 20.0;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Max Chop (CI) Value", GroupName = "2. Trinity Logic", Order = 3)]
        public double MaxCiValue { get; set; } = 60.0;

        // --- Exit Control ---
        [NinjaScriptProperty]
        [Display(Name = "Use Slope Exit", Description="If TRUE, exits when ADX or Dominant DI slopes down.", GroupName = "2. Trinity Logic", Order = 4)]
        public bool UseSlopeExit { get; set; } = true;

        // --- Risk Management ---
        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "Profit Target (ATR Mult)", GroupName = "3. Risk/Reward", Order = 0)]
        public double ProfitAtrMult { get; set; } = 0.88;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "Stop Loss (ATR Mult)", GroupName = "3. Risk/Reward", Order = 1)]
        public double StopAtrMult { get; set; } = 0.75;

        // ===== Internals =====
        private ADX adx;
        private ATR atr;
        private ChoppinessIndex ci; 
        private DM dm;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Trinity_Canary_Bot";
                Calculate = Calculate.OnBarClose; 
                EntriesPerDirection = 2;          
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                
                UseSlopeExit = true; // Default to the requested behavior
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(AdxPeriod);
                dm  = DM(AdxPeriod);
                atr = ATR(14);
                ci  = ChoppinessIndex(14);

                AddChartIndicator(adx);
                AddChartIndicator(ci); 
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 20) return;

            // --- 1. Get Data ---
            double currAdx = adx[0];
            double prevAdx = adx[1];
            double currChop = ci[0];
            
            double diPlus = dm.DiPlus[0];
            double diMinus = dm.DiMinus[0];
            double prevDiPlus = dm.DiPlus[1];
            double prevDiMinus = dm.DiMinus[1];

            bool isAdxRising = currAdx > prevAdx;
            bool isChopSafe = currChop < MaxCiValue; // Hard filter for entries
            bool isAdxSafe = currAdx > MinAdxValue;

            // Slope Checks
            bool adxFalling = currAdx < prevAdx;
            
            // Check dominant DI slope
            bool dominantDiFalling = false;
            if (diPlus > diMinus) dominantDiFalling = (diPlus < prevDiPlus);
            else dominantDiFalling = (diMinus < prevDiMinus);

            // --- 2. Define Signals ---

            // A. Alert (Cross) - Ignition
            bool bullCross = (diPlus > diMinus) && (prevDiPlus <= prevDiMinus);
            bool bearCross = (diMinus > diPlus) && (prevDiMinus <= prevDiPlus);

            // B. Strong (Add-On Condition)
            // Strict: Must have rising ADX and Rising Dominant DI
            bool bullStrong = isAdxSafe && (diPlus > diMinus) && isAdxRising && !dominantDiFalling && (diPlus > TrendStrength);
            bool bearStrong = isAdxSafe && (diMinus > diPlus) && isAdxRising && !dominantDiFalling && (diMinus > TrendStrength);

            // C. Exit Logic
            // 1. Hard Reversal (Always Exit)
            bool bullReversal = (diPlus < diMinus);
            bool bearReversal = (diMinus < diPlus);

            // 2. Slope Weakness (Conditional Exit)
            bool momentumFailed = (adxFalling || dominantDiFalling);

            bool shouldExitLong = bullReversal;
            bool shouldExitShort = bearReversal;

            if (UseSlopeExit && momentumFailed)
            {
                shouldExitLong = true;
                shouldExitShort = true;
            }

            // --- 3. Execution Logic ---

            // Exit Logic (Priority 1)
            if (Position.MarketPosition == MarketPosition.Long && shouldExitLong)
            {
                ExitLong();
                return;
            }
            if (Position.MarketPosition == MarketPosition.Short && shouldExitShort)
            {
                ExitShort();
                return;
            }

            // Entry Logic - Scout (Priority 2)
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (isChopSafe)
                {
                    if (bullCross)
                    {
                        SetStopAndTarget(true); 
                        EnterLong(ScoutSize, "Scout_Long");
                    }
                    else if (bearCross)
                    {
                        SetStopAndTarget(false);
                        EnterShort(ScoutSize, "Scout_Short");
                    }
                }
            }

            // Scaling Logic - Strong (Priority 3)
            // Only add if we are already in a position and haven't scaled yet
            if (Position.MarketPosition == MarketPosition.Long && Position.Quantity == ScoutSize)
            {
                if (bullStrong)
                {
                    EnterLong(AddOnSize, "Strong_AddOn");
                }
            }
            if (Position.MarketPosition == MarketPosition.Short && Position.Quantity == ScoutSize)
            {
                if (bearStrong)
                {
                    EnterShort(AddOnSize, "Strong_AddOn");
                }
            }
        }

        private void SetStopAndTarget(bool isLong)
        {
            double atrVal = atr[0];
            double stopDist = Math.Max(atrVal * StopAtrMult, 8 * TickSize);
            double targetDist = Math.Max(atrVal * ProfitAtrMult, 10 * TickSize);

            if (isLong)
            {
                SetStopLoss("Scout_Long", CalculationMode.Price, Close[0] - stopDist, false);
                SetProfitTarget("Scout_Long", CalculationMode.Price, Close[0] + targetDist);
            }
            else
            {
                SetStopLoss("Scout_Short", CalculationMode.Price, Close[0] + stopDist, false);
                SetProfitTarget("Scout_Short", CalculationMode.Price, Close[0] - targetDist);
            }
        }
    }
}