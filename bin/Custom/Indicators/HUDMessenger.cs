#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public static class HUDMessenger
    {
        // Holds the timestamps of your footprint signals
        public static Dictionary<string, DateTime> SharedSignalMap = new Dictionary<string, DateTime>();
        
        // Holds the exact price levels broadcasted by Trinity HUD
        public static Dictionary<string, double> SharedLevelMap = new Dictionary<string, double>();
        
        public static string CurrentDailyBias { get; set; } = "D"; 

        // =========================================================================
        // V3 GATEKEEPER & DIRECTIONAL ALIGNMENT VARIABLES S
        // =========================================================================
        public static string CurrentPlaybook { get; set; } = "UNKNOWN";
        public static string CurrentMacroRegime { get; set; } = "UNKNOWN";
        public static string CurrentHMMRegime { get; set; } = "UNKNOWN";

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

