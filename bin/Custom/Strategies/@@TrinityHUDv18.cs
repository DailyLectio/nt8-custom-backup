#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using WPFBrushes = System.Windows.Media.Brushes;
using System.Windows.Threading;
#endregion

// CC BY-NC 4.0
// Strategy: TrinityHUDv18
// Updates:
// 1. DROPDOWN FIX: Comboboxes now properly clear and repopulate when levels (like R8) are updated.
// 2. THEME TOGGLE: Added UI Theme toggle (Dark/Light) for visibility on white charts.
// 3. RATE LIMIT FIX: Implemented SafeSetStopLoss memory dictionary to prevent order spamming and ban errors.
// 4. v18 MACRO UI: Added Toggles for Yesterday, ON, and Today. Cleaned up line properties to Stroke objects.

namespace NinjaTrader.NinjaScript.Strategies
{
    public class TrinityHUDv18 : Strategy
    {
        public enum SyncModeType { Standalone, Sender, Receiver }

		// GLOBAL CLOUD MEMORY (Shared across all charts instantly)
		// FIX: Lock object for thread-safe cross-chart syncing
		private static readonly object mapLock = new object(); 
		private static Dictionary<string, double> SharedLevelMap = new Dictionary<string, double>();
		private static List<string> SharedOrderedLevelNames = new List<string>();
        
        //    1. CORE LEVELS
        // =========================================================
        [NinjaScriptProperty, Display(Name = "B8 (Ext High)", GroupName = "1. Core Levels", Order = 0)] public double L_B8 { get; set; }
        [NinjaScriptProperty, Display(Name = "B7", GroupName = "1. Core Levels", Order = 1)] public double L_B7 { get; set; }
        [NinjaScriptProperty, Display(Name = "B6", GroupName = "1. Core Levels", Order = 2)] public double L_B6 { get; set; }
        [NinjaScriptProperty, Display(Name = "B5", GroupName = "1. Core Levels", Order = 3)] public double L_B5 { get; set; }
        [NinjaScriptProperty, Display(Name = "B4", GroupName = "1. Core Levels", Order = 4)] public double L_B4 { get; set; }
        [NinjaScriptProperty, Display(Name = "B3", GroupName = "1. Core Levels", Order = 5)] public double L_B3 { get; set; }
        [NinjaScriptProperty, Display(Name = "B2", GroupName = "1. Core Levels", Order = 6)] public double L_B2 { get; set; }
        [NinjaScriptProperty, Display(Name = "B1", GroupName = "1. Core Levels", Order = 7)] public double L_B1 { get; set; }
        [NinjaScriptProperty, Display(Name = "POC (Median)", GroupName = "1. Core Levels", Order = 8)] public double L_POC { get; set; }
        [NinjaScriptProperty, Display(Name = "R1", GroupName = "1. Core Levels", Order = 9)] public double L_R1 { get; set; }
        [NinjaScriptProperty, Display(Name = "R2", GroupName = "1. Core Levels", Order = 10)] public double L_R2 { get; set; }
        [NinjaScriptProperty, Display(Name = "R3", GroupName = "1. Core Levels", Order = 11)] public double L_R3 { get; set; }
        [NinjaScriptProperty, Display(Name = "R4", GroupName = "1. Core Levels", Order = 12)] public double L_R4 { get; set; }
        [NinjaScriptProperty, Display(Name = "R5", GroupName = "1. Core Levels", Order = 13)] public double L_R5 { get; set; }
        [NinjaScriptProperty, Display(Name = "R6", GroupName = "1. Core Levels", Order = 14)] public double L_R6 { get; set; }
        [NinjaScriptProperty, Display(Name = "R7", GroupName = "1. Core Levels", Order = 15)] public double L_R7 { get; set; }
        [NinjaScriptProperty, Display(Name = "R8 (Ext Low)", GroupName = "1. Core Levels", Order = 16)] public double L_R8 { get; set; }

        // =========================================================
        //    1A. VISIBILITY & LINE STYLES
        // =========================================================
        [NinjaScriptProperty, Display(Name="Show Yesterday", GroupName="1A. Visibility", Order=1)] public bool ShowYesterday { get; set; }
        [NinjaScriptProperty, Display(Name="Show Overnight", GroupName="1A. Visibility", Order=2)] public bool ShowOvernight { get; set; }
        [NinjaScriptProperty, Display(Name="Show Today Core", GroupName="1A. Visibility", Order=3)] public bool ShowToday { get; set; }

        [NinjaScriptProperty, Display(Name="Bull Extreme (B5-B8)", GroupName="1A. Line Styles", Order=4)] public Stroke Stroke_BullExtreme { get; set; }
        [NinjaScriptProperty, Display(Name="Bull Expected (B3-B4)", GroupName="1A. Line Styles", Order=5)] public Stroke Stroke_BullExpected { get; set; }
        [NinjaScriptProperty, Display(Name="Bear Expected (R3-R4)", GroupName="1A. Line Styles", Order=6)] public Stroke Stroke_BearExpected { get; set; }
        [NinjaScriptProperty, Display(Name="Bear Extreme (R5-R8)", GroupName="1A. Line Styles", Order=7)] public Stroke Stroke_BearExtreme { get; set; }
        [NinjaScriptProperty, Display(Name="Mid Levels (B1/B2/R1/R2)", GroupName="1A. Line Styles", Order=8)] public Stroke Stroke_Mids { get; set; }
        [NinjaScriptProperty, Display(Name="Value Area (VAH/VAL)", GroupName="1A. Line Styles", Order=9)] public Stroke Stroke_ValueArea { get; set; }
        [NinjaScriptProperty, Display(Name="POC", GroupName="1A. Line Styles", Order=10)] public Stroke Stroke_POC { get; set; }
        [NinjaScriptProperty, Display(Name="Overnight", GroupName="1A. Line Styles", Order=11)] public Stroke Stroke_Overnight { get; set; }
        [NinjaScriptProperty, Display(Name="Yesterday", GroupName="1A. Line Styles", Order=12)] public Stroke Stroke_Yesterday { get; set; }
		
		// =========================================================
        //    1B. SESSION & PROFILE LEVELS
        // =========================================================
        [NinjaScriptProperty, Display(Name = "Yest PDC", GroupName = "1B. Session Levels", Order = 0)] public double L_PDC { get; set; }
        [NinjaScriptProperty, Display(Name = "Yest PDH", GroupName = "1B. Session Levels", Order = 1)] public double L_PDH { get; set; }
        [NinjaScriptProperty, Display(Name = "Yest PDL", GroupName = "1B. Session Levels", Order = 2)] public double L_PDL { get; set; }
        [NinjaScriptProperty, Display(Name = "Yest VAH", GroupName = "1B. Session Levels", Order = 3)] public double L_VAH { get; set; }
        [NinjaScriptProperty, Display(Name = "Yest VAL", GroupName = "1B. Session Levels", Order = 4)] public double L_VAL { get; set; }
        [NinjaScriptProperty, Display(Name = "Yest POC", GroupName = "1B. Session Levels", Order = 5)] public double L_YestPOC { get; set; }
        [NinjaScriptProperty, Display(Name = "Yest VWAP", GroupName = "1B. Session Levels", Order = 6)] public double L_YestVWAP { get; set; }
        
        [NinjaScriptProperty, Display(Name = "ON High", GroupName = "1B. Session Levels", Order = 7)] public double L_ONH { get; set; }
        [NinjaScriptProperty, Display(Name = "ON Low", GroupName = "1B. Session Levels", Order = 8)] public double L_ONL { get; set; }
        [NinjaScriptProperty, Display(Name = "ON Mid", GroupName = "1B. Session Levels", Order = 9)] public double L_ON_MID { get; set; }
        [NinjaScriptProperty, Display(Name = "ON VWAP", GroupName = "1B. Session Levels", Order = 10)] public double L_ON_VWAP { get; set; }
        
        [NinjaScriptProperty, Display(Name = "Open (9:30)", GroupName = "1B. Session Levels", Order = 11)] public double L_OPEN { get; set; }
        [NinjaScriptProperty, Display(Name = "ORH 5m", GroupName = "1B. Session Levels", Order = 12)] public double L_ORH_5 { get; set; }
        [NinjaScriptProperty, Display(Name = "ORL 5m", GroupName = "1B. Session Levels", Order = 13)] public double L_ORL_5 { get; set; }
        [NinjaScriptProperty, Display(Name = "ORM 5m", GroupName = "1B. Session Levels", Order = 14)] public double L_ORM_5 { get; set; }
        [NinjaScriptProperty, Display(Name = "ORH 30m", GroupName = "1B. Session Levels", Order = 15)] public double L_ORH_30 { get; set; }
        [NinjaScriptProperty, Display(Name = "ORL 30m", GroupName = "1B. Session Levels", Order = 16)] public double L_ORL_30 { get; set; }
        [NinjaScriptProperty, Display(Name = "IBH (10:30)", GroupName = "1B. Session Levels", Order = 17)] public double L_IBH { get; set; }
        [NinjaScriptProperty, Display(Name = "IBL (10:30)", GroupName = "1B. Session Levels", Order = 18)] public double L_IBL { get; set; }

        [NinjaScriptProperty, Display(Name = "Confluence Highlight (Ticks)", GroupName = "1B. Session Levels", Order = 100)] 
        public int ConfluenceTicks { get; set; } = 8;

        // PERFORMANCE
        [NinjaScriptProperty, Display(Name = "Daily Profit Goal ($)", GroupName = "7. Performance")] public double ProfitGoal { get; set; } = 1500;
        [NinjaScriptProperty, Display(Name = "Daily Max Loss ($)", GroupName = "7. Performance")] public double DailyMaxLoss { get; set; } = 1000;

        [NinjaScriptProperty, Display(Name = "Dark Theme (For Black Charts)", GroupName = "8. UI Settings", Order = 1)] 
        public bool UseDarkTheme { get; set; } = true;
        
        [NinjaScriptProperty, Display(Name = "Show HUD (WPF & Text)", GroupName = "8. UI Settings", Order = 2)] 
        public bool ShowHUD { get; set; } = true;

        [NinjaScriptProperty, Display(Name = "Sync Mode", GroupName = "9. Global Sync", Order = 1)] 
        public SyncModeType SyncMode { get; set; } = SyncModeType.Standalone;
		
		// =========================================================
        //    1C. STRATEGY PARAMETERS (SCALP & CORE)
        // =========================================================
        [NinjaScriptProperty, Display(Name="S_Qty1", GroupName="2. Scalp Tactics")] public int S_Qty1 { get; set; }
        [NinjaScriptProperty, Display(Name="S_Leg1TargetTicks", GroupName="2. Scalp Tactics")] public int S_Leg1TargetTicks { get; set; }
        [NinjaScriptProperty, Display(Name="S_Qty2", GroupName="2. Scalp Tactics")] public int S_Qty2 { get; set; }
        [NinjaScriptProperty, Display(Name="S_Leg2TargetAtrMult", GroupName="2. Scalp Tactics")] public double S_Leg2TargetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name="S_Leg2TrailMode", GroupName="2. Scalp Tactics")] public TrailMode S_Leg2TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name="S_Leg2BarN", GroupName="2. Scalp Tactics")] public int S_Leg2BarN { get; set; }
        [NinjaScriptProperty, Display(Name="S_Qty3", GroupName="2. Scalp Tactics")] public int S_Qty3 { get; set; }
        [NinjaScriptProperty, Display(Name="S_Leg3TargetAtrMult", GroupName="2. Scalp Tactics")] public double S_Leg3TargetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name="S_Leg3TrailMode", GroupName="2. Scalp Tactics")] public TrailMode S_Leg3TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name="S_Leg3RatchetAtrMult", GroupName="2. Scalp Tactics")] public double S_Leg3RatchetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name="S_Qty4", GroupName="2. Scalp Tactics")] public int S_Qty4 { get; set; }
        [NinjaScriptProperty, Display(Name="S_Leg4TargetAtrMult", GroupName="2. Scalp Tactics")] public double S_Leg4TargetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name="S_Leg4TrailMode", GroupName="2. Scalp Tactics")] public TrailMode S_Leg4TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name="S_Leg4RatchetAtrMult", GroupName="2. Scalp Tactics")] public double S_Leg4RatchetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name="S_InitialStopAtrMult", GroupName="2. Scalp Tactics")] public double S_InitialStopAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name="S_InitialStopTicks", GroupName="2. Scalp Tactics")] public int S_InitialStopTicks { get; set; }

        [NinjaScriptProperty, Display(Name="C_Qty1", GroupName="3. Core Tactics")] public int C_Qty1 { get; set; }
        [NinjaScriptProperty, Display(Name="C_Leg1TrailMode", GroupName="3. Core Tactics")] public TrailMode C_Leg1TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name="C_Leg1RatchetAtrMult", GroupName="3. Core Tactics")] public double C_Leg1RatchetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name="C_Qty2", GroupName="3. Core Tactics")] public int C_Qty2 { get; set; }
        [NinjaScriptProperty, Display(Name="C_Leg2TrailMode", GroupName="3. Core Tactics")] public TrailMode C_Leg2TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name="C_Leg2BarN", GroupName="3. Core Tactics")] public int C_Leg2BarN { get; set; }
        [NinjaScriptProperty, Display(Name="C_Qty3", GroupName="3. Core Tactics")] public int C_Qty3 { get; set; }
        [NinjaScriptProperty, Display(Name="C_Leg3TrailMode", GroupName="3. Core Tactics")] public TrailMode C_Leg3TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name="C_Leg3RatchetAtrMult", GroupName="3. Core Tactics")] public double C_Leg3RatchetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name="C_Qty4", GroupName="3. Core Tactics")] public int C_Qty4 { get; set; }
        [NinjaScriptProperty, Display(Name="C_Leg4TrailMode", GroupName="3. Core Tactics")] public TrailMode C_Leg4TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name="C_Leg4RatchetAtrMult", GroupName="3. Core Tactics")] public double C_Leg4RatchetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name="C_InitialStopAtrMult", GroupName="3. Core Tactics")] public double C_InitialStopAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name="C_InitialStopTicks", GroupName="3. Core Tactics")] public int C_InitialStopTicks { get; set; }
        [NinjaScriptProperty, Display(Name="CoreTargetOffsetTicks", GroupName="3. Core Tactics")] public int CoreTargetOffsetTicks { get; set; }

        [NinjaScriptProperty, Display(Name="ContextMinutes", GroupName="4. Context")] public int ContextMinutes { get; set; }
        [NinjaScriptProperty, Display(Name="BreakoutOffsetTicks", GroupName="4. Context")] public int BreakoutOffsetTicks { get; set; }
        [NinjaScriptProperty, Display(Name="SmartAllocTicks", GroupName="4. Context")] public int SmartAllocTicks { get; set; }
        [NinjaScriptProperty, Display(Name="ManualStopBufferTicks", GroupName="5. Manual")] public int ManualStopBufferTicks { get; set; }
        [NinjaScriptProperty, Display(Name="PlaybookStopTicks", GroupName="5. Manual")] public int PlaybookStopTicks { get; set; }
        [NinjaScriptProperty, Display(Name="BreakevenOffsetTicks", GroupName="5. Manual")] public int BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty, Display(Name="SafeChopLimit", GroupName="6. Safety")] public double SafeChopLimit { get; set; }
        [NinjaScriptProperty, Display(Name="SafeAdxLimit", GroupName="6. Safety")] public double SafeAdxLimit { get; set; }
        [NinjaScriptProperty, Display(Name="SafeRVolPeriod", GroupName="6. Safety")] public int SafeRVolPeriod { get; set; }
// =========================================================
        //    2. LOGIC VARIABLES
        // =========================================================

        private ATR atrAlgo;
        
        private double sessionVol = 0;
        private double sessionPV = 0;
        private double currentVwap = 0;
        private double currentChartPrice = 0;

        public enum TrailMode { None, BarNTrail, AtrRatchet }
        private int entryBar = -1;
        private double highSinceEntry = double.MinValue;
        private double lowSinceEntry = double.MaxValue;
        private double sessionStartCumProfit = 0;
        private double dailyPnL = 0;
        private bool isGoalReached = false;
        private bool isCombosPopulated = false;

        private int startTrades = 0, startWins = 0, startLosses = 0;
        private int sessionTrades = 0, sessionWins = 0, sessionLosses = 0;

        // RATE LIMIT FIX - STOP MEMORY
        private Dictionary<string, double> lastStopPrices = new Dictionary<string, double>();

        // UI ELEMENTS
        private Grid chartGrid, mainPanel;
        private System.Windows.Controls.Grid visibilityGrid;
        private System.Windows.Controls.Button btnToggleYest, btnToggleON, btnToggleToday;
        
        private Button btnResetLevels;
        private Button btnScalpL, btnScalpS, btnCoreL, btnCoreS;
        private Button btnManScalpL, btnManScalpS, btnNextBarL, btnNextBarS; 
        private Button btnHalfRisk, btnBreakeven, btnDisarm, btnCloseOne, btnFlatten;
        private Button btnManualL, btnManualS;
        private Button btnPxC, btnPxUp, btnPxDn; 
        
        private CheckBox chkSafeChop, chkSafeAdx, chkSafeEma, chkSafeVwap, chkSafeRVol;
        // Trinity Trader v1 Algorithmic Bias & Setup Tracking
        private double open3AM = 0.0;
        private double open830AM = 0.0;
        private double open930AM = 0.0;
        public string CurrentDailyBias = "Pending";
        private string selectedBiasPlaybookL = "None";
        private string selectedBiasPlaybookS = "None";

        private ComboBox cbLongPlays, cbShortPlays; 
        private TextBox txtEntryPrice; 
        private ComboBox boxT1, boxT2, boxT3, boxT4;
        private Label lblStatus, lblPnL;

        private DispatcherTimer flashTimer;
        private bool flashState = false;

        private SharpDX.DirectWrite.TextFormat dxTextFormatCenter;
        private SharpDX.DirectWrite.TextFormat dxTextFormatLeft;
        private SharpDX.Direct2D1.SolidColorBrush dxBrushWhite;
        private SharpDX.Direct2D1.SolidColorBrush dxBrushRed;
        private SharpDX.Direct2D1.SolidColorBrush dxBrushHUDPlan;
        private System.Windows.Media.FontFamily modernFont = new System.Windows.Media.FontFamily("Segoe UI");
        
        // FLAGS
        private volatile bool isArmedScalpLong = false, isArmedScalpShort = false;
        private volatile bool isArmedCoreLong = false, isArmedCoreShort = false;
        private volatile bool isManualPendingL = false, isManualPendingS = false;
        private volatile bool isManScalpPendingL = false, isManScalpPendingS = false;
        private volatile bool isNextBarPendingL = false, isNextBarPendingS = false;
        private volatile bool isHalfRiskPending = false, isBreakevenPending = false, isFlattenPending = false, isCloseOnePending = false;
        private volatile bool isPlaybookActive = false; 
		private volatile bool isResetPending = false;
        
        private string zoneName = "WAITING";
        private string activeStatus = "STANDBY";
        private string allocStatus = "";
        private bool levelsValid = true;
        private double levelAbove = 0, levelBelow = 0;
        private string hud_LongPlan = "", hud_ShortPlan = "";

        private double manT1, manT2, manT3, manT4, manEntryPx;
        private bool isManualActive = false;
        private double userOverrideStopPrice = 0;
        private double masterStopPrice = 0; 
        
        private List<string> activeLegs = new List<string>();

        private Dictionary<string, double> levelMap = new Dictionary<string, double>();
        private List<KeyValuePair<string, double>> orderedActiveLevels = new List<KeyValuePair<string, double>>();
        private List<string> orderedLevelNames = new List<string>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Trinity HUD v18";
                Calculate = Calculate.OnPriceChange;
                EntriesPerDirection = 5;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsOverlay = true;

                S_Qty1 = 2; S_Leg1TargetTicks = 40;
                S_Qty2 = 1; S_Leg2TargetAtrMult = 0.88; S_Leg2TrailMode = TrailMode.BarNTrail; S_Leg2BarN = 2;
                S_Qty3 = 1; S_Leg3TargetAtrMult = 1.5; S_Leg3TrailMode = TrailMode.AtrRatchet; S_Leg3RatchetAtrMult = 1.5;
                S_Qty4 = 0; S_Leg4TargetAtrMult = 2.0; S_Leg4TrailMode = TrailMode.AtrRatchet; S_Leg4RatchetAtrMult = 1.5;
                S_InitialStopAtrMult = 0.75; S_InitialStopTicks = 35;

                C_Qty1 = 1; C_Leg1TrailMode = TrailMode.None; 
                C_Qty2 = 1; C_Leg2TrailMode = TrailMode.BarNTrail; C_Leg2BarN = 2;
                C_Qty3 = 1; C_Leg3TrailMode = TrailMode.AtrRatchet; C_Leg3RatchetAtrMult = 1.5;
                C_Qty4 = 1; C_Leg4TrailMode = TrailMode.AtrRatchet; C_Leg4RatchetAtrMult = 2.0;
                C_InitialStopAtrMult = 0.75; C_InitialStopTicks = 35; 
                CoreTargetOffsetTicks = 0;

                ContextMinutes = 1; BreakoutOffsetTicks = 1; SmartAllocTicks = 160; 
                ManualStopBufferTicks = 10; PlaybookStopTicks = 12; BreakevenOffsetTicks = 5;
                
                // --- Trinity HUD v18 Defaults ---
                SafeChopLimit = 60;
                SafeAdxLimit = 20;
                SafeRVolPeriod = 20;

                ShowYesterday = true; ShowOvernight = true; ShowToday = true;
                Stroke_BullExtreme  = new Stroke(WPFBrushes.Cyan, DashStyleHelper.Solid, 2);
                Stroke_BullExpected = new Stroke(WPFBrushes.LimeGreen, DashStyleHelper.Solid, 2);
                Stroke_BearExpected = new Stroke(WPFBrushes.Red, DashStyleHelper.Solid, 2);
                Stroke_BearExtreme  = new Stroke(WPFBrushes.Magenta, DashStyleHelper.Solid, 2);
                Stroke_Mids         = new Stroke(WPFBrushes.SlateGray, DashStyleHelper.Dash, 1);
                Stroke_ValueArea    = new Stroke(WPFBrushes.DodgerBlue, DashStyleHelper.Solid, 2);
                Stroke_POC          = new Stroke(WPFBrushes.Blue, DashStyleHelper.Solid, 2);
                Stroke_Overnight    = new Stroke(WPFBrushes.DarkOrange, DashStyleHelper.Dash, 2);
                Stroke_Yesterday    = new Stroke(WPFBrushes.DimGray, DashStyleHelper.Dot, 1);
            }
            else if (State == State.Configure) { AddDataSeries(BarsPeriodType.Minute, ContextMinutes); }
            else if (State == State.DataLoaded) { 

                atrAlgo = ATR(14); 

            }
            else if (State == State.Historical)
            {
                if (ChartControl!= null) ChartControl.Dispatcher.InvokeAsync(() => { 
                    CreateWPFControls(); 
                    CreateVisibilityToggles(); // v18 buttons
                    StartFlashTimer(); 
                });
            }
            else if (State == State.Terminated)
            {
                if (ChartControl!= null) ChartControl.Dispatcher.InvokeAsync(() => {
                    DisposeWPFControls();
                    if (visibilityGrid!= null && chartGrid!= null) chartGrid.Children.Remove(visibilityGrid); // Cleanup
                });
                if (dxTextFormatCenter!= null) dxTextFormatCenter.Dispose();
                if (dxTextFormatLeft!= null) dxTextFormatLeft.Dispose();
                if (dxBrushWhite!= null) dxBrushWhite.Dispose();
                if (dxBrushRed!= null) dxBrushRed.Dispose();
                if (dxBrushHUDPlan!= null) dxBrushHUDPlan.Dispose();
                if (flashTimer!= null) flashTimer.Stop();
            }
        }
        private double Rnd(double val) { return Instrument.MasterInstrument.RoundToTickSize(val); }

        private void SafeSetStopLoss(string signal, double price)
        {
            if (string.IsNullOrEmpty(signal)) return;
            price = Rnd(price);
            // Only update the broker if the price has genuinely changed (stops the rate-limit loop ban)
            if (!lastStopPrices.ContainsKey(signal) || Math.Abs(lastStopPrices[signal] - price) > (TickSize / 2.0))
            {
                SetStopLoss(signal, CalculationMode.Price, price, false);
                lastStopPrices[signal] = price;
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time) { if (State == State.Realtime) UpdateUI(); }
        
        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition) 
        { 
            if (State == State.Realtime) {
                sessionTrades = SystemPerformance.AllTrades.Count - startTrades;
                sessionWins = SystemPerformance.AllTrades.WinningTrades.Count - startWins;
                sessionLosses = SystemPerformance.AllTrades.LosingTrades.Count - startLosses;
                UpdateUI(); 
            }
        }

     protected override void OnBarUpdate()
        {
            try
            {
                if (CurrentBar < 20) return;

                currentChartPrice = Close[0];

                if (Bars.IsFirstBarOfSession) { 
                    sessionVol = 0; sessionPV = 0; 
                    startTrades = SystemPerformance.AllTrades.Count;
                    startWins = SystemPerformance.AllTrades.WinningTrades.Count;
                    startLosses = SystemPerformance.AllTrades.LosingTrades.Count;
                }
                sessionVol += Volume[0]; sessionPV += Volume[0] * ((High[0] + Low[0] + Close[0]) / 3.0);
                if (sessionVol > 0) currentVwap = sessionPV / sessionVol;
                
                // --- TRINITY V1 DAILY BIAS LOGIC ---
                int currentTime = ToTime(Time[0]);
                
                if (currentTime == 30000 && open3AM == 0) open3AM = Open[0];
                if (currentTime == 83000 && open830AM == 0) open830AM = Open[0];
                
                if (currentTime == 93000 && open930AM == 0) 
                {
                    open930AM = Open[0];
                    
                    // Automatically calculate the Daily Bias Shape
                    if (SharedLevelMap.ContainsKey("L_VAH") && SharedLevelMap.ContainsKey("L_VAL"))
                    {
                        double vah = SharedLevelMap["L_VAH"];
                        double val = SharedLevelMap["L_VAL"];
                
                        if (open930AM > vah)
                        {
                            CurrentDailyBias = "P-Shape (Bull Trend)";
                            HUDMessenger.CurrentDailyBias = "P"; 
                        }
                        else if (open930AM < val)
                        {
                            CurrentDailyBias = "b-Shape (Bear Trend)";
                            HUDMessenger.CurrentDailyBias = "b"; 
                        }
                        else if (open930AM <= vah && open930AM >= val)
                        {
                            CurrentDailyBias = "D-Shape (Rotation)";
                            HUDMessenger.CurrentDailyBias = "D";
                        }
                    }
                }
                // --- END TRINITY V1 DAILY BIAS LOGIC ---

                // --- GLOBAL SYNC & LEVEL BUILDER ---
                if (SyncMode == SyncModeType.Receiver) {
                    BuildLevelMap(); // Receivers constantly pull latest lines from the cloud
                } else if (levelMap.Count == 0 ||!isCombosPopulated) { 
                    BuildLevelMap(); if (orderedLevelNames.Count > 0) { PopulateCombos(); isCombosPopulated = true; } 
                }

                // Logic Gate: If levels are invalid, we update the status but stay ENABLED.
				if (!levelsValid && SyncMode != SyncModeType.Receiver) 
				{ 
				    activeStatus = "FIX LEVELS (Input Error)";
				    // We do NOT return or disarm here, allowing the HUD to stay visible.
				}
				                
                if (CurrentBar >= 1) DrawCoreLines();

                if (Bars.IsFirstBarOfSession) { sessionStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit; isGoalReached = false; }
                dailyPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;
                
                if (dailyPnL >= ProfitGoal &&!isGoalReached) { isGoalReached = true; activeStatus = "GOAL REACHED"; Disarm(false); }
                if (dailyPnL <= -DailyMaxLoss) { activeStatus = "MAX LOSS HIT"; Disarm(false); if (Position.MarketPosition!= MarketPosition.Flat) ExecuteFlatten(); return; }
				
				// Safely process the Reset request on the NinjaScript Thread
			    if (isResetPending) {
			        ResetAllLevels();
			        BuildLevelMap();
			        PopulateCombos();
			        activeStatus = "LEVELS RESET";
			        UpdateUI();
			        isResetPending = false;
			    }

                IdentifyDynamicContext();
                CalculateTactics();

                if (isFlattenPending) { ExecuteFlatten(); return; }
                
                if (Position.MarketPosition!= MarketPosition.Flat) {
                    if (isHalfRiskPending) ExecuteHalfRiskLogic();
                    if (isBreakevenPending) ExecuteBreakevenLogic();
                    if (isCloseOnePending) ExecuteCloseOne();
                    ManageTrailingStops();
                } else {
                    entryBar = -1; highSinceEntry = double.MinValue; lowSinceEntry = double.MaxValue; isManualActive = false; userOverrideStopPrice = 0; isPlaybookActive = false;
                    masterStopPrice = 0; activeLegs.Clear(); lastStopPrices.Clear();
                }

                if (isManualPendingL) { ExecuteManualTrade(true); return; }
                if (isManualPendingS) { ExecuteManualTrade(false); return; }
                if (isManScalpPendingL) { ExecuteManualScalp(true); return; }
                if (isManScalpPendingS) { ExecuteManualScalp(false); return; }
                
            } // <--- This missing bracket was causing your } expected error
            catch (Exception e) { Print("Trinity Error: " + e.Message); }
        }

		private void IdentifyDynamicContext()
        {
            // SPLIT THE BRAIN: The auto-trader, NEXT button, and Core playbooks ONLY see structural lines
            var coreKeys = new HashSet<string> { "B8", "B7", "B6", "B5", "B4", "B3", "B2", "B1", "POC", "R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8", 
            "B87_50", "B76_50", "B65_50", "B54_50", "B43_50", "B32_50", "B21_50", "B1_POC_50", "POC_R1_50", "R12_50", "R23_50", "R34_50", "R45_50", "R56_50", "R67_50", "R78_50" };
            
            orderedActiveLevels = levelMap.Where(kvp => coreKeys.Contains(kvp.Key) && kvp.Value > 0).OrderByDescending(kvp => kvp.Value).ToList();
            
            double p = Close[ 0 ];
            levelAbove = double.MaxValue;
            levelBelow = double.MinValue;
            zoneName = "WAITING";
            
            for (int i = 0; i < orderedActiveLevels.Count; i++)
            {
                if (orderedActiveLevels[i].Value > p) {
                    levelAbove = orderedActiveLevels[i].Value;
                } 
                else if (orderedActiveLevels[i].Value <= p && levelBelow == double.MinValue) {
                    levelBelow = orderedActiveLevels[i].Value;
                    zoneName = orderedActiveLevels[i > 0? i - 1 : 0].Key + " -> " + orderedActiveLevels[i].Key;
                    break;
                }
            }
            if (levelAbove == double.MaxValue) levelAbove = p + (100 * TickSize);
            if (levelBelow == double.MinValue) levelBelow = p - (100 * TickSize);
        }
		
        private int CalculateSmartAlloc(int originalQty, double entryPx, double stopPx)
        {
            if (originalQty == 0 || SmartAllocTicks <= 0) return originalQty;
            double riskTicks = Math.Abs(entryPx - stopPx) / TickSize;
            if (riskTicks <= SmartAllocTicks) return originalQty;
            
            double ratio = SmartAllocTicks / riskTicks;
            int adjustedQty = (int)Math.Floor(originalQty * ratio);
            return Math.Max(1, adjustedQty); 
        }

        private void ExecuteManualTrade(bool isLong)
        {
            try {
                double entryPrice = manEntryPx > 0? manEntryPx : Close[ 0 ];
                int startIdx = -1;
                
                for (int i = 0; i < orderedActiveLevels.Count; i++) {
                    if (isLong && orderedActiveLevels[i].Value <= entryPrice) { startIdx = i > 0? i - 1 : 0; break; }
                    if (!isLong && orderedActiveLevels[i].Value <= entryPrice) { startIdx = i; break; }
                }

                double t1 = manT1, t2 = manT2, t3 = manT3, t4 = manT4;
                if (!isPlaybookActive) {
                    if (isLong && startIdx!= -1) {
                        t1 = t1 == 0? orderedActiveLevels[startIdx].Value : t1;
                        t2 = t2 == 0 && startIdx - 1 >= 0? orderedActiveLevels[startIdx - 1].Value : t2;
                        t3 = t3 == 0 && startIdx - 2 >= 0? orderedActiveLevels[startIdx - 2].Value : t3;
                        t4 = t4 == 0 && startIdx - 3 >= 0? orderedActiveLevels[startIdx - 3].Value : t4;
                    } else if (!isLong && startIdx!= -1) {
                        t1 = t1 == 0 && startIdx + 1 < orderedActiveLevels.Count? orderedActiveLevels[startIdx + 1].Value : t1;
                        t2 = t2 == 0 && startIdx + 2 < orderedActiveLevels.Count? orderedActiveLevels[startIdx + 2].Value : t2;
                        t3 = t3 == 0 && startIdx + 3 < orderedActiveLevels.Count? orderedActiveLevels[startIdx + 3].Value : t3;
                        t4 = t4 == 0 && startIdx + 4 < orderedActiveLevels.Count? orderedActiveLevels[startIdx + 4].Value : t4;
                    }
                }

                double offset = CoreTargetOffsetTicks * TickSize;
                if (isLong) { t1 = t1 > 0? t1 - offset : t1; t2 = t2 > 0? t2 - offset : t2; t3 = t3 > 0? t3 - offset : t3; t4 = t4 > 0? t4 - offset : t4; } 
                else { t1 = t1 > 0? t1 + offset : t1; t2 = t2 > 0? t2 + offset : t2; t3 = t3 > 0? t3 + offset : t3; t4 = t4 > 0? t4 + offset : t4; }

                bool isStop = false, isLimit = false;
                if (manEntryPx > 0) {
                    if (isLong) { if (manEntryPx > Close[ 0 ]) isStop = true; else isLimit = true; }
                    else { if (manEntryPx < Close[ 0 ]) isStop = true; else isLimit = true; }
                }

                int stopTicks = C_InitialStopTicks;
                double stopPrice = isLong? entryPrice - (stopTicks * TickSize) : entryPrice + (stopTicks * TickSize);
                                
                if (manEntryPx > 0) {
                     if (isLong && stopPrice >= manEntryPx) stopPrice = manEntryPx - (20 * TickSize);
                     if (!isLong && stopPrice <= manEntryPx) stopPrice = manEntryPx + (20 * TickSize);
                }

                t1 = Rnd(t1); t2 = Rnd(t2); t3 = Rnd(t3); t4 = Rnd(t4);
                stopPrice = Rnd(stopPrice);
                entryPrice = Rnd(entryPrice);
                masterStopPrice = stopPrice; 

                int q1 = CalculateSmartAlloc(C_Qty1, entryPrice, stopPrice);
                int q2 = CalculateSmartAlloc(C_Qty2, entryPrice, stopPrice);
                int q3 = CalculateSmartAlloc(C_Qty3, entryPrice, stopPrice);
                int q4 = CalculateSmartAlloc(C_Qty4, entryPrice, stopPrice);

                string sig = "Core_" + (isLong? "L" : "S");
                isManualActive = true; userOverrideStopPrice = 0;
                entryBar = CurrentBar; highSinceEntry = High[ 0 ]; lowSinceEntry = Low[ 0 ];

                activeLegs.Clear();
                if (q1 > 0 && t1 > 0) for(int i=0; i<q1; i++) activeLegs.Add(sig + "_1");
                if (q2 > 0 && t2 > 0) for(int i=0; i<q2; i++) activeLegs.Add(sig + "_2");
                if (q3 > 0 && t3 > 0) for(int i=0; i<q3; i++) activeLegs.Add(sig + "_3");
                if (q4 > 0 && t4 > 0) for(int i=0; i<q4; i++) activeLegs.Add(sig + "_4");

                if (isLong) {
                    if (q1 > 0 && t1 > 0) { SafeSetStopLoss(sig + "_1", stopPrice); SetProfitTarget(sig + "_1", CalculationMode.Price, t1); if (isStop) EnterLongStopMarket(q1, entryPrice, sig + "_1"); else if (isLimit) EnterLongLimit(q1, entryPrice, sig + "_1"); else EnterLong(q1, sig + "_1"); }
                    if (q2 > 0 && t2 > 0) { SafeSetStopLoss(sig + "_2", stopPrice); SetProfitTarget(sig + "_2", CalculationMode.Price, t2); if (isStop) EnterLongStopMarket(q2, entryPrice, sig + "_2"); else if (isLimit) EnterLongLimit(q2, entryPrice, sig + "_2"); else EnterLong(q2, sig + "_2"); }
                    if (q3 > 0 && t3 > 0) { SafeSetStopLoss(sig + "_3", stopPrice); SetProfitTarget(sig + "_3", CalculationMode.Price, t3); if (isStop) EnterLongStopMarket(q3, entryPrice, sig + "_3"); else if (isLimit) EnterLongLimit(q3, entryPrice, sig + "_3"); else EnterLong(q3, sig + "_3"); }
                    if (q4 > 0 && t4 > 0) { SafeSetStopLoss(sig + "_4", stopPrice); SetProfitTarget(sig + "_4", CalculationMode.Price, t4); if (isStop) EnterLongStopMarket(q4, entryPrice, sig + "_4"); else if (isLimit) EnterLongLimit(q4, entryPrice, sig + "_4"); else EnterLong(q4, sig + "_4"); }
                } else {
                    if (q1 > 0 && t1 > 0) { SafeSetStopLoss(sig + "_1", stopPrice); SetProfitTarget(sig + "_1", CalculationMode.Price, t1); if (isStop) EnterShortStopMarket(q1, entryPrice, sig + "_1"); else if (isLimit) EnterShortLimit(q1, entryPrice, sig + "_1"); else EnterShort(q1, sig + "_1"); }
                    if (q2 > 0 && t2 > 0) { SafeSetStopLoss(sig + "_2", stopPrice); SetProfitTarget(sig + "_2", CalculationMode.Price, t2); if (isStop) EnterShortStopMarket(q2, entryPrice, sig + "_2"); else if (isLimit) EnterShortLimit(q2, entryPrice, sig + "_2"); else EnterShort(q2, sig + "_2"); }
                    if (q3 > 0 && t3 > 0) { SafeSetStopLoss(sig + "_3", stopPrice); SetProfitTarget(sig + "_3", CalculationMode.Price, t3); if (isStop) EnterShortStopMarket(q3, entryPrice, sig + "_3"); else if (isLimit) EnterShortLimit(q3, entryPrice, sig + "_3"); else EnterShort(q3, sig + "_3"); }
                    if (q4 > 0 && t4 > 0) { SafeSetStopLoss(sig + "_4", stopPrice); SetProfitTarget(sig + "_4", CalculationMode.Price, t4); if (isStop) EnterShortStopMarket(q4, entryPrice, sig + "_4"); else if (isLimit) EnterShortLimit(q4, entryPrice, sig + "_4"); else EnterShort(q4, sig + "_4"); }
                }

                isManualPendingL = false; isManualPendingS = false;
                activeStatus = "ORDER SENT"; UpdateUI(); 
                Disarm(false);
            }
            catch (Exception ex) { Print("ERROR IN ExecuteManualTrade: " + ex.ToString()); isManualPendingL = false; isManualPendingS = false; }
        }

        private void ExecuteAutoCoreTrade(bool isLong, bool useStopEntry = false, double stopEntryPrice = 0)
        {
            double entryPrice = useStopEntry? stopEntryPrice : Close[0];
            int startIdx = -1;
            
            for (int i = 0; i < orderedActiveLevels.Count; i++) {
                if (isLong && orderedActiveLevels[i].Value <= entryPrice) { startIdx = i > 0? i - 1 : 0; break; }
                if (!isLong && orderedActiveLevels[i].Value <= entryPrice) { startIdx = i; break; }
            }

            double t1 = 0, t2 = 0, t3 = 0, t4 = 0;
            if (isLong && startIdx!= -1) {
                t1 = orderedActiveLevels[startIdx].Value;
                t2 = startIdx - 1 >= 0? orderedActiveLevels[startIdx - 1].Value : t1 + (20 * TickSize);
                t3 = startIdx - 2 >= 0? orderedActiveLevels[startIdx - 2].Value : t2 + (20 * TickSize);
                t4 = startIdx - 3 >= 0? orderedActiveLevels[startIdx - 3].Value : t3 + (20 * TickSize);
            } else if (!isLong && startIdx!= -1) {
                t1 = startIdx + 1 < orderedActiveLevels.Count? orderedActiveLevels[startIdx + 1].Value : t1;
                t2 = startIdx + 2 < orderedActiveLevels.Count? orderedActiveLevels[startIdx + 2].Value : t1 - (20 * TickSize);
                t3 = startIdx + 3 < orderedActiveLevels.Count? orderedActiveLevels[startIdx + 3].Value : t2 - (20 * TickSize);
                t4 = startIdx + 4 < orderedActiveLevels.Count? orderedActiveLevels[startIdx + 4].Value : t3 - (20 * TickSize);
            }

            double offset = CoreTargetOffsetTicks * TickSize;
            if (isLong) { t1 -= offset; t2 -= offset; t3 -= offset; t4 -= offset; } 
            else { t1 += offset; t2 += offset; t3 += offset; t4 += offset; }
            
            double minTarget = 4 * TickSize; 
            if (isLong) {
                if (t1 <= entryPrice) t1 = entryPrice + minTarget;
                if (t2 <= t1) t2 = t1 + minTarget;
                if (t3 <= t2) t3 = t2 + minTarget;
                if (t4 <= t3) t4 = t3 + minTarget;
            } else {
                // Rewritten target adjustment to avoid formatting errors
                if (t1 == 0) t1 = entryPrice - minTarget;
                else if (t1 >= entryPrice) t1 = entryPrice - minTarget;

                if (t2 == 0) t2 = t1 - minTarget;
                else if (t2 >= t1) t2 = t1 - minTarget;

                if (t3 == 0) t3 = t2 - minTarget;
                else if (t3 >= t2) t3 = t2 - minTarget;

                if (t4 == 0) t4 = t3 - minTarget;
                else if (t4 >= t3) t4 = t3 - minTarget;
            }

            string sig = "Core_" + (isLong? "L" : "S");
            userOverrideStopPrice = 0; isManualActive = true; 
            entryBar = CurrentBar; highSinceEntry = High[0]; lowSinceEntry = Low[0];

            double stopPrice = isLong? entryPrice - (C_InitialStopTicks * TickSize) : entryPrice + (C_InitialStopTicks * TickSize);

            t1 = Rnd(t1); t2 = Rnd(t2); t3 = Rnd(t3); t4 = Rnd(t4);
            stopPrice = Rnd(stopPrice); entryPrice = Rnd(entryPrice); masterStopPrice = stopPrice; 

            int q1 = CalculateSmartAlloc(C_Qty1, entryPrice, stopPrice); 
            int q2 = CalculateSmartAlloc(C_Qty2, entryPrice, stopPrice);
            int q3 = CalculateSmartAlloc(C_Qty3, entryPrice, stopPrice); 
            int q4 = CalculateSmartAlloc(C_Qty4, entryPrice, stopPrice);

            activeLegs.Clear();
            if (q1 > 0 && t1 > 0) for(int i=0; i<q1; i++) activeLegs.Add(sig + "_1");
            if (q2 > 0 && t2 > 0) for(int i=0; i<q2; i++) activeLegs.Add(sig + "_2");
            if (q3 > 0 && t3 > 0) for(int i=0; i<q3; i++) activeLegs.Add(sig + "_3");
            if (q4 > 0 && t4 > 0) for(int i=0; i<q4; i++) activeLegs.Add(sig + "_4");

            if (isLong) {
                if (q1 > 0 && t1 > 0) { SafeSetStopLoss(sig + "_1", stopPrice); SetProfitTarget(sig + "_1", CalculationMode.Price, t1); if(useStopEntry) EnterLongStopMarket(q1, entryPrice, sig + "_1"); else EnterLong(q1, sig + "_1"); }
                if (q2 > 0 && t2 > 0) { SafeSetStopLoss(sig + "_2", stopPrice); SetProfitTarget(sig + "_2", CalculationMode.Price, t2); if(useStopEntry) EnterLongStopMarket(q2, entryPrice, sig + "_2"); else EnterLong(q2, sig + "_2"); }
                if (q3 > 0 && t3 > 0) { SafeSetStopLoss(sig + "_3", stopPrice); SetProfitTarget(sig + "_3", CalculationMode.Price, t3); if(useStopEntry) EnterLongStopMarket(q3, entryPrice, sig + "_3"); else EnterLong(q3, sig + "_3"); }
                if (q4 > 0 && t4 > 0) { SafeSetStopLoss(sig + "_4", stopPrice); SetProfitTarget(sig + "_4", CalculationMode.Price, t4); if(useStopEntry) EnterLongStopMarket(q4, entryPrice, sig + "_4"); else EnterLong(q4, sig + "_4"); }
            } else {
                if (q1 > 0 && t1 > 0) { SafeSetStopLoss(sig + "_1", stopPrice); SetProfitTarget(sig + "_1", CalculationMode.Price, t1); if(useStopEntry) EnterShortStopMarket(q1, entryPrice, sig + "_1"); else EnterShort(q1, sig + "_1"); }
                if (q2 > 0 && t2 > 0) { SafeSetStopLoss(sig + "_2", stopPrice); SetProfitTarget(sig + "_2", CalculationMode.Price, t2); if(useStopEntry) EnterShortStopMarket(q2, entryPrice, sig + "_2"); else EnterShort(q2, sig + "_2"); }
                if (q3 > 0 && t3 > 0) { SafeSetStopLoss(sig + "_3", stopPrice); SetProfitTarget(sig + "_3", CalculationMode.Price, t3); if(useStopEntry) EnterShortStopMarket(q3, entryPrice, sig + "_3"); else EnterShort(q3, sig + "_3"); }
                if (q4 > 0 && t4 > 0) { SafeSetStopLoss(sig + "_4", stopPrice); SetProfitTarget(sig + "_4", CalculationMode.Price, t4); if(useStopEntry) EnterShortStopMarket(q4, entryPrice, sig + "_4"); else EnterShort(q4, sig + "_4"); }
            }
            activeStatus = "ORDER SENT"; UpdateUI();
            Disarm(false);
        }
		
		private void ExecuteScalpMoonRunner(bool isLong)
        {
            isManualActive = false; entryBar = CurrentBar; highSinceEntry = High[ 0 ]; lowSinceEntry = Low[ 0 ];
            double entryPx = Close[ 0 ];
            double currentAtr = atrAlgo[ 0 ];
            
            if (double.IsNaN(currentAtr) |

				currentAtr == 0) currentAtr = 10 * TickSize;

            double stopTicks = Math.Max(5, Math.Round((currentAtr * S_InitialStopAtrMult) / TickSize));
            
            double stopPrice = Rnd(isLong? entryPx - (stopTicks * TickSize) : entryPx + (stopTicks * TickSize));
            double t1Price = Rnd(isLong? entryPx + (S_Leg1TargetTicks * TickSize) : entryPx - (S_Leg1TargetTicks * TickSize));
            double t2Price = Rnd(isLong? entryPx + (Math.Max(5, Math.Round((currentAtr * S_Leg2TargetAtrMult) / TickSize)) * TickSize) : entryPx - (Math.Max(5, Math.Round((currentAtr * S_Leg2TargetAtrMult) / TickSize)) * TickSize));
            double t3Price = Rnd(isLong? entryPx + (Math.Max(10, Math.Round((currentAtr * S_Leg3TargetAtrMult) / TickSize)) * TickSize) : entryPx - (Math.Max(10, Math.Round((currentAtr * S_Leg3TargetAtrMult) / TickSize)) * TickSize));
            double t4Price = Rnd(isLong? entryPx + (Math.Max(15, Math.Round((currentAtr * S_Leg4TargetAtrMult) / TickSize)) * TickSize) : entryPx - (Math.Max(15, Math.Round((currentAtr * S_Leg4TargetAtrMult) / TickSize)) * TickSize));

            string dir = isLong? "L" : "S";

            masterStopPrice = stopPrice; 
            allocStatus = "SCALP MOON"; userOverrideStopPrice = 0;

            activeLegs.Clear();
            if (S_Qty1 > 0) for(int i=0; i<S_Qty1; i++) activeLegs.Add("Scalp_" + dir + "_1");
            if (S_Qty2 > 0) for(int i=0; i<S_Qty2; i++) activeLegs.Add("Scalp_" + dir + "_2");
            if (S_Qty3 > 0) for(int i=0; i<S_Qty3; i++) activeLegs.Add("Scalp_" + dir + "_3");
            if (S_Qty4 > 0) for(int i=0; i<S_Qty4; i++) activeLegs.Add("Scalp_" + dir + "_4");

            if (isLong) {
                if (S_Qty1 > 0) { string s = "Scalp_L_1"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t1Price); EnterLong(S_Qty1, s); }
                if (S_Qty2 > 0) { string s = "Scalp_L_2"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t2Price); EnterLong(S_Qty2, s); }
                if (S_Qty3 > 0) { string s = "Scalp_L_3"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t3Price); EnterLong(S_Qty3, s); }
                if (S_Qty4 > 0) { string s = "Scalp_L_4"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t4Price); EnterLong(S_Qty4, s); }
            } else {
                if (S_Qty1 > 0) { string s = "Scalp_S_1"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t1Price); EnterShort(S_Qty1, s); }
                if (S_Qty2 > 0) { string s = "Scalp_S_2"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t2Price); EnterShort(S_Qty2, s); }
                if (S_Qty3 > 0) { string s = "Scalp_S_3"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t3Price); EnterShort(S_Qty3, s); }
                if (S_Qty4 > 0) { string s = "Scalp_S_4"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t4Price); EnterShort(S_Qty4, s); }
            }
            activeStatus = "ORDER SENT"; UpdateUI();
            Disarm(false);
        }

        private void ExecuteManualScalp(bool isLong)
        {
            double currentAtr = atrAlgo[ 0 ];
            if (double.IsNaN(currentAtr) |

				currentAtr == 0) currentAtr = 10 * TickSize;

            double entryPx = Rnd(Close[ 0 ]); 
            double stopTicks = Math.Max(5, Math.Round((currentAtr * S_InitialStopAtrMult) / TickSize));
            
            double stopPrice = Rnd(isLong? entryPx - (stopTicks * TickSize) : entryPx + (stopTicks * TickSize));
            double t1Price = Rnd(isLong? entryPx + (S_Leg1TargetTicks * TickSize) : entryPx - (S_Leg1TargetTicks * TickSize));
            double t2Price = Rnd(isLong? entryPx + (Math.Max(5, Math.Round((currentAtr * S_Leg2TargetAtrMult) / TickSize)) * TickSize) : entryPx - (Math.Max(5, Math.Round((currentAtr * S_Leg2TargetAtrMult) / TickSize)) * TickSize));
            double t3Price = Rnd(isLong? entryPx + (Math.Max(10, Math.Round((currentAtr * S_Leg3TargetAtrMult) / TickSize)) * TickSize) : entryPx - (Math.Max(10, Math.Round((currentAtr * S_Leg3TargetAtrMult) / TickSize)) * TickSize));
            double t4Price = Rnd(isLong? entryPx + (Math.Max(15, Math.Round((currentAtr * S_Leg4TargetAtrMult) / TickSize)) * TickSize) : entryPx - (Math.Max(15, Math.Round((currentAtr * S_Leg4TargetAtrMult) / TickSize)) * TickSize));

            string dir = isLong? "L" : "S";
            
            masterStopPrice = stopPrice; 
            allocStatus = "MAN SCALP"; userOverrideStopPrice = 0;

            activeLegs.Clear();
            if (S_Qty1 > 0) for(int i=0; i<S_Qty1; i++) activeLegs.Add("MScalp_" + dir + "_1");
            if (S_Qty2 > 0) for(int i=0; i<S_Qty2; i++) activeLegs.Add("MScalp_" + dir + "_2");
            if (S_Qty3 > 0) for(int i=0; i<S_Qty3; i++) activeLegs.Add("MScalp_" + dir + "_3");
            if (S_Qty4 > 0) for(int i=0; i<S_Qty4; i++) activeLegs.Add("MScalp_" + dir + "_4");

            if (isLong) {
                if (S_Qty1 > 0) { string s = "MScalp_L_1"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t1Price); EnterLong(S_Qty1, s); }
                if (S_Qty2 > 0) { string s = "MScalp_L_2"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t2Price); EnterLong(S_Qty2, s); }
                if (S_Qty3 > 0) { string s = "MScalp_L_3"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t3Price); EnterLong(S_Qty3, s); }
                if (S_Qty4 > 0) { string s = "MScalp_L_4"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t4Price); EnterLong(S_Qty4, s); }
            } else {
                if (S_Qty1 > 0) { string s = "MScalp_S_1"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t1Price); EnterShort(S_Qty1, s); }
                if (S_Qty2 > 0) { string s = "MScalp_S_2"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t2Price); EnterShort(S_Qty2, s); }
                if (S_Qty3 > 0) { string s = "MScalp_S_3"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t3Price); EnterShort(S_Qty3, s); }
                if (S_Qty4 > 0) { string s = "MScalp_S_4"; SafeSetStopLoss(s, stopPrice); SetProfitTarget(s, CalculationMode.Price, t4Price); EnterShort(S_Qty4, s); }
            }

            isManScalpPendingL = false; isManScalpPendingS = false; 
            activeStatus = "ORDER SENT"; UpdateUI();
            Disarm(false);
        }

        private void ExecuteFlatten() { 
            if (Position.MarketPosition == MarketPosition.Long) ExitLong(); 
            else if (Position.MarketPosition == MarketPosition.Short) ExitShort(); 
            
            var pendingOrders = Orders.Where(o => (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted) && (o.Name.Contains("Core") || o.Name.Contains("Scalp") || o.Name.Contains("Man"))).ToList();
            foreach (Order o in pendingOrders) { CancelOrder(o); } 
            
            isFlattenPending = false; userOverrideStopPrice = 0; masterStopPrice = 0; activeLegs.Clear(); lastStopPrices.Clear();
            activeStatus = "FLATTENED"; UpdateUI(); 
            Disarm(false); 
        }
        
        private void ExecuteCloseOne() { 
            if (Position.MarketPosition == MarketPosition.Flat) { isCloseOnePending = false; activeLegs.Clear(); return; } 
            int currentQty = Position.Quantity; 
            if (currentQty <= 0) { isCloseOnePending = false; return; } 
            
            while (activeLegs.Count > currentQty) { 
                activeLegs.RemoveAt(activeLegs.Count - 1); 
            } 
            
            if (activeLegs.Count > 0) { 
                string legToClose = activeLegs[activeLegs.Count - 1]; 
                if (Position.MarketPosition == MarketPosition.Long) ExitLong(1, "Close 1C", legToClose); 
                else ExitShort(1, "Close 1C", legToClose); 
                activeLegs.RemoveAt(activeLegs.Count - 1); 
            } else { 
                if (Position.MarketPosition == MarketPosition.Long) ExitLong(1, "Close 1C", ""); 
                else ExitShort(1, "Close 1C", ""); 
            } 
            isCloseOnePending = false; 
            activeStatus = "1C CLOSED"; UpdateUI(); 
        }

        private void ExecuteBreakevenLogic() 
        { 
            if (Position.MarketPosition == MarketPosition.Flat) { isBreakevenPending = false; return; } 
            bool isLong = Position.MarketPosition == MarketPosition.Long;
            
            double calculatedStop = isLong? Position.AveragePrice + (BreakevenOffsetTicks * TickSize) : Position.AveragePrice - (BreakevenOffsetTicks * TickSize); 
            
            double cappedStop = isLong? Math.Min(calculatedStop, Close[ 0 ] - (2 * TickSize)) : Math.Max(calculatedStop, Close[ 0 ] + (2 * TickSize));
            
            double be = Rnd(cappedStop);
            userOverrideStopPrice = be; 
            ApplyOverrideStop(be); 
            isBreakevenPending = false; 
            activeStatus = "STOP -> BE+" + BreakevenOffsetTicks; UpdateUI();
        }
        
        private void ExecuteHalfRiskLogic() 
        { 
            if (Position.MarketPosition == MarketPosition.Flat) { isHalfRiskPending = false; return; } 
            double curStop = GetCurrentStop(); 
            if (curStop == 0) { Print("HalfRisk Failed: Cannot determine current stop."); isHalfRiskPending = false; return; }
            bool isLong = Position.MarketPosition == MarketPosition.Long;
            
            double calculatedStop = (curStop + Close[ 0 ]) / 2.0; 
            
            double cappedStop = isLong? Math.Min(calculatedStop, Close[ 0 ] - (2 * TickSize)) : Math.Max(calculatedStop, Close[ 0 ] + (2 * TickSize));

            double newStop = Rnd(cappedStop); 
            userOverrideStopPrice = newStop; 
            ApplyOverrideStop(newStop); 
            isHalfRiskPending = false; 
            activeStatus = "RISK HALVED"; UpdateUI();
        }
        
        private double GetCurrentStop() 
        { 
            foreach (Order o in Orders) { 
                if (o != null && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted) && o.OrderType == OrderType.StopMarket) 
                    return o.StopPrice; 
            } 
            return masterStopPrice; 
        }
        
        private void ApplyOverrideStop(double price) 
        { 
            masterStopPrice = price; 
            string[] allSignals = new string[] { 
			    "Scalp_L_1", "Scalp_L_2", "Scalp_L_3", "Scalp_L_4", 
			    "Scalp_S_1", "Scalp_S_2", "Scalp_S_3", "Scalp_S_4", 
			    "Core_L_1", "Core_L_2", "Core_L_3", "Core_L_4", 
			    "Core_S_1", "Core_S_2", "Core_S_3", "Core_S_4", 
			    "MScalp_L_1", "MScalp_L_2", "MScalp_L_3", "MScalp_L_4", 
			    "MScalp_S_1", "MScalp_S_2", "MScalp_S_3", "MScalp_S_4"
			};

            foreach (string s in allSignals) SafeSetStopLoss(s, price); 
        }

        private void ManageTrailingStops() 
        { 
            highSinceEntry = Math.Max(highSinceEntry, High[ 0 ]);
            lowSinceEntry = Math.Min(lowSinceEntry, Low[ 0 ]);
            
            if (S_Qty2 > 0) ApplyTrail(S_Qty2, "Scalp_L_2", "Scalp_S_2", S_Leg2TrailMode, S_Leg2BarN, 0, "MScalp_L_2", "MScalp_S_2"); 
            if (S_Qty3 > 0) ApplyTrail(S_Qty3, "Scalp_L_3", "Scalp_S_3", S_Leg3TrailMode, 0, S_Leg3RatchetAtrMult, "MScalp_L_3", "MScalp_S_3");
            if (S_Qty4 > 0) ApplyTrail(S_Qty4, "Scalp_L_4", "Scalp_S_4", S_Leg4TrailMode, 0, S_Leg4RatchetAtrMult, "MScalp_L_4", "MScalp_S_4");

            if (isManualActive || isPlaybookActive) {
                if (C_Qty1 > 0) ApplyTrail(C_Qty1, "Core_L_1", "Core_S_1", C_Leg1TrailMode, 0, C_Leg1RatchetAtrMult, "Man_L_1", "Man_S_1");
                if (C_Qty2 > 0) ApplyTrail(C_Qty2, "Core_L_2", "Core_S_2", C_Leg2TrailMode, C_Leg2BarN, 0, "Man_L_2", "Man_S_2"); 
                if (C_Qty3 > 0) ApplyTrail(C_Qty3, "Core_L_3", "Core_S_3", C_Leg3TrailMode, 0, C_Leg3RatchetAtrMult, "Man_L_3", "Man_S_3");
                if (C_Qty4 > 0) ApplyTrail(C_Qty4, "Core_L_4", "Core_S_4", C_Leg4TrailMode, 0, C_Leg4RatchetAtrMult, "Man_L_4", "Man_S_4");
            }
        }
        private void ApplyTrail(int qty, string longSig1, string shortSig1, TrailMode mode, int barN, double atrMult, string longSig2 = "", string shortSig2 = "") 
        { 
            if (mode == TrailMode.None && userOverrideStopPrice == 0) return; 
            bool isLong = Position.MarketPosition == MarketPosition.Long; 
            
            double newStop = 0; 
            if (mode == TrailMode.BarNTrail) { int idx = Math.Min(barN, CurrentBar); if (isLong) newStop = Low[ idx ]; else newStop = High[ idx ]; } 
            else if (mode == TrailMode.AtrRatchet) { double rat = atrAlgo[ 0 ] * atrMult; if (isLong) newStop = highSinceEntry - rat; else newStop = lowSinceEntry + rat; } 
            
            if (userOverrideStopPrice!= 0) { 
                if (newStop == 0) newStop = userOverrideStopPrice; 
                else { if (isLong) newStop = Math.Max(newStop, userOverrideStopPrice); else newStop = Math.Min(newStop, userOverrideStopPrice); } 
            } 
            
            if (newStop!= 0) {
                newStop = Rnd(newStop);
                
                string activeSig1 = isLong? longSig1 : shortSig1;
                if (lastStopPrices.ContainsKey(activeSig1)) {
    				double existingStop = lastStopPrices[activeSig1];
                    if (isLong && newStop < existingStop) newStop = existingStop;
                    if (!isLong && newStop > existingStop) newStop = existingStop;
                }

                masterStopPrice = newStop; 
                SafeSetStopLoss(activeSig1, newStop);
                if (longSig2!= "") SafeSetStopLoss(isLong? longSig2 : shortSig2, newStop);
            }
        }
		
		private void CreateWPFControls()
        {
            if (!ShowHUD) return; 
            
            chartGrid = ChartControl.Parent as System.Windows.Controls.Grid; if (chartGrid == null) return;
            mainPanel = new System.Windows.Controls.Grid { Width = 170, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 50, 110, 0) }; 
            for (int i = 0; i < 35; i++) mainPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

            System.Windows.Media.Brush textColor = UseDarkTheme? WPFBrushes.White : WPFBrushes.Black;
            System.Windows.Media.Brush panelBg = UseDarkTheme? WPFBrushes.DimGray : WPFBrushes.LightGray;

            lblStatus = LabelStyle("STANDBY", textColor, panelBg);
            lblPnL = LabelStyle("$0.00", UseDarkTheme? WPFBrushes.Lime : WPFBrushes.DarkGreen, UseDarkTheme? WPFBrushes.Black : WPFBrushes.White);
            AddRow(lblStatus, 0); AddRow(lblPnL, 1);

            Label lblAuto = new Label { Content = "AUTO TRADER", Foreground = WPFBrushes.DarkOrange, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Bold, FontFamily = modernFont };
            AddRow(lblAuto, 2);
            btnScalpL = Btn("SCALP L", panelBg); btnScalpS = Btn("SCALP S", panelBg);
            btnCoreL = Btn("CORE L", panelBg); btnCoreS = Btn("CORE S", panelBg);
            AddDualRow(btnScalpL, btnScalpS, 3); AddDualRow(btnCoreL, btnCoreS, 4);

            Label lblMan = new Label { Content = "MANUAL / PLAYBOOK", Foreground = UseDarkTheme? WPFBrushes.Cyan : WPFBrushes.DodgerBlue, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Bold, FontFamily = modernFont };
            AddRow(lblMan, 6);

            cbLongPlays = new ComboBox { FontSize = 10, Height = 22, Margin = new Thickness(1), FontFamily = modernFont, FontWeight = FontWeights.Bold };
            cbLongPlays.Items.Add("LONG PLAYS...");
            cbLongPlays.Items.Add("1. Dynamic Fill"); 
            cbLongPlays.Items.Add("2. B6 to B8"); cbLongPlays.Items.Add("3. B4 to B6"); cbLongPlays.Items.Add("4. B2 to B4");
            cbLongPlays.Items.Add("5. POC to B2"); cbLongPlays.Items.Add("6. R1 to POC"); cbLongPlays.Items.Add("7. R1 to B1 x");
            cbLongPlays.Items.Add("8. R2 to B2 x"); cbLongPlays.Items.Add("9. R2 to POC");
            cbLongPlays.Items.Add("10. R3 to R2"); cbLongPlays.Items.Add("11. R4 to R2"); cbLongPlays.Items.Add("12. R4 to B4 x");
            cbLongPlays.Items.Add("13. R6 to R4"); cbLongPlays.Items.Add("14. R5 to POC");
            cbLongPlays.Items.Add("15. VAL to VAH (80% Rule)");
            cbLongPlays.Items.Add("16. Fade ONL to ON Mid");
            cbLongPlays.Items.Add("17. OR5 Breakout Trend");
            cbLongPlays.Items.Add("18. IBL Fail to IBH");
            cbLongPlays.SelectedIndex = 0;
            cbLongPlays.SelectionChanged += (s, e) => { if (cbLongPlays.SelectedIndex > 0) { cbShortPlays.SelectedIndex = 0; ProcessPlaybook(true, cbLongPlays.SelectedIndex); } };
            AddRow(cbLongPlays, 7);

            cbShortPlays = new ComboBox { FontSize = 10, Height = 22, Margin = new Thickness(1), FontFamily = modernFont, FontWeight = FontWeights.Bold };
            cbShortPlays.Items.Add("SHORT PLAYS...");
            cbShortPlays.Items.Add("1. Dynamic Fill"); 
            cbShortPlays.Items.Add("2. B6 to B4"); cbShortPlays.Items.Add("3. B4 to R4 x"); cbShortPlays.Items.Add("4. B4 to B2");
            cbShortPlays.Items.Add("5. B3 to B2"); cbShortPlays.Items.Add("6. B2 to POC"); cbShortPlays.Items.Add("7. B2 to R2 x");
            cbShortPlays.Items.Add("8. B1 to R1 x"); cbShortPlays.Items.Add("9. B1 to POC"); cbShortPlays.Items.Add("10. POC to R2");
            cbShortPlays.Items.Add("11. R2 TO R4"); cbShortPlays.Items.Add("12. R4 to R6"); cbShortPlays.Items.Add("13. B5 to POC");
            cbShortPlays.Items.Add("14. R6 to R8");
            cbShortPlays.Items.Add("15. VAH to VAL (80% Rule)");
            cbShortPlays.Items.Add("16. Fade ONH to ON Mid");
            cbShortPlays.Items.Add("17. OR5 Breakout Trend");
            cbShortPlays.Items.Add("18. IBH Fail to IBL");
            cbShortPlays.SelectedIndex = 0;
            cbShortPlays.SelectionChanged += (s, e) => { if (cbShortPlays.SelectedIndex > 0) { cbLongPlays.SelectedIndex = 0; ProcessPlaybook(false, cbShortPlays.SelectedIndex); } };
            AddRow(cbShortPlays, 8);

            System.Windows.Controls.Grid entryGrid = new System.Windows.Controls.Grid();
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            
            Label lblE = new Label { Content = "Entry:", Foreground = textColor, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            txtEntryPrice = new TextBox { FontSize = 11, Height = 20, Margin = new Thickness(1), Text = "0" };
            btnPxC = new Button { Content = "C", FontSize = 9, FontWeight = FontWeights.Bold, Margin = new Thickness(1) };
            btnPxUp = new Button { Content = "+", FontSize = 9, FontWeight = FontWeights.Bold, Margin = new Thickness(1) };
            btnPxDn = new Button { Content = "-", FontSize = 9, FontWeight = FontWeights.Bold, Margin = new Thickness(1) };

            btnPxC.Click += (s, e) => { ChartControl.Dispatcher.InvokeAsync(() => { txtEntryPrice.Text = currentChartPrice.ToString("F2"); }); };
            btnPxUp.Click += (s, e) => { ChartControl.Dispatcher.InvokeAsync(() => { double val; if (double.TryParse(txtEntryPrice.Text, out val)) { val += TickSize; txtEntryPrice.Text = val.ToString("F2"); } }); };
            btnPxDn.Click += (s, e) => { ChartControl.Dispatcher.InvokeAsync(() => { double val; if (double.TryParse(txtEntryPrice.Text, out val)) { val -= TickSize; txtEntryPrice.Text = val.ToString("F2"); } }); };

            System.Windows.Controls.Grid.SetColumn(lblE, 0); System.Windows.Controls.Grid.SetColumn(txtEntryPrice, 1); System.Windows.Controls.Grid.SetColumn(btnPxC, 2); System.Windows.Controls.Grid.SetColumn(btnPxUp, 3); System.Windows.Controls.Grid.SetColumn(btnPxDn, 4);
            entryGrid.Children.Add(lblE); entryGrid.Children.Add(txtEntryPrice); entryGrid.Children.Add(btnPxC); entryGrid.Children.Add(btnPxUp); entryGrid.Children.Add(btnPxDn);
            AddRow(entryGrid, 9);

            boxT1 = Combo(); boxT2 = Combo(); boxT3 = Combo(); boxT4 = Combo();
            AddRow(boxT1, 10); AddRow(boxT2, 11); AddRow(boxT3, 12); AddRow(boxT4, 13);
            
            Label lblFail = new Label { Content = "FAIL SAFE", Foreground = UseDarkTheme? WPFBrushes.Cyan : WPFBrushes.DodgerBlue, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Bold, FontFamily = modernFont };
            AddRow(lblFail, 15);

            System.Windows.Media.SolidColorBrush gC = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 128, 0)); 
            System.Windows.Media.SolidColorBrush rC = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 128, 0, 0)); 
            System.Windows.Media.SolidColorBrush bC = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 0, 128)); 
            
            btnManScalpL = Btn("SCALP", gC); btnManScalpS = Btn("SCALP", rC);
            btnNextBarL = Btn("NEXT", bC); btnNextBarS = Btn("NEXT", bC);
            btnManualL = Btn("CORE", gC); btnManualS = Btn("CORE", rC);

            AddDualRow(btnManScalpL, btnManScalpS, 19);
            AddDualRow(btnNextBarL, btnNextBarS, 20);
            AddDualRow(btnManualL, btnManualS, 21);

            btnHalfRisk = SolidBtn("50% RISK", WPFBrushes.Orange);
            btnBreakeven = SolidBtn("BE + " + BreakevenOffsetTicks, WPFBrushes.DodgerBlue);
            btnDisarm = SolidBtn("DISARM", WPFBrushes.DarkGray);
            btnCloseOne = SolidBtn("CLOSE 1C", WPFBrushes.Yellow); btnCloseOne.Foreground = WPFBrushes.Black;
            AddDualRow(btnHalfRisk, btnBreakeven, 23); AddDualRow(btnDisarm, btnCloseOne, 24);

            btnFlatten = SolidBtn("FLATTEN", WPFBrushes.Red); btnFlatten.Height = 30;
            AddRow(btnFlatten, 25);
            
            btnResetLevels = SolidBtn("RESET LEVELS", WPFBrushes.DimGray);
            AddRow(btnResetLevels, 26);

            btnScalpL.Click += (s, e) => { bool p = isArmedScalpLong; Disarm(); isArmedScalpLong =!p; activeStatus = isArmedScalpLong? "ARM SCALP L" : "STANDBY"; UpdateUI(); };
            btnScalpS.Click += (s, e) => { bool p = isArmedScalpShort; Disarm(); isArmedScalpShort =!p; activeStatus = isArmedScalpShort? "ARM SCALP S" : "STANDBY"; UpdateUI(); };
            btnCoreL.Click += (s, e) => { bool p = isArmedCoreLong; Disarm(); isArmedCoreLong =!p; activeStatus = isArmedCoreLong? "ARM CORE L" : "STANDBY"; UpdateUI(); };
            btnCoreS.Click += (s, e) => { bool p = isArmedCoreShort; Disarm(); isArmedCoreShort =!p; activeStatus = isArmedCoreShort? "ARM CORE S" : "STANDBY"; UpdateUI(); };
            
            btnManualL.Click += (s, e) => { ParseManualInputs(); isManualPendingL = true; activeStatus = "CORE L SENT"; btnManualL.Background = WPFBrushes.LimeGreen; UpdateUI(); };
            btnManualS.Click += (s, e) => { ParseManualInputs(); isManualPendingS = true; activeStatus = "CORE S SENT"; btnManualS.Background = WPFBrushes.Red; UpdateUI(); };
            btnManScalpL.Click += (s, e) => { isManScalpPendingL = true; activeStatus = "SCALP SENT"; UpdateUI(); };
            btnManScalpS.Click += (s, e) => { isManScalpPendingS = true; activeStatus = "SCALP SENT"; UpdateUI(); };
            btnNextBarL.Click += (s, e) => { bool p = isNextBarPendingL; Disarm(); isNextBarPendingL =!p; activeStatus = isNextBarPendingL? "WAIT NEXT L" : "STANDBY"; UpdateUI(); };
            btnNextBarS.Click += (s, e) => { bool p = isNextBarPendingS; Disarm(); isNextBarPendingS =!p; activeStatus = isNextBarPendingS? "WAIT NEXT S" : "STANDBY"; UpdateUI(); };

            btnDisarm.Click += (s, e) => Disarm(true);
            btnFlatten.Click += (s, e) => { isFlattenPending = true; activeStatus = "FLATTENING"; UpdateUI(); };
            
            btnHalfRisk.Click += (s, e) => { isHalfRiskPending = true; activeStatus = "CALC 50% RISK..."; UpdateUI(); };
            btnBreakeven.Click += (s, e) => { isBreakevenPending = true; activeStatus = "CALC BE..."; UpdateUI(); };
            btnCloseOne.Click += (s, e) => { isCloseOnePending = true; activeStatus = "CLOSING 1C..."; UpdateUI(); };

            btnResetLevels.Click += (s, e) => { isResetPending = true; };

        }

        private void ParseManualInputs()
        {
            Double.TryParse(boxT1.Text, out manT1); Double.TryParse(boxT2.Text, out manT2); 
            Double.TryParse(boxT3.Text, out manT3); Double.TryParse(boxT4.Text, out manT4);
            Double.TryParse(txtEntryPrice.Text, out manEntryPx);
        }

        private void ProcessPlaybook(bool isLong, int idx)
        {
            isPlaybookActive = true;

            if (idx == 1)
            {
                double parsedEntry;
                double entry = (double.TryParse(txtEntryPrice.Text, out parsedEntry) && parsedEntry > 0)? parsedEntry : Close[ 0 ];
                
                var allActiveLevels = levelMap.Where(kvp => kvp.Value > 0).OrderByDescending(kvp => kvp.Value).ToList();

                List<double> rawTargets = isLong 
                  ? allActiveLevels.Where(k => k.Value > entry).OrderBy(k => k.Value).Select(k => k.Value).ToList()
                    : allActiveLevels.Where(k => k.Value < entry).OrderByDescending(k => k.Value).Select(k => k.Value).ToList();

                List<double> filteredTargets = new List<double>();
                
                foreach (double val in rawTargets)
                {
                    if (filteredTargets.Count == 0) {
                        filteredTargets.Add(val);
                    } else {
                        if (Math.Abs(val - filteredTargets.Last()) > (ConfluenceTicks * TickSize)) {
                            filteredTargets.Add(val);
                        }
                    }
                    if (filteredTargets.Count >= 4) break; 
                }

				boxT1.Text = filteredTargets.Count > 0 ? filteredTargets[0].ToString("F2") : "";
				boxT2.Text = filteredTargets.Count > 1 ? filteredTargets[1].ToString("F2") : "";
				boxT3.Text = filteredTargets.Count > 2 ? filteredTargets[2].ToString("F2") : "";
				boxT4.Text = filteredTargets.Count > 3 ? filteredTargets[3].ToString("F2") : "";
                
                return;
            }

            double t1 = 0, t2 = 0, t3 = 0, t4 = 0;
            
            if (isLong)
            {
                if (idx == 2) { t1 = (L_B6 + L_B7) / 2; t2 = L_B7; t3 = (L_B7 + L_B8) / 2; t4 = L_B8; } 
                else if (idx == 3) { t1 = (L_B4 + L_B5) / 2; t2 = L_B5; t3 = L_B6; t4 = L_B6 + (20*TickSize); } 
                else if (idx == 4) { t1 = (L_B2 + L_B3) / 2; t2 = L_B3; t3 = L_B4; t4 = L_B4 + (20*TickSize); } 
                else if (idx == 5) { t1 = (L_POC + L_B1) / 2; t2 = (L_B1 + L_B2) / 2; t3 = L_B2; t4 = L_B2 + (20*TickSize); } 
                else if (idx == 6) { t1 = (L_R1 + L_POC) / 2; t2 = L_POC; } 
                else if (idx == 7) { t1 = (L_R1 + L_POC) / 2; t2 = L_POC; t3 = (L_POC + L_B1) / 2; t4 = L_B1; } 
                else if (idx == 8) { t1 = L_R1; t2 = L_POC; t3 = L_B1; t4 = L_B2; } 
                else if (idx == 9) { t1 = L_R1; t2 = (L_R1 + L_POC) / 2; t3 = L_POC; } 
                else if (idx == 10) { t1 = (L_R3 + L_R2) / 2; t2 = L_R2; } 
                else if (idx == 11) { t1 = L_R3; t2 = (L_R3 + L_R2) / 2; t3 = L_R2; } 
                else if (idx == 12) { t1 = L_R2; t2 = L_POC; t3 = L_B2; t4 = L_B4; } 
                else if (idx == 13) { t1 = (L_R6 + L_R5) / 2; t2 = L_R5; t3 = (L_R5 + L_R4) / 2; t4 = L_R4; } 
                else if (idx == 14) { t1 = L_R4; t2 = L_R2; t3 = L_R1; t4 = L_POC; } 
                else if (idx == 15) { t1 = L_YestVWAP; t2 = L_YestPOC; t3 = L_VAH; t4 = L_PDH; } 
                else if (idx == 16) { t1 = L_ON_VWAP; t2 = L_ON_MID; t3 = L_OPEN; t4 = L_PDC; } 
                else if (idx == 17) { t1 = L_ONH; t2 = L_PDH; t3 = L_PDH + (20*TickSize); t4 = L_PDH + (40*TickSize); } 
                else if (idx == 18) { t1 = L_YestVWAP; t2 = L_OPEN; t3 = L_IBH; t4 = L_PDH; } 
            }
            else
            {
                if (idx == 2) { t1 = (L_B6 + L_B5) / 2; t2 = L_B5; t3 = (L_B5 + L_B4) / 2; t4 = L_B4; } 
                else if (idx == 3) { t1 = L_B2; t2 = L_POC; t3 = L_R2; t4 = L_R4; } 
                else if (idx == 4) { t1 = L_B3; t2 = (L_B3 + L_B2) / 2; t3 = L_B2; } 
                else if (idx == 5) { t1 = (L_B3 + L_B2) / 2; t2 = L_B2; } 
                else if (idx == 6) { t1 = L_B1; t2 = (L_POC + L_B1) / 2; t3 = L_POC; } 
                else if (idx == 7) { t1 = L_B1; t2 = L_POC; t3 = L_R1; t4 = L_R2; } 
                else if (idx == 8) { t1 = (L_POC + L_B1) / 2; t2 = L_POC; t3 = (L_R1 + L_POC) / 2; t4 = L_R1; } 
                else if (idx == 9) { t1 = (L_POC + L_B1) / 2; t2 = L_POC; } 
                else if (idx == 10) { t1 = (L_R1 + L_POC) / 2; t2 = L_R1; t3 = L_R2; } 
                else if (idx == 11) { t1 = (L_R2 + L_R3) / 2; t2 = L_R3; t3 = L_R4; } 
                else if (idx == 12) { t1 = (L_R4 + L_R5) / 2; t2 = L_R5; t3 = L_R6; } 
                else if (idx == 13) { t1 = L_B4; t2 = L_B2; t3 = L_B1; t4 = L_POC; } 
                else if (idx == 14) { t1 = (L_R6 + L_R7) / 2; t2 = L_R7; t3 = (L_R7 + L_R8) / 2; t4 = L_R8; } 
                else if (idx == 15) { t1 = L_YestVWAP; t2 = L_YestPOC; t3 = L_VAL; t4 = L_PDL; } 
                else if (idx == 16) { t1 = L_ON_VWAP; t2 = L_ON_MID; t3 = L_OPEN; t4 = L_PDC; } 
                else if (idx == 17) { t1 = L_ONL; t2 = L_PDL; t3 = L_PDL - (20*TickSize); t4 = L_PDL - (40*TickSize); } 
                else if (idx == 18) { t1 = L_YestVWAP; t2 = L_OPEN; t3 = L_IBL; t4 = L_PDL; } 
            }

            boxT1.Text = t1 > 0? t1.ToString("F2") : ""; 
            boxT2.Text = t2 > 0? t2.ToString("F2") : ""; 
            boxT3.Text = t3 > 0? t3.ToString("F2") : ""; 
            boxT4.Text = t4 > 0? t4.ToString("F2") : "";
        }

        private void StartFlashTimer() { flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; flashTimer.Tick += (s, e) => { flashState =!flashState; if (isGoalReached) UpdateUI(); }; flashTimer.Start(); }

        private void UpdateUI()
		{
		    if (!ShowHUD || lblStatus == null) return;
		    
		    // FIX: Capture strategy thread variables safely BEFORE moving to the UI thread
		    double capturedPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;
		    bool capturedGoalReached = isGoalReached;
		    string capturedStatus = activeStatus;
		    
		    ChartControl.Dispatcher.InvokeAsync(() => {
		        dailyPnL = capturedPnL; // Safely assign it here
		        
		        if (capturedStatus != "STANDBY") lblStatus.Content = capturedStatus;
		        else lblStatus.Content = "STANDBY";
		        
		        lblPnL.Content = dailyPnL.ToString("C");
		        
		        if (UseDarkTheme) lblPnL.Foreground = dailyPnL >= 0? WPFBrushes.Lime : WPFBrushes.Red;
		        else lblPnL.Foreground = dailyPnL >= 0? WPFBrushes.DarkGreen : WPFBrushes.Red;
		
		        if (capturedGoalReached) { lblStatus.Background = flashState? WPFBrushes.Gold : WPFBrushes.DimGray; lblStatus.Foreground = flashState? WPFBrushes.Black : WPFBrushes.White; } 
		        else { lblStatus.Background = UseDarkTheme? WPFBrushes.DimGray : WPFBrushes.LightGray; lblStatus.Foreground = UseDarkTheme? WPFBrushes.White : WPFBrushes.Black; }
		
		        System.Windows.Media.Brush btnBg = UseDarkTheme? WPFBrushes.DimGray : WPFBrushes.DarkGray;
		
		        btnScalpL.Background = isArmedScalpLong? WPFBrushes.LimeGreen : btnBg;
		        btnScalpS.Background = isArmedScalpShort? WPFBrushes.Red : btnBg;
		        btnCoreL.Background = isArmedCoreLong? WPFBrushes.LimeGreen : btnBg;
		        btnCoreS.Background = isArmedCoreShort? WPFBrushes.Red : btnBg;
		
		        btnNextBarL.Background = isNextBarPendingL? WPFBrushes.DodgerBlue : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 0, 128));
		        btnNextBarS.Background = isNextBarPendingS? WPFBrushes.Magenta : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 0, 128));
		        btnManualL.Background = isManualPendingL? WPFBrushes.LimeGreen : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 128, 0));
		        btnManualS.Background = isManualPendingS? WPFBrushes.Red : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 128, 0, 0));
		    });
		}

        private Button Btn(string txt, System.Windows.Media.Brush bg) { return new Button { Content = txt, Background = bg, Foreground = WPFBrushes.White, FontSize = 10, Margin = new Thickness(1), FontWeight = FontWeights.Bold, FontFamily = modernFont, HorizontalContentAlignment = HorizontalAlignment.Center }; }
        private Button SolidBtn(string txt, System.Windows.Media.Brush bg) { return new Button { Content = txt, Background = bg, Foreground = WPFBrushes.White, FontSize = 10, Margin = new Thickness(1), FontWeight = FontWeights.Bold, FontFamily = modernFont }; }
        private CheckBox Check(string content, System.Windows.Media.Brush tc) { return new CheckBox { Content = content, Foreground = tc, FontSize = 10, FontFamily = modernFont, Margin = new Thickness(2) }; }
        private Label LabelStyle(string content, System.Windows.Media.Brush fg, System.Windows.Media.Brush bg) { return new Label { Content = content, Foreground = fg, Background = bg, FontFamily = modernFont, FontWeight = FontWeights.Bold, HorizontalContentAlignment = HorizontalAlignment.Center, Width = 170 }; }
        private void AddRow(FrameworkElement c, int r) { System.Windows.Controls.Grid.SetRow(c, r); mainPanel.Children.Add(c); }
        private void AddDualRow(FrameworkElement l, FrameworkElement r, int row) { System.Windows.Controls.Grid g = new System.Windows.Controls.Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition()); System.Windows.Controls.Grid.SetColumn(l, 0); System.Windows.Controls.Grid.SetColumn(r, 1); g.Children.Add(l); g.Children.Add(r); System.Windows.Controls.Grid.SetRow(g, row); mainPanel.Children.Add(g); }
        private void DisposeWPFControls() { if (chartGrid!= null && mainPanel!= null) chartGrid.Children.Remove(mainPanel); }
        
        private void Disarm(bool clearStatus = true) { 
            isArmedScalpLong = false; isArmedScalpShort = false; isArmedCoreLong = false; isArmedCoreShort = false; 
            isManualPendingL = false; isManualPendingS = false; isManScalpPendingL = false; isManScalpPendingS = false; 
            isHalfRiskPending = false; isBreakevenPending = false; isNextBarPendingL = false; isNextBarPendingS = false; 
            isCloseOnePending = false;
            
            if (clearStatus &&!isGoalReached) activeStatus = "STANDBY"; 
            UpdateUI(); 
        }
        
        private ComboBox Combo() { var c = new ComboBox { IsEditable = true, FontSize = 10, Margin = new Thickness(2), Height = 20, FontFamily = modernFont }; c.DropDownClosed += (s, e) => { ComboBox cb = s as ComboBox; string sel = cb.SelectedItem as string; if (sel!= null && levelMap.ContainsKey(sel)) cb.Text = levelMap[sel].ToString("F2"); }; return c; }
        
        private void PopulateCombos() { 
            ChartControl.Dispatcher.InvokeAsync(() => { 
                boxT1.Items.Clear(); boxT2.Items.Clear(); boxT3.Items.Clear(); boxT4.Items.Clear();
                foreach (string name in orderedLevelNames) { 
                    boxT1.Items.Add(name); boxT2.Items.Add(name); boxT3.Items.Add(name); boxT4.Items.Add(name); 
                } 
            }); 
        }

        private void ResetAllLevels() { 
            L_B8 = L_B7 = L_B6 = L_B5 = L_B4 = L_B3 = L_B2 = L_B1 = L_POC = L_R1 = L_R2 = L_R3 = L_R4 = L_R5 = L_R6 = L_R7 = L_R8 = 0; 
        }
        
        private void ValidateLevels() 
{ 
		    // Simply updates the bool; doesn't trigger a disarm here.
		    levelsValid = (L_B8 > L_B7 && L_B7 > L_B6 && L_B6 > L_B5 && L_B5 > L_B4 && 
                   L_B4 > L_B3 && L_B3 > L_B2 && L_B2 > L_B1 && L_B1 > L_POC && 
                   L_POC > L_R1 && L_R1 > L_R2 && L_R2 > L_R3 && L_R3 > L_R4 && 
                   L_R4 > L_R5 && L_R5 > L_R6 && L_R6 > L_R7 && L_R7 > L_R8); 
}
        private void CalculateTactics() {
            double range = levelAbove - levelBelow; if (range <= 0) return;
            bool isNearTop = (Close[ 0 ] > levelBelow + (0.75 * range));
            bool isNearBot = (Close[ 0 ] < levelBelow + (0.25 * range));
            
            if (isNearTop) { hud_ShortPlan = string.Format("SHORT (Rejection):\n Lvl {0:N2}", levelBelow); } 
            else { hud_ShortPlan = string.Format("SHORT (Breakout):\n Lvl {0:N2}", levelBelow); }
            if (isNearBot) { hud_LongPlan = string.Format("LONG (Bounce):\n Lvl {0:N2}", levelAbove); } 
            else { hud_LongPlan = string.Format("LONG (Breakout):\n Lvl {0:N2}", levelAbove); }
        }

        private void BuildLevelMap() {
		    if (SyncMode == SyncModeType.Receiver) {
		        lock(mapLock) { // FIX: Lock during read
		            if (SharedLevelMap!= null && SharedLevelMap.Count > 0) {
		                levelMap = new Dictionary<string, double>(SharedLevelMap);
		                orderedLevelNames = new List<string>(SharedOrderedLevelNames);
		            }
		        }
		        return;
		    }

            levelMap.Clear(); orderedLevelNames.Clear();
            void Add(string n, double v) { if (v > 0) { levelMap[n] = v; } }
            void AddMid(string n1, string n2, double v1, double v2, string alias) { if (v1 > 0 && v2 > 0) { double mid = (v1 + v2) / 2.0; levelMap[alias] = mid; } }
            
            // Core
            Add("B8", L_B8); AddMid("B8", "B7", L_B8, L_B7, "B87_50"); Add("B7", L_B7); AddMid("B7", "B6", L_B7, L_B6, "B76_50");
            Add("B6", L_B6); AddMid("B6", "B5", L_B6, L_B5, "B65_50"); Add("B5", L_B5); AddMid("B5", "B4", L_B5, L_B4, "B54_50");
            Add("B4", L_B4); AddMid("B4", "B3", L_B4, L_B3, "B43_50"); Add("B3", L_B3); AddMid("B3", "B2", L_B3, L_B2, "B32_50");
            Add("B2", L_B2); AddMid("B2", "B1", L_B2, L_B1, "B21_50"); Add("B1", L_B1); AddMid("B1", "POC", L_B1, L_POC, "B1_POC_50");
            Add("POC", L_POC); AddMid("POC", "R1", L_POC, L_R1, "POC_R1_50"); Add("R1", L_R1); AddMid("R1", "R2", L_R1, L_R2, "R12_50");
            Add("R2", L_R2); AddMid("R2", "R3", L_R2, L_R3, "R23_50"); Add("R3", L_R3); AddMid("R3", "R4", L_R3, L_R4, "R34_50");
            Add("R4", L_R4); AddMid("R4", "R5", L_R4, L_R5, "R45_50"); Add("R5", L_R5); AddMid("R5", "R6", L_R5, L_R6, "R56_50");
            Add("R6", L_R6); AddMid("R6", "R7", L_R6, L_R7, "R67_50"); Add("R7", L_R7); AddMid("R7", "R8", L_R7, L_R8, "R78_50"); Add("R8", L_R8);
            
            // Session
            Add("PDC", L_PDC); Add("PDH", L_PDH); Add("PDL", L_PDL); Add("VAH", L_VAH); Add("VAL", L_VAL); Add("Yest_POC", L_YestPOC); Add("Yest_VWAP", L_YestVWAP);
            Add("ONH", L_ONH); Add("ONL", L_ONL); Add("ON_MID", L_ON_MID); Add("ON_VWAP", L_ON_VWAP);
            Add("OPEN", L_OPEN); Add("ORH_5", L_ORH_5); Add("ORL_5", L_ORL_5); Add("ORM_5", L_ORM_5); 
            Add("ORH_30", L_ORH_30); Add("ORL_30", L_ORL_30); Add("IBH", L_IBH); Add("IBL", L_IBL);

            orderedLevelNames = levelMap.OrderByDescending(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();

		    if (SyncMode == SyncModeType.Sender) {
		        lock(mapLock) { // FIX: Lock during write
		            SharedLevelMap = new Dictionary<string, double>(levelMap);
		            SharedOrderedLevelNames = new List<string>(orderedLevelNames);
		        }
		    }
		}

        private void DrawCoreLines() 
        { 
            foreach (var kvp in levelMap) 
            { 
                if (kvp.Value <= 0) continue; 
                string k = kvp.Key;
                
                // 1. Group Assignments (Rewritten to avoid formatting errors)
                bool isOvernight = k.StartsWith("ON");
                
                bool isYesterday = false;
                if (k.StartsWith("PD")) isYesterday = true;
                if (k.StartsWith("Yest_")) isYesterday = true;

                bool isToday = false;
                if (!isOvernight) 
                {
                    if (!isYesterday) isToday = true;
                }

                // 2. Visibility Check (If off, destroy line)
                if (isOvernight &&!ShowOvernight) { RemoveDrawObject(k + "_Line"); continue; }
                if (isYesterday &&!ShowYesterday) { RemoveDrawObject(k + "_Line"); continue; }
                if (isToday &&!ShowToday) { RemoveDrawObject(k + "_Line"); continue; }

                NinjaTrader.Gui.Stroke activeStroke = Stroke_Mids; 

                // 3. Semantic Assignment (Rewritten to avoid formatting errors)
                if (isOvernight) { activeStroke = Stroke_Overnight; }
                else if (isYesterday) { activeStroke = Stroke_Yesterday; }
                else if (k == "VAH") { activeStroke = Stroke_ValueArea; }
                else if (k == "VAL") { activeStroke = Stroke_ValueArea; }
                else if (k == "POC") { activeStroke = Stroke_POC; }
                else if (k == "Yest_POC") { activeStroke = Stroke_POC; }
                else if (k == "ON_POC") { activeStroke = Stroke_POC; }
                else if (k.StartsWith("B5")) { activeStroke = Stroke_BullExtreme; }
                else if (k.StartsWith("B6")) { activeStroke = Stroke_BullExtreme; }
                else if (k.StartsWith("B7")) { activeStroke = Stroke_BullExtreme; }
                else if (k.StartsWith("B8")) { activeStroke = Stroke_BullExtreme; }
                else if (k.StartsWith("B3")) { activeStroke = Stroke_BullExpected; }
                else if (k.StartsWith("B4")) { activeStroke = Stroke_BullExpected; }
                else if (k.StartsWith("R3")) { activeStroke = Stroke_BearExpected; }
                else if (k.StartsWith("R4")) { activeStroke = Stroke_BearExpected; }
                else if (k.StartsWith("R5")) { activeStroke = Stroke_BearExtreme; }
                else if (k.StartsWith("R6")) { activeStroke = Stroke_BearExtreme; }
                else if (k.StartsWith("R7")) { activeStroke = Stroke_BearExtreme; }
                else if (k.StartsWith("R8")) { activeStroke = Stroke_BearExtreme; }
                else { activeStroke = Stroke_Mids; }

                // 4. Draw basic line first, then apply the Stroke object directly. 
                var line = Draw.HorizontalLine(this, k + "_Line", kvp.Value, activeStroke.Brush); 
                if (line!= null) 
                {
                    line.Stroke = activeStroke;
                }
            } 
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
		    // FIX: Guard against rendering before bars exist
		    if (ChartBars == null || Bars == null || Bars.Count == 0) return;
		
		    // 1. Draw VAH Line from Shared Memory
		    if (ShowYesterday && SharedLevelMap.ContainsKey("L_VAH"))
		    {
		        double vahPrice = SharedLevelMap["L_VAH"];
		        float startX = chartControl.GetXByBarIndex(ChartBars, Math.Max(0, Bars.Count - 100)); 
		        float endX = chartControl.GetXByBarIndex(ChartBars, Bars.Count - 1);                  
		        float yPos = chartScale.GetYByValue(vahPrice);
		        
		        SharpDX.Vector2 startPoint = new SharpDX.Vector2(startX, yPos);
		        SharpDX.Vector2 endPoint = new SharpDX.Vector2(endX, yPos);
		    
		        using (SharpDX.Direct2D1.Brush dxBrush = Stroke_ValueArea.Brush.ToDxBrush(RenderTarget))
		        {
		            // FIX: Removed incompatible StrokeStyle argument to prevent SharpDX crashes
		            RenderTarget.DrawLine(startPoint, endPoint, dxBrush, Stroke_ValueArea.Width);
		        }
		    }
		
		    if (!ShowHUD) return;
		
		    // 2. Initialize DX Resources
		    if (dxTextFormatLeft == null) dxTextFormatLeft = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Calibri", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 11.0f) { TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading, ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near };
		    if (dxBrushRed == null) dxBrushRed = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.Red);
		    if (dxBrushHUDPlan == null) dxBrushHUDPlan = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.DodgerBlue);
			if (dxBrushWhite == null) dxBrushWhite = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.White);
		
		    float leftX = (float)chartControl.CanvasLeft + 10; 
		    float bottomY = (float)ChartPanel.H - 160f; 
		    
		    // 3. Trade Lock Visual
		    if (!levelsValid) 
		    { 
		        RenderTarget.DrawText("!!! LEVEL INPUT ERROR - TRADES LOCKED !!!", dxTextFormatLeft, new SharpDX.RectangleF(leftX, bottomY, 400, 20), dxBrushRed); 
		        bottomY += 20; 
		    }
		    
		    // 4. Draw Session Stats
		    string statsLine = string.Format("SESSION | Trades: {0} | Wins: {1} | Loss: {2} | To Goal: {3}", sessionTrades, sessionWins, sessionLosses, (ProfitGoal - dailyPnL).ToString("C"));
		    RenderTarget.DrawText(statsLine, dxTextFormatLeft, new SharpDX.RectangleF(leftX, bottomY, 500, 20), dxBrushWhite); 
		    bottomY += 20;
		
		    // 5. Draw Dynamic Context
		    RenderTarget.DrawText("ZONE: " + zoneName, dxTextFormatLeft, new SharpDX.RectangleF(leftX, bottomY, 300, 20), dxBrushWhite); 
		    bottomY += 18;
		
		    foreach (string line in hud_LongPlan.Split('\n')) { RenderTarget.DrawText(line, dxTextFormatLeft, new SharpDX.RectangleF(leftX, bottomY, 300, 20), dxBrushHUDPlan); bottomY += 16; } 
		    bottomY += 8;
		    foreach (string line in hud_ShortPlan.Split('\n')) { RenderTarget.DrawText(line, dxTextFormatLeft, new SharpDX.RectangleF(leftX, bottomY, 300, 20), dxBrushHUDPlan); bottomY += 16; }
		}
		        
        private void CreateVisibilityToggles()
        {
            visibilityGrid = new System.Windows.Controls.Grid
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, 30, 0, 0)
            };

            visibilityGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(40) });
            visibilityGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(40) });
            visibilityGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(40) });

            btnToggleYest = CreateVisButton("Y", 0, ShowYesterday);
            btnToggleON = CreateVisButton("ON", 1, ShowOvernight);
            btnToggleToday = CreateVisButton("T", 2, ShowToday);

            visibilityGrid.Children.Add(btnToggleYest);
            visibilityGrid.Children.Add(btnToggleON);
            visibilityGrid.Children.Add(btnToggleToday);

            if (chartGrid!= null) chartGrid.Children.Add(visibilityGrid); 
        }

        private System.Windows.Controls.Button CreateVisButton(string label, int col, bool isActive)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = label,
                Background = isActive? WPFBrushes.SlateGray : WPFBrushes.DimGray,
                Foreground = WPFBrushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Margin = new Thickness(2)
            };
            
            btn.Click += (s, e) =>
            {
                if (label == "Y") { ShowYesterday =!ShowYesterday; btn.Background = ShowYesterday? WPFBrushes.SlateGray : WPFBrushes.DimGray; }
                if (label == "ON") { ShowOvernight =!ShowOvernight; btn.Background = ShowOvernight? WPFBrushes.SlateGray : WPFBrushes.DimGray; }
                if (label == "T") { ShowToday =!ShowToday; btn.Background = ShowToday? WPFBrushes.SlateGray : WPFBrushes.DimGray; }

            };

            System.Windows.Controls.Grid.SetColumn(btn, col);
            return btn;
        }
    }
}