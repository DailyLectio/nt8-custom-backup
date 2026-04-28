// CC BY-NC 4.0
#region Using declarations
using System;
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
    public class V3_Compression_Sniper_V3C : Strategy
    {
        // ===== 0. V3C REGIME GATE =====
        [NinjaScriptProperty]
        [Display(Name="Enable V3C Trinity Filter", GroupName="0. V3C Regime Gate", Order=0)]
        public bool EnableTrinityFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Debug V3C Gate", GroupName="0. V3C Regime Gate", Order=1)]
        public bool DebugV3CGate { get; set; } = false;

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

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = "V3C Regime-Native: Compression Sniper (Sell Rips / Buy Dips)";
                Name                                        = "V3_Compression_Sniper_V3C";
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

            bool compressionAllowed = IsCompressionAllowed(out bool allowLong, out bool allowShort);

            // =========================================================================
            // PHASE 2: ENTRY LOGIC (Strictly constrained to V3C TREND_COMPRESSION)
            // =========================================================================
            if (Position.MarketPosition == MarketPosition.Flat && compressionAllowed)
            {
                // Calculate rigid risk/reward parameters in ticks
                int riskTicks = Math.Max(1, (int)Math.Round((atr[0] * StopAtr) / TickSize));
                int rewardTicks = Math.Max(1, (int)Math.Round((atr[0] * TargetAtr) / TickSize));

                // ---------------------------------------------------------------------
                // LONG SNIPE (Buy the Dip): 
                // Context: V3C direction allows longs.
                // Trigger: Price dipped below Fast EMA, touched Slow EMA, and closed back above Fast EMA.
                // ---------------------------------------------------------------------
                if (allowLong)
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
                // Context: V3C direction allows shorts.
                // Trigger: Price popped above Fast EMA, touched Slow EMA, and closed back below Fast EMA.
                // ---------------------------------------------------------------------
                if (allowShort)
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

        private bool IsCompressionAllowed(out bool allowLong, out bool allowShort)
        {
            allowLong = false;
            allowShort = false;

            if (!EnableTrinityFilter)
            {
                allowLong = true;
                allowShort = true;
                return true;
            }

            Indicators.RegimeMatrixHUD_V3C hud = GetV3CHud();

            if (hud == null)
            {
                DebugGate("Blocked: HUD missing");
                return false;
            }

            if (hud.StaleDataFlag)
            {
                DebugGate("Blocked: stale data");
                return false;
            }

            if (!string.Equals(hud.FinalRegime, "TREND_COMPRESSION", StringComparison.OrdinalIgnoreCase))
            {
                DebugGate("Blocked: FinalRegime=" + hud.FinalRegime);
                return false;
            }

            if (!hud.IsCompressionBotAllowed)
            {
                DebugGate("Blocked: CompressionBot OFF");
                return false;
            }

            allowLong = hud.AllowLong;
            allowShort = hud.AllowShort;

            if (!allowLong && !allowShort)
            {
                DebugGate("Blocked: direction not allowed");
                return false;
            }

            return true;
        }

        private Indicators.RegimeMatrixHUD_V3C GetV3CHud()
        {
            string chartSymbol = Instrument.MasterInstrument.Name;
            string leaderSymbol = GetLeaderSymbol(chartSymbol);

            Indicators.RegimeMatrixHUD_V3C hudInstance = null;

            if (!Indicators.RegimeMatrixHUD_V3C.InstancesV3C.TryGetValue(chartSymbol, out hudInstance))
                Indicators.RegimeMatrixHUD_V3C.InstancesV3C.TryGetValue(leaderSymbol, out hudInstance);

            return hudInstance;
        }

        private string GetLeaderSymbol(string sym)
        {
            if (string.IsNullOrEmpty(sym))
                return sym;

            sym = sym.Trim().ToUpper();

            if (sym == "MES") return "ES";
            if (sym == "MNQ") return "NQ";
            if (sym == "MGC") return "GC";
            if (sym == "MCL") return "CL";
            if (sym == "MSI") return "SI";

            return sym;
        }

        private void DebugGate(string message)
        {
            if (DebugV3CGate)
                Print($"{Time[0]} {Name} V3C Gate: {message}");
        }
    }
}
