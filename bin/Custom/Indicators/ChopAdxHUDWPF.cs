#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;

using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;          // SimpleFont
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Media;           // Brushes
#endregion

// Make the enum visible to all child namespaces (Indicators/Strategies/Columns)
namespace NinjaTrader.NinjaScript
{
    public enum HudCorner { TopLeft, TopRight, BottomLeft, BottomRight }
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class ChopAdxHUD_WPF : Indicator
    {	
		public bool syncGreen; // This allows the Strategy to read the value
		
		// ===== Inputs
        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name = "ADX Length", GroupName = "Inputs", Order = 0)]
        public int AdxLength { get; set; }

        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name = "CHOP Length", GroupName = "Inputs", Order = 1)]
        public int ChopLength { get; set; }

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Chop Watch Level", GroupName = "Inputs", Order = 2)]
        public int ChopWatch { get; set; }

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "ADX Watch Level", GroupName = "Inputs", Order = 3)]
        public int AdxWatch { get; set; }

        [NinjaScriptProperty, Range(0, 10)]
        [Display(Name = "N Allowance (bars)", GroupName = "Inputs", Order = 4)]
        public int NAllowance { get; set; }

        [NinjaScriptProperty, Range(0.0, 5.0)]
        [Display(Name = "Flat Tolerance %", GroupName = "Inputs", Order = 5)]
        public double FlatTolerancePct { get; set; }

        // ===== HUD placement
        [NinjaScriptProperty]
        [Display(Name = "Anchor Corner", GroupName = "HUD Layout", Order = 0)]
        public HudCorner AnchorCorner { get; set; }

        [NinjaScriptProperty, Range(0, 500)]
        [Display(Name = "X Offset (bars)", GroupName = "HUD Layout", Order = 1)]
        public int XOffsetBars { get; set; }

        [NinjaScriptProperty, Range(0, 2000)]
        [Display(Name = "Y Offset (ticks)", GroupName = "HUD Layout", Order = 2)]
        public int YOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Manual Anchor (override corner)", GroupName = "HUD Layout", Order = 3)]
        public bool ManualAnchor { get; set; }

        [NinjaScriptProperty, Range(0, 100000)]
        [Display(Name = "Anchor BarsAgo", GroupName = "HUD Layout", Order = 4)]
        public int AnchorBarsAgo { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Anchor Price", GroupName = "HUD Layout", Order = 5)]
        public double AnchorPrice { get; set; }

        [NinjaScriptProperty, Range(1, 10)]
        [Display(Name = "Bars per Tile", GroupName = "HUD Visual", Order = 0)]
        public int BarsPerTile { get; set; }

        [NinjaScriptProperty, Range(5, 300)]
        [Display(Name = "Tile Height (ticks)", GroupName = "HUD Visual", Order = 1)]
        public int TileHeightTicks { get; set; }

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name = "Tile Opacity %", GroupName = "HUD Visual", Order = 2)]
        public int TileOpacityPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Text Labels", GroupName = "HUD Visual", Order = 3)]
        public bool ShowText { get; set; }

        // ===== Alerts
        [NinjaScriptProperty]
        [Display(Name = "Alert on SYNC Green", GroupName = "Alerts", Order = 0)]
        public bool AlertOnSync { get; set; }

        [NinjaScriptProperty, Range(0, 600)]
        [Display(Name = "Alert Cooldown (sec)", GroupName = "Alerts", Order = 1)]
        public int AlertCooldownSec { get; set; }

        // ===== Internals
        private ADX adx;
        private Series<double> chopSeries;
        private int notDropChop, flatRunChop;
        private int notRiseAdx,  flatRunAdx;
        private DateTime lastAlert = Core.Globals.MinDate;

        private SimpleFont lblFont  = new SimpleFont("Segoe UI", 13);
        private SimpleFont mainFont = new SimpleFont("Segoe UI Semibold", 16) { Bold = true };

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "ChopAdxHUD_WPF";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = true;
                DisplayInDataBox         = false;
                IsSuspendedWhileInactive = true;

                AdxLength       = 14;
                ChopLength      = 14;
                ChopWatch       = 60;
                AdxWatch        = 18;
                NAllowance      = 2;
                FlatTolerancePct= 0.25;

                AnchorCorner    = HudCorner.BottomRight;
                XOffsetBars     = 1;
                YOffsetTicks    = 40;
                ManualAnchor    = false;
                AnchorBarsAgo   = 0;
                AnchorPrice     = 0;

                BarsPerTile     = 3;
                TileHeightTicks = 48;
                TileOpacityPct  = 85;
                ShowText        = true;

                AlertOnSync     = true;
                AlertCooldownSec= 60;

                AddPlot(Brushes.Transparent, "Dummy");
            }
            else if (State == State.DataLoaded)
            {
                adx        = ADX(AdxLength);
                chopSeries = new Series<double>(this);
                notDropChop = 0; flatRunChop = 0;
                notRiseAdx  = 0; flatRunAdx  = 0;
            }
        }

		 protected override void OnBarUpdate()
		{
		    if (CurrentBar < Math.Max(ChopLength, AdxLength) + 2)
		    {
		        Values[0][0] = 0;
		        syncGreen = false; // Reset to safe state
		        return;
		    }
		
		    // ---- Compute Chop (0..100)
		    double hh    = MAX(High, ChopLength)[0];
		    double ll    = MIN(Low,  ChopLength)[0];
		    double range = Math.Max(TickSize, hh - ll);
		    double sumTr = ATR(ChopLength)[0] * ChopLength;              // approximation
		    double chop  = 100.0 * Math.Log10(sumTr / range) / Math.Log10(ChopLength);
		    chop         = Math.Max(0, Math.Min(100, chop));
		    chopSeries[0]= chop;
		
		    // ---- Slope/flat accounting (Chop)
		    double dChop = chopSeries[0] - chopSeries[1];
		    double chopTol = Math.Max(1e-9, Math.Abs(chopSeries[1]) * (FlatTolerancePct / 100.0));
		    bool   chopFlat = Math.Abs(dChop) <= chopTol;
		    flatRunChop = chopFlat ? flatRunChop + 1 : 0;
		    notDropChop = (dChop >= 0 || chopFlat) ? notDropChop + 1 : 0;
		    bool chopStillDropping = notDropChop <= NAllowance;
		
		    // ---- ADX slope/flat accounting
		    double adxNow = adx[0];
		    double dAdx   = adx[0] - adx[1];
		    double adxTol = Math.Max(1e-9, Math.Abs(adx[1]) * (FlatTolerancePct / 100.0));
		    bool   adxFlat = Math.Abs(dAdx) <= adxTol;
		    flatRunAdx = adxFlat ? flatRunAdx + 1 : 0;
		    notRiseAdx = (dAdx <= 0 || adxFlat) ? notRiseAdx + 1 : 0;
		    
		    // adxRisingRegime logic
		    bool adxRisingRegime = (adxNow >= AdxWatch) && (dAdx > 0 || notRiseAdx <= NAllowance);
		
		    // ---- Colors (Chop)
		    Brush chopBrush;
		    if (flatRunChop >= 2)                             chopBrush = Brushes.Orange;
		    else if (chop >= ChopWatch && dChop >= 0)         chopBrush = Brushes.Yellow;
		    else if (chop <  ChopWatch && chopStillDropping) chopBrush = Brushes.LimeGreen;
		    else if (dChop < 0)                               chopBrush = Brushes.DodgerBlue;
		    else if (dChop > 0)                               chopBrush = Brushes.Gray;
		    else                                              chopBrush = Brushes.Orange;
		
		    // ---- Colors (ADX)
		    Brush adxBrush;
		    if (adxNow < AdxWatch)                                adxBrush = Brushes.Yellow;
		    else if (Math.Abs(dAdx) <= adxTol && flatRunAdx >= 2) adxBrush = Brushes.Orange;
		    else if (dAdx > 0)                                    adxBrush = Brushes.DodgerBlue;
		    else if (adxRisingRegime)                             adxBrush = Brushes.LimeGreen;
		    else if (dAdx < 0)                                    adxBrush = Brushes.Gray;
		    else                                                  adxBrush = Brushes.Orange;
		
		    // ---- Sync Logic
		    bool chopDroppingRegime = (chop < ChopWatch) && (dChop < 0 || chopStillDropping);
		    
		    // ASSIGNMENT TO PUBLIC VARIABLE (Fixes Strategy Error CS1061)
		    syncGreen = adxRisingRegime && chopDroppingRegime; 
		    
		    Brush syncBrush = syncGreen ? Brushes.LimeGreen : Brushes.DarkGray;
		
		    // ---- HUD & Alerts
		    DrawHud(chopBrush, adxBrush, syncBrush, chop, adxNow, dChop, dAdx, syncGreen);
		
		    if (AlertOnSync && syncGreen && (Time[0] - lastAlert).TotalSeconds >= AlertCooldownSec)
		    {
		        Alert("ChopAdxHUD_SYNC", Priority.High,
		              $"SYNC GREEN | ADX {adxNow:F1}↑  | CHOP {chop:F0}↓",
		              "Alert1.wav", 10, Brushes.White, Brushes.DarkGreen);
		        lastAlert = Time[0];
		    }
		}
        private void DrawHud(Brush chopBrush, Brush adxBrush, Brush syncBrush,
                             double chop, double adxNow, double dChop, double dAdx, bool syncGreen)
        {
            int   span     = Math.Max(1, BarsPerTile);
            int   opacity  = Math.Max(0, Math.Min(100, TileOpacityPct));
            double h       = Math.Max(5 * TickSize, TileHeightTicks * TickSize);

            // --- Determine anchor X (barsAgo) and baseY (price) ---
            int anchorX;
            double baseY;

            if (ManualAnchor)
            {
                anchorX = Math.Max(0, AnchorBarsAgo);
                baseY   = AnchorPrice != 0 ? AnchorPrice : Close[0];
            }
            else
            {
                int leftBarsAgo = 0;
                try
                {
                    if (ChartBars != null && ChartBars.FromIndex >= 0)
                        leftBarsAgo = Math.Max(0, CurrentBar - ChartBars.FromIndex);
                }
                catch { leftBarsAgo = 0; }

                bool anchorRight = (AnchorCorner == HudCorner.TopRight || AnchorCorner == HudCorner.BottomRight);
                anchorX = anchorRight ? Math.Max(0, XOffsetBars) : Math.Max(0, leftBarsAgo - Math.Max(0, XOffsetBars));

                bool top = (AnchorCorner == HudCorner.TopLeft || AnchorCorner == HudCorner.TopRight);
                if (top)
                    baseY = MAX(High, 50)[0] + Math.Max(0, YOffsetTicks) * TickSize;
                else
                    baseY = MIN(Low, 50)[0]  - Math.Max(0, YOffsetTicks) * TickSize;
            }

            bool drawFromRight = (ManualAnchor && AnchorBarsAgo <= 0) ||
                                 AnchorCorner == HudCorner.TopRight || AnchorCorner == HudCorner.BottomRight;

            int cursor = 0;
            Action<string,string,Brush> tile = (tag, label, fill) =>
            {
                int start, end;
                if (drawFromRight)
                {
                    end   = anchorX + cursor;
                    start = end + span;
                }
                else
                {
                    start = Math.Max(0, anchorX - cursor);
                    end   = Math.Max(0, start - span);
                }

                Draw.Rectangle(this, tag, true, start, baseY, end, baseY + h,
                               Brushes.Transparent, fill, opacity);

                if (ShowText)
                {
                    int mid = end + (start - end) / 2;
                    var t = Draw.Text(this, tag + "_lbl", label, mid, baseY + h + 0.30 * h, Brushes.White);
                    t.Font = lblFont;
                }
                cursor += span + 1;
            };

            // tiles
            tile("cadx_chop", "CHOP", chopBrush);
            tile("cadx_adx" , "ADX" , adxBrush);
            tile("cadx_sync", "SYNC", syncBrush);

            // readout
            if (ShowText)
            {
                int textBarsAgo = drawFromRight ? anchorX : Math.Max(0, anchorX - (3 * (span + 1)));
                var main = Draw.Text(this, "cadx_readout",
                    $"CHOP {chop:F0} ({(dChop >= 0 ? "+" : "")}{dChop:F1})   |   ADX {adxNow:F1} ({(dAdx >= 0 ? "+" : "")}{dAdx:F1})   |   {(syncGreen ? "READY" : "—")}",
                    textBarsAgo,
                    baseY - 0.6 * h,
                    Brushes.White);
                main.Font = mainFont;
            }
        }

        [Browsable(false), XmlIgnore] public Series<double> Dummy => Values[0];
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ChopAdxHUD_WPF[] cacheChopAdxHUD_WPF;
		public ChopAdxHUD_WPF ChopAdxHUD_WPF(int adxLength, int chopLength, int chopWatch, int adxWatch, int nAllowance, double flatTolerancePct, HudCorner anchorCorner, int xOffsetBars, int yOffsetTicks, bool manualAnchor, int anchorBarsAgo, double anchorPrice, int barsPerTile, int tileHeightTicks, int tileOpacityPct, bool showText, bool alertOnSync, int alertCooldownSec)
		{
			return ChopAdxHUD_WPF(Input, adxLength, chopLength, chopWatch, adxWatch, nAllowance, flatTolerancePct, anchorCorner, xOffsetBars, yOffsetTicks, manualAnchor, anchorBarsAgo, anchorPrice, barsPerTile, tileHeightTicks, tileOpacityPct, showText, alertOnSync, alertCooldownSec);
		}

		public ChopAdxHUD_WPF ChopAdxHUD_WPF(ISeries<double> input, int adxLength, int chopLength, int chopWatch, int adxWatch, int nAllowance, double flatTolerancePct, HudCorner anchorCorner, int xOffsetBars, int yOffsetTicks, bool manualAnchor, int anchorBarsAgo, double anchorPrice, int barsPerTile, int tileHeightTicks, int tileOpacityPct, bool showText, bool alertOnSync, int alertCooldownSec)
		{
			if (cacheChopAdxHUD_WPF != null)
				for (int idx = 0; idx < cacheChopAdxHUD_WPF.Length; idx++)
					if (cacheChopAdxHUD_WPF[idx] != null && cacheChopAdxHUD_WPF[idx].AdxLength == adxLength && cacheChopAdxHUD_WPF[idx].ChopLength == chopLength && cacheChopAdxHUD_WPF[idx].ChopWatch == chopWatch && cacheChopAdxHUD_WPF[idx].AdxWatch == adxWatch && cacheChopAdxHUD_WPF[idx].NAllowance == nAllowance && cacheChopAdxHUD_WPF[idx].FlatTolerancePct == flatTolerancePct && cacheChopAdxHUD_WPF[idx].AnchorCorner == anchorCorner && cacheChopAdxHUD_WPF[idx].XOffsetBars == xOffsetBars && cacheChopAdxHUD_WPF[idx].YOffsetTicks == yOffsetTicks && cacheChopAdxHUD_WPF[idx].ManualAnchor == manualAnchor && cacheChopAdxHUD_WPF[idx].AnchorBarsAgo == anchorBarsAgo && cacheChopAdxHUD_WPF[idx].AnchorPrice == anchorPrice && cacheChopAdxHUD_WPF[idx].BarsPerTile == barsPerTile && cacheChopAdxHUD_WPF[idx].TileHeightTicks == tileHeightTicks && cacheChopAdxHUD_WPF[idx].TileOpacityPct == tileOpacityPct && cacheChopAdxHUD_WPF[idx].ShowText == showText && cacheChopAdxHUD_WPF[idx].AlertOnSync == alertOnSync && cacheChopAdxHUD_WPF[idx].AlertCooldownSec == alertCooldownSec && cacheChopAdxHUD_WPF[idx].EqualsInput(input))
						return cacheChopAdxHUD_WPF[idx];
			return CacheIndicator<ChopAdxHUD_WPF>(new ChopAdxHUD_WPF(){ AdxLength = adxLength, ChopLength = chopLength, ChopWatch = chopWatch, AdxWatch = adxWatch, NAllowance = nAllowance, FlatTolerancePct = flatTolerancePct, AnchorCorner = anchorCorner, XOffsetBars = xOffsetBars, YOffsetTicks = yOffsetTicks, ManualAnchor = manualAnchor, AnchorBarsAgo = anchorBarsAgo, AnchorPrice = anchorPrice, BarsPerTile = barsPerTile, TileHeightTicks = tileHeightTicks, TileOpacityPct = tileOpacityPct, ShowText = showText, AlertOnSync = alertOnSync, AlertCooldownSec = alertCooldownSec }, input, ref cacheChopAdxHUD_WPF);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ChopAdxHUD_WPF ChopAdxHUD_WPF(int adxLength, int chopLength, int chopWatch, int adxWatch, int nAllowance, double flatTolerancePct, HudCorner anchorCorner, int xOffsetBars, int yOffsetTicks, bool manualAnchor, int anchorBarsAgo, double anchorPrice, int barsPerTile, int tileHeightTicks, int tileOpacityPct, bool showText, bool alertOnSync, int alertCooldownSec)
		{
			return indicator.ChopAdxHUD_WPF(Input, adxLength, chopLength, chopWatch, adxWatch, nAllowance, flatTolerancePct, anchorCorner, xOffsetBars, yOffsetTicks, manualAnchor, anchorBarsAgo, anchorPrice, barsPerTile, tileHeightTicks, tileOpacityPct, showText, alertOnSync, alertCooldownSec);
		}

		public Indicators.ChopAdxHUD_WPF ChopAdxHUD_WPF(ISeries<double> input , int adxLength, int chopLength, int chopWatch, int adxWatch, int nAllowance, double flatTolerancePct, HudCorner anchorCorner, int xOffsetBars, int yOffsetTicks, bool manualAnchor, int anchorBarsAgo, double anchorPrice, int barsPerTile, int tileHeightTicks, int tileOpacityPct, bool showText, bool alertOnSync, int alertCooldownSec)
		{
			return indicator.ChopAdxHUD_WPF(input, adxLength, chopLength, chopWatch, adxWatch, nAllowance, flatTolerancePct, anchorCorner, xOffsetBars, yOffsetTicks, manualAnchor, anchorBarsAgo, anchorPrice, barsPerTile, tileHeightTicks, tileOpacityPct, showText, alertOnSync, alertCooldownSec);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ChopAdxHUD_WPF ChopAdxHUD_WPF(int adxLength, int chopLength, int chopWatch, int adxWatch, int nAllowance, double flatTolerancePct, HudCorner anchorCorner, int xOffsetBars, int yOffsetTicks, bool manualAnchor, int anchorBarsAgo, double anchorPrice, int barsPerTile, int tileHeightTicks, int tileOpacityPct, bool showText, bool alertOnSync, int alertCooldownSec)
		{
			return indicator.ChopAdxHUD_WPF(Input, adxLength, chopLength, chopWatch, adxWatch, nAllowance, flatTolerancePct, anchorCorner, xOffsetBars, yOffsetTicks, manualAnchor, anchorBarsAgo, anchorPrice, barsPerTile, tileHeightTicks, tileOpacityPct, showText, alertOnSync, alertCooldownSec);
		}

		public Indicators.ChopAdxHUD_WPF ChopAdxHUD_WPF(ISeries<double> input , int adxLength, int chopLength, int chopWatch, int adxWatch, int nAllowance, double flatTolerancePct, HudCorner anchorCorner, int xOffsetBars, int yOffsetTicks, bool manualAnchor, int anchorBarsAgo, double anchorPrice, int barsPerTile, int tileHeightTicks, int tileOpacityPct, bool showText, bool alertOnSync, int alertCooldownSec)
		{
			return indicator.ChopAdxHUD_WPF(input, adxLength, chopLength, chopWatch, adxWatch, nAllowance, flatTolerancePct, anchorCorner, xOffsetBars, yOffsetTicks, manualAnchor, anchorBarsAgo, anchorPrice, barsPerTile, tileHeightTicks, tileOpacityPct, showText, alertOnSync, alertCooldownSec);
		}
	}
}

#endregion
