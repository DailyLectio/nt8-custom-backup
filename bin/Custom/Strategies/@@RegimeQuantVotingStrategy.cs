#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class RegimeQuantVotingStrategy : Strategy
    {
        // Indicators
        private RSI rsi;
        private ROC momentum; // Rate of Change represents % momentum
        private ATR volatility; // Using ATR / Close for % volatility
        private SMA volSma;
        private ADX adx;
        private EMA ema50;
        private EMA ema200;
        private MACD macd;

        // State Tracking
        private DateTime lastExitTime = DateTime.MinValue;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Replicates the 8-point voting system from the Quant tutorial.";
                Name = "RegimeQuantVotingStrategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
            }
            else if (State == State.DataLoaded)
            {
                // Initialize Indicators
                rsi = RSI(14, 1);
                momentum = ROC(14); 
                volatility = ATR(14); 
                volSma = SMA(VOL(), 20);
                adx = ADX(14);
                ema50 = EMA(50);
                ema200 = EMA(200);
                macd = MACD(12, 26, 9);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 200) return; // Wait for EMA200 to populate

            // -------------------------------------------------------------
            // 1. REGIME DETECTION (Read from CSV)
            // -------------------------------------------------------------
            bool isBullRegime = false; 
            bool isBearRegime = false; 

            // Dynamically build the path based on the chart's instrument (e.g., ES, NQ, GC)
            string fileName = Instrument.MasterInstrument.Name + "_Regimes.csv";
            // CORRECTED PATH FOR NT8
			string filePath = @"C:\Users\Valued Customer\NT8_Regimes\" + fileName;

            try
            {
                // Read the file. (We do this safely to avoid locking issues)
                using (var stream = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (var reader = new System.IO.StreamReader(stream))
                {
                    string lastLine = "";
                    string currentLine;
                    while ((currentLine = reader.ReadLine()) != null)
                    {
                        lastLine = currentLine; // Keep overwriting until we hit the end
                    }

                    if (!string.IsNullOrEmpty(lastLine))
                    {
                        string[] columns = lastLine.Split(',');
                        string currentRegime = columns[columns.Length - 1].Trim(); // The Regime Label is the final column

                        if (currentRegime == "Bull") isBullRegime = true;
                        if (currentRegime == "Bear") isBearRegime = true;
                    }
                }
            }
            catch (Exception e)
            {
                // If the file is missing or locked, print an error to the Output window and stay flat
                Print("Regime file error for " + Instrument.MasterInstrument.Name + ": " + e.Message);
                return; 
            }

            // -------------------------------------------------------------
            // 2. RISK MANAGEMENT: COOLDOWN & BEAR EXITS
            // -------------------------------------------------------------
            // Exit Rule: Close position immediately if Regime flips to Bear/Crash
            if (Position.MarketPosition == MarketPosition.Long && isBearRegime)
            {
                ExitLong("BearRegimeExit", "LongEntry");
                lastExitTime = Time[0];
                return; 
            }

            // Cooldown: Enforce a hard 48-hour cooldown after ANY exit
            if ((Time[0] - lastExitTime).TotalHours < 48)
            {
                return; // Do not process entries during cooldown
            }

            // -------------------------------------------------------------
            // 3. VOTING SYSTEM (8 Confirmations)
            // -------------------------------------------------------------
            int votes = 0;

            // Condition 1: RSI < 90
            if (rsi[0] < 90) votes++;

            // Condition 2: Momentum > 1%
            if (momentum[0] > 1.0) votes++;

            // Condition 3: Volatility < 6%
            double volPercent = (volatility[0] / Close[0]) * 100;
            if (volPercent < 6.0) votes++;

            // Condition 4: Volume > 20-period SMA
            if (Volume[0] > volSma[0]) votes++;

            // Condition 5: ADX > 25
            if (adx[0] > 25) votes++;

            // Condition 6: Price > EMA 50
            if (Close[0] > ema50[0]) votes++;

            // Condition 7: Price > EMA 200
            if (Close[0] > ema200[0]) votes++;

            // Condition 8: MACD > Signal Line
            if (macd.Default[0] > macd.Avg[0]) votes++;

            // -------------------------------------------------------------
            // 4. ENTRY LOGIC
            // -------------------------------------------------------------
            // We only enter a trade if the HMM Regime is Bullish AND at least 7 out of 8 met
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (isBullRegime && votes >= 7)
                {
                    EnterLong("LongEntry");
                }
            }
        }

        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            // Record exit time to start the 48-hour cooldown clock
            if (marketPosition == MarketPosition.Flat)
            {
                lastExitTime = Time[0];
            }
        }
    }
}