// PriorBarHL.cs
// NinjaTrader 8 Indicator
// Plots the prior bar's High and Low for the *selected Input series* (e.g., 3m or 5m).
// Use case: trade on 1m, trail stops off 3m/5m prior H/L via "Attach to Indicator".
#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class PriorBarHL : Indicator
    {
        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "Bars Ago (prior = 1)", GroupName = "Parameters", Order = 0)]
        public int BarsAgoRef { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Calculate On Bar Close", GroupName = "Parameters", Order = 1, Description = "If true, updates only when the input bar closes. If false, updates intrabar.")]
        public bool CalcOnClose { get; set; } = true;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "PriorBarHL";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                AddPlot(Brushes.DodgerBlue, "PrevHigh");
                AddPlot(Brushes.IndianRed, "PrevLow");
            }
            else if (State == State.Configure)
            {
                Calculate = CalcOnClose ? Calculate.OnBarClose : Calculate.OnPriceChange;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsAgoRef)
            {
                Values[0][0] = double.NaN;
                Values[1][0] = double.NaN;
                return;
            }

            // High[]/Low[] refer to the chosen Input series (e.g., 3m or 5m) when you add the indicator.
            Values[0][0] = High[BarsAgoRef]; // PrevHigh
            Values[1][0] = Low[BarsAgoRef];  // PrevLow
        }
    }
}
