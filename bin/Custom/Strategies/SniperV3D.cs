// CC BY-NC 4.0
// Sniper_V3D.cs  — V3D Institutional Regime Matrix  — Version A
// ─────────────────────────────────────────────────────────────────
// REGIME TARGET : TREND_COMPRESSION only.  No entries in any other state.
// CHART TYPE    : 1-minute or 3-minute candles (configurable).  Calculate.OnBarClose.
// INSTRUMENT    : NQ / ES  (MNQ / MES supported via leader-symbol map).
//
// SIGNAL ARCHITECTURE — EMA dip/rip
//   Fast EMA (9)  = trigger line. Entry fires when price recovers back above (long)
//                   or below (short) this line after a qualifying dip/rip.
//   Slow EMA (21) = trend anchor / dip zone. Price must touch or penetrate this line
//                   during the pullback.
//
//   LONG (dip-buy) — Version A 3-bar coherent sequence:
//     Bar[2] OR Bar[1]: Low touched or penetrated slow EMA.
//     Bar[1]:           Close was at or below fast EMA (still compressed after the dip).
//     Bar[0]:           Close above fast EMA (recovery confirmed).
//     Requires both the dip bar and the compressed-close to form a contiguous sequence —
//     bar[1] must have still been at or below fast EMA, ensuring the dip is not stale.
//
//   SHORT (rip-sell) — symmetric.
//
// ENTRY GATES (all must pass)
//   FinalRegime == TREND_COMPRESSION
//   FinalDirection == LONG for long entries, SHORT for short entries
//   AllowLong / AllowShort from supervisor
//   IB extension between IbExtensionMin and IbExtensionMax (0.35–0.80 default)
//   Phase gate: LATE_DAY_CONVICTION requires price within LateDayEmaProximityAtr of fast EMA
//   RegimeConfidence >= MinConfidence  (default 60)
//   sniperSizePct > 0
//   consecutiveLosers < MaxConsecutiveLosses
//   Within time window
//   Daily P&L within [−DailyLossLimit, +DailyGoal]  (optional, 0 = off)
//
// EXITS
//   Strict binary: fixed ATR stop and fixed ATR target.  No bar-by-bar management.
//   Emergency: FinalRegime == TRANSITION → immediate flat.
//
// SINGLE-LEG STRUCTURE
//   EntriesPerDirection = 1.  The Sniper is a precision instrument — one entry,
//   one fixed target, one fixed stop.  No runner, no free-trade pivot.
//   This is intentional: compression pullbacks have bounded reward by design.
//
// CHANGES FROM FIRST DRAFT
//   FIX  — Dip/rip sequence now requires coherent 3-bar pattern:
//           touchedSlow fires on bar[2] or bar[1] only if bar[1] close is still
//           at or below fast EMA, ensuring the dip is not stale.
//   FIX  — sniperSizePct == 0 → no entry (removed silent 50% fallback).
//   FIX  — RealtimeErrorHandling changed to StopCancelClose.
//   ADD  — RegimeConfidence minimum gate (default 60, configurable).
//   ADD  — Daily P&L guard (DailyGoal / DailyLossLimit, both optional, default 0).
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
    public class Sniper_V3D : Strategy
    {
        // =====================================================================
        // PARAMETERS
        // =====================================================================

        [NinjaScriptProperty]
        [Display(Name = "Data Folder Path", GroupName = "1. Regime", Order = 0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D";

        // --- Risk ---
        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "Target (ATR)", GroupName = "2. Risk", Order = 0)]
        public double TargetAtr { get; set; } = 0.75;

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "Stop (ATR)", GroupName = "2. Risk", Order = 1)]
        public double StopAtr { get; set; } = 1.0;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", GroupName = "2. Risk", Order = 2)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5.00  ES=12.50  MNQ=0.50  MES=1.25",
                 GroupName = "2. Risk", Order = 3)]
        public double TickValueDollars { get; set; } = 5.00;

        // --- Signal ---
        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "Fast EMA Period", GroupName = "3. Signal", Order = 0)]
        public int FastEmaPeriod { get; set; } = 9;

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "Slow EMA Period", GroupName = "3. Signal", Order = 1)]
        public int SlowEmaPeriod { get; set; } = 21;

        [NinjaScriptProperty, Range(0.0, 1.0)]
        [Display(Name = "IB Extension Min",
                 Description = "Min ib_extension_pct for entry (below = no trend context yet)",
                 GroupName = "3. Signal", Order = 2)]
        public double IbExtensionMin { get; set; } = 0.35;

        [NinjaScriptProperty, Range(0.0, 2.0)]
        [Display(Name = "IB Extension Max",
                 Description = "Max ib_extension_pct for entry (above = exhaustion zone, no fade)",
                 GroupName = "3. Signal", Order = 3)]
        public double IbExtensionMax { get; set; } = 0.80;

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "Late Day EMA Proximity (ATR)",
                 Description = "During LATE_DAY_CONVICTION: only enter if price is within this ATR of fast EMA",
                 GroupName = "3. Signal", Order = 4)]
        public double LateDayEmaProximityAtr { get; set; } = 1.0;

        [NinjaScriptProperty, Range(50, 100)]
        [Display(Name = "Min Regime Confidence",
                 Description = "Skip entry if supervisor confidence is below this threshold. " +
                               "Lower than Expansion (75) because compression is a less certain state.",
                 GroupName = "3. Signal", Order = 5)]
        public int MinConfidence { get; set; } = 60;

        // --- Guards ---
        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "4. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0 = off)",
                 Description = "Stops new entries once daily profit reaches this level",
                 GroupName = "4. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0 = off)",
                 Description = "Stops new entries once daily loss reaches this level",
                 GroupName = "4. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

        // --- Time ---
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "5. Time", Order = 0)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Start Time (HHmmss)", GroupName = "5. Time", Order = 1)]
        public int StartTime { get; set; } = 103500;

        [NinjaScriptProperty]
        [Display(Name = "End Time (HHmmss)", GroupName = "5. Time", Order = 2)]
        public int EndTime { get; set; } = 155000;

        // =====================================================================
        // REGIME STATE
        // NOTE: double/float fields cannot be volatile in C# (CS0677).
        // All doubles are read inside lock(fileLock) in ReadLatestRow.
        // =====================================================================
        private string   matrixFile         = "";
        private string   leaderSymbol       = "";
        private DateTime lastFileWriteUtc   = DateTime.MinValue;
        private DateTime lastFileCheck      = DateTime.MinValue;
        private const int MinCheckSeconds   = 15;
        private FileSystemWatcher regimeWatcher;
        private readonly object   fileLock  = new object();

        private volatile string finalRegime      = "UNKNOWN";
        private volatile string finalDirection   = "UNKNOWN";
        private volatile string phase            = "UNKNOWN";
        private volatile string reasonCode       = "";
        private volatile bool   allowLong        = false;
        private volatile bool   allowShort       = false;
        private volatile int    sniperSizePct    = 0;
        private volatile int    regimeConfidence = 0;
        private volatile bool   staleDataFlag    = true;
        private volatile bool   parseFailed      = true;
        private          double ibExtensionPct   = 0.0;

        private Dictionary<string, int> headerIdx = new Dictionary<string, int>();

        // =====================================================================
        // INDICATORS
        // =====================================================================
        private ATR atr;
        private EMA fastEma;
        private EMA slowEma;

        // =====================================================================
        // RUNTIME STATE
        // =====================================================================
        private int    consecutiveLosers  = 0;
        private int    lastTradeCount     = 0;
        private double sessionStartProfit = 0;

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

        private bool IsInTime()
        {
            if (!EnableTimeFilter) return true;
            int t = ToTime(Time[0]);
            return t >= StartTime && t <= EndTime;
        }

        private int CalcMaxContracts()
        {
            double atrVal = atr[0];
            if (atrVal <= 0) return 1;
            double dollarRisk = (atrVal * StopAtr) / TickSize * TickValueDollars;
            if (dollarRisk <= 0) return 1;
            return Math.Max(1, (int)(1500.0 / dollarRisk));
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
        {
            return Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));
        }

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "Sniper_V3D";
                Calculate                    = Calculate.OnBarClose;
                EntriesPerDirection          = 1;   // Strict single-leg
                EntryHandling                = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                RealtimeErrorHandling        = RealtimeErrorHandling.StopCancelClose;
                TraceOrders                  = false;
            }
            else if (State == State.DataLoaded)
            {
                leaderSymbol   = GetLeaderSymbol(Instrument.MasterInstrument.Name);
                matrixFile     = Path.Combine(DataFolderPath, leaderSymbol + "_RegimeMatrix_Latest.csv");
                atr            = ATR(AtrPeriod);
                fastEma        = EMA(FastEmaPeriod);
                slowEma        = EMA(SlowEmaPeriod);
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
        // REGIME FILE READER
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
                    allowLong        = GetI(row, "AllowLong")  == 1;
                    allowShort       = GetI(row, "AllowShort") == 1;
                    sniperSizePct    = GetI(row, "AllowSniper_SizePct");
                    regimeConfidence = (int)GetD(row, "RegimeConfidence");
                    staleDataFlag    = GetI(row, "StaleDataFlag") == 1;

                    lock (fileLock)
                    {
                        ibExtensionPct = GetD(row, "IBExtensionPct");
                    }

                    parseFailed = false;
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
            int warmup = Math.Max(SlowEmaPeriod, AtrPeriod) + 4;
            if (CurrentBar < warmup) return;

            RefreshRegimeState();

            // ── Session reset ──────────────────────────────────────────────
            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers  = 0;
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }

            // ── TRANSITION: immediate flat ─────────────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat && finalRegime == "TRANSITION")
            {
                if (Position.MarketPosition == MarketPosition.Long)  ExitLong ("TransitionExit", SnipeL);
                else                                                  ExitShort("TransitionExit", SnipeS);
                return;
            }

            // ==================================================================
            // ENTRY
            // ==================================================================
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                // Gate 1: data integrity
                if (parseFailed || staleDataFlag)               return;
                // Gate 2: regime
                if (finalRegime != "TREND_COMPRESSION")        return;
                // Gate 3: circuit breaker
                if (consecutiveLosers >= MaxConsecutiveLosses) return;
                // Gate 4: time
                if (!IsInTime())                               return;
                // Gate 5: IB extension bounds
                if (ibExtensionPct < IbExtensionMin || ibExtensionPct > IbExtensionMax) return;
                // Gate 6: SizePct — zero means supervisor has not approved sniper sizing
                if (sniperSizePct <= 0)                        return;
                // Gate 7: confidence floor
                if (regimeConfidence < MinConfidence)          return;

                // Gate 8: daily P&L guard (optional — 0 disables)
                if (DailyGoal > 0 || DailyLossLimit > 0)
                {
                    double dailyPnL = SystemPerformance.AllTrades
                                          .TradesPerformance.Currency.CumProfit
                                      - sessionStartProfit;
                    if (DailyGoal     > 0 && dailyPnL >=  DailyGoal)     return;
                    if (DailyLossLimit > 0 && dailyPnL <= -DailyLossLimit) return;
                }

                double atrVal = atr[0];
                double fast0  = fastEma[0];
                double fast1  = fastEma[1];
                double slow1  = slowEma[1];
                double slow2  = slowEma[2];

                // Gate 9: phase-adaptive proximity
                if (phase == "LATE_DAY_CONVICTION" &&
                    Math.Abs(Close[0] - fast0) > LateDayEmaProximityAtr * atrVal)
                    return;

                double riskTicks   = (atrVal * StopAtr)  / TickSize;
                double rewardTicks = (atrVal * TargetAtr) / TickSize;

                int maxC = CalcMaxContracts();
                int qty  = ScaleByConfidence(maxC, sniperSizePct);

                // ── LONG SNIPE: dip-buy pattern ────────────────────────────
                // VERSION A coherent 3-bar sequence:
                //   The slow EMA touch (bar[1] or bar[2]) must be paired with bar[1]
                //   still closing at or below the fast EMA — ensuring the dip is not stale.
                //   Bar[0] then closes back above the fast EMA (recovery confirmed).
                if (finalDirection == "LONG" && allowLong)
                {
                    // FIX: require bar[1] to still be compressed (Close[1] <= fast1)
                    // so the slow EMA touch on bar[2] is part of the same pullback sequence.
                    bool dipBar1 = Low[1] <= slow1 && Close[1] <= fast1;
                    bool dipBar2 = Low[2] <= slow2 && Close[1] <= fast1;  // bar[2] dip, bar[1] still compressed
                    bool dipValid   = dipBar1 || dipBar2;
                    bool recovered  = Close[0] > fast0;

                    if (dipValid && recovered)
                    {
                        SetStopLoss(SnipeL, CalculationMode.Ticks, riskTicks, false);
                        SetProfitTarget(SnipeL, CalculationMode.Ticks, rewardTicks);
                        EnterLong(qty, SnipeL);

                        string pattern = dipBar1 ? "DIP_BAR1" : "DIP_BAR2";
                        Print(string.Format(
                            "[Sniper_V3D-A] LONG entry | Regime:{0} | Conf:{1} | Phase:{2} | " +
                            "Reason:{3} | IBExt:{4:F2} | Pattern:{5} | SizePct:{6} | Qty:{7}",
                            finalRegime, regimeConfidence, phase, reasonCode,
                            ibExtensionPct, pattern, sniperSizePct, qty));
                    }
                }

                // ── SHORT SNIPE: rip-sell pattern ──────────────────────────
                else if (finalDirection == "SHORT" && allowShort)
                {
                    // Symmetric: slow EMA touch on bar[1] or bar[2], bar[1] still at/above fast, bar[0] drops below
                    bool ripBar1 = High[1] >= slow1 && Close[1] >= fast1;
                    bool ripBar2 = High[2] >= slow2 && Close[1] >= fast1;  // bar[2] rip, bar[1] still elevated
                    bool ripValid   = ripBar1 || ripBar2;
                    bool collapsed  = Close[0] < fast0;

                    if (ripValid && collapsed)
                    {
                        SetStopLoss(SnipeS, CalculationMode.Ticks, riskTicks, false);
                        SetProfitTarget(SnipeS, CalculationMode.Ticks, rewardTicks);
                        EnterShort(qty, SnipeS);

                        string pattern = ripBar1 ? "RIP_BAR1" : "RIP_BAR2";
                        Print(string.Format(
                            "[Sniper_V3D-A] SHORT entry | Regime:{0} | Conf:{1} | Phase:{2} | " +
                            "Reason:{3} | IBExt:{4:F2} | Pattern:{5} | SizePct:{6} | Qty:{7}",
                            finalRegime, regimeConfidence, phase, reasonCode,
                            ibExtensionPct, pattern, sniperSizePct, qty));
                    }
                }
            }
            // Strict binary targets — no bar-by-bar management needed for single-leg structure.
        }

        // =====================================================================
        // ORDER LABELS
        // =====================================================================
        private const string SnipeL = "SnipeL";
        private const string SnipeS = "SnipeS";
    }
}