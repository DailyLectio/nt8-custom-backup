// CC BY-NC 4.0
// Hybrid v2.2: Momentum Slope + Leg Management + StopX Kill Switches
// Optimized for MNQ Apex Evaluation
// Revision: Added Entry Throttle to prevent rapid-fire orders

#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class AutoMom_BankerRunner_v2 : Strategy
    {
        // ===== Enums =====
        public enum TrailMode { None, TickTrail, BarNTrail, HybridBarNThenTick }

        // ===== Indicators =====
        private ADX adx;
        private ATR atrStop;
        
        // CI Internals
        private Series<double> trSeries;
        private SUM sumTr;
        private MAX maxHigh;
        private MIN minLow;
        private Series<double> ci;

        // DI Internals
        private Series<double> diPlusSeries, diMinusSeries;
        private Series<double> dmPlus, dmMinus, sumDmPlus, sumDmMinus, sumTrDI;

        // Management Internals
        private int entryBar = -1;
        private int lastEntryBar = -1; // <--- NEW: Prevents multiple entries on the same bar
        private double highSinceEntry = double.MinValue;
        private double lowSinceEntry = double.MaxValue;
        
        // Guards
        private double sessionPnLBaseline = 0;
        private bool tradingHalted = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AutoMom_BankerRunner_v2";
                Calculate = Calculate.OnPriceChange; 
                EntriesPerDirection = 1; // <--- MODIFIED: Restricts to 1 entry per direction
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;

                // --- Default Scalp Settings (MNQ) ---
                CiPeriod = 14; AdxPeriod = 14;
                CiEntryThreshold = 60; AdxEntryThreshold = 18; 
                UseDiCrossEntry = true;
                
                // --- Risk & StopX ---
                AtrStopMult = 0.75; 
                AtrStopLen = 14; 
                MaxStopTicks = 25; 
                
                UseExitOnDiCross = true;    
                UseExitOnAdxHook = true;    

                // Leg 1: The Banker (2 Contracts)
                Qty1 = 2;
                Target1Ticks = 40; 
                Trail1Mode = TrailMode.TickTrail;
                Trail1TickTrailTicks = 20; 

                // Leg 2: The Runner (2 Contracts)
                Qty2 = 2;
                Target2Ticks = 100; 
                Trail2Mode = TrailMode.HybridBarNThenTick;
                Trail2BarN = 2;
                Trail2HybridSwitchBars = 5; 
                Trail2TickTrailTicks = 25; 

                // Leg 3: Off
                Qty3 = 0; 
                Target3Ticks = 200;
                Trail3Mode = TrailMode.BarNTrail;

                // Guards
                DailyProfitTarget = 300;
                DailyLossLimit = 250;
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(AdxPeriod);
                atrStop = ATR(AtrStopLen);
                
                trSeries = new Series<double>(this);
                ci = new Series<double>(this);
                sumTr = SUM(trSeries, CiPeriod);
                maxHigh = MAX(High, CiPeriod);
                minLow = MIN(Low, CiPeriod);

                dmPlus = new Series<double>(this);
                dmMinus = new Series<double>(this);
                sumDmPlus = new Series<double>(this);
                sumDmMinus = new Series<double>(this);
                sumTrDI = new Series<double>(this);
                diPlusSeries = new Series<double>(this);
                diMinusSeries = new Series<double>(this);

                AddChartIndicator(adx);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(CiPeriod, AdxPeriod) + 2) return;

            // 1. Session & Guard Management
            if (Bars.IsFirstBarOfSession)
            {
                sessionPnLBaseline = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                tradingHalted = false;
                entryBar = -1;
                lastEntryBar = -1;
            }

            if (EnableDailyGuards && !tradingHalted)
            {
                double currentPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionPnLBaseline;
                if (currentPnL >= DailyProfitTarget || currentPnL <= -DailyLossLimit)
                {
                    tradingHalted = true;
                    if (Position.MarketPosition != MarketPosition.Flat) { ExitLong(); ExitShort(); }
                }
            }

            // 2. Indicator Calculation
            CalculateCI();
            CalculateDI();

            // 3. StopX Logic (The "Emergency Brake")
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (UseExitOnDiCross)
                {
                    if (Position.MarketPosition == MarketPosition.Long && CrossBelow(diPlusSeries, diMinusSeries, 1))
                        ExitLong("StopX_DI", "");
                    else if (Position.MarketPosition == MarketPosition.Short && CrossAbove(diPlusSeries, diMinusSeries, 1))
                        ExitShort("StopX_DI", "");
                }

                if (UseExitOnAdxHook && CurrentBar > 1)
                {
                    bool adxFalling = adx[0] < adx[1] && adx[1] < adx[2];
                    if (adxFalling)
                    {
                         if (Position.MarketPosition == MarketPosition.Long) ExitLong("StopX_ADX", "");
                         else ExitShort("StopX_ADX", "");
                    }
                }
            }

            // 4. Trailing Logic
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (entryBar < 0) entryBar = CurrentBar;
                highSinceEntry = Math.Max(highSinceEntry, High[0]);
                lowSinceEntry = Math.Min(lowSinceEntry, Low[0]);
                UpdateAllLegStops();
            }
            else
            {
                entryBar = -1;
                highSinceEntry = double.MinValue;
                lowSinceEntry = double.MaxValue;
            }

            // 5. Entry Logic with "One Entry Per Bar" Throttle
            if (Position.MarketPosition == MarketPosition.Flat && !tradingHalted && CurrentBar > lastEntryBar)
            {
                bool adxOk = adx[0] >= AdxEntryThreshold;
                bool ciOk = ci[0] <= CiEntryThreshold;
                bool crossUp = CrossAbove(diPlusSeries, diMinusSeries, 1);
                bool crossDown = CrossBelow(diPlusSeries, diMinusSeries, 1);

                if (adxOk && ciOk)
                {
                    double atrValue = atrStop[0];
                    int calcStop = (int)Math.Round((atrValue * AtrStopMult) / TickSize);
                    int finalStop = Math.Min(MaxStopTicks, Math.Max(5, calcStop));

                    if (UseDiCrossEntry && crossUp) 
                    {
                        lastEntryBar = CurrentBar; // Lock entry for this bar
                        SubmitSplitEntry(true, finalStop);
                    }
                    else if (UseDiCrossEntry && crossDown) 
                    {
                        lastEntryBar = CurrentBar; // Lock entry for this bar
                        SubmitSplitEntry(false, finalStop);
                    }
                }
            }
        }

        // ===== Helper Methods =====
        private void SubmitSplitEntry(bool isLong, int stopTicks)
        {
            entryBar = CurrentBar;
            highSinceEntry = High[0];
            lowSinceEntry = Low[0];
            
            // Note: Since we use Split Entry (L1, L2), ensure NT8 is set to "All Entries" 
            // but our throttle prevents the signal from firing twice.
            if (Qty1 > 0) {
                string sig = isLong ? "L1" : "S1";
                SetStopLoss(sig, CalculationMode.Ticks, stopTicks, false);
                SetProfitTarget(sig, CalculationMode.Ticks, Target1Ticks);
                if(isLong) EnterLong(Qty1, sig); else EnterShort(Qty1, sig);
            }
            if (Qty2 > 0) {
                string sig = isLong ? "L2" : "S2";
                SetStopLoss(sig, CalculationMode.Ticks, stopTicks, false);
                SetProfitTarget(sig, CalculationMode.Ticks, Target2Ticks);
                if(isLong) EnterLong(Qty2, sig); else EnterShort(Qty2, sig);
            }
            if (Qty3 > 0) {
                string sig = isLong ? "L3" : "S3";
                SetStopLoss(sig, CalculationMode.Ticks, stopTicks, false);
                SetProfitTarget(sig, CalculationMode.Ticks, Target3Ticks);
                if(isLong) EnterLong(Qty3, sig); else EnterShort(Qty3, sig);
            }
        }

        private void UpdateAllLegStops()
        {
            int barsInTrade = (CurrentBar - entryBar);
            bool isLong = Position.MarketPosition == MarketPosition.Long;
            if (isLong) {
                if(Qty1 > 0) ApplyTrail("L1", Trail1Mode, Trail1TickTrailTicks, Trail1BarN, Trail1BarNOffsetTicks, barsInTrade, Trail1HybridSwitchBars, true);
                if(Qty2 > 0) ApplyTrail("L2", Trail2Mode, Trail2TickTrailTicks, Trail2BarN, Trail2BarNOffsetTicks, barsInTrade, Trail2HybridSwitchBars, true);
                if(Qty3 > 0) ApplyTrail("L3", Trail3Mode, Trail3TickTrailTicks, Trail3BarN, Trail3BarNOffsetTicks, barsInTrade, Trail3HybridSwitchBars, true);
            } else {
                if(Qty1 > 0) ApplyTrail("S1", Trail1Mode, Trail1TickTrailTicks, Trail1BarN, Trail1BarNOffsetTicks, barsInTrade, Trail1HybridSwitchBars, false);
                if(Qty2 > 0) ApplyTrail("S2", Trail2Mode, Trail2TickTrailTicks, Trail2BarN, Trail2BarNOffsetTicks, barsInTrade, Trail2HybridSwitchBars, false);
                if(Qty3 > 0) ApplyTrail("S3", Trail3Mode, Trail3TickTrailTicks, Trail3BarN, Trail3BarNOffsetTicks, barsInTrade, Trail3HybridSwitchBars, false);
            }
        }

        private void ApplyTrail(string signal, TrailMode mode, int tickTrail, int barN, int barOffset, int barsInTrade, int hybridSwitch, bool isLong)
        {
            if (mode == TrailMode.None) return;
            TrailMode effective = mode;
            if (mode == TrailMode.HybridBarNThenTick) effective = (barsInTrade >= hybridSwitch) ? TrailMode.TickTrail : TrailMode.BarNTrail;

            double newStop = 0;
            if (effective == TrailMode.TickTrail) {
                if (isLong) newStop = highSinceEntry - (tickTrail * TickSize);
                else        newStop = lowSinceEntry + (tickTrail * TickSize);
            } else {
                int idx = Math.Min(barN, CurrentBar);
                if (isLong) newStop = Low[idx] - (barOffset * TickSize);
                else        newStop = High[idx] + (barOffset * TickSize);
            }
            SetStopLoss(signal, CalculationMode.Price, newStop, false);
        }

        private void CalculateCI() {
            double tr = Math.Max(High[0] - Low[0], Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));
            trSeries[0] = tr;
            double range = maxHigh[0] - minLow[0];
            double sTR = sumTr[0];
            if (range > 0 && sTR > 0) ci[0] = 100.0 * Math.Log10(sTR / range) / Math.Log10(CiPeriod); else ci[0] = 0;
        }

        private void CalculateDI() {
            double up = High[0] - High[1]; double down = Low[1] - Low[0];
            double dmP = (up > down && up > 0) ? up : 0; double dmM = (down > up && down > 0) ? down : 0;
            double tr = Math.Max(High[0] - Low[0], Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));
            if (CurrentBar == 1) { sumTrDI[0] = tr; sumDmPlus[0] = dmP; sumDmMinus[0] = dmM; }
            else { sumTrDI[0] = sumTrDI[1] - (sumTrDI[1]/AdxPeriod) + tr; sumDmPlus[0] = sumDmPlus[1] - (sumDmPlus[1]/AdxPeriod) + dmP; sumDmMinus[0] = sumDmMinus[1] - (sumDmMinus[1]/AdxPeriod) + dmM; }
            double trVal = sumTrDI[0] > 0 ? sumTrDI[0] : 1;
            diPlusSeries[0] = 100 * sumDmPlus[0] / trVal; diMinusSeries[0] = 100 * sumDmMinus[0] / trVal;
        }

        // ===== Properties =====
        [NinjaScriptProperty, Range(1, 200), Display(Name="CI Period", GroupName="1. Entry", Order=0)] public int CiPeriod { get; set; }
        [NinjaScriptProperty, Range(1, 200), Display(Name="ADX Period", GroupName="1. Entry", Order=1)] public int AdxPeriod { get; set; }
        [NinjaScriptProperty, Display(Name="Use DI Cross Entry", GroupName="1. Entry", Order=2)] public bool UseDiCrossEntry { get; set; }
        [NinjaScriptProperty, Range(0, 100), Display(Name="ADX Threshold", GroupName="1. Entry", Order=3)] public double AdxEntryThreshold { get; set; }
        [NinjaScriptProperty, Range(0, 100), Display(Name="CI Threshold (Below)", GroupName="1. Entry", Order=4)] public double CiEntryThreshold { get; set; }

        [NinjaScriptProperty, Range(0.1, 10.0), Display(Name="ATR Stop Mult", GroupName="2. Risk", Order=0)] public double AtrStopMult { get; set; }
        [NinjaScriptProperty, Range(1, 200), Display(Name="ATR Stop Len", GroupName="2. Risk", Order=1)] public int AtrStopLen { get; set; }
        [NinjaScriptProperty, Range(5, 500), Display(Name="Max Stop Ticks (Hard Cap)", GroupName="2. Risk", Order=2)] public int MaxStopTicks { get; set; }
        [NinjaScriptProperty, Display(Name="StopX on DI Cross", GroupName="2. Risk", Order=3)] public bool UseExitOnDiCross { get; set; }
        [NinjaScriptProperty, Display(Name="StopX on ADX Hook", GroupName="2. Risk", Order=4)] public bool UseExitOnAdxHook { get; set; }
        [NinjaScriptProperty, Display(Name="Enable Guards", GroupName="2. Risk", Order=5)] public bool EnableDailyGuards { get; set; } = true;
        [NinjaScriptProperty, Display(Name="Daily Profit ($)", GroupName="2. Risk", Order=6)] public double DailyProfitTarget { get; set; }
        [NinjaScriptProperty, Display(Name="Daily Loss ($)", GroupName="2. Risk", Order=7)] public double DailyLossLimit { get; set; }

        [NinjaScriptProperty, Range(0, 100), Display(Name="Qty 1", GroupName="3. Leg 1", Order=0)] public int Qty1 { get; set; }
        [NinjaScriptProperty, Display(Name="Target 1", GroupName="3. Leg 1", Order=1)] public int Target1Ticks { get; set; }
        [NinjaScriptProperty, Display(Name="Trail 1 Mode", GroupName="3. Leg 1", Order=2)] public TrailMode Trail1Mode { get; set; }
        [NinjaScriptProperty, Display(Name="Trail 1 Ticks", GroupName="3. Leg 1", Order=3)] public int Trail1TickTrailTicks { get; set; }

        [NinjaScriptProperty, Range(0, 100), Display(Name="Qty 2", GroupName="4. Leg 2", Order=0)] public int Qty2 { get; set; }
        [NinjaScriptProperty, Display(Name="Target 2", GroupName="4. Leg 2", Order=1)] public int Target2Ticks { get; set; }
        [NinjaScriptProperty, Display(Name="Trail 2 Mode", GroupName="4. Leg 2", Order=2)] public TrailMode Trail2Mode { get; set; }
        [NinjaScriptProperty, Display(Name="Trail 2 Ticks", GroupName="4. Leg 2", Order=3)] public int Trail2TickTrailTicks { get; set; }
        [NinjaScriptProperty, Display(Name="Trail 2 Hybrid Switch", GroupName="4. Leg 2", Order=4)] public int Trail2HybridSwitchBars { get; set; }

        [NinjaScriptProperty, Range(0, 100), Display(Name="Qty 3", GroupName="5. Leg 3", Order=0)] public int Qty3 { get; set; }
        [NinjaScriptProperty, Display(Name="Target 3", GroupName="5. Leg 3", Order=1)] public int Target3Ticks { get; set; }
        [NinjaScriptProperty, Display(Name="Trail 3 Mode", GroupName="5. Leg 3", Order=2)] public TrailMode Trail3Mode { get; set; }

        [Browsable(false)] public int Trail1BarN { get; set; } = 1; [Browsable(false)] public int Trail1BarNOffsetTicks { get; set; } = 0; [Browsable(false)] public int Trail1HybridSwitchBars { get; set; } = 5;
        [Browsable(false)] public int Trail2BarN { get; set; } = 2; [Browsable(false)] public int Trail2BarNOffsetTicks { get; set; } = 0;
        [Browsable(false)] public int Trail3TickTrailTicks { get; set; } = 25; [Browsable(false)] public int Trail3BarN { get; set; } = 2; [Browsable(false)] public int Trail3BarNOffsetTicks { get; set; } = 0; [Browsable(false)] public int Trail3HybridSwitchBars { get; set; } = 5;
    }
}