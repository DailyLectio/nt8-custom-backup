// RegimeScorecardHUD.cs
// Multi-panel HUD displaying V3C ↔ V3D regime state side-by-side with the
// Three Questions live scorecard underneath. Per-chart, instrument-specific
// (place one on the NQ chart, one on the ES chart).
//
// READS (per instrument, MNQ→NQ / MES→ES routing):
//   NT8_Regimes\V3D\<INSTR>_RegimeMatrix_Latest.csv   ← V3D current state
//   NT8_Regimes\V3C\<INSTR>_Regimes_V3C_Latest.csv    ← V3C current state
//   NT8_Regimes\V3D\ThreeQ_<INSTR>_Latest.json        ← Q1/Q2/Q3 (sidecar)
//   NT8_Regimes\V3D\ThreeQ_<INSTR>_Yesterday.json     ← prior-day for deltas
//
// DESIGN RULES (mirrors RegimeMatrixHUD_V3D.cs SF-conventions):
//   1. Display-only — NO gate logic, NO bot permission changes.
//   2. Reads on a 15s minimum re-check guard; UI updates only when file mtime changes.
//   3. Per-feed stale flags; each panel greys out independently.
//   4. Header-driven CSV column parsing (no positional indices).
//   5. JSON parsed via Newtonsoft.Json.Linq.JObject (NT8 ships it).
//   6. Sub-panels toggleable via NinjaScriptProperty so layout can be trimmed.
//   7. Parking-garage Instances dict so audits can read parsed scores.
//
// SEPARATE FROM:
//   RegimeMatrixHUD_V3D (V3D safety interlock — unchanged, still authoritative
//   for bot gating).
//   RegimeMatrixHUD_V3C (V3C display — unchanged).

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
using Newtonsoft.Json.Linq;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class RegimeScorecardHUD : Indicator
    {
        // ==============================================================
        // PARKING GARAGE — one instance per instrument, exposed for audits
        // ==============================================================
        public static readonly Dictionary<string, RegimeScorecardHUD> Instances
            = new Dictionary<string, RegimeScorecardHUD>();
        private static readonly object _instanceLock = new object();

        // ==============================================================
        // PUBLIC PROPERTIES — parsed scorecard state (read-only)
        // ==============================================================
        // V3D side
        public string V3D_FinalRegime    { get; private set; } = "—";
        public string V3D_FinalDirection { get; private set; } = "—";
        public int    V3D_RegimeConfidence { get; private set; } = 0;
        public double V3D_ConflictScore  { get; private set; } = 0.0;
        public string V3D_MacroRegime    { get; private set; } = "—";
        public string V3D_HMMRegime      { get; private set; } = "—";
        public int    V3D_StateAgeBars   { get; private set; } = 0;
        public bool   V3D_Stale          { get; private set; } = true;

        // V3C side
        public string V3C_FinalRegime    { get; private set; } = "—";
        public string V3C_FinalDirection { get; private set; } = "—";
        public double V3C_ConflictScore  { get; private set; } = 0.0;
        public string V3C_MacroRegime    { get; private set; } = "—";
        public string V3C_HMM_Micro      { get; private set; } = "—";
        public int    V3C_StateAge       { get; private set; } = 0;
        public bool   V3C_Stale          { get; private set; } = true;

        // Three Questions
        public string Q1_Status   { get; private set; } = "warming";
        public double Q1_Acc      { get; private set; } = 0.0;
        public int    Q1_N        { get; private set; } = 0;
        public string Q2_Status   { get; private set; } = "warming";
        public double Q2_Aligned  { get; private set; } = 0.0;
        public double Q2_Tradable { get; private set; } = 0.0;
        public string Q3_Status   { get; private set; } = "warming";
        public bool   ThreeQ_Stale { get; private set; } = true;

        // ==============================================================
        // NINJASCRIPT PROPERTIES
        // ==============================================================
        [NinjaScriptProperty]
        [Display(Name = "NT8_Regimes Root", Description = "Project root folder.",
                 GroupName = "Paths", Order = 0)]
        public string ProjectRoot { get; set; }
            = @"C:\Users\Valued Customer\NT8_Regimes";

        [NinjaScriptProperty]
        [Display(Name = "Refresh Seconds", Description = "Min seconds between file re-reads.",
                 GroupName = "Display", Order = 10)]
        [Range(5, 120)]
        public int RefreshSeconds { get; set; } = 15;

        [NinjaScriptProperty]
        [Display(Name = "Show Consensus Bar",
                 Description = "Show the V3C↔V3D agreement/divergence indicator.",
                 GroupName = "Panels", Order = 20)]
        public bool ShowConsensusBar { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Show Bot Lanes",
                 Description = "Show the per-model bot permission summary.",
                 GroupName = "Panels", Order = 21)]
        public bool ShowBotLanes { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Show Regime Persistence",
                 Description = "Show state-age bars per model.",
                 GroupName = "Panels", Order = 22)]
        public bool ShowRegimePersistence { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Show Plan A Shadow",
                 Description = "Show the V3D Choppy gate Plan A shadow audit line. Disable after the 2026-05-27 validation gate ships or shelves.",
                 GroupName = "Panels", Order = 23)]
        public bool ShowPlanAShadow { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Show Yesterday Delta",
                 Description = "Show Δ vs yesterday's Q-scores next to today's value.",
                 GroupName = "Panels", Order = 24)]
        public bool ShowYesterdayDelta { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "HUD Width (px)",
                 Description = "Minimum width of the HUD panel.",
                 GroupName = "Display", Order = 11)]
        [Range(280, 720)]
        public int HudWidth { get; set; } = 380;

        // ==============================================================
        // INTERNAL STATE
        // ==============================================================
        private string _leaderSymbol = "";
        private string _v3dLatestPath = "";
        private string _v3cLatestPath = "";
        private string _threeQLatestPath = "";
        private string _threeQYesterdayPath = "";

        private DateTime _lastReadCheck = DateTime.MinValue;
        private DateTime _v3dMtime = DateTime.MinValue;
        private DateTime _v3cMtime = DateTime.MinValue;
        private DateTime _threeQMtime = DateTime.MinValue;
        private DateTime _yesterdayLoadedFor = DateTime.MinValue.Date;

        // Plan A shadow (V3D Latest CSV)
        private int _choppyLegacy = 0;
        private int _choppyPlanA = 0;
        private int _planAExtraBars = 0;        // running tally this session
        private string _planACounterDate = "";  // YYYY-MM-DD for reset

        // Yesterday Q snapshots (for delta arrows)
        private double _yQ1Acc = double.NaN;
        private double _yQ2Aligned = double.NaN;
        private string _yQ3Status = "—";
        private string _ydate = "—";

        // Three Questions live extras
        private int _q1LongCorrect, _q1LongN, _q1ShortCorrect, _q1ShortN, _q1RotCorrect, _q1RotN;
        private int _q2TradesOpen, _q2TradesClosed, _q2TradesTotal;
        private int _q3FullN, _q3PartN, _q3CnflN;
        private double _q3FullNet, _q3PartNet, _q3CnflNet;
        private string _threeQAsOf = "—";
        private string _threeQTradeSource = "—";
        private string _threeQComputedAt = "—";

        // ==============================================================
        // WPF UI
        // ==============================================================
        private Grid      _hudGrid;
        private TextBlock _tbHeader;
        // Side-by-side V3C / V3D
        private TextBlock _tbV3CHead, _tbV3DHead;
        private TextBlock _tbV3CBody, _tbV3DBody;
        // Consensus bar
        private TextBlock _tbConsensus;
        // Bot lanes
        private TextBlock _tbBotLanesHeader;
        private TextBlock _tbV3CBots, _tbV3DBots;
        // Persistence
        private TextBlock _tbPersistence;
        // Plan A
        private TextBlock _tbPlanA;
        // Three Questions
        private TextBlock _tbThreeQHeader;
        private Border    _bdQ1, _bdQ2, _bdQ3;
        private TextBlock _tbQ1, _tbQ2, _tbQ3;
        // Footer
        private TextBlock _tbFooter;

        // ==============================================================
        // STATE LIFECYCLE
        // ==============================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Multi-panel V3C ↔ V3D regime + Three Questions live HUD.";
                Name                     = "Regime Scorecard HUD";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = true;
                DisplayInDataBox         = false;
                PaintPriceMarkers        = false;
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.DataLoaded)
            {
                _leaderSymbol = GetLeaderSymbol(Instrument.MasterInstrument.Name);
                _v3dLatestPath = Path.Combine(ProjectRoot, "V3D",
                    _leaderSymbol + "_RegimeMatrix_Latest.csv");
                _v3cLatestPath = Path.Combine(ProjectRoot, "V3C",
                    _leaderSymbol + "_Regimes_V3C_Latest.csv");
                _threeQLatestPath = Path.Combine(ProjectRoot, "V3D",
                    "ThreeQ_" + _leaderSymbol + "_Latest.json");
                _threeQYesterdayPath = Path.Combine(ProjectRoot, "V3D",
                    "ThreeQ_" + _leaderSymbol + "_Yesterday.json");

                Print($"[ScorecardHUD {_leaderSymbol}] Paths:");
                Print($"  V3D Latest : {_v3dLatestPath}");
                Print($"  V3C Latest : {_v3cLatestPath}");
                Print($"  ThreeQ JSON: {_threeQLatestPath}");

                lock (_instanceLock) { Instances[_leaderSymbol] = this; }

                if (ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync(() => CreateWPFControls());
            }
            else if (State == State.Terminated)
            {
                lock (_instanceLock)
                {
                    if (Instances.ContainsKey(_leaderSymbol) &&
                        Instances[_leaderSymbol] == this)
                        Instances.Remove(_leaderSymbol);
                }
                if (ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync(() => DisposeWPFControls());
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;
            if ((DateTime.Now - _lastReadCheck).TotalSeconds < RefreshSeconds) return;
            _lastReadCheck = DateTime.Now;

            try { RefreshAllSources(); }
            catch (Exception ex) { Print($"[ScorecardHUD] refresh error: {ex.Message}"); }

            if (ChartControl != null && _hudGrid != null)
                ChartControl.Dispatcher.InvokeAsync(() => UpdateHUDDisplay());
        }

        // ==============================================================
        // SOURCE REFRESH — read each file only if mtime changed
        // ==============================================================
        private void RefreshAllSources()
        {
            RefreshV3D();
            RefreshV3C();
            RefreshThreeQ();
            RefreshYesterday();
        }

        private void RefreshV3D()
        {
            if (!File.Exists(_v3dLatestPath)) { V3D_Stale = true; return; }
            DateTime mt = File.GetLastWriteTimeUtc(_v3dLatestPath);
            if (mt == _v3dMtime) { CheckStaleness(); return; }
            _v3dMtime = mt;

            var row = ReadCsvSingleRow(_v3dLatestPath);
            if (row == null) { V3D_Stale = true; return; }

            V3D_FinalRegime      = GetStr(row, "FinalRegime", "—");
            V3D_FinalDirection   = GetStr(row, "FinalDirection", "—");
            V3D_RegimeConfidence = GetInt(row, "RegimeConfidence", 0);
            V3D_ConflictScore    = GetDbl(row, "ConflictScore", 0.0);
            V3D_MacroRegime      = GetStr(row, "MacroRegime", "—");
            V3D_HMMRegime        = GetStr(row, "HMMRegime", "—");
            V3D_StateAgeBars     = GetInt(row, "StateAgeBars", 0);

            // Plan A shadow accumulators
            _choppyLegacy   = GetInt(row, "ChoppyAtLegacyThreshold", 0);
            _choppyPlanA    = GetInt(row, "ChoppyAtPlanAThreshold", 0);
            int hypNoChop   = GetInt(row, "Hyp_AnyDir_if_no_choppy", 0);
            int hypAtPlanA  = GetInt(row, "Hyp_AnyDir_if_choppy_threshold_6", 0);
            // Reset session counter at date rollover; tally bars that would have
            // opened under Plan A but were blocked by legacy
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_planACounterDate != today)
            {
                _planACounterDate = today;
                _planAExtraBars = 0;
            }
            if (hypNoChop > 0 && hypAtPlanA == 0) _planAExtraBars++;

            V3D_Stale = GetStr(row, "StaleDataFlag", "0") == "1";
            CheckStaleness();
        }

        private void RefreshV3C()
        {
            if (!File.Exists(_v3cLatestPath)) { V3C_Stale = true; return; }
            DateTime mt = File.GetLastWriteTimeUtc(_v3cLatestPath);
            if (mt == _v3cMtime) return;
            _v3cMtime = mt;

            var row = ReadCsvSingleRow(_v3cLatestPath);
            if (row == null) { V3C_Stale = true; return; }

            V3C_FinalRegime    = GetStr(row, "FinalRegime", "—");
            V3C_FinalDirection = GetStr(row, "FinalDirection", "—");
            V3C_ConflictScore  = GetDbl(row, "ConflictScore", 0.0);
            V3C_MacroRegime    = GetStr(row, "MacroRegime", "—");
            V3C_HMM_Micro      = GetStr(row, "HMM_Micro", "—");
            V3C_StateAge       = GetInt(row, "HMMStateAge", 0);
            V3C_Stale          = GetStr(row, "StaleDataFlag", "0") == "1";
        }

        private void RefreshThreeQ()
        {
            if (!File.Exists(_threeQLatestPath)) { ThreeQ_Stale = true; return; }
            DateTime mt = File.GetLastWriteTimeUtc(_threeQLatestPath);
            if (mt == _threeQMtime) return;
            _threeQMtime = mt;

            try
            {
                string text = File.ReadAllText(_threeQLatestPath);
                JObject j = JObject.Parse(text);

                _threeQAsOf       = JStr(j, "as_of_checkpoint", "—");
                _threeQComputedAt = JStr(j, "computed_at", "—");
                JObject sources = j["sources"] as JObject;
                _threeQTradeSource = JStr(sources, "trade_source", "—");

                JObject q1 = j["q1"] as JObject ?? new JObject();
                JObject q2 = j["q2"] as JObject ?? new JObject();
                JObject q3 = j["q3"] as JObject ?? new JObject();

                Q1_Status      = JStr(q1, "status", "warming");
                Q1_Acc         = JDbl(q1, "acc", 0);
                Q1_N           = JInt(q1, "n", 0);
                _q1LongCorrect = JInt(q1, "long_correct", 0);
                _q1LongN       = JInt(q1, "long_n", 0);
                _q1ShortCorrect = JInt(q1, "short_correct", 0);
                _q1ShortN      = JInt(q1, "short_n", 0);
                _q1RotCorrect  = JInt(q1, "rot_correct", 0);
                _q1RotN        = JInt(q1, "rot_n", 0);

                Q2_Status      = JStr(q2, "status", "warming");
                Q2_Aligned     = JDbl(q2, "aligned_pct", 0);
                Q2_Tradable    = JDbl(q2, "tradable_pct", 0);
                _q2TradesOpen  = JInt(q2, "trades_when_open", 0);
                _q2TradesClosed = JInt(q2, "trades_when_closed", 0);
                _q2TradesTotal = JInt(q2, "trades_total", 0);

                Q3_Status      = JStr(q3, "status", "warming");
                JObject q3Full = q3["full"]     as JObject ?? new JObject();
                JObject q3Part = q3["partial"]  as JObject ?? new JObject();
                JObject q3Cnfl = q3["conflict"] as JObject ?? new JObject();
                _q3FullN       = JInt(q3Full, "n", 0);
                _q3PartN       = JInt(q3Part, "n", 0);
                _q3CnflN       = JInt(q3Cnfl, "n", 0);
                _q3FullNet     = JDbl(q3Full, "net", 0);
                _q3PartNet     = JDbl(q3Part, "net", 0);
                _q3CnflNet     = JDbl(q3Cnfl, "net", 0);

                ThreeQ_Stale = false;
            }
            catch (Exception ex)
            {
                Print($"[ScorecardHUD {_leaderSymbol}] ThreeQ parse failed: {ex.Message}");
                ThreeQ_Stale = true;
            }
        }

        private void RefreshYesterday()
        {
            // Load once per session
            if (_yesterdayLoadedFor == DateTime.Today) return;
            if (!File.Exists(_threeQYesterdayPath))
            {
                _yQ1Acc = double.NaN; _yQ2Aligned = double.NaN; _yQ3Status = "—"; _ydate = "—";
                _yesterdayLoadedFor = DateTime.Today;
                return;
            }
            try
            {
                JObject j = JObject.Parse(File.ReadAllText(_threeQYesterdayPath));
                _ydate      = JStr(j, "date", "—");
                JObject q1 = j["q1"] as JObject ?? new JObject();
                JObject q2 = j["q2"] as JObject ?? new JObject();
                JObject q3 = j["q3"] as JObject ?? new JObject();
                _yQ1Acc     = JDbl(q1, "acc", double.NaN);
                _yQ2Aligned = JDbl(q2, "aligned_pct", double.NaN);
                _yQ3Status  = JStr(q3, "status", "—");
            }
            catch { _yQ1Acc = double.NaN; _yQ2Aligned = double.NaN; _yQ3Status = "—"; }
            _yesterdayLoadedFor = DateTime.Today;
        }

        private void CheckStaleness()
        {
            // Mark V3D stale if Latest file >20 min old during RTH
            if (_v3dMtime != DateTime.MinValue)
            {
                double ageMin = (DateTime.UtcNow - _v3dMtime).TotalMinutes;
                if (ageMin > 20.0) V3D_Stale = true;
            }
        }

        // ==============================================================
        // WPF BUILD
        // ==============================================================
        private void CreateWPFControls()
        {
            if (_hudGrid != null) return;

            _hudGrid = new Grid
            {
                Background          = new SolidColorBrush(Color.FromArgb(225, 15, 15, 20)),
                VerticalAlignment   = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin              = new Thickness(0, 10, 10, 0),
                MinWidth            = HudWidth,
            };
            // Rows: header, V3C/V3D header, V3C/V3D body, consensus, bot-header,
            // bots, persistence, planA, three-Q header, Q1, Q2, Q3, footer
            for (int i = 0; i < 13; i++)
                _hudGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _tbHeader = Make($"[{Instrument.MasterInstrument.Name} → {_leaderSymbol}]  REGIME SCORECARD",
                Brushes.White, 11, FontWeights.Bold, Brushes.MidnightBlue,
                new Thickness(6, 4, 6, 4), TextAlignment.Center);
            SetRow(_tbHeader, 0);

            // Row 1 — side-by-side V3C / V3D column headers
            var headPair = TwoColGrid();
            _tbV3CHead = Make("V3C", Brushes.White, 10, FontWeights.Bold, Brushes.DarkSlateBlue,
                new Thickness(4, 2, 4, 2), TextAlignment.Center);
            _tbV3DHead = Make("V3D", Brushes.White, 10, FontWeights.Bold, Brushes.DarkGreen,
                new Thickness(4, 2, 4, 2), TextAlignment.Center);
            Grid.SetColumn(_tbV3CHead, 0); Grid.SetColumn(_tbV3DHead, 1);
            headPair.Children.Add(_tbV3CHead); headPair.Children.Add(_tbV3DHead);
            Grid.SetRow(headPair, 1); _hudGrid.Children.Add(headPair);

            // Row 2 — V3C / V3D body
            var bodyPair = TwoColGrid();
            _tbV3CBody = Make("— —\nMacro: —\nHMM: —\nConflict: —\nAge: —",
                Brushes.LightGray, 10, FontWeights.Normal, null,
                new Thickness(6, 2, 6, 4), TextAlignment.Left);
            _tbV3DBody = Make("— —\nMacro: —\nHMM: —\nConflict: —\nAge: —",
                Brushes.LightGray, 10, FontWeights.Normal, null,
                new Thickness(6, 2, 6, 4), TextAlignment.Left);
            Grid.SetColumn(_tbV3CBody, 0); Grid.SetColumn(_tbV3DBody, 1);
            bodyPair.Children.Add(_tbV3CBody); bodyPair.Children.Add(_tbV3DBody);
            Grid.SetRow(bodyPair, 2); _hudGrid.Children.Add(bodyPair);

            // Row 3 — consensus bar
            _tbConsensus = Make("[—]", Brushes.White, 10, FontWeights.Bold, Brushes.DimGray,
                new Thickness(6, 3, 6, 3), TextAlignment.Center);
            SetRow(_tbConsensus, 3);

            // Row 4 — bot lanes header
            _tbBotLanesHeader = Make("BOT LANES", Brushes.White, 9, FontWeights.Bold, Brushes.Black,
                new Thickness(6, 4, 6, 1), TextAlignment.Left);
            SetRow(_tbBotLanesHeader, 4);

            // Row 5 — bot lane content (two stacked lines, not side-by-side, for compactness)
            var botStack = new StackPanel { Orientation = Orientation.Vertical };
            _tbV3CBots = Make("V3C: —", Brushes.LightSkyBlue, 10, FontWeights.Normal, null,
                new Thickness(8, 1, 6, 1), TextAlignment.Left);
            _tbV3DBots = Make("V3D: —", Brushes.LightGreen,  10, FontWeights.Normal, null,
                new Thickness(8, 1, 6, 2), TextAlignment.Left);
            botStack.Children.Add(_tbV3CBots);
            botStack.Children.Add(_tbV3DBots);
            Grid.SetRow(botStack, 5); _hudGrid.Children.Add(botStack);

            // Row 6 — regime persistence
            _tbPersistence = Make("PERSIST  V3C: — bars · V3D: — bars",
                Brushes.LightGray, 10, FontWeights.Normal, null,
                new Thickness(6, 2, 6, 2), TextAlignment.Left);
            SetRow(_tbPersistence, 6);

            // Row 7 — Plan A shadow
            _tbPlanA = Make("PLAN A  Legacy=— · PlanA=— · +— bars opened today",
                Brushes.Khaki, 10, FontWeights.Normal, Brushes.Black,
                new Thickness(6, 2, 6, 2), TextAlignment.Left);
            SetRow(_tbPlanA, 7);

            // Row 8 — Three Questions header
            _tbThreeQHeader = Make("THREE QUESTIONS  as-of —  (—)",
                Brushes.White, 10, FontWeights.Bold, Brushes.MidnightBlue,
                new Thickness(6, 5, 6, 3), TextAlignment.Center);
            SetRow(_tbThreeQHeader, 8);

            // Rows 9-11 — Q1/Q2/Q3 panels (Border wrapping TextBlock for the colored pill background)
            _tbQ1 = MakeQText("Q1  ⏳ warming");
            _bdQ1 = MakeQBorder(_tbQ1);
            Grid.SetRow(_bdQ1, 9); _hudGrid.Children.Add(_bdQ1);

            _tbQ2 = MakeQText("Q2  ⏳ warming");
            _bdQ2 = MakeQBorder(_tbQ2);
            Grid.SetRow(_bdQ2, 10); _hudGrid.Children.Add(_bdQ2);

            _tbQ3 = MakeQText("Q3  ⏳ warming");
            _bdQ3 = MakeQBorder(_tbQ3);
            Grid.SetRow(_bdQ3, 11); _hudGrid.Children.Add(_bdQ3);

            // Row 12 — footer
            _tbFooter = Make("source: — · computed: —",
                Brushes.DimGray, 9, FontWeights.Normal, null,
                new Thickness(6, 2, 6, 4), TextAlignment.Center);
            SetRow(_tbFooter, 12);

            UserControlCollection.Add(_hudGrid);
            UpdateHUDDisplay();
        }

        private void DisposeWPFControls()
        {
            if (_hudGrid != null)
            {
                UserControlCollection.Remove(_hudGrid);
                _hudGrid = null;
            }
        }

        // ==============================================================
        // DISPLAY UPDATE
        // ==============================================================
        private void UpdateHUDDisplay()
        {
            if (_hudGrid == null) return;

            // ───── V3C / V3D body blocks ─────
            _tbV3CHead.Background = V3C_Stale
                ? new SolidColorBrush(Color.FromRgb(80, 60, 60))
                : Brushes.DarkSlateBlue;
            _tbV3DHead.Background = V3D_Stale
                ? new SolidColorBrush(Color.FromRgb(80, 60, 60))
                : Brushes.DarkGreen;

            _tbV3CBody.Foreground = V3C_Stale ? Brushes.Gray : Brushes.LightGray;
            _tbV3DBody.Foreground = V3D_Stale ? Brushes.Gray : Brushes.LightGray;

            _tbV3CBody.Text = $"{V3C_FinalRegime}  {V3C_FinalDirection}\n" +
                              $"Macro: {V3C_MacroRegime}\nHMM: {V3C_HMM_Micro}\n" +
                              $"Conflict: {V3C_ConflictScore:0.0}\nAge: {V3C_StateAge} bars";
            _tbV3DBody.Text = $"{V3D_FinalRegime}  {V3D_FinalDirection} [{V3D_RegimeConfidence}]\n" +
                              $"Macro: {V3D_MacroRegime}\nHMM: {V3D_HMMRegime}\n" +
                              $"Conflict: {V3D_ConflictScore:0.0}\nAge: {V3D_StateAgeBars} bars";

            // ───── Consensus bar ─────
            if (ShowConsensusBar)
            {
                _tbConsensus.Visibility = Visibility.Visible;
                bool sameDir = !string.IsNullOrEmpty(V3C_FinalDirection)
                            && V3C_FinalDirection == V3D_FinalDirection
                            && V3C_FinalDirection != "—";
                bool sameRegime = !string.IsNullOrEmpty(V3C_FinalRegime)
                               && V3C_FinalRegime == V3D_FinalRegime
                               && V3C_FinalRegime != "—";
                if (V3C_Stale || V3D_Stale)
                {
                    _tbConsensus.Text = "[ — STALE — ]";
                    _tbConsensus.Background = Brushes.DimGray;
                    _tbConsensus.Foreground = Brushes.White;
                }
                else if (sameDir && sameRegime)
                {
                    _tbConsensus.Text = $"▲ CONSENSUS — both {V3D_FinalRegime} {V3D_FinalDirection}";
                    _tbConsensus.Background = Brushes.DarkGreen;
                    _tbConsensus.Foreground = Brushes.White;
                }
                else if (sameDir)
                {
                    _tbConsensus.Text = $"≈ PARTIAL — same {V3D_FinalDirection}, regime differs";
                    _tbConsensus.Background = Brushes.DarkGoldenrod;
                    _tbConsensus.Foreground = Brushes.Black;
                }
                else
                {
                    _tbConsensus.Text = $"✗ DIVERGE  V3C:{V3C_FinalDirection} ↔ V3D:{V3D_FinalDirection}";
                    _tbConsensus.Background = Brushes.IndianRed;
                    _tbConsensus.Foreground = Brushes.White;
                }
            }
            else _tbConsensus.Visibility = Visibility.Collapsed;

            // ───── Bot lanes ─────
            if (ShowBotLanes)
            {
                _tbBotLanesHeader.Visibility = Visibility.Visible;
                _tbV3CBots.Visibility = Visibility.Visible;
                _tbV3DBots.Visibility = Visibility.Visible;
                _tbV3CBots.Text = "V3C: " + BuildBotLine_V3C();
                _tbV3DBots.Text = "V3D: " + BuildBotLine_V3D();
            }
            else
            {
                _tbBotLanesHeader.Visibility = Visibility.Collapsed;
                _tbV3CBots.Visibility = Visibility.Collapsed;
                _tbV3DBots.Visibility = Visibility.Collapsed;
            }

            // ───── Regime persistence ─────
            _tbPersistence.Visibility = ShowRegimePersistence ? Visibility.Visible : Visibility.Collapsed;
            if (ShowRegimePersistence)
            {
                _tbPersistence.Text =
                    $"PERSIST  V3C: {V3C_StateAge,2} bars · V3D: {V3D_StateAgeBars,2} bars";
            }

            // ───── Plan A shadow ─────
            _tbPlanA.Visibility = ShowPlanAShadow ? Visibility.Visible : Visibility.Collapsed;
            if (ShowPlanAShadow)
            {
                string legacyTag = _choppyLegacy > 0 ? "BLOCK" : "pass";
                string planATag  = _choppyPlanA  > 0 ? "BLOCK" : "pass";
                _tbPlanA.Text = $"PLAN A  Legacy={legacyTag} · PlanA={planATag} · " +
                                $"+{_planAExtraBars} bars opened if Plan A live (today)";
                _tbPlanA.Background = _planAExtraBars > 0 ? Brushes.DarkOliveGreen : Brushes.Black;
                _tbPlanA.Foreground = _planAExtraBars > 0 ? Brushes.White : Brushes.Khaki;
            }

            // ───── Three Questions ─────
            _tbThreeQHeader.Text =
                $"THREE QUESTIONS (V3D)  as-of {_threeQAsOf}  ({_threeQTradeSource})";

            ApplyQPill(_bdQ1, _tbQ1, Q1_Status, BuildQ1Text());
            ApplyQPill(_bdQ2, _tbQ2, Q2_Status, BuildQ2Text());
            ApplyQPill(_bdQ3, _tbQ3, Q3_Status, BuildQ3Text());

            // ───── Footer ─────
            _tbFooter.Text = $"source: {_threeQTradeSource} · computed: {_threeQComputedAt} · y'day: {_ydate}";
        }

        private string BuildQ1Text()
        {
            if (ThreeQ_Stale) return "Q1  ⚠ stale";
            if (Q1_Status == "warming") return $"Q1  ⏳ warming ({Q1_N} checkpoints)";
            string delta = "";
            if (ShowYesterdayDelta && !double.IsNaN(_yQ1Acc))
            {
                double d = Q1_Acc - _yQ1Acc;
                string arrow = d > 1 ? "▲" : (d < -1 ? "▼" : "▬");
                delta = $"  [{arrow}{(d >= 0 ? "+" : "")}{d:0} v y'day {_yQ1Acc:0}]";
            }
            return $"Q1  {Q1_Acc:0}%{delta}   L {_q1LongCorrect}/{_q1LongN}  S {_q1ShortCorrect}/{_q1ShortN}  R {_q1RotCorrect}/{_q1RotN}";
        }

        private string BuildQ2Text()
        {
            if (ThreeQ_Stale) return "Q2  ⚠ stale";
            if (Q2_Status == "warming") return $"Q2  ⏳ warming ({_q2TradesTotal} trades)";
            string delta = "";
            if (ShowYesterdayDelta && !double.IsNaN(_yQ2Aligned))
            {
                double d = Q2_Aligned - _yQ2Aligned;
                string arrow = d > 1 ? "▲" : (d < -1 ? "▼" : "▬");
                delta = $"  [{arrow}{(d >= 0 ? "+" : "")}{d:0} v y'day {_yQ2Aligned:0}]";
            }
            return $"Q2  {Q2_Aligned:0}% aligned{delta}   open/closed {_q2TradesOpen}/{_q2TradesClosed}  ·  tradable {Q2_Tradable:0}%";
        }

        private string BuildQ3Text()
        {
            if (ThreeQ_Stale) return "Q3  ⚠ stale";
            if (Q3_Status == "warming") return $"Q3  ⏳ warming (F:{_q3FullN}/P:{_q3PartN}/C:{_q3CnflN})";
            return $"Q3   FULL {_q3FullN} {FmtMoney(_q3FullNet)}  ·  PART {_q3PartN} {FmtMoney(_q3PartNet)}  ·  CNFL {_q3CnflN} {FmtMoney(_q3CnflNet)}";
        }

        private string FmtMoney(double v)
        {
            string sign = v < 0 ? "−$" : "+$";
            return $"{sign}{Math.Abs(v):0}";
        }

        private string BuildBotLine_V3D()
        {
            return Bot("MOMO", "AllowMomo") + " " + Bot("EXP", "AllowExpansion") + " " +
                   Bot("FDR", "AllowPine") + " " + Bot("ADX", "AllowADX_DI") + " " +
                   Bot("SNP", "AllowSniper");
        }

        private string BuildBotLine_V3C()
        {
            return BotC("MOMO", "AllowMomo") + " " + BotC("ADX", "AllowAdxx") + " " +
                   BotC("PINE", "AllowPine") + " " + BotC("EXP", "AllowExpansionBot") + " " +
                   BotC("COMP", "AllowCompressionBot") + " " + BotC("FADE", "AllowFadeBot");
        }

        // Bot helpers read the most-recent row's int flag and render ▲ or ·
        private string Bot(string label, string col)
        {
            var row = _v3dLastRow;
            if (row == null) return label + "·";
            return label + (GetInt(row, col, 0) > 0 ? "▲" : "·");
        }

        private string BotC(string label, string col)
        {
            var row = _v3cLastRow;
            if (row == null) return label + "·";
            return label + (GetInt(row, col, 0) > 0 ? "▲" : "·");
        }

        // ==============================================================
        // Q-pill rendering
        // ==============================================================
        private TextBlock MakeQText(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(6, 2, 6, 2),
                TextAlignment = TextAlignment.Left,
                TextWrapping = TextWrapping.Wrap,
            };
        }

        private Border MakeQBorder(TextBlock child)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = child,
            };
        }

        private void ApplyQPill(Border bd, TextBlock tb, string status, string text)
        {
            tb.Text = text;
            Brush bg, fg;
            switch (status)
            {
                case "green":   bg = Brushes.DarkGreen;     fg = Brushes.White; break;
                case "amber":   bg = Brushes.DarkGoldenrod; fg = Brushes.Black; break;
                case "red":     bg = Brushes.IndianRed;     fg = Brushes.White; break;
                default:        bg = new SolidColorBrush(Color.FromRgb(70, 70, 70));
                                fg = Brushes.LightGray; break;
            }
            bd.Background = bg;
            tb.Foreground = fg;
        }

        // ==============================================================
        // CSV / JSON helpers
        // ==============================================================
        private Dictionary<string, string> _v3dLastRow = null;
        private Dictionary<string, string> _v3cLastRow = null;

        private Dictionary<string, string> ReadCsvSingleRow(string path)
        {
            try
            {
                using (var sr = new StreamReader(path))
                {
                    string header = sr.ReadLine();
                    string data   = sr.ReadLine();
                    if (header == null || data == null) return null;
                    string[] hcols = SplitCsv(header);
                    string[] dcols = SplitCsv(data);
                    var dict = new Dictionary<string, string>(hcols.Length, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < hcols.Length && i < dcols.Length; i++)
                        dict[hcols[i].Trim()] = dcols[i];
                    // Stash for bot helpers
                    if (path == _v3dLatestPath) _v3dLastRow = dict;
                    if (path == _v3cLatestPath) _v3cLastRow = dict;
                    return dict;
                }
            }
            catch (Exception ex)
            {
                Print($"[ScorecardHUD] CSV read fail {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
        }

        // Minimal CSV splitter — RegimeMatrix Latest files don't contain quoted commas in the
        // columns we read, so a plain split is safe.
        private string[] SplitCsv(string line)
        {
            return line.Split(',');
        }

        // JSON helpers — defensive against null tokens and type mismatches so a
        // single corrupt field never blanks the whole panel.
        private string JStr(JObject obj, string key, string defVal)
        {
            if (obj == null) return defVal;
            JToken t;
            if (!obj.TryGetValue(key, out t) || t == null || t.Type == JTokenType.Null) return defVal;
            try { var v = (string)t; return string.IsNullOrEmpty(v) ? defVal : v; }
            catch { return defVal; }
        }

        private int JInt(JObject obj, string key, int defVal)
        {
            if (obj == null) return defVal;
            JToken t;
            if (!obj.TryGetValue(key, out t) || t == null || t.Type == JTokenType.Null) return defVal;
            try { return (int)t; } catch { return defVal; }
        }

        private double JDbl(JObject obj, string key, double defVal)
        {
            if (obj == null) return defVal;
            JToken t;
            if (!obj.TryGetValue(key, out t) || t == null || t.Type == JTokenType.Null) return defVal;
            try { return (double)t; } catch { return defVal; }
        }

        private string GetStr(Dictionary<string, string> row, string key, string defVal)
        {
            string v;
            if (row != null && row.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v.Trim();
            return defVal;
        }

        private int GetInt(Dictionary<string, string> row, string key, int defVal)
        {
            string v;
            if (row != null && row.TryGetValue(key, out v))
            {
                int r;
                if (int.TryParse(v.Trim(), out r)) return r;
            }
            return defVal;
        }

        private double GetDbl(Dictionary<string, string> row, string key, double defVal)
        {
            string v;
            if (row != null && row.TryGetValue(key, out v))
            {
                double r;
                if (double.TryParse(v.Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out r)) return r;
            }
            return defVal;
        }

        // ==============================================================
        // Layout helpers
        // ==============================================================
        private Grid TwoColGrid()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }

        private void SetRow(UIElement el, int row)
        {
            Grid.SetRow(el, row);
            _hudGrid.Children.Add(el);
        }

        private TextBlock Make(string text, Brush fg, double fontSize, FontWeight weight,
            Brush bg, Thickness margin, TextAlignment align)
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

        private string GetLeaderSymbol(string sym)
        {
            if (sym == "MES") return "ES";
            if (sym == "MNQ") return "NQ";
            return sym;
        }
    }
}
