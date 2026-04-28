#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.NinjaScript;
#endregion

// ==========================================================
// IMPORTANT:
// Put the enum in the ROOT namespace so NT generated code in
// NinjaTrader.NinjaScript / Strategies / MarketAnalyzerColumns
// can reference it as "OTFThemePreset" without qualification.
// ==========================================================
namespace NinjaTrader.NinjaScript
{
    public enum OTFThemePreset
    {
        Custom = 0,
        Dark   = 1,
        Light  = 2
    }
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class OneTimeFramingOutline : Indicator
    {
        // =========================
        // Safe brush serialization (no NinjaTrader.Serialize dependency)
        // Stores solid colors as "#AARRGGBB"
        // =========================
        private static string BrushToStringSafe(Brush b)
        {
            if (b is SolidColorBrush scb)
                return scb.Color.ToString(); // "#AARRGGBB"
            return null;
        }

        private static Brush StringToBrushSafe(string s, Brush fallback)
        {
            if (string.IsNullOrWhiteSpace(s))
                return fallback;

            try
            {
                var obj = ColorConverter.ConvertFromString(s);
                if (obj is Color c)
                {
                    var b = new SolidColorBrush(c);
                    if (b.CanFreeze) b.Freeze();
                    return b;
                }
            }
            catch { }

            return fallback;
        }

        // =========================
        // Presets
        // =========================
        [NinjaScriptProperty]
        [Display(Name = "Theme Preset", GroupName = "0. Presets", Order = 0)]
        public OTFThemePreset Preset { get; set; } = OTFThemePreset.Custom;

        // =========================
        // Behavior
        // =========================
        [NinjaScriptProperty]
        [Display(Name = "Color on each tick (vs bar close)", GroupName = "1. Behavior", Order = 0)]
        public bool ColorIntrabar { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Color Entire Candle", GroupName = "1. Behavior", Order = 1)]
        public bool ColorEntireCandle { get; set; } = false;

        // =========================
        // Alerts
        // =========================
        [NinjaScriptProperty]
        [Display(Name = "Enable Alerts", GroupName = "2. Alerts", Order = 0)]
        public bool EnableAlerts { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 3600)]
        [Display(Name = "Rearm Seconds (0 = none)", GroupName = "2. Alerts", Order = 1)]
        public int RearmSeconds { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Sound File", GroupName = "2. Alerts", Order = 2)]
        public string SoundFile { get; set; } = "Alert1.wav";

        // =========================
        // CUSTOM colors (persisted)
        // =========================
        [XmlIgnore]
        [Display(Name = "Custom: Broke High", GroupName = "3. Colors (Custom)", Order = 0)]
        public Brush CustomBrokeHigh { get; set; } = Brushes.Yellow;

        [Browsable(false)]
        [XmlElement("CustomBrokeHigh")]
        public string CustomBrokeHighSerialize
        {
            get => BrushToStringSafe(CustomBrokeHigh);
            set => CustomBrokeHigh = StringToBrushSafe(value, Brushes.Yellow);
        }

        [XmlIgnore]
        [Display(Name = "Custom: Broke Low", GroupName = "3. Colors (Custom)", Order = 1)]
        public Brush CustomBrokeLow { get; set; } = Brushes.Purple;

        [Browsable(false)]
        [XmlElement("CustomBrokeLow")]
        public string CustomBrokeLowSerialize
        {
            get => BrushToStringSafe(CustomBrokeLow);
            set => CustomBrokeLow = StringToBrushSafe(value, Brushes.Purple);
        }

        [XmlIgnore]
        [Display(Name = "Custom: Broke Both", GroupName = "3. Colors (Custom)", Order = 2)]
        public Brush CustomBrokeBoth { get; set; } = Brushes.Cyan;

        [Browsable(false)]
        [XmlElement("CustomBrokeBoth")]
        public string CustomBrokeBothSerialize
        {
            get => BrushToStringSafe(CustomBrokeBoth);
            set => CustomBrokeBoth = StringToBrushSafe(value, Brushes.Cyan);
        }

        // =========================
        // DARK preset (persisted)
        // =========================
        [XmlIgnore]
        [Display(Name = "Dark: Broke High", GroupName = "4. Colors (Dark)", Order = 0)]
        public Brush DarkBrokeHigh { get; set; } = Brushes.Yellow;

        [Browsable(false)]
        [XmlElement("DarkBrokeHigh")]
        public string DarkBrokeHighSerialize
        {
            get => BrushToStringSafe(DarkBrokeHigh);
            set => DarkBrokeHigh = StringToBrushSafe(value, Brushes.Yellow);
        }

        [XmlIgnore]
        [Display(Name = "Dark: Broke Low", GroupName = "4. Colors (Dark)", Order = 1)]
        public Brush DarkBrokeLow { get; set; } = Brushes.Purple;

        [Browsable(false)]
        [XmlElement("DarkBrokeLow")]
        public string DarkBrokeLowSerialize
        {
            get => BrushToStringSafe(DarkBrokeLow);
            set => DarkBrokeLow = StringToBrushSafe(value, Brushes.Purple);
        }

        [XmlIgnore]
        [Display(Name = "Dark: Broke Both", GroupName = "4. Colors (Dark)", Order = 2)]
        public Brush DarkBrokeBoth { get; set; } = Brushes.Cyan;

        [Browsable(false)]
        [XmlElement("DarkBrokeBoth")]
        public string DarkBrokeBothSerialize
        {
            get => BrushToStringSafe(DarkBrokeBoth);
            set => DarkBrokeBoth = StringToBrushSafe(value, Brushes.Cyan);
        }

        // =========================
        // LIGHT preset (persisted)
        // =========================
        [XmlIgnore]
        [Display(Name = "Light: Broke High", GroupName = "5. Colors (Light)", Order = 0)]
        public Brush LightBrokeHigh { get; set; } = Brushes.Goldenrod;

        [Browsable(false)]
        [XmlElement("LightBrokeHigh")]
        public string LightBrokeHighSerialize
        {
            get => BrushToStringSafe(LightBrokeHigh);
            set => LightBrokeHigh = StringToBrushSafe(value, Brushes.Goldenrod);
        }

        [XmlIgnore]
        [Display(Name = "Light: Broke Low", GroupName = "5. Colors (Light)", Order = 1)]
        public Brush LightBrokeLow { get; set; } = Brushes.MediumPurple;

        [Browsable(false)]
        [XmlElement("LightBrokeLow")]
        public string LightBrokeLowSerialize
        {
            get => BrushToStringSafe(LightBrokeLow);
            set => LightBrokeLow = StringToBrushSafe(value, Brushes.MediumPurple);
        }

        [XmlIgnore]
        [Display(Name = "Light: Broke Both", GroupName = "5. Colors (Light)", Order = 2)]
        public Brush LightBrokeBoth { get; set; } = Brushes.DeepSkyBlue;

        [Browsable(false)]
        [XmlElement("LightBrokeBoth")]
        public string LightBrokeBothSerialize
        {
            get => BrushToStringSafe(LightBrokeBoth);
            set => LightBrokeBoth = StringToBrushSafe(value, Brushes.DeepSkyBlue);
        }

        // =========================
        // Internals
        // =========================
        private int lastState = -1;
        private int lastAlertBar = -1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "One Time Framing — Outline";
                IsOverlay = true;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = true;
                Calculate = Calculate.OnEachTick;
            }
            else if (State == State.Configure)
            {
                Calculate = ColorIntrabar ? Calculate.OnEachTick : Calculate.OnBarClose;
            }
        }

        private void GetActiveBrushes(out Brush h, out Brush l, out Brush b)
        {
            switch (Preset)
            {
                case OTFThemePreset.Dark:
                    h = DarkBrokeHigh; l = DarkBrokeLow; b = DarkBrokeBoth; break;
                case OTFThemePreset.Light:
                    h = LightBrokeHigh; l = LightBrokeLow; b = LightBrokeBoth; break;
                default:
                    h = CustomBrokeHigh; l = CustomBrokeLow; b = CustomBrokeBoth; break;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            bool bh = High[0] > High[1];
            bool bl = Low[0] < Low[1];

            int state = (bh && bl) ? 3 : (bh ? 1 : (bl ? 2 : 0));

            GetActiveBrushes(out Brush h, out Brush l, out Brush b);
            Brush outline = state == 3 ? b : state == 2 ? l : state == 1 ? h : null;

            CandleOutlineBrush = outline;
            CandleOutlineBrushes[0] = outline;

            if (ColorEntireCandle)
            {
                BarBrush = outline;
                BarBrushes[0] = outline;
            }
            else
            {
                BarBrush = null;
                BarBrushes[0] = null;
            }

            if (EnableAlerts && state != lastState)
            {
                if (CurrentBar != lastAlertBar || RearmSeconds > 0)
                {
                    string msg =
                        state == 3 ? "Outside bar: broke prior high AND low."
                      : state == 2 ? "Broke prior LOW."
                      : state == 1 ? "Broke prior HIGH."
                      : "No break.";

                    string alertId = $"OTF_{Instrument?.FullName}_{BarsPeriod?.ToString()}";
                    Alert(alertId, Priority.High, msg, SoundFile, RearmSeconds, Brushes.Black, Brushes.White);

                    lastAlertBar = CurrentBar;
                }
            }

            lastState = state;
        }
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private OneTimeFramingOutline[] cacheOneTimeFramingOutline;
		public OneTimeFramingOutline OneTimeFramingOutline(OTFThemePreset preset, bool colorIntrabar, bool colorEntireCandle, bool enableAlerts, int rearmSeconds, string soundFile)
		{
			return OneTimeFramingOutline(Input, preset, colorIntrabar, colorEntireCandle, enableAlerts, rearmSeconds, soundFile);
		}

		public OneTimeFramingOutline OneTimeFramingOutline(ISeries<double> input, OTFThemePreset preset, bool colorIntrabar, bool colorEntireCandle, bool enableAlerts, int rearmSeconds, string soundFile)
		{
			if (cacheOneTimeFramingOutline != null)
				for (int idx = 0; idx < cacheOneTimeFramingOutline.Length; idx++)
					if (cacheOneTimeFramingOutline[idx] != null && cacheOneTimeFramingOutline[idx].Preset == preset && cacheOneTimeFramingOutline[idx].ColorIntrabar == colorIntrabar && cacheOneTimeFramingOutline[idx].ColorEntireCandle == colorEntireCandle && cacheOneTimeFramingOutline[idx].EnableAlerts == enableAlerts && cacheOneTimeFramingOutline[idx].RearmSeconds == rearmSeconds && cacheOneTimeFramingOutline[idx].SoundFile == soundFile && cacheOneTimeFramingOutline[idx].EqualsInput(input))
						return cacheOneTimeFramingOutline[idx];
			return CacheIndicator<OneTimeFramingOutline>(new OneTimeFramingOutline(){ Preset = preset, ColorIntrabar = colorIntrabar, ColorEntireCandle = colorEntireCandle, EnableAlerts = enableAlerts, RearmSeconds = rearmSeconds, SoundFile = soundFile }, input, ref cacheOneTimeFramingOutline);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.OneTimeFramingOutline OneTimeFramingOutline(OTFThemePreset preset, bool colorIntrabar, bool colorEntireCandle, bool enableAlerts, int rearmSeconds, string soundFile)
		{
			return indicator.OneTimeFramingOutline(Input, preset, colorIntrabar, colorEntireCandle, enableAlerts, rearmSeconds, soundFile);
		}

		public Indicators.OneTimeFramingOutline OneTimeFramingOutline(ISeries<double> input , OTFThemePreset preset, bool colorIntrabar, bool colorEntireCandle, bool enableAlerts, int rearmSeconds, string soundFile)
		{
			return indicator.OneTimeFramingOutline(input, preset, colorIntrabar, colorEntireCandle, enableAlerts, rearmSeconds, soundFile);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.OneTimeFramingOutline OneTimeFramingOutline(OTFThemePreset preset, bool colorIntrabar, bool colorEntireCandle, bool enableAlerts, int rearmSeconds, string soundFile)
		{
			return indicator.OneTimeFramingOutline(Input, preset, colorIntrabar, colorEntireCandle, enableAlerts, rearmSeconds, soundFile);
		}

		public Indicators.OneTimeFramingOutline OneTimeFramingOutline(ISeries<double> input , OTFThemePreset preset, bool colorIntrabar, bool colorEntireCandle, bool enableAlerts, int rearmSeconds, string soundFile)
		{
			return indicator.OneTimeFramingOutline(input, preset, colorIntrabar, colorEntireCandle, enableAlerts, rearmSeconds, soundFile);
		}
	}
}

#endregion
