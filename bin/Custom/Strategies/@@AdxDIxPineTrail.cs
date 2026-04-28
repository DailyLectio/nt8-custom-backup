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
    public class AdxDIxPineTrail : Strategy
    {
        // ===== Enums =====
        public enum StopMode
        {
            AtrStatic,
            TickTrailing,
            BarNTrailing
        }

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

        // ----- Trailing stop options -----

        [NinjaScriptProperty]
        [Display(Name = "Stop Mode", GroupName = "4. Stops", Order = 0)]
        public StopMode StopModeSelection { get; set; } = StopMode.AtrStatic;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Trail Ticks (TickTrailing)", GroupName = "4. Stops", Order = 1)]
        public int TrailTicks { get; set; } = 8;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Trail Start Bars", GroupName = "4. Stops", Order = 2)]
        public int TrailStartBars { get; set; } = 1;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "BarN N Bars", GroupName = "4. Stops", Order = 3)]
        public int TrailingNBars { get; set; } = 1;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "BarN Offset (ticks)", GroupName = "4. Stops", Order = 4)]
        public int TrailingOffsetTicks { get; set; } = 0;

        // ----- EMA 50 bias + no-trade zone -----

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Bias (Longs above / Shorts below)", GroupName = "5. EMA Filter", Order = 0)]
        public bool UseEmaBias { get; set; } = false;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", GroupName = "5. EMA Filter", Order = 1)]
        public int EmaPeriod { get; set; } = 50;

        [NinjaScriptProperty]
        [Display(Name = "Use EMA No-Trade Zone", GroupName = "5. EMA Filter", Order = 2)]
        public bool UseEmaNoTradeZone { get; set; } = false;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "EMA Zone Width (ticks)", GroupName = "5. EMA Filter", Order = 3)]
        public int EmaZoneTicks { get; set; } = 8;

        // ===== Internals =====
        private ATR atr;
        private EMA emaFilter;

        private Series<double> plusDM;
        private Series<double> minusDM;
        private Series<double> trSeries;
        private Series<double> diPlus;
        private Series<double> diMinus;
        private Series<double> sig;      // ADX-like "sig"

        private double trailingStopLong = double.NaN;
        private double trailingStopShort = double.NaN;

        private int pendingLongStopTicks;
        private int pendingShortStopTicks;

        private double RT(double price) =>
            Instrument.MasterInstrument.RoundToTickSize(price);

        private double BarNStopLong()
        {
            double lo = Low[0];
            for (int i = 1; i < TrailingNBars && i <= CurrentBar; i++)
                lo = Math.Min(lo, Low[i]);
            return RT(lo - TrailingOffsetTicks * TickSize);
        }

        private double BarNStopShort()
        {
            double hi = High[0];
            for (int i = 1; i < TrailingNBars && i <= CurrentBar; i++)
                hi = Math.Max(hi, High[i]);
            return RT(hi + TrailingOffsetTicks * TickSize);
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AdxDIxPineTrail";
                Calculate = Calculate.OnBarClose;   // match Pine timing
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.DataLoaded)
            {
                atr       = ATR(AtrLength);
                emaFilter = EMA(EmaPeriod);

                plusDM    = new Series<double>(this);
                minusDM   = new Series<double>(this);
                trSeries  = new Series<double>(this);
                diPlus    = new Series<double>(this);
                diMinus   = new Series<double>(this);
                sig       = new Series<double>(this);

                AddChartIndicator(emaFilter);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar == 0)
                return;

            // ===== f_dirMov equivalent (Gu5) =====
            double up   = High[0] - High[1];
            double down = Low[1] - Low[0];

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
                plusDM[0]   = plusDM[1]   + (plusDMRaw  - plusDM[1])   / DiLength;
                minusDM[0]  = minusDM[1]  + (minusDMRaw - minusDM[1])  / DiLength;
                trSeries[0] = trSeries[1] + (trueRange  - trSeries[1]) / DiLength;
            }

            double trVal = trSeries[0];
            if (trVal.ApproxCompare(0.0) == 0)
                trVal = 1.0;

            diPlus[0]  = 100.0 * plusDM[0]  / trVal;
            diMinus[0] = 100.0 * minusDM[0] / trVal;

            // ===== f_sig equivalent (ADX "sig") =====
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

            // ===== ATR-based stops/targets (baseline) =====
            double atrVal = atr[0];
            if (atrVal <= 0)
                return;

            double stopTicksD   = StopAtrMult * atrVal / TickSize;
            int    stopTicks    = Math.Max(1, (int)Math.Round(stopTicksD));
            int    profitTicks  = Math.Max(1, (int)Math.Round(stopTicksD * RMultiple));

            // Baseline R:R exits (like Pine)
            SetStopLoss("Long",  CalculationMode.Ticks, stopTicks,   false);
            SetProfitTarget("Long",  CalculationMode.Ticks, profitTicks);
            SetStopLoss("Short", CalculationMode.Ticks, stopTicks,   false);
            SetProfitTarget("Short", CalculationMode.Ticks, profitTicks);

            // ===== Entry conditions: DI cross + sig > level =====
            bool longSignal  = diPlus[1] <= diMinus[1] && diPlus[0] > diMinus[0] && sig[0] > LevelRange;
            bool shortSignal = diPlus[1] >= diMinus[1] && diPlus[0] < diMinus[0] && sig[0] > LevelRange;

            // ===== STOP X conditions =====
            bool exitLongSignal  = diPlus[1] >= diMinus[1] && diPlus[0] < diMinus[0] ||
                                   (sig[0] <= LevelRange && sig[1] > LevelRange);

            bool exitShortSignal = diPlus[1] <= diMinus[1] && diPlus[0] > diMinus[0] ||
                                   (sig[0] <= LevelRange && sig[1] > LevelRange);

            // ===== EMA bias + no-trade zone =====
            bool passEmaLong  = !UseEmaBias || Close[0] > emaFilter[0];
            bool passEmaShort = !UseEmaBias || Close[0] < emaFilter[0];

            double emaDist   = Math.Abs(Close[0] - emaFilter[0]);
            double zoneWidth = EmaZoneTicks * TickSize;
            bool outsideZone = !UseEmaNoTradeZone || emaDist >= zoneWidth;

            bool canLong  = longSignal  && passEmaLong  && outsideZone;
            bool canShort = shortSignal && passEmaShort && outsideZone;

            // ===== STOP X exits =====
            if (ExitOnStopX && Position.MarketPosition == MarketPosition.Long && exitLongSignal)
            {
                ExitLong("StopXLong", "Long");
                trailingStopLong = double.NaN;
                return;
            }

            if (ExitOnStopX && Position.MarketPosition == MarketPosition.Short && exitShortSignal)
            {
                ExitShort("StopXShort", "Short");
                trailingStopShort = double.NaN;
                return;
            }

            // ===== Entries (flat only) =====
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                trailingStopLong  = double.NaN;
                trailingStopShort = double.NaN;

                if (canLong)
                {
                    pendingLongStopTicks = stopTicks;
                    EnterLong(Contracts, "Long");
                }
                else if (canShort)
                {
                    pendingShortStopTicks = stopTicks;
                    EnterShort(Contracts, "Short");
                }
            }

            // ===== Trailing stop management =====

            // --- Long side ---
            if (Position.MarketPosition == MarketPosition.Long)
            {
                int bseLong = BarsSinceEntryExecution(0, "Long", 0);

                if (bseLong == 0)
                {
                    double baseStop = Position.AveragePrice - pendingLongStopTicks * TickSize;
                    trailingStopLong = baseStop;
                    SetStopLoss("Long", CalculationMode.Price, trailingStopLong, false);
                }

                if (StopModeSelection == StopMode.TickTrailing && bseLong >= TrailStartBars)
                {
                    double candidate = RT(Close[0] - TrailTicks * TickSize);
                    if (double.IsNaN(trailingStopLong) || candidate > trailingStopLong)
                        trailingStopLong = candidate;

                    SetStopLoss("Long", CalculationMode.Price, trailingStopLong, false);
                }
                else if (StopModeSelection == StopMode.BarNTrailing &&
                         bseLong >= TrailStartBars && CurrentBar >= TrailingNBars - 1)
                {
                    double candidate = BarNStopLong();
                    if (double.IsNaN(trailingStopLong) || candidate > trailingStopLong)
                        trailingStopLong = candidate;

                    SetStopLoss("Long", CalculationMode.Price, trailingStopLong, false);
                }
            }

            // --- Short side ---
            if (Position.MarketPosition == MarketPosition.Short)
            {
                int bseShort = BarsSinceEntryExecution(0, "Short", 0);

                if (bseShort == 0)
                {
                    double baseStop = Position.AveragePrice + pendingShortStopTicks * TickSize;
                    trailingStopShort = baseStop;
                    SetStopLoss("Short", CalculationMode.Price, trailingStopShort, false);
                }

                if (StopModeSelection == StopMode.TickTrailing && bseShort >= TrailStartBars)
                {
                    double candidate = RT(Close[0] + TrailTicks * TickSize);
                    if (double.IsNaN(trailingStopShort) || candidate < trailingStopShort)
                        trailingStopShort = candidate;

                    SetStopLoss("Short", CalculationMode.Price, trailingStopShort, false);
                }
                else if (StopModeSelection == StopMode.BarNTrailing &&
                         bseShort >= TrailStartBars && CurrentBar >= TrailingNBars - 1)
                {
                    double candidate = BarNStopShort();
                    if (double.IsNaN(trailingStopShort) || candidate < trailingStopShort)
                        trailingStopShort = candidate;

                    SetStopLoss("Short", CalculationMode.Price, trailingStopShort, false);
                }
            }
        }
    }
}
