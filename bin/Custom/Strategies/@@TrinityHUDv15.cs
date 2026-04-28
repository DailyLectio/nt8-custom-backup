// CC BY-NC 4.0
// Strategy: TrinityHUDv15
// Updates: 
// 1. ADDED DIAGNOSTIC PRINTS (Check Output Window).
// 2. Fixed Fail-Safe Boolean Logic (Thread Sync).
// 3. Wrapped Manual Execution in Try-Catch to reveal silent errors.

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
    public class TrinityHUDv15 : Strategy
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
        [NinjaScriptProperty, Display(Name = "Daily Profit Goal ($)", GroupName = "6. Performance")] public double ProfitGoal { get; set; } = 1500;
        [NinjaScriptProperty, Display(Name = "Daily Max Loss ($)", GroupName = "6. Performance")] public double DailyMaxLoss { get; set; } = 1000;
        
        // =========================================================
        //    2. LOGIC VARIABLES
        // =========================================================
        private ADX adx;
        private DM dm;
        private ATR atrAlgo;
        private EMA ema50; // Fail Safe
        
        // Custom VWAP Variables
        private double sessionVol = 0;
        private double sessionPV = 0;
        private double currentVwap = 0;

        public enum TrailMode { None, BarNTrail, AtrRatchet }
        private int entryBar = -1;
        private double highSinceEntry = double.MinValue;
        private double lowSinceEntry = double.MaxValue;
        private double sessionStartCumProfit = 0;
        private double dailyPnL = 0;
        private bool isGoalReached = false;
        private bool isCombosPopulated = false;

        // UI ELEMENTS
        private Grid chartGrid, mainPanel;
        private Button btnScalpL, btnScalpS, btnCoreL, btnCoreS;
        private Button btnManScalpL, btnManScalpS, btnNextBarL, btnNextBarS; 
        private Button btnHalfRisk, btnBreakeven, btnDisarm, btnCloseHalf, btnFlatten;
        private Button btnManualL, btnManualS;
        
        // Fail Safe Checkboxes & State Bools
        private CheckBox chkSafeChop, chkSafeAdx, chkSafeEma, chkSafeVwap;
        // Defaults false
        private volatile bool useSafeChop = false;
        private volatile bool useSafeAdx = false;
        private volatile bool useSafeEma = false;
        private volatile bool useSafeVwap = false;

        private ComboBox cbLongPlays, cbShortPlays; 
        private TextBox txtEntryPrice; 
        private ComboBox boxT1, boxT2, boxT3;
        private Label lblStatus, lblPnL;

        // ANIMATION
        private DispatcherTimer flashTimer;
        private bool flashState = false;

        // DX RESOURCES
        private SharpDX.DirectWrite.TextFormat dxTextFormat;
        private SharpDX.Direct2D1.SolidColorBrush dxBrushWhite;

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

        // MANUAL & PLAYBOOK STATE
        private bool isPlaybookActive = false; 
        
        private string zoneName = "WAITING";
        private string activeStatus = "STANDBY";
        private string allocStatus = "";
        private bool levelsValid = true;
        private double zoneHigh = 0, zoneLow = 0, levelAbove = 0, levelBelow = 0;
        private double longT1, longT2, longGatePrice;
        private double shortT1, shortT2, shortGatePrice;
        private string hud_LongPlan = "", hud_ShortPlan = "";

        private double manT1, manT2, manT3, manEntryPx;
        private bool isManualActive = false;
        private double manualAnchorPrice = 0, manualMidLine = 0;
        private double userOverrideStopPrice = 0;

        private Dictionary<string, double> levelMap = new Dictionary<string, double>();
        private List<string> orderedLevelNames = new List<string>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Trinity HUD v15";
                Calculate = Calculate.OnPriceChange;
                EntriesPerDirection = 5;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsOverlay = true;

                InitialStopAtrMult = 0.75; InitialStopTicks = 35;
                Qty1 = 2; Leg1TargetTicks = 40;
                Qty2 = 1; Leg2TargetAtrMult = 0.88; Leg2TrailMode = TrailMode.BarNTrail; Leg2BarN = 2;
                Qty3 = 1; Leg3TargetAtrMult = 1.5; Leg3TrailMode = TrailMode.AtrRatchet; Leg3RatchetAtrMult = 1.5;

                ContextMinutes = 1;
                BreakoutOffsetTicks = 1;
                SmartAllocTicks = 12;

                ManualStopBufferTicks = 10;
                PlaybookStopTicks = 12;
                BreakevenOffsetTicks = 5;
                
                ShowLines = true;
                SafeChopLimit = 60;
                SafeAdxLimit = 20;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, ContextMinutes);
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(14); 
                dm = DM(14);
                atrAlgo = ATR(14);
                ema50 = EMA(50);
            }
            else if (State == State.Historical)
            {
                if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(() => { CreateWPFControls(); StartFlashTimer(); });
            }
            else if (State == State.Terminated)
            {
                if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(() => DisposeWPFControls());
                if (dxTextFormat != null) dxTextFormat.Dispose();
                if (dxBrushWhite != null) dxBrushWhite.Dispose();
                if (flashTimer != null) flashTimer.Stop();
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time) { if (State == State.Realtime) UpdateUI(); }
        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition) { if (State == State.Realtime) UpdateUI(); }

        protected override void OnBarUpdate()
        {
            try
            {
                if (ResetLevels) { ResetAllLevels(); ResetLevels = false; }
                if (CurrentBar < 20) return;

                if (Bars.IsFirstBarOfSession) { sessionVol = 0; sessionPV = 0; }
                sessionVol += Volume[0];
                sessionPV += Volume[0] * ((High[0] + Low[0] + Close[0]) / 3.0);
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

                if (CurrentBar > 1) {
                    longGatePrice = High[1] + (BreakoutOffsetTicks * TickSize);
                    shortGatePrice = Low[1] - (BreakoutOffsetTicks * TickSize);
                }

                if (isFlattenPending) { ExecuteFlatten(); return; }
                if (Position.MarketPosition != MarketPosition.Flat) {
                    if (isHalfRiskPending) ExecuteHalfRiskLogic();
                    if (isBreakevenPending) ExecuteBreakevenLogic();
                    if (isCloseHalfPending) ExecuteCloseHalf();
                    if (isManualActive) ManageManualTrade();
                    else {
                        if (IsFirstTickOfBar) {
                            highSinceEntry = Math.Max(highSinceEntry, High[1]);
                            lowSinceEntry = Math.Min(lowSinceEntry, Low[1]);
                            UpdateTrailingStops();
                        }
                    }
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

                    double _adx = adx[1];
                    double _diPlus = dm.DiPlus[1];
                    double _diMinus = dm.DiMinus[1];
                    double _prevAdx = adx[2];
                    double _prevDiPlus = dm.DiPlus[2];
                    double _prevDiMinus = dm.DiMinus[2];
                    
                    bool hlRange = _adx <= 20; 
                    bool adxRising = _adx > _prevAdx;
                    
                    bool diUp = _diPlus >= _diMinus;
                    bool diDn = _diMinus > _diPlus;
                    bool diUpCross = diUp && (_prevDiPlus < _prevDiMinus);
                    bool diDnCross = diDn && (_prevDiMinus <= _prevDiPlus);
                    bool strongBull = _diPlus >= 35;
                    bool strongBear = _diMinus >= 35;

                    bool signalLong = (!hlRange && diUp && adxRising) && (diUpCross || (_adx > 20 && _prevAdx <= 20) || strongBull);
                    bool signalShort = (!hlRange && diDn && adxRising) && (diDnCross || (_adx > 20 && _prevAdx <= 20) || strongBear);

                    if (isArmedCoreLong && signalLong && Close[1] >= longGatePrice) ExecuteCoreTrade(true);
                    else if (isArmedCoreShort && signalShort && Close[1] <= shortGatePrice) ExecuteCoreTrade(false);

                    if (isArmedScalpLong && signalLong) ExecuteScalpMoonRunner(true);
                    else if (isArmedScalpShort && signalShort) ExecuteScalpMoonRunner(false);
                }
            }
            catch (Exception e) { Print("Trinity Error: " + e.Message); }
        }

        private bool CheckFailSafes(bool isLong)
        {
            // Print status for debug
            Print(string.Format("CheckFailSafes: Chop={0} ADX={1} EMA={2} VWAP={3}", useSafeChop, useSafeAdx, useSafeEma, useSafeVwap));

            if (useSafeChop && adx[0] < 20) { Print("FAIL SAFE: CHOP (ADX < 20)"); activeStatus = "SAFE: CHOP"; return false; }
            
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
            
            Print("Fail Safes Passed.");
            return true;
        }

        private void ExecuteManualTrade(bool isLong)
        {
            try {
                Print("ExecuteManualTrade Triggered. Long: " + isLong);
                
                if (!CheckFailSafes(isLong)) { 
                    Print("CheckFailSafes returned FALSE. Aborting.");
                    isManualPendingL = false; isManualPendingS = false; return; 
                }

                // 1. Get Targets
                double t1 = manT1; double t2 = manT2; double t3 = manT3;
                if (t1 == 0) t1 = isLong ? Close[0] + (20 * TickSize) : Close[0] - (20 * TickSize);
                
                // 2. Routing
                bool isStop = false; bool isLimit = false;
                double entryPrice = manEntryPx;
                if (entryPrice > 0) {
                    double current = Close[0];
                    if (isLong) { if (entryPrice > current) isStop = true; else isLimit = true; }
                    else { if (entryPrice < current) isStop = true; else isLimit = true; }
                }

                // 3. Stop Logic
                manualAnchorPrice = isLong ? zoneLow : zoneHigh;
                if (manualAnchorPrice == 0) manualAnchorPrice = Close[0];
                manualMidLine = (zoneHigh + zoneLow) / 2.0;

                int stopTicks = (isPlaybookActive) ? PlaybookStopTicks : ManualStopBufferTicks;
                double stopPrice = isLong ? manualAnchorPrice - (stopTicks * TickSize) : manualAnchorPrice + (stopTicks * TickSize);
                
                if (entryPrice > 0) {
                     if (isLong && stopPrice >= entryPrice) stopPrice = entryPrice - (20 * TickSize);
                     if (!isLong && stopPrice <= entryPrice) stopPrice = entryPrice + (20 * TickSize);
                }

                string sig = "Man_" + (isLong ? "L" : "S");
                isManualActive = true; userOverrideStopPrice = 0;
                entryBar = CurrentBar; highSinceEntry = High[0]; lowSinceEntry = Low[0];

                Print("Submitting Orders...");
                SetStopLoss(sig, CalculationMode.Price, stopPrice, false);
                if (t2 > 0) SetStopLoss(sig + "_2", CalculationMode.Price, stopPrice, false);
                if (t3 > 0) SetStopLoss(sig + "_3", CalculationMode.Price, stopPrice, false);

                if (isLong) {
                    SetProfitTarget(sig, CalculationMode.Price, t1);
                    if (isStop) EnterLongStopMarket(Qty1, entryPrice, sig); else if (isLimit) EnterLongLimit(Qty1, entryPrice, sig); else EnterLong(Qty1, sig);
                    if (t2 > 0) { SetProfitTarget(sig + "_2", CalculationMode.Price, t2); if (isStop) EnterLongStopMarket(Qty2, entryPrice, sig + "_2"); else if (isLimit) EnterLongLimit(Qty2, entryPrice, sig + "_2"); else EnterLong(Qty2, sig + "_2"); }
                    if (t3 > 0) { SetProfitTarget(sig + "_3", CalculationMode.Price, t3); if (isStop) EnterLongStopMarket(Qty3, entryPrice, sig + "_3"); else if (isLimit) EnterLongLimit(Qty3, entryPrice, sig + "_3"); else EnterLong(Qty3, sig + "_3"); }
                } else {
                    SetProfitTarget(sig, CalculationMode.Price, t1);
                    if (isStop) EnterShortStopMarket(Qty1, entryPrice, sig); else if (isLimit) EnterShortLimit(Qty1, entryPrice, sig); else EnterShort(Qty1, sig);
                    if (t2 > 0) { SetProfitTarget(sig + "_2", CalculationMode.Price, t2); if (isStop) EnterShortStopMarket(Qty2, entryPrice, sig + "_2"); else if (isLimit) EnterShortLimit(Qty2, entryPrice, sig + "_2"); else EnterShort(Qty2, sig + "_2"); }
                    if (t3 > 0) { SetProfitTarget(sig + "_3", CalculationMode.Price, t3); if (isStop) EnterShortStopMarket(Qty3, entryPrice, sig + "_3"); else if (isLimit) EnterShortLimit(Qty3, entryPrice, sig + "_3"); else EnterShort(Qty3, sig + "_3"); }
                }

                Print("Orders Submitted Successfully.");
                isManualPendingL = false; isManualPendingS = false;
                allocStatus = "MANUAL: " + (isStop ? "STOP" : (isLimit ? "LIMIT" : "MKT"));
                Disarm();
            }
            catch (Exception ex) {
                Print("CRITICAL ERROR IN ExecuteManualTrade: " + ex.ToString());
                isManualPendingL = false; isManualPendingS = false;
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

            if (useSmart) {
                allocStatus = "SMART ALLOC";
                int totalQty = 4;
                if (isLong) { EnterLong(totalQty, signal + "_Full"); SetProfitTarget(signal + "_Full", CalculationMode.Price, t2); }
                else { EnterShort(totalQty, signal + "_Full"); SetProfitTarget(signal + "_Full", CalculationMode.Price, t2); }
            } else {
                allocStatus = "STD ALLOC";
                if (isLong) { EnterLong(2, signal + "_Mid"); EnterLong(2, signal + "_Full"); SetProfitTarget(signal + "_Mid", CalculationMode.Price, t1); SetProfitTarget(signal + "_Full", CalculationMode.Price, t2); }
                else { EnterShort(2, signal + "_Mid"); EnterShort(2, signal + "_Full"); SetProfitTarget(signal + "_Mid", CalculationMode.Price, t1); SetProfitTarget(signal + "_Full", CalculationMode.Price, t2); }
            }
            Disarm();
        }

        private void ExecuteScalpMoonRunner(bool isLong)
        {
            isManualActive = false; entryBar = CurrentBar; highSinceEntry = High[0]; lowSinceEntry = Low[0];
            double currentAtr = atrAlgo[0];
            double t1 = Leg1TargetTicks;
            double t2 = Math.Max(5, Math.Round((currentAtr * Leg2TargetAtrMult) / TickSize));
            double t3 = Math.Max(10, Math.Round((currentAtr * Leg3TargetAtrMult) / TickSize));
            double stop = Math.Max(5, Math.Round((currentAtr * InitialStopAtrMult) / TickSize));
            string dir = isLong ? "L" : "S";

            allocStatus = "SCALP MOON"; userOverrideStopPrice = 0;

            if (Qty1 > 0) { string s = "Scalp_" + dir + "1"; SetStopLoss(s, CalculationMode.Ticks, stop, false); SetProfitTarget(s, CalculationMode.Ticks, t1); if (isLong) EnterLong(Qty1, s); else EnterShort(Qty1, s); }
            if (Qty2 > 0) { string s = "Scalp_" + dir + "2"; SetStopLoss(s, CalculationMode.Ticks, stop, false); SetProfitTarget(s, CalculationMode.Ticks, t2); if (isLong) EnterLong(Qty2, s); else EnterShort(Qty2, s); }
            if (Qty3 > 0) { string s = "Scalp_" + dir + "3"; SetStopLoss(s, CalculationMode.Ticks, stop, false); SetProfitTarget(s, CalculationMode.Ticks, t3); if (isLong) EnterLong(Qty3, s); else EnterShort(Qty3, s); }
            Disarm();
        }

        private void ExecuteManualScalp(bool isLong)
        {
            if (!CheckFailSafes(isLong)) { isManScalpPendingL = false; isManScalpPendingS = false; return; }

            double lim = isLong ? Close[0] + (5 * TickSize) : Close[0] - (5 * TickSize);
            double currentAtr = atrAlgo[0];
            double t1 = Leg1TargetTicks;
            double t2 = Math.Max(5, Math.Round((currentAtr * Leg2TargetAtrMult) / TickSize));
            double t3 = Math.Max(10, Math.Round((currentAtr * Leg3TargetAtrMult) / TickSize));
            double stop = Math.Max(5, Math.Round((currentAtr * InitialStopAtrMult) / TickSize));
            string dir = isLong ? "L" : "S";
            allocStatus = "MAN SCALP"; userOverrideStopPrice = 0;

            if (Qty1 > 0) { string s = "MScalp_" + dir + "1"; SetStopLoss(s, CalculationMode.Ticks, stop, false); SetProfitTarget(s, CalculationMode.Ticks, t1); if (isLong) EnterLongLimit(Qty1, lim, s); else EnterShortLimit(Qty1, lim, s); }
            if (Qty2 > 0) { string s = "MScalp_" + dir + "2"; SetStopLoss(s, CalculationMode.Ticks, stop, false); SetProfitTarget(s, CalculationMode.Ticks, t2); if (isLong) EnterLongLimit(Qty2, lim, s); else EnterShortLimit(Qty2, lim, s); }
            if (Qty3 > 0) { string s = "MScalp_" + dir + "3"; SetStopLoss(s, CalculationMode.Ticks, stop, false); SetProfitTarget(s, CalculationMode.Ticks, t3); if (isLong) EnterLongLimit(Qty3, lim, s); else EnterShortLimit(Qty3, lim, s); }
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
        private void ApplyOverrideStop(double price) { string[] sigs = { "Scalp_L1", "Scalp_L2", "Scalp_L3", "Scalp_S1", "Scalp_S2", "Scalp_S3", "Core_L_Mid", "Core_L_Full", "Core_S_Mid", "Core_S_Full", "Man_L", "Man_L_2", "Man_L_3", "Man_S", "Man_S_2", "Man_S_3", "MScalp_L1", "MScalp_L2", "MScalp_L3", "MScalp_S1", "MScalp_S2", "MScalp_S3" }; foreach (string s in sigs) SetStopLoss(s, CalculationMode.Price, price, false); }
        private void ManageManualTrade() 
        { 
            if (isPlaybookActive)
            {
                if (Qty2 > 0) ApplyTrail(Qty2, "Man_L_2", "Man_S_2", TrailMode.BarNTrail, 1, 0); 
                if (Qty3 > 0) ApplyTrail(Qty3, "Man_L_3", "Man_S_3", TrailMode.AtrRatchet, 0, Leg3RatchetAtrMult);
            }
            else 
            {
                if (Position.MarketPosition == MarketPosition.Long) { if (Close[0] > manualMidLine + TickSize) { double be = Position.AveragePrice; if (userOverrideStopPrice == 0 || be > userOverrideStopPrice) ApplyOverrideStop(be); } } 
                else if (Position.MarketPosition == MarketPosition.Short) { if (Close[0] < manualMidLine - TickSize) { double be = Position.AveragePrice; if (userOverrideStopPrice == 0 || be < userOverrideStopPrice) ApplyOverrideStop(be); } }
            }
        }
        
        private void UpdateTrailingStops() { if (Qty2 > 0) ApplyTrail(Qty2, "Scalp_L2", "Scalp_S2", Leg2TrailMode, Leg2BarN, 0); if (Qty3 > 0) ApplyTrail(Qty3, "Scalp_L3", "Scalp_S3", Leg3TrailMode, 0, Leg3RatchetAtrMult); }
        private void ApplyTrail(int qty, string longSig, string shortSig, TrailMode mode, int barN, double atrMult) { if (mode == TrailMode.None && userOverrideStopPrice == 0) return; bool isLong = Position.MarketPosition == MarketPosition.Long; string signal = isLong ? longSig : shortSig; double newStop = 0; if (mode == TrailMode.BarNTrail) { int idx = Math.Min(barN, CurrentBar); if (isLong) newStop = Low[idx]; else newStop = High[idx]; } else if (mode == TrailMode.AtrRatchet) { double rat = atrAlgo[0] * atrMult; if (isLong) newStop = highSinceEntry - rat; else newStop = lowSinceEntry + rat; } if (userOverrideStopPrice != 0) { if (newStop == 0) newStop = userOverrideStopPrice; else { if (isLong) newStop = Math.Max(newStop, userOverrideStopPrice); else newStop = Math.Min(newStop, userOverrideStopPrice); } } if (newStop != 0) SetStopLoss(signal, CalculationMode.Price, newStop, false); }

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
            cbLongPlays.Items.Add("7. R2 to B2 x"); cbLongPlays.Items.Add("8. R2 to R4"); cbLongPlays.Items.Add("9. R2 to POC");
            cbLongPlays.Items.Add("10. R3 to R2"); cbLongPlays.Items.Add("11. R4 to R2"); cbLongPlays.Items.Add("12. R4 to B4 x");
            cbLongPlays.Items.Add("13. R6 to R4"); cbLongPlays.Items.Add("14. R5 to POC");
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
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Label lblE = new Label { Content = "Entry Px:", Foreground = WPFBrushes.White, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            txtEntryPrice = new TextBox { FontSize = 11, Height = 20, Margin = new Thickness(2), Text = "" };
            Grid.SetColumn(lblE, 0); Grid.SetColumn(txtEntryPrice, 1); entryGrid.Children.Add(lblE); entryGrid.Children.Add(txtEntryPrice);
            AddRow(entryGrid, 9);

            boxT1 = Combo(); boxT2 = Combo(); boxT3 = Combo();
            AddRow(boxT1, 10); AddRow(boxT2, 11); AddRow(boxT3, 12);
            
            Label lblFail = new Label { Content = "FAIL SAFE", Foreground = WPFBrushes.Cyan, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Bold, FontFamily = modernFont };
            AddRow(lblFail, 14);
            
            chkSafeChop = Check("Chop"); chkSafeAdx = Check("ADX");
            chkSafeEma = Check("EMA"); chkSafeVwap = Check("VWAP");
            
            chkSafeChop.Click += (s,e) => useSafeChop = (chkSafeChop.IsChecked == true);
            chkSafeAdx.Click += (s,e) => useSafeAdx = (chkSafeAdx.IsChecked == true);
            chkSafeEma.Click += (s,e) => useSafeEma = (chkSafeEma.IsChecked == true);
            chkSafeVwap.Click += (s,e) => useSafeVwap = (chkSafeVwap.IsChecked == true);
            
            AddDualRow(chkSafeChop, chkSafeAdx, 15);
            AddDualRow(chkSafeEma, chkSafeVwap, 16);

            System.Windows.Media.SolidColorBrush gC = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 128, 0)); 
            System.Windows.Media.SolidColorBrush rC = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 128, 0, 0)); 
            System.Windows.Media.SolidColorBrush bC = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 0, 128)); 
            
            btnManScalpL = Btn("SCALP", gC); btnManScalpS = Btn("SCALP", rC);
            btnNextBarL = Btn("NEXT", bC); btnNextBarS = Btn("NEXT", bC);
            btnManualL = Btn("CORE", gC); btnManualS = Btn("CORE", rC);

            AddDualRow(btnManScalpL, btnManScalpS, 18);
            AddDualRow(btnNextBarL, btnNextBarS, 19);
            AddDualRow(btnManualL, btnManualS, 20);

            btnHalfRisk = SolidBtn("50% RISK", WPFBrushes.Orange);
            btnBreakeven = SolidBtn("BE + " + BreakevenOffsetTicks, WPFBrushes.DodgerBlue);
            btnDisarm = SolidBtn("DISARM", WPFBrushes.DarkGray);
            btnCloseHalf = SolidBtn("CLOSE 50%", WPFBrushes.Yellow); btnCloseHalf.Foreground = WPFBrushes.Black;
            AddDualRow(btnHalfRisk, btnBreakeven, 22); AddDualRow(btnDisarm, btnCloseHalf, 23);

            btnFlatten = SolidBtn("FLATTEN", WPFBrushes.Red); btnFlatten.Height = 30;
            AddRow(btnFlatten, 24);

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
            Double.TryParse(boxT1.Text, out manT1); Double.TryParse(boxT2.Text, out manT2); Double.TryParse(boxT3.Text, out manT3);
            Double.TryParse(txtEntryPrice.Text, out manEntryPx);
        }

        private void ProcessPlaybook(bool isLong, int idx)
        {
            double t1 = 0, t2 = 0, t3 = 0;
            isPlaybookActive = true;
            if (isLong)
            {
                if (idx == 1) { t1 = (L_B6 + L_B7) / 2; t2 = L_B7; t3 = L_B8; Qty1 = 1; Qty2 = 1; Qty3 = 2; } 
                else if (idx == 2) { t1 = (L_B4 + L_B5) / 2; t2 = L_B5; t3 = L_B6; Qty1 = 2; Qty2 = 1; Qty3 = 1; } 
                else if (idx == 3) { t1 = (L_B2 + L_B3) / 2; t2 = L_B3; t3 = L_B4; Qty1 = 2; Qty2 = 1; Qty3 = 1; } 
                else if (idx == 4) { t1 = (L_POC + L_B1) / 2; t2 = (L_B1 + L_B2) / 2; t3 = L_B2; Qty1 = 2; Qty2 = 1; Qty3 = 1; } 
                else if (idx == 5) { t1 = (L_R1 + L_POC) / 2; t2 = L_POC; t3 = 0; Qty1 = 2; Qty2 = 2; Qty3 = 0; } 
                else if (idx == 6) { t1 = (L_R1 + L_POC) / 2; t2 = L_POC; t3 = L_B1; Qty1 = 1; Qty2 = 1; Qty3 = 2; } 
                else if (idx == 7) { t1 = L_R1; t2 = L_POC; t3 = L_B2; Qty1 = 1; Qty2 = 1; Qty3 = 2; } 
                else if (idx == 8) { t1 = (L_R2 + L_R3) / 2; t2 = L_R3; t3 = L_R4; Qty1 = 2; Qty2 = 1; Qty3 = 1; } 
                else if (idx == 9) { t1 = L_R1; t2 = (L_R1 + L_POC) / 2; t3 = L_POC; Qty1 = 2; Qty2 = 1; Qty3 = 1; } 
                else if (idx == 10) { t1 = (L_R3 + L_R2) / 2; t2 = L_R2; t3 = 0; Qty1 = 2; Qty2 = 2; Qty3 = 0; } 
                else if (idx == 11) { t1 = L_R3; t2 = (L_R3 + L_R2) / 2; t3 = L_R2; Qty1 = 2; Qty2 = 1; Qty3 = 1; } 
                else if (idx == 12) { t1 = L_R2; t2 = L_POC; t3 = L_B4; Qty1 = 1; Qty2 = 1; Qty3 = 2; } 
                else if (idx == 13) { t1 = (L_R6 + L_R5) / 2; t2 = L_R5; t3 = L_R4; Qty1 = 1; Qty2 = 1; Qty3 = 2; } 
                else if (idx == 14) { t1 = L_R4; t2 = L_R2; t3 = L_POC; Qty1 = 1; Qty2 = 1; Qty3 = 2; } 
            }
            else
            {
                if (idx == 1) { t1 = (L_B6 + L_B5) / 2; t2 = L_B5; t3 = L_B4; Qty1 = 1; Qty2 = 1; Qty3 = 2; } 
                else if (idx == 2) { t1 = L_B2; t2 = L_POC; t3 = L_R4; Qty1 = 1; Qty2 = 1; Qty3 = 2; } 
                else if (idx == 3) { t1 = L_B3; t2 = (L_B3 + L_B2) / 2; t3 = L_B2; Qty1 = 2; Qty2 = 1; Qty3 = 1; } 
                else if (idx == 4) { t1 = (L_B3 + L_B2) / 2; t2 = L_B2; t3 = 0; Qty1 = 2; Qty2 = 2; Qty3 = 0; } 
                else if (idx == 5) { t1 = L_B1; t2 = (L_POC + L_B1) / 2; t3 = L_POC; Qty1 = 2; Qty2 = 1; Qty3 = 1; } 
                else if (idx == 6) { t1 = L_B1; t2 = L_POC; t3 = L_R2; Qty1 = 1; Qty2 = 1; Qty3 = 2; } 
                else if (idx == 7) { t1 = (L_POC + L_B1) / 2; t2 = L_POC; t3 = L_R1; Qty1 = 1; Qty2 = 1; Qty3 = 2; } 
                else if (idx == 8) { t1 = (L_POC + L_B1) / 2; t2 = L_POC; t3 = 0; Qty1 = 2; Qty2 = 2; Qty3 = 0; } 
                else if (idx == 9) { t1 = (L_R1 + L_POC) / 2; t2 = L_R1; t3 = L_R2; Qty1 = 2; Qty2 = 1; Qty3 = 1; } 
                else if (idx == 10) { t1 = (L_R2 + L_R3) / 2; t2 = L_R3; t3 = L_R4; Qty1 = 2; Qty2 = 1; Qty3 = 1; } 
                else if (idx == 11) { t1 = (L_R4 + L_R5) / 2; t2 = L_R5; t3 = L_R6; Qty1 = 2; Qty2 = 1; Qty3 = 1; } 
                else if (idx == 12) { t1 = L_B4; t2 = L_B2; t3 = L_POC; Qty1 = 1; Qty2 = 1; Qty3 = 2; } 
                else if (idx == 13) { t1 = (L_R6 + L_R7) / 2; t2 = L_R7; t3 = L_R8; Qty1 = 1; Qty2 = 1; Qty3 = 2; } 
            }
            boxT1.Text = t1.ToString("F2"); boxT2.Text = t2.ToString("F2"); boxT3.Text = t3.ToString("F2");
        }

        private void StartFlashTimer()
        {
            flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            flashTimer.Tick += (s, e) => { flashState = !flashState; if (isGoalReached) UpdateUI(); };
            flashTimer.Start();
        }

        private void UpdateUI()
        {
            if (lblStatus == null) return;
            ChartControl.Dispatcher.InvokeAsync(() => {
                dailyPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;
                lblStatus.Content = activeStatus;
                
                lblPnL.Content = dailyPnL.ToString("C");
                lblPnL.Foreground = dailyPnL >= 0 ? WPFBrushes.Lime : WPFBrushes.Red;

                if (isGoalReached) {
                    lblStatus.Background = flashState ? WPFBrushes.Gold : WPFBrushes.DimGray;
                    lblStatus.Foreground = flashState ? WPFBrushes.Black : WPFBrushes.White;
                } else {
                    lblStatus.Background = WPFBrushes.DimGray;
                    lblStatus.Foreground = WPFBrushes.White;
                }

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
        private void PopulateCombos() { ChartControl.Dispatcher.InvokeAsync(() => { if (boxT1.Items.Count > 0) return; foreach (string name in orderedLevelNames) { boxT1.Items.Add(name); boxT2.Items.Add(name); boxT3.Items.Add(name); } }); }

        // =========================================================
        //    HELPERS & RENDERING (LABELS & LINES)
        // =========================================================
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
            
            if (isNearTop) {
                shortT1 = (zoneHigh + zoneLow) / 2.0; shortT2 = zoneLow;
                hud_ShortPlan = string.Format("SHORT (Rejection):\n T1(50%) {0:N2}\n T2(Lvl) {1:N2}", shortT1, shortT2);
            } else {
                shortT1 = (zoneLow + levelBelow) / 2.0; shortT2 = levelBelow;
                hud_ShortPlan = string.Format("SHORT (Breakout):\n T1(50%) {0:N2}\n T2(Lvl) {1:N2}", shortT1, shortT2);
            }
            if (isNearBot) {
                longT1 = (zoneLow + zoneHigh) / 2.0; longT2 = zoneHigh;
                hud_LongPlan = string.Format("LONG (Bounce):\n T1(50%) {0:N2}\n T2(Lvl) {1:N2}", longT1, longT2);
            } else {
                longT1 = (zoneHigh + levelAbove) / 2.0; longT2 = levelAbove;
                hud_LongPlan = string.Format("LONG (Breakout):\n T1(50%) {0:N2}\n T2(Lvl) {1:N2}", longT1, longT2);
            }
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

        private void DrawCoreLines()
        {
            foreach (var kvp in levelMap)
            {
                if (kvp.Value <= 0) continue;
                if (kvp.Key.Contains("50") || kvp.Key.Contains("_"))
                    Draw.HorizontalLine(this, kvp.Key + "_Line", kvp.Value, WPFBrushes.Gray, DashStyleHelper.Dash, 1);
                else
                    Draw.HorizontalLine(this, kvp.Key + "_Line", kvp.Value, WPFBrushes.White, DashStyleHelper.Solid, 2);
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (chartControl == null || chartScale == null || ChartBars == null) return;
            base.OnRender(chartControl, chartScale);

            if (dxTextFormat == null) {
                dxTextFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Calibri", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 11.0f) { TextAlignment = SharpDX.DirectWrite.TextAlignment.Center, ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near };
            }
            if (dxBrushWhite == null) dxBrushWhite = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.White);
            SharpDX.Direct2D1.SolidColorBrush blueBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.DodgerBlue);

            float centerX = (float)(chartControl.CanvasRight - chartControl.CanvasLeft) / 2.0f + (float)chartControl.CanvasLeft;

            foreach (var kvp in levelMap)
            {
                string txt = kvp.Key;
                double price = kvp.Value;
                if (price > 0 && price >= chartScale.MinValue && price <= chartScale.MaxValue)
                {
                    float y = (float)chartScale.GetYByValue(price);
                    RenderTarget.DrawText(txt, dxTextFormat, new SharpDX.RectangleF(centerX - 150, y - 6, 300, 20), dxBrushWhite);
                }
            }

            float x = (float)chartControl.CanvasRight - 280; 
            float ty = 720f; 
            
            if (!levelsValid) { RenderTarget.DrawText("!!! LEVEL ERROR !!!", dxTextFormat, new SharpDX.RectangleF(x, ty, 300, 20), dxBrushWhite); return; }
            RenderTarget.DrawText("ZONE: " + zoneName, dxTextFormat, new SharpDX.RectangleF(x, ty, 300, 20), dxBrushWhite); ty += 18;
            foreach (string line in hud_LongPlan.Split('\n')) { RenderTarget.DrawText(line, dxTextFormat, new SharpDX.RectangleF(x, ty, 300, 20), blueBrush); ty += 18; } ty += 18;
            foreach (string line in hud_ShortPlan.Split('\n')) { RenderTarget.DrawText(line, dxTextFormat, new SharpDX.RectangleF(x, ty, 300, 20), blueBrush); ty += 18; }
            
            blueBrush.Dispose();
        }
        
        // PARAMETERS
        [NinjaScriptProperty, Display(Name = "Reset Levels", GroupName = "1. Core Levels", Order = 99)] public bool ResetLevels { get; set; }
        [NinjaScriptProperty, Display(Name = "Show Core Lines", GroupName = "1. Core Levels", Order = 100)] public bool ShowLines { get; set; }
        
        [NinjaScriptProperty, Display(Name = "Manual Stop Buffer", GroupName = "5. Manual")] public int ManualStopBufferTicks { get; set; }
        [NinjaScriptProperty, Display(Name = "Playbook Stop", GroupName = "5. Manual")] public int PlaybookStopTicks { get; set; }
        [NinjaScriptProperty, Display(Name = "Breakeven Offset", GroupName = "5. Manual")] public int BreakevenOffsetTicks { get; set; }
        
        // FAIL SAFE SETTINGS
        [NinjaScriptProperty, Display(Name = "Safe Chop Limit", GroupName = "4. Safety")] public double SafeChopLimit { get; set; }
        [NinjaScriptProperty, Display(Name = "Safe ADX Limit", GroupName = "4. Safety")] public double SafeAdxLimit { get; set; }
        
        [NinjaScriptProperty, Range(0, 100), Display(Name = "L1 Qty", GroupName = "2. Scalp")] public int Qty1 { get; set; }
        [NinjaScriptProperty, Display(Name = "L1 Target", GroupName = "2. Scalp")] public int Leg1TargetTicks { get; set; }
        [NinjaScriptProperty, Range(0, 100), Display(Name = "L2 Qty", GroupName = "2. Scalp")] public int Qty2 { get; set; }
        [NinjaScriptProperty, Display(Name = "L2 Target (ATR)", GroupName = "2. Scalp")] public double Leg2TargetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name = "L2 Trail", GroupName = "2. Scalp")] public TrailMode Leg2TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name = "L2 BarN", GroupName = "2. Scalp")] public int Leg2BarN { get; set; }
        [NinjaScriptProperty, Range(0, 100), Display(Name = "L3 Qty", GroupName = "2. Scalp")] public int Qty3 { get; set; }
        [NinjaScriptProperty, Display(Name = "L3 Target (ATR)", GroupName = "2. Scalp")] public double Leg3TargetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name = "L3 Trail", GroupName = "2. Scalp")] public TrailMode Leg3TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name = "L3 Ratchet", GroupName = "2. Scalp")] public double Leg3RatchetAtrMult { get; set; }
        
        [NinjaScriptProperty, Display(Name = "Initial Stop (ATR)", GroupName = "2. Scalp")] public double InitialStopAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name = "Initial Stop (Ticks)", GroupName = "2. Scalp")] public int InitialStopTicks { get; set; }
        
        [NinjaScriptProperty, Display(Name = "Context Minutes", GroupName = "3. Context")] public int ContextMinutes { get; set; }
        [NinjaScriptProperty, Display(Name = "Breakout Offset", GroupName = "3. Context")] public int BreakoutOffsetTicks { get; set; }
        [NinjaScriptProperty, Display(Name = "Smart Alloc Ticks", GroupName = "3. Context")] public int SmartAllocTicks { get; set; }
    }
}