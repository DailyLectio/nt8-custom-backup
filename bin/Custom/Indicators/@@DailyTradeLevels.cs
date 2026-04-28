// CC BY-NC 4.0 — Non-commercial use with attribution.

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class DailyTradeLevels : Indicator
    {
        // ---- Daily level inputs (edit these each day) ----
        [NinjaScriptProperty, Display(Name = "Bull Entry", Order = 0, GroupName = "Bull Levels")]
        public double BullEntry { get; set; }

        [NinjaScriptProperty, Display(Name = "Bull Target 1", Order = 1, GroupName = "Bull Levels")]
        public double BullT1 { get; set; }

        [NinjaScriptProperty, Display(Name = "Bull Target 2", Order = 2, GroupName = "Bull Levels")]
        public double BullT2 { get; set; }

        [NinjaScriptProperty, Display(Name = "Bull Target 3", Order = 3, GroupName = "Bull Levels")]
        public double BullT3 { get; set; }

        [NinjaScriptProperty, Display(Name = "Bull Target 4", Order = 4, GroupName = "Bull Levels")]
        public double BullT4 { get; set; }

        [NinjaScriptProperty, Display(Name = "Bear Entry", Order = 5, GroupName = "Bear Levels")]
        public double BearEntry { get; set; }

        [NinjaScriptProperty, Display(Name = "Bear Target 1", Order = 6, GroupName = "Bear Levels")]
        public double BearT1 { get; set; }

        [NinjaScriptProperty, Display(Name = "Bear Target 2", Order = 7, GroupName = "Bear Levels")]
        public double BearT2 { get; set; }

        [NinjaScriptProperty, Display(Name = "Bear Target 3", Order = 8, GroupName = "Bear Levels")]
        public double BearT3 { get; set; }

        [NinjaScriptProperty, Display(Name = "Bear Target 4", Order = 9, GroupName = "Bear Levels")]
        public double BearT4 { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "DailyTradeLevels";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;

                // Defaults (your NQ levels; Bear T2 corrected to 24,298)
                BullEntry = 25237;
                BullT1    = 25392;
                BullT2    = 25480;
                BullT3    = 25500;
                BullT4    = 25550;

                BearEntry = 25152;
                BearT1    = 25013;
                BearT2    = 24298;   // corrected per your note (24,298)
                BearT3    = 24923;
                BearT4    = 24888;

                // Add plots — names here become the right-axis price marker labels
                AddPlot(Brushes.Blue,      "BULL ENTRY"); // 0
                AddPlot(Brushes.LimeGreen, "BULL T1");    // 1
                AddPlot(Brushes.LimeGreen, "BULL T2");    // 2
                AddPlot(Brushes.LimeGreen, "BULL T3");    // 3
                AddPlot(Brushes.LimeGreen, "BULL T4");    // 4

                AddPlot(Brushes.Red,       "BEAR ENTRY"); // 5
                AddPlot(Brushes.Magenta,   "BEAR T1");    // 6
                AddPlot(Brushes.Magenta,   "BEAR T2");    // 7
                AddPlot(Brushes.Magenta,   "BEAR T3");    // 8
                AddPlot(Brushes.Magenta,   "BEAR T4");    // 9
            }
            else if (State == State.Configure)
            {
                // Thickness (Entry = heavy, Targets = medium). All NT8 installs support Width.
                Plots[0].Width = 3; // BULL ENTRY
                Plots[5].Width = 3; // BEAR ENTRY

                Plots[1].Width = 2;
                Plots[2].Width = 2;
                Plots[3].Width = 2;
                Plots[4].Width = 2;
                Plots[6].Width = 2;
                Plots[7].Width = 2;
                Plots[8].Width = 2;
                Plots[9].Width = 2;
            }
        }

        protected override void OnBarUpdate()
        {
            // Keep each plot flat at its price so you get true horizontal rails
            if (CurrentBar < 0) return;

            Values[0][0] = BullEntry;
            Values[1][0] = BullT1;
            Values[2][0] = BullT2;
            Values[3][0] = BullT3;
            Values[4][0] = BullT4;

            Values[5][0] = BearEntry;
            Values[6][0] = BearT1;
            Values[7][0] = BearT2;
            Values[8][0] = BearT3;
            Values[9][0] = BearT4;
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private DailyTradeLevels[] cacheDailyTradeLevels;
		public DailyTradeLevels DailyTradeLevels(double bullEntry, double bullT1, double bullT2, double bullT3, double bullT4, double bearEntry, double bearT1, double bearT2, double bearT3, double bearT4)
		{
			return DailyTradeLevels(Input, bullEntry, bullT1, bullT2, bullT3, bullT4, bearEntry, bearT1, bearT2, bearT3, bearT4);
		}

		public DailyTradeLevels DailyTradeLevels(ISeries<double> input, double bullEntry, double bullT1, double bullT2, double bullT3, double bullT4, double bearEntry, double bearT1, double bearT2, double bearT3, double bearT4)
		{
			if (cacheDailyTradeLevels != null)
				for (int idx = 0; idx < cacheDailyTradeLevels.Length; idx++)
					if (cacheDailyTradeLevels[idx] != null && cacheDailyTradeLevels[idx].BullEntry == bullEntry && cacheDailyTradeLevels[idx].BullT1 == bullT1 && cacheDailyTradeLevels[idx].BullT2 == bullT2 && cacheDailyTradeLevels[idx].BullT3 == bullT3 && cacheDailyTradeLevels[idx].BullT4 == bullT4 && cacheDailyTradeLevels[idx].BearEntry == bearEntry && cacheDailyTradeLevels[idx].BearT1 == bearT1 && cacheDailyTradeLevels[idx].BearT2 == bearT2 && cacheDailyTradeLevels[idx].BearT3 == bearT3 && cacheDailyTradeLevels[idx].BearT4 == bearT4 && cacheDailyTradeLevels[idx].EqualsInput(input))
						return cacheDailyTradeLevels[idx];
			return CacheIndicator<DailyTradeLevels>(new DailyTradeLevels(){ BullEntry = bullEntry, BullT1 = bullT1, BullT2 = bullT2, BullT3 = bullT3, BullT4 = bullT4, BearEntry = bearEntry, BearT1 = bearT1, BearT2 = bearT2, BearT3 = bearT3, BearT4 = bearT4 }, input, ref cacheDailyTradeLevels);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.DailyTradeLevels DailyTradeLevels(double bullEntry, double bullT1, double bullT2, double bullT3, double bullT4, double bearEntry, double bearT1, double bearT2, double bearT3, double bearT4)
		{
			return indicator.DailyTradeLevels(Input, bullEntry, bullT1, bullT2, bullT3, bullT4, bearEntry, bearT1, bearT2, bearT3, bearT4);
		}

		public Indicators.DailyTradeLevels DailyTradeLevels(ISeries<double> input , double bullEntry, double bullT1, double bullT2, double bullT3, double bullT4, double bearEntry, double bearT1, double bearT2, double bearT3, double bearT4)
		{
			return indicator.DailyTradeLevels(input, bullEntry, bullT1, bullT2, bullT3, bullT4, bearEntry, bearT1, bearT2, bearT3, bearT4);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.DailyTradeLevels DailyTradeLevels(double bullEntry, double bullT1, double bullT2, double bullT3, double bullT4, double bearEntry, double bearT1, double bearT2, double bearT3, double bearT4)
		{
			return indicator.DailyTradeLevels(Input, bullEntry, bullT1, bullT2, bullT3, bullT4, bearEntry, bearT1, bearT2, bearT3, bearT4);
		}

		public Indicators.DailyTradeLevels DailyTradeLevels(ISeries<double> input , double bullEntry, double bullT1, double bullT2, double bullT3, double bullT4, double bearEntry, double bearT1, double bearT2, double bearT3, double bearT4)
		{
			return indicator.DailyTradeLevels(input, bullEntry, bullT1, bullT2, bullT3, bullT4, bearEntry, bearT1, bearT2, bearT3, bearT4);
		}
	}
}

#endregion
