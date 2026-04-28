#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;

using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.BarsTypes;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class KeyLevelsADR_IBPOC_TVLadder : Indicator
    {
        // ========= Daily ADR (BIP=1) =========
        private double adrRma = double.NaN;
        private int adrSamples = 0;

        // ========= IB volumetric profile =========
        private readonly Dictionary<double, double> ibVolByPrice = new Dictionary<double, double>();
        private double ibPOC = double.NaN;
        private bool locked = false;

        private VolumetricBarsType volBars;
        private bool hasVolumetric = false;

        // Locked levels
        private double lvlPOC;

        private double h1, hT1, h2, hT2, h3, hX;
        private double m1, mT1, m2, mT2, m3, mX;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "KeyLevelsADR_IBPOC_TVLadder";
                Description              = "TV-style granular ladder around Volumetric IB POC (9:30-10:30) using Daily ADR (RMA/Wilder). Locks at 10:30.";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = true;
                DisplayInDataBox         = true;
                DrawOnPricePanel         = true;
                PaintPriceMarkers        = true;
                IsSuspendedWhileInactive = true;

                AdrLength = 10;

                IBStartHour = 9;   IBStartMinute = 30;
                IBEndHour   = 10;  IBEndMinute   = 30;

                // ---- TV-like ladder defaults (tweakable) ----
                // These are the “named rungs” you asked for:
                // High1, HighTarget, High2, High2Target, High3, Extreme
                MultHigh1       = 0.50;
                MultHighTarget1 = 0.75;
                MultHigh2       = 1.00;
                MultHighTarget2 = 1.25;
                MultHigh3       = 1.50;
                MultExtreme     = 2.00;

                // ----- Plots -----
                // 0: POC (Orange 4pt)
                AddPlot(Brushes.Orange, "POC");

                // Highs (Green 3pt)
                AddPlot(Brushes.LimeGreen, "High1");
                AddPlot(Brushes.LimeGreen, "HighTarget");
                AddPlot(Brushes.LimeGreen, "High2");
                AddPlot(Brushes.LimeGreen, "High2Target");
                AddPlot(Brushes.LimeGreen, "High3");
                AddPlot(Brushes.LimeGreen, "ExtremeHigh");

                // Lows (Red 3pt)
                AddPlot(Brushes.Red, "Minus1");
                AddPlot(Brushes.Red, "MinusTarget");
                AddPlot(Brushes.Red, "Minus2");
                AddPlot(Brushes.Red, "Minus2Target");
                AddPlot(Brushes.Red, "Minus3");
                AddPlot(Brushes.Red, "ExtremeLow");
            }
            else if (State == State.Configure)
            {
                // Daily series for ADR (Wilder/RMA)
                AddDataSeries(BarsPeriodType.Day, 1);
            }
            else if (State == State.DataLoaded)
            {
                volBars = BarsArray[0].BarsType as VolumetricBarsType;
                hasVolumetric = (volBars != null);

                // Force plot widths (no Stroke/DashStyleHelper needed)
                // POC 4pt
                Plots[0].Width = 4;

                // Highs 3pt
                for (int i = 1; i <= 6; i++)
                    Plots[i].Width = 3;

                // Lows 3pt
                for (int i = 7; i <= 12; i++)
                    Plots[i].Width = 3;

                ResetSession();
            }
        }

        private void ResetSession()
        {
            ibVolByPrice.Clear();
            ibPOC = double.NaN;
            locked = false;

            lvlPOC = double.NaN;

            h1 = hT1 = h2 = hT2 = h3 = hX = double.NaN;
            m1 = mT1 = m2 = mT2 = m3 = mX = double.NaN;
        }

        private TimeSpan IBStart => new TimeSpan(IBStartHour, IBStartMinute, 0);
        private TimeSpan IBEnd   => new TimeSpan(IBEndHour,   IBEndMinute,   0);

        private double RoundToTick(double price)
        {
            double ts = TickSize;
            if (ts <= 0) return price;
            return Math.Round(price / ts) * ts;
        }

        private double WilderRmaSeedOrUpdate(double priorRma, double x, int n)
        {
            adrSamples++;

            // Seed with running mean up to n samples
            if (adrSamples <= n)
            {
                if (double.IsNaN(priorRma)) return x;
                return priorRma + (x - priorRma) / adrSamples;
            }

            // Wilder/RMA update
            return (priorRma * (n - 1) + x) / n;
        }

        private void AddVolumetricBarToProfile(int barsAgo)
        {
            if (!hasVolumetric) return;

            int barIndex = CurrentBar - barsAgo;
            if (barIndex < 0) return;

            if (volBars.Volumes == null || barIndex >= volBars.Volumes.Length)
                return;

            double ts = TickSize;
            if (ts <= 0) return;

            double lo = RoundToTick(Low[barsAgo]);
            double hi = RoundToTick(High[barsAgo]);
            if (hi < lo) { double tmp = lo; lo = hi; hi = tmp; }

            for (double p = lo; p <= hi + (ts * 0.5); p += ts)
            {
                double price = RoundToTick(p);

                long bid = volBars.Volumes[barIndex].GetBidVolumeForPrice(price);
                long ask = volBars.Volumes[barIndex].GetAskVolumeForPrice(price);

                double total = bid + ask;
                if (total <= 0) continue;

                if (!ibVolByPrice.ContainsKey(price))
                    ibVolByPrice[price] = 0;

                ibVolByPrice[price] += total;
            }
        }

        private double ComputePOC()
        {
            double poc = double.NaN;
            double maxVol = double.MinValue;

            foreach (var kv in ibVolByPrice)
            {
                if (kv.Value > maxVol)
                {
                    maxVol = kv.Value;
                    poc = kv.Key;
                }
            }
            return poc;
        }

        private void LockLevels()
        {
            if (double.IsNaN(ibPOC) || double.IsNaN(adrRma))
                return;

            lvlPOC = ibPOC;

            h1  = RoundToTick(lvlPOC + MultHigh1       * adrRma);
            hT1 = RoundToTick(lvlPOC + MultHighTarget1 * adrRma);
            h2  = RoundToTick(lvlPOC + MultHigh2       * adrRma);
            hT2 = RoundToTick(lvlPOC + MultHighTarget2 * adrRma);
            h3  = RoundToTick(lvlPOC + MultHigh3       * adrRma);
            hX  = RoundToTick(lvlPOC + MultExtreme     * adrRma);

            m1  = RoundToTick(lvlPOC - MultHigh1       * adrRma);
            mT1 = RoundToTick(lvlPOC - MultHighTarget1 * adrRma);
            m2  = RoundToTick(lvlPOC - MultHigh2       * adrRma);
            mT2 = RoundToTick(lvlPOC - MultHighTarget2 * adrRma);
            m3  = RoundToTick(lvlPOC - MultHigh3       * adrRma);
            mX  = RoundToTick(lvlPOC - MultExtreme     * adrRma);

            locked = true;
        }

        protected override void OnBarUpdate()
        {
            // ===== Daily ADR series =====
            if (BarsInProgress == 1)
            {
                if (CurrentBars[1] < 2) return;

                double range = Highs[1][1] - Lows[1][1];  // prior completed day
                if (range >= 0)
                    adrRma = WilderRmaSeedOrUpdate(adrRma, range, Math.Max(1, AdrLength));

                return;
            }

            if (BarsInProgress != 0) return;
            if (CurrentBar < 1) return;

            if (Bars.IsFirstBarOfSession)
                ResetSession();

            // Require volumetric for accurate IB POC
            if (!hasVolumetric)
            {
                PublishPlots();
                return;
            }

            TimeSpan tod = Time[0].TimeOfDay;
            bool inIB   = tod >= IBStart && tod < IBEnd;
            bool pastIB = tod >= IBEnd;

            if (!locked && inIB)
                AddVolumetricBarToProfile(0);

            if (!locked && pastIB)
            {
                ibPOC = ComputePOC();
                LockLevels();
            }

            PublishPlots();
        }

        private void PublishPlots()
        {
            // Plot indices:
            // 0 POC
            // 1..6 highs
            // 7..12 lows

            Values[0][0] = lvlPOC;

            Values[1][0] = h1;
            Values[2][0] = hT1;
            Values[3][0] = h2;
            Values[4][0] = hT2;
            Values[5][0] = h3;
            Values[6][0] = hX;

            Values[7][0]  = m1;
            Values[8][0]  = mT1;
            Values[9][0]  = m2;
            Values[10][0] = mT2;
            Values[11][0] = m3;
            Values[12][0] = mX;
        }

        #region Properties

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name="ADR Length (Daily RMA/Wilder)", Order=1, GroupName="Parameters")]
        public int AdrLength { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name="IB Start Hour", Order=10, GroupName="IB Window")]
        public int IBStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name="IB Start Minute", Order=11, GroupName="IB Window")]
        public int IBStartMinute { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name="IB End Hour", Order=12, GroupName="IB Window")]
        public int IBEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name="IB End Minute", Order=13, GroupName="IB Window")]
        public int IBEndMinute { get; set; }

        // ---- Ladder multipliers (tune to match TV) ----
        [NinjaScriptProperty]
        [Range(0.05, 10.0)]
        [Display(Name="High1 Mult", Order=20, GroupName="TV Ladder Multipliers")]
        public double MultHigh1 { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10.0)]
        [Display(Name="HighTarget1 Mult", Order=21, GroupName="TV Ladder Multipliers")]
        public double MultHighTarget1 { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10.0)]
        [Display(Name="High2 Mult", Order=22, GroupName="TV Ladder Multipliers")]
        public double MultHigh2 { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10.0)]
        [Display(Name="High2Target Mult", Order=23, GroupName="TV Ladder Multipliers")]
        public double MultHighTarget2 { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10.0)]
        [Display(Name="High3 Mult", Order=24, GroupName="TV Ladder Multipliers")]
        public double MultHigh3 { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 10.0)]
        [Display(Name="Extreme Mult", Order=25, GroupName="TV Ladder Multipliers")]
        public double MultExtreme { get; set; }

        #endregion
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private KeyLevelsADR_IBPOC_TVLadder[] cacheKeyLevelsADR_IBPOC_TVLadder;
		public KeyLevelsADR_IBPOC_TVLadder KeyLevelsADR_IBPOC_TVLadder(int adrLength, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute, double multHigh1, double multHighTarget1, double multHigh2, double multHighTarget2, double multHigh3, double multExtreme)
		{
			return KeyLevelsADR_IBPOC_TVLadder(Input, adrLength, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute, multHigh1, multHighTarget1, multHigh2, multHighTarget2, multHigh3, multExtreme);
		}

		public KeyLevelsADR_IBPOC_TVLadder KeyLevelsADR_IBPOC_TVLadder(ISeries<double> input, int adrLength, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute, double multHigh1, double multHighTarget1, double multHigh2, double multHighTarget2, double multHigh3, double multExtreme)
		{
			if (cacheKeyLevelsADR_IBPOC_TVLadder != null)
				for (int idx = 0; idx < cacheKeyLevelsADR_IBPOC_TVLadder.Length; idx++)
					if (cacheKeyLevelsADR_IBPOC_TVLadder[idx] != null && cacheKeyLevelsADR_IBPOC_TVLadder[idx].AdrLength == adrLength && cacheKeyLevelsADR_IBPOC_TVLadder[idx].IBStartHour == iBStartHour && cacheKeyLevelsADR_IBPOC_TVLadder[idx].IBStartMinute == iBStartMinute && cacheKeyLevelsADR_IBPOC_TVLadder[idx].IBEndHour == iBEndHour && cacheKeyLevelsADR_IBPOC_TVLadder[idx].IBEndMinute == iBEndMinute && cacheKeyLevelsADR_IBPOC_TVLadder[idx].MultHigh1 == multHigh1 && cacheKeyLevelsADR_IBPOC_TVLadder[idx].MultHighTarget1 == multHighTarget1 && cacheKeyLevelsADR_IBPOC_TVLadder[idx].MultHigh2 == multHigh2 && cacheKeyLevelsADR_IBPOC_TVLadder[idx].MultHighTarget2 == multHighTarget2 && cacheKeyLevelsADR_IBPOC_TVLadder[idx].MultHigh3 == multHigh3 && cacheKeyLevelsADR_IBPOC_TVLadder[idx].MultExtreme == multExtreme && cacheKeyLevelsADR_IBPOC_TVLadder[idx].EqualsInput(input))
						return cacheKeyLevelsADR_IBPOC_TVLadder[idx];
			return CacheIndicator<KeyLevelsADR_IBPOC_TVLadder>(new KeyLevelsADR_IBPOC_TVLadder(){ AdrLength = adrLength, IBStartHour = iBStartHour, IBStartMinute = iBStartMinute, IBEndHour = iBEndHour, IBEndMinute = iBEndMinute, MultHigh1 = multHigh1, MultHighTarget1 = multHighTarget1, MultHigh2 = multHigh2, MultHighTarget2 = multHighTarget2, MultHigh3 = multHigh3, MultExtreme = multExtreme }, input, ref cacheKeyLevelsADR_IBPOC_TVLadder);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.KeyLevelsADR_IBPOC_TVLadder KeyLevelsADR_IBPOC_TVLadder(int adrLength, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute, double multHigh1, double multHighTarget1, double multHigh2, double multHighTarget2, double multHigh3, double multExtreme)
		{
			return indicator.KeyLevelsADR_IBPOC_TVLadder(Input, adrLength, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute, multHigh1, multHighTarget1, multHigh2, multHighTarget2, multHigh3, multExtreme);
		}

		public Indicators.KeyLevelsADR_IBPOC_TVLadder KeyLevelsADR_IBPOC_TVLadder(ISeries<double> input , int adrLength, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute, double multHigh1, double multHighTarget1, double multHigh2, double multHighTarget2, double multHigh3, double multExtreme)
		{
			return indicator.KeyLevelsADR_IBPOC_TVLadder(input, adrLength, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute, multHigh1, multHighTarget1, multHigh2, multHighTarget2, multHigh3, multExtreme);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.KeyLevelsADR_IBPOC_TVLadder KeyLevelsADR_IBPOC_TVLadder(int adrLength, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute, double multHigh1, double multHighTarget1, double multHigh2, double multHighTarget2, double multHigh3, double multExtreme)
		{
			return indicator.KeyLevelsADR_IBPOC_TVLadder(Input, adrLength, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute, multHigh1, multHighTarget1, multHigh2, multHighTarget2, multHigh3, multExtreme);
		}

		public Indicators.KeyLevelsADR_IBPOC_TVLadder KeyLevelsADR_IBPOC_TVLadder(ISeries<double> input , int adrLength, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute, double multHigh1, double multHighTarget1, double multHigh2, double multHighTarget2, double multHigh3, double multExtreme)
		{
			return indicator.KeyLevelsADR_IBPOC_TVLadder(input, adrLength, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute, multHigh1, multHighTarget1, multHigh2, multHighTarget2, multHigh3, multExtreme);
		}
	}
}

#endregion
