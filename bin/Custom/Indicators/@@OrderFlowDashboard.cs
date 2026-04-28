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

namespace NinjaTrader.NinjaScript.Indicators
{
    public class OrderFlowDashboard : Indicator
    {
        private string imbSignal = "-";
        private string absSignal = "-";
        private string divSignal = "-";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = @"Dashboard for Stacked Imbalances, Absorption, and Delta Divergence.";
                Name                        = "OrderFlowDashboard";
                Calculate                   = Calculate.OnBarClose; 
                IsOverlay                   = true;
                DisplayInDataBox            = true;
                DrawOnPricePanel            = true;
                ScaleJustification          = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive    = true;
                
                // Imbalance Params
                ImbalanceRatio              = 3.0;
                StackedLevelsRequired       = 3;
                
                // Absorption Params
                VolumeThreshold             = 1000;
                MaxRangeTicks               = 5;
                
                // UI Defaults
                TableLocation               = TextPosition.TopRight;
                TableBackground             = Brushes.DimGray;
                TableText                   = Brushes.White;
            }
            else if (State == State.Configure)
            {
                ClearOutputWindow();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1 || Bars == null) return;
            
            NinjaTrader.NinjaScript.BarsTypes.VolumetricBarsType volBar = Bars.BarsSeries.BarsType as NinjaTrader.NinjaScript.BarsTypes.VolumetricBarsType;
            if (volBar == null) return; 
            
            long currentDelta = volBar.Volumes[CurrentBar].BarDelta;
            long prevDelta = volBar.Volumes[CurrentBar - 1].BarDelta;
            long totalVolume = volBar.Volumes[CurrentBar].TotalVolume;
            double barRangeTicks = (High[0] - Low[0]) / TickSize;

            // Reset signals for the new bar
            imbSignal = "-";
            absSignal = "-";
            divSignal = "-";

            // ==========================================
            // 1. STACKED IMBALANCE CHECK
            // ==========================================
            bool isDeltaExpandingBullish = currentDelta > 0 && currentDelta > prevDelta;
            bool isDeltaExpandingBearish = currentDelta < 0 && currentDelta < prevDelta;
            
            if (Close[0] >= Open[0] && isDeltaExpandingBullish)
            {
                int stackedCount = 0;
                for (double price = Low[0]; price <= High[0]; price += TickSize)
                {
                    double askVol = volBar.Volumes[CurrentBar].GetAskVolumeForPrice(price);
                    double bidVol = volBar.Volumes[CurrentBar].GetBidVolumeForPrice(price - TickSize);
                    
                    bool isImbalance = (bidVol == 0 && askVol > 0) || (bidVol > 0 && (askVol / bidVol) >= ImbalanceRatio);
                    if (isImbalance)
                    {
                        stackedCount++;
                        if (stackedCount >= StackedLevelsRequired)
                        {
                            Draw.ArrowUp(this, "BullStack_" + CurrentBar, true, 0, Low[0] - (TickSize * 5), Brushes.Cyan);
                            imbSignal = "BULLISH";
                            break; 
                        }
                    }
                    else stackedCount = 0; 
                }
            }
            else if (Close[0] <= Open[0] && isDeltaExpandingBearish)
            {
                int stackedCount = 0;
                for (double price = High[0]; price >= Low[0]; price -= TickSize)
                {
                    double bidVol = volBar.Volumes[CurrentBar].GetBidVolumeForPrice(price);
                    double askVol = volBar.Volumes[CurrentBar].GetAskVolumeForPrice(price + TickSize);
                    
                    bool isImbalance = (askVol == 0 && bidVol > 0) || (askVol > 0 && (bidVol / askVol) >= ImbalanceRatio);
                    if (isImbalance)
                    {
                        stackedCount++;
                        if (stackedCount >= StackedLevelsRequired)
                        {
                            Draw.ArrowDown(this, "BearStack_" + CurrentBar, true, 0, High[0] + (TickSize * 5), Brushes.Magenta);
                            imbSignal = "BEARISH";
                            break; 
                        }
                    }
                    else stackedCount = 0; 
                }
            }

            // ==========================================
            // 2. ABSORPTION REVERSAL CHECK
            // ==========================================
            if (totalVolume >= VolumeThreshold && barRangeTicks <= MaxRangeTicks)
            {
                if (currentDelta < 0) 
                {
                    // High volume, tight range, negative delta = Sellers being absorbed
                    absSignal = "BULLISH (Buyers Absorbing)";
                    Draw.Diamond(this, "BullAbs_" + CurrentBar, true, 0, Low[0] - (TickSize * 8), Brushes.LimeGreen);
                }
                else if (currentDelta > 0)
                {
                    // High volume, tight range, positive delta = Buyers being absorbed
                    absSignal = "BEARISH (Sellers Absorbing)";
                    Draw.Diamond(this, "BearAbs_" + CurrentBar, true, 0, High[0] + (TickSize * 8), Brushes.Red);
                }
            }

            // ==========================================
            // 3. DELTA DIVERGENCE CHECK
            // ==========================================
            if (High[0] > High[1] && currentDelta < 0)
            {
                divSignal = "BEARISH (HH but Delta < 0)";
                Draw.TriangleDown(this, "BearDiv_" + CurrentBar, true, 0, High[0] + (TickSize * 11), Brushes.Orange);
            }
            else if (Low[0] < Low[1] && currentDelta > 0)
            {
                divSignal = "BULLISH (LL but Delta > 0)";
                Draw.TriangleUp(this, "BullDiv_" + CurrentBar, true, 0, Low[0] - (TickSize * 11), Brushes.Yellow);
            }

            // ==========================================
            // DRAW THE UI TABLE
            // ==========================================
            string displayText = "ORDER FLOW DASHBOARD\n";
            displayText += "------------------------------\n";
            displayText += "Stacked Imb: " + imbSignal + "\n";
            displayText += "Absorption : " + absSignal + "\n";
            displayText += "Delta Div  : " + divSignal;

            Draw.TextFixed(this, "DashboardTable", displayText, TableLocation, TableText, 
                           new NinjaTrader.Gui.Tools.SimpleFont("Consolas", 14), 
                           Brushes.Transparent, TableBackground, 80);
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1.0, double.MaxValue)]
        [Display(Name="ImbalanceRatio", Description="Minimum ratio for Bid x Ask", Order=1, GroupName="1. Imbalance Params")]
        public double ImbalanceRatio { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="StackedLevelsRequired", Description="Number of consecutive levels", Order=2, GroupName="1. Imbalance Params")]
        public int StackedLevelsRequired { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Volume Threshold", Description="Minimum Volume for Absorption", Order=1, GroupName="2. Absorption Params")]
        public int VolumeThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Max Range (Ticks)", Description="Max bar range to qualify as Absorption", Order=2, GroupName="2. Absorption Params")]
        public int MaxRangeTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Table Location", Description="Choose corner to display table", Order=1, GroupName="3. UI Settings")]
        public TextPosition TableLocation { get; set; }
        
        [XmlIgnore]
        [Display(Name="Table Background", Description="Background color", Order=2, GroupName="3. UI Settings")]
        public Brush TableBackground { get; set; }
        
        [Browsable(false)]
        public string TableBackgroundSerializable
        {
            get { return Serialize.BrushToString(TableBackground); }
            set { TableBackground = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name="Text Color", Description="Text color", Order=3, GroupName="3. UI Settings")]
        public Brush TableText { get; set; }
        
        [Browsable(false)]
        public string TableTextSerializable
        {
            get { return Serialize.BrushToString(TableText); }
            set { TableText = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private OrderFlowDashboard[] cacheOrderFlowDashboard;
		public OrderFlowDashboard OrderFlowDashboard(double imbalanceRatio, int stackedLevelsRequired, int volumeThreshold, int maxRangeTicks, TextPosition tableLocation)
		{
			return OrderFlowDashboard(Input, imbalanceRatio, stackedLevelsRequired, volumeThreshold, maxRangeTicks, tableLocation);
		}

		public OrderFlowDashboard OrderFlowDashboard(ISeries<double> input, double imbalanceRatio, int stackedLevelsRequired, int volumeThreshold, int maxRangeTicks, TextPosition tableLocation)
		{
			if (cacheOrderFlowDashboard != null)
				for (int idx = 0; idx < cacheOrderFlowDashboard.Length; idx++)
					if (cacheOrderFlowDashboard[idx] != null && cacheOrderFlowDashboard[idx].ImbalanceRatio == imbalanceRatio && cacheOrderFlowDashboard[idx].StackedLevelsRequired == stackedLevelsRequired && cacheOrderFlowDashboard[idx].VolumeThreshold == volumeThreshold && cacheOrderFlowDashboard[idx].MaxRangeTicks == maxRangeTicks && cacheOrderFlowDashboard[idx].TableLocation == tableLocation && cacheOrderFlowDashboard[idx].EqualsInput(input))
						return cacheOrderFlowDashboard[idx];
			return CacheIndicator<OrderFlowDashboard>(new OrderFlowDashboard(){ ImbalanceRatio = imbalanceRatio, StackedLevelsRequired = stackedLevelsRequired, VolumeThreshold = volumeThreshold, MaxRangeTicks = maxRangeTicks, TableLocation = tableLocation }, input, ref cacheOrderFlowDashboard);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.OrderFlowDashboard OrderFlowDashboard(double imbalanceRatio, int stackedLevelsRequired, int volumeThreshold, int maxRangeTicks, TextPosition tableLocation)
		{
			return indicator.OrderFlowDashboard(Input, imbalanceRatio, stackedLevelsRequired, volumeThreshold, maxRangeTicks, tableLocation);
		}

		public Indicators.OrderFlowDashboard OrderFlowDashboard(ISeries<double> input , double imbalanceRatio, int stackedLevelsRequired, int volumeThreshold, int maxRangeTicks, TextPosition tableLocation)
		{
			return indicator.OrderFlowDashboard(input, imbalanceRatio, stackedLevelsRequired, volumeThreshold, maxRangeTicks, tableLocation);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.OrderFlowDashboard OrderFlowDashboard(double imbalanceRatio, int stackedLevelsRequired, int volumeThreshold, int maxRangeTicks, TextPosition tableLocation)
		{
			return indicator.OrderFlowDashboard(Input, imbalanceRatio, stackedLevelsRequired, volumeThreshold, maxRangeTicks, tableLocation);
		}

		public Indicators.OrderFlowDashboard OrderFlowDashboard(ISeries<double> input , double imbalanceRatio, int stackedLevelsRequired, int volumeThreshold, int maxRangeTicks, TextPosition tableLocation)
		{
			return indicator.OrderFlowDashboard(input, imbalanceRatio, stackedLevelsRequired, volumeThreshold, maxRangeTicks, tableLocation);
		}
	}
}

#endregion
