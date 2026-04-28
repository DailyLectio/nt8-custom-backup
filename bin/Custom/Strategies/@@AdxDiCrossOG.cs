// CC BY-NC 4.0

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Core.FloatingPoint;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class AdxDiCrossOG : Strategy
    {
        // ---- Stop modes (single-leg only) ----TRUE ADX OG
        public enum StopMode
        {
            AtrStatic = 0,    // fixed ATR stop set at entry
            EmaTrailing = 1,  // EMA +/- ticks trailing
            BarNTrailing = 2, // N-bar +/- ticks trailing
            AtrStep = 3       // step -> BE+ -> ATR trail
        }

        // ---- Indicators ----
        private ADX adxIndicator;
        private ATR atrIndicator;
        private EMA emaIndicator;

        // ---- DI scaffolding ----
        private Series<double> dmPlus, dmMinus, sumDmPlus, sumDmMinus, sumTr, diPlusSeries, diMinusSeries;

        // ---- trailing anchors ----
        private double trailingStopLong = double.NaN, trailingStopShort = double.NaN;

        // ========== PARAMETERS ==========

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Order = 1, GroupName = "Parameters")]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Use Stop X (ADX/DI exit)", Order = 2, GroupName = "Parameters")]
        public bool UseStopX { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Risk Reward (for targets)", Order = 3, GroupName = "Parameters")]
        public double RiskReward { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ADX Period", Order = 4, GroupName = "Parameters")]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Display(Name = "Level Range (ADX min)", Order = 5, GroupName = "Parameters")]
        public double LevelRange { get; set; } = 20;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "Parameters")]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Display(Name = "ATR Multiplier", Order = 7, GroupName = "Parameters")]
        public double AtrMultiplier { get; set; } = 1.0;

        // ---- Stops (single-leg) ----
        [NinjaScriptProperty]
        [Display(Name = "Stop Mode", Order = 10, GroupName = "Stops")]
        public StopMode StopModeSelection { get; set; } = StopMode.AtrStatic;

        // EMA trailing
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 11, GroupName = "Stops - EMA Trailing")]
        public int EmaPeriod { get; set; } = 50;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "EMA Offset (ticks)", Order = 12, GroupName = "Stops - EMA Trailing")]
        public int EmaOffsetTicks { get; set; } = 0;

        // BarN trailing
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Trailing N Bars", Order = 13, GroupName = "Stops - BarN Trailing")]
        public int TrailingNBars { get; set; } = 1;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "Trailing Offset (ticks)", Order = 14, GroupName = "Stops - BarN Trailing")]
        public int TrailingOffsetTicks { get; set; } = 4;

        // ATR Step
        [NinjaScriptProperty]
        [Display(Name = "Step 1 trigger (ATR)", Order = 15, GroupName = "Stops - ATR Step")]
        public double Step1ATR { get; set; } = 0.25;

        [NinjaScriptProperty]
        [Display(Name = "Step 2 trigger (ATR)", Order = 16, GroupName = "Stops - ATR Step")]
        public double Step2ATR { get; set; } = 0.50;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "BE Plus (ticks)", Order = 17, GroupName = "Stops - ATR Step")]
        public int BreakevenPlusTicks { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "Trail ATR Mult", Order = 18, GroupName = "Stops - ATR Step")]
        public double TrailAtrMult { get; set; } = 1.0;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AdxDiCrossOG";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
            }
            else if (State == State.DataLoaded)
            {
                adxIndicator = ADX(AdxPeriod);
                atrIndicator = ATR(AtrPeriod);
                emaIndicator = EMA(EmaPeriod);

                dmPlus       = new Series<double>(this);
                dmMinus      = new Series<double>(this);
                sumDmPlus    = new Series<double>(this);
                sumDmMinus   = new Series<double>(this);
                sumTr        = new Series<double>(this);
                diPlusSeries = new Series<double>(this);
                diMinusSeries= new Series<double>(this);

                AddChartIndicator(adxIndicator);
                AddChartIndicator(emaIndicator);
            }
        }

        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        protected override void OnBarUpdate()
        {
            // ---- DI math matching your original approach ----
            double high0 = High[0], low0 = Low[0];

            if (CurrentBar == 0)
            {
                double tr0 = high0 - low0;
                dmPlus[0] = dmMinus[0] = 0;
                sumTr[0] = tr0; sumDmPlus[0] = 0; sumDmMinus[0] = 0;
                diPlusSeries[0] = diMinusSeries[0] = 0;
                return;
            }
            else
            {
                double high1 = High[1], low1 = Low[1], close1 = Close[1];
                double tr0 = Math.Max(Math.Abs(low0 - close1), Math.Max(high0 - low0, Math.Abs(high0 - close1)));
                dmPlus[0]  = high0 - high1 > low1 - low0 ? Math.Max(high0 - high1, 0) : 0;
                dmMinus[0] = low1 - low0 > high0 - high1 ? Math.Max(low1 - low0, 0) : 0;

                if (CurrentBar < AdxPeriod)
                {
                    sumTr[0]      = sumTr[1] + tr0;
                    sumDmPlus[0]  = sumDmPlus[1] + dmPlus[0];
                    sumDmMinus[0] = sumDmMinus[1] + dmMinus[0];
                    return;
                }
                else
                {
                    double tr1 = sumTr[1], sdp1 = sumDmPlus[1], sdm1 = sumDmMinus[1];
                    sumTr[0]      = tr1  - tr1  / AdxPeriod + tr0;
                    sumDmPlus[0]  = sdp1 - sdp1 / AdxPeriod + dmPlus[0];
                    sumDmMinus[0] = sdm1 - sdm1 / AdxPeriod + dmMinus[0];
                }

                double sTr0 = sumTr[0];
                diPlusSeries[0]  = 100 * (sTr0.ApproxCompare(0) == 0 ? 0 : sumDmPlus[0]  / sTr0);
                diMinusSeries[0] = 100 * (sTr0.ApproxCompare(0) == 0 ? 0 : sumDmMinus[0] / sTr0);
            }

            if (CurrentBar < Math.Max(AdxPeriod, AtrPeriod))
                return;

            bool adxStrong = adxIndicator[0] > LevelRange;
            bool crossUp   = diPlusSeries[1] <= diMinusSeries[1] && diPlusSeries[0] > diMinusSeries[0];
            bool crossDown = diMinusSeries[1] <= diPlusSeries[1] && diMinusSeries[0] > diPlusSeries[0];

            // reset trailing anchors when flat
            if (Position.MarketPosition == MarketPosition.Flat)
                trailingStopLong = trailingStopShort = double.NaN;

            // ---- Entries (single-leg) ----
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                double riskATR = atrIndicator[0] * AtrMultiplier;

                if (adxStrong && crossUp)
                {
                    double tgt = RT(Close[0] + riskATR * RiskReward);
                    SetProfitTarget(CalculationMode.Price, tgt);

                    if (StopModeSelection == StopMode.AtrStatic || StopModeSelection == StopMode.AtrStep)
                    {
                        double stp = RT(Close[0] - riskATR);
                        SetStopLoss(CalculationMode.Price, stp);
                        trailingStopLong = stp;
                    }

                    EnterLong(Contracts, "Long");
                }
                else if (adxStrong && crossDown)
                {
                    double tgt = RT(Close[0] - riskATR * RiskReward);
                    SetProfitTarget(CalculationMode.Price, tgt);

                    if (StopModeSelection == StopMode.AtrStatic || StopModeSelection == StopMode.AtrStep)
                    {
                        double stp = RT(Close[0] + riskATR);
                        SetStopLoss(CalculationMode.Price, stp);
                        trailingStopShort = stp;
                    }

                    EnterShort(Contracts, "Short");
                }
            }
            else
            {
                // optional indicator exit
                if (UseStopX)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        if (crossDown || adxIndicator[0] < adxIndicator[1])
                            ExitLong("StopXLong", "Long");
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        if (crossUp || adxIndicator[0] < adxIndicator[1])
                            ExitShort("StopXShort", "Short");
                    }
                }

                // trailing: EMA / BarN
                if (StopModeSelection == StopMode.EmaTrailing || StopModeSelection == StopMode.BarNTrailing)
                {
                    if (StopModeSelection != StopMode.BarNTrailing || CurrentBar >= TrailingNBars)
                    {
                        if (Position.MarketPosition == MarketPosition.Long)
                        {
                            double candidate = (StopModeSelection == StopMode.EmaTrailing)
                                ? emaIndicator[0] - (EmaOffsetTicks * TickSize)
                                : Low[TrailingNBars] - (TrailingOffsetTicks * TickSize);
                            candidate = RT(candidate);
                            trailingStopLong = double.IsNaN(trailingStopLong) ? candidate : Math.Max(trailingStopLong, candidate);
                            ExitLongStopMarket(Position.Quantity, trailingStopLong, "TSL", "Long");
                        }
                        else if (Position.MarketPosition == MarketPosition.Short)
                        {
                            double candidate = (StopModeSelection == StopMode.EmaTrailing)
                                ? emaIndicator[0] + (EmaOffsetTicks * TickSize)
                                : High[TrailingNBars] + (TrailingOffsetTicks * TickSize);
                            candidate = RT(candidate);
                            trailingStopShort = double.IsNaN(trailingStopShort) ? candidate : Math.Min(trailingStopShort, candidate);
                            ExitShortStopMarket(Position.Quantity, trailingStopShort, "TSS", "Short");
                        }
                    }
                }

                // trailing: ATR Step
                if (StopModeSelection == StopMode.AtrStep)
                {
                    double riskATR = atrIndicator[0] * AtrMultiplier;

                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        double avg = Position.AveragePrice;
                        double rOpen = (Close[0] - avg) / Math.Max(riskATR, TickSize);

                        if (rOpen >= Step2ATR)
                        {
                            double bePlus = RT(avg + BreakevenPlusTicks * TickSize);
                            double trail  = RT(Close[0] - atrIndicator[0] * TrailAtrMult);
                            SetStopLoss(CalculationMode.Price, Math.Max(bePlus, trail));
                        }
                        else if (rOpen >= Step1ATR)
                        {
                            double tightened = RT(Math.Min(avg, avg - riskATR * 0.5 * Step1ATR));
                            SetStopLoss(CalculationMode.Price, tightened);
                        }
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        double avg = Position.AveragePrice;
                        double rOpen = (avg - Close[0]) / Math.Max(riskATR, TickSize);

                        if (rOpen >= Step2ATR)
                        {
                            double beMinus = RT(avg - BreakevenPlusTicks * TickSize);
                            double trail   = RT(Close[0] + atrIndicator[0] * TrailAtrMult);
                            SetStopLoss(CalculationMode.Price, Math.Min(beMinus, trail));
                        }
                        else if (rOpen >= Step1ATR)
                        {
                            double tightened = RT(Math.Max(avg, avg + riskATR * 0.5 * Step1ATR));
                            SetStopLoss(CalculationMode.Price, tightened);
                        }
                    }
                }
            }
        }
    }
}
