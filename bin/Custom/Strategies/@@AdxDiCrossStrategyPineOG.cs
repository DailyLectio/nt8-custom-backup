// CC BY-NC 4.0
#region Using
using System;
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
    // Direct port of your TradingView "ADX DI Cross Strategy" (Gu5-based)
    public class AdxDiCrossStrategy_PineOG : Strategy
    {
        // ===== Parameters =====

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Contracts", GroupName = "1. Orders", Order = 0)]
        public int Contracts { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Exit on STOP X", GroupName = "1. Orders", Order = 1)]
        public bool ExitOnStopX { get; set; } = true;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "R Multiple (1.5 / 2 / 3)", GroupName = "2. Risk/Reward", Order = 0)]
        public double RMultiple { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Length", GroupName = "2. Risk/Reward", Order = 1)]
        public int AtrLength { get; set; } = 14;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "ATR Stop Mult", GroupName = "2. Risk/Reward", Order = 2)]
        public double StopAtrMult { get; set; } = 1.0;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "DI Length (i_diLen)", GroupName = "3. ADX / DI", Order = 0)]
        public int DiLength { get; set; } = 14;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ADX Smoothing (i_sigLen)", GroupName = "3. ADX / DI", Order = 1)]
        public int AdxSmoothing { get; set; } = 14;

        [NinjaScriptProperty, Range(1, double.MaxValue)]
        [Display(Name = "Level Range (i_hlRange)", GroupName = "3. ADX / DI", Order = 2)]
        public double LevelRange { get; set; } = 20.0;

        // ===== Internals =====
        private ATR atr;

        private Series<double> plusDM;
        private Series<double> minusDM;
        private Series<double> trSeries;
        private Series<double> diPlus;
        private Series<double> diMinus;
        private Series<double> sig;      // ADX-like "sig" from Gu5 logic

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AdxDiCrossStrategy_PineOG";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.Configure)
            {
                // nothing special
            }
            else if (State == State.DataLoaded)
            {
                atr       = ATR(AtrLength);
                plusDM    = new Series<double>(this);
                minusDM   = new Series<double>(this);
                trSeries  = new Series<double>(this);
                diPlus    = new Series<double>(this);
                diMinus   = new Series<double>(this);
                sig       = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar == 0)
                return;

            // ===== f_dirMov equivalent =====
            double up   = High[0] - High[1];              // ta.change(high)
            double down = Low[1] - Low[0];                // -ta.change(low)  -> down is positive when low decreases

            double plusDMRaw  = (up   > down && up   > 0) ? up   : 0.0;
            double minusDMRaw = (down > up   && down > 0) ? down : 0.0;

            double trueRange = Math.Max(High[0] - Low[0],
                                Math.Max(Math.Abs(High[0] - Close[1]),
                                         Math.Abs(Low[0]  - Close[1])));

            if (CurrentBar == 1)
            {
                plusDM[0]   = plusDMRaw;
                minusDM[0]  = minusDMRaw;
                trSeries[0] = trueRange;
            }
            else
            {
                // Wilder RMA: prev + (curr - prev) / len
                plusDM[0]   = plusDM[1]   + (plusDMRaw  - plusDM[1])   / DiLength;
                minusDM[0]  = minusDM[1]  + (minusDMRaw - minusDM[1])  / DiLength;
                trSeries[0] = trSeries[1] + (trueRange  - trSeries[1]) / DiLength;
            }

            double trVal = trSeries[0];
            if (trVal.ApproxCompare(0.0) == 0)
                trVal = 1.0;

            diPlus[0]  = 100.0 * plusDM[0]  / trVal;
            diMinus[0] = 100.0 * minusDM[0] / trVal;

            // ===== f_sig equivalent (Gu5 ADX) =====
            double sum = diPlus[0] + diMinus[0];
            double dx  = (sum.ApproxCompare(0.0) == 0)
                         ? 0.0
                         : Math.Abs(diPlus[0] - diMinus[0]) / sum;

            double dxScaled = 100.0 * dx;

            if (CurrentBar == 1)
                sig[0] = dxScaled;
            else
                sig[0] = sig[1] + (dxScaled - sig[1]) / AdxSmoothing;

            if (CurrentBar < Math.Max(DiLength, AdxSmoothing) + 2)
                return;

            // ===== ATR-based stop/target in ticks =====
            double atrVal = atr[0];
            if (atrVal <= 0)
                return;

            double stopTicksD   = StopAtrMult * atrVal / TickSize;
            int    stopTicks    = Math.Max(1, (int)Math.Round(stopTicksD));
            int    profitTicks  = Math.Max(1, (int)Math.Round(stopTicksD * RMultiple));

            // Attach stop & target (like strategy.exit)
            SetStopLoss("Long",  CalculationMode.Ticks, stopTicks,   false);
            SetProfitTarget("Long",  CalculationMode.Ticks, profitTicks);
            SetStopLoss("Short", CalculationMode.Ticks, stopTicks,   false);
            SetProfitTarget("Short", CalculationMode.Ticks, profitTicks);

            // ===== Entry conditions: DI cross with sig > level =====
            bool longSignal  = diPlus[1] <= diMinus[1] && diPlus[0] > diMinus[0] && sig[0] > LevelRange;
            bool shortSignal = diPlus[1] >= diMinus[1] && diPlus[0] < diMinus[0] && sig[0] > LevelRange;

            // ===== STOP X conditions =====
            bool exitLongSignal  = diPlus[1] >= diMinus[1] && diPlus[0] < diMinus[0] ||
                                   (sig[0] <= LevelRange && sig[1] > LevelRange);

            bool exitShortSignal = diPlus[1] <= diMinus[1] && diPlus[0] > diMinus[0] ||
                                   (sig[0] <= LevelRange && sig[1] > LevelRange);

            // ===== Optional STOP X exits =====
            if (ExitOnStopX && Position.MarketPosition == MarketPosition.Long && exitLongSignal)
            {
                ExitLong("StopXLong", "Long");
                return; // don't re-enter same bar
            }

            if (ExitOnStopX && Position.MarketPosition == MarketPosition.Short && exitShortSignal)
            {
                ExitShort("StopXShort", "Short");
                return;
            }

            // ===== Entries =====
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (longSignal)
                    EnterLong(Contracts, "Long");
                else if (shortSignal)
                    EnterShort(Contracts, "Short");
            }
        }
    }
}
