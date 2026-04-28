// TradeLogExporter_V3D.cs
// V3D Institutional Regime Matrix — NT8 Trade Log Exporter
//
// PURPOSE:
//   Apply this indicator to the SAME chart as each V3D bot strategy.
//   It listens to OnExecutionUpdate and writes one row per completed
//   round-trip trade to:
//       C:\Users\Valued Customer\NT8_Regimes\V3D\TradeLog\V3D_TradeLog.csv
//
//   At the moment a fill arrives, the indicator reads the current regime
//   state from the RegimeMatrixHUD_V3D instance for that instrument so
//   every trade row carries the regime context that was live at execution.
//
// SETUP:
//   1. Add this indicator to the same NQ or ES chart where a V3D bot runs.
//   2. Set the BotName parameter to match the strategy name exactly
//      (Expansion_V3D, Momentum_V3D, Fader_V3D, Sniper_V3D, ADX_DI_V3D).
//   3. Leave it running. It appends automatically on every filled trade.
//   4. One indicator instance per chart (one per bot per instrument).
//
// OUTPUT:
//   One CSV row per completed trade (entry + exit = 1 round trip row).
//   Columns: see COLUMN_HEADER below.
//
// NOTES:
//   - Reads regime context directly from the HUD instance at fill time.
//   - If no HUD instance is found, regime fields are written as "UNAVAILABLE".
//   - Entry regime is captured at entry fill; exit regime at exit fill.
//   - The file is never deleted or overwritten — rows are always appended.
//   - Python V3D_Regime_Comparison.py reads this file at EOD.

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
    public class TradeLogExporter_V3D : Indicator
    {
        // ---------------------------------------------------------------
        // Configuration — set BotName to match the strategy on this chart
        // ---------------------------------------------------------------
        [NinjaScriptProperty]
        [Display(
            Name = "Bot Name",
            Description = "Name of the V3D strategy on this chart (e.g. Momentum_V3D)",
            Order = 1, GroupName = "V3D Trade Log")]
        public string BotName { get; set; }

        [NinjaScriptProperty]
        [Display(
            Name = "Model Version",
            Description = "V3C or V3D",
            Order = 2, GroupName = "V3D Trade Log")]
        public string ModelVersion { get; set; }

        // ---------------------------------------------------------------
        // Constants
        // ---------------------------------------------------------------
        private const string BASE_PATH =
            @"C:\Users\Valued Customer\NT8_Regimes\V3D\TradeLog\";
        private const string LOG_FILE  = "V3D_TradeLog.csv";
        private const string COLUMN_HEADER =
            "trade_date,entry_time,exit_time,symbol,bot_name,model_version," +
            "direction,contracts,entry_price,exit_price," +
            "gross_pnl,net_pnl,ticks,win_loss," +
            "exit_reason," +
            "entry_regime,entry_macro,entry_hmm,entry_direction," +
            "entry_confidence,entry_conflict,entry_phase,entry_state_age," +
            "exit_regime,exit_macro,exit_hmm,exit_confidence," +
            "entry_size_pct,entry_velocity,entry_reason_code," +
            "account,instrument";

        // ---------------------------------------------------------------
        // State
        // ---------------------------------------------------------------
        private string logPath       = "";
        private string leaderSymbol  = "";
        private readonly HashSet<string> seenExecutionIds = new HashSet<string>();

        // Pending entry context — captured on entry fill, consumed on exit fill
        private bool   hasPendingEntry   = false;
        private string pendingEntryTime  = "";
        private string pendingDirection  = "";
        private int    pendingContracts  = 0;
        private double pendingEntryPrice = 0;
        private string pendingEntryRegime    = "";
        private string pendingEntryMacro     = "";
        private string pendingEntryHMM       = "";
        private string pendingEntryDir       = "";
        private string pendingEntryConf      = "";
        private string pendingEntryConflict  = "";
        private string pendingEntryPhase     = "";
        private string pendingEntryStateAge  = "";
        private string pendingEntrySizePct   = "";
        private string pendingEntryVelocity  = "";
        private string pendingEntryReason    = "";

        // ---------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description      = "V3D: Writes one CSV row per completed trade with full regime context.";
                Name             = "Trade Log Exporter (V3D)";
                BotName          = "Unknown_Bot";
                ModelVersion     = "V3D";
                Calculate        = Calculate.OnBarClose;
                IsOverlay        = true;
                DisplayInDataBox = false;
            }
            else if (State == State.DataLoaded)
            {
                leaderSymbol = GetLeaderSymbol(
                    Instrument.MasterInstrument.Name.ToUpper());

                string dir = BASE_PATH;
                if (!Directory.Exists(dir))
                {
                    try { Directory.CreateDirectory(dir); }
                    catch (Exception ex)
                    {
                        Print("TradeLogExporter: Cannot create directory — " + ex.Message);
                        return;
                    }
                }

                logPath = Path.Combine(dir, LOG_FILE);
                EnsureHeader();

                Print("TradeLogExporter [" + leaderSymbol + " / " + BotName +
                      "]: Ready. Log: " + logPath);

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

        // ---------------------------------------------------------------
        // Account execution listener - fires on every account fill
        // ---------------------------------------------------------------
        private void OnAccountExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            Execution execution = e.Execution;
            if (execution == null)
                return;

            // Only process fills for our tracked instrument
            if (execution.Instrument.MasterInstrument.Name.ToUpper() != leaderSymbol &&
                execution.Instrument.MasterInstrument.Name.ToUpper() !=
                    Instrument.MasterInstrument.Name.ToUpper())
                return;

            if (string.IsNullOrEmpty(logPath)) return;

            try
            {
                string execKey = execution.ExecutionId;
                if (string.IsNullOrEmpty(execKey))
                    execKey = execution.OrderId + "|" + e.Time.ToString("O") + "|" + e.Price.ToString("F8");
                if (seenExecutionIds.Contains(execKey))
                    return;
                seenExecutionIds.Add(execKey);

                // Determine fill type
                string orderName = execution.Order == null ? "" : execution.Order.Name;
                string fromEntrySignal = execution.Order == null ? "" : execution.Order.FromEntrySignal;
                if (!IsOrderForBot(orderName, fromEntrySignal))
                    return;

                OrderAction orderAction = execution.Order == null ? OrderAction.Buy : execution.Order.OrderAction;
                bool isEntry = execution.IsEntry ||
                                orderName.Contains("Entry") ||
                                orderAction == OrderAction.Buy ||
                                orderAction == OrderAction.SellShort;

                bool isExit = execution.IsExit || !isEntry;

                // ---- ENTRY FILL ----
                if (isEntry)
                {
                    string fillDirection = orderAction == OrderAction.SellShort ? "SHORT" : "LONG";
                    SnapshotRegimeAtEntry(e.Price, e.Quantity, e.Time, fillDirection);
                }

                // ---- EXIT FILL ----
                if (isExit && hasPendingEntry)
                {
                    WriteTradeRow(execution, e.Price, e.Quantity, e.Time);
                    hasPendingEntry = false;
                }
            }
            catch (Exception ex)
            {
                Print("TradeLogExporter Error in OnAccountExecutionUpdate: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------
        // Regime snapshot at entry time
        // ---------------------------------------------------------------
        private void SnapshotRegimeAtEntry(
            double price, int qty, DateTime time, string direction)
        {
            hasPendingEntry   = true;
            pendingEntryTime  = time.ToString("yyyy-MM-dd HH:mm:ss");
            pendingDirection  = direction;
            pendingContracts  = qty;
            pendingEntryPrice = price;

            // Try to read from HUD instance
            var hud = GetHud();
            if (hud != null)
            {
                pendingEntryRegime   = SafeStr(hud.FinalRegime);
                pendingEntryMacro    = SafeStr(hud.MacroRegime);
                pendingEntryHMM      = SafeStr(hud.HMMRegime);
                pendingEntryDir      = SafeStr(hud.FinalDirection);
                pendingEntryConf     = hud.RegimeConfidence.ToString();
                pendingEntryConflict = hud.ConflictScore.ToString("F3");
                pendingEntryPhase    = SafeStr(hud.Phase);
                pendingEntryStateAge = hud.StateAgeBars.ToString();
                pendingEntrySizePct  = GetBotSizePct(hud).ToString();
                pendingEntryVelocity = hud.Velocity3P.ToString("F3");
                pendingEntryReason   = SafeStr(hud.ReasonCode);
            }
            else
            {
                pendingEntryRegime = pendingEntryMacro = pendingEntryHMM =
                    pendingEntryDir = pendingEntryPhase = pendingEntryReason = "UNAVAILABLE";
                pendingEntryConf = pendingEntryConflict =
                    pendingEntryStateAge = pendingEntrySizePct =
                    pendingEntryVelocity = "0";
            }
        }

        // ---------------------------------------------------------------
        // Write completed trade row
        // ---------------------------------------------------------------
        private void WriteTradeRow(
            Execution execution, double exitPrice, int qty, DateTime exitTime)
        {
            var hud = GetHud();

            string exitRegime = "UNAVAILABLE";
            string exitMacro  = "UNAVAILABLE";
            string exitHMM    = "UNAVAILABLE";
            string exitConf   = "0";

            if (hud != null)
            {
                exitRegime = SafeStr(hud.FinalRegime);
                exitMacro  = SafeStr(hud.MacroRegime);
                exitHMM    = SafeStr(hud.HMMRegime);
                exitConf   = hud.RegimeConfidence.ToString();
            }

            // P&L calculation
            double tickSize  = TickSize;
            double tickValue = Instrument.MasterInstrument.PointValue * tickSize;
            double priceDiff = pendingDirection == "LONG"
                ? exitPrice - pendingEntryPrice
                : pendingEntryPrice - exitPrice;
            double grossPnl  = priceDiff / tickSize * tickValue * pendingContracts;
            double netPnl    = grossPnl; // NT8 doesn't expose commission here; adjust in Python if needed
            double ticks     = priceDiff / tickSize;
            string winLoss   = grossPnl > 0 ? "WIN" : (grossPnl < 0 ? "LOSS" : "SCRATCH");

            // Exit reason: try to infer from order name
            string exitReason = InferExitReason(execution.Order.Name);

            string tradeDate = exitTime.ToString("yyyy-MM-dd");
            string exitTimeStr = exitTime.ToString("yyyy-MM-dd HH:mm:ss");

            string line = string.Join(",",
                tradeDate,
                pendingEntryTime,
                exitTimeStr,
                leaderSymbol,
                BotName,
                ModelVersion,
                pendingDirection,
                pendingContracts.ToString(),
                pendingEntryPrice.ToString("F2"),
                exitPrice.ToString("F2"),
                grossPnl.ToString("F2"),
                netPnl.ToString("F2"),
                ticks.ToString("F1"),
                winLoss,
                exitReason,
                pendingEntryRegime,
                pendingEntryMacro,
                pendingEntryHMM,
                pendingEntryDir,
                pendingEntryConf,
                pendingEntryConflict,
                pendingEntryPhase,
                pendingEntryStateAge,
                exitRegime,
                exitMacro,
                exitHMM,
                exitConf,
                pendingEntrySizePct,
                pendingEntryVelocity,
                pendingEntryReason,
                execution.Account == null ? "Unknown" : execution.Account.Name,
                Instrument.FullName
            );

            AppendLine(line);

            Print(string.Format(
                "TradeLogExporter [{0}/{1}]: {2} {3} {4}@{5:F2}→{6:F2} | " +
                "PnL=${7:F2} ({8}) | Regime={9} | Phase={10}",
                leaderSymbol, BotName, winLoss, pendingDirection,
                pendingContracts, pendingEntryPrice, exitPrice,
                grossPnl, exitReason,
                pendingEntryRegime, pendingEntryPhase));
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------
        private dynamic GetHud()
        {
            try
            {
                // Access the static HUD instance dictionary
                // RegimeMatrixHUD_V3D.InstancesV3D is a Dictionary<string, RegimeMatrixHUD_V3D>
                var hudType = Type.GetType(
                    "NinjaTrader.NinjaScript.Indicators.RegimeMatrixHUD_V3D");
                if (hudType == null) return null;

                var instancesProp = hudType.GetProperty("InstancesV3D",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static);
                if (instancesProp == null) return null;

                var instances = instancesProp.GetValue(null) as
                    System.Collections.IDictionary;
                if (instances == null || !instances.Contains(leaderSymbol))
                    return null;

                return instances[leaderSymbol];
            }
            catch { return null; }
        }

        private int GetBotSizePct(dynamic hud)
        {
            try
            {
                // Map bot name to SizePct property
                switch (BotName)
                {
                    case "Expansion_V3D":  return (int)hud.ExpansionSizePct;
                    case "Momentum_V3D":   return (int)hud.MomoSizePct;
                    case "Fader_V3D":      return (int)hud.PineSizePct;
                    case "Sniper_V3D":     return (int)hud.SniperSizePct;
                    case "ADX_DI_V3D":     return (int)hud.ADX_DISizePct;
                    default:               return 0;
                }
            }
            catch { return 0; }
        }

        private bool IsOrderForBot(string orderName, string fromEntrySignal)
        {
            string n = ((orderName ?? "") + "|" + (fromEntrySignal ?? "")).ToUpperInvariant();

            switch ((BotName ?? "").ToUpperInvariant())
            {
                case "EXPANSION_V3D":
                    return n.Contains("EXPL") || n.Contains("EXPS") ||
                           n.Contains("EXPBL") || n.Contains("EXPBS");
                case "MOMENTUM_V3D":
                    return n.Contains("MOMOL") || n.Contains("MOMOS");
                case "FADER_V3D":
                    return n.Contains("FADEL") || n.Contains("FADES") ||
                           n.Contains("FADEBL") || n.Contains("FADEBS");
                case "SNIPER_V3D":
                    return n.Contains("SNIPEL") || n.Contains("SNIPES") ||
                           n.Contains("SNIPEBL") || n.Contains("SNIPEBS");
                case "ADX_DI_V3D":
                    return n.Contains("ADXDIL") || n.Contains("ADXDIS");
                default:
                    return true;
            }
        }

        private static string GetLeaderSymbol(string sym)
        {
            if (sym.Contains("MNQ")) return "NQ";
            if (sym.Contains("MES")) return "ES";
            if (sym.Contains("NQ"))  return "NQ";
            if (sym.Contains("ES"))  return "ES";
            return sym;
        }

        private static string InferExitReason(string orderName)
        {
            if (string.IsNullOrEmpty(orderName))      return "UNKNOWN";
            string n = orderName.ToUpper();
            if (n.Contains("PROFIT"))                 return "TARGET_HIT";
            if (n.Contains("STOP"))                   return "STOP_HIT";
            if (n.Contains("CIRCUITBREAKER"))         return "CIRCUIT_BREAKER";
            if (n.Contains("TRANSITION"))             return "REGIME_TRANSITION_EXIT";
            if (n.Contains("SLOPE"))                  return "SLOPE_EXIT";
            if (n.Contains("TRAIL"))                  return "TRAIL_STOP";
            if (n.Contains("WOBBLE"))                 return "WOBBLE_EJECT";
            if (n.Contains("SESSION") ||
                n.Contains("CLOSE") || n.Contains("EOD")) return "SESSION_CLOSE";
            return orderName;
        }

        private static string SafeStr(object o)
        {
            return o == null ? "" : o.ToString();
        }

        private void EnsureHeader()
        {
            if (File.Exists(logPath)) return;
            try
            {
                using (var sw = new StreamWriter(logPath, false))
                    sw.WriteLine(COLUMN_HEADER);
                Print("TradeLogExporter: Created " + logPath);
            }
            catch (Exception ex)
            {
                Print("TradeLogExporter: Could not create header — " + ex.Message);
            }
        }

        private void AppendLine(string line)
        {
            try
            {
                using (var sw = new StreamWriter(logPath, true))
                    sw.WriteLine(line);
            }
            catch (Exception ex)
            {
                Print("TradeLogExporter: Write error — " + ex.Message);
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private TradeLogExporter_V3D[] cacheTradeLogExporter_V3D;
        public TradeLogExporter_V3D TradeLogExporter_V3D()
        {
            return TradeLogExporter_V3D(Input);
        }
        public TradeLogExporter_V3D TradeLogExporter_V3D(ISeries<double> input)
        {
            if (cacheTradeLogExporter_V3D != null)
                for (int idx = 0; idx < cacheTradeLogExporter_V3D.Length; idx++)
                    if (cacheTradeLogExporter_V3D[idx] != null &&
                        cacheTradeLogExporter_V3D[idx].EqualsInput(input))
                        return cacheTradeLogExporter_V3D[idx];
            return CacheIndicator<TradeLogExporter_V3D>(
                new TradeLogExporter_V3D(), input, ref cacheTradeLogExporter_V3D);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.TradeLogExporter_V3D TradeLogExporter_V3D()
        {
            return indicator.TradeLogExporter_V3D(Input);
        }
        public Indicators.TradeLogExporter_V3D TradeLogExporter_V3D(
            ISeries<double> input)
        {
            return indicator.TradeLogExporter_V3D(input);
        }
    }
}

#endregion
