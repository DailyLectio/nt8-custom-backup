#region Using
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class CoreLevelManualTriggerStrategy : Strategy
    {
        public enum TradeBias { Both, LongOnly, ShortOnly }
        public enum StopMode { ATR, Ticks, BarNTrailing, EmaTrailing }
        public enum OrderPref { Market, LimitAtEntry }
        public enum EntryTriggerMode { CloseConfirmNextOpen, ManualLimit }
        public enum LevelName { B4, B3, B2, B1, POC, R1, R2, R3, R4, Bull1, Bull2, Bear1, Bear2 }
        public enum EntrySource { Price, Level }

        [NinjaScriptProperty, Display(Name = "Entry Mode", GroupName = "Entry", Order = -1)]
        public EntryTriggerMode EntryMode { get; set; } = EntryTriggerMode.CloseConfirmNextOpen;

        // ---- Entry ----
        [NinjaScriptProperty, Display(Name = "Entry From", GroupName = "Entry", Order = 1)]
        public EntrySource EntryFrom { get; set; } = EntrySource.Price;

        [NinjaScriptProperty, Display(Name = "Auto Direction from Targets", GroupName = "Entry", Order = 2)]
        public bool AutoDirection { get; set; } = true;

        // ---- Levels ----
        [NinjaScriptProperty, Display(Name = "Start Level (if Entry From = Level)", GroupName = "Levels", Order = 3)]
        public LevelName StartLevel { get; set; } = LevelName.POC;

        [NinjaScriptProperty, Display(Name = "Trade Bias", GroupName = "Entry", Order = 0)]
        public TradeBias Bias { get; set; } = TradeBias.Both;

        [NinjaScriptProperty, Display(Name = "Order Type", GroupName = "Entry", Order = 1)]
        public OrderPref OrderType { get; set; } = OrderPref.Market;

        [NinjaScriptProperty, Display(Name = "Entry Level Price", GroupName = "Levels", Order = 2)]
        public double EntryLevel { get; set; } = 0;

        [NinjaScriptProperty, Display(Name = "Target 1 (Level)", GroupName = "Targets", Order = 10)]
        public LevelName Target1 { get; set; } = LevelName.R1;
        [NinjaScriptProperty, Range(0, int.MaxValue), Display(Name = "Target 1 Qty (0=off)", GroupName = "Targets", Order = 11)]
        public int Target1Qty { get; set; } = 0;

        [NinjaScriptProperty, Display(Name = "Target 2 (Level)", GroupName = "Targets", Order = 12)]
        public LevelName Target2 { get; set; } = LevelName.R2;
        [NinjaScriptProperty, Range(0, int.MaxValue), Display(Name = "Target 2 Qty (0=off)", GroupName = "Targets", Order = 13)]
        public int Target2Qty { get; set; } = 0;

        [NinjaScriptProperty, Display(Name = "Target 3 (Level)", GroupName = "Targets", Order = 14)]
        public LevelName Target3 { get; set; } = LevelName.R3;
        [NinjaScriptProperty, Range(0, int.MaxValue), Display(Name = "Target 3 Qty (0=off)", GroupName = "Targets", Order = 15)]
        public int Target3Qty { get; set; } = 0;

        [NinjaScriptProperty, Display(Name = "Target 4 (Level)", GroupName = "Targets", Order = 16)]
        public LevelName Target4 { get; set; } = LevelName.R4;
        [NinjaScriptProperty, Range(0, int.MaxValue), Display(Name = "Target 4 Qty (0=off)", GroupName = "Targets", Order = 17)]
        public int Target4Qty { get; set; } = 0;

        [NinjaScriptProperty, Display(Name = "Stop Mode", GroupName = "Stops", Order = 20)]
        public StopMode SMode { get; set; } = StopMode.ATR;
        [NinjaScriptProperty, Range(1, int.MaxValue), Display(Name = "ATR Period", GroupName = "Stops", Order = 21)]
        public int ATRPeriod { get; set; } = 14;
        [NinjaScriptProperty, Range(0.1, double.MaxValue), Display(Name = "ATR Mult", GroupName = "Stops", Order = 22)]
        public double ATRMult { get; set; } = 1.0;
        [NinjaScriptProperty, Range(0, int.MaxValue), Display(Name = "Fixed Stop (ticks)", GroupName = "Stops", Order = 23)]
        public int StopTicks { get; set; } = 12;

        [NinjaScriptProperty, Range(1, int.MaxValue), Display(Name = "Trailing N Bars", GroupName = "Stops", Order = 24)]
        public int TrailingNBars { get; set; } = 2;

        [NinjaScriptProperty, Range(0, int.MaxValue), Display(Name = "Trailing Offset (ticks)", GroupName = "Stops", Order = 25)]
        public int TrailingOffsetTicks { get; set; } = 8;

        [NinjaScriptProperty, Range(1, int.MaxValue), Display(Name = "EMA Trail Period", GroupName = "Stops", Order = 26)]
        public int EmaTrailPeriod { get; set; } = 50;

        [NinjaScriptProperty, Range(0, int.MaxValue), Display(Name = "EMA Offset (ticks)", GroupName = "Stops", Order = 27)]
        public int EmaOffsetTicks { get; set; } = 0;

        [NinjaScriptProperty, Range(1, int.MaxValue), Display(Name = "EMA Switch N Bars", GroupName = "Stops", Order = 28)]
        public int EmaSwitchNBars { get; set; } = 2;

        [NinjaScriptProperty, Display(Name = "Rebase After Targets", GroupName = "Stops", Order = 29)]
        public bool RebaseAfterTargets { get; set; } = true;

        [NinjaScriptProperty, Display(Name = "Use Daily Limits", GroupName = "Daily Limits", Order = 30)]
        public bool UseLimits { get; set; } = true;
        [NinjaScriptProperty, Display(Name = "Daily Profit Target ($)", GroupName = "Daily Limits", Order = 31)]
        public double DayProfitTarget { get; set; } = 1000;
        [NinjaScriptProperty, Display(Name = "Daily Loss Limit ($)", GroupName = "Daily Limits", Order = 32)]
        public double DayLossLimit { get; set; } = 500;
        [NinjaScriptProperty, Range(0, int.MaxValue), Display(Name = "Max Winners", GroupName = "Daily Limits", Order = 33)]
        public int MaxWinners { get; set; } = 0;
        [NinjaScriptProperty, Range(0, int.MaxValue), Display(Name = "Max Losers", GroupName = "Daily Limits", Order = 34)]
        public int MaxLosers { get; set; } = 0;

        [NinjaScriptProperty, Display(Name = "B4", GroupName = "Manual Levels (Prices)", Order = 40)] public double L_B4 { get; set; } = 0;
        [NinjaScriptProperty, Display(Name = "B3", GroupName = "Manual Levels (Prices)", Order = 41)] public double L_B3 { get; set; } = 0;
        [NinjaScriptProperty, Display(Name = "B2", GroupName = "Manual Levels (Prices)", Order = 42)] public double L_B2 { get; set; } = 0;
        [NinjaScriptProperty, Display(Name = "B1", GroupName = "Manual Levels (Prices)", Order = 43)] public double L_B1 { get; set; } = 0;
        [NinjaScriptProperty, Display(Name = "POC", GroupName = "Manual Levels (Prices)", Order = 44)] public double L_POC { get; set; } = 0;
        [NinjaScriptProperty, Display(Name = "R1", GroupName = "Manual Levels (Prices)", Order = 45)] public double L_R1 { get; set; } = 0;
        [NinjaScriptProperty, Display(Name = "R2", GroupName = "Manual Levels (Prices)", Order = 46)] public double L_R2 { get; set; } = 0;
        [NinjaScriptProperty, Display(Name = "R3", GroupName = "Manual Levels (Prices)", Order = 47)] public double L_R3 { get; set; } = 0;
        [NinjaScriptProperty, Display(Name = "R4", GroupName = "Manual Levels (Prices)", Order = 48)] public double L_R4 { get; set; } = 0;
        [NinjaScriptProperty, Display(Name = "Bull1", GroupName = "Manual Levels (Prices)", Order = 49)] public double L_Bull1 { get; set; } = 0;
        [NinjaScriptProperty, Display(Name = "Bull2", GroupName = "Manual Levels (Prices)", Order = 50)] public double L_Bull2 { get; set; } = 0;
        [NinjaScriptProperty, Display(Name = "Bear1", GroupName = "Manual Levels (Prices)", Order = 51)] public double L_Bear1 { get; set; } = 0;
        [NinjaScriptProperty, Display(Name = "Bear2", GroupName = "Manual Levels (Prices)", Order = 52)] public double L_Bear2 { get; set; } = 0;

        private ATR atr;
        private EMA emaTrail;
        private Button enterButton;

        private bool setupArmed;
        private bool longSetup;
        private bool shortSetup;
        private bool allowNewEntries = true;
        private int winnersToday, losersToday;
        private double pnlAtSessionStart;

        // Keep-alive maps per leg signal
        private readonly Dictionary<string, double> legLimitPrice = new Dictionary<string, double>(); // last known limit price
        private readonly Dictionary<string, bool> legIsLong = new Dictionary<string, bool>();         // true=long, false=short
        private readonly HashSet<string> legNeedsResub = new HashSet<string>();                       // mark for re-submit

        private double RT(double p) => Instrument.MasterInstrument.RoundToTickSize(p);

        private double LevelOf(LevelName n)
        {
            switch (n)
            {
                case LevelName.B4: return L_B4;
                case LevelName.B3: return L_B3;
                case LevelName.B2: return L_B2;
                case LevelName.B1: return L_B1;
                case LevelName.POC: return L_POC;
                case LevelName.R1: return L_R1;
                case LevelName.R2: return L_R2;
                case LevelName.R3: return L_R3;
                case LevelName.R4: return L_R4;
                case LevelName.Bull1: return L_Bull1;
                case LevelName.Bull2: return L_Bull2;
                case LevelName.Bear1: return L_Bear1;
                case LevelName.Bear2: return L_Bear2;
                default: return 0;
            }
        }

        // --- New helpers for level/price entry and auto-direction ---
        private double EntryPx()
        {
            return (EntryFrom == EntrySource.Price) ? EntryLevel : LevelOf(StartLevel);
        }

        private bool InferLongFromTargets(double entryPx)
        {
            var picks = new (LevelName level, int qty)[] {
                (Target1, Target1Qty), (Target2, Target2Qty), (Target3, Target3Qty), (Target4, Target4Qty)
            };
            foreach (var p in picks)
            {
                if (p.qty <= 0) continue;
                double tp = LevelOf(p.level);
                if (tp.ApproxCompare(0.0) == 0) continue;
                return tp > entryPx; // target above entry ⇒ long
            }
            return true; // default long if nothing enabled
        }
        // ------------------------------------------------------------

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "CoreLevelManualTriggerStrategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 4;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                TraceOrders = false;
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(ATRPeriod);
                AddChartIndicator(atr);
                emaTrail = EMA(EmaTrailPeriod);
                AddChartIndicator(emaTrail);
            }
            else if (State == State.Realtime)
            {
                if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
                        var grid = ChartControl.Parent as Grid;
                        if (grid == null) return;

                        enterButton = new Button
                        {
                            Content = "Enter (waiting…)",
                            Padding = new Thickness(6, 2, 6, 2),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(10, 28, 0, 0),
                            IsEnabled = false
                        };
                        enterButton.Click += (s, e) =>
                        {
                            if (!allowNewEntries) return;
                            if (EntryMode == EntryTriggerMode.ManualLimit)
                                TryEnterManualLimit();
                            else
                            {
                                if (longSetup) TryEnterConfirm(true);
                                else if (shortSetup) TryEnterConfirm(false);
                            }
                        };
                        grid.Children.Add(enterButton);

                        if (EntryMode == EntryTriggerMode.ManualLimit)
                            UpdateManualLimitButton();
                        else
                            UpdateButton("Enter (waiting…)", false);
                    });
                }
            }
            else if (State == State.Terminated)
            {
                if (ChartControl != null && enterButton != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
                        var grid = ChartControl.Parent as Grid;
                        if (grid != null) grid.Children.Remove(enterButton);
                        enterButton = null;
                    });
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2) return;

            // Guard: if user chose Level start but level price is zero, disable arming
            if (EntryFrom == EntrySource.Level && EntryPx().ApproxCompare(0.0) == 0)
            {
                UpdateButton("Set Start Level", false);
                return;
            }

            EvaluateLimitsAndGate();

            if (Bars.IsFirstBarOfSession)
            {
                setupArmed = false;
                longSetup = shortSetup = false;
                UpdateButton("Enter (waiting…)", false);
                legNeedsResub.Clear();
            }

            if (!allowNewEntries)
            {
                UpdateButton("LIMIT HIT", false);
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                ManageTrailing();
                return;
            }

            // Manual-limit keep-alive + button label
            if (EntryMode == EntryTriggerMode.ManualLimit)
            {
                UpdateManualLimitButton();

                if (legNeedsResub.Count > 0)
                {
                    foreach (var sig in new List<string>(legNeedsResub))
                    {
                        if (!legLimitPrice.ContainsKey(sig) || !legIsLong.ContainsKey(sig))
                        { legNeedsResub.Remove(sig); continue; }

                        var px = RT(legLimitPrice[sig]);
                        var isLong = legIsLong[sig];

                        if (isLong) EnterLongLimit(TargetQtyForSignal(sig), px, sig);
                        else EnterShortLimit(TargetQtyForSignal(sig), px, sig);

                        legNeedsResub.Remove(sig);
                    }
                }
                return;
            }

            // Confirm-next-open mode (Price or Level)
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                double ep = EntryPx();

                if (!setupArmed)
                {
                    if ((Bias == TradeBias.Both || Bias == TradeBias.LongOnly) && Close[0] > ep && Close[1] <= ep)
                    {
                        setupArmed = true; longSetup = true; shortSetup = false;
                        UpdateButton("Enter LONG", true);
                    }
                    else if ((Bias == TradeBias.Both || Bias == TradeBias.ShortOnly) && Close[0] < ep && Close[1] >= ep)
                    {
                        setupArmed = true; shortSetup = true; longSetup = false;
                        UpdateButton("Enter SHORT", true);
                    }
                    else
                        UpdateButton("Enter (waiting…)", false);
                }
                else
                {
                    if (longSetup && Open[0] > ep)
                        UpdateButton("Enter LONG", true);
                    else if (shortSetup && Open[0] < ep)
                        UpdateButton("Enter SHORT", true);
                    else
                    {
                        setupArmed = false; longSetup = shortSetup = false;
                        UpdateButton("Enter (waiting…)", false);
                    }
                }
            }
        }

        private int TargetQtyForSignal(string sig)
        {
            switch (sig)
            {
                case "L1": return Target1Qty;
                case "L2": return Target2Qty;
                case "L3": return Target3Qty;
                case "L4": return Target4Qty;
                default: return 0;
            }
        }

        private void UpdateButton(string text, bool enabled)
        {
            if (enterButton == null || ChartControl == null) return;
            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                enterButton.Content = text;
                enterButton.IsEnabled = enabled && allowNewEntries;
            });
        }

        private void TryEnterConfirm(bool isLong)
        {
            if (!setupArmed) return;

            double ep = EntryPx();
            if (isLong && Open[0] <= ep) return;
            if (!isLong && Open[0] >= ep) return;

            double entryPx = (OrderType == OrderPref.Market) ? Open[0] : ep;
            SubmitBrackets(isLong, entryPx);

            setupArmed = false; longSetup = shortSetup = false;
            UpdateButton("Enter (waiting…)", false);
        }

        private void UpdateManualLimitButton()
        {
            if (enterButton == null) return;

            if (!allowNewEntries)
            {
                UpdateButton("LIMIT HIT", false);
                return;
            }

            double ep = EntryPx();

            bool suggestLong = (Bias == TradeBias.LongOnly) || (Bias == TradeBias.Both && ep < Close[0]);
            bool suggestShort = (Bias == TradeBias.ShortOnly) || (Bias == TradeBias.Both && ep > Close[0]);

            if (Bias == TradeBias.Both && AutoDirection)
            {
                bool wantLong = InferLongFromTargets(ep);
                suggestLong = wantLong;
                suggestShort = !wantLong;
            }

            string label;
            if (suggestLong && !suggestShort) label = "Place BUY LIMIT";
            else if (suggestShort && !suggestLong) label = "Place SELL LIMIT";
            else label = "Place LIMIT @ Entry";

            UpdateButton(label, true);
        }

        private void TryEnterManualLimit()
        {
            double ep = EntryPx();

            bool longIntent = (Bias == TradeBias.LongOnly);
            bool shortIntent = (Bias == TradeBias.ShortOnly);

            if (Bias == TradeBias.Both)
            {
                if (AutoDirection)
                {
                    longIntent = InferLongFromTargets(ep);
                    shortIntent = !longIntent;
                }
                else
                {
                    longIntent = ep < Close[0];
                    shortIntent = ep > Close[0];
                }
            }

            if (!longIntent && !shortIntent) return;

            if (longIntent) SubmitBrackets(true, ep, forceLimit: true);
            if (shortIntent) SubmitBrackets(false, ep, forceLimit: true);

            UpdateButton("Working…", false);
        }

        private void SubmitBrackets(bool isLong, double entryPx, bool forceLimit = false)
        {
            SubmitLeg(isLong, "L1", Target1, Target1Qty, entryPx, forceLimit);
            SubmitLeg(isLong, "L2", Target2, Target2Qty, entryPx, forceLimit);
            SubmitLeg(isLong, "L3", Target3, Target3Qty, entryPx, forceLimit);
            SubmitLeg(isLong, "L4", Target4, Target4Qty, entryPx, forceLimit);
        }

        private void SubmitLeg(bool isLong, string sig, LevelName tgtLevel, int qty, double entryPx, bool forceLimit)
        {
            if (qty <= 0) return;

            double tgt = LevelOf(tgtLevel);
            if (tgt.ApproxCompare(0.0) == 0) return;

            if (isLong && tgt <= entryPx) return;
            if (!isLong && tgt >= entryPx) return;

            double stopPx = ComputeInitialStop(isLong, entryPx);

            SetProfitTarget(sig, CalculationMode.Price, RT(tgt));
            SetStopLoss(sig, CalculationMode.Price, RT(stopPx), false);

            bool useLimit = forceLimit || (OrderType == OrderPref.LimitAtEntry);

            if (useLimit)
            {
                // remember price/side so we can re-submit if NT cancels at bar close
                legLimitPrice[sig] = entryPx;
                legIsLong[sig] = isLong;
            }

            if (isLong)
            {
                if (useLimit) EnterLongLimit(qty, RT(entryPx), sig);
                else EnterLong(qty, sig);
            }
            else
            {
                if (useLimit) EnterShortLimit(qty, RT(entryPx), sig);
                else EnterShort(qty, sig);
            }
        }

        private double ComputeInitialStop(bool isLong, double refPx)
        {
            if (SMode == StopMode.ATR || SMode == StopMode.BarNTrailing || SMode == StopMode.EmaTrailing)
            {
                double dist = Math.Max(0.01, ATRMult) * atr[0];
                return isLong ? RT(refPx - dist) : RT(refPx + dist);
            }
            else
            {
                double dist = StopTicks * TickSize;
                return isLong ? RT(refPx - dist) : RT(refPx + dist);
            }
        }

        private void ManageTrailing()
        {
            if (Position.MarketPosition == MarketPosition.Flat) return;

            double trailPrice = double.NaN;

            if (SMode == StopMode.BarNTrailing)
            {
                int n = Math.Max(1, TrailingNBars);
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    int bse = BarsSinceEntryExecution(0, "L1", 0);
                    if (bse != -1 && bse >= n)
                    {
                        double lo = Low[0];
                        for (int i = 1; i < n; i++) lo = Math.Min(lo, Low[i]);
                        trailPrice = RT(lo - TrailingOffsetTicks * TickSize);
                    }
                }
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    int bse = BarsSinceEntryExecution(0, "L1", 0);
                    if (bse != -1 && bse >= n)
                    {
                        double hi = High[0];
                        for (int i = 1; i < n; i++) hi = Math.Max(hi, High[i]);
                        trailPrice = RT(hi + TrailingOffsetTicks * TickSize);
                    }
                }
            }
            else if (SMode == StopMode.EmaTrailing)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    int bse = BarsSinceEntryExecution(0, "L1", 0);
                    if (bse != -1 && bse >= Math.Max(1, EmaSwitchNBars))
                        trailPrice = RT(emaTrail[0] - EmaOffsetTicks * TickSize);
                }
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    int bse = BarsSinceEntryExecution(0, "L1", 0);
                    if (bse != -1 && bse >= Math.Max(1, EmaSwitchNBars))
                        trailPrice = RT(emaTrail[0] + EmaOffsetTicks * TickSize);
                }
            }
            else
            {
                if (RebaseAfterTargets)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        double maxHit = double.NaN;
                        TryUpdateMax(ref maxHit, LevelOf(Target1), "L1");
                        TryUpdateMax(ref maxHit, LevelOf(Target2), "L2");
                        TryUpdateMax(ref maxHit, LevelOf(Target3), "L3");
                        TryUpdateMax(ref maxHit, LevelOf(Target4), "L4");
                        if (!double.IsNaN(maxHit))
                        {
                            double rebase = ComputeRebaseStop(true, maxHit);
                            trailPrice = double.IsNaN(trailPrice) ? rebase : Math.Max(trailPrice, rebase);
                        }
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        double minHit = double.NaN;
                        TryUpdateMin(ref minHit, LevelOf(Target1), "L1");
                        TryUpdateMin(ref minHit, LevelOf(Target2), "L2");
                        TryUpdateMin(ref minHit, LevelOf(Target3), "L3");
                        TryUpdateMin(ref minHit, LevelOf(Target4), "L4");
                        if (!double.IsNaN(minHit))
                        {
                            double rebase = ComputeRebaseStop(false, minHit);
                            trailPrice = double.IsNaN(trailPrice) ? rebase : Math.Min(trailPrice, rebase);
                        }
                    }
                }
            }

            if (!double.IsNaN(trailPrice))
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    for (int i = 1; i <= 4; i++) SetStopLoss("L" + i, CalculationMode.Price, trailPrice, false);
                else if (Position.MarketPosition == MarketPosition.Short)
                    for (int i = 1; i <= 4; i++) SetStopLoss("L" + i, CalculationMode.Price, trailPrice, false);
            }
        }

        private double ComputeRebaseStop(bool isLong, double levelHit)
        {
            if (SMode == StopMode.ATR || SMode == StopMode.BarNTrailing || SMode == StopMode.EmaTrailing)
            {
                double dist = Math.Max(0.01, ATRMult) * atr[0];
                return isLong ? RT(levelHit - dist) : RT(levelHit + dist);
            }
            else
            {
                double dist = StopTicks * TickSize;
                return isLong ? RT(levelHit - dist) : RT(levelHit + dist);
            }
        }

        private void TryUpdateMax(ref double max, double level, string sig)
        {
            if (level.ApproxCompare(0.0) == 0) return;
            int bse = BarsSinceEntryExecution(0, sig, 0);
            if (bse == -1) return;
            if (Close[0] >= level)
                max = double.IsNaN(max) ? level : Math.Max(max, level);
        }

        private void TryUpdateMin(ref double min, double level, string sig)
        {
            if (level.ApproxCompare(0.0) == 0) return;
            int bse = BarsSinceEntryExecution(0, sig, 0);
            if (bse == -1) return;
            if (Close[0] <= level)
                min = double.IsNaN(min) ? level : Math.Min(min, level);
        }

        private void EvaluateLimitsAndGate()
        {
            if (Bars.IsFirstBarOfSession)
            {
                pnlAtSessionStart = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                winnersToday = losersToday = 0;
                allowNewEntries = true;
            }

            double dayPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - pnlAtSessionStart;

            int w = 0, l = 0;
            foreach (var t in SystemPerformance.AllTrades)
            {
                if (t.Exit != null && t.Exit.Time.Date == Time[0].Date)
                {
                    if (t.ProfitCurrency > 0) w++;
                    else if (t.ProfitCurrency < 0) l++;
                }
            }
            winnersToday = w; losersToday = l;

            if (!UseLimits) return;

            bool hit =
                (DayProfitTarget > 0 && dayPnL >= DayProfitTarget) ||
                (DayLossLimit > 0 && dayPnL <= -Math.Abs(DayLossLimit)) ||
                (MaxWinners > 0 && winnersToday >= MaxWinners) ||
                (MaxLosers > 0 && losersToday >= MaxLosers);

            if (hit) allowNewEntries = false;
        }

        // Extended signature works across NT8 builds
        protected override void OnOrderUpdate(
            NinjaTrader.Cbi.Order order,
            double limitPrice,
            double stopPrice,
            int quantity,
            int filled,
            double averageFillPrice,
            NinjaTrader.Cbi.OrderState orderState,
            DateTime time,
            NinjaTrader.Cbi.ErrorCode error,
            string nativeError)
        {
            if (order == null) return;

            // manage only our four leg signals
            if (order.Name != "L1" && order.Name != "L2" && order.Name != "L3" && order.Name != "L4")
                return;

            // track last working limit price (also updates when you drag on the chart)
            if (order.OrderType == NinjaTrader.Cbi.OrderType.Limit && orderState == NinjaTrader.Cbi.OrderState.Working)
            {
                legLimitPrice[order.Name] = order.LimitPrice;
                if (legNeedsResub.Contains(order.Name))
                    legNeedsResub.Remove(order.Name);
            }

            // bar-close auto-cancel → mark for resubmit (ManualLimit mode, while flat)
            if (EntryMode == EntryTriggerMode.ManualLimit
                && order.OrderType == NinjaTrader.Cbi.OrderType.Limit
                && orderState == NinjaTrader.Cbi.OrderState.Cancelled
                && Position.MarketPosition == MarketPosition.Flat)
            {
                if (allowNewEntries && legLimitPrice.ContainsKey(order.Name))
                    legNeedsResub.Add(order.Name);
            }

            // if filled, clear tracking for that leg
            if (orderState == NinjaTrader.Cbi.OrderState.Filled)
            {
                if (legNeedsResub.Contains(order.Name)) legNeedsResub.Remove(order.Name);
                if (legLimitPrice.ContainsKey(order.Name)) legLimitPrice.Remove(order.Name);
                if (legIsLong.ContainsKey(order.Name)) legIsLong.Remove(order.Name);
            }
        }
    }
}
