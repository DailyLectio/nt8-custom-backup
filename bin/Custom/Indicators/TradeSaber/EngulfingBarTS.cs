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
	public class EngulfingBarTS : Indicator
	{
		private double percentageCalc;
		private double priceCalc;
		private double tickCalc;
		
		//Picks Highest Offset
		private double candleBarOffset;							
		
		private bool setChartOB;

		
		private int CalcInt;
		
		
		#region TradeSaber Social
		
		private string author 								= "TradeSaber(Dre)";
		private string version 								= "Version 2.0 // June 2023";
		
		private string youtube								= "https://youtu.be/IqMJCp8N0-4"; 
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
				Description									= @"Enter the description for your new custom Indicator here.";
				Name										= "EngulfingBarTS";
				Calculate									= Calculate.OnPriceChange;
				IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
			
				///Default colors for Outside Bars
				ColorOutsideBar								= true;
				GreenOutsideBar 							= Brushes.DarkCyan;
				RedOutsideBar								= Brushes.Indigo;
				
				///Default colors for Conti Bars
				ColorContiBar								= true;
				GreenContiBar 								= Brushes.LightBlue;
				RedContiBar									= Brushes.Plum;
				
				priceOffset 								= 0.00;
				percentageOffset 							= 0.00;
				tickOffset									= 0;
				
				engulfBody									= false;
				
				///Outside Bar Alerts
				CustomSoundFile								= @"C:\Program Files\NinjaTrader 8\sounds\Alert2.wav";
				
				SetVolumeAlert								= false;
				SetOBFlash									= false;
				FlashBrush 									= Brushes.Green;
				
				
				///Continuation Bar Alerts
				CustomSoundFileCont							= @"C:\Program Files\NinjaTrader 8\sounds\Alert2.wav";
				
				SetVolumeAlertCont							= false;
				SetOBFlashCont								= false;
				FlashBrushCont								= Brushes.Green;
				
				
				
				setChartOB									= true;
			
				
				///Plot
				AddPlot(new Stroke(Brushes.Transparent, 2), PlotStyle.Dot, "CurrentOutsideBar");
				
				AddPlot(new Stroke(Brushes.Transparent, 2), PlotStyle.Dot, "ContiOutsideBar");
				
				///Will show buttons with links to Youtube/Discord.
				showSocials									= true;

				
			}
			else if (State == State.Configure)
			{
			}
			else if (State == State.DataLoaded)
			{
				if (Calculate == Calculate.OnBarClose)
				{
					CalcInt = 0;
				}
				
				if (Calculate == Calculate.OnPriceChange || Calculate == Calculate.OnEachTick)
				{
					CalcInt = 1;
				}
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
		
		public override string DisplayName
		{
        	get 
			{ 
				if  (State == State.SetDefaults) 
				return Name; 
		
				else  return "TradeSaber Engulfing Bar"; 
			}

		}
		
		
		protected override void OnBarUpdate()
		{
			
			if(CurrentBars[0] < 2) return;
			
			BarBrush	= null;
			
			#region Offset Calculations
			
			//Percent for the current OB forming
			percentageCalc = (High[1] - Low[1]) * percentageOffset;
			
			//These are the same for both because they do not rely on another candle
			priceCalc = 	priceOffset;
			tickCalc = TickSize * tickOffset;
			
			//Picks Highest Offset for the Current OB
			candleBarOffset = Math.Max(percentageCalc, Math.Max(priceCalc, tickCalc));

		#endregion		
			
			
			#region Current Outside Bar Logic
		
		///Current Outside Bars 'Without' Engulf Body Selected	
			if (engulfBody == false)
			{
				//Currently a Green Outside Bar	
				if (
					((High[0]-(candleBarOffset)) >= High[1] 
						&& (Low[0]+(candleBarOffset)) <= Low[1])
							&& (Open[0] < Close[0])
								&& (Open[1] > Close[1])
					)
				
				{
					CurrentOutsideBar[0] = 1;
				}
				
				//Currently a Green Outside Bar	
				else if (
						((High[0]-(candleBarOffset)) >= High[1] 
						&& (Low[0]+(candleBarOffset)) <= Low[1])
							&& (Open[0] > Close[0])
								&& (Open[1] < Close[1])
					)
				
				{
					CurrentOutsideBar[0] = -1;
				}
				
				else
				{
					CurrentOutsideBar[0] = 0;	
				}
			}
			
			
		///Current Outside Bars with Engulf Body Selected	
			
		if (engulfBody)
		{
			//Green Outside Bar w/ Engulf Body
			if (
				((High[0]-(candleBarOffset)) >= High[1]) 
					&& ((Low[0]+(candleBarOffset)) <= Low[1])
						&& (Open[0] < Close[0])
							&& (Open[1] > Close[1])
								&& ((Open[0] <= Close[1]) && Close[0] >= Open[1])
				)
			
			{
				CurrentOutsideBar[0] = 1;
			}
			
			//Red Outside Bar w/ Engulf Body
			else if (
					((High[0]-(candleBarOffset)) >= High[1]) 
					&& ((Low[0]+(candleBarOffset)) <= Low[1])
						&& (Open[0] > Close[0])
							&& (Open[1] < Close[1])
								&& ((Open[0] >= Close[1]) && Close[0] <= Open[1])
				)
			
			{
				CurrentOutsideBar[0] = -1;
			}
				else
			{
				CurrentOutsideBar[0] = 0;
			}
		}
			
			
			
		#endregion
		
			#region Conti Outside Bar Logic
		
		///Conti Outside Bars 'Without' Engulf Body Selected	
			if (engulfBody == false)
			{
				//Contily a Green Outside Bar	
				if (
					((High[0]-(candleBarOffset)) >= High[1] 
						&& (Low[0]+(candleBarOffset)) <= Low[1])
							&& (Open[0] < Close[0])
								&& (Open[1] < Close[1])
					)
				
				{
					ContiOutsideBar[0] = 1;
				}
				
				//Contily a Green Outside Bar	
				else if (
						((High[0]-(candleBarOffset)) >= High[1] 
						&& (Low[0]+(candleBarOffset)) <= Low[1])
							&& (Open[0] > Close[0])
								&& (Open[1] > Close[1])
					)
				
				{
					ContiOutsideBar[0] = -1;
				}
				
				else
				{
					ContiOutsideBar[0] = 0;	
				}
			}
			
			
		///Conti Outside Bars with Engulf Body Selected	
			
		if (engulfBody)
		{
			//Green Outside Bar w/ Engulf Body
			if (
				((High[0]-(candleBarOffset)) >= High[1]) 
					&& ((Low[0]+(candleBarOffset)) <= Low[1])
						&& (Open[0] < Close[0])
							&& (Open[1] < Close[1])
								&& ((Open[0] <= Close[1]) && Close[0] >= Open[1])
				)
			
			{
				ContiOutsideBar[0] = 1;
			}
			
			//Red Outside Bar w/ Engulf Body
			else if (
					((High[0]-(candleBarOffset)) >= High[1]) 
					&& ((Low[0]+(candleBarOffset)) <= Low[1])
						&& (Open[0] > Close[0])
							&& (Open[1] > Close[1])
								&& ((Open[0] >= Close[1]) && Close[0] <= Open[1])
				)
			
			{
				ContiOutsideBar[0] = -1;
			}
				else
			{
				ContiOutsideBar[0] = 0;
			}
		}
			
			
			
		#endregion
			
	
			if (setChartOB)
			{
				#region Color Outside Bars
			
	///Outside Bar Color
		if (ColorOutsideBar)
		{
			//Green Outside Bar Logic - Current Bar is Green, engulfing a previous Red candle
			if (CurrentOutsideBar[0] == 1) 
				
				{
					BarBrush = GreenOutsideBar;
				}
			
			//Red Outside Bar Logic - Current Bar is Red, engulfing a previous Green candle
			else if (CurrentOutsideBar[0] == -1) 
					
				{
					BarBrush = RedOutsideBar;
				}
			
		///Needs body of current candle to be greater than body of previous candle		
		
				//Green Outside Bar Logic - Current Bar is Green, engulfing a previous Red candle
			if (CurrentOutsideBar[0] == 1)
					
				{
					BarBrush = GreenOutsideBar;
				}	
			
				//Red Outside Bar Logic - Current Bar is Red, engulfing a previous Green candle 
			else if (CurrentOutsideBar[0] == -1)
				
				{
					BarBrush = RedOutsideBar;
				}
				
			if (CurrentOutsideBar[0] == 0 && ContiOutsideBar[0] == 0)
				
				{
					BarBrush = null;
				}
	
		}
	
	#endregion
		
				#region Color Cont Conti Bars
			
	///Conti Bar Color
		if (ColorContiBar)
		{
			//Green Conti Bar Logic - Conti Bar is Green, engulfing a previous Red candle
			if (ContiOutsideBar[0] == 1) 
				
				{
					BarBrush = GreenContiBar;
				}
			
			//Red Conti Bar Logic - Conti Bar is Red, engulfing a previous Green candle
			else if (ContiOutsideBar[0] == -1) 
					
				{
					BarBrush = RedContiBar;
				}
			
		///Needs body of Conti candle to be greater than body of previous candle		
		
				//Green Conti Bar Logic - Conti Bar is Green, engulfing a previous Red candle
			if (ContiOutsideBar[0] == 1)
					
				{
					BarBrush = GreenContiBar;
				}	
			
				//Red Conti Bar Logic - Conti Bar is Red, engulfing a previous Green candle 
			else if (ContiOutsideBar[0] == -1)
				
				{
					BarBrush = RedContiBar;
				}
				
			if (CurrentOutsideBar[0] == 0 && ContiOutsideBar[0] == 0)
				
				{
					BarBrush = null;
				}
	
		}
	
	#endregion
			}
	
				#region Flash Alert
		
		if (SetOBFlash)
		{
			///Flash Alert (Once Bar Closes)
			if ( (CurrentOutsideBar[CalcInt] == 1 || CurrentOutsideBar[CalcInt] == -1) && IsFirstTickOfBar)
			{
				BackBrushes[CalcInt] = FlashBrush;
			}
			
			else if (CurrentOutsideBar[CalcInt] == 0 && ContiOutsideBar[CalcInt] == 0 && IsFirstTickOfBar)
			{
				BackBrushes = null;
			}
		}
			
		
		if (SetOBFlashCont)
		{
			///Flash Alert (Once Bar Closes)
			if ( (ContiOutsideBar[CalcInt] == 1 || ContiOutsideBar[CalcInt] == -1) && IsFirstTickOfBar)
			{
				BackBrushes[CalcInt] = FlashBrushCont;
			}
			
			else if (CurrentOutsideBar[CalcInt] == 0 && ContiOutsideBar[CalcInt] == 0 && IsFirstTickOfBar)
			{
				BackBrushes = null;
			}
		}
		
			
	#endregion 
			
				#region Sound Alert
			
		///Sound Alert (Once Bar Closes)	
		if ( (CurrentOutsideBar[CalcInt] == 1 || CurrentOutsideBar[CalcInt] == -1) && IsFirstTickOfBar && SetVolumeAlert)
		{
			PlaySound(CustomSoundFile);	
		}
		
		
		if ( (ContiOutsideBar[CalcInt] == 1 || ContiOutsideBar[CalcInt] == -1) && IsFirstTickOfBar && SetVolumeAlertCont)
		{
			PlaySound(CustomSoundFileCont);	
		}
	
			#endregion
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
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> CurrentOutsideBar
		{ get { return Values[0]; } }
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ContiOutsideBar
		{ get { return Values[1]; } }
	
		#region 01. Offset Properties
	///Offset for Outside Bars. Allows Price to come outside of the High/Low of the previous candle if the user chooses.
		
		[NinjaScriptProperty]
		[Display(Name = "Price Offset", GroupName = "01. Outside Bar Offset", Order = 0)]
		public double priceOffset
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Percentage Offset", GroupName = "01. Outside Bar Offset", Order = 1)]
		public double percentageOffset
		{ get; set; }
		
		[NinjaScriptProperty]
        [Display(Name = "Tick Offset", Description = "Sensetive Value", Order = 2, GroupName = "01. Outside Bar Offset")]
        public int tickOffset 
		{ get; set; }
		
		#endregion
		
		#region 02. Color Outside Bars
	///Change the color of the Green and Red Outside Bars.
		
		[NinjaScriptProperty]
		[Display(Name="Engulf Body of previous candle", Order=0, GroupName="02. Outside Bar Custom Color")]
		public bool engulfBody
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Color Outside Bars", Description = "", Order = 1, GroupName = "02. Outside Bar Custom Color")]
		public bool ColorOutsideBar 
		{ get; set; }
		
		[NinjaScriptProperty]
		[XmlIgnore()]
		[Display(Name = "Green Outside Bar", GroupName = "02. Outside Bar Custom Color", Order = 2)]
		public Brush GreenOutsideBar
		{ get; set; }
		
		// Serialize our Color object
		[Browsable(false)]
		public string GreenOutsideBarSerialize
		{
			get { return Serialize.BrushToString(GreenOutsideBar); }
   			set { GreenOutsideBar = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[XmlIgnore()]
		[Display(Name = "Red Outside Bar", GroupName = "02. Outside Bar Custom Color", Order = 3)]
		public Brush RedOutsideBar
		{ get; set; }
		
		// Serialize our Color object
		[Browsable(false)]
		public string RedOutsideBarSerialize
		{
			get { return Serialize.BrushToString(RedOutsideBar); }
   			set { RedOutsideBar = Serialize.StringToBrush(value); }
		}
		
		
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Color Conti Bars", Description = "", Order = 4, GroupName = "02. Outside Bar Custom Color")]
		public bool ColorContiBar 
		{ get; set; }
		
		[NinjaScriptProperty]
		[XmlIgnore()]
		[Display(Name = "Green Conti Bar", GroupName = "02. Outside Bar Custom Color", Order = 5)]
		public Brush GreenContiBar
		{ get; set; }
		
		// Serialize our Color object
		[Browsable(false)]
		public string GreenContiBarSerialize
		{
			get { return Serialize.BrushToString(GreenContiBar); }
   			set { GreenContiBar = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[XmlIgnore()]
		[Display(Name = "Red Conti Bar", GroupName = "02. Outside Bar Custom Color", Order = 6)]
		public Brush RedContiBar
		{ get; set; }
		
		// Serialize our Color object
		[Browsable(false)]
		public string RedContiBarSerialize
		{
			get { return Serialize.BrushToString(RedContiBar); }
   			set { RedContiBar = Serialize.StringToBrush(value); }
		}
		
		
		
		
		#endregion
		
		#region 03. Flash Alert
	///Enable Flash Alert and Change color 	
		
		[NinjaScriptProperty]
		[Display(ResourceType = typeof (Custom.Resource), Name = "Enable Outside Bar Flash", Order = 0, GroupName = "03. Flash Alert")]
		public bool SetOBFlash
		{ get; set; }	
		
		[NinjaScriptProperty]
		[XmlIgnore()]
		[Display(Name = "Flash Brush Outside Bar", GroupName = "03. Flash Alert", Order = 1)]
		public Brush FlashBrush
		{ get; set; }
		
		// Serialize our Color object
		[Browsable(false)]
		public string FlashBrushSerialize
		{
			get { return Serialize.BrushToString(FlashBrush); }
   			set { FlashBrush = Serialize.StringToBrush(value); }
		}
		
		
		[NinjaScriptProperty]
		[Display(ResourceType = typeof (Custom.Resource), Name = "Enable Continuation Bar Flash", Order = 2, GroupName = "03. Flash Alert")]
		public bool SetOBFlashCont
		{ get; set; }	
		
		[NinjaScriptProperty]
		[XmlIgnore()]
		[Display(Name = "Flash Brush Continuation", GroupName = "03. Flash Alert", Order = 3)]
		public Brush FlashBrushCont
		{ get; set; }
		
		// Serialize our Color object
		[Browsable(false)]
		public string FlashBrushContSerialize
		{
			get { return Serialize.BrushToString(FlashBrushCont); }
   			set { FlashBrushCont = Serialize.StringToBrush(value); }
		}
		#endregion
		
		#region 04. Sound Alerts
	///Enable Sound Alert	
		
		[Display(ResourceType = typeof (Custom.Resource), Name = "Enable Volume Alert", Order = 0, GroupName = "04. Audio Alert")]
		public bool SetVolumeAlert
		{ get; set; }
		
		[Display(Name="Alert sound file", Description="Enter sound file path/name", Order=1, GroupName="04. Audio Alert")]
		[PropertyEditor("NinjaTrader.Gui.Tools.FilePathPicker", Filter="Wav Files (*.wav)|*.wav")]
		public string CustomSoundFile
		{ get; set; }
		
		
		[Display(ResourceType = typeof (Custom.Resource), Name = "Enable Volume Alert Continuation", Order = 2, GroupName = "04. Audio Alert")]
		public bool SetVolumeAlertCont
		{ get; set; }
		
		[Display(Name="Alert sound file Continuation", Description="Enter sound file path/name", Order=4, GroupName="04. Audio Alert")]
		[PropertyEditor("NinjaTrader.Gui.Tools.FilePathPicker", Filter="Wav Files (*.wav)|*.wav")]
		public string CustomSoundFileCont
		{ get; set; }
		
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
		private TradeSaber.EngulfingBarTS[] cacheEngulfingBarTS;
		public TradeSaber.EngulfingBarTS EngulfingBarTS(double priceOffset, double percentageOffset, int tickOffset, bool engulfBody, bool colorOutsideBar, Brush greenOutsideBar, Brush redOutsideBar, bool colorContiBar, Brush greenContiBar, Brush redContiBar, bool setOBFlash, Brush flashBrush, bool setOBFlashCont, Brush flashBrushCont, bool showSocials, string youtube, string discord, string tradeSaber, string author, string version)
		{
			return EngulfingBarTS(Input, priceOffset, percentageOffset, tickOffset, engulfBody, colorOutsideBar, greenOutsideBar, redOutsideBar, colorContiBar, greenContiBar, redContiBar, setOBFlash, flashBrush, setOBFlashCont, flashBrushCont, showSocials, youtube, discord, tradeSaber, author, version);
		}

		public TradeSaber.EngulfingBarTS EngulfingBarTS(ISeries<double> input, double priceOffset, double percentageOffset, int tickOffset, bool engulfBody, bool colorOutsideBar, Brush greenOutsideBar, Brush redOutsideBar, bool colorContiBar, Brush greenContiBar, Brush redContiBar, bool setOBFlash, Brush flashBrush, bool setOBFlashCont, Brush flashBrushCont, bool showSocials, string youtube, string discord, string tradeSaber, string author, string version)
		{
			if (cacheEngulfingBarTS != null)
				for (int idx = 0; idx < cacheEngulfingBarTS.Length; idx++)
					if (cacheEngulfingBarTS[idx] != null && cacheEngulfingBarTS[idx].priceOffset == priceOffset && cacheEngulfingBarTS[idx].percentageOffset == percentageOffset && cacheEngulfingBarTS[idx].tickOffset == tickOffset && cacheEngulfingBarTS[idx].engulfBody == engulfBody && cacheEngulfingBarTS[idx].ColorOutsideBar == colorOutsideBar && cacheEngulfingBarTS[idx].GreenOutsideBar == greenOutsideBar && cacheEngulfingBarTS[idx].RedOutsideBar == redOutsideBar && cacheEngulfingBarTS[idx].ColorContiBar == colorContiBar && cacheEngulfingBarTS[idx].GreenContiBar == greenContiBar && cacheEngulfingBarTS[idx].RedContiBar == redContiBar && cacheEngulfingBarTS[idx].SetOBFlash == setOBFlash && cacheEngulfingBarTS[idx].FlashBrush == flashBrush && cacheEngulfingBarTS[idx].SetOBFlashCont == setOBFlashCont && cacheEngulfingBarTS[idx].FlashBrushCont == flashBrushCont && cacheEngulfingBarTS[idx].ShowSocials == showSocials && cacheEngulfingBarTS[idx].Youtube == youtube && cacheEngulfingBarTS[idx].Discord == discord && cacheEngulfingBarTS[idx].TradeSaber == tradeSaber && cacheEngulfingBarTS[idx].Author == author && cacheEngulfingBarTS[idx].Version == version && cacheEngulfingBarTS[idx].EqualsInput(input))
						return cacheEngulfingBarTS[idx];
			return CacheIndicator<TradeSaber.EngulfingBarTS>(new TradeSaber.EngulfingBarTS(){ priceOffset = priceOffset, percentageOffset = percentageOffset, tickOffset = tickOffset, engulfBody = engulfBody, ColorOutsideBar = colorOutsideBar, GreenOutsideBar = greenOutsideBar, RedOutsideBar = redOutsideBar, ColorContiBar = colorContiBar, GreenContiBar = greenContiBar, RedContiBar = redContiBar, SetOBFlash = setOBFlash, FlashBrush = flashBrush, SetOBFlashCont = setOBFlashCont, FlashBrushCont = flashBrushCont, ShowSocials = showSocials, Youtube = youtube, Discord = discord, TradeSaber = tradeSaber, Author = author, Version = version }, input, ref cacheEngulfingBarTS);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TradeSaber.EngulfingBarTS EngulfingBarTS(double priceOffset, double percentageOffset, int tickOffset, bool engulfBody, bool colorOutsideBar, Brush greenOutsideBar, Brush redOutsideBar, bool colorContiBar, Brush greenContiBar, Brush redContiBar, bool setOBFlash, Brush flashBrush, bool setOBFlashCont, Brush flashBrushCont, bool showSocials, string youtube, string discord, string tradeSaber, string author, string version)
		{
			return indicator.EngulfingBarTS(Input, priceOffset, percentageOffset, tickOffset, engulfBody, colorOutsideBar, greenOutsideBar, redOutsideBar, colorContiBar, greenContiBar, redContiBar, setOBFlash, flashBrush, setOBFlashCont, flashBrushCont, showSocials, youtube, discord, tradeSaber, author, version);
		}

		public Indicators.TradeSaber.EngulfingBarTS EngulfingBarTS(ISeries<double> input , double priceOffset, double percentageOffset, int tickOffset, bool engulfBody, bool colorOutsideBar, Brush greenOutsideBar, Brush redOutsideBar, bool colorContiBar, Brush greenContiBar, Brush redContiBar, bool setOBFlash, Brush flashBrush, bool setOBFlashCont, Brush flashBrushCont, bool showSocials, string youtube, string discord, string tradeSaber, string author, string version)
		{
			return indicator.EngulfingBarTS(input, priceOffset, percentageOffset, tickOffset, engulfBody, colorOutsideBar, greenOutsideBar, redOutsideBar, colorContiBar, greenContiBar, redContiBar, setOBFlash, flashBrush, setOBFlashCont, flashBrushCont, showSocials, youtube, discord, tradeSaber, author, version);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TradeSaber.EngulfingBarTS EngulfingBarTS(double priceOffset, double percentageOffset, int tickOffset, bool engulfBody, bool colorOutsideBar, Brush greenOutsideBar, Brush redOutsideBar, bool colorContiBar, Brush greenContiBar, Brush redContiBar, bool setOBFlash, Brush flashBrush, bool setOBFlashCont, Brush flashBrushCont, bool showSocials, string youtube, string discord, string tradeSaber, string author, string version)
		{
			return indicator.EngulfingBarTS(Input, priceOffset, percentageOffset, tickOffset, engulfBody, colorOutsideBar, greenOutsideBar, redOutsideBar, colorContiBar, greenContiBar, redContiBar, setOBFlash, flashBrush, setOBFlashCont, flashBrushCont, showSocials, youtube, discord, tradeSaber, author, version);
		}

		public Indicators.TradeSaber.EngulfingBarTS EngulfingBarTS(ISeries<double> input , double priceOffset, double percentageOffset, int tickOffset, bool engulfBody, bool colorOutsideBar, Brush greenOutsideBar, Brush redOutsideBar, bool colorContiBar, Brush greenContiBar, Brush redContiBar, bool setOBFlash, Brush flashBrush, bool setOBFlashCont, Brush flashBrushCont, bool showSocials, string youtube, string discord, string tradeSaber, string author, string version)
		{
			return indicator.EngulfingBarTS(input, priceOffset, percentageOffset, tickOffset, engulfBody, colorOutsideBar, greenOutsideBar, redOutsideBar, colorContiBar, greenContiBar, redContiBar, setOBFlash, flashBrush, setOBFlashCont, flashBrushCont, showSocials, youtube, discord, tradeSaber, author, version);
		}
	}
}

#endregion
