// =============================================================================
// V3DStrategyTradeLogger.cs - internal trade logging helper for V3D strategies
// =============================================================================
// Place in:
//   C:\Users\Valued Customer\Documents\NinjaTrader 8\bin\Custom\Indicators\
//
// V3D strategies call this directly from OnExecutionUpdate so trade export is
// scoped to the running strategy/account and no chart-level trade exporter is
// required for V3D fill logs.
// =============================================================================

#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class V3DStrategyTradeLogger
    {
        private const string Header =
            "trade_date,entry_time,exit_time,model_version,account,strategy_name," +
            "bot_name,ab_mode,symbol,instrument,direction,contracts,entry_price," +
            "exit_price,gross_pnl,net_pnl,ticks,win_loss,exit_reason," +
            "initial_stop_price,initial_stop_distance,export_timestamp";

        private readonly Strategy _strategy;
        private readonly string _accountFilter;
        private readonly string _configuredStrategyName;
        private readonly string _modelVersion;
        private readonly string _logFolder;
        private readonly string _botName;
        private readonly string _abMode;

        private DateTime _entryTime = DateTime.MinValue;
        private double _entryPrice = 0.0;
        private double _initialStopPrice = 0.0;
        private double _initialStopTicks = 0.0;
        private string _direction = "";
        private bool _entryOpen = false;
        private int _entryContracts = 0;
        private int _startTradeCount = 0;
        private string _exitReason = "UNKNOWN";

        public V3DStrategyTradeLogger(Strategy strategy, string accountFilter,
            string configuredStrategyName, string modelVersion, string logFolder,
            string botName, string abMode)
        {
            _strategy = strategy;
            _accountFilter = accountFilter ?? "";
            _configuredStrategyName = configuredStrategyName ?? "";
            _modelVersion = string.IsNullOrWhiteSpace(modelVersion) ? "V3D" : modelVersion;
            _logFolder = string.IsNullOrWhiteSpace(logFolder)
                ? @"C:\Users\Valued Customer\NT8_Regimes\V3D\TradeLog"
                : logFolder;
            _botName = string.IsNullOrWhiteSpace(botName) ? strategy.Name : botName;
            _abMode = string.IsNullOrWhiteSpace(abMode) ? "A" : abMode;
        }

        public bool IsConfiguredAccount(Execution execution)
        {
            string accountName = GetAccountName(execution);
            if (string.IsNullOrWhiteSpace(_accountFilter))
                return true;

            foreach (string raw in _accountFilter.Split(new[] { ',', ';', '|' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                string allowed = raw.Trim();
                if (string.Equals(accountName, allowed, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public void CaptureInitialStop(double stopPrice, string direction)
        {
            _initialStopPrice = stopPrice;
            _initialStopTicks = 0.0;
            _direction = direction;
        }

        public void CaptureInitialStopTicks(double stopTicks, string direction)
        {
            _initialStopTicks = stopTicks;
            _initialStopPrice = 0.0;
            _direction = direction;
        }

        public void OnExecution(Execution execution, double price, int quantity,
            MarketPosition marketPosition, DateTime time)
        {
            if (execution == null || execution.Order == null || quantity <= 0)
                return;
            if (!IsConfiguredAccount(execution))
                return;

            OrderAction action = execution.Order.OrderAction;
            bool isEntry = action == OrderAction.Buy || action == OrderAction.SellShort;
            bool isExit = action == OrderAction.Sell || action == OrderAction.BuyToCover;

            if (isEntry)
            {
                string fillDirection = action == OrderAction.SellShort ? "SHORT" : "LONG";
                if (!_entryOpen || _direction != fillDirection)
                {
                    _entryOpen = true;
                    _startTradeCount = _strategy.SystemPerformance.AllTrades.Count;
                    _entryTime = time;
                    _entryPrice = price;
                    _direction = fillDirection;
                    _entryContracts = quantity;
                    _exitReason = "UNKNOWN";
                }
                else
                {
                    double totalNotional = _entryPrice * _entryContracts + price * quantity;
                    _entryContracts += quantity;
                    _entryPrice = totalNotional / Math.Max(1, _entryContracts);
                }

                if (_initialStopPrice <= 0.0 && _initialStopTicks > 0.0)
                {
                    _initialStopPrice = fillDirection == "LONG"
                        ? price - _initialStopTicks * _strategy.TickSize
                        : price + _initialStopTicks * _strategy.TickSize;
                    _initialStopPrice = _strategy.Instrument.MasterInstrument
                        .RoundToTickSize(_initialStopPrice);
                }
                return;
            }

            if (!isExit)
                return;

            _exitReason = InferExitReason(execution.Order.Name);
            if (!_entryOpen ||
                !(marketPosition == MarketPosition.Flat ||
                  _strategy.Position.MarketPosition == MarketPosition.Flat))
                return;

            WriteTrade(exitPrice: price, exitTime: time, execution: execution);
            Reset();
        }

        private void WriteTrade(double exitPrice, DateTime exitTime, Execution execution)
        {
            int tradeCount = _strategy.SystemPerformance.AllTrades.Count;
            double grossPnl = 0.0;
            double commission = 0.0;

            for (int i = _startTradeCount; i < tradeCount; i++)
            {
                var trade = _strategy.SystemPerformance.AllTrades[i];
                grossPnl += trade.ProfitCurrency;
                commission += trade.Commission;
            }

            if (tradeCount <= _startTradeCount)
            {
                double priceDiff = _direction == "LONG"
                    ? exitPrice - _entryPrice
                    : _entryPrice - exitPrice;
                double tickValue = _strategy.Instrument.MasterInstrument.PointValue * _strategy.TickSize;
                grossPnl = (priceDiff / _strategy.TickSize) * tickValue * Math.Max(1, _entryContracts);
                commission = 0.0;
            }

            double netPnl = grossPnl - commission;
            double tickValueForLog = _strategy.Instrument.MasterInstrument.PointValue * _strategy.TickSize;
            double ticks = tickValueForLog > 0.0 ? netPnl / tickValueForLog : 0.0;
            string winLoss = netPnl > 0.0 ? "WIN" : (netPnl < 0.0 ? "LOSS" : "SCRATCH");
            double stopDistance = 0.0;
            if (_initialStopPrice > 0.0 && _entryPrice > 0.0)
                stopDistance = Math.Abs(_entryPrice - _initialStopPrice)
                    * _strategy.Instrument.MasterInstrument.PointValue
                    * Math.Max(1, _entryContracts);

            string accountName = GetAccountName(execution);
            string strategyName = ResolveStrategyName(accountName);
            string[] fields =
            {
                _entryTime.ToString("yyyy-MM-dd"),
                _entryTime.ToString("yyyy-MM-dd HH:mm:ss"),
                exitTime.ToString("yyyy-MM-dd HH:mm:ss"),
                _modelVersion,
                accountName,
                strategyName,
                _botName,
                _abMode,
                GetSymbol(),
                _strategy.Instrument.FullName,
                _direction,
                _entryContracts.ToString(CultureInfo.InvariantCulture),
                Format(_entryPrice, "F2"),
                Format(exitPrice, "F2"),
                Format(grossPnl, "F2"),
                Format(netPnl, "F2"),
                Format(ticks, "F1"),
                winLoss,
                _exitReason,
                Format(_initialStopPrice, "F2"),
                Format(stopDistance, "F2"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            string row = string.Join(",", Array.ConvertAll(fields, SafeCsv));
            EnsureAndAppend(Path.Combine(_logFolder, SafeFileName(accountName) + "_TradeLog.csv"), row);
            EnsureAndAppend(Path.Combine(_logFolder, "V3D_INTERNAL_TradeLog.csv"), row);
        }

        private string ResolveStrategyName(string accountName)
        {
            string mapped = V3DStrategyIdentity.ResolveStrategyName(accountName);
            if (!string.IsNullOrWhiteSpace(mapped))
                return mapped;
            if (!string.IsNullOrWhiteSpace(_configuredStrategyName))
                return _configuredStrategyName;
            return _strategy.Name ?? "UNKNOWN";
        }

        private string GetAccountName(Execution execution)
        {
            if (execution != null && execution.Account != null)
                return execution.Account.Name;
            return _strategy.Account == null ? "UNKNOWN" : _strategy.Account.Name;
        }

        private string GetSymbol()
        {
            string sym = _strategy.Instrument.MasterInstrument.Name.ToUpperInvariant();
            if (sym.Contains("MNQ")) return "NQ";
            if (sym.Contains("MES")) return "ES";
            if (sym.Contains("NQ")) return "NQ";
            if (sym.Contains("ES")) return "ES";
            return sym;
        }

        private void EnsureAndAppend(string path, string row)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    File.WriteAllText(path, Header + Environment.NewLine);
                File.AppendAllText(path, row + Environment.NewLine);
            }
            catch (Exception ex)
            {
                _strategy.Print("[V3DStrategyTradeLogger] Write error: " + ex.Message);
            }
        }

        private void Reset()
        {
            _entryOpen = false;
            _startTradeCount = _strategy.SystemPerformance.AllTrades.Count;
            _entryContracts = 0;
            _entryPrice = 0.0;
            _initialStopPrice = 0.0;
            _initialStopTicks = 0.0;
            _entryTime = DateTime.MinValue;
            _direction = "";
            _exitReason = "UNKNOWN";
        }

        private static string Format(double value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        private static string SafeCsv(string value)
        {
            string s = value ?? "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\r") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string SafeFileName(string value)
        {
            string s = string.IsNullOrWhiteSpace(value) ? "V3D_UNFILTERED" : value;
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Replace(" ", "_").Replace("-", "_");
        }

        private static string InferExitReason(string orderName)
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
    }

    public static class V3DStrategyIdentity
    {
        private static readonly Dictionary<string, string> StrategyByAccount =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "SimV3D-ES-1A", "Expansion_V3D ES" },
            { "SimV3D-ES-2A", "Momentum_V3D ES" },
            { "SimV3D-ES-2B", "Expansion_V3D_B ES" },
            { "SimV3D-ES-3A", "Fader_V3D A" },
            { "SimV3D-ES-4A", "Sniper_V3D" },
            { "SimV3D-ES-5A", "ADX_DI_V3D ES" },
            { "SimV3D-NQ-1A", "Expansion_V3D" },
            { "SimV3D-NQ-1B", "Expansion_V3D_B" },
            { "SimV3D-NQ-2A", "Momentum_V3D" },
            { "SimV3D-NQ-2B", "Momentum_V3D_B" },
            { "SimV3D-NQ-3A", "Fader_V3D A" },
            { "SimV3D-NQ-3B", "Fader_V3D_B" },
            { "SimV3D-NQ-4A", "Sniper_V3D" },
            { "SimV3D-NQ-4B", "Sniper_V3D_B" },
            { "SimV3D-NQ-5A", "ADX_DI_V3D" },
            { "SimV3D-NQ-5B", "ADX_DI_V3D" },
            { "SimV3D-NQ-5C", "ADX_DI_V3D_C" },
        };

        public static string ResolveStrategyName(string accountName)
        {
            string value;
            return StrategyByAccount.TryGetValue(accountName ?? "", out value) ? value : "";
        }
    }
}
