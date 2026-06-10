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
    public enum V3CExpansionM5DirectionMode
    {
        Both,
        LongOnly,
        ShortOnly
    }

    public class V3_Expansion_Rider_V3C_M5 : Strategy
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
        public string TradeLogFolder { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\5A\TradeLog\V3C";

        // ===== 1. M5 DIRECTION FILTER =====
        // Applied only to new Leg1 entries. Leg2, exits, stops, trailing,
        // cooldowns, and trade logging remain unchanged.
        [NinjaScriptProperty]
        [Display(Name="M5 Direction Mode", Description="Both preserves current behavior. LongOnly blocks new shorts. ShortOnly blocks new longs.", GroupName="1. M5 Direction Filter", Order=0)]
        public V3CExpansionM5DirectionMode M5DirectionMode { get; set; } = V3CExpansionM5DirectionMode.Both;

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

        [NinjaScriptProperty, Range(0.0, 1.0)]
        [Display(Name="Leg2 Profit Gate (% of Leg1 target)", Description="Leg2 fires only after Leg1 has reached this fraction of its target distance. 0.5 = 50%. Set to 0 to restore simultaneous entry.", GroupName="2. Risk Management", Order=3)]
        public double Leg2ProfitGatePct { get; set; } = 0.5;

        // ===== 3. TIME GATE (C1) =====
        // Blocks NEW entries during up to three configurable HHmm windows.
        // Position management (Leg2, wobble exit, trailing) is never affected.
        [NinjaScriptProperty]
        [Display(Name="Enable Time Blocks", Description="When true, new entries are blocked during the configured HHmm windows below.", GroupName="3. Time Gate", Order=0)]
        public bool EnableTimeBlocks { get; set; } = false;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Block 1 Start (HHmm)", Description="Start of blocked entry window, 24h HHmm (e.g. 1000). 0 = window unused.", GroupName="3. Time Gate", Order=1)]
        public int Block1Start { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Block 1 End (HHmm)", Description="End of blocked entry window, exclusive (e.g. 1130). 0 = window unused.", GroupName="3. Time Gate", Order=2)]
        public int Block1End { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Block 2 Start (HHmm)", Description="Second blocked window start. 0 = unused.", GroupName="3. Time Gate", Order=3)]
        public int Block2Start { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Block 2 End (HHmm)", Description="Second blocked window end, exclusive. 0 = unused.", GroupName="3. Time Gate", Order=4)]
        public int Block2End { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Block 3 Start (HHmm)", Description="Third blocked window start. 0 = unused.", GroupName="3. Time Gate", Order=5)]
        public int Block3Start { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Block 3 End (HHmm)", Description="Third blocked window end, exclusive. 0 = unused.", GroupName="3. Time Gate", Order=6)]
        public int Block3End { get; set; } = 0;

        // ===== 4. ENTRY COOLDOWN (C2) =====
        [NinjaScriptProperty]
        [Display(Name="Enable Entry Cooldown", Description="When true, a new entry is blocked until Cooldown Minutes have elapsed since the last position closed flat.", GroupName="4. Entry Cooldown", Order=0)]
        public bool EnableEntryCooldown { get; set; } = true;

        [NinjaScriptProperty, Range(0, 120)]
        [Display(Name="Cooldown Minutes", Description="Minutes to wait after going flat before a new entry is allowed. Default 5.", GroupName="4. Entry Cooldown", Order=1)]
        public int CooldownMinutes { get; set; } = 5;

        // ===== 5. SAME-DIRECTION CAP =====
        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name="Max Same-Direction Trades", Description="Caps consecutive same-direction entries per session. 0 = OFF (no limit). Counter resets on a direction flip and at session start. Week-2 baseline = 0.", GroupName="5. Same-Direction Cap", Order=0)]
        public int MaxSameDirTrades { get; set; } = 0;

        // ===== 2. INTERNAL STATE =====
        private ATR atr;
        private int bricksInExpansion = 0;

        private double leg2TrailingStop = 0.0;
        private bool leg1Hit = false;
        private int oppositeBrickCount = 0;

        // Leg2 deferred entry state
        private bool   awaitingLeg2   = false;
        private double leg1EntryPrice  = 0.0;
        private double leg1TargetPrice = 0.0;
        private int    tradeDir        = 0;   // 1 = long, -1 = short

        // Time gate + entry cooldown state (C1/C2)
        private DateTime lastExitTime = DateTime.MinValue;

        // Same-direction cap state (SF-27). Param default 0 = OFF.
        private int  _sameDirCount  = 0;
        private int  _lastEntryDir  = 0;   // 1 = long, -1 = short
        private bool _dirRegistered = false;

        // Stage 1 trade logger
        private V3CTradeLogger _logger;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = "Model 5A copy of V3C Expansion Rider with a new-entry direction filter.";
                Name                                        = "V3_Expansion_Rider_V3C_M5";
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
            leg1Hit          = false;
            oppositeBrickCount = 0;
            awaitingLeg2     = false;
            leg1EntryPrice   = 0.0;
            leg1TargetPrice  = 0.0;
            tradeDir         = 0;
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 20) return;

            if (Bars.IsFirstBarOfSession) ResetSameDirCounter();

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

                bool timeBlocked = InBlockedWindow();
                bool cooldownOk  = CooldownElapsed();

                if (timeBlocked)  DebugGate("Blocked: inside configured time block");
                if (!cooldownOk)  DebugGate("Blocked: entry cooldown active");

                if (expansionAllowed && bricksInExpansion >= WaitBricks && !timeBlocked && cooldownOk)
                {
                    bool isGreenBrick = Close[0] > Open[0];
                    bool isRedBrick = Close[0] < Open[0];
                    double riskTicks = (atr[0] * InitialRiskAtr) / TickSize;
                    // Whole-tick bracket distance. Ticks mode anchors the stop and
                    // target to the actual entry fill, so a session-open gap can't
                    // leave the stop on the wrong side of the market — that gets the
                    // OCO bracket rejected and the strategy terminates itself.
                    int riskTicksI = Math.Max(1, (int)Math.Round(riskTicks));

                    if (isGreenBrick && allowLong && DirectionAllowsLong() && !SameDirBlocked(1))
                    {
                        double stp  = Close[0] - (riskTicksI * TickSize);
                        double tgt1 = Close[0] + (riskTicksI * TickSize);

                        SetStopLoss("Leg1", CalculationMode.Ticks, riskTicksI, false);
                        SetProfitTarget("Leg1", CalculationMode.Ticks, riskTicksI);
                        EnterLong(TotalContracts / 2, "Leg1");

                        // Leg2 is deferred — fires only when Leg1 profit >= Leg2ProfitGatePct × target
                        leg2TrailingStop = stp;
                        leg1EntryPrice   = Close[0];
                        leg1TargetPrice  = tgt1;
                        awaitingLeg2     = true;
                        tradeDir         = 1;
                    }
                    else if (isRedBrick && allowShort && DirectionAllowsShort() && !SameDirBlocked(-1))
                    {
                        double stp  = Close[0] + (riskTicksI * TickSize);
                        double tgt1 = Close[0] - (riskTicksI * TickSize);

                        SetStopLoss("Leg1", CalculationMode.Ticks, riskTicksI, false);
                        SetProfitTarget("Leg1", CalculationMode.Ticks, riskTicksI);
                        EnterShort(TotalContracts / 2, "Leg1");

                        leg2TrailingStop = stp;
                        leg1EntryPrice   = Close[0];
                        leg1TargetPrice  = tgt1;
                        awaitingLeg2     = true;
                        tradeDir         = -1;
                    }
                }
            }

            // 3. MULTI-LEG RISK & PARACHUTE
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                // A. DEFERRED LEG2 ENTRY — fire when Leg1 profit reaches the gate
                if (awaitingLeg2)
                {
                    if (!expansionAllowed)
                    {
                        // Regime changed before gate — abandon Leg2
                        awaitingLeg2 = false;
                    }
                    else
                    {
                        double targetDist    = Math.Abs(leg1TargetPrice - leg1EntryPrice);
                        double currentProfit = tradeDir == 1
                            ? Close[0] - leg1EntryPrice
                            : leg1EntryPrice - Close[0];

                        if (targetDist > 0 && currentProfit >= targetDist * Leg2ProfitGatePct)
                        {
                            SetStopLoss("Leg2", CalculationMode.Price, leg2TrailingStop, false);
                            if (tradeDir == 1) EnterLong(TotalContracts / 2, "Leg2");
                            else               EnterShort(TotalContracts / 2, "Leg2");
                            awaitingLeg2 = false;
                        }
                    }
                }

                // B. LEG1 EXIT DETECTION — only meaningful after Leg2 has entered
                if (!leg1Hit && !awaitingLeg2 && Position.Quantity <= TotalContracts / 2)
                {
                    leg1Hit = true; // Free Trade Pivot
                    leg2TrailingStop = Position.MarketPosition == MarketPosition.Long
                        ? Position.AveragePrice + (4 * TickSize)
                        : Position.AveragePrice - (4 * TickSize);
                    SetStopLoss("Leg2", CalculationMode.Price, leg2TrailingStop, false);
                }

                // C. WOBBLE EXIT (UniRenko Reversal Parachute)
                bool isRedBrick   = Close[0] < Open[0];
                bool isGreenBrick = Close[0] > Open[0];

                if (Position.MarketPosition == MarketPosition.Long  && isRedBrick)   oppositeBrickCount++;
                else if (Position.MarketPosition == MarketPosition.Short && isGreenBrick) oppositeBrickCount++;
                else oppositeBrickCount = 0;

                if (oppositeBrickCount >= 1)
                {
                    ExitOpenExpansionLegs("Wobble Eject");
                    awaitingLeg2 = false;
                    return;
                }

                // D. STEP-TRAIL THE RUNNER (only after Leg1 has exited)
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

        private void ExitOpenExpansionLegs(string exitSignal)
        {
            int qty = Position.Quantity;
            if (qty <= 0 || Position.MarketPosition == MarketPosition.Flat)
                return;

            int legQty = Math.Max(1, TotalContracts / 2);

            if (awaitingLeg2)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong(qty, exitSignal, "Leg1");
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort(qty, exitSignal, "Leg1");
                return;
            }

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

        // ===== C1: TIME GATE =====
        // Returns true if the current bar time falls inside any enabled blocked window.
        private bool InBlockedWindow()
        {
            if (!EnableTimeBlocks) return false;

            int hhmm = Time[0].Hour * 100 + Time[0].Minute;
            return IsInBlock(hhmm, Block1Start, Block1End)
                || IsInBlock(hhmm, Block2Start, Block2End)
                || IsInBlock(hhmm, Block3Start, Block3End);
        }

        private bool IsInBlock(int hhmm, int start, int end)
        {
            if (start == 0 && end == 0) return false;          // unused window
            if (end > start) return hhmm >= start && hhmm < end;
            if (end < start) return hhmm >= start || hhmm < end; // window wraps midnight
            return false;
        }

        // ===== C2: ENTRY COOLDOWN =====
        // Returns true if enough time has elapsed since the last flat to allow a new entry.
        private bool CooldownElapsed()
        {
            if (!EnableEntryCooldown) return true;
            if (lastExitTime == DateTime.MinValue) return true;   // no prior exit this run
            return (Time[0] - lastExitTime).TotalMinutes >= CooldownMinutes;
        }

        // ===== SAME-DIRECTION CAP (SF-27) =====
        private bool DirectionAllowsLong()
        {
            return M5DirectionMode != V3CExpansionM5DirectionMode.ShortOnly;
        }

        private bool DirectionAllowsShort()
        {
            return M5DirectionMode != V3CExpansionM5DirectionMode.LongOnly;
        }

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

        // =========================================================================
        // STAGE 1 TRADE LOGGING — delegates to V3CTradeLogger
        // =========================================================================
        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId,
            DateTime time)
        {
            _logger?.OnExecution(execution, null);

            // Same-direction cap: register once per position on the first entry fill (Leg1).
            if (execution.Order != null && !_dirRegistered)
            {
                if (execution.Order.OrderAction == OrderAction.Buy)
                    { RegisterDirEntry(1);  _dirRegistered = true; }
                else if (execution.Order.OrderAction == OrderAction.SellShort)
                    { RegisterDirEntry(-1); _dirRegistered = true; }
            }

            // C2: stamp the moment the position goes fully flat — starts the entry cooldown.
            if (marketPosition == MarketPosition.Flat)
            {
                lastExitTime = time;
                _dirRegistered = false;
            }
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
