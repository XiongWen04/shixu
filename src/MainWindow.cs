using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace DeskTodo
{
    public class MainWindow : Window
    {
        readonly AppController app;
        readonly Grid body = new Grid();
        readonly TextBlock heading = Ui.Text("", 28, "Text", true);
        readonly TextBlock subtitle = Ui.Text("", 12, "Muted");
        readonly TextBlock status = Ui.Text("", 11, "Muted");
        readonly Grid header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        readonly StackPanel tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        readonly HashSet<string> expandedGroups = new HashSet<string>();
        readonly Button widgetToggle;
        readonly Image backdrop = new Image { Stretch = System.Windows.Media.Stretch.UniformToFill, IsHitTestVisible = false };
        readonly Border backgroundShade = new Border { IsHitTestVisible = false };
        FocusPanel focusPanel;
        StudyStatsPanel studyStats;
        StudyPeriodPanel studyPeriod;
        string taskSearch = "", taskTag = "", agendaQuery = "";
        string page = "月历";
        bool showCompleted = true;
        DateTime selected = DateTime.Today;
        public DateTime Month { get; private set; }
        public int CalendarCellCount { get; private set; }
        public MainWindow(AppController controller)
        {
            app = controller; Title = "拾序"; Width = 1160; Height = 790; MinWidth = 960; MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Ui.Icon(this);
            Month = new DateTime(selected.Year, selected.Month, 1);
            SourceInitialized += (s, e) => { NativeDesktop.DarkTitle(this, app.Settings.Theme == "dark"); NativeDesktop.RegisterActivation(this, app.BasePath, app.ShowMain, app.Exit); };
            Closing += (s, e) => { if (!app.Exiting) { e.Cancel = true; Hide(); } };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { app.Edit(null, page == "全部" ? (DateTime?)null : page == "月历" ? selected : DateTime.Today); e.Handled = true; } };
            var host = new Grid { ClipToBounds = true }; Content = host; host.Children.Add(backdrop); backgroundShade.SetResourceReference(Border.BackgroundProperty, "Surface"); host.Children.Add(backgroundShade); var layout = new DockPanel(); host.Children.Add(layout); UpdateBackground(app.BackgroundImage, app.Settings.BackgroundShade);
            var navigationBar = new Grid { Margin = new Thickness(24, 12, 24, 12) }; navigationBar.ColumnDefinitions.Add(new ColumnDefinition()); navigationBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); navigationBar.ColumnDefinitions.Add(new ColumnDefinition());
            DockPanel.SetDock(navigationBar, Dock.Top); layout.Children.Add(navigationBar);
            var nav = new StackPanel { Orientation = Orientation.Horizontal }; var navCard = Ui.Card(nav, new Thickness(4)); navCard.CornerRadius = new CornerRadius(20); Grid.SetColumn(navCard, 1); navigationBar.Children.Add(navCard);
            string[] keys = { "今天", "全部", "月历", "番茄钟" };
            for (int n = 0; n < keys.Length; n++) { string key = keys[n]; var b = Ui.Button(n == 3 ? "学习" : key, () => SelectPage(key), "Quiet"); System.Windows.Automation.AutomationProperties.SetName(b, "导航 " + key); b.Padding = new Thickness(17, 6, 17, 6); navigation[key] = b; nav.Children.Add(b); }
            var utilities = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetColumn(utilities, 2); navigationBar.Children.Add(utilities);
            widgetToggle = Ui.Button("▣  桌面小组件", () => app.ToggleWidget(), "Quiet"); widgetToggle.FontSize = 11; widgetToggle.Padding = new Thickness(10, 8, 10, 8); utilities.Children.Add(widgetToggle);
            Button theme = null; theme = Ui.Button("换肤", () => app.ThemeMenu(theme), "Quiet"); utilities.Children.Add(theme);
            utilities.Children.Add(Ui.Button("⚙  设置", () => { var window = new SettingsWindow(app) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }; window.ShowDialog(); }, "Quiet"));
            var main = new Grid { Margin = new Thickness(24, 12, 24, 14) }; main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition()); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.Children.Add(main);
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); main.Children.Add(header);
            var titles = new StackPanel(); titles.Children.Add(heading); subtitle.Margin = new Thickness(0, 8, 0, 0); titles.Children.Add(subtitle); header.Children.Add(titles);
            Grid.SetColumn(tools, 1); tools.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(tools);
            Grid.SetRow(body, 1); main.Children.Add(body);
            status.Margin = new Thickness(0, 15, 0, 0); Grid.SetRow(status, 2); main.Children.Add(status);
        }
        public void SelectPage(string name) { page = name; if (name == "今天") { selected = DateTime.Today; Month = new DateTime(selected.Year, selected.Month, 1); } Refresh(); }
        public void UpdateBackground(System.Windows.Media.Imaging.BitmapSource image, double shade) { backdrop.Source = image; backgroundShade.Opacity = shade; backdrop.Visibility = backgroundShade.Visibility = image == null ? Visibility.Collapsed : Visibility.Visible; }
        public void RefreshFocus() { if (focusPanel != null) focusPanel.Refresh(); if (page == "学习统计" && studyStats != null) studyStats.Refresh(); if (page == "学习报告" && studyPeriod != null) studyPeriod.Refresh(); }
        public void SelectDay(DateTime date) { app.EnsureOccurrences(new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month))); selected = date.Date; Month = new DateTime(date.Year, date.Month, 1); if (page == "今天" && selected != DateTime.Today) page = "月历"; Refresh(); }
        public void Refresh()
        {
            bool visible = app.Settings.WidgetVisible;
            widgetToggle.Content = visible ? "▣  隐藏小组件" : "▣  显示小组件";
            widgetToggle.SetResourceReference(Button.BackgroundProperty, visible ? "Soft" : "Sidebar");
            widgetToggle.SetResourceReference(Button.ForegroundProperty, visible ? "Accent" : "Text");
            widgetToggle.ToolTip = visible ? "桌面小组件已开启，点击隐藏" : "桌面小组件已关闭，点击显示";
            System.Windows.Automation.AutomationProperties.SetName(widgetToggle, visible ? "隐藏桌面小组件" : "显示桌面小组件");
            foreach (var pair in navigation)
            {
                bool active = pair.Key == page || ((page == "学习统计" || page == "学习报告") && pair.Key == "番茄钟");
                pair.Value.SetResourceReference(Button.BackgroundProperty, active ? "Soft" : "MainCanvas");
                pair.Value.SetResourceReference(Button.ForegroundProperty, active ? "Accent" : "Text");
                pair.Value.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
            tools.Children.Clear();
            header.Visibility = page == "月历" || page == "今天" || page == "番茄钟" ? Visibility.Collapsed : Visibility.Visible;
            if (page == "月历") { heading.Text = Month.ToString("yyyy年M月"); subtitle.Text = "选择日期查看事项"; }
            else if (page == "番茄钟") { heading.Text = "番茄钟"; subtitle.Text = "给这段时间一个名字，专心学完这一轮"; }
            else if (page == "学习统计") { heading.Text = "学习统计"; subtitle.Text = "看看时间去了哪里，也看看自己走了多远"; }
            else if (page == "学习报告") { heading.Text = "学习周报与月报"; subtitle.Text = "按自然周和自然月回顾实际学习时间"; }
            else { heading.Text = page == "今天" ? "今天" : "全部待办"; subtitle.Text = page == "今天" ? DateTime.Today.ToString("M月d日 dddd") : "有日期与无日期的待办都在这里"; }
            if (page == "学习统计") AddTool("返回番茄钟", () => SelectPage("番茄钟"));
            if (page == "学习统计") AddTool("周报 / 月报", () => SelectPage("学习报告"));
            if (page == "学习报告") { AddTool("学习统计", () => SelectPage("学习统计")); AddTool("返回番茄钟", () => SelectPage("番茄钟")); }
            if (page == "全部") { var add = Ui.Button("＋ 添加", () => app.Edit(null, null), "Primary"); add.Margin = new Thickness(8, 0, 0, 0); tools.Children.Add(add); }
            body.Children.Clear(); body.ColumnDefinitions.Clear(); body.RowDefinitions.Clear();
            if (page == "学习报告") { if (studyPeriod == null) studyPeriod = new StudyPeriodPanel(app); body.Children.Add(studyPeriod); studyPeriod.Refresh(true); }
            else if (page == "学习统计") { if (studyStats == null) studyStats = new StudyStatsPanel(app); body.Children.Add(studyStats); studyStats.Refresh(true); }
            else if (page == "番茄钟") { if (focusPanel == null) focusPanel = new FocusPanel(app); body.Children.Add(focusPanel); focusPanel.Refresh(true); }
            else if (page == "月历") RenderCalendar(); else if (page == "今天") RenderAgenda(); else RenderList();
            int pending = app.Store.State.Items.Count(i => !i.Done);
            status.Text = page == "番茄钟" || page == "学习统计" || page == "学习报告" ? "学习记录保存在本机  ·  暂停时间不计入" : "本地已保存  ·  " + pending + " 件未完成" + (app.CanUndo ? "  ·  可撤销最近一次删除" : "");
        }
        void AddTool(string text, Action action, double width = 0)
        {
            var b = Ui.Button(text, action); b.Padding = new Thickness(10, 8, 10, 8); if (width > 0) b.Width = width; b.Margin = new Thickness(5, 0, 0, 0); tools.Children.Add(b);
        }
        void RenderCalendar()
        {
            body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
            var calendar = new Grid(); calendar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); calendar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) }); calendar.RowDefinitions.Add(new RowDefinition());
            var monthBar = new DockPanel { Margin = new Thickness(0, 0, 0, 16) }; var today = Ui.Button("今天", () => SelectDay(DateTime.Today)); DockPanel.SetDock(today, Dock.Right); monthBar.Children.Add(today);
            var monthControls = new StackPanel { Orientation = Orientation.Horizontal }; monthControls.Children.Add(Ui.Button("‹", () => SelectDay(Month.AddMonths(-1)))); var nextMonth = Ui.Button("›", () => SelectDay(Month.AddMonths(1))); nextMonth.Margin = new Thickness(6, 0, 14, 0); monthControls.Children.Add(nextMonth); var month = Ui.Text(Month.ToString("yyyy年M月"), 21, "Text", true); month.VerticalAlignment = VerticalAlignment.Center; monthControls.Children.Add(month); monthBar.Children.Add(monthControls); calendar.Children.Add(monthBar);
            var weekdays = new UniformGrid { Rows = 1, Columns = 7 }; foreach (string name in new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" }) { var label = Ui.Text(name, 11, "Muted"); label.TextAlignment = TextAlignment.Center; weekdays.Children.Add(label); } Grid.SetRow(weekdays, 1); calendar.Children.Add(weekdays);
            var start = Dates.GridStart(Month); int cells = (int)Math.Ceiling(((Month - start).Days + DateTime.DaysInMonth(Month.Year, Month.Month)) / 7.0) * 7; CalendarCellCount = cells;
            var days = new UniformGrid { Columns = 7, Rows = cells / 7 }; Grid.SetRow(days, 2); calendar.Children.Add(days);
            for (int n = 0; n < cells; n++)
            {
                var date = start.AddDays(n); bool active = date == selected, current = date.Month == Month.Month;
                var content = new StackPanel(); var number = Ui.Text(date.Day.ToString(), 14, active ? "AccentInk" : current ? "Text" : "Muted", active || date == DateTime.Today); number.TextAlignment = TextAlignment.Center; number.VerticalAlignment = VerticalAlignment.Center;
                var badge = new Border { Child = number, Width = 28, Height = 28, CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) }; if (active) badge.SetResourceReference(Border.BackgroundProperty, "Accent"); content.Children.Add(badge);
                var items = app.Store.State.Items.Where(i => i.Date == Dates.Key(date)).OrderBy(i => i.Done).ThenByDescending(i => i.Important).ThenBy(i => i.Time ?? "99:99").ToList();
                foreach (var item in items.Take(2)) { var text = Ui.Text((item.Done ? "✓ " : item.SeriesId != null ? "↻ " : "• ") + (item.Time == null ? "" : item.Time + " ") + item.Title, 10, item.Done || !current ? "Muted" : "Text"); text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis; if (item.Done) text.TextDecorations = TextDecorations.Strikethrough; content.Children.Add(text); }
                if (items.Count > 2) content.Children.Add(Ui.Text("另有 " + (items.Count - 2) + " 件", 9, "Muted"));
                var day = Ui.Button("", () => SelectDay(date), "CalendarDay"); day.Content = content; day.Padding = new Thickness(9, 7, 4, 4); day.MinHeight = 0; day.BorderThickness = active ? new Thickness(1) : new Thickness(n % 7 == 0 ? 1 : 0, n < 7 ? 1 : 0, 1, 1); day.SetResourceReference(Button.BackgroundProperty, active ? "Soft" : "MainCanvas"); day.SetResourceReference(Button.BorderBrushProperty, active ? "Accent" : "Line"); System.Windows.Automation.AutomationProperties.SetName(day, date.ToString("yyyy年M月d日") + "，" + items.Count + " 件待办"); days.Children.Add(day);
            }
            var calendarCard = Ui.Card(calendar, new Thickness(16)); calendarCard.Margin = new Thickness(0, 0, 16, 0); body.Children.Add(calendarCard);
            var detail = new DockPanel(); var title = new DockPanel { Margin = new Thickness(0, 0, 0, 12) }; var dateControls = new StackPanel { Orientation = Orientation.Horizontal }; dateControls.Children.Add(Ui.Button("‹", () => SelectDay(selected.AddDays(-1)), "Quiet")); dateControls.Children.Add(Ui.Button("›", () => SelectDay(selected.AddDays(1)), "Quiet")); DockPanel.SetDock(dateControls, Dock.Right); title.Children.Add(dateControls); var labelDate = new StackPanel(); labelDate.Children.Add(Ui.Text(selected.ToString("M月d日  ddd"), 20, "Text", true)); var selectedItems = app.Store.State.Items.Where(i => i.Date == Dates.Key(selected)).OrderBy(i => i.Done).ThenByDescending(i => i.Important).ThenBy(i => i.Time ?? "99:99").ToList(); labelDate.Children.Add(Ui.Text(selectedItems.Count + " 件待办 · ★ " + selectedItems.Count(i => i.Important), 11, "Muted")); title.Children.Add(labelDate); DockPanel.SetDock(title, Dock.Top); detail.Children.Add(title);
            var actions = new StackPanel { Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); detail.Children.Add(actions);
            actions.Children.Add(Ui.Button("＋ 添加待办", () => app.Edit(null, selected), "Primary"));
            if (app.CanUndo) actions.Children.Add(Ui.Button("撤销删除", app.UndoDelete, "Quiet"));
            var list = new StackPanel(); if (selectedItems.Count == 0) Ui.Empty(list, "这一天还没有安排", "点击下方按钮添加待办"); foreach (var item in selectedItems) list.Children.Add(Ui.TaskRow(app, item, false, true)); detail.Children.Add(Ui.Scroll(list));
            var detailCard = Ui.Card(detail, new Thickness(18)); Grid.SetColumn(detailCard, 1); body.Children.Add(detailCard);
        }
        void RenderAgenda()
        {
            body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
            var agenda = new DockPanel(); var agendaHeader = new StackPanel(); DockPanel.SetDock(agendaHeader, Dock.Top); agenda.Children.Add(agendaHeader);
            var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            var add = Ui.Button("＋ 添加当天待办", () => app.Edit(null, selected), "Primary"); DockPanel.SetDock(add, Dock.Right); add.Margin = new Thickness(12, 0, 0, 0); searchRow.Children.Add(add);
            var searchBox = new DockPanel(); var searchLabel = Ui.Text("搜索", 12, "Muted"); searchLabel.VerticalAlignment = VerticalAlignment.Center; searchLabel.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(searchLabel, Dock.Left); searchBox.Children.Add(searchLabel);
            var search = new TextBox { Text = agendaQuery, MaxLength = 200, ToolTip = "搜索标题、地点、备注和标签" }; System.Windows.Automation.AutomationProperties.SetName(search, "事项搜索"); searchBox.Children.Add(search); searchRow.Children.Add(searchBox); agendaHeader.Children.Add(searchRow);
            agendaHeader.Children.Add(Ui.Text(selected.ToString("M月d日  dddd"), 28, "Text", true));
            var choices = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 16) };
            var completed = new CheckBox { Content = "显示已完成", IsChecked = showCompleted, VerticalAlignment = VerticalAlignment.Center }; choices.Children.Add(completed);
            if (app.CanUndo) { var undo = Ui.Button("撤销删除", app.UndoDelete, "Quiet"); undo.Margin = new Thickness(12, 0, 0, 0); choices.Children.Add(undo); } agendaHeader.Children.Add(choices);
            var list = new StackPanel(); agenda.Children.Add(Ui.Scroll(list));
            Action fill = () =>
            {
                list.Children.Clear();
                var matches = app.Store.State.Items.Where(i => (showCompleted || !i.Done) && TodoSearch.Matches(i, agendaQuery, "")).ToList();
                if (page == "今天")
                {
                    var overdue = matches.Where(i => Dates.Overdue(i, DateTime.Today)).OrderByDescending(i => i.Important).ThenBy(i => i.Date).ThenBy(i => i.Time ?? "99:99").ToList();
                    if (overdue.Count > 0) { var title = Ui.Text("逾期 · " + overdue.Count + " 件", 13, "Danger", true); title.Margin = new Thickness(0, 0, 0, 8); list.Children.Add(title); foreach (var item in overdue) list.Children.Add(Ui.TaskRow(app, item, false, true)); }
                }
                for (int offset = 0; offset < 7; offset++)
                {
                    var date = selected.AddDays(offset);
                    var items = matches.Where(i => i.Date == Dates.Key(date) && (offset < 2 || i.SeriesId == null)).OrderBy(i => i.Done).ThenByDescending(i => i.Important).ThenBy(i => i.Time ?? "99:99").ThenBy(i => i.CreatedUtc).ToList();
                    if (offset > 0 && items.Count == 0) continue;
                    string label = offset == 0 ? selected == DateTime.Today ? "今天" : "当日" : offset == 1 ? selected == DateTime.Today ? "明天" : "次日" : date.ToString("M月d日 dddd");
                    var section = Ui.Text(label + " · " + items.Count + " 件", 13, "Muted", true); section.Margin = new Thickness(0, offset == 0 ? 0 : 16, 0, 8); list.Children.Add(section);
                    if (items.Count == 0) Ui.Empty(list, "这一天没有匹配的事项", "可添加安排，或清除搜索条件");
                    foreach (var item in items) list.Children.Add(Ui.TaskRow(app, item, false, true));
                }
                var hint = Ui.Text("显示所选日期起7天的安排 · 后续重复任务见右侧", 10, "Muted"); hint.Margin = new Thickness(0, 18, 0, 0); list.Children.Add(hint);
            };
            search.TextChanged += (s, e) => { agendaQuery = search.Text; fill(); }; completed.Click += (s, e) => { showCompleted = completed.IsChecked == true; fill(); }; fill();
            var agendaCard = Ui.Card(agenda, new Thickness(22)); agendaCard.Margin = new Thickness(0, 0, 20, 0); body.Children.Add(agendaCard);
            var right = new DockPanel(); Grid.SetColumn(right, 1); body.Children.Add(right);
            var calendar = new StackPanel(); var monthBar = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var controls = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(controls, Dock.Right); monthBar.Children.Add(controls);
            foreach (var button in new[] { Ui.Button("‹", () => SelectDay(Month.AddMonths(-1)), "Quiet"), Ui.Button("本月", () => SelectDay(DateTime.Today), "Quiet"), Ui.Button("›", () => SelectDay(Month.AddMonths(1)), "Quiet") }) { button.Padding = new Thickness(7, 3, 7, 3); controls.Children.Add(button); }
            var month = Ui.Text(Month.ToString("yyyy年M月"), 19, "Text", true); month.VerticalAlignment = VerticalAlignment.Center; monthBar.Children.Add(month); calendar.Children.Add(monthBar);
            var weekdays = new UniformGrid { Rows = 1, Columns = 7, Margin = new Thickness(0, 0, 0, 8) };
            foreach (string name in new[] { "一", "二", "三", "四", "五", "六", "日" }) { var t = Ui.Text(name, 11, "Muted"); t.TextAlignment = TextAlignment.Center; weekdays.Children.Add(t); } calendar.Children.Add(weekdays);
            DateTime start = Dates.GridStart(Month); int cells = (int)Math.Ceiling(((Month - start).Days + DateTime.DaysInMonth(Month.Year, Month.Month)) / 7.0) * 7; CalendarCellCount = cells;
            var days = new UniformGrid { Columns = 7, Rows = cells / 7, Height = cells / 7 * 48 }; calendar.Children.Add(days);
            for (int n = 0; n < cells; n++)
            {
                var date = start.AddDays(n); bool isSelected = date == selected; var items = app.Store.State.Items.Where(i => i.Date == Dates.Key(date)).ToList();
                var content = new StackPanel(); var number = Ui.Text(date.Day.ToString(), 14, isSelected ? "Accent" : date.Month == Month.Month ? "Text" : "Muted", isSelected || date == DateTime.Today); number.TextAlignment = TextAlignment.Center; content.Children.Add(number);
                var dot = Ui.Text(items.Count == 0 ? "" : items.Any(i => !i.Done) ? "●" : "✓", 8, "Accent"); dot.TextAlignment = TextAlignment.Center; content.Children.Add(dot);
                var day = Ui.Button("", () => SelectDay(date), "CalendarDay"); day.Content = content; day.Padding = new Thickness(2, 5, 2, 0); day.MinHeight = 0; day.BorderThickness = isSelected ? new Thickness(1) : new Thickness(n % 7 == 0 ? 1 : 0, n < 7 ? 1 : 0, 1, 1);
                day.SetResourceReference(Button.BackgroundProperty, isSelected ? "Soft" : "MainCanvas"); day.SetResourceReference(Button.BorderBrushProperty, isSelected ? "Accent" : "Line");
                day.ToolTip = date.ToString("M月d日") + " · " + items.Count + " 件待办"; System.Windows.Automation.AutomationProperties.SetName(day, date.ToString("yyyy年M月d日") + "，" + items.Count + " 件待办"); days.Children.Add(day);
            }
            var calendarCard = Ui.Card(calendar, new Thickness(18)); calendarCard.Margin = new Thickness(0, 0, 0, 16); DockPanel.SetDock(calendarCard, Dock.Top); right.Children.Add(calendarCard);
            var repeats = new StackPanel(); repeats.Children.Add(Ui.Text("本月重复任务", 17, "Text", true));
            var groups = app.Store.State.Items.Where(i => i.SeriesId != null && String.CompareOrdinal(i.Date, Dates.Key(selected)) >= 0 && String.CompareOrdinal(i.Date, Dates.Key(Month.AddMonths(1).AddDays(-1))) <= 0).GroupBy(i => i.SeriesId).OrderBy(g => g.Min(i => i.Date)).ToList();
            if (groups.Count == 0) { var empty = Ui.Text("所选日期至月末暂无重复安排", 12, "Muted"); empty.Margin = new Thickness(0, 12, 0, 0); repeats.Children.Add(empty); }
            foreach (var group in groups)
            {
                var occurrences = group.OrderBy(i => i.Date).ToList(); var rule = app.Store.State.Series.First(r => r.Id == group.Key); var panel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
                panel.Children.Add(Ui.Text("↻ " + rule.Template.Title, 14, "Text", true));
                var range = Ui.Text(occurrences[0].Date + " – " + occurrences.Last().Date + " · " + occurrences.Count + " 次", 11, "Muted"); range.Margin = new Thickness(0, 5, 0, 5); panel.Children.Add(range);
                panel.Children.Add(Ui.Text("已完成 " + occurrences.Count(i => i.Done) + " · 未完成 " + occurrences.Count(i => !i.Done), 11, "Muted"));
                var next = occurrences.FirstOrDefault(i => !i.Done); if (next != null) { var view = Ui.Button("查看下一次 · " + next.Date, () => SelectDay(DateTime.ParseExact(next.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), "Quiet"); view.HorizontalAlignment = HorizontalAlignment.Left; view.Padding = new Thickness(0, 7, 0, 0); panel.Children.Add(view); }
                repeats.Children.Add(panel);
            }
            right.Children.Add(Ui.Card(Ui.Scroll(repeats), new Thickness(18)));
        }
        void RenderList()
        {
            var panel = new DockPanel(); body.Children.Add(Ui.Card(panel, new Thickness(22)));
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) }; DockPanel.SetDock(bar, Dock.Top); panel.Children.Add(bar);
            var filter = new CheckBox { Content = "显示已完成", IsChecked = showCompleted, VerticalAlignment = VerticalAlignment.Center };
            filter.Click += (s, e) => { showCompleted = filter.IsChecked == true; Refresh(); }; bar.Children.Add(filter);
            if (app.CanUndo) { var undo = Ui.Button("撤销删除", app.UndoDelete, "Quiet"); undo.Margin = new Thickness(20, 0, 0, 0); bar.Children.Add(undo); }
            var list = new StackPanel(); panel.Children.Add(Ui.Scroll(list));
            if (page == "全部")
            {
                var search = new TextBox { Text = taskSearch, Width = 280, MaxLength = 200, Margin = new Thickness(20, 0, 12, 0), ToolTip = "搜索标题、备注、地点和标签" }; System.Windows.Automation.AutomationProperties.SetName(search, "待办搜索"); bar.Children.Add(search);
                var tagChoices = new[] { "全部标签" }.Concat(app.Store.State.Items.SelectMany(i => i.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).Select(t => "#" + t)).ToList();
                int tagIndex = taskTag == "" ? 0 : tagChoices.FindIndex(1, t => String.Equals(t, "#" + taskTag, StringComparison.OrdinalIgnoreCase)); if (tagIndex < 0) { tagIndex = 0; taskTag = ""; }
                var tags = new ComboBox { Width = 150, ItemsSource = tagChoices, SelectedIndex = tagIndex }; System.Windows.Automation.AutomationProperties.SetName(tags, "待办标签筛选"); bar.Children.Add(tags);
                search.TextChanged += (s, e) => { taskSearch = search.Text; FillList(list); };
                tags.SelectionChanged += (s, e) => { taskTag = tags.SelectedIndex <= 0 ? "" : ((string)tags.SelectedItem).Substring(1); FillList(list); };
                var clear = Ui.Button("清除", () => { search.Text = ""; tags.SelectedIndex = 0; }, "Quiet"); bar.Children.Add(clear);
            }
            FillList(list);
        }
        void FillList(StackPanel list)
        {
            list.Children.Clear();
            var items = app.Store.State.Items.Where(i => (showCompleted || !i.Done) && (page != "全部" || TodoSearch.Matches(i, taskSearch, taskTag))).ToList();
            if (page == "今天")
            {
                var overdue = items.Where(i => Dates.Overdue(i, DateTime.Today)).OrderByDescending(i => i.Important).ThenBy(i => i.Date).ThenBy(i => i.Time ?? "99:99").ToList();
                if (overdue.Count > 0) { var h = Ui.Text("逾期 · " + overdue.Count + " 件", 13, "Danger", true); h.Margin = new Thickness(0, 0, 0, 12); list.Children.Add(h); foreach (var item in overdue) list.Children.Add(Ui.TaskRow(app, item, false)); }
                var today = items.Where(i => i.Date == Dates.Key(DateTime.Today)).OrderBy(i => i.Done).ThenByDescending(i => i.Important).ThenBy(i => i.Time ?? "99:99").ThenBy(i => i.CreatedUtc).ToList();
                var label = Ui.Text("今天 · " + today.Count + " 件", 13, "Muted", true); label.Margin = new Thickness(0, overdue.Count > 0 ? 14 : 0, 0, 12); list.Children.Add(label);
                if (today.Count == 0) Ui.Empty(list, "今天还没有安排", "点右上角“添加”，记下今天要做的事");
                else foreach (var item in today) list.Children.Add(Ui.TaskRow(app, item, false));
            }
            else
            {
                if (items.Count == 0) Ui.Empty(list, "没有匹配的待办", "清除搜索或标签筛选，也可以添加新的待办");
                else foreach (var group in items.GroupBy(TodoGroups.Key).OrderBy(g => g.All(i => i.Done)).ThenByDescending(g => g.Any(i => i.Important)).ThenBy(g => g.Min(i => i.Date ?? "9999-99-99")).ThenBy(g => g.Min(i => i.Time ?? "99:99")))
                {
                    var occurrences = group.OrderBy(i => i.Date).ThenBy(i => i.Time ?? "99:99").ThenBy(i => i.CreatedUtc).ToList();
                    if (occurrences.Count == 1 || (occurrences[0].SeriesId == null && occurrences.Select(i => i.Date).Distinct().Count() == 1)) { foreach (var occurrence in occurrences) list.Children.Add(Ui.TaskRow(app, occurrence, false)); continue; }
                    string key = group.Key;
                    var rule = app.Store.State.Series.FirstOrDefault(r => r.Id == occurrences[0].SeriesId);
                    string name = rule == null ? occurrences[0].Title : rule.Template.Title;
                    var heading = new StackPanel(); heading.Children.Add(Ui.Text((occurrences.Any(i => i.Important) ? "★ " : "") + name, 15, "Text", true));
                    Func<string, string> formatDate = value => value == null ? "未设日期" : DateTime.ParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).ToString("yyyy/M/d"); string range = formatDate(occurrences[0].Date) + " – " + formatDate(occurrences.Last().Date);
                    var summary = Ui.Text(range + "  ·  " + occurrences.Count + " 次  ·  " + occurrences.Count(i => !i.Done) + " 未完成  ·  点击展开", 11, "Muted"); summary.Margin = new Thickness(0, 5, 0, 0); heading.Children.Add(summary);
                    var content = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
                    var expander = new Expander { Header = heading, Content = content, IsExpanded = expandedGroups.Contains(key), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                    expander.SetResourceReference(Expander.ForegroundProperty, "Text"); System.Windows.Automation.AutomationProperties.SetName(expander, "重复任务 " + name);
                    Action fill = () => { if (content.Children.Count == 0) foreach (var occurrence in occurrences) content.Children.Add(Ui.TaskRow(app, occurrence, false)); };
                    expander.Expanded += (s, e) => { expandedGroups.Add(key); summary.Text = range + "  ·  " + occurrences.Count + " 次  ·  " + occurrences.Count(i => !i.Done) + " 未完成  ·  点击收起"; fill(); };
                    expander.Collapsed += (s, e) => { expandedGroups.Remove(key); summary.Text = range + "  ·  " + occurrences.Count + " 次  ·  " + occurrences.Count(i => !i.Done) + " 未完成  ·  点击展开"; }; if (expander.IsExpanded) { summary.Text = range + "  ·  " + occurrences.Count + " 次  ·  " + occurrences.Count(i => !i.Done) + " 未完成  ·  点击收起"; fill(); }
                    var card = Ui.Card(expander, new Thickness(16)); card.Margin = new Thickness(0, 0, 0, 10); list.Children.Add(card);
                }
            }
        }
    }
}
