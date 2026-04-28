#region Using
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media; 
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class MomEngine_ScalpDaysV3_4 : Strategy
    {
        private ADX adx1m, adx5m;
        private ChoppinessIndex chop1m, chop5m;
        private EMA ema50;
        private DM dm1m, dm5m;
        
        private Series<double> sessionVWAP;
        private double cumPV = 0, cumVol = 0;
        private double haOpen1m = 0, haClose1m = 0;
        private double activeMidpoint = 0;
        private string activeGateName = "NONE";
        private double dailyPnL = 0;
        private double pnlFloor = -9999;
        
        // --- UI UNLOCKER VARIABLES ---
        private double lastPocCheck = -1;
        private bool forceRedraw = true;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "V3.4: Thick Orange Gates + UI Unlocker (Drawing Tool Fix)";
                Name = "MomEngine_ScalpDaysV3_4";
                Calculate = Calculate.OnBarClose; 
                EntriesPerDirection = 1;
                IsExitOnSessionCloseStrategy = true;

                // RISK & VISUALS
                DailyGoal = 250; DailyLoss = 150;
                ShieldTrigger = 175; ShieldLock = 100;
                ShowVisuals = true;

                // TIMING
                StartTime = 094000; EndTime = 120000;
                InitialStopTicks = 30;

                // CORE LEVELS
                B6=0; B5=0; B4=0; B3=0; B2=0; B1=0; POC=0; R1=0; R2=0; R3=0; R4=0; R5=0; R6=0;
                HH=0; H3=0; HT2=0; H2=0; HT1=0; H1=0; TV_POC=0; M1=0; MT1=0; M2=0; MT2=0; M3=0; LL=0;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 5); 
            }
            else if (State == State.DataLoaded)
            {
                adx1m = ADX(14); chop1m = ChoppinessIndex(14); dm1m = DM(14);
                adx5m = ADX(BarsArray[1], 14); chop5m = ChoppinessIndex(BarsArray[1], 14); dm5m = DM(BarsArray[1], 14);
                ema50 = EMA(50);
                sessionVWAP = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 30 || BarsInProgress != 0 || POC <= 1.0) return;

            // 1. CALCULATIONS
            if (Bars.IsFirstBarOfSession) { cumPV = 0; cumVol = 0; dailyPnL = 0; pnlFloor = -9999; forceRedraw = true; }
            
            double typ = (High[0] + Low[0] + Close[0]) / 3.0;
            double vol = Math.Max(1.0, Volume[0]);
            cumPV += typ * vol; cumVol += vol;
            sessionVWAP[0] = (cumVol > 0 ? cumPV / cumVol : Close[0]);

            haClose1m = (Open[0] + High[0] + Low[0] + Close[0]) * 0.25;
            haOpen1m = CurrentBar == 0 ? Open[0] : (haOpen1m + haClose1m) * 0.5;
            
            dailyPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            if (dailyPnL >= ShieldTrigger && pnlFloor == -9999) pnlFloor = ShieldLock;
            if (pnlFloor != -9999 && dailyPnL <= pnlFloor) return; 

            UpdateTacticalGates();

            // --- UI UNLOCKER LOGIC ---
            // Only redraw visuals if the levels changed or it's the first bar of the session
            if (POC != lastPocCheck || forceRedraw)
            {
                RenderHighVisibilityVisuals();
                lastPocCheck = POC;
                forceRedraw = false;
            }

            // 2. ENTRY LOGIC
            int tNow = ToTime(Time[0]);
            if (Position.MarketPosition == MarketPosition.Flat && tNow >= StartTime && tNow <= EndTime && dailyPnL < DailyGoal && dailyPnL > -DailyLoss)
            {
                if (tNow >= 095500 && tNow <= 100400) return;

                bool chopOk = chop1m[0] < chop1m[1]; 
                bool adxOk = adx1m[0] > adx1m[1];    
                bool diOk = Math.Abs(dm1m.DiPlus[0] - dm1m.DiMinus[0]) > Math.Abs(dm1m.DiPlus[1] - dm1m.DiMinus[1]);
                bool zoneOk = Math.Abs(Close[0] - ema50[0]) > 6.25 && Math.Abs(Close[0] - sessionVWAP[0]) > 10.0;

                if (chopOk && adxOk && diOk && zoneOk)
                {
                    if (Close[0] > activeMidpoint) { EnterLong(4, "V3.4"); SetStopLoss("V3.4", CalculationMode.Ticks, InitialStopTicks, false); }
                    else if (Close[0] < activeMidpoint) { EnterShort(4, "V3.4"); SetStopLoss("V3.4", CalculationMode.Ticks, InitialStopTicks, false); }
                }
            }

            if (Position.MarketPosition != MarketPosition.Flat) ManageExits();
        }

        private void ManageExits()
        {
            double entry = Position.AveragePrice;
            if (Position.Quantity > 1)
            {
                if ((Position.MarketPosition == MarketPosition.Long && haOpen1m > haClose1m) || 
                    (Position.MarketPosition == MarketPosition.Short && haOpen1m < haClose1m))
                    ExitLong(Position.Quantity - 1, "StopX", "V3.4");
            }
            if (Position.Quantity >= 4)
            {
                bool targetHit = (Position.MarketPosition == MarketPosition.Long && Close[0] >= entry + (40 * TickSize)) || 
                                 (Position.MarketPosition == MarketPosition.Short && Close[0] <= entry - (40 * TickSize));
                if (targetHit)
                {
                    ExitLong(2, "Safety", "V3.4"); ExitShort(2, "Safety", "V3.4");
                    double bePlus = Position.MarketPosition == MarketPosition.Long ? entry + (4 * TickSize) : entry - (4 * TickSize);
                    SetStopLoss("V3.4", CalculationMode.Price, bePlus, false);
                }
            }
            if (Position.Quantity == 1 && BarsInProgress == 1) 
            {
                double gapNow5m = Math.Abs(dm5m.DiPlus[0] - dm5m.DiMinus[0]);
                double gapPrior5m = Math.Abs(dm5m.DiPlus[1] - dm5m.DiMinus[1]);
                if (chop5m[0] >= chop5m[1] || adx5m[0] <= adx5m[1] || gapNow5m < gapPrior5m)
                {
                    ExitLong(1, "Moon_Doom", "V3.4"); ExitShort(1, "Moon_Doom", "V3.4");
                }
            }
        }

        private void UpdateTacticalGates()
        {
            if (Close[0] > POC) {
                if (Close[0] <= B2) { activeMidpoint = POC + ((B2 - POC) * 0.5); activeGateName = "POC+"; }
                else if (Close[0] > B2 && Close[0] <= B4) { activeMidpoint = B2 + ((B4 - B2) * 0.5); activeGateName = "B2+"; }
                else { activeMidpoint = B4 + ((B6 - B4) * 0.5); activeGateName = "B4+"; }
            } else {
                if (Close[0] >= R2) { activeMidpoint = POC - ((POC - R2) * 0.5); activeGateName = "POC-"; }
                else if (Close[0] < R2 && Close[0] >= R4) { activeMidpoint = R2 - ((R2 - R4) * 0.5); activeGateName = "R2-"; }
                else { activeMidpoint = R4 - ((R4 - R6) * 0.5); activeGateName = "R4-"; }
            }
        }

        private void RenderHighVisibilityVisuals()
        {
            if (!ShowVisuals) { RemoveDrawObjects(); return; }

            // CORE LEVELS (White/Cyan/Red)
            DrawL("B6", B6, Brushes.Cyan); DrawL("B5", B5, Brushes.Cyan); DrawL("B4", B4, Brushes.Cyan);
            DrawL("B3", B3, Brushes.Cyan); DrawL("B2", B2, Brushes.Cyan); DrawL("B1", B1, Brushes.Cyan);
            DrawL("POC", POC, Brushes.White);
            DrawL("R1", R1, Brushes.Red); DrawL("R2", R2, Brushes.Red); DrawL("R3", R3, Brushes.Red);
            DrawL("R4", R4, Brushes.Red); DrawL("R5", R5, Brushes.Red); DrawL("R6", R6, Brushes.Red);

            // TV KEY LEVELS (Yellow/Magenta)
            DrawL("HH", HH, Brushes.Yellow); DrawL("H3", H3, Brushes.Yellow); DrawL("HT2", HT2, Brushes.Yellow);
            DrawL("H2", H2, Brushes.Yellow); DrawL("HT1", HT1, Brushes.Yellow); DrawL("H1", H1, Brushes.Yellow);
            DrawL("TV_POC", TV_POC, Brushes.White);
            DrawL("M1", M1, Brushes.Magenta); DrawL("MT1", MT1, Brushes.Magenta); DrawL("M2", M2, Brushes.Magenta);
            DrawL("MT2", MT2, Brushes.Magenta); DrawL("M3", M3, Brushes.Magenta); DrawL("LL", LL, Brushes.Magenta);

            // GATES (High Visibility Orange, Width 2)
            DrawGate("G_B65", B6, B5); DrawGate("G_B54", B5, B4); DrawGate("G_B43", B4, B3);
            DrawGate("G_B32", B3, B2); DrawGate("G_B21", B2, B1); DrawGate("G_B1P", B1, POC);
            DrawGate("G_PR1", POC, R1); DrawGate("G_R12", R1, R2); DrawGate("G_R23", R2, R3);
            DrawGate("G_R34", R3, R4); DrawGate("G_R45", R4, R5); DrawGate("G_R56", R5, R6);

            string status = string.Format("V3.4 TRI-LOCK ACTIVE\nGATE: {0}", activeGateName);
            Draw.TextFixed(this, "V3Dash", status, TextPosition.TopRight, Brushes.Yellow, new SimpleFont("Arial", 11), Brushes.Transparent, Brushes.Black, 80);
        }

        private void DrawL(string tag, double price, Brush color) {
            if (price <= 1.0) return;
            string t = "V3_" + tag;
            Draw.HorizontalLine(this, t + "ln", price, color, DashStyleHelper.Dash, 1);
            Draw.Text(this, t + "tx", tag, 0, price, color);
        }

        private void DrawGate(string tag, double p1, double p2) {
            if (p1 <= 1.0 || p2 <= 1.0) return;
            double mid = p1 + ((p2 - p1) * 0.5);
            // THICKER ORANGE DASH
            Draw.HorizontalLine(this, "V3_" + tag, mid, Brushes.Orange, DashStyleHelper.Dash, 2);
        }

        #region Properties
        [NinjaScriptProperty][Display(Name="Show Visuals", GroupName="0. Visuals", Order=0)] public bool ShowVisuals { get; set; }
        [NinjaScriptProperty][Display(Name="Daily Goal $", GroupName="1. Risk", Order=1)] public double DailyGoal { get; set; }
        [NinjaScriptProperty][Display(Name="Daily Loss $", GroupName="1. Risk", Order=2)] public double DailyLoss { get; set; }
        [NinjaScriptProperty][Display(Name="Shield Trigger $", GroupName="1. Risk", Order=3)] public double ShieldTrigger { get; set; }
        [NinjaScriptProperty][Display(Name="Shield Lock $", GroupName="1. Risk", Order=4)] public double ShieldLock { get; set; }

        [NinjaScriptProperty][Display(Name="B6", GroupName="2. Core Levels", Order=1)] public double B6 { get; set; }
        [NinjaScriptProperty][Display(Name="B5", GroupName="2. Core Levels", Order=2)] public double B5 { get; set; }
        [NinjaScriptProperty][Display(Name="B4", GroupName="2. Core Levels", Order=3)] public double B4 { get; set; }
        [NinjaScriptProperty][Display(Name="B3", GroupName="2. Core Levels", Order=4)] public double B3 { get; set; }
        [NinjaScriptProperty][Display(Name="B2", GroupName="2. Core Levels", Order=5)] public double B2 { get; set; }
        [NinjaScriptProperty][Display(Name="B1", GroupName="2. Core Levels", Order=6)] public double B1 { get; set; }
        [NinjaScriptProperty][Display(Name="POC", GroupName="2. Core Levels", Order=7)] public double POC { get; set; }
        [NinjaScriptProperty][Display(Name="R1", GroupName="2. Core Levels", Order=8)] public double R1 { get; set; }
        [NinjaScriptProperty][Display(Name="R2", GroupName="2. Core Levels", Order=9)] public double R2 { get; set; }
        [NinjaScriptProperty][Display(Name="R3", GroupName="2. Core Levels", Order=10)] public double R3 { get; set; }
        [NinjaScriptProperty][Display(Name="R4", GroupName="2. Core Levels", Order=11)] public double R4 { get; set; }
        [NinjaScriptProperty][Display(Name="R5", GroupName="2. Core Levels", Order=12)] public double R5 { get; set; }
        [NinjaScriptProperty][Display(Name="R6", GroupName="2. Core Levels", Order=13)] public double R6 { get; set; }

        [NinjaScriptProperty][Display(Name="HH", GroupName="3. TV Key Levels", Order=1)] public double HH { get; set; }
        [NinjaScriptProperty][Display(Name="H3", GroupName="3. TV Key Levels", Order=2)] public double H3 { get; set; }
        [NinjaScriptProperty][Display(Name="HT2", GroupName="3. TV Key Levels", Order=3)] public double HT2 { get; set; }
        [NinjaScriptProperty][Display(Name="H2", GroupName="3. TV Key Levels", Order=4)] public double H2 { get; set; }
        [NinjaScriptProperty][Display(Name="HT1", GroupName="3. TV Key Levels", Order=5)] public double HT1 { get; set; }
        [NinjaScriptProperty][Display(Name="H1", GroupName="3. TV Key Levels", Order=6)] public double H1 { get; set; }
        [NinjaScriptProperty][Display(Name="TV POC", GroupName="3. TV Key Levels", Order=7)] public double TV_POC { get; set; }
        [NinjaScriptProperty][Display(Name="M1", GroupName="3. TV Key Levels", Order=8)] public double M1 { get; set; }
        [NinjaScriptProperty][Display(Name="MT1", GroupName="3. TV Key Levels", Order=9)] public double MT1 { get; set; }
        [NinjaScriptProperty][Display(Name="M2", GroupName="3. TV Key Levels", Order=10)] public double M2 { get; set; }
        [NinjaScriptProperty][Display(Name="MT2", GroupName="3. TV Key Levels", Order=11)] public double MT2 { get; set; }
        [NinjaScriptProperty][Display(Name="M3", GroupName="3. TV Key Levels", Order=12)] public double M3 { get; set; }
        [NinjaScriptProperty][Display(Name="LL", GroupName="3. TV Key Levels", Order=13)] public double LL { get; set; }

        [NinjaScriptProperty][Display(Name="Start HHMMSS", GroupName="4. Timing", Order=1)] public int StartTime { get; set; }
        [NinjaScriptProperty][Display(Name="End HHMMSS", GroupName="4. Timing", Order=2)] public int EndTime { get; set; }
        [NinjaScriptProperty][Display(Name="Stop Ticks", GroupName="4. Timing", Order=3)] public int InitialStopTicks { get; set; }
        #endregion
    }
}
