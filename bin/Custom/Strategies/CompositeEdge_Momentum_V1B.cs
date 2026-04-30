// CC BY-NC 4.0
// CompositeEdge_Momentum_V1B.cs — Multi-Factor Momentum Strategy (V1B)
// ─────────────────────────────────────────────────────────────────────────
// V1B ADDITIONS over V1A:
//   1. A/B Mode switch (RequireFootprintConfirmation)
//      false = V1A: RSI/MACD/SuperTrend/DEMA confluence only
//      true  = V1B: also requires fresh SIB, DEB, or PAR signal
//   2. Daily Bias Filter — momentum-specific routing:
//        B = both sides (breakout day, full momentum)
//        P = long only
//        b = short only
//        D = BLOCKED (rotation day, no momentum trades)
//   3. Kill Switch — DEIA, EEMDF, or DT flattens Leg2 runner immediately.
//      DT is especially relevant here: a delta flip against an open
//      momentum position is the first sign the move is stalling.
// ─────────────────────────────────────────────────────────────────────────

#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class CompositeEdge_Momentum_V1B : Strategy
    {
        // =====================================================================
        // PARAMETERS — REGIME GATES (unchanged)
        // =====================================================================
        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name = "ADX Period", GroupName = "1. Regime Gates", Order = 0)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name = "ADX Threshold", GroupName = "1. Regime Gates", Order = 1)]
        public int AdxThreshold { get; set; } = 20;

        [NinjaScriptProperty, Range(10, 500)]
        [Display(Name = "DEMA Fast Period", GroupName = "1. Regime Gates", Order = 2)]
        public int DemaFastPeriod { get; set; } = 100;

        [NinjaScriptProperty, Range(20, 1000)]
        [Display(Name = "DEMA Slow Period", GroupName = "1. Regime Gates", Order = 3)]
        public int DemaSlowPeriod { get; set; } = 200;

        [NinjaScriptProperty, Range(0.1, 10.0)]
        [Display(Name = "SuperTrend Factor", GroupName = "1. Regime Gates", Order = 4)]
        public double SuperTrendFactor { get; set; } = 1.0;

        [NinjaScriptProperty, Range(3, 50)]
        [Display(Name = "SuperTrend ATR Period", GroupName = "1. Regime Gates", Order = 5)]
        public int SuperTrendAtrPeriod { get; set; } = 10;

        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name = "Bollinger Period", GroupName = "1. Regime Gates", Order = 6)]
        public int BollingerPeriod { get; set; } = 20;

        [NinjaScriptProperty, Range(0.5, 4.0)]
        [Display(Name = "Bollinger StdDev", GroupName = "1. Regime Gates", Order = 7)]
        public double BollingerDev { get; set; } = 2.0;

        // =====================================================================
        // PARAMETERS — ENTRY SIGNAL (unchanged)
        // =====================================================================
        [NinjaScriptProperty, Range(3, 50)]
        [Display(Name = "RSI Period", GroupName = "2. Entry Signal", Order = 0)]
        public int RsiPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(3, 50)]
        [Display(Name = "RSI MA Period", GroupName = "2. Entry Signal", Order = 1)]
        public int RsiMaPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(2, 50)]
        [Display(Name = "MACD Fast", GroupName = "2. Entry Signal", Order = 2)]
        public int MacdFast { get; set; } = 12;

        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "MACD Slow", GroupName = "2. Entry Signal", Order = 3)]
        public int MacdSlow { get; set; } = 26;

        [NinjaScriptProperty, Range(2, 50)]
        [Display(Name = "MACD Signal", GroupName = "2. Entry Signal", Order = 4)]
        public int MacdSignal { get; set; } = 9;

        // =====================================================================
        // PARAMETERS — HMM SIZING (unchanged)
        // =====================================================================
        [NinjaScriptProperty, Range(10, 200)]
        [Display(Name = "HMM Lookback", GroupName = "3. HMM Sizing", Order = 0)]
        public int HmmLookback { get; set; } = 50;

        [NinjaScriptProperty, Range(0.01, 1.0)]
        [Display(Name = "HMM Learning Rate", GroupName = "3. HMM Sizing", Order = 1)]
        public double HmmLearningRate { get; set; } = 0.10;

        // =====================================================================
        // PARAMETERS — RISK (unchanged)
        // =====================================================================
        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "ATR Period", GroupName = "4. Risk", Order = 0)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.5, 5.0)]
        [Display(Name = "ATR Stop Multiplier", GroupName = "4. Risk", Order = 1)]
        public double AtrStopMult { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0.5, 10.0)]
        [Display(Name = "Leg1 TP Multiplier (ATR)", GroupName = "4. Risk", Order = 2)]
        public double Leg1TpMult { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1.0, 15.0)]
        [Display(Name = "Leg2 TP Multiplier (ATR)", GroupName = "4. Risk", Order = 3)]
        public double Leg2TpMult { get; set; } = 2.5;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5  ES=12.50  MNQ=0.50  MES=1.25", GroupName = "4. Risk", Order = 4)]
        public double TickValueDollars { get; set; } = 5.00;

        [NinjaScriptProperty, Range(2, 100)]
        [Display(Name = "Max Total Contracts", GroupName = "4. Risk", Order = 5)]
        public int MaxTotalContracts { get; set; } = 2;

        // =====================================================================
        // PARAMETERS — GUARDS / TIME (unchanged)
        // =====================================================================
        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "5. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0=off)", GroupName = "5. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0=off)", GroupName = "5. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "6. Time", Order = 0)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Start Time (HHmmss)", GroupName = "6. Time", Order = 1)]
        public int StartTime { get; set; } = 103500;

        [NinjaScriptProperty]
        [Display(Name = "End Time (HHmmss)", GroupName = "6. Time", Order = 2)]
        public int EndTime { get; set; } = 155500;

        // =====================================================================
        // PARAMETERS — V1B ORDERFLOW LAYER (NEW)
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "A/B: Require Footprint Confirmation",
                 GroupName = "7. OrderFlow (V1B)", Order = 0,
                 Description = "false=V1A (gate confluence only). true=V1B (also needs SIB/DEB/PAR).")]
        public bool RequireFootprintConfirmation { get; set; } = false;

        [NinjaScriptProperty, Range(1, 15)]
        [Display(Name = "Signal Valid Window (minutes)", GroupName = "7. OrderFlow (V1B)", Order = 1)]
        public int FootprintValidMinutes { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Entry: Accept SIB (Stacked Imbalances)",
                 GroupName = "7. OrderFlow (V1B)", Order = 2,
                 Description = "Primary continuation signal — stacked buy/sell imbalances.")]
        public bool UseSibEntry { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Entry: Accept DEB (Delta Expansion Breakout)",
                 GroupName = "7. OrderFlow (V1B)", Order = 3,
                 Description = "Secondary continuation signal — volume expansion with strong close.")]
        public bool UseDebEntry { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Entry: Accept PAR (Pullback Absorption Reversal)",
                 GroupName = "7. OrderFlow (V1B)", Order = 4,
                 Description = "Tertiary continuation signal — pullback in trend absorbed.")]
        public bool UseParEntry { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable Kill Switch (DEIA / EEMDF / DT)",
                 GroupName = "7. OrderFlow (V1B)", Order = 5,
                 Description = "DT especially powerful here — delta flip against open momentum.")]
        public bool EnableKillSwitch { get; set; } = true;

        [NinjaScriptProperty, Range(0, 5)]
        [Display(Name = "Kill Switch Signal Age (minutes)", GroupName = "7. OrderFlow (V1B)", Order = 6)]
        public int KillSwitchMaxMinutes { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Enable Daily Bias Filter (TradeHUD)",
                 GroupName = "7. OrderFlow (V1B)", Order = 7,
                 Description = "B=both, P=long, b=short, D=BLOCKED (no momentum in rotation).")]
        public bool EnableDailyBiasFilter { get; set; } = true;

        // =====================================================================
        // INDICATORS
        // =====================================================================
        private ATR       atrInd;
        private Bollinger bb;

        // =====================================================================
        // ADX STATE
        // =====================================================================
        private double smoothedTR = 0, smoothedDMp = 0, smoothedDMm = 0;
        private double adxValue = 0, dxSum = 0;
        private int    dxCount  = 0;

        // =====================================================================
        // DEMA STATE
        // =====================================================================
        private double emaF1 = double.NaN, emaF2 = double.NaN;
        private double emaS1 = double.NaN, emaS2 = double.NaN;

        // =====================================================================
        // SUPERTREND STATE
        // =====================================================================
        private double stAtr = 0;
        private int    stAtrCount = 0;
        private double finalUpperST = double.NaN, finalLowerST = double.NaN;
        private int    stDirection = 0;

        // =====================================================================
        // RSI STATE
        // =====================================================================
        private double rsiAvgUp = 0, rsiAvgDown = 0;
        private double rsiValue = 50, rsiMaValue = 50;
        private double[] rsiBuffer;
        private int      rsiBufferIdx = 0;
        private double   rsiBufferSum = 0;
        private int      rsiBufferCount = 0;

        // =====================================================================
        // MACD STATE
        // =====================================================================
        private double macdFastEma = double.NaN, macdSlowEma = double.NaN;
        private double macdSignalEma = double.NaN;
        private double macdHist = 0, macdHistPrev = 0;

        // =====================================================================
        // HMM STATE
        // =====================================================================
        private double hmmProb0 = 0.25, hmmProb1 = 0.25, hmmProb2 = 0.25, hmmProb3 = 0.25;

        private const double P00=0.90,P01=0.05,P02=0.02,P03=0.03;
        private const double P10=0.10,P11=0.80,P12=0.05,P13=0.05;
        private const double P20=0.00,P21=0.10,P22=0.85,P23=0.05;
        private const double P30=0.10,P31=0.05,P32=0.00,P33=0.85;

        // =====================================================================
        // RUNTIME STATE
        // =====================================================================
        private int    consecutiveLosers  = 0;
        private int    lastTradeCount     = 0;
        private double sessionStartProfit = 0;

        private bool   leg1Hit        = false;
        private bool   leg1JustHit    = false;
        private int    currentLeg2Qty = 1;
        private string activeLeg2     = "";
        private bool   prevDemaBullish = false;

        // =====================================================================
        // ORDER LABELS
        // =====================================================================
        private const string CEL1 = "CEM_Long1";
        private const string CEL2 = "CEM_Long2";
        private const string CES1 = "CEM_Short1";
        private const string CES2 = "CEM_Short2";

        // =====================================================================
        // HELPERS
        // =====================================================================
        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        private bool IsInTime()
        {
            if (!EnableTimeFilter) return true;
            int t = ToTime(Time[0]);
            return t >= StartTime && t <= EndTime;
        }

        private int CalcMaxContracts()
        {
            double a = atrInd[0];
            if (a <= 0) return 1;
            double r = (a * AtrStopMult) / TickSize * TickValueDollars;
            if (r <= 0) return 1;
            int riskBasedQty = Math.Max(1, (int)(1500.0 / r));
            return Math.Min(MaxTotalContracts, riskBasedQty);
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
            => Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));

        private double EmaUpdate(double prev, double price, int period)
        {
            double k = 2.0 / (period + 1);
            return double.IsNaN(prev) ? price : prev + k * (price - prev);
        }

        // ── V1B helpers ───────────────────────────────────────────────────────
        private bool IsFootprintEntryConfirmed()
        {
            if (!RequireFootprintConfirmation) return true;
            DateTime now = Time[0];
            if (UseSibEntry && HUDMessengerV1B.IsSignalFresh("Scanner_SIB", now, FootprintValidMinutes)) return true;
            if (UseDebEntry && HUDMessengerV1B.IsSignalFresh("Scanner_DEB", now, FootprintValidMinutes)) return true;
            if (UseParEntry && HUDMessengerV1B.IsSignalFresh("Scanner_PAR", now, FootprintValidMinutes)) return true;
            return false;
        }

        private bool IsKillSwitchActive()
        {
            if (!EnableKillSwitch) return false;
            DateTime now = Time[0];
            double w = Math.Max(0.5, KillSwitchMaxMinutes);
            // DT is the first-warning kill signal for momentum — check it alongside DEIA/EEMDF
            return HUDMessengerV1B.IsSignalFresh("Scanner_DEIA",  now, w)
                || HUDMessengerV1B.IsSignalFresh("Scanner_EEMDF", now, w)
                || HUDMessengerV1B.IsSignalFresh("Scanner_DT",    now, w);
        }

        // Momentum-specific bias: D=blocked, B=full, P=long, b=short
        private bool IsBiasCompatible(bool isLong)
        {
            if (!EnableDailyBiasFilter) return true;
            string bias = HUDMessengerV1B.CurrentDailyBias;
            if (string.IsNullOrEmpty(bias)) return true;
            switch (bias)
            {
                case "D": return false;         // Rotation: no momentum entries
                case "P": return isLong;         // Bull trend: long only
                case "b": return !isLong;        // Bear trend: short only
                case "B": return true;           // Breakout: both directions
                default:  return true;
            }
        }

        // ── Indicator updates (per bar) ───────────────────────────────────────
        private void UpdateAdx()
        {
            if (CurrentBar < 1) return;
            double tr  = Math.Max(High[0]-Low[0],Math.Max(Math.Abs(High[0]-Close[1]),Math.Abs(Low[0]-Close[1])));
            double dmp = (High[0]-High[1] > Low[1]-Low[0]) ? Math.Max(High[0]-High[1],0) : 0;
            double dmm = (Low[1]-Low[0] > High[0]-High[1]) ? Math.Max(Low[1]-Low[0],0) : 0;

            if (smoothedTR == 0) { smoothedTR=tr; smoothedDMp=dmp; smoothedDMm=dmm; }
            else
            {
                smoothedTR  = smoothedTR  - smoothedTR  / AdxPeriod + tr;
                smoothedDMp = smoothedDMp - smoothedDMp / AdxPeriod + dmp;
                smoothedDMm = smoothedDMm - smoothedDMm / AdxPeriod + dmm;
            }
            if (smoothedTR <= 0) return;
            double dip = smoothedDMp / smoothedTR * 100;
            double dim = smoothedDMm / smoothedTR * 100;
            double den = dip + dim;
            if (den <= 0) return;
            double dx = Math.Abs(dip - dim) / den * 100;
            dxCount++;
            if (dxCount <= AdxPeriod) { dxSum += dx; adxValue = dxSum / dxCount; }
            else adxValue = (adxValue * (AdxPeriod - 1) + dx) / AdxPeriod;
        }

        private double GetDema(int period, ref double e1, ref double e2)
        {
            double k = 2.0 / (period + 1);
            if (double.IsNaN(e1)) { e1 = Close[0]; e2 = Close[0]; }
            else { e1 = e1 + k*(Close[0]-e1); e2 = e2 + k*(e1-e2); }
            return 2*e1 - e2;
        }

        private void UpdateSuperTrend()
        {
            if (CurrentBar < 1) return;
            double tr = Math.Max(High[0]-Low[0],Math.Max(Math.Abs(High[0]-Close[1]),Math.Abs(Low[0]-Close[1])));
            stAtrCount++;
            stAtr = stAtrCount <= SuperTrendAtrPeriod
                ? stAtr + (tr - stAtr) / stAtrCount
                : (stAtr * (SuperTrendAtrPeriod-1) + tr) / SuperTrendAtrPeriod;

            double hl2 = (High[0]+Low[0])/2.0;
            double ub  = hl2 + SuperTrendFactor * stAtr;
            double lb  = hl2 - SuperTrendFactor * stAtr;

            if (double.IsNaN(finalUpperST)) { finalUpperST=ub; finalLowerST=lb; stDirection=0; return; }

            double pc = Close[1];
            finalUpperST = (ub < finalUpperST || pc > finalUpperST) ? ub : finalUpperST;
            finalLowerST = (lb > finalLowerST || pc < finalLowerST) ? lb : finalLowerST;

            if (Close[0] > finalUpperST) stDirection = -1;
            else if (Close[0] < finalLowerST) stDirection = 1;
        }

        private void UpdateRsi()
        {
            if (CurrentBar < 1) return;
            double chg  = Close[0]-Close[1];
            double gain = chg > 0 ? chg : 0;
            double loss = chg < 0 ? -chg : 0;

            if (rsiBufferCount < RsiPeriod)
            {
                rsiAvgUp   = (rsiAvgUp   * rsiBufferCount + gain) / (rsiBufferCount+1);
                rsiAvgDown = (rsiAvgDown * rsiBufferCount + loss) / (rsiBufferCount+1);
                rsiBufferCount++;
            }
            else
            {
                rsiAvgUp   = (rsiAvgUp   * (RsiPeriod-1) + gain) / RsiPeriod;
                rsiAvgDown = (rsiAvgDown * (RsiPeriod-1) + loss) / RsiPeriod;
            }
            rsiValue = rsiAvgDown == 0 ? 100 : rsiAvgUp == 0 ? 0 : 100 - 100/(1 + rsiAvgUp/rsiAvgDown);

            if (rsiBuffer == null) rsiBuffer = new double[RsiMaPeriod];
            rsiBufferSum -= rsiBuffer[rsiBufferIdx];
            rsiBuffer[rsiBufferIdx] = rsiValue;
            rsiBufferSum += rsiValue;
            rsiBufferIdx  = (rsiBufferIdx + 1) % RsiMaPeriod;
            rsiMaValue    = rsiBufferSum / Math.Min(CurrentBar + 1, RsiMaPeriod);
        }

        private void UpdateMacd()
        {
            if (double.IsNaN(macdFastEma)) macdFastEma = Close[0];
            if (double.IsNaN(macdSlowEma)) macdSlowEma = Close[0];
            macdFastEma = EmaUpdate(macdFastEma, Close[0], MacdFast);
            macdSlowEma = EmaUpdate(macdSlowEma, Close[0], MacdSlow);
            double line = macdFastEma - macdSlowEma;
            if (double.IsNaN(macdSignalEma)) macdSignalEma = line;
            else macdSignalEma = EmaUpdate(macdSignalEma, line, MacdSignal);
            macdHistPrev = macdHist;
            macdHist     = line - macdSignalEma;
        }

        private void UpdateHmm()
        {
            if (CurrentBar < HmmLookback + 1) return;
            double sumLR = 0, sumLR2 = 0;
            for (int i = 0; i < HmmLookback; i++)
            {
                if (Close[i+1] <= 0) continue;
                double lr = Math.Log(Close[i] / Close[i+1]);
                sumLR += lr; sumLR2 += lr*lr;
            }
            double meanLR = sumLR / HmmLookback;
            double varLR  = sumLR2 / HmmLookback - meanLR * meanLR;
            double volLR  = varLR > 0 ? Math.Sqrt(varLR) : 1e-9;
            double logRet = Close[1] > 0 ? Math.Log(Close[0]/Close[1]) : 0;
            double normR  = (logRet - meanLR) / (volLR + 1e-9);
            double sumVol = 0;
            for (int i = 0; i < HmmLookback; i++)
                if (Close[i+1] > 0) sumVol += Math.Abs(Math.Log(Close[i]/Close[i+1]));
            double avgVol = sumVol / HmmLookback;
            double normV  = avgVol > 1e-9 ? volLR / avgVol : 1.0;

            double e0 = Math.Exp(-Math.Pow(normV-0.7,2)/0.5) * Math.Exp(-Math.Pow(normR-0.5,2)/2.0);
            double e1 = Math.Exp(-Math.Pow(normV-1.5,2)/1.0) * Math.Exp(-(normR*normR)/0.5);
            double e2 = Math.Exp(-Math.Pow(normV-2.0,2)/1.0) * (normR < -1.5 ? 1.0 : Math.Exp(-Math.Pow(normR+2.0,2)/1.0));
            double e3 = Math.Exp(-Math.Pow(normV-0.6,2)/0.5) * Math.Exp(-Math.Pow(normR+0.2,2)/1.0);

            double pr0 = hmmProb0*P00+hmmProb1*P10+hmmProb2*P20+hmmProb3*P30;
            double pr1 = hmmProb0*P01+hmmProb1*P11+hmmProb2*P21+hmmProb3*P31;
            double pr2 = hmmProb0*P02+hmmProb1*P12+hmmProb2*P22+hmmProb3*P32;
            double pr3 = hmmProb0*P03+hmmProb1*P13+hmmProb2*P23+hmmProb3*P33;

            double u0=e0*pr0, u1=e1*pr1, u2=e2*pr2, u3=e3*pr3;
            double su = u0+u1+u2+u3;
            if (su <= 0) return;

            double lr_rate = HmmLearningRate;
            hmmProb0 = (1-lr_rate)*hmmProb0 + lr_rate*(u0/su);
            hmmProb1 = (1-lr_rate)*hmmProb1 + lr_rate*(u1/su);
            hmmProb2 = (1-lr_rate)*hmmProb2 + lr_rate*(u2/su);
            hmmProb3 = (1-lr_rate)*hmmProb3 + lr_rate*(u3/su);
            double tot = hmmProb0+hmmProb1+hmmProb2+hmmProb3;
            if (tot > 0) { hmmProb0/=tot; hmmProb1/=tot; hmmProb2/=tot; hmmProb3/=tot; }
        }

        private int GetHmmSizePct()
        {
            if (hmmProb2 > 0.30) return 0;
            if (hmmProb1 > 0.50) return 50;
            if (hmmProb0 > 0.60) return 100;
            return 75;
        }

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "CompositeEdge_Momentum_V1B";
                Calculate                    = Calculate.OnBarClose;
                EntriesPerDirection          = 2;
                EntryHandling                = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                RealtimeErrorHandling        = RealtimeErrorHandling.StopCancelClose;
                TraceOrders                  = false;
            }
            else if (State == State.DataLoaded)
            {
                atrInd         = ATR(AtrPeriod);
                bb             = Bollinger(BollingerDev, BollingerPeriod);
                rsiBuffer      = new double[RsiMaPeriod];
                lastTradeCount = SystemPerformance.AllTrades.Count;
            }
        }

        protected override void OnExecutionUpdate(
            Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            int tc = SystemPerformance.AllTrades.Count;
            if (tc > lastTradeCount)
            {
                var last = SystemPerformance.AllTrades[tc - 1];
                if (last.ProfitCurrency < 0) consecutiveLosers++;
                else                         consecutiveLosers = 0;
                lastTradeCount = tc;
            }
        }

        // =====================================================================
        // MAIN BAR UPDATE
        // =====================================================================
        protected override void OnBarUpdate()
        {
            int minBars = Math.Max(DemaSlowPeriod, Math.Max(BollingerPeriod, HmmLookback)) + 4;
            if (CurrentBar < minBars) return;

            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers  = 0;
                leg1Hit = false; leg1JustHit = false;
                currentLeg2Qty = 1; activeLeg2 = "";
                prevDemaBullish = false;
                hmmProb0 = hmmProb1 = hmmProb2 = hmmProb3 = 0.25;
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }

            UpdateAdx();
            UpdateSuperTrend();
            UpdateRsi();
            UpdateMacd();
            UpdateHmm();

            double demaFast = GetDema(DemaFastPeriod, ref emaF1, ref emaF2);
            double demaSlow = GetDema(DemaSlowPeriod, ref emaS1, ref emaS2);
            bool demaBull   = demaFast > demaSlow;
            bool demaBear   = demaFast < demaSlow;

            // ── DEMA emergency exit ───────────────────────────────────────────
            if (Position.MarketPosition == MarketPosition.Long && demaBear && prevDemaBullish)
            {
                ExitLong("DemaReversalExit", "");
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
            }
            else if (Position.MarketPosition == MarketPosition.Short && demaBull && !prevDemaBullish)
            {
                ExitShort("DemaReversalExit", "");
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
            }

            prevDemaBullish = demaBull;

            // ── V1B Kill Switch ───────────────────────────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat && leg1Hit && IsKillSwitchActive())
            {
                if (Position.MarketPosition == MarketPosition.Long) ExitLong("KillSwitch", CEL2);
                else ExitShort("KillSwitch", CES2);
                leg1Hit = false; leg1JustHit = false; activeLeg2 = "";
                Print(string.Format("[CEM_V1B] KILL SWITCH at {0:F2}", Close[0]));
                return;
            }

            // ── Runner management ─────────────────────────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (!leg1Hit && Position.Quantity <= currentLeg2Qty && currentLeg2Qty > 0)
                {
                    leg1Hit = true; leg1JustHit = true;
                    double pivot = Position.MarketPosition == MarketPosition.Long
                        ? RT(Position.AveragePrice + 4 * TickSize)
                        : RT(Position.AveragePrice - 4 * TickSize);
                    if (activeLeg2 == CEL2) SetStopLoss(CEL2, CalculationMode.Price, pivot, false);
                    else if (activeLeg2 == CES2) SetStopLoss(CES2, CalculationMode.Price, pivot, false);
                }
                else if (leg1JustHit) leg1JustHit = false;
                return;
            }

            // ── Entry gates ───────────────────────────────────────────────────
            leg1Hit = false; leg1JustHit = false; currentLeg2Qty = 1; activeLeg2 = "";

            if (consecutiveLosers >= MaxConsecutiveLosses) return;
            if (!IsInTime()) return;

            if (DailyGoal > 0 || DailyLossLimit > 0)
            {
                double pnl = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit
                             - sessionStartProfit;
                if (DailyGoal > 0 && pnl >= DailyGoal) return;
                if (DailyLossLimit > 0 && pnl <= -DailyLossLimit) return;
            }

            if (adxValue <= AdxThreshold) return;

            double atrVal     = atrInd[0];
            double price      = Close[0];
            int    hmmSizePct = GetHmmSizePct();
            if (hmmSizePct <= 0) return;

            // ── LONG: all four gates + footprint ─────────────────────────────
            bool gB_long = demaBull;
            bool gC_long = stDirection == -1;
            bool gD_long = (Close[1] <= bb.Lower[1] && Close[0] > bb.Lower[0]) ||
                           (Close[2] <= bb.Lower[2] && Close[1] > bb.Lower[1] && Close[0] > bb.Lower[0]);
            bool rsiLong  = rsiValue > rsiMaValue && Close[1] > Open[1];
            bool macdLong = macdHist > macdHistPrev;

            if (gB_long && gC_long && gD_long && rsiLong && macdLong)
            {
                // V1B: bias and footprint
                if (!IsBiasCompatible(true)) goto TryShort;
                if (!IsFootprintEntryConfirmed()) goto TryShort;

                double stopPrice  = RT(price - atrVal * AtrStopMult);
                double leg1Target = RT(price + atrVal * Leg1TpMult);
                double leg2Target = RT(price + atrVal * Leg2TpMult);

                int sz = ScaleByConfidence(CalcMaxContracts(), hmmSizePct);
                int l1q = Math.Max(1, sz / 2);
                int l2q = Math.Max(1, sz - l1q);

                SetStopLoss(CEL1, CalculationMode.Price, stopPrice, false);
                SetStopLoss(CEL2, CalculationMode.Price, stopPrice, false);
                SetProfitTarget(CEL1, CalculationMode.Price, leg1Target);
                SetProfitTarget(CEL2, CalculationMode.Price, leg2Target);
                EnterLong(l1q, CEL1); EnterLong(l2q, CEL2);

                currentLeg2Qty = l2q; activeLeg2 = CEL2;

                Print(string.Format("[CEM_V1B] LONG | ADX:{0:F1} | ST:{1} | RSI:{2:F1} | " +
                    "MACD_H:{3:F4} | HMM%:{4} | Bias:{5} | FP:{6} | Qty:{7}+{8} | " +
                    "Stop:{9:F2} | T1:{10:F2} | T2:{11:F2}",
                    adxValue, stDirection, rsiValue, macdHist, hmmSizePct,
                    HUDMessengerV1B.CurrentDailyBias, RequireFootprintConfirmation ? "ON" : "OFF",
                    l1q, l2q, stopPrice, leg1Target, leg2Target));
                return;
            }

            TryShort:
            // ── SHORT: all four gates + footprint ────────────────────────────
            bool gB_short = demaBear;
            bool gC_short = stDirection == 1;
            bool gD_short = (Close[1] >= bb.Upper[1] && Close[0] < bb.Upper[0]) ||
                            (Close[2] >= bb.Upper[2] && Close[1] < bb.Upper[1] && Close[0] < bb.Upper[0]);
            bool rsiShort  = rsiValue < rsiMaValue && Close[1] < Open[1];
            bool macdShort = macdHist < macdHistPrev;

            if (gB_short && gC_short && gD_short && rsiShort && macdShort)
            {
                if (!IsBiasCompatible(false)) return;
                if (!IsFootprintEntryConfirmed()) return;

                double stopPrice  = RT(price + atrVal * AtrStopMult);
                double leg1Target = RT(price - atrVal * Leg1TpMult);
                double leg2Target = RT(price - atrVal * Leg2TpMult);

                int sz = ScaleByConfidence(CalcMaxContracts(), hmmSizePct);
                int l1q = Math.Max(1, sz / 2);
                int l2q = Math.Max(1, sz - l1q);

                SetStopLoss(CES1, CalculationMode.Price, stopPrice, false);
                SetStopLoss(CES2, CalculationMode.Price, stopPrice, false);
                SetProfitTarget(CES1, CalculationMode.Price, leg1Target);
                SetProfitTarget(CES2, CalculationMode.Price, leg2Target);
                EnterShort(l1q, CES1); EnterShort(l2q, CES2);

                currentLeg2Qty = l2q; activeLeg2 = CES2;

                Print(string.Format("[CEM_V1B] SHORT | ADX:{0:F1} | ST:{1} | RSI:{2:F1} | " +
                    "MACD_H:{3:F4} | HMM%:{4} | Bias:{5} | FP:{6} | Qty:{7}+{8} | " +
                    "Stop:{9:F2} | T1:{10:F2} | T2:{11:F2}",
                    adxValue, stDirection, rsiValue, macdHist, hmmSizePct,
                    HUDMessengerV1B.CurrentDailyBias, RequireFootprintConfirmation ? "ON" : "OFF",
                    l1q, l2q, stopPrice, leg1Target, leg2Target));
            }
        }
    }
}
