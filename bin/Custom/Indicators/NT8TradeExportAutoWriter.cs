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
        // Exit reason / Is code exit appended 2026-05-18: canonical exit-label taxonomy
        // is normalised here at the exporter so downstream reporting never sees the
        // duplicate raw spellings (WOBBLEEJECT/WOBBLE_EJECT, STOPX/STOP_HIT).
        private const string Header =
            "Trade number,Instrument,Account,Strategy,Market pos.,Qty,Entry price,Exit price,Entry time,Exit time,Entry name,Exit name,Profit,Cum. net profit,Commission,MAE,MFE,ETD,Bars,Exit reason,Is code exit";
        private const string DefaultRegistryPath = @"C:\Users\Valued Customer\NT8_Regimes\accounts_registry.json";
        private const string EmbeddedAccountFilter =
            "Sim1OG-ES-ADX-1A;Sim1OG-ES-ADX-1B;Sim1OG-ES-Momo-1A;Sim1OG-ES-Momo-1B;Sim1OG-ES-Pine-1A;Sim1OG-ES-Pine-1B;Sim1OG-NQ-ADX-1A;Sim1OG-NQ-ADX-1B;Sim1OG-NQ-Momo-1A;Sim1OG-NQ-Momo-1B;Sim1OG-NQ-Pine-1A;Sim1OG-NQ-Pine-1B;SimMomoOG-ES-1A;SimMomoOG-ES-1B;SimMomoOG-NQ-1A;SimMomoOG-NQ-1B;SimV1A-ES-1A;SimV1A-ES-2A;SimV1A-ES-3A;SimV1A-NQ-1A;SimV1A-NQ-2A;SimV1A-NQ-3A;SimV1A-NQ-CompMomo-1A;SimV1A-NQ-CompMomo-1A1C;SimV1A-NQ-CompMomo-1B;SimV1A-NQ-KalmanFader-1A;SimV1A-NQ-KalmanFader-1B;SimV1A-NQ-KalmanFader-1C;SimV1A-NQ-VolFader-1A;SimV1A-NQ-VolFader-1B;SimV1A-NQ-VolFader-1C;SimV3C-ES-1A;SimV3C-ES-2A;SimV3C-ES-3A;SimV3C-ES-4A;SimV3C-ES-5A;SimV3C-NQ-1A;SimV3C-NQ-1B;SimV3C-NQ-1C;SimV3C-NQ-2A;SimV3C-NQ-2B;SimV3C-NQ-2C;SimV3C-NQ-2D;SimV3C-NQ-3A;SimV3C-NQ-3B;SimV3C-NQ-4A;SimV3C-NQ-4B;SimV3C-NQ-5A;SimV3C-NQ-5B;SimV3D-ES-1A;SimV3D-ES-1B;SimV3D-ES-1C;SimV3D-ES-1D;SimV3D-ES-2A;SimV3D-ES-2B;SimV3D-ES-2C;SimV3D-ES-2D;SimV3D-ES-3A;SimV3D-ES-3B;SimV3D-ES-3C;SimV3D-ES-3D;SimV3D-ES-4A;SimV3D-ES-4B;SimV3D-ES-4C;SimV3D-ES-4D;SimV3D-ES-5A;SimV3D-ES-5B;SimV3D-ES-5C;SimV3D-ES-5D;SimV3D-NQ-1A;SimV3D-NQ-1B;SimV3D-NQ-1C;SimV3D-NQ-1D;SimV3D-NQ-2A;SimV3D-NQ-2B;SimV3D-NQ-2C;SimV3D-NQ-2D;SimV3D-NQ-3A;SimV3D-NQ-3B;SimV3D-NQ-3C;SimV3D-NQ-3D;SimV3D-NQ-4A;SimV3D-NQ-4B;SimV3D-NQ-4C;SimV3D-NQ-4D;SimV3D-NQ-5A;SimV3D-NQ-5B;SimV3D-NQ-5C;SimV3D-NQ-5D";

        private DateTime lastWriteUtc = DateTime.MinValue;
        private bool subscribed;
        // account -> strategy name, loaded from the registry each WriteExport so
        // the Strategy column is stamped at source (no more NT_STRATEGY_BLANK).
        private Dictionary<string, string> strategyByAccount = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private int lastExecutionCount;
        private int lastSystemTradeCount;
        private int lastFallbackTradeCount;

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
        [Display(Name = "Use Embedded Account Filter", Order = 4, GroupName = "EOD Export")]
        public bool UseEmbeddedAccountFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Registry Path", Order = 5, GroupName = "EOD Export")]
        public string RegistryPath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Refresh Seconds", Order = 6, GroupName = "EOD Export")]
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
                // Writes the dated file the NT8_Regimes EOD pipeline expects.
                // {date} is substituted with today's yyyy-MM-dd at write time
                // (see ResolveOutputPath). Both nt_trade_export_reconcile.py and
                // regime_daily_join.py read Exports\DayTrades\trades_<date>.csv.
                OutputPath = @"C:\Users\Valued Customer\NT8_Regimes\Exports\DayTrades\trades_{date}.csv";
                RegistryPath = DefaultRegistryPath;
                UseRegistryAccountFilter = true;
                UseEmbeddedAccountFilter = true;
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

        // Substitutes {date} in OutputPath with today's date (yyyy-MM-dd) so the
        // exporter writes the dated file the EOD pipeline expects, e.g.
        // ...\Exports\DayTrades\trades_2026-05-19.csv. Added 2026-05-19.
        private string ResolveOutputPath()
        {
            string p = OutputPath ?? "";
            return p.Replace("{date}", DateTime.Today.ToString("yyyy-MM-dd"));
        }

        private void WriteExport()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(OutputPath))
                    return;

                string outputPath = ResolveOutputPath();
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                HashSet<string> allowed = BuildAllowedAccounts();
                strategyByAccount = LoadRegistryStrategies(
                    string.IsNullOrWhiteSpace(RegistryPath) ? DefaultRegistryPath : RegistryPath);
                List<TradeRow> rows = BuildTodayRows(allowed);
                foreach (TradeRow tr in rows)
                    tr.Strategy = LookupStrategy(tr.Account);
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

                string temp = outputPath + ".tmp";
                File.WriteAllText(temp, sb.ToString(), Encoding.UTF8);
                ReplaceFile(temp, outputPath);
                lastWriteUtc = DateTime.UtcNow;
                WriteStatus(
                    "OK rows=" + rows.Count +
                    " accounts=" + allowed.Count +
                    " executions=" + lastExecutionCount +
                    " system_trades=" + lastSystemTradeCount +
                    " fallback_trades=" + lastFallbackTradeCount +
                    " updated=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
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
            if (UseEmbeddedAccountFilter)
                foreach (string account in ParseAccountFilter(EmbeddedAccountFilter))
                    accounts.Add(account);

            string registry = string.IsNullOrWhiteSpace(RegistryPath)
                ? DefaultRegistryPath
                : RegistryPath;

            if (UseRegistryAccountFilter)
                foreach (string account in LoadRegistryAccounts(registry))
                    accounts.Add(account);

            return accounts;
        }

        private List<TradeRow> BuildTodayRows(HashSet<string> allowed)
        {
            DateTime today = DateTime.Today;
            List<TradeRow> rows = new List<TradeRow>();
            lastExecutionCount = 0;
            lastSystemTradeCount = 0;
            lastFallbackTradeCount = 0;

            lock (Account.All)
            {
                foreach (Account account in Account.All)
                {
                    string accountName = AccountName(account);
                    if (!IsAllowedAccount(account, allowed))
                        continue;

                    List<Cbi.Execution> todaysExecutions;
                    lock (account.Executions)
                    {
                        todaysExecutions = account.Executions
                            .Where(e => e != null && e.Time.Date == today)
                            .ToList();
                    }
                    lastExecutionCount += todaysExecutions.Count;

                    foreach (var byInstrument in todaysExecutions.GroupBy(e => e.Instrument == null ? "" : e.Instrument.FullName))
                    {
                        List<Cbi.Execution> executions = byInstrument.OrderBy(e => e.Time).ToList();
                        if (executions.Count == 0)
                            continue;

                        Cbi.TradeCollection trades = Cbi.SystemPerformance.Calculate(executions).AllTrades;
                        int rowsBeforeSystemPerformance = rows.Count;
                        if (trades != null)
                        {
                            foreach (Cbi.Trade trade in trades)
                            {
                                if (trade == null || trade.Entry == null || trade.Exit == null)
                                    continue;
                                if (trade.Exit.Time.Date != today)
                                    continue;

                                lastSystemTradeCount++;
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

                        if (rows.Count == rowsBeforeSystemPerformance)
                        {
                            List<TradeRow> fallbackRows = BuildFallbackRows(accountName, byInstrument.Key, executions, today);
                            lastFallbackTradeCount += fallbackRows.Count;
                            rows.AddRange(fallbackRows);
                        }
                    }
                }
            }

            return rows;
        }

        private static bool IsAllowedAccount(Account account, HashSet<string> allowed)
        {
            if (allowed == null || allowed.Count == 0)
                return true;
            if (account == null)
                return false;
            if (!string.IsNullOrWhiteSpace(account.Name) && allowed.Contains(account.Name))
                return true;
            if (!string.IsNullOrWhiteSpace(account.DisplayName) && allowed.Contains(account.DisplayName))
                return true;
            return false;
        }

        private static List<TradeRow> BuildFallbackRows(string accountName, string instrumentName, List<Cbi.Execution> executions, DateTime today)
        {
            List<TradeRow> rows = new List<TradeRow>();
            List<OpenLot> openLots = new List<OpenLot>();

            foreach (Cbi.Execution execution in executions)
            {
                if (execution == null || execution.Order == null || execution.Instrument == null)
                    continue;
                if (execution.Time.Date != today)
                    continue;

                int quantity = execution.Quantity <= 0 ? 1 : execution.Quantity;
                Cbi.OrderAction action = execution.Order.OrderAction;
                bool entry = action == Cbi.OrderAction.Buy || action == Cbi.OrderAction.SellShort;
                bool exit = action == Cbi.OrderAction.Sell || action == Cbi.OrderAction.BuyToCover;

                if (entry)
                {
                    openLots.Add(new OpenLot
                    {
                        Direction = action == Cbi.OrderAction.SellShort ? "Short" : "Long",
                        Quantity = quantity,
                        Price = execution.Price,
                        Time = execution.Time,
                        EntryName = OrderName(execution),
                        PointValue = execution.Instrument.MasterInstrument == null ? 1.0 : execution.Instrument.MasterInstrument.PointValue,
                    });
                    continue;
                }

                if (!exit)
                    continue;

                int remaining = quantity;
                for (int i = 0; i < openLots.Count && remaining > 0;)
                {
                    OpenLot lot = openLots[i];
                    if ((action == Cbi.OrderAction.Sell && lot.Direction != "Long") ||
                        (action == Cbi.OrderAction.BuyToCover && lot.Direction != "Short"))
                    {
                        i++;
                        continue;
                    }

                    int matched = Math.Min(remaining, lot.Quantity);
                    double profit = lot.Direction == "Long"
                        ? (execution.Price - lot.Price) * matched * lot.PointValue
                        : (lot.Price - execution.Price) * matched * lot.PointValue;

                    rows.Add(new TradeRow
                    {
                        Instrument = execution.Instrument.FullName ?? instrumentName,
                        Account = accountName,
                        Direction = lot.Direction,
                        Quantity = matched,
                        EntryPrice = lot.Price,
                        ExitPrice = execution.Price,
                        EntryTime = lot.Time,
                        ExitTime = execution.Time,
                        EntryName = lot.EntryName,
                        ExitName = OrderName(execution),
                        Profit = profit,
                        Commission = 0,
                    });

                    lot.Quantity -= matched;
                    remaining -= matched;
                    if (lot.Quantity <= 0)
                        openLots.RemoveAt(i);
                    else
                        i++;
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
                MatchCollection matches = Regex.Matches(text, "\"(?<account>(?:Sim|OP)[^\"]+)\"\\s*:");
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

        // Builds an account -> strategy map from accounts_registry.json so the
        // exporter can stamp the Strategy column at source (eliminates the
        // NT_STRATEGY_BLANK data-quality flag). The registry is the single
        // source of truth for taxonomy (dev-log SF-22). Each account block is
        // flat (no nested braces), so a per-block regex is reliable. Aliases
        // are mapped too so NT display-name variants resolve (SF-34).
        private static Dictionary<string, string> LoadRegistryStrategies(string path)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return map;

                string text = File.ReadAllText(path);
                foreach (Match block in Regex.Matches(text,
                    "\"(?<account>(?:Sim|OP)[^\"]+)\"\\s*:\\s*\\{(?<body>[^{}]*)\\}", RegexOptions.Singleline))
                {
                    string account = block.Groups["account"].Value.Trim();
                    string body = block.Groups["body"].Value;
                    Match sm = Regex.Match(body, "\"strategy\"\\s*:\\s*\"(?<strat>[^\"]*)\"");
                    if (account.Length == 0 || !sm.Success)
                        continue;

                    string strat = sm.Groups["strat"].Value.Trim();
                    map[account] = strat;

                    Match am = Regex.Match(body, "\"account_aliases\"\\s*:\\s*\\[(?<aliases>[^\\]]*)\\]", RegexOptions.Singleline);
                    if (am.Success)
                        foreach (Match alias in Regex.Matches(am.Groups["aliases"].Value, "\"(?<a>[^\"]+)\""))
                            map[alias.Groups["a"].Value.Trim()] = strat;
                }
            }
            catch
            {
            }
            return map;
        }

        private string LookupStrategy(string account)
        {
            string s;
            if (account != null && strategyByAccount.TryGetValue(account, out s))
                return s ?? "";
            return "";
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
                File.WriteAllText(ResolveOutputPath() + ".status.txt", message + Environment.NewLine, Encoding.UTF8);
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

        // -------------------------------------------------------------------
        // Canonical exit-label taxonomy.  Added 2026-05-18.
        //
        // The raw NinjaTrader order/signal name varies by strategy and version
        // (WOBBLEEJECT vs WOBBLE_EJECT, STOPX vs STOP_HIT, etc.).  This method
        // is the single normalisation point so every consumer sees one label
        // set.  Rules:
        //   * WOBBLE*                -> WOBBLE_EJECT
        //   * STOP* on a winning trade  -> TRAIL_EXIT   (trailing stop on a runner)
        //   * STOP* on a losing trade   -> STOP_HIT     (protective stop)
        //   * ENVELOPE* / ENVBREAK*  -> ENVELOPE_BREAK_EXIT  (code event, SF-12)
        //   * SESSION / FLATTEN / EOD / MANUAL -> SESSION_CLOSE (code event)
        //   * *CANCEL*               -> STOP_CANCEL_CLOSE (code event)
        // The TRAIL_EXIT split exists so STOP_HIT stops conflating winning
        // trailing-stop exits with losing protective-stop exits.
        // Reverse-signal exits: a stop-and-reverse auto-close inherits the REVERSING
        // entry's signal name (dev-log 2026-05-23 / Exit_Reason_Taxonomy_Coverage_Review).
        // When one of these entry-signal names appears as an EXIT reason, the position
        // was flipped by the opposite-direction entry. Exact-match only (LE/SE etc. are
        // short — Contains would false-positive). Extend when a strategy adds a signal
        // name; a miss just falls through to raw (no regression). Must mirror the same
        // set in regime_daily_join.normalize_exit_reason.
        private static readonly HashSet<string> ReverseSignalNames = new HashSet<string>
        {
            "EXPL1","EXPL2","EXPS1","EXPS2",
            "FADEL1","FADEL2","FADES1","FADES2",
            "FADEBL1","FADEBL2","FADEBS1","FADEBS2","FADEL","FADES",
            "SNIPEL","SNIPES","SNIPEBL","SNIPEBS",
            "ADXDIL","ADXDIS","ADXDICL","ADXDICS",
            "MOMOL","MOMOS",
            "LE","SE"
        };

        private static string NormalizeExitReason(string rawExitName, double profit)
        {
            string n = (rawExitName ?? "").Trim().ToUpperInvariant();
            if (n.Length == 0)                                       return "";
            if (n.Contains("ENVELOPE") || n.Contains("ENVBREAK")
                || n.Contains("TRENDBREAK"))                         return "ENVELOPE_BREAK_EXIT";  // fader fade-invalidation family
            if (n.Contains("CANCEL"))                                return "STOP_CANCEL_CLOSE";
            if (n.Contains("SESSION") || n.Contains("FLATTEN")
                || n.Contains("EOD") || n.Contains("MANUAL"))        return "SESSION_CLOSE";
            if (n.Contains("TARGET") || n.Contains("PROFITTARGET"))  return "TARGET_HIT";
            if (n.Contains("WOBBLE"))                                return "WOBBLE_EJECT";
            if (n.Contains("SLOPE"))                                 return "SLOPE_EXIT";
            if (n.Contains("CIRCUIT") || n.Contains("REVERSAL"))     return "CIRCUIT_BREAKER";  // momentum reversal-against-position family
            if (n.Contains("TRANSITION") || n.Contains("REGIME"))    return "REGIME_EXIT";
            if (n.Contains("ILLIQUID"))                              return "ILLIQUID_EXIT";
            if (n.Contains("CONDEXIT"))                              return "COND_EXIT";
            if (n.Contains("GUARD") || n.Contains("KILL"))           return "GUARD_FLAT";  // protective force-flatten family
            if (n.Contains("TIMEEXIT"))                              return "TIME_EXIT";
            if (n.Contains("STOP"))                                  return profit > 0 ? "TRAIL_EXIT" : "STOP_HIT";
            if (n == "CLOSE")                                        return "CLOSE";
            if (ReverseSignalNames.Contains(n))                      return "REVERSE_EXIT";
            return n;
        }

        // Code events are platform/session artefacts, not strategy decisions.
        // Reporting excludes them from clean P&L (dev-log SF-12).
        // SF-47 (2026-05-22): ENVELOPE_BREAK_EXIT removed — post-BUG-007 it is an
        // intentional protective fade-invalidation exit (a real strategy decision),
        // not a reload/bar-init artefact. Treated as a real exit, like StopX (SF-30).
        private static bool IsCodeExit(string canonicalExitReason)
        {
            return canonicalExitReason == "SESSION_CLOSE"
                || canonicalExitReason == "STOP_CANCEL_CLOSE";
        }

        private static string ToCsvLine(int tradeNumber, TradeRow row, double running)
        {
            string exitReason = NormalizeExitReason(row.ExitName, row.Profit);
            return string.Join(",", new[]
            {
                tradeNumber.ToString(CultureInfo.InvariantCulture),
                Csv(row.Instrument),
                Csv(row.Account),
                Csv(row.Strategy),
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
                Csv(exitReason),
                IsCodeExit(exitReason) ? "1" : "0"
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
            string formatted = value < 0 ? "(" + text + ")" : text;
            // C2 emits thousands-commas ($1,625.00); route through Csv so values
            // >= $1,000 are quoted and don't shift CSV columns (owed since 05-19 reconcile fix).
            return Csv(formatted);
        }

        private class TradeRow
        {
            public string Instrument;
            public string Account;
            public string Strategy;
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

        private class OpenLot
        {
            public string Direction;
            public int Quantity;
            public double Price;
            public DateTime Time;
            public string EntryName;
            public double PointValue;
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private NT8TradeExportAutoWriter[] cacheNT8TradeExportAutoWriter;
		public NT8TradeExportAutoWriter NT8TradeExportAutoWriter(string outputPath, string accountFilter, bool useRegistryAccountFilter, string registryPath, int refreshSeconds)
		{
			return NT8TradeExportAutoWriter(Input, outputPath, accountFilter, useRegistryAccountFilter, registryPath, refreshSeconds);
		}

		public NT8TradeExportAutoWriter NT8TradeExportAutoWriter(ISeries<double> input, string outputPath, string accountFilter, bool useRegistryAccountFilter, string registryPath, int refreshSeconds)
		{
			if (cacheNT8TradeExportAutoWriter != null)
				for (int idx = 0; idx < cacheNT8TradeExportAutoWriter.Length; idx++)
					if (cacheNT8TradeExportAutoWriter[idx] != null && cacheNT8TradeExportAutoWriter[idx].OutputPath == outputPath && cacheNT8TradeExportAutoWriter[idx].AccountFilter == accountFilter && cacheNT8TradeExportAutoWriter[idx].UseRegistryAccountFilter == useRegistryAccountFilter && cacheNT8TradeExportAutoWriter[idx].RegistryPath == registryPath && cacheNT8TradeExportAutoWriter[idx].RefreshSeconds == refreshSeconds && cacheNT8TradeExportAutoWriter[idx].EqualsInput(input))
						return cacheNT8TradeExportAutoWriter[idx];
			return CacheIndicator<NT8TradeExportAutoWriter>(new NT8TradeExportAutoWriter(){ OutputPath = outputPath, AccountFilter = accountFilter, UseRegistryAccountFilter = useRegistryAccountFilter, RegistryPath = registryPath, RefreshSeconds = refreshSeconds }, input, ref cacheNT8TradeExportAutoWriter);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.NT8TradeExportAutoWriter NT8TradeExportAutoWriter(string outputPath, string accountFilter, bool useRegistryAccountFilter, string registryPath, int refreshSeconds)
		{
			return indicator.NT8TradeExportAutoWriter(Input, outputPath, accountFilter, useRegistryAccountFilter, registryPath, refreshSeconds);
		}

		public Indicators.NT8TradeExportAutoWriter NT8TradeExportAutoWriter(ISeries<double> input , string outputPath, string accountFilter, bool useRegistryAccountFilter, string registryPath, int refreshSeconds)
		{
			return indicator.NT8TradeExportAutoWriter(input, outputPath, accountFilter, useRegistryAccountFilter, registryPath, refreshSeconds);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.NT8TradeExportAutoWriter NT8TradeExportAutoWriter(string outputPath, string accountFilter, bool useRegistryAccountFilter, string registryPath, int refreshSeconds)
		{
			return indicator.NT8TradeExportAutoWriter(Input, outputPath, accountFilter, useRegistryAccountFilter, registryPath, refreshSeconds);
		}

		public Indicators.NT8TradeExportAutoWriter NT8TradeExportAutoWriter(ISeries<double> input , string outputPath, string accountFilter, bool useRegistryAccountFilter, string registryPath, int refreshSeconds)
		{
			return indicator.NT8TradeExportAutoWriter(input, outputPath, accountFilter, useRegistryAccountFilter, registryPath, refreshSeconds);
		}
	}
}

#endregion
