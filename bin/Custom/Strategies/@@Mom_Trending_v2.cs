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
    public class Mom_Trending_v2 : Strategy
    {
        private EMA ema50;
        private Series<double> sessionVWAP;
        private double cumPV = 0, cumVol = 0;
        private double haOpen1m = 0, haClose1m = 0;
        private double activeMidpoint = 0;
        private string activeGateName = "NONE";
        
        // PnL & Risk Variables
        private double dailyPnL = 0;
        private double sessionStartCumProfit = 0; // FIX: To track daily isolation
        private double pnlFloor = -9999;
        
        // UI Variables
        private double lastPocCheck = -1;
        private bool forceRedraw = true;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "V2 REVISED: Shield Fixed + Core Level Entries";
                Name = "Mom_Trending_v2";
                Calculate = Calculate.OnBarClose; 
                EntriesPerDirection = 1;
                IsExitOnSessionCloseStrategy = true;

                // 1. RISK & VISUALS
                DailyGoal = 250; DailyLoss = 150;
                ShieldTrigger = 175; ShieldLock = 100;
                ShowVisuals = true;

                // 2. TIMING
                StartTime = 094000; EndTime = 120000;
                InitialStopTicks = 30;

                // 3. CORE LEVELS (Reordered)
                B6=0; B5=0; B4=0; B3=0; B2=0; B1=0; POC=0; R1=0; R2=0; R3=0; R4=0; R5=0; R6=0;

                // 4. TV KEY LEVELS
                HH=0; H3=0; HT2=0; H2=0; HT1=0; H1=0; TV_POC=0; M1=0; MT1=0; M2=0; MT2=0; M3=0; LL=0;
            }
            else if (State == State.DataLoaded)
            {
                ema50 = EMA(50);
                sessionVWAP = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 30 || POC <= 1.0) return;

            // 1. SESSION CALCULATION & PNL RESET FIX
            if (Bars.IsFirstBarOfSession) 
            { 
                cumPV = 0; cumVol = 0; 
                pnlFloor = -9999; 
                forceRedraw = true;
                
                // CAPTURE STARTING PNL TO ISOLATE TODAY'S PERFORMANCE
                sessionStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            }
            
            // CALCULATE REALIZED PNL FOR *THIS* SESSION ONLY
            dailyPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;

            // VWAP Logic
            double typ = (High[0] + Low[0] + Close[0]) / 3.0;
            double vol = Math.Max(1.0, Volume[0]);
            cumPV += typ * vol; cumVol += vol;
            sessionVWAP[0] = (cumVol > 0 ? cumPV / cumVol : Close[0]);

            // HA Logic
            haClose1m = (Open[0] + High[0] + Low[0] + Close[0]) * 0.25;
            haOpen1m = CurrentBar == 0 ? Open[0] : (haOpen1m + haClose1m) * 0.5;
            
            // 2. RISK SHIELD LOGIC
            if (dailyPnL >= ShieldTrigger && pnlFloor == -9999) pnlFloor = ShieldLock;
            if (pnlFloor != -9999 && dailyPnL <= pnlFloor) return; // Hard Stop if Shield Broken

            UpdateTacticalGates();

            // 3. UI UNLOCKER
            if (POC != lastPocCheck || forceRedraw) {
                RenderHighVisibilityVisuals();
                lastPocCheck = POC; forceRedraw = false;
            }

            // 4. V2 ENTRY LOGIC (Gate Cross Only)
            int tNow = ToTime(Time[0]);
            if (Position.MarketPosition == MarketPosition.Flat && tNow >= StartTime && tNow <= EndTime && dailyPnL < DailyGoal && dailyPnL > -DailyLoss)
            {
                if (tNow >= 095500 && tNow <= 100400) return;
                if (activeMidpoint <= 1.0) return; // Gate Guard

                // Trend Filter (EMA50) + Gate Cross
                if (Close[0] > activeMidpoint && Close[0] > ema50[0]) {
                    EnterLong(4, "V2_Core"); SetStopLoss("V2_Core", CalculationMode.Ticks, InitialStopTicks, false);
                }
                else if (Close[0] < activeMidpoint && Close[0] < ema50[0]) {
                    EnterShort(4, "V2_Core"); SetStopLoss("V2_Core", CalculationMode.Ticks, InitialStopTicks, false);
                }
            }

            if (Position.MarketPosition != MarketPosition.Flat) ManageV2Exits();
        }

        private void ManageV2Exits()
        {
            double entry = Position.AveragePrice;
            
            // 1. TACTICAL STOP (1m HA Color Flip)
            if (Position.Quantity > 1)
            {
                if ((Position.MarketPosition == MarketPosition.Long && haOpen1m > haClose1m) || 
                    (Position.MarketPosition == MarketPosition.Short && haOpen1m < haClose1m))
                {
                    ExitLong(Position.Quantity - 1, "StopX", "V2_Core"); 
                    ExitShort(Position.Quantity - 1, "StopX", "V2_Core");
                }
            }

            // 2. SAFETY (BE+4 at 40 Ticks)
            if (Position.Quantity >= 4) {
                bool hit = (Position.MarketPosition == MarketPosition.Long && Close[0] >= entry + (40 * TickSize)) ||
                           (Position.MarketPosition == MarketPosition.Short && Close[0] <= entry - (40 * TickSize));
                if (hit) {
                    ExitLong(2, "Safety", "V2_Core"); ExitShort(2, "Safety", "V2_Core");
                    double slPrice = Position.MarketPosition == MarketPosition.Long ? entry + (4 * TickSize) : entry - (4 * TickSize);
                    SetStopLoss("V2_Core", CalculationMode.Price, slPrice, false);
                }
            }
        }

        private void UpdateTacticalGates()
        {
            if (Close[0] > POC) {
                if (B2 > 1.0 && Close[0] <= B2) { activeMidpoint = POC + ((B2 - POC) * 0.5); activeGateName = "POC+"; }
                else if (B2 > 1.0 && B4 > 1.0 && Close[0] > B2 && Close[0] <= B4) { activeMidpoint = B2 + ((B4 - B2) * 0.5); activeGateName = "B2+"; }
                else if (B4 > 1.0 && B6 > 1.0 && Close[0] > B4) { activeMidpoint = B4 + ((B6 - B4) * 0.5); activeGateName = "B4+"; }
            } else {
                if (R2 > 1.0 && Close[0] >= R2) { activeMidpoint = POC - ((POC - R2) * 0.5); activeGateName = "POC-"; }
                else if (R2 > 1.0 && R4 > 1.0 && Close[0] < R2 && Close[0] >= R4) { activeMidpoint = R2 - ((R2 - R4) * 0.5); activeGateName = "R2-"; }
                else if (R4 > 1.0 && R6 > 1.0 && Close[0] < R4) { activeMidpoint = R4 - ((R4 - R6) * 0.5); activeGateName = "R4-"; }
            }
        }

        private void RenderHighVisibilityVisuals()
        {
            if (!ShowVisuals) { RemoveDrawObjects(); return; }

            DrawL("B6", B6, Brushes.Cyan); DrawL("B4", B4, Brushes.Cyan); DrawL("B2", B2, Brushes.Cyan);
            DrawL("POC", POC, Brushes.White);
            DrawL("R2", R2, Brushes.Red); DrawL("R4", R4, Brushes.Red); DrawL("R6", R6, Brushes.Red);

            DrawL("HH", HH, Brushes.Yellow); DrawL("H3", H3, Brushes.Yellow); DrawL("HT2", HT2, Brushes.Yellow);
            DrawL("H2", H2, Brushes.Yellow); DrawL("HT1", HT1, Brushes.Yellow); DrawL("H1", H1, Brushes.Yellow);
            DrawL("TV_POC", TV_POC, Brushes.White);
            DrawL("M1", M1, Brushes.Magenta); DrawL("MT1", MT1, Brushes.Magenta); DrawL("M2", M2, Brushes.Magenta);
            DrawL("MT2", MT2, Brushes.Magenta); DrawL("M3", M3, Brushes.Magenta); DrawL("LL", LL, Brushes.Magenta);
            
            // Orange Gates
            DrawGate("G1", B6, B4); DrawGate("G2", B4, B2); DrawGate("G3", B2, POC);
            DrawGate("G4", POC, R2); DrawGate("G5", R2, R4); DrawGate("G6", R4, R6);

            string status = string.Format("V2 REVISED | PnL: {0:C0}\nGATE: {1}", dailyPnL, activeGateName);
            Draw.TextFixed(this, "V2Dash", status, TextPosition.TopRight, Brushes.Yellow, new SimpleFont("Arial", 11), Brushes.Transparent, Brushes.Black, 80);
        }

        private void DrawL(string tag, double price, Brush color) {
            if (price <= 1.0) return;
            string t = "V2_" + tag;
            Draw.HorizontalLine(this, t + "ln", price, color, DashStyleHelper.Dash, 1);
            Draw.Text(this, t + "tx", tag, 0, price, color);
        }

        private void DrawGate(string tag, double p1, double p2) {
            if (p1 <= 1.0 || p2 <= 1.0) return;
            Draw.HorizontalLine(this, "V2_" + tag, p1 + ((p2 - p1) * 0.5), Brushes.Orange, DashStyleHelper.Dash, 2);
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
