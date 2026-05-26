using System;
using System.Collections.Generic;
using ATAS.Indicators;
using AlgoOrderflow.Models;

namespace AlgoOrderflow
{
    /// <summary>
    /// Couche Context — niveaux contextuels (ONH/ONL/RTH VWAP) + régime de volatilité (vitesse de formation des bougies).
    /// Aucune décision de trade ici : la couche fournit uniquement des inputs au scoring.
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        private readonly ContextSnapshot _ctx = new ContextSnapshot();
        private readonly List<ContextLevelSegment> _onhSegments = new List<ContextLevelSegment>();
        private readonly List<ContextLevelSegment> _onlSegments = new List<ContextLevelSegment>();
        private readonly List<VwapPoint> _vwapTrail = new List<VwapPoint>();

        // Overnight window cumulators (NY-local logic)
        private DateTime _onWindowDate = DateTime.MinValue;
        private decimal _onHighBuilder = decimal.MinValue;
        private decimal _onLowBuilder = decimal.MaxValue;
        private bool _onHasBuilder;

        // RTH VWAP cumulators
        private DateTime _rthDate = DateTime.MinValue;
        private decimal _rthSumPV;
        private decimal _rthSumV;
        private bool _rthVwapStarted;

        public ContextSnapshot Context => _ctx;
        public IReadOnlyList<ContextLevelSegment> OnhSegments => _onhSegments;
        public IReadOnlyList<ContextLevelSegment> OnlSegments => _onlSegments;
        public IReadOnlyList<VwapPoint> VwapTrail => _vwapTrail;

        private void ResetContextState()
        {
            _ctx.OvernightHigh = 0m;
            _ctx.OvernightLow = 0m;
            _ctx.RthVwap = 0m;
            _ctx.HasOvernight = false;
            _ctx.HasRthVwap = false;
            _ctx.BarFormationSecondsAvg = 0m;

            _onhSegments.Clear();
            _onlSegments.Clear();
            _vwapTrail.Clear();

            _onWindowDate = DateTime.MinValue;
            _onHighBuilder = decimal.MinValue;
            _onLowBuilder = decimal.MaxValue;
            _onHasBuilder = false;

            _rthDate = DateTime.MinValue;
            _rthSumPV = 0m;
            _rthSumV = 0m;
            _rthVwapStarted = false;

            _prevBarNyMins = -1;
            _prevBarNyDate = DateTime.MinValue;
        }

        /// <summary>Maj des compteurs ONH/ONL/VWAP en streaming à chaque bougie clôturée.</summary>
        private void UpdateContext(int bar)
        {
            if (bar < 1) return;

            IndicatorCandle c;
            try { c = GetCandle(bar - 1); }
            catch { return; }
            if (c == null) return;

            var ny = ToNy(c.Time);
            int mins = ny.Hour * 60 + ny.Minute;
            int rthStartMins = 9 * 60 + 30;
            int rthEndMins = 16 * 60;

            UpdateOvernightWindow(bar - 1, c, ny, mins, rthStartMins);
            UpdateRthVwap(bar - 1, c, ny, mins, rthStartMins, rthEndMins);
            UpdateBarFormationSpeed(bar);
        }

        private static void CloseOpenSegments(List<ContextLevelSegment> segments, int toBar)
        {
            for (int i = segments.Count - 1; i >= 0; i--)
            {
                if (segments[i].ToBar < 0)
                    segments[i].ToBar = toBar;
            }
        }

        private static void AddLevelSegment(List<ContextLevelSegment> segments, ContextLevelKind kind,
            decimal price, int fromBar)
        {
            segments.Add(new ContextLevelSegment
            {
                Kind = kind,
                Price = price,
                FromBar = fromBar,
                ToBar = -1
            });
        }

        // État de transition pour détecter le franchissement de 09:30 NY entre 2 bougies consécutives
        private int _prevBarNyMins = -1;
        private DateTime _prevBarNyDate = DateTime.MinValue;

        private void UpdateOvernightWindow(int bar, IndicatorCandle c, DateTime ny, int mins, int rthStartMins)
        {
            // Overnight = de 18:00 NY veille à 09:30 NY (avant ouverture RTH).
            // On accumule High/Low en continu, et on fige les builders ONH/ONL au passage à 09:30 NY.

            int onStartMins = 18 * 60;
            DateTime nyDate = ny.Date;

            bool inEvening = mins >= onStartMins;
            bool inPreRth = mins < rthStartMins;
            bool inRth = mins >= rthStartMins && mins < (16 * 60);

            // Détection du franchissement de 09:30 NY (entre la bougie précédente et celle-ci)
            bool crossedRthOpen = _prevBarNyMins >= 0
                && _prevBarNyMins < rthStartMins
                && mins >= rthStartMins
                && _onHasBuilder;

            if (crossedRthOpen)
            {
                _ctx.OvernightHigh = _onHighBuilder;
                _ctx.OvernightLow = _onLowBuilder;
                _ctx.HasOvernight = true;

                CloseOpenSegments(_onhSegments, bar);
                CloseOpenSegments(_onlSegments, bar);
                AddLevelSegment(_onhSegments, ContextLevelKind.OvernightHigh, _onHighBuilder, bar);
                AddLevelSegment(_onlSegments, ContextLevelKind.OvernightLow, _onLowBuilder, bar);

                AddLog($"ONH/ONL figés : H={_onHighBuilder:F2} L={_onLowBuilder:F2} ({nyDate:yyyy-MM-dd})");
                RedrawChart();
            }

            // Pendant la session RTH : on continue d'agréger l'overnight pour la SESSION D'APRÈS.
            // Nouvelle fenêtre overnight démarre dès le soir.
            if (inEvening)
            {
                if (!_onHasBuilder || _onWindowDate != nyDate)
                {
                    _onHighBuilder = c.High;
                    _onLowBuilder = c.Low;
                    _onWindowDate = nyDate;
                    _onHasBuilder = true;
                }
                else
                {
                    if (c.High > _onHighBuilder) _onHighBuilder = c.High;
                    if (c.Low < _onLowBuilder) _onLowBuilder = c.Low;
                }
            }
            else if (inPreRth)
            {
                if (!_onHasBuilder)
                {
                    // Cas : replay démarre dans la nuit sans avoir vu le soir 18:00.
                    // On commence quand même à construire pour ne pas rater l'ouverture.
                    _onHighBuilder = c.High;
                    _onLowBuilder = c.Low;
                    _onWindowDate = nyDate.AddDays(-1);
                    _onHasBuilder = true;
                }
                else
                {
                    if (c.High > _onHighBuilder) _onHighBuilder = c.High;
                    if (c.Low < _onLowBuilder) _onLowBuilder = c.Low;
                }
            }
            else if (inRth && crossedRthOpen)
            {
                // Reset du builder pour la session overnight suivante
                _onHighBuilder = decimal.MinValue;
                _onLowBuilder = decimal.MaxValue;
                _onHasBuilder = false;
            }

            _prevBarNyMins = mins;
            _prevBarNyDate = nyDate;
        }

        private void UpdateRthVwap(int bar, IndicatorCandle c, DateTime ny, int mins, int rthStartMins, int rthEndMins)
        {
            DateTime nyDate = ny.Date;
            bool inRth = mins >= rthStartMins && mins < rthEndMins;

            if (!inRth)
            {
                if (_rthVwapStarted && _rthDate != nyDate)
                {
                    _rthSumPV = 0m;
                    _rthSumV = 0m;
                    _rthVwapStarted = false;
                    _ctx.HasRthVwap = false;
                }
                return;
            }

            if (!_rthVwapStarted || _rthDate != nyDate)
            {
                _rthSumPV = 0m;
                _rthSumV = 0m;
                _rthVwapStarted = true;
                _rthDate = nyDate;
                _ctx.HasRthVwap = false;
            }

            decimal typical = (c.High + c.Low + c.Close) / 3m;
            decimal v = (decimal)c.Volume;
            _rthSumPV += typical * v;
            _rthSumV += v;

            if (_rthSumV > 0m)
            {
                _ctx.RthVwap = _rthSumPV / _rthSumV;
                _ctx.HasRthVwap = true;

                if (_vwapTrail.Count == 0 || _vwapTrail[_vwapTrail.Count - 1].Bar != bar)
                    _vwapTrail.Add(new VwapPoint { Bar = bar, Vwap = _ctx.RthVwap });
                else
                    _vwapTrail[_vwapTrail.Count - 1] = new VwapPoint { Bar = bar, Vwap = _ctx.RthVwap };
            }
        }

        private void UpdateBarFormationSpeed(int bar)
        {
            int n = Math.Min(LookbackBars, bar);
            if (n < 2) return;

            try
            {
                var first = GetCandle(bar - n);
                var last = GetCandle(bar - 1);
                if (first == null || last == null) return;

                var dt = last.Time - first.Time;
                if (dt.TotalSeconds <= 0) return;

                _ctx.BarFormationSecondsAvg = (decimal)(dt.TotalSeconds / n);
            }
            catch { }
        }

        /// <summary>Proximité aux niveaux contextuels (en ticks). 0 = pas proche.</summary>
        private decimal ComputeProximityFeature(IndicatorCandle c, decimal ts)
        {
            if (ts <= 0m) return 0m;

            decimal best = decimal.MaxValue;
            void Try(decimal level, bool has)
            {
                if (!has) return;
                decimal dTicks = Math.Abs(c.Close - level) / ts;
                if (dTicks < best) best = dTicks;
            }

            Try(_ctx.OvernightHigh, _ctx.HasOvernight);
            Try(_ctx.OvernightLow, _ctx.HasOvernight);
            Try(_ctx.RthVwap, _ctx.HasRthVwap);

            if (best == decimal.MaxValue) return 0m;
            if (best > ProximityTicks) return 0m;

            // Proximité linéaire normalisée [0..1] : 1 = collé au niveau.
            return 1m - (best / ProximityTicks);
        }

        /// <summary>Quel niveau est le plus proche, et de quel côté ? Détermine la direction du reversal candidate.</summary>
        private (decimal nearestLevel, bool hasLevel, bool aboveLevel) GetNearestContextLevel(IndicatorCandle c)
        {
            decimal nearest = 0m;
            bool has = false;
            decimal bestDist = decimal.MaxValue;

            void Try(decimal level, bool levelHas)
            {
                if (!levelHas) return;
                decimal d = Math.Abs(c.Close - level);
                if (d < bestDist)
                {
                    bestDist = d;
                    nearest = level;
                    has = true;
                }
            }

            Try(_ctx.OvernightHigh, _ctx.HasOvernight);
            Try(_ctx.OvernightLow, _ctx.HasOvernight);
            Try(_ctx.RthVwap, _ctx.HasRthVwap);

            return (nearest, has, has && c.Close > nearest);
        }
    }
}
