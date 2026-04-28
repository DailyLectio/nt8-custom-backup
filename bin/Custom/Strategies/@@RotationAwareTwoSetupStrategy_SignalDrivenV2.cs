#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;                    // <- required for BarsPeriodType
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;  // <- lets us use RotationAwareTwoSetupAlerts type directly
#endregion

// Strategy reads the 4 signal series from RotationAwareTwoSetupAlerts:
//   SigOrbLong / SigOrbShort / SigRejLong / SigRejShort
// Designed for Strategy Analyzer backtesting (managed orders + fixed stop/target).

namespace NinjaTrader.NinjaScript.Strategies
{
    public class RotationAwareTwoSetupStrategy_SignalDrivenV2 : Strategy
    {
        // ===== Inputs (mirror the indicator) =====
        [NinjaScriptProperty, Range(2, 200)]
        [Display(Name = "Choppiness Length", Order = 1, GroupName = "Indicator Params")]
        public int ChopLength { get; set; } = 14;

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "EMA Fast (1m/5m)", Order = 2, GroupName = "Indicator Params")]
        public int EmaFast { get; set; } = 8;

        [NinjaScriptProperty, Range(2, 200)]
        [Display(Name = "EMA Slow (1m/5m)", Order = 3, GroupName = "Indicator Params")]
        public int EmaSlow { get; set; } = 21;

        [NinjaScriptProperty, Range(1.0, 10.0)]
        [Display(Name = "3m Volume Multiplier", Order = 4, GroupName = "Indicator Params")]
        public double VolMult { get; set; } = 1.30;

        [NinjaScriptProperty]
        [Display(Name = "Aggressive Break", Order = 5, GroupName = "Indicator Params")]
        public bool AggressiveBreak { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Require Pullback Hold", Order = 6, GroupName = "Indicator Params")]
        public bool RequirePullback { get; set; } = true;

        [NinjaScriptProperty, Range(1, 10)]
        [Display(Name = "CI Decline Streak (bars)", Order = 7, GroupName = "Indicator Params")]
        public int CiStreakMin { get; set; } = 2;

        // ----- Filter toggles -----
        [NinjaScriptProperty] [Display(Name = "Use CI Filter",        Order = 10, GroupName = "Filter Toggles")] public bool UseCIFilter { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Use Volume Filter",     Order = 11, GroupName = "Filter Toggles")] public bool UseVolumeFilter { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Use EMA Bias",          Order = 12, GroupName = "Filter Toggles")] public bool UseEMABiasFilter { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Use IB/Time Logic",     Order = 13, GroupName = "Filter Toggles")] public bool UseIBLogic { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Use Pullback to IB",    Order = 14, GroupName = "Filter Toggles")] public bool UsePullbackFilter { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Use Rotation Windows",  Order = 15, GroupName = "Filter Toggles")] public bool UseRotationMode { get; set; } = true;

        // ----- Trade management -----
        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Quantity", Order = 20, GroupName = "Trade Mgmt")]
        public int Quantity { get; set; } = 1;

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "Stop (ticks)", Order = 21, GroupName = "Trade Mgmt")]
        public int StopTicks { get; set; } = 20;

        [NinjaScriptProperty, Range(1, 400)]
        [Display(Name = "Target (ticks)", Order = 22, GroupName = "Trade Mgmt")]
        public int TargetTicks { get; set; } = 30;

        // ----- Internals -----
        private RotationAwareTwoSetupAlerts ind;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                = "RotationAwareTwoSetupStrategy_SignalDrivenV2"; // unique name
                Calculate           = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling       = EntryHandling.UniqueEntries;
                IsUnmanaged         = false;

                // start quickly in Analyzer
                BarsRequiredToTrade = 1;
            }
            else if (State == State.Configure)
            {
                // Supply MTF series required by indicator/logic
                AddDataSeries(BarsPeriodType.Minute, 3);
                AddDataSeries(BarsPeriodType.Minute, 5);

                // Fixed stop/target for Analyzer backtests
                SetStopLoss(CalculationMode.Ticks, StopTicks);
                SetProfitTarget(CalculationMode.Ticks, TargetTicks);
            }
            else if (State == State.DataLoaded)
            {
                // Instantiate the indicator with the strategy's data series
                ind = RotationAwareTwoSetupAlerts(
                    ChopLength, EmaFast, EmaSlow, VolMult,
                    AggressiveBreak, RequirePullback, CiStreakMin,
                    UseCIFilter, UseVolumeFilter, UseEMABiasFilter,
                    UseIBLogic, UsePullbackFilter, UseRotationMode,
                    /*SuppressTradeMarkers*/ true,
                    /*ShowIBLines*/          false,
                    /*CIPlotMode*/           "Panel",
                    /*ShowCILabels*/         false,
                    /*PlaySounds*/           false,
                    /*ShowDebug*/            false
                );

                // Optional: view indicator on Analyzer's Chart tab
                // AddChartIndicator(ind);
            }
        }

        protected override void OnBarUpdate()
        {
            // Only execute on primary (1-minute) series
            if (BarsInProgress != 0)
                return;

            // Tiny warm-up for indicator internals
            if (CurrentBar < 50)
                return;

            // ---- DEBUG: prove Analyzer is iterating bars ----
            if (CurrentBar % 200 == 0)
                Print($"[{Instrument.FullName}] {Time[0]}  Close:{Close[0]}");

            // Read 1.0/0.0 flags from indicator
            bool orbLong  = ind != null && ind.SigOrbLong[0]  > 0.5;
            bool orbShort = ind != null && ind.SigOrbShort[0] > 0.5;
            bool rejLong  = ind != null && ind.SigRejLong[0]  > 0.5;
            bool rejShort = ind != null && ind.SigRejShort[0] > 0.5;

            // ---- DEBUG: show when any signal appears ----
            if (orbLong || orbShort || rejLong || rejShort)
                Print($"{Time[0]}  orbL:{(orbLong?1:0)}  orbS:{(orbShort?1:0)}  rejL:{(rejLong?1:0)}  rejS:{(rejShort?1:0)}");

            // Managed entries
            if ((orbLong || rejLong) && Position.MarketPosition <= MarketPosition.Flat)
                EnterLong(Quantity, "L_sig");

            if ((orbShort || rejShort) && Position.MarketPosition >= MarketPosition.Flat)
                EnterShort(Quantity, "S_sig");
        }
    }
}