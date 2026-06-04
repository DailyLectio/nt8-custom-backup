// CC BY-NC 4.0
// Expansion_V3D_B.cs  — V3D Institutional Regime Matrix  — Version B
// ─────────────────────────────────────────────────────────────────
// A/B TEST PURPOSE
//   Version A enters on the first valid brick after WaitBricks consecutive
//   expansion bricks (momentum-first).
//
//   Version B adds one additional confirmation gate before entry:
//     • Velocity3P_ATR from the supervisor must exceed MinVwapDistAtr (default 0.5).
//       This measures the 3-checkpoint price change relative to ATR — a proxy for
//       whether the directional move is real and sustained, not a single-bar spike.
//     • An optional VWAP distance gate: if MinVwapDistAtr > 0 and the current
//       close-vs-VWAP (from Velocity3P_ATR field) is less than that threshold,
//       skip the entry.
//
//   EXPECTED TRADEOFF vs Version A:
//     Fewer entries — late-confirmation velocity filter reduces false starts.
//     Later entries on very fast expansion days (some of the move already gone).
//     Higher per-trade confidence in aggregate because marginal entries are filtered.
//
//   TEST DURATION: Run both versions on separate Apex SIM accounts for 4–6 weeks.
//   Compare: entry count, win rate, average winner, average loser, expectancy per
//   trade, and maximum drawdown per session. Do not compare raw P&L — normalize
//   by trade count.
//
// EVERYTHING ELSE IS IDENTICAL TO VERSION A
//   Same bug fixes, same exits, same trail logic, same risk formula, same guards.
//   The only structural difference is the Velocity3P_ATR confirmation gate.
// ─────────────────────────────────────────────────────────────────
// REGIME TARGET : TREND_EXPANSION only.
// CHART TYPE    : UniRenko (primary).  Calculate.OnBarClose.
// INSTRUMENT    : NQ / ES  (MNQ / MES supported via leader-symbol map).
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
    public class Expansion_V3D_B_StopModes : Strategy
    {
        public enum StopMode
        {
            AtrStatic = 0,
            BarNTrailing = 1,
            AtrStep = 2
        }

        // =====================================================================
        // PARAMETERS
        // =====================================================================

        [NinjaScriptProperty]
        [Display(Name = "Data Folder Path", GroupName = "1. Regime", Order = 0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D";

        [NinjaScriptProperty]
        [Display(Name = "Account Name Filter", Description = "V3D only: exact NT8 account name allow-list for this strategy class. Separate multiple baked accounts with semicolons.", GroupName = "0b. Trade Logging", Order = 0)]
        public string AccountNameFilter { get; set; } = "SimV3D-NQ-1B;SimV3D-ES-2B";

        [NinjaScriptProperty]
        [Display(Name = "Configured Strategy Name", Description = "V3D only: exported strategy identity. Leave as the baked default unless intentionally renaming the tab.", GroupName = "0b. Trade Logging", Order = 1)]
        public string ConfiguredStrategyName { get; set; } = "Expansion_V3D_B_StopModes";

        [NinjaScriptProperty]
        [Display(Name = "Trade Log Folder", Description = "V3D only: internal strategy-owned export folder. External V3D trade-log exporter indicators are not required.", GroupName = "0b. Trade Logging", Order = 2)]
        public string TradeLogFolder { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D\TradeLog";
        // --- Risk ---
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", GroupName = "2. Risk", Order = 1)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5.00  ES=12.50  MNQ=0.50  MES=1.25",
                 GroupName = "2. Risk", Order = 2)]
        public double TickValueDollars { get; set; } = 5.00;

        // --- MomentumSlope-style stops / targets ---
        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Stop Mult", GroupName = "2b. Stops/Targets", Order = 0)]
        public double AtrStopMult { get; set; } = 0.75;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Stop Length", GroupName = "2b. Stops/Targets", Order = 1)]
        public int AtrStopLen { get; set; } = 14;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "Min Stop (ticks)", GroupName = "2b. Stops/Targets", Order = 2)]
        public int MinStopTicks { get; set; } = 2;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "Risk:Reward", GroupName = "2b. Stops/Targets", Order = 3)]
        public double RiskReward { get; set; } = 1.5;

        [NinjaScriptProperty]
        [Display(Name = "Stop Mode", GroupName = "2b. Stops/Targets", Order = 4)]
        public StopMode StopModeSelection { get; set; } = StopMode.AtrStatic;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Trailing N Bars", GroupName = "2c. Stops - BarN Trailing", Order = 1)]
        public int TrailingNBars { get; set; } = 1;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "Trailing Offset (ticks)", GroupName = "2c. Stops - BarN Trailing", Order = 2)]
        public int TrailingOffsetTicks { get; set; } = 5;

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name = "Step 1 trigger (ATR)", GroupName = "2d. Stops - ATR Step", Order = 1)]
        public double Step1ATR { get; set; } = 0.50;

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name = "Step 2 trigger (ATR)", GroupName = "2d. Stops - ATR Step", Order = 2)]
        public double Step2ATR { get; set; } = 1.00;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "BE Plus (ticks)", GroupName = "2d. Stops - ATR Step", Order = 3)]
        public int BreakevenPlusTicks { get; set; } = 5;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "Trail ATR Mult", GroupName = "2d. Stops - ATR Step", Order = 4)]
        public double TrailAtrMult { get; set; } = 1.0;

        // --- Signal ---
        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name = "Wait Bricks (Hysteresis)",
                 Description = "Consecutive expansion bricks required before entry",
                 GroupName = "3. Signal", Order = 0)]
        public int WaitBricks { get; set; } = 3;

        [NinjaScriptProperty, Range(50, 100)]
        [Display(Name = "Min Regime Confidence", GroupName = "3. Signal", Order = 1)]
        public int MinConfidence { get; set; } = 75;

        // ── VERSION B EXCLUSIVE GATE ──────────────────────────────────────
        [NinjaScriptProperty, Range(0.0, 3.0)]
        [Display(Name = "Min Velocity3P_ATR (Version B gate)",
                 Description = "Entry skipped if abs(Velocity3P_ATR) < this value. " +
                               "0 = off (identical to Version A). Recommended starting: 0.5",
                 GroupName = "3. Signal", Order = 2)]
        public double MinVelocityAtr { get; set; } = 0.5;
        // ─────────────────────────────────────────────────────────────────

        // --- Trail ---
        private int HybridBarN = 3;

        private double TickTrailAtr = 1.25;

        private double TickTrailAtrDegraded = 0.75;

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name = "Wobble-eject Bricks", GroupName = "4. Trail", Order = 3)]
        public int WobbleBricks { get; set; } = 1;

        // --- Guards ---
        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "5. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0 = off)", GroupName = "5. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0 = off)", GroupName = "5. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

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
        private double          velocity3pAtr     = 0.0;   // VERSION B: additional field

        private Dictionary<string, int> headerIdx = new Dictionary<string, int>();

        private static readonly HashSet<string> BlockedPhases = new HashSet<string>
        {
            "OPENING_AUCTION", "EARLY_TEST", "CASH_CLOSE"
        };

        private static readonly HashSet<string> DegradedRegimes = new HashSet<string>
        {
            "TREND_COMPRESSION", "ROTATION_LIQUID", "ROTATION_ILLIQUID"
        };

        // =====================================================================
        // INDICATORS
        // =====================================================================
        private ATR atr;
        private ATR atrStop;

        // =====================================================================
        // RUNTIME STATE
        // =====================================================================
        private int    consecutiveLosers  = 0;
        private int    lastTradeCount     = 0;
        private int    bricksInExpansion  = 0;
        private string lastRegimeSeen     = "";
        private double sessionStartProfit = 0;

        private double leg2TrailingStop   = 0.0;
        private bool   leg1Hit            = false;
        private int    oppositeBrickCount = 0;
        private int    barsAfterLeg1      = 0;
        private int    currentLeg2Qty     = 1;

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
            double riskDistance = StopRiskDistance();
            if (riskDistance <= 0) return 1;
            double dollarRisk = riskDistance / TickSize * TickValueDollars;
            if (dollarRisk <= 0) return 1;
            return Math.Max(1, (int)(1500.0 / dollarRisk));
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
        {
            return Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));
        }

        private void ClearLegState()
        {
            leg2TrailingStop   = double.NaN;
            leg1Hit            = false;
            oppositeBrickCount = 0;
            barsAfterLeg1      = 0;
            currentLeg2Qty     = 1;
        }

        private double StopAtrValue()
        {
            if (atrStop != null && atrStop[0] > 0)
                return atrStop[0];
            return atr != null && atr[0] > 0 ? atr[0] : TickSize;
        }

        private double StopRiskDistance()
        {
            double atrRisk = Math.Max(0.01, AtrStopMult) * StopAtrValue();
            double minRisk = Math.Max(1, MinStopTicks) * TickSize;
            return Math.Max(atrRisk, minRisk);
        }

        private double BarNStopLong()
        {
            double lo = Low[0];
            for (int i = 1; i < TrailingNBars && i <= CurrentBar; i++)
                lo = Math.Min(lo, Low[i]);
            return RT(lo - TickSize * TrailingOffsetTicks);
        }

        private double BarNStopShort()
        {
            double hi = High[0];
            for (int i = 1; i < TrailingNBars && i <= CurrentBar; i++)
                hi = Math.Max(hi, High[i]);
            return RT(hi + TickSize * TrailingOffsetTicks);
        }

        private int ActiveBarsSinceEntry()
        {
            int leg1 = Position.MarketPosition == MarketPosition.Long
                ? BarsSinceEntryExecution(0, Leg1L, 0)
                : BarsSinceEntryExecution(0, Leg1S, 0);
            int leg2 = Position.MarketPosition == MarketPosition.Long
                ? BarsSinceEntryExecution(0, Leg2L, 0)
                : BarsSinceEntryExecution(0, Leg2S, 0);

            if (leg1 < 0) return leg2;
            if (leg2 < 0) return leg1;
            return Math.Min(leg1, leg2);
        }

        private void SetStopForActiveEntries(double price)
        {
            if (Position.MarketPosition == MarketPosition.Long)
            {
                SetStopLoss(Leg1L, CalculationMode.Price, price, false);
                SetStopLoss(Leg2L, CalculationMode.Price, price, false);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                SetStopLoss(Leg1S, CalculationMode.Price, price, false);
                SetStopLoss(Leg2S, CalculationMode.Price, price, false);
            }
        }

        private void ManageSelectedStop(int barsSinceEntry)
        {
            if (barsSinceEntry == -1)
                return;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                switch (StopModeSelection)
                {
                    case StopMode.BarNTrailing:
                        if (barsSinceEntry >= Math.Max(1, TrailingNBars) && CurrentBar >= TrailingNBars - 1)
                        {
                            double proposed = BarNStopLong();
                            leg2TrailingStop = double.IsNaN(leg2TrailingStop)
                                ? proposed
                                : Math.Max(leg2TrailingStop, proposed);
                            SetStopForActiveEntries(leg2TrailingStop);
                        }
                        break;

                    case StopMode.AtrStep:
                    {
                        double move = Close[0] - Position.AveragePrice;
                        double step1 = Step1ATR * StopAtrValue();
                        double step2 = Step2ATR * StopAtrValue();
                        bool stopChanged = false;

                        if (move >= step2)
                        {
                            double atrTrail = RT(Close[0] - TrailAtrMult * StopAtrValue());
                            leg2TrailingStop = double.IsNaN(leg2TrailingStop)
                                ? atrTrail
                                : Math.Max(leg2TrailingStop, atrTrail);
                            stopChanged = true;
                        }
                        else if (move >= step1)
                        {
                            double be = RT(Position.AveragePrice + BreakevenPlusTicks * TickSize);
                            leg2TrailingStop = double.IsNaN(leg2TrailingStop)
                                ? be
                                : Math.Max(leg2TrailingStop, be);
                            stopChanged = true;
                        }

                        if (stopChanged)
                            SetStopForActiveEntries(leg2TrailingStop);
                        break;
                    }

                    case StopMode.AtrStatic:
                    default:
                        break;
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                switch (StopModeSelection)
                {
                    case StopMode.BarNTrailing:
                        if (barsSinceEntry >= Math.Max(1, TrailingNBars) && CurrentBar >= TrailingNBars - 1)
                        {
                            double proposed = BarNStopShort();
                            leg2TrailingStop = double.IsNaN(leg2TrailingStop)
                                ? proposed
                                : Math.Min(leg2TrailingStop, proposed);
                            SetStopForActiveEntries(leg2TrailingStop);
                        }
                        break;

                    case StopMode.AtrStep:
                    {
                        double move = Position.AveragePrice - Close[0];
                        double step1 = Step1ATR * StopAtrValue();
                        double step2 = Step2ATR * StopAtrValue();
                        bool stopChanged = false;

                        if (move >= step2)
                        {
                            double atrTrail = RT(Close[0] + TrailAtrMult * StopAtrValue());
                            leg2TrailingStop = double.IsNaN(leg2TrailingStop)
                                ? atrTrail
                                : Math.Min(leg2TrailingStop, atrTrail);
                            stopChanged = true;
                        }
                        else if (move >= step1)
                        {
                            double be = RT(Position.AveragePrice - BreakevenPlusTicks * TickSize);
                            leg2TrailingStop = double.IsNaN(leg2TrailingStop)
                                ? be
                                : Math.Min(leg2TrailingStop, be);
                            stopChanged = true;
                        }

                        if (stopChanged)
                            SetStopForActiveEntries(leg2TrailingStop);
                        break;
                    }

                    case StopMode.AtrStatic:
                    default:
                        break;
                }
            }
        }
        private V3DStrategyTradeLogger v3dTradeLogger;


        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "Expansion_V3D_B_StopModes";
                Calculate                    = Calculate.OnBarClose;
                EntriesPerDirection          = 2;
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
                atrStop        = ATR(Math.Max(5, AtrStopLen));
                SetupFileWatcher();
                v3dTradeLogger = new V3DStrategyTradeLogger(this, AccountNameFilter, ConfiguredStrategyName, "V3D", TradeLogFolder, "V3D_Expansion_B_StopModes", "B_StopModes");
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
                    regimeConfidence = (int)GetD(row, "RegimeConfidence");
                    allowLong        = GetI(row, "AllowLong")  == 1;
                    allowShort       = GetI(row, "AllowShort") == 1;
                    expansionSizePct = GetI(row, "AllowExpansion_SizePct");
                    staleDataFlag    = GetI(row, "StaleDataFlag") == 1;
                    velocityConfirmed= GetI(row, "VelocityConfirmed");
                    stateAgeBars     = GetI(row, "StateAgeBars");
                    velocity3pAtr    = GetD(row, "Velocity3P_ATR");   // VERSION B extra field
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
            int barsRequired = Math.Max(20, Math.Max(AtrPeriod, Math.Max(AtrStopLen, TrailingNBars))) + 2;
            if (CurrentBar < barsRequired) return;

            RefreshRegimeState();

            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers  = 0;
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }

            if (finalRegime != lastRegimeSeen)
            {
                consecutiveLosers = 0;
                if (finalRegime != "TREND_EXPANSION") bricksInExpansion = 0;
                lastRegimeSeen = finalRegime;
            }

            if (finalRegime == "TREND_EXPANSION")
                bricksInExpansion++;
            else
                bricksInExpansion = 0;

            // TRANSITION: immediate flat
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

                if (parseFailed || staleDataFlag)               return;
                if (finalRegime != "TREND_EXPANSION")          return;
                if (BlockedPhases.Contains(phase))             return;
                if (regimeConfidence < MinConfidence)           return;
                if (velocityConfirmed != 1)                    return;
                if (stateAgeBars < 2)                          return;
                if (bricksInExpansion < WaitBricks)            return;
                if (consecutiveLosers >= MaxConsecutiveLosses) return;
                if (expansionSizePct <= 0)                     return;
                if (ToTime(Time[0]) >= 154500)                 return;   // ported 2026-05-22 (orphan reconcile, Cat 2: 15:45 ET hard cutoff)

                // ── VERSION B GATE: velocity confirmation ──────────────────
                // abs(Velocity3P_ATR) must exceed the threshold.
                // If MinVelocityAtr == 0 this gate is disabled (identical to A).
                if (MinVelocityAtr > 0 && Math.Abs(velocity3pAtr) < MinVelocityAtr)
                    return;
                // ──────────────────────────────────────────────────────────

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

                int maxC    = CalcMaxContracts();
                int sz      = ScaleByConfidence(maxC, expansionSizePct);
                int leg1Qty = Math.Max(1, sz / 2);
                int leg2Qty = Math.Max(1, sz - leg1Qty);

                double risk = StopRiskDistance();

                if (greenBrick && allowLong)
                {
                    double stp = RT(Close[0] - risk);
                    double tgt = RT(Close[0] + risk * Math.Max(0.1, RiskReward));

                    SetStopLoss(Leg1L, CalculationMode.Price, stp,  false);
                    SetStopLoss(Leg2L, CalculationMode.Price, stp,  false);
                    SetProfitTarget(Leg1L, CalculationMode.Price, tgt);
                    SetProfitTarget(Leg2L, CalculationMode.Price, tgt);

                    EnterLong(leg1Qty, Leg1L);
                    EnterLong(leg2Qty, Leg2L);
                    leg2TrailingStop = stp;
                    currentLeg2Qty   = leg2Qty;

                    Print(string.Format(
                        "[Expansion_V3D-B StopModes] LONG entry | Regime:{0} | Conf:{1} | Phase:{2} | " +
                        "Reason:{3} | SizePct:{4} | Vel3P:{5:F3} | Qty:{6}+{7} | Stop:{8:F2} | Target:{9:F2}",
                        finalRegime, regimeConfidence, phase, reasonCode,
                        expansionSizePct, velocity3pAtr, leg1Qty, leg2Qty, stp, tgt));
                }
                else if (redBrick && allowShort)
                {
                    double stp = RT(Close[0] + risk);
                    double tgt = RT(Close[0] - risk * Math.Max(0.1, RiskReward));

                    SetStopLoss(Leg1S, CalculationMode.Price, stp,  false);
                    SetStopLoss(Leg2S, CalculationMode.Price, stp,  false);
                    SetProfitTarget(Leg1S, CalculationMode.Price, tgt);
                    SetProfitTarget(Leg2S, CalculationMode.Price, tgt);

                    EnterShort(leg1Qty, Leg1S);
                    EnterShort(leg2Qty, Leg2S);
                    leg2TrailingStop = stp;
                    currentLeg2Qty   = leg2Qty;

                    Print(string.Format(
                        "[Expansion_V3D-B StopModes] SHORT entry | Regime:{0} | Conf:{1} | Phase:{2} | " +
                        "Reason:{3} | SizePct:{4} | Vel3P:{5:F3} | Qty:{6}+{7} | Stop:{8:F2} | Target:{9:F2}",
                        finalRegime, regimeConfidence, phase, reasonCode,
                        expansionSizePct, velocity3pAtr, leg1Qty, leg2Qty, stp, tgt));
                }
            }

            // ==================================================================
            // MOMENTUMSLOPE-STYLE STOP MANAGEMENT
            // ==================================================================
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (StopModeSelection == StopMode.AtrStatic ||
                    StopModeSelection == StopMode.BarNTrailing ||
                    StopModeSelection == StopMode.AtrStep)
                {
                    bool greenBrickManaged = Close[0] > Open[0];
                    bool redBrickManaged   = Close[0] < Open[0];

                    if (Position.MarketPosition == MarketPosition.Long && redBrickManaged)
                        oppositeBrickCount++;
                    else if (Position.MarketPosition == MarketPosition.Short && greenBrickManaged)
                        oppositeBrickCount++;
                    else
                        oppositeBrickCount = 0;

                    if (oppositeBrickCount >= WobbleBricks)
                    {
                        if (Position.MarketPosition == MarketPosition.Long)
                        {
                            ExitLong("WobbleEjectL1", Leg1L);
                            ExitLong("WobbleEjectL2", Leg2L);
                        }
                        else
                        {
                            ExitShort("WobbleEjectS1", Leg1S);
                            ExitShort("WobbleEjectS2", Leg2S);
                        }
                        ClearLegState();
                        return;
                    }

                    ManageSelectedStop(ActiveBarsSinceEntry());
                    return;
                }

                if (!leg1Hit && Position.Quantity <= currentLeg2Qty)
                {
                    leg1Hit       = true;
                    barsAfterLeg1 = 0;

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
                bool redBrick   = Close[0] < Open[0];

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

                bool regimeDegraded = leg1Hit && DegradedRegimes.Contains(finalRegime);

                if (leg1Hit)
                {
                    double trailMult = regimeDegraded ? TickTrailAtrDegraded : TickTrailAtr;
                    double trailDist = atr[0] * trailMult;
                    bool   useBarN   = !regimeDegraded && (barsAfterLeg1 <= HybridBarN);

                    if (useBarN)
                    {
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
        private const string Leg1L = "ExpBL1";
        private const string Leg2L = "ExpBL2";
        private const string Leg1S = "ExpBS1";
        private const string Leg2S = "ExpBS2";

        private void ExitOpenExpansionLegs(string exitSignal)
        {
            int qty = Position.Quantity;
            if (qty <= 0 || Position.MarketPosition == MarketPosition.Flat)
                return;

            int leg2Qty = Math.Min(Math.Max(0, currentLeg2Qty), qty);
            int leg1Qty = Math.Max(0, qty - leg2Qty);

            if (leg1Hit || leg1Qty == 0)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong(qty, exitSignal, Leg2L);
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort(qty, exitSignal, Leg2S);
                return;
            }

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (leg1Qty > 0) ExitLong(leg1Qty, exitSignal, Leg1L);
                if (leg2Qty > 0) ExitLong(leg2Qty, exitSignal, Leg2L);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (leg1Qty > 0) ExitShort(leg1Qty, exitSignal, Leg1S);
                if (leg2Qty > 0) ExitShort(leg2Qty, exitSignal, Leg2S);
            }
        }
    }
}
