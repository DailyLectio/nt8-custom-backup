// CC BY-NC 4.0
#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using System.Linq; 
using NinjaTrader.Data; 
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    // CLASS NAME UPDATED TO BREAK LINK TO OLD BACKUP FILES
    public class Pine_V3C : Strategy 
    {
        // ===== 1. Enums =====
        public enum StopMode
        {
            AtrStatic,
            TickTrailing,
            BarNTrailing
        }
        // =========================================================================
        // TRINITY COMMAND CENTER & TIME FILTERS
        // =========================================================================
        [NinjaScriptProperty]
        [Display(Name="1. Enable Trinity Filter", Description="Listen to the Regime HUD", GroupName="0. Trinity Command Center", Order=0)]
        public bool EnableTrinityFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="2. Enable Time Filters", Description="Use Start/End and Block windows", GroupName="0. Trinity Command Center", Order=1)]
        public bool EnableTimeFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="3. Shift Start Time (HHMMSS)", Description="E.g., 93000", GroupName="0. Trinity Command Center", Order=2)]
        public int StartTime { get; set; } = 93000;

        [NinjaScriptProperty]
        [Display(Name="4. Shift End Time (HHMMSS)", Description="E.g., 154500", GroupName="0. Trinity Command Center", Order=3)]
        public int EndTime { get; set; } = 154500;

        [NinjaScriptProperty]
        [Display(Name="5. Use Block Window", Description="Pause trading during this time", GroupName="0. Trinity Command Center", Order=4)]
        public bool UseBlockWindow { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name="6. Block Start (HHMMSS)", Description="E.g., 114500", GroupName="0. Trinity Command Center", Order=5)]
        public int BlockStart { get; set; } = 114500;

        [NinjaScriptProperty]
        [Display(Name="7. Block End (HHMMSS)", Description="E.g., 130000", GroupName="0. Trinity Command Center", Order=6)]
        public int BlockEnd { get; set; } = 130000;

        // The minimal Time & Block Window Logic
        private bool IsTimeAllowed()
        {
            if (!EnableTimeFilter) return true; // Master Switch

            int currentTime = ToTime(Time[0]);

            // 1. Check Primary Window
            if (currentTime < StartTime || currentTime > EndTime)
                return false;

            // 2. Check Block Window
            if (UseBlockWindow && currentTime >= BlockStart && currentTime <= BlockEnd)
                return false;

            return true;
        }

        // The live permission slip linked to the V3C HUD Parking Garage
        private bool IsBotAllowedByTrinity()
        {
            if (!EnableTrinityFilter) return true;

            NinjaTrader.NinjaScript.Indicators.RegimeMatrixHUD_V3C hudInstance = GetV3CHud();
            if (hudInstance != null)
                return hudInstance.IsPineAllowed;

            // If we can't find the V3C HUD, block trades for safety.
            return false;
        }

        private NinjaTrader.NinjaScript.Indicators.RegimeMatrixHUD_V3C GetV3CHud()
        {
            string chartSymbol = Instrument.MasterInstrument.Name;
            string leaderSymbol = GetLeaderSymbol(chartSymbol);

            NinjaTrader.NinjaScript.Indicators.RegimeMatrixHUD_V3C hudInstance = null;

            // First try exact chart symbol, then leader symbol fallback.
            if (!NinjaTrader.NinjaScript.Indicators.RegimeMatrixHUD_V3C.InstancesV3C.TryGetValue(chartSymbol, out hudInstance))
                NinjaTrader.NinjaScript.Indicators.RegimeMatrixHUD_V3C.InstancesV3C.TryGetValue(leaderSymbol, out hudInstance);

            return hudInstance;
        }

        private string GetLeaderSymbol(string sym)
        {
            if (string.IsNullOrEmpty(sym)) return sym;

            sym = sym.Trim().ToUpper();

            if (sym == "MES") return "ES";
            if (sym == "MNQ") return "NQ";
            if (sym == "MGC") return "GC";
            if (sym == "MCL") return "CL";
            if (sym == "MSI") return "SI";

            return sym;
        }

        // ===== 2. Parameters (Properties) =====
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Contracts", GroupName = "1. Orders", Order = 0)]
        public int Contracts { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Exit on STOP X", GroupName = "1. Orders", Order = 1)]
        public bool ExitOnStopX { get; set; } = true;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "R Multiple (1.5 / 2 / 3)", GroupName = "2. Risk/Reward", Order = 0)]
        public double RMultiple { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ATR Length", GroupName = "2. Risk/Reward", Order = 1)]
        public int AtrLength { get; set; } = 14;

        [NinjaScriptProperty, Range(0.1, double.MaxValue)]
        [Display(Name = "ATR Stop Mult", GroupName = "2. Risk/Reward", Order = 2)]
        public double StopAtrMult { get; set; } = 1.0;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "DI Length (i_diLen)", GroupName = "3. ADX / DI", Order = 0)]
        public int DiLength { get; set; } = 14;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "ADX Smoothing (i_sigLen)", GroupName = "3. ADX / DI", Order = 1)]
        public int AdxSmoothing { get; set; } = 14;

        [NinjaScriptProperty, Range(1, double.MaxValue)]
        [Display(Name = "Level Range (i_hlRange)", GroupName = "3. ADX / DI", Order = 2)]
        public double LevelRange { get; set; } = 20.0;

        // ----- Trailing stop options -----

        [NinjaScriptProperty]
        [Display(Name = "Stop Mode", GroupName = "4. Stops", Order = 0)]
        public StopMode StopModeSelection { get; set; } = StopMode.AtrStatic;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Trail Ticks (TickTrailing)", GroupName = "4. Stops", Order = 1)]
        public int TrailTicks { get; set; } = 8;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Trail Start Bars", GroupName = "4. Stops", Order = 2)]
        public int TrailStartBars { get; set; } = 1;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "BarN N Bars", GroupName = "4. Stops", Order = 3)]
        public int TrailingNBars { get; set; } = 1;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "BarN Offset (ticks)", GroupName = "4. Stops", Order = 4)]
        public int TrailingOffsetTicks { get; set; } = 0;

        // ----- EMA 50 bias + no-trade zone -----

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Bias (Longs above / Shorts below)", GroupName = "5. EMA Filter", Order = 0)]
        public bool UseEmaBias { get; set; } = false;

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", GroupName = "5. EMA Filter", Order = 1)]
        public int EmaPeriod { get; set; } = 50;

        [NinjaScriptProperty]
        [Display(Name = "Use EMA No-Trade Zone", GroupName = "5. EMA Filter", Order = 2)]
        public bool UseEmaNoTradeZone { get; set; } = false;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "EMA Zone Width (ticks)", GroupName = "5. EMA Filter", Order = 3)]
        public int EmaZoneTicks { get; set; } = 8;

        // ----- VWAP-based no-trade zones -----

        [NinjaScriptProperty]
        [Display(Name = "Use Session VWAP No-Trade Zone", GroupName = "6. VWAP Zones", Order = 0)]
        public bool UseSessionVwapZone { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Use RTH VWAP No-Trade Zone", GroupName = "6. VWAP Zones", Order = 1)]
        public bool UseDailyVwapZone { get; set; } = false;

        [NinjaScriptProperty, Range(0, int.MaxValue)]
        [Display(Name = "VWAP Zone Width (ticks)", GroupName = "6. VWAP Zones", Order = 2)]
        public int VwapZoneTicks { get; set; } = 8;

        // ----- Manual price no-trade zone -----

        [NinjaScriptProperty]
        [Display(Name = "Use Manual Price No-Trade Zone", GroupName = "7. Manual Zone", Order = 0)]
        public bool UseManualZone { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Manual Zone Price 1", GroupName = "7. Manual Zone", Order = 1)]
        public double ManualZonePrice1 { get; set; } = 0.0;

        [NinjaScriptProperty]
        [Display(Name = "Manual Zone Price 2", GroupName = "7. Manual Zone", Order = 2)]
        public double ManualZonePrice2 { get; set; } = 0.0;

        // ===== 3. Internals (Series and Variables) =====
        private ATR atr;
        private EMA emaFilter;

        // Manual VWAP Calculation Variables (Session)
        private Series<double> sessionVWAP;
        private double cumPVSession = 0.0, cumVolSession = 0.0;
        
        // Manual VWAP Calculation Variables (RTH)
        private Series<double> rthVWAP;
        private double cumPVRth = 0.0, cumVolRth = 0.0;


        // Gu5-style internal DI/ADX (sig) components
        private Series<double> plusDM;
        private Series<double> minusDM;
        private Series<double> trSeries;
        private Series<double> diPlus;
        private Series<double> diMinus;
        private Series<double> sig;      // ADX-like "sig" from DI

        // Trailing Stop variables
        private double trailingStopLong = double.NaN;
        private double trailingStopShort = double.NaN;
        private int pendingLongStopTicks;
        private int pendingShortStopTicks;

        // ===== 4. Helper Methods =====
        private double RT(double price) =>
            Instrument.MasterInstrument.RoundToTickSize(price);

        private double BarNStopLong()
        {
            double lo = Low[0];
            for (int i = 1; i < TrailingNBars && i <= CurrentBar; i++)
                lo = Math.Min(lo, Low[i]);
            return RT(lo - TrailingOffsetTicks * TickSize);
        }

        private double BarNStopShort()
        {
            double hi = High[0];
            for (int i = 1; i < TrailingNBars && i <= CurrentBar; i++)
                hi = Math.Max(hi, High[i]);
            return RT(hi + TrailingOffsetTicks * TickSize);
        }

        // ===== 5. OnStateChange (Initialization) =====

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Pine_V3C";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;
                
                // FIX: Removing the problematic TradingHours assignment entirely. 
                // The strategy will now use the TradingHours inherited from the chart's data series.
                // TradingHours = TradingHours.GetTradingHours("CME US Index Futures RTH"); 

                ManualZonePrice1 = 0.0;
                ManualZonePrice2 = 0.0;
            }
            else if (State == State.DataLoaded)
            {
                atr        = ATR(AtrLength);
                emaFilter = EMA(EmaPeriod);

                // Initialize custom ADX/DI series
                plusDM    = new Series<double>(this);
                minusDM   = new Series<double>(this);
                trSeries  = new Series<double>(this);
                diPlus    = new Series<double>(this);
                diMinus   = new Series<double>(this);
                sig       = new Series<double>(this);
                
                // Manual VWAP Series Initialization
                sessionVWAP = new Series<double>(this);
                rthVWAP = new Series<double>(this);
                
                AddChartIndicator(emaFilter);
            }
        }

        // ===== 6. OnBarUpdate (Core Logic) =====

        protected override void OnBarUpdate()
        {
            // Basic guard
            if (CurrentBar < 1)
                return;
            
            bool isFirstBarOfSession = Bars.IsFirstBarOfSession;

            // ===== Manual Session VWAP Calculation (Relies on inherited TradingHours) =====
            if (isFirstBarOfSession)
            {
                // Reset accumulators at the start of a new session
                cumPVSession = 0.0;
                cumVolSession = 0.0;
            }
            double typSess = (High[0] + Low[0] + Close[0]) / 3.0;
            double volSess = Math.Max(1.0, Volume[0]);
            cumPVSession += typSess * volSess;
            cumVolSession += volSess;
            sessionVWAP[0] = (cumVolSession > 0 ? cumPVSession / cumVolSession : Close[0]);

            // ===== Manual RTH VWAP Calculation (Relies on inherited TradingHours) =====
            if (isFirstBarOfSession)
            {
                // Reset accumulators at the start of the RTH session
                cumPVRth = 0.0;
                cumVolRth = 0.0;
            }
            
            double typRth = (High[0] + Low[0] + Close[0]) / 3.0;
            double volRth = Math.Max(1.0, Volume[0]);
            cumPVRth += typRth * volRth;
            cumVolRth += volRth;
            rthVWAP[0] = (cumVolRth > 0 ? cumPVRth / cumVolRth : Close[0]);


            // ===== Gu5-style f_dirMov (DI) (ADX components) =====
            double up    = High[0] - High[1];
            double down = Low[1] - Low[0];

            double plusDMRaw  = (up    > down && up    > 0) ? up    : 0.0;
            double minusDMRaw = (down > up    && down > 0) ? down : 0.0;

            double trueRange = Math.Max(High[0] - Low[0],
                                 Math.Max(Math.Abs(High[0] - Close[1]),
                                          Math.Abs(Low[0]  - Close[1])));

            if (CurrentBar == 1)
            {
                plusDM[0]   = plusDMRaw;
                minusDM[0]  = minusDMRaw;
                trSeries[0] = trueRange;
            }
            else
            {
                plusDM[0]   = plusDM[1]   + (plusDMRaw  - plusDM[1])   / DiLength;
                minusDM[0]  = minusDM[1]  + (minusDMRaw - minusDM[1])  / DiLength;
                trSeries[0] = trSeries[1] + (trueRange  - trSeries[1]) / DiLength;
            }

            double trVal = trSeries[0];
            if (trVal.ApproxCompare(0.0) == 0)
                trVal = 1.0;

            diPlus[0]  = 100.0 * plusDM[0]  / trVal;
            diMinus[0] = 100.0 * minusDM[0] / trVal;

            // ===== Gu5-style f_sig (ADX-like "sig") =====
            double sum = diPlus[0] + diMinus[0];
            double dx  = (sum.ApproxCompare(0.0) == 0)
                         ? 0.0
                         : Math.Abs(diPlus[0] - diMinus[0]) / sum;

            double dxScaled = 100.0 * dx;

            if (CurrentBar == 1)
                sig[0] = dxScaled;
            else
                sig[0] = sig[1] + (dxScaled - sig[1]) / AdxSmoothing;

            if (CurrentBar < Math.Max(DiLength, AdxSmoothing) + 2)
                return;

            // ===== ATR-based stops/targets (baseline R:R) =====
            double atrVal = atr[0];
            if (atrVal <= 0)
                return;

            double stopTicksD   = StopAtrMult * atrVal / TickSize;
            int    stopTicks    = Math.Max(1, (int)Math.Round(stopTicksD));
            int    profitTicks  = Math.Max(1, (int)Math.Round(stopTicksD * RMultiple));

            // Baseline R:R exits 
            SetStopLoss("Long",  CalculationMode.Ticks, stopTicks,   false);
            SetProfitTarget("Long",  CalculationMode.Ticks, profitTicks);
            SetStopLoss("Short", CalculationMode.Ticks, stopTicks,   false);
            SetProfitTarget("Short", CalculationMode.Ticks, profitTicks);

            // ===== DI cross + ADX level (entry signals) =====
            bool longSignal  = diPlus[1] <= diMinus[1] && diPlus[0] > diMinus[0] && sig[0] > LevelRange;
            bool shortSignal = diPlus[1] >= diMinus[1] && diPlus[0] < diMinus[0] && sig[0] > LevelRange;

            // ===== STOP X conditions =====
            bool exitLongSignal  =
                (diPlus[1] >= diMinus[1] && diPlus[0] < diMinus[0]) ||
                (sig[0] <= LevelRange && sig[1] > LevelRange);

            bool exitShortSignal =
                (diPlus[1] <= diMinus[1] && diPlus[0] > diMinus[0]) ||
                (sig[0] <= LevelRange && sig[1] > LevelRange);

            // ===== EMA bias + EMA no-trade zone =====
            bool passEmaLong  = !UseEmaBias || Close[0] > emaFilter[0];
            bool passEmaShort = !UseEmaBias || Close[0] < emaFilter[0];

            double emaDist    = Math.Abs(Close[0] - emaFilter[0]);
            double emaZoneW   = EmaZoneTicks * TickSize;
            bool outsideEmaZone = !UseEmaNoTradeZone || emaDist > emaZoneW;

            // ===== VWAP no-trade zones (using MANUAL series) =====
            bool inVwapZone = false;
            double vwapWidth = VwapZoneTicks * TickSize;

            // Session VWAP Check
            if (UseSessionVwapZone && !double.IsNaN(sessionVWAP[0]))
            {
                double distSess = Math.Abs(Close[0] - sessionVWAP[0]);
                if (distSess <= vwapWidth)
                    inVwapZone = true;
            }

            // RTH VWAP Check
            if (UseDailyVwapZone && !double.IsNaN(rthVWAP[0]))
            {
                double distDaily = Math.Abs(Close[0] - rthVWAP[0]);
                if (distDaily <= vwapWidth)
                    inVwapZone = true;
            }

            bool outsideVwapZone = !inVwapZone;

            // ===== Manual price no-trade zone =====
            bool outsideManualZone = true;
            
            if (UseManualZone)
            {
                // Determine actual lower/upper bounds regardless of user input order
                double lower = Math.Min(ManualZonePrice1, ManualZonePrice2);
                double upper = Math.Max(ManualZonePrice1, ManualZonePrice2);
                
                // Only check if a valid range is set (upper must be greater than lower)
                if (upper.ApproxCompare(lower) > 0)
                {
                    if (Close[0] >= lower && Close[0] <= upper)
                        outsideManualZone = false;
                }
            }

            // =========================================================================
            // V3C GATEKEEPER & DIRECTIONAL ALIGNMENT (PINE MEAN REVERTER)
            // =========================================================================
            bool allowLongHud = true;
            bool allowShortHud = true;

            if (EnableTrinityFilter)
            {
                NinjaTrader.NinjaScript.Indicators.RegimeMatrixHUD_V3C hudInstance = GetV3CHud();

                if (hudInstance == null)
                {
                    // No V3C HUD found. Block for safety.
                    allowLongHud = false;
                    allowShortHud = false;
                }
                else
                {
                    string activePlaybook = hudInstance.FinalRegime ?? "UNKNOWN";
                    string macroRegime = hudInstance.MacroRegime ?? "UNKNOWN";
                    string hmmRegime = hudInstance.HMMMicro ?? "UNKNOWN";

                    // 1. V3C PLAYBOOK HARD BLOCKS:
                    // Pine needs structure. Block illiquid chop and transition.
                    if (activePlaybook == "ROTATION_ILLIQUID" || activePlaybook == "TRANSITION")
                    {
                        allowLongHud = false;
                        allowShortHud = false;
                    }

                    // 2. V3C direction permission from Python.
                    allowLongHud = allowLongHud && hudInstance.AllowLong;
                    allowShortHud = allowShortHud && hudInstance.AllowShort;

                    // 3. Preserve the original Pine counter-alignment behavior:
                    // If Macro or HMM is UP, only look for exhaustion shorts.
                    if (macroRegime.Contains("UP") || hmmRegime == "TrendUp")
                    {
                        allowLongHud = false;
                    }
                    // If Macro or HMM is DOWN, only look for exhaustion longs.
                    else if (macroRegime.Contains("DOWN") || hmmRegime == "TrendDown")
                    {
                        allowShortHud = false;
                    }
                }
            }
            // =========================================================================

            // ===== Combine filters for canLong / canShort =====
            bool canLong = IsBotAllowedByTrinity() && 
                allowLongHud &&
                IsTimeAllowed() &&
                longSignal &&
                passEmaLong &&
                outsideEmaZone &&
                outsideVwapZone &&
                outsideManualZone;

            bool canShort = IsBotAllowedByTrinity() && 
                allowShortHud &&
                IsTimeAllowed() &&
                shortSignal &&
                passEmaShort &&
                outsideEmaZone &&
                outsideVwapZone &&
                outsideManualZone;

            // ===== STOP X exits =====
            if (ExitOnStopX && Position.MarketPosition == MarketPosition.Long && exitLongSignal)
            {
                ExitLong(Contracts, "StopXLong", "Long");
                trailingStopLong = double.NaN;
                return;
            }

            if (ExitOnStopX && Position.MarketPosition == MarketPosition.Short && exitShortSignal)
            {
                ExitShort(Contracts, "StopXShort", "Short");
                trailingStopShort = double.NaN;
                return;
            }

            // ===== Entries (flat only) =====
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                trailingStopLong  = double.NaN;
                trailingStopShort = double.NaN;

                if (canLong)
                {
                    pendingLongStopTicks = stopTicks;
                    EnterLong(Contracts, "Long");
                }
                else if (canShort)
                {
                    pendingShortStopTicks = stopTicks;
                    EnterShort(Contracts, "Short");
                }
            }

            // ===== Trailing stop management (using updated RT() and NaN checks) =====

            // --- Long side ---
            if (Position.MarketPosition == MarketPosition.Long)
            {
                int bseLong = BarsSinceEntryExecution(0, "Long", 0);

                if (bseLong == 0)
                {
                    double baseStop = Position.AveragePrice - pendingLongStopTicks * TickSize;
                    trailingStopLong = baseStop;
                    SetStopLoss("Long", CalculationMode.Price, trailingStopLong, false);
                }

                if (StopModeSelection == StopMode.TickTrailing && bseLong >= TrailStartBars)
                {
                    double candidate = RT(Close[0] - TrailTicks * TickSize);
                    if (double.IsNaN(trailingStopLong) || candidate > trailingStopLong)
                        trailingStopLong = candidate;

                    SetStopLoss("Long", CalculationMode.Price, trailingStopLong, false);
                }
                else if (StopModeSelection == StopMode.BarNTrailing &&
                         bseLong >= TrailStartBars && CurrentBar >= TrailingNBars - 1)
                {
                    double candidate = BarNStopLong();
                    if (double.IsNaN(trailingStopLong) || candidate > trailingStopLong)
                        trailingStopLong = candidate;

                    SetStopLoss("Long", CalculationMode.Price, trailingStopLong, false);
                }
            }

            // --- Short side ---
            if (Position.MarketPosition == MarketPosition.Short)
            {
                int bseShort = BarsSinceEntryExecution(0, "Short", 0);

                if (bseShort == 0)
                {
                    double baseStop = Position.AveragePrice + pendingShortStopTicks * TickSize;
                    trailingStopShort = baseStop;
                    SetStopLoss("Short", CalculationMode.Price, trailingStopShort, false);
                }

                if (StopModeSelection == StopMode.TickTrailing && bseShort >= TrailStartBars)
                {
                    double candidate = RT(Close[0] + TrailTicks * TickSize);
                    if (double.IsNaN(trailingStopShort) || candidate < trailingStopShort)
                        trailingStopShort = candidate;

                    SetStopLoss("Short", CalculationMode.Price, trailingStopShort, false);
                }
                else if (StopModeSelection == StopMode.BarNTrailing &&
                         bseShort >= TrailStartBars && CurrentBar >= TrailingNBars - 1)
                {
                    double candidate = BarNStopShort();
                    if (double.IsNaN(trailingStopShort) || candidate < trailingStopShort)
                        trailingStopShort = candidate;

                    SetStopLoss("Short", CalculationMode.Price, trailingStopShort, false);
                }
            }
        }
    }
}