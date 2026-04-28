#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// -------------------------------------------------------------------------
public enum MacroSymbol { NQ, ES }
// -------------------------------------------------------------------------

namespace NinjaTrader.NinjaScript.Indicators
{
    public class MacroRegimeReader : Indicator
    {
        private string csvFilePath = "";
        private DateTime lastFileReadTime = DateTime.MinValue;
        
        private string currentRegime = "WAITING FOR DATA";
        private string currentBias = "WAITING FOR DATA";
        private string currentPhase = "--:--";
        private string previousBias = "WAITING FOR DATA";
        
        // Cache: Time -> Tuple<Regime, Bias, PhaseTime>
        private Dictionary<DateTime, Tuple<string, string, string>> regimeCache = new Dictionary<DateTime, Tuple<string, string, string>>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Reads the Python-generated Macro_Regimes.csv (Original Stable Time-Loop Architecture).";
                Name                                        = "Macro Regime Reader";
                Calculate                                   = Calculate.OnBarClose; // Restored True Original
                IsOverlay                                   = true;
                DisplayInDataBox                            = false;
                DrawOnPricePanel                            = true;
                IsSuspendedWhileInactive                    = true;
                
                // User Parameters
                SelectedInstrument                          = MacroSymbol.NQ;
                EnableAlerts                                = true;

                HudLocation                                 = TextPosition.TopRight;
                HudBackgroundOpacity                        = 0.8;
                HudBackgroundColor                          = Brushes.Black;
            }
            else if (State == State.DataLoaded)
            {
                string fileName = SelectedInstrument == MacroSymbol.NQ ? "NQ_Macro_Regimes.csv" : "ES_Macro_Regimes.csv";
                csvFilePath = @"C:\Users\Valued Customer\NT8_Regimes\Active\" + fileName;
                
                LoadCsvData();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 20) return;

            // Live updates: periodically check if the Python script saved new data
            if (State == State.Realtime)
            {
                try
                {
                    DateTime currentWriteTime = File.GetLastWriteTime(csvFilePath);
                    if (currentWriteTime > lastFileReadTime)
                    {
                        LoadCsvData();
                    }
                }
                catch { /* Ignore brief file locks while Python is writing */ }
            }

            // TRUE ORIGINAL LOGIC: 60-Minute stripped-second loop (Matches your initial RED success)
            DateTime barTime = Time[0];
            DateTime lookupTime = new DateTime(barTime.Year, barTime.Month, barTime.Day, barTime.Hour, barTime.Minute, 0);
            
            bool dataFound = false;
            for (int i = 0; i < 60; i++)
            {
                DateTime checkTime = lookupTime.AddMinutes(-i);
                if (regimeCache.ContainsKey(checkTime))
                {
                    currentRegime = regimeCache[checkTime].Item1;
                    currentBias = regimeCache[checkTime].Item2;
                    currentPhase = regimeCache[checkTime].Item3;
                    dataFound = true;
                    break;
                }
            }

            // Trigger Alert if the Bias changes during the live session
            if (State == State.Realtime && dataFound)
            {
                if (previousBias != "WAITING FOR DATA" && currentBias != previousBias)
                {
                    if (EnableAlerts)
                    {
                        Alert("MacroAlert", Priority.High, $"{SelectedInstrument} Phase {currentPhase} Update: {currentBias}", "Alert4.wav", 10, Brushes.Black, Brushes.Yellow);
                    }
                }
                previousBias = currentBias;
            }

            DrawHud();
        }

        private void LoadCsvData()
        {
            if (!File.Exists(csvFilePath)) return;

            try
            {
                using (FileStream fs = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(fs))
                {
                    regimeCache.Clear();
                    string line;
                    bool isHeader = true;

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (isHeader) { isHeader = false; continue; }

                        string[] cols = line.Split(',');
                        if (cols.Length >= 44) 
                        {
                            string dateStr = cols[0]; // trade_date
                            string timeStr = cols[2]; // checkpoint_time (Phase)
                            string regime = cols[43]; // official_regime_label
                            string bias = cols[42];   // official_bias_label

                            if (DateTime.TryParse(dateStr + " " + timeStr, out DateTime rowTime))
                            {
                                regimeCache[rowTime] = new Tuple<string, string, string>(regime, bias, timeStr);
                            }
                        }
                    }
                }
                lastFileReadTime = File.GetLastWriteTime(csvFilePath);
            }
            catch { /* Silent catch for thread safety */ }
        }

        // --- DYNAMIC COLOR LOGIC ---
        private Brush GetBiasColor(string bias)
        {
            string b = bias.ToUpper();
            
            if (b.Contains("BULL") || b.Contains("BUY") || b.Contains("DRIVE_EXPECTED")) return Brushes.LimeGreen;
            if (b.Contains("BEAR") || b.Contains("SELL")) return Brushes.Red;
            if (b.Contains("FADE") || b.Contains("TRADE_LEVELS") || b.Contains("REVERSION") || b.Contains("FAILURE")) return Brushes.Goldenrod;
            
            return Brushes.WhiteSmoke;
        }

        private void DrawHud()
        {
            string hudText = string.Format("{0}\nREGIME:   {1}\nBIAS:     {2}\nPHASE:    {3}", 
                SelectedInstrument.ToString(), 
                currentRegime.Replace("_", " "), 
                currentBias.Replace("_", " "), 
                currentPhase);
            
            Brush dynamicColor = GetBiasColor(currentBias);

            Draw.TextFixed(this, "MacroRegimeHUD", hudText, HudLocation, dynamicColor, 
                new Gui.Tools.SimpleFont("Consolas", 14) { Bold = true }, 
                Brushes.Transparent, HudBackgroundColor, (int)(HudBackgroundOpacity * 255));
        }

        #region Properties
        [NinjaScriptProperty]
        [System.ComponentModel.DataAnnotations.Display(Name="Select Instrument", Order=1, GroupName="1. Settings")]
        public MacroSymbol SelectedInstrument { get; set; }

        [NinjaScriptProperty]
        [System.ComponentModel.DataAnnotations.Display(Name="Enable Audio Alerts", Order=2, GroupName="1. Settings")]
        public bool EnableAlerts { get; set; }

        [NinjaScriptProperty]
        [System.ComponentModel.DataAnnotations.Display(Name="HUD Location", Order=3, GroupName="2. Visuals")]
        public TextPosition HudLocation { get; set; }

        [NinjaScriptProperty]
        [System.Xml.Serialization.XmlIgnore]
        [System.ComponentModel.DataAnnotations.Display(Name="Background Color", Order=4, GroupName="2. Visuals")]
        public Brush HudBackgroundColor { get; set; }

        [System.ComponentModel.Browsable(false)]
        public string HudBackgroundColorSerializable
        {
            get { return Serialize.BrushToString(HudBackgroundColor); }
            set { HudBackgroundColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [System.ComponentModel.DataAnnotations.Range(0.0, 1.0)]
        [System.ComponentModel.DataAnnotations.Display(Name="Background Opacity", Order=5, GroupName="2. Visuals")]
        public double HudBackgroundOpacity { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MacroRegimeReader[] cacheMacroRegimeReader;
		public MacroRegimeReader MacroRegimeReader(MacroSymbol selectedInstrument, bool enableAlerts, TextPosition hudLocation, Brush hudBackgroundColor, double hudBackgroundOpacity)
		{
			return MacroRegimeReader(Input, selectedInstrument, enableAlerts, hudLocation, hudBackgroundColor, hudBackgroundOpacity);
		}

		public MacroRegimeReader MacroRegimeReader(ISeries<double> input, MacroSymbol selectedInstrument, bool enableAlerts, TextPosition hudLocation, Brush hudBackgroundColor, double hudBackgroundOpacity)
		{
			if (cacheMacroRegimeReader != null)
				for (int idx = 0; idx < cacheMacroRegimeReader.Length; idx++)
					if (cacheMacroRegimeReader[idx] != null && cacheMacroRegimeReader[idx].SelectedInstrument == selectedInstrument && cacheMacroRegimeReader[idx].EnableAlerts == enableAlerts && cacheMacroRegimeReader[idx].HudLocation == hudLocation && cacheMacroRegimeReader[idx].HudBackgroundColor == hudBackgroundColor && cacheMacroRegimeReader[idx].HudBackgroundOpacity == hudBackgroundOpacity && cacheMacroRegimeReader[idx].EqualsInput(input))
						return cacheMacroRegimeReader[idx];
			return CacheIndicator<MacroRegimeReader>(new MacroRegimeReader(){ SelectedInstrument = selectedInstrument, EnableAlerts = enableAlerts, HudLocation = hudLocation, HudBackgroundColor = hudBackgroundColor, HudBackgroundOpacity = hudBackgroundOpacity }, input, ref cacheMacroRegimeReader);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MacroRegimeReader MacroRegimeReader(MacroSymbol selectedInstrument, bool enableAlerts, TextPosition hudLocation, Brush hudBackgroundColor, double hudBackgroundOpacity)
		{
			return indicator.MacroRegimeReader(Input, selectedInstrument, enableAlerts, hudLocation, hudBackgroundColor, hudBackgroundOpacity);
		}

		public Indicators.MacroRegimeReader MacroRegimeReader(ISeries<double> input , MacroSymbol selectedInstrument, bool enableAlerts, TextPosition hudLocation, Brush hudBackgroundColor, double hudBackgroundOpacity)
		{
			return indicator.MacroRegimeReader(input, selectedInstrument, enableAlerts, hudLocation, hudBackgroundColor, hudBackgroundOpacity);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MacroRegimeReader MacroRegimeReader(MacroSymbol selectedInstrument, bool enableAlerts, TextPosition hudLocation, Brush hudBackgroundColor, double hudBackgroundOpacity)
		{
			return indicator.MacroRegimeReader(Input, selectedInstrument, enableAlerts, hudLocation, hudBackgroundColor, hudBackgroundOpacity);
		}

		public Indicators.MacroRegimeReader MacroRegimeReader(ISeries<double> input , MacroSymbol selectedInstrument, bool enableAlerts, TextPosition hudLocation, Brush hudBackgroundColor, double hudBackgroundOpacity)
		{
			return indicator.MacroRegimeReader(input, selectedInstrument, enableAlerts, hudLocation, hudBackgroundColor, hudBackgroundOpacity);
		}
	}
}

#endregion
