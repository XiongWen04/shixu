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
        readonly StackPanel tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        readonly Button widgetToggle;
        FocusPanel focusPanel;
        StudyStatsPanel studyStats;
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
            SourceInitialized += (s, e) => { NativeDesktop.DarkTitle(this, app.Settings.Theme == "dark"); NativeDesktop.RegisterActivation(this, app.BasePath, app.ShowMain); };
            Closing += (s, e) => { if (!app.Exiting) { e.Cancel = true; Hide(); } };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { app.Edit(null, page == "全部" ? (DateTime?)null : page == "月历" ? selected : DateTime.Today); e.Handled = true; } };
            var layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) }); layout.ColumnDefinitions.Add(new ColumnDefinition()); Content = layout;
            var side = new DockPanel { Margin = new Thickness(16, 25, 16, 18) };
            var sideBorder = new Border { Child = side }; sideBorder.SetResourceReference(Border.BackgroundProperty, "Sidebar"); layout.Children.Add(sideBorder);
            var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); side.Children.Add(footer);
            widgetToggle = Ui.Button("▣  桌面小组件", () => app.ToggleWidget(), "Quiet"); widgetToggle.HorizontalContentAlignment = HorizontalAlignment.Left; widgetToggle.FontSize = 12; widgetToggle.Padding = new Thickness(10, 10, 6, 10); widgetToggle.Margin = new Thickness(0, 0, 0, 8); footer.Children.Add(widgetToggle);
            var settings = Ui.Button("⚙  设置", () => { var window = new SettingsWindow(app) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }; window.ShowDialog(); }, "Quiet"); settings.HorizontalContentAlignment = HorizontalAlignment.Left; footer.Children.Add(settings);
            var offline = Ui.Text("离线使用 · 自动保存", 10, "Muted"); offline.Margin = new Thickness(12, 18, 0, 0); footer.Children.Add(offline);
            var nav = new StackPanel(); side.Children.Add(nav);
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 36) };
            var badge = new Image { Source = Icon, Width = 32, Height = 32 }; brand.Children.Add(badge);
            var name = Ui.Text("拾序", 20, "Text", true); name.Margin = new Thickness(10, 2, 0, 0); brand.Children.Add(name); nav.Children.Add(brand);
            string[] keys = { "今天", "全部", "月历", "番茄钟" }, icons = { "☀", "☷", "▦", "◷" };
            for (int n = 0; n < keys.Length; n++)
            {
                string key = keys[n]; var b = Ui.Button(icons[n] + "   " + key, () => SelectPage(key), "Quiet"); b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Padding = new Thickness(14, 12, 10, 12); b.Margin = new Thickness(0, 0, 0, 8); navigation[key] = b; nav.Children.Add(b);
            }
            var main = new Grid { Margin = new Thickness(26, 24, 26, 16) }; main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition()); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetColumn(main, 1); layout.Children.Add(main);
            var header = new Grid { Margin = new Thickness(0, 0, 0, 22) }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); main.Children.Add(header);
            var titles = new StackPanel(); titles.Children.Add(heading); subtitle.Margin = new Thickness(0, 8, 0, 0); titles.Children.Add(subtitle); header.Children.Add(titles);
            Grid.SetColumn(tools, 1); tools.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(tools);
            Grid.SetRow(body, 1); main.Children.Add(body);
            status.Margin = new Thickness(0, 15, 0, 0); Grid.SetRow(status, 2); main.Children.Add(status);
        }
        public void SelectPage(string name) { page = name; Refresh(); }
        public void RefreshFocus() { if (focusPanel != null) focusPanel.Refresh(); if (page == "学习统计" && studyStats != null) studyStats.Refresh(); }
        public void SelectDay(DateTime date) { app.EnsureOccurrences(new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month))); selected = date.Date; Month = new DateTime(date.Year, date.Month, 1); Refresh(); }
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
                bool active = pair.Key == page || (page == "学习统计" && pair.Key == "番茄钟");
                pair.Value.SetResourceReference(Button.BackgroundProperty, active ? "Soft" : "Sidebar");
                pair.Value.SetResourceReference(Button.ForegroundProperty, active ? "Accent" : "Text");
                pair.Value.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
            tools.Children.Clear();
            if (page == "月历")
            {
                heading.Text = Month.ToString("yyyy年M月"); subtitle.Text = "点击日期查看当天待办";
                AddTool("‹", () => SelectDay(Month.AddMonths(-1)), 32);
                AddTool("本月", () => SelectDay(DateTime.Today));
                AddTool("›", () => SelectDay(Month.AddMonths(1)), 32);
            }
            else if (page == "番茄钟") { heading.Text = "番茄钟"; subtitle.Text = "给这段时间一个名字，专心学完这一轮"; }
            else if (page == "学习统计") { heading.Text = "学习统计"; subtitle.Text = "看看时间去了哪里，也看看自己走了多远"; }
            else { heading.Text = page == "今天" ? "今天" : "全部待办"; subtitle.Text = page == "今天" ? DateTime.Today.ToString("M月d日 dddd") : "有日期与无日期的待办都在这里"; }
            Button theme = null; theme = Ui.Button("换肤", () => app.ThemeMenu(theme)); theme.Margin = new Thickness(8, 0, 0, 0); tools.Children.Add(theme);
            if (page == "番茄钟") AddTool("学习统计", () => SelectPage("学习统计"));
            if (page == "学习统计") AddTool("返回番茄钟", () => SelectPage("番茄钟"));
            if (page != "番茄钟" && page != "学习统计") { var add = Ui.Button("＋ 添加", () => app.Edit(null, page == "全部" ? (DateTime?)null : page == "今天" ? DateTime.Today : selected), "Primary"); add.Margin = new Thickness(8, 0, 0, 0); tools.Children.Add(add); }
            body.Children.Clear(); body.ColumnDefinitions.Clear(); body.RowDefinitions.Clear();
            if (page == "学习统计") { if (studyStats == null) studyStats = new StudyStatsPanel(app); body.Children.Add(studyStats); studyStats.Refresh(true); }
            else if (page == "番茄钟") { if (focusPanel == null) focusPanel = new FocusPanel(app); body.Children.Add(focusPanel); focusPanel.Refresh(true); }
            else if (page == "月历") RenderCalendar(); else RenderList();
            int pending = app.Store.State.Items.Count(i => !i.Done);
            status.Text = page == "番茄钟" || page == "学习统计" ? "学习记录保存在本机  ·  暂停时间不计入" : "本地已保存  ·  " + pending + " 件未完成" + (app.CanUndo ? "  ·  可撤销最近一次删除" : "");
        }
        void AddTool(string text, Action action, double width = 0)
        {
            var b = Ui.Button(text, action); b.Padding = new Thickness(10, 8, 10, 8); if (width > 0) b.Width = width; b.Margin = new Thickness(5, 0, 0, 0); tools.Children.Add(b);
        }
        void RenderCalendar()
        {
            body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(286) });
            var calendar = new Grid { Margin = new Thickness(0, 0, 18, 0) }; calendar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) }); calendar.RowDefinitions.Add(new RowDefinition()); body.Children.Add(calendar);
            var weekdays = new UniformGrid { Rows = 1, Columns = 7 };
            foreach (string name in new[] { "一", "二", "三", "四", "五", "六", "日" }) { var t = Ui.Text(name, 12, "Muted"); t.HorizontalAlignment = HorizontalAlignment.Center; weekdays.Children.Add(t); }
            calendar.Children.Add(weekdays);
            DateTime start = Dates.GridStart(Month);
            int cells = (int)Math.Ceiling(((Month - start).Days + DateTime.DaysInMonth(Month.Year, Month.Month)) / 7.0) * 7; CalendarCellCount = cells;
            var days = new UniformGrid { Columns = 7, Rows = cells / 7 }; Grid.SetRow(days, 1); calendar.Children.Add(days);
            for (int n = 0; n < cells; n++)
            {
                DateTime date = start.AddDays(n); bool isSelected = date == selected, currentMonth = date.Month == Month.Month;
                var stack = new StackPanel();
                var top = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                var number = Ui.Text(date.Day.ToString(), 13, currentMonth ? (isSelected ? "Accent" : "Text") : "Muted", isSelected || date == DateTime.Today); top.Children.Add(number);
                if (date == DateTime.Today) { var today = Ui.Text("●", 8, "Accent"); today.HorizontalAlignment = HorizontalAlignment.Right; today.VerticalAlignment = VerticalAlignment.Center; top.Children.Add(today); }
                stack.Children.Add(top);
                var items = app.Store.State.Items.Where(i => i.Date == Dates.Key(date)).OrderBy(i => i.Done).ThenByDescending(i => i.Important).ThenBy(i => i.Time ?? "99:99").ThenBy(i => i.CreatedUtc).ToList();
                foreach (var item in items.Take(2))
                {
                    var preview = Ui.Text((item.Done ? "✓ " : item.Important ? "★ " : "• ") + (item.Time == null ? "" : item.Time + " ") + item.Title, 10, item.Done || !currentMonth ? "Muted" : "Text");
                    preview.TextWrapping = TextWrapping.NoWrap; preview.TextTrimming = TextTrimming.CharacterEllipsis; preview.Margin = new Thickness(0, 0, 0, 5); if (item.Done) preview.TextDecorations = TextDecorations.Strikethrough; stack.Children.Add(preview);
                }
                if (items.Count > 2) stack.Children.Add(Ui.Text("另有 " + (items.Count - 2) + " 件", 9, "Muted"));
                var day = Ui.Button("", () => SelectDay(date)); day.Content = stack; day.Padding = new Thickness(9, 10, 9, 5); day.MinHeight = 0; day.Margin = new Thickness(1); day.HorizontalContentAlignment = HorizontalAlignment.Stretch; day.VerticalContentAlignment = VerticalAlignment.Top;
                day.SetResourceReference(Button.BackgroundProperty, isSelected ? "Soft" : "Panel"); day.SetResourceReference(Button.BorderBrushProperty, isSelected ? "Accent" : "Line");
                System.Windows.Automation.AutomationProperties.SetName(day, date.ToString("yyyy年M月d日") + "，" + items.Count + " 件待办"); days.Children.Add(day);
            }
            var detail = new DockPanel();
            var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); detail.Children.Add(footer);
            footer.Children.Add(Ui.Button("＋ 添加当天待办", () => app.Edit(null, selected)));
            if (app.CanUndo) { var undo = Ui.Button("撤销删除", app.UndoDelete, "Quiet"); undo.Margin = new Thickness(0, 7, 0, 0); footer.Children.Add(undo); }
            var title = new StackPanel { Margin = new Thickness(0, 0, 0, 18) }; title.Children.Add(Ui.Text(selected.ToString("M月d日"), 21, "Text", true)); title.Children.Add(Ui.Text(selected.ToString("dddd"), 12, "Muted")); DockPanel.SetDock(title, Dock.Top); detail.Children.Add(title);
            var list = new StackPanel();
            var selectedItems = app.Store.State.Items.Where(i => i.Date == Dates.Key(selected)).OrderBy(i => i.Done).ThenByDescending(i => i.Important).ThenBy(i => i.Time ?? "99:99").ThenBy(i => i.CreatedUtc).ToList();
            if (selectedItems.Count == 0) Ui.Empty(list, "这一天还没有安排", "添加一件想完成的事");
            else foreach (var item in selectedItems) list.Children.Add(Ui.TaskRow(app, item, false));
            detail.Children.Add(Ui.Scroll(list)); var detailCard = Ui.Card(detail, new Thickness(16)); Grid.SetColumn(detailCard, 1); body.Children.Add(detailCard);
        }
        void RenderList()
        {
            var panel = new DockPanel(); body.Children.Add(panel);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) }; DockPanel.SetDock(bar, Dock.Top); panel.Children.Add(bar);
            var filter = new CheckBox { Content = "显示已完成", IsChecked = showCompleted, VerticalAlignment = VerticalAlignment.Center };
            filter.Click += (s, e) => { showCompleted = filter.IsChecked == true; Refresh(); }; bar.Children.Add(filter);
            if (app.CanUndo) { var undo = Ui.Button("撤销删除", app.UndoDelete, "Quiet"); undo.Margin = new Thickness(20, 0, 0, 0); bar.Children.Add(undo); }
            var list = new StackPanel(); panel.Children.Add(Ui.Scroll(list));
            var items = app.Store.State.Items.Where(i => showCompleted || !i.Done).ToList();
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
                if (items.Count == 0) Ui.Empty(list, "还没有待办", "添加第一件事，也可以先不设日期");
                else foreach (var item in items.OrderBy(i => i.Done).ThenByDescending(i => i.Important).ThenBy(i => i.Date ?? "9999-99-99").ThenBy(i => i.Time ?? "99:99").ThenBy(i => i.CreatedUtc)) list.Children.Add(Ui.TaskRow(app, item, false));
            }
        }
    }
}
