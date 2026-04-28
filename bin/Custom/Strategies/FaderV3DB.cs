// CC BY-NC 4.0
// Fader_V3D_B.cs  — V3D Institutional Regime Matrix  — Version B
// ─────────────────────────────────────────────────────────────────
// A/B TEST PURPOSE
//   Version A sets Leg2 profit target at the VWAP price captured at entry time.
//   That snapshot is fixed — if VWAP drifts during the trade, the target does not move.
//
//   Version B tracks VWAP dynamically. When sessionVwap updates from the supervisor
//   file, the Leg2 profit target is recalculated and resubmitted via SetProfitTarget.
//
//   EXPECTED TRADEOFF vs Version A:
//     On true rotation days: VWAP drifts very slowly — minimal difference.
//     On misclassified days (ROTATION_LIQUID but actually drifting directionally):
//       VWAP moves away from the fade target, and the dynamic version pulls the
//       Leg2 target progressively closer to entry — triggering an earlier exit
//       and capturing more of Leg1's profit before the trade deteriorates.
//     Risk: on true rotation days where VWAP briefly overshoots, the dynamic target
//       may follow VWAP higher/lower and delay Leg2 exit slightly.
//
//   TEST DURATION: 4–6 weeks parallel SIM. Compare Leg2 average outcome specifically.
//   The Leg1 result should be identical between A and B since Leg1 uses a fixed target.
//
// EVERYTHING ELSE IS IDENTICAL TO VERSION A.
//   Same bug fixes, same gates, same free-trade pivot, same runner exits.
//   The only structural difference is dynamic Leg2 target tracking.
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
    public class Fader_V3D_B : Strategy
    {
        // =====================================================================
        // PARAMETERS  (identical to Version A)
        // =====================================================================

        [NinjaScriptProperty]
        [Display(Name = "Data Folder Path", GroupName = "1. Regime", Order = 0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D";

        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "ATR Stop Multiplier", GroupName = "2. Risk", Order = 0)]
        public double AtrStopMult { get; set; } = 1.25;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", GroupName = "2. Risk", Order = 1)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.0, 2.0)]
        [Display(Name = "Edge Proximity (ATR)", GroupName = "2. Risk", Order = 2)]
        public double EdgeProximityAtr { get; set; } = 0.5;

        [NinjaScriptProperty, Range(4, 500)]
        [Display(Name = "Min Target Ticks", GroupName = "2. Risk", Order = 3)]
        public int MinTargetTicks { get; set; } = 10;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5.00  ES=12.50  MNQ=0.50  MES=1.25",
                 GroupName = "2. Risk", Order = 4)]
        public double TickValueDollars { get; set; } = 5.00;

        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name = "Bollinger Period", GroupName = "3. Signal", Order = 0)]
        public int BollingerPeriod { get; set; } = 20;

        [NinjaScriptProperty, Range(0.5, 4.0)]
        [Display(Name = "Bollinger StdDev", GroupName = "3. Signal", Order = 1)]
        public double BollingerDev { get; set; } = 2.0;

        // ── VERSION B EXCLUSIVE PARAMETER ─────────────────────────────────
        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Min VWAP Move to Update Target (ticks)",
                 Description = "Only update Leg2 target when VWAP has moved at least this many ticks. " +
                               "Prevents excessive target updates on micro-drifts. Default: 4.",
                 GroupName = "3. Signal", Order = 2)]
        public int VwapUpdateThresholdTicks { get; set; } = 4;
        // ──────────────────────────────────────────────────────────────────

        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "4. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0 = off)", GroupName = "4. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0 = off)", GroupName = "4. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "5. Time", Order = 0)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Start Time (HHmmss)", GroupName = "5. Time", Order = 1)]
        public int StartTime { get; set; } = 103500;

        [NinjaScriptProperty]
        [Display(Name = "End Time (HHmmss)", GroupName = "5. Time", Order = 2)]
        public int EndTime { get; set; } = 155500;

        // =====================================================================
        // REGIME STATE
        // =====================================================================
        private string   matrixFile         = "";
        private string   leaderSymbol       = "";
        private DateTime lastFileWriteUtc   = DateTime.MinValue;
        private DateTime lastFileCheck      = DateTime.MinValue;
        private const int MinCheckSeconds   = 15;
        private FileSystemWatcher regimeWatcher;
        private readonly object   fileLock  = new object();

        private volatile string finalRegime      = "UNKNOWN";
        private volatile string reasonCode       = "";
        private volatile bool   allowFadeLong    = false;
        private volatile bool   allowFadeShort   = false;
        private volatile int    faderSizePct     = 0;
        private volatile bool   staleDataFlag    = true;
        private volatile bool   parseFailed      = true;
        private volatile int    twoSidedFlag     = 0;
        private          double sessionVwap      = 0.0;
        private          double ibHigh           = 0.0;
        private          double ibLow            = 0.0;
        private          double pdVah            = 0.0;
        private          double pdVal            = 0.0;

        private Dictionary<string, int> headerIdx = new Dictionary<string, int>();

        // =====================================================================
        // INDICATORS
        // =====================================================================
        private ATR       atr;
        private Bollinger bb;

        // =====================================================================
        // RUNTIME STATE
        // =====================================================================
        private int    consecutiveLosers  = 0;
        private int    lastTradeCount     = 0;
        private double sessionStartProfit = 0;

        private bool   leg1Hit            = false;
        private int    currentLeg2Qty     = 1;
        private string activeLeg2         = "";
        private bool   isLongTrade        = false;

        // VERSION B: track last VWAP value used for Leg2 target
        private double leg2VwapAtEntry    = 0.0;   // VWAP snapshot when trade was entered
        private double lastUpdatedVwap    = 0.0;   // last VWAP used for target update

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
            double atrVal = atr[0];
            if (atrVal <= 0) return 1;
            double dollarRisk = (atrVal * AtrStopMult) / TickSize * TickValueDollars;
            if (dollarRisk <= 0) return 1;
            return Math.Max(1, (int)(1500.0 / dollarRisk));
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
        {
            return Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));
        }

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "Fader_V3D_B";
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
                leaderSymbol   = GetLeaderSymbol(Instrument.MasterInstrument.Name);
                matrixFile     = Path.Combine(DataFolderPath, leaderSymbol + "_RegimeMatrix_Latest.csv");
                atr            = ATR(AtrPeriod);
                bb             = Bollinger(BollingerDev, BollingerPeriod);
                SetupFileWatcher();
                lastTradeCount = SystemPerformance.AllTrades.Count;
            }
            else if (State == State.Terminated)
            {
                TeardownFileWatcher();
            }
        }

        // =====================================================================
        // FILE WATCHER
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

        // =====================================================================
        // REGIME FILE READER
        // =====================================================================
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

                    finalRegime    = Get(row, "FinalRegime");
                    reasonCode     = Get(row, "ReasonCode");
                    allowFadeLong  = GetI(row, "AllowFadeLong")  == 1;
                    allowFadeShort = GetI(row, "AllowFadeShort") == 1;
                    faderSizePct   = GetI(row, "AllowPine_SizePct");
                    staleDataFlag  = GetI(row, "StaleDataFlag") == 1;
                    twoSidedFlag   = GetI(row, "TwoSidedFlag");

                    lock (fileLock)
                    {
                        sessionVwap = GetD(row, "SessionVWAP");
                        ibHigh      = GetD(row, "IBHigh");
                        ibLow       = GetD(row, "IBLow");
                        pdVah       = GetD(row, "PDVAH");
                        pdVal       = GetD(row, "PDVAL");
                    }

                    parseFailed = false;
                    return;
                }
                catch { Thread.Sleep(20); }
            }
            parseFailed = true;
        }

        private string Get(string[] row, string col)
        {
            int i;
            return headerIdx.TryGetValue(col, out i) && i < row.Length ? row[i].Trim() : "";
        }
        private double GetD(string[] row, string col)
        {
            double v;
            return double.TryParse(Get(row, col), out v) ? v : 0.0;
        }
        private int GetI(string[] row, string col)
        {
            int v;
            return int.TryParse(Get(row, col), out v) ? v : 0;
        }

        // =====================================================================
        // STRUCTURAL EDGE DETECTION  (identical to Version A)
        // =====================================================================
        private bool NearStructuralEdgeLow(double price, double atrVal, out double nearestEdge)
        {
            nearestEdge = 0;
            double proximity = EdgeProximityAtr * atrVal;
            double bestDist  = double.MaxValue;
            foreach (double edge in new[] { ibLow, pdVal })
            {
                if (edge <= 0) continue;
                double dist = price - edge;
                if (dist >= 0 && dist <= proximity && dist < bestDist)
                { bestDist = dist; nearestEdge = edge; }
            }
            return nearestEdge > 0;
        }

        private bool NearStructuralEdgeHigh(double price, double atrVal, out double nearestEdge)
        {
            nearestEdge = 0;
            double proximity = EdgeProximityAtr * atrVal;
            double bestDist  = double.MaxValue;
            foreach (double edge in new[] { ibHigh, pdVah })
            {
                if (edge <= 0) continue;
                double dist = edge - price;
                if (dist >= 0 && dist <= proximity && dist < bestDist)
                { bestDist = dist; nearestEdge = edge; }
            }
            return nearestEdge > 0;
        }

        private bool atHighEdge(double price, double atrVal)
        { double d; return NearStructuralEdgeHigh(price, atrVal, out d); }

        private bool bollingerHighTouch()
        {
            return (ibHigh <= 0 && pdVah <= 0) &&
                   (High[1] >= bb.Upper[1] || High[2] >= bb.Upper[2]);
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
            if (CurrentBar < BollingerPeriod + 4) return;

            RefreshRegimeState();

            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers  = 0;
                leg1Hit            = false;
                currentLeg2Qty     = 1;
                activeLeg2         = "";
                leg2VwapAtEntry    = 0.0;
                lastUpdatedVwap    = 0.0;
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }

            if (Position.MarketPosition != MarketPosition.Flat && finalRegime == "TRANSITION")
            {
                if (Position.MarketPosition == MarketPosition.Long)  ExitLong ("TransitionExit", "");
                else                                                  ExitShort("TransitionExit", "");
                leg1Hit = false; activeLeg2 = ""; leg2VwapAtEntry = 0; lastUpdatedVwap = 0;
                return;
            }

            // ==================================================================
            // RUNNER MANAGEMENT
            // ==================================================================
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (finalRegime == "ROTATION_ILLIQUID")
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong(Position.Quantity, "IlliquidExit", activeLeg2.Length > 0 ? activeLeg2 : "");
                    else
                        ExitShort(Position.Quantity, "IlliquidExit", activeLeg2.Length > 0 ? activeLeg2 : "");
                    leg1Hit = false; activeLeg2 = ""; leg2VwapAtEntry = 0; lastUpdatedVwap = 0;
                    return;
                }

                // Free-trade pivot
                if (!leg1Hit && Position.Quantity <= currentLeg2Qty && currentLeg2Qty > 0)
                {
                    leg1Hit = true;
                    double pivot = Position.MarketPosition == MarketPosition.Long
                        ? RT(Position.AveragePrice + 4 * TickSize)
                        : RT(Position.AveragePrice - 4 * TickSize);
                    if (activeLeg2 == FadeLEntry2)
                        SetStopLoss(FadeLEntry2, CalculationMode.Price, pivot, false);
                    else if (activeLeg2 == FadeSEntry2)
                        SetStopLoss(FadeSEntry2, CalculationMode.Price, pivot, false);

                    // Initialize dynamic VWAP tracking
                    lastUpdatedVwap = leg2VwapAtEntry;
                }

                // ── VERSION B: dynamic VWAP target update ─────────────────
                // Only update after Leg1 hit (runner is on a free trade).
                // Only update when VWAP has moved by at least VwapUpdateThresholdTicks.
                if (leg1Hit && sessionVwap > 0 && activeLeg2.Length > 0)
                {
                    double vwapDelta = Math.Abs(sessionVwap - lastUpdatedVwap);
                    double threshold = VwapUpdateThresholdTicks * TickSize;

                    if (vwapDelta >= threshold)
                    {
                        double newTarget = RT(sessionVwap);
                        if (activeLeg2 == FadeLEntry2)
                            SetProfitTarget(FadeLEntry2, CalculationMode.Price, newTarget);
                        else if (activeLeg2 == FadeSEntry2)
                            SetProfitTarget(FadeSEntry2, CalculationMode.Price, newTarget);

                        Print(string.Format(
                            "[Fader_V3D-B] VWAP target update | New VWAP:{0:F2} | Delta:{1:F2} ticks",
                            sessionVwap, vwapDelta / TickSize));
                        lastUpdatedVwap = sessionVwap;
                    }
                }
                // ──────────────────────────────────────────────────────────
                return;
            }

            // ==================================================================
            // ENTRY  (identical gates to Version A)
            // ==================================================================
            leg1Hit = false; currentLeg2Qty = 1; activeLeg2 = "";
            leg2VwapAtEntry = 0; lastUpdatedVwap = 0;

            if (parseFailed || staleDataFlag)              return;
            if (finalRegime != "ROTATION_LIQUID")          return;
            if (twoSidedFlag != 1)                         return;
            if (consecutiveLosers >= MaxConsecutiveLosses) return;
            if (!IsInTime())                               return;
            if (faderSizePct <= 0)                         return;

            if (DailyGoal > 0 || DailyLossLimit > 0)
            {
                double dailyPnL = SystemPerformance.AllTrades
                                      .TradesPerformance.Currency.CumProfit
                                  - sessionStartProfit;
                if (DailyGoal     > 0 && dailyPnL >=  DailyGoal)     return;
                if (DailyLossLimit > 0 && dailyPnL <= -DailyLossLimit) return;
            }

            bool greenBar = Close[0] > Open[0];
            bool redBar   = Close[0] < Open[0];
            bool wasGreen = Close[1] > Open[1];
            bool wasRed   = Close[1] < Open[1];

            double atrVal = atr[0];
            double price  = Close[0];
            double vwap   = sessionVwap > 0 ? sessionVwap : bb.Middle[0];

            double lowEdge;
            bool atLowEdge = NearStructuralEdgeLow(price, atrVal, out lowEdge);
            bool bollingerLowTouch = (ibLow <= 0 && pdVal <= 0) &&
                                     (Low[1] <= bb.Lower[1] || Low[2] <= bb.Lower[2]);

            if (allowFadeLong && (atLowEdge || bollingerLowTouch) && wasRed && greenBar)
            {
                double distToVwap = vwap - price;
                if (distToVwap / TickSize >= MinTargetTicks)
                {
                    double stopPrice  = RT(price - atrVal * AtrStopMult);
                    double leg1Target = RT(price + distToVwap * 0.50);
                    double leg2Target = RT(vwap);

                    int maxC    = CalcMaxContracts();
                    int sz      = ScaleByConfidence(maxC, faderSizePct);
                    int leg1Qty = Math.Max(1, sz / 2);
                    int leg2Qty = Math.Max(1, sz - leg1Qty);

                    SetStopLoss(FadeLEntry1, CalculationMode.Price, stopPrice, false);
                    SetStopLoss(FadeLEntry2, CalculationMode.Price, stopPrice, false);
                    SetProfitTarget(FadeLEntry1, CalculationMode.Price, leg1Target);
                    SetProfitTarget(FadeLEntry2, CalculationMode.Price, leg2Target);
                    EnterLong(leg1Qty, FadeLEntry1);
                    EnterLong(leg2Qty, FadeLEntry2);

                    currentLeg2Qty  = leg2Qty;
                    activeLeg2      = FadeLEntry2;
                    isLongTrade     = true;
                    leg2VwapAtEntry = vwap;
                    lastUpdatedVwap = vwap;

                    string trigger = atLowEdge ? string.Format("EDGE:{0:F2}", lowEdge) : "BOLLINGER_FALLBACK";
                    Print(string.Format(
                        "[Fader_V3D-B] LONG entry | Regime:{0} | Reason:{1} | Trigger:{2} | " +
                        "SizePct:{3} | Qty:{4}+{5} | Stop:{6:F2} | T1:{7:F2} | T2(VWAP):{8:F2}",
                        finalRegime, reasonCode, trigger,
                        faderSizePct, leg1Qty, leg2Qty, stopPrice, leg1Target, leg2Target));
                }
            }
            else if (allowFadeShort && (atHighEdge(price, atrVal) || bollingerHighTouch()) && wasGreen && redBar)
            {
                double highEdgePrice;
                NearStructuralEdgeHigh(price, atrVal, out highEdgePrice);

                double distToVwap = price - vwap;
                if (distToVwap / TickSize >= MinTargetTicks)
                {
                    double stopPrice  = RT(price + atrVal * AtrStopMult);
                    double leg1Target = RT(price - distToVwap * 0.50);
                    double leg2Target = RT(vwap);

                    int maxC    = CalcMaxContracts();
                    int sz      = ScaleByConfidence(maxC, faderSizePct);
                    int leg1Qty = Math.Max(1, sz / 2);
                    int leg2Qty = Math.Max(1, sz - leg1Qty);

                    SetStopLoss(FadeSEntry1, CalculationMode.Price, stopPrice, false);
                    SetStopLoss(FadeSEntry2, CalculationMode.Price, stopPrice, false);
                    SetProfitTarget(FadeSEntry1, CalculationMode.Price, leg1Target);
                    SetProfitTarget(FadeSEntry2, CalculationMode.Price, leg2Target);
                    EnterShort(leg1Qty, FadeSEntry1);
                    EnterShort(leg2Qty, FadeSEntry2);

                    currentLeg2Qty  = leg2Qty;
                    activeLeg2      = FadeSEntry2;
                    isLongTrade     = false;
                    leg2VwapAtEntry = vwap;
                    lastUpdatedVwap = vwap;

                    string trigger = highEdgePrice > 0
                        ? string.Format("EDGE:{0:F2}", highEdgePrice) : "BOLLINGER_FALLBACK";
                    Print(string.Format(
                        "[Fader_V3D-B] SHORT entry | Regime:{0} | Reason:{1} | Trigger:{2} | " +
                        "SizePct:{3} | Qty:{4}+{5} | Stop:{6:F2} | T1:{7:F2} | T2(VWAP):{8:F2}",
                        finalRegime, reasonCode, trigger,
                        faderSizePct, leg1Qty, leg2Qty, stopPrice, leg1Target, leg2Target));
                }
            }
        }

        // =====================================================================
        // ORDER LABELS
        // =====================================================================
        private const string FadeLEntry1 = "FadeBL1";
        private const string FadeLEntry2 = "FadeBL2";
        private const string FadeSEntry1 = "FadeBS1";
        private const string FadeSEntry2 = "FadeBS2";
    }
}