// CC BY-NC 4.0
// Momentum_V3D.cs  — V3D Institutional Regime Matrix  — Version A
// ─────────────────────────────────────────────────────────────────
// REGIME TARGET : TREND_COMPRESSION (primary), TREND_EXPANSION (secondary, reduced CI ceiling).
// CHART TYPE    : 1-minute candles.  Calculate.OnPriceChange.
// INSTRUMENT    : NQ / ES  (MNQ / MES supported via leader-symbol map).
//
// SIGNAL ARCHITECTURE — DI cross + CI/ADX dual-confirmation + slope exit
//   Wilder DI+ / DI- (manual series — matches Pine f_dirMov exactly).
//   Entry signal: DI cross (DI+ crosses above DI- for long, below for short).
//   Entry filter 1: CI (Choppiness Index) must be below adaptive ceiling.
//     TREND_COMPRESSION: CI <= CiMaxCompression  (default 58, allows more chop)
//     TREND_EXPANSION:   CI <= CiMaxExpansion    (default 50, tighter)
//   Entry filter 2: ADX must be above adaptive floor.
//     Floor = max(AdxEntryThreshold, SuggestedAdxMin from HMM).
//     Velocity relief: if |Velocity3P_ATR| >= VelocityReliefThreshold, floor -2.
//   Exit: Slope exit (CI rising + ADX falling) in Simple or Hysteresis mode.
//   Exit: Gu5 circuit breaker — DI counter-cross fires independent of slope exit.
//   Exit: Regime TRANSITION → immediate flat.
//
// SINGLE-LEG STRUCTURE (Version A)
//   One entry label. Fixed ATR stop + fixed RiskReward target.
//   Slope exit may fire before the fixed target.
//
// ENTRY GATES (all must pass)
//   FinalRegime == TREND_COMPRESSION or TREND_EXPANSION
//   ConflictScore < 0.40  (high conflict = model contradiction = no entry)
//   RegimeConfidence >= MinConfidence  (default 65)
//   AllowLong / AllowShort from supervisor
//   CI <= regime-adaptive ceiling
//   ADX >= adaptive floor
//   DI cross in appropriate direction
//   momoSizePct > 0
//   consecutiveLosers < MaxConsecutiveLosses
//   Not triggered same bar as circuit breaker exit
//   Within time window
//   Daily P&L within [−DailyLossLimit, +DailyGoal]  (optional, 0 = off)
//
// CHANGES FROM FIRST DRAFT
//   FIX  — circuitBreakerFiredThisBar flag prevents same-bar reversal entry after exit.
//   FIX  — hystFailCount now resets at session start (prevents cross-session carryover).
//   FIX  — momoSizePct == 0 → no entry (removed silent 50% fallback in both Submit methods).
//   FIX  — RealtimeErrorHandling changed to StopCancelClose.
//   ADD  — ConflictScore entry gate: conflict >= 0.40 blocks entry.
//   ADD  — RegimeConfidence minimum gate (MinConfidence default 65).
//   ADD  — Daily P&L guard (DailyGoal / DailyLossLimit, both optional, default 0).
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
    public class Momentum_V3D : Strategy
    {
        // =====================================================================
        // ENUMS
        // =====================================================================
        public enum ExitSlopeMode { None, Simple, Hysteresis }

        // =====================================================================
        // PARAMETERS
        // =====================================================================

        [NinjaScriptProperty]
        [Display(Name = "Data Folder Path", GroupName = "1. Regime", Order = 0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D";

        [NinjaScriptProperty]
        [Display(Name = "Account Name Filter", Description = "V3D only: exact NT8 account name allow-list for this strategy class. Separate multiple baked accounts with semicolons.", GroupName = "0b. Trade Logging", Order = 0)]
        public string AccountNameFilter { get; set; } = "SimV3D-NQ-2A;SimV3D-ES-2A";

        [NinjaScriptProperty]
        [Display(Name = "Configured Strategy Name", Description = "V3D only: exported strategy identity. Leave as the baked default unless intentionally renaming the tab.", GroupName = "0b. Trade Logging", Order = 1)]
        public string ConfiguredStrategyName { get; set; } = "Momentum_V3D";

        [NinjaScriptProperty]
        [Display(Name = "Trade Log Folder", Description = "V3D only: internal strategy-owned export folder. External V3D trade-log exporter indicators are not required.", GroupName = "0b. Trade Logging", Order = 2)]
        public string TradeLogFolder { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D\TradeLog";
        // --- Risk ---
        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "ATR Stop Multiplier", GroupName = "2. Risk", Order = 0)]
        public double AtrStopMult { get; set; } = 0.75;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", GroupName = "2. Risk", Order = 1)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "Min Stop (ticks)", GroupName = "2. Risk", Order = 2)]
        public int MinStopTicks { get; set; } = 4;

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "Risk:Reward", GroupName = "2. Risk", Order = 3)]
        public double RiskReward { get; set; } = 1.5;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5.00  ES=12.50  MNQ=0.50  MES=1.25",
                 GroupName = "2. Risk", Order = 4)]
        public double TickValueDollars { get; set; } = 5.00;

        // --- Signal / CI-ADX ---
        [NinjaScriptProperty, Range(2, 200)]
        [Display(Name = "CI Period", GroupName = "3. Signal", Order = 0)]
        public int CiPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ADX Period", GroupName = "3. Signal", Order = 1)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "CI Max — Compression",
                 Description = "TREND_COMPRESSION: CI must be ≤ this. Higher = more chop allowed.",
                 GroupName = "3. Signal", Order = 2)]
        public double CiMaxCompression { get; set; } = 58.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "CI Max — Expansion",
                 Description = "TREND_EXPANSION (secondary): CI must be ≤ this. Tighter than Compression.",
                 GroupName = "3. Signal", Order = 3)]
        public double CiMaxExpansion { get; set; } = 50.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "ADX Entry Threshold",
                 Description = "ADX must be at or above this floor. Raised further by SuggestedAdxMin from HMM.",
                 GroupName = "3. Signal", Order = 4)]
        public double AdxEntryThreshold { get; set; } = 18.0;

        [NinjaScriptProperty, Range(0.0, 5.0)]
        [Display(Name = "Velocity ADX Relief Threshold",
                 Description = "If |Velocity3P_ATR| >= this, ADX floor is lowered by 2 points.",
                 GroupName = "3. Signal", Order = 5)]
        public double VelocityReliefThreshold { get; set; } = 0.85;

        [NinjaScriptProperty, Range(50, 100)]
        [Display(Name = "Min Regime Confidence",
                 Description = "Entry blocked if supervisor RegimeConfidence is below this. " +
                               "Default 65: compression is less certain than expansion but must be directional.",
                 GroupName = "3. Signal", Order = 6)]
        public int MinConfidence { get; set; } = 65;

        // --- Exit ---
        [NinjaScriptProperty]
        [Display(Name = "Slope Exit Mode", GroupName = "4. Exit", Order = 0)]
        public ExitSlopeMode SlopeExit { get; set; } = ExitSlopeMode.Hysteresis;

        [NinjaScriptProperty, Range(0, 1000)]
        [Display(Name = "Min Hold Bars", GroupName = "4. Exit", Order = 1)]
        public int MinHoldBars { get; set; } = 2;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "CI Slope Bars (Simple)", GroupName = "4. Exit", Order = 2)]
        public int CiSlopeBarsExit { get; set; } = 5;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "ADX Slope Bars (Simple)", GroupName = "4. Exit", Order = 3)]
        public int AdxSlopeBarsExit { get; set; } = 5;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "Min CI Rise (Simple)", GroupName = "4. Exit", Order = 4)]
        public double CiRiseMinExit { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "Min ADX Drop (Simple)", GroupName = "4. Exit", Order = 5)]
        public double AdxDropMinExit { get; set; } = 2.0;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "Hysteresis Consecutive Fails", GroupName = "4. Exit", Order = 6)]
        public int HystConsecutive { get; set; } = 2;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "CI Rise Per Bar (Hysteresis)", GroupName = "4. Exit", Order = 7)]
        public double HystCiRisePerBar { get; set; } = 0.5;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "ADX Drop Per Bar (Hysteresis)", GroupName = "4. Exit", Order = 8)]
        public double HystAdxDropPerBar { get; set; } = 0.5;

        // --- Guards ---
        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "5. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0 = off)",
                 Description = "Stops new entries once daily profit reaches this level.",
                 GroupName = "5. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0 = off)",
                 Description = "Stops new entries once daily loss reaches this level.",
                 GroupName = "5. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

        // --- Time ---
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "6. Time", Order = 0)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Start Time (HHmmss)", GroupName = "6. Time", Order = 1)]
        public int StartTime { get; set; } = 93500;

        [NinjaScriptProperty]
        [Display(Name = "End Time (HHmmss)", GroupName = "6. Time", Order = 2)]
        public int EndTime { get; set; } = 155500;

        // =====================================================================
        // REGIME STATE
        // NOTE: double/float fields cannot be volatile in C# (CS0677).
        // conflictScore, velocity3P are plain doubles, read inside fileLock.
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
        private volatile string reasonCode       = "";
        private volatile string phase            = "UNKNOWN";
        private volatile int    regimeConfidence = 0;
        private          double conflictScore    = 1.0;
        private volatile bool   allowLong        = false;
        private volatile bool   allowShort       = false;
        private volatile int    momoSizePct      = 0;
        private volatile bool   staleDataFlag    = true;
        private          double velocity3P       = 0.0;
        private volatile int    stateAgeBars     = 0;
        private volatile bool   parseFailed      = true;
        private volatile int    suggestedAdxMin  = 18;

        private Dictionary<string, int> headerIdx = new Dictionary<string, int>();

        // =====================================================================
        // INDICATORS
        // =====================================================================
        private ATR atrStop;
        private ADX adx;

        // CI internals (manual — no built-in CI in NT8)
        private Series<double> trSeries;
        private SUM  sumTr;
        private MAX  maxHigh;
        private MIN  minLow;
        private Series<double> ci;

        // Wilder DI (own series — matches Pine f_dirMov)
        private Series<double> dmPlus, dmMinus;
        private Series<double> sumDmPlus, sumDmMinus, sumTrDI;
        private Series<double> diPlusSeries, diMinusSeries;

        // =====================================================================
        // RUNTIME STATE
        // =====================================================================
        private int    consecutiveLosers         = 0;
        private int    lastTradeCount            = 0;
        private int    hystFailCount             = 0;
        private bool   circuitBreakerFiredThisBar = false;  // prevents same-bar re-entry
        private string lastRegimeSeen            = "";
        private double sessionStartProfit        = 0;

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
            double atrVal = atrStop[0];
            if (atrVal <= 0) return 1;
            double dollarRisk = (atrVal * AtrStopMult) / TickSize * TickValueDollars;
            if (dollarRisk <= 0) return 1;
            return Math.Max(1, (int)(1500.0 / dollarRisk));
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
        {
            return Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));
        }

        private bool IsRegimeAllowed()
        {
            if (parseFailed || staleDataFlag) return false;
            return finalRegime == "TREND_COMPRESSION" || finalRegime == "TREND_EXPANSION";
        }

        private double ActiveCiMax()
        {
            return finalRegime == "TREND_EXPANSION" ? CiMaxExpansion : CiMaxCompression;
        }

        private double ActiveAdxFloor()
        {
            double floor = Math.Max(AdxEntryThreshold,
                           suggestedAdxMin > 0 ? (double)suggestedAdxMin : AdxEntryThreshold);
            if (Math.Abs(velocity3P) >= VelocityReliefThreshold) floor -= 2.0;
            return floor;
        }

        // =====================================================================
        // STAGE 1 RAW TRADE LOG
        // =====================================================================
        private const string Stage1ModelVersion = "V3D";
        private const string Stage1BotName = "V3D_Momentum_A";
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
                Name                         = "Momentum_V3D";
                Calculate                    = Calculate.OnPriceChange;
                EntriesPerDirection          = 1;
                EntryHandling                = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                RealtimeErrorHandling        = RealtimeErrorHandling.StopCancelClose;
                TraceOrders                  = false;
            }
            else if (State == State.DataLoaded)
            {
                leaderSymbol = GetLeaderSymbol(Instrument.MasterInstrument.Name);
                matrixFile   = Path.Combine(DataFolderPath, leaderSymbol + "_RegimeMatrix_Latest.csv");

                adx     = ADX(AdxPeriod);
                atrStop = ATR(AtrPeriod);

                trSeries = new Series<double>(this);
                sumTr    = SUM(trSeries, CiPeriod);
                maxHigh  = MAX(High, CiPeriod);
                minLow   = MIN(Low,  CiPeriod);
                ci       = new Series<double>(this);

                dmPlus        = new Series<double>(this);
                dmMinus       = new Series<double>(this);
                sumDmPlus     = new Series<double>(this);
                sumDmMinus    = new Series<double>(this);
                sumTrDI       = new Series<double>(this);
                diPlusSeries  = new Series<double>(this);
                diMinusSeries = new Series<double>(this);

                SetupFileWatcher();
                v3dTradeLogger = new V3DStrategyTradeLogger(this, AccountNameFilter, ConfiguredStrategyName, "V3D", TradeLogFolder, "V3D_Momentum_A", "A");
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

                    finalRegime      = Get(row, "FinalRegime");
                    finalDirection   = Get(row, "FinalDirection");
                    phase            = Get(row, "Phase");
                    reasonCode       = Get(row, "ReasonCode");
                    regimeConfidence = (int)GetD(row, "RegimeConfidence");
                    allowLong        = GetI(row, "AllowLong")  == 1;
                    allowShort       = GetI(row, "AllowShort") == 1;
                    momoSizePct      = GetI(row, "AllowMomo_SizePct");
                    staleDataFlag    = GetI(row, "StaleDataFlag") == 1;
                    stateAgeBars     = GetI(row, "StateAgeBars");
                    suggestedAdxMin  = GetI(row, "SuggestedAdxMin");

                    lock (fileLock)
                    {
                        conflictScore = GetD(row, "ConflictScore");
                        velocity3P    = GetD(row, "Velocity3P_ATR");
                    }

                    parseFailed = false;
                    return;
                }
                catch { Thread.Sleep(20); }
            }
            parseFailed = true;
        }

        private string Get(string[] row, string col)
        { int i; return headerIdx.TryGetValue(col, out i) && i < row.Length ? row[i].Trim() : ""; }
        private double GetD(string[] row, string col)
        { double v; return double.TryParse(Get(row, col), out v) ? v : 0.0; }
        private int GetI(string[] row, string col)
        { int v; return int.TryParse(Get(row, col), out v) ? v : 0; }

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

        private void CheckSessionReset()
        {
            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers = 0;
                hystFailCount     = 0;   // FIX: prevent cross-session carryover
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }
            if (finalRegime != lastRegimeSeen)
            {
                consecutiveLosers = 0;
                lastRegimeSeen    = finalRegime;
            }
        }

        // =====================================================================
        // MAIN BAR UPDATE
        // =====================================================================
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2) return;
            int warmup = Math.Max(AdxPeriod, Math.Max(CiPeriod, AtrPeriod)) + 4;
            if (CurrentBar < warmup) return;

            // FIX: reset same-bar circuit breaker flag at top of each bar
            circuitBreakerFiredThisBar = false;

            RefreshRegimeState();
            CheckSessionReset();

            // ── CI computation ──────────────────────────────────────────
            double trBar = Math.Max(High[0] - Low[0],
                           Math.Max(Math.Abs(High[0] - Close[1]),
                                    Math.Abs(Low[0]  - Close[1])));
            trSeries[0] = trBar;
            double range    = maxHigh[0] - minLow[0];
            double sumTrVal = Math.Max(1e-9, sumTr[0]);
            double denom    = Math.Log10(Math.Max(2, CiPeriod));
            double numer    = (range <= 1e-9) ? 1.0 : (sumTrVal / Math.Max(1e-9, range));
            ci[0] = Math.Max(0.0, Math.Min(100.0, 100.0 * Math.Log10(numer) / denom));

            // ── Wilder DI computation ───────────────────────────────────
            double h0 = High[0], l0 = Low[0], h1 = High[1], l1 = Low[1], c1 = Close[1];
            double tr2  = Math.Max(h0 - l0, Math.Max(Math.Abs(h0 - c1), Math.Abs(l0 - c1)));
            double up   = h0 - h1;
            double down = l1 - l0;
            double dmp  = (up   > 0 && up   > down) ? up   : 0;
            double dmn  = (down > 0 && down > up)   ? down : 0;

            if (CurrentBar < AdxPeriod)
            {
                sumTrDI[0]    = sumTrDI[1]    + tr2;
                sumDmPlus[0]  = sumDmPlus[1]  + dmp;
                sumDmMinus[0] = sumDmMinus[1] + dmn;
            }
            else
            {
                sumTrDI[0]    = sumTrDI[1]    - (sumTrDI[1]    / AdxPeriod) + tr2;
                sumDmPlus[0]  = sumDmPlus[1]  - (sumDmPlus[1]  / AdxPeriod) + dmp;
                sumDmMinus[0] = sumDmMinus[1] - (sumDmMinus[1] / AdxPeriod) + dmn;
            }
            double sTr        = sumTrDI[0].ApproxCompare(0) == 0 ? 1e-9 : sumTrDI[0];
            diPlusSeries[0]   = 100.0 * (sumDmPlus[0]  / sTr);
            diMinusSeries[0]  = 100.0 * (sumDmMinus[0] / sTr);

            bool crossUp   = CrossAbove(diPlusSeries, diMinusSeries, 1);
            bool crossDown = CrossBelow(diPlusSeries, diMinusSeries, 1);

            // ── Circuit breaker exit (fires before entries) ─────────────
            ApplyCircuitBreakerExit(crossDown, crossUp);

            // ── TRANSITION: immediate flat ─────────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat && finalRegime == "TRANSITION")
            {
                if (Position.MarketPosition == MarketPosition.Long)  ExitLong ("TransitionExit", LEntry);
                else                                                  ExitShort("TransitionExit", SEntry);
                return;
            }

            // ==================================================================
            // ENTRY
            // ==================================================================
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                // FIX: skip entry if circuit breaker just fired this bar
                if (circuitBreakerFiredThisBar)        return;

                if (!IsRegimeAllowed())                return;
                if (parseFailed || staleDataFlag)      return;
                if (consecutiveLosers >= MaxConsecutiveLosses) return;
                if (!IsInTime())                       return;

                // ADD: conflict score gate — high conflict means model is contradicting itself
                if (conflictScore >= 0.40)             return;

                // ADD: confidence floor
                if (regimeConfidence < MinConfidence)  return;

                // ADD: daily P&L guard (optional — 0 disables)
                if (DailyGoal > 0 || DailyLossLimit > 0)
                {
                    double dailyPnL = SystemPerformance.AllTrades
                                          .TradesPerformance.Currency.CumProfit
                                      - sessionStartProfit;
                    if (DailyGoal     > 0 && dailyPnL >=  DailyGoal)     return;
                    if (DailyLossLimit > 0 && dailyPnL <= -DailyLossLimit) return;
                }

                double ciMax    = ActiveCiMax();
                double adxFloor = ActiveAdxFloor();
                bool   ciOk     = ci[0]  <= ciMax;
                bool   adxOk    = adx[0] >= adxFloor;

                if (ciOk && adxOk)
                {
                    if (crossUp   && allowLong)  SubmitLongWithStops();
                    if (crossDown && allowShort) SubmitShortWithStops();
                }
            }

            // ==================================================================
            // SLOPE EXIT
            // ==================================================================
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                string ent = Position.MarketPosition == MarketPosition.Long ? LEntry : SEntry;
                int bse = BarsSinceEntryExecution(0, ent, 0);
                if (bse < MinHoldBars) return;

                bool exitNow = false;

                if (SlopeExit == ExitSlopeMode.Simple &&
                    CurrentBar > Math.Max(CiSlopeBarsExit, AdxSlopeBarsExit))
                {
                    double ciRise  = ci[0]  - ci[CiSlopeBarsExit];
                    double adxDrop = adx[AdxSlopeBarsExit] - adx[0];
                    exitNow = ciRise >= CiRiseMinExit || adxDrop >= AdxDropMinExit;
                }
                else if (SlopeExit == ExitSlopeMode.Hysteresis && CurrentBar > 1)
                {
                    double ciBar  = ci[0]  - ci[1];
                    double adxBar = adx[1] - adx[0];
                    bool   thisFail = ciBar >= HystCiRisePerBar || adxBar >= HystAdxDropPerBar;
                    if (thisFail) hystFailCount++;
                    else          hystFailCount = Math.Max(0, hystFailCount - 1);
                    exitNow = hystFailCount >= HystConsecutive;
                }

                if (exitNow)
                {
                    if (Position.MarketPosition == MarketPosition.Long)  ExitLong ("SlopeX", LEntry);
                    else                                                  ExitShort("SlopeX", SEntry);
                    hystFailCount = 0;
                }
            }
        }

        // =====================================================================
        // CIRCUIT BREAKER
        // =====================================================================
        private void ApplyCircuitBreakerExit(bool crossDown, bool crossUp)
        {
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (crossDown || finalRegime == "TRANSITION")
                {
                    ExitLong("CircuitBreaker", LEntry);
                    hystFailCount             = 0;
                    circuitBreakerFiredThisBar = true;   // FIX: prevent same-bar re-entry
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (crossUp || finalRegime == "TRANSITION")
                {
                    ExitShort("CircuitBreaker", SEntry);
                    hystFailCount             = 0;
                    circuitBreakerFiredThisBar = true;   // FIX: prevent same-bar re-entry
                }
            }
        }

        // =====================================================================
        // ORDER SUBMISSION
        // =====================================================================
        private const string LEntry = "MomoL";
        private const string SEntry = "MomoS";

        private void SubmitLongWithStops()
        {
            if (!allowLong || staleDataFlag || parseFailed) return;
            // FIX: zero SizePct = supervisor has not approved Momo sizing
            if (momoSizePct <= 0) return;

            int maxC = CalcMaxContracts();
            int qty  = ScaleByConfidence(maxC, momoSizePct);
            if (qty < 1) return;

            double risk = Math.Max(AtrStopMult * atrStop[0], MinStopTicks * TickSize);
            double stp  = RT(Close[0] - risk);
            double tgt  = RT(Close[0] + risk * RiskReward);

            SetStopLoss(LEntry, CalculationMode.Price, stp, false);
            CaptureInitialStopForLog(stp, "LONG");
            SetProfitTarget(LEntry, CalculationMode.Price, tgt);
            EnterLong(qty, LEntry);

            Print(string.Format(
                "[Momentum_V3D-A] LONG entry | Regime:{0} | Conf:{1} | Conflict:{2:F2} | " +
                "Phase:{3} | Reason:{4} | CI:{5:F1} | ADX:{6:F1} | Floor:{7:F1} | " +
                "Vel:{8:F3} | SizePct:{9} | Qty:{10} | Stop:{11:F2} | Tgt:{12:F2}",
                finalRegime, regimeConfidence, conflictScore,
                phase, reasonCode,
                ci[0], adx[0], ActiveAdxFloor(),
                velocity3P, momoSizePct, qty, stp, tgt));
        }

        private void SubmitShortWithStops()
        {
            if (!allowShort || staleDataFlag || parseFailed) return;
            if (momoSizePct <= 0) return;

            int maxC = CalcMaxContracts();
            int qty  = ScaleByConfidence(maxC, momoSizePct);
            if (qty < 1) return;

            double risk = Math.Max(AtrStopMult * atrStop[0], MinStopTicks * TickSize);
            double stp  = RT(Close[0] + risk);
            double tgt  = RT(Close[0] - risk * RiskReward);

            SetStopLoss(SEntry, CalculationMode.Price, stp, false);
            CaptureInitialStopForLog(stp, "SHORT");
            SetProfitTarget(SEntry, CalculationMode.Price, tgt);
            EnterShort(qty, SEntry);

            Print(string.Format(
                "[Momentum_V3D-A] SHORT entry | Regime:{0} | Conf:{1} | Conflict:{2:F2} | " +
                "Phase:{3} | Reason:{4} | CI:{5:F1} | ADX:{6:F1} | Floor:{7:F1} | " +
                "Vel:{8:F3} | SizePct:{9} | Qty:{10} | Stop:{11:F2} | Tgt:{12:F2}",
                finalRegime, regimeConfidence, conflictScore,
                phase, reasonCode,
                ci[0], adx[0], ActiveAdxFloor(),
                velocity3P, momoSizePct, qty, stp, tgt));
        }
    }
}