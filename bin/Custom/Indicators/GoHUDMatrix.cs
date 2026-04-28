#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;

using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;              // SimpleFont
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;

using System.Windows.Media;               // Brushes
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Go/No-Go Matrix (WPF only) — v1.1
    /// Columns = FH, IB, Balance, Primary + GO.
    /// Rows = RVol, U/D, ADX-Slope, Chop, VWAP, Regime.
    /// NEW: each column uses its own lookbacks so the feature tiles differ per window.
    /// </summary>
    public class GoHUDMatrix : Indicator
    {
        // =======================
        // Parameters
        // =======================
        [NinjaScriptProperty, Range(0,100)]
        [Display(Name="Go Threshold %", GroupName="GO/NO-GO", Order=0)]
        public int GoThreshold { get; set; }

        [NinjaScriptProperty, Range(0,100)]
        [Display(Name="Yellow Floor %", GroupName="GO/NO-GO", Order=1)]
        public int YellowFloor { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Require ADX Gate", GroupName="Gates", Order=0)]
        public bool RequireAdxGate { get; set; }

        [NinjaScriptProperty, Range(5,100)]
        [Display(Name="ADX Gate", GroupName="Gates", Order=1)]
        public int AdxGate { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Use First-Hour Micro (09:30–09:45)", GroupName="Windows", Order=0)]
        public bool UseFirstHourMicro { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Dim Inactive Columns", GroupName="Windows", Order=1)]
        public bool DimInactive { get; set; }

        // Feature toggles
        [NinjaScriptProperty] [Display(Name="Use RVol",    GroupName="Features", Order=0)] public bool UseRVol   { get; set; }
        [NinjaScriptProperty] [Display(Name="Use U/D",      GroupName="Features", Order=1)] public bool UseUD     { get; set; }
        [NinjaScriptProperty] [Display(Name="Use ADX/Slope",GroupName="Features", Order=2)] public bool UseAdxSl  { get; set; }
        [NinjaScriptProperty] [Display(Name="Use CHOP",     GroupName="Features", Order=3)] public bool UseChop   { get; set; }
        [NinjaScriptProperty] [Display(Name="Use VWAP",     GroupName="Features", Order=4)] public bool UseVWAP   { get; set; }
        [NinjaScriptProperty] [Display(Name="Use Regime",   GroupName="Features", Order=5)] public bool UseRegime { get; set; }

        // Weights for probability
        [NinjaScriptProperty] [Display(Name="w_RVol",   GroupName="Weights", Order=0)] public double W_RVol   { get; set; }
        [NinjaScriptProperty] [Display(Name="w_UD",     GroupName="Weights", Order=1)] public double W_UD     { get; set; }
        [NinjaScriptProperty] [Display(Name="w_ADXSL",  GroupName="Weights", Order=2)] public double W_ADXSL  { get; set; }
        [NinjaScriptProperty] [Display(Name="w_CHOP",   GroupName="Weights", Order=3)] public double W_CHOP   { get; set; }
        [NinjaScriptProperty] [Display(Name="w_VWAP",   GroupName="Weights", Order=4)] public double W_VWAP   { get; set; }
        [NinjaScriptProperty] [Display(Name="w_Regime", GroupName="Weights", Order=5)] public double W_Regime { get; set; }
        [NinjaScriptProperty] [Display(Name="Bias",     GroupName="Weights", Order=6)] public double Bias     { get; set; }
        [NinjaScriptProperty, Range(-1,1)]
        [Display(Name="Bias Mode (-1 Short, 0 Auto, +1 Long)", GroupName="Weights", Order=7)]
        public int BiasMode { get; set; }

        // Layout
        [NinjaScriptProperty, Range(1,10)]
        [Display(Name="Bars Per Cell (width)", GroupName="Layout", Order=0)]
        public int BarsPerCell { get; set; }

        [NinjaScriptProperty, Range(6,200)]
        [Display(Name="Cell Height (ticks)", GroupName="Layout", Order=1)]
        public int CellHeightTicks { get; set; }

        [NinjaScriptProperty, Range(0,200)]
        [Display(Name="Vertical Pad (ticks)", GroupName="Layout", Order=2)]
        public int VerticalPadTicks { get; set; }

        [NinjaScriptProperty, Range(0,300)]
        [Display(Name="Header Height (ticks)", GroupName="Layout", Order=3)]
        public int HeaderHeightTicks { get; set; }

        // =======================
        // Internals
        // =======================
        private const int  OpacityFull = 85;  // 0..100
        private const int  OpacityDim  = 35;
        private const int  ColGapBars  = 1;

        private ADX   adx14, adx8, adx20;        // we’ll reuse cached indicator instances
        private EMA   ema8, ema24, ema5, ema13, ema21, ema34;
        private Series<double> vwapSeries, chop14, chop20;
        private double cumPV, cumVol;

        private SimpleFont fHeader = new SimpleFont("Segoe UI Semibold", 16) { Bold = true };
        private SimpleFont fCell   = new SimpleFont("Segoe UI", 13);

        // Column spec (lookbacks per window)
        private struct WinSpec
        {
            public string Name;
            public int RVolPeriod;
            public int ADXPeriod;
            public int EMAFast;
            public int EMASlow;
            public int ChopN;
        }
        private WinSpec[] specs;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "GoHUDMatrix";
                Calculate                = Calculate.OnEachTick;
                IsOverlay                = true;
                DisplayInDataBox         = false;
                IsSuspendedWhileInactive = true;

                GoThreshold = 70; YellowFloor = 50;
                RequireAdxGate = false; AdxGate = 18;

                UseFirstHourMicro = true;
                DimInactive       = true;

                UseRVol = true; UseUD = true; UseAdxSl = true; UseChop = true; UseVWAP = true; UseRegime = true;

                W_RVol = 0.8; W_UD = 0.5; W_ADXSL = 1.0; W_CHOP = 0.6; W_VWAP = 0.9; W_Regime = 0.8;
                Bias = -0.5; BiasMode = 0;

                BarsPerCell = 3;
                CellHeightTicks  = 60;
                VerticalPadTicks = 20;
                HeaderHeightTicks = 30;

                AddPlot(Brushes.Transparent, "Probability");
            }
            else if (State == State.DataLoaded)
            {
                // Cache commonly used indicator instances
                adx14 = ADX(14); adx8 = ADX(8); adx20 = ADX(20);
                ema8  = EMA(8);  ema24 = EMA(24);
                ema5  = EMA(5);  ema13 = EMA(13);
                ema21 = EMA(21); ema34 = EMA(34);

                chop14 = new Series<double>(this);
                chop20 = new Series<double>(this);
                vwapSeries = new Series<double>(this);
                cumPV = 0; cumVol = 0;

                // Window-specific lookbacks (tweak to taste)
                specs = new[]
                {
                    new WinSpec{ Name="FH",  RVolPeriod=20, ADXPeriod=8,  EMAFast=5,  EMASlow=13, ChopN=10 },
                    new WinSpec{ Name="IB",  RVolPeriod=30, ADXPeriod=10, EMAFast=8,  EMASlow=21, ChopN=14 },
                    new WinSpec{ Name="Bal", RVolPeriod=40, ADXPeriod=14, EMAFast=8,  EMASlow=24, ChopN=14 },
                    new WinSpec{ Name="Pri", RVolPeriod=60, ADXPeriod=20, EMAFast=13, EMASlow=34, ChopN=20 },
                    // GO column will use “Pri” numbers for a stable read; it’s still a union window for gating.
                };
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 60) return;

            // Session VWAP
            if (Bars.IsFirstBarOfSession) { cumPV = 0; cumVol = 0; }
            double tp = (High[0] + Low[0] + Close[0]) / 3.0;
            cumPV  += tp * Volume[0];
            cumVol += Math.Max(1.0, Volume[0]);
            vwapSeries[0] = cumPV / cumVol;

            // Time windows + gate
            bool inFH  = InFirstHourMicro(Time[0]);
            bool inIB  = InIB(Time[0]);
            bool inBal = InBalance(Time[0]);
            bool inPri = InPrimary(Time[0]);
            bool adxOk = !RequireAdxGate || ADX(AdxGate)[0] >= AdxGate;

            // Compute feature scores per spec (per-column)
            double[] p = new double[5];        // FH, IB, Bal, Pri, GO
            var cols  = new string[] { "FH", "IB", "Bal", "Pri", "GO" };
            bool[] win = new bool[] { inFH, inIB, inBal, inPri, (inFH || inIB || inBal || inPri) };

            // Feature arrays for drawing cells
            (string label, double[] vals)[] rowData = new (string, double[])[]
            {
                ("RVol", new double[5]),
                ("U/D",  new double[5]),
                ("ADX",  new double[5]),
                ("Chop", new double[5]),
                ("VWAP", new double[5]),
                ("Reg",  new double[5]),
            };

            for (int i = 0; i < 4; i++)
            {
                var sp = specs[i];

                // RVol with window-specific period
                double sR = UseRVol ? ScoreRVolPeriod(sp.RVolPeriod) : 0.0;

                // U/D (kept as-is; could add volume period if you want)
                double sUD = UseUD ? ScoreUD() : 0.0;

                // ADX/Slope with window-specific ADX and EMAs
                double sAS = UseAdxSl ? ScoreAdxSlopePeriod(sp.ADXPeriod, sp.EMAFast, sp.EMASlow) : 0.0;

                // Chop with window-specific N
                double sCh = UseChop ? ScoreChopN(sp.ChopN) : 0.0;

                // VWAP alignment (same for all)
                double sVw = UseVWAP ? ScoreVWAP() : 0.0;

                // Regime via ADX strength (use ADX period here too)
                double sRg = UseRegime ? ScoreRegimePeriod(sp.ADXPeriod) : 0.0;

                // Per-column p
                p[i] = ProbabilityFromScores(sR, sUD, sAS, sCh, sVw, sRg, win[i]);

                // Fill row arrays
                rowData[0].vals[i] = sR;
                rowData[1].vals[i] = sUD;
                rowData[2].vals[i] = sAS;
                rowData[3].vals[i] = sCh;
                rowData[4].vals[i] = sVw;
                rowData[5].vals[i] = sRg;
            }

            // GO column — use Pri spec for feature calcs, but gate by union of windows
            {
                var sp = specs[3];
                double sR = UseRVol ? ScoreRVolPeriod(sp.RVolPeriod) : 0.0;
                double sUD= UseUD   ? ScoreUD() : 0.0;
                double sAS= UseAdxSl? ScoreAdxSlopePeriod(sp.ADXPeriod, sp.EMAFast, sp.EMASlow) : 0.0;
                double sCh= UseChop ? ScoreChopN(sp.ChopN) : 0.0;
                double sVw= UseVWAP ? ScoreVWAP() : 0.0;
                double sRg= UseRegime ? ScoreRegimePeriod(sp.ADXPeriod) : 0.0;

                p[4] = ProbabilityFromScores(sR,sUD,sAS,sCh,sVw,sRg, win[4]);

                rowData[0].vals[4] = sR;
                rowData[1].vals[4] = sUD;
                rowData[2].vals[4] = sAS;
                rowData[3].vals[4] = sCh;
                rowData[4].vals[4] = sVw;
                rowData[5].vals[4] = sRg;
            }

            // Draw
            DrawMatrix(cols, p, win, adxOk, rowData);
        }

        // ============== Drawing ==============
        private void DrawMatrix(string[] colNames, double[] p, bool[] inWin, bool adxOk,
                                (string label, double[] vals)[] rows)
        {
            int cols = colNames.Length;                 // 5 (FH, IB, Bal, Pri, GO)
            int featRows = rows.Length;                 // 6
            double cellH = Math.Max(6*TickSize, CellHeightTicks * TickSize);
            double baseY = MIN(Low, 50)[0] - VerticalPadTicks * TickSize;

            int span = Math.Max(1, BarsPerCell);
            int colWidth = span + ColGapBars;
            int cursor = 0;

            Func<double, Brush> map = s =>
            {
                double n = 0.5 * (s + 1.0);  // 0..1
                if (n >= 0.66) return Brushes.LimeGreen;
                if (n >= 0.33) return Brushes.Gold;
                return Brushes.IndianRed;
            };
            Func<double, Brush> goColor = prob =>
            {
                if (prob >= GoThreshold) return Brushes.LimeGreen;
                if (prob >= YellowFloor) return Brushes.Gold;
                return Brushes.IndianRed;
            };

            cursor = 0;
            for (int c = 0; c < cols; c++)
            {
                int start = cursor + span;
                int end   = cursor;
                int mid   = end + (start - end) / 2;

                // Header title + p
                var ht = Draw.Text(this, $"mx_hdr_{c}", colNames[c], mid,
                                   baseY + (featRows+0.25)*cellH + HeaderHeightTicks*TickSize,
                                   Brushes.White);
                ht.Font = fHeader;

                string hp = c == cols - 1
                    ? $"GO {p[c]:0}% {(adxOk ? "| ADX OK" : "| ADX NO")}"
                    : $"p={p[c]:0}%";
                var hpTxt = Draw.Text(this, $"mx_hdrp_{c}", hp, mid,
                                      baseY + (featRows-0.15)*cellH + HeaderHeightTicks*TickSize,
                                      Brushes.White);
                hpTxt.Font = fCell;

                for (int r = 0; r < featRows; r++)
                {
                    double y0 = baseY + r*cellH;
                    double y1 = y0 + cellH;

                    bool activeCol = inWin[c];
                    int  op = (DimInactive && !activeCol && c != cols-1) ? 35 : 85;

                    Brush fill = (c == cols - 1) ? goColor(p[c]) : map(rows[r].vals[c]);

                    Draw.Rectangle(this, $"mx_{r}_{c}", true, start, y0, end, y1,
                        Brushes.Transparent, fill, op);

                    if (c == 0)
                    {
                        var rl = Draw.Text(this, $"mx_rowlbl_{r}", rows[r].label,
                                           start + span + 2, y0 + 0.55*cellH, Brushes.White);
                        rl.Font = fCell;
                    }
                }

                cursor += colWidth;
            }
        }

        // ============== Probability & Scores ==============
        private double ProbabilityFromScores(double sR, double sUD, double sAS, double sCh, double sVw, double sRg, bool inWindow)
        {
            double z = Bias
                     + W_RVol   * sR
                     + W_UD     * sUD
                     + W_ADXSL  * sAS
                     + W_CHOP   * sCh
                     + W_VWAP   * sVw
                     + W_Regime * sRg;
            if (inWindow) z += 0.2;
            return 100.0 * (1.0 / (1.0 + Math.Exp(-z)));
        }

        private double ScoreRVolPeriod(int period)
        {
            double avg = Math.Max(1.0, SMA(Volume, period)[0]);
            double rv  = Volume[0] / avg;
            double s   = (rv - 0.5) / 1.5;
            s = Math.Max(0, Math.Min(1, s));
            return 2*s - 1;
        }
        private double ScoreUD()
        {
            double sign = Close[0] > Open[0] ? 1 : (Close[0] < Open[0] ? -1 : 0);
            double avg  = Math.Max(1.0, SMA(Volume, 50)[0]);
            double vN   = Math.Min(2.0, Volume[0] / avg);
            double s    = sign * Math.Min(1.0, (vN - 0.5) / 0.5);
            return s;
        }
        private double ScoreAdxSlopePeriod(int adxPeriod, int emaFastP, int emaSlowP)
        {
            double dir = DirectionSignPeriod(emaFastP, emaSlowP);
            double a   = ADX(adxPeriod)[0];
            double adxFac = Math.Min(1.0, a / 40.0);
            return dir * adxFac;
        }
        private double ScoreChopN(int n)
        {
            double hh = MAX(High, n)[0];
            double ll = MIN(Low,  n)[0];
            double range = Math.Max(TickSize, hh - ll);
            double sumTr = ATR(n)[0] * n;
            double chop = 100.0 * Math.Log10(sumTr / range) / Math.Log10(n);
            chop = Math.Max(0, Math.Min(100, chop));
            double s = 1.0 - (chop - 10.0) / 90.0;
            s = Math.Max(-1, Math.Min(1, s * 2 - 1));
            return s;
        }
        private double ScoreVWAP()
        {
            double distTicks = (Close[0] - vwapSeries[0]) / TickSize;
            double aligned   = DirectionSignPeriod(8,24) * distTicks;
            return Math.Max(-1, Math.Min(1, aligned / 10.0));
        }
        private double ScoreRegimePeriod(int adxPeriod)
        {
            double a = ADX(adxPeriod)[0];
            double s = (a - 15.0) / 15.0;
            return Math.Max(-1, Math.Min(1, s));
        }

        private int HardBias()
        {
            if (BiasMode > 0) return  1;
            if (BiasMode < 0) return -1;
            return 0;
        }
        private double DirectionSignPeriod(int fast, int slow)
        {
            int hb = HardBias();
            if (hb != 0) return hb;

            double slope = EMA(fast)[0] - EMA(fast)[1];
            double pos   = EMA(fast)[0] - EMA(slow)[0];
            double s = 0.6 * Math.Sign(pos) + 0.4 * Math.Sign(slope);
            if (s > 0) return  1;
            if (s < 0) return -1;
            return 0;
        }

        // ============== Windows ==============
        private bool InFirstHourMicro(DateTime t)
        {
            var td = t.TimeOfDay;
            return UseFirstHourMicro && td >= new TimeSpan(9,30,0) && td < new TimeSpan(9,45,0);
        }
        private bool InIB(DateTime t)
        {
            var td = t.TimeOfDay;
            return td >= new TimeSpan(9,30,0) && td < new TimeSpan(10,00,0);
        }
        private bool InBalance(DateTime t)
        {
            var td = t.TimeOfDay;
            return td >= new TimeSpan(10,00,0) && td < new TimeSpan(10,15,0);
        }
        private bool InPrimary(DateTime t)
        {
            var td = t.TimeOfDay;
            return td >= new TimeSpan(10,15,0) && td < new TimeSpan(11,45,0);
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GoHUDMatrix[] cacheGoHUDMatrix;
		public GoHUDMatrix GoHUDMatrix(int goThreshold, int yellowFloor, bool requireAdxGate, int adxGate, bool useFirstHourMicro, bool dimInactive, bool useRVol, bool useUD, bool useAdxSl, bool useChop, bool useVWAP, bool useRegime, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias, int biasMode, int barsPerCell, int cellHeightTicks, int verticalPadTicks, int headerHeightTicks)
		{
			return GoHUDMatrix(Input, goThreshold, yellowFloor, requireAdxGate, adxGate, useFirstHourMicro, dimInactive, useRVol, useUD, useAdxSl, useChop, useVWAP, useRegime, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias, biasMode, barsPerCell, cellHeightTicks, verticalPadTicks, headerHeightTicks);
		}

		public GoHUDMatrix GoHUDMatrix(ISeries<double> input, int goThreshold, int yellowFloor, bool requireAdxGate, int adxGate, bool useFirstHourMicro, bool dimInactive, bool useRVol, bool useUD, bool useAdxSl, bool useChop, bool useVWAP, bool useRegime, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias, int biasMode, int barsPerCell, int cellHeightTicks, int verticalPadTicks, int headerHeightTicks)
		{
			if (cacheGoHUDMatrix != null)
				for (int idx = 0; idx < cacheGoHUDMatrix.Length; idx++)
					if (cacheGoHUDMatrix[idx] != null && cacheGoHUDMatrix[idx].GoThreshold == goThreshold && cacheGoHUDMatrix[idx].YellowFloor == yellowFloor && cacheGoHUDMatrix[idx].RequireAdxGate == requireAdxGate && cacheGoHUDMatrix[idx].AdxGate == adxGate && cacheGoHUDMatrix[idx].UseFirstHourMicro == useFirstHourMicro && cacheGoHUDMatrix[idx].DimInactive == dimInactive && cacheGoHUDMatrix[idx].UseRVol == useRVol && cacheGoHUDMatrix[idx].UseUD == useUD && cacheGoHUDMatrix[idx].UseAdxSl == useAdxSl && cacheGoHUDMatrix[idx].UseChop == useChop && cacheGoHUDMatrix[idx].UseVWAP == useVWAP && cacheGoHUDMatrix[idx].UseRegime == useRegime && cacheGoHUDMatrix[idx].W_RVol == w_RVol && cacheGoHUDMatrix[idx].W_UD == w_UD && cacheGoHUDMatrix[idx].W_ADXSL == w_ADXSL && cacheGoHUDMatrix[idx].W_CHOP == w_CHOP && cacheGoHUDMatrix[idx].W_VWAP == w_VWAP && cacheGoHUDMatrix[idx].W_Regime == w_Regime && cacheGoHUDMatrix[idx].Bias == bias && cacheGoHUDMatrix[idx].BiasMode == biasMode && cacheGoHUDMatrix[idx].BarsPerCell == barsPerCell && cacheGoHUDMatrix[idx].CellHeightTicks == cellHeightTicks && cacheGoHUDMatrix[idx].VerticalPadTicks == verticalPadTicks && cacheGoHUDMatrix[idx].HeaderHeightTicks == headerHeightTicks && cacheGoHUDMatrix[idx].EqualsInput(input))
						return cacheGoHUDMatrix[idx];
			return CacheIndicator<GoHUDMatrix>(new GoHUDMatrix(){ GoThreshold = goThreshold, YellowFloor = yellowFloor, RequireAdxGate = requireAdxGate, AdxGate = adxGate, UseFirstHourMicro = useFirstHourMicro, DimInactive = dimInactive, UseRVol = useRVol, UseUD = useUD, UseAdxSl = useAdxSl, UseChop = useChop, UseVWAP = useVWAP, UseRegime = useRegime, W_RVol = w_RVol, W_UD = w_UD, W_ADXSL = w_ADXSL, W_CHOP = w_CHOP, W_VWAP = w_VWAP, W_Regime = w_Regime, Bias = bias, BiasMode = biasMode, BarsPerCell = barsPerCell, CellHeightTicks = cellHeightTicks, VerticalPadTicks = verticalPadTicks, HeaderHeightTicks = headerHeightTicks }, input, ref cacheGoHUDMatrix);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GoHUDMatrix GoHUDMatrix(int goThreshold, int yellowFloor, bool requireAdxGate, int adxGate, bool useFirstHourMicro, bool dimInactive, bool useRVol, bool useUD, bool useAdxSl, bool useChop, bool useVWAP, bool useRegime, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias, int biasMode, int barsPerCell, int cellHeightTicks, int verticalPadTicks, int headerHeightTicks)
		{
			return indicator.GoHUDMatrix(Input, goThreshold, yellowFloor, requireAdxGate, adxGate, useFirstHourMicro, dimInactive, useRVol, useUD, useAdxSl, useChop, useVWAP, useRegime, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias, biasMode, barsPerCell, cellHeightTicks, verticalPadTicks, headerHeightTicks);
		}

		public Indicators.GoHUDMatrix GoHUDMatrix(ISeries<double> input , int goThreshold, int yellowFloor, bool requireAdxGate, int adxGate, bool useFirstHourMicro, bool dimInactive, bool useRVol, bool useUD, bool useAdxSl, bool useChop, bool useVWAP, bool useRegime, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias, int biasMode, int barsPerCell, int cellHeightTicks, int verticalPadTicks, int headerHeightTicks)
		{
			return indicator.GoHUDMatrix(input, goThreshold, yellowFloor, requireAdxGate, adxGate, useFirstHourMicro, dimInactive, useRVol, useUD, useAdxSl, useChop, useVWAP, useRegime, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias, biasMode, barsPerCell, cellHeightTicks, verticalPadTicks, headerHeightTicks);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GoHUDMatrix GoHUDMatrix(int goThreshold, int yellowFloor, bool requireAdxGate, int adxGate, bool useFirstHourMicro, bool dimInactive, bool useRVol, bool useUD, bool useAdxSl, bool useChop, bool useVWAP, bool useRegime, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias, int biasMode, int barsPerCell, int cellHeightTicks, int verticalPadTicks, int headerHeightTicks)
		{
			return indicator.GoHUDMatrix(Input, goThreshold, yellowFloor, requireAdxGate, adxGate, useFirstHourMicro, dimInactive, useRVol, useUD, useAdxSl, useChop, useVWAP, useRegime, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias, biasMode, barsPerCell, cellHeightTicks, verticalPadTicks, headerHeightTicks);
		}

		public Indicators.GoHUDMatrix GoHUDMatrix(ISeries<double> input , int goThreshold, int yellowFloor, bool requireAdxGate, int adxGate, bool useFirstHourMicro, bool dimInactive, bool useRVol, bool useUD, bool useAdxSl, bool useChop, bool useVWAP, bool useRegime, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias, int biasMode, int barsPerCell, int cellHeightTicks, int verticalPadTicks, int headerHeightTicks)
		{
			return indicator.GoHUDMatrix(input, goThreshold, yellowFloor, requireAdxGate, adxGate, useFirstHourMicro, dimInactive, useRVol, useUD, useAdxSl, useChop, useVWAP, useRegime, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias, biasMode, barsPerCell, cellHeightTicks, verticalPadTicks, headerHeightTicks);
		}
	}
}

#endregion
