// CC BY-NC 4.0
#region Using
using System;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools; // Required for HUD Drawing
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class TrinityCanaryBot_v3 : Strategy
    {
        // ===== State Machine Enum =====
        public enum CanaryState 
        { 
            Searching, 
            WaitingRoom, 
            ResetSlope, 
            GateKiss 
        }

        private CanaryState currentState = CanaryState.Searching;

        // ===== Parameters =====
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Contracts (MNQ)", GroupName = "1. Position", Order = 0)]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ADX Period", GroupName = "2. Trinity Logic", Order = 1)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(1, 10)]
        [Display(Name = "ADX Slope Lookback (Bars)", Description = "Number of bars to calculate true slope", GroupName = "2. Trinity Logic", Order = 2)]
        public int AdxSlopeBars { get; set; } = 3;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "ADX Peak Threshold", GroupName = "2. Trinity Logic", Order = 3)]
        public double AdxPeakThreshold { get; set; } = 25.0; // Lowered to catch standard rotations

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "ADX Recharge High (Zone Top)", GroupName = "2. Trinity Logic", Order = 4)]
        public double AdxRechargeHigh { get; set; } = 35.0;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Chop (CI) Threshold", GroupName = "2. Trinity Logic", Order = 5)]
        public double CiThreshold { get; set; } = 60.0;

        // --- Time Filters ---
        [NinjaScriptProperty]
        [Display(Name = "Start Time (HHmmss)", GroupName = "3. Time Filters", Order = 1)]
        public int StartTime { get; set; } = 93000;

        [NinjaScriptProperty]
        [Display(Name = "End Time (HHmmss)", GroupName = "3. Time Filters", Order = 2)]
        public int EndTime { get; set; } = 113000;

        // --- Risk Management ---
        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "Profit Target (ATR Mult)", GroupName = "4. Risk", Order = 1)]
        public double ProfitAtrMult { get; set; } = 1.5;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "Stop Loss (ATR Mult)", GroupName = "4. Risk", Order = 2)]
        public double StopAtrMult { get; set; } = 1.0;

        // ===== Internals =====
        private ADX adx;
        private ATR atr;
        private DM dm;
        private ChoppinessIndex ci;
        private string hudText = "";
        private Brush hudColor = Brushes.Gray;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Trinity Canary Bot v3";
                Calculate = Calculate.OnPriceChange; // Updates HUD tick-by-tick
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(AdxPeriod);
                dm = DM(AdxPeriod);
                atr = ATR(14);
                ci = ChoppinessIndex(14);

                AddChartIndicator(adx);
                AddChartIndicator(ci);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(20, AdxSlopeBars)) return;

            // --- 1. Calculate Multi-Bar Slope ---
            double currAdx = adx[0];
            double oldAdx = adx[AdxSlopeBars];
            double adxChange = currAdx - oldAdx;
            
            bool isAdxDropping = adxChange < -0.5; // Filter out flat noise
            bool isAdxHookingUp = adxChange > 0.5;

            double currChop = ci[0];
            double diPlus = dm.DiPlus[0];
            double diMinus = dm.DiMinus[0];

            int timeNow = ToTime(Time[0]);
            bool isTradingWindow = timeNow >= StartTime && timeNow <= EndTime;

            // --- 2. State Machine Logic ---
            if (!isTradingWindow)
            {
                currentState = CanaryState.Searching; // Reset outside hours
            }
            else
            {
                switch (currentState)
                {
                    case CanaryState.Searching:
                        // Looking for the Warning Setup (ADX dropped from Peak, Chop is high)
                        if (currAdx >= AdxPeakThreshold && isAdxDropping && currChop >= CiThreshold)
                        {
                            currentState = CanaryState.WaitingRoom;
                        }
                        break;

                    case CanaryState.WaitingRoom:
                        // Waiting for ADX to drop into Recharge Zone
                        if (currAdx <= AdxRechargeHigh)
                        {
                            currentState = CanaryState.ResetSlope;
                        }
                        else if (isAdxHookingUp && currAdx > AdxPeakThreshold) 
                        {
                            currentState = CanaryState.Searching; // Failed setup, reset
                        }
                        break;

                    case CanaryState.ResetSlope:
                        // Patiently waiting for the J-Hook and Chop drop
                        if (isAdxHookingUp && currChop < CiThreshold)
                        {
                            currentState = CanaryState.GateKiss;
                            ExecuteCanaryTrade(diPlus, diMinus);
                        }
                        // If ADX surges back up without triggering, reset the cycle
                        else if (currAdx > AdxRechargeHigh + 10 && isAdxHookingUp)
                        {
                            currentState = CanaryState.Searching; 
                        }
                        break;

                    case CanaryState.GateKiss:
                        // Reset back to searching once flat
                        if (Position.MarketPosition == MarketPosition.Flat)
                        {
                            currentState = CanaryState.Searching;
                        }
                        break;
                }
            }

            // --- 3. Dynamic HUD Rendering ---
            UpdateHUD(currAdx, adxChange, currChop);
        }

        private void ExecuteCanaryTrade(double diPlus, double diMinus)
        {
            if (Position.MarketPosition != MarketPosition.Flat) return;

            double atrVal = atr[0];
            double stopDist = Math.Max(atrVal * StopAtrMult, 10 * TickSize);
            double targetDist = Math.Max(atrVal * ProfitAtrMult, 20 * TickSize);

            if (diPlus > diMinus)
            {
                SetStopLoss(CalculationMode.Price, Close[0] - stopDist);
                SetProfitTarget(CalculationMode.Price, Close[0] + targetDist);
                EnterLong(Contracts, "Canary_Long");
            }
            else if (diMinus > diPlus)
            {
                SetStopLoss(CalculationMode.Price, Close[0] + stopDist);
                SetProfitTarget(CalculationMode.Price, Close[0] - targetDist);
                EnterShort(Contracts, "Canary_Short");
            }
        }

        private void UpdateHUD(double currAdx, double adxChange, double currChop)
        {
            string slopeText = adxChange > 0.5 ? "UP" : (adxChange < -0.5 ? "DOWN" : "FLAT");
            string orderStatus = Position.MarketPosition == MarketPosition.Flat ? "FLAT" : "LIVE";
            string stateDisplay = "";

            switch (currentState)
            {
                case CanaryState.Searching:
                    stateDisplay = "SEARCHING";
                    hudColor = Brushes.LightSlateGray;
                    break;
                case CanaryState.WaitingRoom:
                    stateDisplay = "WAITING ROOM";
                    hudColor = Brushes.Crimson;
                    break;
                case CanaryState.ResetSlope:
                    // Visual early warning if Chop drops before ADX hooks
                    if (currChop < CiThreshold)
                    {
                        stateDisplay = "CHOP ALERT - AWAITING HOOK";
                        hudColor = Brushes.DarkOrange;
                    }
                    else
                    {
                        stateDisplay = "RESET SLOPE (WAITING)";
                        hudColor = Brushes.Gold;
                    }
                    break;
                case CanaryState.GateKiss:
                    stateDisplay = orderStatus == "LIVE" ? "GATE KISS (LIVE)" : "GATE KISS";
                    hudColor = Brushes.LimeGreen;
                    break;
            }

            hudText = $"TRINITY CANARY v3\n" +
                      $"-----------------\n" +
                      $"STATUS: {stateDisplay}\n" +
                      $"ADX: {currAdx:F1} | {slopeText}\n" +
                      $"CHOP: {currChop:F1}\n" +
                      $"ORDER: {orderStatus}";

            // Draw HUD in top right (using "this" so NT8 knows the Strategy owns the text)
            Draw.TextFixed(this, "CanaryHUD", hudText, TextPosition.TopRight, hudColor, new SimpleFont("Arial", 14) { Bold = true }, Brushes.Transparent, Brushes.Transparent, 0);
        }
    }
}