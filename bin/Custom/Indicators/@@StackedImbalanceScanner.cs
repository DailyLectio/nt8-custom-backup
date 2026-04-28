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
    public class StackedImbalanceScanner : Indicator
    {
        private string currentSignal = "WAITING...";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = @"Detects 3+ Stacked Imbalances and displays a UI Table.";
                Name                        = "StackedImbalanceScanner";
                Calculate                   = Calculate.OnBarClose; 
                IsOverlay                   = true;
                DisplayInDataBox            = true;
                DrawOnPricePanel            = true;
                DrawHorizontalGridLines     = true;
                DrawVerticalGridLines       = true;
                PaintPriceMarkers           = true;
                ScaleJustification          = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive    = true;
                
                ImbalanceRatio              = 3.0;
                StackedLevelsRequired       = 3;
                
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
            
            bool signalFoundThisBar = false;

            // ==========================================
            // BULLISH STACKED IMBALANCE CHECK
            // ==========================================
            bool isDeltaExpandingBullish = currentDelta > 0 && currentDelta > prevDelta;
            
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
                            currentSignal = "BULLISH DETECTED";
                            signalFoundThisBar = true;
                            break; 
                        }
                    }
                    else
                    {
                        stackedCount = 0; 
                    }
                }
            }

            // ==========================================
            // BEARISH STACKED IMBALANCE CHECK
            // ==========================================
            bool isDeltaExpandingBearish = currentDelta < 0 && currentDelta < prevDelta;
            
            if (!signalFoundThisBar && Close[0] <= Open[0] && isDeltaExpandingBearish)
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
                            currentSignal = "BEARISH DETECTED";
                            signalFoundThisBar = true;
                            break; 
                        }
                    }
                    else
                    {
                        stackedCount = 0; 
                    }
                }
            }

            // Reset signal if nothing happened this bar (optional, remove if you want it to linger)
            if (!signalFoundThisBar)
            {
                currentSignal = "WAITING...";
            }

            // ==========================================
            // DRAW THE UI TABLE
            // ==========================================
            string displayText = "ORDER FLOW DASHBOARD\n";
            displayText += "----------------------\n";
            displayText += "Stacked Imb: " + currentSignal + "\n";
            displayText += "Absorption : COMING SOON\n";
            displayText += "Delta Div  : COMING SOON";

            // Draw.TextFixed pins the text to a specific corner of the chart
            Draw.TextFixed(this, "DashboardTable", displayText, TableLocation, TableText, 
                           new NinjaTrader.Gui.Tools.SimpleFont("Consolas", 14), 
                           Brushes.Transparent, TableBackground, 80);
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1.0, double.MaxValue)]
        [Display(Name="ImbalanceRatio", Description="Minimum ratio for Bid x Ask (e.g. 3.0)", Order=1, GroupName="1. Parameters")]
        public double ImbalanceRatio { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="StackedLevelsRequired", Description="Number of consecutive levels to trigger", Order=2, GroupName="1. Parameters")]
        public int StackedLevelsRequired { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Table Location", Description="Choose which corner to display the table", Order=1, GroupName="2. UI Settings")]
        public TextPosition TableLocation { get; set; }
        
        [XmlIgnore]
        [Display(Name="Table Background", Description="Background color of the table", Order=2, GroupName="2. UI Settings")]
        public Brush TableBackground { get; set; }
        
        [Browsable(false)]
        public string TableBackgroundSerializable
        {
            get { return Serialize.BrushToString(TableBackground); }
            set { TableBackground = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name="Text Color", Description="Color of the table text", Order=3, GroupName="2. UI Settings")]
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
		private StackedImbalanceScanner[] cacheStackedImbalanceScanner;
		public StackedImbalanceScanner StackedImbalanceScanner(double imbalanceRatio, int stackedLevelsRequired, TextPosition tableLocation)
		{
			return StackedImbalanceScanner(Input, imbalanceRatio, stackedLevelsRequired, tableLocation);
		}

		public StackedImbalanceScanner StackedImbalanceScanner(ISeries<double> input, double imbalanceRatio, int stackedLevelsRequired, TextPosition tableLocation)
		{
			if (cacheStackedImbalanceScanner != null)
				for (int idx = 0; idx < cacheStackedImbalanceScanner.Length; idx++)
					if (cacheStackedImbalanceScanner[idx] != null && cacheStackedImbalanceScanner[idx].ImbalanceRatio == imbalanceRatio && cacheStackedImbalanceScanner[idx].StackedLevelsRequired == stackedLevelsRequired && cacheStackedImbalanceScanner[idx].TableLocation == tableLocation && cacheStackedImbalanceScanner[idx].EqualsInput(input))
						return cacheStackedImbalanceScanner[idx];
			return CacheIndicator<StackedImbalanceScanner>(new StackedImbalanceScanner(){ ImbalanceRatio = imbalanceRatio, StackedLevelsRequired = stackedLevelsRequired, TableLocation = tableLocation }, input, ref cacheStackedImbalanceScanner);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.StackedImbalanceScanner StackedImbalanceScanner(double imbalanceRatio, int stackedLevelsRequired, TextPosition tableLocation)
		{
			return indicator.StackedImbalanceScanner(Input, imbalanceRatio, stackedLevelsRequired, tableLocation);
		}

		public Indicators.StackedImbalanceScanner StackedImbalanceScanner(ISeries<double> input , double imbalanceRatio, int stackedLevelsRequired, TextPosition tableLocation)
		{
			return indicator.StackedImbalanceScanner(input, imbalanceRatio, stackedLevelsRequired, tableLocation);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.StackedImbalanceScanner StackedImbalanceScanner(double imbalanceRatio, int stackedLevelsRequired, TextPosition tableLocation)
		{
			return indicator.StackedImbalanceScanner(Input, imbalanceRatio, stackedLevelsRequired, tableLocation);
		}

		public Indicators.StackedImbalanceScanner StackedImbalanceScanner(ISeries<double> input , double imbalanceRatio, int stackedLevelsRequired, TextPosition tableLocation)
		{
			return indicator.StackedImbalanceScanner(input, imbalanceRatio, stackedLevelsRequired, tableLocation);
		}
	}
}

#endregion
