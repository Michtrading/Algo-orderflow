using System;
using System.Drawing;
using System.IO;
using ATAS.Indicators;
using ATAS.Strategies.Chart;
using AlgoOrderflow.Models;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using Rectangle = System.Drawing.Rectangle;

namespace AlgoOrderflow
{
    /// <summary>
    /// Couche Render — dashboard + rectangles SL/TP des trades.
    /// Pattern issu du doc : « green rectangle = target, red rectangle = stop ».
    /// </summary>
    public partial class FailedAuctionReversalStrategy
    {
        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            try
            {
                if (ShowContextLevels)
                    RenderContextLevels(context);

                if (ShowTradeRectangles)
                    RenderTradeRectangles(context);

                if (ShowDashboard)
                    RenderDashboard(context);
            }
            catch { }
        }

        /// <summary>
        /// Niveaux contextuels du doc : ONH, ONL (lignes horizontales par session) + VWAP RTH (courbe).
        /// </summary>
        private void RenderContextLevels(RenderContext context)
        {
            if (ChartInfo == null) return;

            int firstVisible = FirstVisibleBarNumber;
            int lastVisible = LastVisibleBarNumber;
            int rightX = (int)context.ClipBounds.Right;
            var fLbl = new RenderFont("Consolas", Math.Max(9, FontSize - 1));

            RenderLevelSegments(context, _onhSegments, firstVisible, lastVisible, rightX, fLbl,
                "ONH", Color.FromArgb(220, 0, 200, 255), dashed: true);

            RenderLevelSegments(context, _onlSegments, firstVisible, lastVisible, rightX, fLbl,
                "ONL", Color.FromArgb(220, 255, 140, 0), dashed: true);

            RenderVwapTrail(context, firstVisible, lastVisible, fLbl);
        }

        private void RenderLevelSegments(RenderContext context, System.Collections.Generic.IReadOnlyList<ContextLevelSegment> segments,
            int firstVisible, int lastVisible, int rightX, RenderFont fLbl, string label, Color color, bool dashed)
        {
            if (segments == null || segments.Count == 0) return;

            var pen = new RenderPen(color, dashed ? 1 : 2);
            if (dashed)
                pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;

            foreach (var seg in segments)
            {
                int x1 = ChartInfo.GetXByBar(seg.FromBar);
                int toBar = seg.ToBar >= 0 ? seg.ToBar : lastVisible;
                int x2 = ChartInfo.GetXByBar(toBar);
                if (x2 <= 0) x2 = rightX;
                if (x1 < 0 && x2 < 0) continue;
                if (toBar < firstVisible || seg.FromBar > lastVisible) continue;

                int y = ChartInfo.GetYByPrice(seg.Price);
                if (y <= 0) continue;

                if (x1 < 0) x1 = 0;
                if (x2 < x1) x2 = x1 + 40;

                context.DrawLine(pen, x1, y, x2, y);
                context.DrawString($"{label} {seg.Price:F2}", fLbl, color, x2 - 72, y - 14);
            }
        }

        private void RenderVwapTrail(RenderContext context, int firstVisible, int lastVisible, RenderFont fLbl)
        {
            if (_vwapTrail == null || _vwapTrail.Count < 2) return;

            var color = Color.FromArgb(220, 255, 220, 0);
            var pen = new RenderPen(color, 2);

            int? prevX = null;
            int? prevY = null;

            foreach (var pt in _vwapTrail)
            {
                if (pt.Bar < firstVisible - 2 || pt.Bar > lastVisible + 2) continue;

                int x = ChartInfo.GetXByBar(pt.Bar);
                int y = ChartInfo.GetYByPrice(pt.Vwap);
                if (x < 0 || y <= 0) continue;

                if (prevX.HasValue && prevY.HasValue)
                    context.DrawLine(pen, prevX.Value, prevY.Value, x, y);

                prevX = x;
                prevY = y;
            }

            var last = _vwapTrail[_vwapTrail.Count - 1];
            if (last.Bar >= firstVisible && last.Bar <= lastVisible + 2)
            {
                int lx = ChartInfo.GetXByBar(last.Bar);
                int ly = ChartInfo.GetYByPrice(last.Vwap);
                if (lx >= 0 && ly > 0)
                    context.DrawString($"VWAP {last.Vwap:F2}", fLbl, color, lx + 4, ly - 14);
            }
        }

        /// <summary>
        /// Pour chaque trade : 2 rectangles (SL rouge + TP vert) de la bougie d'entrée à la bougie de sortie.
        /// Trades encore ouverts → rectangle étiré jusqu'à la dernière bougie visible.
        /// </summary>
        private void RenderTradeRectangles(RenderContext context)
        {
            if (ChartInfo == null) return;
            if (_visualTrades.Count == 0) return;

            int firstVisible = FirstVisibleBarNumber;
            int lastVisible = LastVisibleBarNumber;
            int currentLast = CurrentBar - 1;
            if (currentLast < 0) currentLast = lastVisible;

            foreach (var t in _visualTrades)
            {
                int entryBar = t.EntryBar;
                int rightBar = t.ExitKind == TradeExitKind.Open
                    ? Math.Min(lastVisible, currentLast)
                    : t.ExitBar;

                if (rightBar < entryBar) rightBar = entryBar;
                if (rightBar < firstVisible) continue;
                if (entryBar > lastVisible) continue;

                int x1 = ChartInfo.GetXByBar(entryBar);
                int x2 = ChartInfo.GetXByBar(rightBar);
                if (x2 <= x1) x2 = x1 + 1;

                int yEntry = ChartInfo.GetYByPrice(t.EntryPrice);
                int ySl = ChartInfo.GetYByPrice(t.SlPrice);
                int yTp = ChartInfo.GetYByPrice(t.TpPrice);
                if (yEntry <= 0 || ySl <= 0 || yTp <= 0) continue;

                // Rectangle vert = TP (entre entry et tp)
                int gyTop = Math.Min(yEntry, yTp);
                int gyBot = Math.Max(yEntry, yTp);
                var greenRect = new Rectangle(x1, gyTop, x2 - x1, gyBot - gyTop);
                Color greenFill = Color.FromArgb(_rectFillAlpha, 0, 200, 80);
                Color greenBorder = Color.FromArgb(_rectBorderAlpha, 0, 255, 100);
                context.FillRectangle(greenFill, greenRect);
                context.DrawRectangle(new RenderPen(greenBorder, 1), greenRect);

                // Rectangle rouge = SL (entre entry et sl)
                int ryTop = Math.Min(yEntry, ySl);
                int ryBot = Math.Max(yEntry, ySl);
                var redRect = new Rectangle(x1, ryTop, x2 - x1, ryBot - ryTop);
                Color redFill = Color.FromArgb(_rectFillAlpha, 220, 40, 0);
                Color redBorder = Color.FromArgb(_rectBorderAlpha, 255, 80, 0);
                context.FillRectangle(redFill, redRect);
                context.DrawRectangle(new RenderPen(redBorder, 1), redRect);

                // Ligne entry (blanche fine) pour repérer le fill
                Color entryLineCol = Color.FromArgb(220, 230, 230, 230);
                context.DrawLine(new RenderPen(entryLineCol, 1), x1, yEntry, x2, yEntry);
            }
        }

        private void RenderDashboard(RenderContext context)
        {
            var fN = new RenderFont("Consolas", FontSize);
            var fL = new RenderFont("Consolas", FontSize + 2);

            int pad = 10, lh = FontSize + 9, w = 460;
            int x = DashX, y = DashY;
            int valX = x + pad + DashLabelCol;

            int diagRows = 9;
            int btRows = BacktestMode ? 6 : 0;
            int totalRows = 18 + diagRows + btRows + Math.Min(4, _logIdx);
            int h = lh * totalRows + pad * 2;

            context.FillRectangle(Color.FromArgb(230, 10, 10, 20), new Rectangle(x, y, w, h));
            context.DrawRectangle(new RenderPen(Color.FromArgb(255, 70, 70, 130), 2), new Rectangle(x, y, w, h));
            context.FillRectangle(Color.FromArgb(255, 30, 30, 75), new Rectangle(x, y, w, lh + 4));

            int tx = x + pad, ty = y + pad;
            context.DrawString("FAILED AUCTION REVERSAL — ES", fL, Color.Gold, tx, ty);
            ty += lh + 6;
            Sep(context, tx, ty, x + w - pad); ty += 5;

            bool ok = Portfolio != null && Security != null;
            Row(context, fN, tx, valX, ref ty, lh, "Connexion:", ok ? "ACTIVE" : "INACTIVE",
                ok ? Color.LimeGreen : Color.Red);
            Row(context, fN, tx, valX, ref ty, lh, "Instrument:", Security?.Code ?? "--", Color.White);
            Row(context, fN, tx, valX, ref ty, lh, "Mode:", BacktestMode ? "BACKTEST" : "LIVE",
                BacktestMode ? Color.Orange : Color.LimeGreen);
            Row(context, fN, tx, valX, ref ty, lh, "Session:",
                $"{SessionStartHour:00}:{SessionStartMinute:00}-{SessionEndHour:00}:{SessionEndMinute:00} NY",
                Color.LightSteelBlue);

            string posStr = CurrentPosition == 0 ? "Plat" : $"{CurrentPosition:+#;-#} c";
            Color posCol = CurrentPosition == 0 ? Color.Gray
                : CurrentPosition > 0 ? Color.LimeGreen : Color.OrangeRed;
            Row(context, fN, tx, valX, ref ty, lh, "Position:", posStr, posCol);
            Row(context, fN, tx, valX, ref ty, lh, "State:", _tradeState.ToString(),
                _tradeState == TradeState.InTrade ? Color.Yellow : Color.Gray);

            Row(context, fN, tx, valX, ref ty, lh, "ONH/ONL:",
                _ctx.HasOvernight ? $"{_ctx.OvernightHigh:F2} / {_ctx.OvernightLow:F2}" : "--",
                _ctx.HasOvernight ? Color.LightSteelBlue : Color.Gray);
            Row(context, fN, tx, valX, ref ty, lh, "RTH VWAP:",
                _ctx.HasRthVwap ? _ctx.RthVwap.ToString("F2") : "--",
                _ctx.HasRthVwap ? Color.LightSteelBlue : Color.Gray);
            Row(context, fN, tx, valX, ref ty, lh, "Bar speed (s/bar):",
                _ctx.BarFormationSecondsAvg > 0 ? _ctx.BarFormationSecondsAvg.ToString("F1") : "--",
                Color.Gray);

            Sep(context, tx, ty, x + w - pad); ty += 5;
            context.DrawString("Risk:", fN, Color.Cyan, tx, ty); ty += lh;

            decimal pnl = GetDailyPnLUsdApprox();
            string pnlStr = _dailyPnlBaselineReady ? $"{pnl:F2} $" : "--";
            if (_dailyLimitsHalted) pnlStr += " [HALT]";
            Row(context, fN, tx, valX, ref ty, lh, "PnL jour:", pnlStr,
                _dailyLimitsHalted ? Color.OrangeRed
                : pnl > 0 ? Color.LimeGreen : pnl < 0 ? Color.OrangeRed : Color.LightGray);
            Row(context, fN, tx, valX, ref ty, lh, "Pertes consec.:",
                $"{_consecutiveLossCount} / {MaxConsecutiveLosses}",
                _consecutiveLossHalted ? Color.OrangeRed
                : _consecutiveLossCount > 0 ? Color.Yellow : Color.Gray);
            Row(context, fN, tx, valX, ref ty, lh, "Bracket SL/TP:",
                $"{SLTicks} / {TPTicks} ticks", Color.LightGray);

            if (BacktestMode)
            {
                Sep(context, tx, ty, x + w - pad); ty += 5;
                context.DrawString("Backtest:", fN, Color.Gold, tx, ty); ty += lh;
                Row(context, fN, tx, valX, ref ty, lh, "Entrées:", _btEntries.ToString(), Color.White);
                Row(context, fN, tx, valX, ref ty, lh, "TP / SL:",
                    $"{_btTakeProfit} / {_btStopLoss}",
                    _btTakeProfit >= _btStopLoss ? Color.LimeGreen : Color.OrangeRed);
                Row(context, fN, tx, valX, ref ty, lh, "Win rate:",
                    WinRateStr(_btTakeProfit, _btClosed), Color.LightGray);
                Row(context, fN, tx, valX, ref ty, lh, "PnL (ticks):",
                    $"{_btPnlTicks:+0.0;-0.0}",
                    _btPnlTicks >= 0 ? Color.LimeGreen : Color.OrangeRed);
            }

            // Diagnostic — visible toujours pour savoir pourquoi on n'a (ou pas) de trade
            Sep(context, tx, ty, x + w - pad); ty += 5;
            context.DrawString("Diagnostic detection:", fN, Color.Magenta, tx, ty); ty += lh;
            Row(context, fN, tx, valX, ref ty, lh, "Bougies évaluées:",
                _diagBarsEvaluated.ToString(),
                _diagBarsEvaluated > 0 ? Color.White : Color.OrangeRed);
            Row(context, fN, tx, valX, ref ty, lh, "Delta ineff > 0:",
                $"{_diagDeltaNonZero} ({PctOfEvaluated(_diagDeltaNonZero)})",
                _diagDeltaNonZero > 0 ? Color.LightGray : Color.OrangeRed);
            Row(context, fN, tx, valX, ref ty, lh, "Weak close > 0:",
                $"{_diagWeakCloseNonZero} ({PctOfEvaluated(_diagWeakCloseNonZero)})",
                _diagWeakCloseNonZero > 0 ? Color.LightGray : Color.OrangeRed);
            Row(context, fN, tx, valX, ref ty, lh, "Vol spike > 0:",
                $"{_diagVolSpikeNonZero} ({PctOfEvaluated(_diagVolSpikeNonZero)})",
                _diagVolSpikeNonZero > 0 ? Color.LightGray : Color.OrangeRed);
            Row(context, fN, tx, valX, ref ty, lh, "Proximité > 0:",
                $"{_diagProximityNonZero} ({PctOfEvaluated(_diagProximityNonZero)})",
                _diagProximityNonZero > 0 ? Color.LightGray : Color.OrangeRed);
            Row(context, fN, tx, valX, ref ty, lh, "Score max vu:",
                _diagMaxScore.ToString("F2"),
                _diagMaxScore >= ScoreThreshold ? Color.LimeGreen : Color.Yellow);
            Row(context, fN, tx, valX, ref ty, lh, "Passages threshold:",
                $"{_diagPassedThreshold} (seuil {ScoreThreshold:F2})",
                _diagPassedThreshold > 0 ? Color.LimeGreen : Color.OrangeRed);

            Sep(context, tx, ty, x + w - pad); ty += 5;
            context.DrawString("Status:", fN, Color.Cyan, tx, ty); ty += lh;
            context.DrawString(_status ?? "--", fN, Color.LightGray, tx, ty); ty += lh;

            string logPath = GetLogFilePath();
            if (!string.IsNullOrEmpty(logPath))
            {
                context.DrawString($"Log: {Path.GetFileName(logPath)}", fN, Color.Gray, tx, ty); ty += lh;
            }

            for (int i = 0; i < Math.Min(4, _logIdx); i++)
            {
                int li = ((_logIdx - 1 - i) % LogBufferSize + LogBufferSize) % LogBufferSize;
                if (!string.IsNullOrEmpty(_log[li]))
                {
                    context.DrawString(_log[li], fN, Color.LightGray, tx, ty);
                    ty += lh;
                }
            }
        }

        private static string WinRateStr(int wins, int closed) =>
            closed > 0 ? $"{100.0 * wins / closed:F0} %" : "--";

        private string PctOfEvaluated(int n) =>
            _diagBarsEvaluated > 0 ? $"{100.0 * n / _diagBarsEvaluated:F0}%" : "0%";

        private void Row(RenderContext ctx, RenderFont f, int tx, int valX, ref int ty, int lh,
            string lbl, string val, Color c)
        {
            ctx.DrawString(lbl, f, Color.LightGray, tx, ty);
            ctx.DrawString(val, f, c, valX, ty);
            ty += lh;
        }

        private void Sep(RenderContext ctx, int x1, int y, int x2) =>
            ctx.DrawLine(new RenderPen(Color.FromArgb(80, 80, 120), 1), x1, y, x2, y);
    }
}
