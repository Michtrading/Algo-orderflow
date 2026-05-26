using System;
using System.Globalization;
using System.IO;
using System.Text;
using AlgoOrderflow.Models;

namespace AlgoOrderflow
{
    /// <summary>
    /// Couche Log + Journal — buffer circulaire en mémoire pour le dashboard,
    /// + journal CSV append-only par jour pour l'analyse post-session (doc § DATA STORAGE).
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        private const int LogBufferSize = 5;
        private readonly string[] _log = new string[LogBufferSize];
        private int _logIdx;
        private int _tradeIdSeq;

        private string _logFilePath;
        private string _journalFilePath;

        private void ResetLogState()
        {
            for (int i = 0; i < LogBufferSize; i++) _log[i] = null;
            _logIdx = 0;
            _tradeIdSeq = 0;
            _logFilePath = null;
            _journalFilePath = null;
        }

        private string GetLogFilePath()
        {
            if (!string.IsNullOrEmpty(_logFilePath)) return _logFilePath;
            try
            {
                string dir = GetJournalDirectory();
                Directory.CreateDirectory(dir);
                string fname = $"log_{DateTime.Now:yyyy-MM-dd}.txt";
                _logFilePath = Path.Combine(dir, fname);
            }
            catch { _logFilePath = null; }
            return _logFilePath;
        }

        private string GetJournalFilePath()
        {
            if (!string.IsNullOrEmpty(_journalFilePath)) return _journalFilePath;
            try
            {
                string dir = GetJournalDirectory();
                Directory.CreateDirectory(dir);
                string fname = $"journal_{DateTime.Now:yyyy-MM-dd}.csv";
                _journalFilePath = Path.Combine(dir, fname);
                EnsureJournalHeader(_journalFilePath);
            }
            catch { _journalFilePath = null; }
            return _journalFilePath;
        }

        private string GetJournalDirectory()
        {
            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appdata, "ATAS", "Strategies", "AlgoOrderflow");
        }

        private void EnsureJournalHeader(string path)
        {
            if (File.Exists(path)) return;
            const string header = "timestamp_utc;trade_id;mode;side;entry_bar;exit_bar;entry_price;sl_price;tp_price;exit_price;exit_kind;pnl_ticks;pnl_usd;slippage_ticks;mae;mfe;score_total;score_components";
            File.WriteAllText(path, header + Environment.NewLine, new UTF8Encoding(false));
        }

        internal void AddLog(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            string stamped = $"{DateTime.Now:HH:mm:ss} {message}";

            int slot = _logIdx % LogBufferSize;
            _log[slot] = stamped;
            _logIdx++;

            try
            {
                string p = GetLogFilePath();
                if (!string.IsNullOrEmpty(p))
                    File.AppendAllText(p, stamped + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }

            try { RaiseShowNotification(message, level: Utils.Common.Logging.LoggingLevel.Info); }
            catch { }
        }

        internal int NextTradeId() => ++_tradeIdSeq;

        internal void AppendTradeJournal(TradeRecord rec)
        {
            if (rec == null) return;
            try
            {
                string p = GetJournalFilePath();
                if (string.IsNullOrEmpty(p)) return;

                var inv = CultureInfo.InvariantCulture;
                string line = string.Join(";", new[]
                {
                    rec.EntryTimeUtc.ToString("o", inv),
                    rec.Id.ToString(inv),
                    rec.IsBacktest ? "BT" : "LIVE",
                    rec.IsLong ? "LONG" : "SHORT",
                    rec.EntryBar.ToString(inv),
                    rec.ExitBar.ToString(inv),
                    rec.EntryPrice.ToString("F2", inv),
                    rec.SlPrice.ToString("F2", inv),
                    rec.TpPrice.ToString("F2", inv),
                    rec.ExitPrice.ToString("F2", inv),
                    rec.ExitKind.ToString(),
                    rec.PnlTicks.ToString("F2", inv),
                    rec.PnlUsd.ToString("F2", inv),
                    rec.SlippageTicks.ToString("F2", inv),
                    rec.Mae.ToString("F2", inv),
                    rec.Mfe.ToString("F2", inv),
                    rec.Score?.Total.ToString("F2", inv) ?? "0.00",
                    rec.Score?.ToCsvComponents() ?? ""
                });
                File.AppendAllText(p, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
