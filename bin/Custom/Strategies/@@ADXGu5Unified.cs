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
    public class ADXGu5Unified : Strategy
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

        // ===== Session VWAP (internal) =====
        private Series<double> sessionVWAP;
        private double cumPV = 0.0, cumVol = 0.0;

        // ===== Arming flags =====
        private bool longArmed;
        private bool shortArmed;

        // Chop gate cached (bar-close style)
        private bool chopGateOK = true;

        // ===== Trailing tracking =====
        private int entryBar = -1;
        private double highSinceEntry = double.MinValue;
        private double lowSinceEntry  = double.MaxValue;

        // ===== Trade limiting / anti-machinegun =====
        private int longEntriesThisSession = 0;
        private int shortEntriesThisSession = 0;
        private int lastExitBar = -999999;
        private int lastEntryBar = -999999;
        private DateTime lastEntryTime = Core.Globals.MinDate;

        // ===== Unique signal tracking for current position legs =====
        private int entrySeq = 0;
        private string sigL1, sigL2, sigL3;
        private string sigS1, sigS2, sigS3;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ADXGu5Unified";
                Calculate = Calculate.OnEachTick;

                // Allow multiple entries, but we will enforce our own caps below
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
                StrongAdxLevel = 35;
                SlopeLookback = 1;

                // ===== Chop Filter =====
                UseChopFilter = true;
                ChopPeriod = 14;
                ChopMaxForEntry = 60;
                RequireChopFalling = true;

                // ===== EMA No-Trade Zone =====
                UseEmaNoTradeZone = false;
                EmaPeriod = 50;
                EmaBandTicks = 20;

                // ===== VWAP No-Trade Zone =====
                UseVwapNoTradeZone = false;
                VwapBandTicks = 40;

                // ===== Hard caps + anti-machinegun =====
                MaxLongEntriesPerSession = 1;
                MaxShortEntriesPerSession = 1;
                CooldownBarsAfterExit = 1;
                MinSecondsBetweenEntries = 15;

                // ===== Stops/Targets =====
                UseStopsTargets = true;
                StopLossTicks = 24;

                // ===== Global StopX kill switches =====
                UseExitOnAdxSlopeDown = true;
                UseExitOnDiSlopeDown = true;
                UseExitOnDiCrossKill = true;

                // ===== Leg 1 =====
                Qty1 = 1;
                Target1Ticks = 60;
                Trail1Mode = TrailMode.TickTrail;
                Trail1Ticks = 25;

                // ===== Leg 2 =====
                Qty2 = 1;
                Target2Ticks = 120;
                Trail2Mode = TrailMode.BarNTrail;
                BarN2 = 1;
                BarN2OffsetTicks = 2;
                Trail2Ticks = 25;

                // ===== Leg 3 =====
                Qty3 = 1;
                Target3Ticks = 200;
                Trail3Mode = TrailMode.HybridBarNThenTick;
                BarN3 = 2;
                BarN3OffsetTicks = 2;
                Hybrid3SwitchBars = 6;
                Trail3Ticks = 35;
            }
            else if (State == State.DataLoaded)
            {
                adx  = ADX(SigLen);
                dm   = DM(DiLen);
                chop = ChoppinessIndex(ChopPeriod);

                if (UseEmaNoTradeZone)
                    ema = EMA(EmaPeriod);

                // internal session VWAP series
                sessionVWAP = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            int minBars = Math.Max(Math.Max(SigLen, DiLen), ChopPeriod) + 5;
            if (UseEmaNoTradeZone) minBars = Math.Max(minBars, EmaPeriod + 5);
            if (CurrentBar < minBars) return;

            // ===== Session reset =====
            if (Bars.IsFirstBarOfSession)
            {
                longEntriesThisSession = 0;
                shortEntriesThisSession = 0;

                longArmed = false;
                shortArmed = false;

                // reset VWAP accumulators
                cumPV = 0.0;
                cumVol = 0.0;
            }

            // ===== Session VWAP update (your method) =====
            {
                double typ = (High[0] + Low[0] + Close[0]) / 3.0;
                double vol = Math.Max(1.0, Volume[0]);
                cumPV += typ * vol;
                cumVol += vol;
                sessionVWAP[0] = (cumVol > 0 ? cumPV / cumVol : Close[0]);
            }

            // ===== 1) Bar-close style Chop gate (use last CLOSED bars: [1] and [2]) =====
            if (UseChopFilter)
            {
                double chop1 = chop[1];
                double chop2 = chop[2];

                bool belowMax  = chop1 < ChopMaxForEntry;
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

            // ===== 2b) Optional VWAP no-trade zone (internal session VWAP) =====
            if (UseVwapNoTradeZone && sessionVWAP != null)
            {
                double band = VwapBandTicks * TickSize;
                double v = sessionVWAP[0];
                if (Close[0] > v - band && Close[0] < v + band)
                    return;
            }

            // ===== 3) Read ADX/DI tick-by-tick =====
            double adxNow  = adx[0];
            double adxPrev = adx[SlopeLookback];

            double diPlusNow   = dm.DiPlus[0];
            double diPlusPrev  = dm.DiPlus[SlopeLookback];

            double diMinusNow  = dm.DiMinus[0];
            double diMinusPrev = dm.DiMinus[SlopeLookback];

            bool adxAboveEntry = adxNow >= EntryAdxLevel;
            bool adxRising     = adxNow > adxPrev;

            bool diBullDom = diPlusNow > diMinusNow;
            bool diBearDom = diMinusNow > diPlusNow;

            bool diPlusRising  = diPlusNow > diPlusPrev;
            bool diMinusRising = diMinusNow > diMinusPrev;

            // ===== 4) Update trailing tracking =====
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (entryBar < 0) entryBar = CurrentBar;

                highSinceEntry = Math.Max(highSinceEntry, High[0]);
                lowSinceEntry  = Math.Min(lowSinceEntry, Low[0]);

                UpdateAllLegStops();
            }
            else
            {
                entryBar = -1;
                highSinceEntry = double.MinValue;
                lowSinceEntry = double.MaxValue;

                // clear current-leg signals when flat
                sigL1 = sigL2 = sigL3 = null;
                sigS1 = sigS2 = sigS3 = null;
            }

            // ===== 5) Global StopX exits =====
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
                    lastExitBar = CurrentBar;
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
                    lastExitBar = CurrentBar;
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

            // Anti-machinegun
            if ((CurrentBar - lastExitBar) <= CooldownBarsAfterExit) return;

            if (lastEntryTime != Core.Globals.MinDate)
            {
                double secs = (Times[0][0] - lastEntryTime).TotalSeconds;
                if (secs < MinSecondsBetweenEntries) return;
            }

            if (CurrentBar == lastEntryBar) return;

            bool entryAdxOK = adxAboveEntry && (!RequireAdxRisingForEntry || adxRising);

            bool longOK =
                UseLongs && longArmed && entryAdxOK &&
                (!RequireDiRisingForEntry || diPlusRising);

            bool shortOK =
                UseShorts && shortArmed && entryAdxOK &&
                (!RequireDiRisingForEntry || diMinusRising);

            // Hard session caps (counts an ENTRY EVENT, not each leg)
            if (longOK && longEntriesThisSession >= MaxLongEntriesPerSession) longOK = false;
            if (shortOK && shortEntriesThisSession >= MaxShortEntriesPerSession) shortOK = false;

            if (longOK)
            {
                SubmitEntryLong();
                longArmed = false;
                longEntriesThisSession++;

                lastEntryBar = CurrentBar;
                lastEntryTime = Times[0][0];
            }
            else if (shortOK)
            {
                SubmitEntryShort();
                shortArmed = false;
                shortEntriesThisSession++;

                lastEntryBar = CurrentBar;
                lastEntryTime = Times[0][0];
            }
        }

        private void SubmitEntryLong()
        {
            entrySeq++;
            sigL1 = $"L1_{entrySeq}";
            sigL2 = $"L2_{entrySeq}";
            sigL3 = $"L3_{entrySeq}";

            entryBar = CurrentBar;
            highSinceEntry = High[0];
            lowSinceEntry = Low[0];

            if (UseStopsTargets)
            {
                ConfigureInitialOrdersForLeg(sigL1, Target1Ticks);
                ConfigureInitialOrdersForLeg(sigL2, Target2Ticks);
                ConfigureInitialOrdersForLeg(sigL3, Target3Ticks);
            }

            if (Qty1 > 0) EnterLong(Qty1, sigL1);
            if (Qty2 > 0) EnterLong(Qty2, sigL2);
            if (Qty3 > 0) EnterLong(Qty3, sigL3);
        }

        private void SubmitEntryShort()
        {
            entrySeq++;
            sigS1 = $"S1_{entrySeq}";
            sigS2 = $"S2_{entrySeq}";
            sigS3 = $"S3_{entrySeq}";

            entryBar = CurrentBar;
            highSinceEntry = High[0];
            lowSinceEntry = Low[0];

            if (UseStopsTargets)
            {
                ConfigureInitialOrdersForLeg(sigS1, Target1Ticks);
                ConfigureInitialOrdersForLeg(sigS2, Target2Ticks);
                ConfigureInitialOrdersForLeg(sigS3, Target3Ticks);
            }

            if (Qty1 > 0) EnterShort(Qty1, sigS1);
            if (Qty2 > 0) EnterShort(Qty2, sigS2);
            if (Qty3 > 0) EnterShort(Qty3, sigS3);
        }

        private void ConfigureInitialOrdersForLeg(string signal, int targetTicks)
        {
            SetStopLoss(signal, CalculationMode.Ticks, StopLossTicks, false);
            SetProfitTarget(signal, CalculationMode.Ticks, Math.Max(1, targetTicks));
        }

        private void UpdateAllLegStops()
        {
            int barsInTrade = (entryBar >= 0) ? (CurrentBar - entryBar) : 0;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                ApplyLegTrail_Long(sigL1, Trail1Mode, Trail1Ticks, BarN2, BarN2OffsetTicks, barsInTrade, Hybrid3SwitchBars);
                ApplyLegTrail_Long(sigL2, Trail2Mode, Trail2Ticks, BarN2, BarN2OffsetTicks, barsInTrade, Hybrid3SwitchBars);
                ApplyLegTrail_Long(sigL3, Trail3Mode, Trail3Ticks, BarN3, BarN3OffsetTicks, barsInTrade, Hybrid3SwitchBars);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                ApplyLegTrail_Short(sigS1, Trail1Mode, Trail1Ticks, BarN2, BarN2OffsetTicks, barsInTrade, Hybrid3SwitchBars);
                ApplyLegTrail_Short(sigS2, Trail2Mode, Trail2Ticks, BarN2, BarN2OffsetTicks, barsInTrade, Hybrid3SwitchBars);
                ApplyLegTrail_Short(sigS3, Trail3Mode, Trail3Ticks, BarN3, BarN3OffsetTicks, barsInTrade, Hybrid3SwitchBars);
            }
        }

        // ===== Helpers for safe Bid/Ask when clamping =====
        private double GetSafeBid()
        {
            double bid = GetCurrentBid();
            if (bid <= 0) bid = Close[0];
            return bid;
        }

        private double GetSafeAsk()
        {
            double ask = GetCurrentAsk();
            if (ask <= 0) ask = Close[0];
            return ask;
        }

        private void ApplyLegTrail_Long(string signal, TrailMode mode, int tickTrail, int barN, int barNOffsetTicks, int barsInTrade, int hybridSwitchBars)
        {
            if (string.IsNullOrEmpty(signal)) return;
            if (mode == TrailMode.None) return;

            TrailMode effective = mode;
            if (mode == TrailMode.HybridBarNThenTick)
                effective = (barsInTrade >= hybridSwitchBars) ? TrailMode.TickTrail : TrailMode.BarNTrail;

            double desiredStop;

            if (effective == TrailMode.TickTrail)
            {
                int tt = Math.Max(1, tickTrail);
                desiredStop = highSinceEntry - tt * TickSize;
            }
            else
            {
                int bn = Math.Max(1, barN);
                int idx = Math.Min(bn, CurrentBar);
                desiredStop = Low[idx] - Math.Max(0, barNOffsetTicks) * TickSize;
            }

            // Clamp: never place sell stop ABOVE market
            double bid = GetSafeBid();
            double maxStop = bid - TickSize;
            if (desiredStop > maxStop) desiredStop = maxStop;

            SetStopLoss(signal, CalculationMode.Price, desiredStop, false);
        }

        private void ApplyLegTrail_Short(string signal, TrailMode mode, int tickTrail, int barN, int barNOffsetTicks, int barsInTrade, int hybridSwitchBars)
        {
            if (string.IsNullOrEmpty(signal)) return;
            if (mode == TrailMode.None) return;

            TrailMode effective = mode;
            if (mode == TrailMode.HybridBarNThenTick)
                effective = (barsInTrade >= hybridSwitchBars) ? TrailMode.TickTrail : TrailMode.BarNTrail;

            double desiredStop;

            if (effective == TrailMode.TickTrail)
            {
                int tt = Math.Max(1, tickTrail);
                desiredStop = lowSinceEntry + tt * TickSize;
            }
            else
            {
                int bn = Math.Max(1, barN);
                int idx = Math.Min(bn, CurrentBar);
                desiredStop = High[idx] + Math.Max(0, barNOffsetTicks) * TickSize;
            }

            // Clamp: never place buy stop BELOW market
            double ask = GetSafeAsk();
            double minStop = ask + TickSize;
            if (desiredStop < minStop) desiredStop = minStop;

            SetStopLoss(signal, CalculationMode.Price, desiredStop, false);
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

        // ===== Caps / anti-machinegun =====
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name="Max Long Entries Per Session", GroupName="0. Limits", Order=0)]
        public int MaxLongEntriesPerSession { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name="Max Short Entries Per Session", GroupName="0. Limits", Order=1)]
        public int MaxShortEntriesPerSession { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name="Cooldown Bars After Exit", GroupName="0. Limits", Order=2)]
        public int CooldownBarsAfterExit { get; set; }

        [NinjaScriptProperty]
        [Range(0, 600)]
        [Display(Name="Min Seconds Between Entries", GroupName="0. Limits", Order=3)]
        public int MinSecondsBetweenEntries { get; set; }

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

        // ===== Filters =====
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

        [NinjaScriptProperty]
        [Display(Name="Use VWAP No-Trade Zone", GroupName="4. Filters", Order=3)]
        public bool UseVwapNoTradeZone { get; set; }

        [NinjaScriptProperty]
        [Range(0, 5000)]
        [Display(Name="VWAP Band (ticks)", GroupName="4. Filters", Order=4)]
        public int VwapBandTicks { get; set; }

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
        [Range(0, 5000)]
        [Display(Name = "Trail 1 Ticks", GroupName = "7. Leg 1", Order = 3)]
        public int Trail1Ticks { get; set; }

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
        [Range(1, 20)]
        [Display(Name = "Bar N (Leg 2)", GroupName = "8. Leg 2", Order = 3)]
        public int BarN2 { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Bar N Offset (ticks) (Leg 2)", GroupName = "8. Leg 2", Order = 4)]
        public int BarN2OffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 5000)]
        [Display(Name = "Trail 2 Ticks (if Tick/Hybrid)", GroupName = "8. Leg 2", Order = 5)]
        public int Trail2Ticks { get; set; }

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
        [Range(1, 20)]
        [Display(Name = "Bar N (Leg 3)", GroupName = "9. Leg 3", Order = 3)]
        public int BarN3 { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Bar N Offset (ticks) (Leg 3)", GroupName = "9. Leg 3", Order = 4)]
        public int BarN3OffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Hybrid Switch Bars (Leg 3)", GroupName = "9. Leg 3", Order = 5)]
        public int Hybrid3SwitchBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 5000)]
        [Display(Name = "Trail 3 Ticks (Tick/Hybrid)", GroupName = "9. Leg 3", Order = 6)]
        public int Trail3Ticks { get; set; }

        #endregion
    }
}
