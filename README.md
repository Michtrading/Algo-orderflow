# Algo Orderflow — Failed Auction Reversal (ATAS, ES futures)

Stratégie ATAS C# (.NET 10 Windows) qui implémente le « Failed Auction / Trapped Trader Reversal » défini dans `master_research_document_ES_microstructure`.

> **Objectif** : robustesse statistique, faible variance, compatibilité prop firm.
> **Non-objectif** : maximisation du profit historique.

## Cadre de travail (non négociable)

- Marché : **ES futures**, RTH **09:30–11:00 NY** (extension 11:30 si stats le justifient)
- Graphique : **range bars 8 ticks** (à configurer manuellement dans ATAS)
- Edge : agression inefficace (delta/volume sans progression) près d'un niveau contextuel (ONH/ONL/RTH VWAP)
- Exécution : MKT à la clôture, stop ~9 ticks, target fixe (RR 1:1 à 1:1.5)
- Taille fixe, ±1000 $ / jour, 5–6 pertes consécutives = halt, pas de revenge
- Scoring probabiliste pondéré (pas IF-AND-AND)
- Slippage 1–2 ticks simulé obligatoirement en backtest

## Architecture

Une seule `partial class FailedAuctionReversalStrategy : ChartStrategy`, éclatée par fichier :

| Fichier | Rôle |
|---|---|
| `FailedAuctionReversalStrategy.cs` | ctor, état général, boucle `OnCalculate`, `OnRecalculate`, hooks broker |
| `.Parameters.cs` | tous les paramètres UI ATAS (`[Display]` + `[Parameter]`) |
| `.TradingHours.cs` | fenêtre 09:30–11:00 NY, `IsNewTradingSession` |
| `.Context.cs` | ONH/ONL (fenêtre 18:00 NY J-1 → 09:30 NY J), RTH VWAP, vitesse de formation des bougies |
| `.Detection.cs` | features microstructure : delta inefficient, weak close, volume spike |
| `.Scoring.cs` | combinaison pondérée + seuil, recharge `config/weights.json` |
| `.Trade.cs` | exécution live MKT close + bracket SL/TP attaché à la position |
| `.Backtest.cs` | toggle `BacktestMode` + simulation interne fills/hits, slippage |
| `.Risk.cs` | PnL journalier (baseline `ClosedPnL` + delta) |
| `.Safety.cs` | kill switch pertes consécutives + flatten d'urgence |
| `.Render.cs` | dashboard + flèches d'entrée (lignes SL/TP via `HorizontalLinesTillTouch`) |
| `.Log.cs` | log circulaire + journal CSV append-only par jour |

## Build

Prérequis : ATAS installé dans `C:\Program Files (x86)\ATAS Platform`, .NET 10 SDK.

```powershell
dotnet build -c Release
```

La DLL est compilée puis **copiée automatiquement** vers `%APPDATA%\ATAS\Strategies\AlgoOrderflow.dll` (cible MSBuild `CopyToAtasStrategies`).

## Chargement dans ATAS

1. Ouvrir ATAS, charger un graphique **ES**.
2. Configurer le type de graphique en **range bar 8 ticks** (Chart settings).
3. Clic droit sur le chart → ouvrir la liste des stratégies → ajouter `FailedAuctionReversal_ES`.
4. Régler les paramètres (groupes : Detection / Scoring / Bracket / Risque / Backtest / Interface / Horaires).
5. Cliquer sur Start.

## Backtest

- Activer **Backtest → Mode backtest historique** dans les paramètres.
- La stratégie simule en interne (pas de connexion broker) sur tout l'historique chargé du chart.
- Slippage par défaut = 1 tick (paramètre `SlippageTicksBacktest`, valeurs 1 ou 2 recommandées).
- Chaque trade affiche **deux rectangles** (vert = zone TP, rouge = zone SL) de la bougie d'entrée jusqu'à la bougie de sortie (toggle `ShowTradeRectangles`).
- **Niveaux contexte** affichés sur le chart (toggle `ShowContextLevels`) : ONH cyan pointillé, ONL orange pointillé, VWAP RTH jaune (courbe qui évolue en session). Historique conservé en backtest multi-jours.
- Journal CSV : `%APPDATA%\ATAS\Strategies\AlgoOrderflow\journal_yyyy-MM-dd.csv`.

## Anti-features (interdits par le doc, à respecter)

- Pas d'ATR sur range bars
- Pas d'EMA crossover, RSI, MACD, indicateur empilé
- Pas d'ordre limite à la place du MKT close
- Pas de trailing stop, scaling, martingale, sizing progressif
- Pas de logique de revenge
- Pas de dépendance fine au footprint en phase 1 (delta + volume suffisent)

## Phases de livraison

- **Phase 1 (1 mois exploratoire)** : code livré ici. Lancer en `BacktestMode` sur 1 mois ES, observer la distribution des trades, MAE/MFE, expectancy.
- **Phase 2 (6–12 mois robustesse)** : enrichir le scoring si besoin, n'ajouter des features qu'après validation statistique. Walk-forward.
- **Phase 3** : optimisation des poids hors-période, validation in-sample/out-of-sample.

## Hors périmètre

NQ/GC, footprint avancé (stacked imbalance, unfinished auction), tailles dynamiques, seuils adaptatifs, classification de régime, VWAP mean reversion, breakout initiative, cross-market hedging, DOM metrics → **interdits avant validation de l'edge** (consigne du doc).
