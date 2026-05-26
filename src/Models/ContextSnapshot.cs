namespace AlgoOrderflow.Models
{
    public class ContextSnapshot
    {
        public decimal OvernightHigh { get; set; }
        public decimal OvernightLow { get; set; }
        public decimal RthVwap { get; set; }

        public bool HasOvernight { get; set; }
        public bool HasRthVwap { get; set; }

        public decimal BarFormationSecondsAvg { get; set; }
    }
}
