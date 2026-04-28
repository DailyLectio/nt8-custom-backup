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
    /// Go/No-Go HUD (WPF-only). Renders colored tiles for feature participation,
    /// gate bars for Window/ADX, a final GO tile, and a text readout:
    /// "GO xx% | WIN ... | ADX ...". Probability = logistic of feature scores.
    /// </summary>
    public class GoNoGoHUD_WPF : Indicator
    {
        // ======== Parameters ========
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
        [Display(Name="Restrict To Windows (IB/Balance/Primary)", GroupName="Windows", Order=1)]
        public bool RestrictToWindows { get; set; }

        // Feature toggles
        [NinjaScriptProperty] [Display(Name="Use RVol",    GroupName="Features", Order=0)] public bool UseRVol   { get; set; }
        [NinjaScriptProperty] [Display(Name="Use U/D",      GroupName="Features", Order=1)] public bool UseUD     { get; set; }
        [NinjaScriptProperty] [Display(Name="Use ADX/Slope",GroupName="Features", Order=2)] public bool UseAdxSl  { get; set; }
        [NinjaScriptProperty] [Display(Name="Use CHOP",     GroupName="Features", Order=3)] public bool UseChop   { get; set; }
        [NinjaScriptProperty] [Display(Name="Use VWAP",     GroupName="Features", Order=4)] public bool UseVWAP   { get; set; }
        [NinjaScriptProperty] [Display(Name="Use Regime",   GroupName="Features", Order=5)] public bool UseRegime { get; set; }

        // Weights
        [NinjaScriptProperty] [Display(Name="w_RVol",   GroupName="Weights", Order=0)] public double W_RVol   { get; set; }
        [NinjaScriptProperty] [Display(Name="w_UD",     GroupName="Weights", Order=1)] public double W_UD     { get; set; }
        [NinjaScriptProperty] [Display(Name="w_ADXSL",  GroupName="Weights", Order=2)] public double W_ADXSL  { get; set; }
        [NinjaScriptProperty] [Display(Name="w_CHOP",   GroupName="Weights", Order=3)] public double W_CHOP   { get; set; }
        [NinjaScriptProperty] [Display(Name="w_VWAP",   GroupName="Weights", Order=4)] public double W_VWAP   { get; set; }
        [NinjaScriptProperty] [Display(Name="w_Regime", GroupName="Weights", Order=5)] public double W_Regime { get; set; }
        [NinjaScriptProperty] [Display(Name="Bias",     GroupName="Weights", Order=6)] public double Bias     { get; set; }

        // Layout (pure WPF Draw.*)
        [NinjaScriptProperty, Range(1, 10)]
        [Display(Name="Bars Per Tile (width)", GroupName="HUD Layout", Order=0)]
        public int BarsPerTile { get; set; }

        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name="Tile Height (ticks)", GroupName="HUD Layout", Order=1)]
        public int TileHeightTicks { get; set; }

        [NinjaScriptProperty, Range(0, 200)]
        [Display(Name="Tile Vertical Pad (ticks)", GroupName="HUD Layout", Order=2)]
        public int TilePadTicks { get; set; }

        [NinjaScriptProperty, Range(0, 300)]
        [Display(Name="Alert Cooldown (sec)", GroupName="GO/NO-GO", Order=10)]
        public int AlertCooldownSec { get; set; }

        [NinjaScriptProperty, Range(-1, 1)]
        [Display(Name="Bias Mode (-1 Short, 0 Auto, +1 Long)", GroupName="GO/NO-GO", Order=11)]
        public int BiasMode { get; set; }

        // ======== Internals ========
        private const int TileOpacity = 85; // 0–100 area fill opacity

        private ADX   adx;
        private EMA   emaFast, emaSlow;
        private Series<double> vwapSeries, chopSeries;
        private double cumPV, cumVol;

        private SimpleFont hudFont   = new SimpleFont("Segoe UI Semibold", 18) { Bold = true };
        private SimpleFont smallFont = new SimpleFont("Segoe UI", 14);

        // ======== Lifecycle ========
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "GoNoGoHUD_WPF";
                Calculate                = Calculate.OnEachTick;
                IsOverlay                = true;
                DisplayInDataBox         = false;
                IsSuspendedWhileInactive = true;

                GoThreshold      = 70;
                YellowFloor      = 50;
                RequireAdxGate   = false;
                AdxGate          = 18;

                UseFirstHourMicro = true;
                RestrictToWindows = false;

                UseRVol = true; UseUD = true; UseAdxSl = true; UseChop = true; UseVWAP = true; UseRegime = true;

                W_RVol = 0.8; W_UD = 0.5; W_ADXSL = 1.0; W_CHOP = 0.6; W_VWAP = 0.9; W_Regime = 0.8; Bias = -0.5;

                BarsPerTile     = 3;
                TileHeightTicks = 50;
                TilePadTicks    = 20;

                AlertCooldownSec = 90;
                BiasMode         = 0;

                AddPlot(Brushes.Transparent, "Probability");
            }
            else if (State == State.DataLoaded)
            {
                adx        = ADX(14);
                emaFast    = EMA(8);
                emaSlow    = EMA(24);
                vwapSeries = new Series<double>(this);
                chopSeries = new Series<double>(this);
                cumPV = 0; cumVol = 0;
            }
        }

        // ======== Core calc ========
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 60)
            {
                Values[0][0] = 0;
                return;
            }

            // Session VWAP
            if (Bars.IsFirstBarOfSession) { cumPV = 0; cumVol = 0; }
            double tp = (High[0] + Low[0] + Close[0]) / 3.0;
            cumPV  += tp * Volume[0];
            cumVol += Math.Max(1.0, Volume[0]);
            vwapSeries[0] = cumPV / cumVol;

            // Feature scores
            double sRvol   = UseRVol   ? ScoreRVol()     : 0.0;
            double sUD     = UseUD     ? ScoreUD()       : 0.0;
            double sAdxSl  = UseAdxSl  ? ScoreAdxSlope() : 0.0;
            double sChop   = UseChop   ? ScoreChop()     : 0.0;
            double sVwap   = UseVWAP   ? ScoreVWAP()     : 0.0;
            double sRegime = UseRegime ? ScoreRegime()   : 0.0;

            bool inWindow = InTradingWindows(Time[0]);
            bool adxOk    = !RequireAdxGate || adx[0] >= AdxGate;

            double z = Bias
                     + W_RVol   * sRvol
                     + W_UD     * sUD
                     + W_ADXSL  * sAdxSl
                     + W_CHOP   * sChop
                     + W_VWAP   * sVwap
                     + W_Regime * sRegime;

            if (inWindow) z += 0.2;

            double p = 100.0 * (1.0 / (1.0 + Math.Exp(-z)));
            Values[0][0] = p;

            // Alert when GO
            if (p >= GoThreshold && inWindow && adxOk &&
                (Time[0] - lastAlert).TotalSeconds >= AlertCooldownSec)
            {
                Alert("GoNoGoHUD_WPF_GO", Priority.High,
                    $"GO: p={p:F0}% | ADX={adx[0]:F1} | {CurrentRegime()} | {(inWindow ? "WIN:ON" : "WIN:OFF")}",
                    "Alert1.wav", 10, Brushes.White, Brushes.DarkGreen);
                lastAlert = Time[0];
            }

            // Draw the HUD near the last bar
            DrawHudWpf(p, inWindow, adxOk, sRvol, sUD, sAdxSl, sChop, sVwap, sRegime);
        }

        private DateTime lastAlert = Core.Globals.MinDate;

        // ======== WPF HUD ========
        private void DrawHudWpf(double p, bool inWindow, bool adxOk,
                                double sR, double sUD, double sAS, double sCh, double sVw, double sRg)
        {
            // Baseline just below recent lows so HUD sits at the bottom on a blank chart
            double baseY = MIN(Low, 50)[0] - TilePadTicks * TickSize;
            double h     = Math.Max(5 * TickSize, TileHeightTicks * TickSize);
            int    span  = Math.Max(1, BarsPerTile);

            // Color map [-1,1] → Brush
            Func<double, Brush> col = score =>
            {
                double n = 0.5 * (score + 1.0); // 0..1
                if (n >= 0.66) return Brushes.LimeGreen;
                if (n >= 0.33) return Brushes.Gold;
                return Brushes.IndianRed;
            };

            // Tile helper: rectangle + centered label
            int cursor = 0; // barsAgo at right edge of the tile block
            Action<string,string,Brush> tile = (id, label, fill) =>
            {
                int start = cursor + span;  // older bar (left)
                int end   = cursor;         // newest bar (right)

                Draw.Rectangle(this, id, true,
                    start, baseY, end, baseY + h,
                    Brushes.Transparent, fill, TileOpacity);

                int mid = end + (start - end) / 2;
                var tl  = Draw.Text(this, id + "_lbl", label, mid, baseY + h + 0.30 * h, Brushes.White);
                tl.Font = smallFont;

                cursor += span + 1;         // small gap (1 bar)
            };

            // Tiles (right→left): Regime, VWAP, CHOP, ADX/Slope, U/D, RVol
            cursor = 0;
            tile("gn_reg",  "Reg",  col(sRg));
            tile("gn_vwp",  "VWAP", col(sVw));
            tile("gn_chp",  "Chop", col(sCh));
            tile("gn_adx",  "ADX",  col(sAS));
            tile("gn_ud",   "U/D",  col(sUD));
            tile("gn_rv",   "RVol", col(sR));

            // Gate bars (1-bar width each)
            Brush winBrush = inWindow ? Brushes.LimeGreen : Brushes.IndianRed;
            Brush adxBrush = adxOk    ? Brushes.LimeGreen : Brushes.IndianRed;

            Draw.Rectangle(this, "gn_gate_win", true, cursor + 1, baseY, cursor, baseY + h,
                Brushes.Transparent, winBrush, TileOpacity);
            cursor += 2;

            Draw.Rectangle(this, "gn_gate_adx", true, cursor + 1, baseY, cursor, baseY + h,
                Brushes.Transparent, adxBrush, TileOpacity);
            cursor += 2;

            // Final GO tile (wider)
            Brush goBrush = (p >= GoThreshold && inWindow && adxOk) ? Brushes.LimeGreen
                          : (p >= YellowFloor ? Brushes.Gold : Brushes.IndianRed);

            int goSpan  = span + 2;
            int goStart = cursor + goSpan;
            int goEnd   = cursor;

            Draw.Rectangle(this, "gn_go", true, goStart, baseY, goEnd, baseY + h,
                Brushes.Transparent, goBrush, TileOpacity);

            int goMid = goEnd + (goStart - goEnd) / 2;
            var golbl = Draw.Text(this, "gn_go_lbl", "GO", goMid, baseY + h + 0.30 * h, Brushes.White);
            golbl.Font = smallFont;

            // Main readout
            var tmain = Draw.Text(this, "gn_text",
                $"GO {p:0}%   |   WIN {(inWindow ? "ON" : "OFF")}   |   ADX {(adxOk ? "OK" : "NO")}",
                0, baseY - 0.60 * h, Brushes.White);
            tmain.Font = hudFont;

            // Beacon on last bar (handy while testing)
            Draw.VerticalLine(this, "gn_beacon", 0, Brushes.Lime);
        }

        // ======== Scores ========
        private double ScoreRVol()
        {
            double avg = Math.Max(1.0, SMA(Volume, 50)[0]);
            double rv  = Volume[0] / avg;
            double s   = (rv - 0.5) / 1.5;                // 0.5x→0, ~2x→1
            s = Math.Max(0, Math.Min(1, s));
            return 2 * s - 1;                             // [-1,1]
        }

        private double ScoreUD()
        {
            double sign = Close[0] > Open[0] ? 1 : (Close[0] < Open[0] ? -1 : 0);
            double avg  = Math.Max(1.0, SMA(Volume, 50)[0]);
            double vN   = Math.Min(2.0, Volume[0] / avg);
            double s    = sign * Math.Min(1.0, (vN - 0.5) / 0.5);
            return s;
        }

        private double ScoreAdxSlope()
        {
            double dir    = DirectionSign();              // -1..+1
            double adxFac = Math.Min(1.0, adx[0] / 40.0); // 0..1
            return dir * adxFac;
        }

        private double ScoreChop()
        {
            int n = 14;
            double hh = MAX(High, n)[0];
            double ll = MIN(Low,  n)[0];
            double range = Math.Max(TickSize, hh - ll);
            double sumTr = ATR(n)[0] * n;                 // approx
            double chop = 100.0 * Math.Log10(sumTr / range) / Math.Log10(n);
            chop = Math.Max(0, Math.Min(100, chop));
            chopSeries[0] = chop;

            double s = 1.0 - (chop - 10.0) / 90.0;        // lower chop → more trend
            s = Math.Max(-1, Math.Min(1, s * 2 - 1));
            return s;
        }

        private double ScoreVWAP()
        {
            double distTicks = (Close[0] - vwapSeries[0]) / TickSize;
            double aligned   = DirectionSign() * distTicks; // positive = aligned
            return Math.Max(-1, Math.Min(1, aligned / 10.0));
        }

        private double ScoreRegime()
        {
            double s = (adx[0] - 15.0) / 15.0;
            return Math.Max(-1, Math.Min(1, s));
        }

        private int HardBias()
        {
            if (BiasMode > 0) return  1;
            if (BiasMode < 0) return -1;
            return 0;
        }

        private double DirectionSign()
        {
            int hb = HardBias();
            if (hb != 0) return hb;

            double slope = emaFast[0] - emaFast[1];
            double pos   = emaFast[0] - emaSlow[0];

            double s = 0.6 * Math.Sign(pos) + 0.4 * Math.Sign(slope);
            if (s > 0) return  1;
            if (s < 0) return -1;
            return 0;
        }

        private string CurrentRegime() => adx[0] >= 20 ? "TREND" : "BAL";

        private bool InTradingWindows(DateTime t)
        {
            var tod = t.TimeOfDay;
            bool firstMicro = UseFirstHourMicro && tod >= new TimeSpan(9,30,0) && tod < new TimeSpan(9,45,0);
            bool ibBuild    =                      tod >= new TimeSpan(9,30,0) && tod < new TimeSpan(10,00,0);
            bool balance    =                      tod >= new TimeSpan(10,00,0) && tod < new TimeSpan(10,15,0);
            bool primary    =                      tod >= new TimeSpan(10,15,0) && tod < new TimeSpan(11,45,0);
            return firstMicro || ibBuild || balance || primary;
        }

        [Browsable(false), XmlIgnore] public Series<double> Probability => Values[0];
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GoNoGoHUD_WPF[] cacheGoNoGoHUD_WPF;
		public GoNoGoHUD_WPF GoNoGoHUD_WPF(int goThreshold, int yellowFloor, bool requireAdxGate, int adxGate, bool useFirstHourMicro, bool restrictToWindows, bool useRVol, bool useUD, bool useAdxSl, bool useChop, bool useVWAP, bool useRegime, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias, int barsPerTile, int tileHeightTicks, int tilePadTicks, int alertCooldownSec, int biasMode)
		{
			return GoNoGoHUD_WPF(Input, goThreshold, yellowFloor, requireAdxGate, adxGate, useFirstHourMicro, restrictToWindows, useRVol, useUD, useAdxSl, useChop, useVWAP, useRegime, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias, barsPerTile, tileHeightTicks, tilePadTicks, alertCooldownSec, biasMode);
		}

		public GoNoGoHUD_WPF GoNoGoHUD_WPF(ISeries<double> input, int goThreshold, int yellowFloor, bool requireAdxGate, int adxGate, bool useFirstHourMicro, bool restrictToWindows, bool useRVol, bool useUD, bool useAdxSl, bool useChop, bool useVWAP, bool useRegime, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias, int barsPerTile, int tileHeightTicks, int tilePadTicks, int alertCooldownSec, int biasMode)
		{
			if (cacheGoNoGoHUD_WPF != null)
				for (int idx = 0; idx < cacheGoNoGoHUD_WPF.Length; idx++)
					if (cacheGoNoGoHUD_WPF[idx] != null && cacheGoNoGoHUD_WPF[idx].GoThreshold == goThreshold && cacheGoNoGoHUD_WPF[idx].YellowFloor == yellowFloor && cacheGoNoGoHUD_WPF[idx].RequireAdxGate == requireAdxGate && cacheGoNoGoHUD_WPF[idx].AdxGate == adxGate && cacheGoNoGoHUD_WPF[idx].UseFirstHourMicro == useFirstHourMicro && cacheGoNoGoHUD_WPF[idx].RestrictToWindows == restrictToWindows && cacheGoNoGoHUD_WPF[idx].UseRVol == useRVol && cacheGoNoGoHUD_WPF[idx].UseUD == useUD && cacheGoNoGoHUD_WPF[idx].UseAdxSl == useAdxSl && cacheGoNoGoHUD_WPF[idx].UseChop == useChop && cacheGoNoGoHUD_WPF[idx].UseVWAP == useVWAP && cacheGoNoGoHUD_WPF[idx].UseRegime == useRegime && cacheGoNoGoHUD_WPF[idx].W_RVol == w_RVol && cacheGoNoGoHUD_WPF[idx].W_UD == w_UD && cacheGoNoGoHUD_WPF[idx].W_ADXSL == w_ADXSL && cacheGoNoGoHUD_WPF[idx].W_CHOP == w_CHOP && cacheGoNoGoHUD_WPF[idx].W_VWAP == w_VWAP && cacheGoNoGoHUD_WPF[idx].W_Regime == w_Regime && cacheGoNoGoHUD_WPF[idx].Bias == bias && cacheGoNoGoHUD_WPF[idx].BarsPerTile == barsPerTile && cacheGoNoGoHUD_WPF[idx].TileHeightTicks == tileHeightTicks && cacheGoNoGoHUD_WPF[idx].TilePadTicks == tilePadTicks && cacheGoNoGoHUD_WPF[idx].AlertCooldownSec == alertCooldownSec && cacheGoNoGoHUD_WPF[idx].BiasMode == biasMode && cacheGoNoGoHUD_WPF[idx].EqualsInput(input))
						return cacheGoNoGoHUD_WPF[idx];
			return CacheIndicator<GoNoGoHUD_WPF>(new GoNoGoHUD_WPF(){ GoThreshold = goThreshold, YellowFloor = yellowFloor, RequireAdxGate = requireAdxGate, AdxGate = adxGate, UseFirstHourMicro = useFirstHourMicro, RestrictToWindows = restrictToWindows, UseRVol = useRVol, UseUD = useUD, UseAdxSl = useAdxSl, UseChop = useChop, UseVWAP = useVWAP, UseRegime = useRegime, W_RVol = w_RVol, W_UD = w_UD, W_ADXSL = w_ADXSL, W_CHOP = w_CHOP, W_VWAP = w_VWAP, W_Regime = w_Regime, Bias = bias, BarsPerTile = barsPerTile, TileHeightTicks = tileHeightTicks, TilePadTicks = tilePadTicks, AlertCooldownSec = alertCooldownSec, BiasMode = biasMode }, input, ref cacheGoNoGoHUD_WPF);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GoNoGoHUD_WPF GoNoGoHUD_WPF(int goThreshold, int yellowFloor, bool requireAdxGate, int adxGate, bool useFirstHourMicro, bool restrictToWindows, bool useRVol, bool useUD, bool useAdxSl, bool useChop, bool useVWAP, bool useRegime, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias, int barsPerTile, int tileHeightTicks, int tilePadTicks, int alertCooldownSec, int biasMode)
		{
			return indicator.GoNoGoHUD_WPF(Input, goThreshold, yellowFloor, requireAdxGate, adxGate, useFirstHourMicro, restrictToWindows, useRVol, useUD, useAdxSl, useChop, useVWAP, useRegime, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias, barsPerTile, tileHeightTicks, tilePadTicks, alertCooldownSec, biasMode);
		}

		public Indicators.GoNoGoHUD_WPF GoNoGoHUD_WPF(ISeries<double> input , int goThreshold, int yellowFloor, bool requireAdxGate, int adxGate, bool useFirstHourMicro, bool restrictToWindows, bool useRVol, bool useUD, bool useAdxSl, bool useChop, bool useVWAP, bool useRegime, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias, int barsPerTile, int tileHeightTicks, int tilePadTicks, int alertCooldownSec, int biasMode)
		{
			return indicator.GoNoGoHUD_WPF(input, goThreshold, yellowFloor, requireAdxGate, adxGate, useFirstHourMicro, restrictToWindows, useRVol, useUD, useAdxSl, useChop, useVWAP, useRegime, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias, barsPerTile, tileHeightTicks, tilePadTicks, alertCooldownSec, biasMode);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GoNoGoHUD_WPF GoNoGoHUD_WPF(int goThreshold, int yellowFloor, bool requireAdxGate, int adxGate, bool useFirstHourMicro, bool restrictToWindows, bool useRVol, bool useUD, bool useAdxSl, bool useChop, bool useVWAP, bool useRegime, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias, int barsPerTile, int tileHeightTicks, int tilePadTicks, int alertCooldownSec, int biasMode)
		{
			return indicator.GoNoGoHUD_WPF(Input, goThreshold, yellowFloor, requireAdxGate, adxGate, useFirstHourMicro, restrictToWindows, useRVol, useUD, useAdxSl, useChop, useVWAP, useRegime, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias, barsPerTile, tileHeightTicks, tilePadTicks, alertCooldownSec, biasMode);
		}

		public Indicators.GoNoGoHUD_WPF GoNoGoHUD_WPF(ISeries<double> input , int goThreshold, int yellowFloor, bool requireAdxGate, int adxGate, bool useFirstHourMicro, bool restrictToWindows, bool useRVol, bool useUD, bool useAdxSl, bool useChop, bool useVWAP, bool useRegime, double w_RVol, double w_UD, double w_ADXSL, double w_CHOP, double w_VWAP, double w_Regime, double bias, int barsPerTile, int tileHeightTicks, int tilePadTicks, int alertCooldownSec, int biasMode)
		{
			return indicator.GoNoGoHUD_WPF(input, goThreshold, yellowFloor, requireAdxGate, adxGate, useFirstHourMicro, restrictToWindows, useRVol, useUD, useAdxSl, useChop, useVWAP, useRegime, w_RVol, w_UD, w_ADXSL, w_CHOP, w_VWAP, w_Regime, bias, barsPerTile, tileHeightTicks, tilePadTicks, alertCooldownSec, biasMode);
		}
	}
}

#endregion
