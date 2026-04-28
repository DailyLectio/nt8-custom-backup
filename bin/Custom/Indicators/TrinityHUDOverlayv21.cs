// CC BY-NC 4.0
#region Using
using System;
using System.Text;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;

using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class Trinity_HUD_Overlay : Indicator
    {
        // ===== Inputs: Timeframes =====
        [NinjaScriptProperty]
        [Display(Name = "Show chart TF row", GroupName = "Timeframes", Order = 0)]
        public bool ShowFast { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Mid timeframe (minutes)", GroupName = "Timeframes", Order = 1)]
        public int MidMinutes { get; set; } = 15;

        [NinjaScriptProperty]
        [Display(Name = "Show Mid row", GroupName = "Timeframes", Order = 2)]
        public bool ShowMid { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Slow timeframe (minutes)", GroupName = "Timeframes", Order = 3)]
        public int SlowMinutes { get; set; } = 30;

        [NinjaScriptProperty]
        [Display(Name = "Show Slow row", GroupName = "Timeframes", Order = 4)]
        public bool ShowSlow { get; set; } = true;

        // ===== Inputs: Trinity / Gu5 Logic =====
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ADX Period", GroupName = "Parameters", Order = 0)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Min ADX (Chop Filter)", GroupName = "Parameters", Order = 1)]
        public double MinAdx { get; set; } = 20.0;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Strong Trend Threshold (DI)", GroupName = "Parameters", Order = 2)]
        public double TrendStrength { get; set; } = 35.0; 

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Chop Index Period", GroupName = "Parameters", Order = 3)]
        public int ChopPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Max Chop (Filter)", GroupName = "Parameters", Order = 4)]
        public double ChopThreshold { get; set; } = 60.0;

        // ===== Inputs: Visuals =====
        [NinjaScriptProperty]
        [Display(Name = "Table Position", GroupName = "Visual", Order = 0)]
        public TextPosition TablePosition { get; set; } = TextPosition.TopRight;

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Text Color", GroupName = "Visual", Order = 1)]
        public Brush TextColor { get; set; } = Brushes.WhiteSmoke;

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Go Long Color", GroupName = "Visual", Order = 2)]
        public Brush LongColor { get; set; } = Brushes.LimeGreen;

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Go Short Color", GroupName = "Visual", Order = 3)]
        public Brush ShortColor { get; set; } = Brushes.Red;

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Alert/Cross Color", GroupName = "Visual", Order = 4)]
        public Brush AlertColor { get; set; } = Brushes.Cyan;

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Wait/Chop Color", GroupName = "Visual", Order = 5)]
        public Brush WaitColor { get; set; } = Brushes.Orange;

        // ===== Internal Indicators =====
        private ADX adxFast, adxMid, adxSlow;
        private DM dmFast, dmMid, dmSlow;
        private ChoppinessIndex chopFast; 

        private const string TextTag = "Trinity_HUD_Tag";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Trinity_HUD_Overlay";
                Description = "Heads-up display for Trinity/Gu5 Logic - v2.1 Slope Update";
                IsOverlay = true;
                IsSuspendedWhileInactive = true;
                Calculate = Calculate.OnBarClose;
                BarsRequiredToPlot = 50;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, MidMinutes);
                AddDataSeries(BarsPeriodType.Minute, SlowMinutes);
            }
            else if (State == State.DataLoaded)
            {
                adxFast = ADX(AdxPeriod);
                dmFast = DM(AdxPeriod);
                chopFast = ChoppinessIndex(ChopPeriod);

                if (BarsArray.Length > 1)
                {
                    adxMid = ADX(BarsArray[1], AdxPeriod);
                    dmMid = DM(BarsArray[1], AdxPeriod);
                }
                
                if (BarsArray.Length > 2)
                {
                    adxSlow = ADX(BarsArray[2], AdxPeriod);
                    dmSlow = DM(BarsArray[2], AdxPeriod);
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || CurrentBar < BarsRequiredToPlot) return;

            // --- 1. Get Data ---
            double currAdx = adxFast[0];
            double prevAdx = adxFast[1];
            double currChop = chopFast[0];
            
            double diPlus = dmFast.DiPlus[0];
            double diMinus = dmFast.DiMinus[0];
            double prevDiPlus = dmFast.DiPlus[1];
            double prevDiMinus = dmFast.DiMinus[1];

            // Slope Logic
            bool isAdxRising = currAdx > prevAdx;
            bool isAdxFalling = currAdx < prevAdx;
            bool isChopSafe = currChop < ChopThreshold;
            bool isAdxSafe = currAdx > MinAdx;

            // Dominant DI Slope Logic
            bool dominantDiFalling = false;
            string weakReason = "";

            if (diPlus > diMinus) // Bullish Context
            {
                if (diPlus < prevDiPlus) 
                {
                    dominantDiFalling = true;
                    weakReason = "DI+ Falling";
                }
            }
            else // Bearish Context
            {
                if (diMinus < prevDiMinus) 
                {
                    dominantDiFalling = true;
                    weakReason = "DI- Falling";
                }
            }

            if (isAdxFalling) weakReason = "ADX Falling"; // ADX falling overrides DI falling for text display

            // --- 2. Logic Detection ---

            // A. Crossovers (Ignition)
            bool bullCross = (diPlus > diMinus) && (prevDiPlus <= prevDiMinus);
            bool bearCross = (diMinus > diPlus) && (prevDiMinus <= prevDiPlus);

            // B. Strong Trend (Full Throttle)
            // Strict Definition: Safe ADX, Rising ADX, Rising Dominant DI, High DI Value
            bool bullStrong = isAdxSafe && (diPlus > diMinus) && isAdxRising && !dominantDiFalling && (diPlus > TrendStrength);
            bool bearStrong = isAdxSafe && (diMinus > diPlus) && isAdxRising && !dominantDiFalling && (diMinus > TrendStrength);

            // C. Weak/Warning
            bool isWeak = isAdxFalling || dominantDiFalling;

            // --- 3. Determine HUD State ---
            string signalText = "WAIT";
            string subText = ""; 
            Brush signalColor = WaitColor;

            if (!isChopSafe)
            {
                signalText = "CHOP ZONE";
                subText = $"Idx: {currChop:0.0}";
                signalColor = WaitColor; // Or Blue if you prefer
            }
            else if (bullCross)
            {
                signalText = "BUY ALERT";
                subText = "DI Cross Up";
                signalColor = AlertColor; 
            }
            else if (bearCross)
            {
                signalText = "SELL ALERT";
                subText = "DI Cross Dn";
                signalColor = AlertColor; 
            }
            else if (bullStrong)
            {
                signalText = "BUY STRONG";
                subText = $"+DI: {diPlus:0.0} > 35";
                signalColor = LongColor;
            }
            else if (bearStrong)
            {
                signalText = "SELL STRONG";
                subText = $"-DI: {diMinus:0.0} > 35";
                signalColor = ShortColor;
            }
            else if (diPlus > diMinus)
            {
                // Bullish Holding State
                if (isWeak)
                {
                     signalText = "WARNING";
                     subText = weakReason;
                     signalColor = WaitColor; // Yellow Warning
                }
                else
                {
                    signalText = "LONG (HOLD)";
                    signalColor = LongColor; 
                }
            }
            else if (diMinus > diPlus)
            {
                // Bearish Holding State
                if (isWeak)
                {
                     signalText = "WARNING";
                     subText = weakReason;
                     signalColor = WaitColor; // Yellow Warning
                }
                else
                {
                    signalText = "SHORT (HOLD)";
                    signalColor = ShortColor;
                }
            }

            // --- 4. Draw HUD ---
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("TRINITY HUD v2.1");
            sb.AppendLine("-----------------");
            sb.AppendLine($"SIGNAL: {signalText}");
            if(!string.IsNullOrEmpty(subText)) sb.AppendLine($"INFO:   {subText}");
            sb.AppendLine("-----------------");
            sb.AppendLine($"ADX:    {currAdx:0.0} ({(isAdxRising ? "Rise" : "Fall")})");
            
            if (ShowMid && adxMid != null) {
                 string mTr = dmMid.DiPlus[0] > dmMid.DiMinus[0] ? "Bull" : "Bear";
                 sb.AppendLine($"{MidMinutes}m:     {adxMid[0]:0.0} {mTr}");
            }
            if (ShowSlow && adxSlow != null) {
                 string sTr = dmSlow.DiPlus[0] > dmSlow.DiMinus[0] ? "Bull" : "Bear";
                 sb.AppendLine($"{SlowMinutes}m:     {adxSlow[0]:0.0} {sTr}");
            }

            Draw.TextFixed(this, TextTag, sb.ToString(), TablePosition, 
                signalColor, 
                new SimpleFont("Consolas", 12), 
                Brushes.Black, Brushes.DimGray, 85);
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Trinity_HUD_Overlay[] cacheTrinity_HUD_Overlay;
		public Trinity_HUD_Overlay Trinity_HUD_Overlay(bool showFast, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int adxPeriod, double minAdx, double trendStrength, int chopPeriod, double chopThreshold, TextPosition tablePosition, Brush textColor, Brush longColor, Brush shortColor, Brush alertColor, Brush waitColor)
		{
			return Trinity_HUD_Overlay(Input, showFast, midMinutes, showMid, slowMinutes, showSlow, adxPeriod, minAdx, trendStrength, chopPeriod, chopThreshold, tablePosition, textColor, longColor, shortColor, alertColor, waitColor);
		}

		public Trinity_HUD_Overlay Trinity_HUD_Overlay(ISeries<double> input, bool showFast, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int adxPeriod, double minAdx, double trendStrength, int chopPeriod, double chopThreshold, TextPosition tablePosition, Brush textColor, Brush longColor, Brush shortColor, Brush alertColor, Brush waitColor)
		{
			if (cacheTrinity_HUD_Overlay != null)
				for (int idx = 0; idx < cacheTrinity_HUD_Overlay.Length; idx++)
					if (cacheTrinity_HUD_Overlay[idx] != null && cacheTrinity_HUD_Overlay[idx].ShowFast == showFast && cacheTrinity_HUD_Overlay[idx].MidMinutes == midMinutes && cacheTrinity_HUD_Overlay[idx].ShowMid == showMid && cacheTrinity_HUD_Overlay[idx].SlowMinutes == slowMinutes && cacheTrinity_HUD_Overlay[idx].ShowSlow == showSlow && cacheTrinity_HUD_Overlay[idx].AdxPeriod == adxPeriod && cacheTrinity_HUD_Overlay[idx].MinAdx == minAdx && cacheTrinity_HUD_Overlay[idx].TrendStrength == trendStrength && cacheTrinity_HUD_Overlay[idx].ChopPeriod == chopPeriod && cacheTrinity_HUD_Overlay[idx].ChopThreshold == chopThreshold && cacheTrinity_HUD_Overlay[idx].TablePosition == tablePosition && cacheTrinity_HUD_Overlay[idx].TextColor == textColor && cacheTrinity_HUD_Overlay[idx].LongColor == longColor && cacheTrinity_HUD_Overlay[idx].ShortColor == shortColor && cacheTrinity_HUD_Overlay[idx].AlertColor == alertColor && cacheTrinity_HUD_Overlay[idx].WaitColor == waitColor && cacheTrinity_HUD_Overlay[idx].EqualsInput(input))
						return cacheTrinity_HUD_Overlay[idx];
			return CacheIndicator<Trinity_HUD_Overlay>(new Trinity_HUD_Overlay(){ ShowFast = showFast, MidMinutes = midMinutes, ShowMid = showMid, SlowMinutes = slowMinutes, ShowSlow = showSlow, AdxPeriod = adxPeriod, MinAdx = minAdx, TrendStrength = trendStrength, ChopPeriod = chopPeriod, ChopThreshold = chopThreshold, TablePosition = tablePosition, TextColor = textColor, LongColor = longColor, ShortColor = shortColor, AlertColor = alertColor, WaitColor = waitColor }, input, ref cacheTrinity_HUD_Overlay);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Trinity_HUD_Overlay Trinity_HUD_Overlay(bool showFast, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int adxPeriod, double minAdx, double trendStrength, int chopPeriod, double chopThreshold, TextPosition tablePosition, Brush textColor, Brush longColor, Brush shortColor, Brush alertColor, Brush waitColor)
		{
			return indicator.Trinity_HUD_Overlay(Input, showFast, midMinutes, showMid, slowMinutes, showSlow, adxPeriod, minAdx, trendStrength, chopPeriod, chopThreshold, tablePosition, textColor, longColor, shortColor, alertColor, waitColor);
		}

		public Indicators.Trinity_HUD_Overlay Trinity_HUD_Overlay(ISeries<double> input , bool showFast, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int adxPeriod, double minAdx, double trendStrength, int chopPeriod, double chopThreshold, TextPosition tablePosition, Brush textColor, Brush longColor, Brush shortColor, Brush alertColor, Brush waitColor)
		{
			return indicator.Trinity_HUD_Overlay(input, showFast, midMinutes, showMid, slowMinutes, showSlow, adxPeriod, minAdx, trendStrength, chopPeriod, chopThreshold, tablePosition, textColor, longColor, shortColor, alertColor, waitColor);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Trinity_HUD_Overlay Trinity_HUD_Overlay(bool showFast, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int adxPeriod, double minAdx, double trendStrength, int chopPeriod, double chopThreshold, TextPosition tablePosition, Brush textColor, Brush longColor, Brush shortColor, Brush alertColor, Brush waitColor)
		{
			return indicator.Trinity_HUD_Overlay(Input, showFast, midMinutes, showMid, slowMinutes, showSlow, adxPeriod, minAdx, trendStrength, chopPeriod, chopThreshold, tablePosition, textColor, longColor, shortColor, alertColor, waitColor);
		}

		public Indicators.Trinity_HUD_Overlay Trinity_HUD_Overlay(ISeries<double> input , bool showFast, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int adxPeriod, double minAdx, double trendStrength, int chopPeriod, double chopThreshold, TextPosition tablePosition, Brush textColor, Brush longColor, Brush shortColor, Brush alertColor, Brush waitColor)
		{
			return indicator.Trinity_HUD_Overlay(input, showFast, midMinutes, showMid, slowMinutes, showSlow, adxPeriod, minAdx, trendStrength, chopPeriod, chopThreshold, tablePosition, textColor, longColor, shortColor, alertColor, waitColor);
		}
	}
}

#endregion
