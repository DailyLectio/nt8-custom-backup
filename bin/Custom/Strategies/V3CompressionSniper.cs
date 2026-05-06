// CC BY-NC 4.0
// ============================================================================
// V3_Compression_Sniper (LEGACY — FILE-BASED REGIME READER)
// ============================================================================
// DEPRECATION NOTICE — 2026-05-06 Audit
// ============================================================================
// This file reads regime context from a CSV file on disk inside OnBarUpdate,
// which means file I/O runs on EVERY bar update. This creates:
//   - Potential file-lock collisions with the regime writer process
//   - Latency on 1-minute bars in live trading
//   - Silent failures (try/catch swallows all I/O errors)
//
// ACTION REQUIRED: Remove this strategy from all active live/sim charts.
// Use V3_Compression_Sniper_V3C_PATCHED.cs instead, which reads from the
// in-memory V3C HUD singleton with no file I/O overhead.
//
// KNOWN ISSUES (from 2026-05-06 audit):
//   [HIGH]  riskTicks and rewardTicks typed as double — floating-point
//           imprecision can cause unexpected stop/target rounding.
//   [HIGH]  No session trade cap or cooldown — can overtrade on 1m bars.
//   [MEDIUM] Simultaneous long/short eligibility not protected.
//   [MEDIUM] Slow EMA touch looks back 2 bars — potentially stale.
//   [LOW]   No minimum stop tick floor.
// ============================================================================
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
        [Display(Name="Fixed Target (ATR)", GroupName="2. Risk Management", Order=1)]
        public double TargetAtr { get; set; } = 0.75;
        // AUDIT NOTE: 0.75 = 1:1 R:R with StopAtr 0.75. Below minimum recommended 1.25:1.

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name="Stop Loss (ATR)", GroupName="2. Risk Management", Order=2)]
        public double StopAtr { get; set; } = 1.0;

        // ===== 3. INDICATOR TUNING =====
        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name="Fast EMA Period", GroupName="3. Indicator Tuning", Order=0)]
        public int FastEmaPeriod { get; set; } = 9;

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name="Slow EMA Period", GroupName="3. Indicator Tuning", Order=1)]
        public int SlowEmaPeriod { get; set; } = 21;

        // ===== INTERNAL STATE =====
        private ATR atr;
        private EMA fastEma;
        private EMA slowEma;
        private string currentPlaybook = "UNKNOWN";
        private string currentMacro    = "UNKNOWN";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = "[DEPRECATED] V3 Regime-Native: Compression Sniper";
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
                atr     = ATR(14);
                fastEma = EMA(FastEmaPeriod);
                slowEma = EMA(SlowEmaPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(SlowEmaPeriod, 22)) return;

            UpdateRegimePlaybook();
            // AUDIT ISSUE [HIGH]: File I/O runs every bar. Risk of file-lock and latency.

            if (Position.MarketPosition == MarketPosition.Flat && currentPlaybook == "TREND_COMPRESSION")
            {
                // AUDIT ISSUE [HIGH]: riskTicks is double — floating-point may cause
                // unexpected stop rounding. Should be: (int)Math.Round(...)
                double riskTicks   = (atr[0] * StopAtr)   / TickSize;
                double rewardTicks = (atr[0] * TargetAtr) / TickSize;
                // AUDIT ISSUE [MEDIUM]: No cooldown, no session cap, no circuit breaker.

                if (currentMacro.Contains("TREND_UP") || currentMacro.Contains("INITIATIVE"))
                {
                    // AUDIT ISSUE [MEDIUM]: Low[2] lookback is 2 bars stale. Tighten to Low[1].
                    bool touchedSlowEma  = Low[1] <= slowEma[1] || Low[2] <= slowEma[2];
                    bool closedAboveFast = Close[0] > fastEma[0] && Close[1] <= fastEma[1];

                    if (touchedSlowEma && closedAboveFast)
                    {
                        SetStopLoss("SnipeL",   CalculationMode.Ticks, riskTicks,   false);
                        SetProfitTarget("SnipeL", CalculationMode.Ticks, rewardTicks);
                        EnterLong(Contracts, "SnipeL");
                    }
                }
                else if (currentMacro.Contains("TREND_DOWN") || currentMacro.Contains("FAILURE"))
                {
                    // AUDIT ISSUE [MEDIUM]: High[2] lookback is 2 bars stale. Tighten to High[1].
                    bool touchedSlowEma  = High[1] >= slowEma[1] || High[2] >= slowEma[2];
                    bool closedBelowFast = Close[0] < fastEma[0] && Close[1] >= fastEma[1];

                    if (touchedSlowEma && closedBelowFast)
                    {
                        SetStopLoss("SnipeS",   CalculationMode.Ticks, riskTicks,   false);
                        SetProfitTarget("SnipeS", CalculationMode.Ticks, rewardTicks);
                        EnterShort(Contracts, "SnipeS");
                    }
                }
                // AUDIT ISSUE [MEDIUM]: No mutual exclusion — both macros could theoretically
                // satisfy conditions if the currentMacro string matches both branches.
            }
        }

        // AUDIT ISSUE [HIGH]: This method is called every bar — continuous file I/O.
        // In the V3C version, this is replaced by hud.FinalRegime (in-memory, no I/O).
        private void UpdateRegimePlaybook()
        {
            string symbol    = Instrument.MasterInstrument.Name;
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
                        currentMacro    = parts[parts.Length - 3].Trim();
                        currentPlaybook = parts[parts.Length - 1].Trim();
                    }
                }
            }
            catch { /* Silent fail — errors invisible in live trading */ }
        }
    }
}
