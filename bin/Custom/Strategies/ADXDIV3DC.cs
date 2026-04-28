// CC BY-NC 4.0
// ADX_DI_V3D_C.cs  — V3D Institutional Regime Matrix  — Version C
// ─────────────────────────────────────────────────────────────────
// VERSION C: FULLY UNIFIED Gu5 ADX DI IMPLEMENTATION
// Source: "ADX Di+ Di- [Gu5]" by gu5tavo71 (Gustavo Cardelle)
// https://www.safecreative.org/work/2302153511401-adx-di-di-gu5-
//
// WHY VERSION C EXISTS — THE HYBRID PROBLEM
//   Versions A and B use manual Wilder DI series (diPlusSeries, diMinusSeries)
//   paired with NT8's built-in ADX() indicator for the floor/StopX check.
//   These two computations are INDEPENDENT: NT8's ADX() maintains its own
//   internal DI values with a different initialization sequence.
//
//   In the original Pine source, ALL signals — DI cross, chop detection (hlRange),
//   momentum direction (sigUp), strong/weak threshold (diUpUp/diDnDn), and the
//   condition state machine — derive from the SAME single f_dirMov() computation.
//   The sig (ADX) is RMA of the DX ratio computed from the exact same DI+ and DI-
//   that drive the cross. One computation, six signals.
//
//   Version C implements this correctly. The built-in ADX() indicator is removed.
//   All signals derive from a single unified Wilder series chain.
//
// MATHEMATICAL EQUIVALENCE TO PINE f_dirMov + f_sig:
//   Pine ta.rma(value, len) = prev + (value - prev) / len
//   C# equivalent:           series[0] = series[1] + (value - series[1]) / len
//   These are identical. The accumulated sum pattern used in Versions A/B is
//   also identical (sum[0] = sum[1] - sum[1]/len + value = sum[1] + (value - sum[1]/len)).
//
//   Pine DI+  = 100 * rma(plusDM, diLen)  / rma(tr, diLen)
//   C#  DI+   = 100 * sumDmPlus[0] / sumTrDI[0]              — SAME
//
//   Pine sig  = 100 * rma(|DI+ - DI-| / (DI+ + DI-), sigLen)
//   C#  sig   = sigSeries[0] = sigSeries[1] + (dx - sigSeries[1]) / sigLen,
//               where dx = |diPlus - diMinus| / (diPlus + diMinus)   — SAME
//
// CONDITION STATE MACHINE (Pine -> C#):
//   condition = 1.0   strong long  (entryLongStr : !hlRange AND diUp AND sigUp AND diUpUp)
//   condition = -1.0  strong short (entryShortStr: !hlRange AND diDn AND sigUp AND diDnDn)
//   condition = 0.5   weak long    (entryLong    : !hlRange AND diUp AND sigUp AND DI crossed up)
//   condition = -0.5  weak short   (entryShort   : symmetric)
//   condition = 0     exit         (exitLong/Short: DI counter-cross OR sig drops into hlRange)
//
//   Entry fires on condition TRANSITION (change from previous), not on level.
//   Strong entries require DI >= LevelTrend (default 35).
//   hlRange gate (sig <= LevelRange=20) suppresses ALL entries during chop.
//
// A/B/C TEST DESIGN:
//   A — hybrid (manual DI + built-in ADX floor, fixed target)
//   B — hybrid (manual DI + built-in ADX floor, no fixed target)
//   C — fully unified Gu5 (this file) — condition state machine, no built-in ADX
//
//   Test questions:
//   A vs B: does removing the fixed target improve outcomes (exit question)?
//   C vs A: does full Gu5 alignment improve entry quality (signal question)?
//   C vs B: does full Gu5 signal + no fixed target produce best overall results?
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
    public class ADX_DI_V3D_C : Strategy
    {
        // =====================================================================
        // PARAMETERS
        // =====================================================================

        [NinjaScriptProperty]
        [Display(Name = "Data Folder Path", GroupName = "1. Regime", Order = 0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes\V3D";

        // --- Risk ---
        [NinjaScriptProperty, Range(0.1, 5.0)]
        [Display(Name = "ATR Multiplier (stop)", GroupName = "2. Risk", Order = 0)]
        public double AtrMultiplier { get; set; } = 1.0;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", GroupName = "2. Risk", Order = 1)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.01, 100.0)]
        [Display(Name = "Tick Value ($)  NQ=5.00  ES=12.50  MNQ=0.50  MES=1.25",
                 GroupName = "2. Risk", Order = 2)]
        public double TickValueDollars { get; set; } = 5.00;

        // NOTE: No RiskReward parameter in Version C.
        // Exit is driven entirely by the condition state machine (condition -> 0),
        // which fires on DI counter-cross or sig dropping below LevelRange.
        // The Bar-N trailing stop provides hard stop protection.

        // --- Gu5 ADX DI parameters (direct port from Pine) ---
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "DI Length (i_diLen)",
                 Description = "DI+ / DI- smoothing period. Pine default: 14.",
                 GroupName = "3. Signal — Gu5 ADX", Order = 0)]
        public int DiLength { get; set; } = 14;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ADX Smoothing (i_sigLen)",
                 Description = "RMA smoothing period for the sig (ADX). Pine default: 14.",
                 GroupName = "3. Signal — Gu5 ADX", Order = 1)]
        public int AdxSmoothing { get; set; } = 14;

        [NinjaScriptProperty, Range(1.0, 50.0)]
        [Display(Name = "Level Range (i_hlRange)",
                 Description = "Chop detection: sig <= LevelRange means no entries. Pine default: 20.",
                 GroupName = "3. Signal — Gu5 ADX", Order = 2)]
        public double LevelRange { get; set; } = 20.0;

        [NinjaScriptProperty, Range(1.0, 100.0)]
        [Display(Name = "Level Trend (i_hlTrend)",
                 Description = "Strong entry threshold: DI must exceed this for condition=±1. Pine default: 35.",
                 GroupName = "3. Signal — Gu5 ADX", Order = 3)]
        public double LevelTrend { get; set; } = 35.0;

        [NinjaScriptProperty]
        [Display(Name = "Allow Weak Entries (condition = ±0.5)",
                 Description = "If true: enter on both weak (0.5) and strong (1.0) condition signals. " +
                               "If false: enter only on strong (1.0) signals where DI >= LevelTrend.",
                 GroupName = "3. Signal — Gu5 ADX", Order = 4)]
        public bool AllowWeakEntry { get; set; } = true;

        [NinjaScriptProperty, Range(0.5, 5.0)]
        [Display(Name = "IB Width ATR Minimum",
                 Description = "ib_width_atr must be >= this for ROTATION_LIQUID entry.",
                 GroupName = "3. Signal — Gu5 ADX", Order = 5)]
        public double IbWidthAtrMin { get; set; } = 2.0;

        [NinjaScriptProperty, Range(50, 100)]
        [Display(Name = "Min Regime Confidence",
                 GroupName = "3. Signal — Gu5 ADX", Order = 6)]
        public int MinConfidence { get; set; } = 55;

        // --- Trailing Stop ---
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Trail N Bars", GroupName = "4. Trail", Order = 0)]
        public int TrailNBars { get; set; } = 1;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset (ticks)", GroupName = "4. Trail", Order = 1)]
        public int TrailOffsetTicks { get; set; } = 5;

        // --- Guards ---
        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "Max Consecutive Losses", GroupName = "5. Guards", Order = 0)]
        public int MaxConsecutiveLosses { get; set; } = 2;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily P&L Goal ($, 0 = off)", GroupName = "5. Guards", Order = 1)]
        public double DailyGoal { get; set; } = 0;

        [NinjaScriptProperty, Range(0, 10000)]
        [Display(Name = "Daily Loss Limit ($, 0 = off)", GroupName = "5. Guards", Order = 2)]
        public double DailyLossLimit { get; set; } = 0;

        // --- Time ---
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", GroupName = "6. Time", Order = 0)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Block Open (HHmmss)", GroupName = "6. Time", Order = 1)]
        public int BlockOpenEnd { get; set; } = 93900;

        [NinjaScriptProperty]
        [Display(Name = "Block Lunch Start (HHmmss)", GroupName = "6. Time", Order = 2)]
        public int BlockLunchStart { get; set; } = 113000;

        [NinjaScriptProperty]
        [Display(Name = "Block Lunch End (HHmmss)", GroupName = "6. Time", Order = 3)]
        public int BlockLunchEnd { get; set; } = 132000;

        [NinjaScriptProperty]
        [Display(Name = "Session End (HHmmss)", GroupName = "6. Time", Order = 4)]
        public int SessionEnd { get; set; } = 155500;

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
        private volatile string phase            = "UNKNOWN";
        private volatile string reasonCode       = "";
        private volatile bool   allowLong        = false;
        private volatile bool   allowShort       = false;
        private volatile int    adxDiSizePct     = 0;
        private volatile int    regimeConfidence = 0;
        private volatile bool   staleDataFlag    = true;
        private volatile bool   parseFailed      = true;
        private volatile int    twoSidedFlag     = 0;
        private          double ibWidthAtr       = 0.0;
        // Version C does not read SuggestedAdxMin — it uses LevelRange from the
        // unified sig series instead of an external ADX floor.

        private static readonly HashSet<string> BlockedPhases =
            new HashSet<string> { "OPENING_AUCTION", "EARLY_TEST", "CASH_CLOSE" };

        private Dictionary<string, int> headerIdx = new Dictionary<string, int>();

        // =====================================================================
        // INDICATORS
        // NOTE: No ADX() built-in indicator in Version C.
        //       All computations derive from the unified manual series below.
        // =====================================================================
        private ATR atr;

        // ── Unified Wilder series (Pine f_dirMov equivalent) ──────────────
        // These are the ONLY data sources for DI+, DI-, sig, and all signals.
        private Series<double> dmPlus, dmMinus;         // raw DM per bar
        private Series<double> sumDmPlus, sumDmMinus;   // Wilder-accumulated DM
        private Series<double> sumTrDI;                  // Wilder-accumulated TR
        private Series<double> diPlusSeries;             // DI+ = 100 * sumDmPlus / sumTrDI
        private Series<double> diMinusSeries;            // DI- = 100 * sumDmMinus / sumTrDI

        // ── Unified sig series (Pine f_sig equivalent) ────────────────────
        private Series<double> dxSeries;   // |DI+ - DI-| / (DI+ + DI-) per bar
        private Series<double> sigSeries;  // RMA of dxSeries × 100  (this IS the ADX/sig)

        // ── Condition state machine ────────────────────────────────────────
        private Series<double> condSeries; // 1.0, -1.0, 0.5, -0.5, or 0.0

        // =====================================================================
        // RUNTIME STATE
        // =====================================================================
        private int    consecutiveLosers  = 0;
        private int    lastTradeCount     = 0;
        private double trailingStopLong   = double.NaN;
        private double trailingStopShort  = double.NaN;
        private double sessionStartProfit = 0;

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
            if (t <= BlockOpenEnd)                           return false;
            if (t >= BlockLunchStart && t <= BlockLunchEnd) return false;
            if (t > SessionEnd)                              return false;
            return true;
        }

        private int CalcMaxContracts()
        {
            double atrVal = atr[0];
            if (atrVal <= 0) return 1;
            double dollarRisk = (atrVal * AtrMultiplier) / TickSize * TickValueDollars;
            if (dollarRisk <= 0) return 1;
            return Math.Max(1, (int)(1500.0 / dollarRisk));
        }

        private int ScaleByConfidence(int maxQty, int sizePct)
        {
            return Math.Max(1, (int)Math.Floor(maxQty * sizePct / 100.0));
        }

        private double BarNStopLong()
        {
            double lo = Low[0];
            for (int i = 1; i < TrailNBars && i < CurrentBar; i++)
                lo = Math.Min(lo, Low[i]);
            return RT(lo - TickSize * TrailOffsetTicks);
        }

        private double BarNStopShort()
        {
            double hi = High[0];
            for (int i = 1; i < TrailNBars && i < CurrentBar; i++)
                hi = Math.Max(hi, High[i]);
            return RT(hi + TickSize * TrailOffsetTicks);
        }

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                         = "ADX_DI_V3D_C";
                Calculate                    = Calculate.OnPriceChange;
                EntriesPerDirection          = 1;
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

                // Unified series — all initialized together
                dmPlus        = new Series<double>(this);
                dmMinus       = new Series<double>(this);
                sumDmPlus     = new Series<double>(this);
                sumDmMinus    = new Series<double>(this);
                sumTrDI       = new Series<double>(this);
                diPlusSeries  = new Series<double>(this);
                diMinusSeries = new Series<double>(this);
                dxSeries      = new Series<double>(this);
                sigSeries     = new Series<double>(this);
                condSeries    = new Series<double>(this);

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
                    phase            = Get(row, "Phase");
                    reasonCode       = Get(row, "ReasonCode");
                    allowLong        = GetI(row, "AllowLong")  == 1;
                    allowShort       = GetI(row, "AllowShort") == 1;
                    adxDiSizePct     = GetI(row, "AllowADX_DI_SizePct");
                    regimeConfidence = (int)GetD(row, "RegimeConfidence");
                    staleDataFlag    = GetI(row, "StaleDataFlag") == 1;
                    twoSidedFlag     = GetI(row, "TwoSidedFlag");

                    lock (fileLock) { ibWidthAtr = GetD(row, "IBWidthATR"); }

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

        // =====================================================================
        // MAIN BAR UPDATE
        // =====================================================================
        protected override void OnBarUpdate()
        {
            // ── STEP 1: Unified Wilder DI computation (Pine f_dirMov) ─────
            // Must run on every bar before any logic — series must be continuous.
            if (CurrentBar == 0)
            {
                dmPlus[0] = dmMinus[0] = sumTrDI[0] = sumDmPlus[0] = sumDmMinus[0] =
                diPlusSeries[0] = diMinusSeries[0] = dxSeries[0] = sigSeries[0] = condSeries[0] = 0;
                return;
            }

            double h0 = High[0], l0 = Low[0], h1 = High[1], l1 = Low[1], c1 = Close[1];
            double tr   = Math.Max(h0 - l0, Math.Max(Math.Abs(h0 - c1), Math.Abs(l0 - c1)));
            double up   = h0 - h1;
            double down = l1 - l0;
            double dmp  = (up   > 0 && up   > down) ? up   : 0;
            double dmn  = (down > 0 && down > up)   ? down : 0;
            dmPlus[0]  = dmp;
            dmMinus[0] = dmn;

            // Wilder RMA accumulation: rma[0] = rma[1] + (value - rma[1]) / len
            // Expressed as: sum[0] = sum[1] - sum[1]/len + value
            // Both are algebraically identical.
            if (CurrentBar < DiLength)
            {
                sumTrDI[0]    = sumTrDI[1]    + tr;
                sumDmPlus[0]  = sumDmPlus[1]  + dmp;
                sumDmMinus[0] = sumDmMinus[1] + dmn;
            }
            else
            {
                sumTrDI[0]    = sumTrDI[1]    - (sumTrDI[1]    / DiLength) + tr;
                sumDmPlus[0]  = sumDmPlus[1]  - (sumDmPlus[1]  / DiLength) + dmp;
                sumDmMinus[0] = sumDmMinus[1] - (sumDmMinus[1] / DiLength) + dmn;
            }
            double sTr       = sumTrDI[0].ApproxCompare(0) == 0 ? 1e-9 : sumTrDI[0];
            diPlusSeries[0]  = 100.0 * (sumDmPlus[0]  / sTr);
            diMinusSeries[0] = 100.0 * (sumDmMinus[0] / sTr);

            // ── STEP 2: Unified sig computation (Pine f_sig) ──────────────
            // sig = 100 * rma(|DI+ - DI-| / (DI+ + DI-), sigLen)
            // This is the ADX — computed from the SAME DI series as the cross.
            // No built-in ADX() indicator used.
            double diSum = diPlusSeries[0] + diMinusSeries[0];
            double dx    = diSum.ApproxCompare(0) == 0
                ? 0.0
                : Math.Abs(diPlusSeries[0] - diMinusSeries[0]) / diSum;
            dxSeries[0] = dx;

            // RMA of dx
            if (CurrentBar < AdxSmoothing)
                sigSeries[0] = sigSeries[1] + (dx - sigSeries[1]) / (CurrentBar + 1);
            else
                sigSeries[0] = sigSeries[1] + (dx - sigSeries[1]) / AdxSmoothing;
            sigSeries[0] *= 100.0; // Scale to 0-100

            // Warmup guard — need enough bars for both DI and sig to stabilize
            int warmup = Math.Max(DiLength, AdxSmoothing) + Math.Max(AtrPeriod, 2) + 2;
            if (CurrentBar < warmup)
            {
                condSeries[0] = 0;
                return;
            }

            // ── STEP 3: Pine-derived boolean conditions ────────────────────
            // These match the Pine condition declarations exactly.
            double sig    = sigSeries[0];
            double sigPrev= sigSeries[1];
            double diPlus = diPlusSeries[0];
            double diMinus= diMinusSeries[0];
            double diPlus1= diPlusSeries[1];
            double diMinus1=diMinusSeries[1];

            bool hlRange  = sig  <= LevelRange;            // chop: sig in range zone
            bool hlRange1 = sigPrev <= LevelRange;         // chop last bar
            bool sigUp    = sig  >  sigPrev;                // sig rising
            bool diUp     = diPlus  >= diMinus;             // DI+ dominant
            bool diDn     = diMinus >  diPlus;              // DI- dominant
            bool diUp1    = diPlus1 >= diMinus1;            // DI+ dominant last bar
            bool diDn1    = diMinus1 > diPlus1;             // DI- dominant last bar
            bool diUpUp   = diPlus  >= LevelTrend;          // strong bull (DI+ >= 35)
            bool diDnDn   = diMinus >= LevelTrend;          // strong bear (DI- >= 35)
            // crossDi: DI cross occurred this bar (either direction)
            bool crossDi  = (diPlus >= diMinus) != (diPlus1 >= diMinus1);

            // ── STEP 4: Pine entry/exit conditions ─────────────────────────
            // Direct port of Pine entry logic.
            bool entryLongStr  = !hlRange && diUp  && sigUp && diUpUp;
            bool entryShortStr = !hlRange && diDn  && sigUp && diDnDn;
            bool entryLong     = (!hlRange && diUp  && sigUp && !diUp1) ||
                                  (!hlRange && diUp  && sigUp && sig > LevelRange && hlRange1);
            bool entryShort    = (!hlRange && diDn  && sigUp && !diDn1) ||
                                  (!hlRange && diDn  && sigUp && sig > LevelRange && hlRange1);
            bool exitLong      = (crossDi && diUp1) || (hlRange && !hlRange1);
            bool exitShort     = (crossDi && diDn1) || (hlRange && !hlRange1);

            // ── STEP 5: Condition state machine ────────────────────────────
            // Pine: condition is a persistent variable that transitions on specific events.
            // C# Series<double> preserves history, matching Pine's persistent behavior.
            double prevCond = condSeries[1];
            double newCond;
            if      (prevCond != 1.0   && entryLongStr)  newCond =  1.0;
            else if (prevCond != -1.0  && entryShortStr) newCond = -1.0;
            else if (prevCond != 0.5   && entryLong)     newCond =  0.5;
            else if (prevCond != -0.5  && entryShort)    newCond = -0.5;
            else if (prevCond != 0.0   && exitLong)      newCond =  0.0;
            else if (prevCond != 0.0   && exitShort)     newCond =  0.0;
            else                                          newCond =  prevCond; // hold
            condSeries[0] = newCond;

            RefreshRegimeState();

            // ── Session reset ──────────────────────────────────────────────
            if (Bars.IsFirstBarOfSession)
            {
                consecutiveLosers  = 0;
                trailingStopLong   = double.NaN;
                trailingStopShort  = double.NaN;
                sessionStartProfit = SystemPerformance.AllTrades
                                         .TradesPerformance.Currency.CumProfit;
            }

            // ── TRANSITION: immediate flat ─────────────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat && finalRegime == "TRANSITION")
            {
                if (Position.MarketPosition == MarketPosition.Long)  ExitLong ("TransitionExit", LEntry);
                else                                                  ExitShort("TransitionExit", SEntry);
                trailingStopLong = trailingStopShort = double.NaN;
                return;
            }

            // ── ROTATION_ILLIQUID: market gone dead ────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat && finalRegime == "ROTATION_ILLIQUID")
            {
                if (Position.MarketPosition == MarketPosition.Long)  ExitLong ("IlliquidExit", LEntry);
                else                                                  ExitShort("IlliquidExit", SEntry);
                trailingStopLong = trailingStopShort = double.NaN;
                return;
            }

            // ── Condition-based exit (replaces StopX from A/B) ────────────
            // When condition transitions to 0, the Gu5 signal says the trend is over.
            // This fires instead of the ADX floor check from Versions A/B.
            bool condExitLong  = Position.MarketPosition == MarketPosition.Long  &&
                                 prevCond != 0 && newCond == 0;
            bool condExitShort = Position.MarketPosition == MarketPosition.Short &&
                                 prevCond != 0 && newCond == 0;

            if (condExitLong)
            {
                ExitLong("CondExit", LEntry);
                trailingStopLong = double.NaN;
                return;
            }
            if (condExitShort)
            {
                ExitShort("CondExit", SEntry);
                trailingStopShort = double.NaN;
                return;
            }

            // ── Bar-N trailing stop (runs while in position, before entry) ─
            if (Position.MarketPosition == MarketPosition.Long)
            {
                int bse = BarsSinceEntryExecution(0, LEntry, 0);
                if (bse >= TrailNBars)
                {
                    double candidate = BarNStopLong();
                    trailingStopLong = double.IsNaN(trailingStopLong)
                        ? candidate : Math.Max(trailingStopLong, candidate);
                    SetStopLoss(LEntry, CalculationMode.Price, trailingStopLong, false);
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                int bse = BarsSinceEntryExecution(0, SEntry, 0);
                if (bse >= TrailNBars)
                {
                    double candidate = BarNStopShort();
                    trailingStopShort = double.IsNaN(trailingStopShort)
                        ? candidate : Math.Min(trailingStopShort, candidate);
                    SetStopLoss(SEntry, CalculationMode.Price, trailingStopShort, false);
                }
            }

            // ==================================================================
            // ENTRY — fires on condition TRANSITION only
            // ==================================================================
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                trailingStopLong  = double.NaN;
                trailingStopShort = double.NaN;

                // Gate 1: data integrity
                if (parseFailed || staleDataFlag)              return;
                // Gate 2: circuit breaker
                if (consecutiveLosers >= MaxConsecutiveLosses) return;
                // Gate 3: time window
                if (!IsInTime())                               return;
                // Gate 4: phase (open noise and cash close blocked)
                if (BlockedPhases.Contains(phase))            return;
                // Gate 5: regime
                bool regimeOk = finalRegime == "ROTATION_LIQUID" || finalRegime == "TREND_COMPRESSION";
                if (!regimeOk)                                 return;
                // Gate 6: ROTATION_LIQUID specific structural requirements
                if (finalRegime == "ROTATION_LIQUID")
                {
                    if (twoSidedFlag != 1)          return;
                    if (ibWidthAtr < IbWidthAtrMin) return;
                }
                // Gate 7: hlRange chop filter — Gu5's primary noise suppression
                // If sig <= LevelRange, the market is in chop — no entries regardless of cross
                if (hlRange) return;
                // Gate 8: SizePct
                if (adxDiSizePct <= 0)                         return;
                // Gate 9: confidence floor
                if (regimeConfidence < MinConfidence)          return;

                // Gate 10: daily P&L guard
                if (DailyGoal > 0 || DailyLossLimit > 0)
                {
                    double dailyPnL = SystemPerformance.AllTrades
                                          .TradesPerformance.Currency.CumProfit
                                      - sessionStartProfit;
                    if (DailyGoal     > 0 && dailyPnL >=  DailyGoal)     return;
                    if (DailyLossLimit > 0 && dailyPnL <= -DailyLossLimit) return;
                }

                int maxC = CalcMaxContracts();
                int qty  = ScaleByConfidence(maxC, adxDiSizePct);

                // ── LONG ENTRY: condition transitioned to 1.0 or 0.5 ──────
                // condition[1] != target AND condition[0] == target = fresh transition
                bool longStrong = prevCond != 1.0   && newCond == 1.0;
                bool longWeak   = prevCond != 0.5   && newCond == 0.5;
                bool longEntry  = allowLong && (longStrong || (AllowWeakEntry && longWeak));

                // ── SHORT ENTRY: condition transitioned to -1.0 or -0.5 ───
                bool shortStrong = prevCond != -1.0  && newCond == -1.0;
                bool shortWeak   = prevCond != -0.5  && newCond == -0.5;
                bool shortEntry  = allowShort && (shortStrong || (AllowWeakEntry && shortWeak));

                if (longEntry)
                    SubmitLong(qty, longStrong ? "STRONG" : "WEAK", sig, diPlus - diMinus);
                else if (shortEntry)
                    SubmitShort(qty, shortStrong ? "STRONG" : "WEAK", sig, diMinus - diPlus);
            }
        }

        // =====================================================================
        // ORDER SUBMISSION — Version C: no fixed target, condition drives exit
        // =====================================================================
        private const string LEntry = "AdxDiCL";
        private const string SEntry = "AdxDiCS";

        private void SubmitLong(int qty, string strength, double sig, double diGap)
        {
            if (!allowLong || staleDataFlag || parseFailed) return;
            double risk = atr[0] * AtrMultiplier;
            double stp  = RT(Close[0] - risk);
            SetStopLoss(LEntry, CalculationMode.Price, stp, false);
            // No SetProfitTarget — condition state machine drives the exit
            trailingStopLong = stp;
            EnterLong(qty, LEntry);

            Print(string.Format(
                "[ADX_DI_V3D-C] LONG entry | Strength:{0} | Regime:{1} | Conf:{2} | Phase:{3} | " +
                "Reason:{4} | Sig:{5:F1} | DiGap:{6:F1} | SizePct:{7} | Qty:{8} | Stop:{9:F2}",
                strength, finalRegime, regimeConfidence, phase, reasonCode,
                sig, diGap, adxDiSizePct, qty, stp));
        }

        private void SubmitShort(int qty, string strength, double sig, double diGap)
        {
            if (!allowShort || staleDataFlag || parseFailed) return;
            double risk = atr[0] * AtrMultiplier;
            double stp  = RT(Close[0] + risk);
            SetStopLoss(SEntry, CalculationMode.Price, stp, false);
            // No SetProfitTarget — condition state machine drives the exit
            trailingStopShort = stp;
            EnterShort(qty, SEntry);

            Print(string.Format(
                "[ADX_DI_V3D-C] SHORT entry | Strength:{0} | Regime:{1} | Conf:{2} | Phase:{3} | " +
                "Reason:{4} | Sig:{5:F1} | DiGap:{6:F1} | SizePct:{7} | Qty:{8} | Stop:{9:F2}",
                strength, finalRegime, regimeConfidence, phase, reasonCode,
                sig, diGap, adxDiSizePct, qty, stp));
        }
    }
}