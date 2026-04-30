#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// V1B-facing shared-memory bus.
    /// Uses a distinct class name so it can coexist with the legacy HUDMessenger
    /// class, while delegating to the same shared maps written by the existing
    /// scanner and HUD components.
    /// </summary>
    public static class HUDMessengerV1B
    {
        public static Dictionary<string, DateTime> SharedSignalMap
        {
            get { return HUDMessenger.SharedSignalMap; }
        }

        public static Dictionary<string, double> SharedLevelMap
        {
            get { return HUDMessenger.SharedLevelMap; }
        }

        public static string CurrentDailyBias
        {
            get { return HUDMessenger.CurrentDailyBias; }
            set { HUDMessenger.CurrentDailyBias = value; }
        }

        public static string CurrentPlaybook
        {
            get { return HUDMessenger.CurrentPlaybook; }
            set { HUDMessenger.CurrentPlaybook = value; }
        }

        public static string CurrentMacroRegime
        {
            get { return HUDMessenger.CurrentMacroRegime; }
            set { HUDMessenger.CurrentMacroRegime = value; }
        }

        public static string CurrentHMMRegime
        {
            get { return HUDMessenger.CurrentHMMRegime; }
            set { HUDMessenger.CurrentHMMRegime = value; }
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
