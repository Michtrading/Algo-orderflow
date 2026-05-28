namespace AlgoOrderflow.Models
{
    public class ContextSnapshot
    {
        public decimal OvernightHigh { get; set; }
        public decimal OvernightLow { get; set; }
        public decimal RthVwap { get; set; }
        public decimal RthStdev { get; set; }

        public decimal RthOpen { get; set; }
        public decimal SdPlus1 { get; set; }
        public decimal SdMinus1 { get; set; }
        public decimal SdPlus2 { get; set; }
        public decimal SdMinus2 { get; set; }

        public decimal VwapSlopeTicksPerBar { get; set; }
        public VwapRegime VwapRegime { get; set; }

        public bool HasOvernight { get; set; }
        public bool HasRthVwap { get; set; }
        public bool HasRthOpen { get; set; }
        public bool HasSdBands { get; set; }

        public decimal BarFormationSecondsAvg { get; set; }

        public decimal OprHigh { get; set; }
        public decimal OprLow { get; set; }
        public bool HasOpr { get; set; }

        public decimal PriorDayHigh { get; set; }
        public decimal PriorDayLow { get; set; }
        public decimal PriorDayClose { get; set; }
        public bool HasPriorDay { get; set; }
    }
}
