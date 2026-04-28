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
    public class AdxDiCrossStrategyv4 : Strategy
    {
        // ===== Enums =====
        public enum TradeBias { Both, LongOnly, ShortOnly }

        // ===== Parameters =====

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Contracts", GroupName = "1. Orders", Order = 0)]
        public int Contracts { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "Trade Bias", GroupName = "1. Orders", Order = 1)]
        public TradeBias Bias { get; set; } = TradeBias.Both;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ADX Period", GroupName = "2. ADX / DI", Order = 0)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(1, double.MaxValue)]
        [Display(Name = "ADX Min (Entry Filter)", GroupName = "2. ADX / DI", Order = 1)]
        public double AdxMin { get; set; } = 20.0;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "DI Period", GroupName = "2. ADX / DI", Order = 2)]
        public int DiPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Display(Name = "Use Stop X (Level Range)", GroupName = "2. ADX / DI", Order = 3)]
        public bool UseStopX { get; set; } = true;

        [NinjaScriptProperty, Range(1, double.MaxValue)]
        [Display(Name = "Stop X Level Range", GroupName = "2. ADX / DI", Order = 4)]
        public double StopXLevelRange { get; set; } = 18.0;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Stop (ticks)", GroupName = "3. Brackets", Order = 0)]
        public int StopTicks { get; set; } = 20;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Target1 (ticks)", GroupName = "3. Brackets", Order = 1)]
        public int Target1Ticks { get; set; } = 20;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "Target2 (ticks)", GroupName = "3. Brackets", Order = 2)]
        public int Target2Ticks { get; set; } = 40;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "T1 Contracts", GroupName = "3. Brackets", Order = 3)]
        public int T1Contracts { get; set; } = 1;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "T2 Contracts", GroupName = "3. Brackets", Order = 4)]
        public int T2Contracts { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Use Session Filter", GroupName = "4. Session", Order = 0)]
        public bool UseSessionFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Session Start (HH:mm)", GroupName = "4. Session", Order = 1)]
        public string SessionStart { get; set; } = "09:30";

        [NinjaScriptProperty]
        [Display(Name = "Session End (HH:mm)", GroupName = "4. Session", Order = 2)]
        public string SessionEnd { get; set; } = "16:00";

        [NinjaScriptProperty]
        [Display(Name = "Max Trades Per Day", GroupName = "5. Risk", Order = 0)]
        public int MaxTradesPerDay { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name = "Daily PnL Limit (negative to disable)", GroupName = "5. Risk", Order = 1)]
        public double DailyLossLimit { get; set; } = -1000.0;

        // ===== Internals =====
        private ADX adx;
        private DM dm;          // DM gives us DiPlus / DiMinus
        private int tradesToday;
        private double startingDailyCumProfit;
        private DateTime currentDay;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "AdxDiCrossStrategyv4";
                Calculate   = Calculate.OnBarClose;
                EntriesPerDirection = 2;
                EntryHandling       = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.Configure)
            {
                // no special Configure logic
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(AdxPeriod);
                dm  = DM(DiPeriod);

                currentDay = Time[0].Date;
                startingDailyCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                tradesToday = 0;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(AdxPeriod, DiPeriod))
                return;

            // ===== Daily reset =====
            if (Time[0].Date != currentDay)
            {
                currentDay = Time[0].Date;
                startingDailyCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                tradesToday = 0;
            }

            // ===== Daily loss limit =====
            if (DailyLossLimit < 0)
            {
                double dailyPnl = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - startingDailyCumProfit;
                if (dailyPnl <= DailyLossLimit)
                    return;
            }

            // ===== Max trades per day =====
            if (tradesToday >= MaxTradesPerDay)
                return;

            // ===== Session filter =====
            if (UseSessionFilter && !IsWithinSession(Time[0]))
                return;

            // ===== Stop X: kill trade if ADX wobble (Level Range) =====
            if (UseStopX && Position.MarketPosition != MarketPosition.Flat)
            {
                if (adx[0] < StopXLevelRange)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        ExitLong("StopX_T1", "LongEntry_T1");
                        ExitLong("StopX_T2", "LongEntry_T2");
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        ExitShort("StopX_T1", "ShortEntry_T1");
                        ExitShort("StopX_T2", "ShortEntry_T2");
                    }
                    return; // don't enter new trades on the same bar
                }
            }

            // Already in a position? Let brackets + StopX handle it
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // ADX threshold for *entry*
            if (adx[0] < AdxMin)
                return;

            // ===== DI cross logic via DM indicator =====
            // NOTE: correct casing => DiPlus / DiMinus
            bool longSignal  = CrossAbove(dm.DiPlus,  dm.DiMinus, 1);
            bool shortSignal = CrossAbove(dm.DiMinus, dm.DiPlus,  1);

            // Respect trade bias
            if (Bias == TradeBias.LongOnly)
                shortSignal = false;
            else if (Bias == TradeBias.ShortOnly)
                longSignal = false;

            int totalContracts = T1Contracts + T2Contracts;
            if (totalContracts <= 0 || totalContracts > Contracts)
                totalContracts = Contracts;

            if (longSignal)
            {
                SubmitBracket(true, totalContracts);
            }
            else if (shortSignal)
            {
                SubmitBracket(false, totalContracts);
            }
        }

        private void SubmitBracket(bool isLong, int qty)
        {
            string entrySignal1 = isLong ? "LongEntry_T1" : "ShortEntry_T1";
            string entrySignal2 = isLong ? "LongEntry_T2" : "ShortEntry_T2";

            // Clear and set stops/targets
            SetStopLoss(entrySignal1, CalculationMode.Ticks, StopTicks, false);
            SetStopLoss(entrySignal2, CalculationMode.Ticks, StopTicks, false);

            if (Target1Ticks > 0 && T1Contracts > 0)
                SetProfitTarget(entrySignal1, CalculationMode.Ticks, Target1Ticks);

            if (Target2Ticks > 0 && T2Contracts > 0)
                SetProfitTarget(entrySignal2, CalculationMode.Ticks, Target2Ticks);

            int qtyT1 = Math.Min(T1Contracts, qty);
            int qtyT2 = Math.Max(0, qty - qtyT1);

            if (qtyT1 + qtyT2 == 0)
                qtyT1 = qty; // fall back: all contracts on T1

            if (isLong)
            {
                if (qtyT1 > 0)
                    EnterLong(qtyT1, entrySignal1);
                if (qtyT2 > 0)
                    EnterLong(qtyT2, entrySignal2);
            }
            else
            {
                if (qtyT1 > 0)
                    EnterShort(qtyT1, entrySignal1);
                if (qtyT2 > 0)
                    EnterShort(qtyT2, entrySignal2);
            }

            tradesToday++;
        }

        private bool IsWithinSession(DateTime time)
        {
            TimeSpan start, end;
            if (!TimeSpan.TryParse(SessionStart, out start))
                start = new TimeSpan(9, 30, 0);

            if (!TimeSpan.TryParse(SessionEnd, out end))
                end = new TimeSpan(16, 0, 0);

            var t = time.TimeOfDay;

            if (end > start)
                return t >= start && t <= end;

            // Overnight-style window (wraps past midnight)
            return t >= start || t <= end;
        }
    }
}
