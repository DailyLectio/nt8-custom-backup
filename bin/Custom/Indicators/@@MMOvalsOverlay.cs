#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows.Media;

using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// ========================================================================
// MM_OvalsOverlay — Pivot-origin measured-move ladders (Premarket + RTH)
//  • Premarket (08:00→09:30): tight multipliers (default 0.75/1.00/1.272, RB 1.618)
//  • RTH (≥09:30): standard multipliers (default 1.00/1.382/1.618, RB 2.00)
//  • Early lock by retrace (faster than Swing confirmation)
//  • Direction Mode: AutoImpulse / Bias / Up / Down
//  • Bias HUD (gap, ON slope×R², ON VWAP stretch)
//  • VWAP stretch optional gating for RB
//  • Freeze heavy math after RTH lock
// ========================================================================
namespace NinjaTrader.NinjaScript.Indicators
{
    public enum DirMode { AutoImpulse, Bias, Up, Down }
    public enum PremarketMode { FirstImpulse, LatestImpulse }

    public class MM_OvalsOverlay : Indicator
    {
        // ---------------- Inputs: Core impulse / pivots ----------------
        [NinjaScriptProperty, Display(Name = "SwingStrength", Order = 0, GroupName = "Impulse/Pivot")]
        public int SwingStrength { get; set; } = 5;

        [NinjaScriptProperty, Display(Name = "ATR Period", Order = 1, GroupName = "Impulse/Pivot")]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.0, 5.0)]
        [Display(Name = "ImpulseThreshold Frac (×ATR)", Order = 2, GroupName = "Impulse/Pivot")]
        public double AtrFracThreshold { get; set; } = 0.15;

        [NinjaScriptProperty, Display(Name = "Min Impulse (ticks)", Order = 3, GroupName = "Impulse/Pivot")]
        public int MinImpulseTicks { get; set; } = 6;

        [NinjaScriptProperty, Display(Name = "Confirm by Retrace (RTH)", Order = 4, GroupName = "Impulse/Pivot")]
        public bool UseRetraceConfirm { get; set; } = true;

        [NinjaScriptProperty, Display(Name = "Retrace Ticks (RTH)", Order = 5, GroupName = "Impulse/Pivot")]
        public int ConfirmRetraceTicks { get; set; } = 8;

        [NinjaScriptProperty, Display(Name = "Direction Mode", Order = 6, GroupName = "Impulse/Pivot")]
        public DirMode DirectionMode { get; set; } = DirMode.AutoImpulse;

        // ---------------- Targets visuals / gating ----------------
        [NinjaScriptProperty, Display(Name = "Show T3 (3rd blue)", Order = 10, GroupName = "Targets")]
        public bool ShowT3 { get; set; } = false;

        [NinjaScriptProperty, Display(Name = "Use VWAP RubberBand Confirm", Order = 11, GroupName = "Targets")]
        public bool UseVwapConfirm { get; set; } = false;

        [NinjaScriptProperty, Range(0.0, 10.0)]
        [Display(Name = "RB σ from ON VWAP (if confirm)", Order = 12, GroupName = "Targets")]
        public double RubberBandSigmas { get; set; } = 2.5;

        [NinjaScriptProperty, Range(0.0, 1.0)]
        [Display(Name = "Zone Width = max(MinTicks, L0×Frac)", Order = 13, GroupName = "Zone Widths")]
        public double ZoneWidthFrac { get; set; } = 0.10;

        [NinjaScriptProperty, Display(Name = "Zone Width Min (ticks)", Order = 14, GroupName = "Zone Widths")]
        public int ZoneWidthTicksMin { get; set; } = 6;

        [NinjaScriptProperty, Range(0.0, 1.0)]
        [Display(Name = "Retest Width = max(MinTicks, L0×Frac)", Order = 15, GroupName = "Zone Widths")]
        public double RetestFrac { get; set; } = 0.15;

        [NinjaScriptProperty, Display(Name = "Retest Min (ticks)", Order = 16, GroupName = "Zone Widths")]
        public int RetestTicksMin { get; set; } = 4;

        // ---------------- Premarket block (08:00) ----------------
        [NinjaScriptProperty, Display(Name = "Enable Premarket Block", Order = 0, GroupName = "Premarket")]
        public bool EnablePremarket { get; set; } = true;

        [NinjaScriptProperty, Display(Name = "Premarket Start Hour (local)", Order = 1, GroupName = "Premarket")]
        public int PremarketStartHour { get; set; } = 8;

        [NinjaScriptProperty, Display(Name = "Premarket Mode", Order = 2, GroupName = "Premarket")]
        public PremarketMode PremktMode { get; set; } = PremarketMode.FirstImpulse;

        [NinjaScriptProperty, Display(Name = "Confirm by Retrace (Premarket)", Order = 3, GroupName = "Premarket")]
        public bool PreUseRetraceConfirm { get; set; } = true;

        [NinjaScriptProperty, Display(Name = "Retrace Ticks (Premarket)", Order = 4, GroupName = "Premarket")]
        public int PreConfirmRetraceTicks { get; set; } = 8;

        [NinjaScriptProperty, Range(0.0, 10.0)]
        [Display(Name = "Cap L0 ≤ (x × ATR) (Premkt)", Order = 5, GroupName = "Premarket")]
        public double PreCapL0xATR { get; set; } = 2.0;

        [NinjaScriptProperty, Display(Name = "Keep Premarket at Open", Order = 6, GroupName = "Premarket")]
        public bool KeepPremarketAtOpen { get; set; } = true;

        // Premarket multipliers
        [NinjaScriptProperty, Display(Name = "Pre T1 Mult", Order = 7, GroupName = "Premarket Multipliers")]
        public double PreT1Mult { get; set; } = 0.75;
        [NinjaScriptProperty, Display(Name = "Pre T2 Mult", Order = 8, GroupName = "Premarket Multipliers")]
        public double PreT2Mult { get; set; } = 1.00;
        [NinjaScriptProperty, Display(Name = "Pre T3 Mult", Order = 9, GroupName = "Premarket Multipliers")]
        public double PreT3Mult { get; set; } = 1.272;
        [NinjaScriptProperty, Display(Name = "Pre RB Mult", Order = 10, GroupName = "Premarket Multipliers")]
        public double PreRBMult { get; set; } = 1.618;

        // RTH multipliers
        [NinjaScriptProperty, Display(Name = "RTH T1 Mult", Order = 20, GroupName = "RTH Multipliers")]
        public double RthT1Mult { get; set; } = 1.00;
        [NinjaScriptProperty, Display(Name = "RTH T2 Mult", Order = 21, GroupName = "RTH Multipliers")]
        public double RthT2Mult { get; set; } = 1.382;
        [NinjaScriptProperty, Display(Name = "RTH T3 Mult", Order = 22, GroupName = "RTH Multipliers")]
        public double RthT3Mult { get; set; } = 1.618;
        [NinjaScriptProperty, Display(Name = "RTH RB Mult", Order = 23, GroupName = "RTH Multipliers")]
        public double RthRBMult { get; set; } = 2.00;

        // ---------------- Bias HUD ----------------
        [NinjaScriptProperty, Display(Name = "Show Bias HUD", Order = 30, GroupName = "Bias HUD")]
        public bool ShowBiasHud { get; set; } = true;

        [NinjaScriptProperty, Display(Name = "Bias HUD Position", Order = 31, GroupName = "Bias HUD")]
        public TextPosition BiasHudPosition { get; set; } = TextPosition.BottomRight;

        [NinjaScriptProperty, Display(Name = "Bias HUD Font Size", Order = 32, GroupName = "Bias HUD")]
        public int BiasHudFontSize { get; set; } = 18;

        // ---------------- Performance ----------------
        [NinjaScriptProperty, Display(Name = "Freeze After RTH Lock (stop heavy math)", Order = 50, GroupName = "Performance")]
        public bool FreezeAfterLock { get; set; } = true;

        // ---------------- Colors (self-serialization) ----------------
        [XmlIgnore, Display(Name = "Blue Fill", Order = 40, GroupName = "Colors")]
        public Brush BlueFill { get; set; } = new SolidColorBrush(Color.FromArgb(70, 64, 128, 255));
        [Browsable(false)] public string BlueFillSerializable { get { return BrushToString(BlueFill); } set { BlueFill = StringToBrush(value); } }

        [XmlIgnore, Display(Name = "Blue Outline", Order = 41, GroupName = "Colors")]
        public Brush BlueOutline { get; set; } = new SolidColorBrush(Color.FromArgb(255, 32, 96, 200));
        [Browsable(false)] public string BlueOutlineSerializable { get { return BrushToString(BlueOutline); } set { BlueOutline = StringToBrush(value); } }

        [XmlIgnore, Display(Name = "Green Fill", Order = 42, GroupName = "Colors")]
        public Brush GreenFill { get; set; } = new SolidColorBrush(Color.FromArgb(70, 64, 200, 64));
        [Browsable(false)] public string GreenFillSerializable { get { return BrushToString(GreenFill); } set { GreenFill = StringToBrush(value); } }

        [XmlIgnore, Display(Name = "Green Outline", Order = 43, GroupName = "Colors")]
        public Brush GreenOutline { get; set; } = new SolidColorBrush(Color.FromArgb(255, 20, 130, 20));
        [Browsable(false)] public string GreenOutlineSerializable { get { return BrushToString(GreenOutline); } set { GreenOutline = StringToBrush(value); } }

        [XmlIgnore, Display(Name = "Retest Fill", Order = 44, GroupName = "Colors")]
        public Brush RetestFill { get; set; } = new SolidColorBrush(Color.FromArgb(60, 200, 200, 200));
        [Browsable(false)] public string RetestFillSerializable { get { return BrushToString(RetestFill); } set { RetestFill = StringToBrush(value); } }

        // ---------------- State ----------------
        private ATR atr;
        private Swing swing;
        private StdDev std;
        private PriorDayOHLC prior;

        // ON / RTH VWAP
        private double rthCumPV, rthCumVol, rthVWAP;
        private double onVWAP, onStd;

        // Session times
        private bool sessionInitialized;
        private int sessionStartBar;
        private DateTime rthOpenTime;
        private DateTime todayDate;

        // RTH open & impulse
        private bool sessionOpenCaptured;
        private double sessionOpen;
        private bool impulseLocked;
        private bool directionDown;
        private double firstPivotPrice;     // origin for RTH ladders
        private double L0;                  // |pivot - open|
        private double T1, T2, T3, RB, zoneHalf, retestHalf;
        private bool t1Broken;

        // Premarket block
        private DateTime premktStartTime;
        private bool premktStartCaptured;
        private double premktStartPrice;
        private bool premktLocked;
        private double premktPivotPrice;    // origin for pre ladders
        private double premktL0;
        private double preT1, preT2, preT3, preRB, preZoneHalf, preRetestHalf;
        private bool premktT1Broken;

        // Running extremes (for retrace confirm)
        private double sessionHighSinceOpen, sessionLowSinceOpen;
        private double preHighSinceStart, preLowSinceStart;

        // Draw tags
        private string tagPrefix;
        private string pmTag;

        // Overnight stats (for RB confirm + HUD)
        private double onCumPV, onCumVol;

        // ---- Brush (de)serialization helpers ----
        private static string BrushToString(Brush b)
        {
            var scb = b as SolidColorBrush;
            if (scb == null) return "";
            var c = scb.Color;
            return $"{c.A},{c.R},{c.G},{c.B}";
        }
        private static Brush StringToBrush(string s)
        {
            try
            {
                var p = (s ?? "").Split(',');
                if (p.Length == 4)
                {
                    byte A = byte.Parse(p[0]), R = byte.Parse(p[1]), G = byte.Parse(p[2]), B = byte.Parse(p[3]);
                    var br = new SolidColorBrush(Color.FromArgb(A, R, G, B));
                    if (br.CanFreeze) br.Freeze();
                    return br;
                }
            }
            catch { }
            var fallback = new SolidColorBrush(Colors.Transparent);
            if (fallback.CanFreeze) fallback.Freeze();
            return fallback;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "MM_OvalsOverlay";
                Description = "Pivot-origin measured-move ladders (Premarket + RTH) with Bias HUD.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.DataLoaded)
            {
                atr   = ATR(AtrPeriod);
                swing = Swing(SwingStrength);
                std   = StdDev(30);
                prior = PriorDayOHLC();

                Freeze(BlueFill);   Freeze(BlueOutline);
                Freeze(GreenFill);  Freeze(GreenOutline);
                Freeze(RetestFill);
            }
        }

        private void Freeze(Brush b) { if (b != null && b.CanFreeze) b.Freeze(); }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(200, SwingStrength + 4))
                return;

            if (!sessionInitialized || Bars.IsFirstBarOfSession)
                InitSession();

            // Maintain ON stats (for HUD + RB confirm)
            BuildOvernightStatsIncremental();

            // ---------------- Premarket block (08:00 → 09:30) ----------------
            if (EnablePremarket && Time[0] >= premktStartTime && Time[0] < rthOpenTime)
            {
                PremarketProcess();
                if (ShowBiasHud) DrawBiasHud();
                return; // during premarket we don't process RTH yet
            }

            // ---------------- RTH block (≥ 09:30) ----------------
            if (Time[0] >= rthOpenTime)
            {
                if (!premktStartCaptured && EnablePremarket && !KeepPremarketAtOpen)
                    ClearPremarketDraws();

                if (!sessionOpenCaptured)
                {
                    int idx = Bars.GetBar(rthOpenTime);
                    sessionOpen = (idx >= 0 ? Open[idx] : Open[0]);
                    sessionOpenCaptured = true;
                }

                UpdateRthVwap();

                if (!impulseLocked)
                {
                    TryLockRthByRetraceOrSwing();
                    if (!impulseLocked)
                    {
                        if (ShowBiasHud) DrawBiasHud();
                        return;
                    }

                    // Once locked, compute targets from the pivot (not open)
                    ComputeTargetsFromMultipliers(
                        firstPivotPrice, L0, directionDown,
                        RthT1Mult, RthT2Mult, RthT3Mult, RthRBMult,
                        out T1, out T2, out T3, out RB, out zoneHalf, out retestHalf);

                    // Optional: remove pre ladders at the bell if requested
                    if (EnablePremarket && !KeepPremarketAtOpen)
                        ClearPremarketDraws();
                }

                DrawTargetsAndRetest(tagPrefix, T1, T2, T3, RB, zoneHalf, retestHalf, ref t1Broken);

                if (ShowBiasHud && (!FreezeAfterLock || !impulseLocked))
                    DrawBiasHud();

                if (FreezeAfterLock && impulseLocked)
                    return;
            }
        }

        private void InitSession()
        {
            sessionInitialized = true;
            sessionStartBar = CurrentBar;
            todayDate = Times[0][0].Date;

            // Times
            rthOpenTime = new DateTime(todayDate.Year, todayDate.Month, todayDate.Day, 9, 30, 0, Times[0][0].Kind);
            premktStartTime = new DateTime(todayDate.Year, todayDate.Month, todayDate.Day, Math.Max(0, PremarketStartHour), 0, 0, Times[0][0].Kind);

            // Open & extremes
            sessionOpenCaptured = false;
            sessionOpen = Close[0];
            sessionHighSinceOpen = double.MinValue;
            sessionLowSinceOpen  = double.MaxValue;

            // RTH impulse
            impulseLocked = false;
            directionDown = false;
            L0 = 0;
            firstPivotPrice = double.NaN;
            t1Broken = false;

            // Premarket
            premktStartCaptured = false;
            premktLocked = false;
            premktPivotPrice = double.NaN;
            premktL0 = 0;
            preHighSinceStart = double.MinValue;
            preLowSinceStart  = double.MaxValue;
            premktT1Broken = false;

            // ON stats
            rthCumPV = rthCumVol = 0;
            onCumPV  = onCumVol  = 0;
            onVWAP = onStd = 0;

            // Tags
            tagPrefix = string.Format("{0}_{1:yyyyMMdd}", Instrument.MasterInstrument.Name, todayDate);
            pmTag     = tagPrefix + "_PM";
        }

        // ---------------- Premarket processing ----------------
        private void PremarketProcess()
        {
            // capture 08:00 start price once
            if (!premktStartCaptured)
            {
                int idx = Bars.GetBar(premktStartTime);
                premktStartPrice = (idx >= 0 ? Close[idx] : Close[0]);
                premktStartCaptured = true;
                preHighSinceStart = premktStartPrice;
                preLowSinceStart  = premktStartPrice;
            }

            // track extremes
            preHighSinceStart = Math.Max(preHighSinceStart, High[0]);
            preLowSinceStart  = Math.Min(preLowSinceStart,  Low[0]);

            // threshold
            double thr = Math.Max(MinImpulseTicks * TickSize, AtrFracThreshold * atr[0]);

            bool upLegOk   = (preHighSinceStart - premktStartPrice) >= thr;
            bool downLegOk = (premktStartPrice - preLowSinceStart)  >= thr;

            bool doLock = false;
            bool preDown = false;
            double pivot = double.NaN;

            if (PreUseRetraceConfirm)
            {
                double retr = PreConfirmRetraceTicks * TickSize;
                bool upLocked   = upLegOk   && (preHighSinceStart - Close[0]) >= retr;
                bool downLocked = downLegOk && (Close[0] - preLowSinceStart)  >= retr;

                if (upLocked)  { doLock = true; preDown = false; pivot = preHighSinceStart; }
                if (downLocked){ doLock = true; preDown = true;  pivot = preLowSinceStart;  }
            }
            else
            {
                // fallback: use most recent Swing pivot inside the window
                int lookback = CurrentBar - Bars.GetBar(premktStartTime);
                int loBA = swing.SwingLowBar(0, 1, Math.Max(lookback, 10));
                int hiBA = swing.SwingHighBar(0, 1, Math.Max(lookback, 10));
                if (hiBA >= 0 && upLegOk)   { doLock = true; preDown = false; pivot = High[hiBA]; }
                if (loBA >= 0 && downLegOk) { doLock = true; preDown = true;  pivot = Low[loBA];  }
            }

            if (doLock)
            {
                // Origin = pivot, L0 = |pivot - 08:00 price|
                premktPivotPrice = pivot;
                premktL0 = Math.Abs(premktPivotPrice - premktStartPrice);

                // Optional cap (avoid wild news spikes before 8:00)
                if (PreCapL0xATR > 0)
                {
                    double cap = PreCapL0xATR * atr[0];
                    premktL0 = Math.Min(premktL0, cap);
                }

                // Direction override (Bias/Up/Down)
                switch (DirectionMode)
                {
                    case DirMode.Bias: preDown = ComputeBiasScore() < 0; break;
                    case DirMode.Up:   preDown = false; break;
                    case DirMode.Down: preDown = true;  break;
                }

                // Compute tight premarket ladders
                ComputeTargetsFromMultipliers(
                    premktPivotPrice, premktL0, preDown,
                    PreT1Mult, PreT2Mult, PreT3Mult, PreRBMult,
                    out preT1, out preT2, out preT3, out preRB, out preZoneHalf, out preRetestHalf);

                premktLocked = (PremktMode == PremarketMode.FirstImpulse) ? true : premktLocked;

                // Draw/refresh
                DrawTargets(pmTag, preT1, preT2, preT3, preRB, preZoneHalf);
                // Premarket retest off T1 if it breaks decidedly
                DrawTargetsAndRetest(pmTag, preT1, preT2, preT3, preRB, preZoneHalf, preRetestHalf, ref premktT1Broken);
            }
            else if (premktLocked && PremktMode == PremarketMode.LatestImpulse)
            {
                // in rolling mode we simply redraw existing zones each bar so they persist
                DrawTargets(pmTag, preT1, preT2, preT3, preRB, preZoneHalf);
            }
        }

        private void ClearPremarketDraws()
        {
            foreach (var suffix in new[] { "_T1", "_T2", "_T3", "_RB", "_RETEST_T1" })
                RemoveDrawObject(pmTag + suffix);
        }

        // ---------------- RTH processing ----------------
        private void TryLockRthByRetraceOrSwing()
        {
            double thr = Math.Max(MinImpulseTicks * TickSize, AtrFracThreshold * atr[0]);

            // track extremes since open
            sessionHighSinceOpen = Math.Max(sessionHighSinceOpen, High[0]);
            sessionLowSinceOpen  = Math.Min(sessionLowSinceOpen,  Low[0]);

            if (UseRetraceConfirm)
            {
                double retr = ConfirmRetraceTicks * TickSize;
                bool upOk   = (sessionHighSinceOpen - sessionOpen) >= thr && (sessionHighSinceOpen - Close[0]) >= retr;
                bool downOk = (sessionOpen - sessionLowSinceOpen)  >= thr && (Close[0] - sessionLowSinceOpen)  >= retr;

                if (upOk)
                {
                    directionDown = false;
                    firstPivotPrice = sessionHighSinceOpen;
                    L0 = firstPivotPrice - sessionOpen;
                    impulseLocked = true;
                }
                else if (downOk)
                {
                    directionDown = true;
                    firstPivotPrice = sessionLowSinceOpen;
                    L0 = sessionOpen - firstPivotPrice;
                    impulseLocked = true;
                }
            }
            else
            {
                int lookback = CurrentBar - sessionStartBar;
                int loBA = swing.SwingLowBar(0, 1, Math.Max(lookback, 10));
                if (loBA >= 0)
                {
                    double p = Low[loBA];
                    if ((sessionOpen - p) >= thr)
                    {
                        directionDown = true;
                        firstPivotPrice = p;
                        L0 = sessionOpen - p;
                        impulseLocked = true;
                    }
                }
                if (!impulseLocked)
                {
                    int hiBA = swing.SwingHighBar(0, 1, Math.Max(lookback, 10));
                    if (hiBA >= 0)
                    {
                        double p = High[hiBA];
                        if ((p - sessionOpen) >= thr)
                        {
                            directionDown = false;
                            firstPivotPrice = p;
                            L0 = p - sessionOpen;
                            impulseLocked = true;
                        }
                    }
                }
            }

            if (impulseLocked)
            {
                switch (DirectionMode)
                {
                    case DirMode.Bias: directionDown = ComputeBiasScore() < 0; break;
                    case DirMode.Up:   directionDown = false; break;
                    case DirMode.Down: directionDown = true;  break;
                }
            }
        }

        private void ComputeTargetsFromMultipliers(
            double origin, double L,
            bool down,
            double m1, double m2, double m3, double mRB,
            out double _T1, out double _T2, out double _T3, out double _RB,
            out double _zoneHalf, out double _retestHalf)
        {
            if (down)
            {
                _T1 = origin - m1 * L;
                _T2 = origin - m2 * L;
                _T3 = origin - m3 * L;
                _RB = origin - mRB * L;
            }
            else
            {
                _T1 = origin + m1 * L;
                _T2 = origin + m2 * L;
                _T3 = origin + m3 * L;
                _RB = origin + mRB * L;
            }

            _zoneHalf   = Math.Max(ZoneWidthTicksMin * TickSize, ZoneWidthFrac * L);
            _retestHalf = Math.Max(RetestTicksMin   * TickSize, RetestFrac    * L);
        }

        private void DrawTargetsAndRetest(string tagBase, double t1, double t2, double t3, double rb, double zHalf, double rHalf, ref bool t1WasBroken)
        {
            DrawTargets(tagBase, t1, t2, t3, rb, zHalf);

            if (!t1WasBroken)
            {
                if (directionDown && Low[0] <= (t1 - zHalf - 2 * TickSize)) t1WasBroken = true;
                if (!directionDown && High[0] >= (t1 + zHalf + 2 * TickSize)) t1WasBroken = true;
            }
            if (t1WasBroken)
                DrawZone(tagBase + "_RETEST_T1", t1, rHalf, Brushes.Gray, RetestFill);
        }

        private void DrawTargets(string tagBase, double t1, double t2, double t3, double rb, double zHalf)
        {
            bool rbOk = true;
            if (UseVwapConfirm && onStd > 0)
            {
                double dev = Math.Abs(Close[0] - onVWAP);
                rbOk = dev >= RubberBandSigmas * onStd;
            }

            DrawZone(tagBase + "_T1", t1, zHalf, BlueOutline, BlueFill);
            DrawZone(tagBase + "_T2", t2, zHalf, BlueOutline, BlueFill);
            if (ShowT3) DrawZone(tagBase + "_T3", t3, zHalf, BlueOutline, BlueFill);
            if (rbOk)   DrawZone(tagBase + "_RB", rb, zHalf, GreenOutline, GreenFill);
        }

        private void DrawZone(string tag, double center, double half, Brush outline, Brush fill)
        {
            int startBarsAgo = Math.Max(0, CurrentBar - sessionStartBar);
            Draw.Rectangle(this, tag, true,
                startBarsAgo, center + half,
                0,            center - half,
                outline, fill, 2);
        }

        // ---------------- ON/RTH stats + HUD ----------------
        private void UpdateRthVwap()
        {
            double tp = (High[0] + Low[0] + Close[0]) / 3.0;
            double v  = Math.Max(1.0, Volume[0]);
            rthCumPV  += tp * v;
            rthCumVol += v;
            rthVWAP = rthCumPV / Math.Max(1.0, rthCumVol);
        }

        private void BuildOvernightStatsIncremental()
        {
            // Build ON VWAP + stddev from 02:00 to ~open-5m (same window as before)
            DateTime start = new DateTime(todayDate.Year, todayDate.Month, todayDate.Day, 2, 0, 0, Time[0].Kind);
            DateTime end   = rthOpenTime.AddMinutes(-5);
            DateTime t = Times[0][0];

            if (t < start || t > end) return;

            double v  = Math.Max(1.0, Volume[0]);
            double tp = (High[0] + Low[0] + Close[0]) / 3.0;
            onCumPV += tp * v; onCumVol += v;
            onVWAP = (onCumVol > 0 ? onCumPV / onCumVol : onVWAP);

            // rough stddev via 1-pass Welford could be added; keep light:
            // use StdDev(30) as proxy over ON window
            onStd = std[0];
        }

        private int ComputeBiasScore()
        {
            double pdH = prior.PriorHigh[0];
            double pdL = prior.PriorLow[0];
            double pdC = prior.PriorClose[0];
            int score = 0;

            if (!double.IsNaN(pdH) && !double.IsNaN(pdL) && !double.IsNaN(pdC))
            {
                double gap = Close[0] - pdC;
                double halfATR = 0.5 * atr[0];
                if (Close[0] > pdH + TickSize) score += 3;
                else if (Close[0] < pdL - TickSize) score -= 3;
                else
                {
                    if (gap >  halfATR) score += 1;
                    if (gap < -halfATR) score -= 1;
                }
            }

            // ON slope proxy via stddev level; keep same logic weight as before
            double slope, r2;
            GetOvernightSlopeR2(out slope, out r2);
            if (!double.IsNaN(r2))
            {
                if (r2 >= 0.40) score += (slope >= 0 ? 2 : -2);
                else if (r2 >= 0.25) score += (slope >= 0 ? 1 : -1);
            }

            if (onStd > 0)
            {
                double dev = Math.Abs(Close[0] - onVWAP);
                if (dev >= 1.5 * onStd) score += (Close[0] < onVWAP ? 1 : -1);
            }
            return score;
        }

        private void DrawBiasHud()
        {
            double pdH = prior.PriorHigh[0];
            double pdL = prior.PriorLow[0];
            double pdC = prior.PriorClose[0];

            int score = ComputeBiasScore();
            string dir = score > 0 ? "Bull Bias" : score < 0 ? "Bear Bias" : "Neutral";
            string hud = $"Bias {score}  ({dir})\n" +
                         $"PDH {pdH:0.00}  PDL {pdL:0.00}  PDC {pdC:0.00}\n" +
                         $"ON VWAP {onVWAP:0.00}  σ {onStd:0.00}";

            RemoveDrawObject(tagPrefix + "_BIAS");
            var font = new SimpleFont("Arial", Math.Max(8, BiasHudFontSize));
            Draw.TextFixed(this, tagPrefix + "_BIAS", hud, BiasHudPosition,
                Brushes.White, font, Brushes.DimGray, Brushes.DimGray, 70);
        }

        private void GetOvernightSlopeR2(out double slope, out double r2)
        {
            slope = double.NaN; r2 = double.NaN;

            DateTime start = new DateTime(todayDate.Year, todayDate.Month, todayDate.Day, 2, 0, 0, Time[0].Kind);
            DateTime end   = rthOpenTime.AddMinutes(-5);

            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0, sumY2 = 0;
            int n = 0, t0 = 0;

            for (int barsAgo = CurrentBar; barsAgo >= 0; barsAgo--)
            {
                DateTime tt = Times[0][barsAgo];
                if (tt < start) break;
                if (tt > end) continue;

                double y = Close[barsAgo];
                int x = (int)(tt - start).TotalMinutes;
                if (n == 0) t0 = x;
                int xr = x - t0;

                sumX += xr; sumY += y; sumXY += xr * y; sumX2 += xr * xr; sumY2 += y * y;
                n++;
            }
            if (n >= 3)
            {
                double denom = (n * sumX2 - sumX * sumX);
                if (Math.Abs(denom) < 1e-9) return;
                slope = (n * sumXY - sumX * sumY) / denom;
                double ssTot = n * sumY2 - sumY * sumY;
                double ssReg = slope * (n * sumXY - sumX * sumY);
                r2 = (ssTot > 0 ? Math.Max(0.0, Math.Min(1.0, ssReg / ssTot)) : 0.0);
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MM_OvalsOverlay[] cacheMM_OvalsOverlay;
		public MM_OvalsOverlay MM_OvalsOverlay(int swingStrength, int atrPeriod, double atrFracThreshold, int minImpulseTicks, bool useRetraceConfirm, int confirmRetraceTicks, DirMode directionMode, bool showT3, bool useVwapConfirm, double rubberBandSigmas, double zoneWidthFrac, int zoneWidthTicksMin, double retestFrac, int retestTicksMin, bool enablePremarket, int premarketStartHour, PremarketMode premktMode, bool preUseRetraceConfirm, int preConfirmRetraceTicks, double preCapL0xATR, bool keepPremarketAtOpen, double preT1Mult, double preT2Mult, double preT3Mult, double preRBMult, double rthT1Mult, double rthT2Mult, double rthT3Mult, double rthRBMult, bool showBiasHud, TextPosition biasHudPosition, int biasHudFontSize, bool freezeAfterLock)
		{
			return MM_OvalsOverlay(Input, swingStrength, atrPeriod, atrFracThreshold, minImpulseTicks, useRetraceConfirm, confirmRetraceTicks, directionMode, showT3, useVwapConfirm, rubberBandSigmas, zoneWidthFrac, zoneWidthTicksMin, retestFrac, retestTicksMin, enablePremarket, premarketStartHour, premktMode, preUseRetraceConfirm, preConfirmRetraceTicks, preCapL0xATR, keepPremarketAtOpen, preT1Mult, preT2Mult, preT3Mult, preRBMult, rthT1Mult, rthT2Mult, rthT3Mult, rthRBMult, showBiasHud, biasHudPosition, biasHudFontSize, freezeAfterLock);
		}

		public MM_OvalsOverlay MM_OvalsOverlay(ISeries<double> input, int swingStrength, int atrPeriod, double atrFracThreshold, int minImpulseTicks, bool useRetraceConfirm, int confirmRetraceTicks, DirMode directionMode, bool showT3, bool useVwapConfirm, double rubberBandSigmas, double zoneWidthFrac, int zoneWidthTicksMin, double retestFrac, int retestTicksMin, bool enablePremarket, int premarketStartHour, PremarketMode premktMode, bool preUseRetraceConfirm, int preConfirmRetraceTicks, double preCapL0xATR, bool keepPremarketAtOpen, double preT1Mult, double preT2Mult, double preT3Mult, double preRBMult, double rthT1Mult, double rthT2Mult, double rthT3Mult, double rthRBMult, bool showBiasHud, TextPosition biasHudPosition, int biasHudFontSize, bool freezeAfterLock)
		{
			if (cacheMM_OvalsOverlay != null)
				for (int idx = 0; idx < cacheMM_OvalsOverlay.Length; idx++)
					if (cacheMM_OvalsOverlay[idx] != null && cacheMM_OvalsOverlay[idx].SwingStrength == swingStrength && cacheMM_OvalsOverlay[idx].AtrPeriod == atrPeriod && cacheMM_OvalsOverlay[idx].AtrFracThreshold == atrFracThreshold && cacheMM_OvalsOverlay[idx].MinImpulseTicks == minImpulseTicks && cacheMM_OvalsOverlay[idx].UseRetraceConfirm == useRetraceConfirm && cacheMM_OvalsOverlay[idx].ConfirmRetraceTicks == confirmRetraceTicks && cacheMM_OvalsOverlay[idx].DirectionMode == directionMode && cacheMM_OvalsOverlay[idx].ShowT3 == showT3 && cacheMM_OvalsOverlay[idx].UseVwapConfirm == useVwapConfirm && cacheMM_OvalsOverlay[idx].RubberBandSigmas == rubberBandSigmas && cacheMM_OvalsOverlay[idx].ZoneWidthFrac == zoneWidthFrac && cacheMM_OvalsOverlay[idx].ZoneWidthTicksMin == zoneWidthTicksMin && cacheMM_OvalsOverlay[idx].RetestFrac == retestFrac && cacheMM_OvalsOverlay[idx].RetestTicksMin == retestTicksMin && cacheMM_OvalsOverlay[idx].EnablePremarket == enablePremarket && cacheMM_OvalsOverlay[idx].PremarketStartHour == premarketStartHour && cacheMM_OvalsOverlay[idx].PremktMode == premktMode && cacheMM_OvalsOverlay[idx].PreUseRetraceConfirm == preUseRetraceConfirm && cacheMM_OvalsOverlay[idx].PreConfirmRetraceTicks == preConfirmRetraceTicks && cacheMM_OvalsOverlay[idx].PreCapL0xATR == preCapL0xATR && cacheMM_OvalsOverlay[idx].KeepPremarketAtOpen == keepPremarketAtOpen && cacheMM_OvalsOverlay[idx].PreT1Mult == preT1Mult && cacheMM_OvalsOverlay[idx].PreT2Mult == preT2Mult && cacheMM_OvalsOverlay[idx].PreT3Mult == preT3Mult && cacheMM_OvalsOverlay[idx].PreRBMult == preRBMult && cacheMM_OvalsOverlay[idx].RthT1Mult == rthT1Mult && cacheMM_OvalsOverlay[idx].RthT2Mult == rthT2Mult && cacheMM_OvalsOverlay[idx].RthT3Mult == rthT3Mult && cacheMM_OvalsOverlay[idx].RthRBMult == rthRBMult && cacheMM_OvalsOverlay[idx].ShowBiasHud == showBiasHud && cacheMM_OvalsOverlay[idx].BiasHudPosition == biasHudPosition && cacheMM_OvalsOverlay[idx].BiasHudFontSize == biasHudFontSize && cacheMM_OvalsOverlay[idx].FreezeAfterLock == freezeAfterLock && cacheMM_OvalsOverlay[idx].EqualsInput(input))
						return cacheMM_OvalsOverlay[idx];
			return CacheIndicator<MM_OvalsOverlay>(new MM_OvalsOverlay(){ SwingStrength = swingStrength, AtrPeriod = atrPeriod, AtrFracThreshold = atrFracThreshold, MinImpulseTicks = minImpulseTicks, UseRetraceConfirm = useRetraceConfirm, ConfirmRetraceTicks = confirmRetraceTicks, DirectionMode = directionMode, ShowT3 = showT3, UseVwapConfirm = useVwapConfirm, RubberBandSigmas = rubberBandSigmas, ZoneWidthFrac = zoneWidthFrac, ZoneWidthTicksMin = zoneWidthTicksMin, RetestFrac = retestFrac, RetestTicksMin = retestTicksMin, EnablePremarket = enablePremarket, PremarketStartHour = premarketStartHour, PremktMode = premktMode, PreUseRetraceConfirm = preUseRetraceConfirm, PreConfirmRetraceTicks = preConfirmRetraceTicks, PreCapL0xATR = preCapL0xATR, KeepPremarketAtOpen = keepPremarketAtOpen, PreT1Mult = preT1Mult, PreT2Mult = preT2Mult, PreT3Mult = preT3Mult, PreRBMult = preRBMult, RthT1Mult = rthT1Mult, RthT2Mult = rthT2Mult, RthT3Mult = rthT3Mult, RthRBMult = rthRBMult, ShowBiasHud = showBiasHud, BiasHudPosition = biasHudPosition, BiasHudFontSize = biasHudFontSize, FreezeAfterLock = freezeAfterLock }, input, ref cacheMM_OvalsOverlay);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MM_OvalsOverlay MM_OvalsOverlay(int swingStrength, int atrPeriod, double atrFracThreshold, int minImpulseTicks, bool useRetraceConfirm, int confirmRetraceTicks, DirMode directionMode, bool showT3, bool useVwapConfirm, double rubberBandSigmas, double zoneWidthFrac, int zoneWidthTicksMin, double retestFrac, int retestTicksMin, bool enablePremarket, int premarketStartHour, PremarketMode premktMode, bool preUseRetraceConfirm, int preConfirmRetraceTicks, double preCapL0xATR, bool keepPremarketAtOpen, double preT1Mult, double preT2Mult, double preT3Mult, double preRBMult, double rthT1Mult, double rthT2Mult, double rthT3Mult, double rthRBMult, bool showBiasHud, TextPosition biasHudPosition, int biasHudFontSize, bool freezeAfterLock)
		{
			return indicator.MM_OvalsOverlay(Input, swingStrength, atrPeriod, atrFracThreshold, minImpulseTicks, useRetraceConfirm, confirmRetraceTicks, directionMode, showT3, useVwapConfirm, rubberBandSigmas, zoneWidthFrac, zoneWidthTicksMin, retestFrac, retestTicksMin, enablePremarket, premarketStartHour, premktMode, preUseRetraceConfirm, preConfirmRetraceTicks, preCapL0xATR, keepPremarketAtOpen, preT1Mult, preT2Mult, preT3Mult, preRBMult, rthT1Mult, rthT2Mult, rthT3Mult, rthRBMult, showBiasHud, biasHudPosition, biasHudFontSize, freezeAfterLock);
		}

		public Indicators.MM_OvalsOverlay MM_OvalsOverlay(ISeries<double> input , int swingStrength, int atrPeriod, double atrFracThreshold, int minImpulseTicks, bool useRetraceConfirm, int confirmRetraceTicks, DirMode directionMode, bool showT3, bool useVwapConfirm, double rubberBandSigmas, double zoneWidthFrac, int zoneWidthTicksMin, double retestFrac, int retestTicksMin, bool enablePremarket, int premarketStartHour, PremarketMode premktMode, bool preUseRetraceConfirm, int preConfirmRetraceTicks, double preCapL0xATR, bool keepPremarketAtOpen, double preT1Mult, double preT2Mult, double preT3Mult, double preRBMult, double rthT1Mult, double rthT2Mult, double rthT3Mult, double rthRBMult, bool showBiasHud, TextPosition biasHudPosition, int biasHudFontSize, bool freezeAfterLock)
		{
			return indicator.MM_OvalsOverlay(input, swingStrength, atrPeriod, atrFracThreshold, minImpulseTicks, useRetraceConfirm, confirmRetraceTicks, directionMode, showT3, useVwapConfirm, rubberBandSigmas, zoneWidthFrac, zoneWidthTicksMin, retestFrac, retestTicksMin, enablePremarket, premarketStartHour, premktMode, preUseRetraceConfirm, preConfirmRetraceTicks, preCapL0xATR, keepPremarketAtOpen, preT1Mult, preT2Mult, preT3Mult, preRBMult, rthT1Mult, rthT2Mult, rthT3Mult, rthRBMult, showBiasHud, biasHudPosition, biasHudFontSize, freezeAfterLock);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MM_OvalsOverlay MM_OvalsOverlay(int swingStrength, int atrPeriod, double atrFracThreshold, int minImpulseTicks, bool useRetraceConfirm, int confirmRetraceTicks, DirMode directionMode, bool showT3, bool useVwapConfirm, double rubberBandSigmas, double zoneWidthFrac, int zoneWidthTicksMin, double retestFrac, int retestTicksMin, bool enablePremarket, int premarketStartHour, PremarketMode premktMode, bool preUseRetraceConfirm, int preConfirmRetraceTicks, double preCapL0xATR, bool keepPremarketAtOpen, double preT1Mult, double preT2Mult, double preT3Mult, double preRBMult, double rthT1Mult, double rthT2Mult, double rthT3Mult, double rthRBMult, bool showBiasHud, TextPosition biasHudPosition, int biasHudFontSize, bool freezeAfterLock)
		{
			return indicator.MM_OvalsOverlay(Input, swingStrength, atrPeriod, atrFracThreshold, minImpulseTicks, useRetraceConfirm, confirmRetraceTicks, directionMode, showT3, useVwapConfirm, rubberBandSigmas, zoneWidthFrac, zoneWidthTicksMin, retestFrac, retestTicksMin, enablePremarket, premarketStartHour, premktMode, preUseRetraceConfirm, preConfirmRetraceTicks, preCapL0xATR, keepPremarketAtOpen, preT1Mult, preT2Mult, preT3Mult, preRBMult, rthT1Mult, rthT2Mult, rthT3Mult, rthRBMult, showBiasHud, biasHudPosition, biasHudFontSize, freezeAfterLock);
		}

		public Indicators.MM_OvalsOverlay MM_OvalsOverlay(ISeries<double> input , int swingStrength, int atrPeriod, double atrFracThreshold, int minImpulseTicks, bool useRetraceConfirm, int confirmRetraceTicks, DirMode directionMode, bool showT3, bool useVwapConfirm, double rubberBandSigmas, double zoneWidthFrac, int zoneWidthTicksMin, double retestFrac, int retestTicksMin, bool enablePremarket, int premarketStartHour, PremarketMode premktMode, bool preUseRetraceConfirm, int preConfirmRetraceTicks, double preCapL0xATR, bool keepPremarketAtOpen, double preT1Mult, double preT2Mult, double preT3Mult, double preRBMult, double rthT1Mult, double rthT2Mult, double rthT3Mult, double rthRBMult, bool showBiasHud, TextPosition biasHudPosition, int biasHudFontSize, bool freezeAfterLock)
		{
			return indicator.MM_OvalsOverlay(input, swingStrength, atrPeriod, atrFracThreshold, minImpulseTicks, useRetraceConfirm, confirmRetraceTicks, directionMode, showT3, useVwapConfirm, rubberBandSigmas, zoneWidthFrac, zoneWidthTicksMin, retestFrac, retestTicksMin, enablePremarket, premarketStartHour, premktMode, preUseRetraceConfirm, preConfirmRetraceTicks, preCapL0xATR, keepPremarketAtOpen, preT1Mult, preT2Mult, preT3Mult, preRBMult, rthT1Mult, rthT2Mult, rthT3Mult, rthRBMult, showBiasHud, biasHudPosition, biasHudFontSize, freezeAfterLock);
		}
	}
}

#endregion
