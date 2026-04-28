// CC BY-NC 4.0
#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ADXMomentumScalper : Strategy
    {
        // ===== Parameters =====
        
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Contracts", GroupName = "1. Position", Order = 0)]
        public int Contracts { get; set; } = 1;

        // --- Momentum Gates ---
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ADX Period", GroupName = "2. Momentum Gates", Order = 0)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Min ADX Value", GroupName = "2. Momentum Gates", Order = 1)]
        public double MinAdxValue { get; set; } = 18.0;

        [NinjaScriptProperty, Range(1, 10)]
        [Display(Name = "Slope Lookback (Bars)", GroupName = "2. Momentum Gates", Order = 2, Description="Checks slope over this many bars to allow variance.")]
        public int SlopeLookback { get; set; } = 2;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Chop (CI) Period", GroupName = "2. Momentum Gates", Order = 3)]
        public int CiPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Max Chop (CI) Value", GroupName = "2. Momentum Gates", Order = 4, Description="Must be below this value to enter (e.g. 60).")]
        public double MaxCiValue { get; set; } = 60.0;

        // --- Risk Management ---
        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "Profit Target (ATR Mult)", GroupName = "3. Risk/Reward", Order = 0, Description="Scalp Target (e.g. 0.85)")]
        public double ProfitAtrMult { get; set; } = 0.85;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "Stop Loss (ATR Mult)", GroupName = "3. Risk/Reward", Order = 1, Description="Protective Stop (e.g. 0.75)")]
        public double StopAtrMult { get; set; } = 0.75;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", GroupName = "3. Risk/Reward", Order = 2)]
        public int AtrPeriod { get; set; } = 14;

        // --- Filters ---
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Anchor EMA Period", GroupName = "4. Filters", Order = 0)]
        public int AnchorEmaPeriod { get; set; } = 50;

        [NinjaScriptProperty]
        [Display(Name = "Use Stop X (Kill Switch)", GroupName = "4. Filters", Order = 1, Description="Exit if ADX drops or DI crosses back.")]
        public bool UseStopX { get; set; } = true;

        // --- Daily Limits ---
        [NinjaScriptProperty]
        [Display(Name = "Use Daily Max Loss", GroupName = "5. Capital Preservation", Order = 0)]
        public bool UseDailyLimit { get; set; } = true;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Daily Max Loss ($)", GroupName = "5. Capital Preservation", Order = 1)]
        public double DailyMaxLoss { get; set; } = 500;

        // ===== Internals =====
        private ADX adx;
        private ATR atr;
        private ChoppinessIndex ci; // Using built-in or custom CI logic
        private EMA anchorEma;
        
        // DI Components
        private Series<double> dmPlus;
        private Series<double> dmMinus;
        private Series<double> sumDmPlus;
        private Series<double> sumDmMinus;
        private Series<double> sumTr;
        private Series<double> diPlus;
        private Series<double> diMinus;

        private double currentPnL = 0;
        private double startingCumProfit = 0;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ADXMomentumScalper";
                Calculate = Calculate.OnPriceChange;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(AdxPeriod);
                atr = ATR(AtrPeriod);
                anchorEma = EMA(AnchorEmaPeriod);
                
                // Note: NT8 has a built-in ChoppinessIndex, or we can calc manually if needed.
                // Assuming standard ChoppinessIndex is available. If not, logic is simple.
                ci = ChoppinessIndex(CiPeriod);

                // DI Calculation Series
                dmPlus = new Series<double>(this);
                dmMinus = new Series<double>(this);
                sumDmPlus = new Series<double>(this);
                sumDmMinus = new Series<double>(this);
                sumTr = new Series<double>(this);
                diPlus = new Series<double>(this);
                diMinus = new Series<double>(this);

                AddChartIndicator(adx);
                AddChartIndicator(ci);
                AddChartIndicator(anchorEma);
            }
        }

        protected override void OnBarUpdate()
        {
            // 1. Capital Preservation Check
            if (CurrentBar < Math.Max(AdxPeriod, CiPeriod) + SlopeLookback) return;
            
            if (Bars.IsFirstBarOfSession) 
                startingCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;

            if (UseDailyLimit)
            {
                currentPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - startingCumProfit;
                if (currentPnL <= -Math.Abs(DailyMaxLoss)) return; // Stop trading today
            }

            // 2. DI Manual Calculation (Wilder's Smoothing)
            CalculateDI();

            // 3. Define Signals
            bool diCrossLong = CrossAbove(diPlus, diMinus, 1);
            bool diCrossShort = CrossAbove(diMinus, diPlus, 1);

            // 4. Momentum Gates (The "Energy")
            // A. ADX Gate: > Min AND Rising
            // We use SlopeLookback to allow for minor variance (comparing to N bars ago)
            bool adxRising = adx[0] > adx[SlopeLookback]; 
            bool adxValid = adx[0] > MinAdxValue && adxRising;

            // B. Chop Gate: < 60 AND Falling (ideally)
            bool ciFalling = ci[0] < ci[SlopeLookback];
            bool ciValid = ci[0] < MaxCiValue && ciFalling;

            // C. DI Separation Logic (Gap must be widening compared to previous bar)
            double currentGap = Math.Abs(diPlus[0] - diMinus[0]);
            double prevGap = Math.Abs(diPlus[1] - diMinus[1]);
            bool gapWidening = currentGap > prevGap;

            // D. Anchor Filter
            bool aboveEma = Close[0] > anchorEma[0];
            bool belowEma = Close[0] < anchorEma[0];

            // 5. Entry Logic
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (diCrossLong && adxValid && ciValid && gapWidening && aboveEma)
                {
                    SubmitEntry(true);
                }
                else if (diCrossShort && adxValid && ciValid && gapWidening && belowEma)
                {
                    SubmitEntry(false);
                }
            }

            // 6. Stop X (The Kill Switch)
            // If we are in a trade, check if energy failed
            if (UseStopX && Position.MarketPosition != MarketPosition.Flat)
            {
                // Logic: If ADX curls down significantly or DI reverses
                bool momentumFailed = adx[0] < adx[SlopeLookback]; // ADX lost slope
                
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    // Exit if Momentum fails OR DI+ crosses back below DI-
                    if (momentumFailed || diPlus[0] < diMinus[0])
                        ExitLong("StopX_Kill", "LongScalp");
                }
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    // Exit if Momentum fails OR DI- crosses back below DI+
                    if (momentumFailed || diMinus[0] < diPlus[0])
                        ExitShort("StopX_Kill", "ShortScalp");
                }
            }
        }

        private void SubmitEntry(bool isLong)
        {
            // Calculate Scalp Targets based on ATR
            double atrVal = atr[0];
            double stopDist = atrVal * StopAtrMult;
            double targetDist = atrVal * ProfitAtrMult;

            // Ensure min ticks (e.g. 4 ticks)
            stopDist = Math.Max(stopDist, 4 * TickSize);
            targetDist = Math.Max(targetDist, 4 * TickSize);

            if (isLong)
            {
                SetStopLoss("LongScalp", CalculationMode.Price, Close[0] - stopDist, false);
                SetProfitTarget("LongScalp", CalculationMode.Price, Close[0] + targetDist);
                EnterLong(Contracts, "LongScalp");
            }
            else
            {
                SetStopLoss("ShortScalp", CalculationMode.Price, Close[0] + stopDist, false);
                SetProfitTarget("ShortScalp", CalculationMode.Price, Close[0] - targetDist);
                EnterShort(Contracts, "ShortScalp");
            }
        }

        private void CalculateDI()
        {
            double h = High[0]; double l = Low[0];
            double h1 = High[1]; double l1 = Low[1]; double c1 = Close[1];

            double tr = Math.Max(h - l, Math.Max(Math.Abs(h - c1), Math.Abs(l - c1)));
            double up = h - h1;
            double dn = l1 - l;

            double dp = (up > 0 && up > dn) ? up : 0;
            double dm = (dn > 0 && dn > up) ? dn : 0;

            if (CurrentBar == 1)
            {
                sumTr[0] = tr;
                sumDmPlus[0] = dp;
                sumDmMinus[0] = dm;
            }
            else
            {
                sumTr[0] = sumTr[1] - (sumTr[1] / AdxPeriod) + tr;
                sumDmPlus[0] = sumDmPlus[1] - (sumDmPlus[1] / AdxPeriod) + dp;
                sumDmMinus[0] = sumDmMinus[1] - (sumDmMinus[1] / AdxPeriod) + dm;
            }

            double trVal = sumTr[0];
            if (trVal == 0) trVal = 1;

            diPlus[0] = 100 * (sumDmPlus[0] / trVal);
            diMinus[0] = 100 * (sumDmMinus[0] / trVal);
        }
    }
}