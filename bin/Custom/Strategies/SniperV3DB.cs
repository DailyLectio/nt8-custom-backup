// CC BY-NC 4.0
// Sniper_V3D_B.cs  — V3D Institutional Regime Matrix  — Version B
// ─────────────────────────────────────────────────────────────────
// A/B TEST PURPOSE
//   Version A uses a coherent 3-bar lookback: the slow EMA touch can occur on
//   bar[2] as long as bar[1] is still compressed (close at or below fast EMA).
//   This captures setups where the dip developed over two bars.
//
//   Version B enforces a strict 2-bar adjacent sequence:
//     Bar[1]: Low touched or penetrated the slow EMA AND close was at/below fast EMA.
//             (Both conditions on the same bar — no bar[2] lookback at all.)
//     Bar[0]: Close recovered above fast EMA.
//
//   The dip and recovery must be on directly adjacent bars.  This is the purest
//   EMA dip/rip pattern: price hit the trend anchor, compressed, and bounced in
//   immediate succession — the momentum is fresh, not one bar stale.
//
//   EXPECTED TRADEOFF vs Version A:
//     Fewer entries — only catches the sharpest, most immediate reversals.
//     Higher pattern precision — no stale dip from 2 bars ago polluting the signal.
//     May miss valid setups on volatile sessions where the dip takes 2 bars to develop.
//
//   TEST DURATION: 4–6 weeks parallel SIM.
//   FOCUS COMPARISON: entry count, win rate, and average R per trade.
//   The diagnostic Print line identifies DIP_BAR1 vs DIP_BAR2 in Version A —
//   compare that breakdown against B's results to understand which bar distance
//   drives better outcomes.
//
// EVERYTHING ELSE IS IDENTICAL TO VERSION A.
//   Same bug fixes, same gates, same risk formula, same guards.
//   Only the dip/rip pattern condition changes.
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
    public class Sniper_V3D_B : Strategy
    {
        // =====================================================================
        // PARAMETERS  (identical to Version A)
        // =====================================================================

        [NinjaScriptProperty]
        [Display(Name = "Data Folder Path", GroupName = "1. Regime", Order = 0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D";

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

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "Fast EMA Period", GroupName = "3. Signal", Order = 0)]
        public int FastEmaPeriod { get; set; } = 9;

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "Slow EMA Period", GroupName = "3. Signal", Order = 1)]
        public int SlowEmaPeriod { get; set; } = 21;

        [NinjaScriptProperty, Range(0.0, 1.0)]
        [Display(Name = "IB Extension Min", GroupName = "3. Signal", Order = 2)]
        public double IbExtensionMin { get; set; } = 0.35;

        [NinjaScriptProperty, Range(0.0, 2.0)]
        [Display(Name = "IB Extension Max", GroupName = "3. Signal", Order = 3)]
        public double IbExtensionMax { get; set; } = 0.80;

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "Late Day EMA Proximity (ATR)", GroupName = "3. Signal", Order = 4)]
        public double LateDayEmaProximityAtr { get; set; } = 1.0;

        [NinjaScriptProperty, Range(50, 100)]
        [Display(Name = "Min Regime Confidence", GroupName = "3. Signal", Order = 5)]
        public int MinConfidence { get; set; } = 60;

        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "4. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0 = off)", GroupName = "4. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0 = off)", GroupName = "4. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

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
                Name                         = "Sniper_V3D_B";
                Calculate                    = Calculate.OnBarClose;
                EntriesPerDirection          = 1;
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

            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers  = 0;
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }

            if (Position.MarketPosition != MarketPosition.Flat && finalRegime == "TRANSITION")
            {
                if (Position.MarketPosition == MarketPosition.Long)  ExitLong ("TransitionExit", SnipeL);
                else                                                  ExitShort("TransitionExit", SnipeS);
                return;
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (parseFailed || staleDataFlag)               return;
                if (finalRegime != "TREND_COMPRESSION")        return;
                if (consecutiveLosers >= MaxConsecutiveLosses) return;
                if (!IsInTime())                               return;
                if (ibExtensionPct < IbExtensionMin || ibExtensionPct > IbExtensionMax) return;
                if (sniperSizePct <= 0)                        return;
                if (regimeConfidence < MinConfidence)          return;

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
                double slow1  = slowEma[1];

                if (phase == "LATE_DAY_CONVICTION" &&
                    Math.Abs(Close[0] - fast0) > LateDayEmaProximityAtr * atrVal)
                    return;

                double riskTicks   = (atrVal * StopAtr)  / TickSize;
                double rewardTicks = (atrVal * TargetAtr) / TickSize;

                int maxC = CalcMaxContracts();
                int qty  = ScaleByConfidence(maxC, sniperSizePct);

                // ── LONG SNIPE: VERSION B strict adjacent 2-bar sequence ───
                // Bar[1]: Low touched slow EMA AND close was at/below fast EMA
                // Bar[0]: Close above fast EMA
                // No bar[2] lookback. Both the dip and the compression must be
                // on the immediately preceding bar for the pattern to be valid.
                if (finalDirection == "LONG" && allowLong)
                {
                    bool dipAdjacent = Low[1] <= slow1 && Close[1] <= fastEma[1];
                    bool recovered   = Close[0] > fast0;

                    if (dipAdjacent && recovered)
                    {
                        SetStopLoss(SnipeL, CalculationMode.Ticks, riskTicks, false);
                        SetProfitTarget(SnipeL, CalculationMode.Ticks, rewardTicks);
                        EnterLong(qty, SnipeL);

                        Print(string.Format(
                            "[Sniper_V3D-B] LONG entry | Regime:{0} | Conf:{1} | Phase:{2} | " +
                            "Reason:{3} | IBExt:{4:F2} | Pattern:ADJACENT_BAR1 | SizePct:{5} | Qty:{6}",
                            finalRegime, regimeConfidence, phase, reasonCode,
                            ibExtensionPct, sniperSizePct, qty));
                    }
                }

                // ── SHORT SNIPE: VERSION B strict adjacent 2-bar sequence ──
                else if (finalDirection == "SHORT" && allowShort)
                {
                    bool ripAdjacent = High[1] >= slow1 && Close[1] >= fastEma[1];
                    bool collapsed   = Close[0] < fast0;

                    if (ripAdjacent && collapsed)
                    {
                        SetStopLoss(SnipeS, CalculationMode.Ticks, riskTicks, false);
                        SetProfitTarget(SnipeS, CalculationMode.Ticks, rewardTicks);
                        EnterShort(qty, SnipeS);

                        Print(string.Format(
                            "[Sniper_V3D-B] SHORT entry | Regime:{0} | Conf:{1} | Phase:{2} | " +
                            "Reason:{3} | IBExt:{4:F2} | Pattern:ADJACENT_BAR1 | SizePct:{5} | Qty:{6}",
                            finalRegime, regimeConfidence, phase, reasonCode,
                            ibExtensionPct, sniperSizePct, qty));
                    }
                }
            }
        }

        // =====================================================================
        // ORDER LABELS
        // =====================================================================
        private const string SnipeL = "SnipeBL";
        private const string SnipeS = "SnipeBS";
    }
}