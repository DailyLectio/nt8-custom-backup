// Expansion.cs — V3C lineage upgrade per Strategy Lane Settings v3 (2026-06-05)
//
// Base lineage (data-producing source):
//   * V3_Expansion_Rider_V3C.cs — V3C Expansion behavior that produced the research data
//   * VC3_Expansion_NQ_LIVE.cs  — live clone with deferred Leg 2 + trinity/HMM/time gating
//
// Preserved from lineage:
//   - UniRenko brick entry with WaitBricks hysteresis
//   - Two-leg structure with deferred Leg 2 entry at Leg2ProfitGatePct (live design)
//   - Free-trade pivot (avg ± 4 ticks) when Leg 1 exits on target
//   - Legacy ATR * 1.25 trail on Leg 2 runner
//   - Wobble Eject (UniRenko reversal parachute, >= 1 opposite brick)
//   - V3C Trinity HUD gate (RegimeMatrixHUD_V3C)
//   - V3CTradeLogger (Stage 1 schema)
//   - Entry cooldown
//
// Added per v3 spec (HANDOFF_Expansion_Momentum_CodingChat_RESOLVED_20260605.md):
//   - 3-window allow-list entry gate with per-window Enabled bool (replaces VC3 LIVE block-list)
//   - LongsOnly, EnableHMMTrendDownBlock (block when hud.HMMMicro == "TrendDown")
//   - MinConfidenceThreshold (Path B per resolved spec — configured but NOT read live in v1)
//   - MaxTradesPerDay, DailyLossCircuit, MaxSameDirTrades, MaxConsecutiveLossesIntraday
//   - WobbleHandler enum (Variant B default: Leg 1 close, Leg 2 runs protected; A/B/C selectable)
//   - Stop re-arm correctness fix (same-bar via OnExecutionUpdate + bar-end assertion + fromEntrySignal capture)
//
// Shipped defaults = NQ Research/SIM profile. For ES override MinConfidenceThreshold=60,
// MaxTradesPerDay=3, DailyLossCircuit=2300, MaxConsecutiveLossesIntraday=2, Window3 Enabled=false,
// Window1=1000-1200, Window2=1500-1530. For Apex 50K override DailyLossCircuit=750.

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.Data;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public enum ExpansionWobbleMode
    {
        Leg1Close_Leg2Runs_Protected,
        FlattenBoth,
        Leg2Close_Leg1Runs_Protected
    }

    public class Expansion : Strategy
    {
        // ===== 0. V3C REGIME GATE =====
        [NinjaScriptProperty]
        [Display(Name="Enable Trinity Filter", Description="ON = require V3C TREND_EXPANSION regime. OFF = ungated (proven NQ-1A Mode-1A baseline).", GroupName="0. V3C Regime Gate", Order=0)]
        public bool EnableTrinityFilter { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name="Debug Gate", GroupName="0. V3C Regime Gate", Order=1)]
        public bool DebugV3CGate { get; set; } = false;

        // ===== 0b. STAGE 1 TRADE LOGGING =====
        [NinjaScriptProperty]
        [Display(Name="Account Name Filter", Description="Exact NT8 account name. Log only writes when account matches.", GroupName="0b. Trade Logging", Order=0)]
        public string AccountNameFilter { get; set; } = "";

        [NinjaScriptProperty]
        [Display(Name="Trade Log Folder", GroupName="0b. Trade Logging", Order=1)]
        public string TradeLogFolder { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3C\TradeLog";

        // ===== 1. RESEARCHED GATES (v3 spec) =====
        [NinjaScriptProperty]
        [Display(Name="Longs Only", Description="v3 spec true. Blocks all short entries regardless of brick direction or trinity.", GroupName="1. Researched Gates", Order=0)]
        public bool LongsOnly { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Enable HMM TrendDown Block", Description="v3 spec true. Blocks entries when RegimeMatrixHUD_V3C.HMMMicro == 'TrendDown'. Fails OPEN if HUD missing/UNKNOWN.", GroupName="1. Researched Gates", Order=1)]
        public bool EnableHMMTrendDownBlock { get; set; } = true;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name="Min Confidence Threshold (no-op v1)", Description="v3 spec: NQ=0, ES=60. v1 Path B per resolved HANDOFF §8 — configured but NOT read live. Wire HUD subscription in v2 if SIM data calls for it.", GroupName="1. Researched Gates", Order=2)]
        public int MinConfidenceThreshold { get; set; } = 0;

        // ===== 2. RISK MANAGEMENT (V3C core) =====
        [NinjaScriptProperty, Range(2, 100)]
        [Display(Name="Total Contracts (even)", GroupName="2. Risk Management", Order=0)]
        public int TotalContracts { get; set; } = 2;

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name="Initial Risk (ATR)", GroupName="2. Risk Management", Order=1)]
        public double InitialRiskAtr { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1, 10)]
        [Display(Name="Wait Bricks (Hysteresis)", Description="Wait N consecutive Expansion-allowed bricks before firing.", GroupName="2. Risk Management", Order=2)]
        public int WaitBricks { get; set; } = 3;

        [NinjaScriptProperty, Range(0.0, 1.0)]
        [Display(Name="Leg2 Profit Gate (frac of Leg1 target)", Description="Leg 2 fires when Leg 1 profit reaches this fraction of its target distance. 0 = simultaneous entry (V3C original behavior).", GroupName="2. Risk Management", Order=3)]
        public double Leg2ProfitGatePct { get; set; } = 0.5;

        // ===== 3. ENTRY TIME WINDOWS (allow-list) =====
        [NinjaScriptProperty]
        [Display(Name="Enable Entry Time Windows", Description="When true, new entries are allowed only within enabled windows below.", GroupName="3. Entry Time Windows", Order=0)]
        public bool EnableEntryTimeWindows { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Window 1 Enabled", GroupName="3. Entry Time Windows", Order=1)]
        public bool Window1Enabled { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 1 Start (HHmm)", GroupName="3. Entry Time Windows", Order=2)]
        public int Window1Start { get; set; } = 930;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 1 End (HHmm)", GroupName="3. Entry Time Windows", Order=3)]
        public int Window1End { get; set; } = 1000;

        [NinjaScriptProperty]
        [Display(Name="Window 2 Enabled", GroupName="3. Entry Time Windows", Order=4)]
        public bool Window2Enabled { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 2 Start (HHmm)", GroupName="3. Entry Time Windows", Order=5)]
        public int Window2Start { get; set; } = 1200;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 2 End (HHmm)", GroupName="3. Entry Time Windows", Order=6)]
        public int Window2End { get; set; } = 1330;

        [NinjaScriptProperty]
        [Display(Name="Window 3 Enabled", Description="NQ default: enabled (1500-1530). For ES, disable.", GroupName="3. Entry Time Windows", Order=7)]
        public bool Window3Enabled { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 3 Start (HHmm)", GroupName="3. Entry Time Windows", Order=8)]
        public int Window3Start { get; set; } = 1500;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 3 End (HHmm)", Description="Hard close — no new entries after this time.", GroupName="3. Entry Time Windows", Order=9)]
        public int Window3End { get; set; } = 1530;

        // ===== 4. CIRCUITS & CAPS =====
        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name="Max Trades Per Day", Description="v3 spec: NQ=14, ES=3. 0 = OFF.", GroupName="4. Circuits & Caps", Order=0)]
        public int MaxTradesPerDay { get; set; } = 14;

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name="Daily Loss Circuit ($)", Description="v3 Research/SIM defaults: NQ=2650, ES=2300 (1C Mini). Apex 50K preset: 750. 0 = OFF.", GroupName="4. Circuits & Caps", Order=1)]
        public double DailyLossCircuit { get; set; } = 2650.0;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name="Max Same-Direction Trades", Description="v3 spec: 2. 0 = OFF. Counter resets on direction flip and at session start.", GroupName="4. Circuits & Caps", Order=2)]
        public int MaxSameDirTrades { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name="Max Consecutive Losses Intraday", Description="v3 spec: NQ=4, ES=2. 0 = OFF.", GroupName="4. Circuits & Caps", Order=3)]
        public int MaxConsecutiveLossesIntraday { get; set; } = 4;

        // MaxAddsPerEntry is structural: zero by design (two legs only, no pyramiding). Not a property.

        // ===== 5. WOBBLE HANDLER =====
        [NinjaScriptProperty]
        [Display(Name="Wobble Handler", Description="B (default) = close Leg 1, Leg 2 runs protected. A = flatten both. C = close Leg 2, Leg 1 runs protected. SIM A/B/C = flip per shadow account.", GroupName="5. Wobble Handler", Order=0)]
        public ExpansionWobbleMode WobbleHandler { get; set; } = ExpansionWobbleMode.Leg1Close_Leg2Runs_Protected;

        [NinjaScriptProperty]
        [Display(Name="Auto-Flatten If Unprotected", Description="If true, auto-flatten any open leg whose protective stop is missing at bar end. v1 default OFF — observe assertion log in SIM before enabling.", GroupName="5. Wobble Handler", Order=1)]
        public bool AutoFlattenIfUnprotected { get; set; } = false;

        // ===== 6. ENTRY COOLDOWN =====
        [NinjaScriptProperty]
        [Display(Name="Enable Entry Cooldown", GroupName="6. Entry Cooldown", Order=0)]
        public bool EnableEntryCooldown { get; set; } = true;

        [NinjaScriptProperty, Range(0, 120)]
        [Display(Name="Cooldown Minutes", GroupName="6. Entry Cooldown", Order=1)]
        public int CooldownMinutes { get; set; } = 5;

        // ===== INTERNAL STATE =====
        private ATR atr;
        private V3CTradeLogger _logger;

        private int bricksInExpansion = 0;
        private bool leg1Hit = false;
        private int oppositeBrickCount = 0;

        private double leg2TrailingStop = 0.0;
        private double leg1ManagedStop = double.NaN;
        private double leg2ManagedStop = double.NaN;

        private bool   awaitingLeg2   = false;
        private double leg1EntryPrice  = 0.0;
        private double leg1TargetPrice = 0.0;
        private double leg2EntryPrice  = 0.0;
        private int    tradeDir        = 0;

        private DateTime lastExitTime = DateTime.MinValue;

        private int  _sameDirCount  = 0;
        private int  _lastEntryDir  = 0;
        private bool _dirRegistered = false;

        private int _sessionLossCount = 0;
        private int _lastProcessedTradeCount = 0;
        private double _cycleStartCumProfit = 0.0;
        private bool _cycleInProgress = false;

        private int _sessionEntryCount = 0;
        private double _sessionPnLBaseline = 0.0;
        private bool _circuitTripped = false;

        // §7 leg quantity tracking — drives stop-protection assertion and re-arm
        private int _leg1Qty = 0;
        private int _leg2Qty = 0;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                  = "V3C lineage Expansion — researched gates, Variant B wobble default, stop re-arm correctness fix.";
                Name                         = "Expansion";
                Calculate                    = Calculate.OnBarClose;
                EntriesPerDirection          = 2;
                EntryHandling                = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                IsFillLimitOnTouch           = false;
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(14);
                _logger = new V3CTradeLogger(this, AccountNameFilter, "V3C", TradeLogFolder);
                _cycleStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                _sessionPnLBaseline  = _cycleStartCumProfit;
                _lastProcessedTradeCount = SystemPerformance.AllTrades.Count;
            }
        }

        private void ClearLocals()
        {
            leg2TrailingStop = 0.0;
            leg1ManagedStop  = double.NaN;
            leg2ManagedStop  = double.NaN;
            leg1Hit          = false;
            oppositeBrickCount = 0;
            awaitingLeg2     = false;
            leg1EntryPrice   = 0.0;
            leg1TargetPrice  = 0.0;
            leg2EntryPrice   = 0.0;
            tradeDir         = 0;
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 20) return;

            if (Bars.IsFirstBarOfSession)
            {
                ResetSameDirCounter();
                ResetSessionLossCounter();
                ResetDailyCounters();
            }

            UpdateSessionLossCounterFromClosedCycle();
            UpdateDailyLossCircuit();

            bool expansionAllowed = IsExpansionAllowed(out bool allowLong, out bool allowShort);
            if (LongsOnly) allowShort = false;

            bool contractsValid = TotalContracts >= 2 && TotalContracts % 2 == 0;
            if (!contractsValid)
            {
                DebugGate("Blocked: TotalContracts must be even and >= 2");
                expansionAllowed = false;
            }

            if (expansionAllowed && !allowLong && !allowShort)
            {
                DebugGate("Blocked: no direction allowed (LongsOnly/Trinity)");
                expansionAllowed = false;
            }

            if (expansionAllowed)
                bricksInExpansion++;
            else
                bricksInExpansion = 0;

            // ===== ENTRY =====
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ClearLocals();
                _leg1Qty = 0;
                _leg2Qty = 0;

                bool inWindow     = IsInAllowedEntryWindow();
                bool cooldownOk   = CooldownElapsed();
                bool hmmBlocked   = IsHmmTrendDownBlocked();
                bool lossBlocked  = ConsecutiveLossBlocked();
                bool tradesCapped = TradesCapReached();
                bool circuitHit   = _circuitTripped;

                if (!inWindow)    DebugGate("Blocked: outside entry window");
                if (!cooldownOk)  DebugGate("Blocked: cooldown active");
                if (hmmBlocked)   DebugGate("Blocked: HMM TrendDown");
                if (lossBlocked)  DebugGate("Blocked: max consecutive losses");
                if (tradesCapped) DebugGate("Blocked: max trades per day");
                if (circuitHit)   DebugGate("Blocked: daily loss circuit tripped");

                if (expansionAllowed && bricksInExpansion >= WaitBricks
                    && inWindow && cooldownOk && !hmmBlocked && !lossBlocked && !tradesCapped && !circuitHit)
                {
                    bool isGreenBrick = Close[0] > Open[0];
                    bool isRedBrick   = Close[0] < Open[0];
                    double riskTicks  = (atr[0] * InitialRiskAtr) / TickSize;
                    int riskTicksI    = Math.Max(1, (int)Math.Round(riskTicks));

                    if (isGreenBrick && allowLong && !SameDirBlocked(1))
                    {
                        double stp  = Close[0] - (riskTicksI * TickSize);
                        double tgt1 = Close[0] + (riskTicksI * TickSize);

                        SetStopLoss("Leg1", CalculationMode.Ticks, riskTicksI, false);
                        SetProfitTarget("Leg1", CalculationMode.Ticks, riskTicksI);
                        EnterLong(TotalContracts / 2, "Leg1");

                        leg2TrailingStop = stp;
                        leg1ManagedStop  = stp;
                        leg1EntryPrice   = Close[0];
                        leg1TargetPrice  = tgt1;
                        awaitingLeg2     = (Leg2ProfitGatePct > 0);
                        tradeDir         = 1;

                        if (Leg2ProfitGatePct == 0.0)
                        {
                            // V3C original simultaneous entry
                            SetStopLoss("Leg2", CalculationMode.Ticks, riskTicksI, false);
                            EnterLong(TotalContracts / 2, "Leg2");
                            leg2ManagedStop = stp;
                            leg2EntryPrice  = Close[0];
                        }
                    }
                    else if (isRedBrick && allowShort && !SameDirBlocked(-1))
                    {
                        double stp  = Close[0] + (riskTicksI * TickSize);
                        double tgt1 = Close[0] - (riskTicksI * TickSize);

                        SetStopLoss("Leg1", CalculationMode.Ticks, riskTicksI, false);
                        SetProfitTarget("Leg1", CalculationMode.Ticks, riskTicksI);
                        EnterShort(TotalContracts / 2, "Leg1");

                        leg2TrailingStop = stp;
                        leg1ManagedStop  = stp;
                        leg1EntryPrice   = Close[0];
                        leg1TargetPrice  = tgt1;
                        awaitingLeg2     = (Leg2ProfitGatePct > 0);
                        tradeDir         = -1;

                        if (Leg2ProfitGatePct == 0.0)
                        {
                            SetStopLoss("Leg2", CalculationMode.Ticks, riskTicksI, false);
                            EnterShort(TotalContracts / 2, "Leg2");
                            leg2ManagedStop = stp;
                            leg2EntryPrice  = Close[0];
                        }
                    }
                }
            }

            // ===== MULTI-LEG MANAGEMENT =====
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                // A. DEFERRED LEG 2 ENTRY
                if (awaitingLeg2)
                {
                    if (!expansionAllowed)
                    {
                        awaitingLeg2 = false;
                    }
                    else
                    {
                        double targetDist = Math.Abs(leg1TargetPrice - leg1EntryPrice);
                        double currentProfit = tradeDir == 1
                            ? Close[0] - leg1EntryPrice
                            : leg1EntryPrice - Close[0];

                        if (targetDist > 0 && currentProfit >= targetDist * Leg2ProfitGatePct)
                        {
                            SetStopLoss("Leg2", CalculationMode.Price, leg2TrailingStop, false);
                            leg2ManagedStop = leg2TrailingStop;
                            leg2EntryPrice  = Close[0];
                            if (tradeDir == 1) EnterLong(TotalContracts / 2, "Leg2");
                            else               EnterShort(TotalContracts / 2, "Leg2");
                            awaitingLeg2 = false;
                        }
                    }
                }

                // B. LEG 1 EXIT DETECTION → free-trade pivot on Leg 2
                if (!leg1Hit && !awaitingLeg2 && _leg1Qty <= 0 && _leg2Qty > 0)
                {
                    leg1Hit = true;
                    leg2TrailingStop = Position.MarketPosition == MarketPosition.Long
                        ? Position.AveragePrice + (4 * TickSize)
                        : Position.AveragePrice - (4 * TickSize);
                    leg2ManagedStop = leg2TrailingStop;
                    SetStopLoss("Leg2", CalculationMode.Price, leg2TrailingStop, false);
                }

                // C. WOBBLE EJECT (UniRenko reversal parachute)
                bool isRedBrick   = Close[0] < Open[0];
                bool isGreenBrick = Close[0] > Open[0];

                if (Position.MarketPosition == MarketPosition.Long  && isRedBrick)   oppositeBrickCount++;
                else if (Position.MarketPosition == MarketPosition.Short && isGreenBrick) oppositeBrickCount++;
                else oppositeBrickCount = 0;

                if (oppositeBrickCount >= 1)
                {
                    HandleWobbleExit();
                    AssertStopProtection();
                    return;
                }

                // D. Legacy ATR*1.25 trail on Leg 2 runner (post-Leg1-hit)
                if (leg1Hit && _leg2Qty > 0)
                {
                    double trailDistance = (atr[0] * 1.25);
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        double candidate = High[0] - trailDistance;
                        if (candidate > leg2TrailingStop)
                        {
                            leg2TrailingStop = candidate;
                            leg2ManagedStop = candidate;
                            SetStopLoss("Leg2", CalculationMode.Price, leg2TrailingStop, false);
                        }
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        double candidate = Low[0] + trailDistance;
                        if (candidate < leg2TrailingStop || leg2TrailingStop == 0)
                        {
                            leg2TrailingStop = candidate;
                            leg2ManagedStop = candidate;
                            SetStopLoss("Leg2", CalculationMode.Price, leg2TrailingStop, false);
                        }
                    }
                }
            }

            // §7B BAR-END ASSERTION
            AssertStopProtection();
        }

        // ===== WOBBLE HANDLER (Variant B default) =====
        private void HandleWobbleExit()
        {
            const string exitSig = "Wobble Eject";

            if (_leg1Qty <= 0 && _leg2Qty <= 0)
                return;

            // Edge: only one leg open → wobble closes that leg regardless of variant.
            if (_leg1Qty > 0 && _leg2Qty <= 0)
            {
                ExitLeg(exitSig, "Leg1", _leg1Qty);
                awaitingLeg2 = false;
                return;
            }
            if (_leg1Qty <= 0 && _leg2Qty > 0)
            {
                ExitLeg(exitSig, "Leg2", _leg2Qty);
                return;
            }

            // Both legs open — variant decides.
            switch (WobbleHandler)
            {
                case ExpansionWobbleMode.FlattenBoth:
                    ExitLeg(exitSig, "Leg1", _leg1Qty);
                    ExitLeg(exitSig, "Leg2", _leg2Qty);
                    awaitingLeg2 = false;
                    break;

                case ExpansionWobbleMode.Leg1Close_Leg2Runs_Protected:
                    ExitLeg(exitSig, "Leg1", _leg1Qty);
                    // Leg 2 stop persists; §7A re-arm runs in OnExecutionUpdate if missing.
                    break;

                case ExpansionWobbleMode.Leg2Close_Leg1Runs_Protected:
                    ExitLeg(exitSig, "Leg2", _leg2Qty);
                    // Leg 1 stop persists; §7A re-arm runs in OnExecutionUpdate if missing.
                    break;
            }
        }

        private void ExitLeg(string exitSignal, string entrySignal, int qty)
        {
            if (qty <= 0) return;
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(qty, exitSignal, entrySignal);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(qty, exitSignal, entrySignal);
        }

        // ===== §7 STOP PROTECTION =====
        private void AssertStopProtection()
        {
            if (Position.MarketPosition == MarketPosition.Flat) return;

            if (_leg1Qty > 0 && FindWorkingStopForLeg("Leg1") == null)
                TripUnprotectedLeg("Leg1", leg1ManagedStop);

            if (_leg2Qty > 0 && FindWorkingStopForLeg("Leg2") == null)
                TripUnprotectedLeg("Leg2", leg2ManagedStop);
        }

        private Order FindWorkingStopForLeg(string entrySignal)
        {
            foreach (Order o in Orders)
            {
                if (o == null) continue;
                if (!string.Equals(o.FromEntrySignal, entrySignal, StringComparison.OrdinalIgnoreCase)) continue;
                if (o.OrderType != OrderType.StopMarket && o.OrderType != OrderType.StopLimit) continue;
                var st = o.OrderState;
                if (st == OrderState.Working || st == OrderState.Accepted)
                    return o;
            }
            return null;
        }

        private void TripUnprotectedLeg(string entrySignal, double recoverStopPrice)
        {
            int qty = entrySignal == "Leg1" ? _leg1Qty : _leg2Qty;
            Print($"[UNPROTECTED-LEG] {Time[0]} {Name} {Instrument.FullName} entrySignal={entrySignal} qty={qty} recoverStop={recoverStopPrice:F2}");

            if (!double.IsNaN(recoverStopPrice) && recoverStopPrice > 0)
                SetStopLoss(entrySignal, CalculationMode.Price, recoverStopPrice, false);

            if (AutoFlattenIfUnprotected)
                ExitLeg("Unprotected-AutoFlat", entrySignal, qty);
        }

        // ===== V3C TRINITY + HMM GATE =====
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

            var hud = GetV3CHud();
            if (hud == null) { DebugGate("Blocked: HUD missing"); return false; }
            if (hud.StaleDataFlag) { DebugGate("Blocked: stale data"); return false; }
            if (!string.Equals(hud.FinalRegime, "TREND_EXPANSION", StringComparison.OrdinalIgnoreCase))
            { DebugGate("Blocked: FinalRegime=" + hud.FinalRegime); return false; }
            if (!hud.IsExpansionBotAllowed) { DebugGate("Blocked: ExpansionBot OFF"); return false; }

            allowLong = hud.AllowLong;
            allowShort = hud.AllowShort;
            return allowLong || allowShort;
        }

        private bool IsHmmTrendDownBlocked()
        {
            if (!EnableHMMTrendDownBlock) return false;
            var hud = GetV3CHud();
            if (hud == null) return false;            // fail open
            string state = (hud.HMMMicro ?? "").Trim();
            if (string.IsNullOrEmpty(state) || string.Equals(state, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
                return false;                          // fail open
            return string.Equals(state, "TrendDown", StringComparison.OrdinalIgnoreCase);
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
            if (string.IsNullOrEmpty(sym)) return sym;
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
                Print($"{Time[0]} {Name} Gate: {message}");
        }

        // ===== TIME WINDOWS (allow-list) =====
        private bool IsInAllowedEntryWindow()
        {
            if (!EnableEntryTimeWindows) return true;
            int hhmm = Time[0].Hour * 100 + Time[0].Minute;
            return InWindow(Window1Enabled, Window1Start, Window1End, hhmm)
                || InWindow(Window2Enabled, Window2Start, Window2End, hhmm)
                || InWindow(Window3Enabled, Window3Start, Window3End, hhmm);
        }

        private bool InWindow(bool enabled, int start, int end, int hhmm)
        {
            if (!enabled) return false;
            if (start == 0 && end == 0) return false;
            if (end > start) return hhmm >= start && hhmm < end;
            if (end < start) return hhmm >= start || hhmm < end;
            return false;
        }

        // ===== COOLDOWN =====
        private bool CooldownElapsed()
        {
            if (!EnableEntryCooldown) return true;
            if (lastExitTime == DateTime.MinValue) return true;
            return (Time[0] - lastExitTime).TotalMinutes >= CooldownMinutes;
        }

        // ===== SAME-DIRECTION CAP =====
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

        // ===== CONSECUTIVE LOSS CAP =====
        private bool ConsecutiveLossBlocked()
        {
            return MaxConsecutiveLossesIntraday > 0
                && _sessionLossCount >= MaxConsecutiveLossesIntraday;
        }

        private void UpdateSessionLossCounterFromClosedCycle()
        {
            if (!_cycleInProgress || Position.MarketPosition != MarketPosition.Flat) return;

            int tradeCount = SystemPerformance.AllTrades.Count;
            if (tradeCount <= _lastProcessedTradeCount) return;

            double cyclePnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - _cycleStartCumProfit;
            if (cyclePnL < 0) _sessionLossCount++;

            _cycleInProgress = false;
            _cycleStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            _lastProcessedTradeCount = tradeCount;
        }

        private void ResetSessionLossCounter()
        {
            _sessionLossCount = 0;
            _cycleInProgress = Position.MarketPosition != MarketPosition.Flat;
            _cycleStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            _lastProcessedTradeCount = SystemPerformance.AllTrades.Count;
        }

        // ===== DAILY COUNTERS =====
        private bool TradesCapReached()
        {
            return MaxTradesPerDay > 0 && _sessionEntryCount >= MaxTradesPerDay;
        }

        private void ResetDailyCounters()
        {
            _sessionEntryCount = 0;
            _sessionPnLBaseline = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            _circuitTripped = false;
        }

        private void UpdateDailyLossCircuit()
        {
            if (DailyLossCircuit <= 0 || _circuitTripped) return;
            double sessionPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - _sessionPnLBaseline;
            if (sessionPnL <= -DailyLossCircuit)
            {
                _circuitTripped = true;
                Print($"[CIRCUIT] {Time[0]} {Name} {Instrument.FullName} daily loss circuit tripped: sessionPnL={sessionPnL:F2} limit=-{DailyLossCircuit:F2}");
            }
        }

        // ===== ON EXECUTION =====
        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId,
            DateTime time)
        {
            _logger?.OnExecution(execution, null);

            if (execution?.Order == null) return;
            var order = execution.Order;

            bool isEntry = order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.SellShort;
            bool isExit  = order.OrderAction == OrderAction.Sell || order.OrderAction == OrderAction.BuyToCover;

            // §7C — fromEntrySignal capture on every exit fill
            if (isExit)
                Print($"[EXIT-FILL] {time:HH:mm:ss} {Name} {Instrument.FullName} exitSig={order.Name} fromEntrySignal={order.FromEntrySignal} qty={quantity} price={price:F2}");

            // Leg quantity tracking
            if (isEntry)
            {
                string entrySig = order.Name ?? "";
                if (string.Equals(entrySig, "Leg1", StringComparison.OrdinalIgnoreCase))
                {
                    _leg1Qty += quantity;
                    leg1EntryPrice = price;
                }
                else if (string.Equals(entrySig, "Leg2", StringComparison.OrdinalIgnoreCase))
                {
                    _leg2Qty += quantity;
                    leg2EntryPrice = price;
                }
            }
            else if (isExit)
            {
                string entrySig = order.FromEntrySignal ?? "";
                if (string.Equals(entrySig, "Leg1", StringComparison.OrdinalIgnoreCase))
                    _leg1Qty = Math.Max(0, _leg1Qty - quantity);
                else if (string.Equals(entrySig, "Leg2", StringComparison.OrdinalIgnoreCase))
                    _leg2Qty = Math.Max(0, _leg2Qty - quantity);
            }

            // Cycle tracking for consecutive-loss counter
            if (isEntry && !_cycleInProgress)
            {
                _cycleStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                _cycleInProgress = true;
            }

            // Same-direction register (Leg 1 entry only — one per position)
            if (isEntry && !_dirRegistered)
            {
                if (order.OrderAction == OrderAction.Buy)            { RegisterDirEntry(1);  _dirRegistered = true; }
                else if (order.OrderAction == OrderAction.SellShort) { RegisterDirEntry(-1); _dirRegistered = true; }
            }

            // Daily trade counter (Leg 1 entry = new trade)
            if (isEntry && string.Equals(order.Name, "Leg1", StringComparison.OrdinalIgnoreCase))
                _sessionEntryCount++;

            // §7A same-bar re-arm — verify surviving legs have working protective stops
            if (isExit && marketPosition != MarketPosition.Flat)
            {
                if (_leg1Qty > 0 && FindWorkingStopForLeg("Leg1") == null
                    && !double.IsNaN(leg1ManagedStop) && leg1ManagedStop > 0)
                    SetStopLoss("Leg1", CalculationMode.Price, leg1ManagedStop, false);

                if (_leg2Qty > 0 && FindWorkingStopForLeg("Leg2") == null
                    && !double.IsNaN(leg2ManagedStop) && leg2ManagedStop > 0)
                    SetStopLoss("Leg2", CalculationMode.Price, leg2ManagedStop, false);
            }

            // Flat → reset cooldown, dir register, leg counts
            if (marketPosition == MarketPosition.Flat)
            {
                lastExitTime = time;
                _dirRegistered = false;
                _leg1Qty = 0;
                _leg2Qty = 0;
            }
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
