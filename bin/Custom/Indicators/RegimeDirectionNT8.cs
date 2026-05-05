#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class RegimeDirectionNT8 : Indicator
    {
        private Series<double> rsiSeries;
        private Series<double> rsiMaSeries;
        private Series<double> dxSeries;

        private double prevAtrStop;
        private double prevAtrSuper;
        private double prevAvgGain;
        private double prevAvgLoss;
        private double prevMacdSignal;
        private double prevEma9;
        private double prevEma12;
        private double prevEma14;
        private double prevEma21;
        private double prevEma26;
        private double prevEma100a;
        private double prevEma100b;
        private double prevEma200a;
        private double prevEma200b;
        private double prevTrSmooth;
        private double prevDmPlusSmooth;
        private double prevDmMinusSmooth;
        private double prevFinalUpper;
        private double prevFinalLower;
        private bool prevSuperTrendUp = true;

        private const int BasisPlot = 0;
        private const int UpperPlot = 1;
        private const int LowerPlot = 2;

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Bollinger Period", GroupName = "1. Bollinger Bands", Order = 0)]
        public int BollingerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Bollinger StdDev Mult", GroupName = "1. Bollinger Bands", Order = 1)]
        public double BollingerStdDevMult { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Band Shading", GroupName = "1. Bollinger Bands", Order = 2)]
        public bool ShowBandShading { get; set; }

        [NinjaScriptProperty]
        [Range(10, 5000)]
        [Display(Name = "Shading Lookback Bars", GroupName = "1. Bollinger Bands", Order = 3)]
        public int ShadingLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Shading Opacity", GroupName = "1. Bollinger Bands", Order = 4)]
        public int ShadingOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Signal Markers", GroupName = "2. Signals", Order = 0)]
        public bool ShowSignalMarkers { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Signal Text", GroupName = "2. Signals", Order = 1)]
        public bool ShowSignalText { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Signal Text Offset Ticks", GroupName = "2. Signals", Order = 2)]
        public int SignalTextOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require SuperTrend Filter", GroupName = "2. Signals", Order = 3)]
        public bool RequireSuperTrendFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ATR Stop Period", GroupName = "2. Signals", Order = 4)]
        public int AtrStopPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "ATR Stop Mult", GroupName = "2. Signals", Order = 5)]
        public double AtrStopMult { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "SuperTrend ATR Period", GroupName = "2. Signals", Order = 6)]
        public int SuperTrendAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "SuperTrend Factor", GroupName = "2. Signals", Order = 7)]
        public double SuperTrendFactor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Context HUD", GroupName = "3. Context", Order = 0)]
        public bool ShowContextHud { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "HUD Location", GroupName = "3. Context", Order = 1)]
        public TextPosition HudLocation { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Basis
        {
            get { return Values[BasisPlot]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Upper
        {
            get { return Values[UpperPlot]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Lower
        {
            get { return Values[LowerPlot]; }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "NT8 port of the Pine Regime Direction indicator, focused on Bollinger basis/upper/lower with optional band shading, signals, and context HUD.";
                Name = "RegimeDirectionNT8";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                PaintPriceMarkers = true;
                IsSuspendedWhileInactive = true;

                BollingerPeriod = 20;
                BollingerStdDevMult = 2.0;
                ShowBandShading = true;
                ShadingLookbackBars = 500;
                ShadingOpacity = 12;

                ShowSignalMarkers = true;
                ShowSignalText = true;
                SignalTextOffsetTicks = 8;
                RequireSuperTrendFilter = true;
                AtrStopPeriod = 14;
                AtrStopMult = 2.0;
                SuperTrendAtrPeriod = 10;
                SuperTrendFactor = 1.0;

                ShowContextHud = true;
                HudLocation = TextPosition.BottomRight;

                AddPlot(Brushes.DarkOrange, "Basis");
                AddPlot(Brushes.DodgerBlue, "Upper");
                AddPlot(Brushes.DodgerBlue, "Lower");
            }
            else if (State == State.DataLoaded)
            {
                rsiSeries = new Series<double>(this);
                rsiMaSeries = new Series<double>(this);
                dxSeries = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
            {
                Basis[0] = Close[0];
                Upper[0] = Close[0];
                Lower[0] = Close[0];
                SeedFirstBar();
                return;
            }

            double basis = Sma(Close, BollingerPeriod);
            double stDev = PopulationStdDev(Close, BollingerPeriod, basis);
            Basis[0] = basis;
            Upper[0] = basis + BollingerStdDevMult * stDev;
            Lower[0] = basis - BollingerStdDevMult * stDev;

            if (ShowBandShading && CurrentBar >= BollingerPeriod)
            {
                int startBarsAgo = Math.Min(CurrentBar, ShadingLookbackBars);
                Draw.Region(this, "RegimeDirectionNT8_Band", startBarsAgo, 0, Upper, Lower,
                    Brushes.Transparent, Brushes.DodgerBlue, ShadingOpacity);
            }

            double tr = TrueRange();
            double atrStop = Rma(prevAtrStop, tr, AtrStopPeriod);
            double buyStop = High[0] - atrStop * AtrStopMult;
            double sellStop = Low[0] + atrStop * AtrStopMult;

            double ema9 = Ema(prevEma9, Close[0], 9);
            double ema14 = Ema(prevEma14, Close[0], 14);
            double ema21 = Ema(prevEma21, Close[0], 21);

            bool uptrend = ema9 < Low[0] && ema14 < Low[0];
            bool downtrend = ema14 > High[0] && ema21 > High[0];
            bool sideway = (ema9 < High[0] && ema9 > Low[0]) || (ema21 > Low[0] && ema21 < High[0]);

            double ema100a = Ema(prevEma100a, Close[0], 100);
            double ema100b = Ema(prevEma100b, ema100a, 100);
            double dema100 = 2.0 * ema100a - ema100b;
            double ema200a = Ema(prevEma200a, Close[0], 200);
            double ema200b = Ema(prevEma200b, ema200a, 200);
            double dema200 = 2.0 * ema200a - ema200b;
            bool bullish = dema100 > dema200;

            bool superTrendUp = CalculateSuperTrend(tr);

            double adx = CalculateCustomAdx(tr);
            string volatility = adx > 0 && adx <= 20 ? "Low"
                : adx > 20 && adx <= 30 ? "Medium"
                : adx > 30 && adx <= 40 ? "High"
                : adx > 40 && adx <= 75 ? "Very High"
                : adx > 75 && adx <= 100 ? "Extreme"
                : "-";

            double change = Close[0] - Close[1];
            double avgGain = Rma(prevAvgGain, Math.Max(change, 0.0), 14);
            double avgLoss = Rma(prevAvgLoss, Math.Max(-change, 0.0), 14);
            double rsi = avgLoss == 0.0 ? 100.0 : avgGain == 0.0 ? 0.0 : 100.0 - 100.0 / (1.0 + avgGain / avgLoss);
            rsiSeries[0] = rsi;
            rsiMaSeries[0] = Sma(rsiSeries, 14);

            bool overbought = rsi > 75.0;
            bool oversold = rsi < 25.0;

            double ema12 = Ema(prevEma12, Close[0], 12);
            double ema26 = Ema(prevEma26, Close[0], 26);
            double macd = ema12 - ema26;
            double macdSignal = Ema(prevMacdSignal, macd, 9);
            double hist = macd - macdSignal;
            double histPrev = (prevEma12 - prevEma26) - prevMacdSignal;

            bool rsiCrossUp = rsiSeries[1] <= rsiMaSeries[1] && rsiSeries[0] > rsiMaSeries[0];
            bool rsiCrossDown = rsiMaSeries[1] <= rsiSeries[1] && rsiMaSeries[0] > rsiSeries[0];
            bool longCondition = rsiCrossUp && hist > histPrev && adx > 20.0 && (!RequireSuperTrendFilter || superTrendUp);
            bool shortCondition = rsiCrossDown && histPrev > hist && adx > 20.0 && (!RequireSuperTrendFilter || !superTrendUp);

            if (ShowSignalMarkers && CurrentBar > Math.Max(220, BollingerPeriod + 2))
            {
                if (longCondition)
                {
                    string tag = "RegimeDirectionNT8_Long_" + CurrentBar;
                    Draw.ArrowUp(this, tag, false, 0, Low[0] - 2 * TickSize, Brushes.LimeGreen);
                    if (ShowSignalText)
                    {
                        double textY = Lower[0] - Math.Max(1, SignalTextOffsetTicks) * TickSize;
                        Draw.Text(this, tag + "_Text", "B\nSL: " + buyStop.ToString("0.0"), 0, textY, Brushes.LimeGreen);
                    }
                }
                else if (shortCondition)
                {
                    string tag = "RegimeDirectionNT8_Short_" + CurrentBar;
                    Draw.ArrowDown(this, tag, false, 0, High[0] + 2 * TickSize, Brushes.Red);
                    if (ShowSignalText)
                    {
                        double textY = Upper[0] + Math.Max(1, SignalTextOffsetTicks) * TickSize;
                        Draw.Text(this, tag + "_Text", "S\nSL: " + sellStop.ToString("0.0"), 0, textY, Brushes.Red);
                    }
                }
            }

            if (ShowContextHud)
            {
                string trendText = uptrend ? "Up-Trend" : downtrend ? "Down-Trend" : sideway ? "Sideway" : "-";
                string strengthText = overbought ? "Overbought" : oversold ? "Oversold" : "Normal";
                string hud = "REGIME DIRECTION"
                    + "\nVolatility: " + volatility
                    + "\nTrend: " + trendText
                    + "\nDirection: " + (bullish ? "Bullish" : "Bearish")
                    + "\nStrength: " + strengthText;

                Draw.TextFixed(this, "RegimeDirectionNT8_HUD", hud, HudLocation, Brushes.White,
                    new SimpleFont("Consolas", 12), Brushes.Transparent, Brushes.Black, 70);
            }
            else
            {
                RemoveDrawObject("RegimeDirectionNT8_HUD");
            }

            prevAtrStop = atrStop;
            prevAvgGain = avgGain;
            prevAvgLoss = avgLoss;
            prevMacdSignal = macdSignal;
            prevEma9 = ema9;
            prevEma12 = ema12;
            prevEma14 = ema14;
            prevEma21 = ema21;
            prevEma26 = ema26;
            prevEma100a = ema100a;
            prevEma100b = ema100b;
            prevEma200a = ema200a;
            prevEma200b = ema200b;
        }

        private void SeedFirstBar()
        {
            prevAtrStop = High[0] - Low[0];
            prevAtrSuper = prevAtrStop;
            prevAvgGain = 0.0;
            prevAvgLoss = 0.0;
            prevMacdSignal = 0.0;
            prevEma9 = Close[0];
            prevEma12 = Close[0];
            prevEma14 = Close[0];
            prevEma21 = Close[0];
            prevEma26 = Close[0];
            prevEma100a = Close[0];
            prevEma100b = Close[0];
            prevEma200a = Close[0];
            prevEma200b = Close[0];
            prevTrSmooth = prevAtrStop;
            prevDmPlusSmooth = 0.0;
            prevDmMinusSmooth = 0.0;
            prevFinalUpper = Close[0];
            prevFinalLower = Close[0];
            prevSuperTrendUp = true;
        }

        private double Sma(ISeries<double> series, int period)
        {
            int bars = Math.Min(period, CurrentBar + 1);
            double sum = 0.0;
            for (int i = 0; i < bars; i++)
                sum += series[i];
            return sum / bars;
        }

        private double PopulationStdDev(ISeries<double> series, int period, double mean)
        {
            int bars = Math.Min(period, CurrentBar + 1);
            double sumSq = 0.0;
            for (int i = 0; i < bars; i++)
            {
                double d = series[i] - mean;
                sumSq += d * d;
            }
            return Math.Sqrt(sumSq / bars);
        }

        private double Ema(double previous, double value, int period)
        {
            double alpha = 2.0 / (period + 1.0);
            return previous + alpha * (value - previous);
        }

        private double Rma(double previous, double value, int period)
        {
            return previous + (value - previous) / period;
        }

        private double TrueRange()
        {
            return Math.Max(High[0] - Low[0],
                Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));
        }

        private bool CalculateSuperTrend(double tr)
        {
            double atr = Rma(prevAtrSuper, tr, SuperTrendAtrPeriod);
            double median = (High[0] + Low[0]) / 2.0;
            double basicUpper = median + SuperTrendFactor * atr;
            double basicLower = median - SuperTrendFactor * atr;

            double finalUpper = (basicUpper < prevFinalUpper || Close[1] > prevFinalUpper) ? basicUpper : prevFinalUpper;
            double finalLower = (basicLower > prevFinalLower || Close[1] < prevFinalLower) ? basicLower : prevFinalLower;

            bool trendUp = prevSuperTrendUp;
            if (Close[0] > prevFinalUpper)
                trendUp = true;
            else if (Close[0] < prevFinalLower)
                trendUp = false;

            prevAtrSuper = atr;
            prevFinalUpper = finalUpper;
            prevFinalLower = finalLower;
            prevSuperTrendUp = trendUp;
            return trendUp;
        }

        private double CalculateCustomAdx(double tr)
        {
            double upMove = High[0] - High[1];
            double downMove = Low[1] - Low[0];
            double dmPlus = upMove > downMove ? Math.Max(upMove, 0.0) : 0.0;
            double dmMinus = downMove > upMove ? Math.Max(downMove, 0.0) : 0.0;

            double trSmooth = prevTrSmooth - prevTrSmooth / 14.0 + tr;
            double dmPlusSmooth = prevDmPlusSmooth - prevDmPlusSmooth / 14.0 + dmPlus;
            double dmMinusSmooth = prevDmMinusSmooth - prevDmMinusSmooth / 14.0 + dmMinus;

            double diPlus = trSmooth == 0.0 ? 0.0 : dmPlusSmooth / trSmooth * 100.0;
            double diMinus = trSmooth == 0.0 ? 0.0 : dmMinusSmooth / trSmooth * 100.0;
            double diSum = diPlus + diMinus;
            double dx = diSum == 0.0 ? 0.0 : Math.Abs(diPlus - diMinus) / diSum * 100.0;

            prevTrSmooth = trSmooth;
            prevDmPlusSmooth = dmPlusSmooth;
            prevDmMinusSmooth = dmMinusSmooth;

            dxSeries[0] = dx;
            return Sma(dxSeries, 14);
        }
    }
}
