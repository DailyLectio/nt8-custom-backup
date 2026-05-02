// CC BY-NC 4.0
// ADX_DI_V3D.cs — V3D Institutional Regime Matrix
// Bot 5: ADX_DI — ROTATION_LIQUID (primary), TREND_COMPRESSION edges (secondary).
//
// Signal architecture: Wilder-smoothed ADX with precise DI gap gating. Bracket sniper.
//   Hunts edges of Balance and Mean Reversion regimes.
//   Entry: DI cross + ADX above dynamic floor (reads SuggestedAdxMin from HMM field in Latest.csv).
//
// Gates:
//   ib_width_atr >= rotation_liquid_ib_width_atr threshold (default 2.0).
//   two_sided_trade_flag == 1 required.
//   AllowLong / AllowShort from HUD directional permissions.
//
// Exit: DI counter-cross or ADX drops below floor (StopX logic, no separate indicator needed).
// Sizing: Apex $1,500 ceiling scaled by ADX_DI_SizePct from HUD.
// Chart: 3-minute or 5-minute candles (wider timeframe for bracket edge precision).

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
    public class ADX_DI_V3D : Strategy
    {
        // =========================================================================
        // PARAMETERS
        // =========================================================================

        [NinjaScriptProperty]
        [Display(Name = "Data Folder Path", GroupName = "1. Regime", Order = 0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D";

        // --- Risk ---
        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "ATR Multiplier (stop)", GroupName = "2. Risk", Order = 0)]
        public double AtrMultiplier { get; set; } = 1.0;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", GroupName = "2. Risk", Order = 1)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "Risk:Reward", GroupName = "2. Risk", Order = 2)]
        public double RiskReward { get; set; } = 1.0;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)", Description = "NQ=5.00  ES=12.50  MNQ=0.50  MES=1.25", GroupName = "2. Risk", Order = 3)]
        public double TickValueDollars { get; set; } = 5.00;

        // --- Signal ---
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ADX Period", GroupName = "3. Signal", Order = 0)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "ADX Floor (fallback)", Description = "Used when SuggestedAdxMin not available from HMM", GroupName = "3. Signal", Order = 1)]
        public double AdxFloorFallback { get; set; } = 20.0;

        [NinjaScriptProperty, Range(0.0, 20.0)]
        [Display(Name = "Min DI Gap", Description = "Minimum DI+ minus DI- gap for entry", GroupName = "3. Signal", Order = 2)]
        public double MinDiGap { get; set; } = 5.0;

        [NinjaScriptProperty, Range(0.5, 5.0)]
        [Display(Name = "IB Width ATR Minimum", Description = "ib_width_atr must be >= this for ROTATION_LIQUID entry", GroupName = "3. Signal", Order = 3)]
        public double IbWidthAtrMin { get; set; } = 2.0;

        // --- Trailing Stop ---
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Trail N Bars", GroupName = "4. Trail", Order = 0)]
        public int TrailNBars { get; set; } = 1;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset (ticks)", GroupName = "4. Trail", Order = 1)]
        public int TrailOffsetTicks { get; set; } = 5;

        // --- Guards ---
        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "5. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        // --- Time ---
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "6. Time", Order = 0)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Block Open (HHmmss)", GroupName = "6. Time", Order = 1)]
        public int BlockOpenEnd { get; set; } = 93900;

        [NinjaScriptProperty]
        [Display(Name = "Block Lunch Start (HHmmss)", GroupName = "6. Time", Order = 2)]
        public int BlockLunchStart { get; set; } = 113000;

        [NinjaScriptProperty]
        [Display(Name = "Block Lunch End (HHmmss)", GroupName = "6. Time", Order = 3)]
        public int BlockLunchEnd { get; set; } = 132000;

        [NinjaScriptProperty]
        [Display(Name = "Session End (HHmmss)", GroupName = "6. Time", Order = 4)]
        public int SessionEnd { get; set; } = 155500;

        // =========================================================================
        // REGIME STATE
        // =========================================================================
        private string matrixFile         = "";
        private string leaderSymbol       = "";
        private DateTime lastFileWriteUtc = DateTime.MinValue;
        private DateTime lastFileCheck    = DateTime.MinValue;
        private const int MinCheckSeconds = 15;
        private FileSystemWatcher regimeWatcher;
        private readonly object fileLock = new object();

        private volatile string finalRegime    = "UNKNOWN";
        private volatile bool   allowLong      = false;
        private volatile bool   allowShort     = false;
        private volatile int    adxDiSizePct   = 0;
        private volatile bool   staleDataFlag  = true;
        private volatile bool   parseFailed    = true;
        private volatile int    twoSidedFlag   = 0;
        private          double ibWidthAtr     = 0.0;
        private volatile int    suggestedAdxMin= 0;  // from HMM output via Latest.csv

        private Dictionary<string, int> headerIdx = new Dictionary<string, int>();

        // =========================================================================
        // INDICATORS
        // =========================================================================
        private ATR atr;
        private ADX adx;

        // Wilder DI (own series — more accurate than built-in DM indicator for gating)
        private Series<double> dmPlus, dmMinus;
        private Series<double> sumDmPlus, sumDmMinus, sumTrDI;
        private Series<double> diPlusSeries, diMinusSeries;

        // =========================================================================
        // RUNTIME STATE
        // =========================================================================
        private int    consecutiveLosers    = 0;
        private int    lastTradeCount       = 0;
        private double trailingStopLong     = double.NaN;
        private double trailingStopShort    = double.NaN;

        // =========================================================================
        // HELPERS
        // =========================================================================
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
            if (t <= BlockOpenEnd) return false;
            if (t >= BlockLunchStart && t <= BlockLunchEnd) return false;
            if (t > SessionEnd) return false;
            return true;
        }

        private int CalcMaxContracts()
        {
            double atrVal = atr[0];
            if (atrVal <= 0) return 1;
            double dollarRisk = (atrVal * AtrMultiplier) / TickSize * TickValueDollars;
            if (dollarRisk <= 0) return 1;
            return Math.Max(1, (int)(1500.0 / dollarRisk));
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
        {
            return Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));
        }

        private double ActiveAdxFloor()
        {
            // SuggestedAdxMin from HMM output — dynamic floor.
            return suggestedAdxMin > 0 ? (double)suggestedAdxMin : AdxFloorFallback;
        }

        private double BarNStopLong()
        {
            double lo = Low[0];
            for (int i = 1; i < TrailNBars && i < CurrentBar; i++)
                lo = Math.Min(lo, Low[i]);
            return RT(lo - TickSize * TrailOffsetTicks);
        }

        private double BarNStopShort()
        {
            double hi = High[0];
            for (int i = 1; i < TrailNBars && i < CurrentBar; i++)
                hi = Math.Max(hi, High[i]);
            return RT(hi + TickSize * TrailOffsetTicks);
        }

        // =====================================================================
        // STAGE 1 RAW TRADE LOG
        // =====================================================================
        private const string Stage1ModelVersion = "V3D";
        private const string Stage1BotName = "V3D_ADX_DI_A";
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

        // =========================================================================
        // LIFECYCLE
        // =========================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "ADX_DI_V3D";
                Calculate                    = Calculate.OnPriceChange;
                EntriesPerDirection          = 1;
                EntryHandling                = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                RealtimeErrorHandling        = RealtimeErrorHandling.IgnoreAllErrors;
                TraceOrders                  = false;
            }
            else if (State == State.DataLoaded)
            {
                leaderSymbol = GetLeaderSymbol(Instrument.MasterInstrument.Name);
                matrixFile   = Path.Combine(DataFolderPath, leaderSymbol + "_RegimeMatrix_Latest.csv");
                atr          = ATR(AtrPeriod);
                adx          = ADX(AdxPeriod);

                dmPlus       = new Series<double>(this);
                dmMinus      = new Series<double>(this);
                sumDmPlus    = new Series<double>(this);
                sumDmMinus   = new Series<double>(this);
                sumTrDI      = new Series<double>(this);
                diPlusSeries = new Series<double>(this);
                diMinusSeries= new Series<double>(this);

                SetupFileWatcher();
                ConfigureStage1TradeLog();
                lastTradeCount = SystemPerformance.AllTrades.Count;
            }
            else if (State == State.Terminated)
            {
                TeardownFileWatcher();
            }
        }

        // =========================================================================
        // FILE WATCHER
        // =========================================================================
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

        // =========================================================================
        // REGIME FILE READER
        // =========================================================================
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
                        using (var fs = new FileStream(matrixFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var sr = new StreamReader(fs))
                            lines = sr.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
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
                    allowLong      = GetI(row, "AllowLong")  == 1;
                    allowShort     = GetI(row, "AllowShort") == 1;
                    adxDiSizePct   = GetI(row, "AllowADX_DI_SizePct");
                    staleDataFlag  = GetI(row, "StaleDataFlag") == 1;
                    twoSidedFlag   = GetI(row, "TwoSidedFlag");
                    ibWidthAtr     = GetD(row, "IBWidthATR");          // ib_width_atr projected into Latest
                    suggestedAdxMin= GetI(row, "SuggestedAdxMin");    // from HMM column in Latest.csv
                    parseFailed    = false;
                    return;
                }
                catch { Thread.Sleep(20); }
            }
            parseFailed = true;
        }

        private string Get(string[] row, string col)
        {
            int i; return headerIdx.TryGetValue(col, out i) && i < row.Length ? row[i].Trim() : "";
        }
        private double GetD(string[] row, string col)
        {
            double v; return double.TryParse(Get(row, col), out v) ? v : 0.0;
        }
        private int GetI(string[] row, string col)
        {
            int v; return int.TryParse(Get(row, col), out v) ? v : 0;
        }

        // =========================================================================
        // CONSECUTIVE LOSER TRACKING
        // =========================================================================
        protected override void OnExecutionUpdate(
            Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
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

        // =========================================================================
        // MAIN BAR UPDATE
        // =========================================================================
        protected override void OnBarUpdate()
        {
            if (CurrentBar == 0)
            {
                dmPlus[0] = dmMinus[0] = sumTrDI[0] = sumDmPlus[0] = sumDmMinus[0] =
                diPlusSeries[0] = diMinusSeries[0] = 0;
                return;
            }

            // --- Compute Wilder DI ---
            double h0 = High[0], l0 = Low[0], h1 = High[1], l1 = Low[1], c1 = Close[1];
            double tr = Math.Max(h0 - l0, Math.Max(Math.Abs(h0 - c1), Math.Abs(l0 - c1)));
            double up   = h0 - h1;
            double down = l1 - l0;
            double dmp  = (up   > 0 && up   > down) ? up   : 0;
            double dmn  = (down > 0 && down > up)   ? down : 0;
            dmPlus[0]  = dmp;
            dmMinus[0] = dmn;

            if (CurrentBar < AdxPeriod)
            {
                sumTrDI[0]    = sumTrDI[1]    + tr;
                sumDmPlus[0]  = sumDmPlus[1]  + dmp;
                sumDmMinus[0] = sumDmMinus[1] + dmn;
            }
            else
            {
                sumTrDI[0]    = sumTrDI[1]    - (sumTrDI[1]    / AdxPeriod) + tr;
                sumDmPlus[0]  = sumDmPlus[1]  - (sumDmPlus[1]  / AdxPeriod) + dmp;
                sumDmMinus[0] = sumDmMinus[1] - (sumDmMinus[1] / AdxPeriod) + dmn;
            }
            double sTr    = sumTrDI[0].ApproxCompare(0) == 0 ? 1e-9 : sumTrDI[0];
            diPlusSeries[0]  = 100.0 * (sumDmPlus[0]  / sTr);
            diMinusSeries[0] = 100.0 * (sumDmMinus[0] / sTr);

            if (CurrentBar < Math.Max(AdxPeriod, AtrPeriod) + 2) return;

            RefreshRegimeState();

            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers = 0;
                trailingStopLong  = double.NaN;
                trailingStopShort = double.NaN;
            }

            bool crossUp = diPlusSeries[0] >  diMinusSeries[0] && diPlusSeries[1] <= diMinusSeries[1];
            bool crossDn = diPlusSeries[0] <  diMinusSeries[0] && diPlusSeries[1] >= diMinusSeries[1];

            double diGapLong  = diPlusSeries[0]  - diMinusSeries[0];
            double diGapShort = diMinusSeries[0] - diPlusSeries[0];

            double adxNow   = adx[0];
            double adxFloor = ActiveAdxFloor();

            // --- TRANSITION / regime change exit ---
            if (Position.MarketPosition != MarketPosition.Flat && finalRegime == "TRANSITION")
            {
                if (Position.MarketPosition == MarketPosition.Long)  ExitLong ("TransitionExit", LEntry);
                else                                                  ExitShort("TransitionExit", SEntry);
                return;
            }

            // --- StopX exit: ADX drops below floor or DI counter-cross ---
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (adxNow < adxFloor || crossDn)
                {
                    ExitLong("StopX", LEntry);
                    return;
                }

                // Bar-N trailing
                int bse = BarsSinceEntryExecution(0, LEntry, 0);
                if (bse >= TrailNBars)
                {
                    double candidate = BarNStopLong();
                    trailingStopLong = double.IsNaN(trailingStopLong) ? candidate : Math.Max(trailingStopLong, candidate);
                    SetStopLoss(LEntry, CalculationMode.Price, trailingStopLong, false);
                }
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (adxNow < adxFloor || crossUp)
                {
                    ExitShort("StopX", SEntry);
                    return;
                }

                int bse = BarsSinceEntryExecution(0, SEntry, 0);
                if (bse >= TrailNBars)
                {
                    double candidate = BarNStopShort();
                    trailingStopShort = double.IsNaN(trailingStopShort) ? candidate : Math.Min(trailingStopShort, candidate);
                    SetStopLoss(SEntry, CalculationMode.Price, trailingStopShort, false);
                }
            }

            // --- ENTRY ---
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                trailingStopLong  = double.NaN;
                trailingStopShort = double.NaN;

                if (parseFailed || staleDataFlag)              return;
                if (consecutiveLosers >= MaxConsecutiveLosses) return;
                if (!IsInTime())                               return;

                // Regime gate: ROTATION_LIQUID primary, TREND_COMPRESSION secondary
                bool regimeOk = finalRegime == "ROTATION_LIQUID" || finalRegime == "TREND_COMPRESSION";
                if (!regimeOk) return;

                // ROTATION_LIQUID specific gates
                if (finalRegime == "ROTATION_LIQUID")
                {
                    if (twoSidedFlag != 1)          return;
                    if (ibWidthAtr < IbWidthAtrMin) return;
                }

                // ADX floor check
                if (adxNow < adxFloor) return;

                int maxC = CalcMaxContracts();
                int qty  = ScaleByConfidence(maxC, adxDiSizePct > 0 ? adxDiSizePct : 50);
                if (qty < 1) return;

                if (crossUp && allowLong && diGapLong >= MinDiGap)
                    SubmitLong(qty);
                else if (crossDn && allowShort && diGapShort >= MinDiGap)
                    SubmitShort(qty);
            }
        }

        // =========================================================================
        // ORDER SUBMISSION
        // =========================================================================
        private const string LEntry = "AdxDiL";
        private const string SEntry = "AdxDiS";

        private void SubmitLong(int qty)
        {
            if (!allowLong || staleDataFlag || parseFailed) return;
            double risk = atr[0] * AtrMultiplier;
            double stp  = RT(Close[0] - risk);
            double tgt  = RT(Close[0] + risk * RiskReward);
            SetStopLoss(LEntry, CalculationMode.Price, stp, false);
            CaptureInitialStopForLog(stp, "LONG");
            SetProfitTarget(LEntry, CalculationMode.Price, tgt);
            trailingStopLong = stp;
            EnterLong(qty, LEntry);
        }

        private void SubmitShort(int qty)
        {
            if (!allowShort || staleDataFlag || parseFailed) return;
            double risk = atr[0] * AtrMultiplier;
            double stp  = RT(Close[0] + risk);
            double tgt  = RT(Close[0] - risk * RiskReward);
            SetStopLoss(SEntry, CalculationMode.Price, stp, false);
            CaptureInitialStopForLog(stp, "SHORT");
            SetProfitTarget(SEntry, CalculationMode.Price, tgt);
            trailingStopShort = stp;
            EnterShort(qty, SEntry);
        }
    }
}