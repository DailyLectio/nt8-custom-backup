#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators; 
using WPFBrushes = System.Windows.Media.Brushes;
using NTDrawing = NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public enum TrailModeType { None, BarByBar, ATR_Ratchet }

    public class TrinityTrader : Strategy
    {
        // =========================================================
        //    1. EXECUTION & RISK SETTINGS
        // =========================================================
        [NinjaScriptProperty, Display(Name = "1. Leg 1 Qty", GroupName = "1. Risk & Execution", Order = 1)] public int QtyLeg1 { get; set; } = 2;
        [NinjaScriptProperty, Display(Name = "2. Leg 2 Qty", GroupName = "1. Risk & Execution", Order = 2)] public int QtyLeg2 { get; set; } = 1;
        [NinjaScriptProperty, Display(Name = "3. Leg 3 Qty", GroupName = "1. Risk & Execution", Order = 3)] public int QtyLeg3 { get; set; } = 1;
        [NinjaScriptProperty, Display(Name = "4. Leg 4 Qty", GroupName = "1. Risk & Execution", Order = 4)] public int QtyLeg4 { get; set; } = 0;
        
        [NinjaScriptProperty, Display(Name = "Max Risk Cap ($)", GroupName = "1. Risk & Execution", Order = 5)] public double MaxRiskCap { get; set; } = 2500;
        [NinjaScriptProperty, Display(Name = "Breakeven Offset (Ticks)", GroupName = "1. Risk & Execution", Order = 6)] public int BEOffsetTicks { get; set; } = 4;

        // =========================================================
        //    2. TRAILING STOP MANAGER (Runners)
        // =========================================================
        [NinjaScriptProperty, Display(Name = "Leg 2 Trail Mode", GroupName = "2. Trailing Stops", Order = 1)] public TrailModeType TrailModeL2 { get; set; } = TrailModeType.BarByBar;
        [NinjaScriptProperty, Display(Name = "Leg 2 BarN Offset", GroupName = "2. Trailing Stops", Order = 2)] public int TrailBarOffsetL2 { get; set; } = 2;
        [NinjaScriptProperty, Display(Name = "Leg 2 ATR Ratchet Mult", GroupName = "2. Trailing Stops", Order = 3)] public double RatchetAtrMultL2 { get; set; } = 1.0;

        [NinjaScriptProperty, Display(Name = "Leg 3 Trail Mode", GroupName = "2. Trailing Stops", Order = 4)] public TrailModeType TrailModeL3 { get; set; } = TrailModeType.ATR_Ratchet;
        [NinjaScriptProperty, Display(Name = "Leg 3 ATR Ratchet Mult", GroupName = "2. Trailing Stops", Order = 5)] public double RatchetAtrMultL3 { get; set; } = 1.5;

        [NinjaScriptProperty, Display(Name = "Leg 4 Trail Mode", GroupName = "2. Trailing Stops", Order = 6)] public TrailModeType TrailModeL4 { get; set; } = TrailModeType.ATR_Ratchet;
        [NinjaScriptProperty, Display(Name = "Leg 4 ATR Ratchet Mult", GroupName = "2. Trailing Stops", Order = 7)] public double RatchetAtrMultL4 { get; set; } = 2.0;

        // =========================================================
        //    3. FAIL SAFES & FILTERS
        // =========================================================
        [NinjaScriptProperty, Display(Name = "Use ADX Filter", GroupName = "3. Fail Safes", Order = 1)] public bool UseADX { get; set; } = false;
        [NinjaScriptProperty, Display(Name = "Min ADX (Momentum)", GroupName = "3. Fail Safes", Order = 2)] public int MinADX { get; set; } = 18;
        
        [NinjaScriptProperty, Display(Name = "Use Choppiness Filter", GroupName = "3. Fail Safes", Order = 3)] public bool UseChop { get; set; } = false;
        [NinjaScriptProperty, Display(Name = "Max Chop Index", GroupName = "3. Fail Safes", Order = 4)] public int MaxChop { get; set; } = 60;

        [NinjaScriptProperty, Display(Name = "Use EMA Trend Filter", GroupName = "3. Fail Safes", Order = 5)] public bool UseEMA { get; set; } = false;
        [NinjaScriptProperty, Display(Name = "EMA Period", GroupName = "3. Fail Safes", Order = 6)] public int EMAPeriod { get; set; } = 21;

        // =========================================================
        //    4. HUD & VISUALS
        // =========================================================
        [NinjaScriptProperty, Display(Name = "Enable Smart Alerts", GroupName = "4. HUD & Visuals", Order = 1)] public bool EnableSmartAlerts { get; set; } = true;
        [NinjaScriptProperty, Display(Name = "Draw Levels On Chart", GroupName = "4. HUD & Visuals", Order = 2)] public bool DrawCoreLines { get; set; } = true;
        [NinjaScriptProperty, Display(Name = "Dark Theme UI", GroupName = "4. HUD & Visuals", Order = 3)] public bool UseDarkTheme { get; set; } = true;

        // DYNAMIC VARIABLES & INDICATORS
        private ATR atrAlgo; private ADX adxAlgo; private ChoppinessIndex chopAlgo; private EMA emaAlgo;
        private double liveChartPrice, currentHigh, currentLow, currentEthVwap, currentRthVwap, currentAtr, ethVol, ethPV, rthVol, rthPV;
        private DateTime lastRthDate = DateTime.MinValue;
        private string currentMarketShape = "[ D ]";
        private string activeSmartAlert = "";

        // UI & STATE
        private Grid chartGrid, mainPanel;
        private ComboBox cbPlaybook, cbEntryTrigger, cbStopType, cbTargetType;
        private TextBox txtEntry, txtStop, txtT1, txtT2, txtT3, txtT4;
        private Label lblStatus, lblPnL, lblContext;
        private Button btnLoad, btnExecL, btnExecS, btnNextL, btnNextS, btnHalf, btnBE, btnDisarm, btnFlatten;
        private FontFamily modernFont = new FontFamily("Segoe UI");
        
        private double dailyPnL = 0, sessionStartProfit = 0;
        private volatile bool isNextArmedLong = false, isNextArmedShort = false;
        private volatile bool firePendingLong = false, firePendingShort = false;
        private volatile bool triggerFlatten = false, triggerBE = false, triggerHalf = false;
		
		// TRAIL & STOP MEMORY
        private double highSinceEntry = double.MinValue;
        private double lowSinceEntry = double.MaxValue;
        private Dictionary<string, double> lastStopPrices = new Dictionary<string, double>();
        private double userOverrideStopPrice = 0; 
        private double masterStopPrice = 0;

        // SHARPDX
        private SharpDX.DirectWrite.TextFormat dxTextFormatLeft;
        private SharpDX.Direct2D1.SolidColorBrush dxBrushWhite, dxBrushAlert;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Trinity Trader Master";
                Calculate = Calculate.OnPriceChange;
                EntriesPerDirection = 4;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsOverlay = true;
            }
            else if (State == State.DataLoaded) 
            { 
                atrAlgo = ATR(14); 
                adxAlgo = ADX(14);
                chopAlgo = ChoppinessIndex(14);
                emaAlgo = EMA(EMAPeriod);
            }
            else if (State == State.Historical) { if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(CreateWPFControls); }
            else if (State == State.Terminated) { if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(DisposeWPFControls); }
        }

        private double Rnd(double val) { return Instrument.MasterInstrument.RoundToTickSize(val); }
        
        // INSTRUMENT ISOLATION FOR HUD
        private double GetHUD(string key) { 
            string instKey = Instrument.MasterInstrument.Name + "_" + key;
            if (HUDMessenger.SharedLevelMap != null && HUDMessenger.SharedLevelMap.ContainsKey(instKey)) return HUDMessenger.SharedLevelMap[instKey];
            return HUDMessenger.SharedLevelMap != null && HUDMessenger.SharedLevelMap.ContainsKey(key) ? HUDMessenger.SharedLevelMap[key] : 0; 
        }

        protected override void OnBarUpdate()
        {
            // V17 FIX: Update live price safely on the NinjaScript thread before any UI logic
            if (CurrentBar > 0) liveChartPrice = Close[0];

            try 
            {
                if (CurrentBar < 20) return;
                currentAtr = atrAlgo[0];

                if (Bars.IsFirstBarOfSession) { 
                    sessionStartProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit; 
                    ethVol=0; ethPV=0; 
                    currentHigh = High[0]; 
                    currentLow = Low[0]; 
                }
                
                currentHigh = Math.Max(currentHigh, High[0]);
                currentLow = Math.Min(currentLow, Low[0]);
                
                // VWAP CALCS
                ethVol += Volume[0]; ethPV += Volume[0] * ((High[0] + Low[0] + Close[0]) / 3.0);
                if (ethVol > 0) currentEthVwap = ethPV / ethVol;

                int time = ToTime(Time[0]);
                if (time >= 93000 && time < 160000) {
                    if (Time[0].Date != lastRthDate) { rthVol=0; rthPV=0; lastRthDate = Time[0].Date; }
                    rthVol += Volume[0]; rthPV += Volume[0] * ((High[0] + Low[0] + Close[0]) / 3.0);
                    if (rthVol > 0) currentRthVwap = rthPV / rthVol;
                }

                dailyPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartProfit;

                if (DrawCoreLines) RenderChartLines();
                if (EnableSmartAlerts) DetermineContextAndProximity(time);
                
                if (Position.MarketPosition != MarketPosition.Flat) {
                    highSinceEntry = Math.Max(highSinceEntry, High[0]);
                    lowSinceEntry = Math.Min(lowSinceEntry, Low[0]);
                } else {
                    highSinceEntry = double.MinValue; lowSinceEntry = double.MaxValue;
                    lastStopPrices.Clear(); userOverrideStopPrice = 0; masterStopPrice = 0;
                }

                // 'NEXT' TRIGGER EXECUTION
                if (IsFirstTickOfBar)
                {
                    if (isNextArmedLong) {
                        double entry = High[1] + TickSize;
                        ChartControl.Dispatcher.InvokeAsync(() => { txtEntry.Text = entry.ToString("F2"); });
                        isNextArmedLong = false; firePendingLong = true; 
                    }
                    else if (isNextArmedShort) {
                        double entry = Low[1] - TickSize;
                        ChartControl.Dispatcher.InvokeAsync(() => { txtEntry.Text = entry.ToString("F2"); });
                        isNextArmedShort = false; firePendingShort = true;
                    }
                }

                // ACTION DISPATCHER
                if (triggerFlatten) { ExecuteFlatten(); triggerFlatten = false; }
                if (triggerBE) { ExecuteBreakeven(); triggerBE = false; }
                if (triggerHalf) { ExecuteHalfRisk(); triggerHalf = false; }
                if (firePendingLong) { FireOrder(true); firePendingLong = false; }
                if (firePendingShort) { FireOrder(false); firePendingShort = false; }

                ManageTrailingStops();
            }
            catch (Exception ex) 
            {
                Print("TRINITY HUD RECOVERED CRASH: " + ex.Message);
            }
        }

        private void RenderChartLines()
        {
            if (HUDMessenger.SharedLevelMap == null) return;
            string inst = Instrument.MasterInstrument.Name + "_";
            
            Action<string, double, System.Windows.Media.Brush, DashStyleHelper> DrawLvl = (tag, price, color, style) => {
                if (price > 0) NTDrawing.Draw.HorizontalLine(this, "TrinityLine_" + tag, price, color, style, 2);
            };

            DrawLvl("Dev_VAH", GetHUD(inst + "Dev_VAH"), WPFBrushes.DodgerBlue, DashStyleHelper.Dash);
            DrawLvl("Dev_VAL", GetHUD(inst + "Dev_VAL"), WPFBrushes.DodgerBlue, DashStyleHelper.Dash);
            DrawLvl("Dev_POC", GetHUD(inst + "Dev_POC"), WPFBrushes.Gold, DashStyleHelper.Solid);
            DrawLvl("IBH", GetHUD(inst + "IBH"), WPFBrushes.DarkOrange, DashStyleHelper.Dot);
            DrawLvl("IBL", GetHUD(inst + "IBL"), WPFBrushes.DarkOrange, DashStyleHelper.Dot);
            DrawLvl("VWAP_RTH", currentRthVwap, WPFBrushes.Magenta, DashStyleHelper.Solid);
        }

        private void DetermineContextAndProximity(int time)
        {
            double close = liveChartPrice, vwap = currentRthVwap > 0 ? currentRthVwap : currentEthVwap;
            string inst = Instrument.MasterInstrument.Name + "_";
            double ibh = GetHUD(inst + "IBH"), ibl = GetHUD(inst + "IBL"), yVAH = GetHUD(inst + "yVAH"), yVAL = GetHUD(inst + "yVAL");

            // SHAPE MATRIX
            if (time < 103000) {
                if (close > yVAH) currentMarketShape = "[ P ]";
                else if (close < yVAL) currentMarketShape = "[ b ]";
                else currentMarketShape = "[ D ]";
            } else {
                if (close > ibh && ibh > 0) currentMarketShape = "[ B ]";
                else if (close < ibl && ibl > 0) currentMarketShape = "[ B ]";
                else if (close > vwap) currentMarketShape = "[ P ]";
                else if (close < vwap) currentMarketShape = "[ b ]";
                else currentMarketShape = "[ D ]";
            }

            // PROXIMITY SENSOR (12 Ticks)
            activeSmartAlert = "";
            Dictionary<string, double> levels = new Dictionary<string, double> {
                {"VWAP", vwap}, {"Dev_VAH", GetHUD(inst + "Dev_VAH")}, {"Dev_VAL", GetHUD(inst + "Dev_VAL")}, 
                {"IBH", ibh}, {"IBL", ibl}, {"yPOC", GetHUD(inst + "yPOC")}
            };

            foreach (var lvl in levels) {
                if (lvl.Value == 0) continue;
                double dist = Math.Abs(close - lvl.Value) / TickSize;
                if (dist <= 12) {
                    string dir = close >= lvl.Value ? "LONG" : "SHORT";
                    activeSmartAlert = $"{(dir=="LONG"?"🟢":"🔴")} {dir} SETUP: {currentMarketShape} Shape testing {lvl.Key} ({dist:F0} ticks away)";
                    break;
                }
            }
        }

        private bool CheckFailSafes(bool isLong)
        {
            if (UseADX && adxAlgo[0] < MinADX) return false;
            if (UseChop && chopAlgo[0] > MaxChop) return false;
            if (UseEMA) {
                if (isLong && liveChartPrice < emaAlgo[0]) return false;
                if (!isLong && liveChartPrice > emaAlgo[0]) return false;
            }
            return true;
        }

        // =========================================================
        //    EXECUTION & RISK ENGINE
        // =========================================================
        private void FireOrder(bool isLong)
        {
            if (!CheckFailSafes(isLong)) {
                ChartControl.Dispatcher.InvokeAsync(() => { lblStatus.Content = "BLOCKED: Fail Safe Active"; lblStatus.Foreground = WPFBrushes.Red; });
                return;
            }

            double entry = 0, stop = 0, t1 = 0, t2 = 0, t3 = 0, t4 = 0;
            Double.TryParse(txtEntry.Text, out entry); Double.TryParse(txtStop.Text, out stop);
            Double.TryParse(txtT1.Text, out t1); Double.TryParse(txtT2.Text, out t2);
            Double.TryParse(txtT3.Text, out t3); Double.TryParse(txtT4.Text, out t4);

            if (entry == 0 || stop == 0) return;
            entry = Rnd(entry); stop = Rnd(stop);

            // RISK GUARDRAIL CHECK
            int totalQty = QtyLeg1 + QtyLeg2 + QtyLeg3 + QtyLeg4;
            double pointRisk = Math.Abs(entry - stop);
            double dollarRisk = (pointRisk / TickSize) * Instrument.MasterInstrument.PointValue * TickSize * totalQty;

            if (dollarRisk > MaxRiskCap) {
                ChartControl.Dispatcher.InvokeAsync(() => { lblStatus.Content = $"RISK BLOCKED: ${dollarRisk:F0} > ${MaxRiskCap}"; lblStatus.Foreground = WPFBrushes.Red; });
                return;
            }
			
			highSinceEntry = High[0];
            lowSinceEntry = Low[0];
            lastStopPrices.Clear();
            userOverrideStopPrice = 0;
            masterStopPrice = stop;

            string dir = isLong ? "L" : "S";
            Action<double, int, int> fireLeg = (targetPx, qty, legNum) => {
                if (targetPx > 0 && qty > 0) {
                    targetPx = Rnd(targetPx);
                    string sig = "PB_" + dir + "_" + legNum;
                    SetStopLoss(sig, CalculationMode.Price, stop, false);
                    SetProfitTarget(sig, CalculationMode.Price, targetPx);
                    if (isLong) EnterLong(qty, sig); else EnterShort(qty, sig);
                }
            };

            fireLeg(t1, QtyLeg1, 1); fireLeg(t2, QtyLeg2, 2); fireLeg(t3, QtyLeg3, 3); fireLeg(t4, QtyLeg4, 4);
            ChartControl.Dispatcher.InvokeAsync(() => { lblStatus.Content = "ORDER FIRED"; lblStatus.Foreground = WPFBrushes.LimeGreen; });
        }

        private void SafeSetStopLoss(string signal, double price)
        {
            if (string.IsNullOrEmpty(signal)) return;
            price = Rnd(price);
            if (!lastStopPrices.ContainsKey(signal) || Math.Abs(lastStopPrices[signal] - price) > (TickSize / 2.0))
            {
                SetStopLoss(signal, CalculationMode.Price, price, false);
                lastStopPrices[signal] = price;
            }
        }

        private void ManageTrailingStops()
        {
            if (Position.MarketPosition == MarketPosition.Flat) return;

            if (QtyLeg2 > 0) ApplyTrail("PB_L_2", "PB_S_2", TrailModeL2, TrailBarOffsetL2, RatchetAtrMultL2);
            if (QtyLeg3 > 0) ApplyTrail("PB_L_3", "PB_S_3", TrailModeL3, 0, RatchetAtrMultL3);
            if (QtyLeg4 > 0) ApplyTrail("PB_L_4", "PB_S_4", TrailModeL4, 0, RatchetAtrMultL4);
        }

        private void ApplyTrail(string longSig, string shortSig, TrailModeType mode, int barN, double atrMult)
        {
            if (mode == TrailModeType.None && userOverrideStopPrice == 0) return;
            bool isLong = Position.MarketPosition == MarketPosition.Long;
            
            double newStop = 0;
            if (mode == TrailModeType.BarByBar) {
                int idx = Math.Min(barN, CurrentBar); 
                newStop = isLong ? Low[idx] : High[idx];
            }
            else if (mode == TrailModeType.ATR_Ratchet) {
                double rat = currentAtr * atrMult;
                newStop = isLong ? highSinceEntry - rat : lowSinceEntry + rat;
            }

            if (userOverrideStopPrice != 0) {
                if (newStop == 0) newStop = userOverrideStopPrice;
                else {
                    if (isLong) newStop = Math.Max(newStop, userOverrideStopPrice);
                    else newStop = Math.Min(newStop, userOverrideStopPrice);
                }
            }

            if (newStop != 0) {
                newStop = Rnd(newStop);
                string activeSig = isLong ? longSig : shortSig;

                if (lastStopPrices.ContainsKey(activeSig)) {
                    double existingStop = lastStopPrices[activeSig];
                    if (isLong && newStop < existingStop) newStop = existingStop;
                    if (!isLong && newStop > existingStop) newStop = existingStop;
                }

                masterStopPrice = newStop;
                SafeSetStopLoss(activeSig, newStop);
            }
        }

        private void ApplyOverrideStop(double price) 
        { 
            masterStopPrice = price; 
            string[] allSignals = { "PB_L_1", "PB_L_2", "PB_L_3", "PB_L_4", "PB_S_1", "PB_S_2", "PB_S_3", "PB_S_4" }; 
            foreach (string s in allSignals) SafeSetStopLoss(s, price); 
        }

        private void ExecuteFlatten() {
            if (Position.MarketPosition == MarketPosition.Long) ExitLong(); else ExitShort();
            foreach (Order o in Orders.Where(o => o.OrderState == OrderState.Working)) CancelOrder(o);
            isNextArmedLong = false; isNextArmedShort = false;
            ChartControl.Dispatcher.InvokeAsync(() => { lblStatus.Content = "FLATTENED / DISARMED"; lblStatus.Foreground = WPFBrushes.Red; });
        }

        private void ExecuteBreakeven() {
            if (Position.MarketPosition == MarketPosition.Flat) return;
            bool isLong = Position.MarketPosition == MarketPosition.Long;
            double be = isLong ? Position.AveragePrice + (BEOffsetTicks * TickSize) : Position.AveragePrice - (BEOffsetTicks * TickSize);
            
            double cappedStop = isLong ? Math.Min(be, liveChartPrice - (2 * TickSize)) : Math.Max(be, liveChartPrice + (2 * TickSize));
            
            userOverrideStopPrice = Rnd(cappedStop);
            ApplyOverrideStop(userOverrideStopPrice);
            ChartControl.Dispatcher.InvokeAsync(() => { lblStatus.Content = "STOPS AT B.E."; lblStatus.Foreground = WPFBrushes.DodgerBlue; });
        }

        private void ExecuteHalfRisk() {
            if (Position.MarketPosition == MarketPosition.Flat) {
                QtyLeg1 = Math.Max(1, QtyLeg1 / 2); QtyLeg2 = QtyLeg2 / 2; QtyLeg3 = QtyLeg3 / 2; QtyLeg4 = QtyLeg4 / 2;
                ChartControl.Dispatcher.InvokeAsync(() => { lblStatus.Content = "PRE-TRADE RISK HALVED"; lblStatus.Foreground = WPFBrushes.Gold; });
            } else {
                int halfQty = Position.Quantity / 2;
                if (halfQty > 0) {
                    if (Position.MarketPosition == MarketPosition.Long) ExitLong(halfQty, "Half Risk Exit", "PB_L_1");
                    else ExitShort(halfQty, "Half Risk Exit", "PB_S_1");
                }
            }
        }

        // =========================================================
        //    UI DISPATCHER & PLAYBOOK MAPPER
        // =========================================================
        private void ProcessCustomPlaybook()
        {
            string inst = Instrument.MasterInstrument.Name + "_";
            double safePrice = liveChartPrice > 0 ? liveChartPrice : Close[0];
            double vwap = currentRthVwap > 0 ? currentRthVwap : currentEthVwap;
            
            double devVAH = GetHUD(inst + "Dev_VAH"), devVAL = GetHUD(inst + "Dev_VAL"), devPOC = GetHUD(inst + "Dev_POC");
            double yVAH = GetHUD(inst + "yVAH"), yVAL = GetHUD(inst + "yVAL"), yPOC = GetHUD(inst + "yPOC");
            double ibh = GetHUD(inst + "IBH"), ibl = GetHUD(inst + "IBL"), pdh = GetHUD(inst + "PDH"), pdl = GetHUD(inst + "PDL");
            double onh = GetHUD(inst + "ONH"), onl = GetHUD(inst + "ONL");
            
            double entry = safePrice, stop = 0, t1 = 0, t2 = 0, t3 = 0, t4 = 0;
            string selPlaybook = cbPlaybook.SelectedItem as string;

            // 1. PLAYBOOK AUTO-SET (With 50-tick Sanity Filter)
            if (selPlaybook != null && !selPlaybook.Contains("--")) {
                
                double tacticalBase = (vwap > 0 && Math.Abs(safePrice - vwap) < (50 * TickSize)) ? vwap : safePrice;

                if (selPlaybook.Contains("[ P ]")) { entry = tacticalBase; stop = entry - (10*TickSize); t1 = currentHigh; t2 = yPOC; t3 = yVAH; }
                else if (selPlaybook.Contains("[ b ]")) { entry = tacticalBase; stop = entry + (24*TickSize); t1 = currentLow; t2 = yPOC; t3 = yVAL; }
                else if (selPlaybook.Contains("[ D ]")) { entry = devVAH > 0 ? devVAH : safePrice; stop = entry + (15*TickSize); t1 = vwap > 0 ? vwap : devPOC; }
                else if (selPlaybook.Contains("[ B ]")) { entry = ibh > 0 ? ibh : safePrice; stop = entry - (12*TickSize); t1 = entry + (currentAtr * 2); }
            }
            // 2. MANUAL OVERRIDE SELECTION
            else 
            {
                string selEntry = cbEntryTrigger.SelectedItem as string;
                if (selEntry != null) {
                    if (selEntry.Contains("VAH") && devVAH > 0) entry = devVAH;
                    else if (selEntry.Contains("VAL") && devVAL > 0) entry = devVAL;
                    else if (selEntry.Contains("VWAP") && vwap > 0) entry = vwap;
                    else if (selEntry.Contains("IBH") && ibh > 0) entry = ibh;
                    else if (selEntry.Contains("IBL") && ibl > 0) entry = ibl;
                    else if (selEntry.Contains("PDH") && pdh > 0) entry = pdh;
                    else if (selEntry.Contains("PDL") && pdl > 0) entry = pdl;
                    else if (selEntry.Contains("ONH") && onh > 0) entry = onh;
                    else if (selEntry.Contains("ONL") && onl > 0) entry = onl;
                    else if (selEntry.Contains("yVAH") && yVAH > 0) entry = yVAH;
                    else if (selEntry.Contains("yVAL") && yVAL > 0) entry = yVAL;
                    else if (selEntry.Contains("yPOC") && yPOC > 0) entry = yPOC;
                }

                bool isLongSetup = (selPlaybook != null && (selPlaybook.Contains("[ P ]") || selPlaybook.Contains("[ B ]"))) || (entry <= safePrice);

                double absBull = GetHUD(inst + "ABS_Bull"), absBear = GetHUD(inst + "ABS_Bear");
                double sibBull = GetHUD(inst + "SIB_Bull"), sibBear = GetHUD(inst + "SIB_Bear");
                double ddBull = GetHUD(inst + "DD_Bull"), ddBear = GetHUD(inst + "DD_Bear");

                string selStop = cbStopType.SelectedItem as string;
                if (selStop != null) {
                    if (selStop.Contains("Below Trap")) stop = entry - (10 * TickSize);
                    else if (selStop.Contains("above Trap")) stop = entry + (24 * TickSize);
                    else if (selStop.Contains("outside the VAH / VAL")) stop = entry.ToString().Contains(devVAH.ToString()) ? entry + (15 * TickSize) : entry - (15 * TickSize);
                    else if (selStop.Contains("behind the imbalance")) stop = entry - (12 * TickSize); 
                    
                    else if (selStop.Contains("latest ABS")) {
                        if (isLongSetup && absBull > 0) stop = absBull - (2 * TickSize);
                        else if (!isLongSetup && absBear > 0) stop = absBear + (2 * TickSize);
                        else stop = isLongSetup ? entry - (10 * TickSize) : entry + (10 * TickSize);
                    }
                    else if (selStop.Contains("latest SIB")) {
                        if (isLongSetup && sibBull > 0) stop = sibBull - (2 * TickSize);
                        else if (!isLongSetup && sibBear > 0) stop = sibBear + (2 * TickSize);
                        else stop = isLongSetup ? entry - (10 * TickSize) : entry + (10 * TickSize);
                    }
                    else if (selStop.Contains("latest DD")) {
                        if (isLongSetup && ddBull > 0) stop = ddBull - (2 * TickSize);
                        else if (!isLongSetup && ddBear > 0) stop = ddBear + (2 * TickSize);
                        else stop = isLongSetup ? entry - (10 * TickSize) : entry + (10 * TickSize);
                    }
                }

                string selTarget = cbTargetType.SelectedItem as string;
                if (selTarget != null) {
                    if (selTarget.Contains("VAH")) t1 = devVAH;
                    else if (selTarget.Contains("VAL")) t1 = devVAL;
                    else if (selTarget.Contains("POC")) t1 = devPOC;
                    else if (selTarget.Contains("yVAH")) t1 = yVAH;
                    else if (selTarget.Contains("yVAL")) t1 = yVAL;
                    else if (selTarget.Contains("yPOC")) t1 = yPOC;
                    else if (selTarget.Contains("VWAP")) t1 = vwap;
                    else if (selTarget.Contains("PDH")) t1 = pdh;
                    else if (selTarget.Contains("PDL")) t1 = pdl;
                    else if (selTarget.Contains("Swing High")) t1 = currentHigh;
                    else if (selTarget.Contains("Swing Low")) t1 = currentLow;
                }
            }

            txtEntry.Text = entry > 0 ? entry.ToString("F2") : ""; 
            txtStop.Text = stop > 0 ? stop.ToString("F2") : "";
            txtT1.Text = t1 > 0 ? t1.ToString("F2") : ""; 
            txtT2.Text = t2 > 0 ? t2.ToString("F2") : "";
            txtT3.Text = t3 > 0 ? t3.ToString("F2") : ""; 
            txtT4.Text = t4 > 0 ? t4.ToString("F2") : "";
            
            lblStatus.Content = "TACTICS LOADED"; 
            lblStatus.Foreground = WPFBrushes.Gold;
        }

        private void AdjustEntryTick(int ticks) {
            double e = 0; if (Double.TryParse(txtEntry.Text, out e)) { txtEntry.Text = Rnd(e + (ticks * TickSize)).ToString("F2"); }
        }

        // =========================================================
        //    WPF UI CONSTRUCTION
        // =========================================================
        private void CreateWPFControls()
        {
            chartGrid = ChartControl.Parent as Grid; if (chartGrid == null) return;
            mainPanel = new Grid { Width = 230, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 50, 110, 0) }; 
            
            for (int i = 0; i < 35; i++) mainPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

            Brush textC = UseDarkTheme ? WPFBrushes.White : WPFBrushes.Black; Brush bgC = UseDarkTheme ? WPFBrushes.DimGray : WPFBrushes.LightGray;

            lblStatus = LabelStyle("STANDBY", textC, bgC); lblPnL = LabelStyle("$0.00", UseDarkTheme ? WPFBrushes.Lime : WPFBrushes.DarkGreen, UseDarkTheme ? WPFBrushes.Black : WPFBrushes.White);
            lblContext = new Label { Content = "TACTICAL HUD MASTER", Foreground = WPFBrushes.Cyan, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Bold, FontFamily = modernFont };
            AddRow(lblStatus, 0); AddRow(lblPnL, 1); AddRow(lblContext, 2);

            cbPlaybook = new ComboBox { FontSize=10, Height=22, Margin=new Thickness(1) };
            cbPlaybook.Items.Add("-- SELECT PLAYBOOK --"); cbPlaybook.Items.Add("🟢 [ P ] SHAPE ➔ Bull VWAP Trap"); cbPlaybook.Items.Add("🔴 [ b ] SHAPE ➔ Bear VWAP Trap"); cbPlaybook.Items.Add("🟡 [ D ] SHAPE ➔ Mean Reversion"); cbPlaybook.Items.Add("🔵 [ B ] SHAPE ➔ Breakout Expansion");
            cbPlaybook.SelectedIndex = 0; AddRow(cbPlaybook, 3);

            cbEntryTrigger = new ComboBox { FontSize = 10, Height = 22, Margin = new Thickness(1) };
            cbEntryTrigger.Items.Add("-- MANUAL ENTRY LEVEL --");
            string[] evs = { "VWAP", "Dev_VAH", "Dev_VAL", "IBH", "IBL", "PDH", "PDL", "ONH", "ONL", "yVAH", "yVAL", "yPOC" };
            foreach(string s in evs) cbEntryTrigger.Items.Add("Manual: " + s);
            cbEntryTrigger.SelectedIndex = 0; AddRow(cbEntryTrigger, 4);

            cbStopType = new ComboBox { FontSize = 10, Height = 22, Margin = new Thickness(1) };
            cbStopType.Items.Add("-- STOP LOGIC --");
            cbStopType.Items.Add("Below Trap/VWAP support cluster."); cbStopType.Items.Add("5-7 pts above Trap/VWAP resistance cluster."); cbStopType.Items.Add("Safely outside the VAH / VAL extreme."); cbStopType.Items.Add("Tucked just behind the imbalance cluster.");
            cbStopType.Items.Add("Dynamic: 2 ticks behind latest ABS (Pullback)");
            cbStopType.Items.Add("Dynamic: 2 ticks behind latest SIB (Breakout)");
            cbStopType.Items.Add("Dynamic: 2 ticks behind latest DD (Reversal)");
            cbStopType.SelectedIndex = 0; AddRow(cbStopType, 5);

            cbTargetType = new ComboBox { FontSize = 10, Height = 22, Margin = new Thickness(1) };
            cbTargetType.Items.Add("-- TARGET LEVEL --");
            foreach(string s in evs) cbTargetType.Items.Add("Manual: " + s);
            cbTargetType.Items.Add("Manual: Swing High"); cbTargetType.Items.Add("Manual: Swing Low");
            cbTargetType.SelectedIndex = 0; AddRow(cbTargetType, 6);

            btnLoad = SolidBtn("LOAD TACTICS", WPFBrushes.SlateGray); btnLoad.Click += (s, e) => { ProcessCustomPlaybook(); }; AddRow(btnLoad, 7);

            // TACTICAL ENTRY ROW
            Grid gEntry = new Grid();
            gEntry.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); gEntry.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); gEntry.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); gEntry.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); gEntry.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            Label lE = new Label { Content = "Entry:", Foreground = textC, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            txtEntry = new TextBox { FontSize = 11, Height = 20, FontWeight = FontWeights.Bold };
            
            // EXACT V17 BUTTONS (Restores your text markings)
            Button btnC = SolidBtn("C", WPFBrushes.Gray); 
            btnC.Click += (s, e) => { 
                ChartControl.Dispatcher.InvokeAsync(() => { 
                    if (liveChartPrice > 0) txtEntry.Text = liveChartPrice.ToString("F2"); 
                }); 
            };
            Button btnUp = SolidBtn("+", WPFBrushes.DarkCyan); btnUp.Click += (s, e) => { AdjustEntryTick(1); };
            Button btnDn = SolidBtn("-", WPFBrushes.DarkCyan); btnDn.Click += (s, e) => { AdjustEntryTick(-1); };
            
            Grid.SetColumn(lE, 0); Grid.SetColumn(txtEntry, 1); Grid.SetColumn(btnC, 2); Grid.SetColumn(btnUp, 3); Grid.SetColumn(btnDn, 4);
            gEntry.Children.Add(lE); gEntry.Children.Add(txtEntry); gEntry.Children.Add(btnC); gEntry.Children.Add(btnUp); gEntry.Children.Add(btnDn);
            AddRow(gEntry, 9);

            txtStop = InputRow("Stop Px:", 10, textC); 
            txtT1 = InputRow("Target 1:", 11, textC); 
            txtT2 = InputRow("Target 2:", 12, textC); 
            txtT3 = InputRow("Target 3:", 13, textC); 
            txtT4 = InputRow("Target 4:", 14, textC);

            btnNextL = SolidBtn("NEXT LONG", WPFBrushes.DarkGoldenrod); btnNextL.Click += (s, e) => { isNextArmedLong = true; lblStatus.Content = "ARMED: NEXT LONG"; };
            btnNextS = SolidBtn("NEXT SHORT", WPFBrushes.DarkGoldenrod); btnNextS.Click += (s, e) => { isNextArmedShort = true; lblStatus.Content = "ARMED: NEXT SHORT"; };
            AddDualRow(btnNextL, btnNextS, 16);

            btnExecL = SolidBtn("EXECUTE LONG", WPFBrushes.LimeGreen); btnExecL.Click += (s, e) => { firePendingLong = true; };
            btnExecS = SolidBtn("EXECUTE SHORT", WPFBrushes.Red); btnExecS.Click += (s, e) => { firePendingShort = true; };
            AddDualRow(btnExecL, btnExecS, 18);

            btnHalf = SolidBtn("50% RISK", WPFBrushes.Purple); btnHalf.Click += (s, e) => { triggerHalf = true; };
            btnBE = SolidBtn("BREAKEVEN", WPFBrushes.DodgerBlue); btnBE.Click += (s, e) => { triggerBE = true; };
            AddDualRow(btnHalf, btnBE, 20);

            btnDisarm = SolidBtn("DISARM", WPFBrushes.Orange); btnDisarm.Click += (s, e) => { triggerFlatten = true; };
            btnFlatten = SolidBtn("FLATTEN", WPFBrushes.DarkRed); btnFlatten.Click += (s, e) => { triggerFlatten = true; };
            AddDualRow(btnDisarm, btnFlatten, 22);

            chartGrid.Children.Add(mainPanel);
        }
        
        private TextBox InputRow(string label, int row, Brush fg) {
            Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            Label l = new Label { Content = label, Foreground = fg, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            TextBox t = new TextBox { FontSize = 11, Height = 20, Margin = new Thickness(2), FontWeight = FontWeights.Bold };
            Grid.SetColumn(l, 0); Grid.SetColumn(t, 1); g.Children.Add(l); g.Children.Add(t); AddRow(g, row); return t;
        }

        // EXACT V17 UI HELPERS
        private Label LabelStyle(string content, Brush fg, Brush bg) { return new Label { Content = content, Foreground = fg, Background = bg, FontFamily = modernFont, FontWeight = FontWeights.Bold, HorizontalContentAlignment = HorizontalAlignment.Center, Width = 230 }; }
        private Button SolidBtn(string txt, Brush bg) { return new Button { Content = txt, Background = bg, Foreground = WPFBrushes.White, FontSize = 10, Margin = new Thickness(1), FontWeight = FontWeights.Bold, FontFamily = modernFont }; }
        private void AddRow(FrameworkElement c, int r) { Grid.SetRow(c, r); mainPanel.Children.Add(c); }
        private void AddDualRow(FrameworkElement l, FrameworkElement r, int row) { Grid g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetColumn(l, 0); Grid.SetColumn(r, 1); g.Children.Add(l); g.Children.Add(r); Grid.SetRow(g, row); mainPanel.Children.Add(g); }
        
        private void UpdateUI() { ChartControl.Dispatcher.InvokeAsync(() => { lblPnL.Content = dailyPnL.ToString("C"); lblPnL.Foreground = dailyPnL >= 0 ? (UseDarkTheme ? WPFBrushes.Lime : WPFBrushes.DarkGreen) : WPFBrushes.Red; }); }
        private void DisposeWPFControls() { if (chartGrid != null && mainPanel != null) chartGrid.Children.Remove(mainPanel); }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (ChartBars == null || Bars == null || Bars.Count == 0) return;
            if (dxTextFormatLeft == null) dxTextFormatLeft = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Calibri", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 12.0f) { TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading };
            if (dxBrushWhite == null) dxBrushWhite = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, UseDarkTheme ? SharpDX.Color.White : SharpDX.Color.Black);
            if (dxBrushAlert == null) dxBrushAlert = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.Gold);

            float leftX = (float)chartControl.CanvasLeft + 10; 
            float bottomY = (float)ChartPanel.H - 50f; 

            string contextText = string.Format("ETH VWAP: {0:N2} | RTH VWAP: {1:N2} | SHAPE: {2}", currentEthVwap, currentRthVwap, currentMarketShape);
            RenderTarget.DrawText(contextText, dxTextFormatLeft, new SharpDX.RectangleF(leftX, bottomY, 500, 20), dxBrushWhite); 

            if (EnableSmartAlerts && !string.IsNullOrEmpty(activeSmartAlert)) {
                RenderTarget.DrawText(activeSmartAlert, dxTextFormatLeft, new SharpDX.RectangleF(leftX, bottomY - 20, 600, 20), dxBrushAlert);
            }
        }
    }
}