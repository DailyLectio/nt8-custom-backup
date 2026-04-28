#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// ============================================================================
// GoGoRotationHUD_WPF
// Compact, compile-friendly dual HUD:
//   • Trend GO tile with detailed inputs (RVol, UD, ADX, CI, EMA slopes)
//   • Rotation GO tile with detailed inputs (CI, ADX, Regression, VA proxy)
// Drawn with Draw.TextFixed (no SharpDX), one tile per corner.
// ============================================================================

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GoGoRotationHUD_WPF : Indicator
    {
        // ========================== General / Visual ==========================
        [NinjaScriptProperty]
        [Display(Name = "Show Trend Tile", GroupName = "Visual", Order = 0)]
        public bool ShowTrend { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Show Rotation Tile", GroupName = "Visual", Order = 1)]
        public bool ShowRotation { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Trend Tile Position", GroupName = "Visual", Order = 2)]
        public TextPosition TrendPosition { get; set; } = TextPosition.BottomLeft;

        [NinjaScriptProperty]
        [Display(Name = "Rotation Tile Position", GroupName = "Visual", Order = 3)]
        public TextPosition RotationPosition { get; set; } = TextPosition.BottomRight;

        [NinjaScriptProperty, Range(8, 24)]
        [Display(Name = "Font Size", GroupName = "Visual", Order = 4)]
        public int FontSize { get; set; } = 13;

        [NinjaScriptProperty, Range(10, 100)]
        [Display(Name = "Tile Opacity (10-100)", GroupName = "Visual", Order = 5)]
        public int TileOpacity { get; set; } = 80;

        [NinjaScriptProperty]
        [Display(Name = "Alert Sound (GO)", GroupName = "Visual", Order = 6)]
        public string GoSound { get; set; } = "Alert1.wav";

        // Color thresholds shared by both tiles
        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Green ≥ %", GroupName = "Thresholds", Order = 0)]
        public int GreenMin { get; set; } = 70;

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Yellow ≥ % (else red)", GroupName = "Thresholds", Order = 1)]
        public int YellowMin { get; set; } = 50;

        [NinjaScriptProperty, Range(0, 600)]
        [Display(Name = "Alert Cooldown (sec)", GroupName = "Thresholds", Order = 2)]
        public int AlertCooldownSec { get; set; } = 90;

        // ============================= TREND tile =============================
        [NinjaScriptProperty, Range(10, 200)]
        [Display(Name = "RVol SMA Len", GroupName = "Trend", Order = 0)]
        public int RVolLen { get; set; } = 50;

        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "ADX Len", GroupName = "Trend", Order = 1)]
        public int TrendAdxLen { get; set; } = 14;

        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "Chop Len", GroupName = "Trend", Order = 2)]
        public int TrendChopLen { get; set; } = 14;

        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "UD Len", GroupName = "Trend", Order = 3)]
        public int UDLen { get; set; } = 20;

        [NinjaScriptProperty, Range(2, 50)]
        [Display(Name = "EMA Fast", GroupName = "Trend", Order = 4)]
        public int EmaFastLen { get; set; } = 8;

        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name = "EMA Slow", GroupName = "Trend", Order = 5)]
        public int EmaSlowLen { get; set; } = 24;

        [NinjaScriptProperty]
        [Display(Name = "Require ADX ≥", GroupName = "Trend", Order = 6)]
        public bool RequireTrendAdxGate { get; set; } = false;

        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "ADX Gate", GroupName = "Trend", Order = 7)]
        public int TrendAdxGate { get; set; } = 18;

        // feature weights (0..1 ish)
        [NinjaScriptProperty, Range(0.0, 2.0)]
        [Display(Name = "w_RVol", GroupName = "Trend Weights", Order = 0)]
        public double w_RVol { get; set; } = 0.8;

        [NinjaScriptProperty, Range(0.0, 2.0)]
        [Display(Name = "w_UD", GroupName = "Trend Weights", Order = 1)]
        public double w_UD { get; set; } = 0.5;

        [NinjaScriptProperty, Range(0.0, 2.0)]
        [Display(Name = "w_ADXsl", GroupName = "Trend Weights", Order = 2)]
        public double w_ADXsl { get; set; } = 1.0;

        [NinjaScriptProperty, Range(0.0, 2.0)]
        [Display(Name = "w_CHOP", GroupName = "Trend Weights", Order = 3)]
        public double w_CHOP { get; set; } = 0.6;

        [NinjaScriptProperty, Range(0.0, 2.0)]
        [Display(Name = "w_EMA", GroupName = "Trend Weights", Order = 4)]
        public double w_EMA { get; set; } = 0.9;

        // =========================== ROTATION tile ============================
        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "Rot ADX Len", GroupName = "Rotation", Order = 0)]
        public int RotAdxLen { get; set; } = 14;

        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name = "Rot Chop Len", GroupName = "Rotation", Order = 1)]
        public int RotChopLen { get; set; } = 14;

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Chop Min (rotation)", GroupName = "Rotation", Order = 2)]
        public int RotChopMin { get; set; } = 60;

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "ADX Max (rotation)", GroupName = "Rotation", Order = 3)]
        public int RotAdxMax { get; set; } = 18;

        [NinjaScriptProperty, Range(20, 500)]
        [Display(Name = "Regression Window", GroupName = "Rotation", Order = 4)]
        public int RegrWin { get; set; } = 60;

        [NinjaScriptProperty]
        [Display(Name = "Use Manual VA", GroupName = "Rotation VA", Order = 5)]
        public bool UseManualVA { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Manual VAH", GroupName = "Rotation VA", Order = 6)]
        public double ManualVAH { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Manual VAL", GroupName = "Rotation VA", Order = 7)]
        public double ManualVAL { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Manual POC", GroupName = "Rotation VA", Order = 8)]
        public double ManualPOC { get; set; } = 0;

        [NinjaScriptProperty, Range(0.5, 3.0)]
        [Display(Name = "Proxy k (VWAP ± k·σ)", GroupName = "Rotation VA", Order = 9)]
        public double VAProxyK { get; set; } = 1.35;

        [NinjaScriptProperty, Range(20, 400)]
        [Display(Name = "Proxy σ Len", GroupName = "Rotation VA", Order = 10)]
        public int VAProxyLen { get; set; } = 120;

        // IB window (optional context text)
        [NinjaScriptProperty]
        [Display(Name = "IB Start (HH:mm)", GroupName = "Rotation IB", Order = 11)]
        public string IBStartStr { get; set; } = "09:30";

        [NinjaScriptProperty]
        [Display(Name = "IB End (HH:mm)", GroupName = "Rotation IB", Order = 12)]
        public string IBEndStr { get; set; } = "10:00";

        // =========================== Internals ================================
        private SimpleFont font;
        private DateTime lastGoAlert = DateTime.MinValue;
        private DateTime lastRotAlert = DateTime.MinValue;

        // Trend indicators
        private ADX tADX;
        private ChoppinessIndex tCI;
        private EMA emaFast, emaSlow;
        private SMA volSma;
        private Series<double> upDownScore;

        // Rotation indicators
        private ADX rADX;
        private ChoppinessIndex rCI;
        private Series<double> typ, vwap;
        private StdDev typStd;
        private double cumPV, cumVol;
        private DateTime ibStart, ibEnd;

        // Regression caches
        private double regrCenter, regrSlope, regrSD;

        // ====== life-cycle ======
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "GoGoRotationHUD_WPF";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.Configure)
            {
                font = new SimpleFont("Segoe UI", FontSize) { Bold = true };
            }
            else if (State == State.DataLoaded)
            {
                // Trend bits
                tADX = ADX(TrendAdxLen);
                tCI  = ChoppinessIndex(TrendChopLen);
                emaFast = EMA(EmaFastLen);
                emaSlow = EMA(EmaSlowLen);
                volSma  = SMA(Volume, RVolLen);
                upDownScore = new Series<double>(this);

                // Rotation bits
                rADX = ADX(RotAdxLen);
                rCI  = ChoppinessIndex(RotChopLen);
                typ = new Series<double>(this);
                typStd = StdDev(typ, VAProxyLen);
                vwap = new Series<double>(this);
            }
        }

        // Helpers
        private static double Clamp(double x, double lo, double hi) => Math.Max(lo, Math.Min(hi, x));
        private static double Logistic(double z) => 1.0 / (1.0 + Math.Exp(-z));

        private void ParseIBTimes()
        {
            DateTime t0 = Times[0][0].Date;
            TimeSpan s = TimeSpan.Parse(IBStartStr);
            TimeSpan e = TimeSpan.Parse(IBEndStr);
            ibStart = t0 + s;
            ibEnd   = t0 + e;
        }

        private void ComputeRegression(int win, out double center, out double slope, out double sd)
        {
            int n = Math.Min(win, CurrentBar + 1);
            if (n < 10)
            {
                center = Close[0];
                slope = 0;
                sd    = Instrument.MasterInstrument.TickSize;
                return;
            }
            double sumX=0, sumY=0, sumXY=0, sumXX=0;
            for (int i=0; i<n; i++)
            {
                double x = i;
                double y = Close[i];
                sumX += x; sumY += y; sumXY += x * y; sumXX += x * x;
            }
            double denom = n * sumXX - sumX * sumX;
            slope  = denom != 0 ? (n * sumXY - sumX * sumY) / denom : 0.0;
            double meanX = sumX / n, meanY = sumY / n;
            double intercept = meanY - slope * meanX;
            center = intercept;

            double ss=0;
            for (int i=0;i<n;i++)
            {
                double pred = intercept + slope * i;
                double r = Close[i] - pred;
                ss += r*r;
            }
            sd = Math.Sqrt(ss / Math.Max(1, n-2));
            if (sd < Instrument.MasterInstrument.TickSize) sd = Instrument.MasterInstrument.TickSize;
        }

        // ============================== OnBarUpdate ===========================
        protected override void OnBarUpdate()
        {
            int need = Math.Max(
               Math.Max(RVolLen, Math.Max(TrendAdxLen, TrendChopLen)),
               Math.Max(VAProxyLen, Math.Max(RotAdxLen, RotChopLen))
			           );
			if (CurrentBar < need)
			    return;

            // ---- shared font (update when user changes size) ----
            if (font == null || Math.Abs(font.Size - FontSize) > double.Epsilon)
                font = new SimpleFont("Segoe UI", FontSize) { Bold = true };

            // ========================= TREND tile metrics ======================
            // Relative Volume
            double rv = volSma[0] > 0 ? Volume[0] / volSma[0] : 1.0;
            double sRVol = Clamp((rv - 1.0), -1.0, 1.0);  // around 0: normal

            // Up/Down pressure (simple: avg sign of close change)
            double ud = 0;
            int nUD = Math.Min(UDLen, CurrentBar + 1);
            for (int i = 0; i < nUD; i++)
                ud += Math.Sign(Close[i] - Open[i]);
            ud /= Math.Max(1, nUD);                // [-1..+1]
            upDownScore[0] = ud;

            // ADX & slope
            double adxT = tADX[0];
            double adxTslope = tADX[0] - tADX[1];   // positive = strengthening trend
            double sADXsl = Clamp(adxTslope / 5.0, -1.0, 1.0);

            // Choppiness: high = choppy (bad for trend)
            double ciT = tCI[0];                    // 0..100
            double sCHOP = Clamp((50.0 - (ciT - 50.0)) / 50.0, -1.0, 1.0); // lower CI => +1

            // EMA slopes & alignment
            double slopeFast = emaFast[0] - emaFast[1];
            double slopeSlow = emaSlow[0] - emaSlow[1];
            double posToFast = (Close[0] - emaFast[0]) / (Instrument.MasterInstrument.TickSize * 8.0); // scaled
            double sEMA = Clamp(0.6 * Math.Sign(slopeFast) + 0.3 * Math.Sign(slopeSlow) + 0.1 * Clamp(posToFast, -1, 1),
                                -1.0, 1.0);

            // Trend probability via simple logistic over weighted features
            double zTrend = w_RVol * sRVol + w_UD * ud + w_ADXsl * sADXsl + w_CHOP * sCHOP + w_EMA * sEMA;
            double pTrend = 100.0 * Logistic(zTrend);

            bool trendGate = !RequireTrendAdxGate || adxT >= TrendAdxGate;
            string trendMode = pTrend >= GreenMin && trendGate ? "GO" : (pTrend >= YellowMin && trendGate ? "CAUTION" : "NO-GO");
            Brush trendBg = trendMode == "GO" ? Brushes.DarkSeaGreen : (pTrend >= YellowMin && trendGate ? Brushes.Goldenrod : Brushes.IndianRed);

            // Trend detail text
            var tSB = new StringBuilder();
            tSB.AppendLine($"TREND {pTrend:0}% | {(trendGate ? "ADX OK" : "ADX NO")} | {trendMode}");
            tSB.AppendLine($"RVol {rv:0.00}  UD {ud:0.00}  ADX {adxT:0}  ΔADX {adxTslope:0.00}  CI {ciT:0}");
            tSB.AppendLine($"EMA{EmaFastLen} Δ{(slopeFast/Instrument.MasterInstrument.TickSize):0.0}  " +
                           $"EMA{EmaSlowLen} Δ{(slopeSlow/Instrument.MasterInstrument.TickSize):0.0}  " +
                           $"PosFast {(posToFast):0.00}");

            if (ShowTrend)
            {
                Draw.TextFixed(this, "GGR_TREND",
                    tSB.ToString(),
                    TrendPosition,
                    Brushes.White, font,
                    Brushes.Transparent, trendBg, TileOpacity);
            }

            // GO alert (cooldown)
            if (ShowTrend && trendMode == "GO" && (Time[0] - lastGoAlert).TotalSeconds >= AlertCooldownSec)
            {
                Alert("GoTrend_GO", Priority.High, $"TREND GO {pTrend:0}%", GoSound, 0, Brushes.DarkSeaGreen, Brushes.White);
                lastGoAlert = Time[0];
            }

            // ======================== ROTATION tile metrics ====================
            // Typical & VWAP proxy VA
            typ[0] = (High[0] + Low[0] + Close[0]) / 3.0;
            if (Bars.IsFirstBarOfSession) { cumPV = 0; cumVol = 0; ParseIBTimes(); }
            cumPV += typ[0] * Volume[0];
            cumVol += Volume[0];
            vwap[0] = cumVol > 0 ? cumPV / cumVol : typ[0];

            double vah, val, poc;
            if (UseManualVA && ManualVAH > 0 && ManualVAL > 0 && ManualPOC > 0)
            {
                vah = ManualVAH; val = ManualVAL; poc = ManualPOC;
            }
            else
            {
                double sigma = Math.Max(Instrument.MasterInstrument.TickSize, typStd[0]);
                poc = vwap[0];
                vah = poc + VAProxyK * sigma;
                val = poc - VAProxyK * sigma;
            }

            // Regression mid/rails
            ComputeRegression(RegrWin, out regrCenter, out regrSlope, out regrSD);
            double rail2u = regrCenter + 2.0 * regrSD;
            double rail2l = regrCenter - 2.0 * regrSD;

            // Regime gates for rotation
            double ciR = rCI[0];
            double adxR = rADX[0];
            bool rotRegime = (ciR >= RotChopMin) && (adxR <= RotAdxMax || (rADX[0] < rADX[1] && rADX[1] < rADX[2]));

            // Edge / confluence
            bool nearVAH = Math.Abs(Close[0] - vah) <= Instrument.MasterInstrument.TickSize * 4;
            bool nearVAL = Math.Abs(Close[0] - val) <= Instrument.MasterInstrument.TickSize * 4;
            bool onRail  = (Close[0] >= rail2u) || (Close[0] <= rail2l);
            bool conflUp   = nearVAH && Math.Abs(vah - rail2u) <= Instrument.MasterInstrument.TickSize * 2;
            bool conflDown = nearVAL && Math.Abs(val - rail2l) <= Instrument.MasterInstrument.TickSize * 2;

            // Simple scored probability (location + regime)
            double sReg = rotRegime ? 1.0 : 0.0;              // 0/1
            double sLoc = (nearVAH || nearVAL || onRail ? 0.7 : 0.0) + ((conflUp || conflDown) ? 0.3 : 0.0);
            sLoc = Clamp(sLoc, 0, 1);

            double zRot = 2.2 * sReg + 2.0 * sLoc - 0.8 * Math.Abs(regrSlope) / Math.Max(Instrument.MasterInstrument.TickSize, regrSD * 0.1);
            double pRot = 100.0 * Logistic(zRot);

            // Suggest side
            string side;
            if (nearVAH) side = "Fade Short @ VAH";
            else if (nearVAL) side = "Fade Long  @ VAL";
            else if (Close[0] >= rail2u) side = "Fade Short @ +2σ";
            else if (Close[0] <= rail2l) side = "Fade Long  @ -2σ";
            else side = "Wait @ MID";

            string rotMode = pRot >= GreenMin && rotRegime ? "ROT GO" : (pRot >= YellowMin && rotRegime ? "CAUTION" : "NO-GO");
            Brush rotBg = rotMode == "ROT GO" ? Brushes.SeaGreen : (pRot >= YellowMin && rotRegime ? Brushes.Goldenrod : Brushes.IndianRed);

            // Rotation detail text
            var rSB = new StringBuilder();
            rSB.AppendLine($"{rotMode} {pRot:0}% | CI {ciR:0}  ADX {adxR:0}  RegrΔ {(regrSlope/Instrument.MasterInstrument.TickSize):0.0}t");
            rSB.AppendLine($"VAH {vah:0.00}  POC {poc:0.00}  VAL {val:0.00}");
            rSB.AppendLine($"μ {regrCenter:0.00}  ±2σ: {rail2l:0.00}/{rail2u:0.00}");
            rSB.AppendLine(side);

            if (ShowRotation)
            {
                Draw.TextFixed(this, "GGR_ROT",
                    rSB.ToString(),
                    RotationPosition,
                    Brushes.White, font,
                    Brushes.Transparent, rotBg, TileOpacity);
            }

            if (ShowRotation && rotMode == "ROT GO" && (Time[0] - lastRotAlert).TotalSeconds >= AlertCooldownSec)
            {
                Alert("GoRotation_GO", Priority.High, $"ROT GO {pRot:0}% | {side}", GoSound, 0, Brushes.SeaGreen, Brushes.White);
                lastRotAlert = Time[0];
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GoGoRotationHUD_WPF[] cacheGoGoRotationHUD_WPF;
		public GoGoRotationHUD_WPF GoGoRotationHUD_WPF(bool showTrend, bool showRotation, TextPosition trendPosition, TextPosition rotationPosition, int fontSize, int tileOpacity, string goSound, int greenMin, int yellowMin, int alertCooldownSec, int rVolLen, int trendAdxLen, int trendChopLen, int uDLen, int emaFastLen, int emaSlowLen, bool requireTrendAdxGate, int trendAdxGate, double w_RVol, double w_UD, double w_ADXsl, double w_CHOP, double w_EMA, int rotAdxLen, int rotChopLen, int rotChopMin, int rotAdxMax, int regrWin, bool useManualVA, double manualVAH, double manualVAL, double manualPOC, double vAProxyK, int vAProxyLen, string iBStartStr, string iBEndStr)
		{
			return GoGoRotationHUD_WPF(Input, showTrend, showRotation, trendPosition, rotationPosition, fontSize, tileOpacity, goSound, greenMin, yellowMin, alertCooldownSec, rVolLen, trendAdxLen, trendChopLen, uDLen, emaFastLen, emaSlowLen, requireTrendAdxGate, trendAdxGate, w_RVol, w_UD, w_ADXsl, w_CHOP, w_EMA, rotAdxLen, rotChopLen, rotChopMin, rotAdxMax, regrWin, useManualVA, manualVAH, manualVAL, manualPOC, vAProxyK, vAProxyLen, iBStartStr, iBEndStr);
		}

		public GoGoRotationHUD_WPF GoGoRotationHUD_WPF(ISeries<double> input, bool showTrend, bool showRotation, TextPosition trendPosition, TextPosition rotationPosition, int fontSize, int tileOpacity, string goSound, int greenMin, int yellowMin, int alertCooldownSec, int rVolLen, int trendAdxLen, int trendChopLen, int uDLen, int emaFastLen, int emaSlowLen, bool requireTrendAdxGate, int trendAdxGate, double w_RVol, double w_UD, double w_ADXsl, double w_CHOP, double w_EMA, int rotAdxLen, int rotChopLen, int rotChopMin, int rotAdxMax, int regrWin, bool useManualVA, double manualVAH, double manualVAL, double manualPOC, double vAProxyK, int vAProxyLen, string iBStartStr, string iBEndStr)
		{
			if (cacheGoGoRotationHUD_WPF != null)
				for (int idx = 0; idx < cacheGoGoRotationHUD_WPF.Length; idx++)
					if (cacheGoGoRotationHUD_WPF[idx] != null && cacheGoGoRotationHUD_WPF[idx].ShowTrend == showTrend && cacheGoGoRotationHUD_WPF[idx].ShowRotation == showRotation && cacheGoGoRotationHUD_WPF[idx].TrendPosition == trendPosition && cacheGoGoRotationHUD_WPF[idx].RotationPosition == rotationPosition && cacheGoGoRotationHUD_WPF[idx].FontSize == fontSize && cacheGoGoRotationHUD_WPF[idx].TileOpacity == tileOpacity && cacheGoGoRotationHUD_WPF[idx].GoSound == goSound && cacheGoGoRotationHUD_WPF[idx].GreenMin == greenMin && cacheGoGoRotationHUD_WPF[idx].YellowMin == yellowMin && cacheGoGoRotationHUD_WPF[idx].AlertCooldownSec == alertCooldownSec && cacheGoGoRotationHUD_WPF[idx].RVolLen == rVolLen && cacheGoGoRotationHUD_WPF[idx].TrendAdxLen == trendAdxLen && cacheGoGoRotationHUD_WPF[idx].TrendChopLen == trendChopLen && cacheGoGoRotationHUD_WPF[idx].UDLen == uDLen && cacheGoGoRotationHUD_WPF[idx].EmaFastLen == emaFastLen && cacheGoGoRotationHUD_WPF[idx].EmaSlowLen == emaSlowLen && cacheGoGoRotationHUD_WPF[idx].RequireTrendAdxGate == requireTrendAdxGate && cacheGoGoRotationHUD_WPF[idx].TrendAdxGate == trendAdxGate && cacheGoGoRotationHUD_WPF[idx].w_RVol == w_RVol && cacheGoGoRotationHUD_WPF[idx].w_UD == w_UD && cacheGoGoRotationHUD_WPF[idx].w_ADXsl == w_ADXsl && cacheGoGoRotationHUD_WPF[idx].w_CHOP == w_CHOP && cacheGoGoRotationHUD_WPF[idx].w_EMA == w_EMA && cacheGoGoRotationHUD_WPF[idx].RotAdxLen == rotAdxLen && cacheGoGoRotationHUD_WPF[idx].RotChopLen == rotChopLen && cacheGoGoRotationHUD_WPF[idx].RotChopMin == rotChopMin && cacheGoGoRotationHUD_WPF[idx].RotAdxMax == rotAdxMax && cacheGoGoRotationHUD_WPF[idx].RegrWin == regrWin && cacheGoGoRotationHUD_WPF[idx].UseManualVA == useManualVA && cacheGoGoRotationHUD_WPF[idx].ManualVAH == manualVAH && cacheGoGoRotationHUD_WPF[idx].ManualVAL == manualVAL && cacheGoGoRotationHUD_WPF[idx].ManualPOC == manualPOC && cacheGoGoRotationHUD_WPF[idx].VAProxyK == vAProxyK && cacheGoGoRotationHUD_WPF[idx].VAProxyLen == vAProxyLen && cacheGoGoRotationHUD_WPF[idx].IBStartStr == iBStartStr && cacheGoGoRotationHUD_WPF[idx].IBEndStr == iBEndStr && cacheGoGoRotationHUD_WPF[idx].EqualsInput(input))
						return cacheGoGoRotationHUD_WPF[idx];
			return CacheIndicator<GoGoRotationHUD_WPF>(new GoGoRotationHUD_WPF(){ ShowTrend = showTrend, ShowRotation = showRotation, TrendPosition = trendPosition, RotationPosition = rotationPosition, FontSize = fontSize, TileOpacity = tileOpacity, GoSound = goSound, GreenMin = greenMin, YellowMin = yellowMin, AlertCooldownSec = alertCooldownSec, RVolLen = rVolLen, TrendAdxLen = trendAdxLen, TrendChopLen = trendChopLen, UDLen = uDLen, EmaFastLen = emaFastLen, EmaSlowLen = emaSlowLen, RequireTrendAdxGate = requireTrendAdxGate, TrendAdxGate = trendAdxGate, w_RVol = w_RVol, w_UD = w_UD, w_ADXsl = w_ADXsl, w_CHOP = w_CHOP, w_EMA = w_EMA, RotAdxLen = rotAdxLen, RotChopLen = rotChopLen, RotChopMin = rotChopMin, RotAdxMax = rotAdxMax, RegrWin = regrWin, UseManualVA = useManualVA, ManualVAH = manualVAH, ManualVAL = manualVAL, ManualPOC = manualPOC, VAProxyK = vAProxyK, VAProxyLen = vAProxyLen, IBStartStr = iBStartStr, IBEndStr = iBEndStr }, input, ref cacheGoGoRotationHUD_WPF);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GoGoRotationHUD_WPF GoGoRotationHUD_WPF(bool showTrend, bool showRotation, TextPosition trendPosition, TextPosition rotationPosition, int fontSize, int tileOpacity, string goSound, int greenMin, int yellowMin, int alertCooldownSec, int rVolLen, int trendAdxLen, int trendChopLen, int uDLen, int emaFastLen, int emaSlowLen, bool requireTrendAdxGate, int trendAdxGate, double w_RVol, double w_UD, double w_ADXsl, double w_CHOP, double w_EMA, int rotAdxLen, int rotChopLen, int rotChopMin, int rotAdxMax, int regrWin, bool useManualVA, double manualVAH, double manualVAL, double manualPOC, double vAProxyK, int vAProxyLen, string iBStartStr, string iBEndStr)
		{
			return indicator.GoGoRotationHUD_WPF(Input, showTrend, showRotation, trendPosition, rotationPosition, fontSize, tileOpacity, goSound, greenMin, yellowMin, alertCooldownSec, rVolLen, trendAdxLen, trendChopLen, uDLen, emaFastLen, emaSlowLen, requireTrendAdxGate, trendAdxGate, w_RVol, w_UD, w_ADXsl, w_CHOP, w_EMA, rotAdxLen, rotChopLen, rotChopMin, rotAdxMax, regrWin, useManualVA, manualVAH, manualVAL, manualPOC, vAProxyK, vAProxyLen, iBStartStr, iBEndStr);
		}

		public Indicators.GoGoRotationHUD_WPF GoGoRotationHUD_WPF(ISeries<double> input , bool showTrend, bool showRotation, TextPosition trendPosition, TextPosition rotationPosition, int fontSize, int tileOpacity, string goSound, int greenMin, int yellowMin, int alertCooldownSec, int rVolLen, int trendAdxLen, int trendChopLen, int uDLen, int emaFastLen, int emaSlowLen, bool requireTrendAdxGate, int trendAdxGate, double w_RVol, double w_UD, double w_ADXsl, double w_CHOP, double w_EMA, int rotAdxLen, int rotChopLen, int rotChopMin, int rotAdxMax, int regrWin, bool useManualVA, double manualVAH, double manualVAL, double manualPOC, double vAProxyK, int vAProxyLen, string iBStartStr, string iBEndStr)
		{
			return indicator.GoGoRotationHUD_WPF(input, showTrend, showRotation, trendPosition, rotationPosition, fontSize, tileOpacity, goSound, greenMin, yellowMin, alertCooldownSec, rVolLen, trendAdxLen, trendChopLen, uDLen, emaFastLen, emaSlowLen, requireTrendAdxGate, trendAdxGate, w_RVol, w_UD, w_ADXsl, w_CHOP, w_EMA, rotAdxLen, rotChopLen, rotChopMin, rotAdxMax, regrWin, useManualVA, manualVAH, manualVAL, manualPOC, vAProxyK, vAProxyLen, iBStartStr, iBEndStr);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GoGoRotationHUD_WPF GoGoRotationHUD_WPF(bool showTrend, bool showRotation, TextPosition trendPosition, TextPosition rotationPosition, int fontSize, int tileOpacity, string goSound, int greenMin, int yellowMin, int alertCooldownSec, int rVolLen, int trendAdxLen, int trendChopLen, int uDLen, int emaFastLen, int emaSlowLen, bool requireTrendAdxGate, int trendAdxGate, double w_RVol, double w_UD, double w_ADXsl, double w_CHOP, double w_EMA, int rotAdxLen, int rotChopLen, int rotChopMin, int rotAdxMax, int regrWin, bool useManualVA, double manualVAH, double manualVAL, double manualPOC, double vAProxyK, int vAProxyLen, string iBStartStr, string iBEndStr)
		{
			return indicator.GoGoRotationHUD_WPF(Input, showTrend, showRotation, trendPosition, rotationPosition, fontSize, tileOpacity, goSound, greenMin, yellowMin, alertCooldownSec, rVolLen, trendAdxLen, trendChopLen, uDLen, emaFastLen, emaSlowLen, requireTrendAdxGate, trendAdxGate, w_RVol, w_UD, w_ADXsl, w_CHOP, w_EMA, rotAdxLen, rotChopLen, rotChopMin, rotAdxMax, regrWin, useManualVA, manualVAH, manualVAL, manualPOC, vAProxyK, vAProxyLen, iBStartStr, iBEndStr);
		}

		public Indicators.GoGoRotationHUD_WPF GoGoRotationHUD_WPF(ISeries<double> input , bool showTrend, bool showRotation, TextPosition trendPosition, TextPosition rotationPosition, int fontSize, int tileOpacity, string goSound, int greenMin, int yellowMin, int alertCooldownSec, int rVolLen, int trendAdxLen, int trendChopLen, int uDLen, int emaFastLen, int emaSlowLen, bool requireTrendAdxGate, int trendAdxGate, double w_RVol, double w_UD, double w_ADXsl, double w_CHOP, double w_EMA, int rotAdxLen, int rotChopLen, int rotChopMin, int rotAdxMax, int regrWin, bool useManualVA, double manualVAH, double manualVAL, double manualPOC, double vAProxyK, int vAProxyLen, string iBStartStr, string iBEndStr)
		{
			return indicator.GoGoRotationHUD_WPF(input, showTrend, showRotation, trendPosition, rotationPosition, fontSize, tileOpacity, goSound, greenMin, yellowMin, alertCooldownSec, rVolLen, trendAdxLen, trendChopLen, uDLen, emaFastLen, emaSlowLen, requireTrendAdxGate, trendAdxGate, w_RVol, w_UD, w_ADXsl, w_CHOP, w_EMA, rotAdxLen, rotChopLen, rotChopMin, rotAdxMax, regrWin, useManualVA, manualVAH, manualVAL, manualPOC, vAProxyK, vAProxyLen, iBStartStr, iBEndStr);
		}
	}
}

#endregion
