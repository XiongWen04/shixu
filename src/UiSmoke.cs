using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DeskTodo
{
    static class UiSmoke
    {
        static int passed;
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(NativeDesktop.Point point);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flag);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder buffer, int count);
        [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint relation);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        static void Pump()
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        static void NativeWidgetPixels(AppController app, string root)
        {
            Pump();
            IntPtr previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                var realBounds = NativeDesktop.Bounds(app.Widget);
                var handle = new WindowInteropHelper(app.Widget).Handle;
                var probe = new NativeDesktop.Point { X = realBounds.Left + 10, Y = realBounds.Top + 80 };
                if (WindowFromPoint(probe) == handle)
                {
                    using (var screen = new System.Drawing.Bitmap(realBounds.Right - realBounds.Left, realBounds.Bottom - realBounds.Top))
                    using (var screenGraphics = System.Drawing.Graphics.FromImage(screen))
                    {
                        screenGraphics.CopyFromScreen(realBounds.Left, realBounds.Top, 0, 0, screen.Size);
                        screen.Save(Path.Combine(root, "physical-widget.png"), System.Drawing.Imaging.ImageFormat.Png);
                        var actual = screen.GetPixel(10, 80);
                        Check(actual.R + actual.G + actual.B > 18, "visible glass widget is not an empty black surface");
                    }
                }
            }
            finally { if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi); }
            var mainBounds = NativeDesktop.Bounds(app.Main);
            using (var baseline = new System.Drawing.Bitmap(mainBounds.Right - mainBounds.Left, mainBounds.Bottom - mainBounds.Top))
            using (var baselineGraphics = System.Drawing.Graphics.FromImage(baseline))
            {
                IntPtr mainDc = baselineGraphics.GetHdc();
                try { PrintWindow(new WindowInteropHelper(app.Main).Handle, mainDc, 2); } finally { baselineGraphics.ReleaseHdc(mainDc); }
                baseline.Save(Path.Combine(root, "native-main.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            var bounds = NativeDesktop.Bounds(app.Widget);
            using (var bitmap = new System.Drawing.Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                IntPtr dc = graphics.GetHdc(); bool printed;
                try { printed = PrintWindow(new WindowInteropHelper(app.Widget).Handle, dc, 2); } finally { graphics.ReleaseHdc(dc); }
                bitmap.Save(Path.Combine(root, "native-widget.png"), System.Drawing.Imaging.ImageFormat.Png);
                int black = 0, sampled = 0;
                for (int y = 12; y < bitmap.Height - 12; y += 8) for (int x = 12; x < bitmap.Width - 12; x += 8) { var pixel = bitmap.GetPixel(x, y); sampled++; if (pixel.R + pixel.G + pixel.B < 18) black++; }
                Check(printed && sampled > 0 && black < sampled * 0.85, "native desktop widget paints visible content instead of black");
            }
        }
        static IEnumerable<T> Controls<T>(DependencyObject root) where T : DependencyObject
        {
            for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
            {
                var child = VisualTreeHelper.GetChild(root, n);
                if (child is T) yield return (T)child;
                foreach (var nested in Controls<T>(child)) yield return nested;
            }
        }
        static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); passed++; }
        static void Capture(Window window, string path)
        {
            window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            int width = (int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right);
            int height = (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            bitmap.Render(background); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
        }
        public static void RunLayout(AppController app, string root)
        {
            try
            {
                var day = new DateTime(2026, 10, 22);
                app.Store.ChangeState(state =>
                {
                    string[] names = { "组会 · 论文进展讨论", "整理对话推荐文献", "提交课程作业", "实验方案梳理", "志愿服务", "阅读论文", "整理实验结果", "文献汇报" };
                    int[] dates = { 22, 22, 22, 12, 16, 19, 26, 28 };
                    string[] times = { "14:00", "19:30", "21:00", "10:00", null, "09:30", "15:00", "14:00" };
                    for (int n = 0; n < names.Length; n++) state.Items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = names[n], Date = "2026-10-" + dates[n].ToString("D2"), Time = times[n], CreatedUtc = DateTime.UtcNow.ToString("o"), Location = n == 0 ? "实验室 B528" : null, Tags = new List<string> { n == 2 ? "课程" : n == 4 ? "生活" : "科研" }, Important = n == 0, Done = n == 2 });
                    state.DailyGoalMinutes = 120;
                    var began = new DateTimeOffset(day.AddHours(9), TimeSpan.FromHours(8));
                    state.FocusHistory.Add(new FocusSession { Id = Guid.NewGuid().ToString("N"), Title = "论文阅读", StartedAt = began.ToString("o"), EndedAt = began.AddMinutes(45).ToString("o"), TargetSeconds = 2700, Completed = true, Slices = new List<FocusSlice> { new FocusSlice { StartedAt = began.ToString("o"), Seconds = 2700 } } });
                    string[] activities = { "校级志愿服务", "研究生学术论坛", "学院实践活动" };
                    for (int n = 0; n < activities.Length; n++) state.Credits.Add(new CreditRecord { Id = Guid.NewGuid().ToString("N"), Title = activities[n], Date = "2026-10-" + (20 - n * 3).ToString(), Points = new[] { 0.5m, 1.25m, 0.75m }[n] });
                });
                var settings = app.Settings.Copy(); settings.WidgetVisible = false; app.SaveSettings(settings); app.Widget.Hide();
                app.SelectBuiltInSkin("sunlit"); app.Main.SelectPage("月历"); app.Main.SelectDay(day); Pump();
                Check(Controls<Button>(app.Main).Count(b => AutomationProperties.GetName(b).StartsWith("导航 ")) == 5, "new sidebar includes all five pages");
                Check(app.Main.CalendarCellCount == 35, "redesigned calendar preserves full month");
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "2026 / 日程") && Controls<TextBlock>(app.Main).Any(t => t.Text == "2 项待完成  /  1 项已完成"), "new month and selected-day hierarchy");
                string unchanged = File.ReadAllText(Path.Combine(app.Store.DirectoryPath, "tasks.json"));
                var more = Controls<Button>(app.Main).First(b => (b.Content as string) == "···"); more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                Check(File.ReadAllText(Path.Combine(app.Store.DirectoryPath, "tasks.json")) == unchanged, "opening task menu does not mutate importance");
                foreach (var cm in Controls<Border>(app.Main).Where(b => b.ContextMenu != null)) cm.ContextMenu.IsOpen = false;
                var importantId = app.Store.State.Items.First(i => i.Important).Id;
                var taskMenu = Controls<Border>(app.Main).Select(b => b.ContextMenu).First(m => m != null && m.Items.OfType<MenuItem>().Any(i => (i.Header as string) == "取消重要"));
                taskMenu.Items.OfType<MenuItem>().First(i => (i.Header as string) == "取消重要").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
                Check(!app.Store.State.Items.First(i => i.Id == importantId).Important, "task menu removes important flag");
                var restoreMenu = Controls<Border>(app.Main).First(b => b.ContextMenu != null && (b.ToolTip as string) == "组会 · 论文进展讨论\n").ContextMenu;
                restoreMenu.Items.OfType<MenuItem>().First(i => (i.Header as string) == "标为重要").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
                Check(app.Store.State.Items.First(i => i.Id == importantId).Important, "task menu restores important flag");
                var check = Controls<CheckBox>(app.Main).First(c => AutomationProperties.GetName(c) == "完成 组会 · 论文进展讨论"); check.IsChecked = true; check.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Pump();
                Check(app.Store.State.Items.First(i => i.Id == importantId).Done, "new task card completion saves");
                check = Controls<CheckBox>(app.Main).First(c => AutomationProperties.GetName(c) == "完成 组会 · 论文进展讨论"); check.IsChecked = false; check.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Pump();
                var deleteMenu = Controls<Border>(app.Main).First(b => b.ContextMenu != null && (b.ToolTip as string) == "组会 · 论文进展讨论\n").ContextMenu;
                deleteMenu.Items.OfType<MenuItem>().First(i => (i.Header as string) == "删除（可撤销）").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
                Check(!app.Store.State.Items.Any(i => i.Id == importantId), "task menu deletes selected task"); app.UndoDelete();
                Check(app.Store.State.Items.Any(i => i.Id == importantId && i.Important && !i.Done), "undo preserves card task details");
                unchanged = File.ReadAllText(Path.Combine(app.Store.DirectoryPath, "tasks.json")); Pump();

                Capture(app.Main, Path.Combine(root, "calendar-sunlit.png"));
                app.SelectBuiltInSkin("mountain"); Pump(); Capture(app.Main, Path.Combine(root, "calendar-mountain.png"));
                app.Main.Width = 960; app.Main.Height = 650; Pump(); Capture(app.Main, Path.Combine(root, "calendar-small.png"));
                var selectedCell = Controls<Button>(app.Main).First(b => AutomationProperties.GetName(b).StartsWith("2026年10月22日，"));
                Check(Controls<Border>(selectedCell).Count(b => AutomationProperties.GetName(b).StartsWith("日期任务 ")) == 1 && Controls<TextBlock>(selectedCell).Any(t => t.Text == "+2"), "small calendar shows one task and remaining count");
                app.Main.SelectDay(new DateTime(2026, 3, 22)); Pump(); Check(app.Main.CalendarCellCount == 42, "six-week month retains all dates in compact layout"); app.Main.SelectDay(day); Pump();
                var add = Controls<Button>(app.Main).First(b => (b.Content as string) == "＋ 添加待办"); var position = add.TransformToAncestor((Visual)app.Main.Content).Transform(new Point(0, 0));
                Check(add.ActualWidth > 150 && position.X + add.ActualWidth <= ((FrameworkElement)app.Main.Content).ActualWidth + 1, "add button fits minimum window");
                Exception failure = null;
                app.Main.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    var editor = Application.Current.Windows.OfType<EditWindow>().FirstOrDefault();
                    try { Check(editor != null && editor.Owner == app.Main, "new calendar uses existing owned editor"); Check(Controls<DatePicker>(editor).First().SelectedDate == day, "calendar editor retains selected date"); }
                    catch (Exception ex) { failure = ex; }
                    finally { if (editor != null) editor.Close(); }
                }));
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (failure != null) throw failure;
                app.Main.Width = 1280; app.Main.Height = 820; app.SelectBuiltInSkin("sunlit");
                foreach (string page in new[] { "今天", "全部", "番茄钟", "学习统计", "学习报告", "综测" })
                {
                    app.Main.SelectPage(page); Pump(); Check(Controls<Button>(app.Main).Any(b => AutomationProperties.GetName(b) == "导航 综测"), "sidebar stays available on " + page); Capture(app.Main, Path.Combine(root, page + ".png"));
                }
                Check(File.ReadAllText(Path.Combine(app.Store.DirectoryPath, "tasks.json")) == unchanged, "navigation and skin switching leave example data unchanged");
                app.Main.SelectPage("月历"); app.Main.SelectDay(day);
                File.WriteAllText(Path.Combine(root, "ui-report.txt"), "LAYOUT PASS: " + passed); app.Exit();
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(root, "ui-report.txt"), ex.ToString()); app.Exit(); Environment.ExitCode = 1; }
        }
        public static void RunDesktop(AppController app, string root)
        {
            try
            {
                var today = DateTime.Today;
                app.Store.ChangeState(state =>
                {
                    state.Items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = "组会", Date = Dates.Key(today), Time = "15:00", Location = "B528", Tags = new List<string> { "会议" } });
                    state.Items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = "整理资料", Date = Dates.Key(today), Time = "19:00", Tags = new List<string> { "科研" } });
                    state.Items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = "实验报告初稿", Date = Dates.Key(today.AddDays(1)), Tags = new List<string> { "科研" } });
                    state.Items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = "和导师沟通", Date = Dates.Key(today.AddDays(2)), Time = "14:00", Location = "办公室" });
                    state.Items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = "逾期待办", Date = Dates.Key(today.AddDays(-1)) });
                    Schedule.AddSeries(state, new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = "刷网课", Date = Dates.Key(today), Tags = new List<string> { "日常" } }, "daily"); Schedule.Expand(state, today.AddDays(62));
                    state.DailyGoalMinutes = 120;
                    var from = new DateTimeOffset(today.AddHours(9), DateTimeOffset.Now.Offset);
                    foreach (int duration in new[] { 25, 45, 8 }) { Study.Start(state, duration == 25 ? "阅读论文" : duration == 45 ? "复习笔记" : "学习英语", duration, from); Study.AddTime(state, from, duration * 60); Study.Finish(state, from.AddMinutes(duration), true); from = from.AddHours(2); }
                });
                string picture = Path.Combine(root, "sample-wallpaper.png"); var drawing = new DrawingVisual();
                using (var dc = drawing.RenderOpen())
                {
                    dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(167, 198, 199), Color.FromRgb(53, 92, 75), 90), null, new Rect(0, 0, 1200, 800));
                    dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(235, 235, 211)), null, new Point(960, 160), 90, 90);
                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(111, 154, 141)), null, Geometry.Parse("M0,500 L200,250 350,450 580,230 770,420 1000,300 1200,460 1200,800 0,800 Z"));
                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(63, 111, 91)), null, Geometry.Parse("M0,650 L280,430 500,650 680,420 860,610 1200,450 1200,800 0,800 Z"));
                }
                var bitmap = new RenderTargetBitmap(1200, 800, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(picture)) encoder.Save(file); app.ImportBackground(picture); app.SetBackgroundShade(0.4);
                app.Main.SelectPage("今天"); app.Main.SelectDay(today); app.Main.UpdateLayout();
                Check(app.Main.CalendarCellCount == 35 || app.Main.CalendarCellCount == 42, "small calendar retains full weeks");
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "本月重复任务") && Controls<TextBlock>(app.Main).Any(t => t.Text == "明天 · 2 件"), "agenda displays following day and repeat summary");
                Capture(app.Main, Path.Combine(root, "c-calendar-warm.png"));
                var search = Controls<TextBox>(app.Main).First(t => AutomationProperties.GetName(t) == "事项搜索"); search.Text = "B528"; app.Main.UpdateLayout();
                Check(Controls<CheckBox>(app.Main).Count(c => AutomationProperties.GetName(c).StartsWith("完成 ")) == 1, "agenda search filters by location");
                var meeting = Controls<CheckBox>(app.Main).First(c => AutomationProperties.GetName(c) == "完成 组会"); meeting.IsChecked = true; meeting.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); app.Main.UpdateLayout();
                Check(app.Store.State.Items.First(i => i.Title == "组会").Done, "compact task completion persists through refresh");
                search = Controls<TextBox>(app.Main).First(t => AutomationProperties.GetName(t) == "事项搜索"); search.Text = "";
                var nextDay = today.AddDays(1); Controls<Button>(app.Main).First(b => AutomationProperties.GetName(b).StartsWith(nextDay.ToString("yyyy年M月d日") + "，")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); app.Main.UpdateLayout();
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text == nextDay.ToString("M月d日  ddd")), "clicking small calendar switches to selected monthly details");
                app.Main.SelectPage("今天"); app.Main.UpdateLayout(); Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "逾期 · 1 件"), "today retains overdue task access");
                foreach (string skin in new[] { "light", "dark", "warm" }) { app.ChangeTheme(skin); app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "c-today-" + skin + ".png")); }
                app.Main.Width = 960; app.Main.Height = 650; app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "c-calendar-small.png"));
                app.Main.SelectPage("番茄钟"); app.Main.UpdateLayout();
                var timerCard = Controls<Border>(app.Main).First(b => AutomationProperties.GetName(b) == "居中专注卡片");
                Check(timerCard.ActualWidth == 600 && timerCard.ActualHeight > 200, "timer is centered in bounded card at small size");
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "1小时18分") && Controls<TextBlock>(app.Main).Any(t => t.Text == "已学 78 分 · 还差 42 分"), "goal summary preserves actual time calculation");
                var drawer = Controls<Expander>(app.Main).First(e => AutomationProperties.GetName(e) == "今日学习记录抽屉"); drawer.IsExpanded = false; drawer.IsExpanded = true; app.Main.UpdateLayout();
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "复习笔记"), "learning drawer reopens recorded sessions");
                drawer.IsExpanded = false; app.Main.UpdateLayout();
                Check(Controls<ScrollViewer>(timerCard).First().ScrollableHeight < 2, "small window shows timer controls and complete goal without scrolling");
                Capture(app.Main, Path.Combine(root, "c-focus-small.png")); app.Main.Width = 1160; app.Main.Height = 790; app.Main.UpdateLayout(); drawer.IsExpanded = true; app.Main.UpdateLayout();
                var studyName = Controls<TextBox>(app.Main).First(t => AutomationProperties.GetName(t) == "学习名称"); studyName.Text = "自由阅读";
                var mode = Controls<ComboBox>(app.Main).First(c => AutomationProperties.GetName(c) == "学习计时模式"); mode.SelectedIndex = 1;
                Controls<Button>(app.Main).First(b => (b.Content as string) == "开始学习").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); for (int n = 0; n < 5; n++) Pump();
                Check(app.FocusRunning && app.FocusSeconds > 0 && app.Store.State.ActiveFocus.IsCountUp, "centered count-up timer starts and advances");
                Controls<Button>(app.Main).First(b => (b.Content as string) == "暂停").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); double stopped = app.FocusSeconds; Pump();
                Check(!app.FocusRunning && app.FocusSeconds == stopped, "pause excludes elapsed wall time");
                Controls<Button>(app.Main).First(b => (b.Content as string) == "继续").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                Controls<Button>(app.Main).First(b => (b.Content as string) == "结束并记录").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(app.Store.State.ActiveFocus == null && app.Store.State.FocusHistory.Last().Title == "自由阅读", "end records actual count-up session"); if (app.ReminderPopup != null) app.ReminderPopup.Close();
                mode.SelectedIndex = 0; studyName.Text = "阅读论文"; foreach (string skin in new[] { "light", "dark", "warm" }) { app.ChangeTheme(skin); app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "c-focus-" + skin + ".png")); }
                Controls<Button>(app.Main).First(b => (b.Content as string) == "学习统计").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); app.Main.UpdateLayout(); Check(Controls<StudyStatsPanel>(app.Main).Any(), "learning statistics remains reachable");
                app.Main.SelectPage("番茄钟"); app.Main.UpdateLayout(); Controls<Button>(app.Main).First(b => (b.Content as string) == "周报 / 月报").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); app.Main.UpdateLayout(); Check(Controls<StudyPeriodPanel>(app.Main).Any(), "weekly and monthly reports remain reachable");
                app.Main.SelectPage("全部"); app.Main.UpdateLayout(); var group = Controls<Expander>(app.Main).First(e => AutomationProperties.GetName(e) == "重复任务 刷网课"); Check(!group.IsExpanded, "all view retains collapsed recurrence groups");
                Capture(app.Main, Path.Combine(root, "c-all.png"));
                app.ResetBackground(); app.Main.SelectPage("月历"); app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "c-calendar-default.png"));
                File.WriteAllText(Path.Combine(root, "ui-report.txt"), "DESKTOP C PASS: " + passed); app.Exit();
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(root, "ui-report.txt"), ex.ToString()); app.Exit(); Environment.ExitCode = 1; }
        }
        public static void RunBackground(AppController app, string root)
        {
            try
            {
                string source = Path.Combine(root, "sample-background.png");
                var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen()) { context.DrawRectangle(new LinearGradientBrush(Color.FromRgb(24, 68, 86), Color.FromRgb(210, 180, 117), 35), null, new Rect(0, 0, 1200, 800)); context.DrawEllipse(new SolidColorBrush(Color.FromRgb(215, 217, 191)), null, new Point(970, 150), 90, 90); }
                var image = new RenderTargetBitmap(1200, 800, 96, 96, PixelFormats.Pbgra32); image.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var file = File.Create(source)) encoder.Save(file);
                Check(app.ImportBackground(source), "local image imports successfully");
                Check(app.BackgroundImage != null && app.BackgroundImage.IsFrozen && app.BackgroundImage.PixelWidth == 1200, "image is bounded cached and preserves aspect ratio");
                string imported = app.Settings.BackgroundFile; Check(SettingsFile.Load(app.SettingsPath).BackgroundFile == imported && File.Exists(Path.Combine(root, "Backgrounds", imported)), "managed image and settings persist");
                File.Move(source, source + ".moved"); app.LoadBackground(); Check(app.BackgroundImage != null, "moving original image does not break saved background");
                var skinButton = Controls<Button>(app.Main).First(b => (b.Content as string) == "换肤");
                string[] skinKeys = { "mountain", "sunlit" }, skinNames = { "山湖晨光", "晴日拾光" };
                var sampleDate = new DateTime(2026, 10, 22);
                app.Store.Change(items => { items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = "示例：组会", Date = Dates.Key(sampleDate), Time = "14:00", Tags = new List<string> { "科研" }, CreatedUtc = DateTime.UtcNow.ToString("o") }); });
                string stateBeforeSkins = File.ReadAllText(Path.Combine(app.Store.DirectoryPath, "tasks.json"));
                for (int n = 0; n < skinKeys.Length; n++)
                {
                    string key = skinKeys[n]; app.ThemeMenu(skinButton); Pump();
                    var skinItem = skinButton.ContextMenu.Items.OfType<MenuItem>().First(i => (i.Header as string) == skinNames[n]);
                    skinItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); skinButton.ContextMenu.IsOpen = false; Pump();
                    Check(app.Settings.BuiltInSkin == key && app.BackgroundImage != null && app.BackgroundImage.IsFrozen && app.BackgroundImage.PixelWidth >= 1200, "built-in menu loads embedded wallpaper " + key);
                    Check(app.Settings.BackgroundFile == imported && File.Exists(Path.Combine(root, "Backgrounds", imported)), "built-in selection preserves custom image " + key);
                    Check(SettingsFile.Load(app.SettingsPath).BuiltInSkin == key, "built-in choice persists " + key);
                    var oldImage = app.BackgroundImage; app.LoadBackground(); Check(app.BackgroundImage != null && app.BackgroundImage != oldImage, "built-in reload decodes embedded resource " + key);
                    Check(((SolidColorBrush)Application.Current.Resources["Panel"]).Opacity >= 0.7 && ((SolidColorBrush)Application.Current.Resources["Panel"]).Opacity < 0.85, "built-in panels reveal scenery " + key);
                    app.ThemeMenu(skinButton); Pump(); Check(skinButton.ContextMenu.Items.OfType<MenuItem>().Single(i => i.IsChecked).Header.ToString() == skinNames[n], "built-in menu has one active selection " + key); skinButton.ContextMenu.IsOpen = false;
                    foreach (string page in new[] { "月历", "今天", "全部", "番茄钟", "学习统计", "学习报告", "综测" })
                    {
                        app.Main.SelectPage(page); if (page == "月历") app.Main.SelectDay(sampleDate); app.Main.UpdateLayout();
                        Check(Controls<Image>(app.Main).Any(i => !i.IsHitTestVisible && i.Visibility == Visibility.Visible && i.Source != null), "built-in backdrop visible on " + page + " " + key);
                        Capture(app.Main, Path.Combine(root, "skin-" + key + "-" + page + ".png"));
                    }
                    app.Main.Width = 960; app.Main.Height = 650; app.Main.SelectPage("月历"); app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "skin-" + key + "-small.png")); app.Main.Width = 1160; app.Main.Height = 790;
                }
                Check(File.ReadAllText(Path.Combine(app.Store.DirectoryPath, "tasks.json")) == stateBeforeSkins, "skin switching leaves task and learning data unchanged");
                Check(app.ResetBackground() && app.Settings.BuiltInSkin == "" && app.BackgroundImage == null, "reset clears built-in selection");
                Check(app.SelectBuiltInSkin("mountain"), "built-in selection remains reusable");
                Check(app.ImportBackground(Path.Combine(root, "Backgrounds", imported)) && app.Settings.BuiltInSkin == "", "custom import leaves built-in mode");
                Check(app.SelectBuiltInSkin("sunlit") && app.ChangeTheme("dark") && app.Settings.BuiltInSkin == "" && app.BackgroundImage != null, "plain theme returns to preserved custom background");
                imported = app.Settings.BackgroundFile; app.Main.SelectPage("月历");
                foreach (string theme in new[] { "warm", "light", "dark" }) { app.ChangeTheme(theme); app.Main.UpdateLayout(); Check(Controls<Image>(app.Main).Any(i => i.Source == app.BackgroundImage && i.Visibility == Visibility.Visible), "background survives theme switching " + theme); Capture(app.Main, Path.Combine(root, "background-" + theme + ".png")); }
                var themeButton = Controls<Button>(app.Main).First(b => (b.Content as string) == "换肤"); app.ThemeMenu(themeButton); Pump();
                var menu = themeButton.ContextMenu; Check(menu.Items.OfType<MenuItem>().Any(i => (string)i.Header == "选择背景图片…") && menu.Items.OfType<MenuItem>().Any(i => (string)i.Header == "恢复默认背景"), "change skin menu exposes choose and restore commands");
                var shadeMenu = menu.Items.OfType<MenuItem>().First(i => (string)i.Header == "背景遮罩"); shadeMenu.Items.OfType<MenuItem>().First(i => (string)i.Header == "40%").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.IsOpen = false;
                Check(SettingsFile.Load(app.SettingsPath).BackgroundShade == 0.4, "background shade menu persists chosen opacity");
                app.Main.SelectPage("番茄钟"); app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "background-focus.png"));
                string invalid = Path.Combine(root, "invalid.png"); File.WriteAllText(invalid, "not an image"); bool rejected = false; try { app.ImportBackground(invalid); } catch { rejected = true; }
                Check(rejected && app.Settings.BackgroundFile == imported && app.BackgroundImage != null, "invalid image preserves existing background");
                Check(app.ResetBackground() && app.BackgroundImage == null && app.Settings.BackgroundFile == "", "reset restores theme and saves default");
                Check(!Controls<Image>(app.Main).Any(i => !i.IsHitTestVisible && i.Visibility == Visibility.Visible), "default background hides image layer");
                var missing = app.Settings.Copy(); missing.BackgroundFile = imported; Check(app.SaveSettings(missing), "saved background selection accepted"); File.Move(Path.Combine(root, "Backgrounds", imported), Path.Combine(root, "Backgrounds", imported + ".moved")); app.LoadBackground(); Check(app.BackgroundImage == null, "missing optional image falls back safely"); app.ResetBackground();
                File.WriteAllText(Path.Combine(root, "ui-report.txt"), "BACKGROUND PASS: " + passed); app.Exit();
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(root, "ui-report.txt"), ex.ToString()); app.Exit(); Environment.ExitCode = 1; }
        }
        public static void RunCredits(AppController app, string root)
        {
            try
            {
                app.Main.SelectPage("综测"); app.Main.UpdateLayout();
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "还没有综测加分记录"), "credits empty state");
                Action<bool, bool> edit = (existing, save) =>
                {
                    Exception failure = null;
                    app.Main.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        var editor = Application.Current.Windows.OfType<CreditWindow>().FirstOrDefault();
                        try
                        {
                            Check(editor != null && editor.Owner == app.Main, "credits owned dialog");
                            var title = Controls<TextBox>(editor).First(t => AutomationProperties.GetName(t) == "活动名称");
                            var points = Controls<TextBox>(editor).First(t => AutomationProperties.GetName(t).StartsWith("加分分数"));
                            var date = Controls<DatePicker>(editor).First();
                            if (existing) Check(title.Text == "示例活动：志愿服务" && points.Text == "0.5", "credit edit prefills values");
                            if (save)
                            {
                                title.Text = " "; points.Text = "0.5";
                                Controls<Button>(editor).First(b => (b.Content as string) == "保存加分").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                Check(editor.IsVisible, "empty activity stays in dialog");
                                title.Text = existing ? "示例活动：学术讲座" : "示例活动：志愿服务"; date.SelectedDate = new DateTime(2026, 10, 22);
                                points.Text = "abc"; Controls<Button>(editor).First(b => (b.Content as string) == "保存加分").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(editor.IsVisible, "invalid decimal stays in dialog");
                                points.Text = existing ? "1.25" : "0.5";
                                Capture(editor, Path.Combine(root, "credit-editor.png"));
                                Controls<Button>(editor).First(b => (b.Content as string) == "保存加分").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            }
                        }
                        catch (Exception ex) { failure = ex; }
                        finally { if (editor != null && editor.IsVisible) editor.Close(); }
                    }));
                    if (existing) Controls<Button>(app.Main).First(b => (b.Content as string) == "修改").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else Controls<Button>(app.Main).First(b => (b.Content as string) == "＋ 添加加分").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (failure != null) throw failure;
                    app.Main.UpdateLayout();
                };
                edit(false, false); Check(app.Store.State.Credits.Count == 0, "cancel preserves records");
                edit(false, true); Check(app.Store.State.Credits.Single().Points == 0.5m && app.Store.State.Credits[0].Date == "2026-10-22", "dialog adds credit");
                edit(true, true); Check(app.Store.State.Credits.Single().Points == 1.25m && app.Store.State.Credits[0].Title == "示例活动：学术讲座", "dialog edits without duplication");
                app.SaveCredit(new CreditRecord { Id = Guid.NewGuid().ToString("N"), Title = "示例活动：志愿服务", Date = "2026-10-20", Points = 0.5m }, false);
                foreach (string theme in new[] { "warm", "light", "dark" })
                {
                    app.ChangeTheme(theme); app.Main.UpdateLayout();
                    Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "1.75 分") && Controls<TextBlock>(app.Main).Any(t => t.Text == "2 项活动"), "credits total " + theme);
                    Capture(app.Main, Path.Combine(root, "credits-" + theme + ".png"));
                }
                app.Main.Width = 960; app.Main.Height = 650; app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "credits-small.png"));
                Check(Controls<Button>(app.Main).Where(b => AutomationProperties.GetName(b).StartsWith("导航 ")).All(b => b.ActualWidth > 0), "credit navigation available at minimum size");
                Controls<Button>(app.Main).First(b => (b.Content as string) == "删除").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(app.Store.State.Credits.Single().Points == 0.5m, "delete removes selected record");
                app.Main.SelectPage("月历"); app.Main.UpdateLayout(); Check(app.Main.CalendarCellCount >= 28, "calendar still renders after credits");
                Exception taskError = null;
                app.Main.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    var editor = Application.Current.Windows.OfType<EditWindow>().FirstOrDefault();
                    try
                    {
                        Check(editor != null, "task credit editor opens");
                        Controls<TextBox>(editor).First(t => AutomationProperties.GetName(t) == "待办标题").Text = "示例活动：校园志愿服务";
                        var enabled = Controls<CheckBox>(editor).First(t => AutomationProperties.GetName(t) == "综测活动");
                        Check(enabled.IsChecked != true, "ordinary task defaults to no credit"); enabled.IsChecked = true;
                        var points = Controls<TextBox>(editor).First(t => AutomationProperties.GetName(t) == "任务综测分数");
                        var save = Controls<Button>(editor).First(b => (b.Content as string) == "保存待办");
                        points.Text = "0"; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(editor.IsVisible, "zero task credit rejected in editor");
                        points.Text = "0.5"; editor.UpdateLayout(); Capture(editor, Path.Combine(root, "task-credit-editor.png"));
                        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(!editor.IsVisible, "task credit editor saves");
                    }
                    catch (Exception ex) { taskError = ex; }
                    finally { if (editor != null && editor.IsVisible) editor.Close(); }
                }));
                app.Edit(null, new DateTime(2026, 10, 23)); if (taskError != null) throw taskError;
                var task = app.Store.State.Items.Single(i => i.Title == "示例活动：校园志愿服务");
                Check(task.CreditPoints == .5m && app.Store.State.Credits.Count == 1, "pending activity does not add credit");
                app.Toggle(task.Id, true); app.Toggle(task.Id, true);
                Check(app.Store.State.Credits.Count == 2 && app.Store.State.Credits.Single(c => c.SourceTaskId == task.Id).Points == .5m, "controller completes without duplicate credit");
                app.Toggle(task.Id, false); Check(app.Store.State.Credits.Count == 1, "controller undo retracts linked credit only");
                app.Toggle(task.Id, true); task = app.Store.State.Items.Single(i => i.Id == task.Id).Copy(); task.CreditPoints = 1.5m;
                app.SaveTodo(task, task, null); Check(app.Store.State.Credits.Single(c => c.SourceTaskId == task.Id).Points == 1.5m, "completed task edit updates credit");
                app.Main.SelectPage("综测"); app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "task-credit-result.png"));
                File.WriteAllText(Path.Combine(root, "ui-report.txt"), "CREDITS PASS: " + passed); app.Exit();
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(root, "ui-report.txt"), ex.ToString()); app.Exit(); Environment.ExitCode = 1; }
        }
        public static void RunCalendar(AppController app, string root)
        {
            try
            {
                app.Main.SelectDay(new DateTime(2026, 10, 11)); app.Main.UpdateLayout();
                Action<DateTime, bool> openEditor = (expected, save) =>
                {
                    Exception popupError = null;
                    Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        EditWindow editor = null;
                        try
                        {
                            editor = Application.Current.Windows.OfType<EditWindow>().Single(); editor.UpdateLayout();
                            Check(editor.IsVisible && editor.Owner == app.Main, "calendar button opens owned editor dialog");
                            Check(Controls<DatePicker>(editor).First(p => AutomationProperties.GetName(p) == "待办日期").SelectedDate == expected, "editor defaults to selected calendar day");
                            if (save)
                            {
                                Controls<TextBox>(editor).First(t => AutomationProperties.GetName(t) == "待办标题").Text = "组会";
                                Controls<TextBox>(editor).First(t => AutomationProperties.GetName(t) == "待办地点").Text = "B528";
                                Controls<TextBox>(editor).First(t => AutomationProperties.GetName(t) == "待办标签").Text = "会议，科研";
                                Controls<Button>(editor).First(b => (b.Content as string) == "保存待办").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            }
                        }
                        catch (Exception ex) { popupError = ex; }
                        finally { if (editor != null && editor.IsVisible) editor.Close(); }
                    }));
                    Controls<Button>(app.Main).First(b => (b.Content as string) == "＋ 添加待办").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (popupError != null) throw popupError; app.Main.UpdateLayout();
                };
                openEditor(new DateTime(2026, 10, 11), true);
                Check(app.Store.State.Items.Any(i => i.Title == "组会" && i.Date == "2026-10-11" && i.Location == "B528" && i.Tags.Count == 2), "dialog saves full task details and refreshes calendar");
                Check(!Controls<TextBox>(app.Main).Any(t => AutomationProperties.GetName(t).StartsWith("快速待办")), "inline quick form is removed");
                foreach (string theme in new[] { "warm", "light", "dark" })
                {
                    app.ChangeTheme(theme); app.Main.UpdateLayout();
                    var days = Controls<Button>(app.Main).Where(b => b.Style == Application.Current.Resources["CalendarDay"]).ToList();
                    Check(days.Count == 35, "calendar full weeks " + theme);
                    Check(days.All(b => ((Border)b.Template.FindName("Frame", b)).CornerRadius.TopLeft == 10), "calendar cells use rounded frames " + theme);
                    Check(days.All(b => b.Margin.Left == 2 && b.Margin.Right == 2), "calendar cells retain breathing room " + theme);
                    Check(days.Count(b => b.BorderThickness.Left == 1) == 1, "only selected date has full outline " + theme);
                    Capture(app.Main, Path.Combine(root, "calendar-" + theme + ".png"));
                }
                var nextDay = new DateTime(2026, 10, 12); Controls<Button>(app.Main).First(b => AutomationProperties.GetName(b).StartsWith(nextDay.ToString("yyyy年M月d日") + "，")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); app.Main.UpdateLayout();
                int beforeCancel = app.Store.State.Items.Count; openEditor(nextDay, false); Check(app.Store.State.Items.Count == beforeCancel, "cancelled dialog leaves tasks unchanged");
                app.Main.SelectDay(new DateTime(2026, 10, 11)); app.Main.Width = 960; app.Main.Height = 650; app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "calendar-small.png"));
                File.WriteAllText(Path.Combine(root, "ui-report.txt"), "CALENDAR PASS: " + passed); app.Exit();
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(root, "ui-report.txt"), ex.ToString()); app.Exit(); Environment.ExitCode = 1; }
        }
        public static void Run(AppController app, string root)
        {
            try
            {
                Check(app.Main.IsVisible && app.Widget.IsVisible, "both native windows visible");
                var widgetToggle = Controls<Button>(app.Main).First(b => (b.Content as string ?? "").Contains("小组件"));
                Check((widgetToggle.Content as string) == "▣  隐藏小组件", "widget button reflects visible state");
                widgetToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!app.Widget.IsVisible && (widgetToggle.Content as string) == "▣  显示小组件", "widget button changes after hiding");
                widgetToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(app.Widget.IsVisible && (widgetToggle.Content as string) == "▣  隐藏小组件", "widget button changes after showing");
                Check(Controls<Thumb>(app.Widget).Any(t => AutomationProperties.GetName(t) == "移动小组件"), "widget has a full-header drag control");
                app.Widget.UpdateLayout(); app.Widget.UpdateDesktopSurface();
                Check(((WidgetSurface)app.Widget.Content).HasWallpaper, "glass backdrop loads the desktop wallpaper");
                var drag = Controls<Thumb>(app.Widget).First(t => AutomationProperties.GetName(t) == "移动小组件");
                var beforeDrag = NativeDesktop.Bounds(app.Widget); double dragScale = NativeDesktop.Scale(app.Widget);
                drag.RaiseEvent(new DragDeltaEventArgs(-30, 20) { RoutedEvent = Thumb.DragDeltaEvent });
                var afterDrag = NativeDesktop.Bounds(app.Widget);
                Check(afterDrag.Left == beforeDrag.Left - (int)Math.Round(30 * dragScale) && afterDrag.Top == beforeDrag.Top + (int)Math.Round(20 * dragScale), "header drag moves the native widget");
                drag.RaiseEvent(new DragCompletedEventArgs(-30, 20, false) { RoutedEvent = Thumb.DragCompletedEvent });
                var settled = NativeDesktop.Bounds(app.Widget);
                Check(settled.Left == afterDrag.Left && settled.Top == afterDrag.Top, "free placement does not snap after release");
                Check(SettingsFile.Load(app.SettingsPath).WidgetX == settled.Left && SettingsFile.Load(app.SettingsPath).WidgetY == settled.Top, "dragged position is saved");
                NativeWidgetPixels(app, root);
                app.Main.SelectPage("月历"); app.Main.SelectDay(new DateTime(2026, 10, 8));
                app.Mutate(items => {
                    items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = "整理阅读笔记", Note = "记录今天的重点和想法", Date = Dates.Key(DateTime.Today), CreatedUtc = DateTime.UtcNow.ToString("o") });
                    items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = "完成本周计划", Note = "安排工作与个人事项", Date = Dates.Key(DateTime.Today), CreatedUtc = DateTime.UtcNow.ToString("o") });
                    items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = "买水果", Note = "苹果、香蕉、蓝莓", Date = Dates.Key(DateTime.Today), Done = true, CreatedUtc = DateTime.UtcNow.ToString("o") });
                    items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = "提交材料", Note = "核对后再提交", Date = Dates.Key(DateTime.Today.AddDays(-2)), CreatedUtc = DateTime.UtcNow.ToString("o") });
                    items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = "整理文件夹", Note = "没有指定日期的待办", CreatedUtc = DateTime.UtcNow.ToString("o") });
                });
                app.Main.SelectDay(DateTime.Today);
                Check(app.Widget.DisplayedCount == 3, "widget filters completed and undated");
                Check(app.Main.CalendarCellCount == 35 || app.Main.CalendarCellCount == 42, "month grid contains full weeks");
                string id = app.Store.State.Items[0].Id;
                app.Toggle(id, true);
                Check(app.Widget.DisplayedCount == 2, "completion synchronizes widget");
                app.Toggle(id, false);
                app.Delete(id); Check(app.Widget.DisplayedCount == 2, "delete synchronizes");
                app.UndoDelete(); Check(app.Widget.DisplayedCount == 3, "delete undo synchronizes");
                app.Main.SelectPage("今天");
                app.Main.UpdateLayout();
                var failedToggleItem = app.Store.State.Items.First(i => !i.Done && i.Date == Dates.Key(DateTime.Today));
                var failedToggle = Controls<CheckBox>(app.Main).First(c => AutomationProperties.GetName(c) == "完成 " + failedToggleItem.Title);
                failedToggle.IsChecked = true;
                bool saveFailed = false;
                using (var locked = new FileStream(Path.Combine(app.Store.DirectoryPath, "tasks.json"), FileMode.Open, FileAccess.Read, FileShare.None))
                    try { failedToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); } catch (IOException) { saveFailed = true; }
                app.Main.UpdateLayout();
                Check(saveFailed && Controls<CheckBox>(app.Main).First(c => AutomationProperties.GetName(c) == "完成 " + failedToggleItem.Title).IsChecked == false, "failed completion save restores checkbox state");
                app.Main.SelectPage("月历");
                app.Main.SelectDay(new DateTime(2024, 2, 29));
                Check(app.Main.Month == new DateTime(2024, 2, 1), "leap day navigation");
                app.Main.SelectDay(DateTime.Today);
                foreach (string theme in new[] { "light", "warm", "dark" })
                {
                    Check(app.ChangeTheme(theme), "theme persisted " + theme);
                    app.Main.UpdateLayout(); app.Widget.UpdateLayout();
                    Check(SettingsFile.Load(app.SettingsPath).Theme == theme, "theme survives reload " + theme);
                    Check(app.Main.Background == Application.Current.Resources["Surface"], "main theme " + theme);
                    Pump();
                    var titleBounds = NativeDesktop.Bounds(app.Main);
                    using (var titleBitmap = new System.Drawing.Bitmap(titleBounds.Right - titleBounds.Left, titleBounds.Bottom - titleBounds.Top))
                    using (var titleGraphics = System.Drawing.Graphics.FromImage(titleBitmap))
                    {
                        IntPtr dc = titleGraphics.GetHdc(); try { PrintWindow(new WindowInteropHelper(app.Main).Handle, dc, 2); } finally { titleGraphics.ReleaseHdc(dc); }
                        titleBitmap.Save(Path.Combine(root, theme + "-native-title.png"), System.Drawing.Imaging.ImageFormat.Png);
                        var actual = titleBitmap.GetPixel(180, 15); var expected = ((SolidColorBrush)Application.Current.Resources["Surface"]).Color;
                        Check(Math.Abs(actual.R - expected.R) + Math.Abs(actual.G - expected.G) + Math.Abs(actual.B - expected.B) < 12, "native title bar matches theme " + theme);
                    }
                    Check(app.Widget.Background == Brushes.Transparent && ((SolidColorBrush)Application.Current.Resources["GlassTint"]).Color.A < 255, "transparent glass theme " + theme);
                    Capture(app.Main, Path.Combine(root, theme + "-main.png"));
                    Capture(app.Widget, Path.Combine(root, theme + "-widget.png"));
                    NativeWidgetPixels(app, root);
                }
                var editor = new EditWindow(app, null, DateTime.Today);
                editor.Show(); editor.UpdateLayout(); Capture(editor, Path.Combine(root, "editor.png"));
                Check(Controls<DatePickerTextBox>(editor).First().Background == Application.Current.Resources["Soft"], "date input follows dark theme");
                Check(Controls<ComboBox>(editor).Any(c => AutomationProperties.GetName(c) == "待办小时") && Controls<ComboBox>(editor).Any(c => AutomationProperties.GetName(c) == "待办分钟"), "editor offers hour and minute selection");
                Controls<TextBox>(editor).First(t => AutomationProperties.GetName(t) == "待办标题").Text = "界面输入测试";
                Controls<TextBox>(editor).First(t => AutomationProperties.GetName(t) == "待办地点").Text = "会议室 A";
                Controls<TextBox>(editor).First(t => AutomationProperties.GetName(t) == "待办标签").Text = "工作，会议,工作";
                var timedCheck = Controls<CheckBox>(editor).First(c => (c.Content as string) == "指定时间");
                timedCheck.IsChecked = true; timedCheck.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var hourChoice = Controls<ComboBox>(editor).First(c => AutomationProperties.GetName(c) == "待办小时");
                hourChoice.IsDropDownOpen = true; Pump();
                var hourPopup = (Popup)hourChoice.Template.FindName("PART_Popup", hourChoice);
                Check(hourPopup.IsOpen && ((FrameworkElement)hourPopup.Child).ActualHeight > 0, "hour dropdown opens with visible choices");
                var hourItem = (ComboBoxItem)hourChoice.ItemContainerGenerator.ContainerFromIndex(15);
                hourItem.IsSelected = true;
                Check(hourChoice.SelectedItem as string == "15", "dropdown item selection updates hour");
                hourChoice.IsDropDownOpen = false; hourChoice.SelectedItem = "14";
                Controls<ComboBox>(editor).First(c => AutomationProperties.GetName(c) == "待办分钟").SelectedItem = "35";
                editor.UpdateLayout(); Capture(editor, Path.Combine(root, "editor-time-location.png"));
                Controls<Button>(editor).First(b => (b.Content as string) == "保存待办").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(app.Store.State.Items.Any(i => i.Title == "界面输入测试"), "real editor save button adds task");
                Check(app.Store.State.Items.Any(i => i.Title == "界面输入测试" && i.Time == "14:35" && i.Location == "会议室 A"), "editor saves chosen time and location");
                Check(app.Store.State.Items.First(i => i.Title == "界面输入测试").Tags.SequenceEqual(new[] { "工作", "会议" }), "editor persists deduplicated tags");
                Check(app.Widget.DisplayedCount == 4, "real editor synchronizes widget");
                app.Widget.UpdateLayout();
                Check(Controls<TextBlock>(app.Widget).Any(t => t.Text.Contains("14:35")) && Controls<TextBlock>(app.Widget).Any(t => t.Text.Contains("会议室 A")), "widget displays time and location");
                Capture(app.Widget, Path.Combine(root, "widget-time-location.png"));
                var editedItem = app.Store.State.Items.First(i => i.Title == "界面输入测试");
                var editExisting = new EditWindow(app, editedItem, null); editExisting.Show(); editExisting.UpdateLayout();
                Controls<TextBox>(editExisting).First(t => AutomationProperties.GetName(t) == "待办标题").Text = "界面编辑测试";
                Check(Controls<ComboBox>(editExisting).First(c => AutomationProperties.GetName(c) == "待办小时").SelectedItem as string == "14", "existing time restored in editor");
                Controls<ComboBox>(editExisting).First(c => AutomationProperties.GetName(c) == "待办小时").SelectedItem = "16";
                Controls<TextBox>(editExisting).First(t => AutomationProperties.GetName(t) == "待办地点").Text = "实验室";
                Controls<CheckBox>(editExisting).First(c => (c.Content as string) == "已完成").IsChecked = true;
                Controls<Button>(editExisting).First(b => (b.Content as string) == "保存待办").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(app.Store.State.Items.Any(i => i.Title == "界面编辑测试" && i.Done) && app.Widget.DisplayedCount == 3, "real editor edits title and completion");
                Check(app.Store.State.Items.Any(i => i.Id == editedItem.Id && i.Time == "16:35" && i.Location == "实验室"), "editor updates time and location");
                var clearDate = new EditWindow(app, app.Store.State.Items.First(i => i.Id == editedItem.Id), null); clearDate.Show(); clearDate.UpdateLayout();
                var noDate = Controls<CheckBox>(clearDate).First(c => (c.Content as string) == "不设日期"); noDate.IsChecked = true; noDate.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Controls<Button>(clearDate).First(b => (b.Content as string) == "保存待办").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(app.Store.State.Items.Any(i => i.Id == editedItem.Id && i.Date == null && i.Time == null && i.Location == "实验室"), "clearing date clears time but preserves location");
                app.Mutate(items => items.RemoveAll(i => i.Id == editedItem.Id));
                var settings = new SettingsWindow(app);
                settings.Show(); settings.UpdateLayout(); Capture(settings, Path.Combine(root, "settings.png"));
                Controls<Button>(settings).First(b => (b.Content as string) == "暖色").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(app.Settings.Theme == "warm", "real skin button switches theme"); settings.Close();
                app.Main.SelectPage("今天"); Capture(app.Main, Path.Combine(root, "today.png"));
                app.Main.SelectPage("全部"); Capture(app.Main, Path.Combine(root, "all.png"));
                var repeatEditor = new EditWindow(app, null, DateTime.Today); repeatEditor.Show(); repeatEditor.UpdateLayout();
                Controls<TextBox>(repeatEditor).First(t => AutomationProperties.GetName(t) == "待办标题").Text = "每日阅读";
                var repeatTime = Controls<CheckBox>(repeatEditor).First(c => (c.Content as string) == "指定时间"); repeatTime.IsChecked = true; repeatTime.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Controls<ComboBox>(repeatEditor).First(c => AutomationProperties.GetName(c) == "待办小时").SelectedItem = "00";
                Controls<ComboBox>(repeatEditor).First(c => AutomationProperties.GetName(c) == "待办分钟").SelectedItem = "00";
                Controls<ComboBox>(repeatEditor).First(c => AutomationProperties.GetName(c) == "重复周期").SelectedIndex = 1;
                Controls<CheckBox>(repeatEditor).First(c => AutomationProperties.GetName(c) == "重要任务").IsChecked = true;
                Capture(repeatEditor, Path.Combine(root, "editor-recurring.png"));
                Controls<Button>(repeatEditor).First(b => (b.Content as string) == "保存待办").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(app.Store.State.Series.Count == 1 && app.Store.State.Items.Count(i => i.Title == "每日阅读") == 63, "real editor creates calendar occurrences independent of completion");
                var repeatToday = app.Store.State.Items.First(i => i.Title == "每日阅读" && i.Date == Dates.Key(DateTime.Today));
                Check(repeatToday.Reminder && repeatToday.Important, "real editor saves reminder and importance");
                app.Main.SelectPage("全部"); app.Main.UpdateLayout();
                var recurringGroup = Controls<Expander>(app.Main).First(e => AutomationProperties.GetName(e) == "重复任务 每日阅读");
                Check(!recurringGroup.IsExpanded && !Controls<CheckBox>(app.Main).Any(c => AutomationProperties.GetName(c) == "完成 每日阅读"), "all view collapses recurrence into a summary without loading repeated rows");
                Capture(app.Main, Path.Combine(root, "all-grouped.png"));
                recurringGroup.IsExpanded = true; app.Main.UpdateLayout();
                Check(Controls<CheckBox>(app.Main).Count(c => AutomationProperties.GetName(c) == "完成 每日阅读") == 63, "expanding summary restores individually actionable occurrences");
                Check(AutomationProperties.GetName(Controls<CheckBox>(app.Main).First(c => AutomationProperties.GetName(c).StartsWith("完成 "))) == "完成 每日阅读", "important pending tasks sort before other pending tasks");
                var star = Controls<Button>(app.Main).First(b => AutomationProperties.GetName(b) == "重要 每日阅读"); star.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!app.Store.State.Items.First(i => i.Id == repeatToday.Id).Important && !DataStore.ReadState(Path.Combine(app.Store.DirectoryPath, "tasks.json")).Items.First(i => i.Id == repeatToday.Id).Important, "star toggles and persists on an individual occurrence");
                app.ToggleImportant(repeatToday.Id);
                app.Widget.UpdateLayout();
                Check(AutomationProperties.GetName(Controls<CheckBox>(app.Widget).First()) == "完成 每日阅读", "widget displays important today tasks first");
                IntPtr focusBeforeReminder = GetForegroundWindow();
                app.CheckReminders(DateTime.Today.AddHours(12)); Pump();
                Check(app.ReminderPopup != null && app.ReminderPopup.IsVisible && !app.ReminderPopup.ShowActivated && app.ReminderPopup.Topmost, "small reminder popup is visible without requesting focus");
                Check(GetForegroundWindow() == focusBeforeReminder, "reminder does not activate or steal foreground focus");
                Capture(app.ReminderPopup, Path.Combine(root, "reminder.png"));
                Check(DataStore.ReadState(Path.Combine(app.Store.DirectoryPath, "tasks.json")).Items.First(i => i.Id == repeatToday.Id).NotifiedFor == Schedule.ReminderKey(repeatToday), "reminder acknowledgement is saved before showing");
                Controls<Button>(app.ReminderPopup).First(b => (b.Content as string) == "×").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                app.CheckReminders(DateTime.Today.AddHours(12));
                Check(app.ReminderPopup == null, "same occurrence is not notified twice");
                app.Main.SelectDay(DateTime.Today.AddMonths(4));
                Check(app.Store.State.Items.Any(i => i.SeriesId == repeatToday.SeriesId && i.Date == Dates.Key(DateTime.Today.AddMonths(4))), "navigating future calendar generates future occurrences");
                var futureDoneId = app.Store.State.Items.First(i => i.SeriesId == repeatToday.SeriesId && i.Date == Dates.Key(DateTime.Today.AddDays(1))).Id;
                app.Toggle(futureDoneId, true);
                app.StopRepeating(app.Store.State.Items.First(i => i.Id == repeatToday.Id));
                Check(app.Store.State.Series.Count == 0 && app.Store.State.Items.Count(i => i.Title == "每日阅读") == 2 && app.Store.State.Items.Any(i => i.Id == futureDoneId && i.Done), "stopping repeat removes future pending occurrences and retains current and completed history");
                app.Mutate(items => items.RemoveAll(i => i.Id == repeatToday.Id || i.Id == futureDoneId)); app.Main.SelectDay(DateTime.Today);
                app.Main.SelectPage("番茄钟"); app.Main.UpdateLayout();
                Controls<Button>(app.Main).First(b => (b.Content as string) == "学习统计").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); app.Main.UpdateLayout();
                Check(Controls<Button>(app.Main).Count(b => AutomationProperties.GetName(b).StartsWith("学习柱状图 ")) == 7 && Controls<Button>(app.Main).Count(b => AutomationProperties.GetName(b).StartsWith("学习热力图 ")) == 30, "statistics shows seven chart days and thirty heatmap days even with no records");
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "还没有时长记录"), "empty study statistics gives a useful starting hint");
                Controls<Button>(app.Main).First(b => (b.Content as string) == "返回番茄钟").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var studyName = Controls<TextBox>(app.Main).First(t => AutomationProperties.GetName(t) == "学习名称");
                var studyMinutes = Controls<TextBox>(app.Main).First(t => AutomationProperties.GetName(t) == "学习分钟");
                { var shortcut = Controls<Expander>(app.Main).First(e => (e.Header as string) == "待办名称快捷填入"); shortcut.IsExpanded = true; app.Main.UpdateLayout(); var learningTask = Controls<Button>(app.Main).First(b => AutomationProperties.GetName(b).StartsWith("学习任务 ")); learningTask.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(studyName.Text == AutomationProperties.GetName(learningTask).Substring("学习任务 ".Length), "workbench task selection fills study name"); }
                studyName.Text = "阅读论文"; studyMinutes.Text = "25";
                Check(Controls<TextBlock>(app.Main).First(t => AutomationProperties.GetName(t) == "学习倒计时").Text == "25:00", "focus page previews custom duration");
                Controls<Button>(app.Main).First(b => (b.Content as string) == "开始学习").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int tick = 0; tick < 10; tick++) Pump();
                Check(app.FocusRunning && app.FocusSeconds > 0 && app.Store.State.ActiveFocus.Title == "阅读论文", "start button begins named focus countdown");
                Check(Controls<TextBlock>(app.Main).First(t => AutomationProperties.GetName(t) == "学习倒计时").Text != "25:00", "visible focus clock counts down while dispatcher timer runs");
                Capture(app.Main, Path.Combine(root, "focus-running.png"));
                double beforeStatistics = app.FocusSeconds; app.Main.SelectPage("学习统计"); Pump(); Pump();
                Check(app.FocusRunning && app.FocusSeconds > beforeStatistics, "viewing statistics does not interrupt running countdown");
                app.Main.SelectPage("番茄钟");
                Controls<Button>(app.Main).First(b => (b.Content as string) == "暂停").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                double pausedSeconds = app.FocusSeconds; Pump(); Pump();
                Check(!app.FocusRunning && Math.Abs(app.FocusSeconds - pausedSeconds) < 0.001, "paused focus excludes time while paused");
                Check(DataStore.ReadState(Path.Combine(app.Store.DirectoryPath, "tasks.json")).ActiveFocus.FocusedSeconds > 0, "pause saves active focus progress");
                string restoredFocusRoot = Path.Combine(root, "focus-restored"); Directory.CreateDirectory(Path.Combine(restoredFocusRoot, "Data"));
                File.Copy(Path.Combine(app.Store.DirectoryPath, "tasks.json"), Path.Combine(restoredFocusRoot, "Data", "tasks.json"));
                var restoredFocusApp = new AppController(restoredFocusRoot, true);
                Check(!restoredFocusApp.FocusRunning && Math.Abs(restoredFocusApp.FocusSeconds - pausedSeconds) < 0.001 && restoredFocusApp.Store.State.ActiveFocus.Title == "阅读论文", "reopened controller restores saved focus as paused"); restoredFocusApp.Store.Dispose();
                app.Main.SelectPage("今天"); app.Main.SelectPage("番茄钟");
                Check(Controls<TextBox>(app.Main).First(t => AutomationProperties.GetName(t) == "学习名称").Text == "阅读论文", "focus survives page navigation");
                Controls<Button>(app.Main).First(b => (b.Content as string) == "继续").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                Check(app.FocusRunning && app.FocusSeconds > pausedSeconds, "continue button resumes active countdown");
                string beforeFocusFailure = File.ReadAllText(Path.Combine(app.Store.DirectoryPath, "tasks.json")); bool focusSaveFailed = false;
                double secondsBeforeFailure = app.FocusSeconds;
                using (var locked = new FileStream(Path.Combine(app.Store.DirectoryPath, "tasks.json"), FileMode.Open, FileAccess.Read, FileShare.None))
                    try { app.PauseFocus(); } catch (IOException) { focusSaveFailed = true; }
                Check(focusSaveFailed && !app.FocusRunning && File.ReadAllText(Path.Combine(app.Store.DirectoryPath, "tasks.json")) == beforeFocusFailure, "failed focus checkpoint pauses and preserves original data");
                Check(app.ResumeFocus() && app.FocusSeconds >= secondsBeforeFailure, "resume retries pending focus progress without losing elapsed time");
                Controls<Button>(app.Main).First(b => (b.Content as string) == "结束并记录").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(app.Store.State.ActiveFocus == null && app.Store.State.FocusHistory.Count == 1 && !app.Store.State.FocusHistory[0].Completed && app.Store.State.FocusHistory[0].FocusedSeconds < 25 * 60, "ending early records actual elapsed learning time");
                studyName.Text = "学习英语"; studyMinutes.Text = "10";
                Controls<Button>(app.Main).First(b => (b.Content as string) == "开始学习").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); app.PauseFocus();
                app.Store.ChangeState(state => Study.AddTime(state, DateTimeOffset.Now, 600)); app.ResumeFocus();
                app.FocusTick(); Pump();
                Check(app.Store.State.ActiveFocus == null && app.Store.State.FocusHistory.Count == 2 && app.Store.State.FocusHistory.Last().Completed && app.Store.State.FocusHistory.Last().FocusedSeconds == 600, "countdown completion records exactly planned duration once");
                Check(app.ReminderPopup != null && app.ReminderPopup.IsVisible && !app.ReminderPopup.ShowActivated, "focus completion uses a small nonactivating notification");
                Capture(app.ReminderPopup, Path.Combine(root, "focus-notification.png")); app.ReminderPopup.Close();
                app.FocusTick();
                Check(app.Store.State.FocusHistory.Count == 2, "completed focus is not recorded twice");
                var modeChoice = Controls<ComboBox>(app.Main).First(c => AutomationProperties.GetName(c) == "学习计时模式"); modeChoice.SelectedIndex = 1;
                Check(Controls<TextBlock>(app.Main).First(t => AutomationProperties.GetName(t) == "学习倒计时").Text == "00:00", "count-up starts at zero without a duration");
                studyName.Text = "自由学习";
                Controls<Button>(app.Main).First(b => (b.Content as string) == "开始学习").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int tick = 0; tick < 10; tick++) Pump();
                Check(app.FocusRunning && app.Store.State.ActiveFocus.IsCountUp && app.FocusSeconds > 1 && Controls<TextBlock>(app.Main).First(t => AutomationProperties.GetName(t) == "学习倒计时").Text != "00:00", "count-up increases on screen and never finishes at zero target");
                app.PauseFocus(); double openSeconds = app.FocusSeconds; Pump(); Check(Math.Abs(app.FocusSeconds - openSeconds) < 0.001, "count-up pause excludes paused time");
                app.ResumeFocus(); Pump(); Capture(app.Main, Path.Combine(root, "focus-count-up.png"));
                Controls<Button>(app.Main).First(b => (b.Content as string) == "结束并记录").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(app.Store.State.ActiveFocus == null && app.Store.State.FocusHistory.Last().IsCountUp && app.Store.State.FocusHistory.Last().Completed && app.Store.State.FocusHistory.Last().FocusedSeconds >= openSeconds, "count-up manual ending records a completed session with actual time");
                if (app.ReminderPopup != null) app.ReminderPopup.Close(); modeChoice.SelectedIndex = 0;
                app.Store.ChangeState(state =>
                {
                    state.FocusHistory.Clear();
                    var exampleStart = new DateTimeOffset(DateTime.Today.AddHours(9), DateTimeOffset.Now.Offset);
                    Study.Start(state, "阅读论文", 25, exampleStart); Study.AddTime(state, exampleStart, 1500); Study.Finish(state, exampleStart.AddMinutes(25), true);
                    exampleStart = exampleStart.AddHours(1);
                    Study.Start(state, "复习笔记", 45, exampleStart); Study.AddTime(state, exampleStart, 2700); Study.Finish(state, exampleStart.AddMinutes(45), true);
                    exampleStart = exampleStart.AddHours(4);
                    Study.Start(state, "学习英语", 20, exampleStart); Study.AddTime(state, exampleStart, 480); Study.Finish(state, exampleStart.AddMinutes(8), false);
                });
                { var shortcut = Controls<Expander>(app.Main).First(e => (e.Header as string) == "待办名称快捷填入"); shortcut.IsExpanded = true; app.Main.UpdateLayout(); var learningTask = Controls<Button>(app.Main).First(b => AutomationProperties.GetName(b).StartsWith("学习任务 ")); learningTask.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(studyName.Text == AutomationProperties.GetName(learningTask).Substring("学习任务 ".Length), "workbench task selection fills study name"); }
                studyName.Text = "阅读论文"; studyMinutes.Text = "25";
                app.Main.Refresh(); app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "focus-dashboard.png"));
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "复习笔记") && Controls<TextBlock>(app.Main).Any(t => t.Text.Contains("完成 2 次")), "today dashboard displays names and completed focus count");
                string studyBackup = Path.Combine(root, "study-backup.json"); app.Store.Export(studyBackup);
                Check(DataStore.ReadState(studyBackup).FocusHistory.Count == 3, "backup exports study history together with tasks");
                app.Store.ChangeState(state =>
                {
                    for (int day = 0; day < 14; day++)
                    {
                        var from = new DateTimeOffset(DateTime.Today.AddDays(day - 14).AddHours(9), DateTimeOffset.Now.Offset);
                        int minutes = 30 + day % 7 * 5; bool done = day % 4 != 0;
                        Study.Start(state, new[] { "阅读论文", "学习英语", "复习笔记" }[day % 3], minutes, from);
                        int seconds = (done ? minutes : minutes - 10) * 60; Study.AddTime(state, from, seconds); Study.Finish(state, from.AddSeconds(seconds), done);
                    }
                    var oldDate = new DateTimeOffset(DateTime.Today.AddDays(-45).AddHours(9), DateTimeOffset.Now.Offset);
                    Study.Start(state, "早期学习记录", 35, oldDate); Study.AddTime(state, oldDate, 2100); Study.Finish(state, oldDate.AddMinutes(35), true);
                });
                app.Main.SelectPage("学习统计"); app.Main.UpdateLayout();
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "15 天") && Controls<TextBlock>(app.Main).Any(t => t.Text == "12 次"), "statistics summary counts active dates and completed sessions in last thirty days");
                var lookupDay = DateTime.Today.AddDays(-2);
                Controls<Button>(app.Main).First(b => AutomationProperties.GetName(b) == "学习热力图 " + Dates.Key(lookupDay)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Controls<DatePicker>(app.Main).First(p => AutomationProperties.GetName(p) == "学习记录日期").SelectedDate == lookupDay && Controls<TextBlock>(app.Main).Any(t => t.Text.Contains("1 条记录")), "clicking heatmap selects corresponding historical records");
                var nameFilter = Controls<TextBox>(app.Main).First(t => AutomationProperties.GetName(t) == "学习名称筛选"); nameFilter.Text = "没有这个名称";
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text.Contains("0 条记录")), "study records can be searched by name"); nameFilter.Text = "";
                var statusChoice = Controls<ComboBox>(app.Main).First(c => AutomationProperties.GetName(c) == "学习记录状态"); statusChoice.SelectedIndex = 1;
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text.Contains("0 条记录")), "completed filter excludes early ended sessions"); statusChoice.SelectedIndex = 2;
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text.Contains("1 条记录")), "early ending filter shows matching sessions"); statusChoice.SelectedIndex = 0;
                Controls<DatePicker>(app.Main).First(p => AutomationProperties.GetName(p) == "学习记录日期").SelectedDate = DateTime.Today.AddDays(-45);
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "早期学习记录"), "historical records outside current chart range remain accessible");
                Controls<DatePicker>(app.Main).First(p => AutomationProperties.GetName(p) == "统计截止日期").SelectedDate = DateTime.Today.AddDays(-45);
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "1 天") && Controls<TextBlock>(app.Main).Any(t => t.Text == "1 次"), "changing chart cutoff loads older statistics");
                Controls<Button>(app.Main).First(b => AutomationProperties.GetName(b) == "今天").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Controls<DatePicker>(app.Main).First(p => AutomationProperties.GetName(p) == "学习记录日期").SelectedDate == DateTime.Today, "today button restores current chart and record dates");
                app.Store.ChangeState(state =>
                {
                    var batchDay = new DateTimeOffset(DateTime.Today.AddDays(-50).AddHours(9), DateTimeOffset.Now.Offset);
                    for (int index = 0; index < 51; index++) { var from = batchDay.AddMinutes(index); Study.Start(state, "批次学习 " + index, 1, from); Study.AddTime(state, from, 60); Study.Finish(state, from.AddMinutes(1), true); }
                }); app.Main.Refresh();
                Controls<DatePicker>(app.Main).First(p => AutomationProperties.GetName(p) == "学习记录日期").SelectedDate = DateTime.Today.AddDays(-50);
                Check(Controls<TextBlock>(app.Main).Count(t => t.Text.StartsWith("批次学习 ")) == 50, "large daily record lists initially render fifty sessions");
                Controls<Button>(app.Main).First(b => (b.Content as string ?? "").StartsWith("加载更多")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Controls<TextBlock>(app.Main).Count(t => t.Text.StartsWith("批次学习 ")) == 51, "load more reveals remaining records without dropping history");
                Controls<Button>(app.Main).First(b => AutomationProperties.GetName(b) == "今天").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                foreach (string skin in new[] { "light", "dark", "warm" }) { app.ChangeTheme(skin); app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "study-statistics-" + skin + ".png")); }
                var statsPage = Controls<StudyStatsPanel>(app.Main).First(); statsPage.ScrollToBottom(); app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "study-records.png")); statsPage.ScrollToTop();
                app.Main.Width = 960; app.Main.Height = 650; app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "study-statistics-small.png")); app.Main.Width = 1160; app.Main.Height = 790;
                app.Main.SelectPage("番茄钟");
                var dailyGoal = Controls<TextBox>(app.Main).First(t => AutomationProperties.GetName(t) == "每日目标分钟"); dailyGoal.Text = "120";
                Controls<Button>(app.Main).First(b => AutomationProperties.GetName(b) == "保存每日目标").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(app.Store.State.DailyGoalMinutes == 120 && DataStore.ReadState(Path.Combine(app.Store.DirectoryPath, "tasks.json")).DailyGoalMinutes == 120, "daily goal saves through atomic storage");
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "已学 78 分 · 还差 42 分"), "daily goal uses actual completed and early-ended time");
                dailyGoal.Text = "60"; Controls<Button>(app.Main).First(b => AutomationProperties.GetName(b) == "保存每日目标").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "今日目标已达成 ✓"), "goal completion appears without changing task state");
                dailyGoal.Text = "1441"; Controls<Button>(app.Main).First(b => AutomationProperties.GetName(b) == "保存每日目标").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(app.Store.State.DailyGoalMinutes == 60 && Controls<TextBlock>(app.Main).Any(t => t.Text == "请输入 0–1440 分钟"), "invalid goal leaves saved target intact");
                dailyGoal.Text = "120"; Controls<Button>(app.Main).First(b => AutomationProperties.GetName(b) == "保存每日目标").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "daily-goal.png"));
                Controls<Button>(app.Main).First(b => (b.Content as string) == "周报 / 月报").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); app.Main.UpdateLayout();
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text.StartsWith("比上周同期")) && Controls<TextBlock>(app.Main).Any(t => t.Text == "主要学习内容"), "weekly report shows comparison and learning breakdown"); Capture(app.Main, Path.Combine(root, "study-weekly.png"));
                var reportMode = Controls<ComboBox>(app.Main).First(c => AutomationProperties.GetName(c) == "学习报告周期"); reportMode.SelectedIndex = 1; app.Main.UpdateLayout();
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text.StartsWith("比上月同期")), "monthly report uses calendar month and prior month comparison"); Capture(app.Main, Path.Combine(root, "study-monthly.png"));
                app.Main.Width = 960; app.Main.Height = 650; app.Main.UpdateLayout(); Capture(app.Main, Path.Combine(root, "study-monthly-small.png")); app.Main.Width = 1160; app.Main.Height = 790;
                Controls<DatePicker>(app.Main).First(p => AutomationProperties.GetName(p) == "学习报告日期").SelectedDate = DateTime.Today.AddYears(-2);
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "本期还没有学习时长"), "empty historical report shows no fabricated activity");
                var searchable = new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = "组会资料整理", Location = "B528", Note = "准备报告", Tags = new List<string> { "科研", "全部标签" } }; app.SaveTodo(searchable, null, null);
                app.SaveTodo(new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = "参会事项", Location = "A228", Tags = new List<string> { "会议" } }, null, null); app.Main.SelectPage("全部");
                var taskSearch = Controls<TextBox>(app.Main).First(t => AutomationProperties.GetName(t) == "待办搜索"); taskSearch.Text = "B528"; app.Main.UpdateLayout();
                Check(Controls<CheckBox>(app.Main).Count(c => AutomationProperties.GetName(c).StartsWith("完成 ")) == 1 && Controls<TextBlock>(app.Main).Any(t => t.Text == "组会资料整理"), "todo location search narrows real task list");
                var tagFilter = Controls<ComboBox>(app.Main).First(c => AutomationProperties.GetName(c) == "待办标签筛选"); tagFilter.SelectedItem = "#科研";
                Check(Controls<TextBlock>(app.Main).Any(t => t.Text.Contains("#科研")), "matching tag and query show task labels"); Capture(app.Main, Path.Combine(root, "todo-search-tags.png"));
                tagFilter.SelectedItem = "#会议"; Check(Controls<TextBlock>(app.Main).Any(t => t.Text == "没有匹配的待办"), "combined todo filters exclude unrelated tasks");
                Controls<Button>(app.Main).First(b => (b.Content as string) == "清除").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(taskSearch.Text == "" && tagFilter.SelectedIndex == 0, "clear restores all todo filters");
                tagFilter.SelectedIndex = ((List<string>)tagFilter.ItemsSource).FindLastIndex(t => t == "#全部标签"); app.Main.Refresh(); app.Main.UpdateLayout();
                Check(Controls<ComboBox>(app.Main).First(c => AutomationProperties.GetName(c) == "待办标签筛选").SelectedIndex > 0 && Controls<CheckBox>(app.Main).Count(c => AutomationProperties.GetName(c).StartsWith("完成 ")) == 1, "a tag named all tags remains a real filter across refresh");
                app.Main.SelectPage("月历");
                app.Main.Close();
                Check(!app.Main.IsVisible && app.Widget.IsVisible, "closing main leaves widget running");
                Check(!app.Widget.Topmost, "widget is not topmost");
                Check(NativeDesktop.IsAttached(app.Widget), "widget parent belongs to desktop shell");
                NativeDesktop.ActivateMain(app.BasePath);
                var frame = new DispatcherFrame();
                Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Check(app.Main.IsVisible, "relaunch restores WPF visible state");
                app.ShowMain();
                var widgetBounds = NativeDesktop.Bounds(app.Widget);
                IntPtr mainHwnd = new WindowInteropHelper(app.Main).Handle;
                bool positioned = SetWindowPos(mainHwnd, IntPtr.Zero, widgetBounds.Left - 40, widgetBounds.Top - 40, 0, 0, 0x0001);
                var hit = WindowFromPoint(new NativeDesktop.Point { X = widgetBounds.Left + 100, Y = widgetBounds.Top + 100 });
                var cls = new System.Text.StringBuilder(128); GetClassName(hit, cls, 128);
                var mainBounds = NativeDesktop.Bounds(app.Main);
                File.WriteAllText(Path.Combine(root, "desktop-probe.txt"), "Positioned=" + positioned + "; Main=" + mainHwnd + "; Hit=" + hit + "; HitClass=" + cls + "; HitRoot=" + GetAncestor(hit, 2) + "; Widget=" + widgetBounds.Left + "," + widgetBounds.Top + "," + widgetBounds.Right + "," + widgetBounds.Bottom + "; MainBounds=" + mainBounds.Left + "," + mainBounds.Top + "," + mainBounds.Right + "," + mainBounds.Bottom);
                var order = new List<IntPtr>();
                for (IntPtr hwnd = GetTopWindow(IntPtr.Zero); hwnd != IntPtr.Zero; hwnd = GetWindow(hwnd, 2)) { order.Add(hwnd); if (order.Count > 10000) break; }
                IntPtr desktopRoot = GetAncestor(new WindowInteropHelper(app.Widget).Handle, 2);
                Check(positioned && order.IndexOf(mainHwnd) >= 0 && order.IndexOf(mainHwnd) < order.IndexOf(desktopRoot) && GetAncestor(hit, 2) != desktopRoot, "ordinary windows stack above desktop widget");
                File.WriteAllText(Path.Combine(root, "ui-report.txt"), "ALL PASS: " + passed + Environment.NewLine + "Desktop parent: " + NativeDesktop.ParentClass(app.Widget));
                var exitRequest = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, "--exit \"" + app.BasePath + "\"") { UseShellExecute = false });
                Check(exitRequest.WaitForExit(3000), "exit command requester returns without opening another app window"); Pump();
                Check(app.Exiting, "external exit command closes application through its save-and-exit path");
                File.WriteAllText(Path.Combine(root, "ui-report.txt"), "ALL PASS: " + passed + Environment.NewLine + "Graceful exit verified");
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(root, "ui-report.txt"), ex.ToString());
                app.Exit(); Environment.ExitCode = 1;
            }
        }
    }
}
