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

## Journal de recherche (2026-05-28)

- Breakout sous-performe sur session replay (n faible, vetos dominants: volume/delta/acceptance/trend).
- Débug ajouté côté logs: `BREAKOUT VETO <reason>` pour attribution causale des non-entrées.
- Ajustement v1.2 des defaults breakout pour réduire la sur-filtration:
  - `BreakVolumeLookbackBars=3`
  - `BreakVolRatioMin=1.3`
  - `BreakDeltaMinAbs=35`
  - `BreakVelocityMaxRatio=0.90`
  - `BreakAcceptanceTopRatio=0.70`
  - `BreakConfirmTicks=0`
  - `BreakUseConcentrationFilter=false` (concentration top1/top2 disponible en option)
- Correction context replay: reconstruction de `RthOpen` et `PriorDayClose/High/Low` depuis l'historique chargé pour éviter les décalages si le replay démarre après 9:30 NY.
- Ajustement mean-revert: blocage des `SHORT` contrarians en `TrendUp` hors extrême `SD+2` (`AllowCounterTrendShortMeanRevert=false` par défaut).
- Logs de veto détaillés activés aussi côté absorption: `ABSORPTION VETO <reason>`.

## Protocole de test pour une nouvelle condition

1. **Hypothèse** : formuler en 1 phrase ce que la condition teste
2. **Isolation** : modifier une seule condition à la fois
3. **Sample** : minimum 30 trades déclenchés sur la période de test
4. **Split** : 70% in-sample (optimisation) / 30% out-of-sample (validation)
5. **Régimes** : valider séparément TREND et MEAN_REVERT
6. **Métriques** : win rate, avg R, expectancy, max drawdown journalier

## Preset par défaut : Research-Wide v2 (collecte multi-jours)

Objectif : générer assez de trades + journal shadow pour mesurer l'impact des vetos.

| Groupe | Paramètre | Défaut v2 |
|--------|-----------|-----------|
| Setup | `VolRatioMin` | 1.05 |
| Setup | `DeltaMinAbs` | 25 |
| Setup | `VpocExtremeRatio` | 0.50 |
| Setup | `ZoneProximityTicks` | 16 |
| Regime | `VwapSlopeTrendThreshold` | 0.15 |
| Breakout | `BreakVolRatioMin` | 1.0 (indépendant de VolRatioMin) |
| Breakout | `BreakDeltaMinAbs` | 18 |
| Breakout | `BreakAcceptanceTopRatio` | 0.55 |
| Recherche | `LogShadowVetos` | ON |
| Recherche | `ShadowMaxForwardBars` | 80 |

Fichier shadow : `%APPDATA%/ATAS/Strategies/AlgoOrderflow/journal_shadow_yyyy-MM-dd.csv`

Analyse :
`powershell -File tools/analyze-shadow.ps1 -Path "...journal_shadow_2026-05-29.csv"`

## Journal de recherche (2026-05-29)

- Session live 15h30 : 0 trade ; vetos dominants volume/régime (pas un problème de parsing).
- Vol ratio 1.5 peut passer le filtre volume et échouer sur delta/absorption/zone/trigger/acceptance.
- Ajout journal shadow + veto `absorption_pattern_veto` (zone OK, pattern incomplet).
- Breakout log : `long=...;short=...` pour diagnostic honnête.
