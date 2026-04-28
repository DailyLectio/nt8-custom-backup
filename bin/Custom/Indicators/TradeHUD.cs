#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class TradeHUD : Indicator
    {
        private System.Windows.Controls.Grid myGrid;
        private System.Windows.Controls.Button btnD, btnb, btnP, btnB;
        private System.Windows.Controls.TextBlock statusText;
        private string currentDayShape = "D"; 
        
        // NEW: Memory variable to prevent sound from spamming every tick
        private string lastAlertMessage = "";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Playbook Execution HUD - Reads Trinity HUD Levels";
                Name = "TradeHUD";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;

                ConfluenceTicks = 8;
                SignalValidMinutes = 5; 
            }
            else if (State == State.Historical)
            {
                if (UserControlCollection.Contains(myGrid)) return;
                Dispatcher.InvokeAsync((() => { CreateWPFControls(); }));
            }
            else if (State == State.Terminated)
            {
                Dispatcher.InvokeAsync((() => { if (myGrid != null) UserControlCollection.Remove(myGrid); }));
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 20) return;

            // 1. PULL LEVELS FROM TRINITY HUD (Via HUDMessenger)
            double liveVwap = HUDMessenger.SharedLevelMap.ContainsKey("Live_VWAP") ? HUDMessenger.SharedLevelMap["Live_VWAP"] : 0;
            double onVwap = HUDMessenger.SharedLevelMap.ContainsKey("ON_VWAP") ? HUDMessenger.SharedLevelMap["ON_VWAP"] : 0;
            double vah = HUDMessenger.SharedLevelMap.ContainsKey("VAH") ? HUDMessenger.SharedLevelMap["VAH"] : 0;
            double val = HUDMessenger.SharedLevelMap.ContainsKey("VAL") ? HUDMessenger.SharedLevelMap["VAL"] : 0;
            double ibh = HUDMessenger.SharedLevelMap.ContainsKey("IBH") ? HUDMessenger.SharedLevelMap["IBH"] : 0;
            double ibl = HUDMessenger.SharedLevelMap.ContainsKey("IBL") ? HUDMessenger.SharedLevelMap["IBL"] : 0;
            double onh = HUDMessenger.SharedLevelMap.ContainsKey("ONH") ? HUDMessenger.SharedLevelMap["ONH"] : 0;
            double onl = HUDMessenger.SharedLevelMap.ContainsKey("ONL") ? HUDMessenger.SharedLevelMap["ONL"] : 0;
            double pdh = HUDMessenger.SharedLevelMap.ContainsKey("PDH") ? HUDMessenger.SharedLevelMap["PDH"] : 0;
            double pdl = HUDMessenger.SharedLevelMap.ContainsKey("PDL") ? HUDMessenger.SharedLevelMap["PDL"] : 0;

            // 2. CHECK PROXIMITY
            double p = Close[0];
            bool IsNear(double level) => level > 0 && Math.Abs(p - level) <= ConfluenceTicks * TickSize;

            bool nearVwap = IsNear(liveVwap) || IsNear(onVwap);
            bool nearTopEdge = IsNear(ibh) || IsNear(onh) || IsNear(pdh);
            bool nearBotEdge = IsNear(ibl) || IsNear(onl) || IsNear(pdl);
            bool nearVah = IsNear(vah);
            bool nearVal = IsNear(val);

            // 3. CHECK FOOTPRINT SIGNALS
            DateTime timeABS = HUDMessenger.SharedSignalMap.ContainsKey("Scanner_ABS") ? HUDMessenger.SharedSignalMap["Scanner_ABS"] : DateTime.MinValue;
            DateTime timeSIB = HUDMessenger.SharedSignalMap.ContainsKey("Scanner_SIB") ? HUDMessenger.SharedSignalMap["Scanner_SIB"] : DateTime.MinValue;
            DateTime timeDD  = HUDMessenger.SharedSignalMap.ContainsKey("Scanner_DD") ? HUDMessenger.SharedSignalMap["Scanner_DD"] : DateTime.MinValue;

            bool isAbsActive = timeABS != DateTime.MinValue && (Time[0] - timeABS).TotalMinutes <= SignalValidMinutes && (Time[0] - timeABS).TotalMinutes >= 0;
            bool isSibActive = timeSIB != DateTime.MinValue && (Time[0] - timeSIB).TotalMinutes <= SignalValidMinutes && (Time[0] - timeSIB).TotalMinutes >= 0;
            bool isDdActive  = timeDD != DateTime.MinValue && (Time[0] - timeDD).TotalMinutes <= SignalValidMinutes && (Time[0] - timeDD).TotalMinutes >= 0;

            string hudMessage = "SCANNING PLAYBOOK STRUCTURE...";
            Brush hudColor = Brushes.White;

            // 4. CORE FOUR PLAYBOOK LOGIC
            if (currentDayShape == "P") // BULL TREND
            {
                if (nearVwap) {
                    hudMessage = "★ WAITING FOR REVERSAL AT VWAP ★"; hudColor = Brushes.Orange;
                    if (isAbsActive) { hudMessage = "🟢 BULL TRAP GO! TARGET: SWING HIGH 🟢"; hudColor = Brushes.LimeGreen; }
                }
                else if (nearTopEdge) {
                    hudMessage = "★ WAITING FOR BREAKOUT ABOVE IB/ONH ★"; hudColor = Brushes.Yellow;
                    if (isSibActive) { hudMessage = "🟢 CONTINUATION BREAKOUT GO! 🟢"; hudColor = Brushes.LimeGreen; }
                }
            }
            else if (currentDayShape == "b") // BEAR TREND
            {
                if (nearVwap) {
                    hudMessage = "★ WAITING FOR REVERSAL AT VWAP ★"; hudColor = Brushes.Orange;
                    if (isAbsActive || isDdActive) { hudMessage = "🔴 BEAR TRAP GO! TARGET: SWING LOW 🔴"; hudColor = Brushes.Red; }
                }
                else if (nearBotEdge) {
                    hudMessage = "★ WAITING FOR BREAKOUT BELOW IB/ONL ★"; hudColor = Brushes.Yellow;
                    if (isSibActive) { hudMessage = "🔴 CONTINUATION BREAKOUT GO! 🔴"; hudColor = Brushes.Red; }
                }
            }
            else if (currentDayShape == "D") // ROTATION
            {
                if (nearVah || nearVal) {
                    hudMessage = "★ EDGE FADE ZONE: WAITING FOR WICK/DD ★"; hudColor = Brushes.Orange;
                    if (isDdActive || isAbsActive) { hudMessage = "🟡 REVERSION GO! TARGET: VWAP 🟡"; hudColor = Brushes.Gold; }
                }
            }
            else if (currentDayShape == "B") // BREAKOUT EXPANSION
            {
                if (nearVwap || nearVah || nearTopEdge || nearBotEdge) {
                    hudMessage = "★ MOMENTUM ZONE: WAITING FOR SIB ★"; hudColor = Brushes.Yellow;
                    if (isSibActive) { hudMessage = "🔵 MOMENTUM EXPANSION GO! 🔵"; hudColor = Brushes.DodgerBlue; }
                }
            }

            // 5. CUSTOM ALERT SOUND LOGIC
            if (hudMessage.Contains("GO!") && hudMessage != lastAlertMessage)
            {
                string soundPath = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "sounds", "004.wav");
                PlaySound(soundPath);
                lastAlertMessage = hudMessage;
            }
            else if (!hudMessage.Contains("GO!"))
            {
                // Reset the memory when price moves out of the zone or signal expires
                lastAlertMessage = ""; 
            }

            Dispatcher.InvokeAsync((() =>
            {
                if (statusText != null)
                {
                    statusText.Text = hudMessage;
                    statusText.Foreground = hudColor;
                }
            }));
        }

        #region WPF UI Creation
        private void CreateWPFControls()
        {
            myGrid = new System.Windows.Controls.Grid
            {
                Background = Brushes.DimGray,
                HorizontalAlignment = HorizontalAlignment.Left, 
                VerticalAlignment = VerticalAlignment.Bottom, 
                Margin = new Thickness(260, 0, 0, 20) 
            };

            myGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) }); 
            myGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) }); 
            myGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) }); 

            myGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            myGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            myGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            myGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            myGrid.Children.Add(CreateLabelTextBlock("D", 0));
            myGrid.Children.Add(CreateLabelTextBlock("b", 1));
            myGrid.Children.Add(CreateLabelTextBlock("P", 2));
            myGrid.Children.Add(CreateLabelTextBlock("B", 3));

            statusText = new System.Windows.Controls.TextBlock
            {
                Text = "HUD INITIALIZING...",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13, 
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetRow(statusText, 1); 
            System.Windows.Controls.Grid.SetColumnSpan(statusText, 4);
            myGrid.Children.Add(statusText);

            btnD = CreateButton("D", 0, Brushes.LightSlateGray);
            btnb = CreateButton("b", 1, Brushes.LightCoral);
            btnP = CreateButton("P", 2, Brushes.LightGreen);
            btnB = CreateButton("B", 3, Brushes.LightSkyBlue);

            myGrid.Children.Add(btnD);
            myGrid.Children.Add(btnb);
            myGrid.Children.Add(btnP);
            myGrid.Children.Add(btnB);

            UserControlCollection.Add(myGrid);
            UpdateButtonStyles();
        }

        private System.Windows.Controls.TextBlock CreateLabelTextBlock(string text, int col)
        {
            System.Windows.Controls.TextBlock tb = new System.Windows.Controls.TextBlock
            {
                Text = text,
                Foreground = Brushes.White, 
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetRow(tb, 0); 
            System.Windows.Controls.Grid.SetColumn(tb, col);
            return tb;
        }

        private System.Windows.Controls.Button CreateButton(string content, int col, Brush color)
        {
            System.Windows.Controls.Button btn = new System.Windows.Controls.Button
            {
                Background = color,
                Padding = new Thickness(0),
                Margin = new Thickness(2)
            };
            btn.Click += ButtonClick;
            System.Windows.Controls.Grid.SetRow(btn, 2); 
            System.Windows.Controls.Grid.SetColumn(btn, col);
            return btn;
        }

        private void ButtonClick(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button clickedButton = sender as System.Windows.Controls.Button;
            if (clickedButton != null)
            {
                int col = System.Windows.Controls.Grid.GetColumn(clickedButton);
                if (col == 0) currentDayShape = "D";
                else if (col == 1) currentDayShape = "b";
                else if (col == 2) currentDayShape = "P";
                else if (col == 3) currentDayShape = "B";

                HUDMessenger.CurrentDailyBias = currentDayShape;

                UpdateButtonStyles();
                ForceRefresh(); 
            }
        }

        private void UpdateButtonStyles()
        {
            int targetCol = 0;
            if (currentDayShape == "D") targetCol = 0;
            else if (currentDayShape == "b") targetCol = 1;
            else if (currentDayShape == "P") targetCol = 2;
            else if (currentDayShape == "B") targetCol = 3;

            btnD.BorderThickness = targetCol == 0 ? new Thickness(3) : new Thickness(1);
            btnb.BorderThickness = targetCol == 1 ? new Thickness(3) : new Thickness(1);
            btnP.BorderThickness = targetCol == 2 ? new Thickness(3) : new Thickness(1);
            btnB.BorderThickness = targetCol == 3 ? new Thickness(3) : new Thickness(1);
        }
        #endregion

        #region Properties
        [NinjaScriptProperty]
        [Display(Name="Confluence Distance (Ticks)", Order=1, GroupName="HUD Settings")]
        public int ConfluenceTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Signal Valid (Minutes)", Order=2, GroupName="HUD Settings")]
        public int SignalValidMinutes { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TradeHUD[] cacheTradeHUD;
		public TradeHUD TradeHUD(int confluenceTicks, int signalValidMinutes)
		{
			return TradeHUD(Input, confluenceTicks, signalValidMinutes);
		}

		public TradeHUD TradeHUD(ISeries<double> input, int confluenceTicks, int signalValidMinutes)
		{
			if (cacheTradeHUD != null)
				for (int idx = 0; idx < cacheTradeHUD.Length; idx++)
					if (cacheTradeHUD[idx] != null && cacheTradeHUD[idx].ConfluenceTicks == confluenceTicks && cacheTradeHUD[idx].SignalValidMinutes == signalValidMinutes && cacheTradeHUD[idx].EqualsInput(input))
						return cacheTradeHUD[idx];
			return CacheIndicator<TradeHUD>(new TradeHUD(){ ConfluenceTicks = confluenceTicks, SignalValidMinutes = signalValidMinutes }, input, ref cacheTradeHUD);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TradeHUD TradeHUD(int confluenceTicks, int signalValidMinutes)
		{
			return indicator.TradeHUD(Input, confluenceTicks, signalValidMinutes);
		}

		public Indicators.TradeHUD TradeHUD(ISeries<double> input , int confluenceTicks, int signalValidMinutes)
		{
			return indicator.TradeHUD(input, confluenceTicks, signalValidMinutes);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TradeHUD TradeHUD(int confluenceTicks, int signalValidMinutes)
		{
			return indicator.TradeHUD(Input, confluenceTicks, signalValidMinutes);
		}

		public Indicators.TradeHUD TradeHUD(ISeries<double> input , int confluenceTicks, int signalValidMinutes)
		{
			return indicator.TradeHUD(input, confluenceTicks, signalValidMinutes);
		}
	}
}

#endregion
