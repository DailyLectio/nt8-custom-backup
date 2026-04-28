#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;              // <-- FIX #1 (MarketDataEventArgs)
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ApexKillSwitch : Strategy
    {
        // =========================
        // User Inputs
        // =========================
        [NinjaScriptProperty]
        [Display(Name = "Max Daily Loss ($)", Order = 1, GroupName = "Limits")]
        public double MaxDailyLoss { get; set; } = 350;

        [NinjaScriptProperty]
        [Display(Name = "Max Daily Profit ($)", Order = 2, GroupName = "Limits")]
        public double MaxDailyProfit { get; set; } = 500;

        [NinjaScriptProperty]
        [Display(Name = "Max Losing Trades", Order = 3, GroupName = "Limits")]
        public int MaxLosingTrades { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "Max Winning Trades", Order = 4, GroupName = "Limits")]
        public int MaxWinningTrades { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Enable Feed Stall Kill", Order = 1, GroupName = "Feed Stall")]
        public bool EnableFeedStallKill { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Feed Stall Seconds", Order = 2, GroupName = "Feed Stall")]
        public int FeedStallSeconds { get; set; } = 5;

        [NinjaScriptProperty]
        [Display(Name = "Only Kill During RTH (0930-1600)", Order = 3, GroupName = "Feed Stall")]
        public bool OnlyDuringRTH { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Account Name Filter (optional)", Order = 1, GroupName = "Safety")]
        public string AccountNameFilter { get; set; } = "";

        [NinjaScriptProperty]
        [Display(Name = "Re-issue Flatten/Cancels (seconds)", Order = 2, GroupName = "Safety")]
        public int ReissueSeconds { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name = "Print Debug", Order = 3, GroupName = "Safety")]
        public bool PrintDebug { get; set; } = false;

        // =========================
        // Internals
        // =========================
        private bool killFired = false;
        private string killReason = "";

        private double sessionRealizedBase = 0.0;
        private double lastRealizedSnapshot = 0.0;
        private int winCount = 0;
        private int lossCount = 0;

        private DateTime lastTickTimeUtc;
        private DateTime killStartUtc;
        private DateTime lastReissueUtc;

        private HashSet<string> printed = new HashSet<string>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "ApexKillSwitch";
                Description = "Account-level risk guardian: daily PnL, win/loss count, feed stall kill. Cancels orders + flattens positions.";
                Calculate   = Calculate.OnEachTick;
                IsOverlay   = true;

                // This is a guardian, not a trader
                IsExitOnSessionCloseStrategy = false;
                BarsRequiredToTrade = 1;
            }
            else if (State == State.DataLoaded)
            {
                lastTickTimeUtc = DateTime.UtcNow;
            }
            else if (State == State.Realtime)
            {
                if (!string.IsNullOrWhiteSpace(AccountNameFilter))
                {
                    if (Account == null || !Account.Name.Equals(AccountNameFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        PrintOnce($"[{Name}] Account filter mismatch. Running on '{Account?.Name ?? "NULL"}' but filter is '{AccountNameFilter}'. Strategy will idle.");
                        return;
                    }
                }

                ResetDailyStats("Realtime start");
            }
        }

        private void ResetDailyStats(string why)
        {
            killFired = false;
            killReason = "";
            winCount = 0;
            lossCount = 0;

            sessionRealizedBase = GetAccountRealizedPnL();
            lastRealizedSnapshot = sessionRealizedBase;

            lastTickTimeUtc = DateTime.UtcNow;
            lastReissueUtc = DateTime.MinValue;

            if (PrintDebug)
                Print($"[{Name}] Daily reset ({why}). BaseRealized={sessionRealizedBase:F2} Account={Account?.Name}");
        }

        protected override void OnBarUpdate()
        {
            if (!string.IsNullOrWhiteSpace(AccountNameFilter))
            {
                if (Account == null || !Account.Name.Equals(AccountNameFilter, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            if (Bars.IsFirstBarOfSession)
                ResetDailyStats("FirstBarOfSession");

            EvaluateKills();

            if (killFired)
                ReissueFlattenAndCancelWindow();
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            // Any market data update counts as "data flowing"
            lastTickTimeUtc = DateTime.UtcNow;
        }

        private void EvaluateKills()
        {
            if (killFired)
                return;

            // 1) Daily realized PnL delta (account-wide)
            double realizedNow = GetAccountRealizedPnL();
            double dailyRealized = realizedNow - sessionRealizedBase;

            if (dailyRealized <= -Math.Abs(MaxDailyLoss))
            {
                TriggerKill($"Daily loss hit: {dailyRealized:F2} <= -{Math.Abs(MaxDailyLoss):F2}");
                return;
            }

            if (dailyRealized >= Math.Abs(MaxDailyProfit))
            {
                TriggerKill($"Daily profit hit: {dailyRealized:F2} >= +{Math.Abs(MaxDailyProfit):F2}");
                return;
            }

            // 2) Best-effort win/loss counting from realized PnL changes
            if (!realizedNow.ApproxCompare(lastRealizedSnapshot).Equals(0))
            {
                double delta = realizedNow - lastRealizedSnapshot;
                if (delta > 0) winCount++;
                else if (delta < 0) lossCount++;

                lastRealizedSnapshot = realizedNow;

                if (PrintDebug)
                    Print($"[{Name}] Realized change: delta={delta:F2} wins={winCount} losses={lossCount}");
            }

            if (lossCount >= MaxLosingTrades)
            {
                TriggerKill($"Max losers hit: {lossCount} >= {MaxLosingTrades}");
                return;
            }

            if (winCount >= MaxWinningTrades)
            {
                TriggerKill($"Max wins hit: {winCount} >= {MaxWinningTrades}");
                return;
            }

            // 3) Feed stall kill (green-light stalls included)
            if (EnableFeedStallKill)
            {
                if (OnlyDuringRTH && !IsRTH())
                    return;

                double stalledSec = (DateTime.UtcNow - lastTickTimeUtc).TotalSeconds;
                if (stalledSec >= Math.Max(1, FeedStallSeconds))
                {
                    TriggerKill($"Feed stall detected: {stalledSec:F1}s >= {FeedStallSeconds}s");
                    return;
                }
            }
        }

        private void TriggerKill(string reason)
        {
            killFired = true;
            killReason = reason;
            killStartUtc = DateTime.UtcNow;
            lastReissueUtc = DateTime.MinValue;

            PrintOnce($"[{Name}] *** KILL FIRED *** {killReason} | Account={Account?.Name}");

            TryCancelAllOrders();
            TryFlattenAllPositions();

            lastReissueUtc = DateTime.UtcNow;
        }

        private void ReissueFlattenAndCancelWindow()
        {
            if ((DateTime.UtcNow - killStartUtc).TotalSeconds > Math.Max(1, ReissueSeconds))
                return;

            if ((DateTime.UtcNow - lastReissueUtc).TotalSeconds < 1.0)
                return;

            TryCancelAllOrders();
            TryFlattenAllPositions();
            lastReissueUtc = DateTime.UtcNow;
        }

        private void TryCancelAllOrders()
        {
            if (Account == null)
                return;

            try
            {
                foreach (var inst in GetAccountInstruments())
                {
                    // FIX #2: your build requires CancelAllOrders(Instrument)
                    Account.CancelAllOrders(inst);
                }

                if (PrintDebug)
                    Print($"[{Name}] CancelAllOrders(instrument) executed.");
            }
            catch (Exception e)
            {
                PrintOnce($"[{Name}] Cancel orders failed: {e.Message}");
            }
        }

        private void TryFlattenAllPositions()
        {
            if (Account == null)
                return;

            try
            {
                // FIX #3: your build expects ICollection<Instrument>
                var insts = new List<Instrument>(GetAccountInstruments());
                if (insts.Count == 0)
                    return;

                Account.Flatten(insts);

                if (PrintDebug)
                    Print($"[{Name}] Flatten(ICollection<Instrument>) executed. Count={insts.Count}");
            }
            catch (Exception e)
            {
                PrintOnce($"[{Name}] Flatten failed: {e.Message}");
            }
        }

        private IEnumerable<Instrument> GetAccountInstruments()
        {
            var set = new HashSet<Instrument>();

            // Instruments with open positions
            try
            {
                foreach (var pos in Account.Positions)
                {
                    if (pos?.Instrument == null) continue;
                    if (pos.MarketPosition == MarketPosition.Flat) continue;
                    set.Add(pos.Instrument);
                }
            }
            catch { }

            // Instruments with working orders (covers "no position yet" edge case)
            try
            {
                foreach (var o in Account.Orders)
                {
                    if (o?.Instrument == null) continue;

                    // include any non-final order states
                    if (o.OrderState == OrderState.Working ||
                        o.OrderState == OrderState.Accepted ||
                        o.OrderState == OrderState.PartFilled ||
                        o.OrderState == OrderState.Submitted)
                        set.Add(o.Instrument);
                }
            }
            catch { }

            // Fallback to chart instrument if nothing else is discoverable
            if (set.Count == 0 && Instrument != null)
                set.Add(Instrument);

            return set;
        }

        private double GetAccountRealizedPnL()
        {
            if (Account == null)
                return 0.0;

            try
            {
                return Account.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
            }
            catch
            {
                try
                {
                    return Account.Get(AccountItem.GrossRealizedProfitLoss, Currency.UsDollar);
                }
                catch
                {
                    return 0.0;
                }
            }
        }

        private bool IsRTH()
        {
            // Uses your PC/NT time zone. RTH: 09:30–16:00
            int t = ToTime(Time[0]);
            return t >= 093000 && t <= 160000;
        }

        private void PrintOnce(string msg)
        {
            if (printed.Contains(msg)) return;
            printed.Add(msg);
            Print(msg);
        }
    }
}
