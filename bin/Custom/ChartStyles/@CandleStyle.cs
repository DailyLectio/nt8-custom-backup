// 
// Copyright (C) 2025, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using SharpDX;
using SharpDX.Direct2D1;
using System;
#endregion

namespace NinjaTrader.NinjaScript.ChartStyles
{
	public class CandleStyle : ChartStyle
	{
		private object icon;

		public override int GetBarPaintWidth(int barWidth) => 1 + 2 * (barWidth - 1) + 2 * (int) Math.Round(Stroke.Width);

		public override object Icon => icon ??= Gui.Tools.Icons.ChartChartStyle;

		public override void OnRender(ChartControl chartControl, ChartScale chartScale, ChartBars chartBars)
		{
			Bars			bars			= chartBars.Bars;
			float			barWidth		= GetBarPaintWidth(BarWidthUI);
			Vector2			point0			= new();
			Vector2			point1			= new();
			RectangleF		rect			= new();

			for (int idx = chartBars.FromIndex; idx <= chartBars.ToIndex; idx++)
			{
				Brush		overriddenBarBrush		= chartControl.GetBarOverrideBrush(chartBars, idx);
				Brush		overriddenOutlineBrush	= chartControl.GetCandleOutlineOverrideBrush(chartBars, idx);
				double		closeValue				= bars.GetClose(idx);
				double		highValue				= bars.GetHigh(idx);
				double		lowValue				= bars.GetLow(idx);
				double		openValue				= bars.GetOpen(idx);
				int			close					= chartScale.GetYByValue(closeValue);
				int			high					= chartScale.GetYByValue(highValue);
				int			low						= chartScale.GetYByValue(lowValue);
				int			open					= chartScale.GetYByValue(openValue);
				int			x						= chartControl.GetXByBarIndex(chartBars, idx);

				if (Math.Abs(open - close) < 0.0000000001)
				{
					// Line 
					point0.X	= x - barWidth * 0.5f;
					point0.Y	= close;
					point1.X	= x + barWidth * 0.5f;
					point1.Y	= close;
					Brush b		= overriddenOutlineBrush ?? Stroke.BrushDX;
					if (b is not SolidColorBrush)
						TransformBrush(overriddenOutlineBrush ?? Stroke.BrushDX, new RectangleF(point0.X, point0.Y - Stroke.Width, barWidth, Stroke.Width));
					RenderTarget.DrawLine(point0, point1, b, Stroke.Width, Stroke.StrokeStyle);
				}
				else
				{
					// Candle
					rect.X		= x - barWidth * 0.5f + 0.5f;
					rect.Y		= Math.Min(close, open);
					rect.Width	= barWidth - 1;
					rect.Height	= Math.Max(open, close) - Math.Min(close, open);

					// Rectangle fill
					Brush brush	= overriddenBarBrush ?? (closeValue >= openValue ? UpBrushDX : DownBrushDX);
					if (brush is not SolidColorBrush)
						TransformBrush(brush, rect);
					RenderTarget.FillRectangle(rect, brush);

					// Rectangle border
					brush = overriddenOutlineBrush ?? Stroke.BrushDX;
					if (brush is not SolidColorBrush)
						TransformBrush(brush, rect);
					RenderTarget.DrawRectangle(rect, brush ?? Stroke.BrushDX, Stroke.Width, Stroke.StrokeStyle);
				}

				Brush br = overriddenOutlineBrush ?? Stroke2.BrushDX;

				// High wick
				if (highValue > Math.Max(openValue, closeValue))
				{
					point0.X	= x;
					point0.Y	= high;
					point1.X	= x;
					point1.Y	= openValue > closeValue ? open : close;
					if (br is not SolidColorBrush)
						TransformBrush(br, new RectangleF(point0.X - Stroke2.Width, point0.Y, Stroke2.Width, point1.Y - point0.Y));
					RenderTarget.DrawLine(point0, point1, br, Stroke2.Width, Stroke2.StrokeStyle);
				}

				// Low wick
				if (lowValue < Math.Min(openValue, closeValue))
				{
					point0.X = x;
					point0.Y = low;
					point1.X = x;
					point1.Y = openValue < closeValue ? open : close;
					if (br is not SolidColorBrush)
						TransformBrush(br, new RectangleF(point1.X - Stroke2.Width, point1.Y, Stroke2.Width, point0.Y - point1.Y));
					RenderTarget.DrawLine(point0, point1, br, Stroke2.Width, Stroke2.StrokeStyle);
				}
			}
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name			= Custom.Resource.NinjaScriptChartStyleCandlestick;
				ChartStyleType	= ChartStyleType.CandleStick;
			}
			else if (State == State.Configure)
			{
				SetPropertyName("BarWidth",		Custom.Resource.NinjaScriptChartStyleBarWidth);
				SetPropertyName("DownBrush",	Custom.Resource.NinjaScriptChartStyleCandleDownBarsColor);
				SetPropertyName("UpBrush",		Custom.Resource.NinjaScriptChartStyleCandleUpBarsColor);
				SetPropertyName("Stroke",		Custom.Resource.NinjaScriptChartStyleCandleOutline);
				SetPropertyName("Stroke2",		Custom.Resource.NinjaScriptChartStyleCandleWick);
			}
		}
	}
}
