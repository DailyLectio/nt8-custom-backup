using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class CoreLevels : Indicator
    {
        // Core Level Properties
        [NinjaScriptProperty]
        [Display(Name="Extended High", Order=1, GroupName="Core Levels")]
        public double ExtendedHigh { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Hi Mid 2", Order=2, GroupName="Core Levels")]
        public double HiMid2 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Expected High", Order=3, GroupName="Core Levels")]
        public double ExpectedHigh { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Expected High Hot Zone", Order=4, GroupName="Core Levels")]
        public double ExpectedHighHotZone { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Hi Mid 1", Order=5, GroupName="Core Levels")]
        public double HiMid1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Hi Mid 1 Hot Zone", Order=6, GroupName="Core Levels")]
        public double HiMid1HotZone { get; set; }

        [NinjaScriptProperty]
        [Display(Name="POC (Point of Control)", Order=7, GroupName="Core Levels")]
        public double POC { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Lo Mid 1 Hot Zone", Order=8, GroupName="Core Levels")]
        public double LoMid1HotZone { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Lo Mid 1", Order=9, GroupName="Core Levels")]
        public double LoMid1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Expected Low Hot Zone", Order=10, GroupName="Core Levels")]
        public double ExpectedLowHotZone { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Expected Low", Order=11, GroupName="Core Levels")]
        public double ExpectedLow { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Lo Mid 2", Order=12, GroupName="Core Levels")]
        public double LoMid2 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Extended Low", Order=13, GroupName="Core Levels")]
        public double ExtendedLow { get; set; }

        // Session Levels
        [NinjaScriptProperty]
        [Display(Name="Session 1", Order=14, GroupName="Session Levels")]
        public double Session1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Session 2", Order=15, GroupName="Session Levels")]
        public double Session2 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Euro Session 1", Order=16, GroupName="Session Levels")]
        public double EuroSession1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Euro Session 2", Order=17, GroupName="Session Levels")]
        public double EuroSession2 { get; set; }

        // Extreme Levels
        [NinjaScriptProperty]
        [Display(Name="Extreme High", Order=18, GroupName="Extreme Levels")]
        public double ExtremeHigh { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Hi Mid 3", Order=19, GroupName="Extreme Levels")]
        public double HiMid3 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Lo Mid 3", Order=20, GroupName="Extreme Levels")]
        public double LoMid3 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Extreme Low", Order=21, GroupName="Extreme Levels")]
        public double ExtremeLow { get; set; }

        // Closest Levels
        [NinjaScriptProperty]
        [Display(Name="Closest High", Order=22, GroupName="Closest Levels")]
        public double ClosestHigh { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Closest Low", Order=23, GroupName="Closest Levels")]
        public double ClosestLow { get; set; }

        // Color Properties
        [XmlIgnore]
        [Display(Name="Extended High Color", Order=30, GroupName="Colors")]
        public Brush ExtendedHighColor { get; set; }

        [XmlIgnore]
        [Display(Name="Hi Mid 2 Color", Order=31, GroupName="Colors")]
        public Brush HiMid2Color { get; set; }

        [XmlIgnore]
        [Display(Name="Expected High Color", Order=32, GroupName="Colors")]
        public Brush ExpectedHighColor { get; set; }

        [XmlIgnore]
        [Display(Name="Expected High Hot Zone Color", Order=33, GroupName="Colors")]
        public Brush ExpectedHighHotZoneColor { get; set; }

        [XmlIgnore]
        [Display(Name="Hi Mid 1 Color", Order=34, GroupName="Colors")]
        public Brush HiMid1Color { get; set; }

        [XmlIgnore]
        [Display(Name="Hi Mid 1 Hot Zone Color", Order=35, GroupName="Colors")]
        public Brush HiMid1HotZoneColor { get; set; }

        [XmlIgnore]
        [Display(Name="POC Color", Order=36, GroupName="Colors")]
        public Brush POCColor { get; set; }

        [XmlIgnore]
        [Display(Name="Lo Mid 1 Hot Zone Color", Order=37, GroupName="Colors")]
        public Brush LoMid1HotZoneColor { get; set; }

        [XmlIgnore]
        [Display(Name="Lo Mid 1 Color", Order=38, GroupName="Colors")]
        public Brush LoMid1Color { get; set; }

        [XmlIgnore]
        [Display(Name="Expected Low Hot Zone Color", Order=39, GroupName="Colors")]
        public Brush ExpectedLowHotZoneColor { get; set; }

        [XmlIgnore]
        [Display(Name="Expected Low Color", Order=40, GroupName="Colors")]
        public Brush ExpectedLowColor { get; set; }

        [XmlIgnore]
        [Display(Name="Lo Mid 2 Color", Order=41, GroupName="Colors")]
        public Brush LoMid2Color { get; set; }

        [XmlIgnore]
        [Display(Name="Extended Low Color", Order=42, GroupName="Colors")]
        public Brush ExtendedLowColor { get; set; }

        [XmlIgnore]
        [Display(Name="Session Levels Color", Order=43, GroupName="Colors")]
        public Brush SessionLevelsColor { get; set; }

        [XmlIgnore]
        [Display(Name="Extreme Levels Color", Order=44, GroupName="Colors")]
        public Brush ExtremeLevelsColor { get; set; }

        [XmlIgnore]
        [Display(Name="Closest Levels Color", Order=45, GroupName="Colors")]
        public Brush ClosestLevelsColor { get; set; }

        // Line Thickness Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Core Levels Thickness", Order=50, GroupName="Line Properties")]
        public int CoreLevelsThickness { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Hot Zone Thickness", Order=51, GroupName="Line Properties")]
        public int HotZoneThickness { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="POC Thickness", Order=52, GroupName="Line Properties")]
        public int POCThickness { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Session Levels Thickness", Order=53, GroupName="Line Properties")]
        public int SessionLevelsThickness { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Extreme Levels Thickness", Order=54, GroupName="Line Properties")]
        public int ExtremeLevelsThickness { get; set; }

        // Extension control parameters
        [NinjaScriptProperty]
        [Display(Name = "Left Extension (Bars)", Description = "Bars to extend left", Order = 60, GroupName="Extensions")]
        [Range(1, int.MaxValue)]
        public int LeftExtensionBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Right Extension (Bars)", Description = "Bars to extend right", Order = 61, GroupName="Extensions")]
        [Range(1, int.MaxValue)]
        public int RightExtensionBars { get; set; }

        // Enable/Disable Groups
        [NinjaScriptProperty]
        [Display(Name="Show Hot Zones", Order=70, GroupName="Display Options")]
        public bool ShowHotZones { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Show Session Levels", Order=71, GroupName="Display Options")]
        public bool ShowSessionLevels { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Show Extreme Levels", Order=72, GroupName="Display Options")]
        public bool ShowExtremeLevels { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Show Closest Levels", Order=73, GroupName="Display Options")]
        public bool ShowClosestLevels { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Core Levels indicator with extending lines";
                Name = "CoreLevels";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                // Default level values from your image
                ExtendedHigh = 6120.75;
                HiMid2 = 6099.75;
                ExpectedHigh = 6078.50;
                ExpectedHighHotZone = 6075.75;
                HiMid1 = 6064.25;
                HiMid1HotZone = 6059.50;
                POC = 6050.00;
                LoMid1HotZone = 6040.25;
                LoMid1 = 6035.50;
                ExpectedLowHotZone = 6026.00;
                ExpectedLow = 6021.25;
                LoMid2 = 6000.00;
                ExtendedLow = 5978.50;

                // Session levels (n/a in your image, setting to 0)
                Session1 = 0;
                Session2 = 0;
                EuroSession1 = 0;
                EuroSession2 = 0;

                // Extreme levels from your image
                ExtremeHigh = 6170.50;
                HiMid3 = 6145.75;
                LoMid3 = 5955.50;
                ExtremeLow = 5928.25;

                // Closest levels from your image
                ClosestHigh = 6071.00;
                ClosestLow = 6003.50;

                // Default colors
                ExtendedHighColor = Brushes.Cyan;
                HiMid2Color = Brushes.LightBlue;
                ExpectedHighColor = Brushes.Green;
                ExpectedHighHotZoneColor = Brushes.LightGreen;
                HiMid1Color = Brushes.Yellow;
                HiMid1HotZoneColor = Brushes.LightYellow;
                POCColor = Brushes.Blue;
                LoMid1HotZoneColor = Brushes.LightYellow;
                LoMid1Color = Brushes.Yellow;
                ExpectedLowHotZoneColor = Brushes.LightCoral;
                ExpectedLowColor = Brushes.Red;
                LoMid2Color = Brushes.Magenta;
                ExtendedLowColor = Brushes.Purple;
                SessionLevelsColor = Brushes.Gray;
                ExtremeLevelsColor = Brushes.DarkRed;
                ClosestLevelsColor = Brushes.White;

                // Default thickness
                CoreLevelsThickness = 2;
                HotZoneThickness = 1;
                POCThickness = 3;
                SessionLevelsThickness = 2;
                ExtremeLevelsThickness = 2;

                // Default extensions
                LeftExtensionBars = 20;
                RightExtensionBars = 20;

                // Display options
                ShowHotZones = true;
                ShowSessionLevels = true;
                ShowExtremeLevels = true;
                ShowClosestLevels = true;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            // Calculate extension times
            DateTime startTime = Time[Math.Min(LeftExtensionBars, CurrentBar)];
            DateTime endTime = CalculateEndTime();

            // Draw Core Levels
            DrawLine("ExtendedHigh", ExtendedHigh, ExtendedHighColor, CoreLevelsThickness, startTime, endTime);
            DrawLine("HiMid2", HiMid2, HiMid2Color, CoreLevelsThickness, startTime, endTime);
            DrawLine("ExpectedHigh", ExpectedHigh, ExpectedHighColor, CoreLevelsThickness, startTime, endTime);
            DrawLine("HiMid1", HiMid1, HiMid1Color, CoreLevelsThickness, startTime, endTime);
            DrawLine("POC", POC, POCColor, POCThickness, startTime, endTime);
            DrawLine("LoMid1", LoMid1, LoMid1Color, CoreLevelsThickness, startTime, endTime);
            DrawLine("ExpectedLow", ExpectedLow, ExpectedLowColor, CoreLevelsThickness, startTime, endTime);
            DrawLine("LoMid2", LoMid2, LoMid2Color, CoreLevelsThickness, startTime, endTime);
            DrawLine("ExtendedLow", ExtendedLow, ExtendedLowColor, CoreLevelsThickness, startTime, endTime);

            // Draw Hot Zones if enabled
            if (ShowHotZones)
            {
                DrawLine("ExpectedHighHotZone", ExpectedHighHotZone, ExpectedHighHotZoneColor, HotZoneThickness, startTime, endTime);
                DrawLine("HiMid1HotZone", HiMid1HotZone, HiMid1HotZoneColor, HotZoneThickness, startTime, endTime);
                DrawLine("LoMid1HotZone", LoMid1HotZone, LoMid1HotZoneColor, HotZoneThickness, startTime, endTime);
                DrawLine("ExpectedLowHotZone", ExpectedLowHotZone, ExpectedLowHotZoneColor, HotZoneThickness, startTime, endTime);
            }

            // Draw Session Levels if enabled
            if (ShowSessionLevels)
            {
                if (Session1 > 0)
                    DrawLine("Session1", Session1, SessionLevelsColor, SessionLevelsThickness, startTime, endTime);
                if (Session2 > 0)
                    DrawLine("Session2", Session2, SessionLevelsColor, SessionLevelsThickness, startTime, endTime);
                if (EuroSession1 > 0)
                    DrawLine("EuroSession1", EuroSession1, SessionLevelsColor, SessionLevelsThickness, startTime, endTime);
                if (EuroSession2 > 0)
                    DrawLine("EuroSession2", EuroSession2, SessionLevelsColor, SessionLevelsThickness, startTime, endTime);
            }

            // Draw Extreme Levels if enabled
            if (ShowExtremeLevels)
            {
                if (ExtremeHigh > 0)
                    DrawLine("ExtremeHigh", ExtremeHigh, ExtremeLevelsColor, ExtremeLevelsThickness, startTime, endTime);
                if (HiMid3 > 0)
                    DrawLine("HiMid3", HiMid3, ExtremeLevelsColor, ExtremeLevelsThickness, startTime, endTime);
                if (LoMid3 > 0)
                    DrawLine("LoMid3", LoMid3, ExtremeLevelsColor, ExtremeLevelsThickness, startTime, endTime);
                if (ExtremeLow > 0)
                    DrawLine("ExtremeLow", ExtremeLow, ExtremeLevelsColor, ExtremeLevelsThickness, startTime, endTime);
            }

            // Draw Closest Levels if enabled
            if (ShowClosestLevels)
            {
                if (ClosestHigh > 0)
                    DrawLine("ClosestHigh", ClosestHigh, ClosestLevelsColor, CoreLevelsThickness, startTime, endTime);
                if (ClosestLow > 0)
                    DrawLine("ClosestLow", ClosestLow, ClosestLevelsColor, CoreLevelsThickness, startTime, endTime);
            }
        }

        private DateTime CalculateEndTime()
        {
            int minutesToAdd = 0;
            
            if (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute)
            {
                minutesToAdd = RightExtensionBars * BarsPeriod.Value;
            }
            else if (BarsPeriod.BarsPeriodType == BarsPeriodType.Second)
            {
                minutesToAdd = (RightExtensionBars * BarsPeriod.Value) / 60;
            }
            else if (BarsPeriod.BarsPeriodType == BarsPeriodType.Day)
            {
                return Time[0].AddDays(RightExtensionBars);
            }
            else
            {
                minutesToAdd = RightExtensionBars * 5;
            }
            
            return Time[0].AddMinutes(minutesToAdd);
        }

        private void DrawLine(string tag, double y, Brush brush, int width, DateTime start, DateTime end)
        {
            Draw.Line(this, tag + CurrentBar, false, 
                start, y, 
                end, y, 
                brush, DashStyleHelper.Solid, width);
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private CoreLevels[] cacheCoreLevels;
		public CoreLevels CoreLevels(double extendedHigh, double hiMid2, double expectedHigh, double expectedHighHotZone, double hiMid1, double hiMid1HotZone, double pOC, double loMid1HotZone, double loMid1, double expectedLowHotZone, double expectedLow, double loMid2, double extendedLow, double session1, double session2, double euroSession1, double euroSession2, double extremeHigh, double hiMid3, double loMid3, double extremeLow, double closestHigh, double closestLow, int coreLevelsThickness, int hotZoneThickness, int pOCThickness, int sessionLevelsThickness, int extremeLevelsThickness, int leftExtensionBars, int rightExtensionBars, bool showHotZones, bool showSessionLevels, bool showExtremeLevels, bool showClosestLevels)
		{
			return CoreLevels(Input, extendedHigh, hiMid2, expectedHigh, expectedHighHotZone, hiMid1, hiMid1HotZone, pOC, loMid1HotZone, loMid1, expectedLowHotZone, expectedLow, loMid2, extendedLow, session1, session2, euroSession1, euroSession2, extremeHigh, hiMid3, loMid3, extremeLow, closestHigh, closestLow, coreLevelsThickness, hotZoneThickness, pOCThickness, sessionLevelsThickness, extremeLevelsThickness, leftExtensionBars, rightExtensionBars, showHotZones, showSessionLevels, showExtremeLevels, showClosestLevels);
		}

		public CoreLevels CoreLevels(ISeries<double> input, double extendedHigh, double hiMid2, double expectedHigh, double expectedHighHotZone, double hiMid1, double hiMid1HotZone, double pOC, double loMid1HotZone, double loMid1, double expectedLowHotZone, double expectedLow, double loMid2, double extendedLow, double session1, double session2, double euroSession1, double euroSession2, double extremeHigh, double hiMid3, double loMid3, double extremeLow, double closestHigh, double closestLow, int coreLevelsThickness, int hotZoneThickness, int pOCThickness, int sessionLevelsThickness, int extremeLevelsThickness, int leftExtensionBars, int rightExtensionBars, bool showHotZones, bool showSessionLevels, bool showExtremeLevels, bool showClosestLevels)
		{
			if (cacheCoreLevels != null)
				for (int idx = 0; idx < cacheCoreLevels.Length; idx++)
					if (cacheCoreLevels[idx] != null && cacheCoreLevels[idx].ExtendedHigh == extendedHigh && cacheCoreLevels[idx].HiMid2 == hiMid2 && cacheCoreLevels[idx].ExpectedHigh == expectedHigh && cacheCoreLevels[idx].ExpectedHighHotZone == expectedHighHotZone && cacheCoreLevels[idx].HiMid1 == hiMid1 && cacheCoreLevels[idx].HiMid1HotZone == hiMid1HotZone && cacheCoreLevels[idx].POC == pOC && cacheCoreLevels[idx].LoMid1HotZone == loMid1HotZone && cacheCoreLevels[idx].LoMid1 == loMid1 && cacheCoreLevels[idx].ExpectedLowHotZone == expectedLowHotZone && cacheCoreLevels[idx].ExpectedLow == expectedLow && cacheCoreLevels[idx].LoMid2 == loMid2 && cacheCoreLevels[idx].ExtendedLow == extendedLow && cacheCoreLevels[idx].Session1 == session1 && cacheCoreLevels[idx].Session2 == session2 && cacheCoreLevels[idx].EuroSession1 == euroSession1 && cacheCoreLevels[idx].EuroSession2 == euroSession2 && cacheCoreLevels[idx].ExtremeHigh == extremeHigh && cacheCoreLevels[idx].HiMid3 == hiMid3 && cacheCoreLevels[idx].LoMid3 == loMid3 && cacheCoreLevels[idx].ExtremeLow == extremeLow && cacheCoreLevels[idx].ClosestHigh == closestHigh && cacheCoreLevels[idx].ClosestLow == closestLow && cacheCoreLevels[idx].CoreLevelsThickness == coreLevelsThickness && cacheCoreLevels[idx].HotZoneThickness == hotZoneThickness && cacheCoreLevels[idx].POCThickness == pOCThickness && cacheCoreLevels[idx].SessionLevelsThickness == sessionLevelsThickness && cacheCoreLevels[idx].ExtremeLevelsThickness == extremeLevelsThickness && cacheCoreLevels[idx].LeftExtensionBars == leftExtensionBars && cacheCoreLevels[idx].RightExtensionBars == rightExtensionBars && cacheCoreLevels[idx].ShowHotZones == showHotZones && cacheCoreLevels[idx].ShowSessionLevels == showSessionLevels && cacheCoreLevels[idx].ShowExtremeLevels == showExtremeLevels && cacheCoreLevels[idx].ShowClosestLevels == showClosestLevels && cacheCoreLevels[idx].EqualsInput(input))
						return cacheCoreLevels[idx];
			return CacheIndicator<CoreLevels>(new CoreLevels(){ ExtendedHigh = extendedHigh, HiMid2 = hiMid2, ExpectedHigh = expectedHigh, ExpectedHighHotZone = expectedHighHotZone, HiMid1 = hiMid1, HiMid1HotZone = hiMid1HotZone, POC = pOC, LoMid1HotZone = loMid1HotZone, LoMid1 = loMid1, ExpectedLowHotZone = expectedLowHotZone, ExpectedLow = expectedLow, LoMid2 = loMid2, ExtendedLow = extendedLow, Session1 = session1, Session2 = session2, EuroSession1 = euroSession1, EuroSession2 = euroSession2, ExtremeHigh = extremeHigh, HiMid3 = hiMid3, LoMid3 = loMid3, ExtremeLow = extremeLow, ClosestHigh = closestHigh, ClosestLow = closestLow, CoreLevelsThickness = coreLevelsThickness, HotZoneThickness = hotZoneThickness, POCThickness = pOCThickness, SessionLevelsThickness = sessionLevelsThickness, ExtremeLevelsThickness = extremeLevelsThickness, LeftExtensionBars = leftExtensionBars, RightExtensionBars = rightExtensionBars, ShowHotZones = showHotZones, ShowSessionLevels = showSessionLevels, ShowExtremeLevels = showExtremeLevels, ShowClosestLevels = showClosestLevels }, input, ref cacheCoreLevels);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.CoreLevels CoreLevels(double extendedHigh, double hiMid2, double expectedHigh, double expectedHighHotZone, double hiMid1, double hiMid1HotZone, double pOC, double loMid1HotZone, double loMid1, double expectedLowHotZone, double expectedLow, double loMid2, double extendedLow, double session1, double session2, double euroSession1, double euroSession2, double extremeHigh, double hiMid3, double loMid3, double extremeLow, double closestHigh, double closestLow, int coreLevelsThickness, int hotZoneThickness, int pOCThickness, int sessionLevelsThickness, int extremeLevelsThickness, int leftExtensionBars, int rightExtensionBars, bool showHotZones, bool showSessionLevels, bool showExtremeLevels, bool showClosestLevels)
		{
			return indicator.CoreLevels(Input, extendedHigh, hiMid2, expectedHigh, expectedHighHotZone, hiMid1, hiMid1HotZone, pOC, loMid1HotZone, loMid1, expectedLowHotZone, expectedLow, loMid2, extendedLow, session1, session2, euroSession1, euroSession2, extremeHigh, hiMid3, loMid3, extremeLow, closestHigh, closestLow, coreLevelsThickness, hotZoneThickness, pOCThickness, sessionLevelsThickness, extremeLevelsThickness, leftExtensionBars, rightExtensionBars, showHotZones, showSessionLevels, showExtremeLevels, showClosestLevels);
		}

		public Indicators.CoreLevels CoreLevels(ISeries<double> input , double extendedHigh, double hiMid2, double expectedHigh, double expectedHighHotZone, double hiMid1, double hiMid1HotZone, double pOC, double loMid1HotZone, double loMid1, double expectedLowHotZone, double expectedLow, double loMid2, double extendedLow, double session1, double session2, double euroSession1, double euroSession2, double extremeHigh, double hiMid3, double loMid3, double extremeLow, double closestHigh, double closestLow, int coreLevelsThickness, int hotZoneThickness, int pOCThickness, int sessionLevelsThickness, int extremeLevelsThickness, int leftExtensionBars, int rightExtensionBars, bool showHotZones, bool showSessionLevels, bool showExtremeLevels, bool showClosestLevels)
		{
			return indicator.CoreLevels(input, extendedHigh, hiMid2, expectedHigh, expectedHighHotZone, hiMid1, hiMid1HotZone, pOC, loMid1HotZone, loMid1, expectedLowHotZone, expectedLow, loMid2, extendedLow, session1, session2, euroSession1, euroSession2, extremeHigh, hiMid3, loMid3, extremeLow, closestHigh, closestLow, coreLevelsThickness, hotZoneThickness, pOCThickness, sessionLevelsThickness, extremeLevelsThickness, leftExtensionBars, rightExtensionBars, showHotZones, showSessionLevels, showExtremeLevels, showClosestLevels);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.CoreLevels CoreLevels(double extendedHigh, double hiMid2, double expectedHigh, double expectedHighHotZone, double hiMid1, double hiMid1HotZone, double pOC, double loMid1HotZone, double loMid1, double expectedLowHotZone, double expectedLow, double loMid2, double extendedLow, double session1, double session2, double euroSession1, double euroSession2, double extremeHigh, double hiMid3, double loMid3, double extremeLow, double closestHigh, double closestLow, int coreLevelsThickness, int hotZoneThickness, int pOCThickness, int sessionLevelsThickness, int extremeLevelsThickness, int leftExtensionBars, int rightExtensionBars, bool showHotZones, bool showSessionLevels, bool showExtremeLevels, bool showClosestLevels)
		{
			return indicator.CoreLevels(Input, extendedHigh, hiMid2, expectedHigh, expectedHighHotZone, hiMid1, hiMid1HotZone, pOC, loMid1HotZone, loMid1, expectedLowHotZone, expectedLow, loMid2, extendedLow, session1, session2, euroSession1, euroSession2, extremeHigh, hiMid3, loMid3, extremeLow, closestHigh, closestLow, coreLevelsThickness, hotZoneThickness, pOCThickness, sessionLevelsThickness, extremeLevelsThickness, leftExtensionBars, rightExtensionBars, showHotZones, showSessionLevels, showExtremeLevels, showClosestLevels);
		}

		public Indicators.CoreLevels CoreLevels(ISeries<double> input , double extendedHigh, double hiMid2, double expectedHigh, double expectedHighHotZone, double hiMid1, double hiMid1HotZone, double pOC, double loMid1HotZone, double loMid1, double expectedLowHotZone, double expectedLow, double loMid2, double extendedLow, double session1, double session2, double euroSession1, double euroSession2, double extremeHigh, double hiMid3, double loMid3, double extremeLow, double closestHigh, double closestLow, int coreLevelsThickness, int hotZoneThickness, int pOCThickness, int sessionLevelsThickness, int extremeLevelsThickness, int leftExtensionBars, int rightExtensionBars, bool showHotZones, bool showSessionLevels, bool showExtremeLevels, bool showClosestLevels)
		{
			return indicator.CoreLevels(input, extendedHigh, hiMid2, expectedHigh, expectedHighHotZone, hiMid1, hiMid1HotZone, pOC, loMid1HotZone, loMid1, expectedLowHotZone, expectedLow, loMid2, extendedLow, session1, session2, euroSession1, euroSession2, extremeHigh, hiMid3, loMid3, extremeLow, closestHigh, closestLow, coreLevelsThickness, hotZoneThickness, pOCThickness, sessionLevelsThickness, extremeLevelsThickness, leftExtensionBars, rightExtensionBars, showHotZones, showSessionLevels, showExtremeLevels, showClosestLevels);
		}
	}
}

#endregion
