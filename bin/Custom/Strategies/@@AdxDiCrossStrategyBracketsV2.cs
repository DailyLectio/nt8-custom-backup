// CC BY-NC 4.0
#region Using
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class AdxDiCrossStrategy_Brackets_V2 : Strategy
    {
        // ===== Enums =====
        public enum TradeBias { Both, LongOnly, ShortOnly }
        public enum AnchorMode { None, EMA, VWAP }
        public enum ZoneMode { Ticks, ATR }

        public enum TargetCalcMode { AtrMult = 0, Ticks = 1, Percent = 2, CustomLevel = 3 }
        public enum StopCalcMode   { AtrStatic = 0, Ticks = 1, Percent = 2, PriorHLPlusTicks = 3, BarNTrailing = 4, EmaTrailing = 5 }

        public enum CustomTarget
        {
            None = 0,
            B2   = 1,
            Bull1 = 2, Bull2 = 3, Bull3 = 4, Bull4 = 5,
            R2   = 6,
            Bear1 = 7, Bear2 = 8, Bear3 = 9, Bear4 = 10,
            POC   = 11
        }

        // ===== Session windows =====
        [NinjaScriptProperty] [Display(Name="Enable US (Cash)", GroupName="Windows", Order=1)]
        public bool EnableUS { get; set; } = true;

        [NinjaScriptProperty, Range(0,235959)] [Display(Name="US Start (HHmmss)", GroupName="Windows", Order=2)]
        public int USStart { get; set; } = 093000;

        [NinjaScriptProperty, Range(0,235959)] [Display(Name="US End (HHmmss)", GroupName="Windows", Order=3)]
        public int USEnd { get; set; } = 160000;

        [NinjaScriptProperty] [Display(Name="Enable Custom", GroupName="Windows", Order=4)]
        public bool EnableCustom { get; set; } = false;

        [NinjaScriptProperty, Range(0,235959)] [Display(Name="Custom Start (HHmmss)", GroupName="Windows", Order=5)]
        public int CustomStart { get; set; } = 000000;

        [NinjaScriptProperty, Range(0,235959)] [Display(Name="Custom End (HHmmss)", GroupName="Windows", Order=6)]
        public int CustomEnd { get; set; } = 000000;

        // ===== Core signal inputs =====
        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="ADX Period", GroupName="Signal", Order=10)]
        public int AdxPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0, double.MaxValue)] [Display(Name="ADX Min (gate)", GroupName="Signal", Order=11)]
        public double AdxGate { get; set; } = 20;

        [NinjaScriptProperty] [Display(Name="Trade Direction", GroupName="Signal", Order=12)]
        public TradeBias Bias { get; set; } = TradeBias.Both;

        // EMA direction + no-trade zone
        [NinjaScriptProperty] [Display(Name="Use EMA Direction Filter", GroupName="EMA Filter", Order=20)]
        public bool UseEmaFilter { get; set; } = false;

        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="EMA Filter Period", GroupName="EMA Filter", Order=21)]
        public int EmaFilterPeriod { get; set; } = 50;

        [NinjaScriptProperty] [Display(Name="Use EMA No-Trade Zone", GroupName="EMA Filter", Order=22)]
        public bool UseEmaZone { get; set; } = false;

        [NinjaScriptProperty] [Display(Name="Zone Mode", GroupName="EMA Filter", Order=23)]
        public ZoneMode ZoneWidthMode { get; set; } = ZoneMode.Ticks;

        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Zone Width (ticks)", GroupName="EMA Filter", Order=24)]
        public int ZoneWidthTicks { get; set; } = 8;

        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="Zone Width (ATR mult)", GroupName="EMA Filter", Order=25)]
        public double ZoneWidthAtr { get; set; } = 0.25;

        // Anchor gating
        [NinjaScriptProperty] [Display(Name="Anchor Mode", GroupName="Anchor", Order=30)]
        public AnchorMode SideAnchor { get; set; } = AnchorMode.EMA;

        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="Anchor EMA Period", GroupName="Anchor", Order=31)]
        public int AnchorEmaPeriod { get; set; } = 50;

        [NinjaScriptProperty] [Display(Name="Require Longs Above Anchor", GroupName="Anchor", Order=32)]
        public bool RequireLongsAbove { get; set; } = true;

        [NinjaScriptProperty] [Display(Name="Require Shorts Below Anchor", GroupName="Anchor", Order=33)]
        public bool RequireShortsBelow { get; set; } = true;

        // ===== Manual daily custom levels (set each day) =====
        [NinjaScriptProperty] [Display(Name="B2", GroupName="Manual Levels", Order=40)]  public double Lvl_B2 { get; set; } = 0;
        [NinjaScriptProperty] [Display(Name="Bull #1", GroupName="Manual Levels", Order=41)] public double Lvl_Bull1 { get; set; } = 0;
        [NinjaScriptProperty] [Display(Name="Bull #2", GroupName="Manual Levels", Order=42)] public double Lvl_Bull2 { get; set; } = 0;
        [NinjaScriptProperty] [Display(Name="Bull #3", GroupName="Manual Levels", Order=43)] public double Lvl_Bull3 { get; set; } = 0;
        [NinjaScriptProperty] [Display(Name="Bull #4", GroupName="Manual Levels", Order=44)] public double Lvl_Bull4 { get; set; } = 0;

        [NinjaScriptProperty] [Display(Name="R2", GroupName="Manual Levels", Order=45)]  public double Lvl_R2 { get; set; } = 0;
        [NinjaScriptProperty] [Display(Name="Bear #1", GroupName="Manual Levels", Order=46)] public double Lvl_Bear1 { get; set; } = 0;
        [NinjaScriptProperty] [Display(Name="Bear #2", GroupName="Manual Levels", Order=47)] public double Lvl_Bear2 { get; set; } = 0;
        [NinjaScriptProperty] [Display(Name="Bear #3", GroupName="Manual Levels", Order=48)] public double Lvl_Bear3 { get; set; } = 0;
        [NinjaScriptProperty] [Display(Name="Bear #4", GroupName="Manual Levels", Order=49)] public double Lvl_Bear4 { get; set; } = 0;

        [NinjaScriptProperty] [Display(Name="POC", GroupName="Manual Levels", Order=50)]  public double Lvl_POC { get; set; } = 0;

        // ===== Global ATR inputs =====
        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="ATR Period", GroupName="Risk/ATR", Order=60)]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(0.1,double.MaxValue)] [Display(Name="Default ATR Mult", GroupName="Risk/ATR", Order=61)]
        public double AtrMultDefault { get; set; } = 1.0;

        // ===== Per-leg trade brackets (Qty=0 disables the leg) =====
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg1 Qty", GroupName="Bracket - Leg1", Order=100)]
        public int Leg1Qty { get; set; } = 1;
        [NinjaScriptProperty] [Display(Name="Leg1 Target Mode", GroupName="Bracket - Leg1", Order=101)]
        public TargetCalcMode Leg1TargetMode { get; set; } = TargetCalcMode.AtrMult;
        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="Leg1 Target (ATR/Percent)", GroupName="Bracket - Leg1", Order=102)]
        public double Leg1TargetParam { get; set; } = 1.0; // ATR mult or percent (0.02 = 2%)
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg1 Target Ticks", GroupName="Bracket - Leg1", Order=103)]
        public int Leg1TargetTicks { get; set; } = 8;
        [NinjaScriptProperty] [Display(Name="Leg1 Custom Target", GroupName="Bracket - Leg1", Order=104)]
        public CustomTarget Leg1CustomTarget { get; set; } = CustomTarget.None;

        [NinjaScriptProperty] [Display(Name="Leg1 Stop Mode", GroupName="Bracket - Leg1", Order=110)]
        public StopCalcMode Leg1StopMode { get; set; } = StopCalcMode.BarNTrailing;
        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="Leg1 Stop (ATR/Percent)", GroupName="Bracket - Leg1", Order=111)]
        public double Leg1StopParam { get; set; } = 0.5; // ATR mult or percent
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg1 Stop Ticks", GroupName="Bracket - Leg1", Order=112)]
        public int Leg1StopTicks { get; set; } = 6;
        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="Leg1 BarN", GroupName="Bracket - Leg1", Order=113)]
        public int Leg1BarN { get; set; } = 1;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg1 BarN Offset (ticks)", GroupName="Bracket - Leg1", Order=114)]
        public int Leg1BarNOffset { get; set; } = 4;
        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="Leg1 EMA Trail Period", GroupName="Bracket - Leg1", Order=115)]
        public int Leg1EmaPeriod { get; set; } = 50;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg1 EMA Offset (ticks)", GroupName="Bracket - Leg1", Order=116)]
        public int Leg1EmaOffset { get; set; } = 0;

        // ---- Duplicate the same shape for legs 2–4 (quick defaults) ----
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg2 Qty", GroupName="Bracket - Leg2", Order=120)] public int Leg2Qty { get; set; } = 1;
        [NinjaScriptProperty] [Display(Name="Leg2 Target Mode", GroupName="Bracket - Leg2", Order=121)] public TargetCalcMode Leg2TargetMode { get; set; } = TargetCalcMode.CustomLevel;
        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="Leg2 Target (ATR/Percent)", GroupName="Bracket - Leg2", Order=122)] public double Leg2TargetParam { get; set; } = 1.0;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg2 Target Ticks", GroupName="Bracket - Leg2", Order=123)] public int Leg2TargetTicks { get; set; } = 12;
        [NinjaScriptProperty] [Display(Name="Leg2 Custom Target", GroupName="Bracket - Leg2", Order=124)] public CustomTarget Leg2CustomTarget { get; set; } = CustomTarget.Bull1;

        [NinjaScriptProperty] [Display(Name="Leg2 Stop Mode", GroupName="Bracket - Leg2", Order=130)] public StopCalcMode Leg2StopMode { get; set; } = StopCalcMode.BarNTrailing;
        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="Leg2 Stop (ATR/Percent)", GroupName="Bracket - Leg2", Order=131)] public double Leg2StopParam { get; set; } = 0.5;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg2 Stop Ticks", GroupName="Bracket - Leg2", Order=132)] public int Leg2StopTicks { get; set; } = 8;
        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="Leg2 BarN", GroupName="Bracket - Leg2", Order=133)] public int Leg2BarN { get; set; } = 1;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg2 BarN Offset (ticks)", GroupName="Bracket - Leg2", Order=134)] public int Leg2BarNOffset { get; set; } = 4;
        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="Leg2 EMA Trail Period", GroupName="Bracket - Leg2", Order=135)] public int Leg2EmaPeriod { get; set; } = 50;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg2 EMA Offset (ticks)", GroupName="Bracket - Leg2", Order=136)] public int Leg2EmaOffset { get; set; } = 0;

        // Legs 3–4 (leave Qtys at 0 if unused)
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg3 Qty", GroupName="Bracket - Leg3", Order=140)] public int Leg3Qty { get; set; } = 0;
        [NinjaScriptProperty] [Display(Name="Leg3 Target Mode", GroupName="Bracket - Leg3", Order=141)] public TargetCalcMode Leg3TargetMode { get; set; } = TargetCalcMode.AtrMult;
        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="Leg3 Target (ATR/Percent)", GroupName="Bracket - Leg3", Order=142)] public double Leg3TargetParam { get; set; } = 1.5;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg3 Target Ticks", GroupName="Bracket - Leg3", Order=143)] public int Leg3TargetTicks { get; set; } = 16;
        [NinjaScriptProperty] [Display(Name="Leg3 Custom Target", GroupName="Bracket - Leg3", Order=144)] public CustomTarget Leg3CustomTarget { get; set; } = CustomTarget.POC;
        [NinjaScriptProperty] [Display(Name="Leg3 Stop Mode", GroupName="Bracket - Leg3", Order=150)] public StopCalcMode Leg3StopMode { get; set; } = StopCalcMode.EmaTrailing;
        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="Leg3 Stop (ATR/Percent)", GroupName="Bracket - Leg3", Order=151)] public double Leg3StopParam { get; set; } = 0.6;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg3 Stop Ticks", GroupName="Bracket - Leg3", Order=152)] public int Leg3StopTicks { get; set; } = 10;
        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="Leg3 BarN", GroupName="Bracket - Leg3", Order=153)] public int Leg3BarN { get; set; } = 2;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg3 BarN Offset (ticks)", GroupName="Bracket - Leg3", Order=154)] public int Leg3BarNOffset { get; set; } = 4;
        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="Leg3 EMA Trail Period", GroupName="Bracket - Leg3", Order=155)] public int Leg3EmaPeriod { get; set; } = 50;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg3 EMA Offset (ticks)", GroupName="Bracket - Leg3", Order=156)] public int Leg3EmaOffset { get; set; } = 0;

        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg4 Qty", GroupName="Bracket - Leg4", Order=160)] public int Leg4Qty { get; set; } = 0;
        [NinjaScriptProperty] [Display(Name="Leg4 Target Mode", GroupName="Bracket - Leg4", Order=161)] public TargetCalcMode Leg4TargetMode { get; set; } = TargetCalcMode.CustomLevel;
        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="Leg4 Target (ATR/Percent)", GroupName="Bracket - Leg4", Order=162)] public double Leg4TargetParam { get; set; } = 2.0;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg4 Target Ticks", GroupName="Bracket - Leg4", Order=163)] public int Leg4TargetTicks { get; set; } = 20;
        [NinjaScriptProperty] [Display(Name="Leg4 Custom Target", GroupName="Bracket - Leg4", Order=164)] public CustomTarget Leg4CustomTarget { get; set; } = CustomTarget.Bear2;
        [NinjaScriptProperty] [Display(Name="Leg4 Stop Mode", GroupName="Bracket - Leg4", Order=170)] public StopCalcMode Leg4StopMode { get; set; } = StopCalcMode.BarNTrailing;
        [NinjaScriptProperty, Range(0.0,double.MaxValue)] [Display(Name="Leg4 Stop (ATR/Percent)", GroupName="Bracket - Leg4", Order=171)] public double Leg4StopParam { get; set; } = 0.7;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg4 Stop Ticks", GroupName="Bracket - Leg4", Order=172)] public int Leg4StopTicks { get; set; } = 12;
        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="Leg4 BarN", GroupName="Bracket - Leg4", Order=173)] public int Leg4BarN { get; set; } = 2;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg4 BarN Offset (ticks)", GroupName="Bracket - Leg4", Order=174)] public int Leg4BarNOffset { get; set; } = 5;
        [NinjaScriptProperty, Range(1,int.MaxValue)] [Display(Name="Leg4 EMA Trail Period", GroupName="Bracket - Leg4", Order=175)] public int Leg4EmaPeriod { get; set; } = 50;
        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Leg4 EMA Offset (ticks)", GroupName="Bracket - Leg4", Order=176)] public int Leg4EmaOffset { get; set; } = 0;

        // ===== Daily trading limits =====
        [NinjaScriptProperty] [Display(Name="Use Trading Limits", GroupName="Trading Limits", Order=200)]
        public bool UseLimits { get; set; } = false;

        [NinjaScriptProperty] [Display(Name="Daily Profit Target ($)", GroupName="Trading Limits", Order=201)]
        public double DayProfitTarget { get; set; } = 0;

        [NinjaScriptProperty] [Display(Name="Daily Loss Limit ($)", GroupName="Trading Limits", Order=202)]
        public double DayLossLimit { get; set; } = 0;

        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Max Winners", GroupName="Trading Limits", Order=203)]
        public int MaxWinners { get; set; } = 0;   // 0 = ignore

        [NinjaScriptProperty, Range(0,int.MaxValue)] [Display(Name="Max Losers", GroupName="Trading Limits", Order=204)]
        public int MaxLosers { get; set; } = 0;    // 0 = ignore

        // ===== Internals =====
        private ADX adx; private ATR atr;
        private EMA emaFilter, anchorEma;
        private Series<double> sessionVWAP; private double cumPV, cumVol;

        private Series<double> dmP, dmM, sumDmP, sumDmM, sumTr, diP, diM;

        private struct LegCfg
        {
            public string Signal;
            public int Qty;
            public TargetCalcMode TMode;
            public double TParam;
            public int TicksT;
            public CustomTarget CustomT;
            public StopCalcMode SMode;
            public double SParam;
            public int TicksS;
            public int BarN;
            public int BarNOffset;
            public int EmaLen;
            public int EmaOffset;
        }

        private LegCfg[] legs;
        private Dictionary<string,double> trailBySignal = new Dictionary<string,double>();
        private EMA emaLeg3, emaLeg4, emaLeg1, emaLeg2; // for per-leg EMA trails if used

        private int winnersToday = 0, losersToday = 0;
        private double pnlAtSessionStart = 0;

        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        private bool InWindow()
        {
            int t = ToTime(Time[0]);
            bool us = EnableUS && ((USStart <= USEnd) ? (t >= USStart && t <= USEnd) : (t >= USStart || t <= USEnd));
            bool custom = EnableCustom && ((CustomStart <= CustomEnd) ? (t >= CustomStart && t <= CustomEnd) : (t >= CustomStart || t <= CustomEnd));
            return us || custom;
        }

        private double AnchorVal()
        {
            switch (SideAnchor)
            {
                case AnchorMode.EMA:  return anchorEma != null ? anchorEma[0] : Close[0];
                case AnchorMode.VWAP: return sessionVWAP != null ? sessionVWAP[0] : Close[0];
                default: return Close[0];
            }
        }

        private double LevelOf(CustomTarget t)
        {
            switch (t)
            {
                case CustomTarget.B2:    return Lvl_B2;
                case CustomTarget.Bull1: return Lvl_Bull1;
                case CustomTarget.Bull2: return Lvl_Bull2;
                case CustomTarget.Bull3: return Lvl_Bull3;
                case CustomTarget.Bull4: return Lvl_Bull4;
                case CustomTarget.R2:    return Lvl_R2;
                case CustomTarget.Bear1: return Lvl_Bear1;
                case CustomTarget.Bear2: return Lvl_Bear2;
                case CustomTarget.Bear3: return Lvl_Bear3;
                case CustomTarget.Bear4: return Lvl_Bear4;
                case CustomTarget.POC:   return Lvl_POC;
                default: return 0;
            }
        }

        private double BarNStopLong(int n, int offsetTicks)
        {
            double lo = Low[0];
            for (int i = 1; i < Math.Max(1,n); i++) lo = Math.Min(lo, Low[i]);
            return RT(lo - TickSize * offsetTicks);
        }
        private double BarNStopShort(int n, int offsetTicks)
        {
            double hi = High[0];
            for (int i = 1; i < Math.Max(1,n); i++) hi = Math.Max(hi, High[i]);
            return RT(hi + TickSize * offsetTicks);
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AdxDiCrossStrategy_Brackets_V2";
                Calculate = Calculate.OnPriceChange;
                EntriesPerDirection = 4;        // one per leg
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                TraceOrders = true;
            }
            else if (State == State.DataLoaded)
            {
                adx = ADX(AdxPeriod);
                atr = ATR(AtrPeriod);
                emaFilter = EMA(EmaFilterPeriod);
                anchorEma = EMA(AnchorEmaPeriod);

                emaLeg1 = EMA(Leg1EmaPeriod);
                emaLeg2 = EMA(Leg2EmaPeriod);
                emaLeg3 = EMA(Leg3EmaPeriod);
                emaLeg4 = EMA(Leg4EmaPeriod);

                AddChartIndicator(adx);
                AddChartIndicator(emaFilter);
                if (SideAnchor == AnchorMode.EMA) AddChartIndicator(anchorEma);

                // DI internals
                dmP = new Series<double>(this); dmM = new Series<double>(this);
                sumDmP = new Series<double>(this); sumDmM = new Series<double>(this);
                sumTr = new Series<double>(this); diP = new Series<double>(this); diM = new Series<double>(this);

                sessionVWAP = new Series<double>(this);
                cumPV = cumVol = 0;

                // Leg config array in fixed order 1..4
                legs = new[]
                {
                    new LegCfg{ Signal="L1", Qty=Leg1Qty, TMode=Leg1TargetMode, TParam=Leg1TargetParam, TicksT=Leg1TargetTicks, CustomT=Leg1CustomTarget,
                                SMode=Leg1StopMode, SParam=Leg1StopParam, TicksS=Leg1StopTicks, BarN=Leg1BarN, BarNOffset=Leg1BarNOffset, EmaLen=Leg1EmaPeriod, EmaOffset=Leg1EmaOffset },
                    new LegCfg{ Signal="L2", Qty=Leg2Qty, TMode=Leg2TargetMode, TParam=Leg2TargetParam, TicksT=Leg2TargetTicks, CustomT=Leg2CustomTarget,
                                SMode=Leg2StopMode, SParam=Leg2StopParam, TicksS=Leg2StopTicks, BarN=Leg2BarN, BarNOffset=Leg2BarNOffset, EmaLen=Leg2EmaPeriod, EmaOffset=Leg2EmaOffset },
                    new LegCfg{ Signal="L3", Qty=Leg3Qty, TMode=Leg3TargetMode, TParam=Leg3TargetParam, TicksT=Leg3TargetTicks, CustomT=Leg3CustomTarget,
                                SMode=Leg3StopMode, SParam=Leg3StopParam, TicksS=Leg3StopTicks, BarN=Leg3BarN, BarNOffset=Leg3BarNOffset, EmaLen=Leg3EmaPeriod, EmaOffset=Leg3EmaOffset },
                    new LegCfg{ Signal="L4", Qty=Leg4Qty, TMode=Leg4TargetMode, TParam=Leg4TargetParam, TicksT=Leg4TargetTicks, CustomT=Leg4CustomTarget,
                                SMode=Leg4StopMode, SParam=Leg4StopParam, TicksS=Leg4StopTicks, BarN=Leg4BarN, BarNOffset=Leg4BarNOffset, EmaLen=Leg4EmaPeriod, EmaOffset=Leg4EmaOffset },
                };
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2) { primeVWAP(); return; }
            updateVWAP();

            // Wilder DI math (rolled)
            double tr = Math.Max(High[0]-Low[0], Math.Max(Math.Abs(High[0]-Close[1]), Math.Abs(Low[0]-Close[1])));
            double up = High[0]-High[1];
            double dn = Low[1]-Low[0];
            dmP[0] = (up > 0 && up > dn) ? up : 0;
            dmM[0] = (dn > 0 && dn > up) ? dn : 0;

            if (CurrentBar < AdxPeriod)
            {
                sumTr[0]   = (CurrentBar == 0 ? tr : sumTr[1] + tr);
                sumDmP[0]  = (CurrentBar == 0 ? dmP[0] : sumDmP[1] + dmP[0]);
                sumDmM[0]  = (CurrentBar == 0 ? dmM[0] : sumDmM[1] + dmM[0]);
            }
            else
            {
                sumTr[0]   = sumTr[1]   - (sumTr[1]   / AdxPeriod) + tr;
                sumDmP[0]  = sumDmP[1]  - (sumDmP[1]  / AdxPeriod) + dmP[0];
                sumDmM[0]  = sumDmM[1]  - (sumDmM[1]  / AdxPeriod) + dmM[0];
            }

            double denom = sumTr[0].ApproxCompare(0) == 0 ? 1e-9 : sumTr[0];
            diP[0] = 100.0 * (sumDmP[0] / denom);
            diM[0] = 100.0 * (sumDmM[0] / denom);

            if (CurrentBar < Math.Max(AdxPeriod, AtrPeriod) + 2) return;

            // Trading limits gate (pre-entry)
            if (UseLimits && limitsHit()) return;

            bool strong = adx[0] > AdxGate;
            bool crossUp = CrossAbove(diP, diM, 1);
            bool crossDn = CrossBelow(diP, diM, 1);

            bool dirLongOk  = !UseEmaFilter || Close[0] > emaFilter[0];
            bool dirShortOk = !UseEmaFilter || Close[0] < emaFilter[0];

            double anchor = AnchorVal();
            bool anchorLongOk  = (SideAnchor == AnchorMode.None) || !RequireLongsAbove || Close[0] > anchor;
            bool anchorShortOk = (SideAnchor == AnchorMode.None) || !RequireShortsBelow || Close[0] < anchor;

            // EMA zone
            double zoneWidth = ZoneWidthMode == ZoneMode.Ticks ? ZoneWidthTicks * TickSize : ZoneWidthAtr * atr[0];
            bool outsideZone = !UseEmaZone || Math.Abs(Close[0]-emaFilter[0]) >= zoneWidth;

            if (Position.MarketPosition == MarketPosition.Flat && InWindow())
            {
                if (strong && outsideZone)
                {
                    if ((Bias == TradeBias.Both || Bias == TradeBias.LongOnly) && dirLongOk && anchorLongOk && crossUp)
                        submitLegsLong();
                    else if ((Bias == TradeBias.Both || Bias == TradeBias.ShortOnly) && dirShortOk && anchorShortOk && crossDn)
                        submitLegsShort();
                }
            }

            // Manage trailing for open legs
            manageTrailing(MarketPosition.Long);
            manageTrailing(MarketPosition.Short);
        }

        // ===== Helpers =====
        private void submitLegsLong()
        {
            var list = buildLegs(isLong:true);
            foreach (var L in list)
            {
                if (L.Qty <= 0) continue;

                // Map stop/target BEFORE entry
                double tgt = computeTargetPrice(true, L);
                if (!double.IsNaN(tgt))
                    SetProfitTarget(L.Signal, CalculationMode.Price, tgt);

                double stp = computeInitialStop(true, L);
                if (!double.IsNaN(stp))
                {
                    SetStopLoss(L.Signal, CalculationMode.Price, stp, false);
                    trailBySignal[L.Signal] = stp;
                }

                EnterLong(L.Qty, L.Signal);
            }
        }

        private void submitLegsShort()
        {
            var list = buildLegs(isLong:false);
            foreach (var L in list)
            {
                if (L.Qty <= 0) continue;

                double tgt = computeTargetPrice(false, L);
                if (!double.IsNaN(tgt))
                    SetProfitTarget(L.Signal, CalculationMode.Price, tgt);

                double stp = computeInitialStop(false, L);
                if (!double.IsNaN(stp))
                {
                    SetStopLoss(L.Signal, CalculationMode.Price, stp, false);
                    trailBySignal[L.Signal] = stp;
                }

                EnterShort(L.Qty, L.Signal);
            }
        }

        private List<LegCfg> buildLegs(bool isLong)
        {
            // Re-bind quantities in case user changed in UI mid-session
            legs[0].Qty = Leg1Qty; legs[1].Qty = Leg2Qty; legs[2].Qty = Leg3Qty; legs[3].Qty = Leg4Qty;
            // Use unique signals for short legs to keep OCO mapping separate
            var list = new List<LegCfg>();
            for (int i = 0; i < legs.Length; i++)
            {
                var cfg = legs[i];
                cfg.Signal = (isLong ? "L" : "S") + (i+1).ToString();
                list.Add(cfg);
            }
            return list;
        }

        private double computeTargetPrice(bool isLong, LegCfg L)
        {
            double price = Close[0];
            switch (L.TMode)
            {
                case TargetCalcMode.AtrMult:
                    return isLong ? RT(price + atr[0] * (L.TParam <= 0 ? AtrMultDefault : L.TParam))
                                  : RT(price - atr[0] * (L.TParam <= 0 ? AtrMultDefault : L.TParam));
                case TargetCalcMode.Ticks:
                    return isLong ? RT(price + L.TicksT * TickSize) : RT(price - L.TicksT * TickSize);
                case TargetCalcMode.Percent:
                    return isLong ? RT(price * (1.0 + Math.Max(0, L.TParam))) : RT(price * (1.0 - Math.Max(0, L.TParam)));
                case TargetCalcMode.CustomLevel:
                    double lvl = LevelOf(L.CustomT);
                    return (lvl > 0) ? lvl : double.NaN;
                default:
                    return double.NaN;
            }
        }

        private double computeInitialStop(bool isLong, LegCfg L)
        {
            double price = Close[0];
            switch (L.SMode)
            {
                case StopCalcMode.AtrStatic:
                    return isLong ? RT(price - atr[0] * Math.Max(0.01, L.SParam)) : RT(price + atr[0] * Math.Max(0.01, L.SParam));
                case StopCalcMode.Ticks:
                    return isLong ? RT(price - L.TicksS * TickSize) : RT(price + L.TicksS * TickSize);
                case StopCalcMode.Percent:
                    return isLong ? RT(price * (1.0 - Math.Max(0, L.SParam))) : RT(price * (1.0 + Math.Max(0, L.SParam)));
                case StopCalcMode.PriorHLPlusTicks:
                    if (isLong) return RT(Low[1] - L.TicksS * TickSize);
                    else        return RT(High[1] + L.TicksS * TickSize);
                case StopCalcMode.BarNTrailing:
                    return isLong ? BarNStopLong(L.BarN, L.BarNOffset) : BarNStopShort(L.BarN, L.BarNOffset);
                case StopCalcMode.EmaTrailing:
                    var ema = emaForLeg(L);
                    if (ema == null) return double.NaN;
                    return isLong ? RT(ema[0] - L.EmaOffset * TickSize) : RT(ema[0] + L.EmaOffset * TickSize);
                default:
                    return double.NaN;
            }
        }

        private EMA emaForLeg(LegCfg L)
        {
            if (L.EmaLen == Leg1EmaPeriod) return emaLeg1;
            if (L.EmaLen == Leg2EmaPeriod) return emaLeg2;
            if (L.EmaLen == Leg3EmaPeriod) return emaLeg3;
            if (L.EmaLen == Leg4EmaPeriod) return emaLeg4;
            return EMA(L.EmaLen); // fallback (rare)
        }

        private void manageTrailing(MarketPosition side)
        {
            if (Position.MarketPosition != side) return;

            // Update per-entry trailing stops
            for (int i = 0; i < 4; i++)
            {
                string sig = (side == MarketPosition.Long ? "L" : "S") + (i+1);
                int bse = BarsSinceEntryExecution(0, sig, 0);
                if (bse == -1) continue;

                var L = (side == MarketPosition.Long) ? legs[i] : legs[i];
                L.Signal = sig;

                if (L.SMode == StopCalcMode.BarNTrailing)
                {
                    double proposed = (side == MarketPosition.Long) ? BarNStopLong(L.BarN, L.BarNOffset)
                                                                    : BarNStopShort(L.BarN, L.BarNOffset);
                    if (!trailBySignal.ContainsKey(sig)) trailBySignal[sig] = proposed;

                    if (side == MarketPosition.Long)
                        trailBySignal[sig] = Math.Max(trailBySignal[sig], proposed);
                    else
                        trailBySignal[sig] = Math.Min(trailBySignal[sig], proposed);

                    SetStopLoss(sig, CalculationMode.Price, trailBySignal[sig], false);
                }
                else if (L.SMode == StopCalcMode.EmaTrailing)
                {
                    var ema = emaForLeg(L);
                    double proposed = (side == MarketPosition.Long) ? RT(ema[0] - L.EmaOffset * TickSize)
                                                                    : RT(ema[0] + L.EmaOffset * TickSize);
                    if (!trailBySignal.ContainsKey(sig)) trailBySignal[sig] = proposed;

                    if (side == MarketPosition.Long)
                        trailBySignal[sig] = Math.Max(trailBySignal[sig], proposed);
                    else
                        trailBySignal[sig] = Math.Min(trailBySignal[sig], proposed);

                    SetStopLoss(sig, CalculationMode.Price, trailBySignal[sig], false);
                }
            }
        }

		private bool limitsHit()
		{
		    // Reset baseline at session start
		    if (Bars.IsFirstBarOfSession)
		    {
		        pnlAtSessionStart = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
		        winnersToday = losersToday = 0;
		    }
		
		    double dayPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - pnlAtSessionStart;
		
		    // Recompute W/L counts for today's closed trades
		    int w = 0, l = 0;
		    foreach (var t in SystemPerformance.AllTrades)
		    {
		        // NOTE: Trade.Exit has Time, not Execution
		        if (t.Exit != null && t.Exit.Time.Date == Time[0].Date)
		        {
		            if (t.ProfitCurrency > 0) w++;
		            else if (t.ProfitCurrency < 0) l++;
		        }
		    }
		    winnersToday = w;
		    losersToday  = l;
		
		    if (!UseLimits) return false;
		
		    if (DayProfitTarget > 0 && dayPnL >= DayProfitTarget) return true;
		    if (DayLossLimit   > 0 && dayPnL <= -Math.Abs(DayLossLimit)) return true;
		    if (MaxWinners     > 0 && winnersToday >= MaxWinners) return true;
		    if (MaxLosers      > 0 && losersToday  >= MaxLosers)  return true;
		
		    return false;
		}

        // --- VWAP calc for session anchor ---
        private void primeVWAP()
        {
            if (Bars.IsFirstBarOfSession) { cumPV = 0; cumVol = 0; }
            double typ = (High[0]+Low[0]+Close[0])/3.0;
            double vol = Math.Max(1.0, Volume[0]);
            cumPV += typ*vol; cumVol += vol;
            sessionVWAP[0] = (cumVol > 0 ? cumPV/cumVol : Close[0]);
        }
        private void updateVWAP()
        {
            if (Bars.IsFirstBarOfSession) { cumPV = 0; cumVol = 0; }
            double typ = (High[0]+Low[0]+Close[0])/3.0;
            double vol = Math.Max(1.0, Volume[0]);
            cumPV += typ*vol; cumVol += vol;
            sessionVWAP[0] = (cumVol > 0 ? cumPV/cumVol : Close[0]);
        }
    }
}
