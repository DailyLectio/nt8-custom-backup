// CC BY-NC 4.0
#region Using
using System;
using System.Text;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;                   // For Brushes

using NinjaTrader.Cbi;
using NinjaTrader.Data;                       // For BarsPeriodType
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;                  // For TextPosition
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;     // For ADX / DM
using NinjaTrader.NinjaScript.DrawingTools;   // For Draw.TextFixed
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class MTF_ADXTrendMonitor : Indicator
    {
        // ===== Inputs: Timeframes =====

        [NinjaScriptProperty]
        [Display(Name = "Show chart TF row", GroupName = "Timeframes", Order = 0)]
        public bool ShowFast { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "15m timeframe (minutes)", GroupName = "Timeframes", Order = 1)]
        public int MidMinutes { get; set; } = 15;

        [NinjaScriptProperty]
        [Display(Name = "Show 15m row", GroupName = "Timeframes", Order = 2)]
        public bool ShowMid { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "30m timeframe (minutes)", GroupName = "Timeframes", Order = 3)]
        public int SlowMinutes { get; set; } = 30;

        [NinjaScriptProperty]
        [Display(Name = "Show 30m row", GroupName = "Timeframes", Order = 4)]
        public bool ShowSlow { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "60m timeframe (minutes)", GroupName = "Timeframes", Order = 5)]
        public int UltraMinutes { get; set; } = 60;

        [NinjaScriptProperty]
        [Display(Name = "Show 60m row", GroupName = "Timeframes", Order = 6)]
        public bool ShowUltra { get; set; } = true;


        // ===== Inputs: ADX / Trend Logic =====

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ADX / DM Period", GroupName = "Parameters", Order = 0)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Min ADX for trend", GroupName = "Parameters", Order = 1)]
        public double MinAdx { get; set; } = 20.0;

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Slope lookback (bars)", GroupName = "Parameters", Order = 2)]
        public int SlopeLookback { get; set; } = 3;   // reserved for future slope rules


        // ===== Inputs: Visuals =====

        [NinjaScriptProperty]
        [Display(Name = "Table Position", GroupName = "Visual", Order = 0)]
        public TextPosition TablePosition { get; set; } = TextPosition.TopRight;


        // ===== Internal indicator refs =====

        private ADX adxFast;
        private ADX adxMid;
        private ADX adxSlow;
        private ADX adxUltra;

        private DM dmFast;
        private DM dmMid;
        private DM dmSlow;
        private DM dmUltra;

        private const string TextTag = "MTF_ADXTrendMonitor_Text";


        // ===== State machine =====

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "MTF_ADXTrendMonitor";
                Description              = "Multi-timeframe ADX/DM trend dashboard: chart TF, 15m, 30m, 60m.";
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                Calculate                = Calculate.OnBarClose;
                BarsRequiredToPlot       = 50;
            }
            else if (State == State.Configure)
            {
                // Add higher timeframes (indices: 0 = chart, 1 = mid, 2 = slow, 3 = ultra)
                AddDataSeries(BarsPeriodType.Minute, MidMinutes);
                AddDataSeries(BarsPeriodType.Minute, SlowMinutes);
                AddDataSeries(BarsPeriodType.Minute, UltraMinutes);
            }
            else if (State == State.DataLoaded)
            {
                // Primary (chart) series – assumed 5m, but works on any
                adxFast = ADX(AdxPeriod);
                dmFast  = DM(AdxPeriod);

                // 15m
                adxMid = ADX(BarsArray[1], AdxPeriod);
                dmMid  = DM(BarsArray[1], AdxPeriod);

                // 30m
                adxSlow = ADX(BarsArray[2], AdxPeriod);
                dmSlow  = DM(BarsArray[2], AdxPeriod);

                // 60m
                adxUltra = ADX(BarsArray[3], AdxPeriod);
                dmUltra  = DM(BarsArray[3], AdxPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            // Only run logic on the primary series (your main chart TF)
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < BarsRequiredToPlot)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("TF    ADX   Trend");

            // Build label for the chart timeframe
            string fastLabel;
            if (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute)
                fastLabel = BarsPeriod.Value + "m";
            else if (BarsPeriod.BarsPeriodType == BarsPeriodType.Second)
                fastLabel = BarsPeriod.Value + "s";
            else
                fastLabel = "TF";

            if (ShowFast)
                sb.AppendLine(FormatRow(
                    label: fastLabel,
                    adxInd: adxFast,
                    dmInd: dmFast,
                    currentBar: CurrentBars[0]));

            if (ShowMid)
                sb.AppendLine(FormatRow(
                    label: MidMinutes + "m",
                    adxInd: adxMid,
                    dmInd: dmMid,
                    currentBar: CurrentBars[1]));

            if (ShowSlow)
                sb.AppendLine(FormatRow(
                    label: SlowMinutes + "m",
                    adxInd: adxSlow,
                    dmInd: dmSlow,
                    currentBar: CurrentBars[2]));

            if (ShowUltra)
                sb.AppendLine(FormatRow(
                    label: UltraMinutes + "m",
                    adxInd: adxUltra,
                    dmInd: dmUltra,
                    currentBar: CurrentBars[3]));

            Draw.TextFixed(
                this,
                TextTag,
                sb.ToString(),
                TablePosition,
                Brushes.White,
                new SimpleFont("Segoe UI", 12),
                Brushes.Black,         // background
                Brushes.DimGray,       // border
                80);                   // opacity (0-100)
        }


        // ===== Helpers =====

        private string FormatRow(string label, ADX adxInd, DM dmInd, int currentBar)
        {
            int minBars = Math.Max(AdxPeriod + SlopeLookback, 5);

            // If this data series doesn't have enough bars yet, show n/a
            if (currentBar <= minBars)
                return string.Format("{0,-4}  n/a   n/a", label);

            double adx = adxInd[0];
            double diP = dmInd.DiPlus[0];
            double diM = dmInd.DiMinus[0];

            string trend;

            if (double.IsNaN(adx) || double.IsNaN(diP) || double.IsNaN(diM))
            {
                trend = "n/a";
            }
            else if (adx < MinAdx)
            {
                trend = "Chop";
            }
            else
            {
                trend = (diP > diM) ? "Bullish" : "Bearish";
            }

            // Example text: "15m  24.7  Bullish"
            return string.Format("{0,-4}  {1,4:0.0}  {2}", label, adx, trend);
        }
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MTF_ADXTrendMonitor[] cacheMTF_ADXTrendMonitor;
		public MTF_ADXTrendMonitor MTF_ADXTrendMonitor(bool showFast, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int ultraMinutes, bool showUltra, int adxPeriod, double minAdx, int slopeLookback, TextPosition tablePosition)
		{
			return MTF_ADXTrendMonitor(Input, showFast, midMinutes, showMid, slowMinutes, showSlow, ultraMinutes, showUltra, adxPeriod, minAdx, slopeLookback, tablePosition);
		}

		public MTF_ADXTrendMonitor MTF_ADXTrendMonitor(ISeries<double> input, bool showFast, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int ultraMinutes, bool showUltra, int adxPeriod, double minAdx, int slopeLookback, TextPosition tablePosition)
		{
			if (cacheMTF_ADXTrendMonitor != null)
				for (int idx = 0; idx < cacheMTF_ADXTrendMonitor.Length; idx++)
					if (cacheMTF_ADXTrendMonitor[idx] != null && cacheMTF_ADXTrendMonitor[idx].ShowFast == showFast && cacheMTF_ADXTrendMonitor[idx].MidMinutes == midMinutes && cacheMTF_ADXTrendMonitor[idx].ShowMid == showMid && cacheMTF_ADXTrendMonitor[idx].SlowMinutes == slowMinutes && cacheMTF_ADXTrendMonitor[idx].ShowSlow == showSlow && cacheMTF_ADXTrendMonitor[idx].UltraMinutes == ultraMinutes && cacheMTF_ADXTrendMonitor[idx].ShowUltra == showUltra && cacheMTF_ADXTrendMonitor[idx].AdxPeriod == adxPeriod && cacheMTF_ADXTrendMonitor[idx].MinAdx == minAdx && cacheMTF_ADXTrendMonitor[idx].SlopeLookback == slopeLookback && cacheMTF_ADXTrendMonitor[idx].TablePosition == tablePosition && cacheMTF_ADXTrendMonitor[idx].EqualsInput(input))
						return cacheMTF_ADXTrendMonitor[idx];
			return CacheIndicator<MTF_ADXTrendMonitor>(new MTF_ADXTrendMonitor(){ ShowFast = showFast, MidMinutes = midMinutes, ShowMid = showMid, SlowMinutes = slowMinutes, ShowSlow = showSlow, UltraMinutes = ultraMinutes, ShowUltra = showUltra, AdxPeriod = adxPeriod, MinAdx = minAdx, SlopeLookback = slopeLookback, TablePosition = tablePosition }, input, ref cacheMTF_ADXTrendMonitor);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MTF_ADXTrendMonitor MTF_ADXTrendMonitor(bool showFast, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int ultraMinutes, bool showUltra, int adxPeriod, double minAdx, int slopeLookback, TextPosition tablePosition)
		{
			return indicator.MTF_ADXTrendMonitor(Input, showFast, midMinutes, showMid, slowMinutes, showSlow, ultraMinutes, showUltra, adxPeriod, minAdx, slopeLookback, tablePosition);
		}

		public Indicators.MTF_ADXTrendMonitor MTF_ADXTrendMonitor(ISeries<double> input , bool showFast, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int ultraMinutes, bool showUltra, int adxPeriod, double minAdx, int slopeLookback, TextPosition tablePosition)
		{
			return indicator.MTF_ADXTrendMonitor(input, showFast, midMinutes, showMid, slowMinutes, showSlow, ultraMinutes, showUltra, adxPeriod, minAdx, slopeLookback, tablePosition);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MTF_ADXTrendMonitor MTF_ADXTrendMonitor(bool showFast, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int ultraMinutes, bool showUltra, int adxPeriod, double minAdx, int slopeLookback, TextPosition tablePosition)
		{
			return indicator.MTF_ADXTrendMonitor(Input, showFast, midMinutes, showMid, slowMinutes, showSlow, ultraMinutes, showUltra, adxPeriod, minAdx, slopeLookback, tablePosition);
		}

		public Indicators.MTF_ADXTrendMonitor MTF_ADXTrendMonitor(ISeries<double> input , bool showFast, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int ultraMinutes, bool showUltra, int adxPeriod, double minAdx, int slopeLookback, TextPosition tablePosition)
		{
			return indicator.MTF_ADXTrendMonitor(input, showFast, midMinutes, showMid, slowMinutes, showSlow, ultraMinutes, showUltra, adxPeriod, minAdx, slopeLookback, tablePosition);
		}
	}
}

#endregion
