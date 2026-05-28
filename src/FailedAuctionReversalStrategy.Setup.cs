using System;
using ATAS.Indicators;
using AlgoOrderflow.Models;

namespace AlgoOrderflow
{
    /// <summary>
    /// Gate setup v2 : volume surge, divergence bougie, absorption VPOC, alignement trend.
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        private struct SetupPrimitives
        {
            public bool VolSurge;
            public decimal VolRatio;
            public bool BullishBar;
            public bool BearishBar;
            public bool MeanRevertLong;
            public bool MeanRevertShort;
            public bool TrendLong;
            public bool TrendShort;
            public bool AbsorptionLong;
            public bool AbsorptionShort;
            public decimal PocPrice;
            public bool HasPoc;
        }

        private SetupPrimitives EvaluateSetupPrimitives(IndicatorCandle c, int evalBar)
        {
            var p = new SetupPrimitives();
            decimal vol = (decimal)c.Volume;
            p.BullishBar = c.Close >= c.Open;
            p.BearishBar = c.Close < c.Open;

            decimal sumVol = 0m;
            int n = 0;
            int start = Math.Max(0, evalBar - VolLookbackBars);
            for (int i = start; i < evalBar; i++)
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

            if (n > 0 && sumVol > 0m)
            {
                decimal avg = sumVol / n;
                p.VolRatio = vol / avg;
                p.VolSurge = p.VolRatio >= VolRatioMin;
            }

            p.MeanRevertLong = p.BullishBar && c.Delta <= -DeltaMinAbs;
            p.MeanRevertShort = p.BearishBar && c.Delta >= DeltaMinAbs;
            p.TrendLong = p.BullishBar && c.Delta >= DeltaMinAbs;
            p.TrendShort = p.BearishBar && c.Delta <= -DeltaMinAbs;

            try
            {
                var pocInfo = c.MaxVolumePriceInfo;
                if (pocInfo != null)
                {
                    p.PocPrice = pocInfo.Price;
                    p.HasPoc = true;
                    decimal range = c.High - c.Low;
                    if (range > 0m)
                    {
                        decimal pocPos = (p.PocPrice - c.Low) / range;
                        decimal lowZone = 1m - VpocExtremeRatio;
                        decimal highZone = VpocExtremeRatio;

                        p.AbsorptionLong = pocPos <= lowZone && c.Close >= p.PocPrice;
                        p.AbsorptionShort = pocPos >= highZone && c.Close <= p.PocPrice;
                    }
                }
            }
            catch { }

            return p;
        }

        private void FillSetupSnapshotBase(SetupSnapshot snap, IndicatorCandle c, SetupPrimitives p, decimal ts)
        {
            snap.SignalOpen = c.Open;
            snap.SignalHigh = c.High;
            snap.SignalLow = c.Low;
            snap.SignalClose = c.Close;
            snap.SignalDelta = c.Delta;
            snap.SignalVolume = (decimal)c.Volume;
            snap.VolRatio = p.VolRatio;
            snap.PocPrice = p.PocPrice;
            snap.FlagVolSurge = p.VolSurge;
            snap.RthOpen = _ctx.RthOpen;
            snap.RthVwap = _ctx.RthVwap;
            snap.AboveVwap = _ctx.HasRthVwap && c.Close > _ctx.RthVwap;
            snap.DistVwapTicks = _ctx.HasRthVwap
                ? DistanceToLevelTicks(c.Close, _ctx.RthVwap, ts)
                : decimal.MaxValue;
            snap.VwapSlopeTicksPerBar = _ctx.VwapSlopeTicksPerBar;
            snap.VwapRegime = _ctx.VwapRegime;
            snap.SdPlus1 = _ctx.SdPlus1;
            snap.SdMinus1 = _ctx.SdMinus1;
            snap.SdPlus2 = _ctx.SdPlus2;
            snap.SdMinus2 = _ctx.SdMinus2;

            var hit = FindNearestLevel(c, ts);
            if (hit.HasValue)
            {
                snap.NearestLevel = LevelKindToName(hit.Value.Kind);
                snap.DistNearestTicks = hit.Value.DistTicks;
                snap.FlagInZone = hit.Value.DistTicks <= ZoneProximityTicks;
            }
            else
            {
                snap.NearestLevel = "";
                snap.DistNearestTicks = decimal.MaxValue;
                snap.FlagInZone = false;
            }
        }

        private bool PassesCommonGates(SetupPrimitives p, out string failReason)
        {
            failReason = null;
            if (!p.VolSurge) { failReason = "no_vol_surge"; return false; }
            if (!p.HasPoc) { failReason = "no_poc"; return false; }
            return true;
        }
    }
}
