using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;

namespace DeskTodo
{
    public class WidgetWindow : Window
    {
        readonly AppController app;
        readonly StackPanel tasks = new StackPanel();
        readonly TextBlock dateLabel = Ui.Text("", 12, "Muted");
        readonly TextBlock desktopStatus = Ui.Text("", 10, "Danger");
        readonly WidgetSurface surface;
        public int DisplayedCount { get; private set; }
        public WidgetWindow(AppController controller)
        {
            app = controller; Title = "拾序桌面小组件"; Width = app.Settings.WidgetWidth; Height = app.Settings.WidgetHeight;
            MinWidth = 260; MinHeight = 280; MaxWidth = 1400; MaxHeight = 1800;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = false; Ui.Icon(this);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = System.Windows.Media.Brushes.Transparent;
            SourceInitialized += (s, e) =>
            {
                HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
                {
                    if (msg == 0x21) { handled = true; return new IntPtr(3); }
                    return IntPtr.Zero;
                });
                AttachToDesktop(); Place();
            };
            var grid = new Grid { Margin = new Thickness(20, 18, 20, 16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var frame = Ui.Card(grid, new Thickness(0)); frame.CornerRadius = new CornerRadius(10); surface = new WidgetSurface(this, frame); Content = surface;
            var header = new Grid { Background = System.Windows.Media.Brushes.Transparent, Margin = new Thickness(0, 0, 0, 16), Cursor = Cursors.SizeAll }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.Children.Add(header);
            var titles = new StackPanel { IsHitTestVisible = false };
            var brand = new StackPanel { Orientation = Orientation.Horizontal };
            brand.Children.Add(new Image { Source = Icon, Width = 25, Height = 25 });
            var name = Ui.Text("拾序 · 今天", 18, "Text", true); name.Margin = new Thickness(8, 0, 0, 0); brand.Children.Add(name); titles.Children.Add(brand);
            dateLabel.Margin = new Thickness(0, 7, 0, 0); titles.Children.Add(dateLabel); titles.Children.Add(desktopStatus); header.Children.Add(titles);
            var menuButton = Ui.Button("⋯", () => { var menu = new ContextMenu(); var open = new MenuItem { Header = "打开主窗口" }; open.Click += (s, e) => app.ShowMain(); menu.Items.Add(open); var skin = new MenuItem { Header = "换肤" }; skin.Click += (s, e) => app.ThemeMenu(header); menu.Items.Add(skin); var hide = new MenuItem { Header = "隐藏小组件" }; hide.Click += (s, e) => app.ToggleWidget(); menu.Items.Add(hide); menu.PlacementTarget = header; menu.IsOpen = true; }, "Quiet");
            menuButton.FontSize = 20; menuButton.Padding = new Thickness(7, 0, 7, 0); menuButton.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(menuButton, 1); header.Children.Add(menuButton);
            var drag = new Thumb { Cursor = Cursors.SizeAll };
            var dragTemplate = new FrameworkElementFactory(typeof(Border)); dragTemplate.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent); drag.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = dragTemplate };
            System.Windows.Automation.AutomationProperties.SetName(drag, "移动小组件"); header.Children.Add(drag);
            drag.DragDelta += (s, e) => { var bounds = NativeDesktop.Bounds(this); double scale = NativeDesktop.Scale(this); NativeDesktop.Move(this, bounds.Left + (int)Math.Round(e.HorizontalChange * scale), bounds.Top + (int)Math.Round(e.VerticalChange * scale)); surface.Update(); };
            drag.DragCompleted += (s, e) => { SavePlacement(); surface.Update(); NativeDesktop.Lower(this); };
            var scroll = Ui.Scroll(tasks); Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
            var footer = new Grid { Margin = new Thickness(0, 13, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); Grid.SetRow(footer, 2); grid.Children.Add(footer);
            var add = Ui.Button("＋ 添加", () => app.Edit(null, DateTime.Today), "Quiet"); add.HorizontalAlignment = HorizontalAlignment.Left; add.Padding = new Thickness(0, 5, 8, 5); add.SetResourceReference(Button.ForegroundProperty, "Accent"); footer.Children.Add(add);
            var openButton = Ui.Button("↗", app.ShowMain, "Quiet"); openButton.ToolTip = "打开主窗口"; openButton.FontSize = 19; openButton.Padding = new Thickness(8, 5, 0, 5); Grid.SetColumn(openButton, 1); footer.Children.Add(openButton);
            var resize = new Thumb { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Margin = new Thickness(0, 0, -14, -12) };
            var factory = new FrameworkElementFactory(typeof(TextBlock)); factory.SetValue(TextBlock.TextProperty, "⋱"); factory.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); resize.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
            resize.DragDelta += (s, e) => { Width = Math.Max(MinWidth, Math.Min(MaxWidth, Width + e.HorizontalChange)); Height = Math.Max(MinHeight, Math.Min(MaxHeight, Height + e.VerticalChange)); var rect = NativeDesktop.Bounds(this); NativeDesktop.Move(this, rect.Left, rect.Top); };
            resize.DragCompleted += (s, e) => { SavePlacement(); surface.Update(); }; Grid.SetRow(resize, 2); grid.Children.Add(resize);
            Loaded += (s, e) => { surface.Update(); NativeDesktop.Lower(this); };
        }
        public void AttachToDesktop()
        {
            bool attached = NativeDesktop.Attach(this);
            desktopStatus.Text = attached ? "" : "桌面接入失败，暂以普通小窗显示";
        }
        public void Place() { NativeDesktop.Position(this, app.Settings.WidgetX, app.Settings.WidgetY); }
        public void SetDesktopWarning(string text) { desktopStatus.Text = text; }
        public void UpdateDesktopSurface() { surface.Update(); }
        void SavePlacement()
        {
            var bounds = NativeDesktop.Bounds(this); var next = app.Settings.Copy(); next.WidgetX = bounds.Left; next.WidgetY = bounds.Top; next.WidgetWidth = Width; next.WidgetHeight = Height;
            app.SaveSettings(next);
        }
        public void Refresh()
        {
            dateLabel.Text = DateTime.Today.ToString("M月d日 dddd"); tasks.Children.Clear();
            var items = app.Store.State.Items.Where(i => !i.Done && (i.Date == Dates.Key(DateTime.Today) || Dates.Overdue(i, DateTime.Today))).ToList();
            DisplayedCount = items.Count;
            var today = items.Where(i => i.Date == Dates.Key(DateTime.Today)).OrderBy(i => i.Time ?? "99:99").ThenBy(i => i.CreatedUtc).ToList();
            if (today.Count == 0) Ui.Empty(tasks, "今天的待办已清空", "添加一件事，或享受片刻空闲");
            else foreach (var item in today) tasks.Children.Add(Ui.TaskRow(app, item, true));
            var overdue = items.Where(i => Dates.Overdue(i, DateTime.Today)).OrderBy(i => i.Date).ThenBy(i => i.Time ?? "99:99").ToList();
            if (overdue.Count > 0) { var label = Ui.Text("逾期 · " + overdue.Count + " 件", 12, "Danger", true); label.Margin = new Thickness(0, 13, 0, 10); tasks.Children.Add(label); foreach (var item in overdue) tasks.Children.Add(Ui.TaskRow(app, item, true)); }
            surface.Update();
        }
    }
}
