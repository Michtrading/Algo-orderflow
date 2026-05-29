using ATAS.Indicators;
using AlgoOrderflow.Models;

namespace AlgoOrderflow
{
    /// <summary>
    /// Routeur v2 — réactions sur VWAP / SD (trend + divergence), sans bloquer le mean revert au seul régime Range.
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        private SetupSnapshot EvaluateSetupV2(int evalBar, decimal ts)
        {
            IndicatorCandle c;
            try { c = GetCandle(evalBar); }
            catch { return null; }
            if (c == null) return null;

            if (!_ctx.HasRthVwap || !_ctx.HasRthOpen)
                return null;

            var p = EvaluateSetupPrimitives(c, evalBar);
            var snap = new SetupSnapshot();

            FillSetupSnapshotBase(snap, c, p, ts);

            if (!PassesCommonGates(p, out var gateFail))
            {
                snap.VetoReason = gateFail;
                LogAbsorptionShadow(evalBar, c, p, snap);
                return snap;
            }

            if (IsAuctionVeto(c, ts))
            {
                snap.VetoReason = "auction_open";
                LogAbsorptionShadow(evalBar, c, p, snap);
                return snap;
            }

            if (TrySelectSdZoneSetup(c, p, ts, snap))
                return FinalizeValidSetup(snap, p);

            if (IsNearAnyTradingZone(c, ts))
            {
                snap.VetoReason = "absorption_pattern_veto";
                LogAbsorptionShadow(evalBar, c, p, snap);
                return snap;
            }

            snap.VetoReason = "no_zone_match";
            LogAbsorptionShadow(evalBar, c, p, snap);
            return snap;
        }

        private bool IsAuctionVeto(IndicatorCandle c, decimal ts)
        {
            if (_ctx.VwapRegime != VwapRegime.Range) return false;
            if (!_ctx.HasRthOpen) return false;

            bool nearOpen = DistanceToLevelTicks(c.Close, _ctx.RthOpen, ts) <= AuctionOpenRangeTicks
                || DistanceToLevelTicks(c.Low, _ctx.RthOpen, ts) <= AuctionOpenRangeTicks
                || DistanceToLevelTicks(c.High, _ctx.RthOpen, ts) <= AuctionOpenRangeTicks;
            if (!nearOpen) return false;

            bool nearSd2 = TouchResistanceLevel(c, _ctx.SdPlus2, _ctx.HasSdBands, ts)
                || TouchSupportLevel(c, _ctx.SdMinus2, _ctx.HasSdBands, ts);

            return !nearSd2;
        }

        /// <summary>
        /// Tous les setups sur VWAP / SD±1 / SD±2 — trend (delta aligné OU absorption pullback) et divergence.
        /// </summary>
        private bool TrySelectSdZoneSetup(IndicatorCandle c, SetupPrimitives p, decimal ts, SetupSnapshot snap)
        {
            bool atSupport = TouchVwapZone(c, ts)
                || TouchSupportLevel(c, _ctx.SdMinus1, _ctx.HasSdBands, ts)
                || TouchSupportLevel(c, _ctx.SdMinus2, _ctx.HasSdBands, ts);

            bool atResistance = TouchVwapZone(c, ts)
                || TouchResistanceLevel(c, _ctx.SdPlus1, _ctx.HasSdBands, ts)
                || TouchResistanceLevel(c, _ctx.SdPlus2, _ctx.HasSdBands, ts);

            bool nearSdPlus2 = TouchResistanceLevel(c, _ctx.SdPlus2, _ctx.HasSdBands, ts);
            bool nearSdMinus2 = TouchSupportLevel(c, _ctx.SdMinus2, _ctx.HasSdBands, ts);

            // ── Continuation par absorption (trend) ─────────────────────
            // TrendUp : long au support — delta aligné OU divergence (pullback absorbé)
            if (_ctx.VwapRegime == VwapRegime.TrendUp && atSupport && p.AbsorptionLong)
            {
                if (p.TrendLong)
                    return AssignSetup(snap, SignalSide.Long, TradeMode.Trend, trend: true, divergence: false);
                if (p.MeanRevertLong)
                    return AssignSetup(snap, SignalSide.Long, TradeMode.Trend, trend: false, divergence: true);
            }

            // TrendDown : short à la résistance
            if (_ctx.VwapRegime == VwapRegime.TrendDown && atResistance && p.AbsorptionShort)
            {
                if (p.TrendShort)
                    return AssignSetup(snap, SignalSide.Short, TradeMode.Trend, trend: true, divergence: false);
                if (p.MeanRevertShort)
                    return AssignSetup(snap, SignalSide.Short, TradeMode.Trend, trend: false, divergence: true);
            }

            // ── SD±2 : mean revert (divergence) — toutes pentes ───────────
            if (nearSdPlus2 && p.MeanRevertShort && p.AbsorptionShort && PassesOpenFilterShortMeanRevert(c, ts))
                return AssignSetup(snap, SignalSide.Short, TradeMode.MeanRevert, trend: false, divergence: true);

            if (nearSdMinus2 && p.MeanRevertLong && p.AbsorptionLong && PassesOpenFilterLongMeanRevert(c, ts))
                return AssignSetup(snap, SignalSide.Long, TradeMode.MeanRevert, trend: false, divergence: true);

            // ── Divergence + absorption sur SD±1 / VWAP (tout régime) ───
            if (atSupport && p.MeanRevertLong && p.AbsorptionLong && PassesOpenFilterLongMeanRevert(c, ts))
            {
                var mode = _ctx.VwapRegime == VwapRegime.TrendDown ? TradeMode.MeanRevert : TradeMode.MeanRevert;
                return AssignSetup(snap, SignalSide.Long, mode, trend: false, divergence: true);
            }

            if (atResistance && p.MeanRevertShort && p.AbsorptionShort && PassesOpenFilterShortMeanRevert(c, ts))
            {
                if (_ctx.VwapRegime == VwapRegime.TrendUp && !nearSdPlus2 && !AllowCounterTrendShortMeanRevert)
                {
                    snap.VetoReason = "mr_short_countertrend_veto";
                    return false;
                }
                var mode = _ctx.VwapRegime == VwapRegime.TrendUp ? TradeMode.MeanRevert : TradeMode.MeanRevert;
                return AssignSetup(snap, SignalSide.Short, mode, trend: false, divergence: true);
            }

            return false;
        }

        private static bool AssignSetup(SetupSnapshot snap, SignalSide side, TradeMode mode,
            bool trend, bool divergence)
        {
            snap.Side = side;
            snap.TradeMode = mode;
            snap.FlagAbsorption = true;
            snap.FlagTrendAlignment = trend;
            snap.FlagDivergence = divergence;
            return true;
        }

        private bool PassesOpenFilterLongMeanRevert(IndicatorCandle c, decimal ts)
        {
            if (c.Close >= _ctx.RthOpen) return true;
            return TouchSupportLevel(c, _ctx.SdMinus2, _ctx.HasSdBands, ts);
        }

        private bool PassesOpenFilterShortMeanRevert(IndicatorCandle c, decimal ts)
        {
            if (c.Close <= _ctx.RthOpen) return true;
            return TouchResistanceLevel(c, _ctx.SdPlus2, _ctx.HasSdBands, ts);
        }

        private SetupSnapshot FinalizeValidSetup(SetupSnapshot snap, SetupPrimitives p)
        {
            snap.IsValid = true;
            snap.VetoReason = null;
            return snap;
        }
    }
}
