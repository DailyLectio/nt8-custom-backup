// CC BY-NC 4.0
#region Using declarations
using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;                  
using NinjaTrader.Core.FloatingPoint;   
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class RegimeMatrixHUD : Indicator
    {
        // --- THE PARKING GARAGE (Concurrent ES & NQ Support) ---
        public static readonly Dictionary<string, RegimeMatrixHUD> Instances = new Dictionary<string, RegimeMatrixHUD>();
        private static readonly object instanceLock = new object();

        // --- INTERNAL STATE ---
        private bool isAutoMode = true;
        public bool IsMomoAllowed { get; private set; } = false;
        public bool IsAdxAllowed { get; private set; } = false;
        public bool IsPineAllowed { get; private set; } = false;
        public bool IsEsScalperAllowed { get; private set; } = false;     // NEW V3 LANE
        public bool IsBracketSniperAllowed { get; private set; } = false; // NEW V3 LANE

        // --- DATA MEMORY ---
        private SortedDictionary<DateTime, string> macroData = new SortedDictionary<DateTime, string>();
        private SortedDictionary<DateTime, string> hmmData = new SortedDictionary<DateTime, string>();
        private SortedDictionary<DateTime, string> playbookData = new SortedDictionary<DateTime, string>(); // NEW V3
        
        [NinjaScriptProperty]
        [Display(Name="Data Folder Path", Description="Path to Python CSVs.", GroupName="Data Settings", Order=0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\Active";

        // --- UI ELEMENTS ---
        private System.Windows.Controls.Grid myGrid;
        private TextBlock TrafficLightText;
        private TextBlock playbookText; // NEW V3
        private TextBlock macroText;
        private TextBlock hmmText;
        private System.Windows.Controls.Button modeButton;
        private System.Windows.Controls.Button momoButton;
        private System.Windows.Controls.Button adxButton;
        private System.Windows.Controls.Button pineButton;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Master Regime Supervisor for ES and NQ.";
                Name = "Regime Matrix HUD";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.DataLoaded)
            {
                // Register this specific chart's HUD into the Parking Garage
                string sym = Instrument.MasterInstrument.Name;
                lock (instanceLock)
                {
                    if (!Instances.ContainsKey(sym)) Instances.Add(sym, this);
                    else Instances[sym] = this;
                }

                if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() => { CreateWPFControls(); });
                }
            }
            else if (State == State.Terminated)
            {
                // Remove this HUD from the Parking Garage on close
                string sym = Instrument.MasterInstrument.Name;
                lock (instanceLock)
                {
                    if (Instances.ContainsKey(sym) && Instances[sym] == this) 
                        Instances.Remove(sym);
                }

                if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() => { DisposeWPFControls(); });
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;
            
            // 1. Parse the CSV files into dictionaries
            LoadRegimeData();

            // 2. Perform the "ASOF Backward Merge" to find the active state for the current candle
            // This grabs the most recent state relative to the chart's current time
            string currentMacro = GetLatestStateAtTime(macroData, Time[0]);
            string currentPlaybook = GetLatestStateAtTime(playbookData, Time[0]);
            string currentHMM = GetLatestStateAtTime(hmmData, Time[0]);

            // 3. Broadcast to the Master Gatekeeper (HUDMessenger) so the bots can read it
            HUDMessenger.CurrentMacroRegime = currentMacro;
            HUDMessenger.CurrentPlaybook = currentPlaybook;
            HUDMessenger.CurrentHMMRegime = currentHMM;

            // 4. Update the visual display on the chart
            UpdateHUD();
        }

        // ===========================================================================
        // ASOF LOOKUP HELPER METHOD (Finds the most recent data point)
        // ===========================================================================
        private string GetLatestStateAtTime(SortedDictionary<DateTime, string> dataDict, DateTime chartTime)
        {
            if (dataDict == null || dataDict.Count == 0) return "UNKNOWN";

            string latestState = "UNKNOWN";
            DateTime closestTime = DateTime.MinValue;

            foreach (var kvp in dataDict)
            {
                // Find the newest record that is LESS THAN OR EQUAL TO the current chart time
                if (kvp.Key <= chartTime && kvp.Key > closestTime)
                {
                    closestTime = kvp.Key;
                    latestState = kvp.Value;
                }
            }

            return latestState;
        }

        // ===========================================================================
        // DYNAMIC CRASH-PROOF CSV READER
        // ===========================================================================
        private void LoadRegimeData()
        {
            if (string.IsNullOrEmpty(DataFolderPath)) return;

            string symbol = Instrument.MasterInstrument.Name; 
            string macroFile = Path.Combine(DataFolderPath, $"{symbol}_Macro_Regimes.csv");
            string hmmFile = Path.Combine(DataFolderPath, $"{symbol}_Regimes.csv");

            macroData.Clear();
            hmmData.Clear();
            playbookData.Clear(); // NEW V3

            if (File.Exists(macroFile))
            {
                try 
                {
                    using (FileStream fs = new FileStream(macroFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (StreamReader sr = new StreamReader(fs))
                    {
                        string line;
                        bool isHeader = true;
                        // Fallback indices
                        int dateIdx = 0, timeIdx = 2, macroIdx = 43, playbookIdx = 48; 

                        while ((line = sr.ReadLine()) != null)
                        {
                            string[] parts = line.Split(',');

                            if (isHeader) 
                            { 
                                // Dynamically find columns so we never crash if Python changes
                                for (int i = 0; i < parts.Length; i++)
                                {
                                    string header = parts[i].Trim().ToLower();
                                    if (header == "trade_date") dateIdx = i;
                                    else if (header == "checkpoint_time") timeIdx = i;
                                    else if (header == "official_regime_label") macroIdx = i;
                                    else if (header == "playbook_state") playbookIdx = i;
                                }
                                isHeader = false; 
                                continue; 
                            }
                            
                            // Ensure we have enough columns for the data we need
                            if (parts.Length > Math.Max(macroIdx, playbookIdx))
                            {
                                if (DateTime.TryParse(parts[dateIdx] + " " + parts[timeIdx], out DateTime dt))
                                {
                                    macroData[dt] = parts[macroIdx]; 
                                    playbookData[dt] = parts[playbookIdx]; // NEW V3
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { Print($"{symbol} Macro Read Error: " + ex.Message); }
            }

            if (File.Exists(hmmFile))
            {
                try 
                {
                    using (FileStream fs = new FileStream(hmmFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (StreamReader sr = new StreamReader(fs))
                    {
                        string line;
                        bool isHeader = true;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (isHeader) { isHeader = false; continue; }
                            string[] parts = line.Split(',');
                            if (parts.Length >= 3)
                            {
                                if (DateTime.TryParse(parts[0], out DateTime dt))
                                    hmmData[dt] = parts[2]; 
                            }
                        }
                    }
                }
                catch (Exception ex) { Print($"{symbol} HMM Read Error: " + ex.Message); }
            }
        }

// ===========================================================================
        // LOGIC & UI UPDATES
        // ===========================================================================
        private void UpdateHUD()
        {
            DateTime currentChartTime = Time[0];
            string currentMacro = "UNKNOWN";
            string currentHmm = "UNKNOWN";
            string currentPlaybook = "UNKNOWN"; // NEW V3

            foreach (var kvp in macroData)
            {
                if (kvp.Key <= currentChartTime) currentMacro = kvp.Value;
                else break;
            }

            foreach (var kvp in hmmData)
            {
                if (kvp.Key <= currentChartTime) currentHmm = kvp.Value;
                else break;
            }

            // NEW V3 Dictionary Loop
            foreach (var kvp in playbookData)
            {
                if (kvp.Key <= currentChartTime) currentPlaybook = kvp.Value;
                else break;
            }

            // --- V3 ARCHITECTURE: THE PLAYBOOK GATES ---
            if (isAutoMode)
            {
                // 1. NQ MOMO TRINITY (The All-Weather Engine)
                IsMomoAllowed = true; 
                if (currentPlaybook == "TRANSITION" || currentMacro == "UNRESOLVED" || currentMacro == "OPENING_AUCTION")
                    IsMomoAllowed = false; 
                if (currentMacro == "BRACKET_MACRO" && currentHmm == "TrendDown")
                    IsMomoAllowed = false; 

                // 2. NQ ADXX VOLATILITY BREAKOUT 
                IsAdxAllowed = true; 
                // V3 STRICT ALIGNMENT: Explicitly hard-block all chop and transition
                if (currentPlaybook == "ROTATION_ILLIQUID" || currentPlaybook == "ROTATION_LIQUID" || currentPlaybook == "TRANSITION")
                    IsAdxAllowed = false; 
                if (currentMacro == "BRACKET_MACRO")
                    IsAdxAllowed = false; 

                // 3. PINE ENGINES (BarN & Fade PM)
                IsPineAllowed = true; 
                // V3 STRICT ALIGNMENT: Explicitly hard-block all chop and transition
                if (currentPlaybook == "ROTATION_ILLIQUID" || currentPlaybook == "ROTATION_LIQUID" || currentPlaybook == "TRANSITION")
                    IsPineAllowed = false;
                if (currentMacro == "BRACKET_MACRO" && currentHmm == "TrendDown")
                    IsPineAllowed = false; 

                // 4. ES OPEN SCALPER (Transition Asymmetry)
                IsEsScalperAllowed = false;
                if (currentMacro == "UNRESOLVED" || currentMacro == "OPENING_AUCTION")
                    IsEsScalperAllowed = true;
                if (currentMacro == "BRACKET_MACRO" || currentMacro == "BALANCE_STRUCTURE")
                    IsEsScalperAllowed = false; // Hard block

                // 5. BRACKET SNIPERS (NQ Momo Anchor & ADX.DI)
                IsBracketSniperAllowed = false;
                if (currentMacro == "MEAN_REVERSION_MACRO" || currentMacro == "BALANCE_STRUCTURE" || currentPlaybook == "ROTATION_ILLIQUID")
                    IsBracketSniperAllowed = true;
                if (currentPlaybook == "TREND_EXPANSION" || (currentMacro == "BRACKET_MACRO" && currentHmm == "TrendDown"))
                    IsBracketSniperAllowed = false;
            }

            if (ChartControl != null && macroText != null)
            {
                ChartControl.Dispatcher.InvokeAsync(() =>
                {
                    playbookText.Text = $"PLAYBOOK: {currentPlaybook}";
                    
                    // V3 UI Color Logic
                    if (currentPlaybook == "ROTATION_ILLIQUID")
                        playbookText.Foreground = Brushes.Red;
                    else if (currentPlaybook.Contains("EXPANSION"))
                        playbookText.Foreground = Brushes.LimeGreen;
                    else
                        playbookText.Foreground = Brushes.Yellow;

                    macroText.Text = $"MACRO: {currentMacro}";
                    hmmText.Text = $"HMM: {currentHmm}";

                    UpdateButtonVisual(momoButton, IsMomoAllowed);
                    UpdateButtonVisual(adxButton, IsAdxAllowed);
                    UpdateButtonVisual(pineButton, IsPineAllowed);
                });
            }
        }
        
        // ===========================================================================
        // UI CONSTRUCTION & BUTTON EVENTS
        // ===========================================================================
        private void CreateWPFControls()
        {
            if (myGrid != null) return;

            myGrid = new System.Windows.Controls.Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20)),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 20, 40),
                Width = 280
            };

            for (int i = 0; i < 9; i++) // Increased to 9 rows for Playbook
                myGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock header = new TextBlock { Text = $"[{Instrument.MasterInstrument.Name}] TRINITY MATRIX COMMAND", Foreground = Brushes.White, FontWeight = FontWeights.Bold, Background = Brushes.Blue, TextAlignment = TextAlignment.Center, Padding = new Thickness(5) };
            System.Windows.Controls.Grid.SetRow(header, 0);
            myGrid.Children.Add(header);

            TrafficLightText = new TextBlock { Text = "SYSTEM: AUTO", Foreground = Brushes.Black, FontWeight = FontWeights.Bold, Background = Brushes.LimeGreen, TextAlignment = TextAlignment.Center, Padding = new Thickness(5), Margin = new Thickness(0, 5, 0, 5) };
            System.Windows.Controls.Grid.SetRow(TrafficLightText, 1);
            myGrid.Children.Add(TrafficLightText);
            
            // NEW V3 Playbook UI Element
            playbookText = new TextBlock { Text = "PLAYBOOK: --", Foreground = Brushes.Yellow, FontWeight = FontWeights.Bold, FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 5) };
            System.Windows.Controls.Grid.SetRow(playbookText, 2);
            myGrid.Children.Add(playbookText);

            macroText = new TextBlock { Text = "MACRO: --", Foreground = Brushes.Cyan, FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
            System.Windows.Controls.Grid.SetRow(macroText, 3);
            myGrid.Children.Add(macroText);

            hmmText = new TextBlock { Text = "HMM: --", Foreground = Brushes.Orange, FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 5) };
            System.Windows.Controls.Grid.SetRow(hmmText, 4);
            myGrid.Children.Add(hmmText);

            modeButton = new System.Windows.Controls.Button { Content = "MODE: AUTO", Background = Brushes.DarkSlateGray, Foreground = Brushes.White, Margin = new Thickness(5), Padding = new Thickness(5) };
            modeButton.Click += ModeButton_Click;
            System.Windows.Controls.Grid.SetRow(modeButton, 5);
            myGrid.Children.Add(modeButton);

            momoButton = CreateBotButton("MOMO");
            System.Windows.Controls.Grid.SetRow(momoButton, 6);
            myGrid.Children.Add(momoButton);

            adxButton = CreateBotButton("ADX");
            System.Windows.Controls.Grid.SetRow(adxButton, 7);
            myGrid.Children.Add(adxButton);

            pineButton = CreateBotButton("PINE");
            System.Windows.Controls.Grid.SetRow(pineButton, 8);
            myGrid.Children.Add(pineButton);

            UserControlCollection.Add(myGrid);
        }

        private System.Windows.Controls.Button CreateBotButton(string name)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = $"{name}: OFF",
                Background = Brushes.DarkRed,
                Foreground = Brushes.White,
                Margin = new Thickness(5, 2, 5, 2),
                Padding = new Thickness(5)
            };
            btn.Click += BotButton_Click;
            return btn;
        }

        private void ModeButton_Click(object sender, RoutedEventArgs e)
        {
            isAutoMode = !isAutoMode;
            if (isAutoMode)
            {
                modeButton.Content = "MODE: AUTO";
                TrafficLightText.Text = "SYSTEM: AUTO";
                TrafficLightText.Background = Brushes.LimeGreen;
            }
            else
            {
                modeButton.Content = "MODE: MANUAL";
                TrafficLightText.Text = "SYSTEM: MANUAL";
                TrafficLightText.Background = Brushes.Gold;
            }
            ForceChartUpdate();
        }

        private void BotButton_Click(object sender, RoutedEventArgs e)
        {
            if (isAutoMode) return; 

            var btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;

            string txt = btn.Content.ToString();
            
            if (txt.StartsWith("MOMO")) IsMomoAllowed = !IsMomoAllowed;
            else if (txt.StartsWith("ADX")) IsAdxAllowed = !IsAdxAllowed;
            else if (txt.StartsWith("PINE")) IsPineAllowed = !IsPineAllowed;

            UpdateButtonVisual(momoButton, IsMomoAllowed);
            UpdateButtonVisual(adxButton, IsAdxAllowed);
            UpdateButtonVisual(pineButton, IsPineAllowed);
            
            ForceChartUpdate();
        }

        private void UpdateButtonVisual(System.Windows.Controls.Button btn, bool isAllowed)
        {
            if (btn == null) return;
            string baseName = btn.Content.ToString().Split(':')[0];
            btn.Content = isAllowed ? $"{baseName}: ON" : $"{baseName}: OFF";
            btn.Background = isAllowed ? Brushes.DarkGreen : Brushes.DarkRed;
        }

        private void DisposeWPFControls()
        {
            if (myGrid != null)
            {
                UserControlCollection.Remove(myGrid);
                myGrid = null;
            }
        }

        private void ForceChartUpdate()
        {
            if (ChartControl != null) ChartControl.InvalidateVisual();
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RegimeMatrixHUD[] cacheRegimeMatrixHUD;
		public RegimeMatrixHUD RegimeMatrixHUD(string dataFolderPath)
		{
			return RegimeMatrixHUD(Input, dataFolderPath);
		}

		public RegimeMatrixHUD RegimeMatrixHUD(ISeries<double> input, string dataFolderPath)
		{
			if (cacheRegimeMatrixHUD != null)
				for (int idx = 0; idx < cacheRegimeMatrixHUD.Length; idx++)
					if (cacheRegimeMatrixHUD[idx] != null && cacheRegimeMatrixHUD[idx].DataFolderPath == dataFolderPath && cacheRegimeMatrixHUD[idx].EqualsInput(input))
						return cacheRegimeMatrixHUD[idx];
			return CacheIndicator<RegimeMatrixHUD>(new RegimeMatrixHUD(){ DataFolderPath = dataFolderPath }, input, ref cacheRegimeMatrixHUD);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RegimeMatrixHUD RegimeMatrixHUD(string dataFolderPath)
		{
			return indicator.RegimeMatrixHUD(Input, dataFolderPath);
		}

		public Indicators.RegimeMatrixHUD RegimeMatrixHUD(ISeries<double> input , string dataFolderPath)
		{
			return indicator.RegimeMatrixHUD(input, dataFolderPath);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RegimeMatrixHUD RegimeMatrixHUD(string dataFolderPath)
		{
			return indicator.RegimeMatrixHUD(Input, dataFolderPath);
		}

		public Indicators.RegimeMatrixHUD RegimeMatrixHUD(ISeries<double> input , string dataFolderPath)
		{
			return indicator.RegimeMatrixHUD(input, dataFolderPath);
		}
	}
}

#endregion
