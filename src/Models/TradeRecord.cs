using System;

namespace AlgoOrderflow.Models
{
    public enum TradeExitKind
    {
        Open = 0,
        TakeProfit = 1,
        StopLoss = 2,
        ManualClose = 3,
        SessionEnd = 4
    }

    public class TradeRecord
    {
        public int Id { get; set; }
        public DateTime EntryTimeUtc { get; set; }
        public int EntryBar { get; set; }
        public int ExitBar { get; set; } = -1;
        public DateTime ExitTimeUtc { get; set; }

        public bool IsLong { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal SlPrice { get; set; }
        public decimal TpPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public TradeExitKind ExitKind { get; set; } = TradeExitKind.Open;

        public decimal PnlTicks { get; set; }
        public decimal PnlUsd { get; set; }
        public decimal SlippageTicks { get; set; }

        public decimal Mae { get; set; }
        public decimal Mfe { get; set; }

        public ScoreSnapshot Score { get; set; }
        public bool IsBacktest { get; set; }
    }
}
