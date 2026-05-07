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
    // ============================================================================
    // RegimeMatrixHUD_V3C
    //
    // SIDE-BY-SIDE VERSION.
    //
    // This is intentionally NOT named RegimeMatrixHUD.
    // It can run next to the original vB/OG RegimeMatrixHUD without replacing it.
    //
    // Important:
    // - Existing strategies currently read Indicators.RegimeMatrixHUD.Instances.
    // - This class exposes its own InstancesV3C dictionary, but current bots will NOT
    //   use it unless later updated.
    // - Use this for visual validation / vC shadow comparison while vB keeps controlling bots.
    //
    // Reads:
    //   C:\Users\Valued Customer\NT8_Regimes\V3C\ES_Regimes_V3C_Latest.csv
    //   C:\Users\Valued Customer\NT8_Regimes\V3C\NQ_Regimes_V3C_Latest.csv
    // ============================================================================

    public class RegimeMatrixHUD_V3C : Indicator
    {
        public static readonly Dictionary<string, RegimeMatrixHUD_V3C> InstancesV3C =
            new Dictionary<string, RegimeMatrixHUD_V3C>();

        private static readonly object instanceLock = new object();

        // --- V3C lane outputs ---
        public bool IsMomoAllowed { get; private set; } = false;
        public bool IsMomoLongAllowed { get; private set; } = false;
        public bool IsMomoShortAllowed { get; private set; } = false;
        public bool IsAdxAllowed { get; private set; } = false;
        public bool IsPineAllowed { get; private set; } = false;
        public bool IsEsScalperAllowed { get; private set; } = false;
        public bool IsBracketSniperAllowed { get; private set; } = false;
        public bool IsExpansionBotAllowed { get; private set; } = false;
        public bool IsCompressionBotAllowed { get; private set; } = false;
        public bool IsFadeBotAllowed { get; private set; } = false;
        public bool AllowLong { get; private set; } = false;
        public bool AllowShort { get; private set; } = false;

        // --- V3C state outputs ---
        public string FinalRegime { get; private set; } = "UNKNOWN";
        public string FinalDirection { get; private set; } = "NONE";
        public string MacroRegime { get; private set; } = "UNKNOWN";
        public string MacroPlaybook { get; private set; } = "UNKNOWN";
        public string HMMMicro { get; private set; } = "UNKNOWN";
        public string ReasonCode { get; private set; } = "";
        public string StaleReason { get; private set; } = "";
        public int RegimeConfidence { get; private set; } = 0;
        public int ConflictScore { get; private set; } = 0;
        public double Velocity3CP { get; private set; } = 0.0;
        public bool StaleDataFlag { get; private set; } = false;
        public int HMMStateAgeBars { get; private set; } = 0;
        public bool AsymHysteresisGateOpen { get; private set; } = false;
        public string AsymHysteresisReason { get; private set; } = "UNKNOWN";
        public bool AsymHysteresisEnabled { get; private set; } = false;
        public bool LiveFeedFresh { get; private set; } = false;

        private DateTime lastFileWriteUtc = DateTime.MinValue;
        private string lastFileRead = "";
        private DateTime lastPipelineCheck = DateTime.MinValue;
        private const int PipelineCheckSeconds = 60;
        private string liveExportFile = "";

        [NinjaScriptProperty]
        [Display(Name="Data Folder Path", Description="Path to V3C Python CSVs.", GroupName="Data Settings", Order=0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3C";

        [NinjaScriptProperty]
        [Display(Name="Use Leader Symbol Mapping", Description="MES->ES, MNQ->NQ for regime files.", GroupName="Data Settings", Order=1)]
        public bool UseLeaderSymbolMapping { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Debug Prints", Description="Print V3C read diagnostics.", GroupName="Data Settings", Order=2)]
        public bool DebugPrints { get; set; } = false;

        private Grid myGrid;
        private TextBlock headerText;
        private TextBlock statusText;
        private TextBlock regimeText;
        private TextBlock directionText;
        private TextBlock macroText;
        private TextBlock hmmText;
        private TextBlock reasonText;
        private TextBlock staleText;
        private TextBlock pipelineText;
        private TextBlock gateText;
        private TextBlock laneText1;
        private TextBlock laneText2;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "V3C Regime Matrix HUD side-by-side display. Does not replace OG RegimeMatrixHUD.";
                Name = "Regime Matrix HUD V3C";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.DataLoaded)
            {
                RegisterInstance();
                string leaderSym = GetLeaderSymbol(Instrument.MasterInstrument.Name);
                string exportDir = Path.GetFullPath(Path.Combine(DataFolderPath, @"..\Exports"));
                liveExportFile = Path.Combine(exportDir, $"{leaderSym}_1min_export.txt");

                if (ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync(() => CreateWPFControls());
            }
            else if (State == State.Terminated)
            {
                UnregisterInstance();

                if (ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync(() => DisposeWPFControls());
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            LoadV3CFileIfNeeded();
            CheckPipelineStatus();
            UpdateHUD();
        }

        private void RegisterInstance()
        {
            string chartSym = Instrument.MasterInstrument.Name;
            string leaderSym = GetLeaderSymbol(chartSym);

            lock (instanceLock)
            {
                InstancesV3C[chartSym] = this;
                if (UseLeaderSymbolMapping && leaderSym != chartSym)
                    InstancesV3C[leaderSym] = this;
            }
        }

        private void UnregisterInstance()
        {
            string chartSym = Instrument.MasterInstrument.Name;
            string leaderSym = GetLeaderSymbol(chartSym);

            lock (instanceLock)
            {
                if (InstancesV3C.ContainsKey(chartSym) && InstancesV3C[chartSym] == this)
                    InstancesV3C.Remove(chartSym);

                if (UseLeaderSymbolMapping && InstancesV3C.ContainsKey(leaderSym) && InstancesV3C[leaderSym] == this)
                    InstancesV3C.Remove(leaderSym);
            }
        }

        private string GetLeaderSymbol(string sym)
        {
            if (!UseLeaderSymbolMapping || string.IsNullOrEmpty(sym))
                return sym;

            sym = sym.Trim().ToUpper();

            if (sym == "MES") return "ES";
            if (sym == "MNQ") return "NQ";
            if (sym == "MGC") return "GC";
            if (sym == "MCL") return "CL";
            if (sym == "MSI") return "SI";

            return sym;
        }

        private void LoadV3CFileIfNeeded()
        {
            if (string.IsNullOrEmpty(DataFolderPath))
            {
                SetBlocked("NO_DATA_FOLDER");
                return;
            }

            string chartSym = Instrument.MasterInstrument.Name;
            string leaderSym = GetLeaderSymbol(chartSym);

            string latestFile = Path.Combine(DataFolderPath, $"{leaderSym}_Regimes_V3C_Latest.csv");
            string historyFile = Path.Combine(DataFolderPath, $"{leaderSym}_Regimes_V3C.csv");

            string fileToRead = File.Exists(latestFile) ? latestFile : historyFile;

            if (!File.Exists(fileToRead))
            {
                SetBlocked("MISSING_" + Path.GetFileName(fileToRead));
                return;
            }

            DateTime writeUtc;
            try
            {
                writeUtc = File.GetLastWriteTimeUtc(fileToRead);
            }
            catch
            {
                SetBlocked("FILE_TIMESTAMP_ERROR");
                return;
            }

            if (fileToRead == lastFileRead && writeUtc <= lastFileWriteUtc)
                return;

            try
            {
                Dictionary<string, string> row = ReadLastCsvRow(fileToRead);

                FinalRegime = Get(row, "FinalRegime", "UNKNOWN");
                FinalDirection = Get(row, "FinalDirection", "NONE");
                MacroRegime = Get(row, "MacroRegime", "UNKNOWN");
                MacroPlaybook = Get(row, "MacroPlaybook", "UNKNOWN");
                HMMMicro = Get(row, "HMM_Micro", "UNKNOWN");
                ReasonCode = Get(row, "ReasonCode", "");
                StaleReason = Get(row, "StaleReason", "");

                RegimeConfidence = ParseInt(Get(row, "RegimeConfidence", "0"), 0);
                ConflictScore = ParseInt(Get(row, "ConflictScore", "0"), 0);
                Velocity3CP = ParseDouble(Get(row, "Velocity3CP", "0"), 0.0);
                HMMStateAgeBars = ParseInt(Get(row, "HMMStateAgeBars", "0"), 0);
                AsymHysteresisReason = Get(row, "AsymHysteresisReason", "COL_NOT_FOUND");
                AsymHysteresisGateOpen = ParseBool(Get(row, "AsymHysteresisGateOpen", "False"));
                AsymHysteresisEnabled = ParseBool(Get(row, "AsymHysteresisEnabled", "False"));

                IsMomoAllowed = ParseBool(Get(row, "AllowMomo", "False"));
                IsAdxAllowed = ParseBool(Get(row, "AllowAdxx", "False"));
                IsPineAllowed = ParseBool(Get(row, "AllowPine", "False"));
                IsEsScalperAllowed = ParseBool(Get(row, "AllowEsScalper", "False"));
                IsBracketSniperAllowed = ParseBool(Get(row, "AllowBracketSniper", "False"));
                IsExpansionBotAllowed = ParseBool(Get(row, "AllowExpansionBot", "False"));
                IsCompressionBotAllowed = ParseBool(Get(row, "AllowCompressionBot", "False"));
                IsFadeBotAllowed = ParseBool(Get(row, "AllowFadeBot", "False"));
                AllowLong = ParseBool(Get(row, "AllowLong", "False"));
                AllowShort = ParseBool(Get(row, "AllowShort", "False"));
                IsMomoLongAllowed = ParseBool(Get(row, "AllowMomoLong", (IsMomoAllowed && AllowLong).ToString()));
                IsMomoShortAllowed = ParseBool(Get(row, "AllowMomoShort", (IsMomoAllowed && AllowShort).ToString()));
                StaleDataFlag = ParseBool(Get(row, "StaleDataFlag", "False"));

                if (StaleDataFlag)
                    ForceAllOff();

                CheckPipelineStatus();

                lastFileRead = fileToRead;
                lastFileWriteUtc = writeUtc;

                if (DebugPrints)
                {
                    string snapshotTimestamp = Get(row, "SnapshotTimestamp", "");
                    string macroTimestamp = Get(row, "MacroTimestamp", "");
                    string microTimestamp = Get(row, "MicroTimestamp", "");

                    Print(
                        $"{Instrument.MasterInstrument.Name} V3C HUD READ | " +
                        $"Path={fileToRead} | " +
                        $"FileModifiedLocal={File.GetLastWriteTime(fileToRead):yyyy-MM-dd HH:mm:ss} | " +
                        $"FileModifiedUtc={writeUtc:yyyy-MM-dd HH:mm:ss} | " +
                        $"SnapshotTimestamp={snapshotTimestamp} | " +
                        $"MacroTimestamp={macroTimestamp} | " +
                        $"MicroTimestamp={microTimestamp} | " +
                        $"FinalRegime={FinalRegime} | " +
                        $"MacroRegime={MacroRegime} | " +
                        $"StaleDataFlag={StaleDataFlag} | " +
                        $"StaleReason={StaleReason}");
                }
            }
            catch (Exception ex)
            {
                SetBlocked("CSV_READ_ERROR");
                if (DebugPrints)
                    Print($"{Instrument.MasterInstrument.Name} V3C side HUD read error: {ex.Message}");
            }
        }

        private Dictionary<string, string> ReadLastCsvRow(string filePath)
        {
            List<string> lines = new List<string>();

            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs))
            {
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                        lines.Add(line);
                }
            }

            if (lines.Count < 2)
                throw new Exception("CSV does not contain a data row.");

            string[] headers = SplitCsvLine(lines[0]);
            string[] values = SplitCsvLine(lines[lines.Count - 1]);

            Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < headers.Length && i < values.Length; i++)
            {
                string key = headers[i].Trim();
                if (!row.ContainsKey(key))
                    row.Add(key, values[i].Trim());
            }

            return row;
        }

        private string[] SplitCsvLine(string line)
        {
            List<string> result = new List<string>();
            bool inQuotes = false;
            string current = "";

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current += '"';
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current);
                    current = "";
                }
                else
                {
                    current += c;
                }
            }

            result.Add(current);
            return result.ToArray();
        }

        private string Get(Dictionary<string, string> row, string key, string fallback = "")
        {
            if (row != null && row.ContainsKey(key))
                return row[key];
            return fallback;
        }

        private bool ParseBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string v = value.Trim().ToLower();
            return v == "true" || v == "1" || v == "yes" || v == "y" || v == "on";
        }

        private int ParseInt(string value, int fallback)
        {
            int i;
            if (int.TryParse(value, out i))
                return i;

            double d;
            if (double.TryParse(value, out d))
                return (int)Math.Round(d);

            return fallback;
        }

        private double ParseDouble(string value, double fallback)
        {
            double d;
            if (double.TryParse(value, out d))
                return d;

            return fallback;
        }

        private void SetBlocked(string reason)
        {
            FinalRegime = "TRANSITION";
            FinalDirection = "NONE";
            ReasonCode = reason;
            StaleReason = reason;
            StaleDataFlag = true;
            RegimeConfidence = 0;
            ConflictScore = 100;
            Velocity3CP = 0.0;
            ForceAllOff();
        }

        private void ForceAllOff()
        {
            IsMomoAllowed = false;
            IsMomoLongAllowed = false;
            IsMomoShortAllowed = false;
            IsAdxAllowed = false;
            IsPineAllowed = false;
            IsEsScalperAllowed = false;
            IsBracketSniperAllowed = false;
            IsExpansionBotAllowed = false;
            IsCompressionBotAllowed = false;
            IsFadeBotAllowed = false;
            AllowLong = false;
            AllowShort = false;
        }

        private void CheckPipelineStatus()
        {
            if ((DateTime.Now - lastPipelineCheck).TotalSeconds < PipelineCheckSeconds)
                return;

            lastPipelineCheck = DateTime.Now;

            if (string.IsNullOrEmpty(liveExportFile) || !File.Exists(liveExportFile))
            {
                LiveFeedFresh = false;
                return;
            }

            double ageMinutes = (DateTime.Now - File.GetLastWriteTime(liveExportFile)).TotalMinutes;
            LiveFeedFresh = !IsRTHNow() || ageMinutes < 3.0;
        }

        private bool IsRTHNow()
        {
            DateTime now = DateTime.Now;
            int hms = now.Hour * 10000 + now.Minute * 100 + now.Second;
            return hms >= 93000 && hms <= 160000;
        }

        private void CreateWPFControls()
        {
            if (myGrid != null)
                return;

            myGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(225, 15, 15, 15)),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(20, 0, 0, 40),
                Width = 390
            };

            for (int i = 0; i < 12; i++)
                myGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            headerText = NewText($"[{Instrument.MasterInstrument.Name}->{GetLeaderSymbol(Instrument.MasterInstrument.Name)}] V3C SHADOW HUD", Brushes.White, 13, true);
            headerText.Background = Brushes.DarkSlateBlue;
            headerText.Padding = new Thickness(5);
            Grid.SetRow(headerText, 0);
            myGrid.Children.Add(headerText);

            statusText = NewText("SYSTEM: --", Brushes.White, 12, true);
            statusText.Padding = new Thickness(5);
            statusText.Margin = new Thickness(0, 5, 0, 5);
            Grid.SetRow(statusText, 1);
            myGrid.Children.Add(statusText);

            regimeText = NewText("REGIME: --", Brushes.Yellow, 13, true);
            Grid.SetRow(regimeText, 2);
            myGrid.Children.Add(regimeText);

            directionText = NewText("DIR: --", Brushes.WhiteSmoke, 11, false);
            Grid.SetRow(directionText, 3);
            myGrid.Children.Add(directionText);

            macroText = NewText("MACRO: --", Brushes.Cyan, 10, false);
            Grid.SetRow(macroText, 4);
            myGrid.Children.Add(macroText);

            hmmText = NewText("HMM: --", Brushes.Orange, 10, false);
            Grid.SetRow(hmmText, 5);
            myGrid.Children.Add(hmmText);

            reasonText = NewText("WHY: --", Brushes.LightGray, 10, false);
            Grid.SetRow(reasonText, 6);
            myGrid.Children.Add(reasonText);

            staleText = NewText("STALE: --", Brushes.LightGray, 10, false);
            Grid.SetRow(staleText, 7);
            myGrid.Children.Add(staleText);

            pipelineText = NewText("PIPELINE: --", Brushes.White, 10, true);
            pipelineText.Background = Brushes.DimGray;
            pipelineText.Padding = new Thickness(4, 2, 4, 2);
            Grid.SetRow(pipelineText, 8);
            myGrid.Children.Add(pipelineText);

            gateText = NewText("GATE: --", Brushes.White, 10, true);
            gateText.Background = Brushes.DimGray;
            gateText.Padding = new Thickness(4, 2, 4, 2);
            Grid.SetRow(gateText, 9);
            myGrid.Children.Add(gateText);

            laneText1 = NewText("LANES: --", Brushes.WhiteSmoke, 10, false);
            Grid.SetRow(laneText1, 10);
            myGrid.Children.Add(laneText1);

            laneText2 = NewText("DIR: --", Brushes.WhiteSmoke, 10, false);
            Grid.SetRow(laneText2, 11);
            myGrid.Children.Add(laneText2);

            UserControlCollection.Add(myGrid);
        }

        private TextBlock NewText(string text, Brush brush, int fontSize, bool bold)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = brush,
                FontSize = fontSize,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 1, 4, 1)
            };
        }

        private void UpdateHUD()
        {
            if (ChartControl == null || myGrid == null)
                return;

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                regimeText.Text = $"REGIME: {FinalRegime}";
                regimeText.Foreground = RegimeBrush(FinalRegime);

                directionText.Text = $"DIR: {FinalDirection} | CONF: {RegimeConfidence} | CONFLICT: {ConflictScore} | VEL: {Velocity3CP:F2}";
                macroText.Text = $"MACRO: {MacroRegime} / {MacroPlaybook}";
                hmmText.Text = $"HMM: {HMMMicro}";
                reasonText.Text = $"WHY: {ReasonCode}";
                staleText.Text = StaleDataFlag ? $"STALE: {StaleReason}" : "V3C FILE: FRESH";

                pipelineText.Text = $"PIPELINE: 1M {(LiveFeedFresh ? "OK" : "STALE")} | DATA {(StaleDataFlag ? "STALE" : "FRESH")}";
                pipelineText.Background = LiveFeedFresh && !StaleDataFlag ? Brushes.DarkGreen :
                                          !LiveFeedFresh && IsRTHNow()     ? Brushes.DarkRed :
                                                                            Brushes.DarkGoldenrod;
                pipelineText.Foreground = Brushes.White;

                string gateLabel;
                Brush gateBrush;
                if (!AsymHysteresisEnabled || AsymHysteresisReason == "COL_NOT_FOUND")
                {
                    gateLabel = AsymHysteresisReason == "COL_NOT_FOUND" ? "SCHEMA?" : "DISABLED";
                    gateBrush = Brushes.DimGray;
                }
                else if (AsymHysteresisGateOpen)
                {
                    gateLabel = "OPEN";
                    gateBrush = Brushes.DarkGreen;
                }
                else
                {
                    gateLabel = "BLOCKED";
                    gateBrush = Brushes.DarkRed;
                }

                string ageWarn = HMMStateAgeBars > 0 && HMMStateAgeBars < 2 ? " (YOUNG)" : "";
                gateText.Text = $"GATE: {gateLabel} | HMM AGE: {HMMStateAgeBars} bars{ageWarn} | {AsymHysteresisReason}";
                gateText.Background = gateBrush;
                gateText.Foreground = Brushes.White;

                laneText1.Text =
                    $"MOMO:{OnOff(IsMomoAllowed)} L:{OnOff(IsMomoLongAllowed)} S:{OnOff(IsMomoShortAllowed)} ADXX:{OnOff(IsAdxAllowed)} FADER:{OnOff(IsPineAllowed)} " +
                    $"EXP:{OnOff(IsExpansionBotAllowed)} COMP:{OnOff(IsCompressionBotAllowed)} FADE:{OnOff(IsFadeBotAllowed)}";

                laneText2.Text =
                    $"LONG:{OnOff(AllowLong)} SHORT:{OnOff(AllowShort)} SCALP:{OnOff(IsEsScalperAllowed)} SNIPER:{OnOff(IsBracketSniperAllowed)}";

                UpdateStatusVisual();
            });
        }

        private string OnOff(bool value)
        {
            return value ? "ON" : "OFF";
        }

        private Brush RegimeBrush(string regime)
        {
            if (regime == "TREND_EXPANSION") return Brushes.LimeGreen;
            if (regime == "TREND_COMPRESSION") return Brushes.DeepSkyBlue;
            if (regime == "ROTATION_LIQUID") return Brushes.Gold;
            if (regime == "ROTATION_ILLIQUID") return Brushes.Red;
            if (regime == "TRANSITION") return Brushes.OrangeRed;
            return Brushes.Gray;
        }

        private void UpdateStatusVisual()
        {
            if (statusText == null)
                return;

            if (StaleDataFlag)
            {
                statusText.Text = "V3C: STALE / BLOCKED";
                statusText.Background = Brushes.DarkRed;
                statusText.Foreground = Brushes.White;
            }
            else if (FinalRegime == "TREND_EXPANSION")
            {
                statusText.Text = "V3C: EXPANSION";
                statusText.Background = Brushes.DarkGreen;
                statusText.Foreground = Brushes.White;
            }
            else if (FinalRegime == "TREND_COMPRESSION")
            {
                statusText.Text = "V3C: COMPRESSION";
                statusText.Background = Brushes.SteelBlue;
                statusText.Foreground = Brushes.White;
            }
            else if (FinalRegime == "ROTATION_LIQUID")
            {
                statusText.Text = "V3C: LIQUID ROTATION";
                statusText.Background = Brushes.Goldenrod;
                statusText.Foreground = Brushes.Black;
            }
            else
            {
                statusText.Text = "V3C: BLOCKED / WAIT";
                statusText.Background = Brushes.DarkRed;
                statusText.Foreground = Brushes.White;
            }
        }

        private void DisposeWPFControls()
        {
            if (myGrid != null)
            {
                UserControlCollection.Remove(myGrid);
                myGrid = null;
            }
        }
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RegimeMatrixHUD_V3C[] cacheRegimeMatrixHUD_V3C;
		public RegimeMatrixHUD_V3C RegimeMatrixHUD_V3C(string dataFolderPath, bool useLeaderSymbolMapping, bool debugPrints)
		{
			return RegimeMatrixHUD_V3C(Input, dataFolderPath, useLeaderSymbolMapping, debugPrints);
		}

		public RegimeMatrixHUD_V3C RegimeMatrixHUD_V3C(ISeries<double> input, string dataFolderPath, bool useLeaderSymbolMapping, bool debugPrints)
		{
			if (cacheRegimeMatrixHUD_V3C != null)
				for (int idx = 0; idx < cacheRegimeMatrixHUD_V3C.Length; idx++)
					if (cacheRegimeMatrixHUD_V3C[idx] != null && cacheRegimeMatrixHUD_V3C[idx].DataFolderPath == dataFolderPath && cacheRegimeMatrixHUD_V3C[idx].UseLeaderSymbolMapping == useLeaderSymbolMapping && cacheRegimeMatrixHUD_V3C[idx].DebugPrints == debugPrints && cacheRegimeMatrixHUD_V3C[idx].EqualsInput(input))
						return cacheRegimeMatrixHUD_V3C[idx];
			return CacheIndicator<RegimeMatrixHUD_V3C>(new RegimeMatrixHUD_V3C(){ DataFolderPath = dataFolderPath, UseLeaderSymbolMapping = useLeaderSymbolMapping, DebugPrints = debugPrints }, input, ref cacheRegimeMatrixHUD_V3C);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RegimeMatrixHUD_V3C RegimeMatrixHUD_V3C(string dataFolderPath, bool useLeaderSymbolMapping, bool debugPrints)
		{
			return indicator.RegimeMatrixHUD_V3C(Input, dataFolderPath, useLeaderSymbolMapping, debugPrints);
		}

		public Indicators.RegimeMatrixHUD_V3C RegimeMatrixHUD_V3C(ISeries<double> input , string dataFolderPath, bool useLeaderSymbolMapping, bool debugPrints)
		{
			return indicator.RegimeMatrixHUD_V3C(input, dataFolderPath, useLeaderSymbolMapping, debugPrints);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RegimeMatrixHUD_V3C RegimeMatrixHUD_V3C(string dataFolderPath, bool useLeaderSymbolMapping, bool debugPrints)
		{
			return indicator.RegimeMatrixHUD_V3C(Input, dataFolderPath, useLeaderSymbolMapping, debugPrints);
		}

		public Indicators.RegimeMatrixHUD_V3C RegimeMatrixHUD_V3C(ISeries<double> input , string dataFolderPath, bool useLeaderSymbolMapping, bool debugPrints)
		{
			return indicator.RegimeMatrixHUD_V3C(input, dataFolderPath, useLeaderSymbolMapping, debugPrints);
		}
	}
}

#endregion
