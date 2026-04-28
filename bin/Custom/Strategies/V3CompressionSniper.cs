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
    public class V3_Compression_Sniper : Strategy
    {
        // ===== 1. SETTINGS & CONTEXT =====
        [NinjaScriptProperty]
        [Display(Name="Data Folder Path", GroupName="1. Regime Context", Order=0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\Active";

        // ===== 2. RISK MANAGEMENT =====
        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name="Contracts", GroupName="2. Risk Management", Order=0)]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name="Fixed Target (ATR)", Description="Strict target. Breakouts fail in compression.", GroupName="2. Risk Management", Order=1)]
        public double TargetAtr { get; set; } = 0.75;

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name="Stop Loss (ATR)", Description="Hard stop behind the swing.", GroupName="2. Risk Management", Order=2)]
        public double StopAtr { get; set; } = 1.0;

        // ===== 3. INDICATOR TUNING =====
        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name="Fast EMA Period", Description="The trigger line to cross back over.", GroupName="3. Indicator Tuning", Order=0)]
        public int FastEmaPeriod { get; set; } = 9;

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name="Slow EMA Period", Description="The baseline 'Dip/Rip' zone.", GroupName="3. Indicator Tuning", Order=1)]
        public int SlowEmaPeriod { get; set; } = 21;

        // ===== INTERNAL STATE & INDICATORS =====
        private ATR atr;
        private EMA fastEma;
        private EMA slowEma;
        
        private string currentPlaybook = "UNKNOWN";
        private string currentMacro = "UNKNOWN";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = "V3 Regime-Native: Compression Sniper (Sell Rips / Buy Dips)";
                Name                                        = "V3_Compression_Sniper";
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
                fastEma = EMA(FastEmaPeriod);
                slowEma = EMA(SlowEmaPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            // Ensure enough bars exist for the slower EMA to calculate
            if (CurrentBar < Math.Max(SlowEmaPeriod, 22)) return;

            // =========================================================================
            // PHASE 1: THE REGIME GATEKEEPER
            // =========================================================================
            UpdateRegimePlaybook();

            // =========================================================================
            // PHASE 2: ENTRY LOGIC (Strictly constrained to TREND_COMPRESSION)
            // =========================================================================
            if (Position.MarketPosition == MarketPosition.Flat && currentPlaybook == "TREND_COMPRESSION")
            {
                // Calculate rigid risk/reward parameters in ticks
                double riskTicks = (atr[0] * StopAtr) / TickSize;
                double rewardTicks = (atr[0] * TargetAtr) / TickSize;

                // ---------------------------------------------------------------------
                // LONG SNIPE (Buy the Dip): 
                // Context: Macro is UP. 
                // Trigger: Price dipped below Fast EMA, touched Slow EMA, and closed back above Fast EMA.
                // ---------------------------------------------------------------------
                if (currentMacro.Contains("TREND_UP") || currentMacro.Contains("INITIATIVE"))
                {
                    bool touchedSlowEma = Low[1] <= slowEma[1] || Low[2] <= slowEma[2];
                    bool closedAboveFast = Close[0] > fastEma[0] && Close[1] <= fastEma[1];

                    if (touchedSlowEma && closedAboveFast)
                    {
                        SetStopLoss("SnipeL", CalculationMode.Ticks, riskTicks, false);
                        SetProfitTarget("SnipeL", CalculationMode.Ticks, rewardTicks);
                        EnterLong(Contracts, "SnipeL");
                    }
                }
                
                // ---------------------------------------------------------------------
                // SHORT SNIPE (Sell the Rip): 
                // Context: Macro is DOWN. 
                // Trigger: Price popped above Fast EMA, touched Slow EMA, and closed back below Fast EMA.
                // ---------------------------------------------------------------------
                else if (currentMacro.Contains("TREND_DOWN") || currentMacro.Contains("FAILURE"))
                {
                    bool touchedSlowEma = High[1] >= slowEma[1] || High[2] >= slowEma[2];
                    bool closedBelowFast = Close[0] < fastEma[0] && Close[1] >= fastEma[1];

                    if (touchedSlowEma && closedBelowFast)
                    {
                        SetStopLoss("SnipeS", CalculationMode.Ticks, riskTicks, false);
                        SetProfitTarget("SnipeS", CalculationMode.Ticks, rewardTicks);
                        EnterShort(Contracts, "SnipeS");
                    }
                }
            }
            // *NOTE: No OnBarUpdate management for active trades. Strict binary targets only.*
        }

        // =========================================================================
        // LIGHTWEIGHT CSV READER (Reads Macro Direction AND Playbook State)
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
                    // Fast forward to the most recent checkpoint
                    while ((line = sr.ReadLine()) != null) { lastLine = line; }

                    string[] parts = lastLine.Split(',');
                    if (parts.Length >= 50) 
                    {
                        // Safely grab the structural bias and the actionable playbook
                        currentMacro = parts[parts.Length - 3].Trim(); 
                        currentPlaybook = parts[parts.Length - 1].Trim(); 
                    }
                }
            }
            catch { /* Silent fail to prevent locking the UI during live file writes */ }
        }
    }
}