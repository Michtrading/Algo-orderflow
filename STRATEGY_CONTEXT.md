# Strategy Context — Analyse & Recherche

> Fichier de référence à `@` dans chaque conversation d'analyse ou de développement.

## Hypothèses de l'edge

| # | Hypothèse | Condition code |
|---|-----------|---------------|
| 1 | Delta divergent sur la bougie signal → épuisement des vendeurs/acheteurs agressifs | `DeltaMinAbs`, `Setup.cs` |
| 2 | VPOC en extrême + close du bon côté → absorption institutionnelle | `VpocExtremeRatio`, `Setup.cs` |
| 3 | Régime VWAP filtré → réduction du bruit directionnel | `VwapSlopeTrendThreshold`, `Regime.cs` |
| 4 | Volume surge → présence significative, signal non aléatoire | `VolRatioMin`, `Setup.cs` |

## Architecture des fichiers source

| Fichier | Rôle |
|---------|------|
| `FailedAuctionReversalStrategy.cs` | Boucle `OnCalculate`, routage v2 / legacy |
| `.Context.cs` | ONH/ONL, VWAP RTH, stdev, SD±1/±2, Open RTH, pente |
| `.Setup.cs` | Gate volume, divergence delta, VPOC |
| `.Regime.cs` | Veto auction, filtre Open, routage TREND / MEAN_REVERT |
| `.Trade.cs` / `.Backtest.cs` | Exécution MKT + bracket |
| `.Log.cs` | Journal CSV v2 (rotation auto si ancien header) |

## Colonnes journal CSV v2

`date, time, trade_mode, direction, entry_price, exit_price, pnl_ticks, regime, zone, delta, vpoc_extreme, vol_ratio, setup_score`

## Questions ouvertes / en cours de test

- [ ] Le filtre SD±2 améliore-t-il le Sharpe vs SD±1 seul ?
- [ ] La pente VWAP 20 barres est-elle le bon lookback (vs 10 ou 30) ?
- [ ] MEAN_REVERT performe-t-il mieux en range vs en tendance légère ?
- [ ] Le volume surge (ratio ≥ 1.5) filtre-t-il réellement les faux signaux ?

## Protocole de test pour une nouvelle condition

1. **Hypothèse** : formuler en 1 phrase ce que la condition teste
2. **Isolation** : modifier une seule condition à la fois
3. **Sample** : minimum 30 trades déclenchés sur la période de test
4. **Split** : 70% in-sample (optimisation) / 30% out-of-sample (validation)
5. **Régimes** : valider séparément TREND et MEAN_REVERT
6. **Métriques** : win rate, avg R, expectancy, max drawdown journalier

## Paramètres actuels (défauts v2)

| Groupe | Paramètre | Défaut |
|--------|-----------|--------|
| Setup | `VolLookbackBars` | 3 |
| Setup | `VolRatioMin` | 1.5 |
| Setup | `DeltaMinAbs` | 50 |
| Setup | `VpocExtremeRatio` | 0.25 |
| Setup | `ZoneProximityTicks` | 4 |
| Regime | `VwapSlopeLookback` | 20 |
| Regime | `VwapSlopeTrendThreshold` | 0.5 tick/bar |
| Regime | `AuctionOpenRangeTicks` | — |
| Regime | `SdMultiplier1/2` | 1 / 2 |
