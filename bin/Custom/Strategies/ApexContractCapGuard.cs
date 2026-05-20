// ApexContractCapGuard.cs  -- 2026-05-20 (v2, Layer-2-only)
// =============================================================================
// PURPOSE
//   Hard per-instrument contract cap on a single Apex follower account, with a
//   no-sign-flip-in-one-execution rule. Sim master strategies are left
//   untouched; this guard sits on the follower side and trims any execution
//   that pushes |position| over MaxContracts or flips the position sign.
//
// USAGE
//   Attach ONE instance per Apex account.
//   Set "Apex Account Name" to the exact NT8 account name (e.g.
//   "APEX7678400000195"). MaxContracts default = 2.
//   The strategy can run on any chart; it guards the named account globally
//   via Account.ExecutionUpdate event subscription.
//
// BEHAVIOUR (LAYER-2-ONLY DESIGN)
//   v1 used a pre-fill cancel-and-replace on Account.OrderUpdate. On 2026-05-20
//   that design lost the race against a copier-driven market order: the
//   original AI Sell-4 filled AND our ApexCap_Replace Sell-2 also filled, so
//   Apex briefly held -6 before the Layer-2 trim brought it back to -2.
//   v2 removes Layer 1 entirely -- no cancel attempts, no replacement orders.
//
//   Layer 2 (sole layer) -- on Account.ExecutionUpdate:
//     - track prior signed position per instrument (the position observed
//       before THIS execution).
//     - compute current signed position post-fill.
//     - if prior != 0 and current != 0 and Sign(current) != Sign(prior):
//         sign flip detected -- trim to flat (closes all of the new exposure
//         that crossed zero).
//     - else if |current| > MaxContracts:
//         trim the excess back to +/- MaxContracts.
//     - skip our own trim orders entirely (Name starts "ApexCap_").
//
//   Tradeoff: Apex may briefly hold up to the master's full size before the
//   trim fires. Operator has accepted this slippage cost ("a few ticks").
//   In return, double-fills are impossible -- the guard only ever issues
//   reductions, never new entries.
// =============================================================================

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ApexContractCapGuard : Strategy
    {
        // ===== Parameters =====
        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "Max Contracts (per instrument)",
                 Description = "Hard cap on |position| per instrument on the guarded account.",
                 GroupName = "1. Cap", Order = 0)]
        public int MaxContracts { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "Apex Account Name (required)",
                 Description = "Exact NT8 account name to guard (e.g. APEX7678400000195).",
                 GroupName = "1. Cap", Order = 1)]
        public string ApexAccountName { get; set; } = "";

        [NinjaScriptProperty]
        [Display(Name = "Print Debug", GroupName = "2. Debug", Order = 0)]
        public bool PrintDebug { get; set; } = false;

        // ===== Internals =====
        private Account targetAccount;
        private readonly Dictionary<Instrument, int> priorSigned = new Dictionary<Instrument, int>();
        private readonly object stateLock = new object();
        private const string GuardOrderPrefix = "ApexCap_";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "ApexContractCapGuard";
                Description = "Per-Apex-account contract cap + no-sign-flip-in-one-execution guard. Layer-2-only: trims any over-cap or sign-flipping fill. No pre-fill cancel-replace (would race-double-fill on copier markets).";
                Calculate   = Calculate.OnBarClose;
                IsExitOnSessionCloseStrategy = false;
                BarsRequiredToTrade = 0;
                IsOverlay   = true;
            }
            else if (State == State.Realtime)
            {
                if (string.IsNullOrWhiteSpace(ApexAccountName))
                {
                    Print("[ApexContractCapGuard] ApexAccountName is empty -- guard idle.");
                    return;
                }

                Account found = null;
                try
                {
                    lock (Account.All)
                    {
                        foreach (var a in Account.All)
                        {
                            if (a != null && !string.IsNullOrEmpty(a.Name) &&
                                a.Name.Equals(ApexAccountName, StringComparison.OrdinalIgnoreCase))
                            {
                                found = a;
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Print("[ApexContractCapGuard] Account lookup failed: " + ex.Message);
                    return;
                }

                if (found == null)
                {
                    Print("[ApexContractCapGuard] Account '" + ApexAccountName + "' not found -- guard idle.");
                    return;
                }

                targetAccount = found;
                targetAccount.ExecutionUpdate += OnAccountExecutionUpdate;

                // Seed priorSigned with current account positions so the first
                // execution after attach is compared against a real baseline,
                // not zero (avoids a phantom "sign-flip" trim on a held position).
                try
                {
                    var snap = new List<Position>(targetAccount.Positions);
                    foreach (var p in snap)
                    {
                        if (p == null || p.Instrument == null) continue;
                        int s = 0;
                        if (p.MarketPosition == MarketPosition.Long)  s =  p.Quantity;
                        else if (p.MarketPosition == MarketPosition.Short) s = -p.Quantity;
                        priorSigned[p.Instrument] = s;
                    }
                }
                catch { /* benign */ }

                Print("[ApexContractCapGuard] Armed (Layer-2-only) on '" + targetAccount.Name +
                      "' | MaxContracts=" + MaxContracts);
            }
            else if (State == State.Terminated)
            {
                if (targetAccount != null)
                {
                    try
                    {
                        targetAccount.ExecutionUpdate -= OnAccountExecutionUpdate;
                    }
                    catch { /* ignore on teardown */ }
                }
            }
        }

        protected override void OnBarUpdate() { /* not used -- event-driven */ }

        // -----------------------------------------------------------------
        // Layer 2 -- trim on every execution; sole layer in v2
        // -----------------------------------------------------------------
        private void OnAccountExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            try
            {
                Execution exec = e.Execution;
                if (exec == null || exec.Instrument == null) return;
                Instrument inst = exec.Instrument;

                bool isOwnTrim = (exec.Order != null && !string.IsNullOrEmpty(exec.Order.Name) &&
                                  exec.Order.Name.StartsWith(GuardOrderPrefix, StringComparison.Ordinal));

                int currentSigned = GetSignedPosition(inst);

                if (isOwnTrim)
                {
                    // Our own trim filled -- just refresh the baseline and return.
                    lock (stateLock) priorSigned[inst] = currentSigned;
                    if (PrintDebug)
                        Print(string.Format("[ApexContractCapGuard] (own-trim filled on {0}, new position={1})",
                            inst.FullName, currentSigned));
                    return;
                }

                int priorVal;
                lock (stateLock)
                {
                    if (!priorSigned.TryGetValue(inst, out priorVal)) priorVal = 0;
                }

                int max = Math.Max(1, MaxContracts);
                int correctiveQty = 0;
                OrderAction correctiveAction = OrderAction.Buy;
                string reason = "";

                bool signFlipped = priorVal != 0 && currentSigned != 0 &&
                                   Math.Sign(currentSigned) != Math.Sign(priorVal);

                if (signFlipped)
                {
                    // Trim ALL of current exposure: bring position back to 0.
                    correctiveQty = Math.Abs(currentSigned);
                    correctiveAction = currentSigned > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
                    reason = string.Format("FLIP-TRIM prior={0} current={1} -> flatten", priorVal, currentSigned);
                }
                else if (Math.Abs(currentSigned) > max)
                {
                    correctiveQty = Math.Abs(currentSigned) - max;
                    correctiveAction = currentSigned > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
                    reason = string.Format("CAP-TRIM current={0} > cap={1} -> excess={2}",
                        currentSigned, max, correctiveQty);
                }

                if (correctiveQty > 0)
                {
                    Print(string.Format("[ApexContractCapGuard] {0} on {1}: submitting {2} {3}",
                        reason, inst.FullName, correctiveAction, correctiveQty));
                    try
                    {
                        Order corrective = targetAccount.CreateOrder(
                            inst,
                            correctiveAction,
                            OrderType.Market,
                            OrderEntry.Automated,
                            TimeInForce.Day,
                            correctiveQty,
                            0.0, 0.0,
                            "",
                            GuardOrderPrefix + "Trim",
                            Core.Globals.MaxDate,
                            null);
                        targetAccount.Submit(new List<Order> { corrective });
                    }
                    catch (Exception ex)
                    {
                        Print("[ApexContractCapGuard] Trim submit failed: " + ex.Message);
                    }
                }
                else if (PrintDebug)
                {
                    Print(string.Format("[ApexContractCapGuard] (pass-through on {0}: prior={1} current={2}, no action)",
                        inst.FullName, priorVal, currentSigned));
                }

                // Refresh baseline AFTER deciding -- next non-own execution
                // compares against THIS execution's post-fill state.
                lock (stateLock) priorSigned[inst] = currentSigned;
            }
            catch (Exception ex)
            {
                Print("[ApexContractCapGuard] ExecutionUpdate handler error: " + ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------
        private int GetSignedPosition(Instrument instrument)
        {
            if (targetAccount == null || instrument == null) return 0;
            try
            {
                var snapshot = new List<Position>(targetAccount.Positions);
                foreach (var pos in snapshot)
                {
                    if (pos == null || pos.Instrument == null) continue;
                    if (pos.Instrument != instrument) continue;
                    if (pos.MarketPosition == MarketPosition.Long)  return  pos.Quantity;
                    if (pos.MarketPosition == MarketPosition.Short) return -pos.Quantity;
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Print("[ApexContractCapGuard] Position lookup error: " + ex.Message);
            }
            return 0;
        }
    }
}
