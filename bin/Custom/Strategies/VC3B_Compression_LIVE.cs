// CC BY-NC 4.0
// ============================================================================
// VC3-B_Compression_LIVE â€” LIVE clone of V3_Compression_Sniper_V3C
// Cloned: 2026-05-23 (for SimV3C-ES-2A live Apex testing)
// Purpose: same entry engine as V3_Compression_Sniper_V3C, plus built-in,
//          OPERATOR-ADJUSTABLE limiters derived from the 2-week ES-2A study:
//            - Time-of-day window(s) + a lockout carve-out (default locks 14:30-15:00,
//              the 0-for-4 / -$1,712 block) so the bot only runs where it thrives.
//            - Regime gate toggle (existing Trinity filter; OFF = proven Mode-A baseline).
//            - HMM gate toggle (NEW; reads hud.HMMMicro). NOTE: 2-week ES-2A data shows
//              the HMM gate HURTS this cell (drops profitable Transition trades), so it
//              defaults OFF — it is wired purely for A/B testing.
//            - OPEN-window bypass: trades before 09:35 (regime label not yet live;
//              "NO_DATA" = OPEN per operator) skip the regime + HMM gates.
//   All thresholds are NinjaScript properties so they can be retuned as data grows.
//   UNVERIFIED: this file has NOT been compiled in NinjaTrader by Claude. Compile +
//   sim-replay one week before any live use. Logic mirrors the proven parent.
// ---- inherited parent history ----
// V3_Compression_Sniper_V3C â€” PATCHED VERSION
// Audit Date: 2026-05-06
// Fix Date:   2026-05-21
// Changes vs. original V3C:
//   [P1] Added MaxSessionTrades + CooldownBarsAfterExit session guard
//   [P1] Added MaxConsecutiveLosses circuit breaker
//   [P2] Added mutual exclusion â€” long and short cannot fire same bar
//   [P2] Tightened slow EMA touch to [1] only (removed stale [2] lookback)
//   [P3] Added MinStopTicksFloor parameter (default 8 ticks)
//   [P4] Bars.IsFirstBarOfSession resets all session-scoped counters
//   [INFO] DebugV3CGate should be set True during all test sessions
// 2026-05-21 APEX RATE-LIMIT FIX:
//   [FIX] RealtimeErrorHandling changed StopCancelClose -> IgnoreAllErrors
//         Root cause: Apex rate-limits stop/target submissions on order fill;
//         StopCancelClose responded by firing a burst of cancel requests, each
//         also rate-limited, generating hundreds of error dialogs and terminating
//         the strategy. IgnoreAllErrors breaks the cascade.
//   [FIX] OnOrderUpdate now detects rate-limit rejections by comment text,
//         sets _needStopRearm / _needTargetRearm flags, and logs clearly.
//   [FIX] OnBarUpdate re-submits rejected stop/target orders on the next bar
//         using cached riskTicks/rewardTicks from the entry that triggered them.
//   [FIX] Entry-order rate-limit rejections correct _sessionTradeCount.
// ============================================================================
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
    public class VC3B_Compression_LIVE : Strategy
    {
        // ===== 0. V3C REGIME GATE (operator-adjustable; OFF = proven ES-2A Mode-A baseline) =====
        [NinjaScriptProperty]
        [Display(Name="Enable Regime Gate (Trinity)", Description="ON = require V3C TREND_COMPRESSION regime (Mode-B). OFF = ungated baseline (the proven ES-2A config). Flip for A/B testing.", GroupName="0. V3C Regime Gate", Order=0)]
        public bool EnableTrinityFilter { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name="Debug V3C Gate", Description="ENABLE during testing to log all gate decisions.", GroupName="0. V3C Regime Gate", Order=1)]
        public bool DebugV3CGate { get; set; } = true; // [PATCH] Default changed to true for testing visibility

        // ===== 1. LIVE TIME & HMM LIMITERS (operator-adjustable; derived from 2-week ES-2A study) =====
        // Times are HHmm integers in the chart's session time zone (e.g. 930 = 09:30, 1430 = 14:30).
        [NinjaScriptProperty]
        [Display(Name="Enable Time Window", Description="Master switch for the time-of-day limiter. OFF = trade the whole session.", GroupName="1. LIVE Limiters", Order=0)]
        public bool EnableTimeWindow { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Open-Bypass Until (HHmm)", Description="Trades before this time are OPEN: they bypass the regime + HMM gates (regime label is not live until ~09:35). 0 = no open bypass.", GroupName="1. LIVE Limiters", Order=1)]
        public int OpenWindowEndHHmm { get; set; } = 935;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 1 Start (HHmm)", GroupName="1. LIVE Limiters", Order=2)]
        public int Window1StartHHmm { get; set; } = 930;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 1 End (HHmm)", Description="End is EXCLUSIVE. 1300 = trade through 12:59:59 and stop new entries at 1:00pm (the morning-only ES-2A test window: 2wk +$9,800 / 61% WR).", GroupName="1. LIVE Limiters", Order=3)]
        public int Window1EndHHmm { get; set; } = 1300;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 2 Start (HHmm)", Description="Optional 2nd window. 0/0 = disabled.", GroupName="1. LIVE Limiters", Order=4)]
        public int Window2StartHHmm { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Window 2 End (HHmm)", GroupName="1. LIVE Limiters", Order=5)]
        public int Window2EndHHmm { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Lockout Start (HHmm)", Description="No new entries inside this carve-out (default 1430-1500 = the 0-for-4 / -$1,712 block). 0/0 = no lockout.", GroupName="1. LIVE Limiters", Order=6)]
        public int LockoutStartHHmm { get; set; } = 1430;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name="Lockout End (HHmm)", GroupName="1. LIVE Limiters", Order=7)]
        public int LockoutEndHHmm { get; set; } = 1500;

        [NinjaScriptProperty]
        [Display(Name="Enable HMM Gate", Description="ON = require hud.HMMMicro in 'HMM Allowed States'. NOTE: 2-week ES-2A data shows this HURTS net (cuts profitable Transition trades) — default OFF; wired for A/B testing.", GroupName="1. LIVE Limiters", Order=8)]
        public bool EnableHMMGate { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name="HMM Allowed States", Description="Comma-separated hud.HMMMicro values allowed when the HMM gate is ON.", GroupName="1. LIVE Limiters", Order=9)]
        public string HMMAllowedStates { get; set; } = "TrendUp,TrendDown,Transition";

        // ===== 0b. STAGE 1 TRADE LOGGING =====
        [NinjaScriptProperty]
        [Display(Name="Account Name Filter", Description="Exact NT8 account name.", GroupName="0b. Trade Logging", Order=0)]
        public string AccountNameFilter { get; set; } = "";

        [NinjaScriptProperty]
        [Display(Name="Trade Log Folder", GroupName="0b. Trade Logging", Order=1)]
        public string TradeLogFolder { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3C\TradeLog";

        // ===== 2. RISK MANAGEMENT =====
        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name="Contracts", GroupName="2. Risk Management", Order=0)]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name="Fixed Target (ATR)", Description="Strict target. Must be >= 1.25x StopAtr for positive edge.", GroupName="2. Risk Management", Order=1)]
        public double TargetAtr { get; set; } = 1.25; // [PATCH] Raised default from 0.75 to 1.25 for positive R:R

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name="Stop Loss (ATR)", Description="Hard stop behind the swing.", GroupName="2. Risk Management", Order=2)]
        public double StopAtr { get; set; } = 0.9; // [PATCH] Aligned with 2C best performer

        // [PATCH P1] NEW: Session-level safety caps
        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name="Max Session Trades", Description="Circuit breaker: stops new entries after N trades this session.", GroupName="2. Risk Management", Order=3)]
        public int MaxSessionTrades { get; set; } = 20;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name="Cooldown Bars After Exit", Description="Minimum bars to wait after any exit before re-entering.", GroupName="2. Risk Management", Order=4)]
        public int CooldownBarsAfterExit { get; set; } = 3;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name="Min Stop Ticks Floor", Description="Minimum stop distance in ticks. Prevents noise-level stops.", GroupName="2. Risk Management", Order=5)]
        public int MinStopTicksFloor { get; set; } = 8; // [PATCH P3] NEW

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name="Max Consecutive Losses", Description="Opens circuit breaker after N consecutive stop-outs.", GroupName="2. Risk Management", Order=6)]
        public int MaxConsecutiveLosses { get; set; } = 4; // [PATCH P1] NEW

        // ===== 5. SAME-DIRECTION CAP =====
        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name="Max Same-Direction Trades", Description="Caps consecutive same-direction entries per session. 0 = OFF (no limit). Counter resets on a direction flip and at session start. Week-2 baseline = 0.", GroupName="5. Same-Direction Cap", Order=0)]
        public int MaxSameDirTrades { get; set; } = 0;

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
        private V3CTradeLogger _logger;

        // [PATCH P1] Session-scoped counters reset each session via Bars.IsFirstBarOfSession
        private int _sessionTradeCount    = 0;
        private int _consecutiveLosses    = 0;
        private int _lastExitBar          = -99;
        private bool _sessionCircuitOpen  = false;

        // Same-direction cap state (SF-27). Param default 0 = OFF.
        private int  _sameDirCount  = 0;
        private int  _lastEntryDir  = 0;   // 1 = long, -1 = short
        private bool _dirRegistered = false;

        // [FIX] Apex rate-limit resilience — track pending stop/target re-arms
        private bool _needStopRearm   = false;  // set true when stop order is rate-limit rejected
        private bool _needTargetRearm = false;  // set true when target order is rate-limit rejected
        private int  _rearmRiskTicks  = 0;      // cached ticks for re-arm
        private int  _rearmRewardTicks = 0;     // cached ticks for re-arm
        private string _rearmSignalName = "";   // "SnipeL" or "SnipeS"

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = "VC3-B_Compression_LIVE: ES-2A clone w/ operator-adjustable time/regime/HMM limiters.";
                Name                                        = "VC3B_Compression_LIVE";
                Calculate                                   = Calculate.OnBarClose;
                EntriesPerDirection                         = 1;
                EntryHandling                               = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy                = true;
                ExitOnSessionCloseSeconds                   = 30;
                IsFillLimitOnTouch                          = false;
                TraceOrders                                 = false;
                // [FIX] IgnoreAllErrors prevents Apex rate-limit rejections from cascading into
                // a flood of cancel requests and strategy self-termination. Rejection detection
                // and re-arm logic in OnOrderUpdate/OnBarUpdate replace the default safety net.
                RealtimeErrorHandling                       = RealtimeErrorHandling.IgnoreAllErrors;
            }
            else if (State == State.DataLoaded)
            {
                atr     = ATR(14);
                fastEma = EMA(FastEmaPeriod);
                slowEma = EMA(SlowEmaPeriod);
                _logger = new V3CTradeLogger(this, AccountNameFilter, "V3C", TradeLogFolder);
            }
        }

        // [PATCH P4] Reset all session counters at the start of each new session.
        private void ResetSessionCounters()
        {
            _sessionTradeCount   = 0;
            _consecutiveLosses   = 0;
            _lastExitBar         = -99;
            _sessionCircuitOpen  = false;
            ResetSameDirCounter();

            DebugGate("New session started - counters reset.");
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(SlowEmaPeriod, 22)) return;

            if (Bars.IsFirstBarOfSession)
                ResetSessionCounters();

            // [FIX] Re-arm stop/target orders that were rate-limit rejected by Apex on a prior bar.
            // Only fires when in a live position with pending re-arm flags.
            if ((_needStopRearm || _needTargetRearm) && Position.MarketPosition != MarketPosition.Flat)
            {
                if (_needStopRearm)
                {
                    SetStopLoss(_rearmSignalName, CalculationMode.Ticks, _rearmRiskTicks, false);
                    Print($"{Time[0]} {Name}: [REARM] Re-submitted stop for '{_rearmSignalName}' ({_rearmRiskTicks}t) after rate-limit rejection.");
                    _needStopRearm = false;
                }
                if (_needTargetRearm)
                {
                    SetProfitTarget(_rearmSignalName, CalculationMode.Ticks, _rearmRewardTicks);
                    Print($"{Time[0]} {Name}: [REARM] Re-submitted target for '{_rearmSignalName}' ({_rearmRewardTicks}t) after rate-limit rejection.");
                    _needTargetRearm = false;
                }
                return; // Skip entry logic this bar — orders are being re-armed
            }

            // =========================================================================
            // LIVE LIMITER: time-of-day window + OPEN bypass (entries only; exits unaffected)
            // =========================================================================
            bool isOpenWindow = false;
            if (EnableTimeWindow)
            {
                if (!IsWithinTradingWindow(out isOpenWindow))
                {
                    DebugGate($"Blocked: outside trading window (now={NowHHmm()})");
                    return; // no new entries outside the allowed window; existing orders/exits continue
                }
            }

            // Regime gate: OPEN window bypasses it; otherwise use the existing Trinity logic.
            bool allowLong, allowShort, compressionAllowed;
            if (isOpenWindow)
            {
                compressionAllowed = true;
                allowLong  = true;
                allowShort = true;
                DebugGate("OPEN window — regime + HMM gates bypassed.");
            }
            else
            {
                compressionAllowed = IsCompressionAllowed(out allowLong, out allowShort);

                // HMM gate (NEW): only applies outside the OPEN window. Default OFF.
                if (compressionAllowed && EnableHMMGate && !IsHmmAllowed())
                {
                    DebugGate("Blocked: HMM gate (state not in allowed set).");
                    compressionAllowed = false;
                }
            }

            // =========================================================================
            // PHASE 2: ENTRY LOGIC
            // =========================================================================
            if (Position.MarketPosition == MarketPosition.Flat && compressionAllowed)
            {
                // [PATCH P1] Session guard checks â€” bail out before any entry evaluation
                bool cooldownActive    = (CurrentBar - _lastExitBar) < CooldownBarsAfterExit;
                bool sessionCapHit     = _sessionTradeCount >= MaxSessionTrades;
                bool circuitOpen       = _sessionCircuitOpen;

                if (cooldownActive)
                {
                    DebugGate($"Skipping: cooldown active ({CurrentBar - _lastExitBar}/{CooldownBarsAfterExit} bars)");
                    return;
                }
                if (sessionCapHit)
                {
                    DebugGate($"Skipping: session cap hit ({_sessionTradeCount}/{MaxSessionTrades} trades)");
                    return;
                }
                if (circuitOpen)
                {
                    DebugGate($"Skipping: circuit breaker open ({_consecutiveLosses} consecutive losses)");
                    return;
                }

                // [PATCH P3] Apply MinStopTicksFloor to prevent noise-level stops
                int riskTicks   = Math.Max(MinStopTicksFloor, (int)Math.Round((atr[0] * StopAtr)   / TickSize));
                int rewardTicks = Math.Max(MinStopTicksFloor, (int)Math.Round((atr[0] * TargetAtr) / TickSize));

                // [PATCH P2] Mutual exclusion flag â€” only one direction can fire per bar
                bool enteredThisBar = false;

                // ---------------------------------------------------------------------
                // LONG SNIPE (Buy the Dip)
                // ---------------------------------------------------------------------
                if (allowLong && !enteredThisBar)
                {
                    // [PATCH P2] Tightened to Low[1] only â€” removed stale Low[2] lookback
                    bool touchedSlowEma  = Low[1] <= slowEma[1];
                    bool closedAboveFast = Close[0] > fastEma[0] && Close[1] <= fastEma[1];

                    if (touchedSlowEma && closedAboveFast && !SameDirBlocked(1))
                    {
                        SetStopLoss("SnipeL",   CalculationMode.Ticks, riskTicks,   false);
                        SetProfitTarget("SnipeL", CalculationMode.Ticks, rewardTicks);
                        EnterLong(Contracts, "SnipeL");
                        enteredThisBar = true;        // [PATCH P2] Block short on same bar
                        _sessionTradeCount++;
                        // [FIX] Cache for re-arm if stop/target hit Apex rate limit
                        _rearmSignalName  = "SnipeL";
                        _rearmRiskTicks   = riskTicks;
                        _rearmRewardTicks = rewardTicks;
                        _needStopRearm    = false;
                        _needTargetRearm  = false;
                        DebugGate($"Long entry #{_sessionTradeCount} | Risk={riskTicks}t Target={rewardTicks}t");
                    }
                }

                // ---------------------------------------------------------------------
                // SHORT SNIPE (Sell the Rip)
                // ---------------------------------------------------------------------
                if (allowShort && !enteredThisBar) // [PATCH P2] Mutual exclusion enforced
                {
                    // [PATCH P2] Tightened to High[1] only â€” removed stale High[2] lookback
                    bool touchedSlowEma  = High[1] >= slowEma[1];
                    bool closedBelowFast = Close[0] < fastEma[0] && Close[1] >= fastEma[1];

                    if (touchedSlowEma && closedBelowFast && !SameDirBlocked(-1))
                    {
                        SetStopLoss("SnipeS",   CalculationMode.Ticks, riskTicks,   false);
                        SetProfitTarget("SnipeS", CalculationMode.Ticks, rewardTicks);
                        EnterShort(Contracts, "SnipeS");
                        _sessionTradeCount++;
                        // [FIX] Cache for re-arm if stop/target hit Apex rate limit
                        _rearmSignalName  = "SnipeS";
                        _rearmRiskTicks   = riskTicks;
                        _rearmRewardTicks = rewardTicks;
                        _needStopRearm    = false;
                        _needTargetRearm  = false;
                        DebugGate($"Short entry #{_sessionTradeCount} | Risk={riskTicks}t Target={rewardTicks}t");
                    }
                }
            }
        }

        private bool IsCompressionAllowed(out bool allowLong, out bool allowShort)
        {
            allowLong  = false;
            allowShort = false;

            if (!EnableTrinityFilter)
            {
                allowLong  = true;
                allowShort = true;
                return true;
            }

            Indicators.RegimeMatrixHUD_V3C hud = GetV3CHud();

            if (hud == null)                 { DebugGate("Blocked: HUD missing");          return false; }
            if (hud.StaleDataFlag)           { DebugGate("Blocked: stale data");           return false; }
            if (!string.Equals(hud.FinalRegime, "TREND_COMPRESSION", StringComparison.OrdinalIgnoreCase))
                                             { DebugGate("Blocked: FinalRegime=" + hud.FinalRegime); return false; }
            if (!hud.IsCompressionBotAllowed){ DebugGate("Blocked: CompressionBot OFF");   return false; }

            allowLong  = hud.AllowLong;
            allowShort = hud.AllowShort;

            if (!allowLong && !allowShort)   { DebugGate("Blocked: direction not allowed"); return false; }

            return true;
        }

        private Indicators.RegimeMatrixHUD_V3C GetV3CHud()
        {
            string chartSymbol  = Instrument.MasterInstrument.Name;
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
                Print($"{Time[0]} {Name}: {message}");
        }

        // ===== LIVE LIMITER HELPERS =====
        private int NowHHmm()
        {
            return Time[0].Hour * 100 + Time[0].Minute;
        }

        // Inclusive start, exclusive end. A 0/0 pair means "disabled". Handles only same-day windows.
        private bool InRangeHHmm(int now, int startHHmm, int endHHmm)
        {
            if (startHHmm <= 0 && endHHmm <= 0) return false;
            return now >= startHHmm && now < endHHmm;
        }

        // True if new entries are allowed at the current bar time. Sets isOpenWindow when the
        // bar is inside the pre-regime OPEN window (regime + HMM gates are bypassed there).
        private bool IsWithinTradingWindow(out bool isOpenWindow)
        {
            int now = NowHHmm();
            isOpenWindow = OpenWindowEndHHmm > 0
                         && now >= Window1StartHHmm
                         && now < OpenWindowEndHHmm;

            bool inWindow = InRangeHHmm(now, Window1StartHHmm, Window1EndHHmm)
                         || InRangeHHmm(now, Window2StartHHmm, Window2EndHHmm);

            bool inLockout = InRangeHHmm(now, LockoutStartHHmm, LockoutEndHHmm);

            // OPEN window always wins over a lockout overlap.
            if (isOpenWindow) return true;
            return inWindow && !inLockout;
        }

        // HMM gate: require the live HUD's HMMMicro state to be in the operator's allowed set.
        private bool IsHmmAllowed()
        {
            Indicators.RegimeMatrixHUD_V3C hud = GetV3CHud();
            if (hud == null) { DebugGate("HMM gate: HUD missing — blocking."); return false; }

            string state = (hud.HMMMicro ?? "").Trim();
            if (string.IsNullOrEmpty(HMMAllowedStates)) return false;

            foreach (string allowed in HMMAllowedStates.Split(','))
            {
                if (string.Equals(allowed.Trim(), state, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
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
            double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
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

            // [PATCH P1] Track exit bar and consecutive losses for circuit breaker
            if (execution.Order != null &&
                (execution.Order.OrderAction == OrderAction.Sell ||
                 execution.Order.OrderAction == OrderAction.BuyToCover))
            {
                _lastExitBar = CurrentBar;

                // Identify stop-outs by order name convention
                bool wasStop = execution.Order.Name != null &&
                               (execution.Order.Name.Contains("Stop") ||
                                execution.Order.Name.Contains("stop"));

                if (wasStop)
                {
                    _consecutiveLosses++;
                    DebugGate($"Stop-out #{_consecutiveLosses} consecutive losses.");

                    if (_consecutiveLosses >= MaxConsecutiveLosses)
                    {
                        _sessionCircuitOpen = true;
                        Print($"{Time[0]} {Name}: CIRCUIT BREAKER OPEN â€” {_consecutiveLosses} consecutive losses. No new entries this session.");
                    }
                }
                else
                {
                    // Profit target hit â€” reset consecutive loss streak
                    _consecutiveLosses = 0;
                    DebugGate("Profit target hit â€” consecutive loss streak reset.");
                }
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string comment)
        {
            if (orderState == OrderState.Working)
                _logger?.OnStopOrderSubmitted(order);

            // [FIX] Detect Apex rate-limit rejections and flag for re-arm rather than terminating.
            // With RealtimeErrorHandling.IgnoreAllErrors the strategy stays alive; we handle recovery here.
            if (orderState == OrderState.Rejected && order != null)
            {
                bool isRateLimit = (comment != null && comment.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
                                || (comment != null && comment.IndexOf("Rate limit", StringComparison.Ordinal) >= 0);

                string orderName = order.Name ?? "";
                bool isStop   = orderName.IndexOf("stop",   StringComparison.OrdinalIgnoreCase) >= 0;
                bool isTarget = orderName.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0
                             || orderName.IndexOf("profit", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isRateLimit)
                {
                    // Rate-limit rejection — set re-arm flags; OnBarUpdate will re-submit next bar
                    if (isStop)
                    {
                        _needStopRearm = true;
                        Print($"{Time[0]} {Name}: [RATE-LIMIT] Stop order '{orderName}' rejected by Apex — flagged for re-arm next bar.");
                    }
                    else if (isTarget)
                    {
                        _needTargetRearm = true;
                        Print($"{Time[0]} {Name}: [RATE-LIMIT] Target order '{orderName}' rejected by Apex — flagged for re-arm next bar.");
                    }
                    else
                    {
                        // Entry order rate-limited — decrement session count and log
                        if (_sessionTradeCount > 0) _sessionTradeCount--;
                        Print($"{Time[0]} {Name}: [RATE-LIMIT] Entry order '{orderName}' rejected by Apex — session count corrected to {_sessionTradeCount}.");
                    }
                }
                else
                {
                    // Genuine (non-rate-limit) rejection — log clearly for review
                    Print($"{Time[0]} {Name}: [ORDER REJECTED] Name='{orderName}' Error={error} Comment='{comment}' — NOT rate-limit; investigate.");
                }
            }
        }
    }
}

