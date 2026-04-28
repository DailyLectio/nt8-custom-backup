// CC BY-NC 4.0 — Scalp HUD (Futures) — NT8 v1.4
// Based on your v1.3.3 (internal VWAP, presets, filters, Debug HUD).
// ADD: Selectable Stop Source (Wick / ATR / Max / Min), ATR stop multiplier & length, MinStopTicks.
// Target = StopDistance * RiskReward.

#region Using
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.Gui.Chart;                 // TextPosition
using NinjaTrader.Gui.Tools;                 // SimpleFont
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;  // Draw.*
using NinjaTrader.Core.FloatingPoint;
using System.Windows.Media;                  // Brushes
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ScalpHUD_Futures_NT8_v1_4 : Strategy
    {
        // ===== Orders / Presets =====
        [NinjaScriptProperty, Display(Name="Contracts", Order=0, GroupName="Orders")]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty, Display(Name="Risk:Reward (Target = Stop×RR)", Order=1, GroupName="Orders")]
        public double RiskReward { get; set; } = 1.0;

        [NinjaScriptProperty, Display(Name="Auto-apply ES/NQ Presets", Order=2, GroupName="Presets")]
        public bool UseAutoPresets { get; set; } = true;

        // ===== Session / ORB =====
        [NinjaScriptProperty, Display(Name="Use Session RTH (09:30–16:00 NY)", Order=3, GroupName="Session")]
        public bool UseRth { get; set; } = true;

        [NinjaScriptProperty, Display(Name="ORB Minutes", Order=4, GroupName="Session / ORB")]
        public int OrbMinutes { get; set; } = 15;

        // ===== Core logic inputs =====
        [NinjaScriptProperty, Display(Name="EMA (Signal) Length", Order=10, GroupName="EMAs")]
        public int EmaFastLen { get; set; } = 13;

        [NinjaScriptProperty, Display(Name="EMA (Bias) Length", Order=11, GroupName="EMAs")]
        public int EmaBiasLen { get; set; } = 50;

        [NinjaScriptProperty, Display(Name="ADX Length", Order=20, GroupName="ADX / Chop")]
        public int AdxLen { get; set; } = 14;

        [NinjaScriptProperty, Display(Name="ADX Gate (fixed)", Order=21, GroupName="ADX / Chop")]
        public int AdxGate { get; set; } = 18;

        [NinjaScriptProperty, Display(Name="Chop Length", Order=22, GroupName="ADX / Chop")]
        public int ChopLen { get; set; } = 14;

        [NinjaScriptProperty, Display(Name="Chop must be below", Order=23, GroupName="ADX / Chop")]
        public double ChopCeil { get; set; } = 60;

        [NinjaScriptProperty, Display(Name="Key Level: Session VWAP (internal)", Order=30, GroupName="Key Levels")]
        public bool UseVwap { get; set; } = true;

        [NinjaScriptProperty, Display(Name="Key Level: ORB High/Low", Order=31, GroupName="Key Levels")]
        public bool UseOrb { get; set; } = true;

        [NinjaScriptProperty, Display(Name="Key Level: Daily Open", Order=32, GroupName="Key Levels")]
        public bool UseOpen { get; set; } = true;

        [NinjaScriptProperty, Display(Name="Recent-touch window (bars)", Order=33, GroupName="Key Levels")]
        public int KeyLookback { get; set; } = 10;

        [NinjaScriptProperty, Display(Name="Key-level proximity (ATR×)", Order=34, GroupName="Key Levels")]
        public double KeyProxAtr { get; set; } = 0.25;

        public enum ConfirmType { EMA3Over8, MACD_Hist_Increasing, RSI_Slope }
        [NinjaScriptProperty, Display(Name="Momentum confirmation", Order=40, GroupName="Momentum")]
        public ConfirmType ConfirmMode { get; set; } = ConfirmType.EMA3Over8;

        [NinjaScriptProperty, Display(Name="Show Entry Arrows", Order=50, GroupName="Visuals")]
        public bool ShowVisuals { get; set; } = true;

        // ===== Filters =====
        [NinjaScriptProperty, Display(Name="EMA No-Trade Band (ATR× vs EMA50)", Order=60, GroupName="Filters")]
        public double EmaNoTradeAtr { get; set; } = 0.30;

        [NinjaScriptProperty, Display(Name="Cooldown Bars After Exit", Order=61, GroupName="Filters")]
        public int CooldownBars { get; set; } = 5;

        [NinjaScriptProperty, Display(Name="Cooldown Seconds After Exit (0=off)", Order=62, GroupName="Filters")]
        public int CooldownSeconds { get; set; } = 0;

        [NinjaScriptProperty, Display(Name="Use ADX Percentile Gate", Order=63, GroupName="Filters")]
        public bool UseAdxPercentile { get; set; } = true;

        [NinjaScriptProperty, Display(Name="ADX Percentile Lookback", Order=64, GroupName="Filters")]
        public int AdxPercLookback { get; set; } = 200;

        [NinjaScriptProperty, Display(Name="ADX Percentile Threshold (e.g., 60 = 60th)", Order=65, GroupName="Filters")]
        public int AdxPercentile { get; set; } = 60;

        [NinjaScriptProperty, Display(Name="Avoid Round Numbers", Order=66, GroupName="Filters")]
        public bool AvoidRoundNumbers { get; set; } = true;

        [NinjaScriptProperty, Display(Name="Round Buffer (ticks) from whole/quarter/half", Order=67, GroupName="Filters")]
        public int RoundBufferTicks { get; set; } = 2;

        // ===== Debug HUD =====
        public enum HudPos { TopLeft, TopRight, BottomLeft, BottomRight, Center }
        [NinjaScriptProperty, Display(Name="Show Debug HUD (block reasons)", Order=80, GroupName="Debug HUD")]
        public bool ShowDebugHud { get; set; } = true;

        [NinjaScriptProperty, Display(Name="HUD Position", Order=81, GroupName="Debug HUD")]
        public HudPos HudPosition { get; set; } = HudPos.TopRight;

        [NinjaScriptProperty, Display(Name="HUD Font Size", Order=82, GroupName="Debug HUD")]
        public int HudFontSize { get; set; } = 12;

        // ===== NEW: Stop/Target controls =====
        public enum StopCalcMode { Wick, ATR, MaxOf_Wick_ATR, MinOf_Wick_ATR }

        [NinjaScriptProperty, Display(Name="Stop Source", Order=90, GroupName="Stops & Targets")]
        public StopCalcMode StopSource { get; set; } = StopCalcMode.MaxOf_Wick_ATR;

        [NinjaScriptProperty, Display(Name="ATR Stop Multiplier", Order=91, GroupName="Stops & Targets")]
        public double AtrStopMult { get; set; } = 0.50;

        [NinjaScriptProperty, Display(Name="ATR Stop Length", Order=92, GroupName="Stops & Targets")]
        public int AtrStopLen { get; set; } = 14;

        [NinjaScriptProperty, Display(Name="Minimum Stop (ticks)", Order=93, GroupName="Stops & Targets")]
        public int MinStopTicks { get; set; } = 2;

        // ===== State / Series =====
        EMA emaSig, emaBias, ema3, ema8;
        ADX adx;
        RSI rsi;
        MACD macd;
        Series<double> chSeries;
        MAX hh1; MIN ll1; ATR atr14; ATR atrStop;   // add dedicated ATR for stop

        // ORB / session tracking
        double orbH = double.NaN, orbL = double.NaN, dayOpen = double.NaN;
        DateTime sessionStart = Core.Globals.MinDate;
        bool orbFrozen = false;

        // Last signal wick
        double lastSigHigh = double.NaN, lastSigLow  = double.NaN;

        // Cooldown tracking
        MarketPosition prevPos = MarketPosition.Flat;
        int lastExitBar = int.MinValue;
        DateTime lastExitTime = DateTime.MinValue;

        // ADX rolling window
        readonly Queue<double> adxWindow = new Queue<double>();

        // Internal session VWAP (no Order Flow+)
        double vwapVal = double.NaN;
        double cumPV = 0.0;
        double cumVol = 0.0;

        // HUD font
        SimpleFont hudFont;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Scalp HUD (Futures) — NT8 v1.4";
                Description = "Scalp HUD logic w/ ES/NQ presets, filters, Debug HUD, internal VWAP, and ATR stop options.";
                Calculate = Calculate.OnEachTick;
                TraceOrders = false;
                IsUnmanaged = false;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IncludeCommission = true;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 5;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.DataLoaded)
            {
                // ---- Auto presets ----
                if (UseAutoPresets)
                {
                    string root = (Instrument?.MasterInstrument?.Name ?? "").ToUpperInvariant();
                    if (root.StartsWith("ES") || root == "MES")
                    {
                        Contracts   = 1;  RiskReward  = 1.0;  OrbMinutes = 15;
                        EmaFastLen  = 13; EmaBiasLen  = 50;
                        AdxLen      = 14; AdxGate     = 18;
                        ChopLen     = 14; ChopCeil    = 60;
                        KeyProxAtr  = 0.25; KeyLookback = 10;
                        ConfirmMode = ConfirmType.EMA3Over8; UseRth = true;

                        EmaNoTradeAtr = 0.25;
                        AdxPercLookback = 200; AdxPercentile = 60;
                        RoundBufferTicks = 2; CooldownBars = 5; CooldownSeconds = 0;

                        StopSource = StopCalcMode.MaxOf_Wick_ATR; AtrStopMult = 0.50; AtrStopLen = 14; MinStopTicks = 2;
                    }
                    else if (root.StartsWith("NQ") || root == "MNQ")
                    {
                        Contracts   = 1;  RiskReward  = 1.0;  OrbMinutes = 12;
                        EmaFastLen  = 13; EmaBiasLen  = 50;
                        AdxLen      = 14; AdxGate     = 20;
                        ChopLen     = 14; ChopCeil    = 55;
                        KeyProxAtr  = 0.35; KeyLookback = 12;
                        ConfirmMode = ConfirmType.EMA3Over8; UseRth = true;

                        EmaNoTradeAtr = 0.35;
                        AdxPercLookback = 200; AdxPercentile = 60;
                        RoundBufferTicks = 3; CooldownBars = 6; CooldownSeconds = 0;

                        StopSource = StopCalcMode.MaxOf_Wick_ATR; AtrStopMult = 0.50; AtrStopLen = 14; MinStopTicks = 3;
                    }
                }

                // Indicators
                emaSig = EMA(Close, EmaFastLen);
                emaBias= EMA(Close, EmaBiasLen);
                ema3   = EMA(Close, 3);
                ema8   = EMA(Close, 8);
                adx    = ADX(AdxLen);
                rsi    = RSI(Close, 14, 3);
                macd   = MACD(Close, 12, 26, 9);
                hh1    = MAX(High, ChopLen);
                ll1    = MIN(Low,  ChopLen);
                atr14  = ATR(14);
                atrStop= ATR(Math.Max(5, AtrStopLen));
                chSeries = new Series<double>(this);

                AddChartIndicator(emaSig);
                AddChartIndicator(emaBias);

                hudFont = new SimpleFont("Segoe UI", HudFontSize) { Bold = false };
            }
        }

        // ===== Helpers =====
        private bool InRthNow()
        {
            if (!UseRth) return true;
            int t = ToTime(Time[0]); // HHmmss
            return t >= 093000 && t < 160000;
        }

        private void UpdateSessionOrbAndVWAP()
        {
            bool newSession = Bars.IsFirstBarOfSession;

            if (newSession)
            {
                orbH = double.NaN; orbL = double.NaN; orbFrozen = false;
                dayOpen = double.NaN;
                sessionStart = Time[0];

                // reset session VWAP
                cumPV = 0.0; cumVol = 0.0; vwapVal = double.NaN;
            }

            // internal session VWAP (typical price × volume)
            double typical = (High[0] + Low[0] + Close[0]) / 3.0;
            double vol = Volume[0];
            cumPV  += typical * vol;
            cumVol += vol;
            vwapVal = cumVol.ApproxCompare(0) == 0 ? Close[0] : cumPV / Math.Max(cumVol, 1);

            if (double.IsNaN(dayOpen))
                dayOpen = Open[0];

            if (sessionStart != Core.Globals.MinDate)
            {
                var elapsed = Time[0] - sessionStart;
                bool inOrbWindow = elapsed.TotalMinutes <= OrbMinutes;

                if (inOrbWindow && !orbFrozen)
                {
                    orbH = double.IsNaN(orbH) ? High[0] : Math.Max(orbH, High[0]);
                    orbL = double.IsNaN(orbL) ? Low[0]  : Math.Min(orbL,  Low[0]);
                }
                else if (!inOrbWindow && !orbFrozen && !double.IsNaN(orbH) && !double.IsNaN(orbL))
                {
                    orbFrozen = true;
                }
            }
        }

        private double Choppiness()
        {
            double hh = hh1[0];
            double ll = ll1[0];
            double rng = Math.Max(ATR(1)[0] * 0.1, hh - ll);
            double sumTr = atr14[0] * ChopLen;
            double denom = Math.Log10(Math.Max(ChopLen, 2));
            double val = 100.0 * Math.Log10(sumTr / Math.Max(rng, TickSize)) / Math.Max(denom, 1e-9);
            return Math.Min(100, Math.Max(0, val));
        }

        private bool BullSignal() => CrossAbove(Close, emaSig, 1);
        private bool BearSignal() => CrossBelow(Close, emaSig, 1);

        private bool RsiOkL() { double rv = rsi[0]; return rv > 50 && rv > rsi[1]; }
        private bool RsiOkS() { double rv = rsi[0]; return rv < 50 && rv < rsi[1]; }

        private bool MomoLong()
        {
            switch (ConfirmMode)
            {
                case ConfirmType.EMA3Over8: return ema3[0] > ema8[0];
                case ConfirmType.MACD_Hist_Increasing:
                    double h = macd.Default[0] - macd.Avg[0];
                    double h1 = macd.Default[1] - macd.Avg[1];
                    return h > h1 && h > 0;
                default: return RsiOkL();
            }
        }
        private bool MomoShort()
        {
            switch (ConfirmMode)
            {
                case ConfirmType.EMA3Over8: return ema3[0] < ema8[0];
                case ConfirmType.MACD_Hist_Increasing:
                    double h = macd.Default[0] - macd.Avg[0];
                    double h1 = macd.Default[1] - macd.Avg[1];
                    return h < h1 && h < 0;
                default: return RsiOkS();
            }
        }

        private double RecentMinDiff(double level, int lookback)
        {
            double m = double.PositiveInfinity;
            int lb = Math.Min(lookback, CurrentBar + 1);
            for (int k = 0; k < lb; k++)
            {
                double d = Math.Abs(Close[k] - level);
                if (d < m) m = d;
            }
            return m;
        }

        private (bool keyHit, double bestDist) R2_KeyCalc(bool dirLong)
        {
            double atr = atr14[0];
            double thr = KeyProxAtr * atr;
            double bestDist = double.NaN;
            bool keyHit = false;

            List<double> levels = new List<double>();
            if (UseVwap && InRthNow() && !double.IsNaN(vwapVal)) levels.Add(vwapVal);
            if (UseOrb && orbFrozen && !double.IsNaN(orbH) && !double.IsNaN(orbL)) { levels.Add(orbH); levels.Add(orbL); }
            if (UseOpen && !double.IsNaN(dayOpen)) levels.Add(dayOpen);

            if (levels.Count == 0) return (false, double.NaN);

            foreach (var lvl in levels)
            {
                double recentMin = RecentMinDiff(lvl, KeyLookback);
                bool recentTouch = recentMin < thr;
                keyHit = keyHit || recentTouch;

                double dNow = Math.Abs(Close[0] - lvl);
                bestDist = double.IsNaN(bestDist) ? dNow : Math.Min(bestDist, dNow);
            }

            bool movingAway = dirLong ? Close[0] > Close[1] : Close[0] < Close[1];
            return (keyHit && movingAway, bestDist);
        }

        private void UpdateLastSignalWick()
        {
            if (BullSignal() || BearSignal())
            {
                lastSigHigh = High[0];
                lastSigLow  = Low[0];
            }
        }

        // ===== NEW: unified stop/target calc =====
        private void SetStopsAndTargets(bool longEntry, double entryPrice)
        {
            // Wick distance
            double wickDist = 0.0;
            if (longEntry)
            {
                if (!double.IsNaN(lastSigLow))
                    wickDist = entryPrice - lastSigLow;
            }
            else
            {
                if (!double.IsNaN(lastSigHigh))
                    wickDist = lastSigHigh - entryPrice;
            }

            // ATR distance (dedicated ATR for stops)
            double atrDist = Math.Max(TickSize, atrStop[0] * Math.Max(0.01, AtrStopMult));

            // Choose stop distance per StopSource
            double stopDistance;
            switch (StopSource)
            {
                case StopCalcMode.Wick:
                    stopDistance = wickDist;
                    break;
                case StopCalcMode.ATR:
                    stopDistance = atrDist;
                    break;
                case StopCalcMode.MinOf_Wick_ATR:
                    stopDistance = (wickDist > 0 && atrDist > 0) ? Math.Min(wickDist, atrDist) : Math.Max(wickDist, atrDist);
                    break;
                default: // MaxOf_Wick_ATR
                    stopDistance = Math.Max(wickDist, atrDist);
                    break;
            }

            // Fallback if invalid / too small
            double minTicks = Math.Max(1, MinStopTicks);
            double minDist = minTicks * TickSize;
            if (stopDistance <= TickSize || double.IsNaN(stopDistance))
                stopDistance = Math.Max(minDist, atr14[0] * 0.5);

            // Snap to ticks
            stopDistance = Math.Max(TickSize, Math.Round(stopDistance / TickSize) * TickSize);

            // Target distance is Stop × RiskReward
            double targetDistance = stopDistance * Math.Max(0.1, RiskReward);

            double stopPrice   = longEntry ? entryPrice - stopDistance : entryPrice + stopDistance;
            double targetPrice = longEntry ? entryPrice + targetDistance : entryPrice - targetDistance;

            SetStopLoss(CalculationMode.Price, stopPrice);
            SetProfitTarget(CalculationMode.Price, targetPrice);
        }

        private bool InEmaNoTradeZone()
        {
            if (EmaNoTradeAtr <= 0) return false;
            return Math.Abs(Close[0] - emaBias[0]) < atr14[0] * EmaNoTradeAtr;
        }

        private bool CooldownActive()
        {
            if (prevPos != Position.MarketPosition && Position.MarketPosition == MarketPosition.Flat)
            {
                lastExitBar = CurrentBar;
                lastExitTime = Time[0];
            }
            prevPos = Position.MarketPosition;

            if (lastExitBar == int.MinValue) return false;

            bool barsGate = CooldownBars > 0 && CurrentBar - lastExitBar < CooldownBars;
            bool secsGate = CooldownSeconds > 0 && (Time[0] - lastExitTime).TotalSeconds < CooldownSeconds;
            return barsGate || secsGate;
        }

        private double AdxPercentileThreshold()
        {
            adxWindow.Enqueue(adx[0]);
            while (adxWindow.Count > Math.Max(AdxPercLookback, 10)) adxWindow.Dequeue();

            if (adxWindow.Count < Math.Max(AdxPercLookback / 4, 20))
                return double.NegativeInfinity;

            var arr = adxWindow.ToArray();
            Array.Sort(arr);
            int p = Math.Max(0, Math.Min(100, AdxPercentile));
            int idx = (int)Math.Round((p / 100.0) * (arr.Length - 1));
            return arr[idx];
        }

        private bool AdxGateOk(out string reason)
        {
            reason = null;
            if (UseAdxPercentile)
            {
                double thresh = AdxPercentileThreshold();
                if (adxWindow.Count < Math.Max(AdxPercLookback / 4, 20))
                    return true; // warm-up
                if (adx[0] >= thresh) return true;
                reason = $"ADX<{AdxPercentile}th ({adx[0]:0.0}<{thresh:0.0})";
                return false;
            }
            else
            {
                if (adx[0] >= AdxGate) return true;
                reason = $"ADX<{AdxGate} ({adx[0]:0.0})";
                return false;
            }
        }

        private bool NearRoundNumber()
        {
            if (!AvoidRoundNumbers || RoundBufferTicks <= 0) return false;

            int ticksPerPoint = (int)Math.Round(1.0 / TickSize);
            int priceTicks    = (int)Math.Round(Close[0] / TickSize);

            int r = ((priceTicks % ticksPerPoint) + ticksPerPoint) % ticksPerPoint;

            int q  = Math.Max(1, ticksPerPoint / 4);
            int h  = Math.Max(1, ticksPerPoint / 2);
            int tq = Math.Max(1, (3 * ticksPerPoint) / 4);

            int[] keys = new int[] { 0, q, h, tq };
            foreach (var k in keys)
            {
                int d = Math.Min(Math.Abs(r - k), ticksPerPoint - Math.Abs(r - k));
                if (d <= RoundBufferTicks) return true;
            }
            return false;
        }

        private TextPosition MapHudPos()
        {
            switch (HudPosition)
            {
                case HudPos.TopLeft:     return TextPosition.TopLeft;
                case HudPos.TopRight:    return TextPosition.TopRight;
                case HudPos.BottomLeft:  return TextPosition.BottomLeft;
                case HudPos.BottomRight: return TextPosition.BottomRight;
                default:                 return TextPosition.Center;
            }
        }

        // ===== OnBarUpdate =====
        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(200, Math.Max(EmaBiasLen, Math.Max(EmaFastLen, Math.Max(AdxLen, ChopLen))))) return;
            if (BarsInProgress != 0) return;

            UpdateSessionOrbAndVWAP();
            UpdateLastSignalWick();

            double ch = Choppiness();
            chSeries[0] = ch;

            // --- Base rule checks ---
            bool r1_dir_long  = BullSignal() || (Close[0] > emaSig[0] && Close[0] > Open[0]);
            bool r1_dir_short = BearSignal() || (Close[0] < emaSig[0] && Close[0] < Open[0]);

            var r2L = R2_KeyCalc(true);
            var r2S = R2_KeyCalc(false);
            bool r2_long  = r2L.keyHit;
            bool r2_short = r2S.keyHit;

            bool r3_bias_long  = Close[0] > emaBias[0];
            bool r3_bias_short = Close[0] < emaBias[0];

            bool r4_clear_long  = (!double.IsNaN(lastSigHigh) && Close[0] > Math.Max(lastSigHigh, emaSig[0]));
            bool r4_clear_short = (!double.IsNaN(lastSigLow)  && Close[0] < Math.Min(lastSigLow,  emaSig[0]));

            bool momoLong  = MomoLong();
            bool momoShort = MomoShort();

            bool r7_chop_ok = (ch < ChopCeil) && (ch < chSeries[1]);

            string adxReason;
            bool r8_adx_ok = AdxGateOk(out adxReason);

            bool goLong_base  = r1_dir_long  && r2_long  && r3_bias_long  && r4_clear_long  && momoLong  && r7_chop_ok && r8_adx_ok;
            bool goShort_base = r1_dir_short && r2_short && r3_bias_short && r4_clear_short && momoShort && r7_chop_ok && r8_adx_ok;

            // Filters
            bool inRth    = InRthNow();
            bool emaBand  = InEmaNoTradeZone();
            bool cooldown = CooldownActive();
            bool nearRound= NearRoundNumber();

            bool goLong  = goLong_base;
            bool goShort = goShort_base;

            if (!inRth)    { goLong = false; goShort = false; }
            if (emaBand)   { goLong = false; goShort = false; }
            if (cooldown)  { goLong = false; goShort = false; }
            if (nearRound) { goLong = false; goShort = false; }

            // Debug HUD
            if (ShowDebugHud)
            {
                var blocksL = new List<string>();
                var blocksS = new List<string>();

                if (!r1_dir_long)   blocksL.Add("Dir");         if (!r1_dir_short)  blocksS.Add("Dir");
                if (!r2_long)       blocksL.Add("KeyLvl");      if (!r2_short)      blocksS.Add("KeyLvl");
                if (!r3_bias_long)  blocksL.Add("Bias<EMA50");  if (!r3_bias_short) blocksS.Add("Bias>EMA50");
                if (!r4_clear_long) blocksL.Add("Wick/EMA13");  if (!r4_clear_short)blocksS.Add("Wick/EMA13");
                if (!momoLong)      blocksL.Add("Momentum");    if (!momoShort)     blocksS.Add("Momentum");
                if (!r7_chop_ok)    { blocksL.Add($"Chop>{ChopCeil:0}"); blocksS.Add($"Chop>{ChopCeil:0}"); }
                if (!r8_adx_ok && adxReason != null) { blocksL.Add(adxReason); blocksS.Add(adxReason); }

                if (!inRth)    { blocksL.Add("Session OFF"); blocksS.Add("Session OFF"); }
                if (emaBand)   { blocksL.Add($"EMA Band<{EmaNoTradeAtr:0.00}×ATR"); blocksS.Add($"EMA Band<{EmaNoTradeAtr:0.00}×ATR"); }
                if (cooldown)  { blocksL.Add("Cooldown"); blocksS.Add("Cooldown"); }
                if (nearRound) { blocksL.Add($"Round±{RoundBufferTicks}t"); blocksS.Add($"Round±{RoundBufferTicks}t"); }

                string status = goLong ? "GO LONG" : goShort ? "GO SHORT" : "NO-GO";
                string line1 = $"Status: {status}  |  ADX:{adx[0]:0.0}  Chop:{ch:0}";
                string line2 = $"Blocks L: {(blocksL.Count==0 ? "—" : string.Join(", ", blocksL))}";
                string line3 = $"Blocks S: {(blocksS.Count==0 ? "—" : string.Join(", ", blocksS))}";
                string text = line1 + "\n" + line2 + "\n" + line3;

                Draw.TextFixed(this, "DBG_SCALP_HUD", text, MapHudPos(), Brushes.White, hudFont, Brushes.Black, Brushes.DimGray, 60);
            }

            // Entries
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (goLong)
                {
                    double entry = GetCurrentBid();
                    SetStopsAndTargets(true, entry);
                    EnterLong(Contracts, "GO_LONG");
                    if (ShowVisuals) Draw.ArrowUp(this, "L"+CurrentBar, false, 0, Low[0] - 2*TickSize, Brushes.Lime);
                }
                else if (goShort)
                {
                    double entry = GetCurrentAsk();
                    SetStopsAndTargets(false, entry);
                    EnterShort(Contracts, "GO_SHORT");
                    if (ShowVisuals) Draw.ArrowDown(this, "S"+CurrentBar, false, 0, High[0] + 2*TickSize, Brushes.Red);
                }
            }
        }
    }
}