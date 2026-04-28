#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class DeltaCandleEMA : Indicator
    {
        private EMA emaIndicator;
        private Series<double> cumulativeDelta;
        private double sessionCumulativeDelta = 0;

        #region Properties
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "Period for EMA calculation", Order = 1, GroupName = "EMA Parameters")]
        public int EmaPeriod { get; set; } = 60;

        [Display(Name = "EMA Color", Description = "Color of EMA line", Order = 2, GroupName = "EMA Parameters")]
        public Brush EmaColor { get; set; } = Brushes.Red;

        [Range(1, 10)]
        [Display(Name = "EMA Width", Description = "Width of EMA line", Order = 3, GroupName = "EMA Parameters")]
        public int EmaWidth { get; set; } = 2;

        [Display(Name = "Delta Type", Description = "Type of delta calculation", Order = 4, GroupName = "Delta Parameters")]
        public DeltaType DeltaCalculationType { get; set; } = DeltaType.BidAsk;

        [Display(Name = "Session Reset", Description = "Reset delta at session start", Order = 5, GroupName = "Delta Parameters")]
        public bool ResetOnSessionStart { get; set; } = true;

        [Display(Name = "Show Delta Candles", Description = "Show cumulative delta as candles", Order = 6, GroupName = "Delta Parameters")]
        public bool ShowDeltaCandles { get; set; } = true;

        [Display(Name = "Bull Candle Color", Description = "Color for bullish delta candles", Order = 7, GroupName = "Delta Parameters")]
        public Brush BullCandleColor { get; set; } = Brushes.Green;

        [Display(Name = "Bear Candle Color", Description = "Color for bearish delta candles", Order = 8, GroupName = "Delta Parameters")]
        public Brush BearCandleColor { get; set; } = Brushes.Red;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Cumulative Delta Candle Chart with adjustable EMA";
                Name = "DeltaCandleEMA";
                Calculate = Calculate.OnEachTick;
                IsOverlay = false;
                DisplayInDataBox = true;
                DrawOnPricePanel = false;
                IsSuspendedWhileInactive = true;
                ScaleJustification = ScaleJustification.Right;

                AddPlot(new Stroke(EmaColor, EmaWidth), PlotStyle.Line, "EMA");
                AddPlot(Brushes.Transparent, "CumulativeDelta");
            }
            else if (State == State.DataLoaded)
            {
                cumulativeDelta = new Series<double>(this);
                emaIndicator = EMA(cumulativeDelta, EmaPeriod);
            }
            else if (State == State.Historical)
            {
                sessionCumulativeDelta = 0;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            if (ResetOnSessionStart && Bars.IsFirstBarOfSession)
                sessionCumulativeDelta = 0;

            // Simplified but more accurate delta calculation
            double currentBarDelta = CalculateBarDelta();
            sessionCumulativeDelta += currentBarDelta;
            cumulativeDelta[0] = sessionCumulativeDelta;

            double emaValue = (CurrentBar >= EmaPeriod) ? emaIndicator[0] : sessionCumulativeDelta;
            Values[0][0] = emaValue;
            Values[1][0] = sessionCumulativeDelta;

            // Debug prints
            if (CurrentBar % 60 == 0 || CurrentBar == 1)
            {
                Print($"[Bar {CurrentBar}] Delta: {sessionCumulativeDelta}, EMA: {emaValue}");
            }
        }

        private double CalculateBarDelta()
        {
            // More conservative delta calculation
            double delta = 0;
            
            if (DeltaCalculationType == DeltaType.BidAsk)
            {
                // Much more conservative calculation
                double priceChange = Close[0] - Open[0];
                double range = High[0] - Low[0];
                
                if (range > 0)
                {
                    double pricePosition = (Close[0] - Low[0]) / range;
                    delta = Volume[0] * (pricePosition - 0.5); // Range from -0.5 to +0.5
                }
            }
            else
            {
                // Up/Down tick method - also more conservative
                if (CurrentBar > 0)
                {
                    if (Close[0] > Close[1])
                        delta = Volume[0] * 0.3; // Reduced from 1.0
                    else if (Close[0] < Close[1])
                        delta = Volume[0] * -0.3; // Reduced from -1.0
                }
            }
            
            return delta;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (!ShowDeltaCandles || chartControl == null || cumulativeDelta == null || ChartBars == null)
                return;

            try
            {
                float barWidth = (float)chartControl.GetBarPaintWidth(ChartBars);

                for (int idx = ChartBars.FromIndex; idx <= ChartBars.ToIndex; idx++)
                {
                    if (idx < 0 || idx >= cumulativeDelta.Count)
                        continue;

                    double open = idx == 0 ? 0 : cumulativeDelta[idx - 1];
                    double close = cumulativeDelta[idx];
                    double high = Math.Max(open, close);
                    double low = Math.Min(open, close);

                    float x = chartControl.GetXByBarIndex(ChartBars, idx);
                    float yHigh = chartScale.GetYByValue(high);
                    float yLow = chartScale.GetYByValue(low);
                    float yOpen = chartScale.GetYByValue(open);
                    float yClose = chartScale.GetYByValue(close);

                    Brush bodyBrush = close >= open ? BullCandleColor : BearCandleColor;
                    Brush wickBrush = Brushes.Gray;

                    SharpDX.Direct2D1.SolidColorBrush bodyDxBrush = null;
                    SharpDX.Direct2D1.SolidColorBrush wickDxBrush = null;

                    if (bodyBrush is SolidColorBrush solidBodyBrush)
                    {
                        bodyDxBrush = new SharpDX.Direct2D1.SolidColorBrush(
                            RenderTarget,
                            new SharpDX.Color(
                                solidBodyBrush.Color.R,
                                solidBodyBrush.Color.G,
                                solidBodyBrush.Color.B,
                                solidBodyBrush.Color.A
                            )
                        );
                    }
                    if (wickBrush is SolidColorBrush solidWickBrush)
                    {
                        wickDxBrush = new SharpDX.Direct2D1.SolidColorBrush(
                            RenderTarget,
                            new SharpDX.Color(
                                solidWickBrush.Color.R,
                                solidWickBrush.Color.G,
                                solidWickBrush.Color.B,
                                solidWickBrush.Color.A
                            )
                        );
                    }

                    // Draw candle body
                    if (bodyDxBrush != null)
                    {
                        SharpDX.RectangleF bodyRect = new SharpDX.RectangleF(
                            x - barWidth / 2,
                            Math.Min(yOpen, yClose),
                            barWidth,
                            Math.Abs(yClose - yOpen)
                        );
                        if (bodyRect.Height > 0)
                        {
                            RenderTarget.FillRectangle(bodyRect, bodyDxBrush);
                        }
                        bodyDxBrush.Dispose();
                    }

                    // Draw wick
                    if (wickDxBrush != null && yHigh != yLow)
                    {
                        SharpDX.Vector2 start = new SharpDX.Vector2(x, yHigh);
                        SharpDX.Vector2 end = new SharpDX.Vector2(x, yLow);
                        RenderTarget.DrawLine(start, end, wickDxBrush, 1);
                        wickDxBrush.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"Rendering error: {ex.Message}");
            }
        }
    }

    public enum DeltaType
    {
        BidAsk,
        UpDownTick
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private DeltaCandleEMA[] cacheDeltaCandleEMA;
		public DeltaCandleEMA DeltaCandleEMA()
		{
			return DeltaCandleEMA(Input);
		}

		public DeltaCandleEMA DeltaCandleEMA(ISeries<double> input)
		{
			if (cacheDeltaCandleEMA != null)
				for (int idx = 0; idx < cacheDeltaCandleEMA.Length; idx++)
					if (cacheDeltaCandleEMA[idx] != null &&  cacheDeltaCandleEMA[idx].EqualsInput(input))
						return cacheDeltaCandleEMA[idx];
			return CacheIndicator<DeltaCandleEMA>(new DeltaCandleEMA(), input, ref cacheDeltaCandleEMA);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.DeltaCandleEMA DeltaCandleEMA()
		{
			return indicator.DeltaCandleEMA(Input);
		}

		public Indicators.DeltaCandleEMA DeltaCandleEMA(ISeries<double> input )
		{
			return indicator.DeltaCandleEMA(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.DeltaCandleEMA DeltaCandleEMA()
		{
			return indicator.DeltaCandleEMA(Input);
		}

		public Indicators.DeltaCandleEMA DeltaCandleEMA(ISeries<double> input )
		{
			return indicator.DeltaCandleEMA(input);
		}
	}
}

#endregion
