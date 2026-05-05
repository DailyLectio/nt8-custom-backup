// CC BY-NC 4.0
// Stage 1 trade logging added 2026-05-04 — matches V3D per-bot log schema.
// Uses V3CTradeLogger (shared helper, must be compiled first).
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
    public class V3_Expansion_Rider_V3C : Strategy
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
        [Display(Name="Account Name Filter", Description="Exact NT8 account name. Trade log only writes when account matches.", GroupName="0b. Trade Logging", Order=0)]
        public string AccountNameFilter { get; set; } = "";

        [NinjaScriptProperty]
        [Display(Name="Trade Log Folder", Description="Folder where per-account TradeLog CSV is written.", GroupName="0b. Trade Logging", Order=1)]
        public string TradeLogFolder { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3C\TradeLog";

        // ===== 1. SETTINGS =====
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
        private int bricksInExpansion = 0;
        
        private double leg2TrailingStop = 0.0;
        private bool leg1Hit = false;
        private int oppositeBrickCount = 0;

        // Stage 1 trade logger
        private V3CTradeLogger _logger;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = "V3C Regime-Native: Expansion Rider (UniRenko Multi-Leg)";
                Name                                        = "V3_Expansion_Rider_V3C";
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
                _logger = new V3CTradeLogger(this, AccountNameFilter, "V3C", TradeLogFolder);
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

            // 1. V3C REGIME GATEKEEPER
            bool expansionAllowed = IsExpansionAllowed(out bool allowLong, out bool allowShort);
            bool contractsValid = TotalContracts >= 2 && TotalContracts % 2 == 0;

            if (!contractsValid)
            {
                DebugGate("Blocked: TotalContracts must be even and at least 2");
                expansionAllowed = false;
            }

            if (expansionAllowed)
                bricksInExpansion++;
            else
                bricksInExpansion = 0;

            // 2. ENTRY LOGIC
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ClearLocals();

                if (expansionAllowed && bricksInExpansion >= WaitBricks)
                {
                    bool isGreenBrick = Close[0] > Open[0];
                    bool isRedBrick = Close[0] < Open[0];
                    double riskTicks = (atr[0] * InitialRiskAtr) / TickSize;

                    if (isGreenBrick && allowLong)
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
                    else if (isRedBrick && allowShort)
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
                    if (Position.MarketPosition == MarketPosition.Long) ExitLong(Position.Quantity, "Wobble Eject", "Leg2");
                    if (Position.MarketPosition == MarketPosition.Short) ExitShort(Position.Quantity, "Wobble Eject", "Leg2");
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

        private bool IsExpansionAllowed(out bool allowLong, out bool allowShort)
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

            if (!string.Equals(hud.FinalRegime, "TREND_EXPANSION", StringComparison.OrdinalIgnoreCase))
            {
                DebugGate("Blocked: FinalRegime=" + hud.FinalRegime);
                return false;
            }

            if (!hud.IsExpansionBotAllowed)
            {
                DebugGate("Blocked: ExpansionBot OFF");
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

        // =========================================================================
        // STAGE 1 TRADE LOGGING — delegates to V3CTradeLogger
        // =========================================================================
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
            // Capture initial stop price as soon as the stop order is live
            if (orderState == OrderState.Working)
                _logger?.OnStopOrderSubmitted(order);
        }
    }
}
