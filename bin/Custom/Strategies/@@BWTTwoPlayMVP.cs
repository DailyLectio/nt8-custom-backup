#region Using
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
#endregion

// -------------------------------------------------------------
// BWT_TwoPlay_MVP  (simple, testable 2-play engine)
// - Levels from IB (09:30-10:30). Lock at 10:30.
// - Plays: 2->4 (momentum), 2->2 (rotation)
// - Windows: 09:31–10:30, 10:31–12:00
// - Gates: VWAP align, EMA(8/24) on 1m, CI(HA) <= 60, 3m Vol% >= 125% (latched)
// - Stops: nearest ladder ± 2 pts
// - Targets: scale/final as described
// - Logger: one CSV row per trade
// -------------------------------------------------------------
namespace NinjaTrader.NinjaScript.Strategies
{
    public class BWT_TwoPlay_MVP : Strategy
    {
        // ---------- Inputs (lean "priorities bundle") ----------
        [NinjaScriptProperty, Display(Name="Contract Qty", GroupName="Risk", Order=0)]
        public int Quantity { get; set; } = 2;

        [NinjaScriptProperty, Display(Name="Band ticks (acceptance)", GroupName="Levels", Order=0)]
        public int BandTicks { get; set; } = 2;

        [NinjaScriptProperty, Display(Name="K2 (fraction of IB range)", GroupName="Levels", Order=1)]
        public double K2 { get; set; } = 0.35;

        [NinjaScriptProperty, Display(Name="K4 (fraction of IB range)", GroupName="Levels", Order=2)]
        public double K4 { get; set; } = 0.70;

        [NinjaScriptProperty, Display(Name="Use VWAP Align", GroupName="Gates", Order=0)]
        public bool GateVWAP { get; set; } = true;

        [NinjaScriptProperty, Display(Name="Require EMA8>EMA24 (long) / EMA8<EMA24 (short)", GroupName="Gates", Order=1)]
        public bool GateEMA { get; set; } = true;

        [NinjaScriptProperty, Display(Name="Use CI(HA) <= 60", GroupName="Gates", Order=2)]
        public bool GateCI { get; set; } = true;

        [NinjaScriptProperty, Display(Name="Use 3m Vol% >= 1.25", GroupName="Gates", Order=3)]
        public bool GateVol3m { get; set; } = true;

        [NinjaScriptProperty, Display(Name="CI Period (bars, 1m series)", GroupName="Gates", Order=4)]
        public int CIPeriod { get; set; } = 14;

        [NinjaScriptProperty, Display(Name="3m Vol latch (minutes)", GroupName="Gates", Order=5)]
        public int VolLatchMinutes { get; set; } = 3;

        [NinjaScriptProperty, Display(Name="Enable Play A (2->4)", GroupName="Plays", Order=0)]
        public bool EnablePlayA { get; set; } = true;

        [NinjaScriptProperty, Display(Name="Enable Play B (2->2)", GroupName="Plays", Order=1)]
        public bool EnablePlayB { get; set; } = true;

        [NinjaScriptProperty, Display(Name="Early Window Start (HHmmss)", GroupName="Windows", Order=0)]
        public int EarlyStart { get; set; } =  93100;
        [NinjaScriptProperty, Display(Name="Early Window End (HHmmss)", GroupName="Windows", Order=1)]
        public int EarlyEnd   { get; set; } = 103000;
        [NinjaScriptProperty, Display(Name="Post Window Start (HHmmss)", GroupName="Windows", Order=2)]
        public int PostStart  { get; set; } = 103100;
        [NinjaScriptProperty, Display(Name="Post Window End (HHmmss)", GroupName="Windows", Order=3)]
        public int PostEnd    { get; set; } = 120000;

        // ---------- Internal ----------
        private SessionIterator sess;
        private DateTime curDay = Core.Globals.MinDate;
        private bool levelsLocked = false;
        private double ibHigh, ibLow, M, R, B2, B4, R2, R4, B1, R1, B3, R3;
        private double tickSize;

        // 1m / 3m / 5m series indexes (added)
        private int idx1m = -1, idx3m = -1, idx5m = -1;

        // 1m HA + CI calc
        private double haOpen, haClose; // we maintain minimal HA for CI
        private Queue<double> trQueue = new Queue<double>();
        private double sumTR = 0; // for CI (sum true range over CIPeriod)
        private Queue<double> hhQueue = new Queue<double>();
        private Queue<double> llQueue = new Queue<double>();

        // 3m vol spike latch
        private bool volSpikeLatched = false;
        private DateTime volLatchUntil = Core.Globals.MinDate;

        // Session VWAP on 1m
        private double vwapCumPV = 0, vwapCumV = 0, vwap = double.NaN;

        // EMA(8/24) on 1m
        private double ema8 = double.NaN, ema24 = double.NaN;
        private readonly double ema8Alpha = 2.0 / (8 + 1.0);
        private readonly double ema24Alpha = 2.0 / (24 + 1.0);

        // Trade state
        private bool inTrade = false;
        private string playActive = "";
               private string sideActive = "";
        private double entryPrice, stopPrice, t1Price, t2Price;
        private DateTime entryTime;
        private double maeTicks, mfeTicks;
        private string windowTag = "";

        // CSV logger
        private StreamWriter log;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "BWT_TwoPlay_MVP";
                Calculate = Calculate.OnBarClose;     // stable & replay-friendly
                EntriesPerDirection = 2;              // scale at T1
                EntryHandling = EntryHandling.UniqueEntries;
                IsUnmanaged = false;
                IsInstantiatedOnEachOptimizationIteration = false;
                BarsRequiredToTrade = 50;
            }
            else if (State == State.Configure)
            {
                // Add secondary series regardless of execution chart
                AddDataSeries(BarsPeriodType.Minute, 1); idx1m = 1;
                AddDataSeries(BarsPeriodType.Minute, 3); idx3m = 2;
                AddDataSeries(BarsPeriodType.Minute, 5); idx5m = 3;
            }
            else if (State == State.DataLoaded)
            {
                tickSize = Instrument.MasterInstrument.TickSize;
                sess = new SessionIterator(BarsArray[0]);
                PrepareLog();
            }
            else if (State == State.Terminated)
            {
                try { log?.Dispose(); } catch {}
            }
        }

        private void PrepareLog()
        {
            try
            {
                var dir = Path.Combine(Core.Globals.UserDataDir, "StrategyLogs", Name, Instrument.FullName, Times[0][0].ToString("yyyy-MM"));
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, Times[0][0].ToString("yyyy-MM-dd") + ".csv");
                bool newFile = !File.Exists(path);
                log = new StreamWriter(path, true) { AutoFlush = true };
                if (newFile)
                {
                    log.WriteLine("date,instrument,exec_chart,window,play,side,IB_H,IB_L,M,B2,B4,R2,R4,entry_time,entry_price,stop_price,t1_price,t2_price,exit_time,exit_price,exit_reason,mae_ticks,mfe_ticks,ci_value,vol3m_ratio,vwap_align,ema_ok,confidence");
                }
            }
            catch (Exception ex)
            {
                Print("[BWT] Logger init failed: " + ex.Message);
            }
        }

        // ---------- Utility ----------
        private int HMS(DateTime t) => t.Hour*10000 + t.Minute*100 + t.Second;
        private double Ticks(double points) => points / tickSize;
        private bool InWindow(DateTime t)
        {
            int h = HMS(t);
            if (h >= EarlyStart && h <= EarlyEnd) { windowTag = "early"; return true; }
            if (h >= PostStart  && h <= PostEnd)  { windowTag = "post";  return true; }
            windowTag = "";
            return false;
        }
        private bool NearLevel(double price, double level) =>
            Math.Abs(price - level) <= BandTicks * tickSize ||
            (High[0] >= level - BandTicks*tickSize && Low[0] <= level + BandTicks*tickSize);

        private void ResetSession()
        {
            levelsLocked = false;
            ibHigh = double.MinValue; ibLow = double.MaxValue;
            M = R = B2 = B4 = R2 = R4 = B1 = R1 = B3 = R3 = double.NaN;
            // 1m VWAP + CI state
            vwapCumPV = 0; vwapCumV = 0; vwap = double.NaN;
            trQueue.Clear(); sumTR = 0; hhQueue.Clear(); llQueue.Clear();
            volSpikeLatched = false; volLatchUntil = Core.Globals.MinDate;
        }

        // ---------- Multi-series processing ----------
        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < BarsRequiredToTrade || CurrentBars.Length < 4) return;

            // Detect new session on primary
            DateTime day = sess.GetTradingDay(Times[0][0]);
            if (day != curDay)
            {
                curDay = day;
                ResetSession();
                inTrade = false; playActive = ""; sideActive = "";
            }

            // 1-min series updates: VWAP, EMA8/24, CI(HA), IB accumulation, lock levels at 10:30
            if (BarsInProgress == idx1m)
            {
                var t = Times[idx1m][0];
                double o = Opens[idx1m][0], h = Highs[idx1m][0], l = Lows[idx1m][0], c = Closes[idx1m][0];
                double v = Volumes[idx1m][0];

                // Build minimal Heikin-Ashi OHLC (for CI on HA)
                double haClosePrev = haClose;
                haClose = (o + h + l + c) / 4.0;
                if (CurrentBars[idx1m] == 0)
                    haOpen = (o + c) / 2.0;
                else
                    haOpen = (haOpen + haClosePrev) / 2.0;
                double haHigh = Math.Max(h, Math.Max(haOpen, haClose));
                double haLow  = Math.Min(l, Math.Min(haOpen, haClose));

                // CI(HA) ~ 100*log10( sumTR / (HH-LL) ) / log10(N)
                int N = Math.Max(2, CIPeriod);
                double tr = Math.Max(haHigh - haLow, Math.Max(Math.Abs(haHigh - haClosePrev), Math.Abs(haLow - haClosePrev)));
                trQueue.Enqueue(tr); sumTR += tr;
                hhQueue.Enqueue(haHigh);
                llQueue.Enqueue(haLow);
                if (trQueue.Count > N) { sumTR -= trQueue.Dequeue(); }
                if (hhQueue.Count > N) { hhQueue.Dequeue(); }
                if (llQueue.Count > N) { llQueue.Dequeue(); }
                double HH = hhQueue.Count > 0 ? hhQueue.Max() : haHigh;
                double LL = llQueue.Count > 0 ? llQueue.Min() : haLow;
                double ciVal = double.NaN;
                if (HH > LL)
                {
                    ciVal = 100.0 * Math.Log10(Math.Max(1e-10, sumTR) / (HH - LL)) / Math.Log10(N);
                }

                // Session VWAP (1m)
                vwapCumPV += ((h + l + c) / 3.0) * v;
                vwapCumV  += v;
                vwap = vwapCumV > 0 ? vwapCumPV / vwapCumV : double.NaN;

                // EMA 8/24 (1m)
                ema8  = double.IsNaN(ema8)  ? c : ema8  + ema8Alpha  * (c - ema8);
                ema24 = double.IsNaN(ema24) ? c : ema24 + ema24Alpha * (c - ema24);

                // IB accumulation and lock at 10:30
                int hms = HMS(t);
                if (hms >= 93000 && hms < 103000)
                {
                    ibHigh = Math.Max(ibHigh, h);
                    ibLow  = Math.Min(ibLow,  l);
                }
                if (!levelsLocked && hms >= 103000)
                {
                    levelsLocked = true;
                    if (ibHigh == double.MinValue || ibLow == double.MaxValue) { ibHigh = h; ibLow = l; }
                    R = Math.Max(1 * tickSize, ibHigh - ibLow);
                    M = (ibHigh + ibLow) / 2.0;
                    B2 = M + K2 * R;  R2 = M - K2 * R;
                    B4 = M + K4 * R;  R4 = M - K4 * R;
                    // Define B1/R1 halfway between M and B2/R2, B3/R3 halfway between B2/B4 (or R2/R4)
                    B1 = M + 0.5 * (B2 - M);   R1 = M - 0.5 * (M - R2);
                    B3 = B2 + 0.5 * (B4 - B2); R3 = R2 - 0.5 * (R2 - R4);
                }

                // 3m vol latch expiry check (use primary clock for simplicity)
                if (volSpikeLatched && t >= volLatchUntil)
                {
                    volSpikeLatched = false;
                }

                // Cache CI/VWAP flags into series Tag to read from primary
                // (We just keep them as fields: ciVal, vwap, ema8/ema24, volSpikeLatched)
                // nothing else needed here
                return;
            }

            // 3-min series: volume spike ratio & latch
            if (BarsInProgress == idx3m)
            {
                double vNow = Volumes[idx3m][0];
                double vPrev = Volumes[idx3m][1];
                double ratio = (vPrev > 0 ? vNow / vPrev : 0);
                if (GateVol3m && ratio >= 1.25)
                {
                    volSpikeLatched = true;
                    volLatchUntil = Times[idx3m][0].AddMinutes(VolLatchMinutes);
                }
                return;
            }

            // 5-min series: could compute EMA trend slope if desired; we keep MVP simple
            if (BarsInProgress == idx5m) return;

            // ---------- Primary series (execution) ----------
            if (!levelsLocked) return;                          // wait for 10:30 lock
            if (!InWindow(Times[0][0])) return;                 // only trade inside windows

            // Build gate booleans from latest 1m states
            bool vwapAlignLong  = !GateVWAP || (Close[0] >= vwap);
            bool vwapAlignShort = !GateVWAP || (Close[0] <= vwap);
            bool emaOKLong      = !GateEMA  || (ema8 > ema24);
            bool emaOKShort     = !GateEMA  || (ema8 < ema24);

            // CI check (use last computed value from 1m)
            double ci = 100.0; // default "bad"
            if (hhQueue.Count > 0 && llQueue.Count > 0 && sumTR > 0 && CIPeriod > 1)
            {
                double HH = hhQueue.Max(), LL = llQueue.Min();
                if (HH > LL)
                    ci = 100.0 * Math.Log10(Math.Max(1e-10, sumTR) / (HH - LL)) / Math.Log10(Math.Max(2, CIPeriod));
            }
            bool ciOK = !GateCI || (ci <= 60.0);

            bool volOK = !GateVol3m || volSpikeLatched;

            // manage open trade
            if (inTrade)
            {
                // track MAE/MFE (in ticks)
                double unreal = (sideActive == "long") ? (Close[0] - entryPrice) : (entryPrice - Close[0]);
                mfeTicks = Math.Max(mfeTicks, unreal / tickSize);
                double adv = (sideActive == "long") ? (entryPrice - Low[0]) : (High[0] - entryPrice);
                maeTicks = Math.Max(maeTicks, adv / tickSize);
                return;
            }

            // ---------- Entry logic ----------
            double band = BandTicks * tickSize;

            // Play A (2->4): momentum
            if (EnablePlayA)
            {
                // Long side
                if (vwapAlignLong && emaOKLong && ciOK && volOK && NearLevel(Close[0], B2))
                {
                    EnterPlay("A", "long", B2, B3, B4, B1);
                    return;
                }
                // Short side
                if (vwapAlignShort && emaOKShort && ciOK && volOK && NearLevel(Close[0], R2))
                {
                    EnterPlay("A", "short", R2, R3, R4, R1);
                    return;
                }
            }

            // Play B (2->2): rotation (VWAP near / EMA not strongly trending; keep gates lighter)
            if (EnablePlayB)
            {
                double vwapDist = Math.Abs(Close[0] - vwap);
                bool nearVWAP = vwapDist <= 4 * tickSize; // default proximity; tweak later

                // Short from B2 to R2
                if (nearVWAP && ciOK && NearLevel(Close[0], B2) && Close[0] < M)
                {
                    EnterPlay("B", "short", B2, M, R2, M);
                    return;
                }
                // Long from R2 to B2
                if (nearVWAP && ciOK && NearLevel(Close[0], R2) && Close[0] > M)
                {
                    EnterPlay("B", "long", R2, M, B2, M);
                    return;
                }
            }
        }

        private void EnterPlay(string play, string side, double entryLvl, double t1Lvl, double t2Lvl, double stopRefLvl)
        {
            inTrade = true;
            playActive = play; sideActive = side;
            entryPrice = Instrument.MasterInstrument.RoundToTickSize(Close[0]);
            t1Price = Instrument.MasterInstrument.RoundToTickSize(t1Lvl);
            t2Price = Instrument.MasterInstrument.RoundToTickSize(t2Lvl);

            // Stop: ladder +/- 2 pts (ES points)
            double stopPts = 2.0;
            stopPrice = (side == "long")
                ? Math.Min(entryPrice - 8 * tickSize, stopRefLvl - stopPts) // ensure at least some room; conservative
                : Math.Max(entryPrice + 8 * tickSize, stopRefLvl + stopPts);

            entryTime = Times[0][0];
            maeTicks = mfeTicks = 0;

            // place two entries for scale management
            int q1 = Math.Max(1, Quantity / 2);
            int q2 = Math.Max(1, Quantity - q1);

            // Clear any prior dynamic targets
            SetStopLoss(CalculationMode.Price, stopPrice);
            if (play == "A")
            {
                SetProfitTarget("A_T1", CalculationMode.Price, t1Price);
                SetProfitTarget("A_T2", CalculationMode.Price, t2Price);
                if (side == "long")
                {
                    EnterLong(q1, "A_T1");
                    EnterLong(q2, "A_T2");
                }
                else
                {
                    EnterShort(q1, "A_T1");
                    EnterShort(q2, "A_T2");
                }
            }
            else
            {
                SetProfitTarget("B_T1", CalculationMode.Price, t1Price);
                SetProfitTarget("B_T2", CalculationMode.Price, t2Price);
                if (side == "long")
                {
                    EnterLong(q1, "B_T1"); EnterLong(q2, "B_T2");
                }
                else
                {
                    EnterShort(q1, "B_T1"); EnterShort(q2, "B_T2");
                }
            }

            // Minimal logger at entry
            string playTag = play == "A" ? "2to4" : "2to2";
            WriteRow(
                date: entryTime.ToString("yyyy-MM-dd"),
                execChart: Bars.BarsSeries.BarsPeriod.ToString(),
                window: windowTag,
                play: playTag,
                side: side,
                ib_h: ibHigh, ib_l: ibLow, m: M, b2: B2, b4: B4, r2: R2, r4: R4,
                entTime: entryTime.ToString("HH:mm:ss"), entPrice: entryPrice,
                stp: stopPrice, t1: t1Price, t2: t2Price,
                exitTime: "", exitPrice: double.NaN, exitReason: "open",
                ciVal: GetCIApprox(), vol3m: volSpikeLatched?1.0:0.0,
                vwapAlign: (side=="long" ? (Close[0]>=vwap?1:0):(Close[0]<=vwap?1:0)),
                emaOK: (side=="long" ? (ema8>ema24?1:0):(ema8<ema24?1:0)),
                confidence: ComputeConfidence()
            );
        }

        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            if (position.Account != Account || Instrument != position.Instrument) return;

            if (inTrade && marketPosition == MarketPosition.Flat)
            {
                // Trade closed: figure exit reason & price
                string reason = "target_or_stop";
                double exitPx = Close[0];
                WriteRow(
                    date: entryTime.ToString("yyyy-MM-dd"),
                    execChart: Bars.BarsSeries.BarsPeriod.ToString(),
                    window: windowTag, play: playActive=="A"?"2to4":"2to2",
                    side: sideActive,
                    ib_h: ibHigh, ib_l: ibLow, m: M, b2: B2, b4: B4, r2: R2, r4: R4,
                    entTime: entryTime.ToString("HH:mm:ss"), entPrice: entryPrice,
                    stp: stopPrice, t1: t1Price, t2: t2Price,
                    exitTime: Times[0][0].ToString("HH:mm:ss"), exitPrice: exitPx, exitReason: reason,
                    ciVal: GetCIApprox(), vol3m: volSpikeLatched?1.0:0.0,
                    vwapAlign: 0, emaOK: 0, confidence: ComputeConfidence(),
                    mae: maeTicks, mfe: mfeTicks
                );
                inTrade = false; playActive = ""; sideActive = "";
            }
        }

        private double GetCIApprox()
        {
            if (hhQueue.Count == 0 || llQueue.Count == 0 || sumTR <= 0) return double.NaN;
            double HH = hhQueue.Max(), LL = llQueue.Min();
            if (HH <= LL) return double.NaN;
            return 100.0 * Math.Log10(Math.Max(1e-10, sumTR) / (HH - LL)) / Math.Log10(Math.Max(2, CIPeriod));
        }
        private int ComputeConfidence()
        {
            int conf = 0;
            if (GateVWAP && !double.IsNaN(vwap)) conf += (Close[0] >= vwap ? 1 : 0);
            if (GateEMA  && !double.IsNaN(ema8) && !double.IsNaN(ema24)) conf += (ema8>ema24 ? 1 : 0);
            if (GateCI   && !double.IsNaN(GetCIApprox())) conf += (GetCIApprox() <= 60.0 ? 1 : 0);
            if (GateVol3m && volSpikeLatched) conf += 1;
            return conf;
        }

        private void WriteRow(string date, string execChart, string window, string play, string side,
                              double ib_h, double ib_l, double m, double b2, double b4, double r2, double r4,
                              string entTime, double entPrice, double stp, double t1, double t2,
                              string exitTime, double exitPrice, string exitReason,
                              double ciVal, double vol3m, int vwapAlign, int emaOK, int confidence,
                              double mae=0, double mfe=0)
        {
            try
            {
                log?.WriteLine(string.Join(",",
                    date, Instrument.FullName, execChart, window, play, side,
                    F(ib_h), F(ib_l), F(m), F(b2), F(b4), F(r2), F(r4),
                    entTime, F(entPrice), F(stp), F(t1), F(t2),
                    exitTime, F(exitPrice), exitReason,
                    F(mae), F(mfe),
                    F(ciVal), F(vol3m), vwapAlign, emaOK, confidence
                ));
            }
            catch (Exception ex)
            {
                Print("[BWT] Log write failed: " + ex.Message);
            }
        }

        private string F(double x) => double.IsNaN(x) ? "" : x.ToString(CultureInfo.InvariantCulture);
    }
}