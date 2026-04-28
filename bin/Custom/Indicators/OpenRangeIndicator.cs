// 
// Copyright (C) 2016, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
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
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// This namespace holds indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
	
	/// <summary>
	/// </summary>
	public class OpenRangeIndicator : Indicator
	{
		double dHighestPrice = 0.0;
		double dLowestPrice = 0.0;
		double dMidPrice = 0.0;
		double dUpperRange = 0.0;
		double dLowerRange = 0.0;
		
		DateTime dtStartTime;
		DateTime dtEndTime;
		
		bool bShowMidLine = false;
		bool bExtendLines = false;
		bool bShowRangeLines = false;
		
		double dRangeValue = 5.0;
	
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "Open Range Indicator";
				Name						= "Open Range Indicator";
				IsOverlay					= true;
				IsSuspendedWhileInactive	= true;
				StartTime					= DateTime.Parse("08:30", System.Globalization.CultureInfo.InvariantCulture);
				EndTime						= DateTime.Parse("09:30", System.Globalization.CultureInfo.InvariantCulture);
				
				HighDashStyleHelper = DashStyleHelper.Solid;
				MidDashStyleHelper = DashStyleHelper.Solid;
				LowDashStyleHelper = DashStyleHelper.Solid;
				UpperRangeDashStyleHelper = DashStyleHelper.Solid;
				LowerRangeDashStyleHelper = DashStyleHelper.Solid;
				
				HighColor = Brushes.Red;
				MidColor = Brushes.Blue;
				LowColor = Brushes.Red;
				UpperRangeColor = Brushes.Red;
				LowerRangeColor = Brushes.Red;

				HighLineWidth = 2;
				MidLineWidth = 2;
				LowLineWidth = 2;
				UpperRangeLineWidth = 2;
				LowerRangeLineWidth = 2;
				
				RangeValue = 5.0;
				
				ShowMidLine = false;
				ExtendLines = false;
				ShowRangeLines = false;
				
			}
			else if (State == State.Configure)
			{
        		AddDataSeries(BarsPeriodType.Minute, 1);
				
			}
			else if (State == State.DataLoaded)
			{
				dtStartTime = StartTime;
				dtEndTime = EndTime;
				
				bExtendLines = ExtendLines;
				bShowMidLine = ShowMidLine;
				bShowRangeLines = ShowRangeLines;
				
				dRangeValue = RangeValue;
			}
		}

		protected override void OnBarUpdate()
		{
			DateTime dt = Time[0];
			
			dtStartTime = new DateTime (dt.Year, dt.Month, dt.Day, dtStartTime.Hour, dtStartTime.Minute, dtStartTime.Second);
			dtEndTime  = new DateTime (dt.Year, dt.Month, dt.Day, dtEndTime.Hour, dtEndTime.Minute, dtEndTime.Second);

			if (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute )
   			{
				if (CurrentBar < 2)
				{
					return;
				}

				if( BarsPeriod.Value == 1)
				{
					if (ToTime(Time[0]) < ToTime(dtStartTime))
					{
						dHighestPrice = 0.0;
						dLowestPrice = 0.0;
					}
					else if (ToTime(Time[0]) == ToTime(dtStartTime))
					{
						dHighestPrice = High[0];
						dLowestPrice = Low[0];
						dMidPrice = dLowestPrice + (dHighestPrice-dLowestPrice)/2.0;
						dUpperRange = dHighestPrice + dRangeValue;
						dLowerRange = dLowestPrice - dRangeValue;
					}
					else if (ToTime(Time[0]) > ToTime(dtStartTime) && ToTime(Time[0]) <= ToTime(dtEndTime))
					{
						if(High[0] > dHighestPrice)
							dHighestPrice = High[0];
						if(Low[0] < dLowestPrice)
							dLowestPrice = Low[0];
						
						dMidPrice = dLowestPrice + (dHighestPrice-dLowestPrice)/2.0;
						dUpperRange = dHighestPrice + dRangeValue;
						dLowerRange = dLowestPrice - dRangeValue;
					}
				}
			}
			

			if (dHighestPrice > 0.0 && dLowestPrice > 0.0)
			{
				if(bExtendLines)
				{
					DateTime dtNewEndTime  = new DateTime (dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);		
					Draw.Line(this, "High1" + dt.Month + dt.Day + dt.Year, false, dtStartTime, dHighestPrice, dtNewEndTime, dHighestPrice, HighColor, HighDashStyleHelper, HighLineWidth);
					Draw.Line(this, "Low1" + dt.Month + dt.Day + dt.Year, false, dtStartTime, dLowestPrice, dtNewEndTime, dLowestPrice, LowColor, LowDashStyleHelper, LowLineWidth);

					if(bShowMidLine)
					{
						Draw.Line(this, "Mid1" + dt.Month + dt.Day + dt.Year, false, dtStartTime, dMidPrice, dtNewEndTime, dMidPrice, MidColor, MidDashStyleHelper, MidLineWidth);
					}

					if(bShowRangeLines)
					{
						Draw.Line(this, "Upper1" + dt.Month + dt.Day + dt.Year, false, dtStartTime, dUpperRange, dtNewEndTime, dUpperRange, UpperRangeColor, UpperRangeDashStyleHelper, UpperRangeLineWidth);
						Draw.Line(this, "Lower1" + dt.Month + dt.Day + dt.Year, false, dtStartTime, dLowerRange, dtNewEndTime, dLowerRange, LowerRangeColor, LowerRangeDashStyleHelper, LowerRangeLineWidth);
					}
				}
				else
				{
					Draw.Line(this, "High1" + dt.Month + dt.Day + dt.Year, false, dtStartTime, dHighestPrice, dtEndTime, dHighestPrice, HighColor, HighDashStyleHelper, HighLineWidth);
					Draw.Line(this, "Low1" + dt.Month + dt.Day + dt.Year, false, dtStartTime, dLowestPrice, dtEndTime, dLowestPrice, LowColor, LowDashStyleHelper, LowLineWidth);

					if(bShowMidLine)
					{
						Draw.Line(this, "Mid1" + dt.Month + dt.Day + dt.Year, false, dtStartTime, dMidPrice, dtEndTime, dMidPrice, MidColor, MidDashStyleHelper, MidLineWidth);
					}

					if(bShowRangeLines)
					{
						Draw.Line(this, "Upper1" + dt.Month + dt.Day + dt.Year, false, dtStartTime, dUpperRange, dtEndTime, dUpperRange, UpperRangeColor, UpperRangeDashStyleHelper, UpperRangeLineWidth);
						Draw.Line(this, "Lower1" + dt.Month + dt.Day + dt.Year, false, dtStartTime, dLowerRange, dtEndTime, dLowerRange, LowerRangeColor, LowerRangeDashStyleHelper, LowerRangeLineWidth);
					}
				}
			}
		}


		#region Properties
		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Start Time", GroupName = "NinjaScriptParameters", Order = 0)]
		public DateTime StartTime
		{ get; set; }
		
		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(ResourceType = typeof(Custom.Resource), Name = "End Time", GroupName = "NinjaScriptParameters", Order = 1)]
		public DateTime EndTime
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "High Line Style", GroupName = "NinjaScriptParameters", Order = 2)]
		public DashStyleHelper HighDashStyleHelper
		{ get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "High Line Width", GroupName = "NinjaScriptParameters", Order = 3)]
		public int HighLineWidth
		{ get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "High Line Color", GroupName = "NinjaScriptParameters", Order = 4)]
		public Brush HighColor
		{ get; set; }

		[Browsable(false)]
		public string HighColorSerializable
		{
			get { return Serialize.BrushToString(HighColor); }
			set { HighColor = Serialize.StringToBrush(value); }
		}			
		
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Mid Line Style", GroupName = "NinjaScriptParameters", Order = 5)]
		public DashStyleHelper MidDashStyleHelper
		{ get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Mid Line Width", GroupName = "NinjaScriptParameters", Order = 6)]
		public int MidLineWidth
		{ get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Mid Line Color", GroupName = "NinjaScriptParameters", Order = 7)]
		public Brush MidColor
		{ get; set; }

		[Browsable(false)]
		public string MidColorSerializable
		{
			get { return Serialize.BrushToString(MidColor); }
			set { MidColor = Serialize.StringToBrush(value); }
		}			

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Low Line Style", GroupName = "NinjaScriptParameters", Order = 8)]
		public DashStyleHelper LowDashStyleHelper
		{ get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Low Line Width", GroupName = "NinjaScriptParameters", Order = 9)]
		public int LowLineWidth
		{ get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Low Line Color", GroupName = "NinjaScriptParameters", Order = 10)]
		public Brush LowColor
		{ get; set; }

		[Browsable(false)]
		public string LowColorSerializable
		{
			get { return Serialize.BrushToString(LowColor); }
			set { LowColor = Serialize.StringToBrush(value); }
		}			

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Upper Range Line Style", GroupName = "NinjaScriptParameters", Order = 11)]
		public DashStyleHelper UpperRangeDashStyleHelper
		{ get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Upper Range Line Width", GroupName = "NinjaScriptParameters", Order = 12)]
		public int UpperRangeLineWidth
		{ get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Upper Range Line Color", GroupName = "NinjaScriptParameters", Order = 13)]
		public Brush UpperRangeColor
		{ get; set; }

		[Browsable(false)]
		public string UpperRangeColorSerializable
		{
			get { return Serialize.BrushToString(UpperRangeColor); }
			set { UpperRangeColor = Serialize.StringToBrush(value); }
		}			

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Lower Range Line Style", GroupName = "NinjaScriptParameters", Order = 14)]
		public DashStyleHelper LowerRangeDashStyleHelper
		{ get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Lower Range Line Width", GroupName = "NinjaScriptParameters", Order = 15)]
		public int LowerRangeLineWidth
		{ get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Lower Range Line Color", GroupName = "NinjaScriptParameters", Order = 16)]
		public Brush LowerRangeColor
		{ get; set; }

		[Browsable(false)]
		public string LowerRangeColorSerializable
		{
			get { return Serialize.BrushToString(LowerRangeColor); }
			set { LowerRangeColor = Serialize.StringToBrush(value); }
		}			

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Extend Line", GroupName = "NinjaScriptParameters", Order = 17)]
		public bool ExtendLines
		{ get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Range Value", GroupName = "NinjaScriptParameters", Order = 18)]
		public double RangeValue
		{ get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show Mid Price Line", GroupName = "NinjaScriptParameters", Order = 19)]
		public bool ShowMidLine
		{ get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show Upper/Lower Range", GroupName = "NinjaScriptParameters", Order = 20)]
		public bool ShowRangeLines
		{ get; set; }

		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private OpenRangeIndicator[] cacheOpenRangeIndicator;
		public OpenRangeIndicator OpenRangeIndicator(DateTime startTime, DateTime endTime, DashStyleHelper highDashStyleHelper, int highLineWidth, Brush highColor, DashStyleHelper midDashStyleHelper, int midLineWidth, Brush midColor, DashStyleHelper lowDashStyleHelper, int lowLineWidth, Brush lowColor, DashStyleHelper upperRangeDashStyleHelper, int upperRangeLineWidth, Brush upperRangeColor, DashStyleHelper lowerRangeDashStyleHelper, int lowerRangeLineWidth, Brush lowerRangeColor, bool extendLines, double rangeValue, bool showMidLine, bool showRangeLines)
		{
			return OpenRangeIndicator(Input, startTime, endTime, highDashStyleHelper, highLineWidth, highColor, midDashStyleHelper, midLineWidth, midColor, lowDashStyleHelper, lowLineWidth, lowColor, upperRangeDashStyleHelper, upperRangeLineWidth, upperRangeColor, lowerRangeDashStyleHelper, lowerRangeLineWidth, lowerRangeColor, extendLines, rangeValue, showMidLine, showRangeLines);
		}

		public OpenRangeIndicator OpenRangeIndicator(ISeries<double> input, DateTime startTime, DateTime endTime, DashStyleHelper highDashStyleHelper, int highLineWidth, Brush highColor, DashStyleHelper midDashStyleHelper, int midLineWidth, Brush midColor, DashStyleHelper lowDashStyleHelper, int lowLineWidth, Brush lowColor, DashStyleHelper upperRangeDashStyleHelper, int upperRangeLineWidth, Brush upperRangeColor, DashStyleHelper lowerRangeDashStyleHelper, int lowerRangeLineWidth, Brush lowerRangeColor, bool extendLines, double rangeValue, bool showMidLine, bool showRangeLines)
		{
			if (cacheOpenRangeIndicator != null)
				for (int idx = 0; idx < cacheOpenRangeIndicator.Length; idx++)
					if (cacheOpenRangeIndicator[idx] != null && cacheOpenRangeIndicator[idx].StartTime == startTime && cacheOpenRangeIndicator[idx].EndTime == endTime && cacheOpenRangeIndicator[idx].HighDashStyleHelper == highDashStyleHelper && cacheOpenRangeIndicator[idx].HighLineWidth == highLineWidth && cacheOpenRangeIndicator[idx].HighColor == highColor && cacheOpenRangeIndicator[idx].MidDashStyleHelper == midDashStyleHelper && cacheOpenRangeIndicator[idx].MidLineWidth == midLineWidth && cacheOpenRangeIndicator[idx].MidColor == midColor && cacheOpenRangeIndicator[idx].LowDashStyleHelper == lowDashStyleHelper && cacheOpenRangeIndicator[idx].LowLineWidth == lowLineWidth && cacheOpenRangeIndicator[idx].LowColor == lowColor && cacheOpenRangeIndicator[idx].UpperRangeDashStyleHelper == upperRangeDashStyleHelper && cacheOpenRangeIndicator[idx].UpperRangeLineWidth == upperRangeLineWidth && cacheOpenRangeIndicator[idx].UpperRangeColor == upperRangeColor && cacheOpenRangeIndicator[idx].LowerRangeDashStyleHelper == lowerRangeDashStyleHelper && cacheOpenRangeIndicator[idx].LowerRangeLineWidth == lowerRangeLineWidth && cacheOpenRangeIndicator[idx].LowerRangeColor == lowerRangeColor && cacheOpenRangeIndicator[idx].ExtendLines == extendLines && cacheOpenRangeIndicator[idx].RangeValue == rangeValue && cacheOpenRangeIndicator[idx].ShowMidLine == showMidLine && cacheOpenRangeIndicator[idx].ShowRangeLines == showRangeLines && cacheOpenRangeIndicator[idx].EqualsInput(input))
						return cacheOpenRangeIndicator[idx];
			return CacheIndicator<OpenRangeIndicator>(new OpenRangeIndicator(){ StartTime = startTime, EndTime = endTime, HighDashStyleHelper = highDashStyleHelper, HighLineWidth = highLineWidth, HighColor = highColor, MidDashStyleHelper = midDashStyleHelper, MidLineWidth = midLineWidth, MidColor = midColor, LowDashStyleHelper = lowDashStyleHelper, LowLineWidth = lowLineWidth, LowColor = lowColor, UpperRangeDashStyleHelper = upperRangeDashStyleHelper, UpperRangeLineWidth = upperRangeLineWidth, UpperRangeColor = upperRangeColor, LowerRangeDashStyleHelper = lowerRangeDashStyleHelper, LowerRangeLineWidth = lowerRangeLineWidth, LowerRangeColor = lowerRangeColor, ExtendLines = extendLines, RangeValue = rangeValue, ShowMidLine = showMidLine, ShowRangeLines = showRangeLines }, input, ref cacheOpenRangeIndicator);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.OpenRangeIndicator OpenRangeIndicator(DateTime startTime, DateTime endTime, DashStyleHelper highDashStyleHelper, int highLineWidth, Brush highColor, DashStyleHelper midDashStyleHelper, int midLineWidth, Brush midColor, DashStyleHelper lowDashStyleHelper, int lowLineWidth, Brush lowColor, DashStyleHelper upperRangeDashStyleHelper, int upperRangeLineWidth, Brush upperRangeColor, DashStyleHelper lowerRangeDashStyleHelper, int lowerRangeLineWidth, Brush lowerRangeColor, bool extendLines, double rangeValue, bool showMidLine, bool showRangeLines)
		{
			return indicator.OpenRangeIndicator(Input, startTime, endTime, highDashStyleHelper, highLineWidth, highColor, midDashStyleHelper, midLineWidth, midColor, lowDashStyleHelper, lowLineWidth, lowColor, upperRangeDashStyleHelper, upperRangeLineWidth, upperRangeColor, lowerRangeDashStyleHelper, lowerRangeLineWidth, lowerRangeColor, extendLines, rangeValue, showMidLine, showRangeLines);
		}

		public Indicators.OpenRangeIndicator OpenRangeIndicator(ISeries<double> input , DateTime startTime, DateTime endTime, DashStyleHelper highDashStyleHelper, int highLineWidth, Brush highColor, DashStyleHelper midDashStyleHelper, int midLineWidth, Brush midColor, DashStyleHelper lowDashStyleHelper, int lowLineWidth, Brush lowColor, DashStyleHelper upperRangeDashStyleHelper, int upperRangeLineWidth, Brush upperRangeColor, DashStyleHelper lowerRangeDashStyleHelper, int lowerRangeLineWidth, Brush lowerRangeColor, bool extendLines, double rangeValue, bool showMidLine, bool showRangeLines)
		{
			return indicator.OpenRangeIndicator(input, startTime, endTime, highDashStyleHelper, highLineWidth, highColor, midDashStyleHelper, midLineWidth, midColor, lowDashStyleHelper, lowLineWidth, lowColor, upperRangeDashStyleHelper, upperRangeLineWidth, upperRangeColor, lowerRangeDashStyleHelper, lowerRangeLineWidth, lowerRangeColor, extendLines, rangeValue, showMidLine, showRangeLines);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.OpenRangeIndicator OpenRangeIndicator(DateTime startTime, DateTime endTime, DashStyleHelper highDashStyleHelper, int highLineWidth, Brush highColor, DashStyleHelper midDashStyleHelper, int midLineWidth, Brush midColor, DashStyleHelper lowDashStyleHelper, int lowLineWidth, Brush lowColor, DashStyleHelper upperRangeDashStyleHelper, int upperRangeLineWidth, Brush upperRangeColor, DashStyleHelper lowerRangeDashStyleHelper, int lowerRangeLineWidth, Brush lowerRangeColor, bool extendLines, double rangeValue, bool showMidLine, bool showRangeLines)
		{
			return indicator.OpenRangeIndicator(Input, startTime, endTime, highDashStyleHelper, highLineWidth, highColor, midDashStyleHelper, midLineWidth, midColor, lowDashStyleHelper, lowLineWidth, lowColor, upperRangeDashStyleHelper, upperRangeLineWidth, upperRangeColor, lowerRangeDashStyleHelper, lowerRangeLineWidth, lowerRangeColor, extendLines, rangeValue, showMidLine, showRangeLines);
		}

		public Indicators.OpenRangeIndicator OpenRangeIndicator(ISeries<double> input , DateTime startTime, DateTime endTime, DashStyleHelper highDashStyleHelper, int highLineWidth, Brush highColor, DashStyleHelper midDashStyleHelper, int midLineWidth, Brush midColor, DashStyleHelper lowDashStyleHelper, int lowLineWidth, Brush lowColor, DashStyleHelper upperRangeDashStyleHelper, int upperRangeLineWidth, Brush upperRangeColor, DashStyleHelper lowerRangeDashStyleHelper, int lowerRangeLineWidth, Brush lowerRangeColor, bool extendLines, double rangeValue, bool showMidLine, bool showRangeLines)
		{
			return indicator.OpenRangeIndicator(input, startTime, endTime, highDashStyleHelper, highLineWidth, highColor, midDashStyleHelper, midLineWidth, midColor, lowDashStyleHelper, lowLineWidth, lowColor, upperRangeDashStyleHelper, upperRangeLineWidth, upperRangeColor, lowerRangeDashStyleHelper, lowerRangeLineWidth, lowerRangeColor, extendLines, rangeValue, showMidLine, showRangeLines);
		}
	}
}

#endregion
