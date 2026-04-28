#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Gu5-based "Stop X" manager:
    /// - Does not submit entries.
    /// - Monitors the current position on this instrument/account.
    /// - Exits when ADX slope or DI slope (or Gu5 gold cross) turns against.
    /// </summary>
    public class ADXGu5StopX : Strategy
    {
        // ===== Core Gu5 / DM / ADX parameters =====
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ADX Smoothing (SigLen)", GroupName = "1. Gu5 / ADX", Order = 0)]
        public int SigLen { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "DI Length (DiLen)", GroupName = "1. Gu5 / ADX", Order = 1)]
        public int DiLen { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Level Range (HlRange)", GroupName = "1. Gu5 / ADX", Order = 2)]
        public int HlRange { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Level Trend (HlTrend)", GroupName = "1. Gu5 / ADX", Order = 3)]
        public int HlTrend { get; set; } = 35;

        // ===== Stop X feature toggles =====
        [NinjaScriptProperty]
        [Display(Name = "Use ADX Slope StopX", GroupName = "2. StopX Rules", Order = 0)]
        public bool UseAdxSlopeStopX { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Use DI Slope StopX", GroupName = "2. StopX Rules", Order = 1)]
        public bool UseDiSlopeStopX { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Use Gu5 Gold-Cross StopX", GroupName = "2. StopX Rules", Order = 2)]
        public bool UseGu5ExitStopX { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Slope Lookback Bars", GroupName = "2. StopX Rules", Order = 3)]
        public int SlopeLookback { get; set; } = 1;

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Min ADX for StopX check", GroupName = "2. StopX Rules", Order = 4)]
        public double MinAdxForStopX { get; set; } = 20.0;

        [NinjaScriptProperty]
        [Display(Name = "Debug Print", GroupName = "3. Debug", Order = 0)]
        public bool DebugPrint { get; set; } = false;

        // ===== Internal indicators =====
        private DM dm;
        private ADX adx;
        private ADXGu5 adxGu5;

        // Prevent spamming multiple exits
        private bool stopXSubmitted = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ADXGu5StopX";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                // This strategy does NOT submit entries by default
                IsUnmanaged = false;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
				
 			   // 🔑 allow “Adopt account position” in the UI
    			IsAdoptAccountPositionAware = true;
    			// (optional) make it the default
    			StartBehavior = StartBehavior.AdoptAccountPosition;
            }
            else if (State == State.DataLoaded)
            {
                dm     = DM(DiLen);
                adx    = ADX(SigLen);
                adxGu5 = ADXGu5(SigLen, DiLen, HlRange, HlTrend);

                // Optional: show the same Gu5 instance on chart
                AddChartIndicator(adxGu5);
            }
        }

        protected override void OnBarUpdate()
        {
            int minBars = Math.Max(Math.Max(SigLen, DiLen) + 2, SlopeLookback + 1);
            if (CurrentBar < minBars)
                return;

            // If no open position for THIS strategy, reset flag and do nothing
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                stopXSubmitted = false;
                return;
            }

            double adxNow  = adx[0];
            double adxPrev = adx[SlopeLookback];

            bool adxSlopeDown = adxNow < adxPrev;
            bool adxTrendZone = adxNow >= MinAdxForStopX;

            double diPlusNow   = dm.DiPlus[0];
            double diPlusPrev  = dm.DiPlus[SlopeLookback];
            double diMinusNow  = dm.DiMinus[0];
            double diMinusPrev = dm.DiMinus[SlopeLookback];

            // For longs: DI+ weakening or DI- strengthening = bad
            bool diSlopeBadForLong =
                (diPlusNow < diPlusPrev) || (diMinusNow > diMinusPrev);

            // For shorts: DI- weakening or DI+ strengthening = bad
            bool diSlopeBadForShort =
                (diMinusNow < diMinusPrev) || (diPlusNow > diPlusPrev);

            bool gu5LongExit  = adxGu5.LongXSeries[0]  > 0;
            bool gu5ShortExit = adxGu5.ShortXSeries[0] > 0;

            if (DebugPrint)
            {
                Print(string.Format("{0}  Pos={1} ADX={2:F2} (prev {3:F2})  DI+={4:F2}->{5:F2}  DI-={6:F2}->{7:F2}",
                    Time[0],
                    Position.MarketPosition,
                    adxNow, adxPrev,
                    diPlusPrev, diPlusNow,
                    diMinusPrev, diMinusNow));
            }

            // ===== LONG position StopX logic =====
            if (Position.MarketPosition == MarketPosition.Long && !stopXSubmitted)
            {
                bool fire = false;
                string reason = "";

                if (UseAdxSlopeStopX && adxTrendZone && adxSlopeDown)
                {
                    fire = true;
                    reason = "ADX slope down";
                }

                if (!fire && UseDiSlopeStopX && diSlopeBadForLong)
                {
                    fire = true;
                    reason = "DI slope bad for long";
                }

                if (!fire && UseGu5ExitStopX && gu5LongExit)
                {
                    fire = true;
                    reason = "Gu5 gold-cross long exit";
                }

                if (fire)
                {
                    if (DebugPrint)
                        Print(string.Format("{0}  StopX LONG fired: {1}", Time[0], reason));

                    ExitLong("StopX_Long", "");
                    stopXSubmitted = true;
                }
            }

            // ===== SHORT position StopX logic =====
            if (Position.MarketPosition == MarketPosition.Short && !stopXSubmitted)
            {
                bool fire = false;
                string reason = "";

                if (UseAdxSlopeStopX && adxTrendZone && adxSlopeDown)
                {
                    fire = true;
                    reason = "ADX slope down";
                }

                if (!fire && UseDiSlopeStopX && diSlopeBadForShort)
                {
                    fire = true;
                    reason = "DI slope bad for short";
                }

                if (!fire && UseGu5ExitStopX && gu5ShortExit)
                {
                    fire = true;
                    reason = "Gu5 gold-cross short exit";
                }

                if (fire)
                {
                    if (DebugPrint)
                        Print(string.Format("{0}  StopX SHORT fired: {1}", Time[0], reason));

                    ExitShort("StopX_Short", "");
                    stopXSubmitted = true;
                }
            }

            // Once flat again, flag resets at top of OnBarUpdate
        }
    }
}
