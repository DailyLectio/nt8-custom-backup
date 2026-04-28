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
    /// <summary>
    /// ADX Renko v1 — single-leg, EMA trailing only.
    /// Designed for UniRenko 10/20/40 with EMA(50) alignment and ADX/DI filters.
    /// Time windows default to RTH AM/PM (and optional London/Premarket) with open/lunch/news blackouts.
    /// </summary>
    public class AdxRenkoV1 : Strategy
    {
        // ======== Stops (single mode: EMA trailing) ========
        private ADX adx;
        private ATR atr;
        private EMA emaAlign;  // EMA used for alignment (typically 50)
        private EMA emaTrail;  // EMA used for trailing (typically 10)

        // Wilder-style internal DI (to avoid missing DIPlus/DIMinus refs)
        private Series<double> dmPlus, dmMinus, sumDmPlus, sumDmMinus, sumTr, diPlusSeries, diMinusSeries;

        // Trailing state
        private double trailingStopLong  = double.NaN;
        private double trailingStopShort = double.NaN;

        // Bookkeeping
        private int lastEntryBar = int.MinValue;
        private int todaysDate   = -1;
        private int amCount      = 0;
        private int pmCount      = 0;
        private int dayCount     = 0;

        // ======== PARAMETERS ========

        // Position sizing / targets
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Contracts", GroupName = "Parameters", Order = 1)]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "Risk Reward (for targets)", GroupName = "Parameters", Order = 2)]
        public double RiskReward { get; set; } = 2.0; // You’ve tested up to 3.0 successfully

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", GroupName = "Parameters", Order = 3)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "ATR Multiplier (seed stop)", GroupName = "Parameters", Order = 4)]
        public double AtrMultiplier { get; set; } = 0.9;  // 0.9–1.0 works well on Uni 10/20/40

        // Entry quality gates
        [NinjaScriptProperty]
        [Display(Name = "Enable entry filters", GroupName = "Entry Filters", Order = 0)]
        public bool EnableEntryFilters { get; set; } = true;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ADX Period", GroupName = "Entry Filters", Order = 1)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "ADX min level", GroupName = "Entry Filters", Order = 2)]
        public double AdxMin { get; set; } = 23.0;

        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "ADX rising bars", GroupName = "Entry Filters", Order = 3)]
        public int AdxRisingBars { get; set; } = 3;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "Min DI gap", GroupName = "Entry Filters", Order = 4)]
        public double MinDiGap { get; set; } = 6.0;

        // EMA alignment (industry standard)
        [NinjaScriptProperty]
        [Display(Name = "Use EMA alignment", GroupName = "Entry Filters", Order = 5)]
        public bool UseEmaAlignment { get; set; } = true;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "EMA align period", GroupName = "Entry Filters", Order = 6)]
        public int EmaAlignPeriod { get; set; } = 50;

        // EMA trailing
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "EMA trail period", GroupName = "Stops - EMA Trailing", Order = 10)]
        public int EmaTrailPeriod { get; set; } = 10;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "EMA trail offset (ticks)", GroupName = "Stops - EMA Trailing", Order = 11)]
        public int EmaTrailOffsetTicks { get; set; } = 6;  // move to 8 if you still get nibbled

        [NinjaScriptProperty, Range(1, 10)]
        [Display(Name = "Trail switch N bars (delay)", GroupName = "Stops - EMA Trailing", Order = 12)]
        public int TrailSwitchNBars { get; set; } = 2;

        // Optional ADX/DI exit (Stop X) — default true for safety; can disable
        [NinjaScriptProperty]
        [Display(Name = "Use Stop X (ADX/DI exit)", GroupName = "Stops - EMA Trailing", Order = 13)]
        public bool UseStopX { get; set; } = true;

        // Time filters
        [NinjaScriptProperty]
        [Display(Name = "Enable time filters", GroupName = "Time Filters", Order = 0)]
        public bool EnableTimeFilters { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Clock offset (minutes)", GroupName = "Time Filters", Order = 1)]
        public int ClockOffsetMinutes { get; set; } = 0;  // if you need to nudge to ET

        [NinjaScriptProperty]
        [Display(Name = "Allow London window", GroupName = "Time Filters", Order = 10)]
        public bool AllowLondon { get; set; } = false;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "London start HHmm", GroupName = "Time Filters", Order = 11)]
        public int LondonStartHHmm { get; set; } = 200;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "London end HHmm", GroupName = "Time Filters", Order = 12)]
        public int LondonEndHHmm { get; set; } = 500;

        [NinjaScriptProperty]
        [Display(Name = "Allow Premarket", GroupName = "Time Filters", Order = 20)]
        public bool AllowPremarket { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "Premkt start HHmm", GroupName = "Time Filters", Order = 21)]
        public int PremktStartHHmm { get; set; } = 800;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "Premkt end HHmm", GroupName = "Time Filters", Order = 22)]
        public int PremktEndHHmm { get; set; } = 930;

        [NinjaScriptProperty]
        [Display(Name = "Allow RTH AM", GroupName = "Time Filters", Order = 30)]
        public bool AllowRthAM { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "RTH AM start HHmm", GroupName = "Time Filters", Order = 31)]
        public int RthAMStartHHmm { get; set; } = 940;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "RTH AM end HHmm", GroupName = "Time Filters", Order = 32)]
        public int RthAMEndHHmm { get; set; } = 1120;

        [NinjaScriptProperty]
        [Display(Name = "Allow RTH PM", GroupName = "Time Filters", Order = 40)]
        public bool AllowRthPM { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "RTH PM start HHmm", GroupName = "Time Filters", Order = 41)]
        public int RthPMStartHHmm { get; set; } = 1330;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "RTH PM end HHmm", GroupName = "Time Filters", Order = 42)]
        public int RthPMEndHHmm { get; set; } = 1550;

        // Blackouts
        [NinjaScriptProperty]
        [Display(Name = "Block open", GroupName = "Time Filters", Order = 50)]
        public bool BlockOpen { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "Open block start HHmm", GroupName = "Time Filters", Order = 51)]
        public int OpenBlockStartHHmm { get; set; } = 930;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "Open block end HHmm", GroupName = "Time Filters", Order = 52)]
        public int OpenBlockEndHHmm { get; set; } = 939;

        [NinjaScriptProperty]
        [Display(Name = "Block lunch", GroupName = "Time Filters", Order = 53)]
        public bool BlockLunch { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "Lunch start HHmm", GroupName = "Time Filters", Order = 54)]
        public int LunchStartHHmm { get; set; } = 1130;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "Lunch end HHmm", GroupName = "Time Filters", Order = 55)]
        public int LunchEndHHmm { get; set; } = 1320;

        [NinjaScriptProperty]
        [Display(Name = "Custom blackout 1 on", GroupName = "Time Filters", Order = 60)]
        public bool CustomBlackout1 { get; set; } = true;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "Blk1 start HHmm", GroupName = "Time Filters", Order = 61)]
        public int Blk1StartHHmm { get; set; } = 958; // 9:58 – 10:03 (10:00 news splash)
        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "Blk1 end HHmm", GroupName = "Time Filters", Order = 62)]
        public int Blk1EndHHmm { get; set; } = 1003;

        [NinjaScriptProperty]
        [Display(Name = "Custom blackout 2 on", GroupName = "Time Filters", Order = 63)]
        public bool CustomBlackout2 { get; set; } = false;

        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "Blk2 start HHmm", GroupName = "Time Filters", Order = 64)]
        public int Blk2StartHHmm { get; set; } = 1600;
        [NinjaScriptProperty, Range(0, 2359)]
        [Display(Name = "Blk2 end HHmm", GroupName = "Time Filters", Order = 65)]
        public int Blk2EndHHmm { get; set; } = 1800;

        // Anti-overtrade
        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name = "Cooldown (bars between entries)", GroupName = "Risk Controls", Order = 1)]
        public int MinBarsBetweenEntries { get; set; } = 3;

        [NinjaScriptProperty, Range(0, 50)]
        [Display(Name = "Max trades per AM window (0=off)", GroupName = "Risk Controls", Order = 2)]
        public int MaxTradesPerAM { get; set; } = 6;

        [NinjaScriptProperty, Range(0, 50)]
        [Display(Name = "Max trades per PM window (0=off)", GroupName = "Risk Controls", Order = 3)]
        public int MaxTradesPerPM { get; set; } = 6;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Max trades per day (0=off)", GroupName = "Risk Controls", Order = 4)]
        public int MaxTradesPerDay { get; set; } = 12;

        // ======== Helpers ========
        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        private int NowHHmm()
        {
            // Convert bar time to HHmm with optional minute offset
            DateTime t = Time[0].AddMinutes(ClockOffsetMinutes);
            return t.Hour * 100 + t.Minute;
        }

        private static bool InRange(int t, int startHHmm, int endHHmm)
        {
            // Simple intraday range (same-day, no wrap)
            return t >= startHHmm && t <= endHHmm;
        }

        private bool AllowedTime()
        {
            if (!EnableTimeFilters) return true;

            int t = NowHHmm();

            bool allowed =
                (AllowLondon   && InRange(t, LondonStartHHmm, LondonEndHHmm))   ||
                (AllowPremarket&& InRange(t, PremktStartHHmm,  PremktEndHHmm))  ||
                (AllowRthAM    && InRange(t, RthAMStartHHmm,  RthAMEndHHmm))    ||
                (AllowRthPM    && InRange(t, RthPMStartHHmm,  RthPMEndHHmm));

            if (!allowed) return false;

            // Blackouts override allowed
            if (BlockOpen      && InRange(t, OpenBlockStartHHmm,  OpenBlockEndHHmm)) return false;
            if (BlockLunch     && InRange(t, LunchStartHHmm,      LunchEndHHmm))     return false;
            if (CustomBlackout1&& InRange(t, Blk1StartHHmm,       Blk1EndHHmm))       return false;
            if (CustomBlackout2&& InRange(t, Blk2StartHHmm,       Blk2EndHHmm))       return false;

            return true;
        }

        private bool WindowIsAM()
        {
            int t = NowHHmm();
            return AllowRthAM && InRange(t, RthAMStartHHmm, RthAMEndHHmm);
        }
        private bool WindowIsPM()
        {
            int t = NowHHmm();
            return AllowRthPM && InRange(t, RthPMStartHHmm, RthPMEndHHmm);
        }

        private bool WithinCaps()
        {
            if (MaxTradesPerDay > 0 && dayCount >= MaxTradesPerDay) return false;
            if (WindowIsAM() && MaxTradesPerAM > 0 && amCount >= MaxTradesPerAM) return false;
            if (WindowIsPM() && MaxTradesPerPM > 0 && pmCount >= MaxTradesPerPM) return false;
            return true;
        }

        private bool AdxRisingOK()
        {
            if (AdxRisingBars <= 0) return true;
            for (int i = 0; i < AdxRisingBars; i++)
                if (!(adx[i] > adx[i + 1])) return false;
            return true;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AdxRenkoV1";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                TraceOrders = false;
            }
            else if (State == State.DataLoaded)
            {
                adx      = ADX(AdxPeriod);
                atr      = ATR(AtrPeriod);
                emaAlign = EMA(EmaAlignPeriod);
                emaTrail = EMA(EmaTrailPeriod);

                dmPlus       = new Series<double>(this);
                dmMinus      = new Series<double>(this);
                sumDmPlus    = new Series<double>(this);
                sumDmMinus   = new Series<double>(this);
                sumTr        = new Series<double>(this);
                diPlusSeries = new Series<double>(this);
                diMinusSeries= new Series<double>(this);

                AddChartIndicator(adx);
                AddChartIndicator(emaAlign);
                AddChartIndicator(emaTrail);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(AdxPeriod, AtrPeriod) + 3)
                return;

            // ---- DI math (Wilder smoothing) ----
            double high0 = High[0], low0 = Low[0];
            if (CurrentBar == 0)
            {
                dmPlus[0] = dmMinus[0] = 0;
                sumTr[0] = (high0 - low0);
                sumDmPlus[0] = sumDmMinus[0] = 0;
                diPlusSeries[0] = diMinusSeries[0] = 0;
                return;
            }

            double high1 = High[1], low1 = Low[1], close1 = Close[1];
            double tr = Math.Max(high0 - low0, Math.Max(Math.Abs(high0 - close1), Math.Abs(low0 - close1)));
            double upMove   = high0 - high1;
            double downMove = low1 - low0;

            dmPlus[0]  = (upMove   > 0 && upMove   > downMove) ? upMove   : 0;
            dmMinus[0] = (downMove > 0 && downMove > upMove)   ? downMove : 0;

            if (CurrentBar < AdxPeriod)
            {
                sumTr[0]      = sumTr[1] + tr;
                sumDmPlus[0]  = sumDmPlus[1] + dmPlus[0];
                sumDmMinus[0] = sumDmMinus[1] + dmMinus[0];
            }
            else
            {
                sumTr[0]      = sumTr[1]      - (sumTr[1]      / AdxPeriod) + tr;
                sumDmPlus[0]  = sumDmPlus[1]  - (sumDmPlus[1]  / AdxPeriod) + dmPlus[0];
                sumDmMinus[0] = sumDmMinus[1] - (sumDmMinus[1] / AdxPeriod) + dmMinus[0];
            }

            double sTr = sumTr[0].ApproxCompare(0) == 0 ? 1e-9 : sumTr[0];
            diPlusSeries[0]  = 100.0 * (sumDmPlus[0]  / sTr);
            diMinusSeries[0] = 100.0 * (sumDmMinus[0] / sTr);

            // ---- day/window counters ----
            int curDate = Time[0].Date.ToOADate().ToInt();
            if (curDate != todaysDate)
            {
                todaysDate = curDate;
                amCount = pmCount = dayCount = 0;
            }

            // ---- Signals ----
            bool crossUp = (diPlusSeries[1] <= diMinusSeries[1]) && (diPlusSeries[0] > diMinusSeries[0]);
            bool crossDn = (diPlusSeries[1] >= diMinusSeries[1]) && (diPlusSeries[0] < diMinusSeries[0]);

            bool okAdx     = !EnableEntryFilters || (adx[0] >= AdxMin && AdxRisingOK());
            bool okDiagapU = !EnableEntryFilters || (diPlusSeries[0] - diMinusSeries[0] >= MinDiGap);
            bool okDiagapD = !EnableEntryFilters || (diMinusSeries[0] - diPlusSeries[0] >= MinDiGap);
            bool alignLong = !UseEmaAlignment   || (Close[0] > emaAlign[0]);
            bool alignShort= !UseEmaAlignment   || (Close[0] < emaAlign[0]);

            // Cooldown
            bool cooled = (CurrentBar - lastEntryBar) >= Math.Max(0, MinBarsBetweenEntries);

            // Flat entries
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                trailingStopLong = trailingStopShort = double.NaN;

                if (AllowedTime() && WithinCaps() && cooled)
                {
                    if (okAdx && crossUp && okDiagapU && alignLong)
                    {
                        SubmitLong();
                    }
                    else if (okAdx && crossDn && okDiagapD && alignShort)
                    {
                        SubmitShort();
                    }
                }
            }

            // Manage LONG
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (UseStopX && (adx[0] < AdxMin || crossDn))
                    ExitLong("StopX", "LE");

                int bse = BarsSinceEntryExecution(0, "LE", 0);
                if (bse != -1 && bse >= Math.Max(1, TrailSwitchNBars))
                {
                    double emaStp = RT(emaTrail[0] - EmaTrailOffsetTicks * TickSize);
                    trailingStopLong = double.IsNaN(trailingStopLong) ? emaStp : Math.Max(trailingStopLong, emaStp);
                    SetStopLoss("LE", CalculationMode.Price, trailingStopLong, false);
                }
            }

            // Manage SHORT
            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (UseStopX && (adx[0] < AdxMin || crossUp))
                    ExitShort("StopX", "SE");

                int bse = BarsSinceEntryExecution(0, "SE", 0);
                if (bse != -1 && bse >= Math.Max(1, TrailSwitchNBars))
                {
                    double emaStp = RT(emaTrail[0] + EmaTrailOffsetTicks * TickSize);
                    trailingStopShort = double.IsNaN(trailingStopShort) ? emaStp : Math.Min(trailingStopShort, emaStp);
                    SetStopLoss("SE", CalculationMode.Price, trailingStopShort, false);
                }
            }
        }

        // ======== Order helpers (attach stop/target BEFORE entry) ========
        private void SubmitLong()
        {
            double risk = atr[0] * AtrMultiplier;
            double stp  = RT(Close[0] - risk);
            double tgt  = RT(Close[0] + risk * Math.Max(0.1, RiskReward));

            SetStopLoss    ("LE", CalculationMode.Price, stp, false);
            SetProfitTarget("LE", CalculationMode.Price, tgt);

            trailingStopLong = stp;
            EnterLong(Contracts, "LE");

            lastEntryBar = CurrentBar;
            if (WindowIsAM()) amCount++;
            if (WindowIsPM()) pmCount++;
            dayCount++;
        }

        private void SubmitShort()
        {
            double risk = atr[0] * AtrMultiplier;
            double stp  = RT(Close[0] + risk);
            double tgt  = RT(Close[0] - risk * Math.Max(0.1, RiskReward));

            SetStopLoss    ("SE", CalculationMode.Price, stp, false);
            SetProfitTarget("SE", CalculationMode.Price, tgt);

            trailingStopShort = stp;
            EnterShort(Contracts, "SE");

            lastEntryBar = CurrentBar;
            if (WindowIsAM()) amCount++;
            if (WindowIsPM()) pmCount++;
            dayCount++;
        }
    }

    internal static class NTUtils
    {
        public static int ToInt(this double d) => (int)d;
    }
}
