// CC BY-NC 4.0

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Core.FloatingPoint;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class AdxDiCrossOG : Strategy
    {
        // ---- Stop modes (single-leg only) ----TRUE ADX OG
        public enum StopMode
        {
            AtrStatic = 0,    // fixed ATR stop set at entry
            EmaTrailing = 1,  // EMA +/- ticks trailing
            BarNTrailing = 2, // N-bar +/- ticks trailing
            AtrStep = 3       // step -> BE+ -> ATR trail
        }

        // ---- Indicators ----
        private ADX adxIndicator;
        private ATR atrIndicator;
        private EMA emaIndicator;

        // ---- DI scaffolding ----
        private Series<double> dmPlus, dmMinus, sumDmPlus, sumDmMinus, sumTr, diPlusSeries, diMinusSeries;

        // ---- trailing anchors ----
        private double trailingStopLong = double.NaN, trailingStopShort = double.NaN;

        // ========== PARAMETERS ==========

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Order = 1, GroupName = "Parameters")]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Use Stop X (ADX/DI exit)", Order = 2, GroupName = "Parameters")]
        public bool UseStopX { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Risk Reward (for targets)", Order = 3, GroupName = "Parameters")]
        public double RiskReward { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ADX Period", Order = 4, GroupName = "Parameters")]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Display(Name = "Level Range (ADX min)", Order = 5, GroupName = "Parameters")]
        public double LevelRange { get; set; } = 20;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "Parameters")]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Display(Name = "ATR Multiplier", Order = 7, GroupName = "Parameters")]
        public double AtrMultiplier { get; set; } = 1.0;

        // ---- Stops (single-leg) ----
        [NinjaScriptProperty]
        [Display(Name = "Stop Mode", Order = 10, GroupName = "Stops")]
        public StopMode StopModeSelection { get; set; } = StopMode.AtrStatic;

        // EMA trailing
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 11, GroupName = "Stops - EMA Trailing")]
        public int EmaPeriod { get; set; } = 50;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "EMA Offset (ticks)", Order = 12, GroupName = "Stops - EMA Trailing")]
        public int EmaOffsetTicks { get; set; } = 0;

        // BarN trailing
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Trailing N Bars", Order = 13, GroupName = "Stops - BarN Trailing")]
        public int TrailingNBars { get; set; } = 1;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "Trailing Offset (ticks)", Order = 14, GroupName = "Stops - BarN Trailing")]
        public int TrailingOffsetTicks { get; set; } = 4;

        // ATR Step
        [NinjaScriptProperty]
        [Display(Name = "Step 1 trigger (ATR)", Order = 15, GroupName = "Stops - ATR Step")]
        public double Step1ATR { get; set; } = 0.25;

        [NinjaScriptProperty]
        [Display(Name = "Step 2 trigger (ATR)", Order = 16, GroupName = "Stops - ATR Step")]
        public double Step2ATR { get; set; } = 0.50;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "BE Plus (ticks)", Order = 17, GroupName = "Stops - ATR Step")]
        public int BreakevenPlusTicks { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "Trail ATR Mult", Order = 18, GroupName = "Stops - ATR Step")]
        public double TrailAtrMult { get; set; } = 1.0;


        // =====================================================================
        // STAGE 1 RAW TRADE LOG
        // =====================================================================
        private const string Stage1ModelVersion = "OG";
        private const string Stage1BotName = "OG_AdxDiCrossOG";
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

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            HandleStage1TradeLogExecution(execution, price, quantity, marketPosition, time);
        }

        private void HandleStage1TradeLogExecution(Execution execution, double price, int quantity,
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
                    WriteStage1TradeLog(price, time);
                    ResetStage1TradeLogState();
                }
            }
        }

        private void WriteStage1TradeLog(double exitPrice, DateTime exitTime)
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

            string accountName = Account == null ? "UNKNOWN" : Account.Name;
            string row = string.Join(",",
                entryTimeForLog.ToString("yyyy-MM-dd"),
                entryTimeForLog.ToString("yyyy-MM-dd HH:mm:ss"),
                exitTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Stage1ModelVersion,
                SafeStage1Csv(accountName),
                SafeStage1Csv(Name),
                SafeStage1Csv(Stage1BotName),
                SafeStage1Csv(Stage1DefaultAbMode),
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

        private string InferStage1ExitReason(string orderName)
        {
            if (string.IsNullOrEmpty(orderName)) return "UNKNOWN";
            string n = orderName.ToUpperInvariant();
            if (n.Contains("PROFIT") || n.Contains("TARGET")) return "TARGET_HIT";
            if (n.Contains("STOP")) return "STOP_HIT";
            if (n.Contains("TRAIL")) return "TRAIL_STOP";
            if (n.Contains("SLOPE")) return "SLOPE_EXIT";
            if (n.Contains("CROSS") || n.Contains("STOPX")) return "ADX_DI_CROSS_EXIT";
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
                Name = "AdxDiCrossOG";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
            }
            else if (State == State.DataLoaded)
            {
                adxIndicator = ADX(AdxPeriod);
                atrIndicator = ATR(AtrPeriod);
                emaIndicator = EMA(EmaPeriod);

                dmPlus       = new Series<double>(this);
                dmMinus      = new Series<double>(this);
                sumDmPlus    = new Series<double>(this);
                sumDmMinus   = new Series<double>(this);
                sumTr        = new Series<double>(this);
                diPlusSeries = new Series<double>(this);
                diMinusSeries= new Series<double>(this);

                AddChartIndicator(adxIndicator);
                AddChartIndicator(emaIndicator);
                ConfigureStage1TradeLog();
            }
        }

        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        protected override void OnBarUpdate()
        {
            // ---- DI math matching your original approach ----
            double high0 = High[0], low0 = Low[0];

            if (CurrentBar == 0)
            {
                double tr0 = high0 - low0;
                dmPlus[0] = dmMinus[0] = 0;
                sumTr[0] = tr0; sumDmPlus[0] = 0; sumDmMinus[0] = 0;
                diPlusSeries[0] = diMinusSeries[0] = 0;
                return;
            }
            else
            {
                double high1 = High[1], low1 = Low[1], close1 = Close[1];
                double tr0 = Math.Max(Math.Abs(low0 - close1), Math.Max(high0 - low0, Math.Abs(high0 - close1)));
                dmPlus[0]  = high0 - high1 > low1 - low0 ? Math.Max(high0 - high1, 0) : 0;
                dmMinus[0] = low1 - low0 > high0 - high1 ? Math.Max(low1 - low0, 0) : 0;

                if (CurrentBar < AdxPeriod)
                {
                    sumTr[0]      = sumTr[1] + tr0;
                    sumDmPlus[0]  = sumDmPlus[1] + dmPlus[0];
                    sumDmMinus[0] = sumDmMinus[1] + dmMinus[0];
                    return;
                }
                else
                {
                    double tr1 = sumTr[1], sdp1 = sumDmPlus[1], sdm1 = sumDmMinus[1];
                    sumTr[0]      = tr1  - tr1  / AdxPeriod + tr0;
                    sumDmPlus[0]  = sdp1 - sdp1 / AdxPeriod + dmPlus[0];
                    sumDmMinus[0] = sdm1 - sdm1 / AdxPeriod + dmMinus[0];
                }

                double sTr0 = sumTr[0];
                diPlusSeries[0]  = 100 * (sTr0.ApproxCompare(0) == 0 ? 0 : sumDmPlus[0]  / sTr0);
                diMinusSeries[0] = 100 * (sTr0.ApproxCompare(0) == 0 ? 0 : sumDmMinus[0] / sTr0);
            }

            if (CurrentBar < Math.Max(AdxPeriod, AtrPeriod))
                return;

            bool adxStrong = adxIndicator[0] > LevelRange;
            bool crossUp   = diPlusSeries[1] <= diMinusSeries[1] && diPlusSeries[0] > diMinusSeries[0];
            bool crossDown = diMinusSeries[1] <= diPlusSeries[1] && diMinusSeries[0] > diPlusSeries[0];

            // reset trailing anchors when flat
            if (Position.MarketPosition == MarketPosition.Flat)
                trailingStopLong = trailingStopShort = double.NaN;

            // ---- Entries (single-leg) ----
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                double riskATR = atrIndicator[0] * AtrMultiplier;

                if (adxStrong && crossUp)
                {
                    double tgt = RT(Close[0] + riskATR * RiskReward);
                    SetProfitTarget(CalculationMode.Price, tgt);

                    if (StopModeSelection == StopMode.AtrStatic || StopModeSelection == StopMode.AtrStep)
                    {
                        double stp = RT(Close[0] - riskATR);
                        SetStopLoss(CalculationMode.Price, stp);
                        CaptureInitialStopForLog(stp, "LONG");
                        trailingStopLong = stp;
                    }

                    EnterLong(Contracts, "Long");
                }
                else if (adxStrong && crossDown)
                {
                    double tgt = RT(Close[0] - riskATR * RiskReward);
                    SetProfitTarget(CalculationMode.Price, tgt);

                    if (StopModeSelection == StopMode.AtrStatic || StopModeSelection == StopMode.AtrStep)
                    {
                        double stp = RT(Close[0] + riskATR);
                        SetStopLoss(CalculationMode.Price, stp);
                        CaptureInitialStopForLog(stp, "SHORT");
                        trailingStopShort = stp;
                    }

                    EnterShort(Contracts, "Short");
                }
            }
            else
            {
                // optional indicator exit
                if (UseStopX)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        if (crossDown || adxIndicator[0] < adxIndicator[1])
                            ExitLong("StopXLong", "Long");
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        if (crossUp || adxIndicator[0] < adxIndicator[1])
                            ExitShort("StopXShort", "Short");
                    }
                }

                // trailing: EMA / BarN
                if (StopModeSelection == StopMode.EmaTrailing || StopModeSelection == StopMode.BarNTrailing)
                {
                    if (StopModeSelection != StopMode.BarNTrailing || CurrentBar >= TrailingNBars)
                    {
                        if (Position.MarketPosition == MarketPosition.Long)
                        {
                            double candidate = (StopModeSelection == StopMode.EmaTrailing)
                                ? emaIndicator[0] - (EmaOffsetTicks * TickSize)
                                : Low[TrailingNBars] - (TrailingOffsetTicks * TickSize);
                            candidate = RT(candidate);
                            trailingStopLong = double.IsNaN(trailingStopLong) ? candidate : Math.Max(trailingStopLong, candidate);
                            ExitLongStopMarket(Position.Quantity, trailingStopLong, "TSL", "Long");
                        }
                        else if (Position.MarketPosition == MarketPosition.Short)
                        {
                            double candidate = (StopModeSelection == StopMode.EmaTrailing)
                                ? emaIndicator[0] + (EmaOffsetTicks * TickSize)
                                : High[TrailingNBars] + (TrailingOffsetTicks * TickSize);
                            candidate = RT(candidate);
                            trailingStopShort = double.IsNaN(trailingStopShort) ? candidate : Math.Min(trailingStopShort, candidate);
                            ExitShortStopMarket(Position.Quantity, trailingStopShort, "TSS", "Short");
                        }
                    }
                }

                // trailing: ATR Step
                if (StopModeSelection == StopMode.AtrStep)
                {
                    double riskATR = atrIndicator[0] * AtrMultiplier;

                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        double avg = Position.AveragePrice;
                        double rOpen = (Close[0] - avg) / Math.Max(riskATR, TickSize);

                        if (rOpen >= Step2ATR)
                        {
                            double bePlus = RT(avg + BreakevenPlusTicks * TickSize);
                            double trail  = RT(Close[0] - atrIndicator[0] * TrailAtrMult);
                            SetStopLoss(CalculationMode.Price, Math.Max(bePlus, trail));
                        }
                        else if (rOpen >= Step1ATR)
                        {
                            double tightened = RT(Math.Min(avg, avg - riskATR * 0.5 * Step1ATR));
                            SetStopLoss(CalculationMode.Price, tightened);
                        }
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        double avg = Position.AveragePrice;
                        double rOpen = (avg - Close[0]) / Math.Max(riskATR, TickSize);

                        if (rOpen >= Step2ATR)
                        {
                            double beMinus = RT(avg - BreakevenPlusTicks * TickSize);
                            double trail   = RT(Close[0] + atrIndicator[0] * TrailAtrMult);
                            SetStopLoss(CalculationMode.Price, Math.Min(beMinus, trail));
                        }
                        else if (rOpen >= Step1ATR)
                        {
                            double tightened = RT(Math.Max(avg, avg + riskATR * 0.5 * Step1ATR));
                            SetStopLoss(CalculationMode.Price, tightened);
                        }
                    }
                }
            }
        }
    }
}
