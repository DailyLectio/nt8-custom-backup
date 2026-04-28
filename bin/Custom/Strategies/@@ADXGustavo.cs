#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ADXGustavo : Strategy
    {
        // --- Inputs (backing fields) ---
        private int    contracts        = 3;
        private bool   exitOnStopX      = true;
        private double stopAtrMult      = 1.0;
        private int    atrLen           = 14;
        private int    i_sigLen         = 14; // ADX Smoothing
        private int    i_diLen          = 14; // DI Length
        private int    i_hlRange        = 20; // ADX Level Range

        // P/L Tick inputs
        private int defaultTickProfit   = 35;
        private int defaultTickLoss     = 35;

        // EMA No Trade Zone inputs
        private int emaPeriod           = 50;
        private int emaTickBand         = 20;

        // VWAP No Trade Zone inputs (placeholder – not used yet)
        private int vwapTickBand        = 40;

        // Trailing Stop inputs
        private int trailingStopTicks   = 35;
        private int barNTrailTicks      = 4;

        // Management Inputs
        private int    maxTradeLosers   = 3;       // 0 = off
        private double maxDailyProfit   = 500.0;   // 0 = off
        private double maxDailyLoss     = 300.0;   // 0 = off
        private string rrChoice         = "1.5";

        // --- Internal Variables ---
        private ADX adx;
        private DM  dm;
        private EMA emaNoTrade;

        private double rrRatio;
        private int    loserCount       = 0;

        // Order / trade tracking
        private Order          entryOrder       = null;
        private Order          stopOrder        = null;
        private Order          targetOrder      = null;
        private double         currentStopPrice = 0;
        private bool           exitsPlaced      = false;
        private double         lastEntryPrice   = 0;
        private MarketPosition lastEntryDir     = MarketPosition.Flat;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                = "ADX Gustavo";
                Description         = "ADX DI Cross Strategy with custom enhancements (Gustavo Cardelle CC)";
                Calculate           = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling       = EntryHandling.AllEntries;
                IsUnmanaged         = false;
                SetOrderQuantity    = SetOrderQuantity.Strategy;
            }
            else if (State == State.Configure)
            {
                if      (rrChoice.Equals("1.5")) rrRatio = 1.5;
                else if (rrChoice.Equals("2.0")) rrRatio = 2.0;
                else if (rrChoice.Equals("3.0")) rrRatio = 3.0;
                else                            rrRatio = 1.5;

                adx        = ADX(i_sigLen);
                dm         = DM(i_diLen);
                emaNoTrade = EMA(emaPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(i_diLen, i_sigLen) + atrLen)
                return;

            // --- 1. Risk/Trade Management Checks ---
            if (maxTradeLosers > 0 && loserCount >= maxTradeLosers)
                return;

            double cumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;

            if ((maxDailyProfit > 0 && cumProfit >= maxDailyProfit) ||
                (maxDailyLoss   > 0 && cumProfit <= -maxDailyLoss))
                return;

            // --- 2. Manage Trailing Stops (if position exists) ---
            if (Position.MarketPosition != MarketPosition.Flat && stopOrder != null)
                ManageTrailingStops();

            // --- 3. Get Indicator Values ---
            double diPlus      = dm.DiPlus[0];
            double diMinus     = dm.DiMinus[0];
            double adxVal      = adx[0];

            double diPlusPrev  = dm.DiPlus[1];
            double diMinusPrev = dm.DiMinus[1];
            double adxValPrev  = adx[1];

            // --- 4. No Trade Zone Checks ---
            if (IsInNoTradeZone())
                return;

            // --- 5. Entry & Exit Signals ---
            bool longSignal  = diPlus  > diMinus  && diPlusPrev  <= diMinusPrev && adxVal > i_hlRange;
            bool shortSignal = diMinus > diPlus   && diMinusPrev <= diPlusPrev  && adxVal > i_hlRange;

            bool exitLongSignal  = diMinus > diPlus  || (adxVal <= i_hlRange && adxValPrev > i_hlRange);
            bool exitShortSignal = diPlus  > diMinus || (adxVal <= i_hlRange && adxValPrev > i_hlRange);

            // --- 6. Execute Trades ---
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (longSignal)
                {
                    exitsPlaced  = false;
                    lastEntryDir = MarketPosition.Long;
                    entryOrder   = EnterLong(contracts, "Long");
                }
                else if (shortSignal)
                {
                    exitsPlaced  = false;
                    lastEntryDir = MarketPosition.Short;
                    entryOrder   = EnterShort(contracts, "Short");
                }
            }
            else if (exitOnStopX)
            {
                if (Position.MarketPosition == MarketPosition.Long && exitLongSignal)
                    ExitLong("Stop X", "Long");
                else if (Position.MarketPosition == MarketPosition.Short && exitShortSignal)
                    ExitShort("Stop X", "Short");
            }
        }

        // --- Helper: Check No Trade Zones ---
        private bool IsInNoTradeZone()
        {
            double emaVal  = emaNoTrade[0];
            double emaBand = emaTickBand * TickSize;

            if (Close[0] < emaVal + emaBand && Close[0] > emaVal - emaBand)
                return true;

            // VWAP zone placeholder if you hook up VWAP later
            return false;
        }

        // --- Helper: Manage Trailing Stops ---
        private void ManageTrailingStops()
        {
            double newStopPrice = currentStopPrice;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                double tickTrailPrice = High[0] - trailingStopTicks * TickSize;
                double barNTrailPrice = Low[1]  - barNTrailTicks   * TickSize;

                newStopPrice = Math.Max(currentStopPrice, Math.Max(tickTrailPrice, barNTrailPrice));

                if (newStopPrice > currentStopPrice)
                {
                    ChangeOrder(stopOrder, stopOrder.Quantity, 0, newStopPrice);
                    currentStopPrice = newStopPrice;
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double tickTrailPrice = Low[0]  + trailingStopTicks * TickSize;
                double barNTrailPrice = High[1] + barNTrailTicks    * TickSize;

                newStopPrice = Math.Min(currentStopPrice, Math.Min(tickTrailPrice, barNTrailPrice));

                if (newStopPrice < currentStopPrice)
                {
                    ChangeOrder(stopOrder, stopOrder.Quantity, 0, newStopPrice);
                    currentStopPrice = newStopPrice;
                }
            }
        }

        // =========================================================
        // OnExecutionUpdate – place exits & track losers
        // =========================================================
        protected override void OnExecutionUpdate(
            Execution      execution,
            string         executionId,
            double         price,
            int            quantity,
            MarketPosition marketPosition,
            string         orderId,
            DateTime       time)
        {
            if (execution == null || execution.Order == null)
                return;

            Order execOrder = execution.Order;

            // 1) ENTRY FILLED → place stop & target once
            if (!exitsPlaced &&
                (execOrder.Name == "Long" || execOrder.Name == "Short") &&
                execOrder.OrderState == OrderState.Filled)
            {
                double atrVal = ATR(atrLen)[0];

                double stopTicksCalc   = (defaultTickLoss   > 0)
                                         ? defaultTickLoss
                                         : stopAtrMult * (atrVal / TickSize);

                double profitTicksCalc = (defaultTickProfit > 0)
                                         ? defaultTickProfit
                                         : stopTicksCalc * rrRatio;

                double stopOffset   = stopTicksCalc   * TickSize;
                double targetOffset = profitTicksCalc * TickSize;

                int qty = execOrder.Filled;

                lastEntryPrice = price;    // entry price for loser tracking

                if (execOrder.Name == "Long")
                {
                    currentStopPrice = price - stopOffset;
                    double targetPrice = price + targetOffset;

                    stopOrder   = ExitLongStopMarket(0, true, qty, currentStopPrice, "Stop Loss", "Long");
                    targetOrder = ExitLongLimit(0, true, qty, targetPrice, "Profit Target", "Long");
                }
                else // Short
                {
                    currentStopPrice = price + stopOffset;
                    double targetPrice = price - targetOffset;

                    stopOrder   = ExitShortStopMarket(0, true, qty, currentStopPrice, "Stop Loss", "Short");
                    targetOrder = ExitShortLimit(0, true, qty, targetPrice, "Profit Target", "Short");
                }

                exitsPlaced = true;
            }

            // 2) EXIT FILLED → update loser count & reset tracking when flat
            bool isExitOrder = execOrder.Name != "Long" && execOrder.Name != "Short";

            if (isExitOrder && execOrder.OrderState == OrderState.Filled)
            {
                bool isLoser = false;

                if (lastEntryDir == MarketPosition.Long && price < lastEntryPrice)
                    isLoser = true;
                else if (lastEntryDir == MarketPosition.Short && price > lastEntryPrice)
                    isLoser = true;

                if (isLoser && maxTradeLosers > 0)
                    loserCount++;

                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    exitsPlaced      = false;
                    entryOrder       = null;
                    stopOrder        = null;
                    targetOrder      = null;
                    lastEntryDir     = MarketPosition.Flat;
                    lastEntryPrice   = 0;
                }
            }
        }

        #region Properties

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Order = 1, GroupName = "1. Pine Script Core")]
        public int Contracts
        {
            get { return contracts; }
            set { contracts = Math.Max(1, value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Exit on STOP X", Order = 2, GroupName = "1. Pine Script Core")]
        public bool ExitOnStopX
        {
            get { return exitOnStopX; }
            set { exitOnStopX = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit R:R (If P/L Ticks = 0)", Order = 3, GroupName = "1. Pine Script Core")]
        [TypeConverter(typeof(StringConverter))]
        public string RRChoice
        {
            get { return rrChoice; }
            set { rrChoice = value; }
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Length", Order = 4, GroupName = "1. Pine Script Core")]
        public int AtrLen
        {
            get { return atrLen; }
            set { atrLen = Math.Max(1, value); }
        }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "ATR Stop Mult", Order = 5, GroupName = "1. Pine Script Core")]
        public double StopAtrMult
        {
            get { return stopAtrMult; }
            set { stopAtrMult = Math.Max(0.1, value); }
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ADX Smoothing (ADX Period)", Order = 6, GroupName = "1. Pine Script Core")]
        public int I_sigLen
        {
            get { return i_sigLen; }
            set { i_sigLen = Math.Max(1, value); }
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "DI Length (DM Period)", Order = 7, GroupName = "1. Pine Script Core")]
        public int I_diLen
        {
            get { return i_diLen; }
            set { i_diLen = Math.Max(1, value); }
        }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ADX Level Range", Order = 8, GroupName = "1. Pine Script Core")]
        public int I_hlRange
        {
            get { return i_hlRange; }
            set { i_hlRange = Math.Max(0, value); }
        }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "P/L Tick Profit (0=Use R:R)", Order = 1, GroupName = "2. Profit/Loss Ticks")]
        public int DefaultTickProfit
        {
            get { return defaultTickProfit; }
            set { defaultTickProfit = Math.Max(0, value); }
        }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "P/L Tick Loss (0=Use R:R)", Order = 2, GroupName = "2. Profit/Loss Ticks")]
        public int DefaultTickLoss
        {
            get { return defaultTickLoss; }
            set { defaultTickLoss = Math.Max(0, value); }
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 1, GroupName = "3. No Trade Zone - EMA")]
        public int EmaPeriod
        {
            get { return emaPeriod; }
            set { emaPeriod = Math.Max(1, value); }
        }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "EMA Tick Band", Order = 2, GroupName = "3. No Trade Zone - EMA")]
        public int EmaTickBand
        {
            get { return emaTickBand; }
            set { emaTickBand = Math.Max(0, value); }
        }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "VWAP Tick Band (Requires VWAP Indicator)", Order = 1, GroupName = "4. No Trade Zone - VWAP")]
        public int VwapTickBand
        {
            get { return vwapTickBand; }
            set { vwapTickBand = Math.Max(0, value); }
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Trailing Stop Ticks", Order = 1, GroupName = "5. Trailing Stops")]
        public int TrailingStopTicks
        {
            get { return trailingStopTicks; }
            set { trailingStopTicks = Math.Max(1, value); }
        }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Bar N Trailing Ticks (Offset)", Order = 2, GroupName = "5. Trailing Stops")]
        public int BarNTrailTicks
        {
            get { return barNTrailTicks; }
            set { barNTrailTicks = Math.Max(0, value); }
        }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Consecutive Losers (0=Off)", Order = 1, GroupName = "6. Risk Management")]
        public int MaxTradeLosers
        {
            get { return maxTradeLosers; }
            set { maxTradeLosers = Math.Max(0, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max Daily Profit (Currency, 0=Off)", Order = 2, GroupName = "6. Risk Management")]
        public double MaxDailyProfit
        {
            get { return maxDailyProfit; }
            set { maxDailyProfit = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max Daily Loss (Currency, 0=Off)", Order = 3, GroupName = "6. Risk Management")]
        public double MaxDailyLoss
        {
            get { return maxDailyLoss; }
            set { maxDailyLoss = Math.Max(0.0, value); }
        }

        #endregion
    }
}
