// CC BY-NC 4.0
// Expansion_V3D_R2.cs  — V3D Institutional Regime Matrix  — REV 2 (2026-05-22)
// ─────────────────────────────────────────────────────────────────
// WHAT CHANGED FROM Expansion_V3D (REV 1):
//   REV 1 entry was "3 bricks elapsed -> any green brick = long, any red brick
//   = short" — no strength, no direction confirmation, no chop filter. Forensic
//   on SimV3D-NQ-1A (2026-05-21) showed PF 0.90 / net -$10,845: 72 of 160
//   positions stopped out at 19% win, the bleed concentrated in NQ SHORT and the
//   09:30-11:29 window. The brick-color trigger was the defect.
//
//   REV 2 replaces the entry trigger with the proven MomentumSlope OG V2 stack
//   (DI+/DI- crossover + ADX strength threshold + optional CI ceiling + optional
//   CI-falling/ADX-rising slope confirmation + optional VWAP anchor), while
//   PRESERVING the REV 1 trade-management layer wholesale: two-leg structure,
//   HybridBarN->tick trail, wobble-eject, regime-degradation trail tightening,
//   TRANSITION emergency-flat, Apex $1,500 sizing ceiling, supervisor permission
//   read (now with MaxRowAgeSec wall-clock staleness), V3D trade logger.
//
//   Designed to "breathe" on heavy UniRenko (NQ 40-80-120, ES 22-44-66) — the
//   same charts REV 1 / V3C Expansion use. ADX/DI/CI behave BETTER on heavy
//   UniRenko than on time bars: the brick rule already filters chop, so ADX
//   stays low in chop and rises sharply on real expansion. Operator decisions
//   2026-05-22: CI ceiling OFF by default (gating on CI too restrictive), VWAP
//   anchor OFF by default (OG testing favoured off), slope confirmation OFF by
//   default — base entry is DI cross + ADX threshold, letting the heavy UniRenko
//   chart do the chop-filtering. All three are available as toggles for testing.
//
// REGIME TARGET : TREND_EXPANSION (B/C mode gate). A-mode = baseline, no regime gate.
// CHART TYPE    : UniRenko (primary).  Calculate.OnBarClose.
// INSTRUMENT    : NQ / ES  (MNQ / MES supported via leader-symbol map).
//
// SIGNAL ARCHITECTURE
//   UniRenko brick physics — green brick = bullish momentum, red brick = bearish.
//   WaitBricks hysteresis gate: N consecutive expansion bricks required before entry.
//   Two-leg structure:
//     Leg1 — 1 : 1 ATR target (banker).  Fills quickly, confirms direction.
//     Leg2 — trailing runner.  HybridBarNThenTick trail mode:
//              first HybridBarN bars → N-bar low/high trailing stop.
//              after HybridBarN bars → ATR-based tick trailing stop.
//     Wobble-eject: WobbleBricks consecutive opposite bricks kills Leg2 immediately.
//
// ENTRY GATES (all must pass)
//   FinalRegime == TREND_EXPANSION
//   Phase not in {OPENING_AUCTION, EARLY_TEST, CASH_CLOSE}
//   RegimeConfidence >= MinConfidence  (default 75)
//   VelocityConfirmed == 1 in trade direction
//   StateAgeBars >= 2  (no one-bar pokes)
//   AllowLong / AllowShort from HUD matching brick direction
//   bricksInExpansion >= WaitBricks
//   consecutiveLosers < MaxConsecutiveLosses
//   expansionSizePct > 0  (no silent fallback — zero SizePct = no entry)
//   Daily P&L within [−DailyLossLimit, +DailyGoal]  (optional, 0 = off)
//
// EXITS
//   Leg1      : fixed profit target at 1 : 1 ATR.
//   Leg2      : HybridBarNThenTick trail + wobble-eject.
//   Emergency : FinalRegime == TRANSITION → immediate flat.
//   Soft exit : FinalRegime drops to TREND_COMPRESSION or ROTATION_* while
//               Leg2 is running → trail tightens to 60 % of normal distance
//               and Bar-N phase is bypassed (tick trail immediately).
//
// SIZING
//   Apex $1,500 per-trade ceiling: max = floor(1500 / dollarRiskPerContract).
//   Scaled by ExpansionSizePct from supervisor: actual = floor(max × SizePct/100).
//   Leg1 qty = sz / 2  (rounded up).  Leg2 qty = sz − Leg1 qty.
//
// CHANGES FROM FIRST DRAFT
//   FIX  — Leg1 detection uses currentLeg2Qty threshold (not hard-coded <= 1).
//   FIX  — Bar-N trail loop bounded by min(barsAfterLeg1, HybridBarN) correctly.
//   FIX  — expansionSizePct == 0 → no entry (removed silent 75 % fallback).
//   FIX  — RealtimeErrorHandling changed to StopCancelClose.
//   ADD  — Daily P&L guard (DailyGoal / DailyLossLimit, both optional, default 0).
//   ADD  — Regime-degradation trail tightening (TREND_COMPRESSION or ROTATION_*).
//   ADD  — ReasonCode field read + diagnostic Print on every entry.
// ─────────────────────────────────────────────────────────────────

#region Using declarations
using System;
using System.Globalization;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Expansion_V3D_R2 : Strategy
    {
        // =====================================================================
        // PARAMETERS
        // =====================================================================

        [NinjaScriptProperty]
        [Display(Name = "Data Folder Path", GroupName = "1. Regime", Order = 0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D";
        [NinjaScriptProperty]
        [Display(Name = "V3D Test Mode", Description = "A=baseline/no Trinity gate, B=full Trinity, C=soft Trinity diagnostics.", GroupName = "1. Regime", Order = 1)]
        public V3DPermissionMode PermissionMode { get; set; } = V3DPermissionMode.A_Baseline;

        [NinjaScriptProperty, Range(0, 3600)]
        [Display(Name = "Max Row Age Seconds (staleness)", Description = "Force-stale if matrix row TimestampET is older than this many seconds. 0 = disabled. Default 150. A-mode bypasses staleness gate by design.", GroupName = "1. Regime", Order = 4)]
        public int MaxRowAgeSec { get; set; } = 150;

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Baseline SizePct", Description = "Sizing used in Mode A when supervisor SizePct is intentionally ignored.", GroupName = "1. Regime", Order = 2)]
        public int BaselineSizePct { get; set; } = 75;

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Soft SizePct Floor", Description = "Minimum sizing used in Mode C when the full Trinity bot lane is zero.", GroupName = "1. Regime", Order = 3)]
        public int SoftSizePctFloor { get; set; } = 75;

        [NinjaScriptProperty]
        [Display(Name = "Account Name Filter", Description = "V3D only: exact NT8 account name allow-list for this strategy class. Separate multiple baked accounts with semicolons.", GroupName = "0b. Trade Logging", Order = 0)]
        public string AccountNameFilter { get; set; } = "SimV3D-NQ-1A-R2;SimV3D-ES-1A-R2";

        [NinjaScriptProperty]
        [Display(Name = "Configured Strategy Name", Description = "V3D only: exported strategy identity. Leave as the baked default unless intentionally renaming the tab.", GroupName = "0b. Trade Logging", Order = 1)]
        public string ConfiguredStrategyName { get; set; } = "Expansion_V3D_R2";

        [NinjaScriptProperty]
        [Display(Name = "Trade Log Folder", Description = "V3D only: internal strategy-owned export folder. External V3D trade-log exporter indicators are not required.", GroupName = "0b. Trade Logging", Order = 2)]
        public string TradeLogFolder { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D\TradeLog";
        // --- Risk ---
        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "ATR Risk Multiplier (Leg1 stop)", GroupName = "2. Risk", Order = 0)]
        public double InitialRiskAtr { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", GroupName = "2. Risk", Order = 1)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5.00  ES=12.50  MNQ=0.50  MES=1.25",
                 GroupName = "2. Risk", Order = 2)]
        public double TickValueDollars { get; set; } = 5.00;

        // --- Signal ---
        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name = "Wait Bricks (Hysteresis)",
                 Description = "Consecutive expansion bricks required before entry",
                 GroupName = "3. Signal", Order = 0)]
        public int WaitBricks { get; set; } = 3;

        [NinjaScriptProperty, Range(50, 100)]
        [Display(Name = "Min Regime Confidence", GroupName = "3. Signal", Order = 1)]
        public int MinConfidence { get; set; } = 75;

        // --- R2 Entry Signal (MomentumSlope OG V2 stack, tuned for heavy UniRenko) ---
        [NinjaScriptProperty, Range(2, 100)]
        [Display(Name = "ADX Period", GroupName = "3b. R2 Entry", Order = 0)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Threshold", Description = "Primary expansion gate. Heavy UniRenko already filters chop, so ADX is the main discriminator.", GroupName = "3b. R2 Entry", Order = 1)]
        public bool UseAdxThreshold { get; set; } = true;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "ADX Entry Threshold", Description = "NQ default 20; consider 18 for ES (smaller bricks).", GroupName = "3b. R2 Entry", Order = 2)]
        public double AdxEntryThreshold { get; set; } = 20.0;

        [NinjaScriptProperty, Range(2, 200)]
        [Display(Name = "CI Period", GroupName = "3b. R2 Entry", Order = 3)]
        public int CiPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Display(Name = "Use CI Ceiling (OFF by default)", Description = "Operator 2026-05-22: gating on CI found too restrictive. Left OFF; enable to test 45/55/60.", GroupName = "3b. R2 Entry", Order = 4)]
        public bool UseCiEntryThreshold { get; set; } = false;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "CI Entry Ceiling", Description = "When enabled, block entries with CI above this. 60 avoids only the upper-chop band.", GroupName = "3b. R2 Entry", Order = 5)]
        public double CiEntryThreshold { get; set; } = 60.0;

        [NinjaScriptProperty]
        [Display(Name = "Use Slope Confirmation", Description = "CI falling AND ADX rising over N bars = expansion phase transition. OFF by default.", GroupName = "3b. R2 Entry", Order = 6)]
        public bool UseSlopeGate { get; set; } = false;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "CI Slope Bars", GroupName = "3b. R2 Entry", Order = 7)]
        public int CiSlopeBars { get; set; } = 3;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "ADX Slope Bars", GroupName = "3b. R2 Entry", Order = 8)]
        public int AdxSlopeBars { get; set; } = 3;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "Min CI Decrease (slope)", GroupName = "3b. R2 Entry", Order = 9)]
        public double CiDecreaseMin { get; set; } = 3.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "Min ADX Increase (slope)", GroupName = "3b. R2 Entry", Order = 10)]
        public double AdxIncreaseMin { get; set; } = 3.0;

        [NinjaScriptProperty]
        [Display(Name = "Use VWAP Anchor (OFF by default)", Description = "Require LONG above / SHORT below session VWAP. OG testing favoured OFF.", GroupName = "3b. R2 Entry", Order = 11)]
        public bool UseVwapAnchor { get; set; } = false;

        // --- R2 A-mode failsafe gates (forensic-driven; B/C use supervisor gates) ---
        [NinjaScriptProperty]
        [Display(Name = "Block NQ AM window (A-mode)", Description = "Block the 10:00-11:29 NQ window that bled -$31k in the REV1 forensic.", GroupName = "3c. R2 Failsafe", Order = 0)]
        public bool BlockNQAmTimeWindow { get; set; } = true;

        [NinjaScriptProperty, Range(0, 235959)]
        [Display(Name = "AM Block Start (HHmmss)", GroupName = "3c. R2 Failsafe", Order = 1)]
        public int AmBlockStartHHmmss { get; set; } = 100000;

        [NinjaScriptProperty, Range(0, 235959)]
        [Display(Name = "AM Block End (HHmmss, exclusive)", GroupName = "3c. R2 Failsafe", Order = 2)]
        public int AmBlockEndHHmmss { get; set; } = 112900;

        [NinjaScriptProperty]
        [Display(Name = "Enforce 15:45 Cutoff", GroupName = "3c. R2 Failsafe", Order = 3)]
        public bool EnforceCutoff1545 { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Allow Short on NQ (A-mode)", Description = "SF-13/14: NQ short-trend chronically weak. OFF by default.", GroupName = "3c. R2 Failsafe", Order = 4)]
        public bool AllowShortNQ { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Blocked Phases A (csv)", Description = "A-mode phase block; B/C use supervisor BlockedPhases.", GroupName = "3c. R2 Failsafe", Order = 5)]
        public string BlockedPhasesACsv { get; set; } = "OPENING_AUCTION,FIRST_ACCEPTANCE,MID_MORNING_DISCOVERY,POST_IB_MACRO";

        // --- Trail ---
        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name = "Bar-N Trail Bars (before tick switch)", GroupName = "4. Trail", Order = 0)]
        public int HybridBarN { get; set; } = 3;

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "Tick Trail Multiplier (ATR, normal)", GroupName = "4. Trail", Order = 1)]
        public double TickTrailAtr { get; set; } = 1.25;

        [NinjaScriptProperty, Range(0.1, 3.0)]
        [Display(Name = "Tick Trail Multiplier (ATR, degraded regime)",
                 Description = "Applied when regime drops to COMPRESSION or ROTATION while runner is live",
                 GroupName = "4. Trail", Order = 2)]
        public double TickTrailAtrDegraded { get; set; } = 0.75;

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name = "Wobble-eject Bricks",
                 Description = "Consecutive opposite bricks to eject runner",
                 GroupName = "4. Trail", Order = 3)]
        public int WobbleBricks { get; set; } = 1;

        // --- Guards ---
        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "5. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 10;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0 = off)",
                 Description = "Stops new entries once daily profit reaches this level",
                 GroupName = "5. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0 = off)",
                 Description = "Stops new entries once daily loss reaches this level",
                 GroupName = "5. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

        // =====================================================================
        // REGIME STATE  (volatile — written by FileSystemWatcher thread)
        // =====================================================================
        private string   matrixFile         = "";
        private string   leaderSymbol       = "";
        private DateTime lastFileWriteUtc   = DateTime.MinValue;
        private DateTime lastFileCheck      = DateTime.MinValue;
        private const int MinCheckSeconds   = 15;
        private FileSystemWatcher regimeWatcher;
        private readonly object   fileLock  = new object();

        private volatile string finalRegime       = "UNKNOWN";
        private volatile string finalDirection    = "UNKNOWN";
        private volatile string phase             = "UNKNOWN";
        private volatile string reasonCode        = "";
        private volatile int    regimeConfidence  = 0;
        private volatile bool   allowLong         = false;
        private volatile bool   allowShort        = false;
        private volatile int    expansionSizePct  = 0;
        private volatile bool   staleDataFlag     = true;
        private volatile bool   parseFailed       = true;
        private volatile int    velocityConfirmed = 0;
        private volatile int    stateAgeBars      = 0;

        private Dictionary<string, int> headerIdx = new Dictionary<string, int>();

        // Phases in which no new entries are permitted
        private static readonly HashSet<string> BlockedPhases = new HashSet<string>
        {
            "OPENING_AUCTION", "EARLY_TEST", "CASH_CLOSE"
        };

        // Regimes that trigger trail tightening while runner is live
        private static readonly HashSet<string> DegradedRegimes = new HashSet<string>
        {
            "TREND_COMPRESSION", "ROTATION_LIQUID", "ROTATION_ILLIQUID"
        };

        // =====================================================================
        // INDICATORS
        // =====================================================================
        private ATR atr;
        private ADX adxInd;                 // R2: ADX strength
        private DM  dmInd;                  // R2: DI+/DI- crossover
        private Series<double> ciSeries;    // R2: Choppiness Index history (for slope gate)
        private double sessionCumPV  = 0.0; // R2: session VWAP accumulator (price*vol)
        private double sessionCumVol = 0.0; // R2: session VWAP accumulator (vol)
        private readonly HashSet<string> blockedPhasesA =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // =====================================================================
        // RUNTIME STATE
        // =====================================================================
        private int    consecutiveLosers  = 0;
        private int    lastTradeCount     = 0;
        private int    bricksInExpansion  = 0;
        private string lastRegimeSeen     = "";
        private double sessionStartProfit = 0;

        // Leg2 runner state
        private double leg2TrailingStop   = 0.0;
        private bool   leg1Hit            = false;
        private int    oppositeBrickCount = 0;
        private int    barsAfterLeg1      = 0;
        private int    currentLeg2Qty     = 1;   // dynamic threshold for leg1 detection

        // =====================================================================
        // HELPERS
        // =====================================================================
        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);
        private bool IsBaselineMode()
        {
            return PermissionMode == V3DPermissionMode.A_Baseline;
        }

        private bool IsSoftMode()
        {
            return PermissionMode == V3DPermissionMode.C_SoftTrinity;
        }

        private bool EnforceFullTrinityGates()
        {
            return PermissionMode == V3DPermissionMode.B_FullTrinity;
        }

        private bool AllowRegimeManagedExit()
        {
            return PermissionMode != V3DPermissionMode.A_Baseline;
        }

        private bool DirectionLongAllowed()
        {
            return IsBaselineMode() || allowLong;
        }

        private bool DirectionShortAllowed()
        {
            return IsBaselineMode() || allowShort;
        }

        private int EffectiveSizePct(int trinitySizePct)
        {
            if (IsBaselineMode()) return BaselineSizePct;
            if (IsSoftMode()) return Math.Max(trinitySizePct, SoftSizePctFloor);
            return trinitySizePct;
        }

        private string GetLeaderSymbol(string sym)
        {
            if (string.IsNullOrEmpty(sym)) return sym;
            sym = sym.Trim().ToUpper();
            if (sym == "MES") return "ES";
            if (sym == "MNQ") return "NQ";
            if (sym == "MGC") return "GC";
            if (sym == "MCL") return "CL";
            return sym;
        }

        private int CalcMaxContracts()
        {
            double atrVal = atr[0];
            if (atrVal <= 0) return 1;
            double dollarRisk = (atrVal * InitialRiskAtr) / TickSize * TickValueDollars;
            if (dollarRisk <= 0) return 1;
            return Math.Max(1, (int)(1500.0 / dollarRisk));
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
        {
            return Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));
        }

        private void ClearLegState()
        {
            leg2TrailingStop   = 0.0;
            leg1Hit            = false;
            oppositeBrickCount = 0;
            barsAfterLeg1      = 0;
            currentLeg2Qty     = 1;
        }

        // R2: Choppiness Index over `period` bars (0..100). High = chop, low = trending.
        // Computed inline (no indicator dependency) so it works identically on UniRenko.
        private double ComputeCI(int period)
        {
            if (CurrentBar < period + 1) return 50.0;
            double sumTR = 0.0;
            double hh = double.MinValue, ll = double.MaxValue;
            for (int i = 0; i < period; i++)
            {
                double tr = Math.Max(High[i] - Low[i],
                            Math.Max(Math.Abs(High[i] - Close[i + 1]),
                                     Math.Abs(Low[i]  - Close[i + 1])));
                sumTR += tr;
                if (High[i] > hh) hh = High[i];
                if (Low[i]  < ll) ll = Low[i];
            }
            double range = hh - ll;
            if (range <= 1e-9 || sumTR <= 1e-9) return 100.0;
            double ci = 100.0 * Math.Log10(sumTR / range) / Math.Log10(period);
            return Math.Max(0.0, Math.Min(100.0, ci));
        }

        // =====================================================================
        // STAGE 1 RAW TRADE LOG
        // =====================================================================
        private const string Stage1ModelVersion = "V3D";
        private const string Stage1BotName = "V3D_Expansion_R2_A";
        private const string Stage1DefaultAbMode = "A";
        private const string Stage1TradeLogHeader =
            "trade_date,entry_time,exit_time,model_version,account," +
            "strategy_name,bot_name,ab_mode,symbol,instrument,direction," +
            "contracts,entry_price,exit_price,gross_pnl,net_pnl,ticks," +
            "win_loss,exit_reason,initial_stop_price,initial_stop_distance," +
            "export_timestamp";

        private string tradeLogPath = "";
        private double entryPriceForLog = 0.0;
        private double initialStopPriceForLog = 0.0;
        private double initialStopTicksForLog = 0.0;
        private DateTime entryTimeForLog = DateTime.MinValue;
        private string directionForLog = "";
        private bool stage1TradeOpen = false;
        private int stage1StartTradeCount = 0;
        private int stage1EntryContracts = 0;
        private string stage1ExitReason = "UNKNOWN";

        private void ConfigureStage1TradeLog()
        {
            // 2026-05-19 EOD: inline Stage1 per-model trade logger retired —
            // superseded by NT8TradeExportAutoWriter (Regime Development Log SF-29).
            return;
#pragma warning disable CS0162
            string dir = Path.Combine(@"C:\Users\Valued Customer\NT8_Regimes", Stage1ModelVersion, "TradeLog");
            tradeLogPath = Path.Combine(dir, ResolveStage1BotName() + "_TradeLog.csv");
            EnsureStage1TradeLogHeader();
        }

        private void CaptureInitialStopForLog(double stopPrice, string direction)
        {
            initialStopPriceForLog = stopPrice;
            initialStopTicksForLog = 0.0;
            directionForLog = direction;
        }

        private void CaptureInitialStopTicksForLog(double stopTicks, string direction)
        {
            initialStopTicksForLog = stopTicks;
            initialStopPriceForLog = 0.0;
            directionForLog = direction;
        }

        private void HandleStage1TradeLogExecution(
            Execution execution, double price, int quantity,
            MarketPosition marketPosition, DateTime time)
        {
            // 2026-05-19 EOD: inline Stage1 per-model trade logger retired —
            // superseded by NT8TradeExportAutoWriter (Regime Development Log SF-29).
            return;
#pragma warning disable CS0162
            if (execution == null || execution.Order == null || quantity <= 0)
                return;

            OrderAction action = execution.Order.OrderAction;
            bool isEntry = action == OrderAction.Buy || action == OrderAction.SellShort;
            bool isExit = action == OrderAction.Sell || action == OrderAction.BuyToCover;

            if (isEntry)
            {
                string fillDirection = action == OrderAction.SellShort ? "SHORT" : "LONG";
                if (!stage1TradeOpen || directionForLog != fillDirection)
                {
                    stage1TradeOpen = true;
                    stage1StartTradeCount = SystemPerformance.AllTrades.Count;
                    entryTimeForLog = time;
                    entryPriceForLog = price;
                    directionForLog = fillDirection;
                    stage1EntryContracts = quantity;
                    stage1ExitReason = "UNKNOWN";
                }
                else
                {
                    double totalNotional = entryPriceForLog * stage1EntryContracts + price * quantity;
                    stage1EntryContracts += quantity;
                    entryPriceForLog = totalNotional / Math.Max(1, stage1EntryContracts);
                }

                if (initialStopPriceForLog <= 0.0 && initialStopTicksForLog > 0.0)
                {
                    initialStopPriceForLog = fillDirection == "LONG"
                        ? price - initialStopTicksForLog * TickSize
                        : price + initialStopTicksForLog * TickSize;
                    initialStopPriceForLog = Instrument.MasterInstrument.RoundToTickSize(initialStopPriceForLog);
                }
                return;
            }

            if (isExit)
            {
                stage1ExitReason = InferStage1ExitReason(execution.Order.Name);
                if (stage1TradeOpen &&
                    (marketPosition == MarketPosition.Flat || Position.MarketPosition == MarketPosition.Flat))
                {
                    WriteStage1TradeLog(price, time, execution);
                    ResetStage1TradeLogState();
                }
            }
        }

        private void WriteStage1TradeLog(double exitPrice, DateTime exitTime, Execution execution)
        {
            if (string.IsNullOrEmpty(tradeLogPath))
                ConfigureStage1TradeLog();

            int tradeCount = SystemPerformance.AllTrades.Count;
            double grossPnl = 0.0;
            double commission = 0.0;
            for (int i = stage1StartTradeCount; i < tradeCount; i++)
            {
                var trade = SystemPerformance.AllTrades[i];
                grossPnl += trade.ProfitCurrency;
                commission += trade.Commission;
            }

            if (tradeCount <= stage1StartTradeCount)
            {
                double priceDiff = directionForLog == "LONG"
                    ? exitPrice - entryPriceForLog
                    : entryPriceForLog - exitPrice;
                double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
                grossPnl = (priceDiff / TickSize) * tickValue * Math.Max(1, stage1EntryContracts);
                commission = 0.0;
            }

            double netPnl = grossPnl - commission;
            double tickValueForLog = Instrument.MasterInstrument.PointValue * TickSize;
            double ticks = tickValueForLog > 0.0 ? netPnl / tickValueForLog : 0.0;
            string winLoss = netPnl > 0.0 ? "WIN" : (netPnl < 0.0 ? "LOSS" : "SCRATCH");
            double stopDistance = 0.0;
            if (initialStopPriceForLog > 0.0 && entryPriceForLog > 0.0)
                stopDistance = Math.Abs(entryPriceForLog - initialStopPriceForLog)
                    * Instrument.MasterInstrument.PointValue
                    * Math.Max(1, stage1EntryContracts);

            string accountName = execution.Account != null
                ? execution.Account.Name
                : (Account == null ? "UNKNOWN" : Account.Name);

            string row = string.Join(",",
                entryTimeForLog.ToString("yyyy-MM-dd"),
                entryTimeForLog.ToString("yyyy-MM-dd HH:mm:ss"),
                exitTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Stage1ModelVersion,
                SafeStage1Csv(accountName),
                SafeStage1Csv(Name),
                SafeStage1Csv(ResolveStage1BotName()),
                SafeStage1Csv(ResolveStage1AbMode()),
                SafeStage1Csv(GetStage1Symbol()),
                SafeStage1Csv(Instrument.FullName),
                directionForLog,
                stage1EntryContracts.ToString(CultureInfo.InvariantCulture),
                FormatStage1(entryPriceForLog, "F2"),
                FormatStage1(exitPrice, "F2"),
                FormatStage1(grossPnl, "F2"),
                FormatStage1(netPnl, "F2"),
                FormatStage1(ticks, "F1"),
                winLoss,
                SafeStage1Csv(stage1ExitReason),
                FormatStage1(initialStopPriceForLog, "F2"),
                FormatStage1(stopDistance, "F2"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            );

            try
            {
                EnsureStage1TradeLogHeader();
                File.AppendAllText(tradeLogPath, row + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Print("Stage1 trade log write error: " + ex.Message);
            }
        }

        private void EnsureStage1TradeLogHeader()
        {
            try
            {
                string dir = Path.GetDirectoryName(tradeLogPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                if (!File.Exists(tradeLogPath) || new FileInfo(tradeLogPath).Length == 0)
                    File.WriteAllText(tradeLogPath, Stage1TradeLogHeader + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Print("Stage1 trade log header error: " + ex.Message);
            }
        }

        private void ResetStage1TradeLogState()
        {
            stage1TradeOpen = false;
            stage1StartTradeCount = SystemPerformance.AllTrades.Count;
            stage1EntryContracts = 0;
            entryPriceForLog = 0.0;
            initialStopPriceForLog = 0.0;
            initialStopTicksForLog = 0.0;
            entryTimeForLog = DateTime.MinValue;
            directionForLog = "";
            stage1ExitReason = "UNKNOWN";
        }

        private string GetStage1Symbol()
        {
            string sym = Instrument.MasterInstrument.Name.ToUpperInvariant();
            if (sym.Contains("MNQ")) return "NQ";
            if (sym.Contains("MES")) return "ES";
            if (sym.Contains("NQ")) return "NQ";
            if (sym.Contains("ES")) return "ES";
            return sym;
        }

        private string ResolveStage1AbMode()
        {
            if (PermissionMode == V3DPermissionMode.B_FullTrinity) return "B";
            if (PermissionMode == V3DPermissionMode.C_SoftTrinity) return "C";
            return Stage1DefaultAbMode;
        }

        private string ResolveStage1BotName()
        {
            string suffix = ResolveStage1AbMode();
            if (Stage1BotName.EndsWith("_A", StringComparison.OrdinalIgnoreCase) ||
                Stage1BotName.EndsWith("_B", StringComparison.OrdinalIgnoreCase) ||
                Stage1BotName.EndsWith("_C", StringComparison.OrdinalIgnoreCase))
                return Stage1BotName.Substring(0, Stage1BotName.Length - 2) + "_" + suffix;
            return Stage1BotName + "_" + suffix;
        }

        private string InferStage1ExitReason(string orderName)
        {
            if (string.IsNullOrEmpty(orderName)) return "UNKNOWN";
            string n = orderName.ToUpperInvariant();
            if (n.Contains("PROFIT") || n.Contains("TARGET")) return "TARGET_HIT";
            if (n.Contains("STOP")) return "STOP_HIT";
            if (n.Contains("TRANSITION")) return "REGIME_TRANSITION_EXIT";
            if (n.Contains("CIRCUIT")) return "CIRCUIT_BREAKER";
            if (n.Contains("SLOPE")) return "SLOPE_EXIT";
            if (n.Contains("TRAIL")) return "TRAIL_STOP";
            if (n.Contains("WOBBLE")) return "WOBBLE_EJECT";
            if (n.Contains("ILLIQUID")) return "ILLIQUID_EXIT";
            if (n.Contains("ENVELOPE")) return "ENVELOPE_BREAK_EXIT";
            if (n.Contains("TREND")) return "TREND_BREAK_EXIT";
            if (n.Contains("GUARD")) return "GUARD_FLAT";
            if (n.Contains("KILL")) return "KILL_SWITCH";
            if (n.Contains("TIME")) return "TIME_EXIT";
            if (n.Contains("SESSION") || n.Contains("CLOSE") || n.Contains("EOD")) return "SESSION_CLOSE";
            return orderName;
        }

        private string FormatStage1(double value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        private string SafeStage1Csv(string value)
        {
            string s = value ?? "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\r") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
        private V3DStrategyTradeLogger v3dTradeLogger;


        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "Expansion_V3D_R2";
                Calculate                    = Calculate.OnBarClose;
                EntriesPerDirection          = 2;   // Leg1 + Leg2
                EntryHandling                = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                // StopCancelClose: if an order error occurs in realtime → flatten and stop.
                // Safer than IgnoreAllErrors which hides fill failures silently.
                RealtimeErrorHandling        = RealtimeErrorHandling.StopCancelClose;
                TraceOrders                  = false;
            }
            else if (State == State.DataLoaded)
            {
                leaderSymbol   = GetLeaderSymbol(Instrument.MasterInstrument.Name);
                matrixFile     = Path.Combine(DataFolderPath, leaderSymbol + "_RegimeMatrix_Latest.csv");
                atr            = ATR(AtrPeriod);
                adxInd         = ADX(AdxPeriod);     // R2 entry stack
                dmInd          = DM(AdxPeriod);      // R2 entry stack
                ciSeries       = new Series<double>(this);
                blockedPhasesA.Clear();
                foreach (string p in (BlockedPhasesACsv ?? "").Split(','))
                {
                    string t = p.Trim();
                    if (t.Length > 0) blockedPhasesA.Add(t);
                }
                SetupFileWatcher();
                v3dTradeLogger = new V3DStrategyTradeLogger(this, AccountNameFilter, ConfiguredStrategyName, "V3D", TradeLogFolder, ResolveStage1BotName(), ResolveStage1AbMode());
                ConfigureStage1TradeLog();
                lastTradeCount = SystemPerformance.AllTrades.Count;
            }
            else if (State == State.Terminated)
            {
                TeardownFileWatcher();
            }
        }

        // =====================================================================
        // FILE WATCHER
        // =====================================================================
        private void SetupFileWatcher()
        {
            try
            {
                string dir  = Path.GetDirectoryName(matrixFile);
                string file = Path.GetFileName(matrixFile);
                if (!Directory.Exists(dir)) return;
                regimeWatcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter        = NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                regimeWatcher.Changed += (s, e) => ThreadPool.QueueUserWorkItem(_ => ReadLatestRow());
                ReadLatestRow();
            }
            catch { }
        }

        private void TeardownFileWatcher()
        {
            try
            {
                if (regimeWatcher != null)
                {
                    regimeWatcher.EnableRaisingEvents = false;
                    regimeWatcher.Dispose();
                    regimeWatcher = null;
                }
            }
            catch { }
        }

        // =====================================================================
        // REGIME FILE READER  (timestamp-guarded + FileSystemWatcher dual path)
        // =====================================================================
        private void RefreshRegimeState()
        {
            if ((DateTime.Now - lastFileCheck).TotalSeconds < MinCheckSeconds) return;
            try
            {
                DateTime wt = File.GetLastWriteTimeUtc(matrixFile);
                if (wt <= lastFileWriteUtc) { lastFileCheck = DateTime.Now; return; }
                ReadLatestRow();
                lastFileWriteUtc = wt;
                lastFileCheck    = DateTime.Now;
            }
            catch { }
        }

        private void ReadLatestRow()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    string[] lines;
                    lock (fileLock)
                    {
                        using (var fs = new FileStream(matrixFile, FileMode.Open,
                                                       FileAccess.Read, FileShare.ReadWrite))
                        using (var sr = new StreamReader(fs))
                            lines = sr.ReadToEnd()
                                      .Split(new[] { '\r', '\n' },
                                             StringSplitOptions.RemoveEmptyEntries);
                    }
                    if (lines.Length < 2) { parseFailed = true; return; }

                    // Build header map on first successful read
                    if (headerIdx.Count == 0)
                    {
                        string[] hdr = lines[0].Split(',');
                        headerIdx.Clear();
                        for (int i = 0; i < hdr.Length; i++)
                            headerIdx[hdr[i].Trim()] = i;
                    }

                    string[] row = lines[lines.Length - 1].Split(',');

                    finalRegime      = Get(row, "FinalRegime");
                    finalDirection   = Get(row, "FinalDirection");
                    phase            = Get(row, "Phase");
                    reasonCode       = Get(row, "ReasonCode");
                    regimeConfidence = (int)GetD(row, "RegimeConfidence");
                    allowLong        = GetI(row, "AllowLong")  == 1;
                    allowShort       = GetI(row, "AllowShort") == 1;
                    expansionSizePct = GetI(row, "AllowExpansion_SizePct");
                    staleDataFlag    = GetI(row, "StaleDataFlag") == 1;
                    velocityConfirmed= GetI(row, "VelocityConfirmed");
                    stateAgeBars     = GetI(row, "StateAgeBars");
                    // Wall-clock staleness check (added 2026-05-22): force-stale if row TimestampET older than MaxRowAgeSec.
                    if (MaxRowAgeSec > 0)
                    {
                        DateTime rowTime;
                        if (DateTime.TryParseExact(Get(row, "TimestampET"),
                                "yyyy-MM-dd HH:mm:ss",
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out rowTime))
                        {
                            if ((DateTime.Now - rowTime).TotalSeconds > MaxRowAgeSec)
                                staleDataFlag = true;
                        }
                    }
                    parseFailed      = false;
                    return;
                }
                catch { Thread.Sleep(20); }
            }
            parseFailed = true;
        }

        private string Get(string[] row, string col)
        {
            int i;
            return headerIdx.TryGetValue(col, out i) && i < row.Length ? row[i].Trim() : "";
        }
        private double GetD(string[] row, string col)
        {
            double v;
            return double.TryParse(Get(row, col), out v) ? v : 0.0;
        }
        private int GetI(string[] row, string col)
        {
            int v;
            return int.TryParse(Get(row, col), out v) ? v : 0;
        }

        // =====================================================================
        // CONSECUTIVE LOSER TRACKING
        // =====================================================================
        protected override void OnExecutionUpdate(
            Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (v3dTradeLogger != null && !v3dTradeLogger.IsConfiguredAccount(execution))
                return;
            v3dTradeLogger?.OnExecution(execution, price, quantity, marketPosition, time);
            HandleStage1TradeLogExecution(execution, price, quantity, marketPosition, time);
            int tc = SystemPerformance.AllTrades.Count;
            if (tc > lastTradeCount)
            {
                var last = SystemPerformance.AllTrades[tc - 1];
                if (last.ProfitCurrency < 0) consecutiveLosers++;
                else                         consecutiveLosers = 0;
                lastTradeCount = tc;
            }
        }

        // =====================================================================
        // MAIN BAR UPDATE
        // =====================================================================
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 20) return;

            RefreshRegimeState();

            // ── Session reset ──────────────────────────────────────────────
            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers  = 0;
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
                sessionCumPV  = 0.0;   // R2: reset session VWAP accumulator
                sessionCumVol = 0.0;
            }

            // ── R2: session VWAP accumulation (used only if UseVwapAnchor) ──
            {
                double typ = (High[0] + Low[0] + Close[0]) / 3.0;
                double vol = Math.Max(1.0, Volume[0]);
                sessionCumPV  += typ * vol;
                sessionCumVol += vol;
            }

            // ── Regime change: reset loser count + brick counter ───────────
            if (finalRegime != lastRegimeSeen)
            {
                consecutiveLosers = 0;
                if (finalRegime != "TREND_EXPANSION") bricksInExpansion = 0;
                lastRegimeSeen = finalRegime;
            }

            // ── Hysteresis brick counter ───────────────────────────────────
            if (IsBaselineMode() || finalRegime == "TREND_EXPANSION")
                bricksInExpansion++;
            else
                bricksInExpansion = 0;

            // ── TRANSITION: immediate flat ─────────────────────────────────
            if (AllowRegimeManagedExit() && Position.MarketPosition != MarketPosition.Flat && finalRegime == "TRANSITION")
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong("TransitionExit", "");
                else
                    ExitShort("TransitionExit", "");
                ClearLegState();
                return;
            }

            // ==================================================================
            // ENTRY
            // ==================================================================
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ClearLegState();

                // Gate 1: data integrity
                if (!IsBaselineMode() && (parseFailed || staleDataFlag)) return;
                // Gate 2: regime
                if (EnforceFullTrinityGates() && finalRegime != "TREND_EXPANSION") return;
                if (IsSoftMode() && finalRegime != "TREND_EXPANSION" && finalRegime != "TREND_COMPRESSION" && finalRegime != "TREND_EMERGING") return;
                // Gate 3: phase
                if (!IsBaselineMode() && BlockedPhases.Contains(phase)) return;
                // Gate 4: confidence
                if (EnforceFullTrinityGates() && regimeConfidence < MinConfidence) return;
                if (IsSoftMode() && regimeConfidence < Math.Min(MinConfidence, 35)) return;
                // Gate 5: velocity
                if (EnforceFullTrinityGates() && velocityConfirmed != 1) return;
                // Gate 6: state age — no one-bar pokes
                if (EnforceFullTrinityGates() && stateAgeBars < 2) return;
                // Gate 7: circuit breaker
                if (consecutiveLosers >= MaxConsecutiveLosses) return;
                // Gate 8: SizePct — Mode A/C can supply a controlled floor
                int effectiveSizePct = EffectiveSizePct(expansionSizePct);
                if (effectiveSizePct <= 0) return;

                // Gate 9: daily P&L guard (optional — 0 disables each leg)
                if (DailyGoal > 0 || DailyLossLimit > 0)
                {
                    double dailyPnL = SystemPerformance.AllTrades
                                          .TradesPerformance.Currency.CumProfit
                                      - sessionStartProfit;
                    if (DailyGoal     > 0 && dailyPnL >=  DailyGoal)     return;
                    if (DailyLossLimit > 0 && dailyPnL <= -DailyLossLimit) return;
                }

                // ── R2 A-MODE FAILSAFE GATES (forensic-driven; B/C use supervisor gates above) ──
                if (IsBaselineMode())
                {
                    if (blockedPhasesA.Contains(phase)) return;
                    if (BlockNQAmTimeWindow && leaderSymbol == "NQ"
                        && ToTime(Time[0]) >= AmBlockStartHHmmss
                        && ToTime(Time[0]) <  AmBlockEndHHmmss) return;
                }
                // 15:45 ET hard cutoff (all modes)
                if (EnforceCutoff1545 && ToTime(Time[0]) >= 154500) return;

                // ── R2 ENTRY SIGNAL: MomentumSlope OG V2 stack on UniRenko bricks ──
                double adxVal = adxInd[0];
                ciSeries[0]   = ComputeCI(CiPeriod);

                bool crossUp   = CrossAbove(dmInd.DiPlus, dmInd.DiMinus, 1);
                bool crossDown = CrossBelow(dmInd.DiPlus, dmInd.DiMinus, 1);

                // ADX strength filter (primary expansion gate — heavy UniRenko already filters chop)
                if (UseAdxThreshold && adxVal < AdxEntryThreshold) return;

                // CI ceiling (OPTIONAL — OFF by default per operator 2026-05-22)
                if (UseCiEntryThreshold && ciSeries[0] > CiEntryThreshold) return;

                // Slope confirmation (OPTIONAL — CI falling AND ADX rising = expansion phase transition)
                if (UseSlopeGate && CurrentBar > Math.Max(CiSlopeBars, AdxSlopeBars))
                {
                    double ciDrop  = ciSeries[CiSlopeBars] - ciSeries[0];   // positive if CI fell
                    double adxRise = adxVal - adxInd[AdxSlopeBars];          // positive if ADX rose
                    if (!(ciDrop >= CiDecreaseMin && adxRise >= AdxIncreaseMin)) return;
                }

                // VWAP anchor (OPTIONAL — OFF by default; OG testing favoured off)
                bool anchorLongOk = true, anchorShortOk = true;
                if (UseVwapAnchor)
                {
                    double vwap = (sessionCumVol > 0) ? sessionCumPV / sessionCumVol : Close[0];
                    anchorLongOk  = Close[0] >= vwap;
                    anchorShortOk = Close[0] <= vwap;
                }

                bool greenBrick = crossUp   && anchorLongOk;     // R2: DI cross replaces brick color
                bool redBrick   = crossDown && anchorShortOk;
                // NQ short suppression (SF-13/14) — A-mode default; B/C still honour supervisor allowShort
                if (IsBaselineMode() && leaderSymbol == "NQ" && !AllowShortNQ) redBrick = false;

                int maxC     = CalcMaxContracts();
                int sz       = ScaleByConfidence(maxC, effectiveSizePct);
                int leg1Qty  = Math.Max(1, sz / 2);
                int leg2Qty  = Math.Max(1, sz - leg1Qty);

                double riskTicks = (atr[0] * InitialRiskAtr) / TickSize;
                // Bracket distance in whole ticks. Ticks mode anchors the stop and
                // target to the actual entry fill price, so a session-open gap can
                // never leave the stop on the wrong side of the market — which gets
                // the whole OCO bracket rejected and kills the strategy.
                int riskTicksI = Math.Max(1, (int)Math.Round(riskTicks));

                if (greenBrick && DirectionLongAllowed())
                {
                    double stp  = RT(Close[0] - riskTicksI * TickSize);
                    double tgt1 = RT(Close[0] + riskTicksI * TickSize);

                    SetStopLoss(Leg1L, CalculationMode.Ticks, riskTicksI, false);
                    SetStopLoss(Leg2L, CalculationMode.Ticks, riskTicksI, false);
                    CaptureInitialStopTicksForLog(riskTicksI, "LONG");
                    SetProfitTarget(Leg1L, CalculationMode.Ticks, riskTicksI);
                    // Leg2: no fixed target — trail manages it

                    EnterLong(leg1Qty, Leg1L);
                    EnterLong(leg2Qty, Leg2L);
                    leg2TrailingStop = stp;
                    currentLeg2Qty   = leg2Qty;

                    Print(string.Format(
                        "[Expansion_V3D-A] LONG entry | Regime:{0} | Conf:{1} | Phase:{2} | " +
                        "Reason:{3} | SizePct:{4} | Qty:{5}+{6} | Stop:{7:F2} | T1:{8:F2}",
                        finalRegime, regimeConfidence, phase, reasonCode,
                        effectiveSizePct, leg1Qty, leg2Qty, stp, tgt1));
                }
                else if (redBrick && DirectionShortAllowed())
                {
                    double stp  = RT(Close[0] + riskTicksI * TickSize);
                    double tgt1 = RT(Close[0] - riskTicksI * TickSize);

                    SetStopLoss(Leg1S, CalculationMode.Ticks, riskTicksI, false);
                    SetStopLoss(Leg2S, CalculationMode.Ticks, riskTicksI, false);
                    CaptureInitialStopTicksForLog(riskTicksI, "SHORT");
                    SetProfitTarget(Leg1S, CalculationMode.Ticks, riskTicksI);

                    EnterShort(leg1Qty, Leg1S);
                    EnterShort(leg2Qty, Leg2S);
                    leg2TrailingStop = stp;
                    currentLeg2Qty   = leg2Qty;

                    Print(string.Format(
                        "[Expansion_V3D-A] SHORT entry | Regime:{0} | Conf:{1} | Phase:{2} | " +
                        "Reason:{3} | SizePct:{4} | Qty:{5}+{6} | Stop:{7:F2} | T1:{8:F2}",
                        finalRegime, regimeConfidence, phase, reasonCode,
                        effectiveSizePct, leg1Qty, leg2Qty, stp, tgt1));
                }
            }

            // ==================================================================
            // RUNNER MANAGEMENT  (Leg2)
            // ==================================================================
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                // ── Leg1 fill detection ─────────────────────────────────────
                // Uses currentLeg2Qty so the threshold is correct for any sz.
                // Draft bug: hard-coded <= 1 failed when sz >= 4.
                if (!leg1Hit && Position.Quantity <= currentLeg2Qty)
                {
                    leg1Hit       = true;
                    barsAfterLeg1 = 0;

                    // Free-trade pivot: move stop to BE + small buffer
                    double pivot = Position.MarketPosition == MarketPosition.Long
                        ? RT(Position.AveragePrice + 4 * TickSize)
                        : RT(Position.AveragePrice - 4 * TickSize);

                    leg2TrailingStop = pivot;
                    if (Position.MarketPosition == MarketPosition.Long)
                        SetStopLoss(Leg2L, CalculationMode.Price, pivot, false);
                    else
                        SetStopLoss(Leg2S, CalculationMode.Price, pivot, false);
                }

                if (leg1Hit) barsAfterLeg1++;

                bool greenBrick = Close[0] > Open[0];
                bool redBrick   = Close[0] < Open[0];;

                // ── Wobble eject ────────────────────────────────────────────
                if (Position.MarketPosition == MarketPosition.Long  && redBrick)
                    oppositeBrickCount++;
                else if (Position.MarketPosition == MarketPosition.Short && greenBrick)
                    oppositeBrickCount++;
                else
                    oppositeBrickCount = 0;

                if (oppositeBrickCount >= WobbleBricks)
                {
                    ExitOpenExpansionLegs("WobbleEject");
                    ClearLegState();
                    return;
                }

                // ── Regime degradation: tighten trail immediately ──────────
                // If regime has dropped out of TREND_EXPANSION while runner is
                // live, use the tighter trail multiplier and skip Bar-N phase.
                bool regimeDegraded = leg1Hit && DegradedRegimes.Contains(finalRegime);

                if (leg1Hit)
                {
                    // Choose trail distance based on regime health
                    double trailMult = regimeDegraded ? TickTrailAtrDegraded : TickTrailAtr;
                    double trailDist = atr[0] * trailMult;

                    // Bar-N phase only when regime is healthy
                    bool useBarN = !regimeDegraded && (barsAfterLeg1 <= HybridBarN);

                    if (useBarN)
                    {
                        // FIX: loop bounded by min(barsAfterLeg1, HybridBarN) — not
                        // barsAfterLeg1 alone, which caused progressive lookback growth.
                        int lookback = Math.Min(barsAfterLeg1, Math.Min(HybridBarN, CurrentBar));

                        if (Position.MarketPosition == MarketPosition.Long)
                        {
                            double nLow = Low[0];
                            for (int i = 1; i < lookback; i++)
                                nLow = Math.Min(nLow, Low[i]);
                            double candidate = RT(nLow - TickSize);
                            if (candidate > leg2TrailingStop)
                            {
                                leg2TrailingStop = candidate;
                                SetStopLoss(Leg2L, CalculationMode.Price, leg2TrailingStop, false);
                            }
                        }
                        else
                        {
                            double nHigh = High[0];
                            for (int i = 1; i < lookback; i++)
                                nHigh = Math.Max(nHigh, High[i]);
                            double candidate = RT(nHigh + TickSize);
                            if (candidate < leg2TrailingStop || leg2TrailingStop == 0)
                            {
                                leg2TrailingStop = candidate;
                                SetStopLoss(Leg2S, CalculationMode.Price, leg2TrailingStop, false);
                            }
                        }
                    }
                    else
                    {
                        // Tick-trail phase (or degraded regime forcing immediate tick trail)
                        if (Position.MarketPosition == MarketPosition.Long)
                        {
                            double candidate = RT(High[0] - trailDist);
                            if (candidate > leg2TrailingStop)
                            {
                                leg2TrailingStop = candidate;
                                SetStopLoss(Leg2L, CalculationMode.Price, leg2TrailingStop, false);
                            }
                        }
                        else
                        {
                            double candidate = RT(Low[0] + trailDist);
                            if (candidate < leg2TrailingStop || leg2TrailingStop == 0)
                            {
                                leg2TrailingStop = candidate;
                                SetStopLoss(Leg2S, CalculationMode.Price, leg2TrailingStop, false);
                            }
                        }
                    }
                }
            }
        }

        // =====================================================================
        // ORDER LABELS
        // =====================================================================
        private const string Leg1L = "ExpL1";
        private const string Leg2L = "ExpL2";
        private const string Leg1S = "ExpS1";
        private const string Leg2S = "ExpS2";

        private void ExitOpenExpansionLegs(string exitSignal)
        {
            int qty = Position.Quantity;
            if (qty <= 0) return;

            bool runnerOnly = leg1Hit || currentLeg2Qty <= 0 || qty <= currentLeg2Qty;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (runnerOnly)
                {
                    ExitLong(qty, exitSignal, Leg2L);
                }
                else
                {
                    int leg2Qty = Math.Min(currentLeg2Qty, qty);
                    int leg1Qty = qty - leg2Qty;
                    if (leg1Qty > 0) ExitLong(leg1Qty, exitSignal, Leg1L);
                    if (leg2Qty > 0) ExitLong(leg2Qty, exitSignal, Leg2L);
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (runnerOnly)
                {
                    ExitShort(qty, exitSignal, Leg2S);
                }
                else
                {
                    int leg2Qty = Math.Min(currentLeg2Qty, qty);
                    int leg1Qty = qty - leg2Qty;
                    if (leg1Qty > 0) ExitShort(leg1Qty, exitSignal, Leg1S);
                    if (leg2Qty > 0) ExitShort(leg2Qty, exitSignal, Leg2S);
                }
            }
        }
    }
}
