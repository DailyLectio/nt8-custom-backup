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

        [NinjaScriptProperty, Range(1, 5)]
        [Display(Name="Min Reversal Bars", Description="Consecutive reversal bricks required after band touch before entry fires. Default 2 filters premature entries.", GroupName="2. Risk Management", Order=3)]
        public int MinReversalBars { get; set; } = 2;

        // ===== 5. SAME-DIRECTION CAP =====
        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name="Max Same-Direction Trades", Description="Caps consecutive same-direction entries per session. 0 = OFF (no limit). Counter resets on a direction flip and at session start. Week-2 baseline = 0.", GroupName="5. Same-Direction Cap", Order=0)]
        public int MaxSameDirTrades { get; set; } = 0;

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
        private int longReversalCount  = 0;
        private int shortReversalCount = 0;

        // Same-direction cap state (SF-27). Param default 0 = OFF.
        private int  _sameDirCount  = 0;
        private int  _lastEntryDir  = 0;   // 1 = long, -1 = short
        private bool _dirRegistered = false;

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
            if (CurrentBar < BollingerPeriod + MinReversalBars + 1) return;

            if (Bars.IsFirstBarOfSession) ResetSameDirCounter();

            // Track consecutive reversal bricks for entry confirmation
            if      (Close[0] > Open[0]) { longReversalCount++;  shortReversalCount = 0; }
            else if (Close[0] < Open[0]) { shortReversalCount++; longReversalCount  = 0; }
            else                         { longReversalCount = 0; shortReversalCount = 0; }

            bool fadeAllowed = IsFadeAllowed(out bool allowLong, out bool allowShort);

            // =========================================================================
            // PHASE 2: UNI-RENKO REVERSAL LOGIC (Strictly V3C ROTATION_LIQUID)
            // =========================================================================
            if (Position.MarketPosition == MarketPosition.Flat && fadeAllowed)
            {
                bool isGreenBrick = Close[0] > Open[0];
                bool isRedBrick   = Close[0] < Open[0];

                int riskTicks = Math.Max(1, (int)Math.Round((atr[0] * StopAtr) / TickSize));

                // ---------------------------------------------------------------------
                // LONG FADE: band touched MinReversalBars+ bars ago, then N green bricks
                // Requires MinReversalBars consecutive green bricks ending on current bar.
                // Band-touch window is pushed back to accommodate the reversal lookback.
                // ---------------------------------------------------------------------
                bool touchedLowerEdge = Low[MinReversalBars]     <= bb.Lower[MinReversalBars]
                                     || Low[MinReversalBars + 1] <= bb.Lower[MinReversalBars + 1];

                if (allowLong && touchedLowerEdge && isGreenBrick && longReversalCount >= MinReversalBars && !SameDirBlocked(1))
                {
                    double distanceToMeanTicks = (bb.Middle[0] - Close[0]) / TickSize;
                    int targetTicks = Math.Max(1, (int)Math.Round(distanceToMeanTicks));

                    if (targetTicks >= MinTargetTicks)
                    {
                        SetStopLoss("FadeL", CalculationMode.Ticks, riskTicks, false);
                        SetProfitTarget("FadeL", CalculationMode.Ticks, targetTicks);
                        EnterLong(Contracts, "FadeL");
                    }
                }

                // ---------------------------------------------------------------------
                // SHORT FADE: band touched MinReversalBars+ bars ago, then N red bricks
                // ---------------------------------------------------------------------
                bool touchedUpperEdge = High[MinReversalBars]     >= bb.Upper[MinReversalBars]
                                     || High[MinReversalBars + 1] >= bb.Upper[MinReversalBars + 1];

                if (allowShort && touchedUpperEdge && isRedBrick && shortReversalCount >= MinReversalBars && !SameDirBlocked(-1))
                {
                    double distanceToMeanTicks = (Close[0] - bb.Middle[0]) / TickSize;
                    int targetTicks = Math.Max(1, (int)Math.Round(distanceToMeanTicks));

                    if (targetTicks >= MinTargetTicks)
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

        // ===== SAME-DIRECTION CAP (SF-27) =====
        private bool SameDirBlocked(int dir)
        {
            return MaxSameDirTrades > 0
                && dir == _lastEntryDir
                && _sameDirCount >= MaxSameDirTrades;
        }

        private void RegisterDirEntry(int dir)
        {
            if (dir == _lastEntryDir) _sameDirCount++;
            else { _lastEntryDir = dir; _sameDirCount = 1; }
        }

        private void ResetSameDirCounter()
        {
            _sameDirCount = 0;
            _lastEntryDir = 0;
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId,
            DateTime time)
        {
            _logger?.OnExecution(execution, null);

            // Same-direction cap: register once per position on the first entry fill.
            if (execution.Order != null && !_dirRegistered)
            {
                if (execution.Order.OrderAction == OrderAction.Buy)
                    { RegisterDirEntry(1);  _dirRegistered = true; }
                else if (execution.Order.OrderAction == OrderAction.SellShort)
                    { RegisterDirEntry(-1); _dirRegistered = true; }
            }
            if (marketPosition == MarketPosition.Flat)
                _dirRegistered = false;
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
