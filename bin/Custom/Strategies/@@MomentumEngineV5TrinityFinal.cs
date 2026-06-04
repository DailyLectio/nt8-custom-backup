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
    public class MomentumEngine_V5_Trinity_Final : Strategy
    {
        // 1. INDICATORS
        private ADXGu5v2 gu5_Fast;    
        private ADXGu5v2 gu5_Context; 
        
        private ADX adx_Context;      
        private ChoppinessIndex chop_Context; 
        
        private ATR atr;
        private VOLMA volMa;
        
        // 2. UI CONTROLS
        private Button armScalpLongBtn, armScalpShortBtn;
        private Button armCoreLongBtn, armCoreShortBtn;
        private Button disarmBtn;
        private Grid chartGrid;
        
        // 3. STATE
        private bool isArmedScalpLong = false;
        private bool isArmedScalpShort = false;
        private bool isArmedCoreLong = false;
        private bool isArmedCoreShort = false;
        private string activeStatus = "STANDBY";
        private string tradeType = "NONE";
        
        // 4. RISK
        private double sessionStartCumProfit = 0;
        private double dailyPnL = 0;
        private double pnlFloor = -9999;
        private int consecutiveLosers = 0;

        // 5. TARGETS
        private double t1Price = 0; 
        private double t2Price = 0; 

        // Levels
        [NinjaScriptProperty] public double B6 { get; set; } [NinjaScriptProperty] public double B5 { get; set; }
        [NinjaScriptProperty] public double B4 { get; set; } [NinjaScriptProperty] public double B3 { get; set; }
        [NinjaScriptProperty] public double B2 { get; set; } [NinjaScriptProperty] public double B1 { get; set; }
        [NinjaScriptProperty] public double POC { get; set; }
        [NinjaScriptProperty] public double R1 { get; set; } [NinjaScriptProperty] public double R2 { get; set; }
        [NinjaScriptProperty] public double R3 { get; set; } [NinjaScriptProperty] public double R4 { get; set; }
        [NinjaScriptProperty] public double R5 { get; set; } [NinjaScriptProperty] public double R6 { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "V5 FINAL: Fixed SetStopLoss Overloads";
                Name = "MomentumEngine_V5_Trinity_Final";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                IsExitOnSessionCloseStrategy = true;

                // RISK
                DailyGoal = 600; DailyLoss = 350;
                ShieldTrigger = 200; ShieldLock = 100;
                MaxConsecutiveLosses = 2;
                InitialStopTicks = 35;
                
                // TIMING
                StartTime = 094000; EndTime = 114000;
                
                // CONFIG
                ContextMinutes = 5; 
            }
            else if (State == State.Configure)
            {
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                AddDataSeries(BarsPeriodType.Minute, ContextMinutes); 
            }
            else if (State == State.DataLoaded)
            {
                // 1m Indicators
                gu5_Fast = ADXGu5v2(14, 14, 20, 35, 2, 14, 3, 60, 20);
                atr = ATR(14);
                volMa = VOLMA(20);

                // 5m Indicators (Using Closes[1] for proper type matching)
                gu5_Context = ADXGu5v2(Closes[1], 14, 14, 20, 35, 2, 14, 3, 60, 20);
                
                adx_Context = ADX(BarsArray[1], 14);
                chop_Context = ChoppinessIndex(BarsArray[1], 14);
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
            if (CurrentBar < 20) return;
            if (BarsInProgress == 1) return; 

            // 1. RISK & SESSION
            if (Bars.IsFirstBarOfSession) {
                sessionStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                pnlFloor = -9999; consecutiveLosers = 0;
            }
            dailyPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;

            if (consecutiveLosers >= MaxConsecutiveLosses) { activeStatus = "LOCKED (2 LOSSES)"; Disarm(); return; }
            if (dailyPnL >= ShieldTrigger && pnlFloor == -9999) pnlFloor = ShieldLock;
            if (pnlFloor != -9999 && dailyPnL <= pnlFloor) { activeStatus = "SHIELD LOCKED"; Disarm(); return; }

            // 2. GUI UPDATE
            if (State == State.Realtime) UpdateButtonVisuals();

            // 3. CIRCUIT BREAKER
            if (Position.MarketPosition == MarketPosition.Long) {
                if (gu5_Fast.StopXLongSeries[0] > 0 || gu5_Context.StopXLongSeries[0] > 0) {
                    ExitLong("CircuitBreaker_X"); return;
                }
            }
            if (Position.MarketPosition == MarketPosition.Short) {
                if (gu5_Fast.StopXShortSeries[0] > 0 || gu5_Context.StopXShortSeries[0] > 0) {
                    ExitShort("CircuitBreaker_X"); return;
                }
            }

            // 4. ENTRY LOGIC
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (!isArmedScalpLong && !isArmedScalpShort && !isArmedCoreLong && !isArmedCoreShort) return;

                // 5M REGIME
                double cChop = chop_Context[0];
                bool isChopHigh = cChop > 60;
                
                // 1M TRIGGER
                double sig = gu5_Fast.ConditionSeries[0]; 
                bool rvolSpike = (Volume[0] > volMa[0] * 1.25);

                // --- SCALP LOGIC ---
                if (isArmedScalpLong || isArmedScalpShort)
                {
                    if (!isChopHigh) 
                    {
                        if (isArmedScalpLong && sig >= 0.5) {
                            EnterLong(3, "V5_Scalp");
                            SetStopLoss("V5_Scalp", CalculationMode.Ticks, InitialStopTicks, false);
                            tradeType = "SCALP"; Disarm();
                        }
                        else if (isArmedScalpShort && sig <= -0.5) {
                            EnterShort(3, "V5_Scalp");
                            SetStopLoss("V5_Scalp", CalculationMode.Ticks, InitialStopTicks, false);
                            tradeType = "SCALP"; Disarm();
                        }
                    }
                }

                // --- CORE LOGIC ---
                if (isArmedCoreLong || isArmedCoreShort)
                {
                    bool validTriggerLong = (sig == 1.0) || (sig == 0.5 && rvolSpike);
                    bool validTriggerShort = (sig == -1.0) || (sig == -0.5 && rvolSpike);

                    if (isArmedCoreLong && validTriggerLong) {
                        CalculateCoreTargets(true);
                        EnterLong(4, "V5_Core");
                        SetStopLoss("V5_Core", CalculationMode.Ticks, InitialStopTicks, false);
                        tradeType = "CORE"; Disarm();
                    }
                    else if (isArmedCoreShort && validTriggerShort) {
                        CalculateCoreTargets(false);
                        EnterShort(4, "V5_Core");
                        SetStopLoss("V5_Core", CalculationMode.Ticks, InitialStopTicks, false);
                        tradeType = "CORE"; Disarm();
                    }
                }
            }

            // 5. ATM MANAGEMENT
            if (Position.MarketPosition != MarketPosition.Flat) ManageATM();
        }

        private void ManageATM()
        {
            double entry = Position.AveragePrice;
            double cur = Close[0];
            bool isLong = Position.MarketPosition == MarketPosition.Long;
            double profitTicks = Position.GetUnrealizedProfitLoss(PerformanceUnit.Ticks, cur);

            // FIX: Explicitly defined signal names for SetStopLoss calls
            string scalpSig = "V5_Scalp";
            string coreSig  = "V5_Core";

            if (tradeType == "SCALP")
            {
                if (Position.Quantity == 3 && profitTicks >= 25) {
                    if (isLong)
                        ExitLong(2, "Bank_Scalp", scalpSig);
                    else
                        ExitShort(2, "Bank_Scalp", scalpSig);
                    // FIXED: 4 Arguments
                    SetStopLoss(scalpSig, CalculationMode.Price, isLong ? entry + TickSize : entry - TickSize, false);
                }
                if (Position.Quantity == 1) {
                    double trail = isLong ? Math.Max(entry, Low[1] - TickSize) : Math.Min(entry, High[1] + TickSize);
                    // FIXED: 4 Arguments
                    SetStopLoss(scalpSig, CalculationMode.Price, trail, false);
                }
            }
            
            else if (tradeType == "CORE")
            {
                double dist = isLong ? cur - entry : entry - cur;
                double t1Dist = (t1Price > 0) ? Math.Abs(entry - t1Price) : 40 * TickSize;
                
                if (Position.Quantity == 4 && dist >= t1Dist) {
                    if (isLong)
                        ExitLong(2, "Bank_Mid", coreSig);
                    else
                        ExitShort(2, "Bank_Mid", coreSig);
                    // FIXED: 4 Arguments
                    SetStopLoss(coreSig, CalculationMode.Price, entry, false);
                }
                double t2Dist = (t2Price > 0) ? Math.Abs(entry - t2Price) : 80 * TickSize;
                if (Position.Quantity == 2 && dist >= t2Dist) {
                    if (isLong)
                        ExitLong(1, "Bank_Level", coreSig);
                    else
                        ExitShort(1, "Bank_Level", coreSig);
                }
                if (Position.Quantity == 1) {
                    double trail = isLong ? Math.Min(Low[0], Low[1]) - 2*TickSize : Math.Max(High[0], High[1]) + 2*TickSize;
                    // FIXED: 4 Arguments
                    SetStopLoss(coreSig, CalculationMode.Price, trail, false);
                }
            }
        }

        private void CalculateCoreTargets(bool isLong) {
            double p = Close[0];
            double nextLvl = isLong ? (p < POC ? POC : p < B1 ? B1 : p < B2 ? B2 : B3) : (p > POC ? POC : p > R1 ? R1 : p > R2 ? R2 : R3);
            double prevLvl = isLong ? (p < POC ? R1 : p < B1 ? POC : p < B2 ? B1 : B2) : (p > POC ? B1 : p > R1 ? POC : p > R2 ? R1 : R2);
            t2Price = nextLvl; 
            t1Price = prevLvl + ((nextLvl - prevLvl) * 0.5);
        }

        private void Disarm() { isArmedScalpLong = false; isArmedScalpShort = false; isArmedCoreLong = false; isArmedCoreShort = false; }

        private void CreateWPFControls() {
            chartGrid = ChartControl.Parent as Grid; if (chartGrid == null) return;
            armScalpLongBtn = Btn("SCALP LONG", Brushes.DimGray, 10, 40);
            armScalpShortBtn = Btn("SCALP SHORT", Brushes.DimGray, 100, 40);
            armCoreLongBtn = Btn("CORE LONG", Brushes.DimGray, 10, 75);
            armCoreShortBtn = Btn("CORE SHORT", Brushes.DimGray, 100, 75);
            disarmBtn = Btn("DISARM ALL", Brushes.DarkRed, 190, 40); disarmBtn.Height = 65;

            armScalpLongBtn.Click += (s, e) => { Disarm(); isArmedScalpLong = true; activeStatus = "HUNTING SCALP LONG"; };
            armScalpShortBtn.Click += (s, e) => { Disarm(); isArmedScalpShort = true; activeStatus = "HUNTING SCALP SHORT"; };
            armCoreLongBtn.Click += (s, e) => { Disarm(); isArmedCoreLong = true; activeStatus = "HUNTING CORE LONG"; };
            armCoreShortBtn.Click += (s, e) => { Disarm(); isArmedCoreShort = true; activeStatus = "HUNTING CORE SHORT"; };
            disarmBtn.Click += (s, e) => { Disarm(); activeStatus = "STANDBY"; };

            chartGrid.Children.Add(armScalpLongBtn); chartGrid.Children.Add(armScalpShortBtn);
            chartGrid.Children.Add(armCoreLongBtn); chartGrid.Children.Add(armCoreShortBtn);
            chartGrid.Children.Add(disarmBtn);
        }
        
        private Button Btn(string txt, Brush bg, double x, double y) {
            return new Button { Content = txt, Background = bg, Foreground = Brushes.White, Margin = new Thickness(x, y, 0, 0), Width=85, Height=30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, FontSize=10, FontWeight=FontWeights.Bold };
        }
        private void DisposeWPFControls() { if (chartGrid != null) { chartGrid.Children.Remove(armScalpLongBtn); chartGrid.Children.Remove(armScalpShortBtn); chartGrid.Children.Remove(armCoreLongBtn); chartGrid.Children.Remove(armCoreShortBtn); chartGrid.Children.Remove(disarmBtn); } }
        private void UpdateButtonVisuals() {
            if (armScalpLongBtn == null) return;
            ChartControl.Dispatcher.InvokeAsync(() => {
                armScalpLongBtn.Background = isArmedScalpLong ? Brushes.LimeGreen : Brushes.DimGray;
                armScalpShortBtn.Background = isArmedScalpShort ? Brushes.Red : Brushes.DimGray;
                armCoreLongBtn.Background = isArmedCoreLong ? Brushes.LimeGreen : Brushes.DimGray;
                armCoreShortBtn.Background = isArmedCoreShort ? Brushes.Red : Brushes.DimGray;
                string info = string.Format("V5 FINAL | PnL: {0:C0}\nSTATUS: {1}", dailyPnL, activeStatus);
                Draw.TextFixed(this, "Dash", info, TextPosition.TopRight, Brushes.Cyan, new SimpleFont("Arial", 12), Brushes.Transparent, Brushes.Black, 100);
            });
        }
        
        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time) {
             if (execution.Order != null && execution.Order.OrderState == OrderState.Filled) {
                if (SystemPerformance.AllTrades.Count > 0) {
                    Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                    if (lastTrade.ProfitCurrency < 0) consecutiveLosers++; else consecutiveLosers = 0;
                }
            }
        }

        #region Properties
        [NinjaScriptProperty][Display(Name="Context Timeframe (Minutes)", GroupName="0. Tactics")] public int ContextMinutes { get; set; }
        [NinjaScriptProperty][Display(Name="Daily Goal", GroupName="1. Risk")] public double DailyGoal { get; set; }
        [NinjaScriptProperty][Display(Name="Daily Loss", GroupName="1. Risk")] public double DailyLoss { get; set; }
        [NinjaScriptProperty][Display(Name="Shield Trigger", GroupName="1. Risk")] public double ShieldTrigger { get; set; }
        [NinjaScriptProperty][Display(Name="Shield Lock", GroupName="1. Risk")] public double ShieldLock { get; set; }
        [NinjaScriptProperty][Display(Name="Max Cons. Losses", GroupName="1. Risk")] public int MaxConsecutiveLosses { get; set; }
        [NinjaScriptProperty][Display(Name="Initial Stop Ticks", GroupName="1. Risk")] public int InitialStopTicks { get; set; }
        [NinjaScriptProperty][Display(Name="Start", GroupName="4. Timing")] public int StartTime { get; set; }
        [NinjaScriptProperty][Display(Name="End", GroupName="4. Timing")] public int EndTime { get; set; }
        #endregion
    }
}
