#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Static shared-memory bus between Trinity indicators and strategy suite.
    ///
    /// SharedSignalMap Keys (written by OrderFlowSetupScanner):
    ///   Scanner_SIB   — Stacked Imbalances (continuation/breakout)
    ///   Scanner_ABS   — Absorption (reversal at level)
    ///   Scanner_DD    — Delta Divergence (reversal at extreme)
    ///   Scanner_TF    — Trapped Flow (false-breakout reversal)
    ///   Scanner_DT    — Delta Transition (bar-to-bar delta flip)
    ///   Scanner_DEIA  — Delta Exhaustion / Initiative Action (kill-switch grade)
    ///   Scanner_EEMDF — Extreme Max/Min Delta Failure (kill-switch grade)
    ///   Scanner_PAR   — Pullback Absorption Reversal (trend continuation)
    ///   Scanner_DEB   — Delta Expansion Breakout (continuation)
    ///
    /// SharedLevelMap Keys (written by TrinityDataBridge):
    ///   Dev_VAH, Dev_VAL, Dev_POC — Developing RTH value area
    ///   IBH, IBL                  — Initial Balance high/low
    ///   PDH, PDL, PDC             — Prior Day high/low/close
    ///   yVAH, yVAL, yPOC         — Yesterday's value area
    ///   ONH, ONL, ON_POC          — Overnight high/low/POC
    ///   Live_VWAP                 — (written externally by VWAP indicator if wired)
    ///
    /// CurrentDailyBias (written by TradeHUD button clicks):
    ///   "D" = Rotation/D-shape   "P" = Bull Trend (P-shape)
    ///   "b" = Bear Trend (b-shape) "B" = Breakout Expansion
    /// </summary>
    public static class HUDMessenger
    {
        // ── Signal timestamps (written by OrderFlowSetupScanner) ──────────────
        // Values are the bar Time[0] when the signal last fired.
        public static Dictionary<string, DateTime> SharedSignalMap =
            new Dictionary<string, DateTime>();

        // ── Price levels (written by TrinityDataBridge) ───────────────────────
        public static Dictionary<string, double> SharedLevelMap =
            new Dictionary<string, double>();

        // ── Day shape / daily bias (written by TradeHUD button) ───────────────
        public static string CurrentDailyBias { get; set; } = "D";

        // ── V3 Gatekeeper variables (written by MomentumRegimeDisplayHUD) ─────
        public static string CurrentPlaybook    { get; set; } = "UNKNOWN";
        public static string CurrentMacroRegime { get; set; } = "UNKNOWN";
        public static string CurrentHMMRegime   { get; set; } = "UNKNOWN";

        // ── Convenience helper: is a named signal fresh? ──────────────────────
        /// <summary>
        /// Returns true if the named signal exists in SharedSignalMap and was
        /// fired within the last <paramref name="maxMinutes"/> minutes relative
        /// to <paramref name="referenceTime"/> (usually strategy's Time[0]).
        /// </summary>
        public static bool IsSignalFresh(string key, DateTime referenceTime, double maxMinutes)
        {
            DateTime t;
            if (!SharedSignalMap.TryGetValue(key, out t)) return false;
            if (t == DateTime.MinValue) return false;
            double age = (referenceTime - t).TotalMinutes;
            return age >= 0 && age <= maxMinutes;
        }
    }
}
