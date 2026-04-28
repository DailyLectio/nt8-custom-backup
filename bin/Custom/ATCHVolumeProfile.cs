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

#endregion



#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		
		private ATCHVolumeProfile[] cacheATCHVolumeProfile;

		
		public ATCHVolumeProfile ATCHVolumeProfile(bool useOrderFlowData, int widthPercentage, int profileBarsPeriod, string sessionInterval, int tickPerLevel, int valueAreaPercent, bool autoMergeProfiles, int profileBodyOpacity, int valueAreaOpacity, bool showPOC, bool showValueArea, bool showLabels, int fontSize)
		{
			return ATCHVolumeProfile(Input, useOrderFlowData, widthPercentage, profileBarsPeriod, sessionInterval, tickPerLevel, valueAreaPercent, autoMergeProfiles, profileBodyOpacity, valueAreaOpacity, showPOC, showValueArea, showLabels, fontSize);
		}


		
		public ATCHVolumeProfile ATCHVolumeProfile(ISeries<double> input, bool useOrderFlowData, int widthPercentage, int profileBarsPeriod, string sessionInterval, int tickPerLevel, int valueAreaPercent, bool autoMergeProfiles, int profileBodyOpacity, int valueAreaOpacity, bool showPOC, bool showValueArea, bool showLabels, int fontSize)
		{
			if (cacheATCHVolumeProfile != null)
				for (int idx = 0; idx < cacheATCHVolumeProfile.Length; idx++)
					if (cacheATCHVolumeProfile[idx].UseOrderFlowData == useOrderFlowData && cacheATCHVolumeProfile[idx].WidthPercentage == widthPercentage && cacheATCHVolumeProfile[idx].ProfileBarsPeriod == profileBarsPeriod && cacheATCHVolumeProfile[idx].SessionInterval == sessionInterval && cacheATCHVolumeProfile[idx].TickPerLevel == tickPerLevel && cacheATCHVolumeProfile[idx].ValueAreaPercent == valueAreaPercent && cacheATCHVolumeProfile[idx].AutoMergeProfiles == autoMergeProfiles && cacheATCHVolumeProfile[idx].ProfileBodyOpacity == profileBodyOpacity && cacheATCHVolumeProfile[idx].ValueAreaOpacity == valueAreaOpacity && cacheATCHVolumeProfile[idx].ShowPOC == showPOC && cacheATCHVolumeProfile[idx].ShowValueArea == showValueArea && cacheATCHVolumeProfile[idx].ShowLabels == showLabels && cacheATCHVolumeProfile[idx].FontSize == fontSize && cacheATCHVolumeProfile[idx].EqualsInput(input))
						return cacheATCHVolumeProfile[idx];
			return CacheIndicator<ATCHVolumeProfile>(new ATCHVolumeProfile(){ UseOrderFlowData = useOrderFlowData, WidthPercentage = widthPercentage, ProfileBarsPeriod = profileBarsPeriod, SessionInterval = sessionInterval, TickPerLevel = tickPerLevel, ValueAreaPercent = valueAreaPercent, AutoMergeProfiles = autoMergeProfiles, ProfileBodyOpacity = profileBodyOpacity, ValueAreaOpacity = valueAreaOpacity, ShowPOC = showPOC, ShowValueArea = showValueArea, ShowLabels = showLabels, FontSize = fontSize }, input, ref cacheATCHVolumeProfile);
		}

	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		
		public Indicators.ATCHVolumeProfile ATCHVolumeProfile(bool useOrderFlowData, int widthPercentage, int profileBarsPeriod, string sessionInterval, int tickPerLevel, int valueAreaPercent, bool autoMergeProfiles, int profileBodyOpacity, int valueAreaOpacity, bool showPOC, bool showValueArea, bool showLabels, int fontSize)
		{
			return indicator.ATCHVolumeProfile(Input, useOrderFlowData, widthPercentage, profileBarsPeriod, sessionInterval, tickPerLevel, valueAreaPercent, autoMergeProfiles, profileBodyOpacity, valueAreaOpacity, showPOC, showValueArea, showLabels, fontSize);
		}


		
		public Indicators.ATCHVolumeProfile ATCHVolumeProfile(ISeries<double> input , bool useOrderFlowData, int widthPercentage, int profileBarsPeriod, string sessionInterval, int tickPerLevel, int valueAreaPercent, bool autoMergeProfiles, int profileBodyOpacity, int valueAreaOpacity, bool showPOC, bool showValueArea, bool showLabels, int fontSize)
		{
			return indicator.ATCHVolumeProfile(input, useOrderFlowData, widthPercentage, profileBarsPeriod, sessionInterval, tickPerLevel, valueAreaPercent, autoMergeProfiles, profileBodyOpacity, valueAreaOpacity, showPOC, showValueArea, showLabels, fontSize);
		}
	
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		
		public Indicators.ATCHVolumeProfile ATCHVolumeProfile(bool useOrderFlowData, int widthPercentage, int profileBarsPeriod, string sessionInterval, int tickPerLevel, int valueAreaPercent, bool autoMergeProfiles, int profileBodyOpacity, int valueAreaOpacity, bool showPOC, bool showValueArea, bool showLabels, int fontSize)
		{
			return indicator.ATCHVolumeProfile(Input, useOrderFlowData, widthPercentage, profileBarsPeriod, sessionInterval, tickPerLevel, valueAreaPercent, autoMergeProfiles, profileBodyOpacity, valueAreaOpacity, showPOC, showValueArea, showLabels, fontSize);
		}


		
		public Indicators.ATCHVolumeProfile ATCHVolumeProfile(ISeries<double> input , bool useOrderFlowData, int widthPercentage, int profileBarsPeriod, string sessionInterval, int tickPerLevel, int valueAreaPercent, bool autoMergeProfiles, int profileBodyOpacity, int valueAreaOpacity, bool showPOC, bool showValueArea, bool showLabels, int fontSize)
		{
			return indicator.ATCHVolumeProfile(input, useOrderFlowData, widthPercentage, profileBarsPeriod, sessionInterval, tickPerLevel, valueAreaPercent, autoMergeProfiles, profileBodyOpacity, valueAreaOpacity, showPOC, showValueArea, showLabels, fontSize);
		}

	}
}

#endregion
