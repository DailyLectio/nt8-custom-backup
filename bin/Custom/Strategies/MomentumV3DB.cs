// CC BY-NC 4.0
// Momentum_V3D_B.cs  — V3D Institutional Regime Matrix  — Version B
// ─────────────────────────────────────────────────────────────────
// A/B TEST PURPOSE
//   Version A is single-leg: one entry, fixed ATR stop, fixed RiskReward target,
//   slope exit may close early. Clean and consistent.
//
//   Version B is two-leg with confidence-scaled runner:
//     Leg1 — fires on every valid signal. Closer target (Leg1TargetMult × ATR, default 0.75).
//             Provides quick profit capture and converts trade to near free-risk.
//     Leg2 — fires ONLY when RegimeConfidence >= ConfidenceScaleThreshold (default 80).
//             If confidence is below threshold, only Leg1 is active.
//             Leg2 target = full RiskReward ATR target.
//             After Leg1 fills: Leg2 stop moves to BE+4 ticks (free-trade pivot).
//             Slope exit and circuit breaker still manage Leg2.
//
//   This implements the spec design brief from Section 10:
//   "SizePct scaling: Leg2 contracts = confidence-scaled addition when RegimeConfidence >= 80"
//
//   EXPECTED TRADEOFF vs Version A:
//     When confidence >= threshold: higher expectancy per signal if Leg2 runners
//     frequently hold to the full target. Higher variance per session.
//     When confidence < threshold: identical to Version A (single-leg).
//     The test directly measures whether confidence >= 80 genuinely predicts better
//     trade outcomes that justify the additional runner contract.
//
//   TEST DURATION: 4–6 weeks.
//   KEY COMPARISON: Leg2 average outcome on high-confidence days. If Leg2 win rate
//   is materially below Leg1 win rate, the confidence threshold is not predictive.
//   If Leg2 win rate matches or exceeds Leg1, the multi-leg architecture is justified.
//
// ALL FIXES FROM VERSION A ARE INCLUDED:
//   circuitBreakerFiredThisBar flag, hystFailCount session reset, SizePct fallback
//   removed, StopCancelClose, ConflictScore gate, RegimeConfidence gate,
//   Daily P&L guard, ReasonCode + diagnostic Print.
// ─────────────────────────────────────────────────────────────────

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Momentum_V3D_B : Strategy
    {
        public enum ExitSlopeMode { None, Simple, Hysteresis }

        // =====================================================================
        // PARAMETERS  (Version A params + Leg2 additions)
        // =====================================================================

        [NinjaScriptProperty]
        [Display(Name = "Data Folder Path", GroupName = "1. Regime", Order = 0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D";

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "ATR Stop Multiplier", GroupName = "2. Risk", Order = 0)]
        public double AtrStopMult { get; set; } = 0.75;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", GroupName = "2. Risk", Order = 1)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "Min Stop (ticks)", GroupName = "2. Risk", Order = 2)]
        public int MinStopTicks { get; set; } = 4;

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "Leg1 Target Multiplier (ATR)",
                 Description = "Leg1 quick target. Closer than full RiskReward.",
                 GroupName = "2. Risk", Order = 3)]
        public double Leg1TargetMult { get; set; } = 0.75;

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "Leg2 Risk:Reward (full target)",
                 Description = "Leg2 runner target. Only active when RegimeConfidence >= threshold.",
                 GroupName = "2. Risk", Order = 4)]
        public double RiskReward { get; set; } = 1.5;

        [NinjaScriptProperty, Range(50, 100)]
        [Display(Name = "Confidence Scale Threshold",
                 Description = "Leg2 fires only when RegimeConfidence >= this. Default 80.",
                 GroupName = "2. Risk", Order = 5)]
        public int ConfidenceScaleThreshold { get; set; } = 80;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5.00  ES=12.50  MNQ=0.50  MES=1.25",
                 GroupName = "2. Risk", Order = 6)]
        public double TickValueDollars { get; set; } = 5.00;

        [NinjaScriptProperty, Range(2, 200)]
        [Display(Name = "CI Period", GroupName = "3. Signal", Order = 0)]
        public int CiPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ADX Period", GroupName = "3. Signal", Order = 1)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "CI Max — Compression", GroupName = "3. Signal", Order = 2)]
        public double CiMaxCompression { get; set; } = 58.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "CI Max — Expansion", GroupName = "3. Signal", Order = 3)]
        public double CiMaxExpansion { get; set; } = 50.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "ADX Entry Threshold", GroupName = "3. Signal", Order = 4)]
        public double AdxEntryThreshold { get; set; } = 18.0;

        [NinjaScriptProperty, Range(0.0, 5.0)]
        [Display(Name = "Velocity ADX Relief Threshold", GroupName = "3. Signal", Order = 5)]
        public double VelocityReliefThreshold { get; set; } = 0.85;

        [NinjaScriptProperty, Range(50, 100)]
        [Display(Name = "Min Regime Confidence", GroupName = "3. Signal", Order = 6)]
        public int MinConfidence { get; set; } = 65;

        [NinjaScriptProperty]
        [Display(Name = "Slope Exit Mode", GroupName = "4. Exit", Order = 0)]
        public ExitSlopeMode SlopeExit { get; set; } = ExitSlopeMode.Hysteresis;

        [NinjaScriptProperty, Range(0, 1000)]
        [Display(Name = "Min Hold Bars", GroupName = "4. Exit", Order = 1)]
        public int MinHoldBars { get; set; } = 2;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "CI Slope Bars (Simple)", GroupName = "4. Exit", Order = 2)]
        public int CiSlopeBarsExit { get; set; } = 5;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "ADX Slope Bars (Simple)", GroupName = "4. Exit", Order = 3)]
        public int AdxSlopeBarsExit { get; set; } = 5;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "Min CI Rise (Simple)", GroupName = "4. Exit", Order = 4)]
        public double CiRiseMinExit { get; set; } = 2.0;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "Min ADX Drop (Simple)", GroupName = "4. Exit", Order = 5)]
        public double AdxDropMinExit { get; set; } = 2.0;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "Hysteresis Consecutive Fails", GroupName = "4. Exit", Order = 6)]
        public int HystConsecutive { get; set; } = 2;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "CI Rise Per Bar (Hysteresis)", GroupName = "4. Exit", Order = 7)]
        public double HystCiRisePerBar { get; set; } = 0.5;

        [NinjaScriptProperty, Range(0.0, 100.0)]
        [Display(Name = "ADX Drop Per Bar (Hysteresis)", GroupName = "4. Exit", Order = 8)]
        public double HystAdxDropPerBar { get; set; } = 0.5;

        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "5. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0 = off)", GroupName = "5. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0 = off)", GroupName = "5. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "6. Time", Order = 0)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Start Time (HHmmss)", GroupName = "6. Time", Order = 1)]
        public int StartTime { get; set; } = 93500;

        [NinjaScriptProperty]
        [Display(Name = "End Time (HHmmss)", GroupName = "6. Time", Order = 2)]
        public int EndTime { get; set; } = 155500;

        // =====================================================================
        // REGIME STATE  (identical to Version A)
        // =====================================================================
        private string   matrixFile         = "";
        private string   leaderSymbol       = "";
        private DateTime lastFileWriteUtc   = DateTime.MinValue;
        private DateTime lastFileCheck      = DateTime.MinValue;
        private const int MinCheckSeconds   = 15;
        private FileSystemWatcher regimeWatcher;
        private readonly object   fileLock  = new object();

        private volatile string finalRegime      = "UNKNOWN";
        private volatile string finalDirection   = "UNKNOWN";
        private volatile string reasonCode       = "";
        private volatile string phase            = "UNKNOWN";
        private volatile int    regimeConfidence = 0;
        private          double conflictScore    = 1.0;
        private volatile bool   allowLong        = false;
        private volatile bool   allowShort       = false;
        private volatile int    momoSizePct      = 0;
        private volatile bool   staleDataFlag    = true;
        private          double velocity3P       = 0.0;
        private volatile int    stateAgeBars     = 0;
        private volatile bool   parseFailed      = true;
        private volatile int    suggestedAdxMin  = 18;

        private Dictionary<string, int> headerIdx = new Dictionary<string, int>();

        // =====================================================================
        // INDICATORS
        // =====================================================================
        private ATR atrStop;
        private ADX adx;

        private Series<double> trSeries;
        private SUM  sumTr;
        private MAX  maxHigh;
        private MIN  minLow;
        private Series<double> ci;

        private Series<double> dmPlus, dmMinus;
        private Series<double> sumDmPlus, sumDmMinus, sumTrDI;
        private Series<double> diPlusSeries, diMinusSeries;

        // =====================================================================
        // RUNTIME STATE
        // =====================================================================
        private int    consecutiveLosers          = 0;
        private int    lastTradeCount             = 0;
        private int    hystFailCount              = 0;
        private bool   circuitBreakerFiredThisBar = false;
        private string lastRegimeSeen             = "";
        private double sessionStartProfit         = 0;

        // VERSION B: Leg2 runner state
        private bool   leg1Hit        = false;
        private int    currentLeg2Qty = 0;      // 0 = no Leg2 active this trade

        // =====================================================================
        // HELPERS
        // =====================================================================
        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        private string GetLeaderSymbol(string sym)
        {
            if (string.IsNullOrEmpty(sym)) return sym;
            sym = sym.Trim().ToUpper();
            if (sym == "MES") return "ES";
            if (sym == "MNQ") return "NQ";
            if (sym == "MGC") return "GC";
            if (sym == "MCL") return "CL";
            return sym;
        }

        private bool IsInTime()
        {
            if (!EnableTimeFilter) return true;
            int t = ToTime(Time[0]);
            return t >= StartTime && t <= EndTime;
        }

        private int CalcMaxContracts()
        {
            double atrVal = atrStop[0];
            if (atrVal <= 0) return 1;
            double dollarRisk = (atrVal * AtrStopMult) / TickSize * TickValueDollars;
            if (dollarRisk <= 0) return 1;
            return Math.Max(1, (int)(1500.0 / dollarRisk));
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
        {
            return Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));
        }

        private bool IsRegimeAllowed()
        {
            if (parseFailed || staleDataFlag) return false;
            return finalRegime == "TREND_COMPRESSION" || finalRegime == "TREND_EXPANSION";
        }

        private double ActiveCiMax()
        {
            return finalRegime == "TREND_EXPANSION" ? CiMaxExpansion : CiMaxCompression;
        }

        private double ActiveAdxFloor()
        {
            double floor = Math.Max(AdxEntryThreshold,
                           suggestedAdxMin > 0 ? (double)suggestedAdxMin : AdxEntryThreshold);
            if (Math.Abs(velocity3P) >= VelocityReliefThreshold) floor -= 2.0;
            return floor;
        }

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "Momentum_V3D_B";
                Calculate                    = Calculate.OnPriceChange;
                EntriesPerDirection          = 2;   // Leg1 + Leg2
                EntryHandling                = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                RealtimeErrorHandling        = RealtimeErrorHandling.StopCancelClose;
                TraceOrders                  = false;
            }
            else if (State == State.DataLoaded)
            {
                leaderSymbol = GetLeaderSymbol(Instrument.MasterInstrument.Name);
                matrixFile   = Path.Combine(DataFolderPath, leaderSymbol + "_RegimeMatrix_Latest.csv");

                adx     = ADX(AdxPeriod);
                atrStop = ATR(AtrPeriod);

                trSeries = new Series<double>(this);
                sumTr    = SUM(trSeries, CiPeriod);
                maxHigh  = MAX(High, CiPeriod);
                minLow   = MIN(Low,  CiPeriod);
                ci       = new Series<double>(this);

                dmPlus        = new Series<double>(this);
                dmMinus       = new Series<double>(this);
                sumDmPlus     = new Series<double>(this);
                sumDmMinus    = new Series<double>(this);
                sumTrDI       = new Series<double>(this);
                diPlusSeries  = new Series<double>(this);
                diMinusSeries = new Series<double>(this);

                SetupFileWatcher();
                lastTradeCount = SystemPerformance.AllTrades.Count;
            }
            else if (State == State.Terminated)
            {
                TeardownFileWatcher();
            }
        }

        // =====================================================================
        // FILE WATCHER + READER  (identical to Version A)
        // =====================================================================
        private void SetupFileWatcher()
        {
            try
            {
                string dir  = Path.GetDirectoryName(matrixFile);
                string file = Path.GetFileName(matrixFile);
                if (!Directory.Exists(dir)) return;
                regimeWatcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter        = NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                regimeWatcher.Changed += (s, e) => ThreadPool.QueueUserWorkItem(_ => ReadLatestRow());
                ReadLatestRow();
            }
            catch { }
        }

        private void TeardownFileWatcher()
        {
            try
            {
                if (regimeWatcher != null)
                {
                    regimeWatcher.EnableRaisingEvents = false;
                    regimeWatcher.Dispose();
                    regimeWatcher = null;
                }
            }
            catch { }
        }

        private void RefreshRegimeState()
        {
            if ((DateTime.Now - lastFileCheck).TotalSeconds < MinCheckSeconds) return;
            try
            {
                DateTime wt = File.GetLastWriteTimeUtc(matrixFile);
                if (wt <= lastFileWriteUtc) { lastFileCheck = DateTime.Now; return; }
                ReadLatestRow();
                lastFileWriteUtc = wt;
                lastFileCheck    = DateTime.Now;
            }
            catch { }
        }

        private void ReadLatestRow()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    string[] lines;
                    lock (fileLock)
                    {
                        using (var fs = new FileStream(matrixFile, FileMode.Open,
                                                       FileAccess.Read, FileShare.ReadWrite))
                        using (var sr = new StreamReader(fs))
                            lines = sr.ReadToEnd()
                                      .Split(new[] { '\r', '\n' },
                                             StringSplitOptions.RemoveEmptyEntries);
                    }
                    if (lines.Length < 2) { parseFailed = true; return; }

                    if (headerIdx.Count == 0)
                    {
                        string[] hdr = lines[0].Split(',');
                        headerIdx.Clear();
                        for (int i = 0; i < hdr.Length; i++)
                            headerIdx[hdr[i].Trim()] = i;
                    }

                    string[] row = lines[lines.Length - 1].Split(',');

                    finalRegime      = Get(row, "FinalRegime");
                    finalDirection   = Get(row, "FinalDirection");
                    reasonCode       = Get(row, "ReasonCode");
                    phase            = Get(row, "Phase");
                    regimeConfidence = (int)GetD(row, "RegimeConfidence");
                    allowLong        = GetI(row, "AllowLong")  == 1;
                    allowShort       = GetI(row, "AllowShort") == 1;
                    momoSizePct      = GetI(row, "AllowMomo_SizePct");
                    staleDataFlag    = GetI(row, "StaleDataFlag") == 1;
                    stateAgeBars     = GetI(row, "StateAgeBars");
                    suggestedAdxMin  = GetI(row, "SuggestedAdxMin");

                    lock (fileLock)
                    {
                        conflictScore = GetD(row, "ConflictScore");
                        velocity3P    = GetD(row, "Velocity3P_ATR");
                    }

                    parseFailed = false;
                    return;
                }
                catch { Thread.Sleep(20); }
            }
            parseFailed = true;
        }

        private string Get(string[] row, string col)
        { int i; return headerIdx.TryGetValue(col, out i) && i < row.Length ? row[i].Trim() : ""; }
        private double GetD(string[] row, string col)
        { double v; return double.TryParse(Get(row, col), out v) ? v : 0.0; }
        private int GetI(string[] row, string col)
        { int v; return int.TryParse(Get(row, col), out v) ? v : 0; }

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

        private void CheckSessionReset()
        {
            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers  = 0;
                hystFailCount      = 0;
                leg1Hit            = false;
                currentLeg2Qty     = 0;
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }
            if (finalRegime != lastRegimeSeen)
            {
                consecutiveLosers = 0;
                lastRegimeSeen    = finalRegime;
            }
        }

        // =====================================================================
        // MAIN BAR UPDATE
        // =====================================================================
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2) return;
            int warmup = Math.Max(AdxPeriod, Math.Max(CiPeriod, AtrPeriod)) + 4;
            if (CurrentBar < warmup) return;

            circuitBreakerFiredThisBar = false;

            RefreshRegimeState();
            CheckSessionReset();

            // ── CI computation ──────────────────────────────────────────
            double trBar = Math.Max(High[0] - Low[0],
                           Math.Max(Math.Abs(High[0] - Close[1]),
                                    Math.Abs(Low[0]  - Close[1])));
            trSeries[0] = trBar;
            double range    = maxHigh[0] - minLow[0];
            double sumTrVal = Math.Max(1e-9, sumTr[0]);
            double denom    = Math.Log10(Math.Max(2, CiPeriod));
            double numer    = (range <= 1e-9) ? 1.0 : (sumTrVal / Math.Max(1e-9, range));
            ci[0] = Math.Max(0.0, Math.Min(100.0, 100.0 * Math.Log10(numer) / denom));

            // ── Wilder DI computation ───────────────────────────────────
            double h0 = High[0], l0 = Low[0], h1 = High[1], l1 = Low[1], c1 = Close[1];
            double tr2  = Math.Max(h0 - l0, Math.Max(Math.Abs(h0 - c1), Math.Abs(l0 - c1)));
            double up   = h0 - h1;
            double down = l1 - l0;
            double dmp  = (up > 0 && up > down) ? up : 0;
            double dmn  = (down > 0 && down > up) ? down : 0;

            if (CurrentBar < AdxPeriod)
            {
                sumTrDI[0]    = sumTrDI[1]    + tr2;
                sumDmPlus[0]  = sumDmPlus[1]  + dmp;
                sumDmMinus[0] = sumDmMinus[1] + dmn;
            }
            else
            {
                sumTrDI[0]    = sumTrDI[1]    - (sumTrDI[1]    / AdxPeriod) + tr2;
                sumDmPlus[0]  = sumDmPlus[1]  - (sumDmPlus[1]  / AdxPeriod) + dmp;
                sumDmMinus[0] = sumDmMinus[1] - (sumDmMinus[1] / AdxPeriod) + dmn;
            }
            double sTr        = sumTrDI[0].ApproxCompare(0) == 0 ? 1e-9 : sumTrDI[0];
            diPlusSeries[0]   = 100.0 * (sumDmPlus[0]  / sTr);
            diMinusSeries[0]  = 100.0 * (sumDmMinus[0] / sTr);

            bool crossUp   = CrossAbove(diPlusSeries, diMinusSeries, 1);
            bool crossDown = CrossBelow(diPlusSeries, diMinusSeries, 1);

            ApplyCircuitBreakerExit(crossDown, crossUp);

            if (Position.MarketPosition != MarketPosition.Flat && finalRegime == "TRANSITION")
            {
                if (Position.MarketPosition == MarketPosition.Long)  ExitLong ("TransitionExit", LEntry1);
                else                                                  ExitShort("TransitionExit", SEntry1);
                leg1Hit = false; currentLeg2Qty = 0;
                return;
            }

            // ── VERSION B: Leg1 fill detection → free-trade pivot for Leg2 ─
            if (Position.MarketPosition != MarketPosition.Flat && !leg1Hit && currentLeg2Qty > 0)
            {
                if (Position.Quantity <= currentLeg2Qty)
                {
                    leg1Hit = true;
                    double pivot = Position.MarketPosition == MarketPosition.Long
                        ? RT(Position.AveragePrice + 4 * TickSize)
                        : RT(Position.AveragePrice - 4 * TickSize);
                    if (Position.MarketPosition == MarketPosition.Long)
                        SetStopLoss(LEntry2, CalculationMode.Price, pivot, false);
                    else
                        SetStopLoss(SEntry2, CalculationMode.Price, pivot, false);
                }
            }

            // ==================================================================
            // ENTRY
            // ==================================================================
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                leg1Hit        = false;
                currentLeg2Qty = 0;

                if (circuitBreakerFiredThisBar)        return;
                if (!IsRegimeAllowed())                return;
                if (parseFailed || staleDataFlag)      return;
                if (consecutiveLosers >= MaxConsecutiveLosses) return;
                if (!IsInTime())                       return;
                if (conflictScore >= 0.40)             return;
                if (regimeConfidence < MinConfidence)  return;

                if (DailyGoal > 0 || DailyLossLimit > 0)
                {
                    double dailyPnL = SystemPerformance.AllTrades
                                          .TradesPerformance.Currency.CumProfit
                                      - sessionStartProfit;
                    if (DailyGoal     > 0 && dailyPnL >=  DailyGoal)     return;
                    if (DailyLossLimit > 0 && dailyPnL <= -DailyLossLimit) return;
                }

                double ciMax    = ActiveCiMax();
                double adxFloor = ActiveAdxFloor();
                bool   ciOk     = ci[0]  <= ciMax;
                bool   adxOk    = adx[0] >= adxFloor;

                if (ciOk && adxOk)
                {
                    if (crossUp   && allowLong)  SubmitLongWithStops();
                    if (crossDown && allowShort) SubmitShortWithStops();
                }
            }

            // ==================================================================
            // SLOPE EXIT
            // ==================================================================
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                string ent = Position.MarketPosition == MarketPosition.Long ? LEntry1 : SEntry1;
                int bse = BarsSinceEntryExecution(0, ent, 0);
                if (bse < MinHoldBars) return;

                bool exitNow = false;

                if (SlopeExit == ExitSlopeMode.Simple &&
                    CurrentBar > Math.Max(CiSlopeBarsExit, AdxSlopeBarsExit))
                {
                    double ciRise  = ci[0]  - ci[CiSlopeBarsExit];
                    double adxDrop = adx[AdxSlopeBarsExit] - adx[0];
                    exitNow = ciRise >= CiRiseMinExit || adxDrop >= AdxDropMinExit;
                }
                else if (SlopeExit == ExitSlopeMode.Hysteresis && CurrentBar > 1)
                {
                    double ciBar  = ci[0]  - ci[1];
                    double adxBar = adx[1] - adx[0];
                    bool   thisFail = ciBar >= HystCiRisePerBar || adxBar >= HystAdxDropPerBar;
                    if (thisFail) hystFailCount++;
                    else          hystFailCount = Math.Max(0, hystFailCount - 1);
                    exitNow = hystFailCount >= HystConsecutive;
                }

                if (exitNow)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        ExitLong("SlopeX", LEntry1);
                        if (currentLeg2Qty > 0) ExitLong(currentLeg2Qty, "SlopeX2", LEntry2);
                    }
                    else
                    {
                        ExitShort("SlopeX", SEntry1);
                        if (currentLeg2Qty > 0) ExitShort(currentLeg2Qty, "SlopeX2", SEntry2);
                    }
                    hystFailCount  = 0;
                    leg1Hit        = false;
                    currentLeg2Qty = 0;
                }
            }
        }

        // =====================================================================
        // CIRCUIT BREAKER
        // =====================================================================
        private void ApplyCircuitBreakerExit(bool crossDown, bool crossUp)
        {
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (crossDown || finalRegime == "TRANSITION")
                {
                    ExitLong("CircuitBreaker", LEntry1);
                    if (currentLeg2Qty > 0) ExitLong(currentLeg2Qty, "CircuitBreaker2", LEntry2);
                    hystFailCount = 0; circuitBreakerFiredThisBar = true;
                    leg1Hit = false; currentLeg2Qty = 0;
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (crossUp || finalRegime == "TRANSITION")
                {
                    ExitShort("CircuitBreaker", SEntry1);
                    if (currentLeg2Qty > 0) ExitShort(currentLeg2Qty, "CircuitBreaker2", SEntry2);
                    hystFailCount = 0; circuitBreakerFiredThisBar = true;
                    leg1Hit = false; currentLeg2Qty = 0;
                }
            }
        }

        // =====================================================================
        // ORDER SUBMISSION — VERSION B: two-leg when confidence threshold met
        // =====================================================================
        private const string LEntry1 = "MomoBL1";
        private const string LEntry2 = "MomoBL2";
        private const string SEntry1 = "MomoBS1";
        private const string SEntry2 = "MomoBS2";

        private void SubmitLongWithStops()
        {
            if (!allowLong || staleDataFlag || parseFailed) return;
            if (momoSizePct <= 0) return;

            int maxC    = CalcMaxContracts();
            int leg1Qty = ScaleByConfidence(maxC, momoSizePct);
            if (leg1Qty < 1) return;

            double risk     = Math.Max(AtrStopMult * atrStop[0], MinStopTicks * TickSize);
            double stp      = RT(Close[0] - risk);
            double leg1Tgt  = RT(Close[0] + risk * Leg1TargetMult);
            double leg2Tgt  = RT(Close[0] + risk * RiskReward);

            // Leg1 always fires
            SetStopLoss(LEntry1, CalculationMode.Price, stp, false);
            SetProfitTarget(LEntry1, CalculationMode.Price, leg1Tgt);
            EnterLong(leg1Qty, LEntry1);

            // Leg2 fires only at high confidence
            bool highConf = regimeConfidence >= ConfidenceScaleThreshold;
            if (highConf)
            {
                int leg2Qty = Math.Max(1, leg1Qty / 2); // half the Leg1 size
                SetStopLoss(LEntry2, CalculationMode.Price, stp, false);
                SetProfitTarget(LEntry2, CalculationMode.Price, leg2Tgt);
                EnterLong(leg2Qty, LEntry2);
                currentLeg2Qty = leg2Qty;
            }

            Print(string.Format(
                "[Momentum_V3D-B] LONG entry | Regime:{0} | Conf:{1} | Conflict:{2:F2} | " +
                "Phase:{3} | Reason:{4} | Leg2:{5} | L1Qty:{6} | L2Qty:{7} | Stop:{8:F2}",
                finalRegime, regimeConfidence, conflictScore, phase, reasonCode,
                highConf ? "YES" : "NO", leg1Qty, currentLeg2Qty, stp));
        }

        private void SubmitShortWithStops()
        {
            if (!allowShort || staleDataFlag || parseFailed) return;
            if (momoSizePct <= 0) return;

            int maxC    = CalcMaxContracts();
            int leg1Qty = ScaleByConfidence(maxC, momoSizePct);
            if (leg1Qty < 1) return;

            double risk    = Math.Max(AtrStopMult * atrStop[0], MinStopTicks * TickSize);
            double stp     = RT(Close[0] + risk);
            double leg1Tgt = RT(Close[0] - risk * Leg1TargetMult);
            double leg2Tgt = RT(Close[0] - risk * RiskReward);

            SetStopLoss(SEntry1, CalculationMode.Price, stp, false);
            SetProfitTarget(SEntry1, CalculationMode.Price, leg1Tgt);
            EnterShort(leg1Qty, SEntry1);

            bool highConf = regimeConfidence >= ConfidenceScaleThreshold;
            if (highConf)
            {
                int leg2Qty = Math.Max(1, leg1Qty / 2);
                SetStopLoss(SEntry2, CalculationMode.Price, stp, false);
                SetProfitTarget(SEntry2, CalculationMode.Price, leg2Tgt);
                EnterShort(leg2Qty, SEntry2);
                currentLeg2Qty = leg2Qty;
            }

            Print(string.Format(
                "[Momentum_V3D-B] SHORT entry | Regime:{0} | Conf:{1} | Conflict:{2:F2} | " +
                "Phase:{3} | Reason:{4} | Leg2:{5} | L1Qty:{6} | L2Qty:{7} | Stop:{8:F2}",
                finalRegime, regimeConfidence, conflictScore, phase, reasonCode,
                highConf ? "YES" : "NO", leg1Qty, currentLeg2Qty, stp));
        }
    }
}