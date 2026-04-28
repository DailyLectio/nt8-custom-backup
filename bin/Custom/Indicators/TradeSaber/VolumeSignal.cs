#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.TradeSaber
{
    public class SimpleVolumeSignal : Indicator
    {
        private bool lastSignalWasUp = false;
        private bool hasPrintedSignal = false;
        private SMA sma;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Plots volume histogram and arrows based on volume conditions";
                Name = "SimpleVolumeSignal";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;

                UseVolumeThreshold = true;
                VolumeThreshold = 3000;
                UsePercentAboveSMA = true;
                SMAPeriod = 200;
                PercentAboveSMA = 50;

                UpArrowTag = "VolHigh";
                DownArrowTag = "VolLow";
                ShowFirstSignalOnly = true;
                ClearArrowsOnNewSession = false;

                AddPlot(new Stroke(Brushes.SteelBlue, 2), PlotStyle.Bar, "Volume");
                AddPlot(new Stroke(Brushes.White, 2), PlotStyle.Line, "VolumeSMA");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < SMAPeriod)
                return;

            // Clear all draw objects at the start of a new session if enabled
            if (ClearArrowsOnNewSession && Bars.IsFirstBarOfSession && IsFirstTickOfBar)
            {
                RemoveDrawObjects(); // Removes all drawing objects
            }

            sma = SMA(Volume, SMAPeriod);
            Values[0][0] = Volume[0];
            Values[1][0] = sma[0];

            PlotBrushes[0][0] = Brushes.SteelBlue;

            // Calculate conditions
            bool isVolumeAboveThreshold = Volume[0] > VolumeThreshold;
            double smaMultiplier = 1 + (PercentAboveSMA / 100);
            bool meetsSMAPercentCondition = Volume[0] >= sma[0] * smaMultiplier;

            // Determine signal condition based on selected options
            bool signalCondition = true;
            if (UseVolumeThreshold)
                signalCondition &= isVolumeAboveThreshold;
            if (UsePercentAboveSMA)
                signalCondition &= meetsSMAPercentCondition;

            // Apply color
            if (signalCondition)
            {
                PlotBrushes[0][0] = Brushes.DarkOrange;
            }

            // List to stage draw/remove actions
            List<Action> drawActions = new List<Action>();

            if (!ShowFirstSignalOnly)
            {
                if (signalCondition)
                {
                    drawActions.Add(() => RemoveDrawObject(DownArrowTag + CurrentBar));
                    drawActions.Add(() => Draw.ArrowUp(this, UpArrowTag + CurrentBar, true, 0, Low[0], Brushes.Green));
                }
                else
                {
                    // Avoid enumeration; assume no up arrow if we're adding a down arrow
                    drawActions.Add(() => RemoveDrawObject(UpArrowTag + CurrentBar));
                    drawActions.Add(() => Draw.ArrowDown(this, DownArrowTag + CurrentBar, true, 0, High[0], Brushes.Red));
                }
            }
            else
            {
                if (signalCondition)
                {
                    if (!hasPrintedSignal || !lastSignalWasUp)
                    {
                        drawActions.Add(() => RemoveDrawObject(DownArrowTag + CurrentBar));
                        drawActions.Add(() => Draw.ArrowUp(this, UpArrowTag + CurrentBar, true, 0, Low[0], Brushes.Green));
                        lastSignalWasUp = true;
                        hasPrintedSignal = true;
                    }
                }
                else
                {
                    if (!hasPrintedSignal || lastSignalWasUp)
                    {
                        drawActions.Add(() => RemoveDrawObject(UpArrowTag + CurrentBar));
                        drawActions.Add(() => Draw.ArrowDown(this, DownArrowTag + CurrentBar, true, 0, High[0], Brushes.Red));
                        lastSignalWasUp = false;
                        hasPrintedSignal = true;
                    }
                }
            }

            // Execute all staged actions
            foreach (var action in drawActions)
            {
                action();
            }
        }

        #region Properties for Strategy Builder
        [Browsable(false)]
        public double VolumeValue
        {
            get { return Volume[0]; }
        }

        [Browsable(false)]
        public double SMAValue
        {
            get { return sma[0]; }
        }
        #endregion

        #region Parameters
        [NinjaScriptProperty]
        [Display(Name = "Above Volume Number", Description = "Color bars above volume threshold", Order = 1, GroupName = "Parameters")]
        public bool UseVolumeThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "⤷ Volume Threshold", Description = "Volume level to compare against", Order = 2, GroupName = "Parameters")]
        public int VolumeThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Above Volume Average", Description = "Color bars above SMA percentage", Order = 3, GroupName = "Parameters")]
        public bool UsePercentAboveSMA { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "⤷ SMA Period", Description = "Period for Simple Moving Average", Order = 4, GroupName = "Parameters")]
        public int SMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(-100, double.MaxValue)]
        [Display(Name = "⤷ Percent Above SMA", Description = "Required percentage above/below SMA (negative for below)", Order = 5, GroupName = "Parameters")]
        public double PercentAboveSMA { get; set; }
        #endregion

        #region Predator X Signals
        [NinjaScriptProperty]
        [Display(Name = "Up Arrow Tag", Description = "Tag for up arrows", Order = 6, GroupName = "Predator X Signals")]
        public string UpArrowTag { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Down Arrow Tag", Description = "Tag for down arrows", Order = 7, GroupName = "Predator X Signals")]
        public string DownArrowTag { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show First Signal Only", Description = "Only show the first signal until the opposite signal occurs", Order = 8, GroupName = "Predator X Signals")]
        public bool ShowFirstSignalOnly { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Clear Arrows on New Session", Description = "Remove all previous arrows at the start of a new session", Order = 9, GroupName = "Predator X Signals")]
        public bool ClearArrowsOnNewSession { get; set; }
        #endregion

        [Browsable(false)]
        public bool IsVolumeAboveThreshold
        {
            get { return Volume[0] > VolumeThreshold; }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TradeSaber.SimpleVolumeSignal[] cacheSimpleVolumeSignal;
		public TradeSaber.SimpleVolumeSignal SimpleVolumeSignal(bool useVolumeThreshold, int volumeThreshold, bool usePercentAboveSMA, int sMAPeriod, double percentAboveSMA, string upArrowTag, string downArrowTag, bool showFirstSignalOnly, bool clearArrowsOnNewSession)
		{
			return SimpleVolumeSignal(Input, useVolumeThreshold, volumeThreshold, usePercentAboveSMA, sMAPeriod, percentAboveSMA, upArrowTag, downArrowTag, showFirstSignalOnly, clearArrowsOnNewSession);
		}

		public TradeSaber.SimpleVolumeSignal SimpleVolumeSignal(ISeries<double> input, bool useVolumeThreshold, int volumeThreshold, bool usePercentAboveSMA, int sMAPeriod, double percentAboveSMA, string upArrowTag, string downArrowTag, bool showFirstSignalOnly, bool clearArrowsOnNewSession)
		{
			if (cacheSimpleVolumeSignal != null)
				for (int idx = 0; idx < cacheSimpleVolumeSignal.Length; idx++)
					if (cacheSimpleVolumeSignal[idx] != null && cacheSimpleVolumeSignal[idx].UseVolumeThreshold == useVolumeThreshold && cacheSimpleVolumeSignal[idx].VolumeThreshold == volumeThreshold && cacheSimpleVolumeSignal[idx].UsePercentAboveSMA == usePercentAboveSMA && cacheSimpleVolumeSignal[idx].SMAPeriod == sMAPeriod && cacheSimpleVolumeSignal[idx].PercentAboveSMA == percentAboveSMA && cacheSimpleVolumeSignal[idx].UpArrowTag == upArrowTag && cacheSimpleVolumeSignal[idx].DownArrowTag == downArrowTag && cacheSimpleVolumeSignal[idx].ShowFirstSignalOnly == showFirstSignalOnly && cacheSimpleVolumeSignal[idx].ClearArrowsOnNewSession == clearArrowsOnNewSession && cacheSimpleVolumeSignal[idx].EqualsInput(input))
						return cacheSimpleVolumeSignal[idx];
			return CacheIndicator<TradeSaber.SimpleVolumeSignal>(new TradeSaber.SimpleVolumeSignal(){ UseVolumeThreshold = useVolumeThreshold, VolumeThreshold = volumeThreshold, UsePercentAboveSMA = usePercentAboveSMA, SMAPeriod = sMAPeriod, PercentAboveSMA = percentAboveSMA, UpArrowTag = upArrowTag, DownArrowTag = downArrowTag, ShowFirstSignalOnly = showFirstSignalOnly, ClearArrowsOnNewSession = clearArrowsOnNewSession }, input, ref cacheSimpleVolumeSignal);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TradeSaber.SimpleVolumeSignal SimpleVolumeSignal(bool useVolumeThreshold, int volumeThreshold, bool usePercentAboveSMA, int sMAPeriod, double percentAboveSMA, string upArrowTag, string downArrowTag, bool showFirstSignalOnly, bool clearArrowsOnNewSession)
		{
			return indicator.SimpleVolumeSignal(Input, useVolumeThreshold, volumeThreshold, usePercentAboveSMA, sMAPeriod, percentAboveSMA, upArrowTag, downArrowTag, showFirstSignalOnly, clearArrowsOnNewSession);
		}

		public Indicators.TradeSaber.SimpleVolumeSignal SimpleVolumeSignal(ISeries<double> input , bool useVolumeThreshold, int volumeThreshold, bool usePercentAboveSMA, int sMAPeriod, double percentAboveSMA, string upArrowTag, string downArrowTag, bool showFirstSignalOnly, bool clearArrowsOnNewSession)
		{
			return indicator.SimpleVolumeSignal(input, useVolumeThreshold, volumeThreshold, usePercentAboveSMA, sMAPeriod, percentAboveSMA, upArrowTag, downArrowTag, showFirstSignalOnly, clearArrowsOnNewSession);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TradeSaber.SimpleVolumeSignal SimpleVolumeSignal(bool useVolumeThreshold, int volumeThreshold, bool usePercentAboveSMA, int sMAPeriod, double percentAboveSMA, string upArrowTag, string downArrowTag, bool showFirstSignalOnly, bool clearArrowsOnNewSession)
		{
			return indicator.SimpleVolumeSignal(Input, useVolumeThreshold, volumeThreshold, usePercentAboveSMA, sMAPeriod, percentAboveSMA, upArrowTag, downArrowTag, showFirstSignalOnly, clearArrowsOnNewSession);
		}

		public Indicators.TradeSaber.SimpleVolumeSignal SimpleVolumeSignal(ISeries<double> input , bool useVolumeThreshold, int volumeThreshold, bool usePercentAboveSMA, int sMAPeriod, double percentAboveSMA, string upArrowTag, string downArrowTag, bool showFirstSignalOnly, bool clearArrowsOnNewSession)
		{
			return indicator.SimpleVolumeSignal(input, useVolumeThreshold, volumeThreshold, usePercentAboveSMA, sMAPeriod, percentAboveSMA, upArrowTag, downArrowTag, showFirstSignalOnly, clearArrowsOnNewSession);
		}
	}
}

#endregion
