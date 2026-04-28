// CC BY-NC 4.0
#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;                 // Brushes
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools; // << REQUIRED for Draw.*
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{

    public class AdxDiCrossStrategy_OG : Strategy
    {
        public enum StopMode { AtrStatic = 0, EmaTrailing = 1, BarNTrailing = 2, AtrStep = 3 }
        public enum ZoneMode { Ticks, ATR }
        public enum InstrumentPreset { AutoDetect, ES, NQ, Custom }
        public enum AnchorMode { None, EMA, VWAP }
        public enum DrawdownMode { Ticks, Dollars }   // NEW

        // === Trading windows ===
        [NinjaScriptProperty] [Display(Name="Enable Tokyo", GroupName="Windows", Order=1)]
        public bool EnableTokyo { get; set; } = false;
        [NinjaScriptProperty, Range(0,235959)] [Display(Name="Tokyo Start (HHmmss)", GroupName="Windows", Order=2)]
        public int TokyoStart { get; set; } = 190000;
        [NinjaScriptProperty, Range(0,235959)] [Display(Name="Tokyo End (HHmmss)", GroupName="Windows", Order=3)]
        public int TokyoEnd { get; set; } = 030000;

        [NinjaScriptProperty] [Display(Name="Enable London", GroupName="Windows", Order=4)]
        public bool EnableLondon { get; set; } = false;
        [NinjaScriptProperty, Range(0,235959)] [Display(Name="London Start (HHmmss)", GroupName="Windows", Order=5)]
        public int LondonStart { get; set; } = 020000;
        [NinjaScriptProperty, Range(0,235959)] [Display(Name="London End (HHmmss)", GroupName="Windows", Order=6)]
        public int LondonEnd { get; set; } = 080000;

        [NinjaScriptProperty] [Display(Name="Enable US (Cash)", GroupName="Windows", Order=7)]
        public bool EnableUS { get; set; } = true;
        [NinjaScriptProperty, Range(0,235959)] [Display(Name="US Start (HHmmss)", GroupName="Windows", Order=8)]
        public int USStart { get; set; } = 093000;
        [NinjaScriptProperty, Range(0,235959)] [Display(Name="US End (HHmmss)", GroupName="Windows", Order=9)]
        public int USEnd { get; set; } = 160000;

        [NinjaScriptProperty] [Display(Name="Enable Custom", GroupName="Windows", Order=10)]
        public bool EnableCustom { get; set; } = false;
        [NinjaScriptProperty, Range(0,235959)] [Display(Name="Custom Start (HHmmss)", GroupName="Windows", Order=11)]
        public int CustomStart { get; set; } = 000000;
        [NinjaScriptProperty, Range(0,235959)] [Display(Name="Custom End (HHmmss)", GroupName="Windows", Order=12)]
        public int CustomEnd { get; set; } = 000000;

        // ===== Parameters =====
        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="Contracts", GroupName="Parameters", Order=1)]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty] [Display(Name="Use Stop X (ADX/DI exit)", GroupName="Parameters", Order=2)]
        public bool UseStopX { get; set; } = true;

        [NinjaScriptProperty, Range(0.1,double.MaxValue)] [Display(Name="Risk Reward (for targets)", GroupName="Parameters", Order=3)]
        public double RiskReward { get; set; } = 1.0;

        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="ADX Period", GroupName="Parameters", Order=4)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="Level Range (ADX min)", GroupName="Parameters", Order=5)]
        public double LevelRange { get; set; } = 20;

        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="ATR Period (zones & trails)", GroupName="Parameters", Order=6)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="ATR Multiplier (legacy)", GroupName="Parameters", Order=7)]
        public double AtrMultiplier { get; set; } = 1.0;

        // ===== Trade side selector =====
        [NinjaScriptProperty] [Display(Name="Trade Direction", GroupName="Filters - Bias", Order=8)]
        public TradeBias TradeDirection { get; set; } = TradeBias.Both;

        // ===== Direction filter (EMA) =====
        [NinjaScriptProperty] [Display(Name="Use EMA Direction Filter", GroupName="Filters - EMA Direction", Order=10)]
        public bool UseEmaDirectionFilter { get; set; } = false;
        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="EMA Filter Period", GroupName="Filters - EMA Direction", Order=11)]
        public int EmaFilterPeriod { get; set; } = 50;

        // ===== Anchor gating =====
        [NinjaScriptProperty] [Display(Name="Anchor Mode", GroupName="Filters - Anchor", Order=12)]
        public AnchorMode SideAnchor { get; set; } = AnchorMode.EMA;
        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="Anchor EMA Period", GroupName="Filters - Anchor", Order=13)]
        public int AnchorEmaPeriod { get; set; } = 50;
        [NinjaScriptProperty] [Display(Name="Require Longs Above Anchor", GroupName="Filters - Anchor", Order=14)]
        public bool RequireLongsAboveAnchor { get; set; } = true;
        [NinjaScriptProperty] [Display(Name="Require Shorts Below Anchor", GroupName="Filters - Anchor", Order=15)]
        public bool RequireShortsBelowAnchor { get; set; } = true;

        // ===== EMA no-trade zone =====
        [NinjaScriptProperty] [Display(Name="Use EMA No-Trade Zone", GroupName="Filters - EMA Zone", Order=20)]
        public bool UseEmaNoTradeZone { get; set; } = false;
        [NinjaScriptProperty] [Display(Name="Zone Mode", GroupName="Filters - EMA Zone", Order=21)]
        public ZoneMode EmaZoneMode { get; set; } = ZoneMode.Ticks;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="EMA Zone Width (ticks)", GroupName="Filters - EMA Zone", Order=22)]
        public int EmaZoneTicks { get; set; } = 8;
        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="EMA Zone Width (ATR mult)", GroupName="Filters - EMA Zone", Order=23)]
        public double EmaZoneAtrMult { get; set; } = 0.25;

        // ===== Presets =====
        [NinjaScriptProperty] [Display(Name="Instrument Preset", GroupName="Presets", Order=30)]
        public InstrumentPreset Preset { get; set; } = InstrumentPreset.AutoDetect;
        [NinjaScriptProperty] [Display(Name="Apply Preset Defaults", GroupName="Presets", Order=31)]
        public bool ApplyPresetDefaults { get; set; } = true;

        // ===== Stops =====
        [NinjaScriptProperty] [Display(Name="Stop Mode", GroupName="Stops", Order=40)]
        public StopMode StopModeSelection { get; set; } = StopMode.BarNTrailing;

        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="EMA Period (trail)", GroupName="Stops - EMA Trailing", Order=41)]
        public int EmaPeriod { get; set; } = 50;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="EMA Offset (ticks)", GroupName="Stops - EMA Trailing", Order=42)]
        public int EmaOffsetTicks { get; set; } = 0;
        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="EMA Switch N Bars (delay)", GroupName="Stops - EMA Trailing", Order=43)]
        public int EmaSwitchNBars { get; set; } = 2;

        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="Trailing N Bars", GroupName="Stops - BarN Trailing", Order=50)]
        public int TrailingNBars { get; set; } = 1;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Trailing Offset (ticks)", GroupName="Stops - BarN Trailing", Order=51)]
        public int TrailingOffsetTicks { get; set; } = 5;

        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="Step 1 trigger (ATR)", GroupName="Stops - ATR Step", Order=60)]
        public double Step1ATR { get; set; } = 0.50;
        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="Step 2 trigger (ATR)", GroupName="Stops - ATR Step", Order=61)]
        public double Step2ATR { get; set; } = 1.00;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="BE Plus (ticks)", GroupName="Stops - ATR Step", Order=62)]
        public int BreakevenPlusTicks { get; set; } = 5;
        [NinjaScriptProperty, Range(0.1,double.MaxValue)] [Display(Name="Trail ATR Mult", GroupName="Stops - ATR Step", Order=63)]
        public double TrailAtrMult { get; set; } = 1.0;

        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="ATR Stop Length", GroupName="Stops - ATR Static", Order=70)]
        public int AtrStopLen { get; set; } = 14;
        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="ATR Stop Multiplier", GroupName="Stops - ATR Static", Order=71)]
        public double AtrStopMult { get; set; } = 0.50;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Minimum Stop (ticks)", GroupName="Stops - ATR Static", Order=72)]
        public int MinStopTicks { get; set; } = 2;

        // ===== Risk Control =====
        [NinjaScriptProperty] [Display(Name="Use Fail-Safe Daily Loss", GroupName="Risk Control", Order=80)]
        public bool UseFailSafe { get; set; } = true;
        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="Fail-Safe Max Daily Loss ($)", GroupName="Risk Control", Order=81)]
        public double FailSafeMaxDailyLoss { get; set; } = 500.0;

        [NinjaScriptProperty] [Display(Name="Use Drawdown Trail", GroupName="Risk Control", Order=82)]
        public bool UseDrawdownTrail { get; set; } = false;
        [NinjaScriptProperty] [Display(Name="Drawdown Mode", GroupName="Risk Control", Order=83)]
        public DrawdownMode DDMode { get; set; } = DrawdownMode.Ticks;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Drawdown (ticks)", GroupName="Risk Control", Order=84)]
        public int DrawdownTicks { get; set; } = 40;
        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="Drawdown ($)", GroupName="Risk Control", Order=85)]
        public double DrawdownDollars { get; set; } = 0.0;

        // Visuals
        [NinjaScriptProperty] [Display(Name="Show Trail Lines", GroupName="Visuals", Order=90)]
        public bool ShowTrailLines { get; set; } = true;

        // ===== Internals =====
        private const string LEntry = "LE";
        private const string SEntry = "SE";

        private ADX adx;
        private ATR atr;
        private ATR atrStop;
        private EMA emaTrail;
        private EMA emaFilter;

        private EMA anchorEma;
        private Series<double> sessionVWAP;
        private double cumPV = 0.0, cumVol = 0.0;

        // DI math
        private Series<double> dmPlus, dmMinus, sumDmPlus, sumDmMinus, sumTr, diPlusSeries, diMinusSeries;

        // trailing stop & drawdown state
        private double trailingStopLong  = double.NaN;
        private double trailingStopShort = double.NaN;
        private double ddStopLong  = double.NaN;
        private double ddStopShort = double.NaN;
        private double peakHighSinceEntry = double.NaN;
        private double troughLowSinceEntry = double.NaN;

        // fail-safe
        private double pnlAtSessionStart = 0.0;
        private bool   failSafeLockout   = false;

        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);
        private bool WithinWindow(int t, int s, int e) => (s <= e) ? (t >= s && t <= e) : (t >= s || t <= e);
        private bool AnyWindowActive()
        {
            int t = ToTime(Time[0]);
            if (EnableTokyo  && WithinWindow(t, TokyoStart, TokyoEnd)) return true;
            if (EnableLondon && WithinWindow(t, LondonStart, LondonEnd)) return true;
            if (EnableUS     && WithinWindow(t, USStart, USEnd))         return true;
            if (EnableCustom && WithinWindow(t, CustomStart, CustomEnd)) return true;
            return false;
        }
        private double AnchorValue()
        {
            switch (SideAnchor)
            {
                case AnchorMode.EMA:  return anchorEma != null ? anchorEma[0] : Close[0];
                case AnchorMode.VWAP: return sessionVWAP != null ? sessionVWAP[0] : Close[0];
                default:              return Close[0];
            }
        }
        private double BarNStopLong()
        {
            double lo = Low[0];
            for (int i = 1; i < TrailingNBars; i++) lo = Math.Min(lo, Low[i]);
            return RT(lo - TickSize * TrailingOffsetTicks);
        }
        private double BarNStopShort()
        {
            double hi = High[0];
            for (int i = 1; i < TrailingNBars; i++) hi = Math.Max(hi, High[i]);
            return RT(hi + TickSize * TrailingOffsetTicks);
        }
        private double DollarsPerTick() => Instrument.MasterInstrument.PointValue * TickSize;
        private int CurrentDrawdownTicks()
        {
            if (!UseDrawdownTrail) return 0;
            if (DDMode == DrawdownMode.Ticks) return Math.Max(0, DrawdownTicks);
            if (DrawdownDollars <= 0) return 0;
            return (int)Math.Ceiling(DrawdownDollars / Math.Max(1e-9, DollarsPerTick()));
        }

        private void DrawTrailLines()
        {
            if (!ShowTrailLines) return;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (!double.IsNaN(trailingStopLong))
                    Draw.HorizontalLine(this, "TSL_LONG", trailingStopLong, Brushes.DodgerBlue);
                if (UseDrawdownTrail && !double.IsNaN(ddStopLong))
                    Draw.HorizontalLine(this, "DD_LONG", ddStopLong, Brushes.Goldenrod);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (!double.IsNaN(trailingStopShort))
                    Draw.HorizontalLine(this, "TSL_SHORT", trailingStopShort, Brushes.OrangeRed);
                if (UseDrawdownTrail && !double.IsNaN(ddStopShort))
                    Draw.HorizontalLine(this, "DD_SHORT", ddStopShort, Brushes.Goldenrod);
            }
            else
            {
                RemoveDrawObject("TSL_LONG");  RemoveDrawObject("DD_LONG");
                RemoveDrawObject("TSL_SHORT"); RemoveDrawObject("DD_SHORT");
            }
        }

        protected override void OnStateChange()
        {
			if (State == State.SetDefaults)
			{
			    Name = "AdxDiCrossStrategy_OG";
			    Calculate = Calculate.OnBarClose;   // <<< change this line
			    EntriesPerDirection = 1;
			    EntryHandling = EntryHandling.AllEntries;
			    IsExitOnSessionCloseStrategy = true;
			    ExitOnSessionCloseSeconds = 30;
			    RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
			    TraceOrders = true;
			}
            else if (State == State.DataLoaded)
            {
                adx = ADX(AdxPeriod);
                atr = ATR(AtrPeriod);
                atrStop = ATR(Math.Max(5, AtrStopLen));
                emaTrail  = EMA(EmaPeriod);
                emaFilter = EMA(EmaFilterPeriod);

                anchorEma   = EMA(AnchorEmaPeriod);
                sessionVWAP = new Series<double>(this);

                dmPlus       = new Series<double>(this);
                dmMinus      = new Series<double>(this);
                sumDmPlus    = new Series<double>(this);
                sumDmMinus   = new Series<double>(this);
                sumTr        = new Series<double>(this);
                diPlusSeries = new Series<double>(this);
                diMinusSeries= new Series<double>(this);

                AddChartIndicator(adx);
                AddChartIndicator(emaTrail);
                AddChartIndicator(emaFilter);
                if (SideAnchor == AnchorMode.EMA && anchorEma != null)
                    AddChartIndicator(anchorEma);

                if (AtrMultiplier > 0 && Math.Abs(AtrMultiplier - 1.0) > 1e-9)
                    AtrStopMult = AtrMultiplier;
            }
        }

        protected override void OnBarUpdate()
        {
            // prime session VWAP
            if (CurrentBar < 2)
            {
                if (Bars.IsFirstBarOfSession)
                {
                    cumPV = 0; cumVol = 0;
                    pnlAtSessionStart = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                    failSafeLockout = false;
                }
                double typ0 = (High[0] + Low[0] + Close[0]) / 3.0;
                double vol0 = Math.Max(1.0, Volume[0]);
                cumPV += typ0 * vol0; cumVol += vol0;
                sessionVWAP[0] = (cumVol > 0 ? cumPV / cumVol : Close[0]);
                return;
            }

            // session VWAP update + reset at session start
            if (Bars.IsFirstBarOfSession)
            {
                cumPV = 0.0; cumVol = 0.0;
                pnlAtSessionStart = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                failSafeLockout = false;
            }
            double typ = (High[0] + Low[0] + Close[0]) / 3.0;
            double vol = Math.Max(1.0, Volume[0]);
            cumPV  += typ * vol;  cumVol += vol;
            sessionVWAP[0] = (cumVol > 0 ? cumPV / cumVol : Close[0]);

            // Wilder DI math
            double h0 = High[0], l0 = Low[0];
            double h1 = High[1], l1 = Low[1], c1 = Close[1];

            double trRange = h0 - l0;
            double trUp    = Math.Abs(h0 - c1);
            double trDn    = Math.Abs(l0 - c1);
            double tr      = Math.Max(trRange, Math.Max(trUp, trDn));

            double upMove   = h0 - h1;
            double downMove = l1 - l0;

            dmPlus[0]  = (upMove   > 0 && upMove   > downMove) ? upMove   : 0;
            dmMinus[0] = (downMove > 0 && downMove > upMove)   ? downMove : 0;

            if (CurrentBar < AdxPeriod)
            {
                sumTr[0]      = sumTr[1] + tr;
                sumDmPlus[0]  = sumDmPlus[1] + dmPlus[0];
                sumDmMinus[0] = sumDmMinus[1] + dmMinus[0];
            }
            else
            {
                sumTr[0]      = sumTr[1]      - (sumTr[1]      / AdxPeriod) + tr;
                sumDmPlus[0]  = sumDmPlus[1]  - (sumDmPlus[1]  / AdxPeriod) + dmPlus[0];
                sumDmMinus[0] = sumDmMinus[1] - (sumDmMinus[1] / AdxPeriod) + dmMinus[0];
            }

            double sTr = sumTr[0].ApproxCompare(0) == 0 ? 1e-9 : sumTr[0];
            diPlusSeries[0]  = 100.0 * (sumDmPlus[0]  / sTr);
            diMinusSeries[0] = 100.0 * (sumDmMinus[0] / sTr);

            if (CurrentBar < Math.Max(AdxPeriod, Math.Max(AtrPeriod, AtrStopLen)) + 2)
                return;

            // ===== Fail-Safe (running PnL) =====
            if (UseFailSafe && !failSafeLockout)
            {
                double dayPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - pnlAtSessionStart;
                if (dayPnL <= -Math.Abs(FailSafeMaxDailyLoss))
                {
                    if (Position.MarketPosition == MarketPosition.Long)  ExitLong("FAILSAFE_FLAT", LEntry);
                    if (Position.MarketPosition == MarketPosition.Short) ExitShort("FAILSAFE_FLAT", SEntry);
                    failSafeLockout = true;
                }
            }
            if (failSafeLockout) { DrawTrailLines(); return; }

            // Signals
            bool adxStrong = adx[0] > LevelRange;
            bool crossUp = CrossAbove(diPlusSeries, diMinusSeries, 1);
            bool crossDn = CrossBelow(diPlusSeries, diMinusSeries, 1);

            bool windowActive = AnyWindowActive();

            bool allowLongBias  = TradeDirection == TradeBias.Both || TradeDirection == TradeBias.LongOnly;
            bool allowShortBias = TradeDirection == TradeBias.Both || TradeDirection == TradeBias.ShortOnly;

            bool passEmaLong  = !UseEmaDirectionFilter || Close[0] > emaFilter[0];
            bool passEmaShort = !UseEmaDirectionFilter || Close[0] < emaFilter[0];

            double zoneWidth = (EmaZoneMode == ZoneMode.Ticks) ? EmaZoneTicks * TickSize : EmaZoneAtrMult * atr[0];
            bool outsideEmaZone = !UseEmaNoTradeZone || Math.Abs(Close[0]-emaFilter[0]) >= zoneWidth;

            double anchorVal = AnchorValue();
            bool anchorLongOk  = (SideAnchor == AnchorMode.None) || !RequireLongsAboveAnchor  || Close[0] > anchorVal;
            bool anchorShortOk = (SideAnchor == AnchorMode.None) || !RequireShortsBelowAnchor || Close[0] < anchorVal;

            bool allowLong  = allowLongBias  && passEmaLong  && outsideEmaZone && anchorLongOk;
            bool allowShort = allowShortBias && passEmaShort && outsideEmaZone && anchorShortOk;

            // Flat: entries
            if (Position.MarketPosition == MarketPosition.Flat && windowActive)
            {
                trailingStopLong = trailingStopShort = double.NaN;
                ddStopLong = ddStopShort = double.NaN;
                peakHighSinceEntry = troughLowSinceEntry = double.NaN;

                if (allowLong && adxStrong && crossUp)
                    SubmitLongWithCatStops();
                else if (allowShort && adxStrong && crossDn)
                    SubmitShortWithCatStops();
            }

            // Manage LONG
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (UseStopX && (adx[0] < LevelRange || crossDn))
                    ExitLong("StopX", LEntry);

                int bse = BarsSinceEntryExecution(0, LEntry, 0);

                switch (StopModeSelection)
                {
                    case StopMode.BarNTrailing:
                        if (bse != -1 && bse >= Math.Max(1, TrailingNBars))
                        {
                            double proposed = BarNStopLong();
                            trailingStopLong = double.IsNaN(trailingStopLong) ? proposed : Math.Max(trailingStopLong, proposed);
                        }
                        break;
                    case StopMode.EmaTrailing:
                        if (bse != -1 && bse >= Math.Max(1, EmaSwitchNBars))
                        {
                            double emaStp = RT(emaTrail[0] - EmaOffsetTicks * TickSize);
                            trailingStopLong = double.IsNaN(trailingStopLong) ? emaStp : Math.Max(trailingStopLong, emaStp);
                        }
                        break;
                    case StopMode.AtrStep:
                    {
                        double move  = Close[0] - Position.AveragePrice;
                        double step1 = Step1ATR * atr[0];
                        double step2 = Step2ATR * atr[0];

                        if (move >= step2)
                        {
                            double atrTrail = RT(Close[0] - TrailAtrMult * atr[0]);
                            trailingStopLong = double.IsNaN(trailingStopLong) ? atrTrail : Math.Max(trailingStopLong, atrTrail);
                        }
                        else if (move >= step1)
                        {
                            double be = RT(Position.AveragePrice + BreakevenPlusTicks * TickSize);
                            trailingStopLong = double.IsNaN(trailingStopLong) ? be : Math.Max(trailingStopLong, be);
                        }
                        }
                        break;
                    case StopMode.AtrStatic:
                    default:
                        break;
                }

                // Drawdown trail for LONGS
                int ddTicks = CurrentDrawdownTicks();
                if (ddTicks > 0)
                {
                    peakHighSinceEntry = double.IsNaN(peakHighSinceEntry) ? High[0] : Math.Max(peakHighSinceEntry, High[0]);
                    ddStopLong = RT(peakHighSinceEntry - ddTicks * TickSize);
                }

                double finalLongStop = trailingStopLong;
                if (!double.IsNaN(ddStopLong))
                    finalLongStop = double.IsNaN(finalLongStop) ? ddStopLong : Math.Max(finalLongStop, ddStopLong);

                if (!double.IsNaN(finalLongStop))
                    SetStopLoss(LEntry, CalculationMode.Price, finalLongStop, false);

                DrawTrailLines();
            }

            // Manage SHORT
            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (UseStopX && (adx[0] < LevelRange || crossUp))
                    ExitShort("StopX", SEntry);

                int bse = BarsSinceEntryExecution(0, SEntry, 0);

                switch (StopModeSelection)
                {
                    case StopMode.BarNTrailing:
                        if (bse != -1 && bse >= Math.Max(1, TrailingNBars))
                        {
                            double proposed = BarNStopShort();
                            trailingStopShort = double.IsNaN(trailingStopShort) ? proposed : Math.Min(trailingStopShort, proposed);
                        }
                        break;
                    case StopMode.EmaTrailing:
                        if (bse != -1 && bse >= Math.Max(1, EmaSwitchNBars))
                        {
                            double emaStp = RT(emaTrail[0] + EmaOffsetTicks * TickSize);
                            trailingStopShort = double.IsNaN(trailingStopShort) ? emaStp : Math.Min(trailingStopShort, emaStp);
                        }
                        break;
                    case StopMode.AtrStep:
                    {
                        double move  = Position.AveragePrice - Close[0];
                        double step1 = Step1ATR * atr[0];
                        double step2 = Step2ATR * atr[0];

                        if (move >= step2)
                        {
                            double atrTrail = RT(Close[0] + TrailAtrMult * atr[0]);
                            trailingStopShort = double.IsNaN(trailingStopShort) ? atrTrail : Math.Min(trailingStopShort, atrTrail);
                        }
                        else if (move >= step1)
                        {
                            double be = RT(Position.AveragePrice - BreakevenPlusTicks * TickSize);
                            trailingStopShort = double.IsNaN(trailingStopShort) ? be : Math.Min(trailingStopShort, be);
                        }
                        }
                        break;
                    case StopMode.AtrStatic:
                    default:
                        break;
                }

                // Drawdown trail for SHORTS
                int ddTicks = CurrentDrawdownTicks();
                if (ddTicks > 0)
                {
                    troughLowSinceEntry = double.IsNaN(troughLowSinceEntry) ? Low[0] : Math.Min(troughLowSinceEntry, Low[0]);
                    ddStopShort = RT(troughLowSinceEntry + ddTicks * TickSize);
                }

                double finalShortStop = trailingStopShort;
                if (!double.IsNaN(ddStopShort))
                    finalShortStop = double.IsNaN(finalShortStop) ? ddStopShort : Math.Min(finalShortStop, ddStopShort);

                if (!double.IsNaN(finalShortStop))
                    SetStopLoss(SEntry, CalculationMode.Price, finalShortStop, false);

                DrawTrailLines();
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                peakHighSinceEntry = troughLowSinceEntry = double.NaN;
                ddStopLong = ddStopShort = double.NaN;
                DrawTrailLines();
            }
        }

        // ===== Entry helpers =====
        private void SubmitLongWithCatStops()
        {
            double rawRisk = Math.Max(0.01, AtrStopMult) * atrStop[0];
            double minRisk = Math.Max(1, MinStopTicks) * TickSize;
            double risk    = Math.Max(rawRisk, minRisk);

            double stp  = RT(Close[0] - risk);
            double tgt  = RT(Close[0] + risk * Math.Max(0.1, RiskReward));

            SetStopLoss    (LEntry, CalculationMode.Price, stp, false);
            SetProfitTarget(LEntry, CalculationMode.Price, tgt);

            trailingStopLong = stp;
            ddStopLong = double.NaN;
            peakHighSinceEntry = Close[0];

            EnterLong(Contracts, LEntry);
        }

        private void SubmitShortWithCatStops()
        {
            double rawRisk = Math.Max(0.01, AtrStopMult) * atrStop[0];
            double minRisk = Math.Max(1, MinStopTicks) * TickSize;
            double risk    = Math.Max(rawRisk, minRisk);

            double stp  = RT(Close[0] + risk);
            double tgt  = RT(Close[0] - risk * Math.Max(0.1, RiskReward));

            SetStopLoss    (SEntry, CalculationMode.Price, stp, false);
            SetProfitTarget(SEntry, CalculationMode.Price, tgt);

            trailingStopShort = stp;
            ddStopShort = double.NaN;
            troughLowSinceEntry = Close[0];

            EnterShort(Contracts, SEntry);
        }
    }
}
