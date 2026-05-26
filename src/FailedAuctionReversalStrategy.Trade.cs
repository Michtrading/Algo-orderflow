using System;
using System.Linq;
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using AlgoOrderflow.Models;

namespace AlgoOrderflow
{
    /// <summary>
    /// Couche Trade — exécution live : MKT à la clôture de la bougie, bracket (SL stop + TP limit) après fill.
    /// Pas de scaling, pas de pyramiding, pas de trailing (doc § RISK MANAGEMENT, TARGET MODEL).
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        /// <summary>Point d'entrée appelé par OnCalculate quand le score franchit le seuil.</summary>
        private void EnterTrade(int barNow, int evalBar, decimal ts, ScoreSnapshot score)
        {
            if (_tradeState != TradeState.None)
                return;

            _tradeIsLong = score.Side == SignalSide.Long;
            _entryBar = barNow;
            _entryTimeUtc = DateTime.UtcNow;
            _entryScore = score;
            _maeRunning = 0m;
            _mfeRunning = 0m;

            if (BacktestMode)
            {
                EnterTradeBacktest(barNow, evalBar, ts, score);
                return;
            }

            if (Portfolio == null || Security == null)
            {
                AddLog("EnterTrade abandonné : Portfolio/Security null");
                _tradeState = TradeState.None;
                _entryScore = null;
                return;
            }

            var dir = _tradeIsLong ? OrderDirections.Buy : OrderDirections.Sell;
            _entryOrder = new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Direction = dir,
                Type = OrderTypes.Market,
                QuantityToFill = PositionSize,
                Comment = $"FAR_ENTRY_{barNow}"
            };

            try
            {
                _tradeState = TradeState.EntryPending;
                _bracketPending = true;
                OpenOrder(_entryOrder);
                AddLog($"ENTRY MKT {(_tradeIsLong ? "BUY" : "SELL")} {PositionSize}c " +
                       $"score={score.Total:F2} ({score.ToCsvComponents()})");
            }
            catch (Exception ex)
            {
                AddLog($"OpenOrder MKT entrée: ex={ex.Message}");
                _tradeState = TradeState.None;
                _entryOrder = null;
                _bracketPending = false;
                _entryScore = null;
            }
        }

        /// <summary>Pose le bracket SL+TP une fois la position confirmée (avg fill connu).</summary>
        private void TryPlaceBracketsLive()
        {
            if (BacktestMode) return;
            if (CurrentPosition == 0) return;
            if (!_bracketPending) return;
            if (_slOrder != null || _tpOrder != null) return;

            decimal fillPrice = AveragePrice > 0m ? AveragePrice : 0m;
            if (fillPrice <= 0m) return;

            var ts = Security?.TickSize ?? 0.25m;
            _entryPrice = fillPrice;
            _slPrice = NormalizeToTick(_tradeIsLong
                ? fillPrice - SLTicks * ts
                : fillPrice + SLTicks * ts, ts);
            _tpPrice = NormalizeToTick(_tradeIsLong
                ? fillPrice + TPTicks * ts
                : fillPrice - TPTicks * ts, ts);

            // Garde-fous : SL/TP du bon côté du fill
            if (_tradeIsLong && (_slPrice >= fillPrice || _tpPrice <= fillPrice))
            {
                SafetyFlattenAndHalt($"Bracket LONG invalide fill={fillPrice:F2} sl={_slPrice:F2} tp={_tpPrice:F2}");
                return;
            }
            if (!_tradeIsLong && (_slPrice <= fillPrice || _tpPrice >= fillPrice))
            {
                SafetyFlattenAndHalt($"Bracket SHORT invalide fill={fillPrice:F2} sl={_slPrice:F2} tp={_tpPrice:F2}");
                return;
            }

            var exit = _tradeIsLong ? OrderDirections.Sell : OrderDirections.Buy;
            _slOrder = PlaceStop(exit, _slPrice, PositionSize, $"FAR_SL_{_entryBar}");
            _tpOrder = PlaceLimit(exit, _tpPrice, PositionSize, $"FAR_TP_{_entryBar}");

            _tradeState = TradeState.InTrade;
            _bracketPending = false;
            AddLog($"BRACKET {(_tradeIsLong ? "LONG" : "SHORT")} " +
                   $"fill={fillPrice:F2} sl={_slPrice:F2} tp={_tpPrice:F2}");

            RegisterVisualTradeEntry(
                isBacktest: false,
                barNow: _entryBar,
                entryTimeUtc: _entryTimeUtc,
                isLong: _tradeIsLong,
                fillPrice: fillPrice,
                slPrice: _slPrice,
                tpPrice: _tpPrice,
                slippageTicks: 0m,
                score: _entryScore);
        }

        /// <summary>Quand la position passe à 0, on conclut le trade et on journalise.</summary>
        private void FinalizeTradeOnPositionFlat()
        {
            if (BacktestMode) return;
            if (_tradeState == TradeState.None) return;

            decimal exitPrice = AveragePrice > 0m ? AveragePrice : _entryPrice;
            TradeExitKind kind = TradeExitKind.ManualClose;

            // Heuristique : si SL filled → StopLoss, si TP filled → TakeProfit.
            if (_slOrder != null && _slOrder.State == OrderStates.Done)
                kind = TradeExitKind.StopLoss;
            else if (_tpOrder != null && _tpOrder.State == OrderStates.Done)
                kind = TradeExitKind.TakeProfit;

            CancelLiveBracketOrders();

            var ts = Security?.TickSize ?? 0.25m;
            decimal pnlTicks = (_tradeIsLong ? (exitPrice - _entryPrice) : (_entryPrice - exitPrice)) / ts;
            decimal pnlUsd = pnlTicks * TickValueUsd * PositionSize;

            FinalizeVisualTradeExit(
                isBacktest: false,
                exitBar: _lastBar,
                exitTimeUtc: DateTime.UtcNow,
                exitPrice: exitPrice,
                kind: kind,
                pnlTicks: pnlTicks,
                pnlUsd: pnlUsd,
                mae: _maeRunning,
                mfe: _mfeRunning);

            RegisterConsecutiveLossOnExit(kind);

            AddLog($"EXIT {(_tradeIsLong ? "LONG" : "SHORT")} {kind} " +
                   $"@ {exitPrice:F2} pnl={pnlTicks:+0.0;-0.0}t ({pnlUsd:+0.00;-0.00}$)");

            _tradeState = TradeState.None;
            _entryOrder = null;
            _slOrder = null;
            _tpOrder = null;
            _bracketPending = false;
            _entryScore = null;
            _maeRunning = 0m;
            _mfeRunning = 0m;
        }

        // ── Helpers ordres ──────────────────────────────────────────────

        private Order PlaceLimit(OrderDirections dir, decimal price, decimal qty, string comment)
        {
            var ts = Security?.TickSize ?? 0.25m;
            var o = new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Direction = dir,
                Type = OrderTypes.Limit,
                Price = NormalizeToTick(price, ts),
                QuantityToFill = qty,
                Comment = comment,
                AutoCancel = true
            };
            OpenOrder(o);
            return o;
        }

        private Order PlaceStop(OrderDirections dir, decimal price, decimal qty, string comment)
        {
            var ts = Security?.TickSize ?? 0.25m;
            var p = NormalizeToTick(price, ts);
            var o = new Order
            {
                Portfolio = Portfolio,
                Security = Security,
                Direction = dir,
                Type = OrderTypes.Stop,
                TriggerPrice = p,
                Price = p,
                QuantityToFill = qty,
                Comment = comment,
                AutoCancel = true
            };
            OpenOrder(o);
            return o;
        }

        private void TryCancel(Order o)
        {
            if (o == null) return;
            try
            {
                if (o.State == OrderStates.Active)
                    CancelOrder(o);
            }
            catch { }
        }

        private void CancelLiveBracketOrders()
        {
            TryCancel(_slOrder);
            TryCancel(_tpOrder);
        }

        private void CancelAllManagedOrders()
        {
            TryCancel(_entryOrder);
            TryCancel(_slOrder);
            TryCancel(_tpOrder);

            if (TradingManager?.Orders == null || Security == null)
                return;
            foreach (var o in TradingManager.Orders.ToList())
            {
                if (o.State != OrderStates.Active) continue;
                if (o.Security?.Code != Security.Code) continue;
                if (string.IsNullOrEmpty(o.Comment)) continue;
                if (!o.Comment.StartsWith("FAR_", StringComparison.Ordinal)) continue;
                try { CancelOrder(o); } catch { }
            }
        }

        private static decimal NormalizeToTick(decimal price, decimal ts)
        {
            if (ts <= 0m) return price;
            return Math.Round(price / ts, MidpointRounding.AwayFromZero) * ts;
        }
    }
}
