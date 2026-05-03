#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using NinjaTrader.Gui;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows.Media; 
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.BarsTypes;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class OrderFlowSetupScanner : Indicator
    {
        // --- Internal state ---
        private VolumetricBarsType vol;
        private OrderFlowVWAP vwap;
		
		private string footprintExportPath = string.Empty;
		private DateTime lastExportTime = DateTime.MinValue;
        private const string FootprintHeader = "TimestampET,Signal,Volume,Delta,DeltaPct,VwapDistanceTicks,Symbol,Direction,Category,ClosePrice,HighPrice,LowPrice,ActiveBias,SourceChart";

        private RollingWindow volWin, deltaPctWin, rangeWin, maxSeenWin, minSeenWin, dShWin, dSlWin, barDeltaWin;

        private double htfLastClose, htfLastOpen;
        private bool htfBullBias;

        private Dictionary<string, int> lastSignalBar = new Dictionary<string, int>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Companion scanner for volumetric footprint setups (8 signals).";
                Name        = "OrderFlowSetupScanner";
                Calculate   = Calculate.OnBarClose;
                IsOverlay   = true;
                DrawOnPricePanel = true;

                LookbackN            = 20;
                EdgeTolTicks         = 4;
                ImbalanceRatio       = 3.0;
                MinImbalanceDelta    = 10;
                StackedLevels        = 3;
                OppStackedLevels     = 2;

                VolHiPctl            = 85;
                VolExtremePctl       = 90;
                RangeTightPctl       = 30;
                SeenExtremePctl      = 95;
                DeltaExtremeLoPctl   = 10;
                DeltaExtremeHiPctl   = 90;

                EnableSIB   = true;
                EnableABS   = true;
                EnableDD    = true;
                EnableDEIA  = true;
                EnableEEMDF = true;
                EnableTF    = true;
                EnablePAR   = true;
                EnableDEB   = true;
                EnableDT    = true;

                ShowDashboardTable   = true;
                TableLocation        = TextPosition.TopRight;
                TableOpacity         = 80;

                EnableAlerts         = false;
                AlertRearmSeconds    = 30;
                MinBarsBetweenSameSignal = 2;

                AddHTF30mContext     = true;
                BroadcastToHUD       = true; 
                
                SoundSIB = "Alert1.wav";
                SoundABS = "Alert2.wav";
                SoundDD = "None"; 
                SoundDEIA = "Alert3.wav";
                SoundEEMDF = "Alert4.wav";
                SoundTF = "003.wav";
                SoundPAR = "002.wav";
                SoundDEB = "004.wav";
                SoundDT  = "001.wav"; 
            }
            else if (State == State.Configure)
            {
                if (AddHTF30mContext)
                    AddDataSeries(BarsPeriodType.Minute, 30);
            }
            else if (State == State.DataLoaded)
            {
                // 1. Initialize Volumetric and VWAP
                vol = Bars.BarsSeries.BarsType as VolumetricBarsType;
                vwap = OrderFlowVWAP(VWAPResolution.Standard, Bars.TradingHours, VWAPStandardDeviations.Three, 1, 2, 3);
                InitWindows();

                // 2. Initialize the CSV Export Path
                footprintExportPath = @"C:\Users\Valued Customer\NT8_Regimes\Active\Footprint_Export.csv";
                EnsureFootprintExportFile();
            }
        }

        private void InitWindows()
        {
            volWin      = new RollingWindow(LookbackN);
            deltaPctWin = new RollingWindow(LookbackN);
            rangeWin    = new RollingWindow(LookbackN);
            maxSeenWin  = new RollingWindow(LookbackN);
            minSeenWin  = new RollingWindow(LookbackN);
            dShWin      = new RollingWindow(LookbackN);
            dSlWin      = new RollingWindow(LookbackN);
            barDeltaWin = new RollingWindow(LookbackN);
        }

        private void EnsureFootprintExportFile()
        {
            if (string.IsNullOrEmpty(footprintExportPath)) return;

            try
            {
                string dir = System.IO.Path.GetDirectoryName(footprintExportPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                if (!System.IO.File.Exists(footprintExportPath) || new System.IO.FileInfo(footprintExportPath).Length == 0)
                {
                    using (System.IO.StreamWriter sw = new System.IO.StreamWriter(footprintExportPath, false))
                        sw.WriteLine(FootprintHeader);
                    return;
                }

                string firstLine = string.Empty;
                using (System.IO.StreamReader sr = new System.IO.StreamReader(footprintExportPath))
                    firstLine = sr.ReadLine() ?? string.Empty;

                if (firstLine != FootprintHeader)
                {
                    string backupPath = footprintExportPath.Replace(
                        ".csv",
                        ".schema_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                    System.IO.File.Move(footprintExportPath, backupPath);

                    using (System.IO.StreamWriter sw = new System.IO.StreamWriter(footprintExportPath, false))
                        sw.WriteLine(FootprintHeader);
                }
            }
            catch (Exception)
            {
                // CSV logging must never crash or freeze the chart.
            }
        }

        private bool ShouldExportInstrument()
        {
            string name = Instrument.MasterInstrument.Name;
            return name == "NQ" || name == "MNQ" || name == "ES" || name == "MES";
        }

        private string GetLeaderSymbol()
        {
            string name = Instrument.MasterInstrument.Name;
            if (name == "MNQ") return "NQ";
            if (name == "MES") return "ES";
            return name;
        }

        private string DirectionLabel(int dir)
        {
            if (dir > 0) return "Bullish";
            if (dir < 0) return "Bearish";
            return "Neutral";
        }

        private string CsvNumber(double value)
        {
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private void BroadcastSignal(SignalEvent sig)
        {
            if (sig.Name.Contains("SIB"))   HUDMessenger.SharedSignalMap["Scanner_SIB"]   = Time[0];
            if (sig.Name.Contains("ABS"))   HUDMessenger.SharedSignalMap["Scanner_ABS"]   = Time[0];
            if (sig.Name.Contains("DD"))    HUDMessenger.SharedSignalMap["Scanner_DD"]    = Time[0];
            if (sig.Name.Contains("TF"))    HUDMessenger.SharedSignalMap["Scanner_TF"]    = Time[0];
            if (sig.Name.Contains("DT"))    HUDMessenger.SharedSignalMap["Scanner_DT"]    = Time[0];
            if (sig.Name.Contains("DEIA"))  HUDMessenger.SharedSignalMap["Scanner_DEIA"]  = Time[0];
            if (sig.Name.Contains("EEMDF")) HUDMessenger.SharedSignalMap["Scanner_EEMDF"] = Time[0];
            if (sig.Name.Contains("PAR"))   HUDMessenger.SharedSignalMap["Scanner_PAR"]   = Time[0];
            if (sig.Name.Contains("DEB"))   HUDMessenger.SharedSignalMap["Scanner_DEB"]   = Time[0];
        }

		// --- MACRO EXPORT METHOD ---
        private void ExportFootprintSignal(SignalEvent sig)
        {
            if (CurrentBar < 1 || vol == null || !ShouldExportInstrument()) return;

            try
            {
                EnsureFootprintExportFile();

                double barVol = vol.Volumes[CurrentBar].TotalVolume;
                double barDelta = vol.Volumes[CurrentBar].BarDelta;
                double deltaPct = barVol > 0 ? Math.Round((barDelta / barVol) * 100, 2) : 0;

                double distToVwapTicks = 0;
                if (vwap != null && vwap.VWAP != null && vwap.VWAP.IsValidDataPoint(0))
                {
                    distToVwapTicks = Math.Round((Close[0] - vwap.VWAP[0]) / TickSize, 0);
                }

                using (System.IO.StreamWriter sw = new System.IO.StreamWriter(footprintExportPath, true))
                {
                    sw.WriteLine(string.Join(",", new string[] {
                        Time[0].ToString("yyyy-MM-dd HH:mm:ss"),
                        sig.Name,
                        CsvNumber(barVol),
                        CsvNumber(barDelta),
                        CsvNumber(deltaPct),
                        CsvNumber(distToVwapTicks),
                        GetLeaderSymbol(),
                        DirectionLabel(sig.Dir),
                        sig.Category.ToString(),
                        CsvNumber(Close[0]),
                        CsvNumber(High[0]),
                        CsvNumber(Low[0]),
                        HUDMessenger.CurrentDailyBias ?? string.Empty,
                        Instrument.MasterInstrument.Name
                    }));
                }
            }
            catch (Exception)
            {
                // Fails silently so it doesn't crash the chart
            }
        }
		
        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 1) return;
            if (AddHTF30mContext && CurrentBars.Length > 1 && CurrentBars[1] < 1) return;

            if (BarsInProgress == 0 && Bars.IsFirstBarOfSession)
                ResetSession();

            if (BarsInProgress == 1)
            {
                htfLastClose = Closes[1][0];
                htfLastOpen  = Opens[1][0];
                htfBullBias  = htfLastClose >= htfLastOpen;
                return;
            }

            if (vol == null) return;

            var V = vol.Volumes[CurrentBar];
            long barDelta = V.BarDelta;
            long volume   = V.TotalVolume;
            double deltaPct = V.GetDeltaPercent();
            long maxSeen = V.MaxSeenDelta;
            long minSeen = V.MinSeenDelta;
            long dSh = V.DeltaSh;
            long dSl = V.DeltaSl;

            double range = High[0] - Low[0];
            double rangeTicks = range / TickSize;
            double closePos = range <= TickSize ? 0.5 : (Close[0] - Low[0]) / range;
            
            if (CurrentBar < Bars.Count - (Bars.BarsPeriod.Value == 30 ? 40 : 250)) return;

            if (IsFirstTickOfBar) PushPriorBarStats();

            double volHi      = volWin.Percentile(VolHiPctl);
            double volExtreme = volWin.Percentile(VolExtremePctl);
            double rngTight   = rangeWin.Percentile(RangeTightPctl);
            double maxSeenExtreme = maxSeenWin.Percentile(SeenExtremePctl);
            double minSeenExtreme = minSeenWin.Percentile(100 - SeenExtremePctl);
            double deltaLo = deltaPctWin.Percentile(DeltaExtremeLoPctl);
            double deltaHi = deltaPctWin.Percentile(DeltaExtremeHiPctl);
            double deltaSpikeHi = deltaPctWin.Percentile(85);
            double deltaSpikeLo = deltaPctWin.Percentile(15);

            double vwapVal = vwap.VWAP[0];
            var signals = new List<SignalEvent>();

            // --- 1. SIGNAL DETECTION ---
            if (EnableSIB)   TrySIB(signals, V, barDelta, deltaPct, volume, closePos, volHi);
            if (EnableABS)   TryABS(signals, V, barDelta, deltaPct, volume, closePos, volExtreme, rngTight, dSh, dSl);
            if (EnableDD)    TryDD(signals, barDelta, deltaPct);
            if (EnableDEIA)  TryDEIA(signals, V, barDelta, deltaPct, volume, closePos, volHi, minSeen, maxSeen, minSeenExtreme, maxSeenExtreme, dSh, dSl);
            if (EnableEEMDF) TryEEMDF(signals, barDelta, deltaPct, closePos, volume, volHi, maxSeen, minSeen, maxSeenExtreme, minSeenExtreme, dSh, dSl);
            if (EnableTF)    TryTF(signals, vwapVal, deltaHi, deltaLo);
            if (EnablePAR)   TryPAR(signals, vwapVal, deltaHi, deltaLo, rngTight);
            if (EnableDEB)   TryDEB(signals, barDelta, deltaPct, volume, closePos, volHi, deltaSpikeHi, deltaSpikeLo);

            // --- 2. MASTER BIAS FILTER ---
            string activeBias = HUDMessenger.CurrentDailyBias;
            List<SignalEvent> filteredSignals = new List<SignalEvent>();

            foreach (var sig in signals)
            {
                bool allow = true;
                if (activeBias == "P")
                {
                    if (sig.Dir < 0 && sig.Category == SetupCategory.Continuation) allow = false;
                }
                else if (activeBias == "b")
                {
                    if (sig.Dir > 0 && sig.Category == SetupCategory.Continuation) allow = false;
                }

                if (allow) filteredSignals.Add(sig);
            }

            // --- 3. DELTA TRANSITION (DT) LOGIC ---
            if (EnableDT && CurrentBar > LookbackN)
            {
                double currentDelta = vol.Volumes[CurrentBar].BarDelta;
                double prevDelta    = vol.Volumes[CurrentBar - 1].BarDelta;

                if (prevDelta > 0 && currentDelta <= -MinImbalanceDelta) // Bearish DT
                {
                    filteredSignals.Add(new SignalEvent { Name = "DT", Dir = -1, Category = SetupCategory.Reversal, Y = High[0] + (2 * TickSize) });
                }
                else if (prevDelta < 0 && currentDelta >= MinImbalanceDelta) // Bullish DT
                {
                    filteredSignals.Add(new SignalEvent { Name = "DT", Dir = 1, Category = SetupCategory.Reversal, Y = Low[0] - (2 * TickSize) });
                }
            }

            // --- 4. EXPORT TO CSV ---
            if (ShouldExportInstrument())
            {
                HashSet<string> exportedThisBar = new HashSet<string>();
                foreach (var sig in filteredSignals)
                {
                    string exportKey = sig.Name + "_" + sig.Dir.ToString(CultureInfo.InvariantCulture);
                    if (exportedThisBar.Contains(exportKey)) continue;

                    ExportFootprintSignal(sig);
                    exportedThisBar.Add(exportKey);
                }
            }

            // --- 5. BROADCAST TO HUD ---
            if (BroadcastToHUD)
            {
                foreach (var sig in filteredSignals)
                    BroadcastSignal(sig);
            }

            Render(filteredSignals); 
        }

        private void PushPriorBarStats()
        {
            if (CurrentBar < 2) return;
            var P = vol.Volumes[CurrentBar - 1];
            volWin.Push(P.TotalVolume);
            deltaPctWin.Push(P.GetDeltaPercent());
            barDeltaWin.Push(P.BarDelta);
            double pr = (Highs[0][1] - Lows[0][1]) / TickSize;
            rangeWin.Push(pr);
            maxSeenWin.Push(P.MaxSeenDelta);
            minSeenWin.Push(P.MinSeenDelta);
            dShWin.Push(P.DeltaSh);
            dSlWin.Push(P.DeltaSl);
        }

        private void ResetSession()
        {
            InitWindows();
            lastSignalBar.Clear();
        }

        // ------------------- DETECTORS -------------------

        private void TrySIB(List<SignalEvent> outSig, VolumetricData V,
            long barDelta, double deltaPct, long volume, double closePos, double volHi)
        {
            if (volume < volHi) return;
            if (barDelta > 0 && closePos >= 0.65 && CountStackedBuyImbalances(V) >= StackedLevels)
                outSig.Add(new SignalEvent("SIB", +1, Low[0] - 2 * TickSize, SetupCategory.Continuation));
            if (barDelta < 0 && closePos <= 0.35 && CountStackedSellImbalances(V) >= StackedLevels)
                outSig.Add(new SignalEvent("SIB", -1, High[0] + 2 * TickSize, SetupCategory.Continuation));
        }

        private void TryABS(List<SignalEvent> outSig, VolumetricData V,
            long barDelta, double deltaPct, long volume, double closePos, double volExtreme,
            double rngTight, long dSh, long dSl)
        {
            double rangeTicks = (High[0] - Low[0]) / TickSize;
            if (volume < volExtreme || rangeTicks > rngTight) return;

            if (barDelta < 0 && (closePos >= 0.55 || dSl > 0))
                outSig.Add(new SignalEvent("ABS", +1, Low[0] - 3 * TickSize, SetupCategory.Reversal));
            if (barDelta > 0 && (closePos <= 0.45 || dSh < 0))
                outSig.Add(new SignalEvent("ABS", -1, High[0] + 3 * TickSize, SetupCategory.Reversal));
        }

        private void TryDD(List<SignalEvent> outSig, long barDelta, double deltaPct)
        {
            if (High[0] > High[1] && barDelta < 0)
                outSig.Add(new SignalEvent("DD", -1, High[0] + 4 * TickSize, SetupCategory.Reversal));
            if (Low[0] < Low[1] && barDelta > 0)
                outSig.Add(new SignalEvent("DD", +1, Low[0] - 4 * TickSize, SetupCategory.Reversal));
        }

        private void TryDEIA(List<SignalEvent> outSig, VolumetricData V,
            long barDelta, double deltaPct, long volume, double closePos, double volHi,
            long minSeen, long maxSeen, double minSeenExtreme, double maxSeenExtreme, long dSh, long dSl)
        {
            if (volume < volHi) return;

            if (deltaPct <= deltaPctWin.Percentile(10) && minSeen <= minSeenExtreme
                && closePos >= 0.50 && dSl >= dSlWin.Percentile(80)
                && CountOpposingBuyImbalancesNearLow(V) >= OppStackedLevels)
            {
                outSig.Add(new SignalEvent("DEIA", +1, Low[0] - 5 * TickSize, SetupCategory.Extreme));
            }

            if (deltaPct >= deltaPctWin.Percentile(90) && maxSeen >= maxSeenExtreme
                && closePos <= 0.50 && dSh <= dShWin.Percentile(20)
                && CountOpposingSellImbalancesNearHigh(V) >= OppStackedLevels)
            {
                outSig.Add(new SignalEvent("DEIA", -1, High[0] + 5 * TickSize, SetupCategory.Extreme));
            }
        }

        private void TryEEMDF(List<SignalEvent> outSig, long barDelta, double deltaPct, double closePos,
            long volume, double volHi, long maxSeen, long minSeen, double maxSeenExtreme, double minSeenExtreme, long dSh, long dSl)
        {
            if (volume < volHi) return;

            if (High[0] > High[1] && maxSeen >= maxSeenExtreme
                && barDelta <= barDeltaWin.Percentile(40) && closePos <= 0.40
                && dSh <= dShWin.Percentile(20))
            {
                outSig.Add(new SignalEvent("EEMDF", -1, High[0] + 6 * TickSize, SetupCategory.Extreme));
            }

            if (Low[0] < Low[1] && minSeen <= minSeenExtreme
                && barDelta >= barDeltaWin.Percentile(60) && closePos >= 0.60
                && dSl >= dSlWin.Percentile(80))
            {
                outSig.Add(new SignalEvent("EEMDF", +1, Low[0] - 6 * TickSize, SetupCategory.Extreme));
            }
        }

        private void TryTF(List<SignalEvent> outSig, double level, double deltaHi, double deltaLo)
        {
            if (CurrentBar < 3) return;
            int trapBreakTicks = 2;
            double breakAmount = trapBreakTicks * TickSize;

            bool prevAbove = Close[1] > level && High[1] > level + breakAmount;
            bool nowBelow  = Close[0] < level;
            if (prevAbove && nowBelow && vol.Volumes[CurrentBar].GetDeltaPercent() <= deltaLo)
                outSig.Add(new SignalEvent("TF", -1, High[0] + 7 * TickSize, SetupCategory.Reversal));

            bool prevBelow = Close[1] < level && Low[1] < level - breakAmount;
            bool nowAbove  = Close[0] > level;
            if (prevBelow && nowAbove && vol.Volumes[CurrentBar].GetDeltaPercent() >= deltaHi)
                outSig.Add(new SignalEvent("TF", +1, Low[0] - 7 * TickSize, SetupCategory.Reversal));
        }

        private void TryPAR(List<SignalEvent> outSig, double vwapVal, double deltaHi, double deltaLo, double rngTight)
        {
            double rangeTicks = (High[0] - Low[0]) / TickSize;
            bool bullContext = Close[0] >= vwapVal && (!AddHTF30mContext || htfBullBias);
            
            if (!bullContext || CurrentBar < 2) return;

            double pbDelta = vol.Volumes[CurrentBar - 1].GetDeltaPercent();
            double pbRange = (Highs[0][1] - Lows[0][1]) / TickSize;
            bool pullback = pbDelta <= deltaLo && pbRange <= rngTight;
            bool resumption = vol.Volumes[CurrentBar].GetDeltaPercent() >= deltaHi && High[0] > High[1];

            if (pullback && resumption)
                outSig.Add(new SignalEvent("PAR", +1, Low[0] - 8 * TickSize, SetupCategory.Continuation));
        }

        private void TryDEB(List<SignalEvent> outSig, long barDelta, double deltaPct, long volume, double closePos, double volHi, double deltaSpikeHi, double deltaSpikeLo)
        {
            if (volume < volHi) return;

            if (barDelta > 0 && deltaPct >= deltaSpikeHi && closePos >= 0.75)
                outSig.Add(new SignalEvent("DEB", +1, Low[0] - 9 * TickSize, SetupCategory.Continuation));

            if (barDelta < 0 && deltaPct <= deltaSpikeLo && closePos <= 0.25)
                outSig.Add(new SignalEvent("DEB", -1, High[0] + 9 * TickSize, SetupCategory.Continuation));
        }

        // ------------------- IMBALANCE SCANNERS -------------------
        private int CountStackedBuyImbalances(VolumetricData V)
        {
            int count = 0;
            for (double p = Low[0]; p <= High[0]; p += TickSize)
            {
                double ask = V.GetAskVolumeForPrice(p);
                double bidDiag = V.GetBidVolumeForPrice(p - TickSize);
                if (IsBuyImbalance(ask, bidDiag))
                {
                    count++;
                    if (count >= StackedLevels) return count;
                }
                else count = 0;
            }
            return count;
        }

        private int CountStackedSellImbalances(VolumetricData V)
        {
            int count = 0;
            for (double p = High[0]; p >= Low[0]; p -= TickSize)
            {
                double bid = V.GetBidVolumeForPrice(p);
                double askDiag = V.GetAskVolumeForPrice(p + TickSize);
                if (IsSellImbalance(bid, askDiag))
                {
                    count++;
                    if (count >= StackedLevels) return count;
                }
                else count = 0;
            }
            return count;
        }

        private bool IsBuyImbalance(double ask, double bidDiag)
        {
            if (bidDiag <= 0 && ask > 0) return true;
            if (bidDiag > 0 && (ask / bidDiag) >= ImbalanceRatio && (ask - bidDiag) >= MinImbalanceDelta)
                return true;
            return false;
        }

        private bool IsSellImbalance(double bid, double askDiag)
        {
            if (askDiag <= 0 && bid > 0) return true;
            if (askDiag > 0 && (bid / askDiag) >= ImbalanceRatio && (bid - askDiag) >= MinImbalanceDelta)
                return true;
            return false;
        }

        private int CountOpposingBuyImbalancesNearLow(VolumetricData V)
        {
            double cutoff = Low[0] + 0.25 * (High[0] - Low[0]);
            int hits = 0;
            for (double p = Low[0]; p <= cutoff; p += TickSize)
            {
                double ask = V.GetAskVolumeForPrice(p);
                double bidDiag = V.GetBidVolumeForPrice(p - TickSize);
                if (IsBuyImbalance(ask, bidDiag)) hits++;
            }
            return hits;
        }

        private int CountOpposingSellImbalancesNearHigh(VolumetricData V)
        {
            double cutoff = High[0] - 0.25 * (High[0] - Low[0]);
            int hits = 0;
            for (double p = High[0]; p >= cutoff; p -= TickSize)
            {
                double bid = V.GetBidVolumeForPrice(p);
                double askDiag = V.GetAskVolumeForPrice(p + TickSize);
                if (IsSellImbalance(bid, askDiag)) hits++;
            }
            return hits;
        }

        // ------------------- RENDER + ALERTS -------------------
        private void Render(List<SignalEvent> sigs)
        {
            string table = "OrderFlow Setup Scanner\n------------------------\n";
            foreach (var s in sigs)
            {
                string tag = s.Name + "_" + CurrentBar;
                System.Windows.Media.Brush themeBrush;
                switch (s.Category)
                {
                    case SetupCategory.Continuation: themeBrush = System.Windows.Media.Brushes.Cyan; break;
                    case SetupCategory.Reversal:     themeBrush = System.Windows.Media.Brushes.Magenta; break;
                    case SetupCategory.Extreme:      themeBrush = System.Windows.Media.Brushes.Gold; break;
                    default:                         themeBrush = System.Windows.Media.Brushes.White; break;
                }
                
                double textOffset = 12 * TickSize; 
                double textY = s.Dir > 0 ? s.Y - textOffset : s.Y + textOffset;

                if (s.Dir > 0) Draw.ArrowUp(this, tag, true, 0, s.Y, themeBrush);
                else Draw.ArrowDown(this, tag, true, 0, s.Y, themeBrush);

                Draw.Text(this, tag + "_T", s.Name, 0, textY, System.Windows.Media.Brushes.White);
                
                // --- THE LAUNCHPAD GLOBAL LINE ---
                if (s.Name == "SIB" || s.Name == "DEB" || s.Name == "ABS" || s.Name == "PAR" || s.Name == "DT" || s.Name == "DD")
                {
                    // Find the exact edge of the micro-structure
                    double launchpadPrice = s.Y; 
                    if (s.Dir > 0 && s.Category == SetupCategory.Continuation) launchpadPrice = High[0]; // Bullish SIB -> Top edge
                    else if (s.Dir < 0 && s.Category == SetupCategory.Continuation) launchpadPrice = Low[0]; // Bearish SIB -> Bottom edge
                    else if (s.Dir > 0 && s.Category == SetupCategory.Reversal) launchpadPrice = Low[0]; // Bullish ABS -> Bottom edge
                    else if (s.Dir < 0 && s.Category == SetupCategory.Reversal) launchpadPrice = High[0]; // Bearish ABS -> Top edge

                    // THE FIX: Use a static naming convention so NT8 automatically overwrites the old line.
                    // This guarantees only ONE active bull line and ONE active bear line exist at a time.
                    string rayTag = "Active_Launchpad_" + (s.Dir > 0 ? "Bull" : "Bear");
                    
                    System.Windows.Media.Brush rayBrush = (s.Dir > 0) ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Red;
                    DashStyleHelper dashStyle = (s.Category == SetupCategory.Reversal) ? DashStyleHelper.Dash : DashStyleHelper.Solid;

                    // Draw the ray using the specific overload that accepts 'isGlobal' (true) and an empty template ("")
                    var myRay = Draw.Ray(this, rayTag, 0, launchpadPrice, -10, launchpadPrice, true, "LaunchpadStyle");
                    
                    // Apply the visual styling (colors and dashes) immediately after creation
                    myRay.Stroke = new Stroke(rayBrush, dashStyle, 2); 

                    // NEW: Broadcast the exact price to the HUDMessenger for TrinityTrader to read!
                    if (s.Name == "SIB" || s.Name == "ABS" || s.Name == "DD")
                    {
                        string hudKey = s.Name + "_" + (s.Dir > 0 ? "Bull" : "Bear");
                        if (HUDMessenger.SharedLevelMap != null)
                        {
                            HUDMessenger.SharedLevelMap[hudKey] = launchpadPrice;
                        }
                    }
                }

                table += $"{s.Name}: {(s.Dir > 0 ? "BULL" : "BEAR")} ({s.Category})\n";
                TryAlert(s);
            }

            if (ShowDashboardTable)
            {
                Draw.TextFixed(this, "OFScannerTable", table, TableLocation, System.Windows.Media.Brushes.White,
                    new SimpleFont("Consolas", 14), System.Windows.Media.Brushes.Transparent, System.Windows.Media.Brushes.DimGray, TableOpacity);
            }
        }

        private string GetSoundForSignal(string signalName)
        {
            switch (signalName)
            {
                case "SIB": return SoundSIB;
                case "ABS": return SoundABS;
                case "DD": return SoundDD;
                case "DEIA": return SoundDEIA;
                case "EEMDF": return SoundEEMDF;
                case "TF": return SoundTF;
                case "PAR": return SoundPAR;
                case "DEB": return SoundDEB;
                default: return "Alert1.wav";
            }
        }

        private void TryAlert(SignalEvent s)
        {
            if (!EnableAlerts || State != State.Realtime) return;

            if (lastSignalBar.TryGetValue(s.Name, out int last))
            {
                if (CurrentBar - last < MinBarsBetweenSameSignal) return;
            }

            string soundFile = GetSoundForSignal(s.Name);
            
            if (string.IsNullOrEmpty(soundFile) || soundFile.Trim().ToLower() == "none")
            {
                lastSignalBar[s.Name] = CurrentBar; 
                return; 
            }

            Alert("OF_" + s.Name, Priority.Medium,
                $"{s.Name} {(s.Dir > 0 ? "BULL" : "BEAR")} on {Instrument.FullName}",
                NinjaTrader.Core.Globals.InstallDir + @"\sounds\" + soundFile,
                AlertRearmSeconds, Brushes.Black, Brushes.White);
            
            lastSignalBar[s.Name] = CurrentBar;
        }

        // ------------------- Types -------------------
        public enum SetupCategory
        {
            Continuation,
            Reversal,
            Extreme
        }

        private struct SignalEvent
        {
            public string Name;
            public int Dir;
            public double Y;
            public SetupCategory Category;
            
            public SignalEvent(string n, int d, double y, SetupCategory cat) 
            { 
                Name = n; 
                Dir = d; 
                Y = y; 
                Category = cat; 
            }
        }

        private class RollingWindow
        {
            private readonly double[] buf;
            private int idx = 0;
            private int count = 0;
            public RollingWindow(int n) { buf = new double[Math.Max(5, n)]; }

            public void Push(double v)
            {
                buf[idx] = v;
                idx = (idx + 1) % buf.Length;
                count = Math.Min(count + 1, buf.Length);
            }

            public double Percentile(double p)
            {
                if (count < 5) return double.NaN;
                double[] tmp = new double[count];
                for (int i = 0; i < count; i++) tmp[i] = buf[i];
                Array.Sort(tmp);
                int k = (int)Math.Round((p / 100.0) * (tmp.Length - 1));
                k = Math.Max(0, Math.Min(tmp.Length - 1, k));
                return tmp[k];
            }
        }

        #region Properties
        [NinjaScriptProperty][Display(Name="Broadcast to HUD", GroupName="Context", Order=2)] public bool BroadcastToHUD { get; set; }
        
        [NinjaScriptProperty][Range(10, 200)][Display(Name="LookbackN", GroupName="Thresholds", Order=1)] public int LookbackN { get; set; }
        [NinjaScriptProperty][Range(1, 20)][Display(Name="EdgeTolTicks", GroupName="Thresholds", Order=2)] public int EdgeTolTicks { get; set; }
        [NinjaScriptProperty][Range(1.0, 10.0)][Display(Name="ImbalanceRatio", GroupName="Imbalance", Order=1)] public double ImbalanceRatio { get; set; }
        [NinjaScriptProperty][Range(0, 500)][Display(Name="MinImbalanceDelta", GroupName="Imbalance", Order=2)] public int MinImbalanceDelta { get; set; }
        [NinjaScriptProperty][Range(2, 6)][Display(Name="StackedLevels", GroupName="Imbalance", Order=3)] public int StackedLevels { get; set; }
        [NinjaScriptProperty][Range(1, 5)][Display(Name="OppStackedLevels", GroupName="Imbalance", Order=4)] public int OppStackedLevels { get; set; }
        
        [NinjaScriptProperty][Range(50, 99)][Display(Name="VolHiPctl", GroupName="Thresholds", Order=10)] public int VolHiPctl { get; set; }
        [NinjaScriptProperty][Range(50, 99)][Display(Name="VolExtremePctl", GroupName="Thresholds", Order=11)] public int VolExtremePctl { get; set; }
        [NinjaScriptProperty][Range(1, 60)][Display(Name="RangeTightPctl", GroupName="Thresholds", Order=12)] public int RangeTightPctl { get; set; }
        [NinjaScriptProperty][Range(80, 99)][Display(Name="SeenExtremePctl", GroupName="Thresholds", Order=13)] public int SeenExtremePctl { get; set; }
        [NinjaScriptProperty][Range(1, 49)][Display(Name="DeltaExtremeLoPctl", GroupName="Thresholds", Order=14)] public int DeltaExtremeLoPctl { get; set; }
        [NinjaScriptProperty][Range(51, 99)][Display(Name="DeltaExtremeHiPctl", GroupName="Thresholds", Order=15)] public int DeltaExtremeHiPctl { get; set; }

        [NinjaScriptProperty][Display(Name="Enable Alerts", GroupName="Alerts", Order=1)] public bool EnableAlerts { get; set; }
        [NinjaScriptProperty][Range(5, 300)][Display(Name="Alert Rearm Seconds", GroupName="Alerts", Order=2)] public int AlertRearmSeconds { get; set; }
        [NinjaScriptProperty][Range(0, 10)][Display(Name="Min Bars Between Same Signal", GroupName="Alerts", Order=3)] public int MinBarsBetweenSameSignal { get; set; }
        [NinjaScriptProperty][Display(Name="Show Dashboard Table", GroupName="UI", Order=1)] public bool ShowDashboardTable { get; set; }
        [NinjaScriptProperty][Display(Name="Table Location", GroupName="UI", Order=2)] public TextPosition TableLocation { get; set; }
        [NinjaScriptProperty][Range(0, 100)][Display(Name="Table Opacity", GroupName="UI", Order=3)] public int TableOpacity { get; set; }
        
        [NinjaScriptProperty][Display(Name="Add 30m Context", GroupName="Context", Order=1)] public bool AddHTF30mContext { get; set; }
        [NinjaScriptProperty] public bool EnableSIB { get; set; }
        [NinjaScriptProperty] public bool EnableABS { get; set; }
        [NinjaScriptProperty] public bool EnableDD  { get; set; }
        [NinjaScriptProperty] public bool EnableDEIA { get; set; }
        [NinjaScriptProperty] public bool EnableEEMDF { get; set; }
        [NinjaScriptProperty] public bool EnableTF { get; set; }
        [NinjaScriptProperty] public bool EnablePAR { get; set; }
        [NinjaScriptProperty] public bool EnableDEB { get; set; }
		
        [NinjaScriptProperty][Display(Name="SIB Sound", GroupName="Alert Sounds", Order=1)] public string SoundSIB { get; set; }
        [NinjaScriptProperty][Display(Name="ABS Sound", GroupName="Alert Sounds", Order=2)] public string SoundABS { get; set; }
        [NinjaScriptProperty][Display(Name="DD Sound", GroupName="Alert Sounds", Order=3)] public string SoundDD { get; set; }
        [NinjaScriptProperty][Display(Name="DEIA Sound", GroupName="Alert Sounds", Order=4)] public string SoundDEIA { get; set; }
        [NinjaScriptProperty][Display(Name="EEMDF Sound", GroupName="Alert Sounds", Order=5)] public string SoundEEMDF { get; set; }
        [NinjaScriptProperty][Display(Name="TF Sound", GroupName="Alert Sounds", Order=6)] public string SoundTF { get; set; }
        [NinjaScriptProperty][Display(Name="PAR Sound", GroupName="Alert Sounds", Order=7)] public string SoundPAR { get; set; }
        [NinjaScriptProperty][Display(Name="DEB Sound", GroupName="Alert Sounds", Order=8)] public string SoundDEB { get; set; }
		
		[NinjaScriptProperty]
        [Display(Name = "Enable DT (Delta Transition)", Order = 8, GroupName = "2. Signals")]
        public bool EnableDT { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Sound: DT", Order = 8, GroupName = "3. Alerts")]
        public string SoundDT { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private OrderFlowSetupScanner[] cacheOrderFlowSetupScanner;
		public OrderFlowSetupScanner OrderFlowSetupScanner(bool broadcastToHUD, int lookbackN, int edgeTolTicks, double imbalanceRatio, int minImbalanceDelta, int stackedLevels, int oppStackedLevels, int volHiPctl, int volExtremePctl, int rangeTightPctl, int seenExtremePctl, int deltaExtremeLoPctl, int deltaExtremeHiPctl, bool enableAlerts, int alertRearmSeconds, int minBarsBetweenSameSignal, bool showDashboardTable, TextPosition tableLocation, int tableOpacity, bool addHTF30mContext, bool enableSIB, bool enableABS, bool enableDD, bool enableDEIA, bool enableEEMDF, bool enableTF, bool enablePAR, bool enableDEB, string soundSIB, string soundABS, string soundDD, string soundDEIA, string soundEEMDF, string soundTF, string soundPAR, string soundDEB, bool enableDT, string soundDT)
		{
			return OrderFlowSetupScanner(Input, broadcastToHUD, lookbackN, edgeTolTicks, imbalanceRatio, minImbalanceDelta, stackedLevels, oppStackedLevels, volHiPctl, volExtremePctl, rangeTightPctl, seenExtremePctl, deltaExtremeLoPctl, deltaExtremeHiPctl, enableAlerts, alertRearmSeconds, minBarsBetweenSameSignal, showDashboardTable, tableLocation, tableOpacity, addHTF30mContext, enableSIB, enableABS, enableDD, enableDEIA, enableEEMDF, enableTF, enablePAR, enableDEB, soundSIB, soundABS, soundDD, soundDEIA, soundEEMDF, soundTF, soundPAR, soundDEB, enableDT, soundDT);
		}

		public OrderFlowSetupScanner OrderFlowSetupScanner(ISeries<double> input, bool broadcastToHUD, int lookbackN, int edgeTolTicks, double imbalanceRatio, int minImbalanceDelta, int stackedLevels, int oppStackedLevels, int volHiPctl, int volExtremePctl, int rangeTightPctl, int seenExtremePctl, int deltaExtremeLoPctl, int deltaExtremeHiPctl, bool enableAlerts, int alertRearmSeconds, int minBarsBetweenSameSignal, bool showDashboardTable, TextPosition tableLocation, int tableOpacity, bool addHTF30mContext, bool enableSIB, bool enableABS, bool enableDD, bool enableDEIA, bool enableEEMDF, bool enableTF, bool enablePAR, bool enableDEB, string soundSIB, string soundABS, string soundDD, string soundDEIA, string soundEEMDF, string soundTF, string soundPAR, string soundDEB, bool enableDT, string soundDT)
		{
			if (cacheOrderFlowSetupScanner != null)
				for (int idx = 0; idx < cacheOrderFlowSetupScanner.Length; idx++)
					if (cacheOrderFlowSetupScanner[idx] != null && cacheOrderFlowSetupScanner[idx].BroadcastToHUD == broadcastToHUD && cacheOrderFlowSetupScanner[idx].LookbackN == lookbackN && cacheOrderFlowSetupScanner[idx].EdgeTolTicks == edgeTolTicks && cacheOrderFlowSetupScanner[idx].ImbalanceRatio == imbalanceRatio && cacheOrderFlowSetupScanner[idx].MinImbalanceDelta == minImbalanceDelta && cacheOrderFlowSetupScanner[idx].StackedLevels == stackedLevels && cacheOrderFlowSetupScanner[idx].OppStackedLevels == oppStackedLevels && cacheOrderFlowSetupScanner[idx].VolHiPctl == volHiPctl && cacheOrderFlowSetupScanner[idx].VolExtremePctl == volExtremePctl && cacheOrderFlowSetupScanner[idx].RangeTightPctl == rangeTightPctl && cacheOrderFlowSetupScanner[idx].SeenExtremePctl == seenExtremePctl && cacheOrderFlowSetupScanner[idx].DeltaExtremeLoPctl == deltaExtremeLoPctl && cacheOrderFlowSetupScanner[idx].DeltaExtremeHiPctl == deltaExtremeHiPctl && cacheOrderFlowSetupScanner[idx].EnableAlerts == enableAlerts && cacheOrderFlowSetupScanner[idx].AlertRearmSeconds == alertRearmSeconds && cacheOrderFlowSetupScanner[idx].MinBarsBetweenSameSignal == minBarsBetweenSameSignal && cacheOrderFlowSetupScanner[idx].ShowDashboardTable == showDashboardTable && cacheOrderFlowSetupScanner[idx].TableLocation == tableLocation && cacheOrderFlowSetupScanner[idx].TableOpacity == tableOpacity && cacheOrderFlowSetupScanner[idx].AddHTF30mContext == addHTF30mContext && cacheOrderFlowSetupScanner[idx].EnableSIB == enableSIB && cacheOrderFlowSetupScanner[idx].EnableABS == enableABS && cacheOrderFlowSetupScanner[idx].EnableDD == enableDD && cacheOrderFlowSetupScanner[idx].EnableDEIA == enableDEIA && cacheOrderFlowSetupScanner[idx].EnableEEMDF == enableEEMDF && cacheOrderFlowSetupScanner[idx].EnableTF == enableTF && cacheOrderFlowSetupScanner[idx].EnablePAR == enablePAR && cacheOrderFlowSetupScanner[idx].EnableDEB == enableDEB && cacheOrderFlowSetupScanner[idx].SoundSIB == soundSIB && cacheOrderFlowSetupScanner[idx].SoundABS == soundABS && cacheOrderFlowSetupScanner[idx].SoundDD == soundDD && cacheOrderFlowSetupScanner[idx].SoundDEIA == soundDEIA && cacheOrderFlowSetupScanner[idx].SoundEEMDF == soundEEMDF && cacheOrderFlowSetupScanner[idx].SoundTF == soundTF && cacheOrderFlowSetupScanner[idx].SoundPAR == soundPAR && cacheOrderFlowSetupScanner[idx].SoundDEB == soundDEB && cacheOrderFlowSetupScanner[idx].EnableDT == enableDT && cacheOrderFlowSetupScanner[idx].SoundDT == soundDT && cacheOrderFlowSetupScanner[idx].EqualsInput(input))
						return cacheOrderFlowSetupScanner[idx];
			return CacheIndicator<OrderFlowSetupScanner>(new OrderFlowSetupScanner(){ BroadcastToHUD = broadcastToHUD, LookbackN = lookbackN, EdgeTolTicks = edgeTolTicks, ImbalanceRatio = imbalanceRatio, MinImbalanceDelta = minImbalanceDelta, StackedLevels = stackedLevels, OppStackedLevels = oppStackedLevels, VolHiPctl = volHiPctl, VolExtremePctl = volExtremePctl, RangeTightPctl = rangeTightPctl, SeenExtremePctl = seenExtremePctl, DeltaExtremeLoPctl = deltaExtremeLoPctl, DeltaExtremeHiPctl = deltaExtremeHiPctl, EnableAlerts = enableAlerts, AlertRearmSeconds = alertRearmSeconds, MinBarsBetweenSameSignal = minBarsBetweenSameSignal, ShowDashboardTable = showDashboardTable, TableLocation = tableLocation, TableOpacity = tableOpacity, AddHTF30mContext = addHTF30mContext, EnableSIB = enableSIB, EnableABS = enableABS, EnableDD = enableDD, EnableDEIA = enableDEIA, EnableEEMDF = enableEEMDF, EnableTF = enableTF, EnablePAR = enablePAR, EnableDEB = enableDEB, SoundSIB = soundSIB, SoundABS = soundABS, SoundDD = soundDD, SoundDEIA = soundDEIA, SoundEEMDF = soundEEMDF, SoundTF = soundTF, SoundPAR = soundPAR, SoundDEB = soundDEB, EnableDT = enableDT, SoundDT = soundDT }, input, ref cacheOrderFlowSetupScanner);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.OrderFlowSetupScanner OrderFlowSetupScanner(bool broadcastToHUD, int lookbackN, int edgeTolTicks, double imbalanceRatio, int minImbalanceDelta, int stackedLevels, int oppStackedLevels, int volHiPctl, int volExtremePctl, int rangeTightPctl, int seenExtremePctl, int deltaExtremeLoPctl, int deltaExtremeHiPctl, bool enableAlerts, int alertRearmSeconds, int minBarsBetweenSameSignal, bool showDashboardTable, TextPosition tableLocation, int tableOpacity, bool addHTF30mContext, bool enableSIB, bool enableABS, bool enableDD, bool enableDEIA, bool enableEEMDF, bool enableTF, bool enablePAR, bool enableDEB, string soundSIB, string soundABS, string soundDD, string soundDEIA, string soundEEMDF, string soundTF, string soundPAR, string soundDEB, bool enableDT, string soundDT)
		{
			return indicator.OrderFlowSetupScanner(Input, broadcastToHUD, lookbackN, edgeTolTicks, imbalanceRatio, minImbalanceDelta, stackedLevels, oppStackedLevels, volHiPctl, volExtremePctl, rangeTightPctl, seenExtremePctl, deltaExtremeLoPctl, deltaExtremeHiPctl, enableAlerts, alertRearmSeconds, minBarsBetweenSameSignal, showDashboardTable, tableLocation, tableOpacity, addHTF30mContext, enableSIB, enableABS, enableDD, enableDEIA, enableEEMDF, enableTF, enablePAR, enableDEB, soundSIB, soundABS, soundDD, soundDEIA, soundEEMDF, soundTF, soundPAR, soundDEB, enableDT, soundDT);
		}

		public Indicators.OrderFlowSetupScanner OrderFlowSetupScanner(ISeries<double> input , bool broadcastToHUD, int lookbackN, int edgeTolTicks, double imbalanceRatio, int minImbalanceDelta, int stackedLevels, int oppStackedLevels, int volHiPctl, int volExtremePctl, int rangeTightPctl, int seenExtremePctl, int deltaExtremeLoPctl, int deltaExtremeHiPctl, bool enableAlerts, int alertRearmSeconds, int minBarsBetweenSameSignal, bool showDashboardTable, TextPosition tableLocation, int tableOpacity, bool addHTF30mContext, bool enableSIB, bool enableABS, bool enableDD, bool enableDEIA, bool enableEEMDF, bool enableTF, bool enablePAR, bool enableDEB, string soundSIB, string soundABS, string soundDD, string soundDEIA, string soundEEMDF, string soundTF, string soundPAR, string soundDEB, bool enableDT, string soundDT)
		{
			return indicator.OrderFlowSetupScanner(input, broadcastToHUD, lookbackN, edgeTolTicks, imbalanceRatio, minImbalanceDelta, stackedLevels, oppStackedLevels, volHiPctl, volExtremePctl, rangeTightPctl, seenExtremePctl, deltaExtremeLoPctl, deltaExtremeHiPctl, enableAlerts, alertRearmSeconds, minBarsBetweenSameSignal, showDashboardTable, tableLocation, tableOpacity, addHTF30mContext, enableSIB, enableABS, enableDD, enableDEIA, enableEEMDF, enableTF, enablePAR, enableDEB, soundSIB, soundABS, soundDD, soundDEIA, soundEEMDF, soundTF, soundPAR, soundDEB, enableDT, soundDT);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.OrderFlowSetupScanner OrderFlowSetupScanner(bool broadcastToHUD, int lookbackN, int edgeTolTicks, double imbalanceRatio, int minImbalanceDelta, int stackedLevels, int oppStackedLevels, int volHiPctl, int volExtremePctl, int rangeTightPctl, int seenExtremePctl, int deltaExtremeLoPctl, int deltaExtremeHiPctl, bool enableAlerts, int alertRearmSeconds, int minBarsBetweenSameSignal, bool showDashboardTable, TextPosition tableLocation, int tableOpacity, bool addHTF30mContext, bool enableSIB, bool enableABS, bool enableDD, bool enableDEIA, bool enableEEMDF, bool enableTF, bool enablePAR, bool enableDEB, string soundSIB, string soundABS, string soundDD, string soundDEIA, string soundEEMDF, string soundTF, string soundPAR, string soundDEB, bool enableDT, string soundDT)
		{
			return indicator.OrderFlowSetupScanner(Input, broadcastToHUD, lookbackN, edgeTolTicks, imbalanceRatio, minImbalanceDelta, stackedLevels, oppStackedLevels, volHiPctl, volExtremePctl, rangeTightPctl, seenExtremePctl, deltaExtremeLoPctl, deltaExtremeHiPctl, enableAlerts, alertRearmSeconds, minBarsBetweenSameSignal, showDashboardTable, tableLocation, tableOpacity, addHTF30mContext, enableSIB, enableABS, enableDD, enableDEIA, enableEEMDF, enableTF, enablePAR, enableDEB, soundSIB, soundABS, soundDD, soundDEIA, soundEEMDF, soundTF, soundPAR, soundDEB, enableDT, soundDT);
		}

		public Indicators.OrderFlowSetupScanner OrderFlowSetupScanner(ISeries<double> input , bool broadcastToHUD, int lookbackN, int edgeTolTicks, double imbalanceRatio, int minImbalanceDelta, int stackedLevels, int oppStackedLevels, int volHiPctl, int volExtremePctl, int rangeTightPctl, int seenExtremePctl, int deltaExtremeLoPctl, int deltaExtremeHiPctl, bool enableAlerts, int alertRearmSeconds, int minBarsBetweenSameSignal, bool showDashboardTable, TextPosition tableLocation, int tableOpacity, bool addHTF30mContext, bool enableSIB, bool enableABS, bool enableDD, bool enableDEIA, bool enableEEMDF, bool enableTF, bool enablePAR, bool enableDEB, string soundSIB, string soundABS, string soundDD, string soundDEIA, string soundEEMDF, string soundTF, string soundPAR, string soundDEB, bool enableDT, string soundDT)
		{
			return indicator.OrderFlowSetupScanner(input, broadcastToHUD, lookbackN, edgeTolTicks, imbalanceRatio, minImbalanceDelta, stackedLevels, oppStackedLevels, volHiPctl, volExtremePctl, rangeTightPctl, seenExtremePctl, deltaExtremeLoPctl, deltaExtremeHiPctl, enableAlerts, alertRearmSeconds, minBarsBetweenSameSignal, showDashboardTable, tableLocation, tableOpacity, addHTF30mContext, enableSIB, enableABS, enableDD, enableDEIA, enableEEMDF, enableTF, enablePAR, enableDEB, soundSIB, soundABS, soundDD, soundDEIA, soundEEMDF, soundTF, soundPAR, soundDEB, enableDT, soundDT);
		}
	}
}

#endregion
