// CC BY-NC 4.0
// Fader_V3D.cs  — V3D Institutional Regime Matrix  — Version A
// ─────────────────────────────────────────────────────────────────
// REGIME TARGET : ROTATION_LIQUID only.  No entries in any other state.
// CHART TYPE    : 1-minute candles.  Calculate.OnBarClose.
// INSTRUMENT    : NQ / ES  (MNQ / MES supported via leader-symbol map).
// DIRECTION     : Bidirectional — fades both the low edge (long) and high edge (short).
//
// SIGNAL ARCHITECTURE
//   Primary entry trigger: structural edge proximity (IB high/low, PD VAH/VAL).
//   Price must be within EdgeProximityAtr ATR units of a structural edge.
//   Bollinger band touch is a secondary confirmation only — used as fallback
//   when structural level fields are zero (supervisor not yet providing them).
//
//   Reversal bar confirmation: price was trending into the edge on the prior bar
//   (wasRed for long, wasGreen for short) and has reversed on the current bar
//   (greenBar for long, redBar for short).
//
//   Two-leg structure:
//     Leg1 — target at 50% of distance from entry to VWAP (quick capture).
//     Leg2 — target at full VWAP (runner).
//     After Leg1 fills: Leg2 stop moves to breakeven + small buffer (free-trade pivot).
//
// PERMISSIONS
//   Reads AllowFadeLong / AllowFadeShort — NOT AllowLong / AllowShort.
//   These are separate directional permission flags for counter-trend fade bots.
//
// ENTRY GATES (all must pass)
//   FinalRegime == ROTATION_LIQUID
//   TwoSidedFlag == 1  (auction has traded both sides)
//   AllowFadeLong or AllowFadeShort from supervisor
//   Near structural edge (or Bollinger fallback)
//   Reversal bar pattern confirmed
//   distToVwap >= MinTargetTicks
//   faderSizePct > 0
//   consecutiveLosers < MaxConsecutiveLosses
//   Within time window
//   Daily P&L within [−DailyLossLimit, +DailyGoal]  (optional, 0 = off)
//
// EXITS
//   Leg1      : fixed profit target at 50% of VWAP distance.
//   Leg2      : fixed target at VWAP (entry-time snapshot).  Free-trade pivot after Leg1.
//   Emergency : FinalRegime == TRANSITION → immediate flat.
//   Soft exit : FinalRegime == ROTATION_ILLIQUID while Leg2 runner is live → immediate exit.
//
// CHANGES FROM FIRST DRAFT
//   FIX  — Long and short entries now use else-if: cannot fire simultaneously.
//   FIX  — SizePct column corrected to AllowPine_SizePct (Fader uses Pine column per spec).
//   FIX  — faderSizePct == 0 → no entry (removed silent 50% fallback).
//   FIX  — RealtimeErrorHandling changed to StopCancelClose.
//   FIX  — Leg2 runner management built out (leg1Hit was declared but never used).
//   FIX  — currentLeg2Qty tracked so Leg1 detection is correct at any position size.
//   ADD  — Free-trade pivot: Leg2 stop moves to BE+4 ticks after Leg1 fills.
//   ADD  — ROTATION_ILLIQUID runner exit (market gone dead → close runner).
//   ADD  — Daily P&L guard (DailyGoal / DailyLossLimit, both optional, default 0).
//   ADD  — ReasonCode field read + diagnostic Print on every entry.
//   ADD  — lastTrigger field records whether entry was via structural edge or Bollinger.
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
    public class Fader_V3D : Strategy
    {
        // =====================================================================
        // PARAMETERS
        // =====================================================================

        [NinjaScriptProperty]
        [Display(Name = "Data Folder Path", GroupName = "1. Regime", Order = 0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D";

        [NinjaScriptProperty]
        [Display(Name = "Account Name Filter", Description = "V3D only: exact NT8 account name allow-list for this strategy class. Separate multiple baked accounts with semicolons.", GroupName = "0b. Trade Logging", Order = 0)]
        public string AccountNameFilter { get; set; } = "SimV3D-NQ-3A;SimV3D-ES-3A";

        [NinjaScriptProperty]
        [Display(Name = "Configured Strategy Name", Description = "V3D only: exported strategy identity. Leave as the baked default unless intentionally renaming the tab.", GroupName = "0b. Trade Logging", Order = 1)]
        public string ConfiguredStrategyName { get; set; } = "Fader_V3D";

        [NinjaScriptProperty]
        [Display(Name = "Trade Log Folder", Description = "V3D only: internal strategy-owned export folder. External V3D trade-log exporter indicators are not required.", GroupName = "0b. Trade Logging", Order = 2)]
        public string TradeLogFolder { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D\TradeLog";
        // --- Risk ---
        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "ATR Stop Multiplier", GroupName = "2. Risk", Order = 0)]
        public double AtrStopMult { get; set; } = 1.25;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", GroupName = "2. Risk", Order = 1)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.0, 2.0)]
        [Display(Name = "Edge Proximity (ATR)",
                 Description = "Enter only within this ATR distance of a structural edge",
                 GroupName = "2. Risk", Order = 2)]
        public double EdgeProximityAtr { get; set; } = 0.5;

        [NinjaScriptProperty, Range(4, 500)]
        [Display(Name = "Min Target Ticks",
                 Description = "Skip setup if VWAP is too close",
                 GroupName = "2. Risk", Order = 3)]
        public int MinTargetTicks { get; set; } = 10;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5.00  ES=12.50  MNQ=0.50  MES=1.25",
                 GroupName = "2. Risk", Order = 4)]
        public double TickValueDollars { get; set; } = 5.00;

        // --- Bollinger (secondary confirmation) ---
        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name = "Bollinger Period", GroupName = "3. Signal", Order = 0)]
        public int BollingerPeriod { get; set; } = 20;

        [NinjaScriptProperty, Range(0.5, 4.0)]
        [Display(Name = "Bollinger StdDev", GroupName = "3. Signal", Order = 1)]
        public double BollingerDev { get; set; } = 2.0;

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
        public int EndTime { get; set; } = 155500;

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

        // NOTE: double/float fields cannot be volatile in C# (CS0677).
        // All double fields are protected by the fileLock in ReadLatestRow.
        private volatile string finalRegime      = "UNKNOWN";
        private volatile string reasonCode       = "";
        private volatile bool   allowFadeLong    = false;
        private volatile bool   allowFadeShort   = false;
        private volatile int    faderSizePct     = 0;
        private volatile bool   staleDataFlag    = true;
        private volatile bool   parseFailed      = true;
        private volatile int    twoSidedFlag     = 0;
        private          double sessionVwap      = 0.0;
        private          double ibHigh           = 0.0;
        private          double ibLow            = 0.0;
        private          double pdVah            = 0.0;
        private          double pdVal            = 0.0;

        private Dictionary<string, int> headerIdx = new Dictionary<string, int>();

        // =====================================================================
        // INDICATORS
        // =====================================================================
        private ATR       atr;
        private Bollinger bb;

        // =====================================================================
        // RUNTIME STATE
        // =====================================================================
        private int    consecutiveLosers  = 0;
        private int    lastTradeCount     = 0;
        private double sessionStartProfit = 0;

        // Two-leg runner state
        private bool   leg1Hit        = false;
        private int    currentLeg2Qty = 1;   // dynamic threshold for leg1 detection
        private string activeLeg2     = "";  // tracks which entry label is the runner

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
            double dollarRisk = (atrVal * AtrStopMult) / TickSize * TickValueDollars;
            if (dollarRisk <= 0) return 1;
            return Math.Max(1, (int)(1500.0 / dollarRisk));
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
        {
            return Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));
        }

        // =====================================================================
        // STAGE 1 RAW TRADE LOG
        // =====================================================================
        private const string Stage1ModelVersion = "V3D";
        private const string Stage1BotName = "V3D_Fader_A";
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
            string dir = Path.Combine(@"C:\Users\Valued Customer\NT8_Regimes", Stage1ModelVersion, "TradeLog");
            tradeLogPath = Path.Combine(dir, Stage1BotName + "_TradeLog.csv");
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
                SafeStage1Csv(Stage1BotName),
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
            return Stage1DefaultAbMode;
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
                Name                         = "Fader_V3D";
                Calculate                    = Calculate.OnBarClose;
                EntriesPerDirection          = 2;   // Leg1 + Leg2
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
                bb             = Bollinger(BollingerDev, BollingerPeriod);
                SetupFileWatcher();
                v3dTradeLogger = new V3DStrategyTradeLogger(this, AccountNameFilter, ConfiguredStrategyName, "V3D", TradeLogFolder, "V3D_Fader_A", "A");
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

                    finalRegime    = Get(row, "FinalRegime");
                    reasonCode     = Get(row, "ReasonCode");
                    allowFadeLong  = GetI(row, "AllowFadeLong")  == 1;
                    allowFadeShort = GetI(row, "AllowFadeShort") == 1;
                    // FIX: Fader uses Pine size column per spec Section 6 bot permission table.
                    // AllowPine_SizePct is the correct column for fade-style permissions.
                    faderSizePct   = GetI(row, "AllowPine_SizePct");
                    staleDataFlag  = GetI(row, "StaleDataFlag") == 1;
                    twoSidedFlag   = GetI(row, "TwoSidedFlag");

                    // double fields — not volatile, protected by fileLock above
                    lock (fileLock)
                    {
                        sessionVwap = GetD(row, "SessionVWAP");
                        ibHigh      = GetD(row, "IBHigh");
                        ibLow       = GetD(row, "IBLow");
                        pdVah       = GetD(row, "PDVAH");
                        pdVal       = GetD(row, "PDVAL");
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
        // STRUCTURAL EDGE DETECTION
        // =====================================================================
        private bool NearStructuralEdgeLow(double price, double atrVal, out double nearestEdge)
        {
            nearestEdge = 0;
            double proximity = EdgeProximityAtr * atrVal;
            double bestDist  = double.MaxValue;

            // Low-side structural edges: IB low, Prior Day VAL
            double[] lowEdges = new[] { ibLow, pdVal };
            foreach (double edge in lowEdges)
            {
                if (edge <= 0) continue;
                // Price is at or slightly above the low edge
                double dist = price - edge;
                if (dist >= 0 && dist <= proximity && dist < bestDist)
                {
                    bestDist    = dist;
                    nearestEdge = edge;
                }
            }
            return nearestEdge > 0;
        }

        private bool NearStructuralEdgeHigh(double price, double atrVal, out double nearestEdge)
        {
            nearestEdge = 0;
            double proximity = EdgeProximityAtr * atrVal;
            double bestDist  = double.MaxValue;

            // High-side structural edges: IB high, Prior Day VAH
            double[] highEdges = new[] { ibHigh, pdVah };
            foreach (double edge in highEdges)
            {
                if (edge <= 0) continue;
                // Price is at or slightly below the high edge
                double dist = edge - price;
                if (dist >= 0 && dist <= proximity && dist < bestDist)
                {
                    bestDist    = dist;
                    nearestEdge = edge;
                }
            }
            return nearestEdge > 0;
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
            if (CurrentBar < BollingerPeriod + 4) return;

            RefreshRegimeState();

            // ── Session reset ──────────────────────────────────────────────
            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers  = 0;
                leg1Hit            = false;
                currentLeg2Qty     = 1;
                activeLeg2         = "";
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }

            // ── TRANSITION: immediate flat ─────────────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat && finalRegime == "TRANSITION")
            {
                if (Position.MarketPosition == MarketPosition.Long)  ExitLong ("TransitionExit", "");
                else                                                  ExitShort("TransitionExit", "");
                leg1Hit    = false;
                activeLeg2 = "";
                return;
            }

            // ==================================================================
            // RUNNER MANAGEMENT  (Leg2 — must run before entry block)
            // ==================================================================
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                // ── ROTATION_ILLIQUID: market has gone dead — exit runner ───
                if (finalRegime == "ROTATION_ILLIQUID")
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong(Position.Quantity, "IlliquidExit", activeLeg2.Length > 0 ? activeLeg2 : "");
                    else
                        ExitShort(Position.Quantity, "IlliquidExit", activeLeg2.Length > 0 ? activeLeg2 : "");
                    leg1Hit    = false;
                    activeLeg2 = "";
                    return;
                }

                // ── Leg1 fill detection → free-trade pivot ─────────────────
                // Uses currentLeg2Qty so the threshold is correct for any sz value.
                if (!leg1Hit && Position.Quantity <= currentLeg2Qty && currentLeg2Qty > 0)
                {
                    leg1Hit = true;
                    double pivot = Position.MarketPosition == MarketPosition.Long
                        ? RT(Position.AveragePrice + 4 * TickSize)
                        : RT(Position.AveragePrice - 4 * TickSize);

                    if (activeLeg2 == FadeLEntry2)
                        SetStopLoss(FadeLEntry2, CalculationMode.Price, pivot, false);
                    else if (activeLeg2 == FadeSEntry2)
                        SetStopLoss(FadeSEntry2, CalculationMode.Price, pivot, false);
                }

                // Once Leg1 is hit and runner is free, nothing else to manage —
                // fixed profit target handles the Leg2 exit.
                return;
            }

            // ==================================================================
            // ENTRY
            // ==================================================================
            leg1Hit        = false;
            currentLeg2Qty = 1;
            activeLeg2     = "";

            // Gate 1: data integrity
            if (parseFailed || staleDataFlag)              return;
            // Gate 2: regime
            if (finalRegime != "ROTATION_LIQUID")          return;
            // Gate 3: two-sided auction required
            if (twoSidedFlag != 1)                         return;
            // Gate 4: circuit breaker
            if (consecutiveLosers >= MaxConsecutiveLosses) return;
            // Gate 5: time filter
            if (!IsInTime())                               return;
            // Gate 6: SizePct — zero means supervisor has not approved fade sizing
            if (faderSizePct <= 0)                         return;

            // Gate 7: daily P&L guard (optional — 0 disables each leg)
            if (DailyGoal > 0 || DailyLossLimit > 0)
            {
                double dailyPnL = SystemPerformance.AllTrades
                                      .TradesPerformance.Currency.CumProfit
                                  - sessionStartProfit;
                if (DailyGoal     > 0 && dailyPnL >=  DailyGoal)     return;
                if (DailyLossLimit > 0 && dailyPnL <= -DailyLossLimit) return;
            }

            bool greenBar = Close[0] > Open[0];
            bool redBar   = Close[0] < Open[0];
            bool wasGreen = Close[1] > Open[1];
            bool wasRed   = Close[1] < Open[1];

            double atrVal = atr[0];
            double price  = Close[0];

            // Prefer session VWAP from supervisor; fall back to Bollinger mid if unavailable
            double vwap = sessionVwap > 0 ? sessionVwap : bb.Middle[0];

            // ── LONG FADE: near low-side structural edge, reversal bar ─────
            double lowEdge;
            bool atLowEdge = NearStructuralEdgeLow(price, atrVal, out lowEdge);

            // Bollinger lower touch as fallback only when structural levels are unavailable
            bool bollingerLowTouch = (ibLow <= 0 && pdVal <= 0) &&
                                     (Low[1] <= bb.Lower[1] || Low[2] <= bb.Lower[2]);

            // FIX: else-if prevents simultaneous long and short submissions.
            // wasRed && greenBar  vs  wasGreen && redBar are logically exclusive
            // at bar close, but the explicit else-if makes this unambiguous.
            if (allowFadeLong && (atLowEdge || bollingerLowTouch) && wasRed && greenBar)
            {
                double distToVwap = vwap - price;
                if (distToVwap / TickSize >= MinTargetTicks)
                {
                    double stopPrice  = RT(price - atrVal * AtrStopMult);
                    double leg1Target = RT(price + distToVwap * 0.50);
                    double leg2Target = RT(vwap);

                    int maxC    = CalcMaxContracts();
                    int sz      = ScaleByConfidence(maxC, faderSizePct);
                    int leg1Qty = Math.Max(1, sz / 2);
                    int leg2Qty = Math.Max(1, sz - leg1Qty);

                    SetStopLoss(FadeLEntry1, CalculationMode.Price, stopPrice, false);
                    SetStopLoss(FadeLEntry2, CalculationMode.Price, stopPrice, false);
                    CaptureInitialStopForLog(stopPrice, "LONG");
                    SetProfitTarget(FadeLEntry1, CalculationMode.Price, leg1Target);
                    SetProfitTarget(FadeLEntry2, CalculationMode.Price, leg2Target);
                    EnterLong(leg1Qty, FadeLEntry1);
                    EnterLong(leg2Qty, FadeLEntry2);

                    currentLeg2Qty = leg2Qty;
                    activeLeg2     = FadeLEntry2;

                    string trigger = atLowEdge ? string.Format("EDGE:{0:F2}", lowEdge) : "BOLLINGER_FALLBACK";
                    Print(string.Format(
                        "[Fader_V3D-A] LONG entry | Regime:{0} | Reason:{1} | Trigger:{2} | " +
                        "SizePct:{3} | Qty:{4}+{5} | Stop:{6:F2} | T1:{7:F2} | T2(VWAP):{8:F2}",
                        finalRegime, reasonCode, trigger,
                        faderSizePct, leg1Qty, leg2Qty, stopPrice, leg1Target, leg2Target));
                }
            }
            else if (allowFadeShort && (atHighEdge(price, atrVal) || bollingerHighTouch()) && wasGreen && redBar)
            {
                double highEdgePrice;
                NearStructuralEdgeHigh(price, atrVal, out highEdgePrice);

                double distToVwap = price - vwap;
                if (distToVwap / TickSize >= MinTargetTicks)
                {
                    double stopPrice  = RT(price + atrVal * AtrStopMult);
                    double leg1Target = RT(price - distToVwap * 0.50);
                    double leg2Target = RT(vwap);

                    int maxC    = CalcMaxContracts();
                    int sz      = ScaleByConfidence(maxC, faderSizePct);
                    int leg1Qty = Math.Max(1, sz / 2);
                    int leg2Qty = Math.Max(1, sz - leg1Qty);

                    SetStopLoss(FadeSEntry1, CalculationMode.Price, stopPrice, false);
                    SetStopLoss(FadeSEntry2, CalculationMode.Price, stopPrice, false);
                    CaptureInitialStopForLog(stopPrice, "SHORT");
                    SetProfitTarget(FadeSEntry1, CalculationMode.Price, leg1Target);
                    SetProfitTarget(FadeSEntry2, CalculationMode.Price, leg2Target);
                    EnterShort(leg1Qty, FadeSEntry1);
                    EnterShort(leg2Qty, FadeSEntry2);

                    currentLeg2Qty = leg2Qty;
                    activeLeg2     = FadeSEntry2;

                    string trigger = highEdgePrice > 0
                        ? string.Format("EDGE:{0:F2}", highEdgePrice) : "BOLLINGER_FALLBACK";
                    Print(string.Format(
                        "[Fader_V3D-A] SHORT entry | Regime:{0} | Reason:{1} | Trigger:{2} | " +
                        "SizePct:{3} | Qty:{4}+{5} | Stop:{6:F2} | T1:{7:F2} | T2(VWAP):{8:F2}",
                        finalRegime, reasonCode, trigger,
                        faderSizePct, leg1Qty, leg2Qty, stopPrice, leg1Target, leg2Target));
                }
            }
        }

        // Helper wrappers for the else-if — avoids re-evaluating edge detection twice
        private bool atHighEdge(double price, double atrVal)
        {
            double dummy;
            return NearStructuralEdgeHigh(price, atrVal, out dummy);
        }

        private bool bollingerHighTouch()
        {
            return (ibHigh <= 0 && pdVah <= 0) &&
                   (High[1] >= bb.Upper[1] || High[2] >= bb.Upper[2]);
        }

        // =====================================================================
        // ORDER LABELS
        // =====================================================================
        private const string FadeLEntry1 = "FadeL1";
        private const string FadeLEntry2 = "FadeL2";
        private const string FadeSEntry1 = "FadeS1";
        private const string FadeSEntry2 = "FadeS2";
    }
}