// CC BY-NC 4.0
// Strategy: TrinityHUDv14
// Description: v1.13 Base with Modern Ergonomic HUD (Stacked Layout), Goal Tracking, and Reordered Playbook.
// Fixes: UI Stacked Vertically on Right, Resolved Ambiguous Brushes/Fonts, Restored IdentifyContext/CalculateTactics/ParseManualInputs.

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media; // used for WPF UI
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using WPFBrushes = System.Windows.Media.Brushes;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class TrinityHUDv14 : Strategy
    {
        // =========================================================
        //    1. CORE LEVELS & PROPERTIES
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

        // PERFORMANCE GOALS
        [NinjaScriptProperty, Display(Name = "Daily Profit Goal ($)", GroupName = "6. Performance")] public double ProfitGoal { get; set; } = 1500;
        [NinjaScriptProperty, Display(Name = "Daily Max Loss ($)", GroupName = "6. Performance")] public double DailyMaxLoss { get; set; } = 1000;

        // =========================================================
        //    2. ENGINE & VARIABLES
        // =========================================================
        private ADXGu5v2 gu5_Fast;
        private VOLMA volMa;
        private ATR atrAlgo;

        public enum TrailMode { None, BarNTrail, AtrRatchet }
        private int entryBar = -1;
        private double highSinceEntry = double.MinValue;
        private double lowSinceEntry = double.MaxValue;

        // PERFORMANCE TRACKING
        private double sessionStartCumProfit = 0;
        private int startTradeCount = 0, startWins = 0, startLosses = 0;
        private double dailyPnL = 0;
        private bool isGoalReached = false;
		private bool isCombosPopulated = false;

        // UI CONTROLS
        private Grid chartGrid, mainPanel;
        private Button btnScalpL, btnScalpS, btnCoreL, btnCoreS; // Auto Section
        private Button btnManScalpL, btnManScalpS, btnNextBarL, btnNextBarS; // Entry Section
        private Button btnHalfRisk, btnBreakeven, btnDisarm, btnCloseHalf, btnFlatten; // Modify Section
        private Button btnManualL, btnManualS; // Core Manual

        private ComboBox cbPlaybook;
        private ComboBox boxT1, boxT2, boxT3;
        private Label lblStatus, lblPnL;
        private Label lblTrades, lblWins, lblLoss, lblToGoal, lblToEOD; // New Metrics

        // UI STYLING (Explicitly System.Windows.Media to avoid ambiguity)
        private System.Windows.Media.FontFamily modernFont = new System.Windows.Media.FontFamily("Segoe UI");
        private DispatcherTimer flashTimer;
        private bool flashState = false;

        // FLAGS
        private bool isArmedScalpLong = false, isArmedScalpShort = false;
        private bool isArmedCoreLong = false, isArmedCoreShort = false;
        private bool isManualPendingL = false, isManualPendingS = false;
        private bool isManScalpPendingL = false, isManScalpPendingS = false;
        private bool isNextBarPendingL = false, isNextBarPendingS = false;
        private bool isHalfRiskPending = false;
        private bool isBreakevenPending = false;
        private bool isFlattenPending = false;
        private bool isCloseHalfPending = false;

        private string zoneName = "WAITING";
        private string activeStatus = "STANDBY", gateStatus = "-";
        private string allocStatus = "";
        private bool levelsValid = true;
        private double zoneHigh = 0, zoneLow = 0, levelAbove = 0, levelBelow = 0;

        private double longT1, longT2, longGatePrice, longEntry;
        private double shortT1, shortT2, shortGatePrice, shortEntry;
        private string hud_LongPlan = "", hud_ShortPlan = "";

        // Manual Target Variables
        private double manT1, manT2, manT3;
        private bool isManualActive = false;
        private double manualAnchorPrice = 0;
        private double manualMidLine = 0;

        private double userOverrideStopPrice = 0;
        private Dictionary<string, double> levelMap = new Dictionary<string, double>();
        private List<string> orderedLevelNames = new List<string>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Trinity HUD v14";
                Calculate = Calculate.OnPriceChange;
                EntriesPerDirection = 5;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsOverlay = true;

                // Defaults
                InitialStopAtrMult = 0.75; InitialStopTicks = 35;
                Qty1 = 2; Leg1TargetTicks = 40;
                Qty2 = 1; Leg2TargetAtrMult = 0.88; Leg2TrailMode = TrailMode.BarNTrail; Leg2BarN = 2;
                Qty3 = 1; Leg3TargetAtrMult = 1.5; Leg3TrailMode = TrailMode.AtrRatchet; Leg3RatchetAtrMult = 1.5;

                ContextMinutes = 1;
                BreakoutOffsetTicks = 1;
                SmartAllocTicks = 12;
                UseVolumeFilter = false;

                ManualStopBufferTicks = 5;
                PlaybookStopTicks = 12;
                BreakevenOffsetTicks = 5;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, ContextMinutes);
            }
            else if (State == State.DataLoaded)
            {
                gu5_Fast = ADXGu5v2(14, 14, 20, 35, 2, 14, 3, 60, 20);
                volMa = VOLMA(20);
                atrAlgo = ATR(14);
            }
            else if (State == State.Historical)
            {
                if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(() => { CreateWPFControls(); StartFlashTimer(); });
            }
            else if (State == State.Terminated)
            {
                if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(() => DisposeWPFControls());
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time) { if (State == State.Realtime) UpdateUI(); }
        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition) { if (State == State.Realtime) UpdateUI(); }

		protected override void OnBarUpdate()
		{
		    try
		    {
		        // --- RESET LEVELS LOGIC ---
		        if (ResetLevels)
		        {
		            L_B8 = L_B7 = L_B6 = L_B5 = L_B4 = L_B3 = L_B2 = L_B1 = 0;
		            L_POC = 0;
		            L_R1 = L_R2 = L_R3 = L_R4 = L_R5 = L_R6 = L_R7 = L_R8 = 0;
		            
		            ResetLevels = false; // Automatically uncheck the box
		            levelsValid = false; // Trigger error status until new levels are set
		            levelMap.Clear();    // Clear the map for the UI dropdowns
		        }
		
		        if (CurrentBar < 20) return;

                // --- FIX: One-time population of Dropdowns ---
                if (levelMap.Count == 0 || !isCombosPopulated)
                {
                    BuildLevelMap();
                    if (orderedLevelNames.Count > 0)
                    {
                        PopulateCombos();
                        isCombosPopulated = true;
                    }
                }
                // ---------------------------------------------

                ValidateLevels();
                if (!levelsValid)
                {
                    activeStatus = "ERROR: LEVEL MISMATCH";
                    Disarm();
                    return;
                }

                if (CurrentBar == 20 || (Bars.IsFirstBarOfSession && CurrentBar > 20)) DrawLevelsAndMids();

                if (Bars.IsFirstBarOfSession)
                {
                    sessionStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                    startTradeCount = SystemPerformance.AllTrades.Count;
                    startWins = SystemPerformance.AllTrades.WinningTrades.Count;
                    startLosses = SystemPerformance.AllTrades.LosingTrades.Count;
                    entryBar = -1;
                    isManualActive = false;
                    userOverrideStopPrice = 0;
                    isGoalReached = false;
                }

                // CALC PNL & GOAL
                dailyPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;
                if (dailyPnL >= ProfitGoal && !isGoalReached)
                {
                    isGoalReached = true;
                    activeStatus = "GOAL REACHED";
                    Disarm();
                }

                IdentifyContext();
                CalculateTactics();

                if (CurrentBar > 1)
                {
                    longGatePrice = High[1] + (BreakoutOffsetTicks * TickSize);
                    shortGatePrice = Low[1] - (BreakoutOffsetTicks * TickSize);
                }

                // ----------------------------------------------------
                // INSTANT EXECUTION BLOCK
                // ----------------------------------------------------
                if (isFlattenPending) { ExecuteFlatten(); return; }

                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    if (isHalfRiskPending) ExecuteHalfRiskLogic();
                    if (isBreakevenPending) ExecuteBreakevenLogic();
                    if (isCloseHalfPending) ExecuteCloseHalf();

                    if (isManualActive) ManageManualTrade();
                    else
                    {
                        if (IsFirstTickOfBar)
                        {
                            highSinceEntry = Math.Max(highSinceEntry, High[1]);
                            lowSinceEntry = Math.Min(lowSinceEntry, Low[1]);
                            UpdateTrailingStops();
                        }
                    }
                }
                else
                {
                    entryBar = -1; highSinceEntry = double.MinValue; lowSinceEntry = double.MaxValue;
                    isManualActive = false; userOverrideStopPrice = 0;
                }

                if (isManualPendingL) { ExecuteManualTrade(true); return; }
                if (isManualPendingS) { ExecuteManualTrade(false); return; }
                if (isManScalpPendingL) { ExecuteManualScalp(true); return; }
                if (isManScalpPendingS) { ExecuteManualScalp(false); return; }

                // ----------------------------------------------------
                // AUTOMATED & NEXT BAR LOGIC (First Tick Only)
                // ----------------------------------------------------
                if (IsFirstTickOfBar && !isGoalReached)
                {
                    if (isNextBarPendingL) { ExecuteManualScalp(true); isNextBarPendingL = false; activeStatus = "NB LONG EXEC"; Disarm(); return; }
                    if (isNextBarPendingS) { ExecuteManualScalp(false); isNextBarPendingS = false; activeStatus = "NB SHORT EXEC"; Disarm(); return; }

                    if (!isArmedScalpLong && !isArmedScalpShort && !isArmedCoreLong && !isArmedCoreShort) return;

                    double sig = gu5_Fast.ConditionSeries[0];
                    bool volCondition = !UseVolumeFilter || (Volume[1] > volMa[1]);

                    bool validLong = ((sig == 1.0) || (sig == 0.5)) && volCondition;
                    bool validShort = ((sig == -1.0) || (sig == -0.5)) && volCondition;

                    if (isArmedCoreLong && validLong && Close[1] >= longGatePrice) ExecuteCoreTrade(true);
                    else if (isArmedCoreShort && validShort && Close[1] <= shortGatePrice) ExecuteCoreTrade(false);

                    if (isArmedScalpLong && validLong) ExecuteScalpMoonRunner(true);
                    else if (isArmedScalpShort && validShort) ExecuteScalpMoonRunner(false);
                }
            }
            catch (Exception e) { Print("Trinity Error: " + e.Message); }
        }

        // =========================================================
        //    EXECUTION LOGIC
        // =========================================================
		private void ExecuteFlatten()
        {
            // Close all positions immediately
            if (Position.MarketPosition == MarketPosition.Long) ExitLong();
            else if (Position.MarketPosition == MarketPosition.Short) ExitShort();

            // Cancel any working orders (Entries or Exits)
            foreach (Order o in Orders)
            {
                if (o != null && o.OrderState == OrderState.Working) CancelOrder(o);
            }
            
            // Reset all flags
            isFlattenPending = false;
            userOverrideStopPrice = 0;
            Disarm(); // Prevents auto-trader from firing immediately after flatten
            
            activeStatus = "FLATTENED";
            UpdateUI();
        }

        private void ExecuteCloseHalf()
        {
            // Only works if we have a position
            if (Position.MarketPosition == MarketPosition.Flat) { isCloseHalfPending = false; return; }
            
            int qty = Position.Quantity;
            if (qty <= 1) return; // Cannot split 1 contract

            int exitQty = (int)Math.Ceiling(qty / 2.0);
            if (Position.MarketPosition == MarketPosition.Long) ExitLong(exitQty, "CloseHalf", "");
            else ExitShort(exitQty, "CloseHalf", "");

            Print("CLOSED 50%: " + exitQty + " Contracts");
            isCloseHalfPending = false;
        }

        private void ExecuteBreakevenLogic()
        {
            if (Position.MarketPosition == MarketPosition.Flat) { isBreakevenPending = false; return; }
            
            // Calculate BE price
            double bePrice = Position.MarketPosition == MarketPosition.Long
                ? Position.AveragePrice + (BreakevenOffsetTicks * TickSize)
                : Position.AveragePrice - (BreakevenOffsetTicks * TickSize);

            // Commit the new stop price
            userOverrideStopPrice = bePrice;
            ApplyOverrideStop(bePrice);
            
            Print("MOVED TO BE+" + BreakevenOffsetTicks);
            isBreakevenPending = false;
        }

        private void ExecuteHalfRiskLogic()
        {
            if (Position.MarketPosition == MarketPosition.Flat) { isHalfRiskPending = false; return; }
            
            double currentStop = GetCurrentStop();
            // If no stop found, assume default 20 ticks for calculation safety
            if (currentStop == 0) currentStop = Position.MarketPosition == MarketPosition.Long ? Position.AveragePrice - (20*TickSize) : Position.AveragePrice + (20*TickSize);

            double currentPrice = Close[0];
            
            // Move stop 50% closer to current price
            double newStop = (currentStop + currentPrice) / 2.0;
            
            // Round to nearest tick
            newStop = Math.Round(newStop / TickSize) * TickSize;

            userOverrideStopPrice = newStop;
            ApplyOverrideStop(newStop);
            
            Print("HALF RISK EXECUTED. New Stop: " + newStop);
            isHalfRiskPending = false;
        }

        private double GetCurrentStop()
        {
            foreach (Order o in Orders)
            {
                if (o != null && o.OrderState == OrderState.Working && o.OrderType == OrderType.StopMarket) return o.StopPrice;
            }
            return Position.MarketPosition == MarketPosition.Long ? Position.AveragePrice - (20 * TickSize) : Position.AveragePrice + (20 * TickSize);
        }

        private void ApplyOverrideStop(double price)
        {
            string[] sigs = { "Scalp_L1", "Scalp_L2", "Scalp_L3", "Scalp_S1", "Scalp_S2", "Scalp_S3", "Core_L_Mid", "Core_L_Full", "Core_S_Mid", "Core_S_Full", "Man_L", "Man_L_2", "Man_L_3", "Man_S", "Man_S_2", "Man_S_3", "MScalp_L1", "MScalp_L2", "MScalp_L3", "MScalp_S1", "MScalp_S2", "MScalp_S3" };
            foreach (string s in sigs) SetStopLoss(s, CalculationMode.Price, price, false);
        }

        private void ExecuteManualScalp(bool isLong)
        {
            double limitPrice = isLong ? Close[0] + (5 * TickSize) : Close[0] - (5 * TickSize);
            entryBar = CurrentBar; highSinceEntry = High[0]; lowSinceEntry = Low[0];
            double currentAtr = atrAlgo[0];
            double t1Ticks = Leg1TargetTicks;
            double t2Ticks = Math.Max(5, Math.Round((currentAtr * Leg2TargetAtrMult) / TickSize));
            double t3Ticks = Math.Max(10, Math.Round((currentAtr * Leg3TargetAtrMult) / TickSize));
            double stopTicks = Math.Max(5, Math.Round((currentAtr * InitialStopAtrMult) / TickSize));
            string dir = isLong ? "L" : "S";

            isManualActive = false;
            allocStatus = "MAN SCALP: " + dir;
            userOverrideStopPrice = 0;

            if (Qty1 > 0) { string sig = "MScalp_" + dir + "1"; SetStopLoss(sig, CalculationMode.Ticks, stopTicks, false); SetProfitTarget(sig, CalculationMode.Ticks, t1Ticks); if (isLong) EnterLongLimit(Qty1, limitPrice, sig); else EnterShortLimit(Qty1, limitPrice, sig); }
            if (Qty2 > 0) { string sig = "MScalp_" + dir + "2"; SetStopLoss(sig, CalculationMode.Ticks, stopTicks, false); SetProfitTarget(sig, CalculationMode.Ticks, t2Ticks); if (isLong) EnterLongLimit(Qty2, limitPrice, sig); else EnterShortLimit(Qty2, limitPrice, sig); }
            if (Qty3 > 0) { string sig = "MScalp_" + dir + "3"; SetStopLoss(sig, CalculationMode.Ticks, stopTicks, false); SetProfitTarget(sig, CalculationMode.Ticks, t3Ticks); if (isLong) EnterLongLimit(Qty3, limitPrice, sig); else EnterShortLimit(Qty3, limitPrice, sig); }

            isManScalpPendingL = false; isManScalpPendingS = false;
            Disarm();
        }

		private void ExecuteManualTrade(bool isLong)
        {
            // 1. Setup Anchors
            manualAnchorPrice = isLong ? zoneLow : zoneHigh;
            if (manualAnchorPrice == 0) manualAnchorPrice = Close[0];
            manualMidLine = (zoneHigh + zoneLow) / 2.0;
            if (manualMidLine == 0) manualMidLine = Close[0];

            int stopTicks = (cbPlaybook != null && cbPlaybook.SelectedIndex > 0) ? PlaybookStopTicks : ManualStopBufferTicks;
            double stopPrice = isLong ? manualAnchorPrice - (stopTicks * TickSize) : manualAnchorPrice + (stopTicks * TickSize);

            double t1 = manT1;
            double t2 = manT2;
            double t3 = manT3;

            // Fallback
            double entry = Close[0];
            if (t1 == 0) t1 = isLong ? entry + (20 * TickSize) : entry - (20 * TickSize);
            if (t2 == 0 && Qty2 > 0) t2 = isLong ? entry + (40 * TickSize) : entry - (40 * TickSize); // Only set if needed
            if (t3 == 0 && Qty3 > 0) t3 = isLong ? entry + (60 * TickSize) : entry - (60 * TickSize);

            string sig = "Man_" + (isLong ? "L" : "S");
            isManualActive = true;
            userOverrideStopPrice = 0;

            // Submit Orders
            SetStopLoss(sig, CalculationMode.Price, stopPrice, false);
            if (t2 > 0) SetStopLoss(sig + "_2", CalculationMode.Price, stopPrice, false);
            if (t3 > 0) SetStopLoss(sig + "_3", CalculationMode.Price, stopPrice, false);

            if (isLong)
            {
                EnterLong(2, sig); SetProfitTarget(sig, CalculationMode.Price, t1);
                if (t2 > 0) { EnterLong(1, sig + "_2"); SetProfitTarget(sig + "_2", CalculationMode.Price, t2); }
                if (t3 > 0) { EnterLong(1, sig + "_3"); SetProfitTarget(sig + "_3", CalculationMode.Price, t3); }
            }
            else
            {
                EnterShort(2, sig); SetProfitTarget(sig, CalculationMode.Price, t1);
                if (t2 > 0) { EnterShort(1, sig + "_2"); SetProfitTarget(sig + "_2", CalculationMode.Price, t2); }
                if (t3 > 0) { EnterShort(1, sig + "_3"); SetProfitTarget(sig + "_3", CalculationMode.Price, t3); }
            }

            isManualPendingL = false; isManualPendingS = false;
            allocStatus = "MANUAL: STRUCTURAL";
            Disarm();
        }

        private void ManageManualTrade()
        {
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (Close[0] > manualMidLine + TickSize)
                {
                    double be = Position.AveragePrice;
                    if (userOverrideStopPrice == 0 || be > userOverrideStopPrice) ApplyOverrideStop(be);
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (Close[0] < manualMidLine - TickSize)
                {
                    double be = Position.AveragePrice;
                    if (userOverrideStopPrice == 0 || be < userOverrideStopPrice) ApplyOverrideStop(be);
                }
            }
        }

        private void ExecuteCoreTrade(bool isLong)
        {
            string signal = "Core_" + (isLong ? "L" : "S");
            double t1 = isLong ? longT1 : shortT1;
            double t2 = isLong ? longT2 : shortT2;

            double distToT1 = Math.Abs(t1 - Close[0]);
            bool useSmart = distToT1 <= (SmartAllocTicks * TickSize);
            userOverrideStopPrice = 0;

            SetStopLoss(signal, CalculationMode.Ticks, InitialStopTicks, false);

            if (useSmart)
            {
                allocStatus = "SMART ALLOC";
                int totalQty = 4;
                if (isLong) { EnterLong(totalQty, signal + "_Full"); SetProfitTarget(signal + "_Full", CalculationMode.Price, t2); }
                else { EnterShort(totalQty, signal + "_Full"); SetProfitTarget(signal + "_Full", CalculationMode.Price, t2); }
            }
            else
            {
                allocStatus = "STD ALLOC";
                if (isLong) { EnterLong(2, signal + "_Mid"); EnterLong(2, signal + "_Full"); SetProfitTarget(signal + "_Mid", CalculationMode.Price, t1); SetProfitTarget(signal + "_Full", CalculationMode.Price, t2); }
                else { EnterShort(2, signal + "_Mid"); EnterShort(2, signal + "_Full"); SetProfitTarget(signal + "_Mid", CalculationMode.Price, t1); SetProfitTarget(signal + "_Full", CalculationMode.Price, t2); }
            }
            Disarm();
        }

        private void ExecuteScalpMoonRunner(bool isLong)
        {
            isManualActive = false;
            entryBar = CurrentBar; highSinceEntry = High[0]; lowSinceEntry = Low[0];
            double currentAtr = atrAlgo[0];
            double t1Ticks = Leg1TargetTicks;
            double t2Ticks = Math.Max(5, Math.Round((currentAtr * Leg2TargetAtrMult) / TickSize));
            double t3Ticks = Math.Max(10, Math.Round((currentAtr * Leg3TargetAtrMult) / TickSize));
            double stopTicks = Math.Max(5, Math.Round((currentAtr * InitialStopAtrMult) / TickSize));
            string dir = isLong ? "L" : "S";

            allocStatus = "SCALP MOON";
            userOverrideStopPrice = 0;

            if (Qty1 > 0) { string sig = "Scalp_" + dir + "1"; SetStopLoss(sig, CalculationMode.Ticks, stopTicks, false); SetProfitTarget(sig, CalculationMode.Ticks, t1Ticks); if (isLong) EnterLong(Qty1, sig); else EnterShort(Qty1, sig); }
            if (Qty2 > 0) { string sig = "Scalp_" + dir + "2"; SetStopLoss(sig, CalculationMode.Ticks, stopTicks, false); SetProfitTarget(sig, CalculationMode.Ticks, t2Ticks); if (isLong) EnterLong(Qty2, sig); else EnterShort(Qty2, sig); }
            if (Qty3 > 0) { string sig = "Scalp_" + dir + "3"; SetStopLoss(sig, CalculationMode.Ticks, stopTicks, false); SetProfitTarget(sig, CalculationMode.Ticks, t3Ticks); if (isLong) EnterLong(Qty3, sig); else EnterShort(Qty3, sig); }
            Disarm();
        }

        private void UpdateTrailingStops()
        {
            if (Qty2 > 0) ApplyTrail(Qty2, "Scalp_L2", "Scalp_S2", Leg2TrailMode, Leg2BarN, 0);
            if (Qty3 > 0) ApplyTrail(Qty3, "Scalp_L3", "Scalp_S3", Leg3TrailMode, 0, Leg3RatchetAtrMult);
        }

        private void ApplyTrail(int qty, string longSig, string shortSig, TrailMode mode, int barN, double atrMult)
        {
            if (mode == TrailMode.None && userOverrideStopPrice == 0) return;

            bool isLong = Position.MarketPosition == MarketPosition.Long;
            string signal = isLong ? longSig : shortSig;
            double newStop = 0;

            if (mode == TrailMode.BarNTrail)
            {
                int idx = Math.Min(barN, CurrentBar);
                if (isLong) newStop = Low[idx]; else newStop = High[idx];
            }
            else if (mode == TrailMode.AtrRatchet)
            {
                double rat = atrAlgo[0] * atrMult;
                if (isLong) newStop = highSinceEntry - rat; else newStop = lowSinceEntry + rat;
            }

            if (userOverrideStopPrice != 0)
            {
                if (newStop == 0) newStop = userOverrideStopPrice;
                else
                {
                    if (isLong) newStop = Math.Max(newStop, userOverrideStopPrice);
                    else newStop = Math.Min(newStop, userOverrideStopPrice);
                }
            }

            if (newStop != 0) SetStopLoss(signal, CalculationMode.Price, newStop, false);
        }

        // =========================================================
        //    UI & HELPER
        // =========================================================

        private void IdentifyContext()
        {
            double p = Close[0];
            if (CheckZone(p, L_B8, L_B7, "B8 -> B7")) { levelAbove = L_B8 + 100; levelBelow = L_B6; }
            else if (CheckZone(p, L_B7, L_B6, "B7 -> B6")) { levelAbove = L_B8; levelBelow = L_B5; }
            else if (CheckZone(p, L_B6, L_B5, "B6 -> B5")) { levelAbove = L_B7; levelBelow = L_B4; }
            else if (CheckZone(p, L_B5, L_B4, "B5 -> B4")) { levelAbove = L_B6; levelBelow = L_B3; }
            else if (CheckZone(p, L_B4, L_B3, "B4 -> B3")) { levelAbove = L_B5; levelBelow = L_B2; }
            else if (CheckZone(p, L_B3, L_B2, "B3 -> B2")) { levelAbove = L_B4; levelBelow = L_B1; }
            else if (CheckZone(p, L_B2, L_B1, "B2 -> B1")) { levelAbove = L_B3; levelBelow = L_POC; }
            else if (CheckZone(p, L_B1, L_POC, "B1 -> POC")) { levelAbove = L_B2; levelBelow = L_R1; }
            else if (CheckZone(p, L_POC, L_R1, "POC -> R1")) { levelAbove = L_B1; levelBelow = L_R2; }
            else if (CheckZone(p, L_R1, L_R2, "R1 -> R2")) { levelAbove = L_POC; levelBelow = L_R3; }
            else if (CheckZone(p, L_R2, L_R3, "R2 -> R3")) { levelAbove = L_R1; levelBelow = L_R4; }
            else if (CheckZone(p, L_R3, L_R4, "R3 -> R4")) { levelAbove = L_R2; levelBelow = L_R5; }
            else if (CheckZone(p, L_R4, L_R5, "R4 -> R5")) { levelAbove = L_R3; levelBelow = L_R6; }
            else if (CheckZone(p, L_R5, L_R6, "R5 -> R6")) { levelAbove = L_R4; levelBelow = L_R7; }
            else if (CheckZone(p, L_R6, L_R7, "R6 -> R7")) { levelAbove = L_R5; levelBelow = L_R8; }
            else if (CheckZone(p, L_R7, L_R8, "R7 -> R8")) { levelAbove = L_R6; levelBelow = L_R8 - 100; }
            else if (L_R8 > 0 && p < L_R8) { SetZone("BSMT (Below R8)", L_R8 - 100, L_R8); levelAbove = L_R7; levelBelow = L_R8 - 200; }
            else if (L_B8 > 0 && p > L_B8) { SetZone("SKY (Above B8)", L_B8, L_B8 + 100); levelAbove = L_B8 + 200; levelBelow = L_B7; }
            else { zoneName = "WAITING"; }
        }

        private bool CheckZone(double p, double top, double bot, string name)
        {
            if (top > 0 && bot > 0 && p <= top && p >= bot) { SetZone(name, top, bot); return true; }
            return false;
        }

        private void SetZone(string name, double high, double low)
        {
            zoneName = name; zoneHigh = high; zoneLow = low;
        }

        // =========================================================
        //   CRITICAL UPDATE: SMART PROXIMITY LOGIC
        // =========================================================
        private void CalculateTactics()
        {
            // Calculate the Range of the current zone
            double range = zoneHigh - zoneLow;
            double price = Close[0];
            
            // Safety check to prevent errors if zone is invalid
            if (range <= 0) return;

            // 1. Determine Position within the Zone
            // We split the zone into upper and lower quadrants to decide "Tactics"
            bool isNearTop = (price > zoneLow + (0.75 * range)); // Upper 25% of the zone
            bool isNearBot = (price < zoneLow + (0.25 * range)); // Lower 25% of the zone

            // 2. Short Logic (The "Smart Flip")
            if (isNearTop) 
            {
                // SITUATION: We are at the CEILING of the room (e.g. Price is 24794, Zone Top is 24780/R2).
                // OLD LOGIC: Would look for a breakdown of the FLOOR (R3), which is too far away.
                // NEW LOGIC: We plan a "Rejection" trade. If it fails back under the ceiling, we short to the floor.
                
                shortEntry = zoneHigh - TickSize;    // Entry: Break back under the Top Level
                shortT2 = zoneLow;                   // Target 2: The Bottom of current zone
                shortT1 = (shortEntry + shortT2) / 2.0; // Target 1: 50% Midline
                
                hud_ShortPlan = string.Format("SHORT (Rejection):\n Fail <   {0:N2}\n T1(50%)  {1:N2}\n T2(Lvl)  {2:N2}", shortEntry, shortT1, shortT2);
            } 
            else 
            {
                // SITUATION: We are in the middle or bottom of the room.
                // LOGIC: Standard Breakout. We want to break the floor to go to the basement.
                
                shortEntry = zoneLow - TickSize;     // Entry: Break the Floor
                shortT2 = levelBelow;                // Target 2: The Next Level Down
                shortT1 = (shortEntry + shortT2) / 2.0;
                
                hud_ShortPlan = string.Format("SHORT (Breakout):\n Break <  {0:N2}\n T1(50%)  {1:N2}\n T2(Lvl)  {2:N2}", shortEntry, shortT1, shortT2);
            }

            // 3. Long Logic (The "Smart Flip")
            if (isNearBot) 
            {
                // SITUATION: We are at the FLOOR of the room.
                // NEW LOGIC: Plan a "Bounce" trade. If it holds the floor, we go to the ceiling.
                
                longEntry = zoneLow + TickSize;      // Entry: Bounce off Bottom Level
                longT2 = zoneHigh;                   // Target 2: The Top of current zone
                longT1 = (longEntry + longT2) / 2.0;
                
                hud_LongPlan = string.Format("LONG (Bounce):\n Hold >   {0:N2}\n T1(50%)  {1:N2}\n T2(Lvl)  {2:N2}", longEntry, longT1, longT2);
            } 
            else 
            {
                // SITUATION: We are in the middle or top of the room.
                // LOGIC: Standard Breakout. We want to break the ceiling to go upstairs.
                
                longEntry = zoneHigh + TickSize;     // Entry: Break the Ceiling
                longT2 = levelAbove;                 // Target 2: The Next Level Up
                longT1 = (longEntry + longT2) / 2.0;
                
                hud_LongPlan = string.Format("LONG (Breakout):\n Break >  {0:N2}\n T1(50%)  {1:N2}\n T2(Lvl)  {2:N2}", longEntry, longT1, longT2);
            }
        }

		private void ParseManualInputs()
        {
            Double.TryParse(boxT1.Text, out manT1);
            Double.TryParse(boxT2.Text, out manT2);
            Double.TryParse(boxT3.Text, out manT3);
        }

		private void CreateWPFControls()
		{
		    chartGrid = ChartControl.Parent as Grid; if (chartGrid == null) return;
            
            // --- UPDATED ALIGNMENT: STACKED ---
            // Top Margin: 250 (pushes it down below the cyan text)
            // Right Margin: 110 (Since width is 170, Left edge = 280, aligning with text)
		    mainPanel = new Grid { Width = 170, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 250, 110, 0) };
		
		    for (int i = 0; i < 26; i++) mainPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(i == 1 || i == 7 || i == 10 || i == 18 || i == 22 ? 8 : 26) });
		
		    lblStatus = LabelStyle("STANDBY", System.Windows.Media.Brushes.White, System.Windows.Media.Brushes.DimGray);
		    lblPnL = LabelStyle("$0.00", System.Windows.Media.Brushes.Lime, System.Windows.Media.Brushes.Black);
		    lblTrades = DetailLabel("# Trades: 0"); lblWins = DetailLabel("# Wins: 0"); lblLoss = DetailLabel("# Loss: 0");
		    lblToGoal = DetailLabel("$ to Goal: 0"); lblToEOD = DetailLabel("$ to EOD: 0");
		
		    AddRow(lblStatus, 0); AddRow(lblPnL, 2);
		    AddRow(lblTrades, 3); AddRow(lblWins, 4); AddRow(lblLoss, 5);
		    AddRow(lblToGoal, 6); AddRow(lblToEOD, 8);
		
		    Label lblAuto = new Label { Content = "AUTO TRADER", Foreground = System.Windows.Media.Brushes.Orange, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Bold, FontFamily = modernFont };
		    AddRow(lblAuto, 9);
		
		    btnScalpL = Btn("SCALP L", System.Windows.Media.Brushes.DimGray); btnScalpS = Btn("SCALP S", System.Windows.Media.Brushes.DimGray);
		    btnCoreL = Btn("CORE L", System.Windows.Media.Brushes.DimGray); btnCoreS = Btn("CORE S", System.Windows.Media.Brushes.DimGray);
		    AddDualRow(btnScalpL, btnScalpS, 11); AddDualRow(btnCoreL, btnCoreS, 12);
		
		    cbPlaybook = new ComboBox { FontSize = 10, Height = 22, Margin = new Thickness(2), FontFamily = modernFont, FontWeight = FontWeights.Bold };
		    string[] plays = { "Manual / Custom", "1. Trend B2->B4", "2. Trend R2->R4", "3. Rot B3->B2", "4. Rot R3->R2", "5. Rot B2->R2", "6. Rot R2->B2", "7. B1->R1", "8. R1->B1", "9. B6->B8 (NEW)", "10. R6->R8 (NEW)", "11. Trend B3->B1", "12. Trend R3->R1" };
		    foreach (var p in plays) cbPlaybook.Items.Add(p);
		    cbPlaybook.SelectedIndex = 0; cbPlaybook.SelectionChanged += CbPlaybook_SelectionChanged;
		    AddRow(cbPlaybook, 14);
		
		    boxT1 = Combo(); boxT2 = Combo(); boxT3 = Combo();
		    AddRow(boxT1, 15); AddRow(boxT2, 16); AddRow(boxT3, 17);
		
		    btnManScalpL = Btn("SCALP", System.Windows.Media.Brushes.DimGray); btnManScalpS = Btn("SCALP", System.Windows.Media.Brushes.DimGray);
		    btnNextBarL = Btn("NEXT", System.Windows.Media.Brushes.DimGray); btnNextBarS = Btn("NEXT", System.Windows.Media.Brushes.DimGray);
		    btnManualL = Btn("CORE", System.Windows.Media.Brushes.DimGray); btnManualS = Btn("CORE", System.Windows.Media.Brushes.DimGray);
		
		    AddDualRow(btnManScalpL, btnManScalpS, 19);
		    AddDualRow(btnNextBarL, btnNextBarS, 20);
		    AddDualRow(btnManualL, btnManualS, 21);
		
		    btnHalfRisk = SolidBtn("50% RISK", System.Windows.Media.Brushes.Orange);
		    btnBreakeven = SolidBtn("BE + " + BreakevenOffsetTicks, System.Windows.Media.Brushes.DodgerBlue);
		    btnDisarm = SolidBtn("DISARM", System.Windows.Media.Brushes.DarkGray);
		    btnCloseHalf = SolidBtn("CLOSE 50%", System.Windows.Media.Brushes.Yellow); btnCloseHalf.Foreground = System.Windows.Media.Brushes.Black;
		    AddDualRow(btnHalfRisk, btnBreakeven, 23); AddDualRow(btnDisarm, btnCloseHalf, 24);
		
		    btnFlatten = SolidBtn("FLATTEN", System.Windows.Media.Brushes.Red); btnFlatten.Height = 30;
		    mainPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) });
		    AddRow(btnFlatten, 25);
		
		    // Click Handlers
		    btnScalpL.Click += (s, e) => { bool p = isArmedScalpLong; Disarm(); isArmedScalpLong = !p; activeStatus = isArmedScalpLong ? "ARM SCALP L" : "STANDBY"; UpdateUI(); };
		    btnScalpS.Click += (s, e) => { bool p = isArmedScalpShort; Disarm(); isArmedScalpShort = !p; activeStatus = isArmedScalpShort ? "ARM SCALP S" : "STANDBY"; UpdateUI(); };
		    btnCoreL.Click += (s, e) => { bool p = isArmedCoreLong; Disarm(); isArmedCoreLong = !p; activeStatus = isArmedCoreLong ? "ARM CORE L" : "STANDBY"; UpdateUI(); };
		    btnCoreS.Click += (s, e) => { bool p = isArmedCoreShort; Disarm(); isArmedCoreShort = !p; activeStatus = isArmedCoreShort ? "ARM CORE S" : "STANDBY"; UpdateUI(); };
		
		    btnManScalpL.Click += (s, e) => { isManScalpPendingL = true; activeStatus = "SCALP SENT..."; btnManScalpL.Background = System.Windows.Media.Brushes.LimeGreen; UpdateUI(); };
		    btnManScalpS.Click += (s, e) => { isManScalpPendingS = true; activeStatus = "SCALP SENT..."; btnManScalpS.Background = System.Windows.Media.Brushes.Red; UpdateUI(); };
		    btnNextBarL.Click += (s, e) => { bool p = isNextBarPendingL; Disarm(); isNextBarPendingL = !p; activeStatus = isNextBarPendingL ? "WAIT NEXT L" : "STANDBY"; UpdateUI(); };
		    btnNextBarS.Click += (s, e) => { bool p = isNextBarPendingS; Disarm(); isNextBarPendingS = !p; activeStatus = isNextBarPendingS ? "WAIT NEXT S" : "STANDBY"; UpdateUI(); };
		    
		    btnManualL.Click += (s, e) => { 
		        ParseManualInputs(); 
		        ExecuteManualTrade(true); 
		        activeStatus = "CORE LONG SENT"; 
		        btnManualL.Background = System.Windows.Media.Brushes.LimeGreen; 
		        UpdateUI(); 
		    };
		    btnManualS.Click += (s, e) => { 
		        ParseManualInputs(); 
		        ExecuteManualTrade(false); 
		        activeStatus = "CORE SHORT SENT"; 
		        btnManualS.Background = System.Windows.Media.Brushes.Red; 
		        UpdateUI(); 
		    };
		
		    btnDisarm.Click += (s, e) => Disarm(); 
		    btnFlatten.Click += (s, e) => isFlattenPending = true;
		    btnHalfRisk.Click += (s, e) => isHalfRiskPending = true; 
		    btnBreakeven.Click += (s, e) => isBreakevenPending = true;
		    btnCloseHalf.Click += (s, e) => isCloseHalfPending = true;
		
		    BuildLevelMap();
		    chartGrid.Children.Add(mainPanel);
		}

		private void BuildLevelMap()
        {
            levelMap.Clear(); orderedLevelNames.Clear();
            
            // Helper to add main levels
            void Add(string n, double v) { 
                if (v > 0) { levelMap[n] = v; orderedLevelNames.Add(n); } 
            }
            
            // Helper to add midpoints
            void AddMid(string n1, string n2, double v1, double v2, string alias) { 
                if (v1 > 0 && v2 > 0) { 
                    double mid = (v1 + v2) / 2.0; 
                    levelMap[alias] = mid; 
                    orderedLevelNames.Add(alias); 
                } 
            }

            // 1. Build sequence High to Low (B8 -> R8)
            // This order determines the Dropdown order
            Add("B8", L_B8);      AddMid("B8", "B7", L_B8, L_B7, "B87_50");
            Add("B7", L_B7);      AddMid("B7", "B6", L_B7, L_B6, "B76_50");
            Add("B6", L_B6);      AddMid("B6", "B5", L_B6, L_B5, "B65_50");
            Add("B5", L_B5);      AddMid("B5", "B4", L_B5, L_B4, "B54_50");
            Add("B4", L_B4);      AddMid("B4", "B3", L_B4, L_B3, "B43_50");
            Add("B3", L_B3);      AddMid("B3", "B2", L_B3, L_B2, "B32_50");
            Add("B2", L_B2);      AddMid("B2", "B1", L_B2, L_B1, "B21_50");
            Add("B1", L_B1);      AddMid("B1", "POC", L_B1, L_POC, "B1_POC_50");
            
            Add("POC", L_POC);    AddMid("POC", "R1", L_POC, L_R1, "POC_R1_50");
            
            Add("R1", L_R1);      AddMid("R1", "R2", L_R1, L_R2, "R12_50");
            Add("R2", L_R2);      AddMid("R2", "R3", L_R2, L_R3, "R23_50");
            Add("R3", L_R3);      AddMid("R3", "R4", L_R3, L_R4, "R34_50");
            Add("R4", L_R4);      AddMid("R4", "R5", L_R4, L_R5, "R45_50");
            Add("R5", L_R5);      AddMid("R5", "R6", L_R5, L_R6, "R56_50");
            Add("R6", L_R6);      AddMid("R6", "R7", L_R6, L_R7, "R67_50");
            Add("R7", L_R7);      AddMid("R7", "R8", L_R7, L_R8, "R78_50");
            Add("R8", L_R8);

            PopulateCombos();
        }

		private void PopulateCombos()
		        {
		            if (boxT1 == null) return;
		            
		            ChartControl.Dispatcher.InvokeAsync(() => {
		                if (boxT1.Items.Count > 0) return;
		
		                foreach (string name in orderedLevelNames)
		                {
		                    boxT1.Items.Add(name);
		                    boxT2.Items.Add(name);
		                    boxT3.Items.Add(name);
		                }
		            });
		        }

		private void StartFlashTimer()
        {
            flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            flashTimer.Tick += (s, e) => { flashState = !flashState; if (isGoalReached) UpdateUI(); };
            flashTimer.Start();
        }
		
        private ComboBox Combo()
        {
            var c = new ComboBox { IsEditable = true, FontSize = 10, Margin = new Thickness(2), Height = 20, FontFamily = modernFont };
            c.DropDownClosed += (s, e) => {
                ComboBox cb = s as ComboBox;
                string sel = cb.SelectedItem as string;
                if (sel != null && levelMap.ContainsKey(sel)) cb.Text = levelMap[sel].ToString("F2");
            };
            return c;
        }

		private void CbPlaybook_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
		    int idx = cbPlaybook.SelectedIndex; 
		    if (idx == 0) return;
		
		    double t1 = 0, t2 = 0, t3 = 0;
		    
		    // 12-PLAY PLAYBOOK LOGIC
		    if (idx == 1) { t1 = (L_B2 + L_B3) / 2; t2 = L_B3; t3 = L_B4; }      // 1. Trend B2->B4
		    else if (idx == 2) { t1 = (L_R2 + L_R3) / 2; t2 = L_R3; t3 = L_R4; } // 2. Trend R2->R4
		    else if (idx == 3) { t1 = (L_B3 + L_B2) / 2; t2 = L_B2; t3 = L_B1; } // 3. Rot B3->B2
		    else if (idx == 4) { t1 = (L_R3 + L_R2) / 2; t2 = L_R2; t3 = L_R1; } // 4. Rot R3->R2
		    else if (idx == 5) { t1 = (L_B2 + L_B1) / 2; t2 = L_B1; t3 = L_R2; } // 5. Rot B2->R2
		    else if (idx == 6) { t1 = (L_R2 + L_R1) / 2; t2 = L_R1; t3 = L_B2; } // 6. Rot R2->B2
		    else if (idx == 7) { t1 = (L_B1 + L_POC) / 2; t2 = L_POC; t3 = L_R1; } // 7. B1->R1
		    else if (idx == 8) { t1 = (L_R1 + L_POC) / 2; t2 = L_POC; t3 = L_B1; } // 8. R1->B1
		    else if (idx == 9) { t1 = (L_B6 + L_B7) / 2; t2 = L_B7; t3 = L_B8; }   // 9. B6->B8
		    else if (idx == 10) { t1 = (L_R6 + L_R7) / 2; t2 = L_R7; t3 = L_R8; }  // 10. R6->R8
		    else if (idx == 11) { t1 = (L_B3 + L_B2) / 2; t2 = L_B2; t3 = L_B1; }  // 11. Trend B3->B1
		    else if (idx == 12) { t1 = (L_R3 + L_R2) / 2; t2 = L_R2; t3 = L_R1; }  // 12. Trend R3->R1
		
		    // Update Text Boxes
		    boxT1.Text = t1.ToString("F2"); 
		    boxT2.Text = t2.ToString("F2"); 
		    boxT3.Text = t3.ToString("F2");
		}
		
		private void UpdateUI()
		{
		    if (lblStatus == null) return;
		    ChartControl.Dispatcher.InvokeAsync(() => {
		        // 1. PnL & Labels
		        dailyPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;
		        lblStatus.Content = activeStatus;
		        lblPnL.Content = dailyPnL.ToString("C");
		        lblPnL.Foreground = dailyPnL >= 0 ? System.Windows.Media.Brushes.Lime : System.Windows.Media.Brushes.Red;
		
		        lblTrades.Content = "# Trades: " + (SystemPerformance.AllTrades.Count - startTradeCount);
		        lblWins.Content = "# Wins: " + (SystemPerformance.AllTrades.WinningTrades.Count - startWins);
		        lblLoss.Content = "# Loss: " + (SystemPerformance.AllTrades.LosingTrades.Count - startLosses);
		        lblToGoal.Content = "$ to Goal: " + Math.Max(0, ProfitGoal - dailyPnL).ToString("C");
		        lblToEOD.Content = "$ to EOD: " + (ProfitGoal + 1000 - dailyPnL).ToString("C");
		
		        // 2. Auto-Trader Button Colors (Toggle based on Armed state)
		        btnScalpL.Background = isArmedScalpLong ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.DimGray;
		        btnScalpS.Background = isArmedScalpShort ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.DimGray;
		        btnCoreL.Background = isArmedCoreLong ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.DimGray;
		        btnCoreS.Background = isArmedCoreShort ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.DimGray;
		
		        // 3. Manual Button Colors (Keep lit if Pending)
		        btnNextBarL.Background = isNextBarPendingL ? System.Windows.Media.Brushes.DodgerBlue : System.Windows.Media.Brushes.DimGray;
		        btnNextBarS.Background = isNextBarPendingS ? System.Windows.Media.Brushes.Magenta : System.Windows.Media.Brushes.DimGray;
		        
		        btnManScalpL.Background = isManScalpPendingL ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.DimGray;
		        btnManScalpS.Background = isManScalpPendingS ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.DimGray;
		        
		        btnManualL.Background = isManualPendingL ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.DimGray;
		        btnManualS.Background = isManualPendingS ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.DimGray;
		
		        // 4. Goal Reached Flash Animation
		        if (isGoalReached) {
		            lblStatus.Background = flashState ? System.Windows.Media.Brushes.Gold : System.Windows.Media.Brushes.DimGray;
		            lblStatus.Foreground = flashState ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
		        }
		    });
		}

        // STYLING HELPERS - EXPLICIT BRUSH TO FIX AMBIGUITY
        private Button Btn(string txt, System.Windows.Media.Brush bg)
        {
            return new Button { Content = txt, Background = bg, Foreground = WPFBrushes.White, FontSize = 10, Margin = new Thickness(1), FontWeight = FontWeights.Bold, FontFamily = modernFont, HorizontalContentAlignment = HorizontalAlignment.Center };
        }
        private Button SolidBtn(string txt, System.Windows.Media.Brush bg)
        {
            return new Button { Content = txt, Background = bg, Foreground = WPFBrushes.White, FontSize = 10, Margin = new Thickness(1), FontWeight = FontWeights.Bold, FontFamily = modernFont };
        }
        private Label LabelStyle(string content, System.Windows.Media.Brush fg, System.Windows.Media.Brush bg)
        {
            return new Label { Content = content, Foreground = fg, Background = bg, FontFamily = modernFont, FontWeight = FontWeights.Bold, HorizontalContentAlignment = HorizontalAlignment.Center, Width = 170 };
        }
        private Label DetailLabel(string content)
        {
            return new Label { Content = content, Foreground = WPFBrushes.White, FontSize = 10, FontFamily = modernFont, FontWeight = FontWeights.Bold, Margin = new Thickness(2, 0, 0, 0) };
        }
        private void AddRow(FrameworkElement c, int r) { Grid.SetRow(c, r); mainPanel.Children.Add(c); }
        private void AddDualRow(FrameworkElement l, FrameworkElement r, int row)
        {
            Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(l, 0); Grid.SetColumn(r, 1); g.Children.Add(l); g.Children.Add(r);
            Grid.SetRow(g, row); mainPanel.Children.Add(g);
        }

        private void DisposeWPFControls()
        {
            if (chartGrid != null && mainPanel != null) chartGrid.Children.Remove(mainPanel);
        }

        private void Disarm()
        {
            isArmedScalpLong = false; isArmedScalpShort = false;
            isArmedCoreLong = false; isArmedCoreShort = false;
            isManualPendingL = false; isManualPendingS = false;
            isManScalpPendingL = false; isManScalpPendingS = false;
            isHalfRiskPending = false; isBreakevenPending = false;
            isNextBarPendingL = false; isNextBarPendingS = false;
            if (!isGoalReached) activeStatus = "STANDBY";
            UpdateUI();
        }

		#region Properties & Filters
		        [Display(Name="Reset All Levels?", Description="Check this and hit Apply to clear all Core Levels.", GroupName="1. Core Levels", Order=20)]
		        public bool ResetLevels { get; set; }
		
		        [NinjaScriptProperty, Display(Name = "Use Volume Filter", GroupName = "4. Filters")] public bool UseVolumeFilter { get; set; }
		
		        [NinjaScriptProperty, Display(Name = "Manual Stop Buffer (Ticks)", GroupName = "5. Manual Controls")] public int ManualStopBufferTicks { get; set; }
		        [NinjaScriptProperty, Display(Name = "Playbook Stop (Ticks)", GroupName = "5. Manual Controls")] public int PlaybookStopTicks { get; set; }
		        [NinjaScriptProperty, Display(Name = "Breakeven Offset (Ticks)", GroupName = "5. Manual Controls")] public int BreakevenOffsetTicks { get; set; }
		
		        [NinjaScriptProperty, Range(0, 100), Display(Name = "L1 Qty", GroupName = "2. Scalp Tactics", Order = 0)] public int Qty1 { get; set; }
		        [NinjaScriptProperty, Display(Name = "L1 Target", GroupName = "2. Scalp Tactics", Order = 1)] public int Leg1TargetTicks { get; set; }
		        [NinjaScriptProperty, Range(0, 100), Display(Name = "L2 Qty", GroupName = "2. Scalp Tactics", Order = 2)] public int Qty2 { get; set; }
		        [NinjaScriptProperty, Display(Name = "L2 Target", GroupName = "2. Scalp Tactics", Order = 3)] public double Leg2TargetAtrMult { get; set; }
		        [NinjaScriptProperty, Display(Name = "L2 Trail Mode", GroupName = "2. Scalp Tactics", Order = 4)] public TrailMode Leg2TrailMode { get; set; }
		        [NinjaScriptProperty, Display(Name = "L2 Bar N", GroupName = "2. Scalp Tactics", Order = 5)] public int Leg2BarN { get; set; }
		        [NinjaScriptProperty, Range(0, 100), Display(Name = "L3 Qty", GroupName = "2. Scalp Tactics", Order = 6)] public int Qty3 { get; set; }
		        [NinjaScriptProperty, Display(Name = "L3 Target", GroupName = "2. Scalp Tactics", Order = 7)] public double Leg3TargetAtrMult { get; set; }
		        [NinjaScriptProperty, Display(Name = "L3 Trail Mode", GroupName = "2. Scalp Tactics", Order = 8)] public TrailMode Leg3TrailMode { get; set; }
		        [NinjaScriptProperty, Display(Name = "L3 Ratchet", GroupName = "2. Scalp Tactics", Order = 9)] public double Leg3RatchetAtrMult { get; set; }
		        [NinjaScriptProperty][Display(Name = "Initial Stop (ATR)", GroupName = "2. Scalp Tactics", Order = 10)] public double InitialStopAtrMult { get; set; }
		        [NinjaScriptProperty][Display(Name = "Initial Stop (Ticks)", GroupName = "2. Scalp Tactics", Order = 11)] public int InitialStopTicks { get; set; }
		        [NinjaScriptProperty][Display(Name = "Context Minutes", GroupName = "3. Context")] public int ContextMinutes { get; set; }
		        [NinjaScriptProperty][Display(Name = "Breakout Offset", GroupName = "3. Context")] public int BreakoutOffsetTicks { get; set; }
		        [NinjaScriptProperty][Display(Name = "Smart Alloc (Ticks)", GroupName = "3. Context")] public int SmartAllocTicks { get; set; }

		private void ValidateLevels()
        {
            levelsValid = (L_B8 > L_B7 && L_B7 > L_B6 && L_B6 > L_B5 && L_B5 > L_B4 && L_B4 > L_B3 && L_B3 > L_B2 && L_B2 > L_B1 && L_B1 > L_POC && L_POC > L_R1 && L_R1 > L_R2 && L_R2 > L_R3 && L_R3 > L_R4 && L_R4 > L_R5 && L_R5 > L_R6 && L_R6 > L_R7 && L_R7 > L_R8);
        }
        private void DrawLevelsAndMids()
        {
            DrawLevelPair(L_B8, L_B7, "B8", "B7"); DrawLevelPair(L_B7, L_B6, "B7", "B6");
            DrawLevelPair(L_B6, L_B5, "B6", "B5"); DrawLevelPair(L_B5, L_B4, "B5", "B4"); DrawLevelPair(L_B4, L_B3, "B4", "B3"); DrawLevelPair(L_B3, L_B2, "B3", "B2");
            DrawLevelPair(L_B2, L_B1, "B2", "B1"); DrawLevelPair(L_B1, L_POC, "B1", "POC"); DrawLevelPair(L_POC, L_R1, "POC", "R1"); DrawLevelPair(L_R1, L_R2, "R1", "R2");
            DrawLevelPair(L_R2, L_R3, "R2", "R3"); DrawLevelPair(L_R3, L_R4, "R3", "R4"); DrawLevelPair(L_R4, L_R5, "R4", "R5"); DrawLevelPair(L_R5, L_R6, "R5", "R6");
            DrawLevelPair(L_R6, L_R7, "R6", "R7"); DrawLevelPair(L_R7, L_R8, "R7", "R8");
        }
        private void DrawLevelPair(double top, double bot, string nameTop, string nameBot)
        {
            if (top == 0 || bot == 0) return;
            Draw.HorizontalLine(this, nameTop, top, WPFBrushes.Gray); Draw.HorizontalLine(this, nameBot, bot, WPFBrushes.Gray);
            double mid = (top + bot) / 2; Draw.HorizontalLine(this, "Mid_" + nameTop + "_" + nameBot, mid, WPFBrushes.DimGray, DashStyleHelper.Dash, 1);
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (chartControl == null || chartScale == null || ChartBars == null) return;
            base.OnRender(chartControl, chartScale);

            SharpDX.Direct2D1.SolidColorBrush blueBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.DodgerBlue);
            SharpDX.Direct2D1.SolidColorBrush whiteBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.White);
            SharpDX.Direct2D1.SolidColorBrush redBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.OrangeRed);
            SharpDX.Direct2D1.SolidColorBrush greenBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.LimeGreen);

            SimpleFont wpfFont = new SimpleFont("Segoe UI", 11);
            SharpDX.DirectWrite.TextFormat textFormat = wpfFont.ToDirectWriteTextFormat();

            // TEXT POSITION: Stays anchored at Right-280 (matches the panel Left-280)
            float x = (float)chartControl.CanvasRight - 280; float y = 50f; float lh = 18f;
            try
            {
                if (!levelsValid)
                {
                    RenderTarget.DrawText("!!! LEVEL ERROR !!!", textFormat, new SharpDX.RectangleF(x, y, 300, 20), redBrush); y += lh;
                    RenderTarget.DrawText("CHECK INPUTS", textFormat, new SharpDX.RectangleF(x, y, 300, 20), redBrush);
                    return;
                }

                RenderTarget.DrawText("ZONE:    " + zoneName, textFormat, new SharpDX.RectangleF(x, y, 300, 20), whiteBrush); y += lh;
                string volTxt = UseVolumeFilter ? "ON" : "OFF";
                RenderTarget.DrawText("VOL FILT: " + volTxt, textFormat, new SharpDX.RectangleF(x, y, 300, 20), UseVolumeFilter ? greenBrush : whiteBrush); y += lh;

                foreach (string line in hud_LongPlan.Split('\n')) { RenderTarget.DrawText(line, textFormat, new SharpDX.RectangleF(x, y, 300, 20), blueBrush); y += lh; } y += lh;
                foreach (string line in hud_ShortPlan.Split('\n')) { RenderTarget.DrawText(line, textFormat, new SharpDX.RectangleF(x, y, 300, 20), blueBrush); y += lh; }
            }
            catch { }
            finally { if (textFormat != null) textFormat.Dispose(); if (blueBrush != null) blueBrush.Dispose(); if (whiteBrush != null) whiteBrush.Dispose(); if (redBrush != null) redBrush.Dispose(); if (greenBrush != null) greenBrush.Dispose(); }
        }
        #endregion
    }
}