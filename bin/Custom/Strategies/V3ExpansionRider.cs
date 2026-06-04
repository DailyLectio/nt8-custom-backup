// CC BY-NC 4.0
#region Using declarations
using System;
using System.IO;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.Data;
using NinjaTrader.Core.FloatingPoint;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class V3_Expansion_Rider : Strategy
    {
        // ===== 1. SETTINGS =====
        [NinjaScriptProperty]
        [Display(Name="Data Folder Path", GroupName="1. Regime Context", Order=0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\Active";

        [NinjaScriptProperty, Range(2, 100)]
        [Display(Name="Total Contracts (Must be even)", GroupName="2. Risk Management", Order=0)]
        public int TotalContracts { get; set; } = 2;

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name="Initial Risk (ATR)", GroupName="2. Risk Management", Order=1)]
        public double InitialRiskAtr { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1, 10)]
        [Display(Name="Wait Bricks (Hysteresis Entry)", Description="Wait X bricks in Expansion before firing", GroupName="2. Risk Management", Order=2)]
        public int WaitBricks { get; set; } = 3;

        // ===== 2. INTERNAL STATE =====
        private ATR atr;
        private string currentPlaybook = "UNKNOWN";
        private int bricksInExpansion = 0;
        
        private double leg2TrailingStop = 0.0;
        private bool leg1Hit = false;
        private int oppositeBrickCount = 0;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = "V3 Regime-Native: Expansion Rider (UniRenko Multi-Leg)";
                Name                                        = "V3_Expansion_Rider";
                Calculate                                   = Calculate.OnBarClose;
                EntriesPerDirection                         = 2;
                EntryHandling                               = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy                = true;
                ExitOnSessionCloseSeconds                   = 30;
                IsFillLimitOnTouch                          = false;
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(14);
            }
        }

        private void ClearLocals()
        {
            leg2TrailingStop = 0.0;
            leg1Hit = false;
            oppositeBrickCount = 0;
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 20) return;

            // 1. REGIME GATEKEEPER
            string oldPlaybook = currentPlaybook;
            UpdateRegimePlaybook();

            if (currentPlaybook == "TREND_EXPANSION")
            {
                if (oldPlaybook != "TREND_EXPANSION") bricksInExpansion = 0;
                bricksInExpansion++;
            }
            else { bricksInExpansion = 0; }

            // 2. ENTRY LOGIC
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ClearLocals();

                if (currentPlaybook == "TREND_EXPANSION" && bricksInExpansion >= WaitBricks)
                {
                    bool isGreenBrick = Close[0] > Open[0];
                    bool isRedBrick = Close[0] < Open[0];
                    double riskTicks = (atr[0] * InitialRiskAtr) / TickSize;

                    if (isGreenBrick)
                    {
                        double stp = Close[0] - (riskTicks * TickSize);
                        double tgt1 = Close[0] + (riskTicks * TickSize);

                        SetStopLoss("Leg1", CalculationMode.Price, stp, false);
                        SetStopLoss("Leg2", CalculationMode.Price, stp, false);
                        SetProfitTarget("Leg1", CalculationMode.Price, tgt1);

                        EnterLong(TotalContracts / 2, "Leg1");
                        EnterLong(TotalContracts / 2, "Leg2");
                        leg2TrailingStop = stp;
                    }
                    else if (isRedBrick)
                    {
                        double stp = Close[0] + (riskTicks * TickSize);
                        double tgt1 = Close[0] - (riskTicks * TickSize);

                        SetStopLoss("Leg1", CalculationMode.Price, stp, false);
                        SetStopLoss("Leg2", CalculationMode.Price, stp, false);
                        SetProfitTarget("Leg1", CalculationMode.Price, tgt1);

                        EnterShort(TotalContracts / 2, "Leg1");
                        EnterShort(TotalContracts / 2, "Leg2");
                        leg2TrailingStop = stp;
                    }
                }
            }

            // 3. MULTI-LEG RISK & PARACHUTE
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (!leg1Hit && Position.Quantity <= TotalContracts / 2)
                {
                    leg1Hit = true; // Free Trade Pivot
                    leg2TrailingStop = Position.MarketPosition == MarketPosition.Long 
                        ? Position.AveragePrice + (4 * TickSize) 
                        : Position.AveragePrice - (4 * TickSize);
                    SetStopLoss("Leg2", CalculationMode.Price, leg2TrailingStop, false);
                }

                // A. THE WOBBLE EXIT (UniRenko Reversal Parachute)
                bool isRedBrick = Close[0] < Open[0];
                bool isGreenBrick = Close[0] > Open[0];

                if (Position.MarketPosition == MarketPosition.Long && isRedBrick) oppositeBrickCount++;
                else if (Position.MarketPosition == MarketPosition.Short && isGreenBrick) oppositeBrickCount++;
                else oppositeBrickCount = 0; 

                // 1 UniRenko reversal brick is enough to kill a runner in thin liquidity
                if (oppositeBrickCount >= 1) 
                {
                    ExitOpenExpansionLegs("Wobble Eject");
                    return;
                }

                // B. STEP-TRAIL THE RUNNER
                if (leg1Hit)
                {
                    double trailDistance = (atr[0] * 1.25);
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        double candidate = High[0] - trailDistance;
                        if (candidate > leg2TrailingStop) { leg2TrailingStop = candidate; SetStopLoss("Leg2", CalculationMode.Price, leg2TrailingStop, false); }
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        double candidate = Low[0] + trailDistance;
                        if (candidate < leg2TrailingStop || leg2TrailingStop == 0) { leg2TrailingStop = candidate; SetStopLoss("Leg2", CalculationMode.Price, leg2TrailingStop, false); }
                    }
                }
            }
        }

        private void UpdateRegimePlaybook()
        {
            string symbol = Instrument.MasterInstrument.Name;
            string macroFile = Path.Combine(DataFolderPath, $"{symbol}_Macro_Regimes.csv");
            if (!File.Exists(macroFile)) return;
            try {
                using (FileStream fs = new FileStream(macroFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs)) {
                    string lastLine = ""; string line;
                    while ((line = sr.ReadLine()) != null) { lastLine = line; }
                    string[] parts = lastLine.Split(',');
                    if (parts.Length >= 50) currentPlaybook = parts[parts.Length - 1].Trim(); 
                }
            } catch { }
        }

        private void ExitOpenExpansionLegs(string exitSignal)
        {
            int qty = Position.Quantity;
            if (qty <= 0 || Position.MarketPosition == MarketPosition.Flat)
                return;

            int legQty = Math.Max(1, TotalContracts / 2);

            if (leg1Hit || qty <= legQty)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong(qty, exitSignal, "Leg2");
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort(qty, exitSignal, "Leg2");
                return;
            }

            int leg2Qty = Math.Min(legQty, qty);
            int leg1Qty = qty - leg2Qty;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (leg1Qty > 0) ExitLong(leg1Qty, exitSignal, "Leg1");
                if (leg2Qty > 0) ExitLong(leg2Qty, exitSignal, "Leg2");
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (leg1Qty > 0) ExitShort(leg1Qty, exitSignal, "Leg1");
                if (leg2Qty > 0) ExitShort(leg2Qty, exitSignal, "Leg2");
            }
        }
    }
}
