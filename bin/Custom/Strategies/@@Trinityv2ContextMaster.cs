// CC BY-NC 4.0
// Strategy: Trinity_v2_13_Flexible
// Updates: 
// 1. Added 'UseVolumeFilter' toggle (Default: False).
// 2. Changed default ContextMinutes to 1 (Faster reaction).
// 3. Maintained chart agnostic logic (Works on Renko, Time, Heikin Ashi).

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls;
using WPFBrushes = System.Windows.Media.Brushes; 
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
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Trinity_v2_13_Flexible : Strategy
    {
        // =========================================================
        //    1. CORE LEVELS
        // =========================================================
        [NinjaScriptProperty, Display(Name="B8 (Ext High)", GroupName="1. Core Levels", Order=0)] public double L_B8 { get; set; }
        [NinjaScriptProperty, Display(Name="B7", GroupName="1. Core Levels", Order=1)] public double L_B7 { get; set; }
        [NinjaScriptProperty, Display(Name="B6", GroupName="1. Core Levels", Order=2)] public double L_B6 { get; set; }
        [NinjaScriptProperty, Display(Name="B5", GroupName="1. Core Levels", Order=3)] public double L_B5 { get; set; }
        [NinjaScriptProperty, Display(Name="B4", GroupName="1. Core Levels", Order=4)] public double L_B4 { get; set; }
        [NinjaScriptProperty, Display(Name="B3", GroupName="1. Core Levels", Order=5)] public double L_B3 { get; set; }
        [NinjaScriptProperty, Display(Name="B2", GroupName="1. Core Levels", Order=6)] public double L_B2 { get; set; }
        [NinjaScriptProperty, Display(Name="B1", GroupName="1. Core Levels", Order=7)] public double L_B1 { get; set; }
        [NinjaScriptProperty, Display(Name="POC (Median)", GroupName="1. Core Levels", Order=8)] public double L_POC { get; set; }
        [NinjaScriptProperty, Display(Name="R1", GroupName="1. Core Levels", Order=9)] public double L_R1 { get; set; }
        [NinjaScriptProperty, Display(Name="R2", GroupName="1. Core Levels", Order=10)] public double L_R2 { get; set; }
        [NinjaScriptProperty, Display(Name="R3", GroupName="1. Core Levels", Order=11)] public double L_R3 { get; set; }
        [NinjaScriptProperty, Display(Name="R4", GroupName="1. Core Levels", Order=12)] public double L_R4 { get; set; }
        [NinjaScriptProperty, Display(Name="R5", GroupName="1. Core Levels", Order=13)] public double L_R5 { get; set; }
        [NinjaScriptProperty, Display(Name="R6", GroupName="1. Core Levels", Order=14)] public double L_R6 { get; set; }
        [NinjaScriptProperty, Display(Name="R7", GroupName="1. Core Levels", Order=15)] public double L_R7 { get; set; }
        [NinjaScriptProperty, Display(Name="R8 (Ext Low)", GroupName="1. Core Levels", Order=16)] public double L_R8 { get; set; }

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

        // UI 
        private System.Windows.Controls.Button armScalpLongBtn, armScalpShortBtn;
        private System.Windows.Controls.Button armCoreLongBtn, armCoreShortBtn;
        private System.Windows.Controls.Button disarmBtn;
        private System.Windows.Controls.Grid chartGrid;
        
        private bool isArmedScalpLong = false, isArmedScalpShort = false;
        private bool isArmedCoreLong = false, isArmedCoreShort = false;
        
        private string zoneName = "WAITING";
        private double sessionStartCumProfit = 0, dailyPnL = 0;
        private string activeStatus = "STANDBY", gateStatus = "GATE CLOSED"; 
        private string allocStatus = ""; 
        private bool levelsValid = true; 

        private double zoneHigh = 0, zoneLow = 0, levelAbove = 0, levelBelow = 0;
        
        private double longT1, longT2, longGatePrice, longEntry;
        private double shortT1, shortT2, shortGatePrice, shortEntry;
        
        private string hud_LongPlan = "", hud_ShortPlan = "";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Trinity v2.13 [Flexible]";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 5; 
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsOverlay = true; 

                // Defaults
                InitialStopAtrMult = 0.75; InitialStopTicks = 35; 
                Qty1 = 2; Leg1TargetTicks = 40;        
                Qty2 = 1; Leg2TargetAtrMult = 0.88; Leg2TrailMode = TrailMode.BarNTrail; Leg2BarN = 2;
                Qty3 = 1; Leg3TargetAtrMult = 1.5; Leg3TrailMode = TrailMode.AtrRatchet; Leg3RatchetAtrMult = 1.5;     
                
                ContextMinutes = 1; // CHANGED: Default to 1m for faster reaction
                BreakoutOffsetTicks = 1; 
                SmartAllocTicks = 12; 
                
                UseVolumeFilter = false; // CHANGED: Default Off
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
                if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(() => CreateWPFControls());
            }
            else if (State == State.Terminated)
            {
                if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(() => DisposeWPFControls());
            }
        }

        protected override void OnBarUpdate()
        {
            try
            {
                if (CurrentBar < 20) return;
                
                // LEVEL GUARD
                ValidateLevels();
                if (!levelsValid) {
                    activeStatus = "ERROR: LEVEL MISMATCH";
                    gateStatus = "DISABLED";
                    Disarm(); 
                    if (State == State.Realtime) ChartControl.InvalidateVisual();
                    return; 
                }

                if (CurrentBar == 20 || (Bars.IsFirstBarOfSession && CurrentBar > 20)) DrawLevelsAndMids();
                
                if (Bars.IsFirstBarOfSession) {
                    sessionStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                    entryBar = -1; 
                    allocStatus = "";
                }
                dailyPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;

                IdentifyContext();
                CalculateTactics(); 
                
                if (CurrentBar > 1) {
                    longGatePrice = High[1] + (BreakoutOffsetTicks * TickSize);
                    shortGatePrice = Low[1] - (BreakoutOffsetTicks * TickSize);
                }

                if (State == State.Realtime) {
                    UpdateButtonColors(); 
                    ChartControl.InvalidateVisual(); 
                }
                
                // Trade Mgmt
                if (Position.MarketPosition != MarketPosition.Flat) {
                    if (entryBar < 0) entryBar = CurrentBar;
                    highSinceEntry = Math.Max(highSinceEntry, High[0]);
                    lowSinceEntry = Math.Min(lowSinceEntry, Low[0]);
                    UpdateTrailingStops();
                } else {
                    entryBar = -1; highSinceEntry = double.MinValue; lowSinceEntry = double.MaxValue;
                }

                if (BarsInProgress == 1) return; 

                // Triggers
                if (!isArmedScalpLong && !isArmedScalpShort && !isArmedCoreLong && !isArmedCoreShort) return;

                double sig = gu5_Fast.ConditionSeries[0]; 
                
                // VOLUME FILTER LOGIC
                bool volCondition = true;
                if (UseVolumeFilter) {
                    // Requires Volume > 20 Period SMA of Volume
                    if (Volume[0] <= volMa[0]) volCondition = false;
                }
                
                bool validLong = (sig == 1.0) || (sig == 0.5); 
                bool validShort = (sig == -1.0) || (sig == -0.5);
                
                // Combine Signal + Volume Check
                validLong = validLong && volCondition;
                validShort = validShort && volCondition;

                if (isArmedCoreLong && validLong) {
                    if (Close[0] >= longGatePrice) ExecuteCoreTrade(true); else gateStatus = "WAIT: CLOSE > " + longGatePrice; 
                }
                else if (isArmedCoreShort && validShort) {
                    if (Close[0] <= shortGatePrice) ExecuteCoreTrade(false); else gateStatus = "WAIT: CLOSE < " + shortGatePrice;
                }
                else if (isArmedCoreLong || isArmedCoreShort) gateStatus = "WAITING FOR SIGNAL";

                if (isArmedScalpLong && validLong) ExecuteScalpMoonRunner(true);
                else if (isArmedScalpShort && validShort) ExecuteScalpMoonRunner(false);
            }
            catch (Exception e) { Print("Trinity Error: " + e.Message); }
        }

        private void ValidateLevels()
        {
            bool v = true;
            if (L_B8 <= L_B7) v = false;
            if (L_B7 <= L_B6) v = false;
            if (L_B6 <= L_B5) v = false;
            if (L_B5 <= L_B4) v = false;
            if (L_B4 <= L_B3) v = false;
            if (L_B3 <= L_B2) v = false;
            if (L_B2 <= L_B1) v = false;
            if (L_B1 <= L_POC) v = false;
            if (L_POC <= L_R1) v = false;
            if (L_R1 <= L_R2) v = false;
            if (L_R2 <= L_R3) v = false;
            if (L_R3 <= L_R4) v = false;
            if (L_R4 <= L_R5) v = false;
            if (L_R5 <= L_R6) v = false;
            if (L_R6 <= L_R7) v = false;
            if (L_R7 <= L_R8) v = false;
            levelsValid = v;
        }

        private void ExecuteCoreTrade(bool isLong) {
            string signal = "Core_" + (isLong ? "L" : "S");
            double t1 = isLong ? longT1 : shortT1; 
            double t2 = isLong ? longT2 : shortT2;
            double entryPrice = Close[0];
            
            double distToT1 = Math.Abs(t1 - entryPrice);
            bool useSmart = distToT1 <= (SmartAllocTicks * TickSize);

            SetStopLoss(signal, CalculationMode.Ticks, InitialStopTicks, false); 
            
            if (useSmart) {
                allocStatus = "SMART ALLOC: SKIP T1"; Print(allocStatus);
                int totalQty = 4; 
                if (isLong) { EnterLong(totalQty, signal + "_Full"); SetProfitTarget(signal + "_Full", CalculationMode.Price, t2); } 
                else { EnterShort(totalQty, signal + "_Full"); SetProfitTarget(signal + "_Full", CalculationMode.Price, t2); }
            } else {
                allocStatus = "STANDARD ALLOC";
                if (isLong) { EnterLong(2, signal + "_Mid"); EnterLong(2, signal + "_Full"); SetProfitTarget(signal + "_Mid", CalculationMode.Price, t1); SetProfitTarget(signal + "_Full", CalculationMode.Price, t2); } 
                else { EnterShort(2, signal + "_Mid"); EnterShort(2, signal + "_Full"); SetProfitTarget(signal + "_Mid", CalculationMode.Price, t1); SetProfitTarget(signal + "_Full", CalculationMode.Price, t2); }
            }
            Disarm();
        }

        private void ExecuteScalpMoonRunner(bool isLong) {
            entryBar = CurrentBar; highSinceEntry = High[0]; lowSinceEntry = Low[0];
            double currentAtr = atrAlgo[0]; double entryPrice = Close[0];
            double coreT1 = isLong ? longT1 : shortT1;
            double distToCoreT1 = Math.Abs(coreT1 - entryPrice);
            bool useSmart = distToCoreT1 <= (SmartAllocTicks * TickSize);

            double t1Ticks = Leg1TargetTicks;
            double t2Ticks = Math.Max(5, Math.Round((currentAtr * Leg2TargetAtrMult) / TickSize));
            double t3Ticks = Math.Max(10, Math.Round((currentAtr * Leg3TargetAtrMult) / TickSize));
            double stopTicks = Math.Max(5, Math.Round((currentAtr * InitialStopAtrMult) / TickSize));
            string dir = isLong ? "L" : "S";

            if (useSmart) {
                allocStatus = "SMART SCALP: SPLIT T1"; Print(allocStatus);
                int smartQ2 = Qty2 + 1; int smartQ3 = Qty3 + 1;
                if (smartQ2 > 0) { string sig = "Scalp_" + dir + "2"; SetStopLoss(sig, CalculationMode.Ticks, stopTicks, false); SetProfitTarget(sig, CalculationMode.Ticks, t2Ticks); if(isLong) EnterLong(smartQ2, sig); else EnterShort(smartQ2, sig); }
                if (smartQ3 > 0) { string sig = "Scalp_" + dir + "3"; SetStopLoss(sig, CalculationMode.Ticks, stopTicks, false); SetProfitTarget(sig, CalculationMode.Ticks, t3Ticks); if(isLong) EnterLong(smartQ3, sig); else EnterShort(smartQ3, sig); }
            } else {
                allocStatus = "STANDARD SCALP";
                if (Qty1 > 0) { string sig = "Scalp_" + dir + "1"; SetStopLoss(sig, CalculationMode.Ticks, stopTicks, false); SetProfitTarget(sig, CalculationMode.Ticks, t1Ticks); if(isLong) EnterLong(Qty1, sig); else EnterShort(Qty1, sig); }
                if (Qty2 > 0) { string sig = "Scalp_" + dir + "2"; SetStopLoss(sig, CalculationMode.Ticks, stopTicks, false); SetProfitTarget(sig, CalculationMode.Ticks, t2Ticks); if(isLong) EnterLong(Qty2, sig); else EnterShort(Qty2, sig); }
                if (Qty3 > 0) { string sig = "Scalp_" + dir + "3"; SetStopLoss(sig, CalculationMode.Ticks, stopTicks, false); SetProfitTarget(sig, CalculationMode.Ticks, t3Ticks); if(isLong) EnterLong(Qty3, sig); else EnterShort(Qty3, sig); }
            }
            Disarm(); 
        }

        // =========================================================
        //    HELPERS
        // =========================================================
        private void UpdateTrailingStops() {
            if (Qty2 > 0) ApplyTrail(Qty2, "Scalp_L2", "Scalp_S2", Leg2TrailMode, Leg2BarN, 0);
            if (Qty3 > 0) ApplyTrail(Qty3, "Scalp_L3", "Scalp_S3", Leg3TrailMode, 0, Leg3RatchetAtrMult);
        }
        private void ApplyTrail(int qty, string longSig, string shortSig, TrailMode mode, int barN, double atrMult) {
            if (mode == TrailMode.None) return;
            bool isLong = Position.MarketPosition == MarketPosition.Long;
            string signal = isLong ? longSig : shortSig; double newStop = 0;
            if (mode == TrailMode.BarNTrail) { int idx = Math.Min(barN, CurrentBar); if (isLong) newStop = Low[idx]; else newStop = High[idx]; }
            else if (mode == TrailMode.AtrRatchet) { double rat = atrAlgo[0] * atrMult; if (isLong) newStop = highSinceEntry - rat; else newStop = lowSinceEntry + rat; }
            SetStopLoss(signal, CalculationMode.Price, newStop, false);
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (chartControl == null || chartScale == null || ChartBars == null) return;
            base.OnRender(chartControl, chartScale);

            SharpDX.Direct2D1.SolidColorBrush cyanBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.Cyan);
            SharpDX.Direct2D1.SolidColorBrush whiteBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.White);
            SharpDX.Direct2D1.SolidColorBrush redBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.OrangeRed);
            SharpDX.Direct2D1.SolidColorBrush greenBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.LimeGreen);
            SharpDX.Direct2D1.SolidColorBrush yellowBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.Yellow);

            SimpleFont wpfFont = new SimpleFont("Consolas", 11);
            SharpDX.DirectWrite.TextFormat textFormat = wpfFont.ToDirectWriteTextFormat();

            float x = (float)chartControl.CanvasRight - 280; float y = 50f; float lh = 18f; 
            try {
                if (!levelsValid) {
                      RenderTarget.DrawText("!!! LEVEL ERROR !!!", textFormat, new SharpDX.RectangleF(x, y, 300, 20), redBrush); y += lh;
                      RenderTarget.DrawText("CHECK INPUTS", textFormat, new SharpDX.RectangleF(x, y, 300, 20), redBrush);
                      return;
                }

                SharpDX.Direct2D1.Brush statusBrush = whiteBrush;
                if (activeStatus.Contains("SCALP") || activeStatus.Contains("CORE")) 
                    statusBrush = activeStatus.Contains("Short") ? redBrush : greenBrush;

                RenderTarget.DrawText("STATUS: " + activeStatus, textFormat, new SharpDX.RectangleF(x, y, 300, 20), statusBrush); y += lh;
                RenderTarget.DrawText("GATE:    " + gateStatus, textFormat, new SharpDX.RectangleF(x, y, 300, 20), whiteBrush); y += lh;
                
                if (!string.IsNullOrEmpty(allocStatus)) {
                    RenderTarget.DrawText("MODE:    " + allocStatus, textFormat, new SharpDX.RectangleF(x, y, 300, 20), yellowBrush); y += lh;
                }
                
                // Show Volume Filter Status
                string volTxt = UseVolumeFilter ? "ON" : "OFF";
                RenderTarget.DrawText("VOL FILT: " + volTxt, textFormat, new SharpDX.RectangleF(x, y, 300, 20), UseVolumeFilter ? greenBrush : whiteBrush); y += lh;

                RenderTarget.DrawText("PnL:     " + dailyPnL.ToString("C"), textFormat, new SharpDX.RectangleF(x, y, 300, 20), (dailyPnL >= 0 ? greenBrush : redBrush)); y += lh * 1.5f;
                RenderTarget.DrawText("ZONE:    " + zoneName, textFormat, new SharpDX.RectangleF(x, y, 300, 20), whiteBrush); y += lh * 1.5f;

                foreach (string line in hud_LongPlan.Split('\n')) { RenderTarget.DrawText(line, textFormat, new SharpDX.RectangleF(x, y, 300, 20), cyanBrush); y += lh; } y += lh; 
                foreach (string line in hud_ShortPlan.Split('\n')) { RenderTarget.DrawText(line, textFormat, new SharpDX.RectangleF(x, y, 300, 20), cyanBrush); y += lh; }
            }
            catch { }
            finally { if (textFormat != null) textFormat.Dispose(); if (cyanBrush != null) cyanBrush.Dispose(); if (whiteBrush != null) whiteBrush.Dispose(); if (redBrush != null) redBrush.Dispose(); if (greenBrush != null) greenBrush.Dispose(); if (yellowBrush != null) yellowBrush.Dispose(); }
        }

        private void IdentifyContext() {
            double p = Close[0];
            if (CheckZone(p, L_B8, L_B7, "B8 -> B7")) { levelAbove=L_B8+100; levelBelow=L_B6; }
            else if (CheckZone(p, L_B7, L_B6, "B7 -> B6")) { levelAbove=L_B8; levelBelow=L_B5; }
            else if (CheckZone(p, L_B6, L_B5, "B6 -> B5")) { levelAbove=L_B7; levelBelow=L_B4; }
            else if (CheckZone(p, L_B5, L_B4, "B5 -> B4")) { levelAbove=L_B6; levelBelow=L_B3; }
            else if (CheckZone(p, L_B4, L_B3, "B4 -> B3")) { levelAbove=L_B5; levelBelow=L_B2; }
            else if (CheckZone(p, L_B3, L_B2, "B3 -> B2")) { levelAbove=L_B4; levelBelow=L_B1; }
            else if (CheckZone(p, L_B2, L_B1, "B2 -> B1")) { levelAbove=L_B3; levelBelow=L_POC; }
            else if (CheckZone(p, L_B1, L_POC, "B1 -> POC")) { levelAbove=L_B2; levelBelow=L_R1; }
            else if (CheckZone(p, L_POC, L_R1, "POC -> R1")) { levelAbove=L_B1; levelBelow=L_R2; }
            else if (CheckZone(p, L_R1, L_R2, "R1 -> R2")) { levelAbove=L_POC; levelBelow=L_R3; }
            else if (CheckZone(p, L_R2, L_R3, "R2 -> R3")) { levelAbove=L_R1; levelBelow=L_R4; }
            else if (CheckZone(p, L_R3, L_R4, "R3 -> R4")) { levelAbove=L_R2; levelBelow=L_R5; }
            else if (CheckZone(p, L_R4, L_R5, "R4 -> R5")) { levelAbove=L_R3; levelBelow=L_R6; }
            else if (CheckZone(p, L_R5, L_R6, "R5 -> R6")) { levelAbove=L_R4; levelBelow=L_R7; }
            else if (CheckZone(p, L_R6, L_R7, "R6 -> R7")) { levelAbove=L_R5; levelBelow=L_R8; }
            else if (CheckZone(p, L_R7, L_R8, "R7 -> R8")) { levelAbove=L_R6; levelBelow=L_R8-100; }
            else if (L_R8 > 0 && p < L_R8) { SetZone("BSMT (Below R8)", L_R8 - 100, L_R8); levelAbove=L_R7; levelBelow=L_R8-200;}
            else if (L_B8 > 0 && p > L_B8) { SetZone("SKY (Above B8)", L_B8, L_B8 + 100); levelAbove=L_B8+200; levelBelow=L_B7;}
            else { zoneName = "WAITING"; }
        }
        private void CalculateTactics() {
            longEntry = zoneLow + TickSize; longT2 = zoneHigh; longT1 = (longEntry + longT2) / 2.0;
            shortEntry = zoneLow - TickSize; shortT2 = levelBelow; shortT1 = (shortEntry + shortT2) / 2.0;
            hud_LongPlan = string.Format("LONG PLAN:\n Break >  {0:N2}\n T1(50%)  {1:N2}\n T2(Lvl)  {2:N2}", longEntry, longT1, longT2);
            hud_ShortPlan = string.Format("SHORT PLAN:\n Break <  {0:N2}\n T1(50%)  {1:N2}\n T2(Lvl)  {2:N2}", shortEntry, shortT1, shortT2);
        }
        private bool CheckZone(double p, double top, double bot, string name) { if (top > 0 && bot > 0 && p <= top && p >= bot) { SetZone(name, top, bot); return true; } return false; }
        private void SetZone(string name, double high, double low) { zoneName = name; zoneHigh = high; zoneLow = low; }
        private void DrawLevelsAndMids() {
            DrawLevelPair(L_B8, L_B7, "B8", "B7"); DrawLevelPair(L_B7, L_B6, "B7", "B6");
            DrawLevelPair(L_B6, L_B5, "B6", "B5"); DrawLevelPair(L_B5, L_B4, "B5", "B4"); DrawLevelPair(L_B4, L_B3, "B4", "B3"); DrawLevelPair(L_B3, L_B2, "B3", "B2");
            DrawLevelPair(L_B2, L_B1, "B2", "B1"); DrawLevelPair(L_B1, L_POC, "B1", "POC"); DrawLevelPair(L_POC, L_R1, "POC", "R1"); DrawLevelPair(L_R1, L_R2, "R1", "R2");
            DrawLevelPair(L_R2, L_R3, "R2", "R3"); DrawLevelPair(L_R3, L_R4, "R3", "R4"); DrawLevelPair(L_R4, L_R5, "R4", "R5"); DrawLevelPair(L_R5, L_R6, "R5", "R6");
            DrawLevelPair(L_R6, L_R7, "R6", "R7"); DrawLevelPair(L_R7, L_R8, "R7", "R8");
        }
        private void DrawLevelPair(double top, double bot, string nameTop, string nameBot) {
            if (top == 0 || bot == 0) return;
            Draw.HorizontalLine(this, nameTop, top, WPFBrushes.Gray); Draw.HorizontalLine(this, nameBot, bot, WPFBrushes.Gray);
            double mid = (top + bot) / 2; Draw.HorizontalLine(this, "Mid_" + nameTop + "_" + nameBot, mid, WPFBrushes.DimGray, DashStyleHelper.Dash, 1);
        }
        private void CreateWPFControls() {
            chartGrid = ChartControl.Parent as System.Windows.Controls.Grid; if (chartGrid == null) return;
            armScalpLongBtn = Btn("SCALP L", WPFBrushes.DimGray, 10, 40); armScalpShortBtn = Btn("SCALP S", WPFBrushes.DimGray, 105, 40);
            armCoreLongBtn = Btn("CORE L", WPFBrushes.DimGray, 10, 75); armCoreShortBtn = Btn("CORE S", WPFBrushes.DimGray, 105, 75);
            disarmBtn = Btn("DISARM", WPFBrushes.DarkRed, 200, 40); disarmBtn.Height = 65; disarmBtn.Width = 70;
            armScalpLongBtn.Click += (s, e) => { if (levelsValid) { Disarm(); isArmedScalpLong = true; activeStatus = "ARMED: SCALP L"; gateStatus = "MONITORING (Gu5)"; } };
            armScalpShortBtn.Click += (s, e) => { if (levelsValid) { Disarm(); isArmedScalpShort = true; activeStatus = "ARMED: SCALP S"; gateStatus = "MONITORING (Gu5)"; } };
            armCoreLongBtn.Click += (s, e) => { if (levelsValid) { Disarm(); isArmedCoreLong = true; activeStatus = "ARMED: CORE L"; gateStatus = "CHECKING GATE"; } };
            armCoreShortBtn.Click += (s, e) => { if (levelsValid) { Disarm(); isArmedCoreShort = true; activeStatus = "ARMED: CORE S"; gateStatus = "CHECKING GATE"; } };
            disarmBtn.Click += (s, e) => { Disarm(); activeStatus = "STANDBY"; gateStatus = "-"; };
            chartGrid.Children.Add(armScalpLongBtn); chartGrid.Children.Add(armScalpShortBtn); chartGrid.Children.Add(armCoreLongBtn); chartGrid.Children.Add(armCoreShortBtn); chartGrid.Children.Add(disarmBtn);
        }
        private void UpdateButtonColors() { if (armScalpLongBtn != null) { ChartControl.Dispatcher.InvokeAsync(() => {
            armScalpLongBtn.Background = isArmedScalpLong ? WPFBrushes.LimeGreen : WPFBrushes.DimGray;
            armCoreLongBtn.Background = isArmedCoreLong ? WPFBrushes.LimeGreen : WPFBrushes.DimGray;
            armScalpShortBtn.Background = isArmedScalpShort ? WPFBrushes.Red : WPFBrushes.DimGray;
            armCoreShortBtn.Background = isArmedCoreShort ? WPFBrushes.Red : WPFBrushes.DimGray; }); } }
        private System.Windows.Controls.Button Btn(string txt, System.Windows.Media.Brush bg, double x, double y) {
            return new System.Windows.Controls.Button { Content = txt, Background = bg, Foreground = WPFBrushes.White, Margin = new Thickness(x, y, 0, 0), Width=90, Height=30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, FontSize=11, FontWeight=FontWeights.Bold };
        }
        private void DisposeWPFControls() { if (chartGrid != null) { chartGrid.Children.Remove(armScalpLongBtn); chartGrid.Children.Remove(armScalpShortBtn); chartGrid.Children.Remove(armCoreLongBtn); chartGrid.Children.Remove(armCoreShortBtn); chartGrid.Children.Remove(disarmBtn); } }
        private void Disarm() { isArmedScalpLong = false; isArmedScalpShort = false; isArmedCoreLong = false; isArmedCoreShort = false; allocStatus = ""; }

        #region Properties
        // Scalp Properties
        [NinjaScriptProperty, Range(0, 100), Display(Name="L1 Qty (Bank)", GroupName="2. Scalp Tactics", Order=0)] public int Qty1 { get; set; }
        [NinjaScriptProperty, Display(Name="L1 Target (Ticks)", GroupName="2. Scalp Tactics", Order=1)] public int Leg1TargetTicks { get; set; }
        [NinjaScriptProperty, Range(0, 100), Display(Name="L2 Qty (Core)", GroupName="2. Scalp Tactics", Order=2)] public int Qty2 { get; set; }
        [NinjaScriptProperty, Display(Name="L2 Target (ATR Mult)", GroupName="2. Scalp Tactics", Order=3)] public double Leg2TargetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name="L2 Trail Mode", GroupName="2. Scalp Tactics", Order=4)] public TrailMode Leg2TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name="L2 Bar N", GroupName="2. Scalp Tactics", Order=5)] public int Leg2BarN { get; set; }
        [NinjaScriptProperty, Range(0, 100), Display(Name="L3 Qty (Runner)", GroupName="2. Scalp Tactics", Order=6)] public int Qty3 { get; set; }
        [NinjaScriptProperty, Display(Name="L3 Target (ATR Mult)", GroupName="2. Scalp Tactics", Order=7)] public double Leg3TargetAtrMult { get; set; }
        [NinjaScriptProperty, Display(Name="L3 Trail Mode", GroupName="2. Scalp Tactics", Order=8)] public TrailMode Leg3TrailMode { get; set; }
        [NinjaScriptProperty, Display(Name="L3 Ratchet (ATR Mult)", GroupName="2. Scalp Tactics", Order=9)] public double Leg3RatchetAtrMult { get; set; }
        
        [NinjaScriptProperty][Display(Name="Initial Stop (ATR Mult)", GroupName="2. Scalp Tactics", Order=10)] public double InitialStopAtrMult { get; set; }
        [NinjaScriptProperty][Display(Name="Initial Stop (Ticks)", GroupName="2. Scalp Tactics", Order=11)] public int InitialStopTicks { get; set; }

        // V2 Properties
        [NinjaScriptProperty][Display(Name="Context Minutes", GroupName="3. Context")] public int ContextMinutes { get; set; }
        [NinjaScriptProperty][Display(Name="Breakout Offset (Ticks)", GroupName="3. Context")] public int BreakoutOffsetTicks { get; set; }
        [NinjaScriptProperty][Display(Name="Smart Alloc (Ticks)", GroupName="3. Context", Description="If price is within X ticks of T1, skip T1.")] public int SmartAllocTicks { get; set; }
        
        // Volume Filter
        [NinjaScriptProperty, Display(Name="Use Volume Filter", GroupName="4. Filters", Description="If true, requires Volume > 20 SMA Volume to enter.")]
        public bool UseVolumeFilter { get; set; }
        #endregion
    }
}