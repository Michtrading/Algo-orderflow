using System;
using System.Collections.Generic;
using ATAS.Indicators;
using AlgoOrderflow.Models;

namespace AlgoOrderflow
{
    /// <summary>
    /// Breakout microstructure — cassure swing ou nœud volume (MaxAsk/MaxBid ATAS), filtre trend VWAP.
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        private SetupSnapshot EvaluateBreakoutSetup(int evalBar, decimal ts)
        {
            if (!EnableBreakoutEngine) return null;

            IndicatorCandle c;
            try { c = GetCandle(evalBar); }
            catch { return null; }
            if (c == null) return null;

            var snap = new SetupSnapshot();
            var p = EvaluateSetupPrimitives(c, evalBar);
            FillSetupSnapshotBase(snap, c, p, ts);
            FillBreakoutFootprintStats(snap, c);

            if (_ctx.VwapRegime == VwapRegime.Range)
            {
                if (!BreakoutAllowInRange)
                {
                    snap.VetoReason = "breakout_range_veto";
                    return snap;
                }

                if (Math.Abs(_ctx.VwapSlopeTicksPerBar) < BreakoutRangeMinSlope)
                {
                    snap.VetoReason = "breakout_range_flat_veto";
                    return snap;
                }
            }

            if (TryBuildBreakoutLong(evalBar, c, p, ts, snap, out var longReason))
                return FinalizeValidSetup(snap, p);

            if (TryBuildBreakoutShort(evalBar, c, p, ts, snap, out var shortReason))
                return FinalizeValidSetup(snap, p);

            snap.VetoReason = FormatBreakoutVeto(longReason, shortReason);
            LogBreakoutShadows(evalBar, c, snap, longReason, shortReason);
            return snap;
        }

        private bool TryBuildBreakoutLong(int evalBar, IndicatorCandle c, SetupPrimitives p, decimal ts, SetupSnapshot snap, out string reason)
        {
            reason = null;
            if (!PassesBreakoutTrendFilter(isLong: true, out reason)) return false;
            if (!PassesBreakoutInitiation(c, p, evalBar, ts, isLong: true, out reason)) return false;
            if (IsAbsorptionConflict(p, isLong: true)) { reason = "breakout_absorption_conflict_long"; return false; }

            decimal refPrice = 0m;
            string trigger = null;
            decimal confirm = Math.Max(0, BreakConfirmTicks) * ts;

            if (BreakUseSwing && TryGetSwingHigh(evalBar, out decimal swingHigh))
            {
                if (c.Close >= swingHigh + confirm)
                {
                    refPrice = swingHigh;
                    trigger = $"swing_high_{BreakSwingLookbackBars}";
                }
            }

            if (BreakUseVolumeNode && TryGetMaxAskRefPrice(evalBar, out decimal askPrice, out int askLookback))
            {
                if (c.Close >= askPrice + confirm && (refPrice <= 0m || askPrice >= refPrice))
                {
                    refPrice = askPrice;
                    trigger = trigger == null
                        ? $"max_ask_{askLookback}"
                        : $"{trigger}+max_ask_{askLookback}";
                }
            }

            if (refPrice <= 0m || string.IsNullOrEmpty(trigger)) { reason = "breakout_long_no_trigger"; return false; }
            if (!PassesBreakoutAcceptance(c, isLong: true, out reason)) return false;

            snap.Side = SignalSide.Long;
            snap.TradeMode = TradeMode.Breakout;
            snap.BreakTrigger = trigger;
            snap.BreakRefPrice = refPrice;
            snap.BreakRefDelta = TryGetPriceLevelDelta(c, refPrice, out var dLong) ? dLong : 0m;
            snap.FlagVolSurge = p.VolSurge;
            snap.FlagTrendAlignment = true;
            snap.FlagDivergence = false;
            snap.FlagAbsorption = false;
            return true;
        }

        private bool TryBuildBreakoutShort(int evalBar, IndicatorCandle c, SetupPrimitives p, decimal ts, SetupSnapshot snap, out string reason)
        {
            reason = null;
            if (!PassesBreakoutTrendFilter(isLong: false, out reason)) return false;
            if (!PassesBreakoutInitiation(c, p, evalBar, ts, isLong: false, out reason)) return false;
            if (IsAbsorptionConflict(p, isLong: false)) { reason = "breakout_absorption_conflict_short"; return false; }

            decimal refPrice = 0m;
            string trigger = null;
            decimal confirm = Math.Max(0, BreakConfirmTicks) * ts;

            if (BreakUseSwing && TryGetSwingLow(evalBar, out decimal swingLow))
            {
                if (c.Close <= swingLow - confirm)
                {
                    refPrice = swingLow;
                    trigger = $"swing_low_{BreakSwingLookbackBars}";
                }
            }

            if (BreakUseVolumeNode && TryGetMaxBidRefPrice(evalBar, out decimal bidPrice, out int bidLookback))
            {
                if (c.Close <= bidPrice - confirm && (refPrice <= 0m || bidPrice <= refPrice))
                {
                    refPrice = bidPrice;
                    trigger = trigger == null
                        ? $"max_bid_{bidLookback}"
                        : $"{trigger}+max_bid_{bidLookback}";
                }
            }

            if (refPrice <= 0m || string.IsNullOrEmpty(trigger)) { reason = "breakout_short_no_trigger"; return false; }
            if (!PassesBreakoutAcceptance(c, isLong: false, out reason)) return false;

            snap.Side = SignalSide.Short;
            snap.TradeMode = TradeMode.Breakout;
            snap.BreakTrigger = trigger;
            snap.BreakRefPrice = refPrice;
            snap.BreakRefDelta = TryGetPriceLevelDelta(c, refPrice, out var dShort) ? dShort : 0m;
            snap.FlagVolSurge = p.VolSurge;
            snap.FlagTrendAlignment = true;
            snap.FlagDivergence = false;
            snap.FlagAbsorption = false;
            return true;
        }

        private bool PassesBreakoutInitiation(IndicatorCandle c, SetupPrimitives p, int evalBar, decimal ts, bool isLong, out string reason)
        {
            reason = null;
            if (p.VolRatio < BreakVolRatioMin) { reason = "breakout_no_volume_surge"; return false; }

            if (isLong)
            {
                if (!p.BullishBar || c.Delta < BreakDeltaMinAbs) { reason = "breakout_no_delta_align_long"; return false; }
            }
            else
            {
                if (!p.BearishBar || c.Delta > -BreakDeltaMinAbs) { reason = "breakout_no_delta_align_short"; return false; }
            }

            if (!PassesBreakoutVelocity(evalBar, c)) { reason = "breakout_no_velocity"; return false; }
            return true;
        }

        private bool PassesBreakoutVelocity(int evalBar, IndicatorCandle c)
        {
            if (_ctx.BarFormationSecondsAvg <= 0m) return true;

            decimal duration = GetBarDurationSeconds(evalBar, c);
            if (duration <= 0m) return true;

            return duration <= BreakVelocityMaxRatio * _ctx.BarFormationSecondsAvg;
        }

        private bool PassesBreakoutAcceptance(IndicatorCandle c, bool isLong, out string reason)
        {
            reason = null;
            decimal range = c.High - c.Low;
            if (range <= 0m) { reason = "breakout_zero_range"; return false; }

            decimal pos = (c.Close - c.Low) / range;
            if (isLong)
            {
                if (pos < BreakAcceptanceTopRatio) { reason = "breakout_long_no_acceptance"; return false; }
                try
                {
                    var maxAsk = c.MaxAskPriceInfo;
                    if (maxAsk != null && maxAsk.Price > 0m)
                    {
                        decimal askPos = (maxAsk.Price - c.Low) / range;
                        if (askPos < BreakAcceptanceTopRatio) { reason = "breakout_long_no_acceptance"; return false; }
                    }
                }
                catch { }
                if (!PassesBreakoutConcentration(c, isLong: true))
                {
                    reason = "breakout_long_no_concentration";
                    return false;
                }
                return true;
            }

            if (pos > (1m - BreakAcceptanceTopRatio)) { reason = "breakout_short_no_acceptance"; return false; }
            try
            {
                var maxBid = c.MaxBidPriceInfo;
                if (maxBid != null && maxBid.Price > 0m)
                {
                    decimal bidPos = (maxBid.Price - c.Low) / range;
                    if (bidPos > (1m - BreakAcceptanceTopRatio)) { reason = "breakout_short_no_acceptance"; return false; }
                }
            }
            catch { }
            if (!PassesBreakoutConcentration(c, isLong: false))
            {
                reason = "breakout_short_no_concentration";
                return false;
            }
            return true;
        }

        private bool PassesBreakoutConcentration(IndicatorCandle c, bool isLong)
        {
            if (!BreakUseConcentrationFilter) return true;
            if (!TryGetSideConcentration(c, isLong, out decimal top1, out decimal top2))
                return true;

            return top1 >= BreakConcentrationTop1Min && top2 >= BreakConcentrationTop2Min;
        }

        private static bool TryGetSideConcentration(IndicatorCandle c, bool isLong, out decimal top1Ratio, out decimal top2Ratio)
        {
            top1Ratio = 0m;
            top2Ratio = 0m;

            try
            {
                var levels = c.GetAllPriceLevels();
                if (levels == null) return false;

                var sideValues = new List<decimal>();
                decimal sum = 0m;

                foreach (var lvl in levels)
                {
                    if (lvl == null) continue;
                    decimal v = isLong ? lvl.Ask : lvl.Bid;
                    if (v <= 0m) continue;
                    sideValues.Add(v);
                    sum += v;
                }

                if (sum <= 0m || sideValues.Count == 0) return false;
                sideValues.Sort((a, b) => b.CompareTo(a));

                var top1 = sideValues[0];
                var top2 = sideValues.Count > 1 ? sideValues[1] : 0m;

                top1Ratio = top1 / sum;
                top2Ratio = (top1 + top2) / sum;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void FillBreakoutFootprintStats(SetupSnapshot snap, IndicatorCandle c)
        {
            try
            {
                var maxAsk = c.MaxAskPriceInfo;
                if (maxAsk != null) snap.MaxAskPrice = maxAsk.Price;
            }
            catch { }

            try
            {
                var maxBid = c.MaxBidPriceInfo;
                if (maxBid != null) snap.MaxBidPrice = maxBid.Price;
            }
            catch { }

            if (TryGetSideConcentration(c, isLong: true, out var a1, out var a2))
            {
                snap.Top1AskRatio = a1;
                snap.Top2AskRatio = a2;
            }

            if (TryGetSideConcentration(c, isLong: false, out var b1, out var b2))
            {
                snap.Top1BidRatio = b1;
                snap.Top2BidRatio = b2;
            }
        }

        private static bool TryGetPriceLevelDelta(IndicatorCandle c, decimal price, out decimal delta)
        {
            delta = 0m;
            if (price <= 0m) return false;
            try
            {
                var info = c.GetPriceVolumeInfo(price);
                if (info == null) return false;
                delta = info.Ask - info.Bid;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAbsorptionConflict(SetupPrimitives p, bool isLong)
        {
            return isLong
                ? (p.MeanRevertLong && p.AbsorptionLong)
                : (p.MeanRevertShort && p.AbsorptionShort);
        }

        private bool TryGetSwingHigh(int evalBar, out decimal swingHigh)
        {
            swingHigh = decimal.MinValue;
            int n = Math.Max(1, BreakSwingLookbackBars);
            int start = Math.Max(0, evalBar - n);
            bool any = false;

            for (int i = start; i < evalBar; i++)
            {
                try
                {
                    var ci = GetCandle(i);
                    if (ci == null) continue;
                    if (ci.High > swingHigh) swingHigh = ci.High;
                    any = true;
                }
                catch { }
            }

            return any && swingHigh > decimal.MinValue;
        }

        private bool TryGetSwingLow(int evalBar, out decimal swingLow)
        {
            swingLow = decimal.MaxValue;
            int n = Math.Max(1, BreakSwingLookbackBars);
            int start = Math.Max(0, evalBar - n);
            bool any = false;

            for (int i = start; i < evalBar; i++)
            {
                try
                {
                    var ci = GetCandle(i);
                    if (ci == null) continue;
                    if (ci.Low < swingLow) swingLow = ci.Low;
                    any = true;
                }
                catch { }
            }

            return any && swingLow < decimal.MaxValue;
        }

        private bool TryGetMaxAskRefPrice(int evalBar, out decimal price, out int lookbackUsed)
        {
            price = 0m;
            lookbackUsed = Math.Max(1, BreakVolumeLookbackBars);
            decimal bestAsk = 0m;

            int start = Math.Max(0, evalBar - lookbackUsed);
            for (int i = start; i < evalBar; i++)
            {
                try
                {
                    var ci = GetCandle(i);
                    if (ci == null) continue;
                    var info = ci.MaxAskPriceInfo;
                    if (info == null || info.Ask <= 0m) continue;
                    if (info.Ask > bestAsk)
                    {
                        bestAsk = info.Ask;
                        price = info.Price;
                    }
                }
                catch { }
            }

            return price > 0m;
        }

        private bool TryGetMaxBidRefPrice(int evalBar, out decimal price, out int lookbackUsed)
        {
            price = 0m;
            lookbackUsed = Math.Max(1, BreakVolumeLookbackBars);
            decimal bestBid = 0m;

            int start = Math.Max(0, evalBar - lookbackUsed);
            for (int i = start; i < evalBar; i++)
            {
                try
                {
                    var ci = GetCandle(i);
                    if (ci == null) continue;
                    var info = ci.MaxBidPriceInfo;
                    if (info == null || info.Bid <= 0m) continue;
                    if (info.Bid > bestBid)
                    {
                        bestBid = info.Bid;
                        price = info.Price;
                    }
                }
                catch { }
            }

            return price > 0m;
        }

        private decimal GetBarDurationSeconds(int evalBar, IndicatorCandle c)
        {
            if (evalBar <= 0) return 0m;
            try
            {
                var prev = GetCandle(evalBar - 1);
                if (prev == null) return 0m;
                var dt = c.Time - prev.Time;
                if (dt.TotalSeconds > 0) return (decimal)dt.TotalSeconds;
            }
            catch { }
            return 0m;
        }

        private bool PassesBreakoutTrendFilter(bool isLong, out string reason)
        {
            reason = null;
            if (isLong)
            {
                if (_ctx.VwapRegime == VwapRegime.TrendUp) return true;
                if (BreakoutAllowInRange && _ctx.VwapRegime == VwapRegime.Range && _ctx.VwapSlopeTicksPerBar > 0m)
                    return true;
                reason = _ctx.VwapRegime == VwapRegime.Range ? "breakout_range_no_up_bias" : "breakout_trend_not_up";
                return false;
            }

            if (_ctx.VwapRegime == VwapRegime.TrendDown) return true;
            if (BreakoutAllowInRange && _ctx.VwapRegime == VwapRegime.Range && _ctx.VwapSlopeTicksPerBar < 0m)
                return true;
            reason = _ctx.VwapRegime == VwapRegime.Range ? "breakout_range_no_down_bias" : "breakout_trend_not_down";
            return false;
        }

        private (int sl, int tp) GetBracketTicksForSetup(SetupSnapshot setup)
        {
            if (setup != null && setup.TradeMode == TradeMode.Breakout)
                return (BreakSlTicks, BreakTpTicks);
            return (SLTicks, TPTicks);
        }
    }
}
