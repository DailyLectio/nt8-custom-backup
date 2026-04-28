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
        // V3 GATEKEEPER & DIRECTIONAL ALIGNMENT VARIABLES
        // =========================================================================
        public static string CurrentPlaybook { get; set; } = "UNKNOWN";
        public static string CurrentMacroRegime { get; set; } = "UNKNOWN";
        public static string CurrentHMMRegime { get; set; } = "UNKNOWN";
    }
}
