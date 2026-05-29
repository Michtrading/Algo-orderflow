using System;
using System.Globalization;
using System.IO;
using System.Text;
using ATAS.Indicators;
using AlgoOrderflow.Models;

namespace AlgoOrderflow
{
    /// <summary>
    /// Journal shadow : PnL contrefactuel des setups vetés (recherche / calibration filtres).
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        private int _shadowIdSeq;
        private string _shadowJournalFilePath;
        private int _lastShadowBreakoutBar = -1;
        private int _lastShadowAbsorptionBar = -1;

        private const string ShadowJournalHeader =
            "timestamp_utc;shadow_id;run_mode;engine;eval_bar;veto_reason;breakout_long_veto;breakout_short_veto;hypo_side;trade_mode;" +
            "entry_price;sl_price;tp_price;sim_exit_kind;sim_pnl_ticks;sim_mae_ticks;sim_mfe_ticks;sim_bars;" +
            "signal_open;signal_high;signal_low;signal_close;signal_delta;signal_volume;vol_ratio;rth_open;rth_vwap;dist_vwap_ticks;above_vwap;vwap_slope_ticks;vwap_regime;" +
            "sd_p1;sd_m1;sd_p2;sd_m2;nearest_level;dist_nearest_ticks;flag_divergence;flag_absorption;flag_vol_surge;flag_in_zone;poc_price;break_trigger;break_ref_price;break_ref_delta;" +
            "max_ask_price;max_bid_price;top1_ask_ratio;top2_ask_ratio;top1_bid_ratio;top2_bid_ratio";

        private string GetShadowJournalFilePath()
        {
            if (!string.IsNullOrEmpty(_shadowJournalFilePath)) return _shadowJournalFilePath;
            try
            {
                string dir = GetJournalDirectory();
                Directory.CreateDirectory(dir);
                _shadowJournalFilePath = Path.Combine(dir, $"journal_shadow_{DateTime.Now:yyyy-MM-dd}.csv");
                EnsureShadowJournalHeader(_shadowJournalFilePath);
            }
            catch { _shadowJournalFilePath = null; }
            return _shadowJournalFilePath;
        }

        private void EnsureShadowJournalHeader(string path)
        {
            if (!File.Exists(path))
                File.WriteAllText(path, ShadowJournalHeader + Environment.NewLine, new UTF8Encoding(false));
        }

        private void ResetShadowState()
        {
            _shadowIdSeq = 0;
            _shadowJournalFilePath = null;
            _lastShadowBreakoutBar = -1;
            _lastShadowAbsorptionBar = -1;
        }

        private void LogBreakoutShadows(int evalBar, IndicatorCandle c, SetupSnapshot snap, string longVeto, string shortVeto)
        {
            if (!LogShadowVetos || evalBar == _lastShadowBreakoutBar) return;
            if (snap.VolRatio < LogShadowMinVolRatio) return;

            bool logged = false;
            if (TryAppendBreakoutShadow(evalBar, c, snap, SignalSide.Long, TradeMode.Breakout, longVeto, shortVeto))
                logged = true;
            if (TryAppendBreakoutShadow(evalBar, c, snap, SignalSide.Short, TradeMode.Breakout, longVeto, shortVeto))
                logged = true;

            if (logged)
                _lastShadowBreakoutBar = evalBar;
        }

        private bool TryAppendBreakoutShadow(int evalBar, IndicatorCandle c, SetupSnapshot snap,
            SignalSide side, TradeMode mode, string longVeto, string shortVeto)
        {
            string sideVeto = side == SignalSide.Long ? longVeto : shortVeto;
            if (string.IsNullOrEmpty(sideVeto)) return false;

            return AppendShadowRow(evalBar, c, snap, "Breakout", side, mode, snap.VetoReason, longVeto, shortVeto);
        }

        private void LogAbsorptionShadow(int evalBar, IndicatorCandle c, SetupPrimitives p, SetupSnapshot snap)
        {
            if (!LogShadowVetos || evalBar == _lastShadowAbsorptionBar) return;
            if (p.VolRatio < LogShadowMinVolRatio) return;
            if (!IsNearAnyTradingZone(c, Security?.TickSize ?? 0.25m)) return;
            if (!p.AbsorptionLong && !p.AbsorptionShort) return;

            bool logged = false;
            if (p.AbsorptionLong && TryInferAbsorptionHypothesis(c, p, Security?.TickSize ?? 0.25m, true, out var longMode))
            {
                if (AppendShadowRow(evalBar, c, snap, "Absorption", SignalSide.Long, longMode, snap.VetoReason, "", ""))
                    logged = true;
            }

            if (p.AbsorptionShort && TryInferAbsorptionHypothesis(c, p, Security?.TickSize ?? 0.25m, false, out var shortMode))
            {
                if (AppendShadowRow(evalBar, c, snap, "Absorption", SignalSide.Short, shortMode, snap.VetoReason, "", ""))
                    logged = true;
            }

            if (logged)
                _lastShadowAbsorptionBar = evalBar;
        }

        private bool TryInferAbsorptionHypothesis(IndicatorCandle c, SetupPrimitives p, decimal ts, bool wantLong, out TradeMode mode)
        {
            mode = TradeMode.MeanRevert;
            if (wantLong)
            {
                if (p.MeanRevertLong) { mode = TradeMode.MeanRevert; return true; }
                if (p.TrendLong && _ctx.VwapRegime == VwapRegime.TrendUp) { mode = TradeMode.Trend; return true; }
                if (p.AbsorptionLong) { mode = TradeMode.MeanRevert; return true; }
                return false;
            }

            if (p.MeanRevertShort) { mode = TradeMode.MeanRevert; return true; }
            if (p.TrendShort && _ctx.VwapRegime == VwapRegime.TrendDown) { mode = TradeMode.Trend; return true; }
            if (p.AbsorptionShort) { mode = TradeMode.MeanRevert; return true; }
            return false;
        }

        private bool IsNearAnyTradingZone(IndicatorCandle c, decimal ts)
        {
            if (ts <= 0m) return false;
            return TouchVwapZone(c, ts)
                || TouchSupportLevel(c, _ctx.SdMinus1, _ctx.HasSdBands, ts)
                || TouchSupportLevel(c, _ctx.SdMinus2, _ctx.HasSdBands, ts)
                || TouchResistanceLevel(c, _ctx.SdPlus1, _ctx.HasSdBands, ts)
                || TouchResistanceLevel(c, _ctx.SdPlus2, _ctx.HasSdBands, ts);
        }

        private bool AppendShadowRow(int evalBar, IndicatorCandle c, SetupSnapshot snap, string engine,
            SignalSide side, TradeMode mode, string vetoReason, string longVeto, string shortVeto)
        {
            decimal ts = Security?.TickSize ?? 0.25m;
            if (ts <= 0m) return false;

            var hypo = new SetupSnapshot { TradeMode = mode, Side = side };
            var (slTicks, tpTicks) = GetBracketTicksForSetup(hypo);
            decimal entry = c.Close;
            decimal sl = side == SignalSide.Long ? entry - slTicks * ts : entry + slTicks * ts;
            decimal tp = side == SignalSide.Long ? entry + tpTicks * ts : entry - tpTicks * ts;

            var sim = SimulateHypotheticalOutcome(evalBar, entry, sl, tp, side == SignalSide.Long, ts);

            try
            {
                string path = GetShadowJournalFilePath();
                if (string.IsNullOrEmpty(path)) return false;

                var inv = CultureInfo.InvariantCulture;
                int id = ++_shadowIdSeq;
                DateTime tsUtc;
                try { tsUtc = c.Time.ToUniversalTime(); }
                catch { tsUtc = DateTime.UtcNow; }

                string line = string.Join(";", new[]
                {
                    tsUtc.ToString("o", inv),
                    id.ToString(inv),
                    BacktestMode ? "BT" : "LIVE",
                    engine,
                    evalBar.ToString(inv),
                    vetoReason ?? "",
                    longVeto ?? "",
                    shortVeto ?? "",
                    side == SignalSide.Long ? "LONG" : "SHORT",
                    mode.ToString(),
                    entry.ToString("F2", inv),
                    sl.ToString("F2", inv),
                    tp.ToString("F2", inv),
                    sim.ExitKind,
                    sim.PnlTicks.ToString("F2", inv),
                    sim.MaeTicks.ToString("F2", inv),
                    sim.MfeTicks.ToString("F2", inv),
                    sim.BarsHeld.ToString(inv),
                    snap.SignalOpen.ToString("F2", inv),
                    snap.SignalHigh.ToString("F2", inv),
                    snap.SignalLow.ToString("F2", inv),
                    snap.SignalClose.ToString("F2", inv),
                    snap.SignalDelta.ToString("F0", inv),
                    snap.SignalVolume.ToString("F0", inv),
                    snap.VolRatio.ToString("F2", inv),
                    snap.RthOpen.ToString("F2", inv),
                    snap.RthVwap.ToString("F2", inv),
                    snap.DistVwapTicks.ToString("F1", inv),
                    snap.AboveVwap ? "1" : "0",
                    snap.VwapSlopeTicksPerBar.ToString("F3", inv),
                    snap.VwapRegime.ToString(),
                    snap.SdPlus1.ToString("F2", inv),
                    snap.SdMinus1.ToString("F2", inv),
                    snap.SdPlus2.ToString("F2", inv),
                    snap.SdMinus2.ToString("F2", inv),
                    snap.NearestLevel ?? "",
                    snap.DistNearestTicks.ToString("F1", inv),
                    snap.FlagDivergence ? "1" : "0",
                    snap.FlagAbsorption ? "1" : "0",
                    snap.FlagVolSurge ? "1" : "0",
                    snap.FlagInZone ? "1" : "0",
                    snap.PocPrice.ToString("F2", inv),
                    snap.BreakTrigger ?? "",
                    snap.BreakRefPrice.ToString("F2", inv),
                    snap.BreakRefDelta.ToString("F2", inv),
                    snap.MaxAskPrice.ToString("F2", inv),
                    snap.MaxBidPrice.ToString("F2", inv),
                    snap.Top1AskRatio.ToString("F4", inv),
                    snap.Top2AskRatio.ToString("F4", inv),
                    snap.Top1BidRatio.ToString("F4", inv),
                    snap.Top2BidRatio.ToString("F4", inv)
                });
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }

        private struct ShadowSimResult
        {
            public string ExitKind;
            public decimal PnlTicks;
            public decimal MaeTicks;
            public decimal MfeTicks;
            public int BarsHeld;
        }

        private ShadowSimResult SimulateHypotheticalOutcome(int entryBar, decimal entry, decimal sl, decimal tp, bool isLong, decimal ts)
        {
            var result = new ShadowSimResult { ExitKind = "Open", PnlTicks = 0m, MaeTicks = 0m, MfeTicks = 0m, BarsHeld = 0 };
            if (ts <= 0m) return result;

            int maxBar = Math.Min(CurrentBar, entryBar + ShadowMaxForwardBars);
            decimal mae = 0m;
            decimal mfe = 0m;

            for (int b = entryBar + 1; b <= maxBar; b++)
            {
                IndicatorCandle bar;
                try { bar = GetCandle(b); }
                catch { break; }
                if (bar == null) break;

                result.BarsHeld++;

                if (isLong)
                {
                    decimal adv = entry - bar.Low;
                    if (adv > mae) mae = adv;
                    decimal fav = bar.High - entry;
                    if (fav > mfe) mfe = fav;

                    if (bar.Low <= sl)
                    {
                        result.ExitKind = "StopLoss";
                        result.PnlTicks = (sl - entry) / ts;
                        result.MaeTicks = mae / ts;
                        result.MfeTicks = mfe / ts;
                        return result;
                    }
                    if (bar.High >= tp)
                    {
                        result.ExitKind = "TakeProfit";
                        result.PnlTicks = (tp - entry) / ts;
                        result.MaeTicks = mae / ts;
                        result.MfeTicks = mfe / ts;
                        return result;
                    }
                }
                else
                {
                    decimal adv = bar.High - entry;
                    if (adv > mae) mae = adv;
                    decimal fav = entry - bar.Low;
                    if (fav > mfe) mfe = fav;

                    if (bar.High >= sl)
                    {
                        result.ExitKind = "StopLoss";
                        result.PnlTicks = (entry - sl) / ts;
                        result.MaeTicks = mae / ts;
                        result.MfeTicks = mfe / ts;
                        return result;
                    }
                    if (bar.Low <= tp)
                    {
                        result.ExitKind = "TakeProfit";
                        result.PnlTicks = (entry - tp) / ts;
                        result.MaeTicks = mae / ts;
                        result.MfeTicks = mfe / ts;
                        return result;
                    }
                }
            }

            IndicatorCandle last;
            try { last = GetCandle(maxBar); }
            catch { last = null; }
            if (last != null)
            {
                result.ExitKind = "Timeout";
                decimal exit = last.Close;
                result.PnlTicks = isLong ? (exit - entry) / ts : (entry - exit) / ts;
            }

            result.MaeTicks = mae / ts;
            result.MfeTicks = mfe / ts;
            return result;
        }

        private static string FormatBreakoutVeto(string longVeto, string shortVeto)
        {
            if (string.IsNullOrEmpty(longVeto) && string.IsNullOrEmpty(shortVeto))
                return "no_breakout";
            return $"long={longVeto ?? "-"};short={shortVeto ?? "-"}";
        }
    }
}
