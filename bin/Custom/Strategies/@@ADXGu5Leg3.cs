#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ADXGu5Leg3 : Strategy
    {
        // ===== Trail modes per leg =====
        public enum TrailMode
        {
            None = 0,
            TickTrail = 1,
            BarNTrail = 2,
            HybridBarNThenTick = 3
        }

        // ===== Indicators =====
        private ADX adx;
        private DM dm;
        private ChoppinessIndex chop;
        private EMA ema;

        // ===== Arming flags =====
        private bool longArmed;
        private bool shortArmed;

        // Chop gate cached (bar-close style)
        private bool chopGateOK = true;

        // ===== Trailing tracking (shared, since entries are same bar) =====
        private int entryBar = -1;
        private double highSinceEntry = double.MinValue;
        private double lowSinceEntry = double.MaxValue;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ADXGu5Leg3";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 3;
                EntryHandling = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;

                // ===== Entries =====
                UseLongs = true;
                UseShorts = true;
                UseWaitForAdxArming = true;
                ConfirmEntryOnNewBar = true;

                RequireAdxRisingForEntry = true;
                RequireDiRisingForEntry = true;

                // ===== ADX/DI params =====
                SigLen = 14;
                DiLen = 14;
                EntryAdxLevel = 20;
                StrongAdxLevel = 35;     // kept for future expansion (not required in current entry gate)
                SlopeLookback = 1;

                // ===== Chop Filter (bar-close style) =====
                UseChopFilter = true;
                ChopPeriod = 14;
                ChopMaxForEntry = 60;
                RequireChopFalling = true;

                // ===== EMA No-Trade Zone (optional) =====
                UseEmaNoTradeZone = false;
                EmaPeriod = 50;
                EmaBandTicks = 20;

                // ===== Stops/Targets (initial protection) =====
                UseStopsTargets = true;
                StopLossTicks = 24;

                // ===== Global StopX kill switches =====
                UseExitOnAdxSlopeDown = true;
                UseExitOnDiSlopeDown = true;
                UseExitOnDiCrossKill = true;

                // ===== Leg 1 defaults =====
                Qty1 = 1;
                Target1Ticks = 60;
                Trail1Mode = TrailMode.TickTrail;
                Trail1TickTrailTicks = 25;
                Trail1BarN = 1;
                Trail1BarNOffsetTicks = 2;
                Trail1HybridSwitchBars = 6;

                // ===== Leg 2 defaults =====
                Qty2 = 1;
                Target2Ticks = 120;
                Trail2Mode = TrailMode.BarNTrail;
                Trail2TickTrailTicks = 25;
                Trail2BarN = 1;
                Trail2BarNOffsetTicks = 2;
                Trail2HybridSwitchBars = 6;

                // ===== Leg 3 defaults =====
                Qty3 = 1;
                Target3Ticks = 200;
                Trail3Mode = TrailMode.HybridBarNThenTick;
                Trail3TickTrailTicks = 35;
                Trail3BarN = 2;
                Trail3BarNOffsetTicks = 2;
                Trail3HybridSwitchBars = 6;
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(SigLen);
                dm = DM(DiLen);
                chop = ChoppinessIndex(ChopPeriod);

                if (UseEmaNoTradeZone)
                    ema = EMA(EmaPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            int minBars = Math.Max(Math.Max(SigLen, DiLen), ChopPeriod) + 5;
            if (UseEmaNoTradeZone) minBars = Math.Max(minBars, EmaPeriod + 5);
            if (CurrentBar < minBars) return;

            // ===== 1) Bar-close style Chop gate (use last CLOSED bars: [1] and [2]) =====
            if (UseChopFilter)
            {
                double chop1 = chop[1];
                double chop2 = chop[2];

                bool belowMax = chop1 < ChopMaxForEntry;
                bool fallingOK = !RequireChopFalling || chop1 <= chop2;

                chopGateOK = belowMax && fallingOK;
            }
            else chopGateOK = true;

            // ===== 2) Optional EMA no-trade zone =====
            if (UseEmaNoTradeZone && ema != null)
            {
                double band = EmaBandTicks * TickSize;
                double e = ema[0];

                if (Close[0] > e - band && Close[0] < e + band)
                    return;
            }

            // ===== 3) Read ADX/DI tick-by-tick =====
            double adxNow = adx[0];
            double adxPrev = adx[SlopeLookback];

            double diPlusNow = dm.DiPlus[0];
            double diPlusPrev = dm.DiPlus[SlopeLookback];

            double diMinusNow = dm.DiMinus[0];
            double diMinusPrev = dm.DiMinus[SlopeLookback];

            bool adxAboveEntry = adxNow >= EntryAdxLevel;
            bool adxRising = adxNow > adxPrev;

            bool diBullDom = diPlusNow > diMinusNow;
            bool diBearDom = diMinusNow > diPlusNow;

            bool diPlusRising = diPlusNow > diPlusPrev;
            bool diMinusRising = diMinusNow > diMinusPrev;

            // ===== 4) Update trailing tracking =====
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (entryBar < 0) entryBar = CurrentBar;

                highSinceEntry = Math.Max(highSinceEntry, High[0]);
                lowSinceEntry = Math.Min(lowSinceEntry, Low[0]);

                // continuously update per-leg stops
                UpdateAllLegStops();
            }
            else
            {
                entryBar = -1;
                highSinceEntry = double.MinValue;
                lowSinceEntry = double.MaxValue;
            }

            // ===== 5) Global StopX exits (optional) =====
            if (Position.MarketPosition == MarketPosition.Long)
            {
                bool kill =
                    (UseExitOnDiCrossKill && CrossBelow(dm.DiPlus, dm.DiMinus, 1)) ||
                    (UseExitOnAdxSlopeDown && adxNow < adxPrev) ||
                    (UseExitOnDiSlopeDown && diPlusNow < diPlusPrev);

                if (kill)
                {
                    ExitLong("StopX_Long", "");
                    longArmed = false;
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                bool kill =
                    (UseExitOnDiCrossKill && CrossAbove(dm.DiPlus, dm.DiMinus, 1)) ||
                    (UseExitOnAdxSlopeDown && adxNow < adxPrev) ||
                    (UseExitOnDiSlopeDown && diMinusNow < diMinusPrev);

                if (kill)
                {
                    ExitShort("StopX_Short", "");
                    shortArmed = false;
                }
            }

            // ===== 6) Arming logic =====
            if (UseWaitForAdxArming)
            {
                bool bullPrev = dm.DiPlus[1] > dm.DiMinus[1];
                bool bearPrev = dm.DiMinus[1] > dm.DiPlus[1];

                if (!longArmed && diBullDom && !bullPrev)
                {
                    longArmed = true;
                    shortArmed = false;
                }

                if (!shortArmed && diBearDom && !bearPrev)
                {
                    shortArmed = true;
                    longArmed = false;
                }

                // disarm if dominance lost
                if (longArmed && !diBullDom) longArmed = false;
                if (shortArmed && !diBearDom) shortArmed = false;
            }
            else
            {
                longArmed = diBullDom;
                shortArmed = diBearDom;
            }

            // ===== 7) Entry gate =====
            if (Position.MarketPosition != MarketPosition.Flat) return;
            if (!chopGateOK) return;
            if (ConfirmEntryOnNewBar && !IsFirstTickOfBar) return;

            bool entryAdxOK = adxAboveEntry && (!RequireAdxRisingForEntry || adxRising);

            bool longOK =
                UseLongs && longArmed && entryAdxOK &&
                (!RequireDiRisingForEntry || diPlusRising);

            bool shortOK =
                UseShorts && shortArmed && entryAdxOK &&
                (!RequireDiRisingForEntry || diMinusRising);

            if (longOK)
            {
                SubmitEntryLong();
                longArmed = false;
            }
            else if (shortOK)
            {
                SubmitEntryShort();
                shortArmed = false;
            }
        }

        // ===== Submit entries (3 legs) =====
        private void SubmitEntryLong()
        {
            entryBar = CurrentBar;
            highSinceEntry = High[0];
            lowSinceEntry = Low[0];

            if (UseStopsTargets)
            {
                ConfigureInitialOrdersForLeg("L1", Target1Ticks);
                ConfigureInitialOrdersForLeg("L2", Target2Ticks);
                ConfigureInitialOrdersForLeg("L3", Target3Ticks);
            }

            if (Qty1 > 0) EnterLong(Qty1, "L1");
            if (Qty2 > 0) EnterLong(Qty2, "L2");
            if (Qty3 > 0) EnterLong(Qty3, "L3");
        }

        private void SubmitEntryShort()
        {
            entryBar = CurrentBar;
            highSinceEntry = High[0];
            lowSinceEntry = Low[0];

            if (UseStopsTargets)
            {
                ConfigureInitialOrdersForLeg("S1", Target1Ticks);
                ConfigureInitialOrdersForLeg("S2", Target2Ticks);
                ConfigureInitialOrdersForLeg("S3", Target3Ticks);
            }

            if (Qty1 > 0) EnterShort(Qty1, "S1");
            if (Qty2 > 0) EnterShort(Qty2, "S2");
            if (Qty3 > 0) EnterShort(Qty3, "S3");
        }

        private void ConfigureInitialOrdersForLeg(string signal, int targetTicks)
        {
            SetStopLoss(signal, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(signal, CalculationMode.Ticks, Math.Max(1, targetTicks));
        }

        // ===== Update per-leg trailing stops =====
        private void UpdateAllLegStops()
        {
            int barsInTrade = (entryBar >= 0) ? (CurrentBar - entryBar) : 0;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                ApplyLegTrail_Long("L1", Trail1Mode, Trail1TickTrailTicks, Trail1BarN, Trail1BarNOffsetTicks, barsInTrade, Trail1HybridSwitchBars);
                ApplyLegTrail_Long("L2", Trail2Mode, Trail2TickTrailTicks, Trail2BarN, Trail2BarNOffsetTicks, barsInTrade, Trail2HybridSwitchBars);
                ApplyLegTrail_Long("L3", Trail3Mode, Trail3TickTrailTicks, Trail3BarN, Trail3BarNOffsetTicks, barsInTrade, Trail3HybridSwitchBars);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                ApplyLegTrail_Short("S1", Trail1Mode, Trail1TickTrailTicks, Trail1BarN, Trail1BarNOffsetTicks, barsInTrade, Trail1HybridSwitchBars);
                ApplyLegTrail_Short("S2", Trail2Mode, Trail2TickTrailTicks, Trail2BarN, Trail2BarNOffsetTicks, barsInTrade, Trail2HybridSwitchBars);
                ApplyLegTrail_Short("S3", Trail3Mode, Trail3TickTrailTicks, Trail3BarN, Trail3BarNOffsetTicks, barsInTrade, Trail3HybridSwitchBars);
            }
        }

        private void ApplyLegTrail_Long(string signal, TrailMode mode, int tickTrailTicks, int barN, int barNOffsetTicks, int barsInTrade, int hybridSwitchBars)
        {
            if (mode == TrailMode.None) return;
            if (!IsLegActive(signal)) return;

            TrailMode effective = mode;
            if (mode == TrailMode.HybridBarNThenTick)
                effective = (barsInTrade >= hybridSwitchBars) ? TrailMode.TickTrail : TrailMode.BarNTrail;

            double desiredStop;

            if (effective == TrailMode.TickTrail)
            {
                desiredStop = highSinceEntry - Math.Max(1, tickTrailTicks) * TickSize;
            }
            else // BarNTrail
            {
                int idx = Math.Min(Math.Max(1, barN), CurrentBar);
                desiredStop = Low[idx] - Math.Max(0, barNOffsetTicks) * TickSize;
            }

            SetStopLoss(signal, CalculationMode.Price, desiredStop, false);
        }

        private void ApplyLegTrail_Short(string signal, TrailMode mode, int tickTrailTicks, int barN, int barNOffsetTicks, int barsInTrade, int hybridSwitchBars)
        {
            if (mode == TrailMode.None) return;
            if (!IsLegActive(signal)) return;

            TrailMode effective = mode;
            if (mode == TrailMode.HybridBarNThenTick)
                effective = (barsInTrade >= hybridSwitchBars) ? TrailMode.TickTrail : TrailMode.BarNTrail;

            double desiredStop;

            if (effective == TrailMode.TickTrail)
            {
                desiredStop = lowSinceEntry + Math.Max(1, tickTrailTicks) * TickSize;
            }
            else // BarNTrail
            {
                int idx = Math.Min(Math.Max(1, barN), CurrentBar);
                desiredStop = High[idx] + Math.Max(0, barNOffsetTicks) * TickSize;
            }

            SetStopLoss(signal, CalculationMode.Price, desiredStop, false);
        }

        // avoid updating stops for legs with qty 0
        private bool IsLegActive(string signal)
        {
            if (signal == "L1" || signal == "S1") return Qty1 > 0;
            if (signal == "L2" || signal == "S2") return Qty2 > 0;
            if (signal == "L3" || signal == "S3") return Qty3 > 0;
            return true;
        }

        #region Properties

        // ===== Entries =====
        [NinjaScriptProperty]
        [Display(Name = "Use Longs", GroupName = "1. Entries", Order = 0)]
        public bool UseLongs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Shorts", GroupName = "1. Entries", Order = 1)]
        public bool UseShorts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Wait-for-ADX Arming", GroupName = "1. Entries", Order = 2)]
        public bool UseWaitForAdxArming { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Confirm Entry On New Bar", GroupName = "1. Entries", Order = 3)]
        public bool ConfirmEntryOnNewBar { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require ADX Rising (Entry)", GroupName = "1. Entries", Order = 4)]
        public bool RequireAdxRisingForEntry { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require DI Rising (Entry side)", GroupName = "1. Entries", Order = 5)]
        public bool RequireDiRisingForEntry { get; set; }

        // ===== ADX/DI =====
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ADX Smoothing (SigLen)", GroupName = "2. ADX/DI", Order = 0)]
        public int SigLen { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "DI Length (DiLen)", GroupName = "2. ADX/DI", Order = 1)]
        public int DiLen { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry ADX Level", GroupName = "2. ADX/DI", Order = 2)]
        public int EntryAdxLevel { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Strong ADX Level", GroupName = "2. ADX/DI", Order = 3)]
        public int StrongAdxLevel { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Slope Lookback Bars", GroupName = "2. ADX/DI", Order = 4)]
        public int SlopeLookback { get; set; }

        // ===== Chop =====
        [NinjaScriptProperty]
        [Display(Name = "Use Chop Filter", GroupName = "3. Chop", Order = 0)]
        public bool UseChopFilter { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "Chop Period", GroupName = "3. Chop", Order = 1)]
        public int ChopPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Chop Max For Entry", GroupName = "3. Chop", Order = 2)]
        public int ChopMaxForEntry { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Chop Falling", GroupName = "3. Chop", Order = 3)]
        public bool RequireChopFalling { get; set; }

        // ===== EMA Filter =====
        [NinjaScriptProperty]
        [Display(Name = "Use EMA No-Trade Zone", GroupName = "4. Filters", Order = 0)]
        public bool UseEmaNoTradeZone { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "EMA Period", GroupName = "4. Filters", Order = 1)]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 5000)]
        [Display(Name = "EMA Band (ticks)", GroupName = "4. Filters", Order = 2)]
        public int EmaBandTicks { get; set; }

        // ===== Orders =====
        [NinjaScriptProperty]
        [Display(Name = "Use Stops/Targets", GroupName = "5. Orders", Order = 0)]
        public bool UseStopsTargets { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Stop Loss (ticks)", GroupName = "5. Orders", Order = 1)]
        public int StopLossTicks { get; set; }

        // ===== StopX =====
        [NinjaScriptProperty]
        [Display(Name = "Exit if ADX Slope Down", GroupName = "6. StopX", Order = 0)]
        public bool UseExitOnAdxSlopeDown { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit if DI Slope Down (side)", GroupName = "6. StopX", Order = 1)]
        public bool UseExitOnDiSlopeDown { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit on DI Cross Kill", GroupName = "6. StopX", Order = 2)]
        public bool UseExitOnDiCrossKill { get; set; }

        // ===== Leg 1 =====
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Qty 1", GroupName = "7. Leg 1", Order = 0)]
        public int Qty1 { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Target 1 (ticks)", GroupName = "7. Leg 1", Order = 1)]
        public int Target1Ticks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trail 1 Mode", GroupName = "7. Leg 1", Order = 2)]
        public TrailMode Trail1Mode { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Tick Trail Ticks (Leg 1)", GroupName = "7. Leg 1", Order = 3)]
        public int Trail1TickTrailTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Bar N (Leg 1)", GroupName = "7. Leg 1", Order = 4)]
        public int Trail1BarN { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Bar N Offset (ticks) (Leg 1)", GroupName = "7. Leg 1", Order = 5)]
        public int Trail1BarNOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Hybrid Switch Bars (Leg 1)", GroupName = "7. Leg 1", Order = 6)]
        public int Trail1HybridSwitchBars { get; set; }

        // ===== Leg 2 =====
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Qty 2", GroupName = "8. Leg 2", Order = 0)]
        public int Qty2 { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Target 2 (ticks)", GroupName = "8. Leg 2", Order = 1)]
        public int Target2Ticks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trail 2 Mode", GroupName = "8. Leg 2", Order = 2)]
        public TrailMode Trail2Mode { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Tick Trail Ticks (Leg 2)", GroupName = "8. Leg 2", Order = 3)]
        public int Trail2TickTrailTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Bar N (Leg 2)", GroupName = "8. Leg 2", Order = 4)]
        public int Trail2BarN { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Bar N Offset (ticks) (Leg 2)", GroupName = "8. Leg 2", Order = 5)]
        public int Trail2BarNOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Hybrid Switch Bars (Leg 2)", GroupName = "8. Leg 2", Order = 6)]
        public int Trail2HybridSwitchBars { get; set; }

        // ===== Leg 3 =====
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Qty 3", GroupName = "9. Leg 3", Order = 0)]
        public int Qty3 { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Target 3 (ticks)", GroupName = "9. Leg 3", Order = 1)]
        public int Target3Ticks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trail 3 Mode", GroupName = "9. Leg 3", Order = 2)]
        public TrailMode Trail3Mode { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(Name = "Tick Trail Ticks (Leg 3)", GroupName = "9. Leg 3", Order = 3)]
        public int Trail3TickTrailTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Bar N (Leg 3)", GroupName = "9. Leg 3", Order = 4)]
        public int Trail3BarN { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Bar N Offset (ticks) (Leg 3)", GroupName = "9. Leg 3", Order = 5)]
        public int Trail3BarNOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Hybrid Switch Bars (Leg 3)", GroupName = "9. Leg 3", Order = 6)]
        public int Trail3HybridSwitchBars { get; set; }

        #endregion
    }
}
