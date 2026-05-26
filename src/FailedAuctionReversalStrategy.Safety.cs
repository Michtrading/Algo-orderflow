using System;
using System.Linq;
using ATAS.DataFeedsCore;
using AlgoOrderflow.Models;

namespace AlgoOrderflow
{
    /// <summary>
    /// Couche Safety — kill switch (pertes consécutives) + flatten d'urgence + détection désynchro broker.
    /// EffectiveSafetyFlatten = EnableSafetyFlatten && !BacktestMode (jamais de flatten broker en backtest).
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        private int _consecutiveLossCount;
        private bool _consecutiveLossHalted;

        private bool EffectiveSafetyFlatten => EnableSafetyFlatten && !BacktestMode;

        private void ResetSafetyState()
        {
            _consecutiveLossCount = 0;
            _consecutiveLossHalted = false;
        }

        private void ResetConsecutiveLossCounter(string reason)
        {
            if (_consecutiveLossCount > 0 || _consecutiveLossHalted)
                AddLog($"Compteur pertes consécutives reset ({reason})");
            _consecutiveLossCount = 0;
            _consecutiveLossHalted = false;
        }

        /// <summary>Appelé après chaque sortie de trade pour maj le compteur de pertes consécutives.</summary>
        private void RegisterConsecutiveLossOnExit(TradeExitKind kind)
        {
            if (kind == TradeExitKind.TakeProfit)
            {
                _consecutiveLossCount = 0;
                return;
            }
            if (kind != TradeExitKind.StopLoss) return;

            _consecutiveLossCount++;
            if (MaxConsecutiveLosses > 0 && _consecutiveLossCount >= MaxConsecutiveLosses)
            {
                _consecutiveLossHalted = true;
                AddLog($"HALT : {_consecutiveLossCount} pertes consécutives (limite {MaxConsecutiveLosses})");
            }
        }

        /// <summary>Vérifications anti-désynchro (orphelin, position sans bracket, etc.). Désactivé en backtest.</summary>
        private void RunSafetyChecks()
        {
            if (!EffectiveSafetyFlatten) return;
            if (Portfolio == null || Security == null) return;

            try
            {
                // Position broker existante mais pas de bracket et pas de pending entry → désynchro
                if (CurrentPosition != 0
                    && _tradeState != TradeState.EntryPending
                    && _slOrder == null && _tpOrder == null
                    && !_bracketPending)
                {
                    SafetyFlattenAndHalt($"Position broker {CurrentPosition} sans bracket ni état interne");
                    return;
                }

                // Etat interne InTrade mais position broker = 0 → désynchro inverse
                if (CurrentPosition == 0
                    && _tradeState == TradeState.InTrade
                    && !_bracketPending)
                {
                    AddLog("Désynchro : state InTrade mais position broker 0 — finalisation");
                    FinalizeTradeOnPositionFlat();
                }
            }
            catch (Exception ex)
            {
                AddLog($"RunSafetyChecks ex: {ex.Message}");
            }
        }

        /// <summary>Flatten d'urgence + halt jusqu'à fin de session. Pas d'effet en backtest.</summary>
        private void SafetyFlattenAndHalt(string reason)
        {
            AddLog($"SAFETY HALT — {reason}");

            if (BacktestMode) return;
            if (Portfolio == null || Security == null) return;

            try
            {
                CancelAllManagedOrders();

                if (CurrentPosition != 0)
                {
                    OpenOrder(new Order
                    {
                        Portfolio = Portfolio,
                        Security = Security,
                        Direction = CurrentPosition > 0 ? OrderDirections.Sell : OrderDirections.Buy,
                        Type = OrderTypes.Market,
                        QuantityToFill = Math.Abs(CurrentPosition),
                        Comment = "FAR_SAFETY_FLATTEN"
                    });
                }
            }
            catch (Exception ex)
            {
                AddLog($"SafetyFlatten OpenOrder ex: {ex.Message}");
            }

            _dailyLimitsHalted = true;
            _consecutiveLossHalted = true;
        }
    }
}
