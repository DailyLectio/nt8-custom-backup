#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class NT8TradeExportAutoWriter : Indicator
    {
        private const string Header =
            "Trade number,Instrument,Account,Strategy,Market pos.,Qty,Entry price,Exit price,Entry time,Exit time,Entry name,Exit name,Profit,Cum. net profit,Commission,MAE,MFE,ETD,Bars,";
        private const string DefaultRegistryPath = @"C:\Users\Valued Customer\NT8_Regimes\accounts_registry.json";
        private const string EmbeddedAccountFilter =
            "Sim1OG-ES-ADX-1A;Sim1OG-ES-ADX-1B;Sim1OG-ES-Momo-1A;Sim1OG-ES-Momo-1B;Sim1OG-ES-Pine-1A;Sim1OG-ES-Pine-1B;Sim1OG-NQ-ADX-1A;Sim1OG-NQ-ADX-1B;Sim1OG-NQ-Momo-1A;Sim1OG-NQ-Momo-1B;Sim1OG-NQ-Pine-1A;Sim1OG-NQ-Pine-1B;SimMomoOG-ES-1A;SimMomoOG-ES-1B;SimMomoOG-NQ-1A;SimMomoOG-NQ-1B;SimV1A-ES-1A;SimV1A-ES-2A;SimV1A-ES-3A;SimV1A-NQ-1A;SimV1A-NQ-2A;SimV1A-NQ-3A;SimV1A-NQ-CompMomo-1A;SimV1A-NQ-CompMomo-1A1C;SimV1A-NQ-CompMomo-1B;SimV1A-NQ-KalmanFader-1A;SimV1A-NQ-KalmanFader-1B;SimV1A-NQ-KalmanFader-1C;SimV1A-NQ-VolFader-1A;SimV1A-NQ-VolFader-1B;SimV1A-NQ-VolFader-1C;SimV3C-ES-1A;SimV3C-ES-2A;SimV3C-ES-3A;SimV3C-ES-4A;SimV3C-ES-5A;SimV3C-NQ-1A;SimV3C-NQ-1B;SimV3C-NQ-1C;SimV3C-NQ-2A;SimV3C-NQ-2B;SimV3C-NQ-2C;SimV3C-NQ-2D;SimV3C-NQ-3A;SimV3C-NQ-3B;SimV3C-NQ-4A;SimV3C-NQ-4B;SimV3C-NQ-5A;SimV3C-NQ-5B;SimV3D-ES-1A;SimV3D-ES-1B;SimV3D-ES-1C;SimV3D-ES-1D;SimV3D-ES-2A;SimV3D-ES-2B;SimV3D-ES-2C;SimV3D-ES-2D;SimV3D-ES-3A;SimV3D-ES-3B;SimV3D-ES-3C;SimV3D-ES-3D;SimV3D-ES-4A;SimV3D-ES-4B;SimV3D-ES-4C;SimV3D-ES-4D;SimV3D-ES-5A;SimV3D-ES-5B;SimV3D-ES-5C;SimV3D-ES-5D;SimV3D-NQ-1A;SimV3D-NQ-1B;SimV3D-NQ-1C;SimV3D-NQ-1D;SimV3D-NQ-2A;SimV3D-NQ-2B;SimV3D-NQ-2C;SimV3D-NQ-2D;SimV3D-NQ-3A;SimV3D-NQ-3B;SimV3D-NQ-3C;SimV3D-NQ-3D;SimV3D-NQ-4A;SimV3D-NQ-4B;SimV3D-NQ-4C;SimV3D-NQ-4D;SimV3D-NQ-5A;SimV3D-NQ-5B;SimV3D-NQ-5C;SimV3D-NQ-5D";

        private DateTime lastWriteUtc = DateTime.MinValue;
        private bool subscribed;

        [NinjaScriptProperty]
        [Display(Name = "Output Path", Order = 1, GroupName = "EOD Export")]
        public string OutputPath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Account Filter", Order = 2, GroupName = "EOD Export")]
        public string AccountFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Registry Account Filter", Order = 3, GroupName = "EOD Export")]
        public bool UseRegistryAccountFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Registry Path", Order = 4, GroupName = "EOD Export")]
        public string RegistryPath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Refresh Seconds", Order = 5, GroupName = "EOD Export")]
        public int RefreshSeconds { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Writes today's NT8 Trade Performance-style tradeexport.csv automatically for the NT8_Regimes EOD batch.";
                Name = "NT8TradeExportAutoWriter";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = false;
                IsSuspendedWhileInactive = false;
                OutputPath = @"C:\Users\Valued Customer\Downloads\tradeexport.csv";
                RegistryPath = DefaultRegistryPath;
                UseRegistryAccountFilter = true;
                RefreshSeconds = 30;
                AccountFilter = "";
            }
            else if (State == State.DataLoaded)
            {
                SubscribeAccounts();
                WriteExport();
            }
            else if (State == State.Terminated)
            {
                UnsubscribeAccounts();
            }
        }

        protected override void OnBarUpdate()
        {
            if ((DateTime.UtcNow - lastWriteUtc).TotalSeconds >= Math.Max(5, RefreshSeconds))
                WriteExport();
        }

        private void SubscribeAccounts()
        {
            if (subscribed)
                return;

            lock (Account.All)
            {
                foreach (Account account in Account.All)
                    account.ExecutionUpdate += OnAccountExecutionUpdate;
            }
            subscribed = true;
        }

        private void UnsubscribeAccounts()
        {
            if (!subscribed)
                return;

            lock (Account.All)
            {
                foreach (Account account in Account.All)
                    account.ExecutionUpdate -= OnAccountExecutionUpdate;
            }
            subscribed = false;
        }

        private void OnAccountExecutionUpdate(object sender, Cbi.ExecutionEventArgs e)
        {
            if (e == null || e.Execution == null || e.Operation != Cbi.Operation.Add)
                return;

            WriteExport();
        }

        private void WriteExport()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(OutputPath))
                    return;

                string dir = Path.GetDirectoryName(OutputPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                HashSet<string> allowed = BuildAllowedAccounts();
                List<TradeRow> rows = BuildTodayRows(allowed);
                rows.Sort((a, b) =>
                {
                    int c = a.EntryTime.CompareTo(b.EntryTime);
                    if (c != 0) return c;
                    c = string.Compare(a.Account, b.Account, StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return string.Compare(a.Instrument, b.Instrument, StringComparison.OrdinalIgnoreCase);
                });

                double running = 0;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(Header);
                for (int i = 0; i < rows.Count; i++)
                {
                    running += rows[i].Profit;
                    sb.AppendLine(ToCsvLine(i + 1, rows[i], running));
                }

                string temp = OutputPath + ".tmp";
                File.WriteAllText(temp, sb.ToString(), Encoding.UTF8);
                ReplaceFile(temp, OutputPath);
                lastWriteUtc = DateTime.UtcNow;
                WriteStatus("OK rows=" + rows.Count + " accounts=" + allowed.Count + " updated=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                WriteStatus("ERROR " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + ex.Message);
                Print("NT8TradeExportAutoWriter error: " + ex.Message);
            }
        }

        private HashSet<string> BuildAllowedAccounts()
        {
            HashSet<string> accounts = ParseAccountFilter(AccountFilter);
            foreach (string account in ParseAccountFilter(EmbeddedAccountFilter))
                accounts.Add(account);

            string registry = string.IsNullOrWhiteSpace(RegistryPath)
                ? DefaultRegistryPath
                : RegistryPath;

            foreach (string account in LoadRegistryAccounts(registry))
                accounts.Add(account);

            return accounts;
        }

        private List<TradeRow> BuildTodayRows(HashSet<string> allowed)
        {
            DateTime today = DateTime.Today;
            List<TradeRow> rows = new List<TradeRow>();

            lock (Account.All)
            {
                foreach (Account account in Account.All)
                {
                    string accountName = AccountName(account);
                    if (allowed.Count > 0 && !allowed.Contains(accountName))
                        continue;

                    List<Cbi.Execution> todaysExecutions;
                    lock (account.Executions)
                    {
                        todaysExecutions = account.Executions
                            .Where(e => e != null && e.Time.Date == today)
                            .ToList();
                    }

                    foreach (var byInstrument in todaysExecutions.GroupBy(e => e.Instrument == null ? "" : e.Instrument.FullName))
                    {
                        List<Cbi.Execution> executions = byInstrument.OrderBy(e => e.Time).ToList();
                        if (executions.Count == 0)
                            continue;

                        Cbi.TradeCollection trades = Cbi.SystemPerformance.Calculate(executions).AllTrades;
                        foreach (Cbi.Trade trade in trades)
                        {
                            if (trade == null || trade.Entry == null || trade.Exit == null)
                                continue;
                            if (trade.Exit.Time.Date != today)
                                continue;

                            rows.Add(new TradeRow
                            {
                                Instrument = trade.Entry.Instrument == null ? byInstrument.Key : trade.Entry.Instrument.FullName,
                                Account = accountName,
                                Direction = InferDirection(trade),
                                Quantity = trade.Quantity,
                                EntryPrice = trade.Entry.Price,
                                ExitPrice = trade.Exit.Price,
                                EntryTime = trade.Entry.Time,
                                ExitTime = trade.Exit.Time,
                                EntryName = OrderName(trade.Entry),
                                ExitName = OrderName(trade.Exit),
                                Profit = trade.ProfitCurrency,
                                Commission = trade.Commission,
                            });
                        }
                    }
                }
            }

            return rows;
        }

        private static HashSet<string> ParseAccountFilter(string filter)
        {
            HashSet<string> accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(filter))
                return accounts;

            foreach (string raw in filter.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string account = raw.Trim();
                if (account.Length > 0)
                    accounts.Add(account);
            }
            return accounts;
        }

        private static IEnumerable<string> LoadRegistryAccounts(string path)
        {
            List<string> accounts = new List<string>();
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return accounts;

                string text = File.ReadAllText(path);
                MatchCollection matches = Regex.Matches(text, "\"(?<account>Sim[^\"]+)\"\\s*:");
                foreach (Match match in matches)
                {
                    string account = match.Groups["account"].Value.Trim();
                    if (account.Length > 0 && !accounts.Contains(account, StringComparer.OrdinalIgnoreCase))
                        accounts.Add(account);
                }
            }
            catch
            {
            }
            return accounts;
        }

        private static void ReplaceFile(string temp, string target)
        {
            if (File.Exists(target))
            {
                try
                {
                    File.Replace(temp, target, null);
                    return;
                }
                catch
                {
                    File.Copy(temp, target, true);
                    File.Delete(temp);
                    return;
                }
            }

            File.Move(temp, target);
        }

        private void WriteStatus(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(OutputPath))
                    return;
                File.WriteAllText(OutputPath + ".status.txt", message + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string AccountName(Account account)
        {
            if (account == null)
                return "";
            return string.IsNullOrWhiteSpace(account.DisplayName) ? account.Name : account.DisplayName;
        }

        private static string InferDirection(Cbi.Trade trade)
        {
            if (trade.Entry != null && trade.Entry.Order != null)
            {
                Cbi.OrderAction action = trade.Entry.Order.OrderAction;
                if (action == Cbi.OrderAction.SellShort)
                    return "Short";
                if (action == Cbi.OrderAction.Buy)
                    return "Long";
            }

            return trade.Exit.Price >= trade.Entry.Price ? "Long" : "Short";
        }

        private static string OrderName(Cbi.Execution execution)
        {
            if (execution == null || execution.Order == null)
                return "";
            return execution.Order.Name ?? "";
        }

        private static string ToCsvLine(int tradeNumber, TradeRow row, double running)
        {
            return string.Join(",", new[]
            {
                tradeNumber.ToString(CultureInfo.InvariantCulture),
                Csv(row.Instrument),
                Csv(row.Account),
                "",
                Csv(row.Direction),
                row.Quantity.ToString(CultureInfo.InvariantCulture),
                Num(row.EntryPrice),
                Num(row.ExitPrice),
                Csv(row.EntryTime.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture)),
                Csv(row.ExitTime.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture)),
                Csv(row.EntryName),
                Csv(row.ExitName),
                Money(row.Profit),
                Money(running),
                Money(row.Commission),
                "",
                "",
                "",
                "",
                ""
            });
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\r") || value.Contains("\n"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static string Num(double value)
        {
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private static string Money(double value)
        {
            string text = Math.Abs(value).ToString("C2", CultureInfo.GetCultureInfo("en-US"));
            return value < 0 ? "(" + text + ")" : text;
        }

        private class TradeRow
        {
            public string Instrument;
            public string Account;
            public string Direction;
            public int Quantity;
            public double EntryPrice;
            public double ExitPrice;
            public DateTime EntryTime;
            public DateTime ExitTime;
            public string EntryName;
            public string ExitName;
            public double Profit;
            public double Commission;
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private NT8TradeExportAutoWriter[] cacheNT8TradeExportAutoWriter;
		public NT8TradeExportAutoWriter NT8TradeExportAutoWriter(string outputPath, string accountFilter, int refreshSeconds)
		{
			return NT8TradeExportAutoWriter(Input, outputPath, accountFilter, refreshSeconds);
		}

		public NT8TradeExportAutoWriter NT8TradeExportAutoWriter(ISeries<double> input, string outputPath, string accountFilter, int refreshSeconds)
		{
			if (cacheNT8TradeExportAutoWriter != null)
				for (int idx = 0; idx < cacheNT8TradeExportAutoWriter.Length; idx++)
					if (cacheNT8TradeExportAutoWriter[idx] != null && cacheNT8TradeExportAutoWriter[idx].OutputPath == outputPath && cacheNT8TradeExportAutoWriter[idx].AccountFilter == accountFilter && cacheNT8TradeExportAutoWriter[idx].RefreshSeconds == refreshSeconds && cacheNT8TradeExportAutoWriter[idx].EqualsInput(input))
						return cacheNT8TradeExportAutoWriter[idx];
			return CacheIndicator<NT8TradeExportAutoWriter>(new NT8TradeExportAutoWriter(){ OutputPath = outputPath, AccountFilter = accountFilter, RefreshSeconds = refreshSeconds }, input, ref cacheNT8TradeExportAutoWriter);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.NT8TradeExportAutoWriter NT8TradeExportAutoWriter(string outputPath, string accountFilter, int refreshSeconds)
		{
			return indicator.NT8TradeExportAutoWriter(Input, outputPath, accountFilter, refreshSeconds);
		}

		public Indicators.NT8TradeExportAutoWriter NT8TradeExportAutoWriter(ISeries<double> input , string outputPath, string accountFilter, int refreshSeconds)
		{
			return indicator.NT8TradeExportAutoWriter(input, outputPath, accountFilter, refreshSeconds);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.NT8TradeExportAutoWriter NT8TradeExportAutoWriter(string outputPath, string accountFilter, int refreshSeconds)
		{
			return indicator.NT8TradeExportAutoWriter(Input, outputPath, accountFilter, refreshSeconds);
		}

		public Indicators.NT8TradeExportAutoWriter NT8TradeExportAutoWriter(ISeries<double> input , string outputPath, string accountFilter, int refreshSeconds)
		{
			return indicator.NT8TradeExportAutoWriter(input, outputPath, accountFilter, refreshSeconds);
		}
	}
}

#endregion
