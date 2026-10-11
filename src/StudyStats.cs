using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DeskTodo
{
    public class StudyPeriodPanel : ScrollViewer
    {
        readonly AppController app;
        readonly StackPanel root = new StackPanel(), reportContent = new StackPanel();
        readonly ComboBox mode = new ComboBox { ItemsSource = new[] { "周报", "月报" }, SelectedIndex = 0, Width = 100 };
        readonly DatePicker date = new DatePicker { SelectedDate = DateTime.Today, FirstDayOfWeek = DayOfWeek.Monday, Width = 160 };
        string reportKey;
        public StudyPeriodPanel(AppController controller)
        {
            app = controller; Content = root; VerticalScrollBarVisibility = ScrollBarVisibility.Auto; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) }; AutomationProperties.SetName(mode, "学习报告周期"); AutomationProperties.SetName(date, "学习报告日期"); date.DisplayDateStart = new DateTime(2, 1, 1); date.DisplayDateEnd = DateTime.Today;
            bar.Children.Add(mode); date.Margin = new Thickness(12, 0, 12, 0); bar.Children.Add(date); bar.Children.Add(Ui.Button("本期", () => date.SelectedDate = DateTime.Today)); root.Children.Add(bar); root.Children.Add(reportContent);
            mode.SelectionChanged += (s, e) => Refresh(true); date.SelectedDateChanged += (s, e) => Refresh(true); Refresh(true);
        }
        static string Duration(double seconds) { return ((int)(seconds / 3600)) + " 小时 " + ((int)(seconds % 3600 / 60)) + " 分"; }
        static Border Metric(string title, string value) { var panel = new StackPanel(); panel.Children.Add(Ui.Text(title, 12, "Muted")); var number = Ui.Text(value, 24, "Accent", true); number.Margin = new Thickness(0, 8, 0, 0); panel.Children.Add(number); var card = Ui.Card(panel, new Thickness(16)); card.Margin = new Thickness(0, 0, 12, 0); return card; }
        public void Refresh(bool force = false)
        {
            date.DisplayDateEnd = DateTime.Today; var chosen = (date.SelectedDate ?? DateTime.Today).Date; if (chosen > DateTime.Today) chosen = DateTime.Today;
            string key = Dates.Key(chosen) + ":" + Dates.Key(DateTime.Today) + ":" + mode.SelectedIndex + ":" + app.Store.State.FocusHistory.Count + ":" + (app.Store.State.FocusHistory.LastOrDefault() == null ? "" : app.Store.State.FocusHistory.Last().Id) + ":" + (app.Store.State.ActiveFocus == null ? "" : app.Store.State.ActiveFocus.Id) + ":" + Math.Floor(app.FocusSeconds / 5);
            if (!force && key == reportKey) return; reportKey = key;
            bool monthly = mode.SelectedIndex == 1; var range = Study.PeriodRange(chosen, monthly); var end = range[1] < DateTime.Today ? range[1] : DateTime.Today;
            var sessions = app.Store.State.FocusHistory.ToList(); var active = app.FocusSnapshot(); if (active != null) sessions.Add(active); var report = Study.Summarize(sessions, range[0], end);
            var prior = Study.PeriodRange(monthly ? range[0].AddMonths(-1) : range[0].AddDays(-7), monthly); var previousEnd = prior[0].AddDays((end - range[0]).Days); if (previousEnd > prior[1]) previousEnd = prior[1]; var previous = Study.Summarize(sessions, prior[0], previousEnd);
            reportContent.Children.Clear(); var heading = Ui.Text(range[0].ToString("yyyy年M月d日") + " – " + range[1].ToString("M月d日"), 23, "Text", true); reportContent.Children.Add(heading);
            var hint = Ui.Text("统计至 " + end.ToString("M月d日") + " · 实际计时，含提前结束与进行中的学习 · 不包含待办", 12, "Muted"); hint.Margin = new Thickness(0, 6, 0, 18); reportContent.Children.Add(hint);
            var summary = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 18) }; summary.Children.Add(Metric("学习总时长", Duration(report.TotalSeconds))); summary.Children.Add(Metric("学习天数", report.SecondsByDay.Count(p => p.Value > 0) + " 天")); summary.Children.Add(Metric("完成次数", report.Completed + " 次")); summary.Children.Add(Metric("日均时长（含未学习日）", Duration(report.TotalSeconds / report.SecondsByDay.Count))); reportContent.Children.Add(summary);
            double difference = report.TotalSeconds - previous.TotalSeconds;
            var compare = Ui.Text("比上" + (monthly ? "月" : "周") + "同期" + (difference >= 0 ? "增加 " : "减少 ") + Duration(Math.Abs(difference)) + " · 对比 " + prior[0].ToString("M/d") + " – " + previousEnd.ToString("M/d"), 13, "Muted"); compare.Margin = new Thickness(0, 0, 0, 18); reportContent.Children.Add(compare);
            var chartPanel = new StackPanel(); chartPanel.Children.Add(Ui.Text("每日学习时长 · 分钟", 16, "Text", true)); var chart = new UniformGrid { Columns = report.SecondsByDay.Count, Margin = new Thickness(0, 16, 0, 0) }; double max = Math.Max(60, report.SecondsByDay.Values.Max());
            foreach (var pair in report.SecondsByDay.OrderBy(p => p.Key))
            {
                var column = new StackPanel { Margin = new Thickness(monthly ? 2 : 8, 0, monthly ? 2 : 8, 0), ToolTip = pair.Key + " · " + Duration(pair.Value) }; var value = Ui.Text((pair.Value / 60).ToString("0.#"), monthly ? 9 : 11, "Muted"); value.TextAlignment = TextAlignment.Center; column.Children.Add(value);
                var plot = new Grid { Height = 140 }; var fill = new Border { Height = pair.Value / max * 140, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(3, 3, 0, 0) }; fill.SetResourceReference(Border.BackgroundProperty, "Accent"); plot.Children.Add(fill); column.Children.Add(plot);
                var label = Ui.Text(DateTime.ParseExact(pair.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString(monthly ? "%d" : "M/d"), 10, "Muted"); label.TextAlignment = TextAlignment.Center; label.Margin = new Thickness(0, 6, 0, 0); column.Children.Add(label); chart.Children.Add(column);
            }
            chartPanel.Children.Add(chart); var chartCard = Ui.Card(chartPanel, new Thickness(18)); chartCard.Margin = new Thickness(0, 0, 0, 18); reportContent.Children.Add(chartCard);
            var ranking = new StackPanel(); ranking.Children.Add(Ui.Text("主要学习内容", 16, "Text", true));
            if (report.TotalSeconds == 0) Ui.Empty(ranking, "本期还没有学习时长", "开始计时后，学习记录会自动汇总到这里");
            foreach (var pair in report.SecondsByTitle.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(5)) { var item = Ui.Text(pair.Key + "  ·  " + Duration(pair.Value) + "  ·  " + (pair.Value / report.TotalSeconds * 100).ToString("0.#") + "%", 13, "Text"); item.Margin = new Thickness(0, 10, 0, 0); ranking.Children.Add(item); }
            var best = report.SecondsByDay.OrderByDescending(p => p.Value).ThenBy(p => p.Key).First(); if (best.Value > 0) { var peak = Ui.Text("学习最多的一天：" + best.Key + " · " + Duration(best.Value), 12, "Muted"); peak.Margin = new Thickness(0, 16, 0, 0); ranking.Children.Add(peak); } reportContent.Children.Add(Ui.Card(ranking, new Thickness(18)));
        }
    }
    public class StudyStatsPanel : ScrollViewer
    {
        readonly AppController app;
        readonly StackPanel root = new StackPanel();
        readonly DatePicker through = new DatePicker { SelectedDate = DateTime.Today, FirstDayOfWeek = DayOfWeek.Monday, Width = 150 };
        readonly DatePicker recordDay = new DatePicker { SelectedDate = DateTime.Today, FirstDayOfWeek = DayOfWeek.Monday, Width = 150 };
        readonly TextBox search = new TextBox { MaxLength = 200, ToolTip = "输入学习名称筛选当天记录" };
        readonly ComboBox stateFilter = new ComboBox { ItemsSource = new[] { "全部状态", "已完成", "提前结束", "未结束" }, SelectedIndex = 0, Width = 130 };
        readonly TextBlock total = Ui.Text("", 25, "Accent", true), days = Ui.Text("", 25, "Text", true), completed = Ui.Text("", 25, "Text", true);
        readonly StackPanel weekly = new StackPanel(), heatmap = new StackPanel(), ranking = new StackPanel(), records = new StackPanel();
        readonly TextBlock recordCount = Ui.Text("", 11, "Muted");
        List<FocusSession> sessions = new List<FocusSession>();
        string reportKey;
        int recordLimit = 50;
        DateTime lastToday = DateTime.Today;
        public StudyStatsPanel(AppController controller)
        {
            app = controller; Content = root; VerticalScrollBarVisibility = ScrollBarVisibility.Auto; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            var label = Ui.Text("统计截至", 12, "Muted"); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 0, 12, 0); bar.Children.Add(label);
            through.DisplayDateStart = DateTime.MinValue.AddDays(29); through.DisplayDateEnd = DateTime.Today; AutomationProperties.SetName(through, "统计截止日期"); bar.Children.Add(through);
            var today = Ui.Button("今天", () => { through.SelectedDate = DateTime.Today; SelectRecordDay(DateTime.Today); search.Text = ""; stateFilter.SelectedIndex = 0; Refresh(true); }); today.Margin = new Thickness(10, 0, 0, 0); bar.Children.Add(today);
            var hint = Ui.Text("实际计时 · 含提前结束和进行中的学习", 11, "Muted"); hint.VerticalAlignment = VerticalAlignment.Center; hint.Margin = new Thickness(16, 0, 0, 0); bar.Children.Add(hint); root.Children.Add(bar);
            var summary = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 16) };
            summary.Children.Add(Metric("近30天专注时长", total)); summary.Children.Add(Metric("近30天学习天数", days)); summary.Children.Add(Metric("近30天完成次数", completed)); root.Children.Add(summary);
            var charts = TwoColumns(); charts.Margin = new Thickness(0, 0, 0, 16);
            var weekCard = Ui.Card(weekly, new Thickness(18)); weekCard.Margin = new Thickness(0, 0, 16, 0); charts.Children.Add(weekCard);
            var heatCard = Ui.Card(heatmap, new Thickness(18)); Grid.SetColumn(heatCard, 1); charts.Children.Add(heatCard); root.Children.Add(charts);
            var bottom = TwoColumns(); bottom.Height = 350;
            var rankCard = Ui.Card(ranking, new Thickness(18)); rankCard.Margin = new Thickness(0, 0, 16, 0); bottom.Children.Add(rankCard);
            var recordPanel = new DockPanel(); var recordHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(recordHeader, Dock.Top); recordPanel.Children.Add(recordHeader);
            var dateRow = new DockPanel(); DockPanel.SetDock(recordDay, Dock.Right); dateRow.Children.Add(recordDay); dateRow.Children.Add(Ui.Text("学习记录", 16, "Text", true)); recordHeader.Children.Add(dateRow);
            recordDay.DisplayDateEnd = DateTime.Today; AutomationProperties.SetName(recordDay, "学习记录日期");
            var filters = new Grid { Margin = new Thickness(0, 10, 0, 8) }; filters.ColumnDefinitions.Add(new ColumnDefinition()); filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nameFilter = new DockPanel(); var nameLabel = Ui.Text("名称", 10, "Muted"); nameLabel.VerticalAlignment = VerticalAlignment.Center; nameLabel.Margin = new Thickness(0, 0, 6, 0); DockPanel.SetDock(nameLabel, Dock.Left); nameFilter.Children.Add(nameLabel);
            search.Margin = new Thickness(0, 0, 10, 0); AutomationProperties.SetName(search, "学习名称筛选"); nameFilter.Children.Add(search); filters.Children.Add(nameFilter); Grid.SetColumn(stateFilter, 1); AutomationProperties.SetName(stateFilter, "学习记录状态"); filters.Children.Add(stateFilter); recordHeader.Children.Add(filters); recordHeader.Children.Add(recordCount);
            recordPanel.Children.Add(Ui.Scroll(records)); var recordCard = Ui.Card(recordPanel, new Thickness(18)); Grid.SetColumn(recordCard, 1); bottom.Children.Add(recordCard); root.Children.Add(bottom);
            through.SelectedDateChanged += (s, e) => { if (through.SelectedDate > DateTime.Today) through.SelectedDate = DateTime.Today; recordDay.SelectedDate = through.SelectedDate ?? DateTime.Today; Refresh(true); };
            recordDay.SelectedDateChanged += (s, e) => { if (recordDay.SelectedDate > DateTime.Today) recordDay.SelectedDate = DateTime.Today; recordLimit = 50; Refresh(true); };
            search.TextChanged += (s, e) => { recordLimit = 50; RefreshRecords(); };
            stateFilter.SelectionChanged += (s, e) => { recordLimit = 50; RefreshRecords(); };
            Refresh(true);
        }
        static Grid TwoColumns() { var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition()); return grid; }
        static Border Metric(string caption, TextBlock value) { var panel = new StackPanel(); panel.Children.Add(Ui.Text(caption, 11, "Muted")); value.Margin = new Thickness(0, 6, 0, 0); panel.Children.Add(value); var card = Ui.Card(panel, new Thickness(15, 12, 15, 12)); card.Margin = new Thickness(0, 0, 8, 0); return card; }
        static string Duration(double seconds) { long value = (long)Math.Floor(seconds); return value >= 3600 ? (value / 3600) + " 小时 " + ((value % 3600) / 60) + " 分" : value >= 60 ? (value / 60) + " 分 " + (value % 60) + " 秒" : value + " 秒"; }
        public void Refresh(bool force = false)
        {
            if (lastToday != DateTime.Today)
            {
                var previous = lastToday; lastToday = DateTime.Today; through.DisplayDateEnd = recordDay.DisplayDateEnd = lastToday;
                if (through.SelectedDate == previous) through.SelectedDate = lastToday; if (recordDay.SelectedDate == previous) recordDay.SelectedDate = lastToday;
            }
            var end = (through.SelectedDate ?? DateTime.Today).Date;
            string key = Dates.Key(end) + ":" + app.Store.State.FocusHistory.Count + ":" + (app.Store.State.FocusHistory.LastOrDefault() == null ? "" : app.Store.State.FocusHistory.Last().Id) + ":" + (app.Store.State.ActiveFocus == null ? "" : app.Store.State.ActiveFocus.Id) + ":" + Math.Floor(app.FocusSeconds / 5) + ":" + app.FocusRunning;
            if (!force && key == reportKey) return; reportKey = key;
            sessions = app.Store.State.FocusHistory.ToList(); var active = app.FocusSnapshot(); if (active != null) sessions.Add(active);
            var report = Study.Summarize(sessions, end.AddDays(-29), end);
            total.Text = Duration(report.TotalSeconds); total.ToolTip = Study.FormatDuration(report.TotalSeconds); days.Text = report.SecondsByDay.Count(p => p.Value > 0) + " 天"; completed.Text = report.Completed + " 次";
            RenderWeek(report, end); RenderHeatmap(report, end); RenderRanking(report); RefreshRecords();
        }
        void SelectRecordDay(DateTime day) { recordDay.SelectedDate = day; recordLimit = 50; RefreshRecords(); }
        void RenderWeek(StudyReport report, DateTime end)
        {
            weekly.Children.Clear(); weekly.Children.Add(Ui.Text("近7天学习时长", 16, "Text", true));
            var chart = new Grid { Margin = new Thickness(0, 12, 0, 0) }; chart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            for (int n = 0; n < 7; n++) chart.ColumnDefinitions.Add(new ColumnDefinition());
            double maximum = Enumerable.Range(0, 7).Max(n => report.SecondsByDay[Dates.Key(end.AddDays(n - 6))]) / 60;
            double step = maximum > 300 ? 60 : maximum > 60 ? 30 : 5;
            double ceiling = Math.Max(step, Math.Ceiling(maximum / step) * step);
            var axis = new Grid { Height = 125, Margin = new Thickness(0, 19, 0, 24), VerticalAlignment = VerticalAlignment.Top };
            foreach (var tick in new[] { ceiling, ceiling / 2, 0 }) { var t = Ui.Text(tick.ToString("0.#") + "分", 9, "Muted"); t.VerticalAlignment = tick == ceiling ? VerticalAlignment.Top : tick == 0 ? VerticalAlignment.Bottom : VerticalAlignment.Center; axis.Children.Add(t); }
            chart.Children.Add(axis);
            for (int n = 0; n < 7; n++)
            {
                var day = end.AddDays(n - 6); double seconds = report.SecondsByDay[Dates.Key(day)];
                var column = new StackPanel(); var value = Ui.Text(seconds == 0 ? "0" : seconds < 60 ? "<1分" : (seconds / 60).ToString("0.#") + "分", 9, "Muted"); value.Height = 19; value.TextAlignment = TextAlignment.Center; column.Children.Add(value);
                var plot = new Grid { Height = 125, Margin = new Thickness(8, 0, 8, 0) };
                var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom }; line.SetResourceReference(Border.BackgroundProperty, "Line"); plot.Children.Add(line);
                var bar = new Border { Height = seconds / 60 / ceiling * 125, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(3, 3, 0, 0), Opacity = day == DateTime.Today ? 1 : 0.65 }; bar.SetResourceReference(Border.BackgroundProperty, "Accent"); plot.Children.Add(bar); column.Children.Add(plot);
                var date = Ui.Text(day.ToString("M/d"), 10, "Muted"); date.Height = 24; date.TextAlignment = TextAlignment.Center; date.Margin = new Thickness(0, 5, 0, 0); column.Children.Add(date);
                var button = Ui.Button("", () => SelectRecordDay(day), "Quiet"); button.Content = column; button.Padding = new Thickness(0); button.BorderThickness = new Thickness(0); button.Background = Brushes.Transparent; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.ToolTip = day.ToString("yyyy年M月d日 dddd") + " · " + Duration(seconds); AutomationProperties.SetName(button, "学习柱状图 " + Dates.Key(day)); Grid.SetColumn(button, n + 1); chart.Children.Add(button);
            }
            weekly.Children.Add(chart);
        }
        void RenderHeatmap(StudyReport report, DateTime end)
        {
            heatmap.Children.Clear(); heatmap.Children.Add(Ui.Text("近30天学习热力图", 16, "Text", true));
            var labels = new UniformGrid { Columns = 7, Margin = new Thickness(0, 12, 0, 4) }; foreach (string name in new[] { "一", "二", "三", "四", "五", "六", "日" }) { var label = Ui.Text(name, 10, "Muted"); label.TextAlignment = TextAlignment.Center; labels.Children.Add(label); } heatmap.Children.Add(labels);
            var calendar = new UniformGrid { Columns = 7 };
            var start = end.AddDays(-29); int blanks = ((int)start.DayOfWeek + 6) % 7; for (int n = 0; n < blanks; n++) calendar.Children.Add(new Border());
            for (int n = 0; n < 30; n++)
            {
                var day = start.AddDays(n); double seconds = report.SecondsByDay[Dates.Key(day)];
                var button = Ui.Button(n == 0 || day.Day == 1 ? day.ToString("M/d") : day.Day.ToString(), () => SelectRecordDay(day)); button.Margin = new Thickness(2); button.Padding = new Thickness(0); button.MinHeight = 0; button.Height = 24; button.FontSize = 10; button.BorderThickness = new Thickness(day == recordDay.SelectedDate ? 2 : 1); button.SetResourceReference(Button.BorderBrushProperty, day == recordDay.SelectedDate ? "Accent" : "Line");
                double intensity = seconds <= 0 ? 0 : seconds <= 900 ? 0.18 : seconds <= 1800 ? 0.32 : seconds <= 3600 ? 0.48 : seconds <= 7200 ? 0.7 : 1;
                if (intensity == 0) button.SetResourceReference(Button.BackgroundProperty, "Panel"); else { var accent = (SolidColorBrush)Application.Current.Resources["Accent"]; button.Background = new SolidColorBrush(accent.Color) { Opacity = intensity }; }
                button.SetResourceReference(Button.ForegroundProperty, intensity >= 1 || intensity >= 0.7 && app.Settings.Theme == "dark" ? "AccentInk" : "Text"); button.ToolTip = day.ToString("yyyy年M月d日") + " · " + Duration(seconds); AutomationProperties.SetName(button, "学习热力图 " + Dates.Key(day)); calendar.Children.Add(button);
            }
            heatmap.Children.Add(calendar); var legend = Ui.Text(start.ToString("M/d") + " – " + end.ToString("M/d") + "  ·  颜色越深，学习越久  ·  点击查看记录", 10, "Muted"); legend.Margin = new Thickness(0, 8, 0, 0); heatmap.Children.Add(legend);
        }
        void RenderRanking(StudyReport report)
        {
            ranking.Children.Clear(); ranking.Children.Add(Ui.Text("近30天学习时长排行", 16, "Text", true)); var hint = Ui.Text("同名学习合并 · 含提前结束 · 展示前5项", 10, "Muted"); hint.Margin = new Thickness(0, 6, 0, 12); ranking.Children.Add(hint);
            if (report.SecondsByTitle.Count == 0) { Ui.Empty(ranking, "还没有时长记录", "开始一轮学习后，就能看到时间分配"); return; }
            foreach (var pair in report.SecondsByTitle.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(5))
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 5) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = Ui.Text(pair.Key, 12, "Text", true); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.ToolTip = pair.Key; row.Children.Add(name);
                double ratio = Math.Min(1, pair.Value / report.TotalSeconds); var value = Ui.Text(Duration(pair.Value) + " · " + (ratio * 100).ToString("0.#") + "%", 10, "Muted"); value.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(value, 1); row.Children.Add(value); ranking.Children.Add(row);
                var track = new Grid { Height = 6, Margin = new Thickness(0, 0, 0, 13) }; track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) }); track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star) });
                var background = new Border { CornerRadius = new CornerRadius(3) }; background.SetResourceReference(Border.BackgroundProperty, "Soft"); Grid.SetColumnSpan(background, 2); track.Children.Add(background);
                var fill = new Border { CornerRadius = new CornerRadius(3) }; fill.SetResourceReference(Border.BackgroundProperty, "Accent"); track.Children.Add(fill); ranking.Children.Add(track);
            }
        }
        void RefreshRecords()
        {
            if (sessions == null) return;
            var day = (recordDay.SelectedDate ?? DateTime.Today).Date; string term = search.Text.Trim(); int filter = stateFilter.SelectedIndex;
            var matches = sessions.Where(f => (Study.SecondsOn(f, day) > 0 || DateTimeOffset.ParseExact(f.StartedAt, "o", CultureInfo.InvariantCulture).Date == day || (f.EndedAt != null && DateTimeOffset.ParseExact(f.EndedAt, "o", CultureInfo.InvariantCulture).Date == day)) && (term.Length == 0 || f.Title.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) && (filter == 0 || filter == 1 && f.Completed || filter == 2 && !f.Completed && f.EndedAt != null || filter == 3 && f.EndedAt == null)).OrderByDescending(f => DateTimeOffset.ParseExact(f.StartedAt, "o", CultureInfo.InvariantCulture)).ToList();
            recordCount.Text = day.ToString("M月d日") + " · " + matches.Count + " 条记录"; records.Children.Clear();
            if (matches.Count == 0) Ui.Empty(records, "这一天没有匹配的记录", "可选择其他日期，或清除名称与状态筛选");
            foreach (var focus in matches.Take(recordLimit))
            {
                var panel = new StackPanel(); panel.Children.Add(Ui.Text(focus.Title, 13, "Text", true)); var duration = Ui.Text("当天 " + Duration(Study.SecondsOn(focus, day)) + (focus.IsCountUp ? " · 正向计时" : " · 计划 " + (focus.TargetSeconds / 60) + " 分"), 11, "Muted"); duration.Margin = new Thickness(0, 5, 0, 4); panel.Children.Add(duration);
                var from = DateTimeOffset.ParseExact(focus.StartedAt, "o", CultureInfo.InvariantCulture); string ended = focus.EndedAt == null ? "" : DateTimeOffset.ParseExact(focus.EndedAt, "o", CultureInfo.InvariantCulture).ToString("M/d HH:mm");
                panel.Children.Add(Ui.Text(from.ToString("M/d HH:mm") + (ended == "" ? "" : " – " + ended) + " · " + (focus.EndedAt == null ? app.FocusRunning ? "进行中" : "已暂停" : focus.Completed ? "已完成" : "提前结束"), 10, focus.Completed ? "Accent" : "Muted"));
                var card = Ui.Card(panel, new Thickness(12)); card.Margin = new Thickness(0, 0, 0, 8); records.Children.Add(card);
            }
            if (matches.Count > recordLimit) { var more = Ui.Button("加载更多（还有 " + (matches.Count - recordLimit) + " 条）", () => { recordLimit += 50; RefreshRecords(); }); records.Children.Add(more); }
        }
    }
}
