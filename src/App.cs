using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("拾序")]
[assembly: AssemblyProduct("拾序 · 桌面待办")]
[assembly: AssemblyVersion("1.5.0.0")]

namespace DeskTodo
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("zh-CN");
            Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture;
            bool test = args.Length == 2 && args[0] == "--ui-test";
            string root = test ? Path.GetFullPath(args[1]) : AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(root);
            bool created;
            using (var mutex = new Mutex(true, "Local\\DeskTodo-" + Hash(root), out created))
            {
                if (!created) { NativeDesktop.ActivateMain(root); return 0; }
                try
                {
                    var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Styles.xaml"))
                        application.Resources = (ResourceDictionary)XamlReader.Load(stream);
                    var app = new AppController(root, test);
                    application.DispatcherUnhandledException += (s, e) => { app.Error("操作未完成", e.Exception); e.Handled = true; };
                    application.SessionEnding += (s, e) => app.Exit();
                    app.Start();
                    if (test) application.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => UiSmoke.Run(app, root)));
                    application.Run();
                    return Environment.ExitCode;
                }
                catch (Exception ex)
                {
                    if (test) File.WriteAllText(Path.Combine(root, "ui-report.txt"), ex.ToString());
                    else MessageBox.Show("无法启动拾序。原数据不会被清空。\n\n" + ex.Message, "拾序", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                }
            }
        }
        internal static string Hash(string text)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()))).Replace("-", "").Substring(0, 16);
        }
    }

    public partial class AppController
    {
        public DataStore Store { get; private set; }
        public AppSettings Settings { get; private set; }
        public string SettingsPath { get; private set; }
        public MainWindow Main { get; private set; }
        public WidgetWindow Widget { get; private set; }
        public bool Exiting { get; private set; }
        public bool TestMode { get; private set; }
        public bool CanUndo { get { return deleted != null; } }
        public string BasePath { get; private set; }
        TodoItem deleted;
        Forms.NotifyIcon tray;
        DispatcherTimer timer;
        internal Window ReminderPopup;
        bool reminderErrorShown;
        DateTime lastDay = DateTime.Today;

        public AppController(string root, bool test)
        {
            BasePath = root; TestMode = test;
            SettingsPath = Path.Combine(root, "preferences.json");
            try { JsonFile.CheckWritable(root); }
            catch
            {
                string alternate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskTodo", Program.Hash(root));
                JsonFile.CheckWritable(alternate);
                string portableSettings = SettingsPath;
                SettingsPath = Path.Combine(alternate, "preferences.json");
                if (!File.Exists(SettingsPath) && File.Exists(portableSettings)) File.Copy(portableSettings, SettingsPath);
            }
            try { Settings = SettingsFile.Load(SettingsPath); }
            catch (Exception ex)
            {
                if (test) throw;
                if (MessageBox.Show("设置读取失败：" + ex.Message + "\n\n从备份恢复设置？原文件会另存保留。", "恢复设置", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) throw;
                var restored = SettingsFile.Load(SettingsPath + ".bak");
                if (File.Exists(SettingsPath)) File.Copy(SettingsPath, SettingsPath + ".corrupt-" + Guid.NewGuid().ToString("N"));
                JsonFile.Write(SettingsPath, restored, true); Settings = restored;
            }
            string data = Settings.DataPath == "" ? Path.Combine(root, "Data") : Settings.DataPath;
            while (Store == null)
            {
                try { Store = DataStore.Open(data); }
                catch (Exception ex)
                {
                    if (test) throw;
                    if (File.Exists(Path.Combine(data, "tasks.json.bak")) && MessageBox.Show("待办读取失败：" + ex.Message + "\n\n是否从备份恢复？当前文件会另存保留。", "恢复待办", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    {
                        try { DataStore.Recover(data); continue; }
                        catch (Exception recovery) { MessageBox.Show("备份恢复失败：" + recovery.Message, "拾序", MessageBoxButton.OK, MessageBoxImage.Error); }
                    }
                    using (var picker = new Forms.FolderBrowserDialog { Description = "当前数据目录无法使用。请选择一个可写入的数据目录；取消则退出。", ShowNewFolderButton = true })
                    {
                        if (picker.ShowDialog() != Forms.DialogResult.OK) throw new IOException("未选择可用的数据目录。", ex);
                        data = picker.SelectedPath; Settings.DataPath = data;
                    }
                }
            }
            try { SettingsFile.Save(SettingsPath, Settings); }
            catch { Store.Dispose(); throw; }
        }
        public void Start()
        {
            SetPalette(Settings.Theme);
            Store.ExpandThrough(DateTime.Today.AddDays(62));
            Main = new MainWindow(this);
            Application.Current.MainWindow = Main;
            Widget = new WidgetWindow(this);
            Main.Show();
            if (Settings.WidgetVisible) Widget.Show();
            Refresh();
            tray = new Forms.NotifyIcon { Text = "拾序 — 双击打开", Visible = true };
            using (var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("App.ico")) tray.Icon = new System.Drawing.Icon(iconStream);
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开拾序", null, (s, e) => ShowMain());
            menu.Items.Add("添加待办", null, (s, e) => Edit(null, DateTime.Today));
            menu.Items.Add("显示 / 隐藏桌面小组件", null, (s, e) => ToggleWidget());
            menu.Items.Add("找回小组件", null, (s, e) => ResetWidget());
            menu.Items.Add("退出", null, (s, e) => Exit());
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => ShowMain();
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (s, e) =>
            {
                if (lastDay != DateTime.Today) { if (EnsureOccurrences(DateTime.Today.AddDays(62))) { lastDay = DateTime.Today; Refresh(); } }
                if (!TestMode) CheckReminders(DateTime.Now);
                if (Settings.WidgetVisible)
                {
                    if (Widget == null || !Widget.IsLoaded || !NativeDesktop.IsAlive(Widget)) { Widget = new WidgetWindow(this); Widget.Show(); Widget.Refresh(); }
                    if (!NativeDesktop.IsAttached(Widget)) Widget.AttachToDesktop();
                    Widget.UpdateDesktopSurface();
                }
            };
            timer.Start();
            StartFocusClock();
            if (!TestMode) CheckReminders(DateTime.Now);
        }
        public bool EnsureOccurrences(DateTime through)
        {
            try { Store.ExpandThrough(through); return true; }
            catch (Exception ex) { if (TestMode) throw; if (!reminderErrorShown) { reminderErrorShown = true; Error("生成重复待办失败", ex); } return false; }
        }
        public void CheckReminders(DateTime now)
        {
            if (ReminderPopup != null || Exiting) return;
            var due = Store.State.Items.Where(i => Schedule.IsDue(i, now)).OrderByDescending(i => i.Important).ThenBy(i => i.Time).ToList();
            if (due.Count == 0) return;
            try
            {
                Store.Change(items => { foreach (var item in items.Where(i => Schedule.IsDue(i, now))) item.NotifiedFor = Schedule.ReminderKey(item); });
                ReminderPopup = new ReminderWindow(this, due);
                ReminderPopup.Closed += (s, e) => ReminderPopup = null;
                ReminderPopup.Show(); reminderErrorShown = false;
            }
            catch (Exception ex) { if (TestMode) throw; if (!reminderErrorShown) { reminderErrorShown = true; Error("提醒记录保存失败", ex); } }
        }
        public bool SaveTodo(TodoItem item, TodoItem existing, string repeatKind)
        {
            try
            {
                Store.ChangeState(state =>
                {
                    if (existing != null) { int index = state.Items.FindIndex(i => i.Id == item.Id); if (index < 0) throw new InvalidOperationException("这条待办已不存在。"); state.Items.RemoveAt(index); }
                    if (item.SeriesId == null && repeatKind != null) Schedule.AddSeries(state, item.Copy(), repeatKind);
                    else state.Items.Add(item.Copy());
                    Schedule.Expand(state, DateTime.Today.AddDays(62));
                });
                Refresh(); return true;
            }
            catch (Exception ex) { if (TestMode) throw; Error("保存失败", ex); return false; }
        }
        public void ToggleImportant(string id) { Mutate(items => { var item = items.First(i => i.Id == id); item.Important = !item.Important; }); }
        public void StopRepeating(TodoItem item)
        {
            if (item.SeriesId == null) return;
            if (!TestMode && MessageBox.Show("停止这组任务的重复？\n\n保留当次及更早任务，移除后续未完成任务；已完成记录保留。", "停止重复", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                Store.ChangeState(state =>
                {
                    state.Series.RemoveAll(r => r.Id == item.SeriesId);
                    state.Items.RemoveAll(i => i.SeriesId == item.SeriesId && !i.Done && String.CompareOrdinal(i.Date, item.Date) > 0);
                    foreach (var occurrence in state.Items.Where(i => i.SeriesId == item.SeriesId)) occurrence.SeriesId = null;
                });
                deleted = null; Refresh();
            }
            catch (Exception ex) { if (TestMode) throw; Error("停止重复失败", ex); }
        }
        public void Error(string operation, Exception ex) { MessageBox.Show(operation + "：\n" + ex.Message + "\n\n已保存的数据保持不变。", "拾序", MessageBoxButton.OK, MessageBoxImage.Error); }
        public bool Mutate(Action<List<TodoItem>> change)
        {
            try { Store.Change(change); Refresh(); return true; }
            catch (Exception ex) { Refresh(); if (TestMode) throw; Error("保存失败", ex); return false; }
        }
        public void Refresh() { if (Main != null) Main.Refresh(); if (Widget != null) Widget.Refresh(); }
        public void Toggle(string id, bool done) { Mutate(items => items.First(i => i.Id == id).Done = done); }
        public void Delete(string id)
        {
            var item = Store.State.Items.First(i => i.Id == id).Copy();
            if (Mutate(items => items.RemoveAll(i => i.Id == id))) { deleted = item; Refresh(); }
        }
        public void UndoDelete()
        {
            if (deleted == null) return;
            var restore = deleted.Copy();
            if (Mutate(items => items.Add(restore))) { deleted = null; Refresh(); }
        }
        public void Edit(TodoItem item, DateTime? date)
        {
            var editor = new EditWindow(this, item, date);
            if (Main.IsVisible) { editor.Owner = Main; editor.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            editor.ShowDialog();
        }
        public void ShowMain() { Main.Show(); Main.WindowState = WindowState.Normal; Main.Activate(); }
        public bool SaveSettings(AppSettings next)
        {
            try { SettingsFile.Save(SettingsPath, next); Settings = next; return true; }
            catch (Exception ex) { if (TestMode) throw; Error("设置保存失败", ex); return false; }
        }
        public bool ChangeTheme(string name)
        {
            var next = Settings.Copy(); next.Theme = name;
            if (!SaveSettings(next)) return false;
            SetPalette(name); Refresh();
            foreach (Window window in Application.Current.Windows) NativeDesktop.DarkTitle(window, name == "dark");
            return true;
        }
        static void SetPalette(string theme)
        {
            string[] keys = { "Surface", "Panel", "Sidebar", "Text", "Muted", "Accent", "Soft", "Line", "Danger", "AccentInk" };
            string[] colors = theme == "dark"
                ? new[] { "#20242D", "#282D38", "#1A1E26", "#EBEEF5", "#A0A8B8", "#8393FF", "#343D60", "#3A4150", "#FF989A", "#141A30" }
                : theme == "light"
                ? new[] { "#FBFBFD", "#FFFFFF", "#F2F4F8", "#1D2433", "#727A89", "#2167EA", "#E9F0FF", "#E0E5EC", "#C24248", "#FFFFFF" }
                : new[] { "#FBFAF6", "#FFFFFF", "#F0EEE6", "#283027", "#778071", "#677C51", "#E9EDDF", "#E1E4D8", "#BA514C", "#FFFFFF" };
            for (int n = 0; n < keys.Length; n++) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[n])); brush.Freeze(); Application.Current.Resources[keys[n]] = brush; }
            Application.Current.Resources["GlassTint"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme == "dark" ? "#B21B202B" : theme == "warm" ? "#A6F4F1E7" : "#A6F4F7FC"));
            Application.Current.Resources["GlassCard"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme == "dark" ? "#70343B49" : "#90FFFFFF"));
            Application.Current.Resources["GlassBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme == "dark" ? "#707C879E" : "#A0FFFFFF"));
        }
        public void ThemeMenu(FrameworkElement owner)
        {
            var menu = new System.Windows.Controls.ContextMenu();
            string[] keys = { "light", "warm", "dark" }, names = { "蓝白", "暖色", "深色" };
            for (int n = 0; n < keys.Length; n++)
            {
                string key = keys[n]; var item = new System.Windows.Controls.MenuItem { Header = names[n], IsCheckable = true, IsChecked = Settings.Theme == key };
                item.Click += (s, e) => ChangeTheme(key); menu.Items.Add(item);
            }
            menu.PlacementTarget = owner; menu.IsOpen = true;
        }
        public void ToggleWidget()
        {
            var next = Settings.Copy(); next.WidgetVisible = !next.WidgetVisible;
            if (!SaveSettings(next)) return;
            if (Settings.WidgetVisible) { if (Widget == null) Widget = new WidgetWindow(this); Widget.Show(); Widget.AttachToDesktop(); }
            else Widget.Hide();
            Refresh();
        }
        public void ResetWidget()
        {
            var next = Settings.Copy(); next.WidgetX = next.WidgetY = -100000; next.WidgetVisible = true;
            if (!SaveSettings(next)) return;
            if (Widget == null) Widget = new WidgetWindow(this);
            Widget.Show(); Widget.AttachToDesktop(); Widget.Place();
        }
        public bool MoveData(string directory)
        {
            DataStore migrated = null;
            try
            {
                migrated = Store.MoveTo(directory);
                var next = Settings.Copy(); next.DataPath = migrated.DirectoryPath;
                SettingsFile.Save(SettingsPath, next);
                Store.Dispose(); Store = migrated; Settings = next; deleted = null; Refresh(); return true;
            }
            catch (Exception ex) { if (migrated != null) migrated.Dispose(); if (TestMode) throw; Error("更换位置失败，仍使用原目录", ex); return false; }
        }
        public void Import(string path)
        {
            try { if (Store.State.ActiveFocus != null) throw new InvalidOperationException("请先结束当前学习，再导入备份。"); var imported = DataStore.ReadState(path); Schedule.Expand(imported, DateTime.Today.AddDays(62)); Store.ChangeState(state => { state.Items = imported.Items; state.Series = imported.Series; state.FocusHistory = imported.FocusHistory; state.ActiveFocus = imported.ActiveFocus; }); deleted = null; Refresh(); }
            catch (Exception ex) { Error("导入失败", ex); }
        }
        public void Exit()
        {
            if (Exiting) return;
            if (!PauseFocus()) return;
            Exiting = true;
            StopFocusClock();
            if (timer != null) timer.Stop();
            if (ReminderPopup != null) ReminderPopup.Close();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (Widget != null) { NativeDesktop.Detach(Widget); Widget.Close(); }
            if (Main != null) Main.Close();
            Store.Dispose(); Application.Current.Shutdown();
        }
    }
}
