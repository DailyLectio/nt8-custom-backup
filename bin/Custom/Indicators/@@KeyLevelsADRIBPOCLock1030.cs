#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class KeyLevelsADR_IBPOC_Lock1030 : Indicator
    {
        // --- Session tracking for ADR (based on completed prior session range)
        private double sessionHigh = double.MinValue;
        private double sessionLow  = double.MaxValue;

        private double prevSessionHigh = double.NaN;
        private double prevSessionLow  = double.NaN;

        // ADR (Wilder/RMA) of completed session range
        private double adrRma = double.NaN;
        private int adrSamples = 0;  // for seeding

        // --- IB profile tracking (for current session 9:30–10:30)
        private Dictionary<double, double> ibVolByPrice = new Dictionary<double, double>();
        private double ibPOC = double.NaN;

        private bool levelsLockedThisSession = false;

        // Levels (locked at 10:30)
        private double lvlPOC, lvlH1, lvlH2, lvlHT, lvlM1, lvlM2, lvlMT;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Key levels projected from IB (9:30-10:30) POC using ADR (Wilder/RMA), locked at 10:30.";
                Name                     = "KeyLevelsADR_IBPOC_Lock1030";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = true;
                DisplayInDataBox         = true;
                DrawOnPricePanel         = true;
                PaintPriceMarkers        = true;
                IsSuspendedWhileInactive = true;

                AdrLength  = 10;
                Mult1      = 0.50;
                Mult2      = 1.00;
                MultTarget = 1.618;

                // Default IB window (chart/instrument time zone)
                IBStartHour = 9;
                IBStartMinute = 30;
                IBEndHour = 10;
                IBEndMinute = 30;

                AddPlot(Brushes.DodgerBlue, "POC");
                AddPlot(Brushes.Gray,       "High1");
                AddPlot(Brushes.Teal,       "High2");
                AddPlot(Brushes.DeepSkyBlue,"HighTarget");
                AddPlot(Brushes.Gray,       "Minus1");
                AddPlot(Brushes.Purple,     "Minus2");
                AddPlot(Brushes.Magenta,    "MinusTarget");
            }
            else if (State == State.DataLoaded)
            {
                ResetForNewSession();
                lvlPOC = lvlH1 = lvlH2 = lvlHT = lvlM1 = lvlM2 = lvlMT = double.NaN;
            }
        }

        private void ResetForNewSession()
        {
            sessionHigh = double.MinValue;
            sessionLow  = double.MaxValue;

            ibVolByPrice.Clear();
            ibPOC = double.NaN;

            levelsLockedThisSession = false;

            // keep levels NaN until locked
            lvlPOC = lvlH1 = lvlH2 = lvlHT = lvlM1 = lvlM2 = lvlMT = double.NaN;
        }

        private TimeSpan IBStart => new TimeSpan(IBStartHour, IBStartMinute, 0);
        private TimeSpan IBEnd   => new TimeSpan(IBEndHour,   IBEndMinute,   0);

        private double RoundToTick(double price)
        {
            double ts = TickSize;
            if (ts <= 0) return price;
            return Math.Round(price / ts) * ts;
        }

        private void AddBarVolumeToVAP(Dictionary<double, double> map, double low, double high, double barVol)
        {
            // Distribute bar volume evenly across ticks from Low..High (good proxy for session volume profile)
            double ts = TickSize;
            if (ts <= 0 || barVol <= 0)
                return;

            double lo = RoundToTick(low);
            double hi = RoundToTick(high);

            if (hi < lo)
            {
                double tmp = lo; lo = hi; hi = tmp;
            }

            int steps = (int)Math.Round((hi - lo) / ts) + 1;
            if (steps <= 0) return;

            double volPerStep = barVol / steps;
            double p = lo;

            for (int i = 0; i < steps; i++)
            {
                double key = RoundToTick(p);
                if (!map.ContainsKey(key))
                    map[key] = 0;
                map[key] += volPerStep;
                p += ts;
            }
        }

        private double ComputePOC(Dictionary<double, double> map)
        {
            double poc = double.NaN;
            double maxVol = double.MinValue;

            foreach (var kv in map)
            {
                if (kv.Value > maxVol)
                {
                    maxVol = kv.Value;
                    poc = kv.Key;
                }
            }
            return poc;
        }

        private double WilderRmaSeedOrUpdate(double priorRma, double x, int n)
        {
            // Seed using SMA for first n samples, then Wilder
            adrSamples++;

            if (adrSamples <= n)
            {
                if (double.IsNaN(priorRma))
                    return x;
                return priorRma + (x - priorRma) / adrSamples;
            }

            return (priorRma * (n - 1) + x) / n;
        }

        private void LockLevelsFromIBPOC()
        {
            if (double.IsNaN(ibPOC) || double.IsNaN(adrRma))
                return;

            lvlPOC = ibPOC;

            lvlH1 = lvlPOC + Mult1      * adrRma;
            lvlH2 = lvlPOC + Mult2      * adrRma;
            lvlHT = lvlPOC + MultTarget * adrRma;

            lvlM1 = lvlPOC - Mult1      * adrRma;
            lvlM2 = lvlPOC - Mult2      * adrRma;
            lvlMT = lvlPOC - MultTarget * adrRma;

            levelsLockedThisSession = true;
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            // New session boundary (based on Trading Hours template)
            if (Bars.IsFirstBarOfSession)
            {
                // finalize prior session for ADR update
                if (CurrentBar > 1 && sessionHigh != double.MinValue && sessionLow != double.MaxValue)
                {
                    prevSessionHigh = sessionHigh;
                    prevSessionLow  = sessionLow;

                    double prevRange = prevSessionHigh - prevSessionLow;
                    if (prevRange >= 0)
                        adrRma = WilderRmaSeedOrUpdate(adrRma, prevRange, Math.Max(1, AdrLength));
                }

                ResetForNewSession();
            }

            // Track current session H/L (for next session ADR update)
            sessionHigh = Math.Max(sessionHigh, High[0]);
            sessionLow  = Math.Min(sessionLow,  Low[0]);

            // Determine time-of-day for IB window
            TimeSpan tod = Time[0].TimeOfDay;

            bool inIB = tod >= IBStart && tod < IBEnd;
            bool pastIB = tod >= IBEnd;

            // Build IB volume profile only during IB window
            if (!levelsLockedThisSession && inIB)
            {
                AddBarVolumeToVAP(ibVolByPrice, Low[0], High[0], Volume[0]);
            }

            // At/after IB end, lock once using the computed IB POC
            if (!levelsLockedThisSession && pastIB)
            {
                ibPOC = ComputePOC(ibVolByPrice);
                LockLevelsFromIBPOC();
            }

            // Plot levels (NaN until locked)
            Values[0][0] = lvlPOC;
            Values[1][0] = lvlH1;
            Values[2][0] = lvlH2;
            Values[3][0] = lvlHT;
            Values[4][0] = lvlM1;
            Values[5][0] = lvlM2;
            Values[6][0] = lvlMT;
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name="ADR Length (RMA/Wilder)", Order=1, GroupName="Parameters")]
        public int AdrLength { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name="Multiplier 1 (High1/Minus1)", Order=2, GroupName="Parameters")]
        public double Mult1 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name="Multiplier 2 (High2/Minus2)", Order=3, GroupName="Parameters")]
        public double Mult2 { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name="Target Multiplier (HighTarget/MinusTarget)", Order=4, GroupName="Parameters")]
        public double MultTarget { get; set; }

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
        #endregion
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private KeyLevelsADR_IBPOC_Lock1030[] cacheKeyLevelsADR_IBPOC_Lock1030;
		public KeyLevelsADR_IBPOC_Lock1030 KeyLevelsADR_IBPOC_Lock1030(int adrLength, double mult1, double mult2, double multTarget, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute)
		{
			return KeyLevelsADR_IBPOC_Lock1030(Input, adrLength, mult1, mult2, multTarget, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute);
		}

		public KeyLevelsADR_IBPOC_Lock1030 KeyLevelsADR_IBPOC_Lock1030(ISeries<double> input, int adrLength, double mult1, double mult2, double multTarget, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute)
		{
			if (cacheKeyLevelsADR_IBPOC_Lock1030 != null)
				for (int idx = 0; idx < cacheKeyLevelsADR_IBPOC_Lock1030.Length; idx++)
					if (cacheKeyLevelsADR_IBPOC_Lock1030[idx] != null && cacheKeyLevelsADR_IBPOC_Lock1030[idx].AdrLength == adrLength && cacheKeyLevelsADR_IBPOC_Lock1030[idx].Mult1 == mult1 && cacheKeyLevelsADR_IBPOC_Lock1030[idx].Mult2 == mult2 && cacheKeyLevelsADR_IBPOC_Lock1030[idx].MultTarget == multTarget && cacheKeyLevelsADR_IBPOC_Lock1030[idx].IBStartHour == iBStartHour && cacheKeyLevelsADR_IBPOC_Lock1030[idx].IBStartMinute == iBStartMinute && cacheKeyLevelsADR_IBPOC_Lock1030[idx].IBEndHour == iBEndHour && cacheKeyLevelsADR_IBPOC_Lock1030[idx].IBEndMinute == iBEndMinute && cacheKeyLevelsADR_IBPOC_Lock1030[idx].EqualsInput(input))
						return cacheKeyLevelsADR_IBPOC_Lock1030[idx];
			return CacheIndicator<KeyLevelsADR_IBPOC_Lock1030>(new KeyLevelsADR_IBPOC_Lock1030(){ AdrLength = adrLength, Mult1 = mult1, Mult2 = mult2, MultTarget = multTarget, IBStartHour = iBStartHour, IBStartMinute = iBStartMinute, IBEndHour = iBEndHour, IBEndMinute = iBEndMinute }, input, ref cacheKeyLevelsADR_IBPOC_Lock1030);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.KeyLevelsADR_IBPOC_Lock1030 KeyLevelsADR_IBPOC_Lock1030(int adrLength, double mult1, double mult2, double multTarget, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute)
		{
			return indicator.KeyLevelsADR_IBPOC_Lock1030(Input, adrLength, mult1, mult2, multTarget, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute);
		}

		public Indicators.KeyLevelsADR_IBPOC_Lock1030 KeyLevelsADR_IBPOC_Lock1030(ISeries<double> input , int adrLength, double mult1, double mult2, double multTarget, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute)
		{
			return indicator.KeyLevelsADR_IBPOC_Lock1030(input, adrLength, mult1, mult2, multTarget, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.KeyLevelsADR_IBPOC_Lock1030 KeyLevelsADR_IBPOC_Lock1030(int adrLength, double mult1, double mult2, double multTarget, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute)
		{
			return indicator.KeyLevelsADR_IBPOC_Lock1030(Input, adrLength, mult1, mult2, multTarget, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute);
		}

		public Indicators.KeyLevelsADR_IBPOC_Lock1030 KeyLevelsADR_IBPOC_Lock1030(ISeries<double> input , int adrLength, double mult1, double mult2, double multTarget, int iBStartHour, int iBStartMinute, int iBEndHour, int iBEndMinute)
		{
			return indicator.KeyLevelsADR_IBPOC_Lock1030(input, adrLength, mult1, mult2, multTarget, iBStartHour, iBStartMinute, iBEndHour, iBEndMinute);
		}
	}
}

#endregion
