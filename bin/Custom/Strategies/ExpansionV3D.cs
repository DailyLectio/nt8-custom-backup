// CC BY-NC 4.0
// Expansion_V3D.cs  — V3D Institutional Regime Matrix  — Version A
// ─────────────────────────────────────────────────────────────────
// REGIME TARGET : TREND_EXPANSION only.  No entries in any other state.
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
    public class Expansion_V3D : Strategy
    {
        // =====================================================================
        // PARAMETERS
        // =====================================================================

        [NinjaScriptProperty]
        [Display(Name = "Data Folder Path", GroupName = "1. Regime", Order = 0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D";

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
        public int MaxConsecutiveLosses { get; set; } = 2;

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

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "Expansion_V3D";
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
                SetupFileWatcher();
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
            }

            // ── Regime change: reset loser count + brick counter ───────────
            if (finalRegime != lastRegimeSeen)
            {
                consecutiveLosers = 0;
                if (finalRegime != "TREND_EXPANSION") bricksInExpansion = 0;
                lastRegimeSeen = finalRegime;
            }

            // ── Hysteresis brick counter ───────────────────────────────────
            if (finalRegime == "TREND_EXPANSION")
                bricksInExpansion++;
            else
                bricksInExpansion = 0;

            // ── TRANSITION: immediate flat ─────────────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat && finalRegime == "TRANSITION")
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
                if (parseFailed || staleDataFlag)               return;
                // Gate 2: regime
                if (finalRegime != "TREND_EXPANSION")          return;
                // Gate 3: phase
                if (BlockedPhases.Contains(phase))             return;
                // Gate 4: confidence
                if (regimeConfidence < MinConfidence)           return;
                // Gate 5: velocity
                if (velocityConfirmed != 1)                    return;
                // Gate 6: state age — no one-bar pokes
                if (stateAgeBars < 2)                          return;
                // Gate 7: hysteresis
                if (bricksInExpansion < WaitBricks)            return;
                // Gate 8: circuit breaker
                if (consecutiveLosers >= MaxConsecutiveLosses) return;
                // Gate 9: SizePct — zero means supervisor has not approved sizing
                if (expansionSizePct <= 0)                     return;

                // Gate 10: daily P&L guard (optional — 0 disables each leg)
                if (DailyGoal > 0 || DailyLossLimit > 0)
                {
                    double dailyPnL = SystemPerformance.AllTrades
                                          .TradesPerformance.Currency.CumProfit
                                      - sessionStartProfit;
                    if (DailyGoal     > 0 && dailyPnL >=  DailyGoal)     return;
                    if (DailyLossLimit > 0 && dailyPnL <= -DailyLossLimit) return;
                }

                bool greenBrick = Close[0] > Open[0];
                bool redBrick   = Close[0] < Open[0];

                int maxC     = CalcMaxContracts();
                int sz       = ScaleByConfidence(maxC, expansionSizePct);
                int leg1Qty  = Math.Max(1, sz / 2);
                int leg2Qty  = Math.Max(1, sz - leg1Qty);

                double riskTicks = (atr[0] * InitialRiskAtr) / TickSize;

                if (greenBrick && allowLong)
                {
                    double stp  = RT(Close[0] - riskTicks * TickSize);
                    double tgt1 = RT(Close[0] + riskTicks * TickSize);

                    SetStopLoss(Leg1L, CalculationMode.Price, stp,  false);
                    SetStopLoss(Leg2L, CalculationMode.Price, stp,  false);
                    SetProfitTarget(Leg1L, CalculationMode.Price, tgt1);
                    // Leg2: no fixed target — trail manages it

                    EnterLong(leg1Qty, Leg1L);
                    EnterLong(leg2Qty, Leg2L);
                    leg2TrailingStop = stp;
                    currentLeg2Qty   = leg2Qty;

                    Print(string.Format(
                        "[Expansion_V3D-A] LONG entry | Regime:{0} | Conf:{1} | Phase:{2} | " +
                        "Reason:{3} | SizePct:{4} | Qty:{5}+{6} | Stop:{7:F2} | T1:{8:F2}",
                        finalRegime, regimeConfidence, phase, reasonCode,
                        expansionSizePct, leg1Qty, leg2Qty, stp, tgt1));
                }
                else if (redBrick && allowShort)
                {
                    double stp  = RT(Close[0] + riskTicks * TickSize);
                    double tgt1 = RT(Close[0] - riskTicks * TickSize);

                    SetStopLoss(Leg1S, CalculationMode.Price, stp,  false);
                    SetStopLoss(Leg2S, CalculationMode.Price, stp,  false);
                    SetProfitTarget(Leg1S, CalculationMode.Price, tgt1);

                    EnterShort(leg1Qty, Leg1S);
                    EnterShort(leg2Qty, Leg2S);
                    leg2TrailingStop = stp;
                    currentLeg2Qty   = leg2Qty;

                    Print(string.Format(
                        "[Expansion_V3D-A] SHORT entry | Regime:{0} | Conf:{1} | Phase:{2} | " +
                        "Reason:{3} | SizePct:{4} | Qty:{5}+{6} | Stop:{7:F2} | T1:{8:F2}",
                        finalRegime, regimeConfidence, phase, reasonCode,
                        expansionSizePct, leg1Qty, leg2Qty, stp, tgt1));
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
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong(Position.Quantity, "WobbleEject", Leg2L);
                    else
                        ExitShort(Position.Quantity, "WobbleEject", Leg2S);
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
    }
}