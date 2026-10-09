using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DeskTodo
{
    static class Ui
    {
        public static TextBlock Text(string text, double size = 13, string color = "Text", bool bold = false)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.ForegroundProperty, color); return t;
        }
        public static Button Button(string text, Action action, string style = null)
        {
            var b = new Button { Content = text };
            if (style != null) b.SetResourceReference(FrameworkElement.StyleProperty, style);
            b.Click += (s, e) => action(); AutomationProperties.SetName(b, text); return b;
        }
        public static Border Card(UIElement content, Thickness padding)
        {
            var border = new Border { Child = content, Padding = padding, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
            border.SetResourceReference(Border.BackgroundProperty, "Panel"); border.SetResourceReference(Border.BorderBrushProperty, "Line"); return border;
        }
        public static ScrollViewer Scroll(UIElement content) { return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; }
        public static void Icon(Window window)
        {
            window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("App.ico"))
                window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        }
        public static FrameworkElement TaskRow(AppController app, TodoItem item, bool compact)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            if (!compact) { row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); }
            var check = new CheckBox { IsChecked = item.Done, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0), ToolTip = "标记完成 / 恢复待办" };
            AutomationProperties.SetName(check, "完成 " + item.Title);
            check.Click += (s, e) => app.Toggle(item.Id, check.IsChecked == true); row.Children.Add(check);
            var content = new StackPanel { Margin = new Thickness(2, 0, 6, 0) };
            var title = Text(item.Title, 14, item.Done ? "Muted" : "Text", true);
            title.MaxHeight = compact ? 44 : 62;
            if (item.Done) title.TextDecorations = TextDecorations.Strikethrough;
            content.Children.Add(title);
            string note = item.Note ?? "";
            if (!String.IsNullOrWhiteSpace(note)) { var n = Text(note, 11, "Muted"); n.MaxHeight = 34; n.Margin = new Thickness(0, 5, 0, 0); content.Children.Add(n); }
            string date = item.Date == null ? "未设日期" : DateTime.ParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("M月d日");
            if (item.Time != null) date += "  " + item.Time;
            if (item.Reminder) date += " · 提醒";
            if (item.SeriesId != null) date += " · 重复";
            if (Dates.Overdue(item, DateTime.Today)) date += " · 逾期";
            var label = Text(date, 10, Dates.Overdue(item, DateTime.Today) ? "Danger" : "Muted"); label.Margin = new Thickness(0, 6, 0, 0); content.Children.Add(label);
            if (!String.IsNullOrWhiteSpace(item.Location)) { var location = Text("地点：" + item.Location, 11, "Muted"); location.MaxHeight = 34; location.Margin = new Thickness(0, 4, 0, 0); location.ToolTip = item.Location; content.Children.Add(location); }
            content.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) app.Edit(item, null); };
            Grid.SetColumn(content, 1); row.Children.Add(content);
            var star = Button(item.Important ? "★" : "☆", () => app.ToggleImportant(item.Id), "Quiet"); star.Padding = new Thickness(0); star.FontSize = 18; star.VerticalAlignment = VerticalAlignment.Top; star.SetResourceReference(System.Windows.Controls.Button.ForegroundProperty, item.Important ? "Accent" : "Muted"); star.ToolTip = item.Important ? "取消重要任务" : "标为重要任务"; AutomationProperties.SetName(star, "重要 " + item.Title); Grid.SetColumn(star, 2); row.Children.Add(star);
            if (!compact)
            {
                var edit = Button("✎", () => app.Edit(item, null), "Quiet"); edit.Padding = new Thickness(0); edit.ToolTip = "编辑"; edit.VerticalAlignment = VerticalAlignment.Top; AutomationProperties.SetName(edit, "编辑 " + item.Title); Grid.SetColumn(edit, 3); row.Children.Add(edit);
                var remove = Button("×", () => app.Delete(item.Id), "Quiet"); remove.Padding = new Thickness(0); remove.ToolTip = "删除当次（可撤销）"; remove.FontSize = 19; remove.VerticalAlignment = VerticalAlignment.Top; AutomationProperties.SetName(remove, "删除 " + item.Title); Grid.SetColumn(remove, 4); row.Children.Add(remove);
            }
            var card = Card(row, new Thickness(compact ? 12 : 14)); card.Margin = new Thickness(0, 0, 0, 9);
            if (compact) { card.SetResourceReference(Border.BackgroundProperty, "GlassCard"); card.SetResourceReference(Border.BorderBrushProperty, "GlassBorder"); }
            var menu = new ContextMenu();
            var editMenu = new MenuItem { Header = "编辑" }; editMenu.Click += (s, e) => app.Edit(item, null); menu.Items.Add(editMenu);
            var deleteMenu = new MenuItem { Header = "删除（可撤销）" }; deleteMenu.Click += (s, e) => app.Delete(item.Id); menu.Items.Add(deleteMenu);
            if (item.SeriesId != null) { var stop = new MenuItem { Header = "停止后续重复" }; stop.Click += (s, e) => app.StopRepeating(item); menu.Items.Add(stop); }
            card.ContextMenu = menu; return card;
        }
        public static void Empty(Panel panel, string title, string hint)
        {
            var box = new StackPanel { Margin = new Thickness(10, 26, 10, 26) };
            var mark = Text("✓", 30, "Accent", true); mark.HorizontalAlignment = HorizontalAlignment.Center; box.Children.Add(mark);
            var t = Text(title, 15, "Text", true); t.TextAlignment = TextAlignment.Center; t.Margin = new Thickness(0, 12, 0, 6); box.Children.Add(t);
            var h = Text(hint, 12, "Muted"); h.TextAlignment = TextAlignment.Center; box.Children.Add(h); panel.Children.Add(box);
        }
    }

    public class EditWindow : Window
    {
        public EditWindow(AppController app, TodoItem existing, DateTime? initialDate)
        {
            Title = existing == null ? "添加待办" : "编辑待办"; Width = 520; Height = Math.Min(700, SystemParameters.WorkArea.Height - 80); MinWidth = 450; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false; Ui.Icon(this);
            SourceInitialized += (s, e) => NativeDesktop.DarkTitle(this, app.Settings.Theme == "dark");
            var root = new DockPanel { Margin = new Thickness(26) }; Content = root;
            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
            var cancel = Ui.Button("取消", () => Close()); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 10, 0); bottom.Children.Add(cancel);
            var fields = new StackPanel(); root.Children.Add(Ui.Scroll(fields));
            fields.Children.Add(Ui.Text(Title, 25, "Text", true));
            var titleLabel = Ui.Text("标题", 12, "Muted"); titleLabel.Margin = new Thickness(0, 20, 0, 6); fields.Children.Add(titleLabel);
            var title = new TextBox { Text = existing == null ? "" : existing.Title, MaxLength = 200 }; AutomationProperties.SetName(title, "待办标题"); fields.Children.Add(title);
            var dateLabel = Ui.Text("日期", 12, "Muted"); dateLabel.Margin = new Thickness(0, 15, 0, 6); fields.Children.Add(dateLabel);
            var dateRow = new StackPanel { Orientation = Orientation.Horizontal };
            var picker = new DatePicker { Width = 200, FirstDayOfWeek = DayOfWeek.Monday, SelectedDateFormat = DatePickerFormat.Long };
            DateTime? date = existing == null ? initialDate : existing.Date == null ? (DateTime?)null : DateTime.ParseExact(existing.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            picker.SelectedDate = date; AutomationProperties.SetName(picker, "待办日期"); dateRow.Children.Add(picker);
            var noDate = new CheckBox { Content = "不设日期", IsChecked = !date.HasValue, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            picker.IsEnabled = date.HasValue;
            dateRow.Children.Add(noDate); fields.Children.Add(dateRow);
            var timeLabel = Ui.Text("时间（可选）", 12, "Muted"); timeLabel.Margin = new Thickness(0, 15, 0, 6); fields.Children.Add(timeLabel);
            var timeRow = new StackPanel { Orientation = Orientation.Horizontal };
            var hours = new ComboBox { Width = 76, Height = 36, ItemsSource = Enumerable.Range(0, 24).Select(v => v.ToString("D2")).ToArray() };
            var minutes = new ComboBox { Width = 76, Height = 36, ItemsSource = Enumerable.Range(0, 60).Select(v => v.ToString("D2")).ToArray() };
            AutomationProperties.SetName(hours, "待办小时"); AutomationProperties.SetName(minutes, "待办分钟");
            string initialTime = existing == null || existing.Time == null ? DateTime.Now.ToString("HH:mm") : existing.Time;
            hours.SelectedItem = initialTime.Substring(0, 2); minutes.SelectedItem = initialTime.Substring(3, 2);
            var clock = new StackPanel { Orientation = Orientation.Horizontal }; clock.Children.Add(hours);
            var colon = Ui.Text(":", 18, "Muted"); colon.Margin = new Thickness(9, 3, 9, 0); clock.Children.Add(colon); clock.Children.Add(minutes); timeRow.Children.Add(clock);
            var timed = new CheckBox { Content = "指定时间", IsChecked = existing != null && existing.Time != null, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), ToolTip = "需先选择日期；不勾选时仅保存日期" }; timeRow.Children.Add(timed); fields.Children.Add(timeRow);
            Action updateClock = () => { bool dated = noDate.IsChecked != true && picker.SelectedDate.HasValue; timed.IsEnabled = dated; if (!dated) timed.IsChecked = false; clock.IsEnabled = dated && timed.IsChecked == true; };
            noDate.Click += (s, e) => { picker.IsEnabled = noDate.IsChecked != true; if (picker.IsEnabled && !picker.SelectedDate.HasValue) picker.SelectedDate = DateTime.Today; updateClock(); };
            timed.Click += (s, e) => updateClock(); picker.SelectedDateChanged += (s, e) => updateClock(); updateClock();
            var reminder = new CheckBox { Content = "到点提醒", IsChecked = existing == null || existing.Reminder, Margin = new Thickness(0, 12, 0, 0), ToolTip = "右下角小通知，无声音；需要日期和时间，程序须保持运行" }; AutomationProperties.SetName(reminder, "到点提醒"); fields.Children.Add(reminder);
            Action updateReminder = () => reminder.IsEnabled = timed.IsChecked == true && noDate.IsChecked != true;
            timed.Checked += (s, e) => updateReminder(); timed.Unchecked += (s, e) => updateReminder(); updateReminder();
            var repeatLabel = Ui.Text("重复", 12, "Muted"); repeatLabel.Margin = new Thickness(0, 15, 0, 6); fields.Children.Add(repeatLabel);
            string[] repeatNames = { "不重复", "每天", "每周", "每月" }; string[] repeatKinds = { null, "daily", "weekly", "monthly" };
            var repeat = new ComboBox { ItemsSource = repeatNames, SelectedIndex = 0, Height = 36 }; AutomationProperties.SetName(repeat, "重复周期"); fields.Children.Add(repeat);
            if (existing != null && existing.SeriesId != null) { var rule = app.Store.State.Series.First(r => r.Id == existing.SeriesId); repeat.SelectedIndex = Array.IndexOf(repeatKinds, rule.Kind); repeat.IsEnabled = false; fields.Children.Add(Ui.Text("编辑只影响当次；右键任务可停止后续重复。", 11, "Muted")); }
            var important = new CheckBox { Content = "重要任务（星标置顶）", IsChecked = existing != null && existing.Important, Margin = new Thickness(0, 12, 0, 0) }; AutomationProperties.SetName(important, "重要任务"); fields.Children.Add(important);
            var locationLabel = Ui.Text("地点（可选）", 12, "Muted"); locationLabel.Margin = new Thickness(0, 15, 0, 6); fields.Children.Add(locationLabel);
            var location = new TextBox { Text = existing == null ? "" : existing.Location ?? "", MaxLength = 200, ToolTip = "例如：会议室、实验室、线上" }; AutomationProperties.SetName(location, "待办地点"); fields.Children.Add(location);
            var noteLabel = Ui.Text("备注（可选）", 12, "Muted"); noteLabel.Margin = new Thickness(0, 15, 0, 6); fields.Children.Add(noteLabel);
            var note = new TextBox { Text = existing == null ? "" : existing.Note ?? "", MaxLength = 4000, Height = 80, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            AutomationProperties.SetName(note, "备注"); fields.Children.Add(note);
            var done = new CheckBox { Content = "已完成", IsChecked = existing != null && existing.Done, Margin = new Thickness(0, 14, 0, 0) }; fields.Children.Add(done);
            var error = Ui.Text("", 12, "Danger"); error.Margin = new Thickness(0, 8, 0, 0); fields.Children.Add(error);
            var save = Ui.Button("保存待办", () =>
            {
                if (String.IsNullOrWhiteSpace(title.Text)) { error.Text = "请填写待办标题。"; title.Focus(); return; }
                if (noDate.IsChecked != true && !picker.SelectedDate.HasValue) { error.Text = "请选择日期，或勾选“不设日期”。"; return; }
                if (timed.IsChecked == true && (hours.SelectedItem == null || minutes.SelectedItem == null)) { error.Text = "请选择小时和分钟。"; return; }
                if (noDate.IsChecked == true && repeat.SelectedIndex > 0 && repeat.IsEnabled) { error.Text = "重复任务需要设置日期。"; return; }
                if (existing != null && existing.SeriesId != null && noDate.IsChecked == true) { error.Text = "重复任务需要日期；请先停止后续重复。"; return; }
                var item = existing == null ? new TodoItem { Id = Guid.NewGuid().ToString("N"), CreatedUtc = DateTime.UtcNow.ToString("o") } : existing.Copy();
                item.Title = title.Text.Trim(); item.Note = note.Text.Trim(); item.Done = done.IsChecked == true;
                item.Date = noDate.IsChecked == true ? null : Dates.Key(picker.SelectedDate.Value);
                item.Time = item.Date != null && timed.IsChecked == true ? hours.SelectedItem + ":" + minutes.SelectedItem : null;
                item.Location = String.IsNullOrWhiteSpace(location.Text) ? null : location.Text.Trim();
                item.Important = important.IsChecked == true; item.Reminder = item.Time != null && reminder.IsChecked == true;
                if (app.SaveTodo(item, existing, repeat.IsEnabled ? repeatKinds[repeat.SelectedIndex] : null)) Close();
            }, "Primary"); save.IsDefault = true; bottom.Children.Add(save);
            Loaded += (s, e) => { title.Focus(); title.SelectAll(); };
        }
    }

    public class ReminderWindow : Window
    {
        readonly System.Windows.Threading.DispatcherTimer dismiss;
        public ReminderWindow(AppController app, System.Collections.Generic.List<TodoItem> items, string destination = "今天", string caption = null)
        {
            Title = "拾序提醒"; Width = 360; SizeToContent = SizeToContent.Height; WindowStyle = WindowStyle.None;
            AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true; Ui.Icon(this);
            var panel = new StackPanel();
            var header = new DockPanel(); var close = Ui.Button("×", () => Close(), "Quiet"); close.ToolTip = "关闭提醒"; close.Padding = new Thickness(6, 0, 6, 0); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
            header.Children.Add(Ui.Text(caption ?? (items.Count == 1 ? "拾序 · 到点提醒" : "拾序 · " + items.Count + " 件待办到点"), 14, "Accent", true)); panel.Children.Add(header);
            foreach (var item in items.Take(3))
            {
                var title = Ui.Text((item.Important ? "★ " : "") + item.Title, 15, "Text", true); title.Margin = new Thickness(0, 12, 0, 4); title.MaxHeight = 44; panel.Children.Add(title);
                var details = Ui.Text(item.Time + (String.IsNullOrWhiteSpace(item.Location) ? "" : " · " + item.Location), 12, "Muted"); details.MaxHeight = 36; details.ToolTip = details.Text; panel.Children.Add(details);
            }
            var hint = Ui.Text(items.Count > 3 ? "还有 " + (items.Count - 3) + " 件，点击查看全部" : "点击查看 · 8 秒后收起", 11, "Muted"); hint.Margin = new Thickness(0, 14, 0, 0); panel.Children.Add(hint);
            var card = Ui.Card(panel, new Thickness(18)); card.Margin = new Thickness(4); Content = card;
            card.MouseLeftButtonUp += (s, e) => { app.ShowMain(); app.Main.SelectPage(destination); Close(); };
            dismiss = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            dismiss.Tick += (s, e) => Close();
            MouseEnter += (s, e) => dismiss.Stop(); MouseLeave += (s, e) => dismiss.Start();
            Loaded += (s, e) => { var area = SystemParameters.WorkArea; Left = area.Right - ActualWidth - 16; Top = area.Bottom - ActualHeight - 16; dismiss.Start(); };
            Closed += (s, e) => dismiss.Stop();
        }
    }

    public class SettingsWindow : Window
    {
        public SettingsWindow(AppController app)
        {
            Title = "拾序设置"; Width = 580; Height = 680; MinWidth = 500; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false; Ui.Icon(this);
            SourceInitialized += (s, e) => NativeDesktop.DarkTitle(this, app.Settings.Theme == "dark");
            var root = new DockPanel { Margin = new Thickness(26) }; Content = root;
            var close = Ui.Button("完成", () => Close(), "Primary"); close.IsCancel = true; close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(0, 18, 0, 0); DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
            var fields = new StackPanel(); root.Children.Add(Ui.Scroll(fields));
            fields.Children.Add(Ui.Text("设置", 26, "Text", true));
            var label = Ui.Text("待办数据位置", 15, "Text", true); label.Margin = new Thickness(0, 24, 0, 8); fields.Children.Add(label);
            var path = new TextBox { Text = app.Store.DirectoryPath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap }; fields.Children.Add(path);
            var location = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var change = Ui.Button("更换位置…", () =>
            {
                using (var folder = new Forms.FolderBrowserDialog { Description = "选择空文件夹；当前待办会复制过去，原数据会保留。", ShowNewFolderButton = true })
                {
                    if (folder.ShowDialog() == Forms.DialogResult.OK && app.MoveData(folder.SelectedPath)) path.Text = app.Store.DirectoryPath;
                }
            }); change.Margin = new Thickness(0, 0, 10, 0); location.Children.Add(change);
            location.Children.Add(Ui.Button("打开文件夹", () => { try { Process.Start("explorer.exe", "\"" + app.Store.DirectoryPath + "\""); } catch (Exception ex) { app.Error("打开文件夹失败", ex); } })); fields.Children.Add(location);
            var hint = Ui.Text("每次修改自动保存，并保留上一份备份。更换位置不会删除原目录的数据。", 12, "Muted"); hint.Margin = new Thickness(0, 10, 0, 0); fields.Children.Add(hint);
            var backup = Ui.Text("备份与恢复", 15, "Text", true); backup.Margin = new Thickness(0, 24, 0, 10); fields.Children.Add(backup);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var export = Ui.Button("导出备份…", () =>
            {
                var picker = new SaveFileDialog { Filter = "待办备份 (*.json)|*.json", FileName = "待办备份-" + DateTime.Now.ToString("yyyyMMdd") + ".json", DefaultExt = ".json" };
                if (picker.ShowDialog(this) == true) try { app.Store.Export(picker.FileName); MessageBox.Show(this, "备份已导出。", "拾序"); } catch (Exception ex) { app.Error("导出失败", ex); }
            }); export.Margin = new Thickness(0, 0, 10, 0); actions.Children.Add(export);
            actions.Children.Add(Ui.Button("导入备份…", () =>
            {
                var picker = new OpenFileDialog { Filter = "待办备份 (*.json)|*.json", CheckFileExists = true };
                if (picker.ShowDialog(this) == true)
                {
                    try { DataStore.ReadState(picker.FileName); } catch (Exception ex) { app.Error("备份文件无效", ex); return; }
                    if (MessageBox.Show(this, "导入会替换当前待办，当前数据会保留为自动备份。\n\n确认导入？", "导入备份", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) app.Import(picker.FileName);
                }
            })); fields.Children.Add(actions);
            var desktopLabel = Ui.Text("桌面小组件", 15, "Text", true); desktopLabel.Margin = new Thickness(0, 24, 0, 10); fields.Children.Add(desktopLabel);
            var widget = new StackPanel { Orientation = Orientation.Horizontal };
            var toggle = Ui.Button(app.Settings.WidgetVisible ? "隐藏小组件" : "显示小组件", () => app.ToggleWidget());
            toggle.Click += (s, e) => toggle.Content = app.Settings.WidgetVisible ? "隐藏小组件" : "显示小组件"; toggle.Margin = new Thickness(0, 0, 10, 0); widget.Children.Add(toggle);
            widget.Children.Add(Ui.Button("找回小组件", () => app.ResetWidget())); fields.Children.Add(widget);
            var info = Ui.Text("拖动小组件顶部可移动，拖动右下角可调整大小。普通软件窗口会盖住它。", 12, "Muted"); info.Margin = new Thickness(0, 10, 0, 0); fields.Children.Add(info);
            var themeLabel = Ui.Text("外观", 15, "Text", true); themeLabel.Margin = new Thickness(0, 24, 0, 10); fields.Children.Add(themeLabel);
            var themeButtons = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var pair in new[] { new[] { "light", "蓝白" }, new[] { "warm", "暖色" }, new[] { "dark", "深色" } })
            {
                string key = pair[0]; var button = Ui.Button(pair[1], () => app.ChangeTheme(key)); button.Margin = new Thickness(0, 0, 10, 0); themeButtons.Children.Add(button);
            }
            fields.Children.Add(themeButtons);
        }
    }
}
