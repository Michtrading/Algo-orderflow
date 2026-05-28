using System;
using System.ComponentModel;
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using ATAS.Strategies.Chart;
using AlgoOrderflow.Models;

namespace AlgoOrderflow
{
    /// <summary>
    /// Failed Auction / Trapped Trader Reversal — ES futures, range bars 8 ticks.
    /// Edge : agression inefficace (delta/volume sans progression) près d'un niveau contextuel (ONH/ONL/VWAP).
    /// Cf. master_research_document_ES_microstructure pour les invariants non négociables.
    /// </summary>
    [DisplayName("FailedAuctionReversal_ES")]
    public partial class FailedAuctionReversalStrategy : ChartStrategy
    {
        // ── Status / état général ────────────────────────────────────────
        private string _status = "Initialisation...";
        private int _lastBar = -1;
        private bool _wasLive;

        // ── État trade unique (pas de multi-niveau, taille fixe) ────────
        private TradeState _tradeState = TradeState.None;
        private bool _tradeIsLong;
        private decimal _entryPrice;
        private decimal _slPrice;
        private decimal _tpPrice;
        private int _entryBar = -1;
        private DateTime _entryTimeUtc = DateTime.MinValue;
        private ScoreSnapshot _entryScore;
        private SetupSnapshot _entrySetup;
        private string _lastVetoReason = "";
        private decimal _maeRunning;
        private decimal _mfeRunning;

        // Ordres ATAS (live)
        private Order _entryOrder;
        private Order _slOrder;
        private Order _tpOrder;
        private bool _bracketPending;

        // ── Backing fields paramètres ────────────────────────────────────
        private int _slTicks = 9;
        private int _tpTicks = 8;
        private int _positionSize = 1;
        private decimal _tickValueUsd = 12.50m;

        // Defaults relâchés pour la phase exploratoire (cf. doc § Phase 1)
        private decimal _efficiencyThreshold = 0.5m;
        private decimal _deltaIneffMinAbs = 200m;
        private decimal _volumeSpikeMult = 1.2m;
        private decimal _weakCloseRatio = 0.4m;
        private int _proximityTicks = 12;
        private int _lookbackBars = 20;
        private bool _requireDirectionalProximity;
        private bool _requireProximityForTrade;

        // Moteur v2 — divergence bougie + absorption VPOC + régime
        private bool _useLegacyEngine;
        private bool _enableAbsorptionEngine = true;
        private bool _enableBreakoutEngine;
        private int _volLookbackBars = 3;
        private decimal _volRatioMin = 1.5m;
        private decimal _deltaMinAbs = 50m;
        private decimal _vpocExtremeRatio = 0.65m;
        private int _zoneProximityTicks = 8;
        private int _vwapSlopeLookback = 20;
        private decimal _vwapSlopeTrendThreshold = 0.5m;
        private int _auctionOpenRangeTicks = 12;
        private decimal _sdMultiplier1 = 1m;
        private decimal _sdMultiplier2 = 2m;
        private bool _allowCounterTrendShortMeanRevert;

        private int _breakSwingLookbackBars = 5;
        private int _breakVolumeLookbackBars = 3;
        private decimal _breakVolRatioMin = 1.3m;
        private decimal _breakDeltaMinAbs = 35m;
        private decimal _breakVelocityMaxRatio = 0.90m;
        private decimal _breakAcceptanceTopRatio = 0.70m;
        private int _breakConfirmTicks = 0;
        private bool _breakUseConcentrationFilter;
        private decimal _breakConcentrationTop1Min = 0.30m;
        private decimal _breakConcentrationTop2Min = 0.50m;
        private bool _breakUseSwing = true;
        private bool _breakUseVolumeNode = true;
        private int _breakSlTicks = 9;
        private int _breakTpTicks = 8;

        private decimal _scoreThreshold = 3.5m;
        private decimal _wDelta = 3.0m;
        private decimal _wWeakClose = 2.0m;
        private decimal _wVolume = 1.0m;
        private decimal _wProximity = 2.0m;

        private decimal _maxDailyGainUsd = 1000m;
        private decimal _maxDailyLossUsd = 1000m;
        private int _maxConsecutiveLosses = 5;
        private bool _dailyPnlResetUseTradingSession = true;
        private bool _enableSafetyFlatten = true;

        private bool _backtestMode;
        private int _slippageTicksBacktest = 1;
        private bool _showTradeRectangles = true;
        private bool _showContextLevels = true;
        private byte _rectFillAlpha = 50;
        private byte _rectBorderAlpha = 180;

        private bool _useTradingHoursFilter = true;
        private int _sessionStartHour = 9;
        private int _sessionStartMinute = 30;
        private int _sessionEndHour = 11;
        private int _sessionEndMinute = 0;

        private bool _showDashboard = true;
        private int _dashX = 20;
        private int _dashY = 20;
        private int _fontSize = 11;

        private const int DashLabelCol = 235;

        public FailedAuctionReversalStrategy()
        {
            EnableCustomDrawing = true;
            SubscribeToDrawingEvents(DrawingLayouts.Final | DrawingLayouts.LatestBar | DrawingLayouts.Historical);

            var ds0 = (ValueDataSeries)DataSeries[0];
            ds0.IsHidden = true;
        }

        // ── Reset state ──────────────────────────────────────────────────

        private void ResetState()
        {
            CancelAllManagedOrders();

            _tradeState = TradeState.None;
            _entryOrder = null;
            _slOrder = null;
            _tpOrder = null;
            _bracketPending = false;
            _entryPrice = 0m;
            _slPrice = 0m;
            _tpPrice = 0m;
            _entryBar = -1;
            _entryTimeUtc = DateTime.MinValue;
            _entryScore = null;
            _entrySetup = null;
            _lastVetoReason = "";
            _maeRunning = 0m;
            _mfeRunning = 0m;

            _lastBar = -1;
            _wasLive = false;

            ResetContextState();
            ResetRiskState();
            ResetSafetyState();
            ResetBacktestState();
            ResetLogState();
            ResetDiagnosticCounters();
        }

        protected override void OnRecalculate()
        {
            ResetState();
            ReloadWeightsFromJson();
            base.OnRecalculate();
        }

        // ── Boucle principale ────────────────────────────────────────────

        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar < _lookbackBars + 2)
                return;

            var ts = Security?.TickSize ?? 0.25m;
            bool canProcessBar = CanProcess(bar);
            bool processBar = canProcessBar || BacktestMode;

            UpdateContext(bar);

            if (!processBar)
            {
                if (!BacktestMode)
                    _wasLive = false;
                return;
            }

            if (BacktestMode && bar == _lastBar)
                return;

            if (!BacktestMode && !_wasLive)
                _wasLive = true;

            if (bar == _lastBar)
                return;

            _lastBar = bar;

            if (!BacktestMode)
            {
                TryResetDailyPnlBaseline(bar);
                TryEnforceDailyUsdLimits(bar);
            }

            bool tradingHoursOk = IsBarInTradingWindow(bar);

            if (EffectiveSafetyFlatten)
                RunSafetyChecks();

            if (BacktestMode)
                SimulateBacktestBar(bar, ts);

            if (tradingHoursOk
                && _tradeState == TradeState.None
                && (BacktestMode || (!_dailyLimitsHalted && !_consecutiveLossHalted)))
            {
                TryEvaluateAndEnter(bar, ts);
            }

            UpdateStatusString(tradingHoursOk);
        }

        private void UpdateStatusString(bool tradingHoursOk)
        {
            if (BacktestMode)
            {
                _status = $"Backtest trades:{_btClosed} TP:{_btTakeProfit} SL:{_btStopLoss} ticks:{_btPnlTicks:+0;-0}";
                return;
            }
            if (_dailyLimitsHalted)
                _status = "Limites journalieres HALT";
            else if (_consecutiveLossHalted)
                _status = "Pertes consecutives HALT";
            else if (!tradingHoursOk)
                _status = "Hors fenetre horaire";
            else if (_tradeState == TradeState.InTrade)
                _status = $"In trade {(_tradeIsLong ? "LONG" : "SHORT")} @ {_entryPrice:F2}";
            else if (!UseLegacyEngine && !string.IsNullOrEmpty(_lastVetoReason))
                _status = $"Pret ({_ctx.VwapRegime})";
            else
                _status = "Pret";
        }

        // ── Hooks broker ─────────────────────────────────────────────────

        protected override void OnCurrentPositionChanged()
        {
            base.OnCurrentPositionChanged();

            if (BacktestMode)
                return;
            if (Portfolio == null || Security == null)
                return;

            if (CurrentPosition != 0 && _bracketPending)
                TryPlaceBracketsLive();

            if (CurrentPosition == 0 && _tradeState != TradeState.None)
                FinalizeTradeOnPositionFlat();
        }

        protected override void OnStopping()
        {
            _wasLive = false;
            _lastBar = -1;

            CancelAllManagedOrders();

            if (!BacktestMode && CurrentPosition != 0 && Portfolio != null && Security != null)
            {
                OpenOrder(new Order
                {
                    Portfolio = Portfolio,
                    Security = Security,
                    Direction = CurrentPosition > 0 ? OrderDirections.Sell : OrderDirections.Buy,
                    Type = OrderTypes.Market,
                    QuantityToFill = Math.Abs(CurrentPosition)
                });
            }

            base.OnStopping();
        }

        // ── Évaluation et déclenchement ─────────────────────────────────

        private void TryEvaluateAndEnter(int bar, decimal ts)
        {
            int evalBar = bar - 1;
            int minBars = UseLegacyEngine
                ? _lookbackBars + 1
                : Math.Max(_lookbackBars, VolLookbackBars) + 1;
            if (evalBar < minBars)
                return;

            if (UseLegacyEngine)
            {
                var score = ComputeScore(evalBar, ts);
                if (score == null || score.Side == SignalSide.None)
                    return;
                if (score.Total < ScoreThreshold)
                    return;
                EnterTrade(bar, evalBar, ts, score, null);
                return;
            }

            var setup = (SetupSnapshot)null;

            if (!UseLegacyEngine && !EnableAbsorptionEngine && !EnableBreakoutEngine)
            {
                setup = null;
                return;
            }

            if (EnableBreakoutEngine)
            {
                setup = EvaluateBreakoutSetup(evalBar, ts);
                if (setup != null && !string.IsNullOrEmpty(setup.VetoReason))
                {
                    if (setup.VetoReason.StartsWith("breakout_", StringComparison.Ordinal)
                        && setup.VetoReason != _lastVetoReason)
                        AddLog($"BREAKOUT VETO {setup.VetoReason}");
                    _lastVetoReason = setup.VetoReason;
                }
            }

            if ((setup == null || !setup.IsValid) && EnableAbsorptionEngine)
            {
                setup = EvaluateSetupV2(evalBar, ts);
                if (setup != null && !string.IsNullOrEmpty(setup.VetoReason))
                {
                    if (!setup.VetoReason.StartsWith("breakout_", StringComparison.Ordinal)
                        && setup.VetoReason != _lastVetoReason)
                        AddLog($"ABSORPTION VETO {setup.VetoReason}");
                    _lastVetoReason = setup.VetoReason;
                }
            }

            if (setup == null || !setup.IsValid || setup.Side == SignalSide.None)
                return;

            EnterTrade(bar, evalBar, ts, null, setup);
        }
    }
}
