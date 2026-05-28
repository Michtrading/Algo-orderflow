# Algo Orderflow — ES futures (ATAS, range 8 ticks)

Stratégie ATAS C# (.NET 10 Windows) — moteur **v2** : divergence delta **par bougie** + absorption VPOC + régime VWAP / Open RTH / bandes SD.

> **Objectif** : robustesse statistique, faible variance, compatibilité prop firm.

## Edge v2 (défaut)

1. **Volume surge** : volume bougie signal ≥ ratio × moyenne des N bougies précédentes (défaut N=3, ratio≥1.5).
2. **Divergence bougie** (mean revert) : verte + delta vendeur ≤ −min, ou rouge + delta acheteur ≥ +min (défaut 50).
3. **Absorption VPOC** : POC concentré en haut/bas + close du bon côté du POC (`MaxVolumePriceInfo` ATAS).
4. **Régime** : pente VWAP (20 barres, ±0.5 tick/bar) ; filtre Open RTH 9:30.
5. **Zones** : VWAP + SD±1/±2 uniquement (proximité close **ou** mèche low/high).
6. **TREND** : delta aligné **ou** divergence pullback absorbée au support/résistance dans le sens de la pente.
7. **MEAN_REVERT** : divergence + absorption (SD±2 prioritaire, puis SD±1/VWAP).

Le moteur **legacy** (ancien scoring failed auction mono-bougie) reste disponible via `Moteur legacy = ON`.

## Architecture

| Fichier | Rôle |
|---|---|
| `FailedAuctionReversalStrategy.cs` | boucle `OnCalculate`, entrée v2 / legacy |
| `.Context.cs` | ONH/ONL, VWAP RTH, stdev, SD±1/±2, Open RTH, pente |
| `.Setup.cs` | gate volume, divergence, VPOC |
| `.Regime.cs` | veto auction, filtre Open, routage TREND / MEAN_REVERT |
| `.Detection.cs` / `.Scoring.cs` | legacy uniquement |
| `.Trade.cs` / `.Backtest.cs` | exécution MKT + bracket |
| `.Log.cs` | journal CSV v2 (rotation auto si ancien header) |

## Paramètres principaux (ATAS)

| Groupe | Paramètres |
|--------|------------|
| **Moteur** | `UseLegacyEngine` (défaut OFF) |
| **Setup** | `VolLookbackBars`, `VolRatioMin`, `DeltaMinAbs`, `VpocExtremeRatio`, `ZoneProximityTicks` |
| **Regime** | `VwapSlopeLookback`, `VwapSlopeTrendThreshold`, `AuctionOpenRangeTicks`, `SdMultiplier1/2` |

## Build

```powershell
dotnet build -c Release
```

DLL copiée vers `%APPDATA%\ATAS\Strategies\AlgoOrderflow.dll`.

## Journal CSV v2

`%APPDATA%\ATAS\Strategies\AlgoOrderflow\journal_yyyy-MM-dd.csv`

Si un ancien fichier sans colonnes `trade_mode` existe, il est renommé en `journal_yyyy-MM-dd_pre_v2.csv`.

Analyse PowerShell :

```powershell
.\tools\analyze-journal.ps1 -Path "$env:APPDATA\ATAS\Strategies\AlgoOrderflow\journal_2026-05-26.csv"
# LIVE d'un jour précis :
.\tools\analyze-journal.ps1 -Path "..." -LiveDate "2026-05-26"
```

Le script affiche un bloc **BT vs LIVE** (expectancy, exp_R, R_ratio, profit factor), les régimes/modes en backtest, les segments faibles, et le détail live si ≤ 30 trades.

## Backtest

- Activer **Mode backtest historique**.
- Pas de popups en backtest (logs fichier + dashboard).
- Recharger la stratégie après chaque build.
