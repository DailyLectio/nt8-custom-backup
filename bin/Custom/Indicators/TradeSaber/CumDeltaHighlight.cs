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

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators.TradeSaber
{
	public class CumDeltaHighlight : Indicator
	{
		private OrderFlowCumulativeDelta cumulativeDelta;
		
        private const string SystemName 					= "Cum Delta Highlight";
		
		private double isAbove			= 0;
		private double isBelow			= 0;
		
		private int isAboveOpacity	 	= 25;
		private int isBelowOpacity	 	= 25;
		
		private bool highlightChart								= false;
		private bool highlightIndicator							= true;
		
		private int	sizeFilter			= 0;
		
		#region Enums
		
		private DeltaCalc	deltaCalc	= DeltaCalc.Close;
		
		public enum DeltaCalc
		{
			Close,
			HighLow,	
		}
		
		private DeltaType	deltaType	= DeltaType.BidAsk;
		
		public enum DeltaType
		{
			BidAsk,
			UpDownTick,	
		}
		
		
		private PeriodType	periodType	= PeriodType.Session;
		
		public enum PeriodType
		{
			Session,
			Bar,	
		}
		
		#endregion
		
		#region TradeSaber Social
		
		private string author 								= "TradeSaber(Dre)";
		private string version 								= "Version 1.3 // December 2022";
		
		private string youtube								= "https://youtu.be/o8st_QX_JLE"; 
		private string discord								= "https://discord.gg/2YU9GDme8j";
		private string tradeSaber							= "https://tradesaber.com/";
		
		private bool showSocials;
		
		private bool youtubeButtonClicked;
		private bool discordButtonClicked;
		private bool tradeSaberButtonClicked;
		
		private System.Windows.Controls.Button youtubeButton;
		private System.Windows.Controls.Button discordButton;
		private System.Windows.Controls.Button tradeSaberButton;
		
		
		private System.Windows.Controls.Grid myGrid29;
		
		#endregion
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Will highlight the Cumulative Delta zones";
				Name										= SystemName;
				Calculate									= Calculate.OnEachTick;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= false;
				DrawVerticalGridLines						= false;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
				
				IsAboveColor 								= Brushes.Green;
				IsBelowColor								= Brushes.Red;
				
				highlightChart								= false;
				highlightIndicator							= true;				
				
				AddLine(new Stroke (Brushes.Aqua, DashStyleHelper.Dash, 2), isAbove, "UpperLevel");
				AddLine(new Stroke (Brushes.Aqua, DashStyleHelper.Dash, 2), isBelow, "LowerLevel");
				
				showSocials									= true;
				
				
			}
			else if (State == State.Configure)
			{
				AddDataSeries(Data.BarsPeriodType.Tick, 1);
				
				Brush tempB = IsAboveColor.Clone(); //Copy the brush into a temporary brush
				tempB.Opacity = isAboveOpacity / 100.0; // set the opacity
				tempB.Freeze(); // freeze the temp brush
				IsAboveColor = tempB; // assign the temp brush value to OverBoughtArea.
				
				
				Brush tempS = IsBelowColor.Clone(); //Copy the brush into a temporary brush
				tempS.Opacity = isBelowOpacity / 100.0; // set the opacity
				tempS.Freeze(); // freeze the temp brush
				IsBelowColor = tempS; // assign the temp brush value to OverSoldArea.
				
			}
			else if (State == State.DataLoaded)
			{				
			      // Instantiate the indicator
			      cumulativeDelta = OrderFlowCumulativeDelta(CumulativeDeltaType.UpDownTick, CumulativeDeltaPeriod.Bar, 0);

			}
			
			#region Add Buttons with Links
			
		else if (State == State.Historical)
		{
			#region TradeSaber Socials
			
			if (showSocials)
			{
				if (UserControlCollection.Contains(myGrid29))
					return;
				
				Dispatcher.InvokeAsync((() =>
				{
					myGrid29 = new System.Windows.Controls.Grid
					{
						Name = "MyCustomGrid", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom
					};
					
					System.Windows.Controls.ColumnDefinition column1 = new System.Windows.Controls.ColumnDefinition();
					System.Windows.Controls.ColumnDefinition column2 = new System.Windows.Controls.ColumnDefinition();
					System.Windows.Controls.ColumnDefinition column3 = new System.Windows.Controls.ColumnDefinition();
					
					myGrid29.ColumnDefinitions.Add(column1);
					myGrid29.ColumnDefinitions.Add(column2);
					myGrid29.ColumnDefinitions.Add(column3);
					
					youtubeButton = new System.Windows.Controls.Button
					{
						Name = "YoutubeButton", Content = "Youtube", Foreground = Brushes.White, Background = Brushes.Red
					};
					
					discordButton = new System.Windows.Controls.Button
					{
						Name = "DiscordButton", Content = "Discord", Foreground = Brushes.White, Background = Brushes.RoyalBlue
					};
					
					tradeSaberButton = new System.Windows.Controls.Button
					{
						Name = "TradeSaberButton", Content = "TradeSaber", Foreground = Brushes.White, Background = Brushes.DarkOrange
					};
					
					youtubeButton.Click += OnButtonClick;
					discordButton.Click += OnButtonClick;
					tradeSaberButton.Click += OnButtonClick;
					
					System.Windows.Controls.Grid.SetColumn(youtubeButton, 0);
					System.Windows.Controls.Grid.SetColumn(discordButton, 1);
					System.Windows.Controls.Grid.SetColumn(tradeSaberButton, 2);
					
					myGrid29.Children.Add(youtubeButton);
					myGrid29.Children.Add(discordButton);
					myGrid29.Children.Add(tradeSaberButton);
					
					UserControlCollection.Add(myGrid29);
				}));
			}
		#endregion
			
			else if (State == State.Terminated)
			{
				#region Terminate TradeSaber Socials
			
			if (showSocials)
			{
				Dispatcher.InvokeAsync((() =>
				{
					if (myGrid29 != null)
					{
						if (youtubeButton != null)
						{
							myGrid29.Children.Remove(youtubeButton);
							youtubeButton.Click -= OnButtonClick;
							youtubeButton = null;
						}
						
						if (discordButton != null)
						{
							myGrid29.Children.Remove(discordButton);
							discordButton.Click -= OnButtonClick;
							discordButton = null;
						}
						
						if (tradeSaberButton != null)
						{
							myGrid29.Children.Remove(tradeSaberButton);
							tradeSaberButton.Click -= OnButtonClick;
							tradeSaberButton = null;
						}		
					}
				}));
			}
		#endregion
			}
		}
		#endregion	
			
		}

		
		protected override void OnBarUpdate()
		{
			
			if (BarsInProgress == 0)
			{
				BackBrushAll = null;
				BackBrush = null;
				
			#region Delta Type
				
			//Declare Variable First
			var myDelta = CumulativeDeltaType.BidAsk;
	
				
				switch (deltaType)
				{
					case DeltaType.BidAsk:
					{
						myDelta = CumulativeDeltaType.BidAsk;
						//Print("BidAsk  " + myDelta);
			
					}break;
					
					
					case DeltaType.UpDownTick:
					{
						 myDelta = CumulativeDeltaType.UpDownTick;
						//Print("UpDown  " + myDelta);
						
					}break;
	
				}
			#endregion
			
			#region Period
				
			//Declare Variable First
			var myPeriod = CumulativeDeltaPeriod.Session;
	
				
				switch (periodType)
				{
					case PeriodType.Session:
					{
						myPeriod = CumulativeDeltaPeriod.Session;
						
					}break;
					
					
					case PeriodType.Bar:
					{
						 myPeriod = CumulativeDeltaPeriod.Bar;
						
					}break;
	
				}
			#endregion
				
			#region Delta calc
				
			switch (deltaCalc)
				{	
			
					
				///Uses Delta Close to color Region		
				case DeltaCalc.Close:
					{
			
					
				if (highlightChart)
					{
						if (OrderFlowCumulativeDelta(BarsArray[0], myDelta, myPeriod, sizeFilter).DeltaClose[0] > isAbove)
						{
							BackBrushesAll[0] = IsAboveColor;
						}
						
						if (OrderFlowCumulativeDelta(BarsArray[0], myDelta, myPeriod, sizeFilter).DeltaClose[0] < isBelow)
						{
							BackBrushesAll[0] = IsBelowColor;
						}
					}
				
				if (highlightIndicator)
					{
						if (OrderFlowCumulativeDelta(BarsArray[0], myDelta, myPeriod, sizeFilter).DeltaClose[0] > isAbove)
						{
							BackBrushes[0] = IsAboveColor;
						}
						
						if (OrderFlowCumulativeDelta(BarsArray[0], myDelta, myPeriod, sizeFilter).DeltaClose[0] < isBelow)
						{
							BackBrushes[0] = IsBelowColor;
						}
						
					}
					
					}break;
			
			///Uses High and Low of Delta to color Region		
				case DeltaCalc.HighLow:
					{
						
						if (highlightChart)
					{
						if (OrderFlowCumulativeDelta(BarsArray[0], myDelta, myPeriod, sizeFilter).DeltaHigh[0] > isAbove)
						{
							BackBrushesAll[0] = IsAboveColor;
						}
						
						if (OrderFlowCumulativeDelta(BarsArray[0], myDelta, myPeriod, sizeFilter).DeltaLow[0] < isBelow)
						{
							BackBrushesAll[0] = IsBelowColor;
						}
					}
				
				if (highlightIndicator)
					{
						if (OrderFlowCumulativeDelta(BarsArray[0], myDelta, myPeriod, sizeFilter).DeltaHigh[0] > isAbove)
						{
							BackBrushes[0] = IsAboveColor;
						}
						
						if (OrderFlowCumulativeDelta(BarsArray[0], myDelta, myPeriod, sizeFilter).DeltaLow[0] < isBelow)
						{
							BackBrushes[0] = IsBelowColor;
						}
					}
					
					}break;	
						
				}
				
				#endregion
				
			}
			
			else if (BarsInProgress == 1)
			{
			      // We have to update the secondary series of the hosted indicator to make sure the values we get in BarsInProgress == 0 are in sync
			      cumulativeDelta.Update(cumulativeDelta.BarsArray[1].Count - 1, 1);			
			}		
			
			else
			{
				return;
			}
		}
		
		#region Button Click Event
		
		private void OnButtonClick(object sender, RoutedEventArgs rea)
		{
			System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
			
			#region TradeSaber Socials
			
			if (showSocials)
			{
				if (button == youtubeButton && button.Name == "YoutubeButton" && button.Content == "Youtube")
				{
					System.Diagnostics.Process.Start(youtube);
					return;
				}
				
				if (button == discordButton && button.Name == "DiscordButton" && button.Content == "Discord")
				{	
					System.Diagnostics.Process.Start(discord);
					return;
				}
				
				if (button == tradeSaberButton && button.Name == "TradeSaberButton" && button.Content == "TradeSaber")
				{	
					System.Diagnostics.Process.Start(tradeSaber);
					return;
				}
			}
			
			#endregion
		}
	
	#endregion	
		
		#region Properties
		
		#region 01. Delta Calculation Method
		
		[Display(Name = "Delta Type", GroupName = "01. Delta Parameters", Description="Bid/Ask or Up Down Tick", Order = 1)]
		public DeltaType _DeltaType
		{
			get { return deltaType; }
			set { deltaType = value; }
		}	
		
		[Display(Name = "Period", GroupName = "01. Delta Parameters", Description="", Order = 2)]
		public PeriodType _PeriodType
		{
			get { return periodType; }
			set { periodType = value; }
		}	
		
		[Range(0, int.MaxValue)]
		[Display(Name = "Size Filter", GroupName = "01. Delta Parameters", Order = 3)]
		public int SizeFilter
		{
			get{return sizeFilter;}
			set{sizeFilter = (value);}
		}
		
		[Display(Name = "Delta Calculation Method", GroupName = "01. Delta Parameters", Description="User can select either Delta Close, or Delta High/Low", Order = 4)]
		public DeltaCalc _DeltaCalc
		{
			get { return deltaCalc; }
			set { deltaCalc = value; }
		}	
		
		
		#endregion
		
		#region 02. Color Region Above
		
		[Display(Name = "Above Period", GroupName = "02. Color Region Above", Order = 0)]
		public double IsAbove
		{
			get{return isAbove;}
			set{isAbove = (value);}
		}
		
		[XmlIgnore()]
		[Display(Name = "Color Region Above", GroupName = "02. Color Region Above", Order = 1)]
		public Brush IsAboveColor
		{ get; set; }
		
		// Serialize our Color object
		[Browsable(false)]
		public string IsAboveColorSerialize
		{
			get { return Serialize.BrushToString(IsAboveColor); }
   			set { IsAboveColor = Serialize.StringToBrush(value); }
		}
		
		
		[Range(0, 100)]
		[Display(Name = "Opacity %", GroupName = "02. Color Region Above", Order = 2)]
		public int IsAboveOpacity
		{
			get{return isAboveOpacity;}
			set{isAboveOpacity = (value);}
		}
		
		#endregion
			
		#region 03. Color Region Below
		
		[Display(Name = "Below Period", GroupName = "03. Color Region Below", Order = 0)]
		public double IsBelow
		{
			get{return isBelow;}
			set{isBelow = (value);}
		}
		
		[XmlIgnore()]
		[Display(Name = "Color Region Above", GroupName = "03. Color Region Below", Order = 1)]
		public Brush IsBelowColor
		{ get; set; }
		
		// Serialize our Color object
		[Browsable(false)]
		public string IsBelowColorSerialize
		{
			get { return Serialize.BrushToString(IsBelowColor); }
   			set { IsBelowColor = Serialize.StringToBrush(value); }
		}
		
		[Range(0, 100)]
		[Display(Name = "Opacity %", GroupName = "03. Color Region Below", Order = 2)]
		public int IsBelowOpacity
		{
			get{return isBelowOpacity;}
			set{isBelowOpacity = (value);}
		}
		
		#endregion

		#region 04. Highlight Options
		
		[RefreshProperties(RefreshProperties.All)]
		[NinjaScriptProperty]
		[Display(Name = "Chart and Indicator", Order = 1, GroupName = "04. Highlight Options")]
		public bool HighlightChart
		{
		    get
		   {
		      return highlightChart;
		   }
		   set
		   {
		      if (value == true)
		      {
		         HighlightIndicator = false;
		      }
		      highlightChart = value;
		    }
		}

		[RefreshProperties(RefreshProperties.All)]
		[NinjaScriptProperty]
		[Display(Name = "Indicator Only", Order = 2, GroupName = "04. Highlight Options")]
		public bool HighlightIndicator
		{
		   get
		   {
		      return highlightIndicator;
		   }
		   set
		   {
		      if (value == true)
		      {
		         HighlightChart = false;
		      }
		      highlightIndicator = value;
		   }
		}
		
		#endregion
		
		#region 29. TradeSaber Socials
		
		[NinjaScriptProperty]
		[Display(Name = "Show Social Media Buttons", Description = "", Order = 0, GroupName = "29. TradeSaber Socials")]
		public bool ShowSocials 
		{
		 	get{return showSocials;} 
			set{showSocials = (value);} 
		}
		
		[NinjaScriptProperty]
		[Display(Name="Explanation Video", Order=1, GroupName="29. TradeSaber Socials")]
		public  string Youtube
		{
		 	get{return youtube;} 
			set{youtube = (value);} 
		}
		
		[NinjaScriptProperty]
		[Display(Name="Discord Link", Order=2, GroupName="29. TradeSaber Socials")]
		public  string Discord
		{
		 	get{return discord;} 
			set{discord = (value);} 
		}
		
		[NinjaScriptProperty]
		[Display(Name="TradeSaber Link", Order=3, GroupName="29. TradeSaber Socials")]
		public  string TradeSaber
		{
		 	get{return tradeSaber;} 
			set{tradeSaber = (value);} 
		}
		
		[NinjaScriptProperty]
		[ReadOnly(true)]
		[Display(Name = "Author", GroupName = "29. TradeSaber Socials", Order = 4)]
		public string Author
		{
		 	get{return author;} 
			set{author = (value);} 
		}
		
		[NinjaScriptProperty]
		[ReadOnly(true)]
		[Display(Name = "Version", GroupName = "29. TradeSaber Socials", Order = 5)]
		public string Version
		{
		 	get{return version;} 
			set{version = (value);} 
		}
		
		#endregion 
		
		#endregion

	}

	
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TradeSaber.CumDeltaHighlight[] cacheCumDeltaHighlight;
		public TradeSaber.CumDeltaHighlight CumDeltaHighlight(bool highlightChart, bool highlightIndicator, bool showSocials, string youtube, string discord, string tradeSaber, string author, string version)
		{
			return CumDeltaHighlight(Input, highlightChart, highlightIndicator, showSocials, youtube, discord, tradeSaber, author, version);
		}

		public TradeSaber.CumDeltaHighlight CumDeltaHighlight(ISeries<double> input, bool highlightChart, bool highlightIndicator, bool showSocials, string youtube, string discord, string tradeSaber, string author, string version)
		{
			if (cacheCumDeltaHighlight != null)
				for (int idx = 0; idx < cacheCumDeltaHighlight.Length; idx++)
					if (cacheCumDeltaHighlight[idx] != null && cacheCumDeltaHighlight[idx].HighlightChart == highlightChart && cacheCumDeltaHighlight[idx].HighlightIndicator == highlightIndicator && cacheCumDeltaHighlight[idx].ShowSocials == showSocials && cacheCumDeltaHighlight[idx].Youtube == youtube && cacheCumDeltaHighlight[idx].Discord == discord && cacheCumDeltaHighlight[idx].TradeSaber == tradeSaber && cacheCumDeltaHighlight[idx].Author == author && cacheCumDeltaHighlight[idx].Version == version && cacheCumDeltaHighlight[idx].EqualsInput(input))
						return cacheCumDeltaHighlight[idx];
			return CacheIndicator<TradeSaber.CumDeltaHighlight>(new TradeSaber.CumDeltaHighlight(){ HighlightChart = highlightChart, HighlightIndicator = highlightIndicator, ShowSocials = showSocials, Youtube = youtube, Discord = discord, TradeSaber = tradeSaber, Author = author, Version = version }, input, ref cacheCumDeltaHighlight);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TradeSaber.CumDeltaHighlight CumDeltaHighlight(bool highlightChart, bool highlightIndicator, bool showSocials, string youtube, string discord, string tradeSaber, string author, string version)
		{
			return indicator.CumDeltaHighlight(Input, highlightChart, highlightIndicator, showSocials, youtube, discord, tradeSaber, author, version);
		}

		public Indicators.TradeSaber.CumDeltaHighlight CumDeltaHighlight(ISeries<double> input , bool highlightChart, bool highlightIndicator, bool showSocials, string youtube, string discord, string tradeSaber, string author, string version)
		{
			return indicator.CumDeltaHighlight(input, highlightChart, highlightIndicator, showSocials, youtube, discord, tradeSaber, author, version);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TradeSaber.CumDeltaHighlight CumDeltaHighlight(bool highlightChart, bool highlightIndicator, bool showSocials, string youtube, string discord, string tradeSaber, string author, string version)
		{
			return indicator.CumDeltaHighlight(Input, highlightChart, highlightIndicator, showSocials, youtube, discord, tradeSaber, author, version);
		}

		public Indicators.TradeSaber.CumDeltaHighlight CumDeltaHighlight(ISeries<double> input , bool highlightChart, bool highlightIndicator, bool showSocials, string youtube, string discord, string tradeSaber, string author, string version)
		{
			return indicator.CumDeltaHighlight(input, highlightChart, highlightIndicator, showSocials, youtube, discord, tradeSaber, author, version);
		}
	}
}

#endregion
