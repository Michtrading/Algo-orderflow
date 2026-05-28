---
name: session-review-quant
description: Analyse une session de trading Algo Orderflow de facon standardisee avec KPIs, segments, vetos, biais statistiques et recommandations actionnables. Utiliser quand l'utilisateur demande une analyse de journal/log ATAS, une revue de session, ou des pistes d'optimisation setup, filtres, stop loss et take profit.
disable-model-invocation: true
---

# Session Review Quant

## Objectif

Produire une revue quantitative complete, comparable d'une session a l'autre, orientee decisions de recherche et robustesse (pas curve fitting).

Contexte cible:
- ES futures, range 8 ticks, ATAS, Algo Orderflow
- Journal CSV: `%APPDATA%/ATAS/Strategies/AlgoOrderflow/journal_yyyy-MM-dd.csv`
- Logs texte: `%APPDATA%/ATAS/Strategies/AlgoOrderflow/log_yyyy-MM-dd.txt`

## Entrees minimales

- `journal_path` (obligatoire)
- `log_path` (recommande)
- `live_date` (optionnel, pour separer BT vs LIVE)
- `focus_window` (optionnel, ex: `16:00-17:00`)
- `version_tag` (optionnel, ex: `breakout_v2_relaxed`)

Si une entree critique manque, demander explicitement avant de conclure.

## Workflow standard (a suivre dans l'ordre)

1. **Verifier la qualite des donnees**
   - Colonnes presentes, lignes corrompues, valeurs manquantes.
   - Detecter incoherences `pnl_ticks` vs `entry/sl/tp/exit`.
   - Signaler toute limite qui peut biaiser l'analyse.

2. **Calculer les KPIs globaux**
   - `N trades`, `WR`, `PnL total ticks`, `avg win`, `avg loss`, `R ratio`, `expectancy`, `profit factor`.
   - Comparer aux seuils cibles:
     - WR > 45%
     - R moyen > 0.8
     - Expectancy > 0.2R
     - Echantillon >= 30 trades par condition analysee

3. **Segmenter la performance**
   - BT vs LIVE (si possible)
   - Par `trade_mode` (MeanRevert, Trend, Breakout)
   - Par `vwap_regime`
   - Par `nearest_level` / contexte (VWAP, SD, Open, D-1, OPR, etc.)
   - Par trigger breakout (`break_trigger`) si disponible

4. **Diagnostiquer les causes**
   - Top vetos logs (frequence + impact probable).
   - Poches de pertes: combinaisons regime x mode x niveau.
   - Verifier risque de look-ahead, overfitting, ou filtre trop strict.

5. **Produire decisions actionnables**
   - 1 a 3 changements max par iteration.
   - Pour chaque changement: hypothese, impact attendu, risque, test minimal.

## Questions parfaites a traiter a chaque revue

1. Qu'est-ce qui gagne/perd structurellement (pas juste sur une session)?
2. Quels segments ont un echantillon trop faible pour conclure?
3. Quels vetos bloquent des setups potentiellement valides?
4. Quels trades contre-regime sont destructeurs et doivent etre veto?
5. Breakout: quel trigger fonctionne le mieux (swing vs volume node)?
6. Le filtre directionnel (VWAP/Open) aide-t-il reellement ou coupe-t-il les bons trades?
7. Les niveaux contextuels utilises sont-ils informatifs ou redondants?
8. Le ratio rendement/risque est-il suffisant par mode?
9. Faut-il modifier le stop ou le TP pour optimiser, ou passer en TP/SL dynamique?
10. Les conclusions sont-elles robustes out-of-sample et compatibles prop firm?

## Bloc obligatoire: decision SL/TP

Toujours inclure ce mini-diagnostic:

- `Etat actuel`: SL/TP fixes par mode (ex: breakout 9/8), performance observee.
- `Test A (fixe ajuste)`: proposition de 1-2 variantes simples (ex: 10/8, 9/10).
- `Test B (dynamique)`: regle simple basee contexte (ex: TP partiel + trailing, ou TP sur VWAP/SD).
- `Critere de validation`: expectancy, max drawdown session, regularite des resultats, respect des contraintes prop firm.
- `Decision`: garder fixe / passer dynamique / reporter faute d'echantillon.

Ne jamais recommander un schema dynamique complexe sans evidence statistique suffisante.

## Format de sortie impose

Repondre avec cette structure:

### 1) Synthese executive
- 3 a 5 points cles (ce qui marche, ce qui casse, niveau de confiance).

### 2) Tableau KPI
- Global puis BT/LIVE si disponible.

### 3) Segments prioritaires
- Top 3 gagnants
- Top 3 perdants
- Conditions a couper immediatement (si evidence suffisante)

### 4) Vetos et anomalies
- Top raisons de veto
- Incoherences de log/journal qui limitent la conclusion

### 5) Decision SL/TP (obligatoire)
- Comparatif fixe vs dynamique
- Recommandation testable sur prochaine iteration

### 6) Plan d'experimentation court
- Maximum 3 changements
- Pour chaque changement: hypothese, metrique de succes, seuil d'arret

## Garde-fous quant

- Eviter de modifier plusieurs blocs logiques en meme temps sans test isole.
- Si `N < 30` sur un segment, classer "exploratoire".
- Toujours expliciter les biais possibles: overfitting, biais de selection, donnees live incomplètes.
- Respecter contraintes prop firm (drawdown journalier, regularite des gains).

## References code a verifier pendant l'analyse

- `src/FailedAuctionReversalStrategy.Setup.cs`
- `src/FailedAuctionReversalStrategy.Regime.cs`
- `src/FailedAuctionReversalStrategy.Breakout.cs`
- `src/FailedAuctionReversalStrategy.Trade.cs`
- `src/FailedAuctionReversalStrategy.Context.cs`
- `tools/analyze-journal.ps1`
