using System;

namespace AlgoOrderflow
{
    /// <summary>
    /// Couche Risk — PnL journalier (limites $) calculé sur ClosedPnL/OpenPnL ATAS.
    /// Aucune logique d'ordres ici : si limite atteinte → halt entries (le flatten est délégué à Safety).
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        private bool _dailyPnlBaselineReady;
        private bool _dailyLimitsHalted;
        private decimal _dailyPnlBaselineClosedPnL;
        private DateTime _dailyPnlLastCalendarDate = DateTime.MinValue;

        private void ResetRiskState()
        {
            _dailyPnlBaselineReady = false;
            _dailyLimitsHalted = false;
            _dailyPnlBaselineClosedPnL = 0m;
            _dailyPnlLastCalendarDate = DateTime.MinValue;
        }

        private decimal GetDailyPnLUsdApprox()
        {
            if (!_dailyPnlBaselineReady) return 0m;
            return (ClosedPnL - _dailyPnlBaselineClosedPnL) + OpenPnL;
        }

        private void TryResetDailyPnlBaseline(int bar)
        {
            if (bar < 0) return;

            if (DailyPnlResetUseTradingSession)
            {
                if (!_dailyPnlBaselineReady)
                {
                    _dailyPnlBaselineClosedPnL = ClosedPnL;
                    _dailyPnlBaselineReady = true;
                    _dailyLimitsHalted = false;
                    ResetConsecutiveLossCounter("init session");
                }
                else if (IsNewTradingSession(bar))
                {
                    _dailyPnlBaselineClosedPnL = ClosedPnL;
                    _dailyLimitsHalted = false;
                    ResetConsecutiveLossCounter("nouvelle session");
                    AddLog("PnL : nouvelle session, baseline remise à zéro");
                }
            }
            else
            {
                DateTime d;
                try { d = GetCandle(bar).Time.Date; }
                catch { return; }

                if (!_dailyPnlBaselineReady)
                {
                    _dailyPnlBaselineClosedPnL = ClosedPnL;
                    _dailyPnlLastCalendarDate = d;
                    _dailyPnlBaselineReady = true;
                    _dailyLimitsHalted = false;
                    ResetConsecutiveLossCounter("init jour");
                }
                else if (d != _dailyPnlLastCalendarDate)
                {
                    _dailyPnlBaselineClosedPnL = ClosedPnL;
                    _dailyPnlLastCalendarDate = d;
                    _dailyLimitsHalted = false;
                    ResetConsecutiveLossCounter("nouveau jour calendaire");
                    AddLog($"PnL : nouveau jour ({d:yyyy-MM-dd})");
                }
            }
        }

        private void TryEnforceDailyUsdLimits(int bar)
        {
            if (Portfolio == null || Security == null) return;
            if (MaxDailyGainUsd <= 0m && MaxDailyLossUsd <= 0m) return;
            if (!_dailyPnlBaselineReady || _dailyLimitsHalted) return;

            decimal pnl = GetDailyPnLUsdApprox();

            if (MaxDailyGainUsd > 0m && pnl >= MaxDailyGainUsd)
            {
                _dailyLimitsHalted = true;
                SafetyFlattenAndHalt($"Gain journalier {pnl:F2}$ >= {MaxDailyGainUsd:F2}$");
                return;
            }

            if (MaxDailyLossUsd > 0m && pnl <= -MaxDailyLossUsd)
            {
                _dailyLimitsHalted = true;
                SafetyFlattenAndHalt($"Perte journalière {pnl:F2}$ <= -{MaxDailyLossUsd:F2}$");
            }
        }
    }
}
