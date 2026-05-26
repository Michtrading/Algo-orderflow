using System;
using System.Collections.Generic;
using ATAS.Indicators;
using AlgoOrderflow.Models;

namespace AlgoOrderflow
{
    /// <summary>
    /// Couche Backtest — simulation interne sans broker, pilotée par le toggle BacktestMode.
    /// MKT à clôture de bougie filled avec slippage paramétré. SL/TP hits évalués sur la bougie suivante (c.Low/c.High).
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        /// <summary>Source unique des trades pour le rendu (live + backtest). Discriminés via TradeRecord.IsBacktest.</summary>
        private readonly List<TradeRecord> _visualTrades = new List<TradeRecord>();
        private int _visualTradeIdNext;

        // Stats backtest (rendu rapide dans le dashboard sans recalcul)
        private int _btClosed;
        private int _btTakeProfit;
        private int _btStopLoss;
        private int _btEntries;
        private decimal _btPnlTicks;

        public IReadOnlyList<TradeRecord> VisualTrades => _visualTrades;

        private void ResetBacktestState()
        {
            _visualTrades.Clear();
            _visualTradeIdNext = 0;
            _btClosed = _btTakeProfit = _btStopLoss = _btEntries = 0;
            _btPnlTicks = 0m;
        }

        /// <summary>Évalue les fills/hits sur la bougie qui vient de clore (bar - 1).</summary>
        private void SimulateBacktestBar(int bar, decimal ts)
        {
            if (bar < 1) return;

            IndicatorCandle c;
            try { c = GetCandle(bar - 1); }
            catch { return; }
            if (c == null) return;

            if (_tradeState == TradeState.InTrade)
            {
                int evalBar = bar - 1;
                UpdateMaeMfeBacktest(c);
                TrySimResolveOpenTrade(c, evalBar, ts);
            }
        }

        /// <summary>Suivi de l'excursion adverse/favorable pour la bougie en cours.</summary>
        private void UpdateMaeMfeBacktest(IndicatorCandle c)
        {
            if (_tradeIsLong)
            {
                if (c.Low < _entryPrice && (_entryPrice - c.Low) > _maeRunning)
                    _maeRunning = _entryPrice - c.Low;
                if (c.High > _entryPrice && (c.High - _entryPrice) > _mfeRunning)
                    _mfeRunning = c.High - _entryPrice;
            }
            else
            {
                if (c.High > _entryPrice && (c.High - _entryPrice) > _maeRunning)
                    _maeRunning = c.High - _entryPrice;
                if (c.Low < _entryPrice && (_entryPrice - c.Low) > _mfeRunning)
                    _mfeRunning = _entryPrice - c.Low;
            }
        }

        /// <summary>Détecte un hit SL ou TP sur la bougie évaluée et clôture le trade.</summary>
        private void TrySimResolveOpenTrade(IndicatorCandle c, int evalBar, decimal ts)
        {
            bool slHit = _tradeIsLong ? c.Low <= _slPrice : c.High >= _slPrice;
            bool tpHit = _tradeIsLong ? c.High >= _tpPrice : c.Low <= _tpPrice;

            if (!slHit && !tpHit) return;

            // Double hit dans la même bougie : conservateur, on prend le SL.
            TradeExitKind exitKind = slHit ? TradeExitKind.StopLoss : TradeExitKind.TakeProfit;
            decimal exitPrice = slHit ? _slPrice : _tpPrice;

            CloseTradeBacktest(exitKind, exitPrice, evalBar, ts);
        }

        /// <summary>Crée l'entrée simulée et pose immédiatement SL/TP (pas de pending puisque MKT).</summary>
        private void EnterTradeBacktest(int barNow, int evalBar, decimal ts, ScoreSnapshot score)
        {
            IndicatorCandle src;
            try { src = GetCandle(evalBar); }
            catch { return; }
            if (src == null) return;

            // Slippage : 1 à 2 ticks, dans le sens défavorable (paye le spread).
            decimal slipTicks = SlippageTicksBacktest;
            decimal slipAdj = slipTicks * ts;
            decimal fillPrice = _tradeIsLong ? src.Close + slipAdj : src.Close - slipAdj;
            fillPrice = NormalizeToTick(fillPrice, ts);

            _entryPrice = fillPrice;
            _slPrice = NormalizeToTick(_tradeIsLong
                ? fillPrice - SLTicks * ts
                : fillPrice + SLTicks * ts, ts);
            _tpPrice = NormalizeToTick(_tradeIsLong
                ? fillPrice + TPTicks * ts
                : fillPrice - TPTicks * ts, ts);

            _tradeState = TradeState.InTrade;
            _btEntries++;

            RegisterVisualTradeEntry(
                isBacktest: true,
                barNow: barNow,
                entryTimeUtc: src.Time.ToUniversalTime(),
                isLong: _tradeIsLong,
                fillPrice: fillPrice,
                slPrice: _slPrice,
                tpPrice: _tpPrice,
                slippageTicks: slipTicks,
                score: score);

            AddLog($"BT ENTRY {(_tradeIsLong ? "BUY" : "SELL")} @ {fillPrice:F2} " +
                   $"sl={_slPrice:F2} tp={_tpPrice:F2} score={score.Total:F2}");
        }

        private void CloseTradeBacktest(TradeExitKind kind, decimal exitPrice, int evalBar, decimal ts)
        {
            decimal pnlTicks = (_tradeIsLong ? (exitPrice - _entryPrice) : (_entryPrice - exitPrice)) / ts;
            decimal pnlUsd = pnlTicks * TickValueUsd * PositionSize;

            FinalizeVisualTradeExit(
                isBacktest: true,
                exitBar: evalBar,
                exitTimeUtc: TryGetBarTimeUtc(evalBar),
                exitPrice: exitPrice,
                kind: kind,
                pnlTicks: pnlTicks,
                pnlUsd: pnlUsd,
                mae: _maeRunning,
                mfe: _mfeRunning);

            _btClosed++;
            if (kind == TradeExitKind.TakeProfit) _btTakeProfit++;
            else if (kind == TradeExitKind.StopLoss) _btStopLoss++;
            _btPnlTicks += pnlTicks;

            RegisterConsecutiveLossOnExit(kind);

            AddLog($"BT EXIT {(_tradeIsLong ? "LONG" : "SHORT")} {kind} @ {exitPrice:F2} " +
                   $"pnl={pnlTicks:+0.0;-0.0}t ({pnlUsd:+0.00;-0.00}$)");

            _tradeState = TradeState.None;
            _entryScore = null;
            _maeRunning = 0m;
            _mfeRunning = 0m;
        }

        private DateTime TryGetBarTimeUtc(int bar)
        {
            try { return GetCandle(bar).Time.ToUniversalTime(); }
            catch { return DateTime.UtcNow; }
        }

        // ── API unifiée d'enregistrement visuel (utilisée par live ET backtest) ─

        private TradeRecord RegisterVisualTradeEntry(bool isBacktest, int barNow, DateTime entryTimeUtc,
            bool isLong, decimal fillPrice, decimal slPrice, decimal tpPrice,
            decimal slippageTicks, ScoreSnapshot score)
        {
            var rec = new TradeRecord
            {
                Id = ++_visualTradeIdNext,
                EntryTimeUtc = entryTimeUtc,
                EntryBar = barNow,
                ExitBar = -1,
                IsLong = isLong,
                EntryPrice = fillPrice,
                SlPrice = slPrice,
                TpPrice = tpPrice,
                ExitKind = TradeExitKind.Open,
                SlippageTicks = slippageTicks,
                Score = score,
                IsBacktest = isBacktest
            };
            _visualTrades.Add(rec);
            RedrawChart();
            return rec;
        }

        private void FinalizeVisualTradeExit(bool isBacktest, int exitBar, DateTime exitTimeUtc,
            decimal exitPrice, TradeExitKind kind, decimal pnlTicks, decimal pnlUsd, decimal mae, decimal mfe)
        {
            // Trouve le dernier trade ouvert correspondant
            for (int i = _visualTrades.Count - 1; i >= 0; i--)
            {
                var t = _visualTrades[i];
                if (t.IsBacktest != isBacktest) continue;
                if (t.ExitKind != TradeExitKind.Open) continue;

                t.ExitBar = exitBar;
                t.ExitTimeUtc = exitTimeUtc;
                t.ExitPrice = exitPrice;
                t.ExitKind = kind;
                t.PnlTicks = pnlTicks;
                t.PnlUsd = pnlUsd;
                t.Mae = mae;
                t.Mfe = mfe;
                AppendTradeJournal(t);
                RedrawChart();
                return;
            }
        }
    }
}
