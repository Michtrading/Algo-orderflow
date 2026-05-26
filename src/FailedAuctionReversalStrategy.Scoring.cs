using System;
using System.IO;
using System.Text.Json;
using AlgoOrderflow.Models;

namespace AlgoOrderflow
{
    /// <summary>
    /// Couche Scoring — combinaison pondérée des features.
    /// Pas de cascade IF-AND-AND (doc § SCORING MODEL). Chaque feature contribue à un total.
    /// Trade ssi total >= ScoreThreshold ET direction non nulle.
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        // Compteurs diagnostic (visibles dans le dashboard)
        private int _diagBarsEvaluated;
        private int _diagDeltaNonZero;
        private int _diagWeakCloseNonZero;
        private int _diagVolSpikeNonZero;
        private int _diagProximityNonZero;
        private int _diagPassedThreshold;
        private decimal _diagMaxScore;

        private void ResetDiagnosticCounters()
        {
            _diagBarsEvaluated = 0;
            _diagDeltaNonZero = 0;
            _diagWeakCloseNonZero = 0;
            _diagVolSpikeNonZero = 0;
            _diagProximityNonZero = 0;
            _diagPassedThreshold = 0;
            _diagMaxScore = 0m;
        }

        /// <summary>
        /// Calcule le score sur la bougie indiquée et renvoie un ScoreSnapshot complet.
        /// Renvoie null si données indisponibles.
        /// </summary>
        private ScoreSnapshot ComputeScore(int evalBar, decimal ts)
        {
            ATAS.Indicators.IndicatorCandle c;
            try { c = GetCandle(evalBar); }
            catch { return null; }
            if (c == null) return null;

            var side = DetermineSide(c);
            if (side == SignalSide.None) return null;

            _diagBarsEvaluated++;

            decimal deltaIneff = ComputeDeltaInefficiencyFeature(c, ts);
            decimal weakClose = ComputeWeakCloseFeature(c, ts);
            decimal volSpike = ComputeVolumeSpikeFeature(evalBar);
            decimal proximity = ComputeProximityFeature(c, ts);

            // Gate de cohérence directionnelle (optionnelle).
            // SHORT candidate → on doit être PROCHE PAR LE HAUT d'un niveau (close au-dessus du level)
            // LONG candidate → on doit être PROCHE PAR LE BAS d'un niveau (close en-dessous du level)
            var (_, hasLevel, aboveLevel) = GetNearestContextLevel(c);
            if (!hasLevel)
            {
                proximity = 0m;
            }
            else if (RequireDirectionalProximity)
            {
                if (side == SignalSide.Short && !aboveLevel) proximity = 0m;
                else if (side == SignalSide.Long && aboveLevel) proximity = 0m;
            }

            // Gate dure optionnelle : pas de trade sans proximité du tout
            if (RequireProximityForTrade && proximity <= 0m)
                return null;

            decimal total = WeightDeltaInefficiency * deltaIneff
                          + WeightWeakClose * weakClose
                          + WeightVolumeSpike * volSpike
                          + WeightProximityContext * proximity;

            if (deltaIneff > 0m) _diagDeltaNonZero++;
            if (weakClose > 0m) _diagWeakCloseNonZero++;
            if (volSpike > 0m) _diagVolSpikeNonZero++;
            if (proximity > 0m) _diagProximityNonZero++;
            if (total > _diagMaxScore) _diagMaxScore = total;
            if (total >= ScoreThreshold) _diagPassedThreshold++;

            return new ScoreSnapshot
            {
                DeltaInefficiency = deltaIneff,
                WeakClose = weakClose,
                VolumeSpike = volSpike,
                ProximityContext = proximity,
                Total = total,
                Side = side
            };
        }

        /// <summary>
        /// Charge les poids depuis config/weights.json si présent à côté de la DLL.
        /// Permet de modifier les poids sans recompilation (recharge à chaque OnRecalculate).
        /// </summary>
        private void ReloadWeightsFromJson()
        {
            try
            {
                string path = LocateConfigFile("weights.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return;

                using var fs = File.OpenRead(path);
                using var doc = JsonDocument.Parse(fs);
                var root = doc.RootElement;

                if (root.TryGetProperty("threshold", out var thr) && thr.TryGetDecimal(out var thrVal))
                    _scoreThreshold = thrVal;

                if (root.TryGetProperty("weights", out var weights))
                {
                    if (weights.TryGetProperty("deltaInefficiency", out var w1) && w1.TryGetDecimal(out var v1))
                        _wDelta = v1;
                    if (weights.TryGetProperty("weakClose", out var w2) && w2.TryGetDecimal(out var v2))
                        _wWeakClose = v2;
                    if (weights.TryGetProperty("volumeSpike", out var w3) && w3.TryGetDecimal(out var v3))
                        _wVolume = v3;
                    if (weights.TryGetProperty("proximityContext", out var w4) && w4.TryGetDecimal(out var v4))
                        _wProximity = v4;
                }

                AddLog($"weights.json chargé (threshold={_scoreThreshold:F2})");
            }
            catch (Exception ex)
            {
                AddLog($"weights.json ignoré : {ex.Message}");
            }
        }

        private string LocateConfigFile(string fileName)
        {
            try
            {
                string asmDir = Path.GetDirectoryName(typeof(FailedAuctionReversalStrategy).Assembly.Location);
                if (string.IsNullOrEmpty(asmDir)) return null;

                string p1 = Path.Combine(asmDir, "config", fileName);
                if (File.Exists(p1)) return p1;

                string p2 = Path.Combine(asmDir, fileName);
                if (File.Exists(p2)) return p2;

                return null;
            }
            catch { return null; }
        }
    }
}
