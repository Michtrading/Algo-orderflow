using System;
using ATAS.Indicators;

namespace AlgoOrderflow
{
    /// <summary>
    /// Filtre horaire — fenêtre RTH 09:30-11:00 NY (doc § SESSION).
    /// Détection nouvelle session pour reset du baseline PnL.
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        private static readonly TimeZoneInfo NyTimeZone = ResolveNyTimezone();

        private DateTime _lastSessionStartUtc = DateTime.MinValue;

        private static TimeZoneInfo ResolveNyTimezone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
            catch
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
                catch { return TimeZoneInfo.Utc; }
            }
        }

        private DateTime ToNy(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Unspecified)
                utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            try { return TimeZoneInfo.ConvertTimeFromUtc(utc.ToUniversalTime(), NyTimeZone); }
            catch { return utc; }
        }

        /// <summary>Vrai si le timestamp de la bougie est dans la fenêtre de trading paramétrée.</summary>
        private bool IsBarInTradingWindow(int bar)
        {
            if (!UseTradingHoursFilter)
                return true;

            IndicatorCandle c;
            try { c = GetCandle(bar); }
            catch { return false; }
            if (c == null) return false;

            var ny = ToNy(c.Time);
            int mins = ny.Hour * 60 + ny.Minute;
            int startMins = SessionStartHour * 60 + SessionStartMinute;
            int endMins = SessionEndHour * 60 + SessionEndMinute;

            if (startMins == endMins)
                return false;

            if (startMins < endMins)
                return mins >= startMins && mins < endMins;

            return mins >= startMins || mins < endMins;
        }

        /// <summary>Vrai au passage du seuil de session (transition hors → dans).</summary>
        private bool IsNewTradingSession(int bar)
        {
            try
            {
                var c = GetCandle(bar);
                if (c == null) return false;

                var nyNow = ToNy(c.Time);

                // Heuristique : nouvelle session si on franchit le start de session ET
                // que la dernière entrée enregistrée appartient à un jour calendaire NY antérieur.
                int startMins = SessionStartHour * 60 + SessionStartMinute;
                int curMins = nyNow.Hour * 60 + nyNow.Minute;

                bool justEnteredWindow = curMins == startMins;
                bool newDay = _lastSessionStartUtc == DateTime.MinValue
                              || ToNy(_lastSessionStartUtc).Date != nyNow.Date;

                if (justEnteredWindow && newDay)
                {
                    _lastSessionStartUtc = c.Time.ToUniversalTime();
                    return true;
                }
                return false;
            }
            catch { return false; }
        }
    }
}
