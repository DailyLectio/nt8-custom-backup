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
using System.IO;                  
using System.Globalization;       
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

// 1. Move the enum to the Parent Namespace (The foundation for all chambers)
namespace NinjaTrader.NinjaScript
{
    public enum RegimeInstrument
    {
        NQ, 
        ES, 
        MNQ, 
        MES, 
        GC, 
        CL, 
        SI
    }
}

// 2. The Indicators chamber now sits below it
namespace NinjaTrader.NinjaScript.Indicators
{
    public class MomentumRegimeDisplayHUD : Indicator
    {
        // --- NEW MACRO & SYSTEM LOGIC VARIABLES ---
        private OrderFlowVWAP vwap;
        private string macroFilePath = ""; 
        private string currentMacroBias = "AWAITING 9:35 PRINT";
        private string systemStatus = "HALTED (NO ALIGNMENT)";

        [NinjaScriptProperty]
        [Display(Name = "Selected Instrument", Order = 1, GroupName = "1. Instrument Settings")]
        public RegimeInstrument SelectedInstrument { get; set; }
        
        // --- MICRO STATE MANAGEMENT ---
        private class RegimeRow
        {
            public DateTime TimestampET;
            public int StateId;
            public string RegimeLabel;
            public bool Tradeable;
            public bool AllowLong;
            public bool AllowShort;
            public int SuggestedAdxMin;
            public int SuggestedCiMax;
            public bool SuggestedSlopeGate;
            public string SuggestedStopBucket;
            public string ModelVersion;
        }

        private List<RegimeRow> rows = new List<RegimeRow>();
        private string lastLoadStatus = "UNKNOWN";
        private string lastLoadMessage = "Waiting for first tick...";
        private DateTime lastFileWriteTime = Core.Globals.MinDate;
        private DateTime lastFileReadAttempt = DateTime.MinValue;

        // --- MACRO STATE MANAGEMENT ---
        private class MacroRow
        {
            public DateTime TimestampET;
            public string MacroBias;
        }

        private List<MacroRow> macroRows = new List<MacroRow>();
        private DateTime lastMacroFileWriteTime = Core.Globals.MinDate;
        private DateTime lastMacroFileReadAttempt = DateTime.MinValue;

        [NinjaScriptProperty]
        [Display(Name = "Regime File Path (Auto-Updates)", Order = 1, GroupName = "2. File Settings")]
        public string RegimeFilePath { get; set; }

        [NinjaScriptProperty]
        [Range(1, 300)]
        [Display(Name = "Reload Interval (sec)", Order = 2, GroupName = "2. File Settings")]
        public int ReloadSeconds { get; set; }

        [NinjaScriptProperty]
        [Range(1, 72)]
        [Display(Name = "File Stale Warning (hrs)", Order = 3, GroupName = "2. File Settings")]
        public int StaleHours { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "HUD Position", Order = 1, GroupName = "3. Display Settings")]
        public TextPosition HudPosition { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Timestamps", Order = 2, GroupName = "3. Display Settings")]
        public bool ShowTimestamps { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Suggested Settings", Order = 3, GroupName = "3. Display Settings")]
        public bool ShowSuggestedSettings { get; set; }

        [NinjaScriptProperty]
        [Range(8, 36)]
        [Display(Name = "Font Size", Order = 4, GroupName = "3. Display Settings")]
        public int FontSize { get; set; }

        private const string HudTag = "MomentumRegimeHUD_Tag";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Displays the current structural Macro bias and tactical Micro momentum regime read from local CSVs.";
                Name = "Momentum Regime HUD";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
        
                SelectedInstrument = RegimeInstrument.NQ; 
                RegimeFilePath = @"C:\Users\Valued Customer\NT8_Regimes\Active\NQ_Regimes.csv";
                ReloadSeconds = 5;
                StaleHours = 24;
                HudPosition = TextPosition.TopRight;
                ShowTimestamps = true;
                ShowSuggestedSettings = true;
                FontSize = 14; 
            }
            else if (State == State.DataLoaded)
            {
                vwap = OrderFlowVWAP(VWAPResolution.Standard, Bars.TradingHours, VWAPStandardDeviations.Three, 1, 2, 3);
                
                // --- DYNAMIC PATH ASSIGNMENT FOR MULTI-INSTRUMENT ---
                if (SelectedInstrument == RegimeInstrument.NQ || SelectedInstrument == RegimeInstrument.MNQ)
                {
                    RegimeFilePath = @"C:\Users\Valued Customer\NT8_Regimes\Active\NQ_Regimes.csv";
                    macroFilePath = @"C:\Users\Valued Customer\NT8_Regimes\Active\NQ_Macro_Regimes.csv";
                }
                else if (SelectedInstrument == RegimeInstrument.ES || SelectedInstrument == RegimeInstrument.MES)
                {
                    RegimeFilePath = @"C:\Users\Valued Customer\NT8_Regimes\Active\ES_Regimes.csv";
                    macroFilePath = @"C:\Users\Valued Customer\NT8_Regimes\Active\ES_Macro_Regimes.csv";
                }
                
                TryReloadFile();
                TryReloadMacroFile();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;
        
            TryReloadFile();
            TryReloadMacroFile();
        
            currentMacroBias = GetMacroBiasAt(Time[0]);
        
            if (rows.Count == 0)
            {
                string bodyMissing =
                    "STATUS >>> " + lastLoadStatus + Environment.NewLine +
                    "Message: " + (string.IsNullOrWhiteSpace(lastLoadMessage) ? "No regime rows loaded." : lastLoadMessage) + Environment.NewLine +
                    "File: " + RegimeFilePath;
        
                DrawHud("Momentum Regime HUD", bodyMissing, Brushes.LightGray);
                return;
            }
        
            RegimeRow row = FindLatestRowAtOrBefore(Time[0]);
        
            if (row == null)
            {
                string bodyNoRow =
                    "STATUS >>> NO_MATCHING_ROW" + Environment.NewLine +
                    "Chart Time: " + Time[0].ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Earliest Regime Row: " + rows[0].TimestampET.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        
                DrawHud("Momentum Regime HUD", bodyNoRow, Brushes.LightGray);
                return;
            }
        
            string regimePrefix = GetRegimePrefix(row.RegimeLabel);
            string staleStatus = GetCombinedStaleStatus(row, Time[0]);
        
            // Calculate system status (Restriction Removed)
            systemStatus = CalculateSystemStatus(currentMacroBias, row.RegimeLabel);
        
            string setupWarning = "";
            double proximityTicks = 12 * TickSize; 
            double price = Close[0];
        
            if (currentMacroBias.Contains("ROTATION"))
            {
                // Hook in VAH/VAL proximity alerts here in the future
            }
            else if (currentMacroBias.Contains("RECLAIM"))
            {
                if (Math.Abs(price - vwap.VWAP[0]) <= proximityTicks)
                    setupWarning = " !!! APPROACHING VWAP RECLAIM SETUP !!!";
            }
        
            // Universal Display Logic for all instruments
            string body = "MACRO REGIME >>> " + currentMacroBias + (setupWarning != "" ? Environment.NewLine + setupWarning : "") + Environment.NewLine +
                          "MICRO REGIME >>> " + regimePrefix + " " + Safe(row.RegimeLabel) + Environment.NewLine +
                          "SYSTEM STATUS >>> " + systemStatus + Environment.NewLine +
                          "------------------------" + Environment.NewLine +
                          "Tradeable: " + YesNo(row.Tradeable) + Environment.NewLine +
                          "Allow Long: " + YesNo(row.AllowLong) + Environment.NewLine +
                          "Allow Short: " + YesNo(row.AllowShort) + Environment.NewLine +
                          "File Status: " + staleStatus;

            if (ShowSuggestedSettings)
            {
                body += Environment.NewLine +
                        "ADX Min: " + row.SuggestedAdxMin + Environment.NewLine +
                        "CI Max: " + row.SuggestedCiMax + Environment.NewLine +
                        "Slope Gate: " + YesNo(row.SuggestedSlopeGate);
            }
        
            body += Environment.NewLine + "Instrument: " + SelectedInstrument;
        
            if (ShowTimestamps)
            {
                body += Environment.NewLine + "Chart Time: " + Time[0].ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                body += Environment.NewLine + "Micro Row Time: " + row.TimestampET.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
        
            // Traffic Light & Playbook Background Color Logic
            Brush bgBrush = Brushes.LightGray; 
            
            if (currentMacroBias.Contains("EXIT") || currentMacroBias.Contains("Exhaustion"))
                bgBrush = Brushes.DarkRed; 
            else if (currentMacroBias.Contains("BREAKOUT"))
                bgBrush = Brushes.LimeGreen;
            else if (currentMacroBias.Contains("ROTATION"))
                bgBrush = Brushes.SteelBlue;
            else if (currentMacroBias.Contains("RECLAIM"))
                bgBrush = Brushes.White;
            else if (systemStatus.Contains("HALTED") || currentMacroBias.Contains("CHOP"))
                bgBrush = Brushes.Yellow;
        
            DrawHud("Momentum Regime HUD", body, bgBrush);
        }

        private void TryReloadFile()
        {
            DateTime now = DateTime.Now;
            if ((now - lastFileReadAttempt).TotalSeconds < ReloadSeconds)
                return;

            lastFileReadAttempt = now;

            try
            {
                if (!File.Exists(RegimeFilePath))
                {
                    rows.Clear();
                    lastLoadStatus = "MISSING";
                    lastLoadMessage = "File not found.";
                    lastFileWriteTime = Core.Globals.MinDate;
                    return;
                }

                DateTime writeTime = File.GetLastWriteTime(RegimeFilePath);

                if (writeTime == lastFileWriteTime && rows.Count > 0)
                {
                    lastLoadStatus = "OK_CACHED";
                    lastLoadMessage = "Using cached regime rows.";
                    return;
                }

                var loaded = new List<RegimeRow>();

                using (var stream = new FileStream(RegimeFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    bool headerSkipped = false;

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        if (!headerSkipped)
                        {
                            headerSkipped = true;
                            if (line.StartsWith("TimestampET", StringComparison.OrdinalIgnoreCase))
                                continue;
                        }

                        string[] parts = line.Split(',');
                        if (parts.Length < 11)
                            continue;

                        DateTime ts;
                        if (!DateTime.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out ts))
                            continue;

                        loaded.Add(new RegimeRow
                        {
                            TimestampET = ts,
                            StateId = ParseInt(parts[1], -1),
                            RegimeLabel = Safe(parts[2]),
                            Tradeable = ParseBool(parts[3]),
                            AllowLong = ParseBool(parts[4]),
                            AllowShort = ParseBool(parts[5]),
                            SuggestedAdxMin = ParseInt(parts[6], 0),
                            SuggestedCiMax = ParseInt(parts[7], 0),
                            SuggestedSlopeGate = ParseBool(parts[8]),
                            SuggestedStopBucket = Safe(parts[9]),
                            ModelVersion = Safe(parts[10])
                        });
                    }
                }

                loaded.Sort((a, b) => a.TimestampET.CompareTo(b.TimestampET));

                rows.Clear();
                rows.AddRange(loaded);

                lastFileWriteTime = writeTime;
                lastLoadStatus = rows.Count > 0 ? "OK" : "EMPTY";
                lastLoadMessage = rows.Count > 0
                    ? "Loaded " + rows.Count + " regime rows."
                    : "No valid regime rows found.";
            }
            catch (Exception ex)
            {
                rows.Clear();
                lastLoadStatus = "INVALID";
                lastLoadMessage = ex.Message;
            }
        }

        private void TryReloadMacroFile()
        {
            DateTime now = DateTime.Now;
            if ((now - lastMacroFileReadAttempt).TotalSeconds < ReloadSeconds)
                return;

            lastMacroFileReadAttempt = now;

            try
            {
                if (!File.Exists(macroFilePath)) return;

                DateTime writeTime = File.GetLastWriteTime(macroFilePath);
                if (writeTime == lastMacroFileWriteTime && macroRows.Count > 0) return;

                var loaded = new List<MacroRow>();

                using (var stream = new FileStream(macroFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    bool headerSkipped = false;

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        if (!headerSkipped)
                        {
                            headerSkipped = true;
                            continue;
                        }

                        string[] parts = line.Split(',');
                        if (parts.Length < 2) continue;

                        DateTime ts;
                        if (!DateTime.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out ts)) 
                            continue;

                        loaded.Add(new MacroRow
                        {
                            TimestampET = ts,
                            MacroBias = Safe(parts[1])
                        });
                    }
                }

                loaded.Sort((a, b) => a.TimestampET.CompareTo(b.TimestampET));
                macroRows.Clear();
                macroRows.AddRange(loaded);
                lastMacroFileWriteTime = writeTime;
            }
            catch (Exception)
            {
                // Fail silently
            }
        }

        private RegimeRow FindLatestRowAtOrBefore(DateTime chartTime)
        {
            if (rows.Count == 0)
                return null;

            int lo = 0;
            int hi = rows.Count - 1;
            int best = -1;

            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                DateTime ts = rows[mid].TimestampET;

                if (ts <= chartTime)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return best >= 0 ? rows[best] : null;
        }

        private string GetMacroBiasAt(DateTime chartTime)
        {
            if (macroRows.Count == 0) return "AWAITING MACRO DATA";

            int lo = 0, hi = macroRows.Count - 1, best = -1;

            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (macroRows[mid].TimestampET <= chartTime)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return best >= 0 ? macroRows[best].MacroBias : "AWAITING MACRO DATA";
        }

        private string GetFileStaleStatus()
        {
            if (!File.Exists(RegimeFilePath))
                return "MISSING";

            if (lastLoadStatus == "INVALID")
                return "INVALID";

            if (lastFileWriteTime <= Core.Globals.MinDate)
                return "UNKNOWN";

            double hoursOld = (DateTime.Now - lastFileWriteTime).TotalHours;
            if (hoursOld > StaleHours)
                return "FILE_STALE";

            return "FILE_OK";
        }

        private string GetCombinedStaleStatus(RegimeRow row, DateTime chartTime)
        {
            string fileStatus = GetFileStaleStatus();

            if (row == null)
                return fileStatus;

            double rowAgeHours = (chartTime - row.TimestampET).TotalHours;

            if (rowAgeHours > 72)
                return fileStatus + " / ROW_VERY_STALE";
            if (rowAgeHours > 24)
                return fileStatus + " / ROW_STALE";

            return fileStatus + " / ROW_OK";
        }

        private string CalculateSystemStatus(string macro, string micro)
        {
            // Removed the ES lockout here
            if (micro == "Transition") 
                return "HALTED: Transition Chop Detected";

            if (macro == "Gap & Go" && (micro == "TrendUp" || micro == "TrendDown")) 
                return "MAXIMUM AGGRESSION: Trend Alignment";

            if (macro == "Gap & Fill" && (micro == "TrendUp" || micro == "TrendDown")) 
                return "HALTED: False Momentum Conflict";

            if (macro == "Inside Value" && micro == "Balance") 
                return "NORMAL SIZE: Structural Mean Reversion";

            return "HALTED: Macro/Micro Conflict";
        }

        private void DrawHud(string header, string body, Brush areaBrush)
        {
            string fullText = header + Environment.NewLine +
                              "------------------------" + Environment.NewLine +
                              body;

            Draw.TextFixed(
                this,
                HudTag,
                fullText,
                HudPosition,
                Brushes.Black, 
                new SimpleFont("Calibri", FontSize), 
                Brushes.Black, 
                areaBrush,     
                100);          
        }

        private static bool ParseBool(string value)
        {
            string s = Safe(value).ToLower();
            
            if (s == "true" || s == "1" || s == "1.0" || s == "yes" || s == "y")
                return true;
                
            return false;
        }

        private static int ParseInt(string value, int fallback)
        {
            string s = Safe(value);
            
            double tempDouble;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out tempDouble))
            {
                return (int)Math.Round(tempDouble); 
            }
            
            return fallback;
        }

        private static string Safe(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) 
                return string.Empty;
                
            return s.Replace("\"", "").Trim();
        }

        private static string YesNo(bool value)
        {
            return value ? "Yes" : "No";
        }

        private static string GetRegimePrefix(string regimeLabel)
        {
            switch (Safe(regimeLabel))
            {
                case "TrendUp":
                    return "[UP]";
                case "TrendDown":
                    return "[DOWN]";
                case "Balance":
                    return "[BAL]";
                case "Transition":
                    return "[TRANS]";
                default:
                    return "[?]";
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MomentumRegimeDisplayHUD[] cacheMomentumRegimeDisplayHUD;
		public MomentumRegimeDisplayHUD MomentumRegimeDisplayHUD(RegimeInstrument selectedInstrument, string regimeFilePath, int reloadSeconds, int staleHours, TextPosition hudPosition, bool showTimestamps, bool showSuggestedSettings, int fontSize)
		{
			return MomentumRegimeDisplayHUD(Input, selectedInstrument, regimeFilePath, reloadSeconds, staleHours, hudPosition, showTimestamps, showSuggestedSettings, fontSize);
		}

		public MomentumRegimeDisplayHUD MomentumRegimeDisplayHUD(ISeries<double> input, RegimeInstrument selectedInstrument, string regimeFilePath, int reloadSeconds, int staleHours, TextPosition hudPosition, bool showTimestamps, bool showSuggestedSettings, int fontSize)
		{
			if (cacheMomentumRegimeDisplayHUD != null)
				for (int idx = 0; idx < cacheMomentumRegimeDisplayHUD.Length; idx++)
					if (cacheMomentumRegimeDisplayHUD[idx] != null && cacheMomentumRegimeDisplayHUD[idx].SelectedInstrument == selectedInstrument && cacheMomentumRegimeDisplayHUD[idx].RegimeFilePath == regimeFilePath && cacheMomentumRegimeDisplayHUD[idx].ReloadSeconds == reloadSeconds && cacheMomentumRegimeDisplayHUD[idx].StaleHours == staleHours && cacheMomentumRegimeDisplayHUD[idx].HudPosition == hudPosition && cacheMomentumRegimeDisplayHUD[idx].ShowTimestamps == showTimestamps && cacheMomentumRegimeDisplayHUD[idx].ShowSuggestedSettings == showSuggestedSettings && cacheMomentumRegimeDisplayHUD[idx].FontSize == fontSize && cacheMomentumRegimeDisplayHUD[idx].EqualsInput(input))
						return cacheMomentumRegimeDisplayHUD[idx];
			return CacheIndicator<MomentumRegimeDisplayHUD>(new MomentumRegimeDisplayHUD(){ SelectedInstrument = selectedInstrument, RegimeFilePath = regimeFilePath, ReloadSeconds = reloadSeconds, StaleHours = staleHours, HudPosition = hudPosition, ShowTimestamps = showTimestamps, ShowSuggestedSettings = showSuggestedSettings, FontSize = fontSize }, input, ref cacheMomentumRegimeDisplayHUD);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MomentumRegimeDisplayHUD MomentumRegimeDisplayHUD(RegimeInstrument selectedInstrument, string regimeFilePath, int reloadSeconds, int staleHours, TextPosition hudPosition, bool showTimestamps, bool showSuggestedSettings, int fontSize)
		{
			return indicator.MomentumRegimeDisplayHUD(Input, selectedInstrument, regimeFilePath, reloadSeconds, staleHours, hudPosition, showTimestamps, showSuggestedSettings, fontSize);
		}

		public Indicators.MomentumRegimeDisplayHUD MomentumRegimeDisplayHUD(ISeries<double> input , RegimeInstrument selectedInstrument, string regimeFilePath, int reloadSeconds, int staleHours, TextPosition hudPosition, bool showTimestamps, bool showSuggestedSettings, int fontSize)
		{
			return indicator.MomentumRegimeDisplayHUD(input, selectedInstrument, regimeFilePath, reloadSeconds, staleHours, hudPosition, showTimestamps, showSuggestedSettings, fontSize);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MomentumRegimeDisplayHUD MomentumRegimeDisplayHUD(RegimeInstrument selectedInstrument, string regimeFilePath, int reloadSeconds, int staleHours, TextPosition hudPosition, bool showTimestamps, bool showSuggestedSettings, int fontSize)
		{
			return indicator.MomentumRegimeDisplayHUD(Input, selectedInstrument, regimeFilePath, reloadSeconds, staleHours, hudPosition, showTimestamps, showSuggestedSettings, fontSize);
		}

		public Indicators.MomentumRegimeDisplayHUD MomentumRegimeDisplayHUD(ISeries<double> input , RegimeInstrument selectedInstrument, string regimeFilePath, int reloadSeconds, int staleHours, TextPosition hudPosition, bool showTimestamps, bool showSuggestedSettings, int fontSize)
		{
			return indicator.MomentumRegimeDisplayHUD(input, selectedInstrument, regimeFilePath, reloadSeconds, staleHours, hudPosition, showTimestamps, showSuggestedSettings, fontSize);
		}
	}
}

#endregion
