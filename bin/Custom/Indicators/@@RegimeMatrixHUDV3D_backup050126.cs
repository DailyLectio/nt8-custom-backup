// RegimeMatrixHUD_V3D.cs
// V3D Institutional Regime Matrix — NT8 HUD Indicator
//
// PURPOSE:
//   Display device and safety interlock for the V3D regime classification
//   system. Reads NQ_RegimeMatrix_Latest.csv or ES_RegimeMatrix_Latest.csv
//   (single-row atomic files written by RegimeSupervisor_V3D.py).
//   Exposes parsed state as public properties consumed by V3D strategy bots.
//
// DESIGN RULES (from V3D_Architecture_Spec.md Section 7):
//   1. Reads ONE file, ONE row. No dictionaries. No as-of merge.
//   2. Header-driven column parsing. No positional indices.
//   3. Modification timestamp guard — minimum 15 seconds between re-reads.
//   4. No gate logic in C#. Only safety guard is && !StaleDataFlag.
//   5. MES → ES file, MNQ → NQ file.
//
// SEPARATE FROM:
//   RegimeMatrixHUD    (V3B — retired)
//   RegimeMatrixHUD_V3C (V3C — still running for comparison)

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class RegimeMatrixHUD_V3D : Indicator
    {
        // ===================================================================
        // PARKING GARAGE — concurrent ES & NQ support
        // Bots access their HUD instance via RegimeMatrixHUD_V3D.InstancesV3D["NQ"]
        // ===================================================================
        public static readonly Dictionary<string, RegimeMatrixHUD_V3D> InstancesV3D
            = new Dictionary<string, RegimeMatrixHUD_V3D>();
        private static readonly object _instanceLock = new object();

        // ===================================================================
        // PUBLIC PROPERTIES — read by all V3D bots
        // ===================================================================

        // Core regime state
        public string FinalRegime      { get; private set; } = "UNKNOWN";
        public string FinalDirection   { get; private set; } = "UNKNOWN";
        public int    RegimeConfidence { get; private set; } = 0;
        public double ConflictScore    { get; private set; } = 0.0;
        public string Phase            { get; private set; } = "UNKNOWN";
        public double Velocity3P       { get; private set; } = 0.0;
        public int    StateAgeBars     { get; private set; } = 0;
        public bool   StaleDataFlag    { get; private set; } = true;
        public string ReasonCode       { get; private set; } = "INIT";

        // Source regime layers (for display and debugging)
        public string MacroRegime          { get; private set; } = "UNKNOWN";
        public string MacroPlaybook        { get; private set; } = "UNKNOWN";
        public string MacroCheckpointState { get; private set; } = "UNKNOWN";
        public string HMMRegime            { get; private set; } = "UNKNOWN";
        public string HMMDirection         { get; private set; } = "UNKNOWN";

        // Bot permission booleans
        public bool IsExpansionAllowed { get; private set; } = false;
        public bool IsMomoAllowed      { get; private set; } = false;
        public bool IsPineAllowed      { get; private set; } = false;
        public bool IsADX_DIAllowed    { get; private set; } = false;
        public bool IsSniperAllowed    { get; private set; } = false;

        // Directional permissions
        public bool AllowLong      { get; private set; } = false;
        public bool AllowShort     { get; private set; } = false;
        public bool AllowFadeLong  { get; private set; } = false;
        public bool AllowFadeShort { get; private set; } = false;

        // Sizing scalars (0–100)
        public int ExpansionSizePct { get; private set; } = 0;
        public int MomoSizePct      { get; private set; } = 0;
        public int PineSizePct      { get; private set; } = 0;
        public int ADX_DISizePct    { get; private set; } = 0;
        public int SniperSizePct    { get; private set; } = 0;

        // ===================================================================
        // PARAMETERS
        // ===================================================================
        [NinjaScriptProperty]
        [Display(Name = "Data Folder Path", Description = "Path to V3D folder containing RegimeMatrix_Latest CSV files.",
                 GroupName = "Data Settings", Order = 0)]
        public string DataFolderPath { get; set; }
            = @"C:\Users\Valued Customer\NT8_Regimes\V3D";

        // ===================================================================
        // INTERNAL STATE
        // ===================================================================

        // File tracking
        private string   _matrixFile      = "";
        private string   _leaderSymbol    = "";
        private DateTime _lastFileCheck   = DateTime.MinValue;
        private DateTime _lastFileWriteUtc = DateTime.MinValue;
        private const int MinCheckSeconds = 15;

        // Staleness guard
        private const int MaxStateAgeMinutes = 20;
        private DateTime _lastSuccessfulReadUtc = DateTime.MinValue;

        // Safety state (separate from public StaleDataFlag — tracks parse failures)
        private bool _parseFailed  = false;
        private bool _fileMissing  = false;

        // Manual override state
        private bool _isAutoMode         = true;
        private bool _killAllActive      = false;
        private int  _overrideBarsCount  = 0;
        private const int MaxOverrideBars = 30;

        // Per-bot manual override flags (only active in manual mode)
        private bool _ovrdExpansion = false;
        private bool _ovrdMomo      = false;
        private bool _ovrdPine      = false;
        private bool _ovrdADX_DI    = false;
        private bool _ovrdSniper    = false;

        // Override log path
        private string _overrideLogPath = "";

        // ===================================================================
        // WPF UI ELEMENTS
        // ===================================================================
        private Grid        _hudGrid;
        private TextBlock   _tbHeader;
        private TextBlock   _tbStatusBar;    // AUTO/MAN | FRESH/STALE
        private TextBlock   _tbRegimeLine;   // REGIME + DIR + CONF + CONFLICT
        private TextBlock   _tbLayerLine;    // MACRO | HMM | VEL | AGE
        private TextBlock   _tbBotLine;      // bot permission summary
        private TextBlock   _tbReasonLine;   // WHY + PHASE
        private Button      _btnMode;
        private Button      _btnExpansion;
        private Button      _btnMomo;
        private Button      _btnPine;
        private Button      _btnADX_DI;
        private Button      _btnSniper;
        private Button      _btnKillAll;

        // ===================================================================
        // LIFECYCLE
        // ===================================================================

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description          = "V3D Regime Matrix HUD — display and safety interlock.";
                Name                 = "Regime Matrix HUD V3D";
                Calculate            = Calculate.OnBarClose;
                IsOverlay            = true;
                DisplayInDataBox     = false;
                PaintPriceMarkers    = false;
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.DataLoaded)
            {
                _leaderSymbol = GetLeaderSymbol(Instrument.MasterInstrument.Name);
                _matrixFile   = Path.Combine(DataFolderPath,
                    _leaderSymbol + "_RegimeMatrix_Latest.csv");
                _overrideLogPath = Path.Combine(DataFolderPath,
                    @"..\Overrides\HUD_Override_Log.csv");

                // Ensure override log directory exists
                try
                {
                    string logDir = Path.GetDirectoryName(_overrideLogPath);
                    if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                    if (!File.Exists(_overrideLogPath))
                        File.WriteAllText(_overrideLogPath,
                            "TimestampET,Symbol,Bot,OverrideState,Mode,Reason\n");
                }
                catch { /* non-fatal */ }

                // Register in parking garage
                lock (_instanceLock)
                {
                    InstancesV3D[_leaderSymbol] = this;
                }

                if (ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync(() => CreateWPFControls());
            }
            else if (State == State.Terminated)
            {
                lock (_instanceLock)
                {
                    if (InstancesV3D.ContainsKey(_leaderSymbol) &&
                        InstancesV3D[_leaderSymbol] == this)
                        InstancesV3D.Remove(_leaderSymbol);
                }

                if (ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync(() => DisposeWPFControls());
            }
        }

        // ===================================================================
        // BAR UPDATE — gate refresh, apply overrides, update display
        // ===================================================================

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            // Tick override expiry counter (only in manual mode)
            if (!_isAutoMode && !_killAllActive)
                _overrideBarsCount++;

            if (_overrideBarsCount >= MaxOverrideBars && !_isAutoMode)
            {
                // Silently revert manual overrides after MaxOverrideBars
                ResetManualOverrides();
                _overrideBarsCount = 0;
                LogOverrideEvent("ALL", false, "AUTO_EXPIRY");
            }

            // Refresh regime file (timestamp-guarded)
            RefreshRegimeState();

            // Safety: if we have a successful read but the write time is old, force stale
            CheckStaleness();

            // Apply safety layer — the only C# gate logic
            ApplySafetyGuards();

            // Apply manual overrides on top (only if not killed and in manual mode)
            if (!_isAutoMode && !_killAllActive)
                ApplyManualOverrides();

            // Refresh WPF display
            if (ChartControl != null && _hudGrid != null)
                ChartControl.Dispatcher.InvokeAsync(() => UpdateHUDDisplay());
        }

        // ===================================================================
        // FILE REFRESH — modification-timestamp guarded
        // ===================================================================

        private void RefreshRegimeState()
        {
            if ((DateTime.Now - _lastFileCheck).TotalSeconds < MinCheckSeconds)
                return;

            _lastFileCheck = DateTime.Now;

            if (!File.Exists(_matrixFile))
            {
                _fileMissing = true;
                _parseFailed = false;
                StaleDataFlag = true;
                return;
            }

            _fileMissing = false;

            try
            {
                DateTime writeTime = File.GetLastWriteTimeUtc(_matrixFile);
                if (writeTime <= _lastFileWriteUtc)
                    return;  // nothing new

                ReadLatestRow(_matrixFile);
                _lastFileWriteUtc       = writeTime;
                _lastSuccessfulReadUtc  = DateTime.UtcNow;
                _parseFailed            = false;
            }
            catch (Exception ex)
            {
                _parseFailed = true;
                Print($"[V3D HUD {_leaderSymbol}] File read error: {ex.Message}");
            }
        }

        // ===================================================================
        // CSV READER — header-driven, no positional indices
        // ===================================================================

        private void ReadLatestRow(string filePath)
        {
            string[] lines;
            using (FileStream fs = new FileStream(filePath, FileMode.Open,
                                                   FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs))
            {
                var list = new List<string>();
                string ln;
                while ((ln = sr.ReadLine()) != null)
                    if (!string.IsNullOrWhiteSpace(ln))
                        list.Add(ln);
                lines = list.ToArray();
            }

            if (lines.Length < 2) { _parseFailed = true; return; }

            // Build column index map from header
            string[] headers = lines[0].Split(',');
            var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
                col[headers[i].Trim()] = i;

            // Last data row (file is single-row but tolerate extras)
            string[] vals = lines[lines.Length - 1].Split(',');

            string Get(string name)
            {
                if (col.TryGetValue(name, out int idx) && idx < vals.Length)
                    return vals[idx].Trim();
                return "";
            }

            int GetInt(string name, int def = 0)
                => int.TryParse(Get(name), out int v) ? v : def;

            double GetDbl(string name, double def = 0.0)
                => double.TryParse(Get(name), System.Globalization.NumberStyles.Any,
                                   System.Globalization.CultureInfo.InvariantCulture,
                                   out double v) ? v : def;

            bool GetBool(string name)
                => Get(name) == "1";

            // --- Core regime state ---
            FinalRegime          = Get("FinalRegime");
            FinalDirection       = Get("FinalDirection");
            RegimeConfidence     = GetInt("RegimeConfidence");
            ConflictScore        = GetDbl("ConflictScore");
            Phase                = Get("Phase");
            Velocity3P           = GetDbl("Velocity3P_ATR");
            StateAgeBars         = GetInt("StateAgeBars");
            ReasonCode           = Get("ReasonCode");

            // Stale data flag from Python (also evaluated locally below)
            bool pythonStaleFlag  = GetBool("StaleDataFlag");

            // --- Source layers ---
            MacroRegime          = Get("MacroRegime");
            MacroPlaybook        = Get("MacroPlaybook");
            MacroCheckpointState = Get("MacroCheckpointState");
            HMMRegime            = Get("HMMRegime");
            HMMDirection         = Get("HMMDirection");

            // --- Bot permissions ---
            IsExpansionAllowed = GetBool("AllowExpansion");
            IsMomoAllowed      = GetBool("AllowMomo");
            IsPineAllowed      = GetBool("AllowPine");
            IsADX_DIAllowed    = GetBool("AllowADX_DI");
            IsSniperAllowed    = GetBool("AllowSniper");

            // --- Directional ---
            AllowLong      = GetBool("AllowLong");
            AllowShort     = GetBool("AllowShort");
            AllowFadeLong  = GetBool("AllowFadeLong");
            AllowFadeShort = GetBool("AllowFadeShort");

            // --- Sizing scalars ---
            ExpansionSizePct = GetInt("AllowExpansion_SizePct");
            MomoSizePct      = GetInt("AllowMomo_SizePct");
            PineSizePct      = GetInt("AllowPine_SizePct");
            ADX_DISizePct    = GetInt("AllowADX_DI_SizePct");
            SniperSizePct    = GetInt("AllowSniper_SizePct");

            // Combine Python stale flag with local assessment
            StaleDataFlag = pythonStaleFlag;

            if (string.IsNullOrEmpty(FinalRegime) || FinalRegime == "UNKNOWN")
                _parseFailed = true;
            else
                _parseFailed = false;
        }

        // ===================================================================
        // STALENESS CHECK — file write time vs MaxStateAgeMinutes
        // ===================================================================

        private void CheckStaleness()
        {
            // If we've never read successfully, stay stale
            if (_lastSuccessfulReadUtc == DateTime.MinValue) { StaleDataFlag = true; return; }

            double ageMinutes = (DateTime.UtcNow - _lastFileWriteUtc).TotalMinutes;
            if (ageMinutes > MaxStateAgeMinutes)
                StaleDataFlag = true;
            // Note: if Python already set StaleDataFlag=1 in the CSV, it stays true
            // from ReadLatestRow. We only upgrade here, never downgrade.
        }

        // ===================================================================
        // SAFETY GUARDS — the only C# gate logic
        // ===================================================================

        private void ApplySafetyGuards()
        {
            if (StaleDataFlag || _parseFailed || _fileMissing || _killAllActive)
            {
                string reason = _killAllActive   ? "KILL_ALL_ACTIVE" :
                                _fileMissing     ? "FILE_MISSING"    :
                                _parseFailed     ? "PARSE_FAILED"    :
                                                   "STALE_DATA";
                ForceAllOff(reason);
            }
        }

        private void ForceAllOff(string reason)
        {
            IsExpansionAllowed = IsMomoAllowed = IsPineAllowed =
            IsADX_DIAllowed    = IsSniperAllowed = false;
            AllowLong = AllowShort = AllowFadeLong = AllowFadeShort = false;
            ExpansionSizePct = MomoSizePct = PineSizePct =
            ADX_DISizePct    = SniperSizePct = 0;
        }

        // ===================================================================
        // MANUAL OVERRIDES
        // ===================================================================

        private void ApplyManualOverrides()
        {
            // In manual mode, per-bot override buttons toggle the permission booleans.
            // The safety guard already ran — we only reach here if not stale/failed/killed.
            if (_ovrdExpansion) IsExpansionAllowed = !IsExpansionAllowed;
            if (_ovrdMomo)      IsMomoAllowed      = !IsMomoAllowed;
            if (_ovrdPine)      IsPineAllowed       = !IsPineAllowed;
            if (_ovrdADX_DI)    IsADX_DIAllowed     = !IsADX_DIAllowed;
            if (_ovrdSniper)    IsSniperAllowed      = !IsSniperAllowed;

            // Overrides are one-shot toggles — clear after applying
            _ovrdExpansion = _ovrdMomo = _ovrdPine = _ovrdADX_DI = _ovrdSniper = false;
        }

        private void ResetManualOverrides()
        {
            _ovrdExpansion = _ovrdMomo = _ovrdPine = _ovrdADX_DI = _ovrdSniper = false;
        }

        // ===================================================================
        // LEADER SYMBOL MAPPING (micro → mini)
        // ===================================================================

        private string GetLeaderSymbol(string sym)
        {
            if (sym == "MES") return "ES";
            if (sym == "MNQ") return "NQ";
            if (sym == "MGC") return "GC";
            if (sym == "MCL") return "CL";
            return sym;
        }

        // ===================================================================
        // OVERRIDE LOGGING
        // ===================================================================

        private void LogOverrideEvent(string bot, bool newState, string reason)
        {
            try
            {
                string line = string.Format("{0},{1},{2},{3},{4},{5}",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    _leaderSymbol,
                    bot,
                    newState ? "ON" : "OFF",
                    _isAutoMode ? "AUTO" : "MANUAL",
                    reason);

                File.AppendAllText(_overrideLogPath, line + "\n");
            }
            catch { /* non-fatal */ }
        }

        // ===================================================================
        // WPF CONTROL CONSTRUCTION
        // ===================================================================

        private void CreateWPFControls()
        {
            if (_hudGrid != null) return;

            _hudGrid = new Grid
            {
                Background          = new SolidColorBrush(Color.FromArgb(225, 15, 15, 20)),
                VerticalAlignment   = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin              = new Thickness(10, 10, 0, 0),
                MinWidth            = 400,
            };

            // Row definitions
            for (int i = 0; i < 8; i++)
                _hudGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0 — Header bar
            _tbHeader = MakeTextBlock(
                $"[{Instrument.MasterInstrument.Name} → {_leaderSymbol}]  V3D REGIME MATRIX",
                Brushes.White, 11, FontWeights.Bold, Brushes.MidnightBlue,
                new Thickness(6, 4, 6, 4), TextAlignment.Center);
            SetRow(_tbHeader, 0);

            // Row 1 — Status bar (AUTO/MAN | FRESH/STALE)
            _tbStatusBar = MakeTextBlock("AUTO  |  CHECKING...",
                Brushes.Black, 10, FontWeights.Bold, Brushes.LimeGreen,
                new Thickness(6, 2, 6, 2), TextAlignment.Center);
            SetRow(_tbStatusBar, 1);

            // Row 2 — Regime line
            _tbRegimeLine = MakeTextBlock("REGIME: --   DIR: --  |  CONF: --  |  CONFLICT: --",
                Brushes.Cyan, 11, FontWeights.Bold, null,
                new Thickness(6, 3, 6, 1), TextAlignment.Left);
            SetRow(_tbRegimeLine, 2);

            // Row 3 — Layer line
            _tbLayerLine = MakeTextBlock("MACRO: --  |  HMM: --  |  VEL: --  |  AGE: --",
                Brushes.Orange, 10, FontWeights.Normal, null,
                new Thickness(6, 1, 6, 1), TextAlignment.Left);
            SetRow(_tbLayerLine, 3);

            // Row 4 — Bot permissions line
            _tbBotLine = MakeTextBlock("MOMO:--  EXP:--  PINE:--  ADX_DI:--  LONG:--  SHORT:--",
                Brushes.White, 10, FontWeights.Normal, null,
                new Thickness(6, 1, 6, 3), TextAlignment.Left);
            SetRow(_tbBotLine, 4);

            // Row 5 — Reason / Phase line
            _tbReasonLine = MakeTextBlock("WHY: --  |  PHASE: --",
                Brushes.LightGray, 10, FontWeights.Normal, null,
                new Thickness(6, 1, 6, 4), TextAlignment.Left);
            SetRow(_tbReasonLine, 5);

            // Row 6 — Mode + Override buttons (StackPanel)
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(4, 2, 4, 2),
            };

            _btnMode      = MakeButton("AUTO/MAN",  Brushes.DarkSlateGray,  ModeButton_Click);
            _btnExpansion = MakeButton("EXP OVRD",  Brushes.DarkOliveGreen, ExpansionOvrd_Click);
            _btnMomo      = MakeButton("MOMO OVRD", Brushes.DarkOliveGreen, MomoOvrd_Click);
            _btnPine      = MakeButton("PINE OVRD", Brushes.DarkOliveGreen, PineOvrd_Click);
            _btnADX_DI    = MakeButton("ADX OVRD",  Brushes.DarkOliveGreen, AdxDiOvrd_Click);
            _btnSniper    = MakeButton("SNP OVRD",  Brushes.DarkOliveGreen, SniperOvrd_Click);
            _btnKillAll   = MakeButton("KILL ALL",  Brushes.DarkRed,        KillAll_Click);

            buttonPanel.Children.Add(_btnMode);
            buttonPanel.Children.Add(_btnExpansion);
            buttonPanel.Children.Add(_btnMomo);
            buttonPanel.Children.Add(_btnPine);
            buttonPanel.Children.Add(_btnADX_DI);
            buttonPanel.Children.Add(_btnSniper);
            buttonPanel.Children.Add(_btnKillAll);

            Grid.SetRow(buttonPanel, 6);
            _hudGrid.Children.Add(buttonPanel);

            UserControlCollection.Add(_hudGrid);

            // Initial display paint
            UpdateHUDDisplay();
        }

        private void SetRow(UIElement el, int row)
        {
            Grid.SetRow(el, row);
            _hudGrid.Children.Add(el);
        }

        private TextBlock MakeTextBlock(string text, Brush fg, double fontSize,
            FontWeight weight, Brush bg, Thickness margin, TextAlignment align)
        {
            var tb = new TextBlock
            {
                Text          = text,
                Foreground    = fg,
                FontSize      = fontSize,
                FontWeight    = weight,
                Margin        = margin,
                TextAlignment = align,
                TextWrapping  = TextWrapping.Wrap,
            };
            if (bg != null) tb.Background = bg;
            return tb;
        }

        private Button MakeButton(string label, Brush bg, RoutedEventHandler handler)
        {
            var btn = new Button
            {
                Content   = label,
                Background = bg,
                Foreground = Brushes.White,
                Margin    = new Thickness(3, 0, 3, 0),
                Padding   = new Thickness(5, 3, 5, 3),
                FontSize  = 9,
            };
            btn.Click += handler;
            return btn;
        }

        private void DisposeWPFControls()
        {
            if (_hudGrid != null)
            {
                UserControlCollection.Remove(_hudGrid);
                _hudGrid = null;
            }
        }

        // ===================================================================
        // HUD DISPLAY UPDATE — called on UI thread via Dispatcher
        // ===================================================================

        private void UpdateHUDDisplay()
        {
            if (_hudGrid == null) return;

            // --- Status bar ---
            string modeLabel  = _isAutoMode   ? "AUTO"  : "MANUAL";
            string freshLabel = StaleDataFlag || _parseFailed || _fileMissing
                                ? "STALE" : "FRESH";
            bool   isFresh    = freshLabel == "FRESH";

            _tbStatusBar.Text       = $"{modeLabel}  |  {freshLabel}";
            _tbStatusBar.Background = _killAllActive         ? Brushes.DarkRed  :
                                      !isFresh               ? Brushes.OrangeRed :
                                      !_isAutoMode           ? Brushes.Goldenrod :
                                                               Brushes.DarkGreen;
            _tbStatusBar.Foreground = Brushes.White;

            // --- Regime line ---
            string conflictPct = (ConflictScore * 100).ToString("00");
            _tbRegimeLine.Text = $"REGIME: {FinalRegime,-22} DIR: {FinalDirection,-6}" +
                                 $"  CONF: {RegimeConfidence,3}  |  CONFLICT: {conflictPct}";
            _tbRegimeLine.Foreground = FinalRegime == "TREND_EXPANSION"   ? Brushes.LimeGreen  :
                                       FinalRegime == "TREND_COMPRESSION" ? Brushes.Cyan       :
                                       FinalRegime == "ROTATION_LIQUID"   ? Brushes.Yellow     :
                                       FinalRegime == "TRANSITION"        ? Brushes.OrangeRed  :
                                                                            Brushes.LightGray;

            // --- Layer line ---
            _tbLayerLine.Text = $"MACRO: {MacroPlaybook}  |  HMM: {HMMRegime}" +
                                $"  |  VEL: {Velocity3P:F2}  |  AGE: {StateAgeBars} bars";

            // --- Bot permissions line ---
            string Fmt(string label, bool allowed, int pct)
                => allowed ? $"{label}:ON({pct}%)" : $"{label}:OFF";

            _tbBotLine.Text = Fmt("MOMO", IsMomoAllowed, MomoSizePct) + "  " +
                              Fmt("EXP",  IsExpansionAllowed, ExpansionSizePct) + "  " +
                              Fmt("PINE", IsPineAllowed, PineSizePct) + "  " +
                              Fmt("ADX_DI", IsADX_DIAllowed, ADX_DISizePct) + "  " +
                              Fmt("SNP", IsSniperAllowed, SniperSizePct) + "  " +
                              $"LONG:{(AllowLong ? "ON" : "OFF")}  SHORT:{(AllowShort ? "ON" : "OFF")}";

            // --- Reason / Phase line ---
            _tbReasonLine.Text = $"WHY: {ReasonCode}  |  PHASE: {Phase}";

            // --- Kill-all button style ---
            _btnKillAll.Background = _killAllActive ? Brushes.Red : Brushes.DarkRed;
            _btnKillAll.Content    = _killAllActive ? "KILL (ON)" : "KILL ALL";

            // --- Mode button style ---
            _btnMode.Background = _isAutoMode ? Brushes.DarkSlateGray : Brushes.Goldenrod;
            _btnMode.Foreground = _isAutoMode ? Brushes.White          : Brushes.Black;
            _btnMode.Content    = _isAutoMode ? "AUTO/MAN" : "MAN/AUTO";
        }

        // ===================================================================
        // BUTTON HANDLERS
        // ===================================================================

        private void ModeButton_Click(object sender, RoutedEventArgs e)
        {
            _isAutoMode = !_isAutoMode;
            if (_isAutoMode)
            {
                // Returning to auto: reset all manual override state
                ResetManualOverrides();
                _killAllActive    = false;
                _overrideBarsCount = 0;
                LogOverrideEvent("ALL", false, "AUTO_MODE_RESTORED");
            }
            else
            {
                LogOverrideEvent("MODE", false, "MANUAL_MODE_ACTIVATED");
            }

            if (ChartControl != null)
                ChartControl.Dispatcher.InvokeAsync(() => UpdateHUDDisplay());
        }

        private void KillAll_Click(object sender, RoutedEventArgs e)
        {
            _killAllActive = !_killAllActive;
            if (_killAllActive)
            {
                _isAutoMode = false;  // Kill implies manual
                LogOverrideEvent("ALL", false, "KILL_ALL_ACTIVATED");
            }
            else
            {
                LogOverrideEvent("ALL", false, "KILL_ALL_RELEASED");
            }

            if (ChartControl != null)
                ChartControl.Dispatcher.InvokeAsync(() => UpdateHUDDisplay());
        }

        private void ExpansionOvrd_Click(object sender, RoutedEventArgs e)
        {
            if (_isAutoMode || _killAllActive) return;
            IsExpansionAllowed = !IsExpansionAllowed;
            if (IsExpansionAllowed) ExpansionSizePct = RegimeConfidence;
            LogOverrideEvent("EXPANSION", IsExpansionAllowed, "MANUAL_OVERRIDE");
            if (ChartControl != null)
                ChartControl.Dispatcher.InvokeAsync(() => UpdateHUDDisplay());
        }

        private void MomoOvrd_Click(object sender, RoutedEventArgs e)
        {
            if (_isAutoMode || _killAllActive) return;
            IsMomoAllowed = !IsMomoAllowed;
            if (IsMomoAllowed) MomoSizePct = RegimeConfidence;
            LogOverrideEvent("MOMO", IsMomoAllowed, "MANUAL_OVERRIDE");
            if (ChartControl != null)
                ChartControl.Dispatcher.InvokeAsync(() => UpdateHUDDisplay());
        }

        private void PineOvrd_Click(object sender, RoutedEventArgs e)
        {
            if (_isAutoMode || _killAllActive) return;
            IsPineAllowed = !IsPineAllowed;
            if (IsPineAllowed) PineSizePct = RegimeConfidence;
            LogOverrideEvent("PINE", IsPineAllowed, "MANUAL_OVERRIDE");
            if (ChartControl != null)
                ChartControl.Dispatcher.InvokeAsync(() => UpdateHUDDisplay());
        }

        private void AdxDiOvrd_Click(object sender, RoutedEventArgs e)
        {
            if (_isAutoMode || _killAllActive) return;
            IsADX_DIAllowed = !IsADX_DIAllowed;
            if (IsADX_DIAllowed) ADX_DISizePct = RegimeConfidence;
            LogOverrideEvent("ADX_DI", IsADX_DIAllowed, "MANUAL_OVERRIDE");
            if (ChartControl != null)
                ChartControl.Dispatcher.InvokeAsync(() => UpdateHUDDisplay());
        }

        private void SniperOvrd_Click(object sender, RoutedEventArgs e)
        {
            if (_isAutoMode || _killAllActive) return;
            IsSniperAllowed = !IsSniperAllowed;
            if (IsSniperAllowed) SniperSizePct = RegimeConfidence;
            LogOverrideEvent("SNIPER", IsSniperAllowed, "MANUAL_OVERRIDE");
            if (ChartControl != null)
                ChartControl.Dispatcher.InvokeAsync(() => UpdateHUDDisplay());
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RegimeMatrixHUD_V3D[] cacheRegimeMatrixHUD_V3D;
		public RegimeMatrixHUD_V3D RegimeMatrixHUD_V3D(string dataFolderPath)
		{
			return RegimeMatrixHUD_V3D(Input, dataFolderPath);
		}

		public RegimeMatrixHUD_V3D RegimeMatrixHUD_V3D(ISeries<double> input, string dataFolderPath)
		{
			if (cacheRegimeMatrixHUD_V3D != null)
				for (int idx = 0; idx < cacheRegimeMatrixHUD_V3D.Length; idx++)
					if (cacheRegimeMatrixHUD_V3D[idx] != null && cacheRegimeMatrixHUD_V3D[idx].DataFolderPath == dataFolderPath && cacheRegimeMatrixHUD_V3D[idx].EqualsInput(input))
						return cacheRegimeMatrixHUD_V3D[idx];
			return CacheIndicator<RegimeMatrixHUD_V3D>(new RegimeMatrixHUD_V3D(){ DataFolderPath = dataFolderPath }, input, ref cacheRegimeMatrixHUD_V3D);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RegimeMatrixHUD_V3D RegimeMatrixHUD_V3D(string dataFolderPath)
		{
			return indicator.RegimeMatrixHUD_V3D(Input, dataFolderPath);
		}

		public Indicators.RegimeMatrixHUD_V3D RegimeMatrixHUD_V3D(ISeries<double> input , string dataFolderPath)
		{
			return indicator.RegimeMatrixHUD_V3D(input, dataFolderPath);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RegimeMatrixHUD_V3D RegimeMatrixHUD_V3D(string dataFolderPath)
		{
			return indicator.RegimeMatrixHUD_V3D(Input, dataFolderPath);
		}

		public Indicators.RegimeMatrixHUD_V3D RegimeMatrixHUD_V3D(ISeries<double> input , string dataFolderPath)
		{
			return indicator.RegimeMatrixHUD_V3D(input, dataFolderPath);
		}
	}
}

#endregion
