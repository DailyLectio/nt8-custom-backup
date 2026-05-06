// CC BY-NC 4.0
// KalmanPulse_Fader_V1B.cs
// FIXES 2026-05-06: BUG-003 (ExitCooldownBars promoted to parameter), BUG-005 (MaxTotalContracts Range unified) â€” Adaptive Kalman Fade Strategy (V1B)
// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// V1B ADDITIONS over V1A:
//   1. A/B Mode switch (RequireFootprintConfirmation)
//      false = V1A: tick micro-reversal at inner band only
//      true  = V1B: also requires fresh ABS, DD, or TF within window
//   2. Daily Bias Filter â€” same D/P/b/B routing as VolState
//   3. Kill Switch â€” DEIA, EEMDF, or DT immediately flattens Leg2 runner
// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

#region Using declarations
using System;
using System.Globalization;
using System.IO;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class KalmanPulse_Fader_V1B : Strategy
    {
        // =====================================================================
        // PARAMETERS â€” KALMAN ENGINE (unchanged)
        // =====================================================================
        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name = "Kernel Length", GroupName = "1. Kalman Engine", Order = 0)]
        public int KernelLength { get; set; } = 33;

        [NinjaScriptProperty, Range(0.1, 10.0)]
        [Display(Name = "Kernel Alpha", GroupName = "1. Kalman Engine", Order = 1)]
        public double KernelAlpha { get; set; } = 1.0;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name = "ATR Period", GroupName = "1. Kalman Engine", Order = 2)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.5, 5.0)]
        [Display(Name = "ATR Factor (outer envelope)", GroupName = "1. Kalman Engine", Order = 3)]
        public double AtrFactor { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0.5, 4.0)]
        [Display(Name = "Inner Band Multiplier", GroupName = "1. Kalman Engine", Order = 4)]
        public double InnerMult { get; set; } = 1.5;

        // =====================================================================
        // PARAMETERS â€” RISK (unchanged)
        // =====================================================================
        [NinjaScriptProperty, Range(0.5, 5.0)]
        [Display(Name = "ATR Stop Multiplier", GroupName = "2. Risk", Order = 0)]
        public double AtrStopMult { get; set; } = 0.9;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Size Pct", GroupName = "2. Risk", Order = 1)]
        public int SizePct { get; set; } = 100;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5  ES=12.50  MNQ=0.50  MES=1.25", GroupName = "2. Risk", Order = 2)]
        public double TickValueDollars { get; set; } = 5.00;

        [NinjaScriptProperty]
        [Display(Name = "AB Mode", GroupName = "6. Trade Log", Order = 0)]
        public string AbMode { get; set; } = "B";

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Max Total Contracts", GroupName = "2. Risk", Order = 3)]
        public int MaxTotalContracts { get; set; } = 2;

        [NinjaScriptProperty, Range(0.10, 10.0)]
        [Display(Name = "Leg 1 Target ATR", GroupName = "2. Risk", Order = 4)]
        public double Leg1TargetAtr { get; set; } = 1.0;

        [NinjaScriptProperty, Range(0.10, 10.0)]
        [Display(Name = "Leg 2 Target ATR", GroupName = "2. Risk", Order = 5)]
        public double Leg2TargetAtr { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "Min Leg 1 Target Ticks", GroupName = "2. Risk", Order = 6)]
        public int MinLeg1TargetTicks { get; set; } = 10;

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "Min Leg 2 Target Ticks", GroupName = "2. Risk", Order = 7)]
        public int MinLeg2TargetTicks { get; set; } = 16;

        [NinjaScriptProperty, Range(0.10, 10.0)]
        [Display(Name = "Break Even After ATR", GroupName = "2. Risk", Order = 8)]
        public double BreakEvenAfterAtr { get; set; } = 0.75;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Break Even Plus Ticks", GroupName = "2. Risk", Order = 9)]
        public int BreakEvenPlusTicks { get; set; } = 4;
        // =====================================================================
        // PARAMETERS â€” GUARDS / TIME (unchanged)
        // =====================================================================
        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "3. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0=off)", GroupName = "3. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0=off)", GroupName = "3. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 50)]
        [Display(Name = "Exit Cooldown (Bars)", GroupName = "3. Guards", Order = 3,
                 Description = "Bars to wait after any exit before re-entering. BUG-003 FIX: promoted from hardcoded const.")]
        public int ExitCooldownBars { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "4. Time", Order = 0)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Start Time (HHmmss)", GroupName = "4. Time", Order = 1)]
        public int StartTime { get; set; } = 103500;

        [NinjaScriptProperty]
        [Display(Name = "End Time (HHmmss)", GroupName = "4. Time", Order = 2)]
        public int EndTime { get; set; } = 155500;

        // =====================================================================
        // PARAMETERS â€” V1B ORDERFLOW LAYER (NEW)
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "A/B: Require Footprint Confirmation",
                 GroupName = "5. OrderFlow (V1B)", Order = 0,
                 Description = "false=V1A (tick reversal only). true=V1B (also needs ABS/DD/TF).")]
        public bool RequireFootprintConfirmation { get; set; } = false;

        [NinjaScriptProperty, Range(1, 15)]
        [Display(Name = "Signal Valid Window (minutes)", GroupName = "5. OrderFlow (V1B)", Order = 1)]
        public int FootprintValidMinutes { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Entry: Accept ABS", GroupName = "5. OrderFlow (V1B)", Order = 2)]
        public bool UseAbsEntry { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Entry: Accept DD", GroupName = "5. OrderFlow (V1B)", Order = 3)]
        public bool UseDdEntry { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Entry: Accept TF", GroupName = "5. OrderFlow (V1B)", Order = 4)]
        public bool UseTfEntry { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable Kill Switch (DEIA / EEMDF / DT)",
                 GroupName = "5. OrderFlow (V1B)", Order = 5)]
        public bool EnableKillSwitch { get; set; } = true;

        [NinjaScriptProperty, Range(0, 5)]
        [Display(Name = "Kill Switch Signal Age (minutes)", GroupName = "5. OrderFlow (V1B)", Order = 6)]
        public int KillSwitchMaxMinutes { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Enable Daily Bias Filter (TradeHUD)",
                 GroupName = "5. OrderFlow (V1B)", Order = 7)]
        public bool EnableDailyBiasFilter { get; set; } = true;

        // =====================================================================
        // KALMAN STATE
        // =====================================================================
        private double kalmanState     = double.NaN;
        private double kalmanVar       = 1.0;
        private double prevKalmanState = double.NaN;

        private double baseline      = 0;
        private double upperEnvelope = 0;
        private double lowerEnvelope = 0;
        private double innerUpper    = 0;
        private double innerLower    = 0;
        private double baselineSlope = 0;

        // =====================================================================
        // ATR (manual Wilder, tick-safe)
        // =====================================================================
        private double runningAtr   = 0;
        private int    atrInitCount = 0;

        // =====================================================================
        // RUNTIME STATE
        // =====================================================================
        private int    consecutiveLosers  = 0;
        private int    lastTradeCount     = 0;
        private double sessionStartProfit = 0;

        private bool   leg1Hit        = false;
        private bool   leg1JustHit    = false;
        private int    currentLeg2Qty = 1;
        private string activeLeg2     = "";

        private int    lastEntryBar   = -1;
        private int    lastExitBar    = -1;
        private double prevTickPrice  = 0;
        private double lastLeg1Target = 0;
        private double lastLeg2Target = 0;

        private double entryAtrForTrade = 0;
        private double activeEntryPrice = 0;
        private bool   leg2StopAtBreakeven = false;

        // =====================================================================
        // ORDER LABELS
        // =====================================================================
        private const string KPL1 = "KPF_Long1";
        private const string KPL2 = "KPF_Long2";
        private const string KPS1 = "KPF_Short1";
        private const string KPS2 = "KPF_Short2";

        // =====================================================================
        // HELPERS
        // =====================================================================
        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        private bool IsInTime()
        {
            if (!EnableTimeFilter) return true;
            int t = ToTime(Time[0]);
            return t >= StartTime && t <= EndTime;
        }

        private int CalcMaxContracts(double atrVal)
        {
            if (atrVal <= 0) return 1;
            double dollarRisk = (atrVal * AtrStopMult) / TickSize * TickValueDollars;
            if (dollarRisk <= 0) return 1;
            int riskBasedQty = Math.Max(1, (int)(1500.0 / dollarRisk));
            return Math.Min(MaxTotalContracts, riskBasedQty);
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
            => Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));

        private double ComputeAtr()
        {
            if (CurrentBar < 1) return TickSize * 10;
            double tr = Math.Max(High[0] - Low[0],
                        Math.Max(Math.Abs(High[0] - Close[1]),
                                 Math.Abs(Low[0] - Close[1])));
            atrInitCount++;
            if (atrInitCount <= AtrPeriod)
                runningAtr = runningAtr + (tr - runningAtr) / atrInitCount;
            else
                runningAtr = (runningAtr * (AtrPeriod - 1) + tr) / AtrPeriod;
            return runningAtr > 0 ? runningAtr : TickSize * 10;
        }

        private double ComputeKernelSmoothed()
        {
            int len = Math.Min(KernelLength, CurrentBar + 1);
            double center = len / 2.0;
            double ws = 0, ps = 0;
            for (int i = 0; i < len; i++)
            {
                double dist = Math.Abs(i - center);
                double w = Math.Exp(-KernelAlpha * (len - 1 - i) / len)
                         * Math.Exp(-Math.Pow(dist / (len / 3.0), 2));
                ps += Close[len - 1 - i] * w;
                ws += w;
            }
            return ws > 0 ? ps / ws : Close[0];
        }

        private void UpdateKalman(double measurement, double atrVol)
        {
            if (double.IsNaN(kalmanState)) { kalmanState = measurement; kalmanVar = 1.0; return; }
            double predictedVar = kalmanVar + atrVol * 0.05;
            double innovationVar = predictedVar + atrVol * 0.10;
            double gain = predictedVar / innovationVar;
            prevKalmanState = kalmanState;
            kalmanState    += gain * (measurement - kalmanState);
            kalmanVar       = (1.0 - gain) * predictedVar;
        }

        private void RefreshEnvelopes(double atrVal)
        {
            baseline      = kalmanState;
            baselineSlope = double.IsNaN(prevKalmanState) ? 0 : kalmanState - prevKalmanState;
            upperEnvelope = baseline + atrVal * AtrFactor;
            lowerEnvelope = baseline - atrVal * AtrFactor;
            innerUpper    = baseline + atrVal * InnerMult;
            innerLower    = baseline - atrVal * InnerMult;
        }

        // â”€â”€ V1B helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private bool IsFootprintEntryConfirmed()
        {
            if (!RequireFootprintConfirmation) return true;
            DateTime now = Time[0];
            if (UseAbsEntry && HUDMessengerV1B.IsSignalFresh("Scanner_ABS", now, FootprintValidMinutes)) return true;
            if (UseDdEntry  && HUDMessengerV1B.IsSignalFresh("Scanner_DD",  now, FootprintValidMinutes)) return true;
            if (UseTfEntry  && HUDMessengerV1B.IsSignalFresh("Scanner_TF",  now, FootprintValidMinutes)) return true;
            return false;
        }

        private bool IsKillSwitchActive()
        {
            if (!EnableKillSwitch) return false;
            DateTime now = Time[0];
            double w = Math.Max(0.5, KillSwitchMaxMinutes);
            return HUDMessengerV1B.IsSignalFresh("Scanner_DEIA",  now, w)
                || HUDMessengerV1B.IsSignalFresh("Scanner_EEMDF", now, w)
                || HUDMessengerV1B.IsSignalFresh("Scanner_DT",    now, w);
        }

        private bool IsBiasCompatible(bool isLong)
        {
            if (!EnableDailyBiasFilter) return true;
            string bias = HUDMessengerV1B.CurrentDailyBias;
            if (string.IsNullOrEmpty(bias)) return true;
            switch (bias)
            {
                case "D": return true;
                case "P": return isLong;
                case "b": return !isLong;
                case "B": return false;
                default:  return true;
            }
        }

        // =====================================================================
        // STAGE 1 RAW TRADE LOG
        // =====================================================================
        private const string Stage1ModelVersion = "V1B";
        private const string Stage1BotName = "KalmanPulse_Fader_B";
        private const string Stage1DefaultAbMode = "B";
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
            string mode = (AbMode ?? "").Trim().ToUpperInvariant();
            return mode.Length == 0 ? Stage1DefaultAbMode : mode;
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

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "KalmanPulse_Fader_V1B";
                Calculate                    = Calculate.OnEachTick;
                EntriesPerDirection          = 2;
                EntryHandling                = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                RealtimeErrorHandling        = RealtimeErrorHandling.StopCancelClose;
                TraceOrders                  = false;
            }
            else if (State == State.DataLoaded)
            {
                ConfigureStage1TradeLog();
                lastTradeCount = SystemPerformance.AllTrades.Count;
            }
        }

        protected override void OnExecutionUpdate(
            Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            HandleStage1TradeLogExecution(execution, price, quantity, marketPosition, time);
            if (execution != null
                && execution.Order != null
                && execution.Order.OrderState == OrderState.Filled
                && execution.Order.Name != null
                && execution.Order.Name.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                lastExitBar = CurrentBar;
            }

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
        // MAIN TICK UPDATE
        // =====================================================================
        protected override void OnBarUpdate()
        {
            if (string.IsNullOrEmpty(AbMode) || AbMode.Trim().ToUpperInvariant() == "UNKNOWN")
            {
                Print("KalmanPulse: ab_mode not initialized - bot disabled this bar.");
                return;
            }

            if (CurrentBar < KernelLength + 4) return;

            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers  = 0;
                leg1Hit = false; leg1JustHit = false;
                currentLeg2Qty = 1; activeLeg2 = "";
                lastEntryBar = -1; lastExitBar = -1; prevTickPrice = 0;
                lastLeg1Target = 0; lastLeg2Target = 0;
                entryAtrForTrade = 0; activeEntryPrice = 0; leg2StopAtBreakeven = false;
                kalmanState = double.NaN; prevKalmanState = double.NaN;
                kalmanVar = 1.0; runningAtr = 0; atrInitCount = 0;
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }

            double atrVal    = ComputeAtr();
            double kernelVal = ComputeKernelSmoothed();
            UpdateKalman(kernelVal, atrVal);
            RefreshEnvelopes(atrVal);

            double curPrice = Close[0];

            // â”€â”€ Envelope breach exit â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (Position.MarketPosition == MarketPosition.Long && curPrice < lowerEnvelope)
            {
                ExitLong("EnvelopeBreakExit", "");
                lastExitBar = CurrentBar;
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                prevTickPrice = curPrice; return;
            }
            if (Position.MarketPosition == MarketPosition.Short && curPrice > upperEnvelope)
            {
                ExitShort("EnvelopeBreakExit", "");
                lastExitBar = CurrentBar;
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                prevTickPrice = curPrice; return;
            }

            // â”€â”€ V1B Kill Switch â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (Position.MarketPosition != MarketPosition.Flat && leg1Hit && IsKillSwitchActive())
            {
                if (Position.MarketPosition == MarketPosition.Long) ExitLong("KillSwitch", KPL2);
                else ExitShort("KillSwitch", KPS2);
                lastExitBar = CurrentBar;
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                Print(string.Format("[KPF_V1B] KILL SWITCH at {0:F2}", curPrice));
                prevTickPrice = curPrice; return;
            }

            // â”€â”€ Runner management + ATR breakeven stop â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (!leg1Hit && Position.Quantity <= currentLeg2Qty && currentLeg2Qty > 0)
                {
                    leg1Hit = true; leg1JustHit = true;
                    double pivot = Position.MarketPosition == MarketPosition.Long
                        ? RT(Position.AveragePrice + BreakEvenPlusTicks * TickSize)
                        : RT(Position.AveragePrice - BreakEvenPlusTicks * TickSize);
                    if (activeLeg2 == KPL2) SetStopLoss(KPL2, CalculationMode.Price, pivot, false);
                    else if (activeLeg2 == KPS2) SetStopLoss(KPS2, CalculationMode.Price, pivot, false);
                    leg2StopAtBreakeven = true;
                }
                else if (leg1JustHit) leg1JustHit = false;

                if (!leg2StopAtBreakeven && activeLeg2.Length > 0 && entryAtrForTrade > 0)
                {
                    double favorableMove = Position.MarketPosition == MarketPosition.Long
                        ? curPrice - activeEntryPrice
                        : activeEntryPrice - curPrice;
                    if (favorableMove >= entryAtrForTrade * BreakEvenAfterAtr)
                    {
                        double pivot = Position.MarketPosition == MarketPosition.Long
                            ? RT(activeEntryPrice + BreakEvenPlusTicks * TickSize)
                            : RT(activeEntryPrice - BreakEvenPlusTicks * TickSize);
                        if (activeLeg2 == KPL2) SetStopLoss(KPL2, CalculationMode.Price, pivot, false);
                        else if (activeLeg2 == KPS2) SetStopLoss(KPS2, CalculationMode.Price, pivot, false);
                        leg2StopAtBreakeven = true;
                    }
                }

                prevTickPrice = curPrice; return;
            }

            // â”€â”€ Entry gates â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            leg1Hit = false; leg1JustHit = false; currentLeg2Qty = 1; activeLeg2 = "";
            entryAtrForTrade = 0; activeEntryPrice = 0; leg2StopAtBreakeven = false;

            if (CurrentBar == lastEntryBar) { prevTickPrice = curPrice; return; }
            if (CurrentBar - lastExitBar < ExitCooldownBars) { prevTickPrice = curPrice; return; }
            if (consecutiveLosers >= MaxConsecutiveLosses) { prevTickPrice = curPrice; return; }
            if (!IsInTime()) { prevTickPrice = curPrice; return; }

            if (DailyGoal > 0 || DailyLossLimit > 0)
            {
                double pnl = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit
                             - sessionStartProfit;
                if (DailyGoal > 0 && pnl >= DailyGoal) { prevTickPrice = curPrice; return; }
                if (DailyLossLimit > 0 && pnl <= -DailyLossLimit) { prevTickPrice = curPrice; return; }
            }

            bool tickUp   = prevTickPrice > 0 && curPrice > prevTickPrice;
            bool tickDown = prevTickPrice > 0 && curPrice < prevTickPrice;
            double slopeThreshold = atrVal * 0.1;

            // â”€â”€ LONG FADE â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            bool fadeLong = curPrice <= innerLower && curPrice > lowerEnvelope
                         && baselineSlope >= -slopeThreshold;

            if (fadeLong && tickUp && IsBiasCompatible(true) && IsFootprintEntryConfirmed())
            {
                double stopPrice  = RT(curPrice - atrVal * AtrStopMult);
                double leg1Dist   = Math.Max(atrVal * Leg1TargetAtr, MinLeg1TargetTicks * TickSize);
                double leg2Dist   = Math.Max(atrVal * Leg2TargetAtr, MinLeg2TargetTicks * TickSize);
                double leg1Target = RT(curPrice + leg1Dist);
                double leg2Target = RT(curPrice + leg2Dist);

                if (curPrice <= stopPrice) { prevTickPrice = curPrice; return; }
                if ((leg1Target - curPrice) / TickSize < MinLeg1TargetTicks || (leg2Target - curPrice) / TickSize < MinLeg2TargetTicks) { prevTickPrice = curPrice; return; }

                int maxC = CalcMaxContracts(atrVal);
                int sz   = ScaleByConfidence(maxC, SizePct);
                int l1q  = Math.Max(1, sz / 2);
                int l2q  = Math.Max(1, sz - l1q);

                SetStopLoss(KPL1, CalculationMode.Price, stopPrice, false);
                SetStopLoss(KPL2, CalculationMode.Price, stopPrice, false);
                CaptureInitialStopForLog(stopPrice, "LONG");
                SetProfitTarget(KPL1, CalculationMode.Price, leg1Target);
                SetProfitTarget(KPL2, CalculationMode.Price, leg2Target);
                EnterLong(l1q, KPL1); EnterLong(l2q, KPL2);

                currentLeg2Qty = l2q; activeLeg2 = KPL2;
                lastLeg1Target = leg1Target; lastLeg2Target = leg2Target;
                lastEntryBar   = CurrentBar;
                entryAtrForTrade = atrVal; activeEntryPrice = curPrice; leg2StopAtBreakeven = false;

                Print(string.Format("[KPF_V1B] LONG | Baseline:{0:F2} | Bias:{1} | FP:{2} | " +
                    "Qty:{3}+{4} | Stop:{5:F2} | T1:{6:F2} | T2:{7:F2}",
                    baseline, HUDMessengerV1B.CurrentDailyBias,
                    RequireFootprintConfirmation ? "ON" : "OFF",
                    l1q, l2q, stopPrice, leg1Target, leg2Target));
            }
            // â”€â”€ SHORT FADE â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            else
            {
                bool fadeShort = curPrice >= innerUpper && curPrice < upperEnvelope
                              && baselineSlope <= slopeThreshold;

                if (fadeShort && tickDown && IsBiasCompatible(false) && IsFootprintEntryConfirmed())
                {
                    double stopPrice  = RT(curPrice + atrVal * AtrStopMult);
                    double leg1Dist   = Math.Max(atrVal * Leg1TargetAtr, MinLeg1TargetTicks * TickSize);
                    double leg2Dist   = Math.Max(atrVal * Leg2TargetAtr, MinLeg2TargetTicks * TickSize);
                    double leg1Target = RT(curPrice - leg1Dist);
                    double leg2Target = RT(curPrice - leg2Dist);

                    if (curPrice >= stopPrice) { prevTickPrice = curPrice; return; }
                    if ((curPrice - leg1Target) / TickSize < MinLeg1TargetTicks || (curPrice - leg2Target) / TickSize < MinLeg2TargetTicks) { prevTickPrice = curPrice; return; }

                    int maxC = CalcMaxContracts(atrVal);
                    int sz   = ScaleByConfidence(maxC, SizePct);
                    int l1q  = Math.Max(1, sz / 2);
                    int l2q  = Math.Max(1, sz - l1q);

                    SetStopLoss(KPS1, CalculationMode.Price, stopPrice, false);
                    SetStopLoss(KPS2, CalculationMode.Price, stopPrice, false);
                    CaptureInitialStopForLog(stopPrice, "SHORT");
                    SetProfitTarget(KPS1, CalculationMode.Price, leg1Target);
                    SetProfitTarget(KPS2, CalculationMode.Price, leg2Target);
                    EnterShort(l1q, KPS1); EnterShort(l2q, KPS2);

                    currentLeg2Qty = l2q; activeLeg2 = KPS2;
                    lastLeg1Target = leg1Target; lastLeg2Target = leg2Target;
                    lastEntryBar   = CurrentBar;
                    entryAtrForTrade = atrVal; activeEntryPrice = curPrice; leg2StopAtBreakeven = false;

                    Print(string.Format("[KPF_V1B] SHORT | Baseline:{0:F2} | Bias:{1} | FP:{2} | " +
                        "Qty:{3}+{4} | Stop:{5:F2} | T1:{6:F2} | T2:{7:F2}",
                        baseline, HUDMessengerV1B.CurrentDailyBias,
                        RequireFootprintConfirmation ? "ON" : "OFF",
                        l1q, l2q, stopPrice, leg1Target, leg2Target));
                }
            }

            prevTickPrice = curPrice;
        }
    }
}



