#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ADXGu5Strategy : Strategy
    {
        // ===== Inputs: Position sizing =====
        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Regular Contracts", GroupName = "1. Position Sizing", Order = 0)]
        public int RegularContracts { get; set; } = 1;

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Strong Contracts", GroupName = "1. Position Sizing", Order = 1)]
        public int StrongContracts { get; set; } = 2;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Ticks", GroupName = "1. Position Sizing", Order = 2)]
        public int StopTicks { get; set; } = 40;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Target Ticks", GroupName = "1. Position Sizing", Order = 3)]
        public int TargetTicks { get; set; } = 60;

        // ===== Inputs: ADX / DI parameters (match Gu5) =====
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ADX Smoothing (SigLen)", GroupName = "2. ADX / DI", Order = 0)]
        public int SigLen { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "DI Length (DiLen)", GroupName = "2. ADX / DI", Order = 1)]
        public int DiLen { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Level Range (HlRange)", GroupName = "2. ADX / DI", Order = 2)]
        public int HlRange { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Level Trend (HlTrend)", GroupName = "2. ADX / DI", Order = 3)]
        public int HlTrend { get; set; } = 35;

        // ===== Internal indicators =====
        private DM dm;
        private ADX adx;
        private ADXGu5 adxGu5;

        // ===== Pending setup flags =====
        private bool longSetupPending = false;
        private bool shortSetupPending = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ADXGu5Strategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.Configure)
            {
                // Apply stops / targets for each signal name
                // Long
                SetStopLoss("Long",       CalculationMode.Ticks, StopTicks, false);
                SetProfitTarget("Long",   CalculationMode.Ticks, TargetTicks);
                SetStopLoss("LongStrong", CalculationMode.Ticks, StopTicks, false);
                SetProfitTarget("LongStrong", CalculationMode.Ticks, TargetTicks);

                // Short
                SetStopLoss("Short",       CalculationMode.Ticks, StopTicks, false);
                SetProfitTarget("Short",   CalculationMode.Ticks, TargetTicks);
                SetStopLoss("ShortStrong", CalculationMode.Ticks, StopTicks, false);
                SetProfitTarget("ShortStrong", CalculationMode.Ticks, TargetTicks);
            }
            else if (State == State.DataLoaded)
            {
                dm     = DM(DiLen);
                adx    = ADX(SigLen);
                adxGu5 = ADXGu5(SigLen, DiLen, HlRange, HlTrend);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(SigLen, DiLen) + 2)
                return;

            double diPlus     = dm.DiPlus[0];
            double diMinus    = dm.DiMinus[0];
            double diPlusPrev = dm.DiPlus[1];
            double diMinusPrev = dm.DiMinus[1];

            double adxNow  = adx[0];
            double adxPrev = adx[1];

            bool adxGate = adxNow > HlRange && adxNow > adxPrev; // ADX above 20 and rising

            bool diPlusDominant  = diPlus > diMinus;
            bool diMinusDominant = diMinus > diPlus;

            bool diCrossUp   = diPlus > diMinus && diPlusPrev <= diMinusPrev;
            bool diCrossDown = diMinus > diPlus && diMinusPrev <= diPlusPrev;

            // ===== Kill switch: Gu5 gold cross =====
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (adxGu5.LongXSeries[0] > 0 || adxGu5.ShortXSeries[0] > 0)
                {
                    ExitLong();
                    longSetupPending = false;
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (adxGu5.ShortXSeries[0] > 0 || adxGu5.LongXSeries[0] > 0)
                {
                    ExitShort();
                    shortSetupPending = false;
                }
            }

            // ===== Manage pending setups when flat =====
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                // 1) Update pending flags based on fresh crosses
                if (diCrossUp)
                {
                    longSetupPending = true;
                    shortSetupPending = false; // cancel opposite
                }
                else if (diCrossDown)
                {
                    shortSetupPending = true;
                    longSetupPending = false;
                }

                // If DI ordering has flipped back, cancel pending
                if (longSetupPending && !diPlusDominant)
                    longSetupPending = false;
                if (shortSetupPending && !diMinusDominant)
                    shortSetupPending = false;

                // 2) Fire entries once ADX confirms + DI ordering still valid

                // ---- Long side ----
                if (longSetupPending && adxGate && diPlusDominant)
                {
                    bool strongLong =
                        diPlus >= HlTrend || adxNow >= HlTrend;

                    int qty = strongLong ? StrongContracts : RegularContracts;

                    if (qty > 0)
                    {
                        if (strongLong)
                            EnterLong(qty, "LongStrong");
                        else
                            EnterLong(qty, "Long");
                    }

                    longSetupPending = false;
                }

                // ---- Short side ----
                if (shortSetupPending && adxGate && diMinusDominant)
                {
                    bool strongShort =
                        diMinus >= HlTrend || adxNow >= HlTrend;

                    int qty = strongShort ? StrongContracts : RegularContracts;

                    if (qty > 0)
                    {
                        if (strongShort)
                            EnterShort(qty, "ShortStrong");
                        else
                            EnterShort(qty, "Short");
                    }

                    shortSetupPending = false;
                }
            }
        }
    }
}
