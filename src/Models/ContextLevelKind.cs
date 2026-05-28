namespace AlgoOrderflow.Models
{
    public enum ContextLevelKind
    {
        OvernightHigh = 0,
        OvernightLow = 1,
        RthVwap = 2,
        SdPlus1 = 3,
        SdMinus1 = 4,
        SdPlus2 = 5,
        SdMinus2 = 6,
        RthOpen = 7,
        OprHigh = 8,
        OprLow = 9,
        PriorDayHigh = 10,
        PriorDayLow = 11,
        PriorDayClose = 12
    }
}
