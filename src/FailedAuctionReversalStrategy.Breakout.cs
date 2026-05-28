using System;
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

            if (_ctx.VwapRegime == VwapRegime.Range)
            {
                snap.VetoReason = "breakout_range_veto";
                return snap;
            }

            if (TryBuildBreakoutLong(evalBar, c, p, ts, snap))
                return FinalizeValidSetup(snap, p);

            if (TryBuildBreakoutShort(evalBar, c, p, ts, snap))
                return FinalizeValidSetup(snap, p);

            snap.VetoReason = "no_breakout";
            return snap;
        }

        private bool TryBuildBreakoutLong(int evalBar, IndicatorCandle c, SetupPrimitives p, decimal ts, SetupSnapshot snap)
        {
            if (_ctx.VwapRegime != VwapRegime.TrendUp) return false;
            if (!PassesBreakoutInitiation(c, p, evalBar, ts, isLong: true)) return false;
            if (IsAbsorptionConflict(p, isLong: true)) return false;

            decimal refPrice = 0m;
            string trigger = null;

            if (BreakUseSwing && TryGetSwingHigh(evalBar, out decimal swingHigh))
            {
                if (c.Close > swingHigh)
                {
                    refPrice = swingHigh;
                    trigger = $"swing_high_{BreakSwingLookbackBars}";
                }
            }

            if (BreakUseVolumeNode && TryGetMaxAskRefPrice(evalBar, out decimal askPrice, out int askLookback))
            {
                if (c.Close > askPrice && (refPrice <= 0m || askPrice >= refPrice))
                {
                    refPrice = askPrice;
                    trigger = trigger == null
                        ? $"max_ask_{askLookback}"
                        : $"{trigger}+max_ask_{askLookback}";
                }
            }

            if (refPrice <= 0m || string.IsNullOrEmpty(trigger)) return false;
            if (!PassesBreakoutAcceptance(c, isLong: true)) return false;

            snap.Side = SignalSide.Long;
            snap.TradeMode = TradeMode.Breakout;
            snap.BreakTrigger = trigger;
            snap.BreakRefPrice = refPrice;
            snap.FlagVolSurge = p.VolSurge;
            snap.FlagTrendAlignment = true;
            snap.FlagDivergence = false;
            snap.FlagAbsorption = false;
            return true;
        }

        private bool TryBuildBreakoutShort(int evalBar, IndicatorCandle c, SetupPrimitives p, decimal ts, SetupSnapshot snap)
        {
            if (_ctx.VwapRegime != VwapRegime.TrendDown) return false;
            if (!PassesBreakoutInitiation(c, p, evalBar, ts, isLong: false)) return false;
            if (IsAbsorptionConflict(p, isLong: false)) return false;

            decimal refPrice = 0m;
            string trigger = null;

            if (BreakUseSwing && TryGetSwingLow(evalBar, out decimal swingLow))
            {
                if (c.Close < swingLow)
                {
                    refPrice = swingLow;
                    trigger = $"swing_low_{BreakSwingLookbackBars}";
                }
            }

            if (BreakUseVolumeNode && TryGetMaxBidRefPrice(evalBar, out decimal bidPrice, out int bidLookback))
            {
                if (c.Close < bidPrice && (refPrice <= 0m || bidPrice <= refPrice))
                {
                    refPrice = bidPrice;
                    trigger = trigger == null
                        ? $"max_bid_{bidLookback}"
                        : $"{trigger}+max_bid_{bidLookback}";
                }
            }

            if (refPrice <= 0m || string.IsNullOrEmpty(trigger)) return false;
            if (!PassesBreakoutAcceptance(c, isLong: false)) return false;

            snap.Side = SignalSide.Short;
            snap.TradeMode = TradeMode.Breakout;
            snap.BreakTrigger = trigger;
            snap.BreakRefPrice = refPrice;
            snap.FlagVolSurge = p.VolSurge;
            snap.FlagTrendAlignment = true;
            snap.FlagDivergence = false;
            snap.FlagAbsorption = false;
            return true;
        }

        private bool PassesBreakoutInitiation(IndicatorCandle c, SetupPrimitives p, int evalBar, decimal ts, bool isLong)
        {
            if (!p.VolSurge || p.VolRatio < BreakVolRatioMin) return false;

            if (isLong)
            {
                if (!p.BullishBar || c.Delta < BreakDeltaMinAbs) return false;
            }
            else
            {
                if (!p.BearishBar || c.Delta > -BreakDeltaMinAbs) return false;
            }

            if (!PassesBreakoutVelocity(evalBar, c)) return false;
            return true;
        }

        private bool PassesBreakoutVelocity(int evalBar, IndicatorCandle c)
        {
            if (_ctx.BarFormationSecondsAvg <= 0m) return true;

            decimal duration = GetBarDurationSeconds(evalBar, c);
            if (duration <= 0m) return true;

            return duration <= BreakVelocityMaxRatio * _ctx.BarFormationSecondsAvg;
        }

        private bool PassesBreakoutAcceptance(IndicatorCandle c, bool isLong)
        {
            decimal range = c.High - c.Low;
            if (range <= 0m) return false;

            decimal pos = (c.Close - c.Low) / range;
            if (isLong)
            {
                if (pos < BreakAcceptanceTopRatio) return false;
                try
                {
                    var maxAsk = c.MaxAskPriceInfo;
                    if (maxAsk != null && maxAsk.Price > 0m)
                    {
                        decimal askPos = (maxAsk.Price - c.Low) / range;
                        if (askPos < BreakAcceptanceTopRatio) return false;
                    }
                }
                catch { }
                return true;
            }

            if (pos > (1m - BreakAcceptanceTopRatio)) return false;
            try
            {
                var maxBid = c.MaxBidPriceInfo;
                if (maxBid != null && maxBid.Price > 0m)
                {
                    decimal bidPos = (maxBid.Price - c.Low) / range;
                    if (bidPos > (1m - BreakAcceptanceTopRatio)) return false;
                }
            }
            catch { }
            return true;
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

        private (int sl, int tp) GetBracketTicksForSetup(SetupSnapshot setup)
        {
            if (setup != null && setup.TradeMode == TradeMode.Breakout)
                return (BreakSlTicks, BreakTpTicks);
            return (SLTicks, TPTicks);
        }
    }
}
