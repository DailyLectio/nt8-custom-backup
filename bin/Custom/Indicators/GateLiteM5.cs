#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GateLiteM5 : Indicator
    {
        private const string Header = "timestamp_et,model_version,account,profile_id,mode_code,variant,instrument,tod_bucket,regime_state,hmm_state,final_regime,final_direction,allow_expansion,allow_momo,allow_adx_di,gate_recommendation,gate_decision,allow_long,allow_short,reason_code,would_allow_m5a,would_allow_m5b,would_allow_m5o,operator_override,operator_decision,baseline_trade_taken";
        private DateTime lastRefreshUtc = DateTime.MinValue;
        private string lastLogStamp = "";

        [Browsable(false)]
        [XmlIgnore]
        public bool AllowLong { get; private set; }

        [Browsable(false)]
        [XmlIgnore]
        public bool AllowShort { get; private set; }

        [Browsable(false)]
        [XmlIgnore]
        public string GateRecommendation { get; private set; }

        [Browsable(false)]
        [XmlIgnore]
        public string ReasonCode { get; private set; }

        [Browsable(false)]
        [XmlIgnore]
        public string RegimeState { get; private set; }

        [Browsable(false)]
        [XmlIgnore]
        public string FinalRegime { get; private set; }

        [Browsable(false)]
        [XmlIgnore]
        public string FinalDirection { get; private set; }

        [Browsable(false)]
        [XmlIgnore]
        public string HmmState { get; private set; }

        [NinjaScriptProperty]
        [Display(Name = "Account Name", Order = 0, GroupName = "Model 5")]
        public string AccountName { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profile Id", Order = 1, GroupName = "Model 5")]
        public string ProfileId { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mode Code", Description = "M5A, M5B, M5O, or M5C.", Order = 2, GroupName = "Model 5")]
        public string ModeCode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Variant", Order = 3, GroupName = "Model 5")]
        public string Variant { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Operator Override", Description = "Only used for M5O/operator charts.", Order = 4, GroupName = "Model 5")]
        public bool OperatorOverride { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Operator Decision", Description = "ON, WATCH, or OFF.", Order = 5, GroupName = "Model 5")]
        public string OperatorDecision { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "NQ Matrix Path", Order = 10, GroupName = "Files")]
        public string NqMatrixPath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ES Matrix Path", Order = 11, GroupName = "Files")]
        public string EsMatrixPath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Decision Log Path", Description = "Use {date} for yyyy-MM-dd.", Order = 12, GroupName = "Files")]
        public string DecisionLogPath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Refresh Seconds", Order = 20, GroupName = "Runtime")]
        public int RefreshSeconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Freshness Minutes", Order = 21, GroupName = "Runtime")]
        public int FreshnessMinutes { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "GateLiteM5";
                Description = "Model 5A chart gate and decision logger. Keeps V3C/V3D HUDs untouched.";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = false;
                IsSuspendedWhileInactive = false;
                AccountName = "";
                ProfileId = "";
                ModeCode = "M5C";
                Variant = "001";
                OperatorOverride = false;
                OperatorDecision = "WATCH";
                NqMatrixPath = @"C:\Users\Valued Customer\NT8_Regimes\V3D\NQ_RegimeMatrix_Latest.csv";
                EsMatrixPath = @"C:\Users\Valued Customer\NT8_Regimes\V3D\ES_RegimeMatrix_Latest.csv";
                DecisionLogPath = @"C:\Users\Valued Customer\NT8_Regimes\5A\Logs\GateDecisions\gate_decisions_{date}.csv";
                RefreshSeconds = 15;
                FreshnessMinutes = 7;
                GateRecommendation = "WATCH";
                ReasonCode = "INIT";
                RegimeState = "NO_REGIME_FILE";
                FinalRegime = "";
                FinalDirection = "";
                HmmState = "";
            }
        }

        protected override void OnBarUpdate()
        {
            if ((DateTime.UtcNow - lastRefreshUtc).TotalSeconds < Math.Max(3, RefreshSeconds))
                return;

            lastRefreshUtc = DateTime.UtcNow;
            EvaluateAndLog();
        }

        private void EvaluateAndLog()
        {
            string instrument = ResolveInstrument();
            MatrixContext ctx = ReadMatrixContext(instrument);
            string tod = ToTodBucket(Time[0]);
            string mode = Clean(ModeCode).ToUpperInvariant();
            string profile = Clean(ProfileId);
            string variant = Clean(Variant);

            bool wouldA = WouldAllowA(profile, variant, Time[0], ctx, true);
            bool wouldB = WouldAllowB(profile, Time[0], ctx, true);
            bool wouldO = OperatorOverride && Clean(OperatorDecision).Equals("ON", StringComparison.OrdinalIgnoreCase);

            bool allowLong = false;
            bool allowShort = false;
            string rec = "WATCH";
            string reason = ctx.State;

            if (mode == "M5C")
            {
                rec = "WATCH";
                allowLong = true;
                allowShort = true;
                reason = "BASELINE_CONTEXT_CAPTURE";
            }
            else if (mode == "M5A")
            {
                allowLong = WouldAllowA(profile, variant, Time[0], ctx, true);
                allowShort = WouldAllowA(profile, variant, Time[0], ctx, false);
                rec = (allowLong || allowShort) ? "ON" : "OFF";
                reason = (allowLong || allowShort) ? "M5A_ALLOWLIST_PASS" : "M5A_ALLOWLIST_BLOCK";
            }
            else if (mode == "M5B")
            {
                allowLong = WouldAllowB(profile, Time[0], ctx, true);
                allowShort = WouldAllowB(profile, Time[0], ctx, false);
                rec = (allowLong || allowShort) ? "ON" : "OFF";
                reason = (allowLong || allowShort) ? "M5B_BLOCKLIST_PASS" : "M5B_BLOCKLIST_BLOCK";
            }
            else if (mode == "M5O")
            {
                bool baseRec = WouldAllowB(profile, Time[0], ctx, true) || WouldAllowA(profile, variant, Time[0], ctx, true);
                rec = baseRec ? "ON" : "WATCH";
                if (OperatorOverride)
                    rec = Clean(OperatorDecision).ToUpperInvariant();
                allowLong = rec == "ON";
                allowShort = rec == "ON" && !IsLongOnlyProfile(profile);
                reason = OperatorOverride ? "M5O_OPERATOR_DECISION" : "M5O_OPERATOR_REQUIRED";
            }

            AllowLong = allowLong;
            AllowShort = allowShort;
            GateRecommendation = rec;
            ReasonCode = reason;
            RegimeState = ctx.State;
            FinalRegime = ctx.FinalRegime;
            FinalDirection = ctx.FinalDirection;
            HmmState = ctx.HmmState;

            Draw.TextFixed(this, "GateLiteM5Status",
                "M5 " + mode + " " + rec + "\n" +
                "Acct: " + Clean(AccountName) + "\n" +
                "Regime: " + Safe(ctx.FinalRegime, ctx.State) + " / " + Safe(ctx.HmmState, "-") + "\n" +
                "Long: " + (allowLong ? "Y" : "N") + "  Short: " + (allowShort ? "Y" : "N") + "\n" +
                reason,
                TextPosition.TopLeft);

            string stamp = Time[0].ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "|" + mode + "|" + rec + "|" + ctx.State;
            if (stamp != lastLogStamp)
            {
                lastLogStamp = stamp;
                AppendDecisionLog(instrument, tod, ctx, rec, allowLong || allowShort ? "ALLOW" : "BLOCK", allowLong, allowShort, reason, wouldA, wouldB, wouldO);
            }
        }

        private bool WouldAllowA(string profile, string variant, DateTime now, MatrixContext ctx, bool isLong)
        {
            if (ctx.State == "NO_REGIME_FILE" || ctx.State == "STALE_REGIME")
                return false;
            if (ctx.State == "OPEN_PRE_HMM" && !profile.Contains("ADX_NQ_5A"))
                return false;

            TimeSpan t = now.TimeOfDay;
            string p = profile.ToUpperInvariant();

            if (p.Contains("V3D_EXP"))
                return isLong && InWindow(t, "12:00", "15:59") && (variant == "002" || ctx.AllowExpansion);
            if (p.Contains("V3C_ADX"))
                return isLong && InWindow(t, "09:30", "12:00") && (variant == "002" || ctx.AllowAdxDi);
            if (p.Contains("1OG_MOMO"))
                return InWindow(t, variant == "002" ? "11:30" : "12:30", "15:59") && !IsBlockedRegime(ctx, "TRANSITION", "ROTATION_LIQUID");
            if (p.Contains("V3C_EXP"))
                return InWindow(t, "12:00", "15:59") && !InWindow(t, "10:00", "11:59");

            return false;
        }

        private bool WouldAllowB(string profile, DateTime now, MatrixContext ctx, bool isLong)
        {
            if (ctx.State == "NO_REGIME_FILE" || ctx.State == "STALE_REGIME")
                return false;
            TimeSpan t = now.TimeOfDay;
            string p = profile.ToUpperInvariant();

            if (p.Contains("V3D_EXP"))
                return !InWindow(t, "10:00", "11:59") && ctx.State != "OPEN_PRE_HMM" && !IsBlockedRegime(ctx, "TREND_EMERGING") && !(ctx.AllowExpansion && !isLong);
            if (p.Contains("V3C_ADX"))
                return isLong && !InWindow(t, "12:00", "14:59") && !IsBlockedRegime(ctx, "ROTATION_LIQUID");
            if (p.Contains("1OG_MOMO"))
                return !InWindow(t, "09:30", "10:29") && !IsBlockedRegime(ctx, "TRANSITION", "ROTATION_LIQUID");
            if (p.Contains("V3C_EXP"))
                return !InWindow(t, "10:00", "11:59") && !IsBlockedRegime(ctx, "BALANCE", "ROTATION_LIQUID_CHOP");

            return true;
        }

        private MatrixContext ReadMatrixContext(string instrument)
        {
            MatrixContext ctx = new MatrixContext();
            ctx.State = "NO_REGIME_FILE";
            string path = instrument.StartsWith("ES", StringComparison.OrdinalIgnoreCase) ? EsMatrixPath : NqMatrixPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return ApplyOpenPreHmm(ctx);

            try
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length < 2)
                    return ApplyOpenPreHmm(ctx);

                string[] header = SplitCsv(lines[0]);
                string[] values = SplitCsv(lines[lines.Length - 1]);
                ctx.FinalRegime = GetColumn(header, values, "FinalRegime", "Regime", "Environment");
                ctx.FinalDirection = GetColumn(header, values, "FinalDirection", "Direction");
                ctx.HmmState = GetColumn(header, values, "HMMRegime", "HMM", "HMM_State", "HmmState");
                ctx.HmmDirection = GetColumn(header, values, "HMMDirection", "HMMDirection", "HmmDirection");
                ctx.AllowExpansion = ParseBool(GetColumn(header, values, "AllowExpansion", "Allow_Expansion"));
                ctx.AllowMomo = ParseBool(GetColumn(header, values, "AllowMomo", "Allow_Momo"));
                ctx.AllowAdxDi = ParseBool(GetColumn(header, values, "AllowADX_DI", "AllowAdxDi", "AllowADXDI"));

                DateTime rowTime;
                string ts = GetColumn(header, values, "TimestampET", "timestamp", "Timestamp", "Time", "DateTime");
                if (DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out rowTime)
                    && (DateTime.Now - rowTime).TotalMinutes > Math.Max(1, FreshnessMinutes))
                    ctx.State = "STALE_REGIME";
                else
                    ctx.State = "OK";
            }
            catch
            {
                ctx.State = "NO_REGIME_FILE";
            }

            return ApplyOpenPreHmm(ctx);
        }

        private MatrixContext ApplyOpenPreHmm(MatrixContext ctx)
        {
            TimeSpan t = DateTime.Now.TimeOfDay;
            if (InWindow(t, "09:30", "09:34"))
                ctx.State = "OPEN_PRE_HMM";
            return ctx;
        }

        private void AppendDecisionLog(string instrument, string tod, MatrixContext ctx, string rec, string decision, bool allowLong, bool allowShort, string reason, bool wouldA, bool wouldB, bool wouldO)
        {
            try
            {
                string path = DecisionLogPath.Replace("{date}", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                bool exists = File.Exists(path);
                StringBuilder sb = new StringBuilder();
                if (!exists)
                    sb.AppendLine(Header);
                sb.AppendLine(string.Join(",", new string[]
                {
                    Csv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    "M5",
                    Csv(AccountName),
                    Csv(ProfileId),
                    Csv(ModeCode),
                    Csv(Variant),
                    Csv(instrument),
                    Csv(tod),
                    Csv(ctx.State),
                    Csv(ctx.HmmState),
                    Csv(ctx.FinalRegime),
                    Csv(ctx.FinalDirection),
                    ctx.AllowExpansion ? "1" : "0",
                    ctx.AllowMomo ? "1" : "0",
                    ctx.AllowAdxDi ? "1" : "0",
                    Csv(rec),
                    Csv(decision),
                    allowLong ? "1" : "0",
                    allowShort ? "1" : "0",
                    Csv(reason),
                    wouldA ? "1" : "0",
                    wouldB ? "1" : "0",
                    wouldO ? "1" : "0",
                    OperatorOverride ? "1" : "0",
                    Csv(OperatorDecision),
                    ModeCode != null && ModeCode.Equals("M5C", StringComparison.OrdinalIgnoreCase) ? "1" : "0"
                }));
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private string ResolveInstrument()
        {
            try
            {
                if (Instrument != null && Instrument.MasterInstrument != null)
                    return Instrument.MasterInstrument.Name;
            }
            catch
            {
            }
            return "NQ";
        }

        private static bool IsLongOnlyProfile(string profile)
        {
            string p = (profile ?? "").ToUpperInvariant();
            return p.Contains("V3D_EXP") || p.Contains("V3C_ADX");
        }

        private static bool IsBlockedRegime(MatrixContext ctx, params string[] blocked)
        {
            string r = ((ctx.FinalRegime ?? "") + " " + (ctx.HmmState ?? "") + " " + (ctx.HmmDirection ?? "") + " " + (ctx.State ?? "")).ToUpperInvariant();
            return blocked.Any(b => r.Contains((b ?? "").ToUpperInvariant()));
        }

        private static bool InWindow(TimeSpan time, string start, string end)
        {
            TimeSpan s = TimeSpan.Parse(start, CultureInfo.InvariantCulture);
            TimeSpan e = TimeSpan.Parse(end, CultureInfo.InvariantCulture);
            return time >= s && time <= e;
        }

        private static string ToTodBucket(DateTime dt)
        {
            int minute = dt.Minute < 30 ? 0 : 30;
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, minute, 0).ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private static string[] SplitCsv(string line)
        {
            return Regex.Split(line ?? "", ",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)").Select(s => s.Trim().Trim('"')).ToArray();
        }

        private static string GetColumn(string[] header, string[] values, params string[] names)
        {
            for (int i = 0; i < header.Length && i < values.Length; i++)
                foreach (string name in names)
                    if (header[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                        return values[i];
            return "";
        }

        private static bool ParseBool(string value)
        {
            string v = (value ?? "").Trim().ToUpperInvariant();
            return v == "1" || v == "TRUE" || v == "YES" || v == "PASS" || v == "ALLOW";
        }

        private static string Clean(string value)
        {
            return (value ?? "").Trim();
        }

        private static string Safe(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\r") || value.Contains("\n"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private class MatrixContext
        {
            public string State = "";
            public string FinalRegime = "";
            public string FinalDirection = "";
            public string HmmState = "";
            public string HmmDirection = "";
            public bool AllowExpansion;
            public bool AllowMomo;
            public bool AllowAdxDi;
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GateLiteM5[] cacheGateLiteM5;
		public GateLiteM5 GateLiteM5(string accountName, string profileId, string modeCode, string variant, bool operatorOverride, string operatorDecision, string nqMatrixPath, string esMatrixPath, int refreshSeconds, int freshnessMinutes)
		{
			return GateLiteM5(Input, accountName, profileId, modeCode, variant, operatorOverride, operatorDecision, nqMatrixPath, esMatrixPath, refreshSeconds, freshnessMinutes);
		}

		public GateLiteM5 GateLiteM5(ISeries<double> input, string accountName, string profileId, string modeCode, string variant, bool operatorOverride, string operatorDecision, string nqMatrixPath, string esMatrixPath, int refreshSeconds, int freshnessMinutes)
		{
			if (cacheGateLiteM5 != null)
				for (int idx = 0; idx < cacheGateLiteM5.Length; idx++)
					if (cacheGateLiteM5[idx] != null && cacheGateLiteM5[idx].AccountName == accountName && cacheGateLiteM5[idx].ProfileId == profileId && cacheGateLiteM5[idx].ModeCode == modeCode && cacheGateLiteM5[idx].Variant == variant && cacheGateLiteM5[idx].OperatorOverride == operatorOverride && cacheGateLiteM5[idx].OperatorDecision == operatorDecision && cacheGateLiteM5[idx].NqMatrixPath == nqMatrixPath && cacheGateLiteM5[idx].EsMatrixPath == esMatrixPath && cacheGateLiteM5[idx].RefreshSeconds == refreshSeconds && cacheGateLiteM5[idx].FreshnessMinutes == freshnessMinutes && cacheGateLiteM5[idx].EqualsInput(input))
						return cacheGateLiteM5[idx];
			return CacheIndicator<GateLiteM5>(new GateLiteM5(){ AccountName = accountName, ProfileId = profileId, ModeCode = modeCode, Variant = variant, OperatorOverride = operatorOverride, OperatorDecision = operatorDecision, NqMatrixPath = nqMatrixPath, EsMatrixPath = esMatrixPath, RefreshSeconds = refreshSeconds, FreshnessMinutes = freshnessMinutes }, input, ref cacheGateLiteM5);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GateLiteM5 GateLiteM5(string accountName, string profileId, string modeCode, string variant, bool operatorOverride, string operatorDecision, string nqMatrixPath, string esMatrixPath, int refreshSeconds, int freshnessMinutes)
		{
			return indicator.GateLiteM5(Input, accountName, profileId, modeCode, variant, operatorOverride, operatorDecision, nqMatrixPath, esMatrixPath, refreshSeconds, freshnessMinutes);
		}

		public Indicators.GateLiteM5 GateLiteM5(ISeries<double> input , string accountName, string profileId, string modeCode, string variant, bool operatorOverride, string operatorDecision, string nqMatrixPath, string esMatrixPath, int refreshSeconds, int freshnessMinutes)
		{
			return indicator.GateLiteM5(input, accountName, profileId, modeCode, variant, operatorOverride, operatorDecision, nqMatrixPath, esMatrixPath, refreshSeconds, freshnessMinutes);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GateLiteM5 GateLiteM5(string accountName, string profileId, string modeCode, string variant, bool operatorOverride, string operatorDecision, string nqMatrixPath, string esMatrixPath, int refreshSeconds, int freshnessMinutes)
		{
			return indicator.GateLiteM5(Input, accountName, profileId, modeCode, variant, operatorOverride, operatorDecision, nqMatrixPath, esMatrixPath, refreshSeconds, freshnessMinutes);
		}

		public Indicators.GateLiteM5 GateLiteM5(ISeries<double> input , string accountName, string profileId, string modeCode, string variant, bool operatorOverride, string operatorDecision, string nqMatrixPath, string esMatrixPath, int refreshSeconds, int freshnessMinutes)
		{
			return indicator.GateLiteM5(input, accountName, profileId, modeCode, variant, operatorOverride, operatorDecision, nqMatrixPath, esMatrixPath, refreshSeconds, freshnessMinutes);
		}
	}
}

#endregion
