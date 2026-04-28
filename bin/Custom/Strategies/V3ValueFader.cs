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
    public class V3_Value_Fader : Strategy
    {
        // ===== 1. SETTINGS & CONTEXT =====
        [NinjaScriptProperty]
        [Display(Name="Data Folder Path", GroupName="1. Regime Context", Order=0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\Active";

        // ===== 2. RISK & REWARD =====
        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name="Contracts", GroupName="2. Risk Management", Order=0)]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name="Stop Loss (ATR)", Description="Hard stop behind the bracket edge.", GroupName="2. Risk Management", Order=1)]
        public double StopAtr { get; set; } = 1.25;

        [NinjaScriptProperty, Range(4, 100)]
        [Display(Name="Minimum Target Ticks", Description="Ignore setups if the Mean is too close.", GroupName="2. Risk Management", Order=2)]
        public int MinTargetTicks { get; set; } = 10;

        // ===== 3. INDICATOR TUNING =====
        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name="Bollinger Period", Description="Defines the moving Mean value.", GroupName="3. Value Mapper", Order=0)]
        public int BollingerPeriod { get; set; } = 20;

        [NinjaScriptProperty, Range(0.5, 4.0)]
        [Display(Name="Bollinger StdDev", Description="Defines the extreme Edges of the bracket.", GroupName="3. Value Mapper", Order=1)]
        public double BollingerDev { get; set; } = 2.0;

        // ===== INTERNAL STATE =====
        private ATR atr;
        private Bollinger bb;
        private string currentPlaybook = "UNKNOWN";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = "V3 Regime-Native: Value Fader (Fades Edges to the Mean)";
                Name                                        = "V3_Value_Fader";
                Calculate                                   = Calculate.OnBarClose;
                EntriesPerDirection                         = 1;
                EntryHandling                               = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy                = true;
                ExitOnSessionCloseSeconds                   = 30;
                IsFillLimitOnTouch                          = false;
                TraceOrders                                 = false;
                RealtimeErrorHandling                       = RealtimeErrorHandling.StopCancelClose;
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(14);
                bb = Bollinger(BollingerDev, BollingerPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BollingerPeriod + 2) return;

            // =========================================================================
            // PHASE 1: THE REGIME GATEKEEPER
            // =========================================================================
            UpdateRegimePlaybook();

            // =========================================================================
            // PHASE 2: UNI-RENKO REVERSAL LOGIC (Strictly ROTATION_LIQUID)
            // =========================================================================
            if (Position.MarketPosition == MarketPosition.Flat && currentPlaybook == "ROTATION_LIQUID")
            {
                // Brick Physics
                bool isGreenBrick = Close[0] > Open[0];
                bool isRedBrick = Close[0] < Open[0];
                
                bool wasRedBrick = Close[1] < Open[1];
                bool wasGreenBrick = Close[1] > Open[1];

                double riskTicks = (atr[0] * StopAtr) / TickSize;

                // ---------------------------------------------------------------------
                // LONG FADE: We hit the Lower Band, now printing a Green Reversal
                // ---------------------------------------------------------------------
                bool touchedLowerEdge = Low[1] <= bb.Lower[1] || Low[2] <= bb.Lower[2];
                
                if (touchedLowerEdge && wasRedBrick && isGreenBrick)
                {
                    // Calculate distance to the Mean (Middle Band)
                    double distanceToMeanTicks = (bb.Middle[0] - Close[0]) / TickSize;

                    if (distanceToMeanTicks >= MinTargetTicks) // Only take it if room exists
                    {
                        SetStopLoss("FadeL", CalculationMode.Ticks, riskTicks, false);
                        SetProfitTarget("FadeL", CalculationMode.Ticks, distanceToMeanTicks);
                        EnterLong(Contracts, "FadeL");
                    }
                }

                // ---------------------------------------------------------------------
                // SHORT FADE: We hit the Upper Band, now printing a Red Reversal
                // ---------------------------------------------------------------------
                bool touchedUpperEdge = High[1] >= bb.Upper[1] || High[2] >= bb.Upper[2];

                if (touchedUpperEdge && wasGreenBrick && isRedBrick)
                {
                    // Calculate distance to the Mean (Middle Band)
                    double distanceToMeanTicks = (Close[0] - bb.Middle[0]) / TickSize;

                    if (distanceToMeanTicks >= MinTargetTicks) // Only take it if room exists
                    {
                        SetStopLoss("FadeS", CalculationMode.Ticks, riskTicks, false);
                        SetProfitTarget("FadeS", CalculationMode.Ticks, distanceToMeanTicks);
                        EnterShort(Contracts, "FadeS");
                    }
                }
            }
        }

        // =========================================================================
        // LIGHTWEIGHT CSV READER
        // =========================================================================
        private void UpdateRegimePlaybook()
        {
            string symbol = Instrument.MasterInstrument.Name;
            string macroFile = Path.Combine(DataFolderPath, $"{symbol}_Macro_Regimes.csv");

            if (!File.Exists(macroFile)) return;

            try
            {
                using (FileStream fs = new FileStream(macroFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs))
                {
                    string lastLine = "";
                    string line;
                    while ((line = sr.ReadLine()) != null) { lastLine = line; }

                    string[] parts = lastLine.Split(',');
                    if (parts.Length >= 50) 
                    {
                        currentPlaybook = parts[parts.Length - 1].Trim(); 
                    }
                }
            }
            catch { }
        }
    }
}