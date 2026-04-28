import pandas as pd
import numpy as np
import time
import os
from hmmlearn import hmm

# --- FILE PATHS (Ensure these match your actual files) ---
# Your 90-day base file (Update this manually once a month)
BASE_FILE = "NQ_Stitched_Continuous_Data.txt" 

# The live file NinjaTrader is updating every minute
LIVE_FILE = "Live_NQ_Data.txt"

# The output file your HUD indicator reads
OUTPUT_FILE = "NQ_Regimes.csv"

def run_hmm_pipeline():
    print(f"[{time.strftime('%H:%M:%S')}] Live data detected. Running HMM...")
    
    # 1. Load Base Data
    columns = ['Timestamp', 'Open', 'High', 'Low', 'Close', 'Volume']
    df_base = pd.read_csv(BASE_FILE, sep=';', header=None, names=columns)
    
    # 2. Load Live Data
    df_live = pd.read_csv(LIVE_FILE, sep=';', header=None, names=columns)
    
    # 3. Combine and Clean
    merged_df = pd.concat([df_base, df_live], ignore_index=True)
    merged_df['Datetime'] = pd.to_datetime(merged_df['Timestamp'], format='%Y%m%d %H%M%S')
    merged_df = merged_df.sort_values('Datetime').drop_duplicates(subset=['Timestamp'], keep='last')
    
    # >>>>> PASTE YOUR COLAB HMM MATH HERE <<<<<
    # Take the exact logic from your Colab script starting from the 5-min resample 
    # all the way through the hmm.GaussianHMM() model fitting and regime labeling.
    # Ensure your final output dataframe is saved exactly as it was in Colab:
    # final_df.to_csv(OUTPUT_FILE, index=False)
    # >>>>>>>>>>>>>>>>>>>>><<<<<<<<<<<<<<<<<<<<<
    
    print(f"[{time.strftime('%H:%M:%S')}] Regimes updated successfully.")

# --- WATCHDOG LOOP ---
last_mod_time = 0
print("Starting Local HMM Watchdog. Waiting for NinjaTrader data...")

while True:
    try:
        if os.path.exists(LIVE_FILE):
            # Check exactly when the file was last saved
            current_mod_time = os.path.getmtime(LIVE_FILE)
            
            # If NinjaTrader just saved it, run the math!
            if current_mod_time > last_mod_time:
                run_hmm_pipeline()
                last_mod_time = current_mod_time
                
        time.sleep(5) # Rest for 5 seconds to prevent CPU burn
        
    except Exception as e:
        print(f"Error reading file: {e}")
        time.sleep(5)