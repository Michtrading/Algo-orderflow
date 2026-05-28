using System.Globalization;

namespace AlgoOrderflow.Models
{
    public class SetupSnapshot
    {
        public bool IsValid { get; set; }
        public SignalSide Side { get; set; }
        public TradeMode TradeMode { get; set; }
        public string VetoReason { get; set; }

        public decimal SignalOpen { get; set; }
        public decimal SignalHigh { get; set; }
        public decimal SignalLow { get; set; }
        public decimal SignalClose { get; set; }
        public decimal SignalDelta { get; set; }
        public decimal SignalVolume { get; set; }
        public decimal VolRatio { get; set; }
        public decimal PocPrice { get; set; }

        public decimal RthOpen { get; set; }
        public decimal RthVwap { get; set; }
        public decimal DistVwapTicks { get; set; }
        public bool AboveVwap { get; set; }
        public decimal VwapSlopeTicksPerBar { get; set; }
        public VwapRegime VwapRegime { get; set; }

        public decimal SdPlus1 { get; set; }
        public decimal SdMinus1 { get; set; }
        public decimal SdPlus2 { get; set; }
        public decimal SdMinus2 { get; set; }

        public string NearestLevel { get; set; }
        public decimal DistNearestTicks { get; set; }

        public bool FlagVolSurge { get; set; }
        public bool FlagDivergence { get; set; }
        public bool FlagAbsorption { get; set; }
        public bool FlagInZone { get; set; }
        public bool FlagTrendAlignment { get; set; }

        /// <summary>Déclencheur micro (ex. swing_high_5, max_ask_1).</summary>
        public string BreakTrigger { get; set; }
        public decimal BreakRefPrice { get; set; }
        public decimal BreakRefDelta { get; set; }
        public decimal MaxAskPrice { get; set; }
        public decimal MaxBidPrice { get; set; }
        public decimal Top1AskRatio { get; set; }
        public decimal Top2AskRatio { get; set; }
        public decimal Top1BidRatio { get; set; }
        public decimal Top2BidRatio { get; set; }

        public string ToJournalExtension(bool legacyEngine)
        {
            var inv = CultureInfo.InvariantCulture;
            return string.Join(";", new[]
            {
                SignalOpen.ToString("F2", inv),
                SignalHigh.ToString("F2", inv),
                SignalLow.ToString("F2", inv),
                SignalClose.ToString("F2", inv),
                SignalDelta.ToString("F0", inv),
                SignalVolume.ToString("F0", inv),
                VolRatio.ToString("F2", inv),
                RthOpen.ToString("F2", inv),
                RthVwap.ToString("F2", inv),
                DistVwapTicks.ToString("F1", inv),
                AboveVwap ? "1" : "0",
                VwapSlopeTicksPerBar.ToString("F3", inv),
                VwapRegime.ToString(),
                SdPlus1.ToString("F2", inv),
                SdMinus1.ToString("F2", inv),
                SdPlus2.ToString("F2", inv),
                SdMinus2.ToString("F2", inv),
                NearestLevel ?? "",
                DistNearestTicks.ToString("F1", inv),
                TradeMode.ToString(),
                FlagDivergence ? "1" : "0",
                FlagAbsorption ? "1" : "0",
                FlagVolSurge ? "1" : "0",
                FlagInZone ? "1" : "0",
                PocPrice.ToString("F2", inv),
                legacyEngine ? "1" : "0",
                BreakTrigger ?? "",
                BreakRefPrice.ToString("F2", inv),
                BreakRefDelta.ToString("F2", inv),
                MaxAskPrice.ToString("F2", inv),
                MaxBidPrice.ToString("F2", inv),
                Top1AskRatio.ToString("F4", inv),
                Top2AskRatio.ToString("F4", inv),
                Top1BidRatio.ToString("F4", inv),
                Top2BidRatio.ToString("F4", inv)
            });
        }
    }
}
