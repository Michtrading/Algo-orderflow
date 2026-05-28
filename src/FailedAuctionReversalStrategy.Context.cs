using System;
using System.Collections.Generic;
using ATAS.Indicators;
using AlgoOrderflow.Models;

namespace AlgoOrderflow
{
    /// <summary>
    /// Couche Context — ONH/ONL, RTH VWAP + stdev (bandes SD), Open RTH 9:30, pente VWAP.
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        private readonly ContextSnapshot _ctx = new ContextSnapshot();
        private readonly List<ContextLevelSegment> _onhSegments = new List<ContextLevelSegment>();
        private readonly List<ContextLevelSegment> _onlSegments = new List<ContextLevelSegment>();
        private readonly List<VwapPoint> _vwapTrail = new List<VwapPoint>();

        private DateTime _onWindowDate = DateTime.MinValue;
        private decimal _onHighBuilder = decimal.MinValue;
        private decimal _onLowBuilder = decimal.MaxValue;
        private bool _onHasBuilder;

        private DateTime _rthDate = DateTime.MinValue;
        private decimal _rthSumPV;
        private decimal _rthSumV;
        private decimal _rthSumVar;
        private bool _rthVwapStarted;

        private int _prevBarNyMins = -1;
        private DateTime _prevBarNyDate = DateTime.MinValue;

        private decimal _sessionHigh;
        private decimal _sessionLow;
        private decimal _sessionClose;
        private bool _sessionHasRange;

        private decimal _oprHighBuilder = decimal.MinValue;
        private decimal _oprLowBuilder = decimal.MaxValue;
        private bool _oprFrozen;
        private DateTime _oprDate = DateTime.MinValue;

        public ContextSnapshot Context => _ctx;
        public IReadOnlyList<ContextLevelSegment> OnhSegments => _onhSegments;
        public IReadOnlyList<ContextLevelSegment> OnlSegments => _onlSegments;
        public IReadOnlyList<VwapPoint> VwapTrail => _vwapTrail;

        private void ResetContextState()
        {
            _ctx.OvernightHigh = 0m;
            _ctx.OvernightLow = 0m;
            _ctx.RthVwap = 0m;
            _ctx.RthStdev = 0m;
            _ctx.RthOpen = 0m;
            _ctx.SdPlus1 = _ctx.SdMinus1 = _ctx.SdPlus2 = _ctx.SdMinus2 = 0m;
            _ctx.VwapSlopeTicksPerBar = 0m;
            _ctx.VwapRegime = VwapRegime.Unknown;
            _ctx.HasOvernight = false;
            _ctx.HasRthVwap = false;
            _ctx.HasRthOpen = false;
            _ctx.HasSdBands = false;
            _ctx.BarFormationSecondsAvg = 0m;
            _ctx.OprHigh = _ctx.OprLow = 0m;
            _ctx.HasOpr = false;
            _ctx.PriorDayHigh = _ctx.PriorDayLow = _ctx.PriorDayClose = 0m;
            _ctx.HasPriorDay = false;

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
            _rthSumVar = 0m;
            _rthVwapStarted = false;

            _prevBarNyMins = -1;
            _prevBarNyDate = DateTime.MinValue;

            _sessionHigh = decimal.MinValue;
            _sessionLow = decimal.MaxValue;
            _sessionClose = 0m;
            _sessionHasRange = false;

            _oprHighBuilder = decimal.MinValue;
            _oprLowBuilder = decimal.MaxValue;
            _oprFrozen = false;
            _oprDate = DateTime.MinValue;
        }

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
            UpdateRthVwapAndBands(bar - 1, c, ny, mins, rthStartMins, rthEndMins);
            UpdateOprRange(bar - 1, c, ny, mins, rthStartMins);
            UpdateBarFormationSpeed(bar);
        }

        private void UpdateOprRange(int bar, IndicatorCandle c, DateTime ny, int mins, int rthStartMins)
        {
            int oprEndMins = rthStartMins + 15;
            DateTime nyDate = ny.Date;

            if (_oprDate != nyDate)
            {
                _oprDate = nyDate;
                _oprHighBuilder = decimal.MinValue;
                _oprLowBuilder = decimal.MaxValue;
                _oprFrozen = false;
                _ctx.HasOpr = false;
            }

            if (mins < rthStartMins || mins >= oprEndMins)
            {
                if (mins >= oprEndMins && !_oprFrozen && _oprHighBuilder > decimal.MinValue)
                {
                    _ctx.OprHigh = _oprHighBuilder;
                    _ctx.OprLow = _oprLowBuilder;
                    _ctx.HasOpr = true;
                    _oprFrozen = true;
                }
                return;
            }

            if (c.High > _oprHighBuilder) _oprHighBuilder = c.High;
            if (c.Low < _oprLowBuilder) _oprLowBuilder = c.Low;
        }

        private void FinalizePriorDaySession()
        {
            if (!_sessionHasRange) return;
            _ctx.PriorDayHigh = _sessionHigh;
            _ctx.PriorDayLow = _sessionLow;
            _ctx.PriorDayClose = _sessionClose;
            _ctx.HasPriorDay = true;
        }

        private void TrackRthSessionRange(IndicatorCandle c)
        {
            if (c.High > _sessionHigh) _sessionHigh = c.High;
            if (c.Low < _sessionLow) _sessionLow = c.Low;
            _sessionClose = c.Close;
            _sessionHasRange = true;
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

        private void UpdateOvernightWindow(int bar, IndicatorCandle c, DateTime ny, int mins, int rthStartMins)
        {
            int onStartMins = 18 * 60;
            DateTime nyDate = ny.Date;

            bool inEvening = mins >= onStartMins;
            bool inPreRth = mins < rthStartMins;
            bool inRth = mins >= rthStartMins && mins < (16 * 60);

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
                _onHighBuilder = decimal.MinValue;
                _onLowBuilder = decimal.MaxValue;
                _onHasBuilder = false;
            }

            _prevBarNyMins = mins;
            _prevBarNyDate = nyDate;
        }

        /// <summary>
        /// VWAP session RTH + écart-type pondéré volume (aligné indicateur VWAP ATAS / stdev).
        /// variance = sum(v * (typical - vwap)^2) / sum(v), stdev = sqrt(variance).
        /// </summary>
        private void UpdateRthVwapAndBands(int bar, IndicatorCandle c, DateTime ny, int mins, int rthStartMins, int rthEndMins)
        {
            DateTime nyDate = ny.Date;
            bool inRth = mins >= rthStartMins && mins < rthEndMins;

            if (!inRth)
            {
                if (_rthVwapStarted && _rthDate != nyDate)
                {
                    _rthSumPV = 0m;
                    _rthSumV = 0m;
                    _rthSumVar = 0m;
                    _rthVwapStarted = false;
                    _ctx.HasRthVwap = false;
                    _ctx.HasRthOpen = false;
                    _ctx.HasSdBands = false;
                }
                return;
            }

            bool newSession = !_rthVwapStarted || _rthDate != nyDate;
            if (newSession)
            {
                FinalizePriorDaySession();
                _sessionHigh = decimal.MinValue;
                _sessionLow = decimal.MaxValue;
                _sessionClose = 0m;
                _sessionHasRange = false;

                _rthSumPV = 0m;
                _rthSumV = 0m;
                _rthSumVar = 0m;
                _rthVwapStarted = true;
                _rthDate = nyDate;
                _ctx.HasRthVwap = false;
                _ctx.HasSdBands = false;
                _ctx.RthOpen = c.Open;
                _ctx.HasRthOpen = true;
            }

            decimal typical = (c.High + c.Low + c.Close) / 3m;
            decimal v = (decimal)c.Volume;
            if (v <= 0m) v = 1m;

            _rthSumPV += typical * v;
            _rthSumV += v;

            if (_rthSumV > 0m)
            {
                _ctx.RthVwap = _rthSumPV / _rthSumV;
                _ctx.HasRthVwap = true;

                decimal diff = typical - _ctx.RthVwap;
                _rthSumVar += v * diff * diff;
                _ctx.RthStdev = (decimal)Math.Sqrt((double)(_rthSumVar / _rthSumV));

                _ctx.SdPlus1 = _ctx.RthVwap + SdMultiplier1 * _ctx.RthStdev;
                _ctx.SdMinus1 = _ctx.RthVwap - SdMultiplier1 * _ctx.RthStdev;
                _ctx.SdPlus2 = _ctx.RthVwap + SdMultiplier2 * _ctx.RthStdev;
                _ctx.SdMinus2 = _ctx.RthVwap - SdMultiplier2 * _ctx.RthStdev;
                _ctx.HasSdBands = _ctx.RthStdev > 0m;

                if (_vwapTrail.Count == 0 || _vwapTrail[_vwapTrail.Count - 1].Bar != bar)
                    _vwapTrail.Add(new VwapPoint { Bar = bar, Vwap = _ctx.RthVwap });
                else
                    _vwapTrail[_vwapTrail.Count - 1] = new VwapPoint { Bar = bar, Vwap = _ctx.RthVwap };

                UpdateVwapSlopeAndRegime(bar);
            }

            TrackRthSessionRange(c);
        }

        private void UpdateVwapSlopeAndRegime(int bar)
        {
            decimal ts = Security?.TickSize ?? 0.25m;
            if (ts <= 0m || !_ctx.HasRthVwap) return;

            int lookback = Math.Max(2, VwapSlopeLookback);
            decimal vwapNow = _ctx.RthVwap;
            decimal vwapPast = vwapNow;

            int targetBar = bar - lookback;
            for (int i = _vwapTrail.Count - 1; i >= 0; i--)
            {
                if (_vwapTrail[i].Bar <= targetBar)
                {
                    vwapPast = _vwapTrail[i].Vwap;
                    break;
                }
            }

            _ctx.VwapSlopeTicksPerBar = (vwapNow - vwapPast) / (lookback * ts);

            if (_ctx.VwapSlopeTicksPerBar >= VwapSlopeTrendThreshold)
                _ctx.VwapRegime = VwapRegime.TrendUp;
            else if (_ctx.VwapSlopeTicksPerBar <= -VwapSlopeTrendThreshold)
                _ctx.VwapRegime = VwapRegime.TrendDown;
            else
                _ctx.VwapRegime = VwapRegime.Range;
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

        private decimal DistanceToLevelTicks(decimal price, decimal level, decimal ts)
        {
            if (ts <= 0m) return decimal.MaxValue;
            return Math.Abs(price - level) / ts;
        }

        private struct LevelHit
        {
            public ContextLevelKind Kind;
            public decimal Price;
            public decimal DistTicks;
        }

        private LevelHit? FindNearestLevel(IndicatorCandle c, decimal ts)
        {
            LevelHit? best = null;
            bool preferLong = c.Close >= c.Open;

            void Try(ContextLevelKind kind, decimal level, bool has)
            {
                if (!has) return;
                decimal d = WickAwareDistTicks(c, level, ts, preferLong);
                if (!best.HasValue || d < best.Value.DistTicks)
                    best = new LevelHit { Kind = kind, Price = level, DistTicks = d };
            }

            Try(ContextLevelKind.RthVwap, _ctx.RthVwap, _ctx.HasRthVwap);
            Try(ContextLevelKind.SdPlus1, _ctx.SdPlus1, _ctx.HasSdBands);
            Try(ContextLevelKind.SdMinus1, _ctx.SdMinus1, _ctx.HasSdBands);
            Try(ContextLevelKind.SdPlus2, _ctx.SdPlus2, _ctx.HasSdBands);
            Try(ContextLevelKind.SdMinus2, _ctx.SdMinus2, _ctx.HasSdBands);
            Try(ContextLevelKind.RthOpen, _ctx.RthOpen, _ctx.HasRthOpen);
            Try(ContextLevelKind.OprHigh, _ctx.OprHigh, _ctx.HasOpr);
            Try(ContextLevelKind.OprLow, _ctx.OprLow, _ctx.HasOpr);
            Try(ContextLevelKind.PriorDayHigh, _ctx.PriorDayHigh, _ctx.HasPriorDay);
            Try(ContextLevelKind.PriorDayLow, _ctx.PriorDayLow, _ctx.HasPriorDay);
            Try(ContextLevelKind.PriorDayClose, _ctx.PriorDayClose, _ctx.HasPriorDay);
            Try(ContextLevelKind.OvernightHigh, _ctx.OvernightHigh, _ctx.HasOvernight);
            Try(ContextLevelKind.OvernightLow, _ctx.OvernightLow, _ctx.HasOvernight);

            return best;
        }

        private static string LevelKindToName(ContextLevelKind kind)
        {
            switch (kind)
            {
                case ContextLevelKind.OvernightHigh: return "ONH";
                case ContextLevelKind.OvernightLow: return "ONL";
                case ContextLevelKind.RthVwap: return "VWAP";
                case ContextLevelKind.SdPlus1: return "SD+1";
                case ContextLevelKind.SdMinus1: return "SD-1";
                case ContextLevelKind.SdPlus2: return "SD+2";
                case ContextLevelKind.SdMinus2: return "SD-2";
                case ContextLevelKind.RthOpen: return "Open";
                case ContextLevelKind.OprHigh: return "OPR_H";
                case ContextLevelKind.OprLow: return "OPR_L";
                case ContextLevelKind.PriorDayHigh: return "D1_H";
                case ContextLevelKind.PriorDayLow: return "D1_L";
                case ContextLevelKind.PriorDayClose: return "D1_C";
                default: return kind.ToString();
            }
        }

        private bool IsNearLevel(decimal price, decimal level, bool has, decimal ts, int maxTicks)
        {
            if (!has || ts <= 0m) return false;
            return DistanceToLevelTicks(price, level, ts) <= maxTicks;
        }

        /// <summary>Support : close ou low touche le niveau (réaction sur la mèche).</summary>
        private bool TouchSupportLevel(IndicatorCandle c, decimal level, bool has, decimal ts)
        {
            if (!has || ts <= 0m) return false;
            int t = ZoneProximityTicks;
            return IsNearLevel(c.Close, level, true, ts, t) || IsNearLevel(c.Low, level, true, ts, t);
        }

        /// <summary>Résistance : close ou high touche le niveau.</summary>
        private bool TouchResistanceLevel(IndicatorCandle c, decimal level, bool has, decimal ts)
        {
            if (!has || ts <= 0m) return false;
            int t = ZoneProximityTicks;
            return IsNearLevel(c.Close, level, true, ts, t) || IsNearLevel(c.High, level, true, ts, t);
        }

        private bool TouchVwapZone(IndicatorCandle c, decimal ts)
        {
            if (!_ctx.HasRthVwap || ts <= 0m) return false;
            return TouchSupportLevel(c, _ctx.RthVwap, true, ts)
                || TouchResistanceLevel(c, _ctx.RthVwap, true, ts);
        }

        private decimal WickAwareDistTicks(IndicatorCandle c, decimal level, decimal ts, bool forLong)
        {
            decimal dClose = DistanceToLevelTicks(c.Close, level, ts);
            decimal dExt = forLong
                ? DistanceToLevelTicks(c.Low, level, ts)
                : DistanceToLevelTicks(c.High, level, ts);
            return Math.Min(dClose, dExt);
        }

        /// <summary>Proximité aux niveaux contextuels (legacy scoring).</summary>
        private decimal ComputeProximityFeature(IndicatorCandle c, decimal ts)
        {
            var hit = FindNearestLevel(c, ts);
            if (!hit.HasValue) return 0m;
            if (hit.Value.DistTicks > ProximityTicks) return 0m;
            return 1m - (hit.Value.DistTicks / ProximityTicks);
        }

        private (decimal nearestLevel, bool hasLevel, bool aboveLevel) GetNearestContextLevel(IndicatorCandle c)
        {
            decimal ts = Security?.TickSize ?? 0.25m;
            var hit = FindNearestLevel(c, ts);
            if (!hit.HasValue) return (0m, false, false);
            return (hit.Value.Price, true, c.Close > hit.Value.Price);
        }
    }
}
