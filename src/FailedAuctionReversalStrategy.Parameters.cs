using System.ComponentModel.DataAnnotations;
using OFT.Attributes;
using ParameterAttribute = OFT.Attributes.ParameterAttribute;

namespace AlgoOrderflow
{
    public partial class FailedAuctionReversalStrategy
    {
        // ── Détection (cœur de l'edge) ──────────────────────────────────

        [Display(Name = "Seuil efficacité (ticks/unité)", GroupName = "Detection", Order = 1,
            Description = "Une bougie est inefficace si |close-open|_ticks / max(volume, |delta|) <= seuil.")]
        [Parameter]
        public decimal EfficiencyThreshold
        {
            get => _efficiencyThreshold;
            set => SetTradingParam(ref _efficiencyThreshold, value);
        }

        [Display(Name = "Delta inefficace min (abs)", GroupName = "Detection", Order = 2,
            Description = "|delta| absolu minimum pour qu'une bougie soit considérée agressive.")]
        [Parameter]
        public decimal DeltaIneffMinAbs
        {
            get => _deltaIneffMinAbs;
            set => SetTradingParam(ref _deltaIneffMinAbs, value);
        }

        [Display(Name = "Multiplicateur volume spike", GroupName = "Detection", Order = 3,
            Description = "Volume / SMA(volume, lookback) >= mult => spike.")]
        [Parameter]
        public decimal VolumeSpikeMult
        {
            get => _volumeSpikeMult;
            set => SetTradingParam(ref _volumeSpikeMult, value);
        }

        [Display(Name = "Ratio weak close max", GroupName = "Detection", Order = 4,
            Description = "|close - extreme_opp| / range <= ratio => clôture faible (rejet).")]
        [Parameter]
        public decimal WeakCloseRatio
        {
            get => _weakCloseRatio;
            set => SetTradingParam(ref _weakCloseRatio, value);
        }

        [Display(Name = "Proximité contexte (ticks)", GroupName = "Detection", Order = 5,
            Description = "Distance max (en ticks) à ONH/ONL/VWAP pour valider la proximité.")]
        [Parameter]
        public int ProximityTicks
        {
            get => _proximityTicks;
            set => SetTradingParam(ref _proximityTicks, value);
        }

        [Display(Name = "Lookback (bougies)", GroupName = "Detection", Order = 6,
            Description = "Nombre de bougies pour SMA volume et fenêtre statistique.")]
        [Parameter]
        public int LookbackBars
        {
            get => _lookbackBars;
            set => SetTradingParam(ref _lookbackBars, value);
        }

        [Display(Name = "Proximité directionnelle obligatoire", GroupName = "Detection", Order = 7,
            Description = "ON = la proximité au niveau ne compte que si elle va dans le sens du reversal (SHORT au-dessus, LONG en dessous). OFF = compte des deux côtés.")]
        [Parameter]
        public bool RequireDirectionalProximity
        {
            get => _requireDirectionalProximity;
            set => SetTradingParam(ref _requireDirectionalProximity, value);
        }

        [Display(Name = "Proximité contexte obligatoire", GroupName = "Detection", Order = 8,
            Description = "ON = pas de trade sans proximité d'un niveau (ONH/ONL/VWAP). OFF = trade possible sur les autres features seules.")]
        [Parameter]
        public bool RequireProximityForTrade
        {
            get => _requireProximityForTrade;
            set => SetTradingParam(ref _requireProximityForTrade, value);
        }

        // ── Scoring ──────────────────────────────────────────────────────

        [Display(Name = "Seuil score d'entrée", GroupName = "Scoring", Order = 10,
            Description = "Trade déclenché si total >= seuil.")]
        [Parameter]
        public decimal ScoreThreshold
        {
            get => _scoreThreshold;
            set => SetTradingParam(ref _scoreThreshold, value);
        }

        [Display(Name = "Poids — delta inefficace", GroupName = "Scoring", Order = 11)]
        [Parameter]
        public decimal WeightDeltaInefficiency
        {
            get => _wDelta;
            set => SetTradingParam(ref _wDelta, value);
        }

        [Display(Name = "Poids — weak close", GroupName = "Scoring", Order = 12)]
        [Parameter]
        public decimal WeightWeakClose
        {
            get => _wWeakClose;
            set => SetTradingParam(ref _wWeakClose, value);
        }

        [Display(Name = "Poids — volume spike", GroupName = "Scoring", Order = 13)]
        [Parameter]
        public decimal WeightVolumeSpike
        {
            get => _wVolume;
            set => SetTradingParam(ref _wVolume, value);
        }

        [Display(Name = "Poids — proximité contexte", GroupName = "Scoring", Order = 14)]
        [Parameter]
        public decimal WeightProximityContext
        {
            get => _wProximity;
            set => SetTradingParam(ref _wProximity, value);
        }

        // ── Bracket ─────────────────────────────────────────────────────

        [Display(Name = "Stop Loss (ticks)", GroupName = "Bracket", Order = 20,
            Description = "Distance du SL en ticks depuis le prix de fill.")]
        [Parameter]
        public int SLTicks
        {
            get => _slTicks;
            set => SetTradingParam(ref _slTicks, value);
        }

        [Display(Name = "Take Profit (ticks)", GroupName = "Bracket", Order = 21,
            Description = "Distance du TP en ticks depuis le prix de fill. RR = TP/SL.")]
        [Parameter]
        public int TPTicks
        {
            get => _tpTicks;
            set => SetTradingParam(ref _tpTicks, value);
        }

        [Display(Name = "Taille position (contrats)", GroupName = "Bracket", Order = 22,
            Description = "Taille fixe — pas de scaling, pas de martingale (cf. doc).")]
        [Parameter]
        public int PositionSize
        {
            get => _positionSize;
            set => SetTradingParam(ref _positionSize, value);
        }

        [Display(Name = "Valeur tick ($)", GroupName = "Bracket", Order = 23,
            Description = "ES = 12.50 ; MES = 1.25.")]
        [Parameter]
        public decimal TickValueUsd
        {
            get => _tickValueUsd;
            set => SetTradingParam(ref _tickValueUsd, value);
        }

        // ── Risque ──────────────────────────────────────────────────────

        [Display(Name = "Gain journalier max ($, 0 = off)", GroupName = "Risque", Order = 30)]
        [Parameter]
        public decimal MaxDailyGainUsd
        {
            get => _maxDailyGainUsd;
            set => SetTradingParam(ref _maxDailyGainUsd, value);
        }

        [Display(Name = "Perte journalière max ($, 0 = off)", GroupName = "Risque", Order = 31)]
        [Parameter]
        public decimal MaxDailyLossUsd
        {
            get => _maxDailyLossUsd;
            set => SetTradingParam(ref _maxDailyLossUsd, value);
        }

        [Display(Name = "Pertes consécutives max (0 = off)", GroupName = "Risque", Order = 32)]
        [Parameter]
        public int MaxConsecutiveLosses
        {
            get => _maxConsecutiveLosses;
            set => SetTradingParam(ref _maxConsecutiveLosses, value);
        }

        [Display(Name = "Reset PnL sur session", GroupName = "Risque", Order = 33,
            Description = "ON = baseline ré-initialisée à chaque session ; OFF = jour calendaire.")]
        [Parameter]
        public bool DailyPnlResetUseTradingSession
        {
            get => _dailyPnlResetUseTradingSession;
            set => SetTradingParam(ref _dailyPnlResetUseTradingSession, value);
        }

        [Display(Name = "Sécurité auto-flatten", GroupName = "Risque", Order = 34,
            Description = "Flatten automatique si désynchro broker détectée. Désactivé en backtest.")]
        [Parameter]
        public bool EnableSafetyFlatten
        {
            get => _enableSafetyFlatten;
            set => SetTradingParam(ref _enableSafetyFlatten, value);
        }

        // ── Backtest ────────────────────────────────────────────────────

        [Display(Name = "Mode backtest historique", GroupName = "Backtest", Order = 40,
            Description = "OFF = live. ON = simulation sur historique chargé du chart (pas d'ordre broker).")]
        [Parameter]
        public bool BacktestMode
        {
            get => _backtestMode;
            set => SetTradingParam(ref _backtestMode, value);
        }

        [Display(Name = "Slippage backtest (ticks)", GroupName = "Backtest", Order = 41,
            Description = "Slippage appliqué aux entrées MKT en backtest. Doc impose 1 à 2 ticks.")]
        [Parameter]
        public int SlippageTicksBacktest
        {
            get => _slippageTicksBacktest;
            set => SetTradingParam(ref _slippageTicksBacktest, value);
        }

        [Display(Name = "Rectangles SL/TP des trades", GroupName = "Backtest", Order = 42,
            Description = "Affiche un rectangle rouge (zone SL) et vert (zone TP) de la bougie d'entrée à la bougie de sortie. S'applique aux trades live et backtest.")]
        [Parameter]
        public bool ShowTradeRectangles
        {
            get => _showTradeRectangles;
            set => SetUiParam(ref _showTradeRectangles, value);
        }

        // ── Interface ───────────────────────────────────────────────────

        [Display(Name = "Lignes contexte (ONH/ONL/VWAP)", GroupName = "Interface", Order = 49,
            Description = "Affiche les niveaux utilisés par le scoring : ONH (cyan), ONL (orange), VWAP RTH (jaune). Historique visible en backtest.")]
        [Parameter]
        public bool ShowContextLevels
        {
            get => _showContextLevels;
            set => SetUiParam(ref _showContextLevels, value);
        }

        [Display(Name = "Afficher dashboard", GroupName = "Interface", Order = 50)]
        [Parameter]
        public bool ShowDashboard
        {
            get => _showDashboard;
            set => SetUiParam(ref _showDashboard, value);
        }

        [Display(Name = "Dashboard X", GroupName = "Interface", Order = 51)]
        [Parameter]
        public int DashX
        {
            get => _dashX;
            set => SetUiParam(ref _dashX, value);
        }

        [Display(Name = "Dashboard Y", GroupName = "Interface", Order = 52)]
        [Parameter]
        public int DashY
        {
            get => _dashY;
            set => SetUiParam(ref _dashY, value);
        }

        [Display(Name = "Taille police", GroupName = "Interface", Order = 53)]
        [Parameter]
        public int FontSize
        {
            get => _fontSize;
            set => SetUiParam(ref _fontSize, value);
        }

        // ── Horaires ────────────────────────────────────────────────────

        [Display(Name = "Filtre horaire actif", GroupName = "Horaires", Order = 60)]
        [Parameter]
        public bool UseTradingHoursFilter
        {
            get => _useTradingHoursFilter;
            set => SetTradingParam(ref _useTradingHoursFilter, value);
        }

        [Display(Name = "Session — début heure (NY)", GroupName = "Horaires", Order = 61)]
        [Parameter]
        public int SessionStartHour
        {
            get => _sessionStartHour;
            set => SetTradingParam(ref _sessionStartHour, value);
        }

        [Display(Name = "Session — début minute (NY)", GroupName = "Horaires", Order = 62)]
        [Parameter]
        public int SessionStartMinute
        {
            get => _sessionStartMinute;
            set => SetTradingParam(ref _sessionStartMinute, value);
        }

        [Display(Name = "Session — fin heure (NY)", GroupName = "Horaires", Order = 63)]
        [Parameter]
        public int SessionEndHour
        {
            get => _sessionEndHour;
            set => SetTradingParam(ref _sessionEndHour, value);
        }

        [Display(Name = "Session — fin minute (NY)", GroupName = "Horaires", Order = 64)]
        [Parameter]
        public int SessionEndMinute
        {
            get => _sessionEndMinute;
            set => SetTradingParam(ref _sessionEndMinute, value);
        }

        // ── Setters communs ─────────────────────────────────────────────

        private void SetTradingParam(ref int field, int value)
        {
            if (field == value) return;
            field = value;
            RecalculateValues();
        }

        private void SetTradingParam(ref bool field, bool value)
        {
            if (field == value) return;
            field = value;
            RecalculateValues();
        }

        private void SetTradingParam(ref decimal field, decimal value)
        {
            if (field == value) return;
            field = value;
            RecalculateValues();
        }

        private void SetUiParam(ref int field, int value)
        {
            if (field == value) return;
            field = value;
            RedrawChart();
        }

        private void SetUiParam(ref bool field, bool value)
        {
            if (field == value) return;
            field = value;
            RedrawChart();
        }
    }
}
