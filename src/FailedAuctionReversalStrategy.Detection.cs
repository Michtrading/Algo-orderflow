using System;
using ATAS.Indicators;
using AlgoOrderflow.Models;

namespace AlgoOrderflow
{
    /// <summary>
    /// Couche Detection — calcule les features de microstructure sur la bougie qui vient de clore.
    /// AUCUN seuil empilé : chaque feature est normalisée [0..1] (ou contrib brute) et passée au Scoring.
    /// Voir doc § DEFINITION OF INEFFICIENT AGGRESSION.
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        /// <summary>
        /// Inefficacité du delta : delta absolu rapporté à la progression du prix.
        /// Une valeur élevée = beaucoup de delta sans bougie qui suit = agression coincée.
        /// Output normalisé [0..1].
        /// </summary>
        private decimal ComputeDeltaInefficiencyFeature(IndicatorCandle c, decimal ts)
        {
            if (ts <= 0m) return 0m;

            decimal absDelta = Math.Abs(c.Delta);
            if (absDelta < DeltaIneffMinAbs) return 0m;

            decimal moveTicks = Math.Abs(c.Close - c.Open) / ts;
            if (moveTicks <= 0m) moveTicks = 0.5m;

            decimal efficiency = moveTicks / absDelta;
            if (efficiency > EfficiencyThreshold) return 0m;

            decimal score = 1m - (efficiency / EfficiencyThreshold);
            return Math.Clamp(score, 0m, 1m);
        }

        /// <summary>
        /// Weak close — clôture loin du sens dominant de la bougie (rejet de l'extrême).
        /// Pour une bougie haussière (close > open) : weak si close proche du Low (rejet par le haut, on a poussé puis on s'est replié).
        /// Pour une bougie baissière : weak si close proche du High.
        /// Output [0..1] (1 = clôture parfaitement à l'extrême opposé).
        /// </summary>
        private decimal ComputeWeakCloseFeature(IndicatorCandle c, decimal ts)
        {
            decimal range = c.High - c.Low;
            if (range <= 0m) return 0m;

            bool bullishBar = c.Close >= c.Open;
            decimal distFromOppExtreme = bullishBar
                ? c.Close - c.Low
                : c.High - c.Close;

            decimal ratio = distFromOppExtreme / range;
            if (ratio > WeakCloseRatio) return 0m;

            return Math.Clamp(1m - (ratio / WeakCloseRatio), 0m, 1m);
        }

        /// <summary>
        /// Volume spike — volume de la bougie / SMA(volume, lookback).
        /// Output : 0 si pas spike, [0..1] si dépasse le seuil (1 = volume = mult × moyenne).
        /// </summary>
        private decimal ComputeVolumeSpikeFeature(int evalBar)
        {
            if (evalBar < LookbackBars + 1) return 0m;

            decimal sumVol = 0m;
            int n = 0;
            for (int i = evalBar - LookbackBars; i < evalBar; i++)
            {
                try
                {
                    var ci = GetCandle(i);
                    if (ci == null) continue;
                    sumVol += (decimal)ci.Volume;
                    n++;
                }
                catch { }
            }
            if (n == 0) return 0m;

            decimal avgVol = sumVol / n;
            if (avgVol <= 0m) return 0m;

            decimal curVol;
            try { curVol = (decimal)GetCandle(evalBar).Volume; }
            catch { return 0m; }

            decimal mult = curVol / avgVol;
            if (mult < VolumeSpikeMult) return 0m;

            // Saturation à 2x le seuil → renvoie 1.0
            decimal headroom = VolumeSpikeMult;
            decimal score = (mult - VolumeSpikeMult) / headroom;
            return Math.Clamp(score, 0m, 1m);
        }

        /// <summary>
        /// Détermine la direction probable du reversal en fonction du sens de la bougie d'agression
        /// ET de la position par rapport au niveau contextuel le plus proche.
        ///
        /// Logique : Failed Auction Reversal.
        /// - Bougie haussière agressive (Delta>0) qui clôture faible et est PROCHE de ONH/VWAP → SHORT (les acheteurs trapped au-dessus)
        /// - Bougie baissière agressive (Delta<0) qui clôture faible et est PROCHE de ONL/VWAP → LONG (les vendeurs trapped en dessous)
        /// </summary>
        private SignalSide DetermineSide(IndicatorCandle c)
        {
            bool aggressiveBull = c.Delta > 0m;
            bool aggressiveBear = c.Delta < 0m;
            if (!aggressiveBull && !aggressiveBear) return SignalSide.None;

            return aggressiveBull ? SignalSide.Short : SignalSide.Long;
        }
    }
}
