using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AlgoOrderflow.Models;

namespace AlgoOrderflow
{
    public partial class FailedAuctionReversalStrategy
    {
        private const int LogBufferSize = 5;
        private readonly string[] _log = new string[LogBufferSize];
        private int _logIdx;
        private int _tradeIdSeq;

        private string _logFilePath;
        private string _journalFilePath;

        private const string JournalHeaderV3 =
            "timestamp_utc;trade_id;mode;side;entry_bar;exit_bar;entry_price;sl_price;tp_price;exit_price;exit_kind;pnl_ticks;pnl_usd;slippage_ticks;mae;mfe;score_total;score_components;" +
            "signal_open;signal_high;signal_low;signal_close;signal_delta;signal_volume;vol_ratio;rth_open;rth_vwap;dist_vwap_ticks;above_vwap;vwap_slope_ticks;vwap_regime;" +
            "sd_p1;sd_m1;sd_p2;sd_m2;nearest_level;dist_nearest_ticks;trade_mode;flag_divergence;flag_absorption;flag_vol_surge;flag_in_zone;poc_price;legacy_engine;break_trigger;break_ref_price;break_ref_delta;max_ask_price;max_bid_price;top1_ask_ratio;top2_ask_ratio;top1_bid_ratio;top2_bid_ratio";

        private void ResetLogState()
        {
            for (int i = 0; i < LogBufferSize; i++) _log[i] = null;
            _logIdx = 0;
            _tradeIdSeq = 0;
            _logFilePath = null;
            _journalFilePath = null;
            ResetShadowState();
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
            if (!File.Exists(path))
            {
                File.WriteAllText(path, JournalHeaderV3 + Environment.NewLine, new UTF8Encoding(false));
                return;
            }

            try
            {
                string firstLine = File.ReadLines(path).FirstOrDefault() ?? "";
                if (firstLine.Contains("top2_bid_ratio"))
                    return;

                string backup = path.Replace(".csv", firstLine.Contains("trade_mode") ? "_pre_v3.csv" : "_pre_v2.csv");
                if (File.Exists(backup))
                    File.Delete(backup);
                File.Move(path, backup);
                File.WriteAllText(path, JournalHeaderV3 + Environment.NewLine, new UTF8Encoding(false));
                AddLog($"Journal migré vers v3 (backup: {Path.GetFileName(backup)})");
            }
            catch
            {
                File.WriteAllText(path, JournalHeaderV3 + Environment.NewLine, new UTF8Encoding(false));
            }
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

            if (!BacktestMode)
            {
                try { RaiseShowNotification(message, level: Utils.Common.Logging.LoggingLevel.Info); }
                catch { }
            }
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
                bool legacyRow = rec.Setup == null && rec.Score != null;
                string ext = rec.Setup != null
                    ? rec.Setup.ToJournalExtension(false)
                    : EmptyJournalExtension(legacyRow);

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
                    rec.Score?.ToCsvComponents() ?? "0.00;0.00;0.00;0.00",
                    ext
                });
                File.AppendAllText(p, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }

        private static string EmptyJournalExtension(bool legacyEngine)
        {
            var parts = new string[36];
            for (int i = 0; i < parts.Length; i++) parts[i] = "0";
            parts[17] = "";
            parts[25] = legacyEngine ? "1" : "0";
            parts[26] = "";
            return string.Join(";", parts);
        }
    }
}
