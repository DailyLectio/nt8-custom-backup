// TradeLogExporter_V1AB.cs
// V1A / V1B NT8 Trade Log Exporter
//
// Add one instance to each V1A/V1B strategy chart or tab.
// Set:
//   BotName           = strategy/bot label you want in the CSV
//   ModelVersion      = V1A or V1B
//   AccountNameFilter = exact NT8 account name for that chart
//   OrderSignalFilter = optional substring found in that strategy's order names
//
// Output:
//   C:\Users\Valued Customer\NT8_Regimes\V1A\TradeLog\V1A_TradeLog.csv
//   C:\Users\Valued Customer\NT8_Regimes\V1B\TradeLog\V1B_TradeLog.csv

#region Using declarations
using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class TradeLogExporter_V1AB : Indicator
    {
        [NinjaScriptProperty]
        [Display(Name = "Bot Name", Order = 1, GroupName = "V1A/V1B Trade Log")]
        public string BotName { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Model Version", Description = "V1A or V1B", Order = 2, GroupName = "V1A/V1B Trade Log")]
        public string ModelVersion { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Account Name Filter", Description = "Exact NT8 account name for this chart/tab", Order = 3, GroupName = "V1A/V1B Trade Log")]
        public string AccountNameFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Order Signal Filter", Description = "Optional order-name substring for this strategy", Order = 4, GroupName = "V1A/V1B Trade Log")]
        public string OrderSignalFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Notes Tag", Description = "Optional static tag, e.g. FP:ON Bias:D", Order = 5, GroupName = "V1A/V1B Trade Log")]
        public string NotesTag { get; set; }

        private const string ROOT_PATH = @"C:\Users\Valued Customer\NT8_Regimes\";
        private const string COLUMN_HEADER =
            "trade_date,entry_time,exit_time,symbol,bot_name,model_version," +
            "direction,contracts,entry_price,exit_price," +
            "gross_pnl,net_pnl,ticks,win_loss," +
            "exit_reason," +
            "entry_regime,entry_macro,entry_hmm,entry_direction," +
            "entry_confidence,entry_conflict,entry_phase,entry_state_age," +
            "exit_regime,exit_macro,exit_hmm,exit_confidence," +
            "entry_size_pct,entry_velocity,entry_reason_code," +
            "account,instrument,notes";

        private string leaderSymbol = "";
        private readonly HashSet<string> seenExecutionIds = new HashSet<string>();

        private bool hasPendingEntry = false;
        private string pendingEntryTime = "";
        private string pendingDirection = "";
        private int pendingContracts = 0;
        private double pendingEntryPrice = 0;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "V1A/V1B: Writes one CSV row per completed trade.";
                Name = "Trade Log Exporter (V1A V1B)";
                BotName = "UNMAPPED";
                ModelVersion = "V1A";
                AccountNameFilter = "";
                OrderSignalFilter = "";
                NotesTag = "";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
            }
            else if (State == State.DataLoaded)
            {
                leaderSymbol = GetLeaderSymbol(Instrument.MasterInstrument.Name.ToUpper());
                string logPath = GetLogPath();
                EnsureHeader(logPath);

                Print("TradeLogExporter_V1AB [" + leaderSymbol + " / " + BotName +
                      " / " + CleanModelVersion() + "]: Ready. Log: " + logPath);

                lock (Account.All)
                {
                    foreach (var account in Account.All)
                        account.ExecutionUpdate += OnAccountExecutionUpdate;
                }
            }
            else if (State == State.Terminated)
            {
                lock (Account.All)
                {
                    foreach (var account in Account.All)
                        account.ExecutionUpdate -= OnAccountExecutionUpdate;
                }
            }
        }

        private void OnAccountExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            Execution execution = e.Execution;
            if (execution == null)
                return;

            string accountName = execution.Account == null ? "Unknown" : execution.Account.Name;
            if (!string.IsNullOrWhiteSpace(AccountNameFilter) &&
                !accountName.Equals(AccountNameFilter, StringComparison.OrdinalIgnoreCase))
                return;

            string execSymbol = execution.Instrument.MasterInstrument.Name.ToUpper();
            if (execSymbol != leaderSymbol && execSymbol != Instrument.MasterInstrument.Name.ToUpper())
                return;

            try
            {
                string execKey = execution.ExecutionId;
                if (string.IsNullOrEmpty(execKey))
                    execKey = execution.OrderId + "|" + e.Time.ToString("O") + "|" + e.Price.ToString("F8");
                if (seenExecutionIds.Contains(execKey))
                    return;
                seenExecutionIds.Add(execKey);

                string orderName = execution.Order == null ? "" : execution.Order.Name;
                string fromEntrySignal = execution.Order == null ? "" : execution.Order.FromEntrySignal;
                if (!IsOrderForThisExporter(orderName, fromEntrySignal))
                    return;

                OrderAction orderAction = execution.Order == null ? OrderAction.Buy : execution.Order.OrderAction;
                bool isEntry = execution.IsEntry ||
                               orderName.IndexOf("Entry", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               orderAction == OrderAction.Buy ||
                               orderAction == OrderAction.SellShort;
                bool isExit = execution.IsExit || !isEntry;

                if (isEntry)
                {
                    pendingEntryTime = e.Time.ToString("yyyy-MM-dd HH:mm:ss");
                    pendingDirection = orderAction == OrderAction.SellShort ? "SHORT" : "LONG";
                    pendingContracts = e.Quantity;
                    pendingEntryPrice = e.Price;
                    hasPendingEntry = true;
                }

                if (isExit && hasPendingEntry)
                {
                    WriteTradeRow(execution, e.Price, e.Quantity, e.Time, accountName);
                    hasPendingEntry = false;
                }
            }
            catch (Exception ex)
            {
                Print("TradeLogExporter_V1AB Error: " + ex.Message);
            }
        }

        private void WriteTradeRow(Execution execution, double exitPrice, int qty, DateTime exitTime, string accountName)
        {
            double tickSize = TickSize;
            double tickValue = Instrument.MasterInstrument.PointValue * tickSize;
            double priceDiff = pendingDirection == "LONG"
                ? exitPrice - pendingEntryPrice
                : pendingEntryPrice - exitPrice;
            double grossPnl = priceDiff / tickSize * tickValue * pendingContracts;
            double netPnl = grossPnl;
            double ticks = priceDiff / tickSize;
            string winLoss = grossPnl > 0 ? "WIN" : (grossPnl < 0 ? "LOSS" : "SCRATCH");
            string exitReason = InferExitReason(execution.Order == null ? "" : execution.Order.Name);

            string line = string.Join(",",
                exitTime.ToString("yyyy-MM-dd"),
                pendingEntryTime,
                exitTime.ToString("yyyy-MM-dd HH:mm:ss"),
                leaderSymbol,
                SafeCsv(BotName),
                CleanModelVersion(),
                pendingDirection,
                pendingContracts.ToString(),
                pendingEntryPrice.ToString("F2"),
                exitPrice.ToString("F2"),
                grossPnl.ToString("F2"),
                netPnl.ToString("F2"),
                ticks.ToString("F1"),
                winLoss,
                SafeCsv(exitReason),
                "UNAVAILABLE",
                "UNAVAILABLE",
                "UNAVAILABLE",
                "UNAVAILABLE",
                "0",
                "0",
                "UNAVAILABLE",
                "0",
                "UNAVAILABLE",
                "UNAVAILABLE",
                "UNAVAILABLE",
                "0",
                "0",
                "0",
                "UNAVAILABLE",
                SafeCsv(accountName),
                SafeCsv(Instrument.FullName),
                SafeCsv(NotesTag)
            );

            string logPath = GetLogPath();
            EnsureHeader(logPath);
            AppendLine(logPath, line);
        }

        private bool IsOrderForThisExporter(string orderName, string fromEntrySignal)
        {
            string filter = (OrderSignalFilter ?? "").Trim();
            if (filter.Length == 0)
                return true;
            string haystack = ((orderName ?? "") + "|" + (fromEntrySignal ?? "")).ToUpperInvariant();
            return haystack.Contains(filter.ToUpperInvariant());
        }

        private string CleanModelVersion()
        {
            string model = (ModelVersion ?? "").Trim().ToUpperInvariant();
            return model == "V1B" ? "V1B" : "V1A";
        }

        private string GetLogPath()
        {
            string model = CleanModelVersion();
            string dir = Path.Combine(ROOT_PATH, model, "TradeLog");
            return Path.Combine(dir, model + "_TradeLog.csv");
        }

        private void EnsureHeader(string path)
        {
            if (File.Exists(path)) return;
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            using (var sw = new StreamWriter(path, false))
                sw.WriteLine(COLUMN_HEADER);
        }

        private void AppendLine(string path, string line)
        {
            using (var sw = new StreamWriter(path, true))
                sw.WriteLine(line);
        }

        private static string GetLeaderSymbol(string sym)
        {
            if (sym.Contains("MNQ")) return "NQ";
            if (sym.Contains("MES")) return "ES";
            if (sym.Contains("NQ")) return "NQ";
            if (sym.Contains("ES")) return "ES";
            return sym;
        }

        private static string InferExitReason(string orderName)
        {
            if (string.IsNullOrEmpty(orderName)) return "UNKNOWN";
            string n = orderName.ToUpperInvariant();
            if (n.Contains("PROFIT") || n.Contains("TARGET")) return "TARGET_HIT";
            if (n.Contains("STOP")) return "STOP_HIT";
            if (n.Contains("TRAIL")) return "TRAIL_STOP";
            if (n.Contains("SESSION") || n.Contains("CLOSE") || n.Contains("EOD")) return "SESSION_CLOSE";
            if (n.Contains("SLOPE")) return "SLOPE_EXIT";
            if (n.Contains("BIAS")) return "BIAS_EXIT";
            return orderName;
        }

        private static string SafeCsv(string value)
        {
            string s = value ?? "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TradeLogExporter_V1AB[] cacheTradeLogExporter_V1AB;
		public TradeLogExporter_V1AB TradeLogExporter_V1AB(string botName, string modelVersion, string accountNameFilter, string orderSignalFilter, string notesTag)
		{
			return TradeLogExporter_V1AB(Input, botName, modelVersion, accountNameFilter, orderSignalFilter, notesTag);
		}

		public TradeLogExporter_V1AB TradeLogExporter_V1AB(ISeries<double> input, string botName, string modelVersion, string accountNameFilter, string orderSignalFilter, string notesTag)
		{
			if (cacheTradeLogExporter_V1AB != null)
				for (int idx = 0; idx < cacheTradeLogExporter_V1AB.Length; idx++)
					if (cacheTradeLogExporter_V1AB[idx] != null && cacheTradeLogExporter_V1AB[idx].BotName == botName && cacheTradeLogExporter_V1AB[idx].ModelVersion == modelVersion && cacheTradeLogExporter_V1AB[idx].AccountNameFilter == accountNameFilter && cacheTradeLogExporter_V1AB[idx].OrderSignalFilter == orderSignalFilter && cacheTradeLogExporter_V1AB[idx].NotesTag == notesTag && cacheTradeLogExporter_V1AB[idx].EqualsInput(input))
						return cacheTradeLogExporter_V1AB[idx];
			return CacheIndicator<TradeLogExporter_V1AB>(new TradeLogExporter_V1AB(){ BotName = botName, ModelVersion = modelVersion, AccountNameFilter = accountNameFilter, OrderSignalFilter = orderSignalFilter, NotesTag = notesTag }, input, ref cacheTradeLogExporter_V1AB);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TradeLogExporter_V1AB TradeLogExporter_V1AB(string botName, string modelVersion, string accountNameFilter, string orderSignalFilter, string notesTag)
		{
			return indicator.TradeLogExporter_V1AB(Input, botName, modelVersion, accountNameFilter, orderSignalFilter, notesTag);
		}

		public Indicators.TradeLogExporter_V1AB TradeLogExporter_V1AB(ISeries<double> input , string botName, string modelVersion, string accountNameFilter, string orderSignalFilter, string notesTag)
		{
			return indicator.TradeLogExporter_V1AB(input, botName, modelVersion, accountNameFilter, orderSignalFilter, notesTag);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TradeLogExporter_V1AB TradeLogExporter_V1AB(string botName, string modelVersion, string accountNameFilter, string orderSignalFilter, string notesTag)
		{
			return indicator.TradeLogExporter_V1AB(Input, botName, modelVersion, accountNameFilter, orderSignalFilter, notesTag);
		}

		public Indicators.TradeLogExporter_V1AB TradeLogExporter_V1AB(ISeries<double> input , string botName, string modelVersion, string accountNameFilter, string orderSignalFilter, string notesTag)
		{
			return indicator.TradeLogExporter_V1AB(input, botName, modelVersion, accountNameFilter, orderSignalFilter, notesTag);
		}
	}
}

#endregion
