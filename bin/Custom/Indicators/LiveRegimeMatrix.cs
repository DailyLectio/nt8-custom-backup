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
    public class Live_Regime_Matrix : Indicator
    {
        // ===== Parameters =====
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Threshold", GroupName = "Parameters", Order = 0)]
        public double AdxThresh { get; set; } = 20.0;

        [NinjaScriptProperty]
        [Display(Name = "Matrix Position", GroupName = "Visual", Order = 0)]
        public TextPosition MatrixPosition { get; set; } = TextPosition.TopLeft;

        // ===== Internals =====
        private ADX adx5, adx15, adx30;
        private ChoppinessIndex chop5;
        
        private string currentRegime = "CALCULATING...";
        private string previousRegime = "";
        private string shiftWarning = "Awaiting first shift...";
        private string timeMilestone = "Morning Session";
        private DateTime lastShiftTime = Core.Globals.MinDate;

        private const string TagMatrix = "Live_Regime_Matrix_Tag";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Live_Regime_Matrix";
                Description = "Standalone 5m/15m/30m ADX & Chop Regime Monitor";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                BarsRequiredToPlot = 50;
            }
            else if (State == State.Configure)
            {
                // Primary is 5m (BarsArray[0])
                AddDataSeries(BarsPeriodType.Minute, 15); // BarsArray[1]
                AddDataSeries(BarsPeriodType.Minute, 30); // BarsArray[2]
            }
            else if (State == State.DataLoaded)
            {
                adx5 = ADX(14);
                chop5 = ChoppinessIndex(14);

                if (BarsArray.Length > 1) adx15 = ADX(BarsArray[1], 14);
                if (BarsArray.Length > 2) adx30 = ADX(BarsArray[2], 14);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < BarsRequiredToPlot || CurrentBars[1] < BarsRequiredToPlot || CurrentBars[2] < BarsRequiredToPlot) return;
            if (BarsInProgress != 0) return; // Only execute on the 5m chart close

            // 1. Get Data
            double a5 = adx5[0];
            double a5Prev = adx5[1];
            double c5 = chop5[0];
            
            double a15 = adx15[0];
            double a15Prev = adx15[1];
            
            double a30 = adx30[0];
            double a30Prev = adx30[1];

            bool a5Rising = a5 > a5Prev;
            bool a15Rising = a15 > a15Prev;
            bool a30Rising = a30 > a30Prev;

			// 2. Determine Regime (Based on CORE Playbook)
            string newRegime = "";
            Brush bgBrush = Brushes.Black;
            Brush borderBrush = Brushes.DimGray;
            Brush textBrush = Brushes.WhiteSmoke;

            // Chaotic (F): STRICTLY based on extreme Chop (> 70) 
            if (c5 > 70)
            {
                newRegime = "CHAOTIC (F)";
                bgBrush = Brushes.DarkRed;
                borderBrush = Brushes.Red;
            }
            // Trend (A+): All ADX >= 20 and rising, Chop dropping/low
            else if (a30 >= AdxThresh && a30Rising && a15 >= AdxThresh && a15Rising && a5 >= AdxThresh && a5Rising && c5 < 50)
            {
                newRegime = "TREND (A+)";
                bgBrush = Brushes.DarkGreen;
                borderBrush = Brushes.LimeGreen;
            }
            // Responsive (B): ADX is low/mixed, but Chop is safely under 70. 
            // PERFECT for fading edges (like B3 to POC).
            else
            {
                newRegime = "RESPONSIVE (B)";
                bgBrush = Brushes.DarkGoldenrod;
                borderBrush = Brushes.Orange;
            }

            // 3. Track Shifts & Milestones
            DateTime currentTime = Times[0][0];

            if (previousRegime != "" && newRegime != previousRegime)
            {
                lastShiftTime = currentTime;
                shiftWarning = $"Shifted to {newRegime} at {lastShiftTime.ToString("HH:mm")}. WAIT 15-30 MINS.";
            }
            
            previousRegime = currentRegime;
            currentRegime = newRegime;

            // Time Milestones
            if (currentTime.TimeOfDay >= new TimeSpan(11, 30, 0))
            {
                timeMilestone = "11:30 MILESTONE: Pre-Lunch Drift. Protect Capital.";
            }
            else if (currentTime.TimeOfDay >= new TimeSpan(10, 30, 0))
            {
                timeMilestone = "10:30 MILESTONE: Euro Close / Morning Digestion.";
            }
            else if (currentTime.TimeOfDay >= new TimeSpan(9, 30, 0))
            {
                timeMilestone = "09:30 MILESTONE: NY Open / Establishing Range.";
            }

            // 4. Draw Matrix
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== MTF REGIME MATRIX ===");
            sb.AppendLine($"STATUS: {currentRegime}");
            sb.AppendLine("-------------------------");
            sb.AppendLine($"30m ADX: {a30:0.0} ({(a30Rising ? "UP" : "DN")})");
            sb.AppendLine($"15m ADX: {a15:0.0} ({(a15Rising ? "UP" : "DN")})");
            sb.AppendLine($" 5m ADX: {a5:0.0} ({(a5Rising ? "UP" : "DN")})");
            sb.AppendLine($" 5m CHP: {c5:0.0}");
            sb.AppendLine("-------------------------");
            sb.AppendLine($"EVENT: {timeMilestone}");
            if (lastShiftTime != Core.Globals.MinDate)
            {
                sb.AppendLine($"ALERT: {shiftWarning}");
            }

            Draw.TextFixed(this, TagMatrix, sb.ToString(), MatrixPosition, 
                textBrush, 
                new SimpleFont("Consolas", 13) { Bold = true }, 
                borderBrush, bgBrush, 90);
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Live_Regime_Matrix[] cacheLive_Regime_Matrix;
		public Live_Regime_Matrix Live_Regime_Matrix(double adxThresh, TextPosition matrixPosition)
		{
			return Live_Regime_Matrix(Input, adxThresh, matrixPosition);
		}

		public Live_Regime_Matrix Live_Regime_Matrix(ISeries<double> input, double adxThresh, TextPosition matrixPosition)
		{
			if (cacheLive_Regime_Matrix != null)
				for (int idx = 0; idx < cacheLive_Regime_Matrix.Length; idx++)
					if (cacheLive_Regime_Matrix[idx] != null && cacheLive_Regime_Matrix[idx].AdxThresh == adxThresh && cacheLive_Regime_Matrix[idx].MatrixPosition == matrixPosition && cacheLive_Regime_Matrix[idx].EqualsInput(input))
						return cacheLive_Regime_Matrix[idx];
			return CacheIndicator<Live_Regime_Matrix>(new Live_Regime_Matrix(){ AdxThresh = adxThresh, MatrixPosition = matrixPosition }, input, ref cacheLive_Regime_Matrix);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Live_Regime_Matrix Live_Regime_Matrix(double adxThresh, TextPosition matrixPosition)
		{
			return indicator.Live_Regime_Matrix(Input, adxThresh, matrixPosition);
		}

		public Indicators.Live_Regime_Matrix Live_Regime_Matrix(ISeries<double> input , double adxThresh, TextPosition matrixPosition)
		{
			return indicator.Live_Regime_Matrix(input, adxThresh, matrixPosition);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Live_Regime_Matrix Live_Regime_Matrix(double adxThresh, TextPosition matrixPosition)
		{
			return indicator.Live_Regime_Matrix(Input, adxThresh, matrixPosition);
		}

		public Indicators.Live_Regime_Matrix Live_Regime_Matrix(ISeries<double> input , double adxThresh, TextPosition matrixPosition)
		{
			return indicator.Live_Regime_Matrix(input, adxThresh, matrixPosition);
		}
	}
}

#endregion
