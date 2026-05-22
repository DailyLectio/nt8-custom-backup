// CC BY-NC 4.0
// CompositeEdge_Momentum.cs — Multi-Factor Confluence Trend-Following Strategy
// ─────────────────────────────────────────────────────────────────────────────
// REGIME ENGINE : Four-gate confluence system (all gates must agree on direction).
//   Gate A: ADX > threshold (directional energy present).
//   Gate B: DEMA(fast) vs DEMA(slow) crossover direction (macro trend bias).
//   Gate C: SuperTrend direction agrees with DEMA bias (momentum confirmation).
//   Gate D: Price recovering from BB lower (long) or rejecting BB upper (short).
//
// SECONDARY LAYER: HMM State Probabilities (position size modifier only).
//   Low Vol Trend prob > 60%  → full size (100%).
//   High Vol Chop prob > 50%  → half size (50%).
//   Crash prob > 30%          → full block (no entry).
//   Otherwise                 → reduced size (75%).
//
// TRADE TYPE    : Two-sided trend-following (momentum direction, not fade).
//   Long entry:  all four gates bullish + RSI crosses above its SMA + MACD expanding.
//   Short entry: all four gates bearish + RSI crosses below its SMA + MACD contracting.
//
// TWO-LEG EXIT STRUCTURE
//   Leg1: entry ± ATR × Leg1TpMult (configurable, default 1.5×).
//   Leg2: entry ± ATR × Leg2TpMult (configurable, default 2.5×).
//   After Leg1 fills: Leg2 stop pivots to breakeven + 4 ticks (free trade).
//   Emergency: DEMA macro bias reverses while in position → immediate flat.
//
// INSTRUMENT    : Agnostic. Optimized for NQ/MNQ; set TickValue accordingly.
// CHART TYPE    : 1-minute candles. Calculate.OnBarClose.
// ─────────────────────────────────────────────────────────────────────────────

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class CompositeEdge_Momentum : Strategy
    {
        // =====================================================================
        // PARAMETERS
        // =====================================================================

        // --- ADX ---
        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name = "ADX Period", GroupName = "1. Regime Gates", Order = 0)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name = "ADX Threshold (min for entry)", GroupName = "1. Regime Gates", Order = 1,
                 Description = "ADX must exceed this for directional entries. Default 20.")]
        public int AdxThreshold { get; set; } = 20;

        // --- DEMA ---
        [NinjaScriptProperty, Range(10, 500)]
        [Display(Name = "DEMA Fast Period", GroupName = "1. Regime Gates", Order = 2)]
        public int DemaFastPeriod { get; set; } = 100;

        [NinjaScriptProperty, Range(20, 1000)]
        [Display(Name = "DEMA Slow Period", GroupName = "1. Regime Gates", Order = 3)]
        public int DemaSlowPeriod { get; set; } = 200;

        // --- SuperTrend ---
        [NinjaScriptProperty, Range(0.1, 10.0)]
        [Display(Name = "SuperTrend Factor", GroupName = "1. Regime Gates", Order = 4)]
        public double SuperTrendFactor { get; set; } = 1.0;

        [NinjaScriptProperty, Range(3, 50)]
        [Display(Name = "SuperTrend ATR Period", GroupName = "1. Regime Gates", Order = 5)]
        public int SuperTrendAtrPeriod { get; set; } = 10;

        // --- Bollinger (Gate D) ---
        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name = "Bollinger Period", GroupName = "1. Regime Gates", Order = 6)]
        public int BollingerPeriod { get; set; } = 20;

        [NinjaScriptProperty, Range(0.5, 4.0)]
        [Display(Name = "Bollinger StdDev", GroupName = "1. Regime Gates", Order = 7)]
        public double BollingerDev { get; set; } = 2.0;

        // --- RSI / MACD entry signal ---
        [NinjaScriptProperty, Range(3, 50)]
        [Display(Name = "RSI Period", GroupName = "2. Entry Signal", Order = 0)]
        public int RsiPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(3, 50)]
        [Display(Name = "RSI MA Period", GroupName = "2. Entry Signal", Order = 1)]
        public int RsiMaPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(2, 50)]
        [Display(Name = "MACD Fast Period", GroupName = "2. Entry Signal", Order = 2)]
        public int MacdFast { get; set; } = 12;

        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "MACD Slow Period", GroupName = "2. Entry Signal", Order = 3)]
        public int MacdSlow { get; set; } = 26;

        [NinjaScriptProperty, Range(2, 50)]
        [Display(Name = "MACD Signal Period", GroupName = "2. Entry Signal", Order = 4)]
        public int MacdSignal { get; set; } = 9;

        // --- HMM ---
        [NinjaScriptProperty, Range(10, 200)]
        [Display(Name = "HMM Lookback", GroupName = "3. HMM Sizing", Order = 0)]
        public int HmmLookback { get; set; } = 50;

        [NinjaScriptProperty, Range(0.01, 1.0)]
        [Display(Name = "HMM Learning Rate", GroupName = "3. HMM Sizing", Order = 1)]
        public double HmmLearningRate { get; set; } = 0.10;

        // --- Risk ---
        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "ATR Period (for stops/targets)", GroupName = "4. Risk", Order = 0)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.5, 5.0)]
        [Display(Name = "ATR Stop Multiplier", GroupName = "4. Risk", Order = 1)]
        public double AtrStopMult { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0.5, 10.0)]
        [Display(Name = "Leg1 TP Multiplier (ATR)", GroupName = "4. Risk", Order = 2,
                 Description = "First profit target = entry ± ATR × this. Default 1.5.")]
        public double Leg1TpMult { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1.0, 15.0)]
        [Display(Name = "Leg2 TP Multiplier (ATR)", GroupName = "4. Risk", Order = 3,
                 Description = "Second profit target (runner) = entry ± ATR × this. Default 2.5.")]
        public double Leg2TpMult { get; set; } = 2.5;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5.00  ES=12.50  MNQ=0.50  MES=1.25",
                 GroupName = "4. Risk", Order = 4)]
        public double TickValueDollars { get; set; } = 5.00;

        // --- Guards ---
        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "5. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0=off)", GroupName = "5. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0=off)", GroupName = "5. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

        // --- Time ---
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "6. Time", Order = 0)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Start Time (HHmmss)", GroupName = "6. Time", Order = 1)]
        public int StartTime { get; set; } = 103500;

        [NinjaScriptProperty]
        [Display(Name = "End Time (HHmmss)", GroupName = "6. Time", Order = 2)]
        public int EndTime { get; set; } = 155500;

        // --- Entry Score (Phase 1 renovation, 2026-05-22 — see dev-log TL-04) ---
        // The original entry required all 6 gates as a hard AND, which never fired
        // (zero trades). Phase 1 converts the 5 confirmation gates into a weighted
        // 0-100 score; ADX>floor and the HMM crash-block remain HARD pre-filters.
        // Enter when score >= threshold; position is also SIZED by the score
        // (effective size = min(HMM size pct, score)). Weights are tunable for
        // calibration. Phase 2 (regimeConfidence/HMM blend) is deferred.
        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Entry Score Threshold (0-100)", GroupName = "7. Entry Score", Order = 0,
                 Description = "Minimum weighted confirmation score to enter. Default 50.")]
        public int EntryScoreThreshold { get; set; } = 50;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Weight: BB Recovery/Rejection", GroupName = "7. Entry Score", Order = 1)]
        public int WeightBbRecovery { get; set; } = 25;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Weight: RSI Cross", GroupName = "7. Entry Score", Order = 2)]
        public int WeightRsiCross { get; set; } = 25;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Weight: MACD Histogram Expanding", GroupName = "7. Entry Score", Order = 3)]
        public int WeightMacd { get; set; } = 20;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Weight: DEMA Trend", GroupName = "7. Entry Score", Order = 4)]
        public int WeightDema { get; set; } = 20;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Weight: SuperTrend", GroupName = "7. Entry Score", Order = 5)]
        public int WeightSuperTrend { get; set; } = 10;

        // =====================================================================
        // INDICATORS
        // =====================================================================
        private ATR       atrInd;
        private Bollinger bb;

        // =====================================================================
        // ADX STATE (Wilder smoothing, manual)
        // =====================================================================
        private double smoothedTR   = 0;
        private double smoothedDMp  = 0;
        private double smoothedDMm  = 0;
        private double adxValue     = 0;
        private double dxSum        = 0;
        private int    dxCount      = 0;

        // =====================================================================
        // DEMA STATE
        // =====================================================================
        // Fast DEMA
        private double emaF1 = double.NaN;
        private double emaF2 = double.NaN;
        // Slow DEMA
        private double emaS1 = double.NaN;
        private double emaS2 = double.NaN;

        // =====================================================================
        // SUPERTREND STATE
        // =====================================================================
        private double stAtr          = 0;
        private int    stAtrCount     = 0;
        private double finalUpperST   = double.NaN;
        private double finalLowerST   = double.NaN;
        private int    stDirection    = 0; // -1=bullish, 1=bearish, 0=unknown

        // =====================================================================
        // RSI STATE (Wilder smoothing)
        // =====================================================================
        private double rsiAvgUp    = 0;
        private double rsiAvgDown  = 0;
        private double rsiValue    = 50;
        private double rsiMaValue  = 50;
        // RSI MA as simple rolling sum
        private double[] rsiBuffer;
        private int      rsiBufferIdx = 0;
        private double   rsiBufferSum = 0;
        private int      rsiBufferCount = 0;

        // =====================================================================
        // MACD STATE
        // =====================================================================
        private double macdFastEma  = double.NaN;
        private double macdSlowEma  = double.NaN;
        private double macdSignalEma = double.NaN;
        private double macdHist     = 0;
        private double macdHistPrev = 0;

        // =====================================================================
        // HMM STATE
        // =====================================================================
        private double hmmProb0 = 0.25; // Low Vol Trend
        private double hmmProb1 = 0.25; // High Vol Chop
        private double hmmProb2 = 0.25; // Crash
        private double hmmProb3 = 0.25; // Accumulation

        // Transition matrix (persistence on diagonal)
        private const double P00 = 0.90, P01 = 0.05, P02 = 0.02, P03 = 0.03;
        private const double P10 = 0.10, P11 = 0.80, P12 = 0.05, P13 = 0.05;
        private const double P20 = 0.00, P21 = 0.10, P22 = 0.85, P23 = 0.05;
        private const double P30 = 0.10, P31 = 0.05, P32 = 0.00, P33 = 0.85;

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
        private bool   isLongTrade    = false;

        // Track previous DEMA state for emergency exit detection
        private bool prevDemaBullish = false;

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
            double atrVal = atrInd[0];
            if (atrVal <= 0) return 1;
            double dollarRisk = (atrVal * AtrStopMult) / TickSize * TickValueDollars;
            if (dollarRisk <= 0) return 1;
            return Math.Max(1, (int)(1500.0 / dollarRisk));
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
        {
            return Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));
        }

        // EMA helper
        private double EmaUpdate(double prev, double price, int period)
        {
            double k = 2.0 / (period + 1);
            return double.IsNaN(prev) ? price : prev + k * (price - prev);
        }

        // =====================================================================
        // INDICATOR UPDATES (called each bar)
        // =====================================================================

        private void UpdateAdx()
        {
            if (CurrentBar < 1) return;
            double high = High[0], low = Low[0], prevClose = Close[1];
            double prevHigh = High[1], prevLow = Low[1];

            double tr  = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
            double dmp = (high - prevHigh > prevLow - low) ? Math.Max(high - prevHigh, 0) : 0;
            double dmm = (prevLow - low > high - prevHigh) ? Math.Max(prevLow - low, 0) : 0;

            if (smoothedTR == 0)
            {
                smoothedTR  = tr;
                smoothedDMp = dmp;
                smoothedDMm = dmm;
            }
            else
            {
                smoothedTR  = smoothedTR  - smoothedTR  / AdxPeriod + tr;
                smoothedDMp = smoothedDMp - smoothedDMp / AdxPeriod + dmp;
                smoothedDMm = smoothedDMm - smoothedDMm / AdxPeriod + dmm;
            }

            if (smoothedTR <= 0) return;
            double diPlus  = smoothedDMp / smoothedTR * 100;
            double diMinus = smoothedDMm / smoothedTR * 100;
            double denom   = diPlus + diMinus;
            if (denom <= 0) return;

            double dx = Math.Abs(diPlus - diMinus) / denom * 100;

            // Wilder SMA for ADX
            dxCount++;
            if (dxCount <= AdxPeriod)
            {
                dxSum += dx;
                adxValue = dxSum / dxCount;
            }
            else
            {
                adxValue = (adxValue * (AdxPeriod - 1) + dx) / AdxPeriod;
            }
        }

        private double GetDema(int period, ref double e1, ref double e2)
        {
            double k = 2.0 / (period + 1);
            if (double.IsNaN(e1)) { e1 = Close[0]; e2 = Close[0]; }
            else
            {
                e1 = e1 + k * (Close[0] - e1);
                e2 = e2 + k * (e1 - e2);
            }
            return 2 * e1 - e2;
        }

        private void UpdateSuperTrend()
        {
            if (CurrentBar < 1) return;

            // Wilder ATR for SuperTrend
            double tr = Math.Max(High[0] - Low[0],
                        Math.Max(Math.Abs(High[0] - Close[1]),
                                 Math.Abs(Low[0] - Close[1])));
            stAtrCount++;
            if (stAtrCount <= SuperTrendAtrPeriod)
                stAtr = stAtr + (tr - stAtr) / stAtrCount;
            else
                stAtr = (stAtr * (SuperTrendAtrPeriod - 1) + tr) / SuperTrendAtrPeriod;

            double hl2         = (High[0] + Low[0]) / 2.0;
            double upperBand   = hl2 + SuperTrendFactor * stAtr;
            double lowerBand   = hl2 - SuperTrendFactor * stAtr;

            if (double.IsNaN(finalUpperST))
            {
                finalUpperST = upperBand;
                finalLowerST = lowerBand;
                stDirection  = 0;
                return;
            }

            double prevClose = Close[1];

            // Tighten bands (only move in favorable direction)
            finalUpperST = (upperBand < finalUpperST || prevClose > finalUpperST)
                           ? upperBand : finalUpperST;
            finalLowerST = (lowerBand > finalLowerST || prevClose < finalLowerST)
                           ? lowerBand : finalLowerST;

            // Direction flip
            if (Close[0] > finalUpperST) stDirection = -1; // bullish
            else if (Close[0] < finalLowerST) stDirection = 1; // bearish
        }

        private void UpdateRsi()
        {
            if (CurrentBar < 1) return;
            double change = Close[0] - Close[1];
            double gain   = change > 0 ? change : 0;
            double loss   = change < 0 ? -change : 0;

            if (rsiBufferCount < RsiPeriod)
            {
                rsiAvgUp   = (rsiAvgUp   * rsiBufferCount + gain) / (rsiBufferCount + 1);
                rsiAvgDown = (rsiAvgDown * rsiBufferCount + loss) / (rsiBufferCount + 1);
                rsiBufferCount++;
            }
            else
            {
                rsiAvgUp   = (rsiAvgUp   * (RsiPeriod - 1) + gain) / RsiPeriod;
                rsiAvgDown = (rsiAvgDown * (RsiPeriod - 1) + loss) / RsiPeriod;
            }

            rsiValue = rsiAvgDown == 0 ? 100 : rsiAvgUp == 0 ? 0 : 100 - 100 / (1 + rsiAvgUp / rsiAvgDown);

            // RSI MA as SMA using circular buffer
            if (rsiBuffer == null) rsiBuffer = new double[RsiMaPeriod];
            rsiBufferSum -= rsiBuffer[rsiBufferIdx];
            rsiBuffer[rsiBufferIdx] = rsiValue;
            rsiBufferSum += rsiValue;
            rsiBufferIdx  = (rsiBufferIdx + 1) % RsiMaPeriod;
            int bufFill   = Math.Min(CurrentBar + 1, RsiMaPeriod);
            rsiMaValue    = rsiBufferSum / bufFill;
        }

        private void UpdateMacd()
        {
            if (double.IsNaN(macdFastEma)) macdFastEma = Close[0];
            if (double.IsNaN(macdSlowEma)) macdSlowEma = Close[0];

            macdFastEma = EmaUpdate(macdFastEma, Close[0], MacdFast);
            macdSlowEma = EmaUpdate(macdSlowEma, Close[0], MacdSlow);

            double macdLine = macdFastEma - macdSlowEma;

            if (double.IsNaN(macdSignalEma)) macdSignalEma = macdLine;
            else macdSignalEma = EmaUpdate(macdSignalEma, macdLine, MacdSignal);

            macdHistPrev = macdHist;
            macdHist     = macdLine - macdSignalEma;
        }

        private void UpdateHmm()
        {
            if (CurrentBar < HmmLookback + 1) return;

            // Log returns and volatility over lookback
            double sumLR = 0, sumLR2 = 0;
            for (int i = 0; i < HmmLookback; i++)
            {
                if (Close[i + 1] <= 0) continue;
                double lr = Math.Log(Close[i] / Close[i + 1]);
                sumLR  += lr;
                sumLR2 += lr * lr;
            }
            double meanLR  = sumLR / HmmLookback;
            double varLR   = sumLR2 / HmmLookback - meanLR * meanLR;
            double volLR   = varLR > 0 ? Math.Sqrt(varLR) : 1e-9;

            double logRet   = Close[1] > 0 ? Math.Log(Close[0] / Close[1]) : 0;
            double normRet  = (logRet - meanLR) / (volLR + 1e-9);

            // Normalized vol: current vol vs. average vol over lookback
            double sumVol = 0;
            for (int i = 0; i < HmmLookback; i++)
            {
                if (Close[i + 1] <= 0) continue;
                double lr = Math.Log(Close[i] / Close[i + 1]);
                sumVol += Math.Abs(lr);
            }
            double avgVol  = sumVol / HmmLookback;
            double normVol = avgVol > 1e-9 ? volLR / avgVol : 1.0;

            // Emission likelihoods
            double em0 = Math.Exp(-Math.Pow(normVol - 0.7, 2) / 0.5)
                       * Math.Exp(-Math.Pow(normRet - 0.5, 2) / 2.0);
            double em1 = Math.Exp(-Math.Pow(normVol - 1.5, 2) / 1.0)
                       * Math.Exp(-(normRet * normRet) / 0.5);
            double em2 = Math.Exp(-Math.Pow(normVol - 2.0, 2) / 1.0)
                       * (normRet < -1.5 ? 1.0 : Math.Exp(-Math.Pow(normRet + 2.0, 2) / 1.0));
            double em3 = Math.Exp(-Math.Pow(normVol - 0.6, 2) / 0.5)
                       * Math.Exp(-Math.Pow(normRet + 0.2, 2) / 1.0);

            // Prediction step (transition matrix × current probs)
            double prior0 = hmmProb0 * P00 + hmmProb1 * P10 + hmmProb2 * P20 + hmmProb3 * P30;
            double prior1 = hmmProb0 * P01 + hmmProb1 * P11 + hmmProb2 * P21 + hmmProb3 * P31;
            double prior2 = hmmProb0 * P02 + hmmProb1 * P12 + hmmProb2 * P22 + hmmProb3 * P32;
            double prior3 = hmmProb0 * P03 + hmmProb1 * P13 + hmmProb2 * P23 + hmmProb3 * P33;

            // Update step
            double u0 = em0 * prior0;
            double u1 = em1 * prior1;
            double u2 = em2 * prior2;
            double u3 = em3 * prior3;

            double sumU = u0 + u1 + u2 + u3;
            if (sumU <= 0) return;

            double lr_rate = HmmLearningRate;
            hmmProb0 = (1 - lr_rate) * hmmProb0 + lr_rate * (u0 / sumU);
            hmmProb1 = (1 - lr_rate) * hmmProb1 + lr_rate * (u1 / sumU);
            hmmProb2 = (1 - lr_rate) * hmmProb2 + lr_rate * (u2 / sumU);
            hmmProb3 = (1 - lr_rate) * hmmProb3 + lr_rate * (u3 / sumU);

            // Renormalize
            double total = hmmProb0 + hmmProb1 + hmmProb2 + hmmProb3;
            if (total > 0)
            {
                hmmProb0 /= total;
                hmmProb1 /= total;
                hmmProb2 /= total;
                hmmProb3 /= total;
            }
        }

        // Derive HMM size modifier
        private int GetHmmSizePct()
        {
            if (hmmProb2 > 0.30) return 0;     // Crash regime — full block
            if (hmmProb1 > 0.50) return 50;    // High vol chop — half size
            if (hmmProb0 > 0.60) return 100;   // Low vol trend — full size
            return 75;                          // Uncertain — reduced size
        }

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "CompositeEdge_Momentum";
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

        // =====================================================================
        // CONSECUTIVE LOSER TRACKING
        // =====================================================================
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

            // Session open reset
            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers  = 0;
                leg1Hit            = false;
                leg1JustHit        = false;
                currentLeg2Qty     = 1;
                activeLeg2         = "";
                prevDemaBullish    = false;
                hmmProb0 = hmmProb1 = hmmProb2 = hmmProb3 = 0.25;
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }

            // Update all indicator states each bar
            UpdateAdx();
            UpdateSuperTrend();
            UpdateRsi();
            UpdateMacd();
            UpdateHmm();

            double demaFast  = GetDema(DemaFastPeriod, ref emaF1, ref emaF2);
            double demaSlow  = GetDema(DemaSlowPeriod, ref emaS1, ref emaS2);
            bool   demaBull  = demaFast > demaSlow;
            bool   demaBear  = demaFast < demaSlow;

            // ==================================================================
            // EMERGENCY EXIT: DEMA macro bias reversed while in position
            // ==================================================================
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

            // ==================================================================
            // RUNNER MANAGEMENT
            // ==================================================================
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (!leg1Hit && Position.Quantity <= currentLeg2Qty && currentLeg2Qty > 0)
                {
                    leg1Hit     = true;
                    leg1JustHit = true;

                    double pivot = Position.MarketPosition == MarketPosition.Long
                        ? RT(Position.AveragePrice + 4 * TickSize)
                        : RT(Position.AveragePrice - 4 * TickSize);

                    if (activeLeg2 == CEL2)
                        SetStopLoss(CEL2, CalculationMode.Price, pivot, false);
                    else if (activeLeg2 == CES2)
                        SetStopLoss(CES2, CalculationMode.Price, pivot, false);
                }
                else if (leg1JustHit)
                {
                    leg1JustHit = false;
                }

                return;
            }

            // ==================================================================
            // ENTRY GATES
            // ==================================================================
            leg1Hit = false; leg1JustHit = false; currentLeg2Qty = 1; activeLeg2 = "";

            // Gate 1: circuit breaker
            if (consecutiveLosers >= MaxConsecutiveLosses) return;

            // Gate 2: time filter
            if (!IsInTime()) return;

            // Gate 3: daily P&L guard
            if (DailyGoal > 0 || DailyLossLimit > 0)
            {
                double dailyPnL = SystemPerformance.AllTrades
                                      .TradesPerformance.Currency.CumProfit
                                  - sessionStartProfit;
                if (DailyGoal     > 0 && dailyPnL >=  DailyGoal)      return;
                if (DailyLossLimit > 0 && dailyPnL <= -DailyLossLimit) return;
            }

            // Gate A: ADX directional energy
            if (adxValue <= AdxThreshold) return;

            // Gate B + C + D evaluated per direction
            double atrVal    = atrInd[0];
            double price     = Close[0];
            int    hmmSizePct = GetHmmSizePct();
            if (hmmSizePct <= 0) return; // HMM crash block

            // ==================================================================
            // LONG: Gate B (dema bullish) + Gate C (ST bullish) + Gate D (BB lower recovery)
            // ==================================================================
            bool gateB_long = demaBull;
            bool gateC_long = stDirection == -1;
            // Gate D: close crossed above BB lower within last 2 bars
            bool gateD_long = (Close[1] <= bb.Lower[1] && Close[0] > bb.Lower[0]) ||
                              (Close[2] <= bb.Lower[2] && Close[1] > bb.Lower[1] && Close[0] > bb.Lower[0]);

            // Entry signal: RSI crosses above its MA + MACD histogram expanding
            bool rsiLongCross = rsiValue > rsiMaValue && (rsiValue - rsiMaValue) > 0 &&
                                Close[1] > Open[1]; // previous bar confirms direction
            bool macdLongSignal = macdHist > macdHistPrev;

            // Phase 1 (2026-05-22): weighted confirmation score replaces the 6-way hard AND.
            // ADX>floor (Gate A above) and the HMM crash-block (hmmSizePct>0 above) stay HARD.
            int longScore = (gateD_long      ? WeightBbRecovery  : 0)
                          + (rsiLongCross    ? WeightRsiCross    : 0)
                          + (macdLongSignal  ? WeightMacd        : 0)
                          + (gateB_long      ? WeightDema        : 0)
                          + (gateC_long      ? WeightSuperTrend  : 0);

            if (longScore >= EntryScoreThreshold)
            {
                double stopPrice  = RT(price - atrVal * AtrStopMult);
                double leg1Target = RT(price + atrVal * Leg1TpMult);
                double leg2Target = RT(price + atrVal * Leg2TpMult);

                int maxC    = CalcMaxContracts();
                // Size by the score, capped by the HMM size pct (high score + good HMM = full size).
                int sizePct = Math.Min(hmmSizePct, longScore);
                int sz      = ScaleByConfidence(maxC, sizePct);
                int leg1Qty = Math.Max(1, sz / 2);
                int leg2Qty = Math.Max(1, sz - leg1Qty);

                SetStopLoss(CEL1, CalculationMode.Price, stopPrice, false);
                SetStopLoss(CEL2, CalculationMode.Price, stopPrice, false);
                SetProfitTarget(CEL1, CalculationMode.Price, leg1Target);
                SetProfitTarget(CEL2, CalculationMode.Price, leg2Target);
                EnterLong(leg1Qty, CEL1);
                EnterLong(leg2Qty, CEL2);

                currentLeg2Qty = leg2Qty;
                activeLeg2     = CEL2;
                isLongTrade    = true;

                Print(string.Format(
                    "[CompositeEdge_Momentum] LONG | ADX:{0:F1} | DEMA:{1:F2}>{2:F2} | " +
                    "ST:{3} | RSI:{4:F1} | MACD_H:{5:F4} | HMM_Pct:{6} | " +
                    "Qty:{7}+{8} | Stop:{9:F2} | T1:{10:F2} | T2:{11:F2}",
                    adxValue, demaFast, demaSlow,
                    stDirection, rsiValue, macdHist, hmmSizePct,
                    leg1Qty, leg2Qty, stopPrice, leg1Target, leg2Target));
                return;
            }

            // ==================================================================
            // SHORT: Gate B (dema bearish) + Gate C (ST bearish) + Gate D (BB upper rejection)
            // ==================================================================
            bool gateB_short = demaBear;
            bool gateC_short = stDirection == 1;
            bool gateD_short = (Close[1] >= bb.Upper[1] && Close[0] < bb.Upper[0]) ||
                               (Close[2] >= bb.Upper[2] && Close[1] < bb.Upper[1] && Close[0] < bb.Upper[0]);

            bool rsiShortCross  = rsiValue < rsiMaValue && (rsiMaValue - rsiValue) > 0 &&
                                  Close[1] < Open[1];
            bool macdShortSignal = macdHist < macdHistPrev;

            // Phase 1 (2026-05-22): weighted confirmation score (mirror of LONG).
            int shortScore = (gateD_short     ? WeightBbRecovery  : 0)
                           + (rsiShortCross   ? WeightRsiCross    : 0)
                           + (macdShortSignal ? WeightMacd        : 0)
                           + (gateB_short     ? WeightDema        : 0)
                           + (gateC_short     ? WeightSuperTrend  : 0);

            if (shortScore >= EntryScoreThreshold)
            {
                double stopPrice  = RT(price + atrVal * AtrStopMult);
                double leg1Target = RT(price - atrVal * Leg1TpMult);
                double leg2Target = RT(price - atrVal * Leg2TpMult);

                int maxC    = CalcMaxContracts();
                int sizePct = Math.Min(hmmSizePct, shortScore);
                int sz      = ScaleByConfidence(maxC, sizePct);
                int leg1Qty = Math.Max(1, sz / 2);
                int leg2Qty = Math.Max(1, sz - leg1Qty);

                SetStopLoss(CES1, CalculationMode.Price, stopPrice, false);
                SetStopLoss(CES2, CalculationMode.Price, stopPrice, false);
                SetProfitTarget(CES1, CalculationMode.Price, leg1Target);
                SetProfitTarget(CES2, CalculationMode.Price, leg2Target);
                EnterShort(leg1Qty, CES1);
                EnterShort(leg2Qty, CES2);

                currentLeg2Qty = leg2Qty;
                activeLeg2     = CES2;
                isLongTrade    = false;

                Print(string.Format(
                    "[CompositeEdge_Momentum] SHORT | ADX:{0:F1} | DEMA:{1:F2}<{2:F2} | " +
                    "ST:{3} | RSI:{4:F1} | MACD_H:{5:F4} | HMM_Pct:{6} | " +
                    "Qty:{7}+{8} | Stop:{9:F2} | T1:{10:F2} | T2:{11:F2}",
                    adxValue, demaFast, demaSlow,
                    stDirection, rsiValue, macdHist, hmmSizePct,
                    leg1Qty, leg2Qty, stopPrice, leg1Target, leg2Target));
            }
        }
    }
}