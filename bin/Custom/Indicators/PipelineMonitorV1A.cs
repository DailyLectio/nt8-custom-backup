// PipelineMonitor_V1A.cs
// Lightweight V1A/V1B infrastructure health HUD.
// Load once on a V1A leader/support chart. Do not add to every strategy tab.

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class PipelineMonitor_V1A : Indicator
    {
        [NinjaScriptProperty]
        [Display(Name = "Data Folder Path", Description = "Base NT8_Regimes folder.", GroupName = "Pipeline Monitor", Order = 0)]
        public string DataFolderPath { get; set; } = @"C:\Users\Valued Customer\NT8_Regimes";

        [NinjaScriptProperty]
        [Display(Name = "Refresh Seconds", Description = "How often to re-check files.", GroupName = "Pipeline Monitor", Order = 1)]
        public int RefreshSeconds { get; set; } = 30;

        [NinjaScriptProperty]
        [Display(Name = "Footprint History Start Date", Description = "Informational display only.", GroupName = "Pipeline Monitor", Order = 2)]
        public string FootprintHistoryStartDate { get; set; } = "2024-06-01";

        private readonly SimpleFont panelFont = new SimpleFont("Consolas", 12);
        private DateTime lastCheck = DateTime.MinValue;

        private string liveStatus = "CHECKING";
        private string valueAreaStatus = "CHECKING";
        private string footprintStatus = "CHECKING";
        private string biasStatus = "CHECKING";
        private string tradeLogStatus = "CHECKING";

        private Brush liveBrush = Brushes.Gray;
        private Brush valueAreaBrush = Brushes.Gray;
        private Brush footprintBrush = Brushes.Gray;
        private Brush biasBrush = Brushes.Gray;
        private Brush tradeLogBrush = Brushes.Gray;
        private Brush panelBrush = Brushes.DimGray;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "V1A/V1B pipeline health monitor. Load once on a leader/support chart.";
                Name = "PipelineMonitor_V1A";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = true;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            if ((DateTime.Now - lastCheck).TotalSeconds >= Math.Max(5, RefreshSeconds))
            {
                lastCheck = DateTime.Now;
                RefreshStatus();
            }

            DrawPanel();
        }

        private void RefreshStatus()
        {
            string livePath = Path.Combine(DataFolderPath, @"Exports\NQ_1min_export.txt");
            string valueAreaPath = Path.Combine(DataFolderPath, @"Exports\ValueArea_NQ.csv");
            string footprintPath = Path.Combine(DataFolderPath, @"Active\Footprint_Export.csv");
            string tradeLogDir = Path.Combine(DataFolderPath, @"V1A\TradeLog");

            double liveAge = FileAgeMinutes(livePath);
            if (liveAge < 3)
            {
                liveStatus = "LIVE " + AgeStr(liveAge);
                liveBrush = Brushes.LimeGreen;
            }
            else if (liveAge < 10)
            {
                liveStatus = "WARN " + AgeStr(liveAge);
                liveBrush = Brushes.Gold;
            }
            else
            {
                liveStatus = liveAge >= 9999 ? "MISSING" : "DOWN " + AgeStr(liveAge);
                liveBrush = Brushes.OrangeRed;
            }

            DateTime vaDate = LastCsvDate(valueAreaPath);
            if (vaDate.Date == DateTime.Today)
            {
                valueAreaStatus = "TODAY";
                valueAreaBrush = Brushes.LimeGreen;
            }
            else if (vaDate.Date == DateTime.Today.AddDays(-1))
            {
                valueAreaStatus = "YESTERDAY";
                valueAreaBrush = Brushes.Gold;
            }
            else
            {
                valueAreaStatus = vaDate == DateTime.MinValue ? "MISSING" : vaDate.ToString("yyyy-MM-dd");
                valueAreaBrush = Brushes.OrangeRed;
            }

            int footprintRows = HasDataRows(footprintPath) ? 1 : 0;
            double footprintAge = FileAgeMinutes(footprintPath);
            if (footprintRows > 0 && File.GetLastWriteTime(footprintPath).Date == DateTime.Today)
            {
                footprintStatus = "LIVE Jun 2024+";
                footprintBrush = Brushes.LimeGreen;
            }
            else if (footprintRows > 0)
            {
                footprintStatus = "STALE " + AgeStr(footprintAge);
                footprintBrush = Brushes.Gold;
            }
            else
            {
                footprintStatus = "MISSING/EMPTY";
                footprintBrush = Brushes.OrangeRed;
            }

            try
            {
                string bias = HUDMessenger.CurrentDailyBias;
                biasStatus = string.IsNullOrWhiteSpace(bias) ? "N/A" : bias.Trim();
                biasBrush = biasStatus == "N/A" ? Brushes.Gray : Brushes.DeepSkyBlue;
            }
            catch
            {
                biasStatus = "N/A";
                biasBrush = Brushes.Gray;
            }

            DateTime tradeLogDate = LatestFileDate(tradeLogDir, "*.csv");
            if (tradeLogDate.Date == DateTime.Today)
            {
                tradeLogStatus = "TODAY";
                tradeLogBrush = Brushes.LimeGreen;
            }
            else if (tradeLogDate.Date == DateTime.Today.AddDays(-1))
            {
                tradeLogStatus = "YESTERDAY";
                tradeLogBrush = Brushes.Gold;
            }
            else
            {
                tradeLogStatus = "NO SESSION LOG";
                tradeLogBrush = Brushes.OrangeRed;
            }

            panelBrush = liveBrush == Brushes.OrangeRed || valueAreaBrush == Brushes.OrangeRed ||
                         footprintBrush == Brushes.OrangeRed || tradeLogBrush == Brushes.OrangeRed
                         ? Brushes.DarkRed
                         : liveBrush == Brushes.Gold || valueAreaBrush == Brushes.Gold ||
                           footprintBrush == Brushes.Gold || tradeLogBrush == Brushes.Gold
                           ? Brushes.DarkGoldenrod
                           : Brushes.DarkGreen;
        }

        private void DrawPanel()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("V1A PIPELINE MONITOR");
            sb.AppendLine(StatusDot(liveBrush) + " LIVE FEED    " + liveStatus);
            sb.AppendLine(StatusDot(valueAreaBrush) + " VALUE AREA   " + valueAreaStatus);
            sb.AppendLine(StatusDot(footprintBrush) + " FOOTPRINT    " + footprintStatus);
            sb.AppendLine(StatusDot(biasBrush) + " BIAS         " + biasStatus);
            sb.AppendLine(StatusDot(tradeLogBrush) + " TRADE LOG    " + tradeLogStatus);

            Draw.TextFixed(this, "PipelineMonitorV1A_Status", sb.ToString(), TextPosition.TopLeft,
                Brushes.White, panelFont, Brushes.Transparent, panelBrush, 80);
        }

        private string StatusDot(Brush brush)
        {
            if (brush == Brushes.LimeGreen) return "[G]";
            if (brush == Brushes.Gold) return "[Y]";
            if (brush == Brushes.OrangeRed) return "[R]";
            return "[I]";
        }

        private double FileAgeMinutes(string path)
        {
            if (!File.Exists(path))
                return 9999;

            return (DateTime.Now - File.GetLastWriteTime(path)).TotalMinutes;
        }

        private string AgeStr(double mins)
        {
            if (mins >= 9999) return "MISSING";
            if (mins < 1) return "<1m";
            if (mins < 60) return ((int)mins).ToString() + "m";
            return ((int)(mins / 60)).ToString() + "h " + ((int)(mins % 60)).ToString() + "m";
        }

        private bool HasDataRows(string path)
        {
            if (!File.Exists(path))
                return false;

            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs))
                {
                    int nonEmpty = 0;
                    while (!sr.EndOfStream && nonEmpty < 3)
                    {
                        string line = sr.ReadLine();
                        if (!string.IsNullOrWhiteSpace(line))
                            nonEmpty++;
                    }

                    return nonEmpty >= 2;
                }
            }
            catch
            {
                return false;
            }
        }

        private DateTime LastCsvDate(string path)
        {
            if (!File.Exists(path))
                return DateTime.MinValue;

            try
            {
                string last = "";
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                        if (!string.IsNullOrWhiteSpace(line))
                            last = line;
                }

                if (string.IsNullOrWhiteSpace(last) || last.IndexOf(',') < 0)
                    return DateTime.MinValue;

                string first = last.Split(',')[0].Trim();
                DateTime dt;
                return DateTime.TryParse(first, out dt) ? dt : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private DateTime LatestFileDate(string dir, string pattern)
        {
            if (!Directory.Exists(dir))
                return DateTime.MinValue;

            DateTime latest = DateTime.MinValue;
            foreach (string file in Directory.GetFiles(dir, pattern))
            {
                DateTime t = File.GetLastWriteTime(file);
                if (t > latest)
                    latest = t;
            }

            return latest;
        }
    }
}
