using System.ComponentModel.DataAnnotations;
using OFT.Attributes;
using ParameterAttribute = OFT.Attributes.ParameterAttribute;

namespace AlgoOrderflow
{
    public partial class FailedAuctionReversalStrategy
    {
        // ── Moteur v2 ───────────────────────────────────────────────────

        [Display(Name = "Moteur legacy (ancien scoring)", GroupName = "Moteur", Order = 0,
            Description = "OFF = divergence + absorption + régime VWAP (v2). ON = ancien failed auction mono-bougie.")]
        [Parameter]
        public bool UseLegacyEngine
        {
            get => _useLegacyEngine;
            set => SetTradingParam(ref _useLegacyEngine, value);
        }

        [Display(Name = "Moteur absorption (v2)", GroupName = "Moteur", Order = 1,
            Description = "Divergence + absorption VPOC + zones VWAP/SD.")]
        [Parameter]
        public bool EnableAbsorptionEngine
        {
            get => _enableAbsorptionEngine;
            set => SetTradingParam(ref _enableAbsorptionEngine, value);
        }

        [Display(Name = "Moteur breakout structurel", GroupName = "Moteur", Order = 2,
            Description = "Initiation order flow : swing / max ask-bid, trend VWAP requis.")]
        [Parameter]
        public bool EnableBreakoutEngine
        {
            get => _enableBreakoutEngine;
            set => SetTradingParam(ref _enableBreakoutEngine, value);
        }

        // ── Setup v2 ────────────────────────────────────────────────────

        [Display(Name = "Lookback volume (bougies)", GroupName = "Setup", Order = 1,
            Description = "Moyenne volume sur les N bougies avant le signal.")]
        [Parameter]
        public int VolLookbackBars
        {
            get => _volLookbackBars;
            set => SetTradingParam(ref _volLookbackBars, value);
        }

        [Display(Name = "Ratio volume min", GroupName = "Setup", Order = 2,
            Description = "Volume signal / moyenne >= ratio (ex. 1.5, idéal ~2).")]
        [Parameter]
        public decimal VolRatioMin
        {
            get => _volRatioMin;
            set => SetTradingParam(ref _volRatioMin, value);
        }

        [Display(Name = "Delta min (abs)", GroupName = "Setup", Order = 3,
            Description = "|delta| minimum pour divergence ou alignement trend.")]
        [Parameter]
        public decimal DeltaMinAbs
        {
            get => _deltaMinAbs;
            set => SetTradingParam(ref _deltaMinAbs, value);
        }

        [Display(Name = "VPOC extrémité (ratio)", GroupName = "Setup", Order = 4,
            Description = "POC dans le tiers haut/bas de la bougie (0.65 = 65% du range).")]
        [Parameter]
        public decimal VpocExtremeRatio
        {
            get => _vpocExtremeRatio;
            set => SetTradingParam(ref _vpocExtremeRatio, value);
        }

        [Display(Name = "Zone proximité (ticks)", GroupName = "Setup", Order = 5,
            Description = "Distance max au niveau (VWAP, SD, ONH/ONL) pour valider le setup.")]
        [Parameter]
        public int ZoneProximityTicks
        {
            get => _zoneProximityTicks;
            set => SetTradingParam(ref _zoneProximityTicks, value);
        }

        // ── Régime ──────────────────────────────────────────────────────

        [Display(Name = "Pente VWAP lookback (barres)", GroupName = "Regime", Order = 10)]
        [Parameter]
        public int VwapSlopeLookback
        {
            get => _vwapSlopeLookback;
            set => SetTradingParam(ref _vwapSlopeLookback, value);
        }

        [Display(Name = "Pente VWAP trend (ticks/bar)", GroupName = "Regime", Order = 11)]
        [Parameter]
        public decimal VwapSlopeTrendThreshold
        {
            get => _vwapSlopeTrendThreshold;
            set => SetTradingParam(ref _vwapSlopeTrendThreshold, value);
        }

        [Display(Name = "Veto auction Open (ticks)", GroupName = "Regime", Order = 12,
            Description = "VWAP range + proche Open RTH => pas de trade sauf SD±2.")]
        [Parameter]
        public int AuctionOpenRangeTicks
        {
            get => _auctionOpenRangeTicks;
            set => SetTradingParam(ref _auctionOpenRangeTicks, value);
        }

        [Display(Name = "SD multiplicateur ±1", GroupName = "Regime", Order = 13)]
        [Parameter]
        public decimal SdMultiplier1
        {
            get => _sdMultiplier1;
            set => SetTradingParam(ref _sdMultiplier1, value);
        }

        [Display(Name = "SD multiplicateur ±2", GroupName = "Regime", Order = 14)]
        [Parameter]
        public decimal SdMultiplier2
        {
            get => _sdMultiplier2;
            set => SetTradingParam(ref _sdMultiplier2, value);
        }

        [Display(Name = "Autoriser MR short contre TrendUp", GroupName = "Regime", Order = 15,
            Description = "OFF recommandé: bloque les shorts mean-revert en TrendUp hors extrême SD+2.")]
        [Parameter]
        public bool AllowCounterTrendShortMeanRevert
        {
            get => _allowCounterTrendShortMeanRevert;
            set => SetTradingParam(ref _allowCounterTrendShortMeanRevert, value);
        }

        // ── Breakout ────────────────────────────────────────────────────

        [Display(Name = "Swing lookback (barres)", GroupName = "Breakout", Order = 20)]
        [Parameter]
        public int BreakSwingLookbackBars
        {
            get => _breakSwingLookbackBars;
            set => SetTradingParam(ref _breakSwingLookbackBars, value);
        }

        [Display(Name = "Volume node lookback (barres)", GroupName = "Breakout", Order = 21,
            Description = "Barres précédentes scannées pour MaxAsk (long) / MaxBid (short).")]
        [Parameter]
        public int BreakVolumeLookbackBars
        {
            get => _breakVolumeLookbackBars;
            set => SetTradingParam(ref _breakVolumeLookbackBars, value);
        }

        [Display(Name = "Ratio volume min", GroupName = "Breakout", Order = 22)]
        [Parameter]
        public decimal BreakVolRatioMin
        {
            get => _breakVolRatioMin;
            set => SetTradingParam(ref _breakVolRatioMin, value);
        }

        [Display(Name = "Delta min (abs)", GroupName = "Breakout", Order = 23)]
        [Parameter]
        public decimal BreakDeltaMinAbs
        {
            get => _breakDeltaMinAbs;
            set => SetTradingParam(ref _breakDeltaMinAbs, value);
        }

        [Display(Name = "Vélocité max (ratio vs moyenne)", GroupName = "Breakout", Order = 24,
            Description = "Durée barre <= ratio × moyenne session (ex. 0.65 = 35% plus rapide).")]
        [Parameter]
        public decimal BreakVelocityMaxRatio
        {
            get => _breakVelocityMaxRatio;
            set => SetTradingParam(ref _breakVelocityMaxRatio, value);
        }

        [Display(Name = "Acceptance (top/bottom ratio)", GroupName = "Breakout", Order = 25)]
        [Parameter]
        public decimal BreakAcceptanceTopRatio
        {
            get => _breakAcceptanceTopRatio;
            set => SetTradingParam(ref _breakAcceptanceTopRatio, value);
        }

        [Display(Name = "Confirmation cassure (ticks)", GroupName = "Breakout", Order = 26,
            Description = "Close au-delà du niveau de référence d'au moins X ticks.")]
        [Parameter]
        public int BreakConfirmTicks
        {
            get => _breakConfirmTicks;
            set => SetTradingParam(ref _breakConfirmTicks, value);
        }

        [Display(Name = "Filtre concentration top1/top2", GroupName = "Breakout", Order = 27,
            Description = "Exige une concentration du volume agressif sur 1-2 niveaux de prix.")]
        [Parameter]
        public bool BreakUseConcentrationFilter
        {
            get => _breakUseConcentrationFilter;
            set => SetTradingParam(ref _breakUseConcentrationFilter, value);
        }

        [Display(Name = "Concentration top1 min", GroupName = "Breakout", Order = 28,
            Description = "Part minimale du volume ask (long) ou bid (short) sur le niveau principal.")]
        [Parameter]
        public decimal BreakConcentrationTop1Min
        {
            get => _breakConcentrationTop1Min;
            set => SetTradingParam(ref _breakConcentrationTop1Min, value);
        }

        [Display(Name = "Concentration top2 min", GroupName = "Breakout", Order = 29,
            Description = "Part minimale cumulée des 2 premiers niveaux ask/bid.")]
        [Parameter]
        public decimal BreakConcentrationTop2Min
        {
            get => _breakConcentrationTop2Min;
            set => SetTradingParam(ref _breakConcentrationTop2Min, value);
        }

        [Display(Name = "Trigger swing H/L", GroupName = "Breakout", Order = 30)]
        [Parameter]
        public bool BreakUseSwing
        {
            get => _breakUseSwing;
            set => SetTradingParam(ref _breakUseSwing, value);
        }

        [Display(Name = "Trigger max ask/bid", GroupName = "Breakout", Order = 31)]
        [Parameter]
        public bool BreakUseVolumeNode
        {
            get => _breakUseVolumeNode;
            set => SetTradingParam(ref _breakUseVolumeNode, value);
        }

        [Display(Name = "Stop Loss (ticks)", GroupName = "Breakout", Order = 32)]
        [Parameter]
        public int BreakSlTicks
        {
            get => _breakSlTicks;
            set => SetTradingParam(ref _breakSlTicks, value);
        }

        [Display(Name = "Take Profit (ticks)", GroupName = "Breakout", Order = 33)]
        [Parameter]
        public int BreakTpTicks
        {
            get => _breakTpTicks;
            set => SetTradingParam(ref _breakTpTicks, value);
        }

        // ── Détection legacy ────────────────────────────────────────────

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
