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
        [Display(Name = "Gate timeframe (minutes)", GroupName = "Timeframes", Order = 1)]
        public int GateMinutes { get; set; } = 1;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Mid timeframe (minutes)", GroupName = "Timeframes", Order = 2)]
        public int MidMinutes { get; set; } = 15;

        [NinjaScriptProperty]
        [Display(Name = "Show Mid row", GroupName = "Timeframes", Order = 3)]
        public bool ShowMid { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Slow timeframe (minutes)", GroupName = "Timeframes", Order = 4)]
        public int SlowMinutes { get; set; } = 30;

        [NinjaScriptProperty]
        [Display(Name = "Show Slow row", GroupName = "Timeframes", Order = 5)]
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
        private ADX adxGate, adxMid, adxSlow;
        private DM dmGate, dmMid, dmSlow;
        private ChoppinessIndex chopPrimary; 

        private const string TextTag = "Trinity_HUD_Tag";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Trinity_HUD_v3";
                Description = "Heads-up display for Trinity/Gu5 Logic - v3 MTF Gate";
                IsOverlay = true;
                IsSuspendedWhileInactive = true;
                Calculate = Calculate.OnBarClose;
                BarsRequiredToPlot = 50;
            }
            else if (State == State.Configure)
            {
                // Primary is BarsArray[0] (e.g., UniRenko)
                AddDataSeries(BarsPeriodType.Minute, GateMinutes); // BarsArray[1] - The Gate
                AddDataSeries(BarsPeriodType.Minute, MidMinutes);  // BarsArray[2]
                AddDataSeries(BarsPeriodType.Minute, SlowMinutes); // BarsArray[3]
            }
            else if (State == State.DataLoaded)
            {
                // Choppiness runs strictly on the primary chart (UniRenko)
                chopPrimary = ChoppinessIndex(ChopPeriod);

                // ADX/DM runs on the Gate (1m) and higher timeframes
                if (BarsArray.Length > 1)
                {
                    adxGate = ADX(BarsArray[1], AdxPeriod);
                    dmGate = DM(BarsArray[1], AdxPeriod);
                }
                
                if (BarsArray.Length > 2)
                {
                    adxMid = ADX(BarsArray[2], AdxPeriod);
                    dmMid = DM(BarsArray[2], AdxPeriod);
                }
                
                if (BarsArray.Length > 3)
                {
                    adxSlow = ADX(BarsArray[3], AdxPeriod);
                    dmSlow = DM(BarsArray[3], AdxPeriod);
                }
            }
        }

        protected override void OnBarUpdate()
        {
            // Ensure we have enough bars on both primary and gate series
            if (CurrentBars[0] < BarsRequiredToPlot || CurrentBars[1] < BarsRequiredToPlot) return;
            
            // Only execute logic when the primary series (UniRenko) updates
            if (BarsInProgress != 0) return; 

            // --- 1. Get Data ---
            // Primary Chart Data (Trees)
            double currChop = chopPrimary[0];
            bool isChopSafe = currChop < ChopThreshold;

            // Gate Chart Data (Forest 1m)
            double currAdx = adxGate[0];
            double prevAdx = adxGate[1]; // Previous closed 1m bar
            
            double diPlus = dmGate.DiPlus[0];
            double diMinus = dmGate.DiMinus[0];
            double prevDiPlus = dmGate.DiPlus[1];
            double prevDiMinus = dmGate.DiMinus[1];

            // Slope Logic (From the 1m Gate)
            bool isAdxRising = currAdx > prevAdx;
            bool isAdxFalling = currAdx < prevAdx;
            bool isAdxSafe = currAdx > MinAdx;

            // Dominant DI Slope Logic (From the 1m Gate)
            bool dominantDiFalling = false;
            string weakReason = "";

            if (diPlus > diMinus) // Bullish Context
            {
                if (diPlus < prevDiPlus) 
                {
                    dominantDiFalling = true;
                    weakReason = "1m DI+ Falling";
                }
            }
            else // Bearish Context
            {
                if (diMinus < prevDiMinus) 
                {
                    dominantDiFalling = true;
                    weakReason = "1m DI- Falling";
                }
            }

            if (isAdxFalling) weakReason = "1m ADX Falling"; 

            // --- 2. Logic Detection ---

            // A. Crossovers (Ignition)
            bool bullCross = (diPlus > diMinus) && (prevDiPlus <= prevDiMinus);
            bool bearCross = (diMinus > diPlus) && (prevDiMinus <= prevDiPlus);

            // B. Strong Trend (Full Throttle)
            bool bullStrong = isAdxSafe && (diPlus > diMinus) && isAdxRising && !dominantDiFalling && (diPlus > TrendStrength);
            bool bearStrong = isAdxSafe && (diMinus > diPlus) && isAdxRising && !dominantDiFalling && (diMinus > TrendStrength);

            // C. Weak/Warning (The Waiting Room)
            bool isWeak = isAdxFalling || dominantDiFalling;

            // --- 3. Determine HUD State ---
            string signalText = "WAIT";
            string subText = ""; 
            Brush signalColor = WaitColor;

            if (!isChopSafe)
            {
                signalText = "CHOP ZONE";
                subText = $"Renko Chop: {currChop:0.0}";
                signalColor = WaitColor; 
            }
            else if (bullCross)
            {
                signalText = "BUY ALERT";
                subText = "1m DI Cross Up";
                signalColor = AlertColor; 
            }
            else if (bearCross)
            {
                signalText = "SELL ALERT";
                subText = "1m DI Cross Dn";
                signalColor = AlertColor; 
            }
            else if (bullStrong)
            {
                signalText = "BUY STRONG";
                subText = $"1m +DI: {diPlus:0.0} > 35";
                signalColor = LongColor;
            }
            else if (bearStrong)
            {
                signalText = "SELL STRONG";
                subText = $"1m -DI: {diMinus:0.0} > 35";
                signalColor = ShortColor;
            }
            else if (diPlus > diMinus)
            {
                if (isWeak)
                {
                     signalText = "WARNING";
                     subText = weakReason;
                     signalColor = WaitColor; 
                }
                else
                {
                    signalText = "LONG (HOLD)";
                    signalColor = LongColor; 
                }
            }
            else if (diMinus > diPlus)
            {
                if (isWeak)
                {
                     signalText = "WARNING";
                     subText = weakReason;
                     signalColor = WaitColor; 
                }
                else
                {
                    signalText = "SHORT (HOLD)";
                    signalColor = ShortColor;
                }
            }

            // --- 4. Draw HUD ---
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("TRINITY HUD v3 (MTF)");
            sb.AppendLine("-----------------");
            sb.AppendLine($"SIGNAL: {signalText}");
            if(!string.IsNullOrEmpty(subText)) sb.AppendLine($"INFO:   {subText}");
            sb.AppendLine("-----------------");
            sb.AppendLine($"{GateMinutes}m Gate ADX: {currAdx:0.0} ({(isAdxRising ? "Rise" : "Fall")})");
            sb.AppendLine($"Renko Chop:  {currChop:0.0}");
            
            if (ShowMid && adxMid != null) {
                 string mTr = dmMid.DiPlus[0] > dmMid.DiMinus[0] ? "Bull" : "Bear";
                 sb.AppendLine($"{MidMinutes}m ADX:    {adxMid[0]:0.0} {mTr}");
            }
            if (ShowSlow && adxSlow != null) {
                 string sTr = dmSlow.DiPlus[0] > dmSlow.DiMinus[0] ? "Bull" : "Bear";
                 sb.AppendLine($"{SlowMinutes}m ADX:    {adxSlow[0]:0.0} {sTr}");
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
		public Trinity_HUD_Overlay Trinity_HUD_Overlay(bool showFast, int gateMinutes, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int adxPeriod, double minAdx, double trendStrength, int chopPeriod, double chopThreshold, TextPosition tablePosition, Brush textColor, Brush longColor, Brush shortColor, Brush alertColor, Brush waitColor)
		{
			return Trinity_HUD_Overlay(Input, showFast, gateMinutes, midMinutes, showMid, slowMinutes, showSlow, adxPeriod, minAdx, trendStrength, chopPeriod, chopThreshold, tablePosition, textColor, longColor, shortColor, alertColor, waitColor);
		}

		public Trinity_HUD_Overlay Trinity_HUD_Overlay(ISeries<double> input, bool showFast, int gateMinutes, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int adxPeriod, double minAdx, double trendStrength, int chopPeriod, double chopThreshold, TextPosition tablePosition, Brush textColor, Brush longColor, Brush shortColor, Brush alertColor, Brush waitColor)
		{
			if (cacheTrinity_HUD_Overlay != null)
				for (int idx = 0; idx < cacheTrinity_HUD_Overlay.Length; idx++)
					if (cacheTrinity_HUD_Overlay[idx] != null && cacheTrinity_HUD_Overlay[idx].ShowFast == showFast && cacheTrinity_HUD_Overlay[idx].GateMinutes == gateMinutes && cacheTrinity_HUD_Overlay[idx].MidMinutes == midMinutes && cacheTrinity_HUD_Overlay[idx].ShowMid == showMid && cacheTrinity_HUD_Overlay[idx].SlowMinutes == slowMinutes && cacheTrinity_HUD_Overlay[idx].ShowSlow == showSlow && cacheTrinity_HUD_Overlay[idx].AdxPeriod == adxPeriod && cacheTrinity_HUD_Overlay[idx].MinAdx == minAdx && cacheTrinity_HUD_Overlay[idx].TrendStrength == trendStrength && cacheTrinity_HUD_Overlay[idx].ChopPeriod == chopPeriod && cacheTrinity_HUD_Overlay[idx].ChopThreshold == chopThreshold && cacheTrinity_HUD_Overlay[idx].TablePosition == tablePosition && cacheTrinity_HUD_Overlay[idx].TextColor == textColor && cacheTrinity_HUD_Overlay[idx].LongColor == longColor && cacheTrinity_HUD_Overlay[idx].ShortColor == shortColor && cacheTrinity_HUD_Overlay[idx].AlertColor == alertColor && cacheTrinity_HUD_Overlay[idx].WaitColor == waitColor && cacheTrinity_HUD_Overlay[idx].EqualsInput(input))
						return cacheTrinity_HUD_Overlay[idx];
			return CacheIndicator<Trinity_HUD_Overlay>(new Trinity_HUD_Overlay(){ ShowFast = showFast, GateMinutes = gateMinutes, MidMinutes = midMinutes, ShowMid = showMid, SlowMinutes = slowMinutes, ShowSlow = showSlow, AdxPeriod = adxPeriod, MinAdx = minAdx, TrendStrength = trendStrength, ChopPeriod = chopPeriod, ChopThreshold = chopThreshold, TablePosition = tablePosition, TextColor = textColor, LongColor = longColor, ShortColor = shortColor, AlertColor = alertColor, WaitColor = waitColor }, input, ref cacheTrinity_HUD_Overlay);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Trinity_HUD_Overlay Trinity_HUD_Overlay(bool showFast, int gateMinutes, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int adxPeriod, double minAdx, double trendStrength, int chopPeriod, double chopThreshold, TextPosition tablePosition, Brush textColor, Brush longColor, Brush shortColor, Brush alertColor, Brush waitColor)
		{
			return indicator.Trinity_HUD_Overlay(Input, showFast, gateMinutes, midMinutes, showMid, slowMinutes, showSlow, adxPeriod, minAdx, trendStrength, chopPeriod, chopThreshold, tablePosition, textColor, longColor, shortColor, alertColor, waitColor);
		}

		public Indicators.Trinity_HUD_Overlay Trinity_HUD_Overlay(ISeries<double> input , bool showFast, int gateMinutes, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int adxPeriod, double minAdx, double trendStrength, int chopPeriod, double chopThreshold, TextPosition tablePosition, Brush textColor, Brush longColor, Brush shortColor, Brush alertColor, Brush waitColor)
		{
			return indicator.Trinity_HUD_Overlay(input, showFast, gateMinutes, midMinutes, showMid, slowMinutes, showSlow, adxPeriod, minAdx, trendStrength, chopPeriod, chopThreshold, tablePosition, textColor, longColor, shortColor, alertColor, waitColor);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Trinity_HUD_Overlay Trinity_HUD_Overlay(bool showFast, int gateMinutes, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int adxPeriod, double minAdx, double trendStrength, int chopPeriod, double chopThreshold, TextPosition tablePosition, Brush textColor, Brush longColor, Brush shortColor, Brush alertColor, Brush waitColor)
		{
			return indicator.Trinity_HUD_Overlay(Input, showFast, gateMinutes, midMinutes, showMid, slowMinutes, showSlow, adxPeriod, minAdx, trendStrength, chopPeriod, chopThreshold, tablePosition, textColor, longColor, shortColor, alertColor, waitColor);
		}

		public Indicators.Trinity_HUD_Overlay Trinity_HUD_Overlay(ISeries<double> input , bool showFast, int gateMinutes, int midMinutes, bool showMid, int slowMinutes, bool showSlow, int adxPeriod, double minAdx, double trendStrength, int chopPeriod, double chopThreshold, TextPosition tablePosition, Brush textColor, Brush longColor, Brush shortColor, Brush alertColor, Brush waitColor)
		{
			return indicator.Trinity_HUD_Overlay(input, showFast, gateMinutes, midMinutes, showMid, slowMinutes, showSlow, adxPeriod, minAdx, trendStrength, chopPeriod, chopThreshold, tablePosition, textColor, longColor, shortColor, alertColor, waitColor);
		}
	}
}

#endregion
