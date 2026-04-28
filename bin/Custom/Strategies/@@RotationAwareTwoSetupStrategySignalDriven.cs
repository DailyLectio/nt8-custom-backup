#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
#endregion

// Strategy reads the 4 Series from RotationAwareTwoSetupAlerts (SigOrbLong/Short, SigRejLong/Short).
// Backtest Mode only (uses Managed orders with SetStopLoss/SetProfitTarget). We can add ATM later if you want.

namespace NinjaTrader.NinjaScript.Strategies
{
    public class RotationAwareTwoSetupStrategy_SignalDriven : Strategy
    {
        // ---- Inputs mirroring indicator ----
        [NinjaScriptProperty, Range(2,200)]
        [Display(Name="Choppiness Length", Order=1, GroupName="Indicator Params")] public int ChopLength { get; set; } = 14;
        [NinjaScriptProperty, Range(1,100)]
        [Display(Name="EMA Fast (1m/5m)",   Order=2, GroupName="Indicator Params")] public int EmaFast { get; set; } = 8;
        [NinjaScriptProperty, Range(2,200)]
        [Display(Name="EMA Slow (1m/5m)",   Order=3, GroupName="Indicator Params")] public int EmaSlow { get; set; } = 21;
        [NinjaScriptProperty, Range(1.0,10.0)]
        [Display(Name="3m Volume Multiplier", Order=4, GroupName="Indicator Params")] public double VolMult { get; set; } = 1.30;
        [NinjaScriptProperty] [Display(Name="Aggressive Break",     Order=5, GroupName="Indicator Params")] public bool AggressiveBreak { get; set; } = false;
        [NinjaScriptProperty] [Display(Name="Require Pullback Hold", Order=6, GroupName="Indicator Params")] public bool RequirePullback { get; set; } = true;
        [NinjaScriptProperty, Range(1,10)]
        [Display(Name="CI Decline Streak (bars)", Order=7, GroupName="Indicator Params")] public int CiStreakMin { get; set; } = 2;

        [NinjaScriptProperty] [Display(Name="Use CI Filter",        Order=10, GroupName="Filter Toggles")] public bool UseCIFilter { get; set; } = true;
        [NinjaScriptProperty] [Display(Name="Use Volume Filter",     Order=11, GroupName="Filter Toggles")] public bool UseVolumeFilter { get; set; } = true;
        [NinjaScriptProperty] [Display(Name="Use EMA Bias",          Order=12, GroupName="Filter Toggles")] public bool UseEMABiasFilter { get; set; } = true;
        [NinjaScriptProperty] [Display(Name="Use IB/Time Logic",     Order=13, GroupName="Filter Toggles")] public bool UseIBLogic { get; set; } = true;
        [NinjaScriptProperty] [Display(Name="Use Pullback to IB",    Order=14, GroupName="Filter Toggles")] public bool UsePullbackFilter { get; set; } = true;
        [NinjaScriptProperty] [Display(Name="Use Rotation Windows",  Order=15, GroupName="Filter Toggles")] public bool UseRotationMode { get; set; } = true;

        // Basic trade mgmt for backtest
        [NinjaScriptProperty, Range(1,100)]
        [Display(Name="Quantity",    Order=20, GroupName="Trade Mgmt")] public int Quantity { get; set; } = 1;
        [NinjaScriptProperty, Range(1,200)]
        [Display(Name="Stop (ticks)",Order=21, GroupName="Trade Mgmt")] public int StopTicks { get; set; } = 20;
        [NinjaScriptProperty, Range(1,400)]
        [Display(Name="Target (ticks)",Order=22, GroupName="Trade Mgmt")] public int TargetTicks { get; set; } = 30;

        // internal indicator instance
        private Indicators.RotationAwareTwoSetupAlerts ind;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                = "RotationAwareTwoSetupStrategy_SignalDriven";
                Calculate           = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling       = EntryHandling.UniqueEntries;
                IsUnmanaged         = false;
            }
            else if (State == State.Configure)
            {
                // managed stops/targets for Strategy Analyzer
                SetStopLoss(CalculationMode.Ticks, StopTicks);
                SetProfitTarget(CalculationMode.Ticks, TargetTicks);
            }
            else if (State == State.DataLoaded)
            {
                // IMPORTANT: in a Strategy, call the indicator factory WITHOUT the "Indicators." prefix.
                // The auto-generated wrapper lives on the Strategy partial class.
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

                // Optional: plot it via strategy so you can see CI panel while testing
                // AddChartIndicator(ind);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 50) return;

            // read 1.0/0.0 flags
            bool orbLong  = ind.SigOrbLong[0]  > 0.5;
            bool orbShort = ind.SigOrbShort[0] > 0.5;
            bool rejLong  = ind.SigRejLong[0]  > 0.5;
            bool rejShort = ind.SigRejShort[0] > 0.5;

            bool goLong  = orbLong  || rejLong;
            bool goShort = orbShort || rejShort;

            // basic Managed entries (so Strategy Analyzer works)
            if (goLong  && Position.MarketPosition <= MarketPosition.Flat)
                EnterLong(Quantity, "L_sig");
            if (goShort && Position.MarketPosition >= MarketPosition.Flat)
                EnterShort(Quantity, "S_sig");
        }
    }
}