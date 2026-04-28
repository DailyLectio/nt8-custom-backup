// DonchianStopLine.cs
// NinjaTrader 8 Indicator
// Plots ready-to-attach Donchian-based stop lines for manual ATM use on any Input series (e.g., 3m or 5m).
// LongStop = LowerDonchian + offset; ShortStop = UpperDonchian - offset.
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
    public class DonchianStopLine : Indicator
    {
        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "Period", GroupName = "Parameters", Order = 0)]
        public int Period { get; set; } = 20;

        [NinjaScriptProperty, Range(0, 200)]
        [Display(Name = "Offset Ticks", GroupName = "Parameters", Order = 1)]
        public int OffsetTicks { get; set; } = 4;

        [NinjaScriptProperty]
        [Display(Name = "Calculate On Bar Close", GroupName = "Parameters", Order = 2, Description = "If true, updates only on close; if false, updates intrabar.")]
        public bool CalcOnClose { get; set; } = true;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "DonchianStopLine";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                AddPlot(Brushes.ForestGreen, "LongStop");
                AddPlot(Brushes.Firebrick, "ShortStop");
            }
            else if (State == State.Configure)
            {
                Calculate = CalcOnClose ? Calculate.OnBarClose : Calculate.OnPriceChange;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Period)
            {
                Values[0][0] = double.NaN;
                Values[1][0] = double.NaN;
                return;
            }

            double upper = MAX(High, Period)[0];
            double lower = MIN(Low, Period)[0];

            double longStop  = lower + OffsetTicks * TickSize;
            double shortStop = upper - OffsetTicks * TickSize;

            Values[0][0] = longStop;   // Attach for LONG positions
            Values[1][0] = shortStop;  // Attach for SHORT positions
        }
    }
}
