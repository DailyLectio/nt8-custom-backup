# HMM Regime Matrix Repo Handoff

## Goal

Create a new, separate GitHub repository for the local Python/HMM quant trading
model files that work alongside the NT8 V3C/V3D Regime Matrix indicators and
strategies.

Recommended repo name:

`hmm-regime-matrix-models`

Recommended visibility:

Private

## Keep Separate From NT8 Backup

The current NT8 repo is:

`https://github.com/DailyLectio/nt8-custom-backup.git`

That repo should continue to protect NT8 NinjaScript and template files. The HMM
repo should be separate because Python model code, model artifacts, notebooks,
data exports, environments, and logs follow a different backup pattern.

## NT8 Files Already Covered

The NT8 backup already includes:

- `C:\Users\Valued Customer\Documents\NinjaTrader 8\bin\Custom\Indicators`
- `C:\Users\Valued Customer\Documents\NinjaTrader 8\bin\Custom\Strategies`

Important V3C/V3D bridge files currently covered by the NT8 repo include:

- `bin/Custom/Indicators/RegimeMatrixHUDV3C.cs`
- `bin/Custom/Indicators/RegimeMatrixHUDV3D.cs`
- `bin/Custom/Indicators/TradeLogExporterV3D.cs`
- `bin/Custom/Indicators/TradeLogExporter_V3D.cs`
- V3C/V3D strategy files under `bin/Custom/Strategies`
- V3C/V3D strategy templates under `templates/Strategy`

## Paths To Provide In The Next Chat

Share the full local folder paths for the HMM/Python model system, for example:

```text
C:\path\to\hmm-model-folder
C:\path\to\regime-matrix-python-folder
C:\path\to\model-configs
C:\path\to\model-outputs-to-keep
```

Also identify which folders contain:

- Python source code
- model configuration files
- model weights or serialized model artifacts
- notebooks or research files
- generated logs
- generated data exports
- credentials, API keys, tokens, or account-specific settings

## Suggested Include Rules

Usually safe to include:

- `*.py`
- `*.ipynb` if notebooks are important and not huge
- `*.toml`
- `*.yaml`
- `*.yml`
- `*.json` if they do not contain secrets
- `*.md`
- `requirements.txt`
- `pyproject.toml`
- model metadata/config folders
- small serialized model files if they are essential and reasonably sized

Usually exclude:

- `.venv/`, `venv/`, `env/`
- `__pycache__/`
- `.pytest_cache/`
- logs
- temporary files
- raw market data dumps
- large backtest outputs
- credentials, secrets, tokens, broker keys, and account files
- large model artifacts unless Git LFS is intentionally enabled

## New Chat Prompt

Use this prompt in a new chat:

```text
I want to create a new private GitHub repo for my local HMM Regime Matrix Python
model files that work with my NT8 V3C/V3D setup. Keep it separate from
DailyLectio/nt8-custom-backup. Please inspect these paths, propose a safe
.gitignore, identify any secrets or huge generated files that should not be
pushed, initialize a new repo, create the first commit, connect it to a new
GitHub repo named hmm-regime-matrix-models, and set up an auto-backup schedule.

Paths:
[paste full local paths here]
```

## First Safety Checks For The Next Chat

Before pushing the HMM repo, inspect for:

- `.env`
- API keys
- broker/account identifiers
- passwords or tokens
- oversized CSV/parquet/log/model files
- files that are generated every run and would create noisy commits

The next repo should use its own folder, its own `.gitignore`, its own GitHub
remote, and its own scheduled backup task name.
