using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace DeskTodo
{
    public class CreditPanel : ScrollViewer
    {
        readonly AppController app;
        readonly StackPanel root = new StackPanel();
        DateTime month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        public DateTime SelectedDate { get; private set; }
        bool compact;
        public CreditPanel(AppController controller)
        {
            app = controller; SelectedDate = DateTime.Today; Content = root; VerticalScrollBarVisibility = ScrollBarVisibility.Auto; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            SizeChanged += (s, e) => { bool value = ActualWidth < 830; if (value != compact) { compact = value; Refresh(); } };
        }
        public static string Format(decimal points) { return points.ToString("0.############################", CultureInfo.CurrentCulture); }
        static Border Card(UIElement content) { var card = Ui.Card(content, new Thickness(22)); card.Margin = new Thickness(0, 0, 0, 16); return card; }
        static TextBlock Hint(string text) { return Ui.Text(text, 11, "Muted"); }
        static Viewbox Number(string text, double size, double height, string color = "Accent")
        {
            var label = Ui.Text(text, size, color, true); label.FontFamily = new FontFamily("Segoe UI"); label.ToolTip = text;
            return new Viewbox { Child = label, Height = height, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left };
        }
        Grid Columns(UIElement left, UIElement right, double ratio = 1)
        {
            var grid = new Grid();
            if (compact) { grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetRow(right, 1); }
            else { grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetColumn(right, 1); ((FrameworkElement)left).Margin = new Thickness(0, 0, 16, 16); }
            grid.Children.Add(left); grid.Children.Add(right); return grid;
        }
        void ChangeMonth(DateTime date)
        {
            month = new DateTime(date.Year, date.Month, 1); SelectedDate = month.Year == DateTime.Today.Year && month.Month == DateTime.Today.Month ? DateTime.Today : month; Refresh();
        }
        public void Refresh()
        {
            root.Children.Clear(); var summary = CreditSummary.Create(app.Store.State.Credits, month);
            var accumulation = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; accumulation.Children.Add(Hint("累计综测加分"));
            var totalNumber = Number(Format(summary.Total) + " 分", 50, 74); var totalText = (TextBlock)totalNumber.Child; totalText.Inlines.Clear(); totalText.Inlines.Add(new Run(Format(summary.Total))); var unit = new Run(" 分") { FontSize = 14, FontWeight = FontWeights.Normal }; unit.SetResourceReference(TextElement.ForegroundProperty, "Muted"); totalText.Inlines.Add(unit); accumulation.Children.Add(totalNumber);
            accumulation.Children.Add(Hint(summary.Count + " 项活动，留下每一份积累"));
            var monthTotal = Ui.Text(month.ToString("yyyy年M月") + " · 新增 " + Format(summary.MonthTotal) + " 分 · " + summary.Records.Count + " 项活动", 12, "Text"); monthTotal.Margin = new Thickness(0, 18, 0, 0); accumulation.Children.Add(monthTotal);
            var sources = new StackPanel(); sources.Children.Add(Ui.Text("加分来源", 15, "Text", true)); var autoCount = Hint(summary.LinkedCount + " 项活动由任务完成自动计入"); autoCount.Margin = new Thickness(0, 16, 0, 12); sources.Children.Add(autoCount);
            var track = new Grid { Height = 9 }; track.SetResourceReference(Grid.BackgroundProperty, "Soft");
            if (summary.Total > 0)
            {
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)(summary.LinkedTotal / summary.Total), GridUnitType.Star) }); track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)(summary.ManualTotal / summary.Total), GridUnitType.Star) });
                var automatic = new Border(); automatic.SetResourceReference(Border.BackgroundProperty, "Accent"); track.Children.Add(automatic); var manual = new Border { Background = new SolidColorBrush(Color.FromRgb(137, 164, 181)) }; Grid.SetColumn(manual, 1); track.Children.Add(manual);
            }
            AutomationProperties.SetName(track, "任务完成 " + Format(summary.LinkedTotal) + " 分；手动添加 " + Format(summary.ManualTotal) + " 分"); sources.Children.Add(track);
            foreach (bool linked in new[] { true, false })
            {
                var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) }; var points = Number(Format(linked ? summary.LinkedTotal : summary.ManualTotal) + " 分", 13, 20, "Text"); points.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(points, Dock.Right); row.Children.Add(points); row.Children.Add(Hint(linked ? "任务完成" : "手动添加")); sources.Children.Add(row);
            }
            var explanation = Hint("已关联的来源任务撤销完成时，对应加分同步撤回。"); explanation.Margin = new Thickness(0, 12, 0, 0); sources.Children.Add(explanation);
            var summaryGrid = Columns(accumulation, sources); accumulation.Margin = compact ? new Thickness(0, 0, 0, 20) : new Thickness(0, 0, 26, 0); root.Children.Add(Card(summaryGrid));
            var calendar = new StackPanel(); var top = new DockPanel(); var controls = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(controls, Dock.Right); top.Children.Add(controls);
            var previous = Ui.Button("‹", () => ChangeMonth(month.AddMonths(-1)), "Quiet"); previous.IsEnabled = month.Year > 1 || month.Month > 1; previous.ToolTip = "上个月"; AutomationProperties.SetName(previous, "综测上个月"); controls.Children.Add(previous);
            controls.Children.Add(Ui.Button("本月", () => ChangeMonth(DateTime.Today), "Quiet")); var next = Ui.Button("›", () => ChangeMonth(month.AddMonths(1)), "Quiet"); next.IsEnabled = month.Year < 9999 || month.Month < 12; next.ToolTip = "下个月"; AutomationProperties.SetName(next, "综测下个月"); controls.Children.Add(next);
            top.Children.Add(Ui.Text(month.ToString("yyyy年M月"), 18, "Text", true)); calendar.Children.Add(top);
            if (app.Store.State.Credits.Count == 0) { var empty = Hint("还没有综测加分记录"); empty.Margin = new Thickness(0, 12, 0, 0); calendar.Children.Add(empty); }
            var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 16, 0, 6) }; foreach (string weekday in new[] { "一", "二", "三", "四", "五", "六", "日" }) { var text = Hint(weekday); text.TextAlignment = TextAlignment.Center; weekdays.Children.Add(text); } calendar.Children.Add(weekdays);
            int offset = ((int)month.DayOfWeek + 6) % 7, count = DateTime.DaysInMonth(month.Year, month.Month), cells = ((offset + count + 6) / 7) * 7;
            var days = new UniformGrid { Columns = 7, Rows = cells / 7 }; AutomationProperties.SetName(days, "综测活动月历");
            for (int n = 0; n < cells; n++)
            {
                int day = n - offset + 1; if (day < 1 || day > count) { days.Children.Add(new Border()); continue; }
                DateTime date = month.AddDays(day - 1); decimal points = summary.ByDay[Dates.Key(date)]; bool selected = date == SelectedDate; var content = new StackPanel(); var label = Ui.Text(day.ToString(), 13, selected ? "Accent" : "Text", selected || date == DateTime.Today); label.TextAlignment = TextAlignment.Center; content.Children.Add(label);
                var value = Number(points == 0 ? "—" : "+" + Format(points), 11, 18, selected ? "Accent" : "Muted"); value.HorizontalAlignment = HorizontalAlignment.Stretch; value.Margin = new Thickness(0, 5, 0, 0); content.Children.Add(value);
                var button = Ui.Button("", () => { SelectedDate = date; Refresh(); }, "Quiet"); button.Content = content; button.Height = 64; button.MinWidth = 0; button.Padding = new Thickness(2, 6, 2, 6); button.Margin = new Thickness(2); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.BorderThickness = new Thickness(selected ? 1 : 0); button.SetResourceReference(Button.BorderBrushProperty, selected ? "Accent" : "Line"); button.SetResourceReference(Button.BackgroundProperty, selected || points > 0 ? "Soft" : "MainCanvas"); button.ToolTip = Dates.Key(date) + " · " + Format(points) + " 分"; AutomationProperties.SetName(button, "综测日期 " + Dates.Key(date)); days.Children.Add(button);
            }
            calendar.Children.Add(days); var hint = Hint("格内显示当日加分 · 点击日期查看全部活动"); hint.Margin = new Thickness(0, 12, 0, 0); calendar.Children.Add(hint);
            var detail = new StackPanel(); detail.Children.Add(Ui.Text(SelectedDate.ToString("M月d日 dddd"), 18, "Text", true)); var records = summary.Records.Where(c => c.Date == Dates.Key(SelectedDate)).Reverse().ToList(); var subtotal = Hint(records.Count + " 项活动 · 合计 " + Format(records.Sum(c => c.Points)) + " 分"); subtotal.Margin = new Thickness(0, 8, 0, 6); detail.Children.Add(subtotal);
            if (records.Count == 0) Ui.Empty(detail, "这一天还没有加分记录", "点击“添加加分”，记下这一天的活动");
            foreach (var record in records)
            {
                var row = new StackPanel { Margin = new Thickness(0, 17, 0, 17) }; var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition());
                var title = Ui.Text(record.Title, 14, "Text", true); title.Margin = new Thickness(0, 0, 10, 0); heading.Children.Add(title); var points = Number("+" + Format(record.Points), 22, 31); points.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(points, 1); heading.Children.Add(points); row.Children.Add(heading);
                bool linked = record.SourceTaskId != null, live = linked && app.Store.State.Items.Any(i => i.Id == record.SourceTaskId); row.Children.Add(Hint(linked ? live ? "任务完成自动加分" : "任务历史加分" : "手动添加"));
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) }; buttons.Children.Add(Ui.Button("修改", () => app.EditCredit(record), "Quiet")); buttons.Children.Add(Ui.Button("删除", () => app.DeleteCredit(record), "Quiet")); row.Children.Add(buttons); var line = new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, 1) }; line.SetResourceReference(Border.BorderBrushProperty, "Line"); detail.Children.Add(line);
            }
            root.Children.Add(Columns(Card(calendar), Card(detail), 1.35));
            var chartPanel = new StackPanel(); chartPanel.Children.Add(Ui.Text("每月加分", 15, "Text", true)); chartPanel.Children.Add(Hint("近 " + summary.ByMonth.Count + " 个月 · 点击柱形切换月份"));
            var chart = new Grid { Margin = new Thickness(0, 18, 0, 0) }; AutomationProperties.SetName(chart, "每月综测加分图"); chart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); var axis = new Grid { Height = 168 }; var maximum = summary.ByMonth.Values.Max(); if (maximum == 0) maximum = 1;
            foreach (double fraction in new[] { 0d, .5d, 1d }) { var label = Number(Format(maximum * (decimal)fraction), 11, 17, "Muted"); label.VerticalAlignment = VerticalAlignment.Top; label.Margin = new Thickness(0, (1 - fraction) * 136 + 10, 2, 0); axis.Children.Add(label); } chart.Children.Add(axis);
            int column = 1;
            foreach (var pair in summary.ByMonth.OrderBy(p => p.Key))
            {
                string key = pair.Key; chart.ColumnDefinitions.Add(new ColumnDefinition()); var body = new StackPanel { Margin = new Thickness(7, 0, 7, 0) }; var value = Number(Format(pair.Value), 11, 18, "Muted"); value.HorizontalAlignment = HorizontalAlignment.Center;
                var plot = new Grid { Height = 154 }; var bar = new Border { Height = (double)(pair.Value / maximum) * 136, Width = 29, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(4, 4, 0, 0), Opacity = key == month.ToString("yyyy-MM", CultureInfo.InvariantCulture) ? 1 : .45 }; bar.SetResourceReference(Border.BackgroundProperty, "Accent"); plot.Children.Add(bar); value.VerticalAlignment = VerticalAlignment.Top; value.Margin = new Thickness(0, 136 - bar.Height, 0, 0); plot.Children.Add(value); body.Children.Add(plot); var monthLabel = Hint(DateTime.ParseExact(key, "yyyy-MM", CultureInfo.InvariantCulture).ToString("M月")); monthLabel.Margin = new Thickness(0, 8, 0, 0); monthLabel.TextAlignment = TextAlignment.Center; body.Children.Add(monthLabel);
                var button = Ui.Button("", () => ChangeMonth(DateTime.ParseExact(key, "yyyy-MM", CultureInfo.InvariantCulture)), "Quiet"); button.Content = body; button.Padding = new Thickness(0); button.BorderThickness = new Thickness(0); button.Background = Brushes.Transparent; button.ToolTip = key + " · " + Format(pair.Value) + " 分"; AutomationProperties.SetName(button, "综测月份 " + key + "，" + Format(pair.Value) + "分"); Grid.SetColumn(button, column++); chart.Children.Add(button);
            }
            var gridlines = new Grid { Height = 136, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(38, 18, 0, 0), IsHitTestVisible = false }; Grid.SetColumnSpan(gridlines, summary.ByMonth.Count + 1);
            foreach (double fraction in new[] { 0d, .5d, 1d }) { var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, (1 - fraction) * 135, 0, 0) }; line.SetResourceReference(Border.BackgroundProperty, "Line"); gridlines.Children.Add(line); } chart.Children.Insert(0, gridlines);
            chartPanel.Children.Add(chart); root.Children.Add(Card(chartPanel));
        }
    }
}
