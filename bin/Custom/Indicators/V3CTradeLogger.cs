// =============================================================================
// V3C_TradeLogger.cs  — Shared Stage 1 trade logging helper for V3C strategies
// =============================================================================
// Place this file in:
//   C:\Users\Valued Customer\Documents\NinjaTrader 8\bin\Custom\Indicators\
// Compile once in NinjaScript Editor.
// All four V3C strategies call V3CTradeLogger.LogTrade() from OnExecutionUpdate.
// =============================================================================
//
// USAGE in each V3C strategy:
//
//   // In OnStateChange / DataLoaded:
//   _logger = new V3CTradeLogger(this, AccountNameFilter, "V3C",
//       @"C:\Users\Valued Customer\NT8_Regimes\V3C\TradeLog");
//
//   // In OnExecutionUpdate (fill received):
//   _logger.OnExecution(execution, previousOrder);
//
// =============================================================================

#region Using declarations
using System;
using System.IO;
using System.Globalization;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Lightweight Stage 1 trade logger for V3C strategies.
    /// Writes one CSV row per closed trade to V3C\TradeLog\{AccountName}_TradeLog.csv.
    /// Schema matches the Stage 1 per-bot log format used by V3D strategies.
    /// </summary>
    public class V3CTradeLogger
    {
        private readonly Strategy    _strategy;
        private readonly string      _accountFilter;
        private readonly string      _modelVersion;
        private readonly string      _logFolder;
        private          string      _currentLogPath;
        private          string      _entryAccount;
        private          double      _entryPrice;
        private          string      _entryTime;
        private          string      _direction;
        private          string      _entryName;
        private          int         _contracts;

        // Stage 1 fields captured at entry
        private double _initialStopPrice;
        private double _initialStopDistance;
        private bool   _entryFilled;

        public V3CTradeLogger(Strategy strategy, string accountFilter,
                               string modelVersion, string logFolder)
        {
            _strategy      = strategy;
            _accountFilter = accountFilter ?? "";
            _modelVersion  = modelVersion ?? "V3C";
            _logFolder     = logFolder;
        }

        /// <summary>
        /// Call from the strategy's OnExecutionUpdate.
        /// </summary>
        public void OnExecution(Execution execution, Order previousOrder)
        {
            OnExecution(execution, previousOrder, _strategy.Time[0]);
        }

        /// <summary>
        /// Call from the strategy's OnExecutionUpdate with the execution timestamp.
        /// </summary>
        public void OnExecution(Execution execution, Order previousOrder, DateTime executionTime)
        {
            if (execution?.Order == null) return;

            string accountName = execution.Account != null
                ? execution.Account.Name
                : (_strategy.Account == null ? "" : _strategy.Account.Name);

            // Only log for the configured account
            if (!string.IsNullOrEmpty(_accountFilter) &&
                !string.Equals(accountName, _accountFilter,
                               StringComparison.OrdinalIgnoreCase))
                return;

            var order = execution.Order;

            // Capture entry on first fill of a new position
            if (order.OrderAction == OrderAction.Buy ||
                order.OrderAction == OrderAction.SellShort)
            {
                if (!_entryFilled)
                {
                    _entryFilled          = true;
                    _entryAccount         = accountName;
                    _entryPrice           = execution.Price;
                    _entryTime            = executionTime.ToString("yyyy-MM-dd HH:mm:ss");
                    _direction            = order.OrderAction == OrderAction.Buy ? "LONG" : "SHORT";
                    _entryName            = order.Name ?? "";
                    _contracts            = execution.Quantity;

                    // Capture initial stop at entry time
                    // The strategy sets stop orders — capture price from outstanding stop orders
                    _initialStopPrice     = 0.0;
                    _initialStopDistance  = 0.0;
                }
                return;
            }

            // Exit fill — write the row
            if (order.OrderAction == OrderAction.Sell ||
                order.OrderAction == OrderAction.BuyToCover)
            {
                if (!_entryFilled) return;  // Guard: no entry captured

                double exitPrice = execution.Price;
                string exitTime  = executionTime.ToString("yyyy-MM-dd HH:mm:ss");
                string tradeDate = executionTime.ToString("yyyy-MM-dd");

                double sign = _direction == "LONG" ? 1.0 : -1.0;
                double pointValue = _strategy.Instrument.MasterInstrument.PointValue;
                double grossPnl   = sign * (exitPrice - _entryPrice) * _contracts * pointValue;
                double netPnl     = grossPnl;  // V3C is SIM — no commissions deducted
                double ticks      = sign * (exitPrice - _entryPrice) / _strategy.TickSize;
                string winLoss    = netPnl > 0 ? "WIN" : netPnl < 0 ? "LOSS" : "SCRATCH";
                string exitReason = order.Name ?? "UNKNOWN";
                string symbol     = _strategy.Instrument.MasterInstrument.Name.ToUpper();
                symbol            = symbol == "MNQ" ? "NQ" : symbol == "MES" ? "ES" : symbol;
                string instrument = _strategy.Instrument.FullName;
                string stratName  = _strategy.Name ?? "UNKNOWN";
                string botName    = _strategy.Name ?? "UNKNOWN";

                // Compute initial_stop_distance if we have the stop price
                if (_initialStopPrice > 0)
                    _initialStopDistance = Math.Abs(_entryPrice - _initialStopPrice)
                                           * _contracts * pointValue;

                EnsureLogFile(tradeDate, symbol, accountName);
                WriteRow(
                    tradeDate, _entryTime, exitTime, _modelVersion,
                    string.IsNullOrEmpty(_entryAccount) ? accountName : _entryAccount,
                    stratName, botName, "N/A",
                    symbol, instrument, _direction,
                    _contracts.ToString(),
                    _entryPrice.ToString("F4", CultureInfo.InvariantCulture),
                    exitPrice.ToString("F4", CultureInfo.InvariantCulture),
                    grossPnl.ToString("F2", CultureInfo.InvariantCulture),
                    netPnl.ToString("F2", CultureInfo.InvariantCulture),
                    ticks.ToString("F1", CultureInfo.InvariantCulture),
                    winLoss, exitReason,
                    _initialStopPrice > 0
                        ? _initialStopPrice.ToString("F4", CultureInfo.InvariantCulture)
                        : "",
                    _initialStopDistance > 0
                        ? _initialStopDistance.ToString("F2", CultureInfo.InvariantCulture)
                        : "",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                );

                // Reset entry state
                _entryFilled         = false;
                _entryAccount        = "";
                _initialStopPrice    = 0.0;
                _initialStopDistance = 0.0;
            }
        }

        /// <summary>
        /// Call from OnOrderUpdate to capture the initial stop price as soon
        /// as the stop order is submitted, while the entry position is open.
        /// </summary>
        public void OnStopOrderSubmitted(Order order)
        {
            if (order == null) return;
            if (!_entryFilled) return;
            if (order.OrderAction == OrderAction.Sell ||
                order.OrderAction == OrderAction.BuyToCover)
            {
                // First stop order sets the initial stop
                if (_initialStopPrice == 0.0 && order.StopPrice > 0)
                    _initialStopPrice = order.StopPrice;
            }
        }

        private void EnsureLogFile(string tradeDate, string symbol, string accountName)
        {
            // Use account name as the file key — one file per account
            string keyName = string.IsNullOrEmpty(accountName) ? _accountFilter : accountName;
            string safeName = keyName
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace("(", "")
                .Replace(")", "");
            if (string.IsNullOrEmpty(safeName))
                safeName = "V3C_UNFILTERED";

            _currentLogPath = Path.Combine(_logFolder, $"{safeName}_TradeLog.csv");

            if (!Directory.Exists(_logFolder))
                Directory.CreateDirectory(_logFolder);

            if (!File.Exists(_currentLogPath))
            {
                // Write header — matches Stage 1 per-bot schema used by V3D
                File.WriteAllText(_currentLogPath,
                    "trade_date,entry_time,exit_time,model_version,account,strategy_name," +
                    "bot_name,ab_mode,symbol,instrument,direction,contracts," +
                    "entry_price,exit_price,gross_pnl,net_pnl,ticks,win_loss," +
                    "exit_reason,initial_stop_price,initial_stop_distance,export_timestamp\r\n");
            }
        }

        private void WriteRow(params string[] fields)
        {
            if (_currentLogPath == null) return;
            try
            {
                string line = string.Join(",",
                    Array.ConvertAll(fields, f => SafeField(f))) + "\r\n";
                File.AppendAllText(_currentLogPath, line);
            }
            catch (Exception ex)
            {
                _strategy.Print($"[V3CTradeLogger] Write error: {ex.Message}");
            }
        }

        private static string SafeField(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            if (v.Contains(",") || v.Contains("\"") || v.Contains("\n"))
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }
    }
}
