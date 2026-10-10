using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DeskTodo
{
    public partial class AppController
    {
        DispatcherTimer focusTimer;
        Stopwatch focusWatch;
        double focusCheckpointed;
        DateTimeOffset focusSegmentStart;
        PowerModeChangedEventHandler focusPowerHandler;
        public bool FocusRunning { get { return focusWatch != null && focusWatch.IsRunning; } }
        public double FocusSeconds
        {
            get { var focus = Store.State.ActiveFocus; return focus == null ? 0 : focus.IsCountUp ? focus.FocusedSeconds + PendingFocusSeconds : Math.Min(focus.TargetSeconds, focus.FocusedSeconds + PendingFocusSeconds); }
        }
        double PendingFocusSeconds { get { return focusWatch == null ? 0 : Math.Max(0, focusWatch.Elapsed.TotalSeconds - focusCheckpointed); } }
        public FocusSession FocusSnapshot()
        {
            if (Store.State.ActiveFocus == null) return null;
            var state = new TodoState { ActiveFocus = Store.State.ActiveFocus.Copy() };
            if (PendingFocusSeconds > 0) Study.AddTime(state, focusSegmentStart, PendingFocusSeconds);
            return state.ActiveFocus;
        }
        void StartFocusClock()
        {
            focusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            focusTimer.Tick += (s, e) => FocusTick(); focusTimer.Start();
            if (!TestMode)
            {
                focusPowerHandler = (s, e) => { if (e.Mode == PowerModes.Suspend && !Exiting && !Application.Current.Dispatcher.HasShutdownStarted) Application.Current.Dispatcher.Invoke(new Action(() => PauseFocus())); };
                SystemEvents.PowerModeChanged += focusPowerHandler;
            }
        }
        void StopFocusClock()
        {
            if (focusTimer != null) focusTimer.Stop();
            if (focusPowerHandler != null) { SystemEvents.PowerModeChanged -= focusPowerHandler; focusPowerHandler = null; }
        }
        void BeginFocusRun() { focusSegmentStart = DateTimeOffset.Now; focusCheckpointed = 0; focusWatch = Stopwatch.StartNew(); }
        public bool StartFocus(string title, int minutes, bool countUp = false)
        {
            try { Store.ChangeState(state => Study.Start(state, title, minutes, DateTimeOffset.Now, countUp)); BeginFocusRun(); Main.RefreshFocus(); return true; }
            catch (Exception ex) { if (TestMode) throw; Error("开始学习失败", ex); return false; }
        }
        bool SaveFocusProgress()
        {
            if (Store.State.ActiveFocus == null || PendingFocusSeconds <= 0) return true;
            try
            {
                double elapsed = focusWatch.Elapsed.TotalSeconds;
                Store.ChangeState(state => Study.AddTime(state, focusSegmentStart, Math.Max(0, elapsed - focusCheckpointed)));
                focusCheckpointed = elapsed; return true;
            }
            catch (Exception ex) { if (focusWatch != null) focusWatch.Stop(); if (TestMode) throw; Error("学习进度保存失败，计时已暂停", ex); return false; }
        }
        public bool PauseFocus()
        {
            if (focusWatch != null) focusWatch.Stop();
            bool saved = SaveFocusProgress(); if (Main != null) Main.RefreshFocus(); return saved;
        }
        public bool ResumeFocus()
        {
            if (Store.State.ActiveFocus == null || FocusRunning) return false;
            if (Store.State.ActiveFocus.Slices.Count >= 2000) { var ex = new InvalidOperationException("本轮暂停次数已达上限，请结束并记录，再开始新的一轮。"); if (TestMode) throw ex; Error("无法继续本轮学习", ex); return false; }
            if (!SaveFocusProgress()) return false;
            BeginFocusRun(); Main.RefreshFocus(); return true;
        }
        public bool FinishFocus(bool completed)
        {
            if (Store.State.ActiveFocus == null) return false;
            if (focusWatch != null) focusWatch.Stop();
            var focus = FocusSnapshot();
            try
            {
                double pending = PendingFocusSeconds;
                Store.ChangeState(state => { if (pending > 0) Study.AddTime(state, focusSegmentStart, pending); Study.Finish(state, DateTimeOffset.Now, completed); });
                focusWatch = null; focusCheckpointed = 0; Main.RefreshFocus();
                if (completed)
                {
                    if (ReminderPopup != null) ReminderPopup.Close();
                    ReminderPopup = new ReminderWindow(this, new List<TodoItem> { new TodoItem { Title = focus.Title, Time = "已专注 " + Study.FormatDuration(focus.FocusedSeconds) } }, "番茄钟", "拾序 · 学习完成");
                    ReminderPopup.Closed += (s, e) => ReminderPopup = null; ReminderPopup.Show();
                }
                return true;
            }
            catch (Exception ex) { if (TestMode) throw; Error("学习记录保存失败，计时已暂停", ex); Main.RefreshFocus(); return false; }
        }
        internal void FocusTick()
        {
            if (Exiting) return;
            var focus = Store.State.ActiveFocus;
            if (focus != null && FocusRunning)
            {
                if (!focus.IsCountUp && FocusSeconds >= focus.TargetSeconds) FinishFocus(true);
                else if (focusWatch.Elapsed.TotalSeconds - focusCheckpointed >= 5 && !SaveFocusProgress()) focusWatch.Stop();
            }
            Main.RefreshFocus();
        }
    }

    public class FocusPanel : Grid
    {
        readonly AppController app;
        readonly TextBox name = new TextBox { MaxLength = 200, ToolTip = "例如：阅读论文、复习英语" };
        readonly TextBox minutes = new TextBox { Text = "25", Width = 90, MaxLength = 3 };
        readonly ComboBox mode = new ComboBox { ItemsSource = new[] { "倒计时", "正向计时" }, SelectedIndex = 0, Height = 36 };
        readonly StackPanel durationRow = new StackPanel { Orientation = Orientation.Horizontal };
        readonly TextBlock countdown = Ui.Text("25:00", 66, "Accent", true);
        readonly TextBlock phase = Ui.Text("准备开始", 13, "Muted");
        readonly TextBlock dayLabel = Ui.Text("", 12, "Muted");
        readonly TextBlock total = Ui.Text("00:00", 32, "Text", true);
        readonly TextBlock count = Ui.Text("", 12, "Muted");
        readonly StackPanel history = new StackPanel();
        readonly Button start, pause, finish;
        string shownSession, historyKey;
        public FocusPanel(AppController controller)
        {
            app = controller;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var timerPanel = new StackPanel(); timerPanel.Children.Add(Ui.Text("这次学什么", 18, "Text", true));
            name.Margin = new Thickness(0, 16, 0, 16); AutomationProperties.SetName(name, "学习名称"); timerPanel.Children.Add(name);
            AutomationProperties.SetName(mode, "学习计时模式"); mode.Margin = new Thickness(0, 0, 0, 14); timerPanel.Children.Add(mode);
            var label = Ui.Text("专注时长", 13, "Muted"); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 0, 12, 0); durationRow.Children.Add(label);
            AutomationProperties.SetName(minutes, "学习分钟"); durationRow.Children.Add(minutes);
            var unit = Ui.Text("分钟 · 1–720", 12, "Muted"); unit.Margin = new Thickness(12, 0, 0, 0); unit.VerticalAlignment = VerticalAlignment.Center; durationRow.Children.Add(unit); timerPanel.Children.Add(durationRow);
            countdown.FontFamily = new FontFamily("Consolas"); countdown.HorizontalAlignment = HorizontalAlignment.Center; countdown.Margin = new Thickness(0, 38, 0, 8); AutomationProperties.SetName(countdown, "学习倒计时"); timerPanel.Children.Add(countdown);
            phase.HorizontalAlignment = HorizontalAlignment.Center; phase.Margin = new Thickness(0, 0, 0, 26); timerPanel.Children.Add(phase);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            start = Ui.Button("开始学习", () => { int value = 0; bool countUp = mode.SelectedIndex == 1; if (String.IsNullOrWhiteSpace(name.Text) || (!countUp && (!Int32.TryParse(minutes.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 1 || value > 720))) { phase.Text = countUp ? "请填写学习名称" : "请填写名称和 1–720 分钟的时长"; return; } app.StartFocus(name.Text, value, countUp); }, "Primary"); controls.Children.Add(start);
            pause = Ui.Button("暂停", () => { if (app.FocusRunning) app.PauseFocus(); else app.ResumeFocus(); }); pause.Margin = new Thickness(10, 0, 0, 0); controls.Children.Add(pause); timerPanel.Children.Add(controls);
            finish = Ui.Button("结束并记录", () => app.FinishFocus(app.Store.State.ActiveFocus != null && app.Store.State.ActiveFocus.IsCountUp), "Quiet"); finish.HorizontalAlignment = HorizontalAlignment.Center; finish.Margin = new Thickness(0, 12, 0, 0); timerPanel.Children.Add(finish);
            var hint = Ui.Text("暂停时间不计入学习时长。关闭主窗口仍计时，彻底退出或休眠时暂停。", 11, "Muted"); hint.Margin = new Thickness(0, 24, 0, 0); timerPanel.Children.Add(hint);
            var timerCard = Ui.Card(Ui.Scroll(timerPanel), new Thickness(24)); timerCard.Margin = new Thickness(0, 0, 20, 0); Children.Add(timerCard);
            var stats = new DockPanel(); var summary = new StackPanel { Margin = new Thickness(0, 0, 0, 20) }; DockPanel.SetDock(summary, Dock.Top); stats.Children.Add(summary);
            summary.Children.Add(Ui.Text("今日学习", 20, "Text", true)); dayLabel.Margin = new Thickness(0, 6, 0, 20); summary.Children.Add(dayLabel);
            summary.Children.Add(Ui.Text("累计专注时长（分:秒）", 12, "Muted")); total.Margin = new Thickness(0, 6, 0, 6); summary.Children.Add(total); summary.Children.Add(count);
            var divider = new Border { Height = 1, Margin = new Thickness(0, 20, 0, 14) }; divider.SetResourceReference(Border.BackgroundProperty, "Line"); summary.Children.Add(divider);
            summary.Children.Add(Ui.Text("学习记录", 14, "Text", true)); summary.Children.Add(Ui.Text("按实际计时统计；进行中的时长也计入累计。", 11, "Muted"));
            stats.Children.Add(Ui.Scroll(history)); var statsCard = Ui.Card(stats, new Thickness(22)); Grid.SetColumn(statsCard, 1); Children.Add(statsCard);
            minutes.TextChanged += (s, e) => { if (app.Store.State.ActiveFocus == null) { int value; if (Int32.TryParse(minutes.Text, out value) && value >= 1 && value <= 720) countdown.Text = Study.FormatDuration(value * 60); } };
            mode.SelectionChanged += (s, e) => Refresh();
            Refresh();
        }
        public void Refresh(bool forceHistory = false)
        {
            if (forceHistory) historyKey = null;
            var active = app.Store.State.ActiveFocus;
            if (active != null && (shownSession != active.Id || forceHistory)) { shownSession = active.Id; name.Text = active.Title; if (!active.IsCountUp) minutes.Text = (active.TargetSeconds / 60).ToString(); mode.SelectedIndex = active.IsCountUp ? 1 : 0; }
            if (active == null) shownSession = null;
            name.IsEnabled = minutes.IsEnabled = mode.IsEnabled = active == null; start.IsEnabled = active == null; pause.IsEnabled = finish.IsEnabled = active != null; durationRow.Visibility = mode.SelectedIndex == 1 ? Visibility.Collapsed : Visibility.Visible;
            pause.Content = active == null || app.FocusRunning ? "暂停" : "继续"; AutomationProperties.SetName(pause, pause.Content.ToString());
            if (active != null) { countdown.Text = Study.FormatDuration(active.IsCountUp ? app.FocusSeconds : Math.Ceiling(Math.Max(0, active.TargetSeconds - app.FocusSeconds))); phase.Text = app.FocusRunning ? active.IsCountUp ? "正向计时中 · 随时结束并记录" : "专注中 · 一次只做一件事" : "已暂停 · 可继续本次学习"; }
            else { int value; countdown.Text = Study.FormatDuration(mode.SelectedIndex == 0 && Int32.TryParse(minutes.Text, out value) && value > 0 && value <= 720 ? value * 60 : 0); phase.Text = mode.SelectedIndex == 1 ? "无需预设时长，从 00:00 开始" : "准备开始"; }
            var today = DateTime.Today; dayLabel.Text = today.ToString("M月d日 dddd");
            var records = app.Store.State.FocusHistory.Where(f => Study.SecondsOn(f, today) > 0 || DateTimeOffset.ParseExact(f.EndedAt, "o", CultureInfo.InvariantCulture).Date == today).ToList();
            double seconds = records.Sum(f => Study.SecondsOn(f, today)); var current = app.FocusSnapshot(); if (current != null) seconds += Study.SecondsOn(current, today);
            total.Text = Study.FormatDuration(seconds);
            int completed = records.Count(f => f.Completed && DateTimeOffset.ParseExact(f.EndedAt, "o", CultureInfo.InvariantCulture).Date == today);
            int ended = records.Count(f => !f.Completed && DateTimeOffset.ParseExact(f.EndedAt, "o", CultureInfo.InvariantCulture).Date == today);
            count.Text = "完成 " + completed + " 次  ·  提前结束 " + ended + " 次";
            string key = Dates.Key(today) + ":" + app.Store.State.FocusHistory.Count + ":" + (app.Store.State.FocusHistory.LastOrDefault() == null ? "" : app.Store.State.FocusHistory.Last().Id);
            if (key == historyKey) return; historyKey = key; history.Children.Clear();
            if (records.Count == 0) Ui.Empty(history, "今天还没有学习记录", "结束一次学习后，记录会出现在这里");
            foreach (var record in records.OrderByDescending(f => DateTimeOffset.ParseExact(f.StartedAt, "o", CultureInfo.InvariantCulture)))
            {
                var row = new StackPanel(); row.Children.Add(Ui.Text(record.Title, 14, "Text", true));
                var duration = Ui.Text("今日 " + Study.FormatDuration(Study.SecondsOn(record, today)) + (record.IsCountUp ? "  ·  正向计时" : "  ·  计划 " + (record.TargetSeconds / 60) + " 分钟"), 12, "Muted"); duration.Margin = new Thickness(0, 6, 0, 4); row.Children.Add(duration);
                var from = DateTimeOffset.ParseExact(record.StartedAt, "o", CultureInfo.InvariantCulture); var to = DateTimeOffset.ParseExact(record.EndedAt, "o", CultureInfo.InvariantCulture);
                row.Children.Add(Ui.Text(from.ToString("M/d HH:mm") + " – " + to.ToString("M/d HH:mm") + "  ·  " + (record.Completed ? "已完成" : "提前结束"), 11, record.Completed ? "Accent" : "Muted"));
                var card = Ui.Card(row, new Thickness(14)); card.Margin = new Thickness(0, 0, 0, 10); history.Children.Add(card);
            }
        }
    }
}
