#region Using
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class MomentumEngine_V4_Pro : Strategy
    {
        // 1. INDICATORS (The Engine)
        private ADX adx;
        private ChoppinessIndex chop;
        private DM dm;
        private EMA ema50;
        private Series<double> sessionVWAP;

        // 2. UI CONTROLS
        private Button armLongBtn, armShortBtn, cancelBtn;
        private Grid chartGrid;
        
        // 3. STATE MANAGEMENT
        private bool isArmedLong = false;
        private bool isArmedShort = false;
        private string activeStatus = "STANDBY";
        
        // 4. RISK & PNL ISOLATION
        private double sessionStartCumProfit = 0;
        private double dailyPnL = 0;
        private double pnlFloor = -9999;

        // 5. SMART TARGETS
        private double calculatedT1 = 0;
        private double calculatedT2 = 0;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "V4 PRO: Full Level Assistant + Smart Targeting + Risk Shield";
                Name = "MomentumEngine_V4_Pro";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                IsExitOnSessionCloseStrategy = true;

                // RISK DEFAULTS
                DailyGoal = 250; DailyLoss = 150;
                ShieldTrigger = 175; ShieldLock = 100;
                InitialStopTicks = 30;

                // TIMING
                StartTime = 094000; EndTime = 120000;

                // INITIALIZE ALL LEVELS (Default 0)
                B6=0; B5=0; B4=0; B3=0; B2=0; B1=0; POC=0; R1=0; R2=0; R3=0; R4=0; R5=0; R6=0;
                HH=0; H3=0; HT2=0; H2=0; HT1=0; H1=0; TV_POC=0; M1=0; MT1=0; M2=0; MT2=0; M3=0; LL=0;
            }
            else if (State == State.Configure)
            {
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                AddDataSeries(BarsPeriodType.Minute, 5); // 5m Data for context
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(14); chop = ChoppinessIndex(14); dm = DM(14); ema50 = EMA(50);
                sessionVWAP = new Series<double>(this);
            }
            else if (State == State.Historical)
            {
                if (ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync(() => CreateWPFControls());
            }
            else if (State == State.Terminated)
            {
                if (ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync(() => DisposeWPFControls());
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 20 || POC <= 1.0) return;

            // 1. SESSION & RISK LOGIC
            if (Bars.IsFirstBarOfSession) {
                sessionStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                pnlFloor = -9999;
            }
            dailyPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;

            // Shield Logic
            if (dailyPnL >= ShieldTrigger && pnlFloor == -9999) pnlFloor = ShieldLock;
            if (pnlFloor != -9999 && dailyPnL <= pnlFloor) {
                activeStatus = "SHIELD LOCKED";
                isArmedLong = false; isArmedShort = false;
                if (State == State.Realtime) UpdateButtonVisuals();
                return;
            }

            // 2. INDICATOR UPDATE
            double typ = (High[0] + Low[0] + Close[0]) / 3.0;
            double vol = Math.Max(1.0, Volume[0]);
            // Simple VWAP calc for visual reference
            if (Bars.IsFirstBarOfSession) sessionVWAP[0] = typ;
            else sessionVWAP[0] = (sessionVWAP[1] * (CurrentBar) + typ) / (CurrentBar + 1);

            // 3. UI UPDATE
            if (State == State.Realtime) UpdateButtonVisuals();

            // 4. ENTRY LOGIC (The Assistant)
            if (Position.MarketPosition == MarketPosition.Flat && (isArmedLong || isArmedShort))
            {
                // "Goosebump" Trigger: Chop Drop + Momentum Rise
                bool chopTrigger = chop[0] < chop[1] && chop[1] > 50; 
                bool adxTrigger = adx[0] > adx[1];
                bool gateCross = false;

                if (isArmedLong) {
                    // Smart Target Calculation
                    CalculateSmartTargets(true);
                    // Gate check: Price must be above a relevant midpoint or level
                    gateCross = Close[0] > (High[1] + Low[1]) / 2.0; 
                    
                    if (chopTrigger && adxTrigger && gateCross) {
                        EnterLong(4, "V4_Pro_Long");
                        SetStopLoss("V4_Pro_Long", CalculationMode.Ticks, InitialStopTicks, false);
                        SetProfitTarget("V4_Pro_Long", CalculationMode.Price, calculatedT2); // Main Target
                        isArmedLong = false; // Disarm
                    }
                }
                else if (isArmedShort) {
                    CalculateSmartTargets(false);
                    gateCross = Close[0] < (High[1] + Low[1]) / 2.0;

                    if (chopTrigger && adxTrigger && gateCross) {
                        EnterShort(4, "V4_Pro_Short");
                        SetStopLoss("V4_Pro_Short", CalculationMode.Ticks, InitialStopTicks, false);
                        SetProfitTarget("V4_Pro_Short", CalculationMode.Price, calculatedT2);
                        isArmedShort = false; // Disarm
                    }
                }
            }

            // 5. TRADE MANAGEMENT
            if (Position.MarketPosition != MarketPosition.Flat) ManageProExits();
        }

        private void CalculateSmartTargets(bool isLong)
        {
            double p = Close[0];
            // Simple Logic: Find the next level up/down
            if (isLong) {
                if (p < POC) { calculatedT1 = POC; calculatedT2 = R1; } // Recovering to POC
                else if (p < B1) { calculatedT1 = B1; calculatedT2 = B2; }
                else if (p < B2) { calculatedT1 = B2; calculatedT2 = B3; }
                else { calculatedT1 = B4; calculatedT2 = B5; }
            } else {
                if (p > POC) { calculatedT1 = POC; calculatedT2 = B1; } // Recovering to POC
                else if (p > R1) { calculatedT1 = R1; calculatedT2 = R2; }
                else if (p > R2) { calculatedT1 = R2; calculatedT2 = R3; }
                else { calculatedT1 = R4; calculatedT2 = R5; }
            }
        }

        private void ManageProExits()
        {
            double entry = Position.AveragePrice;
            
            // Scale out 2 contracts at +40 ticks or T1
            if (Position.Quantity >= 4) {
                bool hitT1 = (Position.MarketPosition == MarketPosition.Long && Close[0] >= calculatedT1) ||
                             (Position.MarketPosition == MarketPosition.Short && Close[0] <= calculatedT1);
                bool hitFixed = (Position.MarketPosition == MarketPosition.Long && Close[0] >= entry + (40 * TickSize)) ||
                                (Position.MarketPosition == MarketPosition.Short && Close[0] <= entry - (40 * TickSize));
                
                if (hitT1 || hitFixed) {
                    ExitLong(2, "Bank_Half", "V4_Pro_Long"); 
                    ExitShort(2, "Bank_Half", "V4_Pro_Short");
                    // Move Stop to BE+4
                    double sl = Position.MarketPosition == MarketPosition.Long ? entry + (4 * TickSize) : entry - (4 * TickSize);
                    SetStopLoss("V4_Pro_Long", CalculationMode.Price, sl, false);
                    SetStopLoss("V4_Pro_Short", CalculationMode.Price, sl, false);
                }
            }
        }

        // --- WPF CONTROLS ---
        private void CreateWPFControls()
        {
            chartGrid = ChartControl.Parent as Grid;
            if (chartGrid == null) return;

            armLongBtn = new Button { Content = "ARM LONG", Background = Brushes.DimGray, Foreground = Brushes.White, Margin = new Thickness(10, 40, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Width = 90, Height=30, FontWeight = FontWeights.Bold };
            armShortBtn = new Button { Content = "ARM SHORT", Background = Brushes.DimGray, Foreground = Brushes.White, Margin = new Thickness(110, 40, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Width = 90, Height=30, FontWeight = FontWeights.Bold };
            cancelBtn = new Button { Content = "DISARM", Background = Brushes.DarkRed, Foreground = Brushes.White, Margin = new Thickness(210, 40, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Width = 80, Height=30 };

            armLongBtn.Click += (s, e) => { isArmedLong = true; isArmedShort = false; activeStatus = "HUNTING LONG"; };
            armShortBtn.Click += (s, e) => { isArmedShort = true; isArmedLong = false; activeStatus = "HUNTING SHORT"; };
            cancelBtn.Click += (s, e) => { isArmedLong = false; isArmedShort = false; activeStatus = "STANDBY"; };

            chartGrid.Children.Add(armLongBtn);
            chartGrid.Children.Add(armShortBtn);
            chartGrid.Children.Add(cancelBtn);
        }

        private void DisposeWPFControls()
        {
            if (chartGrid != null) {
                chartGrid.Children.Remove(armLongBtn);
                chartGrid.Children.Remove(armShortBtn);
                chartGrid.Children.Remove(cancelBtn);
            }
        }

        private void UpdateButtonVisuals()
        {
            if (armLongBtn == null) return;
            ChartControl.Dispatcher.InvokeAsync(() => {
                armLongBtn.Background = isArmedLong ? Brushes.LimeGreen : Brushes.DimGray;
                armShortBtn.Background = isArmedShort ? Brushes.Red : Brushes.DimGray;
                
                string info = string.Format("V4 PRO | PnL: {0:C0}\nSTATUS: {1}\nNEXT TGT: {2}", dailyPnL, activeStatus, calculatedT1);
                Draw.TextFixed(this, "Dash", info, TextPosition.TopRight, Brushes.Cyan, new SimpleFont("Arial", 12), Brushes.Transparent, Brushes.Black, 100);
            });
        }

        #region Properties
        [NinjaScriptProperty][Display(Name="Daily Goal", GroupName="1. Risk")] public double DailyGoal { get; set; }
        [NinjaScriptProperty][Display(Name="Daily Loss", GroupName="1. Risk")] public double DailyLoss { get; set; }
        [NinjaScriptProperty][Display(Name="Shield Trigger", GroupName="1. Risk")] public double ShieldTrigger { get; set; }
        [NinjaScriptProperty][Display(Name="Shield Lock", GroupName="1. Risk")] public double ShieldLock { get; set; }
        [NinjaScriptProperty][Display(Name="Stop Ticks", GroupName="1. Risk")] public int InitialStopTicks { get; set; }

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
        
        [NinjaScriptProperty][Display(Name="HH", GroupName="3. Key Levels", Order=1)] public double HH { get; set; }
        [NinjaScriptProperty][Display(Name="H3", GroupName="3. Key Levels", Order=2)] public double H3 { get; set; }
        [NinjaScriptProperty][Display(Name="HT2", GroupName="3. Key Levels", Order=3)] public double HT2 { get; set; }
        [NinjaScriptProperty][Display(Name="H2", GroupName="3. Key Levels", Order=4)] public double H2 { get; set; }
        [NinjaScriptProperty][Display(Name="HT1", GroupName="3. Key Levels", Order=5)] public double HT1 { get; set; }
        [NinjaScriptProperty][Display(Name="H1", GroupName="3. Key Levels", Order=6)] public double H1 { get; set; }
        [NinjaScriptProperty][Display(Name="TV_POC", GroupName="3. Key Levels", Order=7)] public double TV_POC { get; set; }
        [NinjaScriptProperty][Display(Name="M1", GroupName="3. Key Levels", Order=8)] public double M1 { get; set; }
        [NinjaScriptProperty][Display(Name="MT1", GroupName="3. Key Levels", Order=9)] public double MT1 { get; set; }
        [NinjaScriptProperty][Display(Name="M2", GroupName="3. Key Levels", Order=10)] public double M2 { get; set; }
        [NinjaScriptProperty][Display(Name="MT2", GroupName="3. Key Levels", Order=11)] public double MT2 { get; set; }
        [NinjaScriptProperty][Display(Name="M3", GroupName="3. Key Levels", Order=12)] public double M3 { get; set; }
        [NinjaScriptProperty][Display(Name="LL", GroupName="3. Key Levels", Order=13)] public double LL { get; set; }

        [NinjaScriptProperty][Display(Name="Start", GroupName="4. Timing")] public int StartTime { get; set; }
        [NinjaScriptProperty][Display(Name="End", GroupName="4. Timing")] public int EndTime { get; set; }
        #endregion
    }
}