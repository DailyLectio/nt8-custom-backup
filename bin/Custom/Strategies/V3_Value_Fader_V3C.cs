// CC BY-NC 4.0
// Stage 1 trade logging added 2026-05-04.
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
    public class V3_Value_Fader_V3C : Strategy
    {
        // ===== 0. V3C REGIME GATE =====
        [NinjaScriptProperty]
        [Display(Name="Enable V3C Trinity Filter", GroupName="0. V3C Regime Gate", Order=0)]
        public bool EnableTrinityFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Debug V3C Gate", GroupName="0. V3C Regime Gate", Order=1)]
        public bool DebugV3CGate { get; set; } = false;

        // ===== 0b. STAGE 1 TRADE LOGGING =====
        [NinjaScriptProperty]
        [Display(Name="Account Name Filter", Description="Exact NT8 account name.", GroupName="0b. Trade Logging", Order=0)]
        public string AccountNameFilter { get; set; } = "";

        [NinjaScriptProperty]
        [Display(Name="Trade Log Folder", GroupName="0b. Trade Logging", Order=1)]
        public string TradeLogFolder { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3C\TradeLog";

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
        private V3CTradeLogger _logger;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = "V3C Regime-Native: Value Fader (Fades Edges to the Mean)";
                Name                                        = "V3_Value_Fader_V3C";
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
                _logger = new V3CTradeLogger(this, AccountNameFilter, "V3C", TradeLogFolder);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BollingerPeriod + 2) return;

            bool fadeAllowed = IsFadeAllowed(out bool allowLong, out bool allowShort);

            // =========================================================================
            // PHASE 2: UNI-RENKO REVERSAL LOGIC (Strictly V3C ROTATION_LIQUID)
            // =========================================================================
            if (Position.MarketPosition == MarketPosition.Flat && fadeAllowed)
            {
                // Brick Physics
                bool isGreenBrick = Close[0] > Open[0];
                bool isRedBrick = Close[0] < Open[0];
                
                bool wasRedBrick = Close[1] < Open[1];
                bool wasGreenBrick = Close[1] > Open[1];

                int riskTicks = Math.Max(1, (int)Math.Round((atr[0] * StopAtr) / TickSize));

                // ---------------------------------------------------------------------
                // LONG FADE: We hit the Lower Band, now printing a Green Reversal
                // ---------------------------------------------------------------------
                bool touchedLowerEdge = Low[1] <= bb.Lower[1] || Low[2] <= bb.Lower[2];
                
                if (allowLong && touchedLowerEdge && wasRedBrick && isGreenBrick)
                {
                    // Calculate distance to the Mean (Middle Band)
                    double distanceToMeanTicks = (bb.Middle[0] - Close[0]) / TickSize;
                    int targetTicks = Math.Max(1, (int)Math.Round(distanceToMeanTicks));

                    if (targetTicks >= MinTargetTicks) // Only take it if room exists
                    {
                        SetStopLoss("FadeL", CalculationMode.Ticks, riskTicks, false);
                        SetProfitTarget("FadeL", CalculationMode.Ticks, targetTicks);
                        EnterLong(Contracts, "FadeL");
                    }
                }

                // ---------------------------------------------------------------------
                // SHORT FADE: We hit the Upper Band, now printing a Red Reversal
                // ---------------------------------------------------------------------
                bool touchedUpperEdge = High[1] >= bb.Upper[1] || High[2] >= bb.Upper[2];

                if (allowShort && touchedUpperEdge && wasGreenBrick && isRedBrick)
                {
                    // Calculate distance to the Mean (Middle Band)
                    double distanceToMeanTicks = (Close[0] - bb.Middle[0]) / TickSize;
                    int targetTicks = Math.Max(1, (int)Math.Round(distanceToMeanTicks));

                    if (targetTicks >= MinTargetTicks) // Only take it if room exists
                    {
                        SetStopLoss("FadeS", CalculationMode.Ticks, riskTicks, false);
                        SetProfitTarget("FadeS", CalculationMode.Ticks, targetTicks);
                        EnterShort(Contracts, "FadeS");
                    }
                }
            }
        }

        private bool IsFadeAllowed(out bool allowLong, out bool allowShort)
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

            if (!string.Equals(hud.FinalRegime, "ROTATION_LIQUID", StringComparison.OrdinalIgnoreCase))
            {
                DebugGate("Blocked: FinalRegime=" + hud.FinalRegime);
                return false;
            }

            if (!hud.IsFadeBotAllowed)
            {
                DebugGate("Blocked: FadeBot OFF");
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

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId,
            DateTime time)
        {
            _logger?.OnExecution(execution, null);
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string comment)
        {
            if (orderState == OrderState.Working)
                _logger?.OnStopOrderSubmitted(order);
        }
    }
}
