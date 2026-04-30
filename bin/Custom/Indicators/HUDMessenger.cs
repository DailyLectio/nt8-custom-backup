#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class HUDMessenger : Indicator
    {
        // Holds the timestamps of your footprint signals.
        public static Dictionary<string, DateTime> SharedSignalMap = new Dictionary<string, DateTime>();

        // Holds the exact price levels broadcasted by Trinity HUD.
        public static Dictionary<string, double> SharedLevelMap = new Dictionary<string, double>();

        public static string CurrentDailyBias { get; set; } = "D";

        // V3 gatekeeper and directional alignment variables.
        public static string CurrentPlaybook { get; set; } = "UNKNOWN";
        public static string CurrentMacroRegime { get; set; } = "UNKNOWN";
        public static string CurrentHMMRegime { get; set; } = "UNKNOWN";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Shared HUD message bus used by scanner, HUD, and strategy scripts.";
                Name = "HUDMessenger";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = false;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = true;
            }
        }

        public static bool IsSignalFresh(string key, DateTime referenceTime, double maxMinutes)
        {
            DateTime signalTime;
            if (!SharedSignalMap.TryGetValue(key, out signalTime)) return false;
            if (signalTime == DateTime.MinValue) return false;

            double ageMinutes = (referenceTime - signalTime).TotalMinutes;
            return ageMinutes >= 0 && ageMinutes <= maxMinutes;
        }
    }
}
