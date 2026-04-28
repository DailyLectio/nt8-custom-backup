// CC BY-NC 4.0
// Strategy: TrinityHUDv15_13
// Updates: 
// 1. FIXED CS1061: Replaced non-existent 'CanvasBottom' with 'ChartPanel.H'.
// 2. UI: Info text and Level Error moved to Bottom-Left.
// 3. UI: Added [+], [-], and [C] buttons to Entry Px to bypass NT8 typing block.
// 4. Data: Session Stats tracker added to Bottom-Left info.

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media; 
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
    public class TrinityHUDv15_13 : Strategy
    {
        // =========================================================
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

        // PERFORMANCE
        [NinjaScriptProperty, Display(Name = "Daily Profit Goal ($)", GroupName = "7. Performance")] public double ProfitGoal { get; set; } = 1500;
        [NinjaScriptProperty, Display(Name = "Daily Max Loss ($)", GroupName = "7. Performance")] public double DailyMaxLoss { get; set; } = 1000;
        
        // =========================================================
        //    2. LOGIC VARIABLES
        // =========================================================
        private ADX adx;
        private DM dm;
        private ATR atrAlgo;
        private EMA ema50; 
        
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

        // Session Stats Tracking
        private int startTrades = 0, startWins = 0, startLosses = 0;
        private int sessionTrades = 0, sessionWins = 0, sessionLosses = 0;

        // UI ELEMENTS
        private Grid chartGrid, mainPanel;
        private Button btnScalpL, btnScalpS, btnCoreL, btnCoreS;
        private Button btnManScalpL, btnManScalpS, btnNextBarL, btnNextBarS; 
        private Button btnHalfRisk, btnBreakeven, btnDisarm, btnCloseHalf, btnFlatten;
        private Button btnManualL, btnManualS;
        private Button btnPxC, btnPxUp, btnPxDn; 
        
        private CheckBox chkSafeChop, chkSafeAdx, chkSafeEma, chkSafeVwap;
        private volatile bool useSafeChop = false, useSafeAdx = false, useSafeEma = false, useSafeVwap = false;

        private ComboBox cbLongPlays, cbShortPlays; 
        private TextBox txtEntryPrice; 
        private ComboBox boxT1, boxT2, boxT3, boxT4;
        private Label lblStatus, lblPnL;

        // ANIMATION
        private DispatcherTimer flashTimer;
        private bool flashState = false;

        // DX RESOURCES
        private SharpDX.DirectWrite.TextFormat dxTextFormatCenter;
        private SharpDX.DirectWrite.TextFormat dxTextFormatLeft;
        private SharpDX.Direct2D1.SolidColorBrush dxBrushWhite;
        private SharpDX.Direct2D1.SolidColorBrush dxBrushRed;

        private System.Windows.Media.FontFamily modernFont = new System.Windows.Media.FontFamily("Segoe UI");
        
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
        private bool isPlaybookActive = false; 
        
        private string zoneName = "WAITING";
        private string activeStatus = "STANDBY";
        private string allocStatus = "";
        private bool levelsValid = true;
        private double zoneHigh = 0, zoneLow = 0, levelAbove = 0, levelBelow = 0;
        private double longT1, longT2, longGatePrice;
        private double shortT1, shortT2, shortGatePrice;
        private string hud_LongPlan = "", hud_ShortPlan = "";

        private double manT1, manT2, manT3, manT4, manEntryPx;
        private bool isManualActive = false;
        private double manualAnchorPrice = 0, manualMidLine = 0;
        private double userOverrideStopPrice = 0;

        private Dictionary<string, double> levelMap = new Dictionary<string, double>();
        private List<string> orderedLevelNames = new List<string>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Trinity HUD v15.13";
                Calculate = Calculate.OnPriceChange;
                EntriesPerDirection = 5;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsOverlay = true;

                // Scalp Defaults
                S_Qty1 = 2; S_Leg1TargetTicks = 40;
                S_Qty2 = 1; S_Leg2TargetAtrMult = 0.88; S_Leg2TrailMode = TrailMode.BarNTrail; S_Leg2BarN = 2;
                S_Qty3 = 1; S_Leg3TargetAtrMult = 1.5; S_Leg3TrailMode = TrailMode.AtrRatchet; S_Leg3RatchetAtrMult = 1.5;
                S_Qty4 = 0; S_Leg4TargetAtrMult = 2.0; S_Leg4TrailMode = TrailMode.AtrRatchet; S_Leg4RatchetAtrMult = 1.5;
                S_InitialStopAtrMult = 0.75; S_InitialStopTicks = 35;

                // Core Defaults
                C_Qty1 = 1; C_Leg1TrailMode = TrailMode.None; 
                C_Qty2 = 1; C_Leg2TrailMode = TrailMode.BarNTrail; C_Leg2BarN = 2;
                C_Qty3 = 1; C_Leg3TrailMode = TrailMode.AtrRatchet; C_Leg3RatchetAtrMult = 1.5;
                C_Qty4 = 1; C_Leg4TrailMode = TrailMode.AtrRatchet; C_Leg4RatchetAtrMult = 2.0;
                C_InitialStopAtrMult = 0.75; C_InitialStopTicks = 35; 
                CoreTargetOffsetTicks = 0;

                ContextMinutes = 1; BreakoutOffsetTicks = 1; SmartAllocTicks = 12;
                ManualStopBufferTicks = 10; PlaybookStopTicks = 12; BreakevenOffsetTicks = 5;
                
                ShowLines = true;
                SafeChopLimit = 60; SafeAdxLimit = 20;
            }
            else if (State == State.Configure) { AddDataSeries(BarsPeriodType.Minute, ContextMinutes); }
            else if (State == State.DataLoaded) { adx = ADX(14); dm = DM(14); atrAlgo = ATR(14); ema50 = EMA(50); }
            else if (State == State.Historical)
            {
                if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(() => { CreateWPFControls(); StartFlashTimer(); });
            }
            else if (State == State.Terminated)
            {
                if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(() => DisposeWPFControls());
                if (dxTextFormatCenter != null) dxTextFormatCenter.Dispose();
                if (dxTextFormatLeft != null) dxTextFormatLeft.Dispose();
                if (dxBrushWhite != null) dxBrushWhite.Dispose();
                if (dxBrushRed != null) dxBrushRed.Dispose();
                if (flashTimer != null) flashTimer.Stop();
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
                if (ResetLevels) { ResetAllLevels(); ResetLevels = false; }
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

                if (levelMap.Count == 0 || !isCombosPopulated) { BuildLevelMap(); if (orderedLevelNames.Count > 0) { PopulateCombos(); isCombosPopulated = true; } }

                ValidateLevels();
                if (!levelsValid) { activeStatus = "ERROR: LEVEL MISMATCH"; Disarm(); return; }
                if (ShowLines) DrawCoreLines();

                if (Bars.IsFirstBarOfSession) { sessionStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit; isGoalReached = false; }
                dailyPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;
                
                if (dailyPnL >= ProfitGoal && !isGoalReached) { isGoalReached = true; activeStatus = "GOAL REACHED"; Disarm(); }
                if (dailyPnL <= -DailyMaxLoss) { activeStatus = "MAX LOSS HIT"; Disarm(); if (Position.MarketPosition != MarketPosition.Flat) ExecuteFlatten(); return; }

                IdentifyContext();
                CalculateTactics();


                if (isFlattenPending) { ExecuteFlatten(); return; }
                if (Position.MarketPosition != MarketPosition.Flat) {
                    if (isHalfRiskPending) ExecuteHalfRiskLogic();
                    if (isBreakevenPending) ExecuteBreakevenLogic();
                    if (isCloseHalfPending) ExecuteCloseHalf();
                    ManageTrailingStops();
                } else {
                    entryBar = -1; highSinceEntry = double.MinValue; lowSinceEntry = double.MaxValue; isManualActive = false; userOverrideStopPrice = 0; isPlaybookActive = false;
                }

                if (isManualPendingL) { ExecuteManualTrade(true); return; }
                if (isManualPendingS) { ExecuteManualTrade(false); return; }
                if (isManScalpPendingL) { ExecuteManualScalp(true); return; }
                if (isManScalpPendingS) { ExecuteManualScalp(false); return; }

                if (IsFirstTickOfBar && !isGoalReached)
                {
                    if (isNextBarPendingL) { ExecuteManualScalp(true); isNextBarPendingL = false; activeStatus = "NB LONG EXEC"; Disarm(); return; }
                    if (isNextBarPendingS) { ExecuteManualScalp(false); isNextBarPendingS = false; activeStatus = "NB SHORT EXEC"; Disarm(); return; }

                    if (!isArmedScalpLong && !isArmedScalpShort && !isArmedCoreLong && !isArmedCoreShort) return;

                    // --- NEW UI-CONTROLLED AUTO TRADER LOGIC ---

				// Checks if the previous 1-minute candle closed in the correct direction by the Breakout Offset
				bool isBullBar = Close[1] >= (Open[1] + (BreakoutOffsetTicks * TickSize));
				bool isBearBar = Close[1] <= (Open[1] - (BreakoutOffsetTicks * TickSize));
				
				// Core Execution
				if (isArmedCoreLong && isBullBar) 
				{
				    if (CheckFailSafes(true)) ExecuteCoreTrade(true);
				}
				else if (isArmedCoreShort && isBearBar) 
				{
				    if (CheckFailSafes(false)) ExecuteCoreTrade(false);
				}
				
				// Scalp Execution
				if (isArmedScalpLong && isBullBar) 
				{
				    if (CheckFailSafes(true)) ExecuteScalpMoonRunner(true);
				}
				else if (isArmedScalpShort && isBearBar) 
				{
				    if (CheckFailSafes(false)) ExecuteScalpMoonRunner(false);
				}
                }
            }
            catch (Exception e) { Print("Trinity Error: " + e.Message); }
        }

        private bool CheckFailSafes(bool isLong)
        {
            if (useSafeChop && adx[0] < SafeChopLimit) { Print("FAIL SAFE: CHOP"); activeStatus = "SAFE: CHOP"; return false; }
            if (useSafeAdx) {
                if (adx[0] < SafeAdxLimit) { Print("FAIL SAFE: LOW ADX"); activeStatus = "SAFE: ADX<20"; return false; }
                if (adx[0] < adx[1]) { Print("FAIL SAFE: ADX FALLING"); activeStatus = "SAFE: ADX FALL"; return false; }
            }
            if (useSafeEma) {
                if (isLong && Close[0] < ema50[0]) { Print("FAIL SAFE: PRICE < EMA"); activeStatus = "SAFE: < EMA"; return false; }
                if (!isLong && Close[0] > ema50[0]) { Print("FAIL SAFE: PRICE > EMA"); activeStatus = "SAFE: > EMA"; return false; }
            }
            if (useSafeVwap && currentVwap > 0) {
                if (isLong && Close[0] < currentVwap) { Print("FAIL SAFE: PRICE < VWAP"); activeStatus = "SAFE: < VWAP"; return false; }
                if (!isLong && Close[0] > currentVwap) { Print("FAIL SAFE: PRICE > VWAP"); activeStatus = "SAFE: > VWAP"; return false; }
            }
            return true;
        }

        private void ExecuteManualTrade(bool isLong)
        {
            try {
                if (!CheckFailSafes(isLong)) { isManualPendingL = false; isManualPendingS = false; return; }

                double t1 = manT1; double t2 = manT2; double t3 = manT3; double t4 = manT4;
                if (t1 == 0) t1 = isLong ? Close[0] + (20 * TickSize) : Close[0] - (20 * TickSize);
                
                double offset = CoreTargetOffsetTicks * TickSize;
                if (isLong) {
                    if (t1 > 0) t1 -= offset; if (t2 > 0) t2 -= offset;
                    if (t3 > 0) t3 -= offset; if (t4 > 0) t4 -= offset;
                } else {
                    if (t1 > 0) t1 += offset; if (t2 > 0) t2 += offset;
                    if (t3 > 0) t3 += offset; if (t4 > 0) t4 += offset;
                }

                bool isStop = false; bool isLimit = false; double entryPrice = manEntryPx;
                if (entryPrice > 0) {
                    double current = Close[0];
                    if (isLong) { if (entryPrice > current) isStop = true; else isLimit = true; }
                    else { if (entryPrice < current) isStop = true; else isLimit = true; }
                }

                manualAnchorPrice = isLong ? zoneLow : zoneHigh;
                if (manualAnchorPrice == 0) manualAnchorPrice = Close[0];
                manualMidLine = (zoneHigh + zoneLow) / 2.0;

                int stopTicks = (isPlaybookActive) ? PlaybookStopTicks : ManualStopBufferTicks;
                double stopPrice = isLong ? manualAnchorPrice - (stopTicks * TickSize) : manualAnchorPrice + (stopTicks * TickSize);
                
                if (entryPrice > 0) {
                     if (isLong && stopPrice >= entryPrice) stopPrice = entryPrice - (20 * TickSize);
                     if (!isLong && stopPrice <= entryPrice) stopPrice = entryPrice + (20 * TickSize);
                }

                string sig = "Core_" + (isLong ? "L" : "S");
                isManualActive = true; userOverrideStopPrice = 0;
                entryBar = CurrentBar; highSinceEntry = High[0]; lowSinceEntry = Low[0];

                SetStopLoss(sig + "_1", CalculationMode.Price, stopPrice, false);
                if (C_Qty2 > 0) SetStopLoss(sig + "_2", CalculationMode.Price, stopPrice, false);
                if (C_Qty3 > 0) SetStopLoss(sig + "_3", CalculationMode.Price, stopPrice, false);
                if (C_Qty4 > 0) SetStopLoss(sig + "_4", CalculationMode.Price, stopPrice, false);

                if (isLong) {
                    if (C_Qty1 > 0) { SetProfitTarget(sig + "_1", CalculationMode.Price, t1); if (isStop) EnterLongStopMarket(C_Qty1, entryPrice, sig + "_1"); else if (isLimit) EnterLongLimit(C_Qty1, entryPrice, sig + "_1"); else EnterLong(C_Qty1, sig + "_1"); }
                    if (C_Qty2 > 0 && t2 > 0) { SetProfitTarget(sig + "_2", CalculationMode.Price, t2); if (isStop) EnterLongStopMarket(C_Qty2, entryPrice, sig + "_2"); else if (isLimit) EnterLongLimit(C_Qty2, entryPrice, sig + "_2"); else EnterLong(C_Qty2, sig + "_2"); }
                    if (C_Qty3 > 0 && t3 > 0) { SetProfitTarget(sig + "_3", CalculationMode.Price, t3); if (isStop) EnterLongStopMarket(C_Qty3, entryPrice, sig + "_3"); else if (isLimit) EnterLongLimit(C_Qty3, entryPrice, sig + "_3"); else EnterLong(C_Qty3, sig + "_3"); }
                    if (C_Qty4 > 0 && t4 > 0) { SetProfitTarget(sig + "_4", CalculationMode.Price, t4); if (isStop) EnterLongStopMarket(C_Qty4, entryPrice, sig + "_4"); else if (isLimit) EnterLongLimit(C_Qty4, entryPrice, sig + "_4"); else EnterLong(C_Qty4, sig + "_4"); }
                } else {
                    if (C_Qty1 > 0) { SetProfitTarget(sig + "_1", CalculationMode.Price, t1); if (isStop) EnterShortStopMarket(C_Qty1, entryPrice, sig + "_1"); else if (isLimit) EnterShortLimit(C_Qty1, entryPrice, sig + "_1"); else EnterShort(C_Qty1, sig + "_1"); }
                    if (C_Qty2 > 0 && t2 > 0) { SetProfitTarget(sig + "_2", CalculationMode.Price, t2); if (isStop) EnterShortStopMarket(C_Qty2, entryPrice, sig + "_2"); else if (isLimit) EnterShortLimit(C_Qty2, entryPrice, sig + "_2"); else EnterShort(C_Qty2, sig + "_2"); }
                    if (C_Qty3 > 0 && t3 > 0) { SetProfitTarget(sig + "_3", CalculationMode.Price, t3); if (isStop) EnterShortStopMarket(C_Qty3, entryPrice, sig + "_3"); else if (isLimit) EnterShortLimit(C_Qty3, entryPrice, sig + "_3"); else EnterShort(C_Qty3, sig + "_3"); }
                    if (C_Qty4 > 0 && t4 > 0) { SetProfitTarget(sig + "_4", CalculationMode.Price, t4); if (isStop) EnterShortStopMarket(C_Qty4, entryPrice, sig + "_4"); else if (isLimit) EnterShortLimit(C_Qty4, entryPrice, sig + "_4"); else EnterShort(C_Qty4, sig + "_4"); }
                }

                isManualPendingL = false; isManualPendingS = false;
                allocStatus = "MANUAL: " + (isStop ? "STOP" : (isLimit ? "LIMIT" : "MKT"));
                Disarm();
            }
            catch (Exception ex) { Print("ERROR IN ExecuteManualTrade: " + ex.ToString()); isManualPendingL = false; isManualPendingS = false; }
        }

        private void ExecuteCoreTrade(bool isLong)
        {
            double t1 = isLong ? longT1 : shortT1;
            double t2 = isLong ? longT2 : shortT2;
            double t3 = isLong ? longT2 + (20 * TickSize) : shortT2 - (20 * TickSize);
            double t4 = isLong ? longT2 + (40 * TickSize) : shortT2 - (40 * TickSize);

            double offset = CoreTargetOffsetTicks * TickSize;
            if (isLong) { t1 -= offset; t2 -= offset; t3 -= offset; t4 -= offset; } 
            else { t1 += offset; t2 += offset; t3 += offset; t4 += offset; }

            string sig = "Core_" + (isLong ? "L" : "S");
            userOverrideStopPrice = 0;
            isManualActive = true; 
            entryBar = CurrentBar; highSinceEntry = High[0]; lowSinceEntry = Low[0];

            double stopPrice = isLong ? Close[0] - (C_InitialStopTicks * TickSize) : Close[0] + (C_InitialStopTicks * TickSize);

            if (C_Qty1 > 0) SetStopLoss(sig + "_1", CalculationMode.Price, stopPrice, false);
            if (C_Qty2 > 0) SetStopLoss(sig + "_2", CalculationMode.Price, stopPrice, false);
            if (C_Qty3 > 0) SetStopLoss(sig + "_3", CalculationMode.Price, stopPrice, false);
            if (C_Qty4 > 0) SetStopLoss(sig + "_4", CalculationMode.Price, stopPrice, false);

            if (isLong) {
                if (C_Qty1 > 0) { EnterLong(C_Qty1, sig + "_1"); SetProfitTarget(sig + "_1", CalculationMode.Price, t1); }
                if (C_Qty2 > 0) { EnterLong(C_Qty2, sig + "_2"); SetProfitTarget(sig + "_2", CalculationMode.Price, t2); }
                if (C_Qty3 > 0) { EnterLong(C_Qty3, sig + "_3"); SetProfitTarget(sig + "_3", CalculationMode.Price, t3); }
                if (C_Qty4 > 0) { EnterLong(C_Qty4, sig + "_4"); SetProfitTarget(sig + "_4", CalculationMode.Price, t4); }
            } else {
                if (C_Qty1 > 0) { EnterShort(C_Qty1, sig + "_1"); SetProfitTarget(sig + "_1", CalculationMode.Price, t1); }
                if (C_Qty2 > 0) { EnterShort(C_Qty2, sig + "_2"); SetProfitTarget(sig + "_2", CalculationMode.Price, t2); }
                if (C_Qty3 > 0) { EnterShort(C_Qty3, sig + "_3"); SetProfitTarget(sig + "_3", CalculationMode.Price, t3); }
                if (C_Qty4 > 0) { EnterShort(C_Qty4, sig + "_4"); SetProfitTarget(sig + "_4", CalculationMode.Price, t4); }
            }
            allocStatus = "AUTO CORE";
            Disarm();
        }

        private void ExecuteScalpMoonRunner(bool isLong)
        {
            isManualActive = false; entryBar = CurrentBar; highSinceEntry = High[0]; lowSinceEntry = Low[0];
            double currentAtr = atrAlgo[0];
            double t1 = S_Leg1TargetTicks;
            double t2 = Math.Max(5, Math.Round((currentAtr * S_Leg2TargetAtrMult) / TickSize));
            double t3 = Math.Max(10, Math.Round((currentAtr * S_Leg3TargetAtrMult) / TickSize));
            double t4 = Math.Max(15, Math.Round((currentAtr * S_Leg4TargetAtrMult) / TickSize));
            double stop = Math.Max(5, Math.Round((currentAtr * S_InitialStopAtrMult) / TickSize));
            string dir = isLong ? "L" : "S";

            allocStatus = "SCALP MOON"; userOverrideStopPrice = 0;

            if (S_Qty1 > 0) { string s = "Scalp_" + dir + "_1"; SetStopLoss(s, CalculationMode.Ticks, stop, false); SetProfitTarget(s, CalculationMode.Ticks, t1); if (isLong) EnterLong(S_Qty1, s); else EnterShort(S_Qty1, s); }
            if (S_Qty2 > 0) { string s = "Scalp_" + dir + "_2"; SetStopLoss(s, CalculationMode.Ticks, stop, false); SetProfitTarget(s, CalculationMode.Ticks, t2); if (isLong) EnterLong(S_Qty2, s); else EnterShort(S_Qty2, s); }
            if (S_Qty3 > 0) { string s = "Scalp_" + dir + "_3"; SetStopLoss(s, CalculationMode.Ticks, stop, false); SetProfitTarget(s, CalculationMode.Ticks, t3); if (isLong) EnterLong(S_Qty3, s); else EnterShort(S_Qty3, s); }
            if (S_Qty4 > 0) { string s = "Scalp_" + dir + "_4"; SetStopLoss(s, CalculationMode.Ticks, stop, false); SetProfitTarget(s, CalculationMode.Ticks, t4); if (isLong) EnterLong(S_Qty4, s); else EnterShort(S_Qty4, s); }
            Disarm();
        }

        private void ExecuteManualScalp(bool isLong)
        {
            if (!CheckFailSafes(isLong)) { isManScalpPendingL = false; isManScalpPendingS = false; return; }

            double lim = isLong ? Close[0] + (5 * TickSize) : Close[0] - (5 * TickSize);
            double currentAtr = atrAlgo[0];
            double t1 = S_Leg1TargetTicks;
            double t2 = Math.Max(5, Math.Round((currentAtr * S_Leg2TargetAtrMult) / TickSize));
            double t3 = Math.Max(10, Math.Round((currentAtr * S_Leg3TargetAtrMult) / TickSize));
            double t4 = Math.Max(15, Math.Round((currentAtr * S_Leg4TargetAtrMult) / TickSize));
            double stop = Math.Max(5, Math.Round((currentAtr * S_InitialStopAtrMult) / TickSize));
            string dir = isLong ? "L" : "S";
            allocStatus = "MAN SCALP"; userOverrideStopPrice = 0;

            if (S_Qty1 > 0) { string s = "MScalp_" + dir + "_1"; SetStopLoss(s, CalculationMode.Ticks, stop, false); SetProfitTarget(s, CalculationMode.Ticks, t1); if (isLong) EnterLongLimit(S_Qty1, lim, s); else EnterShortLimit(S_Qty1, lim, s); }
            if (S_Qty2 > 0) { string s = "MScalp_" + dir + "_2"; SetStopLoss(s, CalculationMode.Ticks, stop, false); SetProfitTarget(s, CalculationMode.Ticks, t2); if (isLong) EnterLongLimit(S_Qty2, lim, s); else EnterShortLimit(S_Qty2, lim, s); }
            if (S_Qty3 > 0) { string s = "MScalp_" + dir + "_3"; SetStopLoss(s, CalculationMode.Ticks, stop, false); SetProfitTarget(s, CalculationMode.Ticks, t3); if (isLong) EnterLongLimit(S_Qty3, lim, s); else EnterShortLimit(S_Qty3, lim, s); }
            if (S_Qty4 > 0) { string s = "MScalp_" + dir + "_4"; SetStopLoss(s, CalculationMode.Ticks, stop, false); SetProfitTarget(s, CalculationMode.Ticks, t4); if (isLong) EnterLongLimit(S_Qty4, lim, s); else EnterShortLimit(S_Qty4, lim, s); }
            isManScalpPendingL = false; isManScalpPendingS = false; Disarm();
        }

        private void ExecuteFlatten() { if (Position.MarketPosition == MarketPosition.Long) ExitLong(); else if (Position.MarketPosition == MarketPosition.Short) ExitShort(); foreach (Order o in Orders) { if (o != null && o.OrderState == OrderState.Working) CancelOrder(o); } isFlattenPending = false; userOverrideStopPrice = 0; Disarm(); activeStatus = "FLATTENED"; UpdateUI(); }
        private void ExecuteCloseHalf() { if (Position.MarketPosition == MarketPosition.Flat) { isCloseHalfPending = false; return; } int qty = Position.Quantity; if (qty <= 1) return; int exitQty = (int)Math.Ceiling(qty / 2.0); if (Position.MarketPosition == MarketPosition.Long) ExitLong(exitQty, "CloseHalf", ""); else ExitShort(exitQty, "CloseHalf", ""); isCloseHalfPending = false; }
        private void ExecuteBreakevenLogic() { if (Position.MarketPosition == MarketPosition.Flat) { isBreakevenPending = false; return; } double be = Position.MarketPosition == MarketPosition.Long ? Position.AveragePrice + (BreakevenOffsetTicks * TickSize) : Position.AveragePrice - (BreakevenOffsetTicks * TickSize); userOverrideStopPrice = be; ApplyOverrideStop(be); isBreakevenPending = false; }
        
        private void ExecuteHalfRiskLogic() { 
            if (Position.MarketPosition == MarketPosition.Flat) { isHalfRiskPending = false; return; } 
            double curStop = GetCurrentStop(); 
            if (curStop == 0) { Print("HalfRisk Failed: Stop Order Not Found"); isHalfRiskPending = false; return; }
            double newStop = (curStop + Close[0]) / 2.0; 
            newStop = Math.Round(newStop / TickSize) * TickSize; 
            userOverrideStopPrice = newStop; 
            ApplyOverrideStop(newStop); 
            isHalfRiskPending = false; 
        }
        
        private double GetCurrentStop() { foreach (Order o in Orders) { if (o != null && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted) && o.OrderType == OrderType.StopMarket) return o.StopPrice; } return 0; }
        private void ApplyOverrideStop(double price) { 
            string[] sigs = { "Scalp_L_1", "Scalp_L_2", "Scalp_L_3", "Scalp_L_4", "Scalp_S_1", "Scalp_S_2", "Scalp_S_3", "Scalp_S_4", 
                              "Core_L_1", "Core_L_2", "Core_L_3", "Core_L_4", "Core_S_1", "Core_S_2", "Core_S_3", "Core_S_4", 
                              "Man_L_1", "Man_L_2", "Man_L_3", "Man_L_4", "Man_S_1", "Man_S_2", "Man_S_3", "Man_S_4", 
                              "MScalp_L_1", "MScalp_L_2", "MScalp_L_3", "MScalp_L_4", "MScalp_S_1", "MScalp_S_2", "MScalp_S_3", "MScalp_S_4" }; 
            foreach (string s in sigs) SetStopLoss(s, CalculationMode.Price, price, false); 
        }

        private void ManageTrailingStops() 
        { 
            if (IsFirstTickOfBar) {
                highSinceEntry = Math.Max(highSinceEntry, High[1]);
                lowSinceEntry = Math.Min(lowSinceEntry, Low[1]);
                
                if (S_Qty2 > 0) ApplyTrail(S_Qty2, "Scalp_L_2", "Scalp_S_2", S_Leg2TrailMode, S_Leg2BarN, 0, "MScalp_L_2", "MScalp_S_2"); 
                if (S_Qty3 > 0) ApplyTrail(S_Qty3, "Scalp_L_3", "Scalp_S_3", S_Leg3TrailMode, 0, S_Leg3RatchetAtrMult, "MScalp_L_3", "MScalp_S_3");
                if (S_Qty4 > 0) ApplyTrail(S_Qty4, "Scalp_L_4", "Scalp_S_4", S_Leg4TrailMode, 0, S_Leg4RatchetAtrMult, "MScalp_L_4", "MScalp_S_4");

                if (isManualActive || isPlaybookActive) {
                    if (C_Qty2 > 0) ApplyTrail(C_Qty2, "Core_L_2", "Core_S_2", C_Leg2TrailMode, C_Leg2BarN, 0, "Man_L_2", "Man_S_2"); 
                    if (C_Qty3 > 0) ApplyTrail(C_Qty3, "Core_L_3", "Core_S_3", C_Leg3TrailMode, 0, C_Leg3RatchetAtrMult, "Man_L_3", "Man_S_3");
                    if (C_Qty4 > 0) ApplyTrail(C_Qty4, "Core_L_4", "Core_S_4", C_Leg4TrailMode, 0, C_Leg4RatchetAtrMult, "Man_L_4", "Man_S_4");
                }
            }
        }
        
        private void ApplyTrail(int qty, string longSig1, string shortSig1, TrailMode mode, int barN, double atrMult, string longSig2 = "", string shortSig2 = "") 
        { 
            if (mode == TrailMode.None && userOverrideStopPrice == 0) return; 
            bool isLong = Position.MarketPosition == MarketPosition.Long; 
            
            double newStop = 0; 
            if (mode == TrailMode.BarNTrail) { int idx = Math.Min(barN, CurrentBar); if (isLong) newStop = Low[idx]; else newStop = High[idx]; } 
            else if (mode == TrailMode.AtrRatchet) { double rat = atrAlgo[0] * atrMult; if (isLong) newStop = highSinceEntry - rat; else newStop = lowSinceEntry + rat; } 
            
            if (userOverrideStopPrice != 0) { 
                if (newStop == 0) newStop = userOverrideStopPrice; 
                else { if (isLong) newStop = Math.Max(newStop, userOverrideStopPrice); else newStop = Math.Min(newStop, userOverrideStopPrice); } 
            } 
            
            if (newStop != 0) {
                SetStopLoss(isLong ? longSig1 : shortSig1, CalculationMode.Price, newStop, false);
                if (longSig2 != "") SetStopLoss(isLong ? longSig2 : shortSig2, CalculationMode.Price, newStop, false);
            }
        }

        private void CreateWPFControls()
        {
            chartGrid = ChartControl.Parent as Grid; if (chartGrid == null) return;
            mainPanel = new Grid { Width = 170, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 50, 110, 0) }; 
            for (int i = 0; i < 35; i++) mainPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

            lblStatus = LabelStyle("STANDBY", WPFBrushes.White, WPFBrushes.DimGray);
            lblPnL = LabelStyle("$0.00", WPFBrushes.Lime, WPFBrushes.Black);
            AddRow(lblStatus, 0); AddRow(lblPnL, 1);

            Label lblAuto = new Label { Content = "AUTO TRADER", Foreground = WPFBrushes.Orange, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Bold, FontFamily = modernFont };
            AddRow(lblAuto, 2);
            btnScalpL = Btn("SCALP L", WPFBrushes.DimGray); btnScalpS = Btn("SCALP S", WPFBrushes.DimGray);
            btnCoreL = Btn("CORE L", WPFBrushes.DimGray); btnCoreS = Btn("CORE S", WPFBrushes.DimGray);
            AddDualRow(btnScalpL, btnScalpS, 3); AddDualRow(btnCoreL, btnCoreS, 4);

            Label lblMan = new Label { Content = "MANUAL / PLAYBOOK", Foreground = WPFBrushes.Cyan, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Bold, FontFamily = modernFont };
            AddRow(lblMan, 6);

            cbLongPlays = new ComboBox { FontSize = 10, Height = 22, Margin = new Thickness(1), FontFamily = modernFont, FontWeight = FontWeights.Bold };
            cbLongPlays.Items.Add("LONG PLAYS...");
            cbLongPlays.Items.Add("1. B6 to B8"); cbLongPlays.Items.Add("2. B4 to B6"); cbLongPlays.Items.Add("3. B2 to B4");
            cbLongPlays.Items.Add("4. POC to B2"); cbLongPlays.Items.Add("5. R1 to POC"); cbLongPlays.Items.Add("6. R1 to B1 x");
            cbLongPlays.Items.Add("7. R2 to B2 x"); cbLongPlays.Items.Add("8. R2 to POC");
            cbLongPlays.Items.Add("9. R3 to R2"); cbLongPlays.Items.Add("10. R4 to R2"); cbLongPlays.Items.Add("11. R4 to B4 x");
            cbLongPlays.Items.Add("12. R6 to R4"); cbLongPlays.Items.Add("13. R5 to POC");
            cbLongPlays.SelectedIndex = 0;
            cbLongPlays.SelectionChanged += (s, e) => { if (cbLongPlays.SelectedIndex > 0) { cbShortPlays.SelectedIndex = 0; ProcessPlaybook(true, cbLongPlays.SelectedIndex); } };
            AddRow(cbLongPlays, 7);

            cbShortPlays = new ComboBox { FontSize = 10, Height = 22, Margin = new Thickness(1), FontFamily = modernFont, FontWeight = FontWeights.Bold };
            cbShortPlays.Items.Add("SHORT PLAYS...");
            cbShortPlays.Items.Add("1. B6 to B4"); cbShortPlays.Items.Add("2. B4 to R4 x"); cbShortPlays.Items.Add("3. B4 to B2");
            cbShortPlays.Items.Add("4. B3 to B2"); cbShortPlays.Items.Add("5. B2 to POC"); cbShortPlays.Items.Add("6. B2 to R2 x");
            cbShortPlays.Items.Add("7. B1 to R1 x"); cbShortPlays.Items.Add("8. B1 to POC"); cbShortPlays.Items.Add("9. POC to R2");
            cbShortPlays.Items.Add("10. R2 TO R4"); cbShortPlays.Items.Add("11. R4 to R6"); cbShortPlays.Items.Add("12. B5 to POC");
            cbShortPlays.Items.Add("13. R6 to R8");
            cbShortPlays.SelectedIndex = 0;
            cbShortPlays.SelectionChanged += (s, e) => { if (cbShortPlays.SelectedIndex > 0) { cbLongPlays.SelectedIndex = 0; ProcessPlaybook(false, cbShortPlays.SelectedIndex); } };
            AddRow(cbShortPlays, 8);

            Grid entryGrid = new Grid();
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            
            Label lblE = new Label { Content = "Entry:", Foreground = WPFBrushes.White, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            txtEntryPrice = new TextBox { FontSize = 11, Height = 20, Margin = new Thickness(1), Text = "0" };
            btnPxC = new Button { Content = "C", FontSize = 9, FontWeight = FontWeights.Bold, Margin = new Thickness(1) };
            btnPxUp = new Button { Content = "+", FontSize = 9, FontWeight = FontWeights.Bold, Margin = new Thickness(1) };
            btnPxDn = new Button { Content = "-", FontSize = 9, FontWeight = FontWeights.Bold, Margin = new Thickness(1) };

            btnPxC.Click += (s, e) => { ChartControl.Dispatcher.InvokeAsync(() => { txtEntryPrice.Text = currentChartPrice.ToString("F2"); }); };
            btnPxUp.Click += (s, e) => { ChartControl.Dispatcher.InvokeAsync(() => { double val; if (double.TryParse(txtEntryPrice.Text, out val)) { val += TickSize; txtEntryPrice.Text = val.ToString("F2"); } }); };
            btnPxDn.Click += (s, e) => { ChartControl.Dispatcher.InvokeAsync(() => { double val; if (double.TryParse(txtEntryPrice.Text, out val)) { val -= TickSize; txtEntryPrice.Text = val.ToString("F2"); } }); };

            Grid.SetColumn(lblE, 0); Grid.SetColumn(txtEntryPrice, 1); Grid.SetColumn(btnPxC, 2); Grid.SetColumn(btnPxUp, 3); Grid.SetColumn(btnPxDn, 4);
            entryGrid.Children.Add(lblE); entryGrid.Children.Add(txtEntryPrice); entryGrid.Children.Add(btnPxC); entryGrid.Children.Add(btnPxUp); entryGrid.Children.Add(btnPxDn);
            AddRow(entryGrid, 9);

            boxT1 = Combo(); boxT2 = Combo(); boxT3 = Combo(); boxT4 = Combo();
            AddRow(boxT1, 10); AddRow(boxT2, 11); AddRow(boxT3, 12); AddRow(boxT4, 13);
            
            Label lblFail = new Label { Content = "FAIL SAFE", Foreground = WPFBrushes.Cyan, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Bold, FontFamily = modernFont };
            AddRow(lblFail, 15);
            
            chkSafeChop = Check("Chop"); chkSafeAdx = Check("ADX");
            chkSafeEma = Check("EMA"); chkSafeVwap = Check("VWAP");
            
            chkSafeChop.Checked += (s,e) => useSafeChop = true; chkSafeChop.Unchecked += (s,e) => useSafeChop = false;
            chkSafeAdx.Checked += (s,e) => useSafeAdx = true; chkSafeAdx.Unchecked += (s,e) => useSafeAdx = false;
            chkSafeEma.Checked += (s,e) => useSafeEma = true; chkSafeEma.Unchecked += (s,e) => useSafeEma = false;
            chkSafeVwap.Checked += (s,e) => useSafeVwap = true; chkSafeVwap.Unchecked += (s,e) => useSafeVwap = false;
            
            AddDualRow(chkSafeChop, chkSafeAdx, 16); AddDualRow(chkSafeEma, chkSafeVwap, 17);

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
            btnCloseHalf = SolidBtn("CLOSE 50%", WPFBrushes.Yellow); btnCloseHalf.Foreground = WPFBrushes.Black;
            AddDualRow(btnHalfRisk, btnBreakeven, 23); AddDualRow(btnDisarm, btnCloseHalf, 24);

            btnFlatten = SolidBtn("FLATTEN", WPFBrushes.Red); btnFlatten.Height = 30;
            AddRow(btnFlatten, 25);

            btnScalpL.Click += (s, e) => { bool p = isArmedScalpLong; Disarm(); isArmedScalpLong = !p; activeStatus = isArmedScalpLong ? "ARM SCALP L" : "STANDBY"; UpdateUI(); };
            btnScalpS.Click += (s, e) => { bool p = isArmedScalpShort; Disarm(); isArmedScalpShort = !p; activeStatus = isArmedScalpShort ? "ARM SCALP S" : "STANDBY"; UpdateUI(); };
            btnCoreL.Click += (s, e) => { bool p = isArmedCoreLong; Disarm(); isArmedCoreLong = !p; activeStatus = isArmedCoreLong ? "ARM CORE L" : "STANDBY"; UpdateUI(); };
            btnCoreS.Click += (s, e) => { bool p = isArmedCoreShort; Disarm(); isArmedCoreShort = !p; activeStatus = isArmedCoreShort ? "ARM CORE S" : "STANDBY"; UpdateUI(); };
            
            btnManualL.Click += (s, e) => { ParseManualInputs(); isManualPendingL = true; activeStatus = "CORE L SENT"; btnManualL.Background = WPFBrushes.LimeGreen; UpdateUI(); };
            btnManualS.Click += (s, e) => { ParseManualInputs(); isManualPendingS = true; activeStatus = "CORE S SENT"; btnManualS.Background = WPFBrushes.Red; UpdateUI(); };
            btnManScalpL.Click += (s, e) => { isManScalpPendingL = true; activeStatus = "SCALP SENT"; UpdateUI(); };
            btnManScalpS.Click += (s, e) => { isManScalpPendingS = true; activeStatus = "SCALP SENT"; UpdateUI(); };
            btnNextBarL.Click += (s, e) => { bool p = isNextBarPendingL; Disarm(); isNextBarPendingL = !p; activeStatus = isNextBarPendingL ? "WAIT NEXT L" : "STANDBY"; UpdateUI(); };
            btnNextBarS.Click += (s, e) => { bool p = isNextBarPendingS; Disarm(); isNextBarPendingS = !p; activeStatus = isNextBarPendingS ? "WAIT NEXT S" : "STANDBY"; UpdateUI(); };

            btnDisarm.Click += (s, e) => Disarm();
            btnFlatten.Click += (s, e) => isFlattenPending = true;
            btnHalfRisk.Click += (s, e) => isHalfRiskPending = true;
            btnBreakeven.Click += (s, e) => isBreakevenPending = true;
            btnCloseHalf.Click += (s, e) => isCloseHalfPending = true;

            BuildLevelMap();
            chartGrid.Children.Add(mainPanel);
        }

        private void ParseManualInputs()
        {
            Double.TryParse(boxT1.Text, out manT1); Double.TryParse(boxT2.Text, out manT2); 
            Double.TryParse(boxT3.Text, out manT3); Double.TryParse(boxT4.Text, out manT4);
            Double.TryParse(txtEntryPrice.Text, out manEntryPx);
        }

        private void ProcessPlaybook(bool isLong, int idx)
        {
            double t1 = 0, t2 = 0, t3 = 0, t4 = 0;
            isPlaybookActive = true;
            
            if (isLong)
            {
                if (idx == 1) { t1 = (L_B6 + L_B7) / 2; t2 = L_B7; t3 = (L_B7 + L_B8) / 2; t4 = L_B8; } 
                else if (idx == 2) { t1 = (L_B4 + L_B5) / 2; t2 = L_B5; t3 = L_B6; t4 = L_B6 + (20*TickSize); } 
                else if (idx == 3) { t1 = (L_B2 + L_B3) / 2; t2 = L_B3; t3 = L_B4; t4 = L_B4 + (20*TickSize); } 
                else if (idx == 4) { t1 = (L_POC + L_B1) / 2; t2 = (L_B1 + L_B2) / 2; t3 = L_B2; t4 = L_B2 + (20*TickSize); } 
                else if (idx == 5) { t1 = (L_R1 + L_POC) / 2; t2 = L_POC; } 
                else if (idx == 6) { t1 = (L_R1 + L_POC) / 2; t2 = L_POC; t3 = (L_POC + L_B1) / 2; t4 = L_B1; } 
                else if (idx == 7) { t1 = L_R1; t2 = L_POC; t3 = L_B1; t4 = L_B2; } 
                else if (idx == 8) { t1 = L_R1; t2 = (L_R1 + L_POC) / 2; t3 = L_POC; } 
                else if (idx == 9) { t1 = (L_R3 + L_R2) / 2; t2 = L_R2; } 
                else if (idx == 10) { t1 = L_R3; t2 = (L_R3 + L_R2) / 2; t3 = L_R2; } 
                else if (idx == 11) { t1 = L_R2; t2 = L_POC; t3 = L_B2; t4 = L_B4; } 
                else if (idx == 12) { t1 = (L_R6 + L_R5) / 2; t2 = L_R5; t3 = (L_R5 + L_R4) / 2; t4 = L_R4; } 
                else if (idx == 13) { t1 = L_R4; t2 = L_R2; t3 = L_R1; t4 = L_POC; } 
            }
            else
            {
                if (idx == 1) { t1 = (L_B6 + L_B5) / 2; t2 = L_B5; t3 = (L_B5 + L_B4) / 2; t4 = L_B4; } 
                else if (idx == 2) { t1 = L_B2; t2 = L_POC; t3 = L_R2; t4 = L_R4; } 
                else if (idx == 3) { t1 = L_B3; t2 = (L_B3 + L_B2) / 2; t3 = L_B2; } 
                else if (idx == 4) { t1 = (L_B3 + L_B2) / 2; t2 = L_B2; } 
                else if (idx == 5) { t1 = L_B1; t2 = (L_POC + L_B1) / 2; t3 = L_POC; } 
                else if (idx == 6) { t1 = L_B1; t2 = L_POC; t3 = L_R1; t4 = L_R2; } 
                else if (idx == 7) { t1 = (L_POC + L_B1) / 2; t2 = L_POC; t3 = (L_R1 + L_POC) / 2; t4 = L_R1; } 
                else if (idx == 8) { t1 = (L_POC + L_B1) / 2; t2 = L_POC; } 
                else if (idx == 9) { t1 = (L_R1 + L_POC) / 2; t2 = L_R1; t3 = L_R2; } 
                else if (idx == 10) { t1 = (L_R2 + L_R3) / 2; t2 = L_R3; t3 = L_R4; } 
                else if (idx == 11) { t1 = (L_R4 + L_R5) / 2; t2 = L_R5; t3 = L_R6; } 
                else if (idx == 12) { t1 = L_B4; t2 = L_B2; t3 = L_B1; t4 = L_POC; } 
                else if (idx == 13) { t1 = (L_R6 + L_R7) / 2; t2 = L_R7; t3 = (L_R7 + L_R8) / 2; t4 = L_R8; } 
            }
            boxT1.Text = t1.ToString("F2"); boxT2.Text = t2.ToString("F2"); 
            boxT3.Text = t3.ToString("F2"); boxT4.Text = t4.ToString("F2");
        }

        private void StartFlashTimer() { flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; flashTimer.Tick += (s, e) => { flashState = !flashState; if (isGoalReached) UpdateUI(); }; flashTimer.Start(); }

        private void UpdateUI()
        {
            if (lblStatus == null) return;
            ChartControl.Dispatcher.InvokeAsync(() => {
                dailyPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;
                lblStatus.Content = activeStatus;
                lblPnL.Content = dailyPnL.ToString("C");
                lblPnL.Foreground = dailyPnL >= 0 ? WPFBrushes.Lime : WPFBrushes.Red;

                if (isGoalReached) { lblStatus.Background = flashState ? WPFBrushes.Gold : WPFBrushes.DimGray; lblStatus.Foreground = flashState ? WPFBrushes.Black : WPFBrushes.White; } 
                else { lblStatus.Background = WPFBrushes.DimGray; lblStatus.Foreground = WPFBrushes.White; }

                btnScalpL.Background = isArmedScalpLong ? WPFBrushes.LimeGreen : WPFBrushes.DimGray;
                btnScalpS.Background = isArmedScalpShort ? WPFBrushes.Red : WPFBrushes.DimGray;
                btnCoreL.Background = isArmedCoreLong ? WPFBrushes.LimeGreen : WPFBrushes.DimGray;
                btnCoreS.Background = isArmedCoreShort ? WPFBrushes.Red : WPFBrushes.DimGray;

                btnNextBarL.Background = isNextBarPendingL ? WPFBrushes.DodgerBlue : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 0, 128));
                btnNextBarS.Background = isNextBarPendingS ? WPFBrushes.Magenta : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 0, 128));
                btnManualL.Background = isManualPendingL ? WPFBrushes.LimeGreen : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 128, 0));
                btnManualS.Background = isManualPendingS ? WPFBrushes.Red : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 128, 0, 0));
            });
        }

        private Button Btn(string txt, System.Windows.Media.Brush bg) { return new Button { Content = txt, Background = bg, Foreground = WPFBrushes.White, FontSize = 10, Margin = new Thickness(1), FontWeight = FontWeights.Bold, FontFamily = modernFont, HorizontalContentAlignment = HorizontalAlignment.Center }; }
        private Button SolidBtn(string txt, System.Windows.Media.Brush bg) { return new Button { Content = txt, Background = bg, Foreground = WPFBrushes.White, FontSize = 10, Margin = new Thickness(1), FontWeight = FontWeights.Bold, FontFamily = modernFont }; }
        private CheckBox Check(string content) { return new CheckBox { Content = content, Foreground = WPFBrushes.White, FontSize = 10, FontFamily = modernFont, Margin = new Thickness(2) }; }
        private Label LabelStyle(string content, System.Windows.Media.Brush fg, System.Windows.Media.Brush bg) { return new Label { Content = content, Foreground = fg, Background = bg, FontFamily = modernFont, FontWeight = FontWeights.Bold, HorizontalContentAlignment = HorizontalAlignment.Center, Width = 170 }; }
        private void AddRow(FrameworkElement c, int r) { Grid.SetRow(c, r); mainPanel.Children.Add(c); }
        private void AddDualRow(FrameworkElement l, FrameworkElement r, int row) { Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetColumn(l, 0); Grid.SetColumn(r, 1); g.Children.Add(l); g.Children.Add(r); Grid.SetRow(g, row); mainPanel.Children.Add(g); }
        private void DisposeWPFControls() { if (chartGrid != null && mainPanel != null) chartGrid.Children.Remove(mainPanel); }
        private void Disarm() { isArmedScalpLong = false; isArmedScalpShort = false; isArmedCoreLong = false; isArmedCoreShort = false; isManualPendingL = false; isManualPendingS = false; isManScalpPendingL = false; isManScalpPendingS = false; isHalfRiskPending = false; isBreakevenPending = false; isNextBarPendingL = false; isNextBarPendingS = false; if (!isGoalReached) activeStatus = "STANDBY"; UpdateUI(); }
        private ComboBox Combo() { var c = new ComboBox { IsEditable = true, FontSize = 10, Margin = new Thickness(2), Height = 20, FontFamily = modernFont }; c.DropDownClosed += (s, e) => { ComboBox cb = s as ComboBox; string sel = cb.SelectedItem as string; if (sel != null && levelMap.ContainsKey(sel)) cb.Text = levelMap[sel].ToString("F2"); }; return c; }
        private void PopulateCombos() { ChartControl.Dispatcher.InvokeAsync(() => { if (boxT1.Items.Count > 0) return; foreach (string name in orderedLevelNames) { boxT1.Items.Add(name); boxT2.Items.Add(name); boxT3.Items.Add(name); boxT4.Items.Add(name); } }); }

        private void ResetAllLevels() { L_B8 = L_B7 = L_B6 = L_B5 = L_B4 = L_B3 = L_B2 = L_B1 = 0; L_POC = 0; L_R1 = L_R2 = L_R3 = L_R4 = L_R5 = L_R6 = L_R7 = L_R8 = 0; }
        private void ValidateLevels() { levelsValid = (L_B8 > L_B7 && L_B7 > L_B6 && L_B6 > L_B5 && L_B5 > L_B4 && L_B4 > L_B3 && L_B3 > L_B2 && L_B2 > L_B1 && L_B1 > L_POC && L_POC > L_R1 && L_R1 > L_R2 && L_R2 > L_R3 && L_R3 > L_R4 && L_R4 > L_R5 && L_R5 > L_R6 && L_R6 > L_R7 && L_R7 > L_R8); }
        
        private void IdentifyContext() {
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
            else { zoneName = "WAITING"; }
        }
        private bool CheckZone(double p, double top, double bot, string name) { if (top > 0 && bot > 0 && p <= top && p >= bot) { zoneName = name; zoneHigh = top; zoneLow = bot; return true; } return false; }

        private void CalculateTactics() {
            double range = zoneHigh - zoneLow; if (range <= 0) return;
            bool isNearTop = (Close[0] > zoneLow + (0.75 * range));
            bool isNearBot = (Close[0] < zoneLow + (0.25 * range));
            
            if (isNearTop) { shortT1 = (zoneHigh + zoneLow) / 2.0; shortT2 = zoneLow; hud_ShortPlan = string.Format("SHORT (Rejection):\n T1(50%) {0:N2}\n T2(Lvl) {1:N2}", shortT1, shortT2); } 
            else { shortT1 = (zoneLow + levelBelow) / 2.0; shortT2 = levelBelow; hud_ShortPlan = string.Format("SHORT (Breakout):\n T1(50%) {0:N2}\n T2(Lvl) {1:N2}", shortT1, shortT2); }
            if (isNearBot) { longT1 = (zoneLow + zoneHigh) / 2.0; longT2 = zoneHigh; hud_LongPlan = string.Format("LONG (Bounce):\n T1(50%) {0:N2}\n T2(Lvl) {1:N2}", longT1, longT2); } 
            else { longT1 = (zoneHigh + levelAbove) / 2.0; longT2 = levelAbove; hud_LongPlan = string.Format("LONG (Breakout):\n T1(50%) {0:N2}\n T2(Lvl) {1:N2}", longT1, longT2); }
        }

        private void BuildLevelMap() {
            levelMap.Clear(); orderedLevelNames.Clear();
            void Add(string n, double v) { if (v > 0) { levelMap[n] = v; orderedLevelNames.Add(n); } }
            void AddMid(string n1, string n2, double v1, double v2, string alias) { if (v1 > 0 && v2 > 0) { double mid = (v1 + v2) / 2.0; levelMap[alias] = mid; orderedLevelNames.Add(alias); } }
            
            Add("B8", L_B8); AddMid("B8", "B7", L_B8, L_B7, "B87_50");
            Add("B7", L_B7); AddMid("B7", "B6", L_B7, L_B6, "B76_50");
            Add("B6", L_B6); AddMid("B6", "B5", L_B6, L_B5, "B65_50");
            Add("B5", L_B5); AddMid("B5", "B4", L_B5, L_B4, "B54_50");
            Add("B4", L_B4); AddMid("B4", "B3", L_B4, L_B3, "B43_50");
            Add("B3", L_B3); AddMid("B3", "B2", L_B3, L_B2, "B32_50");
            Add("B2", L_B2); AddMid("B2", "B1", L_B2, L_B1, "B21_50");
            Add("B1", L_B1); AddMid("B1", "POC", L_B1, L_POC, "B1_POC_50");
            Add("POC", L_POC); AddMid("POC", "R1", L_POC, L_R1, "POC_R1_50");
            Add("R1", L_R1); AddMid("R1", "R2", L_R1, L_R2, "R12_50");
            Add("R2", L_R2); AddMid("R2", "R3", L_R2, L_R3, "R23_50");
            Add("R3", L_R3); AddMid("R3", "R4", L_R3, L_R4, "R34_50");
            Add("R4", L_R4); AddMid("R4", "R5", L_R4, L_R5, "R45_50");
            Add("R5", L_R5); AddMid("R5", "R6", L_R5, L_R6, "R56_50");
            Add("R6", L_R6); AddMid("R6", "R7", L_R6, L_R7, "R67_50");
            Add("R7", L_R7); AddMid("R7", "R8", L_R7, L_R8, "R78_50");
            Add("R8", L_R8);
        }

        private void DrawCoreLines() { foreach (var kvp in levelMap) { if (kvp.Value <= 0) continue; if (kvp.Key.Contains("50") || kvp.Key.Contains("_")) Draw.HorizontalLine(this, kvp.Key + "_Line", kvp.Value, WPFBrushes.Gray, DashStyleHelper.Dash, 1); else Draw.HorizontalLine(this, kvp.Key + "_Line", kvp.Value, WPFBrushes.White, DashStyleHelper.Solid, 2); } }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (chartControl == null || chartScale == null || ChartBars == null) return;
            base.OnRender(chartControl, chartScale);

            if (dxTextFormatCenter == null) dxTextFormatCenter = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Calibri", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 11.0f) { TextAlignment = SharpDX.DirectWrite.TextAlignment.Center, ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near };
            if (dxTextFormatLeft == null) dxTextFormatLeft = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Calibri", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 11.0f) { TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading, ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near };
            
            if (dxBrushWhite == null) dxBrushWhite = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.White);
            if (dxBrushRed == null) dxBrushRed = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.Red);
            SharpDX.Direct2D1.SolidColorBrush blueBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.DodgerBlue);

            // 1. Draw Core Level Labels Centered on Chart
            float centerX = (float)(chartControl.CanvasRight - chartControl.CanvasLeft) / 2.0f + (float)chartControl.CanvasLeft;
            foreach (var kvp in levelMap) { if (kvp.Value > 0 && kvp.Value >= chartScale.MinValue && kvp.Value <= chartScale.MaxValue) RenderTarget.DrawText(kvp.Key, dxTextFormatCenter, new SharpDX.RectangleF(centerX - 150, (float)chartScale.GetYByValue(kvp.Value) - 6, 300, 20), dxBrushWhite); }

            // 2. BOTTOM LEFT DATA PANEL
            float leftX = (float)chartControl.CanvasLeft + 10; 
            float bottomY = (float)ChartPanel.H - 160f; // <--- FIXED CS1061 HERE
            
            if (!levelsValid) { 
                RenderTarget.DrawText("!!! LEVEL ERROR !!!", dxTextFormatLeft, new SharpDX.RectangleF(leftX, bottomY, 300, 20), dxBrushRed); 
                RenderTarget.DrawText("CHECK CORE LEVELS IN SETTINGS", dxTextFormatLeft, new SharpDX.RectangleF(leftX, bottomY + 20, 300, 20), dxBrushRed); 
                blueBrush.Dispose(); return; 
            }
            
            string statsLine = string.Format("SESSION | Trades: {0} | Wins: {1} | Loss: {2} | To Goal: {3}", sessionTrades, sessionWins, sessionLosses, (ProfitGoal - dailyPnL).ToString("C"));
            RenderTarget.DrawText(statsLine, dxTextFormatLeft, new SharpDX.RectangleF(leftX, bottomY, 500, 20), dxBrushWhite); 
            bottomY += 20;

            RenderTarget.DrawText("ZONE: " + zoneName, dxTextFormatLeft, new SharpDX.RectangleF(leftX, bottomY, 300, 20), dxBrushWhite); 
            bottomY += 18;

            foreach (string line in hud_LongPlan.Split('\n')) { RenderTarget.DrawText(line, dxTextFormatLeft, new SharpDX.RectangleF(leftX, bottomY, 300, 20), blueBrush); bottomY += 16; } bottomY += 8;
            foreach (string line in hud_ShortPlan.Split('\n')) { RenderTarget.DrawText(line, dxTextFormatLeft, new SharpDX.RectangleF(leftX, bottomY, 300, 20), blueBrush); bottomY += 16; }
            
            blueBrush.Dispose();
        }
        
        // PARAMETERS
        [NinjaScriptProperty, Display(Name = "Reset Levels", GroupName = "1. Core Levels", Order = 99)] public bool ResetLevels { get; set; }
        [NinjaScriptProperty, Display(Name = "Show Core Lines", GroupName = "1. Core Levels", Order = 100)] public bool ShowLines { get; set; }
        
        // 2. SCALP TACTICS
        [NinjaScriptProperty, Display(Name = "Initial Stop (ATR)", GroupName = "2. Scalp Tactics", Order = 1)] public double S_InitialStopAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name = "Initial Stop (Ticks)", GroupName = "2. Scalp Tactics", Order = 2)] public int S_InitialStopTicks { get; set; }
        [NinjaScriptProperty, Range(0, 100), Display(Name = "L1 Qty", GroupName = "2. Scalp Tactics", Order = 3)] public int S_Qty1 { get; set; }
        [NinjaScriptProperty, Display(Name = "L1 Target", GroupName = "2. Scalp Tactics", Order = 4)] public int S_Leg1TargetTicks { get; set; }
        [NinjaScriptProperty, Range(0, 100), Display(Name = "L2 Qty", GroupName = "2. Scalp Tactics", Order = 5)] public int S_Qty2 { get; set; }
        [NinjaScriptProperty, Display(Name = "L2 Target (ATR)", GroupName = "2. Scalp Tactics", Order = 6)] public double S_Leg2TargetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name = "L2 Trail", GroupName = "2. Scalp Tactics", Order = 7)] public TrailMode S_Leg2TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name = "L2 BarN", GroupName = "2. Scalp Tactics", Order = 8)] public int S_Leg2BarN { get; set; }
        [NinjaScriptProperty, Range(0, 100), Display(Name = "L3 Qty", GroupName = "2. Scalp Tactics", Order = 9)] public int S_Qty3 { get; set; }
        [NinjaScriptProperty, Display(Name = "L3 Target (ATR)", GroupName = "2. Scalp Tactics", Order = 10)] public double S_Leg3TargetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name = "L3 Trail", GroupName = "2. Scalp Tactics", Order = 11)] public TrailMode S_Leg3TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name = "L3 Ratchet", GroupName = "2. Scalp Tactics", Order = 12)] public double S_Leg3RatchetAtrMult { get; set; }
        [NinjaScriptProperty, Range(0, 100), Display(Name = "L4 Qty", GroupName = "2. Scalp Tactics", Order = 13)] public int S_Qty4 { get; set; }
        [NinjaScriptProperty, Display(Name = "L4 Target (ATR)", GroupName = "2. Scalp Tactics", Order = 14)] public double S_Leg4TargetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name = "L4 Trail", GroupName = "2. Scalp Tactics", Order = 15)] public TrailMode S_Leg4TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name = "L4 Ratchet", GroupName = "2. Scalp Tactics", Order = 16)] public double S_Leg4RatchetAtrMult { get; set; }

        // 3. CORE TACTICS
        [NinjaScriptProperty, Display(Name = "Core Target Offset (Ticks)", GroupName = "3. Core Tactics", Order = 0)] public int CoreTargetOffsetTicks { get; set; }
        [NinjaScriptProperty, Display(Name = "Initial Stop (ATR)", GroupName = "3. Core Tactics", Order = 1)] public double C_InitialStopAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name = "Initial Stop (Ticks)", GroupName = "3. Core Tactics", Order = 2)] public int C_InitialStopTicks { get; set; }
        [NinjaScriptProperty, Range(0, 100), Display(Name = "L1 Qty", GroupName = "3. Core Tactics", Order = 3)] public int C_Qty1 { get; set; }
        [NinjaScriptProperty, Display(Name = "L1 Trail", GroupName = "3. Core Tactics", Order = 4)] public TrailMode C_Leg1TrailMode { get; set; }
        [NinjaScriptProperty, Range(0, 100), Display(Name = "L2 Qty", GroupName = "3. Core Tactics", Order = 5)] public int C_Qty2 { get; set; }
        [NinjaScriptProperty, Display(Name = "L2 Trail", GroupName = "3. Core Tactics", Order = 6)] public TrailMode C_Leg2TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name = "L2 BarN", GroupName = "3. Core Tactics", Order = 7)] public int C_Leg2BarN { get; set; }
        [NinjaScriptProperty, Range(0, 100), Display(Name = "L3 Qty", GroupName = "3. Core Tactics", Order = 8)] public int C_Qty3 { get; set; }
        [NinjaScriptProperty, Display(Name = "L3 Trail", GroupName = "3. Core Tactics", Order = 9)] public TrailMode C_Leg3TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name = "L3 Ratchet", GroupName = "3. Core Tactics", Order = 10)] public double C_Leg3RatchetAtrMult { get; set; }
        [NinjaScriptProperty, Range(0, 100), Display(Name = "L4 Qty", GroupName = "3. Core Tactics", Order = 11)] public int C_Qty4 { get; set; }
        [NinjaScriptProperty, Display(Name = "L4 Trail", GroupName = "3. Core Tactics", Order = 12)] public TrailMode C_Leg4TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name = "L4 Ratchet", GroupName = "3. Core Tactics", Order = 13)] public double C_Leg4RatchetAtrMult { get; set; }

        [NinjaScriptProperty, Display(Name = "Context Minutes", GroupName = "4. Context")] public int ContextMinutes { get; set; }
        [NinjaScriptProperty, Display(Name = "Breakout Offset", GroupName = "4. Context")] public int BreakoutOffsetTicks { get; set; }
        [NinjaScriptProperty, Display(Name = "Smart Alloc Ticks", GroupName = "4. Context")] public int SmartAllocTicks { get; set; }
        
        [NinjaScriptProperty, Display(Name = "Manual Stop Buffer", GroupName = "5. Manual")] public int ManualStopBufferTicks { get; set; }
        [NinjaScriptProperty, Display(Name = "Playbook Stop", GroupName = "5. Manual")] public int PlaybookStopTicks { get; set; }
        [NinjaScriptProperty, Display(Name = "Breakeven Offset", GroupName = "5. Manual")] public int BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty, Display(Name = "Safe Chop Limit", GroupName = "6. Safety")] public double SafeChopLimit { get; set; }
        [NinjaScriptProperty, Display(Name = "Safe ADX Limit", GroupName = "6. Safety")] public double SafeAdxLimit { get; set; }
    }
}