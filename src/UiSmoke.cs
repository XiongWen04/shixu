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
                Check(Controls<DatePickerTextBox>(editor).First().Background == Application.Current.Resources["Panel"], "date input follows dark theme");
                Check(Controls<ComboBox>(editor).Any(c => AutomationProperties.GetName(c) == "待办小时") && Controls<ComboBox>(editor).Any(c => AutomationProperties.GetName(c) == "待办分钟"), "editor offers hour and minute selection");
                Controls<TextBox>(editor).First(t => AutomationProperties.GetName(t) == "待办标题").Text = "界面输入测试";
                Controls<TextBox>(editor).First(t => AutomationProperties.GetName(t) == "待办地点").Text = "会议室 A";
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
                app.Exit();
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(root, "ui-report.txt"), ex.ToString());
                app.Exit(); Environment.ExitCode = 1;
            }
        }
    }
}
