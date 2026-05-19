// CC BY-NC 4.0
#region Using
using System;
using System.Globalization;
using System.IO;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    // CLASS NAME UPDATED TO BREAK LINK TO OLD BACKUP FILES
    public class ADXX : Strategy 
    {
        public enum StopMode
        {
            AtrStatic    = 0, // fixed ATR stop + RR target; no trailing
            EmaTrailing  = 1, // waits N bars, then EMA +/- offset trailing  
            BarNTrailing = 2, // waits N bars, then N-bar trailing +/- offset
            AtrStep      = 3  // step -> BE+ticks -> ATR trail
        }

        // =========================================================================
        // TRINITY COMMAND CENTER (The HUD Master Switch)
        // =========================================================================
        [NinjaScriptProperty]
        [Display(Name="1. Enable Trinity Filter", Description="If true, listens to the Regime HUD for permission.", GroupName="Trinity Command Center", Order=0)]
        public bool EnableTrinityFilter { get; set; } = true;

        // --- NEW V3 LANE SELECTOR ---
        public enum AdxxHudLane { DefaultBreakout, BracketSniper }

        [NinjaScriptProperty]
        [Display(Name="1b. Select HUD Lane", Description="Routes the strategy to a specific Regime Lane on the HUD.", GroupName="Trinity Command Center", Order=1)]
        public AdxxHudLane SelectedHudLane { get; set; } = AdxxHudLane.DefaultBreakout;

        // The live permission slip linked to the HUD Parking Garage
        private bool IsBotAllowedByTrinity()
        {
            if (!EnableTrinityFilter) return true;

            string baseSymbol = Instrument.MasterInstrument.Name;

            Indicators.RegimeMatrixHUD hudInstance = null;
            if (Indicators.RegimeMatrixHUD.Instances.TryGetValue(baseSymbol, out hudInstance))
            {
                if (hudInstance != null)
                {
                    if (SelectedHudLane == AdxxHudLane.DefaultBreakout) 
                        return hudInstance.IsAdxAllowed;
                    else if (SelectedHudLane == AdxxHudLane.BracketSniper) 
                        return hudInstance.IsBracketSniperAllowed;
                }
            }

            return false;
        }

        // ===== Core Parameters =====
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Contracts", GroupName = "Parameters", Order = 1)]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Use Stop X (ADX/DI exit)", GroupName = "Parameters", Order = 2)]
        public bool UseStopX { get; set; } = true;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "Risk Reward (for targets)", GroupName = "Parameters", Order = 3)]
        public double RiskReward { get; set; } = 1.0;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ADX Period", GroupName = "Parameters", Order = 4)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name = "Level Range (ADX min)", GroupName = "Parameters", Order = 5)]
        public double LevelRange { get; set; } = 20;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", GroupName = "Parameters", Order = 6)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "ATR Multiplier", GroupName = "Parameters", Order = 7)]
        public double AtrMultiplier { get; set; } = 1.0;

        [NinjaScriptProperty]
        [Display(Name = "Stop Mode", GroupName = "Stops", Order = 8)]
        public StopMode StopModeSelection { get; set; } = StopMode.BarNTrailing;

        // ===== Entry Filters (OPT-IN) =====
        [NinjaScriptProperty]
        [Display(Name = "Enable entry filters", GroupName = "Entry Filters", Order = 1)]
        public bool EnableEntryFilters { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Require ADX rising", GroupName = "Entry Filters", Order = 2)]
        public bool RequireAdxRising { get; set; } = true;

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name = "ADX rising bars", GroupName = "Entry Filters", Order = 3)]
        public int AdxRisingBars { get; set; } = 3;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "Min DI gap", GroupName = "Entry Filters", Order = 4)]
        public double MinDiGap { get; set; } = 5.0;

        [NinjaScriptProperty]
        [Display(Name = "Debug entry filters", GroupName = "Entry Filters", Order = 5)]
        public bool DebugEntryFilters { get; set; } = false;

        // ===== EMA Trailing (with N-bar delay) =====
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", GroupName = "Stops - EMA Trailing", Order = 1)]
        public int EmaPeriod { get; set; } = 50;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "EMA Offset (ticks)", GroupName = "Stops - EMA Trailing", Order = 2)]
        public int EmaOffsetTicks { get; set; } = 0;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "EMA Switch N Bars (delay)", GroupName = "Stops - EMA Trailing", Order = 3)]
        public int EmaSwitchNBars { get; set; } = 2;

        // ===== BarN Trailing =====
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Trailing N Bars", GroupName = "Stops - BarN Trailing", Order = 1)]
        public int TrailingNBars { get; set; } = 1;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "Trailing Offset (ticks)", GroupName = "Stops - BarN Trailing", Order = 2)]
        public int TrailingOffsetTicks { get; set; } = 5;

        // ===== ATR Step =====
        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name = "Step 1 trigger (ATR)", GroupName = "Stops - ATR Step", Order = 1)]
        public double Step1ATR { get; set; } = 0.50;

        [NinjaScriptProperty, Range(0.0, double.MaxValue)]
        [Display(Name = "Step 2 trigger (ATR)", GroupName = "Stops - ATR Step", Order = 2)]
        public double Step2ATR { get; set; } = 1.00;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "BE Plus (ticks)", GroupName = "Stops - ATR Step", Order = 3)]
        public int BreakevenPlusTicks { get; set; } = 5;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "Trail ATR Mult", GroupName = "Stops - ATR Step", Order = 4)]
        public double TrailAtrMult { get; set; } = 1.0;

        // ===== Time Windows (ETH + RTH) =====
        [NinjaScriptProperty]
        [Display(Name = "Enable time filters", GroupName = "Time Filters", Order = 1)]
        public bool EnableTimeFilters { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Clock offset (minutes)", GroupName = "Time Filters", Order = 2)]
        public int ClockOffsetMinutes { get; set; } = 0; // adjust if chart time != your intended reference

        // Allowed windows (toggle + HHmm)
        [NinjaScriptProperty] [Display(Name = "Allow London window", GroupName = "Time Filters", Order = 10)]
        public bool AllowLondon { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "London start HHmm", GroupName = "Time Filters", Order = 11)]
        public int LondonStartHHmm { get; set; } = 200;
        [NinjaScriptProperty] [Display(Name = "London end HHmm", GroupName = "Time Filters", Order = 12)]
        public int LondonEndHHmm { get; set; } = 500;

        [NinjaScriptProperty] [Display(Name = "Allow Premarket", GroupName = "Time Filters", Order = 20)]
        public bool AllowPremarket { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Premkt start HHmm", GroupName = "Time Filters", Order = 21)]
        public int PremktStartHHmm { get; set; } = 800;
        [NinjaScriptProperty] [Display(Name = "Premkt end HHmm", GroupName = "Time Filters", Order = 22)]
        public int PremktEndHHmm { get; set; } = 930;

        [NinjaScriptProperty] [Display(Name = "Allow RTH AM", GroupName = "Time Filters", Order = 30)]
        public bool AllowRthAm { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "RTH AM start HHmm", GroupName = "Time Filters", Order = 31)]
        public int RthAmStartHHmm { get; set; } = 940;
        [NinjaScriptProperty] [Display(Name = "RTH AM end HHmm", GroupName = "Time Filters", Order = 32)]
        public int RthAmEndHHmm { get; set; } = 1120;

        [NinjaScriptProperty] [Display(Name = "Allow RTH PM", GroupName = "Time Filters", Order = 40)]
        public bool AllowRthPm { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "RTH PM start HHmm", GroupName = "Time Filters", Order = 41)]
        public int RthPmStartHHmm { get; set; } = 1330;
        [NinjaScriptProperty] [Display(Name = "RTH PM end HHmm", GroupName = "Time Filters", Order = 42)]
        public int RthPmEndHHmm { get; set; } = 1550;

        // Blackouts (toggle + HHmm)
        [NinjaScriptProperty] [Display(Name = "Block Open", GroupName = "Time Filters", Order = 50)]
        public bool BlockOpen { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Open block start HHmm", GroupName = "Time Filters", Order = 51)]
        public int OpenBlockStartHHmm { get; set; } = 930;
        [NinjaScriptProperty] [Display(Name = "Open block end HHmm", GroupName = "Time Filters", Order = 52)]
        public int OpenBlockEndHHmm { get; set; } = 939;

        [NinjaScriptProperty] [Display(Name = "Block Lunch", GroupName = "Time Filters", Order = 60)]
        public bool BlockLunch { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Lunch start HHmm", GroupName = "Time Filters", Order = 61)]
        public int LunchStartHHmm { get; set; } = 1130;
        [NinjaScriptProperty] [Display(Name = "Lunch end HHmm", GroupName = "Time Filters", Order = 62)]
        public int LunchEndHHmm { get; set; } = 1320;

        [NinjaScriptProperty] [Display(Name = "Custom blackout 1 on", GroupName = "Time Filters", Order = 70)]
        public bool CustomBlk1On { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Blk1 start HHmm", GroupName = "Time Filters", Order = 71)]
        public int Blk1StartHHmm { get; set; } = 958;     // e.g., 10:00 news buffer
        [NinjaScriptProperty] [Display(Name = "Blk1 end HHmm", GroupName = "Time Filters", Order = 72)]
        public int Blk1EndHHmm { get; set; } = 1003;

        [NinjaScriptProperty] [Display(Name = "Custom blackout 2 on", GroupName = "Time Filters", Order = 80)]
        public bool CustomBlk2On { get; set; } = false;
        [NinjaScriptProperty] [Display(Name = "Blk2 start HHmm", GroupName = "Time Filters", Order = 81)]
        public int Blk2StartHHmm { get; set; } = 1358;    // e.g., FOMC 14:00 window start
        [NinjaScriptProperty] [Display(Name = "Blk2 end HHmm", GroupName = "Time Filters", Order = 82)]
        public int Blk2EndHHmm { get; set; } = 1407;

        // ===== Internals =====
        private const string LEntry = "LE";
        private const string SEntry = "SE";

        private ADX adx;
        private ATR atr;
        private EMA ema;

        // Internal DI calc (Wilder smoothing)
        private Series<double> dmPlus, dmMinus, sumDmPlus, sumDmMinus, sumTr, diPlusSeries, diMinusSeries;

        private double trailingStopLong  = double.NaN;
        private double trailingStopShort = double.NaN;

        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        // ---- BarN reference levels ----
        private double BarNStopLong()
        {
            double lo = Low[0];
            for (int i = 1; i < TrailingNBars; i++) lo = Math.Min(lo, Low[i]);
            return RT(lo - TickSize * TrailingOffsetTicks);
        }
        private double BarNStopShort()
        {
            double hi = High[0];
            for (int i = 1; i < TrailingNBars; i++) hi = Math.Max(hi, High[i]);
            return RT(hi + TickSize * TrailingOffsetTicks);
        }

        // ---- Entry filters ----
        private bool AdxSlopeOK()
        {
            if (!EnableEntryFilters || !RequireAdxRising) return true;
            if (CurrentBar <= AdxRisingBars) return false;
            return adx[0] > adx[AdxRisingBars];
        }
        private bool DIGapOK_Long()  => !EnableEntryFilters || MinDiGap <= 0 || (diPlusSeries[0]  - diMinusSeries[0]) >= MinDiGap;
        private bool DIGapOK_Short() => !EnableEntryFilters || MinDiGap <= 0 || (diMinusSeries[0] - diPlusSeries[0])  >= MinDiGap;

        // ---- Time helpers ----
        private int HHmmNow()
        {
            // Apply user offset so comparisons match intended reference clock (e.g., ET vs CT vs chart TZ)
            DateTime t = Time[0].AddMinutes(ClockOffsetMinutes);
            return t.Hour * 100 + t.Minute;
        }
        private static bool InRange(int hhmm, int startHHmm, int endHHmm)
        {
            // supports same-day ranges only
            return hhmm >= startHHmm && hhmm <= endHHmm;
        }
        private bool EntryTimeAllowed()
        {
            if (!EnableTimeFilters) return true;

            int hhmm = HHmmNow();

            // Blackouts first (any match blocks)
            if (BlockOpen && InRange(hhmm, OpenBlockStartHHmm, OpenBlockEndHHmm)) return false;
            if (BlockLunch && InRange(hhmm, LunchStartHHmm, LunchEndHHmm)) return false;
            if (CustomBlk1On && InRange(hhmm, Blk1StartHHmm, Blk1EndHHmm)) return false;
            if (CustomBlk2On && InRange(hhmm, Blk2StartHHmm, Blk2EndHHmm)) return false;

            // Allowed windows (any match ok)
            bool allowed = false;
            if (AllowLondon    && InRange(hhmm, LondonStartHHmm,  LondonEndHHmm))  allowed = true;
            if (AllowPremarket && InRange(hhmm, PremktStartHHmm,  PremktEndHHmm))  allowed = true;
            if (AllowRthAm     && InRange(hhmm, RthAmStartHHmm,   RthAmEndHHmm))   allowed = true;
            if (AllowRthPm     && InRange(hhmm, RthPmStartHHmm,   RthPmEndHHmm))   allowed = true;

            return allowed;
        }

        // =====================================================================
        // STAGE 1 RAW TRADE LOG
        // =====================================================================
        private const string Stage1ModelVersion = "OG";
        private const string Stage1BotName = "OG_ADXX";
        private const string Stage1DefaultAbMode = "OG";
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

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ADXX";
                Calculate = Calculate.OnPriceChange;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                TraceOrders = true;
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(AdxPeriod);
                atr = ATR(AtrPeriod);
                ema = EMA(EmaPeriod);

                dmPlus       = new Series<double>(this);
                dmMinus      = new Series<double>(this);
                sumDmPlus    = new Series<double>(this);
                sumDmMinus   = new Series<double>(this);
                sumTr        = new Series<double>(this);
                diPlusSeries = new Series<double>(this);
                diMinusSeries= new Series<double>(this);

                AddChartIndicator(adx);
                ConfigureStage1TradeLog();
                AddChartIndicator(ema);
            }
        }

        protected override void OnExecutionUpdate(
            Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            HandleStage1TradeLogExecution(execution, price, quantity, marketPosition, time);
        }

        protected override void OnBarUpdate()
        {
            // ---- Internal DI math (classic Wilder smoothing) ----
            double high0 = High[0], low0 = Low[0];

            if (CurrentBar == 0)
            {
                dmPlus[0] = dmMinus[0] = 0;
                sumTr[0] = (high0 - low0);
                sumDmPlus[0] = sumDmMinus[0] = 0;
                diPlusSeries[0] = diMinusSeries[0] = 0;
                return;
            }

            double high1 = High[1], low1 = Low[1], close1 = Close[1];
            double tr = Math.Max(high0 - low0, Math.Max(Math.Abs(high0 - close1), Math.Abs(low0 - close1)));
            double upMove   = high0 - high1;
            double downMove = low1 - low0;

            dmPlus[0]  = (upMove   > 0 && upMove   > downMove) ? upMove   : 0;
            dmMinus[0] = (downMove > 0 && downMove > upMove)   ? downMove : 0;

            if (CurrentBar < AdxPeriod)
            {
                sumTr[0]      = sumTr[1] + tr;
                sumDmPlus[0]  = sumDmPlus[1] + dmPlus[0];
                sumDmMinus[0] = sumDmMinus[1] + dmMinus[0];
            }
            else
            {
                sumTr[0]      = sumTr[1]      - (sumTr[1]      / AdxPeriod) + tr;
                sumDmPlus[0]  = sumDmPlus[1]  - (sumDmPlus[1]  / AdxPeriod) + dmPlus[0];
                sumDmMinus[0] = sumDmMinus[1] - (sumDmMinus[1] / AdxPeriod) + dmMinus[0];
            }

            double sTr = sumTr[0].ApproxCompare(0) == 0 ? 1e-9 : sumTr[0];
            diPlusSeries[0]  = 100.0 * (sumDmPlus[0]  / sTr);
            diMinusSeries[0] = 100.0 * (sumDmMinus[0] / sTr);

            if (CurrentBar < Math.Max(AdxPeriod, AtrPeriod) + 2)
                return;

            // ---- DI cross signals ----
            bool crossUp = diPlusSeries[0] > diMinusSeries[0] && diPlusSeries[1] <= diMinusSeries[1];
            bool crossDn = diPlusSeries[0] < diMinusSeries[0] && diPlusSeries[1] >= diMinusSeries[1];
            bool adxStrong = adx[0] > LevelRange;

            // ---- Flat: entries (time windows + filters) ----
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                trailingStopLong  = double.NaN;
                trailingStopShort = double.NaN;

                bool timeOK = EntryTimeAllowed();

                // =========================================================================
                // V3 GATEKEEPER & DIRECTIONAL ALIGNMENT (ADX TREND RIDER)
                // =========================================================================
                bool allowLong = true;
                bool allowShort = true;

                if (EnableTrinityFilter)
                {
                    // Accessing the global HUD Messenger state
                    string activePlaybook = HUDMessenger.CurrentPlaybook ?? "UNKNOWN";
                    string macroRegime = HUDMessenger.CurrentMacroRegime ?? "UNKNOWN";
                    string hmmRegime = HUDMessenger.CurrentHMMRegime ?? "UNKNOWN";

                    // 1. PLAYBOOK HARD BLOCKS: 
                    // ADX hates chop and mean-reversion. Block Illiquid, Transition, AND Liquid Rotation.
                    if (activePlaybook == "ROTATION_ILLIQUID" || activePlaybook == "TRANSITION" || activePlaybook == "ROTATION_LIQUID")
                    {
                        allowLong = false;
                        allowShort = false;
                    }

                    // 2. DIRECTIONAL PERMISSIONS: (Strict Alignment)
                    // If Macro or HMM is UP, strictly ban Shorts
                    if (macroRegime.Contains("UP") || hmmRegime == "TrendUp")
                    {
                        allowShort = false;
                    }
                    // If Macro or HMM is DOWN, strictly ban Longs
                    else if (macroRegime.Contains("DOWN") || hmmRegime == "TrendDown")
                    {
                        allowLong = false;
                    }
                }
                // =========================================================================

                // Apply the allowLong and allowShort gates to the execution triggers
                if (allowLong && timeOK && adxStrong && crossUp && AdxSlopeOK() && DIGapOK_Long())
                {
                    SubmitLongWithCatStops();
                }
                else if (allowShort && timeOK && adxStrong && crossDn && AdxSlopeOK() && DIGapOK_Short())
                {
                    SubmitShortWithCatStops();
                }
                else if (DebugEntryFilters && (crossUp || crossDn))
                {
                    string why = "";
                    if (!allowLong && crossUp) why += "HUD_Blocked_Long ";
                    if (!allowShort && crossDn) why += "HUD_Blocked_Short ";
                    if (!timeOK) why += "time ";
                    if (!adxStrong) why += "ADX<min ";
                    if (!AdxSlopeOK()) why += "ADXslope ";
                    if (crossUp && !DIGapOK_Long()) why += "DIgapLong ";
                    if (crossDn && !DIGapOK_Short()) why += "DIgapShort ";
                    if (why != "") Print($"{Time[0]} entry filtered: {why} | ADX={adx[0]:F1} DI+={diPlusSeries[0]:F1} DI-={diMinusSeries[0]:F1} hhmm={HHmmNow()}");
                }
            }

            // ---- Manage LONG ----
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (UseStopX && (adx[0] < LevelRange || crossDn))
                    ExitLong("StopX", LEntry);

                int bse = BarsSinceEntryExecution(0, LEntry, 0);

                switch (StopModeSelection)
                {
                    case StopMode.BarNTrailing:
                        if (bse != -1 && bse >= Math.Max(1, TrailingNBars))
                        {
                            double proposed = BarNStopLong();
                            trailingStopLong = double.IsNaN(trailingStopLong)
                                ? proposed
                                : Math.Max(trailingStopLong, proposed);
                            SetStopLoss(LEntry, CalculationMode.Price, trailingStopLong, false);
                        }
                        break;

                    case StopMode.EmaTrailing:
                        if (bse != -1 && bse >= Math.Max(1, EmaSwitchNBars))
                        {
                            double emaStp = RT(ema[0] - EmaOffsetTicks * TickSize);
                            trailingStopLong = double.IsNaN(trailingStopLong)
                                ? emaStp
                                : Math.Max(trailingStopLong, emaStp);
                            SetStopLoss(LEntry, CalculationMode.Price, trailingStopLong, false);
                        }
                        break;

                    case StopMode.AtrStep:
                    {
                        double move  = Close[0] - Position.AveragePrice;
                        double step1 = Step1ATR * atr[0];
                        double step2 = Step2ATR * atr[0];

                        if (move >= step2)
                        {
                            double atrTrail = RT(Close[0] - TrailAtrMult * atr[0]);
                            trailingStopLong = double.IsNaN(trailingStopLong)
                                ? atrTrail
                                : Math.Max(trailingStopLong, atrTrail);
                        }
                        else if (move >= step1)
                        {
                            double be = RT(Position.AveragePrice + BreakevenPlusTicks * TickSize);
                            trailingStopLong = double.IsNaN(trailingStopLong)
                                ? be
                                : Math.Max(trailingStopLong, be);
                        }
                        SetStopLoss(LEntry, CalculationMode.Price, trailingStopLong, false);
                        }
                        break;

                    case StopMode.AtrStatic:
                    default:
                        break;
                }
            }

            // ---- Manage SHORT ----
            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (UseStopX && (adx[0] < LevelRange || crossUp))
                    ExitShort("StopX", SEntry);

                int bse = BarsSinceEntryExecution(0, SEntry, 0);

                switch (StopModeSelection)
                {
                    case StopMode.BarNTrailing:
                        if (bse != -1 && bse >= Math.Max(1, TrailingNBars))
                        {
                            double proposed = BarNStopShort();
                            trailingStopShort = double.IsNaN(trailingStopShort)
                                ? proposed
                                : Math.Min(trailingStopShort, proposed);
                            SetStopLoss(SEntry, CalculationMode.Price, trailingStopShort, false);
                        }
                        break;

                    case StopMode.EmaTrailing:
                        if (bse != -1 && bse >= Math.Max(1, EmaSwitchNBars))
                        {
                            double emaStp = RT(ema[0] + EmaOffsetTicks * TickSize);
                            trailingStopShort = double.IsNaN(trailingStopShort)
                                ? emaStp
                                : Math.Min(trailingStopShort, emaStp);
                            SetStopLoss(SEntry, CalculationMode.Price, trailingStopShort, false);
                        }
                        break;

                    case StopMode.AtrStep:
                    {
                        double move  = Position.AveragePrice - Close[0];
                        double step1 = Step1ATR * atr[0];
                        double step2 = Step2ATR * atr[0];

                        if (move >= step2)
                        {
                            double atrTrail = RT(Close[0] + TrailAtrMult * atr[0]);
                            trailingStopShort = double.IsNaN(trailingStopShort)
                                ? atrTrail
                                : Math.Min(trailingStopShort, atrTrail);
                        }
                        else if (move >= step1)
                        {
                            double be = RT(Position.AveragePrice - BreakevenPlusTicks * TickSize);
                            trailingStopShort = double.IsNaN(trailingStopShort)
                                ? be
                                : Math.Min(trailingStopShort, be);
                        }
                        SetStopLoss(SEntry, CalculationMode.Price, trailingStopShort, false);
                        }
                        break;

                    case StopMode.AtrStatic:
                    default:
                        break;
                }
            }
        }

        // ===== Entry helpers: attach stop/target BEFORE entry =====
        private void SubmitLongWithCatStops()
        {
            if (!IsBotAllowedByTrinity()) return;

            double risk = atr[0] * AtrMultiplier;
            double stp  = RT(Close[0] - risk);
            double tgt  = RT(Close[0] + risk * Math.Max(0.1, RiskReward));

            SetStopLoss    (LEntry, CalculationMode.Price, stp, false);
            CaptureInitialStopForLog(stp, "LONG");
            SetProfitTarget(LEntry, CalculationMode.Price, tgt);

            trailingStopLong = stp;
            EnterLong(Contracts, LEntry);
        }

        private void SubmitShortWithCatStops()
        {
            if (!IsBotAllowedByTrinity()) return;

            double risk = atr[0] * AtrMultiplier;
            double stp  = RT(Close[0] + risk);
            double tgt  = RT(Close[0] - risk * Math.Max(0.1, RiskReward));

            SetStopLoss    (SEntry, CalculationMode.Price, stp, false);
            CaptureInitialStopForLog(stp, "SHORT");
            SetProfitTarget(SEntry, CalculationMode.Price, tgt);

            trailingStopShort = stp;
            EnterShort(Contracts, SEntry);
        }
    }
}