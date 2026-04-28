#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class RotationHUD : Indicator
    {
        // =====================================================================
        //  GATES & SOURCES (kept from your v0)
        // =====================================================================
        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "Chop Length", GroupName = "Gates", Order = 0)]
        public int ChopLen { get; set; } = 14;

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Chop Min (rotation if ≥)", GroupName = "Gates", Order = 1)]
        public int ChopMin { get; set; } = 60;

        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "ADX Length", GroupName = "Gates", Order = 2)]
        public int AdxLen { get; set; } = 14;

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "ADX Max (rotation if ≤)", GroupName = "Gates", Order = 3)]
        public int AdxMax { get; set; } = 18;

        [NinjaScriptProperty, Range(10, 400)]
        [Display(Name = "Regression Window (bars)", GroupName = "Gates", Order = 4)]
        public int RegrWin { get; set; } = 60;

        // Value Area: manual or VWAP ± k·σ proxy
        [NinjaScriptProperty]
        [Display(Name = "Use Manual VA (type VAH/VAL/POC)", GroupName = "Value Area", Order = 10)]
        public bool UseManualVA { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Manual VAH", GroupName = "Value Area", Order = 11)]
        public double ManualVAH { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Manual VAL", GroupName = "Value Area", Order = 12)]
        public double ManualVAL { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Manual POC", GroupName = "Value Area", Order = 13)]
        public double ManualPOC { get; set; } = 0;

        [NinjaScriptProperty, Range(0.5, 3.0)]
        [Display(Name = "Proxy k (VWAP ± k·σ)", GroupName = "Value Area", Order = 14)]
        public double VAProxyK { get; set; } = 1.35;

        [NinjaScriptProperty, Range(20, 400)]
        [Display(Name = "Proxy σ Length", GroupName = "Value Area", Order = 15)]
        public int VAProxyLen { get; set; } = 120;

        // Initial Balance window
        [NinjaScriptProperty]
        [Display(Name = "IB Start (HH:mm)", GroupName = "Initial Balance", Order = 20)]
        public string IBStartStr { get; set; } = "09:30";

        [NinjaScriptProperty]
        [Display(Name = "IB End (HH:mm)", GroupName = "Initial Balance", Order = 21)]
        public string IBEndStr { get; set; } = "10:00";

        // =====================================================================
        //  ROT GO PROBABILITY (new)
        // =====================================================================
        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Rot Go Threshold %", GroupName = "Rotation GO", Order = 100)]
        public int RotGoThreshold { get; set; } = 65;

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Rot Yellow Floor %", GroupName = "Rotation GO", Order = 101)]
        public int RotYellowFloor { get; set; } = 50;

        [NinjaScriptProperty]
        [Display(Name = "Require Balance Gate", GroupName = "Rotation GO", Order = 102)]
        public bool RequireBalanceGate { get; set; } = true;

        [NinjaScriptProperty, Range(0.10, 0.50)]
        [Display(Name = "Value Edge Band (fraction of VA width)", GroupName = "Rotation GO", Order = 103)]
        public double RotEdgeBand { get; set; } = 0.30;

        [NinjaScriptProperty, Range(0.0, 3.0)]
        [Display(Name = "Max RV for Rotation", GroupName = "Rotation GO", Order = 104)]
        public double RotMaxRV { get; set; } = 1.8;

        [NinjaScriptProperty, Range(0.0, 2.0)]
        [Display(Name = "Max |UD| for Rotation", GroupName = "Rotation GO", Order = 105)]
        public double RotMaxAbsUD { get; set; } = 0.6;

        [NinjaScriptProperty, Range(0.0, 1.0)]
        [Display(Name = "EMA Slope Gate (ticks per bar)", GroupName = "Rotation GO", Order = 106)]
        public double RotMaxEmaSlope { get; set; } = 0.15;

        [NinjaScriptProperty, Range(-5.0, 5.0)]
        [Display(Name = "Rot Bias", GroupName = "Rotation GO", Order = 107)]
        public double RotBias { get; set; } = 0.0;

        [NinjaScriptProperty, Range(1, 10)]
        [Display(Name = "Acceptance Outside VA Bars", GroupName = "Rotation GO", Order = 108)]
        public int AcceptOutsideBars { get; set; } = 2;

        [NinjaScriptProperty, Range(1, 600)]
        [Display(Name = "Alert Cooldown (sec)", GroupName = "Rotation GO", Order = 109)]
        public int AlertCooldownSec { get; set; } = 90;

        // Weights for the logistic
        [NinjaScriptProperty, Range(0, 2)]
        [Display(Name = "w_CI", GroupName = "Rotation GO Weights", Order = 120)]
        public double w_CI { get; set; } = 1.0;

        [NinjaScriptProperty, Range(0, 2)]
        [Display(Name = "w_ADXFlat", GroupName = "Rotation GO Weights", Order = 121)]
        public double w_ADXFlat { get; set; } = 0.8;

        [NinjaScriptProperty, Range(0, 2)]
        [Display(Name = "w_Edge", GroupName = "Rotation GO Weights", Order = 122)]
        public double w_Edge { get; set; } = 1.2;

        [NinjaScriptProperty, Range(0, 2)]
        [Display(Name = "w_VWAPAlign", GroupName = "Rotation GO Weights", Order = 123)]
        public double w_VWAPAlign { get; set; } = 0.4;

        [NinjaScriptProperty, Range(0, 2)]
        [Display(Name = "w_RVPenalty", GroupName = "Rotation GO Weights", Order = 124)]
        public double w_RVPenalty { get; set; } = 0.6;

        [NinjaScriptProperty, Range(0, 2)]
        [Display(Name = "w_UDPenalty", GroupName = "Rotation GO Weights", Order = 125)]
        public double w_UDPenalty { get; set; } = 0.6;

        [NinjaScriptProperty, Range(0, 2)]
        [Display(Name = "w_SlopePenalty", GroupName = "Rotation GO Weights", Order = 126)]
        public double w_SlopePenalty { get; set; } = 0.6;

        // =====================================================================
        //  VISUAL
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name = "Badge Position", GroupName = "Visual", Order = 200)]
        public TextPosition BadgePosition { get; set; } = TextPosition.TopLeft;

        [NinjaScriptProperty]
        [Display(Name = "Show Lines (VA/POC/IB/Rails)", GroupName = "Visual", Order = 201)]
        public bool ShowLines { get; set; } = true;

        // =====================================================================
        //  INTERNALS
        // =====================================================================
        private ChoppinessIndex ci;
        private ADX adx;
        private EMA ema24;

        private Series<double> typ;
        private StdDev typStd;
        private Series<double> vwap;
        private double cumPV, cumVol;

        private Series<double> volD;
        private SMA volSma;

        private SimpleFont font;
        private DateTime curDay = DateTime.MinValue;
        private DateTime ibStart, ibEnd;
        private double vah, val, poc;

        // Regression stats
        private double regrCenter, regrSlope, regrSD;

        // Acceptance outside VA
        private int outsideCount = 0;
        private bool AcceptedOutsideValue => outsideCount >= AcceptOutsideBars;

        // ROT GO telemetry
        private DateTime lastRotAlert = DateTime.MinValue;

        // Brushes
        private Brush badgeGreen = Brushes.LimeGreen;
        private Brush badgeYellow = Brushes.Goldenrod;
        private Brush badgeRed = Brushes.IndianRed;
        private Brush textBrush = Brushes.White;

        // =====================================================================
        //  LIFECYCLE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "RotationHUD";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                DrawOnPricePanel = true;
            }
            else if (State == State.Configure)
            {
                font = new SimpleFont("Segoe UI", 13) { Bold = true };
            }
            else if (State == State.DataLoaded)
            {
                ci = ChoppinessIndex(ChopLen);
                adx = ADX(AdxLen);
                ema24 = EMA(24);

                typ = new Series<double>(this);
                typStd = StdDev(typ, VAProxyLen);

                vwap = new Series<double>(this);

                volD = new Series<double>(this);
                volSma = SMA(volD, 50);
            }
        }

        // =====================================================================
        //  CORE
        // =====================================================================
        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(Math.Max(ChopLen, AdxLen), Math.Max(VAProxyLen, 50)))
                return;

            // New day → refresh IB times
            DateTime d = Times[0][0].Date;
            if (d != curDay)
            {
                curDay = d;
                ParseIBTimes();
                outsideCount = 0;
            }

            // --- session VWAP + σ proxy inputs
            typ[0] = (High[0] + Low[0] + Close[0]) / 3.0;

            if (Bars.IsFirstBarOfSession)
            {
                cumPV = 0;
                cumVol = 0;
            }
            cumPV += typ[0] * Volume[0];
            cumVol += Volume[0];
            vwap[0] = cumVol != 0 ? cumPV / cumVol : typ[0];

            if (UseManualVA && ManualVAH > 0 && ManualVAL > 0 && ManualPOC > 0)
            {
                vah = ManualVAH;
                val = ManualVAL;
                poc = ManualPOC;
            }
            else
            {
                double sigma = Math.Max(TickSize, typStd[0]);
                double vwapNow = vwap[0];
                poc = vwapNow;
                vah = vwapNow + VAProxyK * sigma;
                val = vwapNow - VAProxyK * sigma;
            }

            // --- regression rails
            ComputeRegression(RegrWin, out regrCenter, out regrSlope, out regrSD);
            double rail2u = regrCenter + 2.0 * regrSD;
            double rail2l = regrCenter - 2.0 * regrSD;

            // --- acceptance outside VA (simple run-length)
            bool outsideNow = (Close[0] > vah && Close[1] > vah) || (Close[0] < val && Close[1] < val);
            if (outsideNow) outsideCount++;
            else outsideCount = 0;

            // --- UD & RV & EMA slope
            volD[0] = Volume[0];
            double rv = volSma[0] > 0 ? Volume[0] / volSma[0] : 1.0;

            // lightweight UD proxy based on bar body / range (clamped)
            double rng = Math.Max(TickSize, High[0] - Low[0]);
            double ud = Math.Max(-1.0, Math.Min(1.0, (Close[0] - Open[0]) / (rng + TickSize)));

            double emaSlopeTicksPerBar = CurrentBar > 0 ? (ema24[0] - ema24[1]) / TickSize : 0.0;

            // --- logistic Rotation GO probability
            double ciVal = ci[0];
            double adxVal = adx[0];
            double adxSlope = CurrentBar > 0 ? adx[0] - adx[1] : 0.0;

            bool inBalanceSoft = ScoreBalance(ciVal, adxVal, adxSlope) >= 0.45;
            bool balanceGateOK = !RequireBalanceGate || inBalanceSoft;

            double edgeScore = ScoreEdge(Close[0], val, vah, RotEdgeBand);

            // +1 long@VAL, -1 short@VAH
            int rotDir = (Math.Abs(Close[0] - val) <= Math.Abs(vah - Close[0])) ? +1 : -1;
            double vwapAlign = ScoreVWAPAlign(Close[0], vwap[0], rotDir);

            double rvPen    = PenaltyRV(rv, RotMaxRV);
            double udPen    = PenaltyUD(ud, RotMaxAbsUD);
            double slopePen = PenaltySlope(emaSlopeTicksPerBar, RotMaxEmaSlope);

            double zRot =
                w_CI         * ScoreBalance(ciVal, adxVal, adxSlope) +
                w_Edge       * edgeScore +
                w_VWAPAlign  * vwapAlign +
                (-w_RVPenalty)   * rvPen +
                (-w_UDPenalty)   * udPen +
                (-w_SlopePenalty)* slopePen +
                RotBias;

            double pRot = 100.0 * (1.0 / (1.0 + Math.Exp(-zRot)));

            bool gatesOK =
                balanceGateOK &&
                (edgeScore > 0.15) &&
                (rv <= RotMaxRV) &&
                (Math.Abs(ud) <= RotMaxAbsUD) &&
                (Math.Abs(emaSlopeTicksPerBar) <= RotMaxEmaSlope) &&
                !AcceptedOutsideValue;

            bool rotGo   = gatesOK && pRot >= RotGoThreshold;
            bool rotOkay = gatesOK && pRot >= RotYellowFloor;

            // --- side suggestion text (same semantics as before)
            bool nearVAH = Math.Abs(Close[0] - vah) <= TickSize * 4;
            bool nearVAL = Math.Abs(Close[0] - val) <= TickSize * 4;
            string side;
            if (nearVAH) side = "Fade Short @ VAH";
            else if (nearVAL) side = "Fade Long  @ VAL";
            else if (Close[0] >= rail2u) side = "Fade Short @ +2σ";
            else if (Close[0] <= rail2l) side = "Fade Long  @ -2σ";
            else side = "Wait @ Mid";

            // --- banner
            Brush badge = badgeRed;
            string mode = "ROT NO GO";
            if (rotGo)       { badge = badgeGreen;  mode = "ROT GO"; }
            else if (rotOkay){ badge = badgeYellow; mode = "ROT CAUTION"; }

            string text =
                $"{mode}  {pRot:0}%\n" +
                $"CI {ciVal:0}  ADX {adxVal:0}  RV {rv:0.0}  UD {ud:0.0}\n" +
                $"VAH {vah:0.00}  POC {poc:0.00}  VAL {val:0.00}\n" +
                $"{side}";

            Draw.TextFixed(
                this,
                "rot_badge",
                text,
                BadgePosition,
                textBrush,
                font,
                badge,
                Brushes.Black,
                80
            );

            if (ShowLines)
            {
                string dtag = curDay.ToString("yyyyMMdd");
                Draw.HorizontalLine(this, "VAH_" + dtag, vah, Brushes.ForestGreen);
                Draw.HorizontalLine(this, "VAL_" + dtag, val, Brushes.IndianRed);
                Draw.HorizontalLine(this, "POC_" + dtag, poc, Brushes.Goldenrod);

                double ibH = IBHigh();
                double ibL = IBLow();
                if (!double.IsNaN(ibH)) Draw.HorizontalLine(this, "IBH_" + dtag, ibH, Brushes.SteelBlue);
                if (!double.IsNaN(ibL)) Draw.HorizontalLine(this, "IBL_" + dtag, ibL, Brushes.SteelBlue);

                Draw.HorizontalLine(this, "R2U_" + dtag, regrCenter + 2.0 * regrSD, Brushes.DimGray);
                Draw.HorizontalLine(this, "R2L_" + dtag, regrCenter - 2.0 * regrSD, Brushes.DimGray);
            }

            // --- alert (cooldown)
            if (rotGo && (Time[0] - lastRotAlert).TotalSeconds >= AlertCooldownSec)
            {
                string dirTxt = rotDir > 0 ? "Fade LONG @ VAL" : "Fade SHORT @ VAH";
                Alert("Rotation_GO",
			      Priority.High,
			      $"ROT GO {pRot:0}% | {dirTxt}",
			      "Alert1.wav",
			      0,                        // rearmSeconds (keep 0 since we handle cooldown ourselves)
			      Brushes.DarkOliveGreen,   // background
			      Brushes.White);           // foreground
            }
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        // Balance score 0..1 (CI high, ADX not ramping)
        private double ScoreBalance(double ci, double adx, double adxSlope)
        {
            double ciN = Math.Max(0, Math.Min(1, (ci - 55.0) / 25.0)); // 55..80 → 0..1
            double flat = Math.Max(0, Math.Min(1, (22.0 - adx) / 12.0)); // lower ADX → flatter
            if (adxSlope <= 0) flat = Math.Max(flat, 0.8);
            return 0.6 * ciN + 0.4 * flat;
        }

        // Edge score 0..1 : 1 near VA edge, 0 in middle
        private double ScoreEdge(double price, double val, double vah, double bandFrac)
        {
            double width = Math.Max(1 * TickSize, vah - val);
            double band = width * bandFrac;
            double dEdge = Math.Min(Math.Abs(price - val), Math.Abs(vah - price));
            double s = 1.0 - (dEdge / band);
            return Math.Max(0, Math.Min(1, s));
        }

        // +1 if aligned for fade toward VWAP; -1 if against
        private double ScoreVWAPAlign(double price, double vwap, int dir)
        {
            double side = dir * Math.Sign(vwap - price);
            return side; // -1..+1
        }

        private double PenaltyRV(double rv, double maxRV)
        {
            if (rv <= 1.0) return 0.0;
            return Math.Max(0, Math.Min(1, (rv - 1.0) / Math.Max(0.01, (maxRV - 1.0))));
        }

        private double PenaltyUD(double ud, double maxAbsUD)
        {
            double a = Math.Abs(ud);
            if (a <= 0.2) return 0.0;
            return Math.Max(0, Math.Min(1, (a - 0.2) / Math.Max(0.01, (maxAbsUD - 0.2))));
        }

        private double PenaltySlope(double slope, double gate)
        {
            double a = Math.Abs(slope);
            if (a <= gate) return 0.0;
            return Math.Max(0, Math.Min(1, (a - gate) / Math.Max(0.01, gate)));
        }

        private void ParseIBTimes()
        {
            DateTime t0 = Times[0][0].Date;
            TimeSpan s = TimeSpan.Parse(IBStartStr);
            TimeSpan e = TimeSpan.Parse(IBEndStr);
            ibStart = t0 + s;
            ibEnd = t0 + e;
        }

        private double IBHigh()
        {
            double h = double.MinValue;
            int n = Math.Min(CurrentBar + 1, 5000);
            for (int i = 0; i < n; i++)
            {
                DateTime t = Times[0][i];
                if (t >= ibStart && t <= ibEnd)
                    h = Math.Max(h, High[i]);
                if (t < ibStart.AddMinutes(-90)) break;
            }
            return h == double.MinValue ? double.NaN : h;
        }

        private double IBLow()
        {
            double l = double.MaxValue;
            int n = Math.Min(CurrentBar + 1, 5000);
            for (int i = 0; i < n; i++)
            {
                DateTime t = Times[0][i];
                if (t >= ibStart && t <= ibEnd)
                    l = Math.Min(l, Low[i]);
                if (t < ibStart.AddMinutes(-90)) break;
            }
            return l == double.MaxValue ? double.NaN : l;
        }

        private void ComputeRegression(int win, out double center, out double slope, out double sd)
        {
            int n = Math.Min(win, CurrentBar + 1);
            if (n < 10)
            {
                center = Close[0];
                slope = 0;
                sd = TickSize;
                return;
            }

            double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
            for (int i = 0; i < n; i++)
            {
                double x = i;
                double y = Close[i];
                sumX += x; sumY += y; sumXY += x * y; sumXX += x * x;
            }
            double denom = n * sumXX - sumX * sumX;
            slope = denom != 0 ? (n * sumXY - sumX * sumY) / denom : 0;

            double meanX = sumX / n;
            double meanY = sumY / n;
            double intercept = meanY - slope * meanX;
            center = intercept;

            double ss = 0;
            for (int i = 0; i < n; i++)
            {
                double pred = intercept + slope * i;
                double r = Close[i] - pred;
                ss += r * r;
            }
            sd = Math.Sqrt(ss / Math.Max(1, n - 2));
            if (sd < TickSize) sd = TickSize;
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RotationHUD[] cacheRotationHUD;
		public RotationHUD RotationHUD(int chopLen, int chopMin, int adxLen, int adxMax, int regrWin, bool useManualVA, double manualVAH, double manualVAL, double manualPOC, double vAProxyK, int vAProxyLen, string iBStartStr, string iBEndStr, int rotGoThreshold, int rotYellowFloor, bool requireBalanceGate, double rotEdgeBand, double rotMaxRV, double rotMaxAbsUD, double rotMaxEmaSlope, double rotBias, int acceptOutsideBars, int alertCooldownSec, double w_CI, double w_ADXFlat, double w_Edge, double w_VWAPAlign, double w_RVPenalty, double w_UDPenalty, double w_SlopePenalty, TextPosition badgePosition, bool showLines)
		{
			return RotationHUD(Input, chopLen, chopMin, adxLen, adxMax, regrWin, useManualVA, manualVAH, manualVAL, manualPOC, vAProxyK, vAProxyLen, iBStartStr, iBEndStr, rotGoThreshold, rotYellowFloor, requireBalanceGate, rotEdgeBand, rotMaxRV, rotMaxAbsUD, rotMaxEmaSlope, rotBias, acceptOutsideBars, alertCooldownSec, w_CI, w_ADXFlat, w_Edge, w_VWAPAlign, w_RVPenalty, w_UDPenalty, w_SlopePenalty, badgePosition, showLines);
		}

		public RotationHUD RotationHUD(ISeries<double> input, int chopLen, int chopMin, int adxLen, int adxMax, int regrWin, bool useManualVA, double manualVAH, double manualVAL, double manualPOC, double vAProxyK, int vAProxyLen, string iBStartStr, string iBEndStr, int rotGoThreshold, int rotYellowFloor, bool requireBalanceGate, double rotEdgeBand, double rotMaxRV, double rotMaxAbsUD, double rotMaxEmaSlope, double rotBias, int acceptOutsideBars, int alertCooldownSec, double w_CI, double w_ADXFlat, double w_Edge, double w_VWAPAlign, double w_RVPenalty, double w_UDPenalty, double w_SlopePenalty, TextPosition badgePosition, bool showLines)
		{
			if (cacheRotationHUD != null)
				for (int idx = 0; idx < cacheRotationHUD.Length; idx++)
					if (cacheRotationHUD[idx] != null && cacheRotationHUD[idx].ChopLen == chopLen && cacheRotationHUD[idx].ChopMin == chopMin && cacheRotationHUD[idx].AdxLen == adxLen && cacheRotationHUD[idx].AdxMax == adxMax && cacheRotationHUD[idx].RegrWin == regrWin && cacheRotationHUD[idx].UseManualVA == useManualVA && cacheRotationHUD[idx].ManualVAH == manualVAH && cacheRotationHUD[idx].ManualVAL == manualVAL && cacheRotationHUD[idx].ManualPOC == manualPOC && cacheRotationHUD[idx].VAProxyK == vAProxyK && cacheRotationHUD[idx].VAProxyLen == vAProxyLen && cacheRotationHUD[idx].IBStartStr == iBStartStr && cacheRotationHUD[idx].IBEndStr == iBEndStr && cacheRotationHUD[idx].RotGoThreshold == rotGoThreshold && cacheRotationHUD[idx].RotYellowFloor == rotYellowFloor && cacheRotationHUD[idx].RequireBalanceGate == requireBalanceGate && cacheRotationHUD[idx].RotEdgeBand == rotEdgeBand && cacheRotationHUD[idx].RotMaxRV == rotMaxRV && cacheRotationHUD[idx].RotMaxAbsUD == rotMaxAbsUD && cacheRotationHUD[idx].RotMaxEmaSlope == rotMaxEmaSlope && cacheRotationHUD[idx].RotBias == rotBias && cacheRotationHUD[idx].AcceptOutsideBars == acceptOutsideBars && cacheRotationHUD[idx].AlertCooldownSec == alertCooldownSec && cacheRotationHUD[idx].w_CI == w_CI && cacheRotationHUD[idx].w_ADXFlat == w_ADXFlat && cacheRotationHUD[idx].w_Edge == w_Edge && cacheRotationHUD[idx].w_VWAPAlign == w_VWAPAlign && cacheRotationHUD[idx].w_RVPenalty == w_RVPenalty && cacheRotationHUD[idx].w_UDPenalty == w_UDPenalty && cacheRotationHUD[idx].w_SlopePenalty == w_SlopePenalty && cacheRotationHUD[idx].BadgePosition == badgePosition && cacheRotationHUD[idx].ShowLines == showLines && cacheRotationHUD[idx].EqualsInput(input))
						return cacheRotationHUD[idx];
			return CacheIndicator<RotationHUD>(new RotationHUD(){ ChopLen = chopLen, ChopMin = chopMin, AdxLen = adxLen, AdxMax = adxMax, RegrWin = regrWin, UseManualVA = useManualVA, ManualVAH = manualVAH, ManualVAL = manualVAL, ManualPOC = manualPOC, VAProxyK = vAProxyK, VAProxyLen = vAProxyLen, IBStartStr = iBStartStr, IBEndStr = iBEndStr, RotGoThreshold = rotGoThreshold, RotYellowFloor = rotYellowFloor, RequireBalanceGate = requireBalanceGate, RotEdgeBand = rotEdgeBand, RotMaxRV = rotMaxRV, RotMaxAbsUD = rotMaxAbsUD, RotMaxEmaSlope = rotMaxEmaSlope, RotBias = rotBias, AcceptOutsideBars = acceptOutsideBars, AlertCooldownSec = alertCooldownSec, w_CI = w_CI, w_ADXFlat = w_ADXFlat, w_Edge = w_Edge, w_VWAPAlign = w_VWAPAlign, w_RVPenalty = w_RVPenalty, w_UDPenalty = w_UDPenalty, w_SlopePenalty = w_SlopePenalty, BadgePosition = badgePosition, ShowLines = showLines }, input, ref cacheRotationHUD);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RotationHUD RotationHUD(int chopLen, int chopMin, int adxLen, int adxMax, int regrWin, bool useManualVA, double manualVAH, double manualVAL, double manualPOC, double vAProxyK, int vAProxyLen, string iBStartStr, string iBEndStr, int rotGoThreshold, int rotYellowFloor, bool requireBalanceGate, double rotEdgeBand, double rotMaxRV, double rotMaxAbsUD, double rotMaxEmaSlope, double rotBias, int acceptOutsideBars, int alertCooldownSec, double w_CI, double w_ADXFlat, double w_Edge, double w_VWAPAlign, double w_RVPenalty, double w_UDPenalty, double w_SlopePenalty, TextPosition badgePosition, bool showLines)
		{
			return indicator.RotationHUD(Input, chopLen, chopMin, adxLen, adxMax, regrWin, useManualVA, manualVAH, manualVAL, manualPOC, vAProxyK, vAProxyLen, iBStartStr, iBEndStr, rotGoThreshold, rotYellowFloor, requireBalanceGate, rotEdgeBand, rotMaxRV, rotMaxAbsUD, rotMaxEmaSlope, rotBias, acceptOutsideBars, alertCooldownSec, w_CI, w_ADXFlat, w_Edge, w_VWAPAlign, w_RVPenalty, w_UDPenalty, w_SlopePenalty, badgePosition, showLines);
		}

		public Indicators.RotationHUD RotationHUD(ISeries<double> input , int chopLen, int chopMin, int adxLen, int adxMax, int regrWin, bool useManualVA, double manualVAH, double manualVAL, double manualPOC, double vAProxyK, int vAProxyLen, string iBStartStr, string iBEndStr, int rotGoThreshold, int rotYellowFloor, bool requireBalanceGate, double rotEdgeBand, double rotMaxRV, double rotMaxAbsUD, double rotMaxEmaSlope, double rotBias, int acceptOutsideBars, int alertCooldownSec, double w_CI, double w_ADXFlat, double w_Edge, double w_VWAPAlign, double w_RVPenalty, double w_UDPenalty, double w_SlopePenalty, TextPosition badgePosition, bool showLines)
		{
			return indicator.RotationHUD(input, chopLen, chopMin, adxLen, adxMax, regrWin, useManualVA, manualVAH, manualVAL, manualPOC, vAProxyK, vAProxyLen, iBStartStr, iBEndStr, rotGoThreshold, rotYellowFloor, requireBalanceGate, rotEdgeBand, rotMaxRV, rotMaxAbsUD, rotMaxEmaSlope, rotBias, acceptOutsideBars, alertCooldownSec, w_CI, w_ADXFlat, w_Edge, w_VWAPAlign, w_RVPenalty, w_UDPenalty, w_SlopePenalty, badgePosition, showLines);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RotationHUD RotationHUD(int chopLen, int chopMin, int adxLen, int adxMax, int regrWin, bool useManualVA, double manualVAH, double manualVAL, double manualPOC, double vAProxyK, int vAProxyLen, string iBStartStr, string iBEndStr, int rotGoThreshold, int rotYellowFloor, bool requireBalanceGate, double rotEdgeBand, double rotMaxRV, double rotMaxAbsUD, double rotMaxEmaSlope, double rotBias, int acceptOutsideBars, int alertCooldownSec, double w_CI, double w_ADXFlat, double w_Edge, double w_VWAPAlign, double w_RVPenalty, double w_UDPenalty, double w_SlopePenalty, TextPosition badgePosition, bool showLines)
		{
			return indicator.RotationHUD(Input, chopLen, chopMin, adxLen, adxMax, regrWin, useManualVA, manualVAH, manualVAL, manualPOC, vAProxyK, vAProxyLen, iBStartStr, iBEndStr, rotGoThreshold, rotYellowFloor, requireBalanceGate, rotEdgeBand, rotMaxRV, rotMaxAbsUD, rotMaxEmaSlope, rotBias, acceptOutsideBars, alertCooldownSec, w_CI, w_ADXFlat, w_Edge, w_VWAPAlign, w_RVPenalty, w_UDPenalty, w_SlopePenalty, badgePosition, showLines);
		}

		public Indicators.RotationHUD RotationHUD(ISeries<double> input , int chopLen, int chopMin, int adxLen, int adxMax, int regrWin, bool useManualVA, double manualVAH, double manualVAL, double manualPOC, double vAProxyK, int vAProxyLen, string iBStartStr, string iBEndStr, int rotGoThreshold, int rotYellowFloor, bool requireBalanceGate, double rotEdgeBand, double rotMaxRV, double rotMaxAbsUD, double rotMaxEmaSlope, double rotBias, int acceptOutsideBars, int alertCooldownSec, double w_CI, double w_ADXFlat, double w_Edge, double w_VWAPAlign, double w_RVPenalty, double w_UDPenalty, double w_SlopePenalty, TextPosition badgePosition, bool showLines)
		{
			return indicator.RotationHUD(input, chopLen, chopMin, adxLen, adxMax, regrWin, useManualVA, manualVAH, manualVAL, manualPOC, vAProxyK, vAProxyLen, iBStartStr, iBEndStr, rotGoThreshold, rotYellowFloor, requireBalanceGate, rotEdgeBand, rotMaxRV, rotMaxAbsUD, rotMaxEmaSlope, rotBias, acceptOutsideBars, alertCooldownSec, w_CI, w_ADXFlat, w_Edge, w_VWAPAlign, w_RVPenalty, w_UDPenalty, w_SlopePenalty, badgePosition, showLines);
		}
	}
}

#endregion
