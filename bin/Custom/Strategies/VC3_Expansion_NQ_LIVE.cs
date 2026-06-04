// CC BY-NC 4.0
// ============================================================================
// VC3_Expansion_NQ_LIVE — LIVE clone of V3_Expansion_Rider_V3C
// Cloned: 2026-05-23 (for SimV3C-NQ-1A live Apex testing, MNQ)
// Same 2-leg expansion engine as the parent, plus operator-adjustable limiters
// derived from the 2-week NQ-1A study:
//   - TIME GATE extended 3->4 blocked windows. Week-1 defaults block:
//       0930-0935 (enforce 09:35 start — regime/HMM not live until ~09:35),
//       1000-1200 (mid-morning dead zone), 1330-1400, 1430-1500.
//     => allowed lanes: 09:35-09:59, 12:00-13:29, 14:00-14:29, 15:00-close.
//   - HMM GATE (NEW): block-list on hud.HMMMicro. Week-1 blocks "TrendDown"
//     (TrendDn = NOT TRADE — structurally weak for NQ Expansion; confirmed by
//     the 2-wk data and operator's longer-run experience). Read-only HUD access
//     (tethered, non-contaminating). Regime/Trinity gate stays OFF (proven Mode-1A).
//   - Start 09:35 (not 09:30): the HUD can't classify TrendDown pre-0935, so the
//     un-gateable open sliver (~+$11/day MNQ) is skipped to keep the rule airtight.
//   Sizing: TotalContracts=2 = 2 MNQ (1/leg — the bracket minimum). $1,000 Apex
//   daily: 2-wk worst window day ~-$200 MNQ, never near the limit.
//   UNVERIFIED: NOT compiled by Claude. F5-compile + sim-replay before live.
//   Verify on sim: hud.HMMMicro emits exact "TrendDown"; chart session TZ = ET.
// ---- inherited parent header ----
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
    public enum VC3ExpansionTradeDirectionMode
    {
        LongAndShort,
        LongOnly,
        ShortOnly
    }

    public enum VC3ExpansionTrailMode
    {
        Off,
        BarNTrailing,
        AtrStep
    }

    public class VC3_Expansion_NQ_LIVE : Strategy
    {
        // ===== 0. V3C REGIME GATE (OFF = proven NQ-1A Mode-1A baseline) =====
        [NinjaScriptProperty]
        [Display(Name="Enable Regime Gate (Trinity)", Description="ON = require V3C TREND_EXPANSION regime. OFF = ungated baseline (proven NQ-1A config). The LIVE bot uses the HMM block-list below, not this gate.", GroupName="0. V3C Regime Gate", Order=0)]
        public bool EnableTrinityFilter { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name="Debug V3C Gate", GroupName="0. V3C Regime Gate", Order=1)]
        public bool DebugV3CGate { get; set; } = false;

        // ===== 0c. HMM BLOCK-LIST GATE (LIVE; read-only HUD, tethered/non-contaminating) =====
        [NinjaScriptProperty]
        [Display(Name="Enable HMM Block Gate", Description="ON = block new entries when hud.HMMMicro matches a state in 'HMM Blocked States'. Reads the HUD read-only; independent of the Trinity gate.", GroupName="0c. HMM Gate", Order=0)]
        public bool EnableHMMGate { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="HMM Blocked States", Description="Comma-separated hud.HMMMicro values that BLOCK entry (TrendDn = NOT TRADE). Default: TrendDown. If the HUD is missing/UNKNOWN the gate fails OPEN (trades) — we start at 09:35 when the regime is live.", GroupName="0c. HMM Gate", Order=1)]
        public string HMMBlockedStates { get; set; } = "TrendDown";

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

        [NinjaScriptProperty, Range(0.0, 1.0)]
        [Display(Name="Leg2 Profit Gate (% of Leg1 target)", Description="Leg2 fires only after Leg1 has reached this fraction of its target distance. 0.5 = 50%. Set to 0 to restore simultaneous entry.", GroupName="2. Risk Management", Order=3)]
        public double Leg2ProfitGatePct { get; set; } = 0.5;

        // ===== 2b. DIRECTION FILTER =====
        [NinjaScriptProperty]
        [Display(Name="Trade Direction", Description="Long And Short = current behavior. Long Only blocks short entries. Short Only blocks long entries.", GroupName="2b. Direction Filter", Order=0)]
        public VC3ExpansionTradeDirectionMode TradeDirection { get; set; } = VC3ExpansionTradeDirectionMode.LongAndShort;

        // ===== 3. TIME GATE (C1) =====
        // Blocks NEW entries during up to three configurable HHmm windows.
        // Position management (Leg2, wobble exit, trailing) is never affected.
        // Week-1 defaults block: 0930-0935 (enforce 09:35 start), 1000-1200 (dead zone),
        // 1330-1400, 1430-1500 => allowed lanes 09:35-09:59, 12:00-13:29, 14:00-14:29, 15:00-close.
        [NinjaScriptProperty]
        [Display(Name="Enable Time Blocks", Description="When true, new entries are blocked during the configured HHmm windows below.", GroupName="3. Time Gate", Order=0)]
        public bool EnableTimeBlocks { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Block 1 Start (HHmm)", Description="Start of blocked entry window, 24h HHmm (e.g. 1000). 0 = window unused.", GroupName="3. Time Gate", Order=1)]
        public int Block1Start { get; set; } = 930;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Block 1 End (HHmm)", Description="End of blocked entry window, exclusive (e.g. 1130). 0 = window unused.", GroupName="3. Time Gate", Order=2)]
        public int Block1End { get; set; } = 935;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Block 2 Start (HHmm)", Description="Second blocked window start. 0 = unused.", GroupName="3. Time Gate", Order=3)]
        public int Block2Start { get; set; } = 1000;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Block 2 End (HHmm)", Description="Second blocked window end, exclusive. 0 = unused.", GroupName="3. Time Gate", Order=4)]
        public int Block2End { get; set; } = 1200;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Block 3 Start (HHmm)", Description="Third blocked window start. 0 = unused.", GroupName="3. Time Gate", Order=5)]
        public int Block3Start { get; set; } = 1330;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Block 3 End (HHmm)", Description="Third blocked window end, exclusive. 0 = unused.", GroupName="3. Time Gate", Order=6)]
        public int Block3End { get; set; } = 1400;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Block 4 Start (HHmm)", Description="Fourth blocked window start (LIVE clone addition). 0 = unused.", GroupName="3. Time Gate", Order=7)]
        public int Block4Start { get; set; } = 1430;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Block 4 End (HHmm)", Description="Fourth blocked window end, exclusive. 0 = unused.", GroupName="3. Time Gate", Order=8)]
        public int Block4End { get; set; } = 1500;

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

        // ===== 6. CONSECUTIVE LOSS LIMIT =====
        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name="Max Consecutive Losses", Description="Session loss cap: blocks new entries after this many losing flat-to-flat trade cycles in the current session. A loss is total realized PnL below 0 after the whole position closes. 0 = OFF. Default 1.", GroupName="6. Consecutive Loss Limit", Order=0)]
        public int MaxConsecutiveLosses { get; set; } = 1;

        // ===== 7. PER-LEG TRAILING STOPS =====
        [NinjaScriptProperty]
        [Display(Name="Leg1 Trail Mode", Description="Off = initial stop/target only. BarNTrailing = N-bar stop with offset. AtrStep = BE+ step, then ATR trail.", GroupName="7. Leg1 Trail", Order=0)]
        public VC3ExpansionTrailMode Leg1TrailMode { get; set; } = VC3ExpansionTrailMode.Off;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name="Leg1 Trailing N Bars", GroupName="7. Leg1 Trail", Order=1)]
        public int Leg1TrailingNBars { get; set; } = 1;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name="Leg1 Trailing Offset (ticks)", GroupName="7. Leg1 Trail", Order=2)]
        public int Leg1TrailingOffsetTicks { get; set; } = 5;

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name="Leg1 Step 1 trigger (ATR)", GroupName="7. Leg1 Trail", Order=3)]
        public double Leg1Step1ATR { get; set; } = 0.50;

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name="Leg1 Step 2 trigger (ATR)", GroupName="7. Leg1 Trail", Order=4)]
        public double Leg1Step2ATR { get; set; } = 1.00;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name="Leg1 BE Plus (ticks)", GroupName="7. Leg1 Trail", Order=5)]
        public int Leg1BreakevenPlusTicks { get; set; } = 5;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name="Leg1 Trail ATR Mult", GroupName="7. Leg1 Trail", Order=6)]
        public double Leg1TrailAtrMult { get; set; } = 1.0;

        [NinjaScriptProperty]
        [Display(Name="Leg2 Trail Mode", Description="Off keeps the original runner trail. BarNTrailing = N-bar stop with offset. AtrStep = BE+ step, then ATR trail.", GroupName="8. Leg2 Trail", Order=0)]
        public VC3ExpansionTrailMode Leg2TrailMode { get; set; } = VC3ExpansionTrailMode.Off;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name="Leg2 Trailing N Bars", GroupName="8. Leg2 Trail", Order=1)]
        public int Leg2TrailingNBars { get; set; } = 1;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name="Leg2 Trailing Offset (ticks)", GroupName="8. Leg2 Trail", Order=2)]
        public int Leg2TrailingOffsetTicks { get; set; } = 5;

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name="Leg2 Step 1 trigger (ATR)", GroupName="8. Leg2 Trail", Order=3)]
        public double Leg2Step1ATR { get; set; } = 0.50;

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name="Leg2 Step 2 trigger (ATR)", GroupName="8. Leg2 Trail", Order=4)]
        public double Leg2Step2ATR { get; set; } = 1.00;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name="Leg2 BE Plus (ticks)", GroupName="8. Leg2 Trail", Order=5)]
        public int Leg2BreakevenPlusTicks { get; set; } = 5;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name="Leg2 Trail ATR Mult", GroupName="8. Leg2 Trail", Order=6)]
        public double Leg2TrailAtrMult { get; set; } = 1.0;

        // ===== 9. SLOW STOP X (ADX/DI EXIT) =====
        [NinjaScriptProperty]
        [Display(Name="Use Stop X (Slow ADX/DI exit)", Description="When true, a secondary 30/60 minute ADX/DI check can exit open positions. Long exits on ADX below level or DI- cross above DI+. Short exits on ADX below level or DI+ cross above DI-.", GroupName="9. Stop X", Order=0)]
        public bool UseSlowStopX { get; set; } = false;

        [NinjaScriptProperty, Range(30, 60)]
        [Display(Name="Stop X Minutes (30 or 60)", GroupName="9. Stop X", Order=1)]
        public int SlowStopXMinutes { get; set; } = 30;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name="Stop X ADX Period", GroupName="9. Stop X", Order=2)]
        public int SlowStopXAdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name="Stop X ADX Min Level", GroupName="9. Stop X", Order=3)]
        public double SlowStopXAdxMinLevel { get; set; } = 20;

        // ===== 2. INTERNAL STATE =====
        private ATR atr;
        private ADX slowStopXAdx;
        private int bricksInExpansion = 0;

        private double leg2TrailingStop = 0.0;
        private double leg1ManagedStop = double.NaN;
        private double leg2ManagedStop = double.NaN;
        private bool leg1Hit = false;
        private int oppositeBrickCount = 0;

        // Leg2 deferred entry state
        private bool   awaitingLeg2   = false;
        private double leg1EntryPrice  = 0.0;
        private double leg1TargetPrice = 0.0;
        private double leg2EntryPrice  = 0.0;
        private int    tradeDir        = 0;   // 1 = long, -1 = short

        // Time gate + entry cooldown state (C1/C2)
        private DateTime lastExitTime = DateTime.MinValue;

        // Same-direction cap state (SF-27). Param default 0 = OFF.
        private int  _sameDirCount  = 0;
        private int  _lastEntryDir  = 0;   // 1 = long, -1 = short
        private bool _dirRegistered = false;

        // Session loss cap state. Param default 1 = stop after first losing flat-to-flat cycle.
        private int _sessionLossCount = 0;
        private int _lastProcessedTradeCount = 0;
        private double _cycleStartCumProfit = 0.0;
        private bool _cycleInProgress = false;

        // Slow Stop X ADX/DI state (secondary 30/60 minute series).
        private double _slowSumTr = 0.0;
        private double _slowSumDmPlus = 0.0;
        private double _slowSumDmMinus = 0.0;
        private double _slowDiPlus = 0.0;
        private double _slowDiMinus = 0.0;
        private double _slowPrevDiPlus = 0.0;
        private double _slowPrevDiMinus = 0.0;
        private bool _slowStopXExitLong = false;
        private bool _slowStopXExitShort = false;

        // Stage 1 trade logger
        private V3CTradeLogger _logger;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = "VC3_Expansion_NQ_LIVE: NQ-1A clone, MNQ, time blocks + HMM block-list (TrendDn=NOT TRADE).";
                Name                                        = "VC3_Expansion_NQ_LIVE";
                Calculate                                   = Calculate.OnBarClose;
                EntriesPerDirection                         = 2;
                EntryHandling                               = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy                = true;
                ExitOnSessionCloseSeconds                   = 30;
                IsFillLimitOnTouch                          = false;
            }
            else if (State == State.Configure)
            {
                if (UseSlowStopX)
                    AddDataSeries(BarsPeriodType.Minute, SlowStopXMinutes >= 60 ? 60 : 30);
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(14);
                if (UseSlowStopX && BarsArray.Length > 1)
                    slowStopXAdx = ADX(BarsArray[1], SlowStopXAdxPeriod);

                _logger = new V3CTradeLogger(this, AccountNameFilter, "V3C", TradeLogFolder);
                _cycleStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
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
            if (BarsInProgress == 1)
            {
                UpdateSlowStopX();
                return;
            }

            if (BarsInProgress != 0)
                return;

            if (CurrentBar < 20) return;

            ApplySlowStopXExits();

            if (Bars.IsFirstBarOfSession)
            {
                ResetSameDirCounter();
                ResetSessionLossCounter();
            }

            UpdateSessionLossCounterFromClosedCycle();

            // 1. V3C REGIME GATEKEEPER
            bool expansionAllowed = IsExpansionAllowed(out bool allowLong, out bool allowShort);
            ApplyTradeDirectionFilter(ref allowLong, ref allowShort);

            if (expansionAllowed && !allowLong && !allowShort)
            {
                DebugGate("Blocked: trade direction filter");
                expansionAllowed = false;
            }

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
                bool hmmBlocked  = IsHmmBlocked();
                bool lossBlocked = ConsecutiveLossBlocked();

                if (timeBlocked)  DebugGate("Blocked: inside configured time block");
                if (!cooldownOk)  DebugGate("Blocked: entry cooldown active");
                if (lossBlocked)  DebugGate("Blocked: max consecutive losses reached");

                if (expansionAllowed && bricksInExpansion >= WaitBricks && !timeBlocked && cooldownOk && !hmmBlocked && !lossBlocked)
                {
                    bool isGreenBrick = Close[0] > Open[0];
                    bool isRedBrick = Close[0] < Open[0];
                    double riskTicks = (atr[0] * InitialRiskAtr) / TickSize;
                    // Whole-tick bracket distance. Ticks mode anchors the stop and
                    // target to the actual entry fill, so a session-open gap can't
                    // leave the stop on the wrong side of the market — that gets the
                    // OCO bracket rejected and the strategy terminates itself.
                    int riskTicksI = Math.Max(1, (int)Math.Round(riskTicks));

                    if (isGreenBrick && allowLong && !SameDirBlocked(1))
                    {
                        double stp  = Close[0] - (riskTicksI * TickSize);
                        double tgt1 = Close[0] + (riskTicksI * TickSize);

                        SetStopLoss("Leg1", CalculationMode.Ticks, riskTicksI, false);
                        SetProfitTarget("Leg1", CalculationMode.Ticks, riskTicksI);
                        EnterLong(TotalContracts / 2, "Leg1");

                        // Leg2 is deferred — fires only when Leg1 profit >= Leg2ProfitGatePct × target
                        leg2TrailingStop = stp;
                        leg1ManagedStop  = stp;
                        leg1EntryPrice   = Close[0];
                        leg1TargetPrice  = tgt1;
                        awaitingLeg2     = true;
                        tradeDir         = 1;
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
                            leg2ManagedStop = leg2TrailingStop;
                            leg2EntryPrice = Close[0];
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
                    leg2ManagedStop = leg2TrailingStop;
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

                if (!leg1Hit && Leg1TrailMode != VC3ExpansionTrailMode.Off)
                    ApplyLegTrail("Leg1", Leg1TrailMode, Leg1TrailingNBars, Leg1TrailingOffsetTicks,
                        Leg1Step1ATR, Leg1Step2ATR, Leg1BreakevenPlusTicks, Leg1TrailAtrMult,
                        leg1EntryPrice, ref leg1ManagedStop);

                if (!awaitingLeg2 && Leg2TrailMode != VC3ExpansionTrailMode.Off)
                    ApplyLegTrail("Leg2", Leg2TrailMode, Leg2TrailingNBars, Leg2TrailingOffsetTicks,
                        Leg2Step1ATR, Leg2Step2ATR, Leg2BreakevenPlusTicks, Leg2TrailAtrMult,
                        leg2EntryPrice, ref leg2ManagedStop);

                // D. Legacy runner trail (kept when the new Leg2 trail selector is Off)
                if (leg1Hit && Leg2TrailMode == VC3ExpansionTrailMode.Off)
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

        private double RT(double price)
        {
            return Instrument.MasterInstrument.RoundToTickSize(price);
        }

        private void SetStopSafe(string signal, double price, bool isLong)
        {
            if (double.IsNaN(price)) return;

            double clamped = isLong
                ? Math.Min(price, RT(Close[0] - TickSize))
                : Math.Max(price, RT(Close[0] + TickSize));

            SetStopLoss(signal, CalculationMode.Price, clamped, false);
        }

        private double BarNStopLong(int bars, int offsetTicks)
        {
            int n = Math.Max(1, bars);
            double lo = Low[0];

            for (int i = 1; i < n && i <= CurrentBar; i++)
                lo = Math.Min(lo, Low[i]);

            return RT(lo - TickSize * Math.Max(0, offsetTicks));
        }

        private double BarNStopShort(int bars, int offsetTicks)
        {
            int n = Math.Max(1, bars);
            double hi = High[0];

            for (int i = 1; i < n && i <= CurrentBar; i++)
                hi = Math.Max(hi, High[i]);

            return RT(hi + TickSize * Math.Max(0, offsetTicks));
        }

        private void ApplyLegTrail(string signal, VC3ExpansionTrailMode mode, int nBars, int offsetTicks,
            double step1Atr, double step2Atr, int bePlusTicks, double trailAtrMult,
            double entryPrice, ref double managedStop)
        {
            if (mode == VC3ExpansionTrailMode.Off || tradeDir == 0 || entryPrice <= 0)
                return;

            int bse = BarsSinceEntryExecution(0, signal, 0);
            if (bse == -1)
                return;

            bool isLong = Position.MarketPosition == MarketPosition.Long;

            switch (mode)
            {
                case VC3ExpansionTrailMode.BarNTrailing:
                    if (bse >= Math.Max(1, nBars))
                    {
                        double proposed = isLong
                            ? BarNStopLong(nBars, offsetTicks)
                            : BarNStopShort(nBars, offsetTicks);

                        managedStop = double.IsNaN(managedStop)
                            ? proposed
                            : (isLong ? Math.Max(managedStop, proposed) : Math.Min(managedStop, proposed));
                    }
                    break;

                case VC3ExpansionTrailMode.AtrStep:
                {
                    double move = isLong ? Close[0] - entryPrice : entryPrice - Close[0];
                    double step1 = Math.Max(0.0, step1Atr) * atr[0];
                    double step2 = Math.Max(0.0, step2Atr) * atr[0];

                    if (move >= step2)
                    {
                        double atrTrail = isLong
                            ? RT(Close[0] - Math.Max(0.1, trailAtrMult) * atr[0])
                            : RT(Close[0] + Math.Max(0.1, trailAtrMult) * atr[0]);

                        managedStop = double.IsNaN(managedStop)
                            ? atrTrail
                            : (isLong ? Math.Max(managedStop, atrTrail) : Math.Min(managedStop, atrTrail));
                    }
                    else if (move >= step1)
                    {
                        double be = isLong
                            ? RT(entryPrice + Math.Max(0, bePlusTicks) * TickSize)
                            : RT(entryPrice - Math.Max(0, bePlusTicks) * TickSize);

                        managedStop = double.IsNaN(managedStop)
                            ? be
                            : (isLong ? Math.Max(managedStop, be) : Math.Min(managedStop, be));
                    }
                    break;
                }
            }

            SetStopSafe(signal, managedStop, isLong);
        }

        private void ApplyTradeDirectionFilter(ref bool allowLong, ref bool allowShort)
        {
            if (TradeDirection == VC3ExpansionTradeDirectionMode.LongOnly)
                allowShort = false;
            else if (TradeDirection == VC3ExpansionTradeDirectionMode.ShortOnly)
                allowLong = false;
        }

        // ===== SLOW STOP X (ADX/DI EXIT) =====
        private void UpdateSlowStopX()
        {
            if (!UseSlowStopX || BarsArray.Length < 2 || slowStopXAdx == null)
                return;

            int cb = CurrentBars[1];
            double high0 = Highs[1][0];
            double low0 = Lows[1][0];

            if (cb == 0)
            {
                _slowSumTr = high0 - low0;
                _slowSumDmPlus = 0.0;
                _slowSumDmMinus = 0.0;
                _slowDiPlus = 0.0;
                _slowDiMinus = 0.0;
                _slowPrevDiPlus = 0.0;
                _slowPrevDiMinus = 0.0;
                return;
            }

            double high1 = Highs[1][1];
            double low1 = Lows[1][1];
            double close1 = Closes[1][1];
            double tr = Math.Max(high0 - low0, Math.Max(Math.Abs(high0 - close1), Math.Abs(low0 - close1)));
            double upMove = high0 - high1;
            double downMove = low1 - low0;
            double dmPlus = (upMove > 0 && upMove > downMove) ? upMove : 0.0;
            double dmMinus = (downMove > 0 && downMove > upMove) ? downMove : 0.0;

            if (cb < SlowStopXAdxPeriod)
            {
                _slowSumTr += tr;
                _slowSumDmPlus += dmPlus;
                _slowSumDmMinus += dmMinus;
            }
            else
            {
                _slowSumTr = _slowSumTr - (_slowSumTr / SlowStopXAdxPeriod) + tr;
                _slowSumDmPlus = _slowSumDmPlus - (_slowSumDmPlus / SlowStopXAdxPeriod) + dmPlus;
                _slowSumDmMinus = _slowSumDmMinus - (_slowSumDmMinus / SlowStopXAdxPeriod) + dmMinus;
            }

            _slowPrevDiPlus = _slowDiPlus;
            _slowPrevDiMinus = _slowDiMinus;

            double safeTr = _slowSumTr.ApproxCompare(0) == 0 ? 1e-9 : _slowSumTr;
            _slowDiPlus = 100.0 * (_slowSumDmPlus / safeTr);
            _slowDiMinus = 100.0 * (_slowSumDmMinus / safeTr);

            if (cb < SlowStopXAdxPeriod + 2)
                return;

            bool crossUp = _slowDiPlus > _slowDiMinus && _slowPrevDiPlus <= _slowPrevDiMinus;
            bool crossDn = _slowDiPlus < _slowDiMinus && _slowPrevDiPlus >= _slowPrevDiMinus;
            bool adxWeak = slowStopXAdx[0] < SlowStopXAdxMinLevel;

            if (Position.MarketPosition == MarketPosition.Long && (adxWeak || crossDn))
                _slowStopXExitLong = true;
            else if (Position.MarketPosition == MarketPosition.Short && (adxWeak || crossUp))
                _slowStopXExitShort = true;
        }

        private void ApplySlowStopXExits()
        {
            if (!UseSlowStopX)
                return;

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                _slowStopXExitLong = false;
                _slowStopXExitShort = false;
                return;
            }

            if (Position.MarketPosition == MarketPosition.Long && _slowStopXExitLong)
            {
                ExitLong("StopX_L1", "Leg1");
                if (!awaitingLeg2)
                    ExitLong("StopX_L2", "Leg2");
                awaitingLeg2 = false;
                _slowStopXExitLong = false;
            }
            else if (Position.MarketPosition == MarketPosition.Short && _slowStopXExitShort)
            {
                ExitShort("StopX_S1", "Leg1");
                if (!awaitingLeg2)
                    ExitShort("StopX_S2", "Leg2");
                awaitingLeg2 = false;
                _slowStopXExitShort = false;
            }
        }

        // ===== C1: TIME GATE =====
        // Returns true if the current bar time falls inside any enabled blocked window.
        private bool InBlockedWindow()
        {
            if (!EnableTimeBlocks) return false;

            int hhmm = Time[0].Hour * 100 + Time[0].Minute;
            return IsInBlock(hhmm, Block1Start, Block1End)
                || IsInBlock(hhmm, Block2Start, Block2End)
                || IsInBlock(hhmm, Block3Start, Block3End)
                || IsInBlock(hhmm, Block4Start, Block4End);
        }

        // ===== HMM BLOCK-LIST GATE (LIVE) =====
        // Returns true if the live HUD's HMMMicro state is in the operator's blocked set.
        // Read-only HUD access; fails OPEN (returns false) when the HUD is missing/UNKNOWN.
        private bool IsHmmBlocked()
        {
            if (!EnableHMMGate) return false;
            if (string.IsNullOrEmpty(HMMBlockedStates)) return false;

            Indicators.RegimeMatrixHUD_V3C hud = GetV3CHud();
            if (hud == null) { DebugGate("HMM gate: HUD missing — fail open (trade allowed)."); return false; }

            string state = (hud.HMMMicro ?? "").Trim();
            if (string.IsNullOrEmpty(state) || string.Equals(state, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
                { DebugGate("HMM gate: state UNKNOWN — fail open (trade allowed)."); return false; }

            foreach (string blocked in HMMBlockedStates.Split(','))
            {
                if (string.Equals(blocked.Trim(), state, StringComparison.OrdinalIgnoreCase))
                {
                    DebugGate("Blocked: HMM state '" + state + "' is in the block-list.");
                    return true;
                }
            }
            return false;
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

        // ===== CONSECUTIVE LOSS LIMIT =====
        private bool ConsecutiveLossBlocked()
        {
            return MaxConsecutiveLosses > 0
                && _sessionLossCount >= MaxConsecutiveLosses;
        }

        private void UpdateSessionLossCounterFromClosedCycle()
        {
            if (!_cycleInProgress || Position.MarketPosition != MarketPosition.Flat)
                return;

            int tradeCount = SystemPerformance.AllTrades.Count;
            if (tradeCount <= _lastProcessedTradeCount)
                return;

            double cyclePnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - _cycleStartCumProfit;

            if (cyclePnL < 0)
                _sessionLossCount++;

            if (DebugV3CGate)
                Print($"{Time[0]} {Name} Loss cap: closed cycle PnL={cyclePnL:F2}, sessionLossCount={_sessionLossCount}, max={MaxConsecutiveLosses}");

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

        // =========================================================================
        // STAGE 1 TRADE LOGGING — delegates to V3CTradeLogger
        // =========================================================================
        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId,
            DateTime time)
        {
            _logger?.OnExecution(execution, null);

            if (execution.Order != null
                && (execution.Order.OrderAction == OrderAction.Buy || execution.Order.OrderAction == OrderAction.SellShort))
            {
                if (string.Equals(execution.Order.Name, "Leg1", StringComparison.OrdinalIgnoreCase))
                    leg1EntryPrice = price;
                else if (string.Equals(execution.Order.Name, "Leg2", StringComparison.OrdinalIgnoreCase))
                    leg2EntryPrice = price;
            }

            if (execution.Order != null && !_cycleInProgress
                && (execution.Order.OrderAction == OrderAction.Buy || execution.Order.OrderAction == OrderAction.SellShort))
            {
                _cycleStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                _cycleInProgress = true;
            }

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
