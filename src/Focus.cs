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
        readonly TextBox goalMinutes = new TextBox { Width = 62, MaxLength = 4, ToolTip = "每天 1–1440 分钟；填 0 关闭目标" };
        readonly TextBlock goalStatus = Ui.Text("", 11, "Muted");
        readonly ProgressBar goalProgress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5, Margin = new Thickness(0, 7, 0, 0) };
        readonly Button start, pause, finish;
        Button downMode, upMode;
        readonly StackPanel taskList = new StackPanel();
        readonly System.Windows.Controls.Primitives.UniformGrid trend = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7 };
        object taskState; DateTime taskDay;
        string shownSession, historyKey;
        public FocusPanel(AppController controller)
        {
            app = controller;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = new GridLength(136) });
            var timerPanel = new StackPanel(); timerPanel.Children.Add(Ui.Text("这次学什么", 18, "Text", true));
            name.FontSize = 23; name.FontWeight = FontWeights.SemiBold; name.Height = 44; name.Padding = new Thickness(8, 0, 8, 0); name.VerticalContentAlignment = VerticalAlignment.Center; name.Background = Brushes.Transparent; name.Margin = new Thickness(0, 6, 0, 10); AutomationProperties.SetName(name, "学习名称"); timerPanel.Children.Add(name);
            AutomationProperties.SetName(mode, "学习计时模式"); mode.Visibility = Visibility.Collapsed; timerPanel.Children.Add(mode); var modeBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 12) }; downMode = Ui.Button("倒计时", () => mode.SelectedIndex = 0, "Quiet"); upMode = Ui.Button("正向计时", () => mode.SelectedIndex = 1, "Quiet"); modeBar.Children.Add(downMode); modeBar.Children.Add(upMode); timerPanel.Children.Add(modeBar);
            var label = Ui.Text("专注时长", 13, "Muted"); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 0, 12, 0); durationRow.Children.Add(label);
            AutomationProperties.SetName(minutes, "学习分钟"); durationRow.Children.Add(minutes);
            var unit = Ui.Text("分钟 · 1–720", 12, "Muted"); unit.Margin = new Thickness(12, 0, 0, 0); unit.VerticalAlignment = VerticalAlignment.Center; durationRow.Children.Add(unit); timerPanel.Children.Add(durationRow);
            countdown.FontSize = 82; countdown.FontFamily = new FontFamily("Consolas"); countdown.HorizontalAlignment = HorizontalAlignment.Center; countdown.Margin = new Thickness(0, 12, 0, 4); AutomationProperties.SetName(countdown, "学习倒计时"); timerPanel.Children.Add(countdown);
            phase.HorizontalAlignment = HorizontalAlignment.Center; phase.Margin = new Thickness(0, 0, 0, 10); timerPanel.Children.Add(phase);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            start = Ui.Button("开始学习", () => { int value = 0; bool countUp = mode.SelectedIndex == 1; if (String.IsNullOrWhiteSpace(name.Text) || (!countUp && (!Int32.TryParse(minutes.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 1 || value > 720))) { phase.Text = countUp ? "请填写学习名称" : "请填写名称和 1–720 分钟的时长"; return; } app.StartFocus(name.Text, value, countUp); }, "Primary"); controls.Children.Add(start);
            pause = Ui.Button("暂停", () => { if (app.FocusRunning) app.PauseFocus(); else app.ResumeFocus(); }); pause.Margin = new Thickness(10, 0, 0, 0); controls.Children.Add(pause); timerPanel.Children.Add(controls);
            finish = Ui.Button("结束并记录", () => app.FinishFocus(app.Store.State.ActiveFocus != null && app.Store.State.ActiveFocus.IsCountUp), "Quiet"); finish.Margin = new Thickness(8, 0, 0, 0); controls.Children.Add(finish);
            var hint = Ui.Text("暂停时间不计入学习时长。关闭主窗口仍计时，彻底退出或休眠时暂停。", 11, "Muted"); hint.Visibility = Visibility.Collapsed; timerPanel.Children.Add(hint);
            var timerCard = Ui.Card(Ui.Scroll(timerPanel), new Thickness(24)); timerCard.Margin = new Thickness(20, 0, 0, 14); timerCard.BorderThickness = new Thickness(0); Grid.SetColumn(timerCard, 1); Children.Add(timerCard);
            var stats = new DockPanel(); var listHeader = new DockPanel(); DockPanel.SetDock(listHeader, Dock.Top); stats.Children.Add(listHeader);
            var addTask = Ui.Button("＋ 添加待办", () => app.Edit(null, DateTime.Today), "Quiet"); DockPanel.SetDock(addTask, Dock.Right); listHeader.Children.Add(addTask); listHeader.Children.Add(Ui.Text("今日任务与记录", 17, "Text", true));
            var listContent = new StackPanel(); listContent.Children.Add(taskList); var recorded = Ui.Text("已记录的学习", 12, "Muted"); recorded.Margin = new Thickness(0, 18, 0, 12); listContent.Children.Add(recorded); listContent.Children.Add(history);
            stats.Children.Add(Ui.Scroll(listContent)); var statsCard = Ui.Card(stats, new Thickness(16)); statsCard.BorderThickness = new Thickness(0); statsCard.Margin = new Thickness(0, 0, 0, 14); Children.Add(statsCard);
            var band = new Grid(); band.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) }); band.ColumnDefinitions.Add(new ColumnDefinition()); band.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            var summary = new StackPanel(); summary.Children.Add(Ui.Text("今日学习", 14, "Text", true)); dayLabel.Margin = new Thickness(0, 3, 0, 5); summary.Children.Add(dayLabel); total.FontSize = 30; summary.Children.Add(total); summary.Children.Add(count); band.Children.Add(summary);
            var trendPanel = new StackPanel(); trendPanel.Children.Add(Ui.Text("近7天学习时长 · 分钟", 11, "Muted")); trend.Margin = new Thickness(0, 6, 0, 0); trendPanel.Children.Add(trend); Grid.SetColumn(trendPanel, 1); band.Children.Add(trendPanel);
            var goal = new StackPanel { Margin = new Thickness(20, 0, 0, 0) }; goal.Children.Add(Ui.Text("每日学习目标", 14, "Text", true));
            var goalRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) }; goalMinutes.Text = app.Store.State.DailyGoalMinutes.ToString(); AutomationProperties.SetName(goalMinutes, "每日目标分钟"); goalRow.Children.Add(goalMinutes);
            var goalUnit = Ui.Text("分钟", 11, "Muted"); goalUnit.VerticalAlignment = VerticalAlignment.Center; goalUnit.Margin = new Thickness(7, 0, 7, 0); goalRow.Children.Add(goalUnit);
            var saveGoal = Ui.Button("设置", () => { int value; if (!Int32.TryParse(goalMinutes.Text.Trim(), out value) || value < 0 || value > 1440) { goalStatus.Text = "请输入 0–1440 分钟"; return; } if (app.SetDailyGoal(value)) Refresh(); }); AutomationProperties.SetName(saveGoal, "保存每日目标"); saveGoal.Padding = new Thickness(8, 4, 8, 4); goalRow.Children.Add(saveGoal); goal.Children.Add(goalRow); goal.Children.Add(goalStatus); goalProgress.SetResourceReference(ProgressBar.ForegroundProperty, "Accent"); goalProgress.SetResourceReference(ProgressBar.BackgroundProperty, "Soft"); goal.Children.Add(goalProgress); Grid.SetColumn(goal, 2); band.Children.Add(goal);
            var bandCard = Ui.Card(band, new Thickness(16)); Grid.SetRow(bandCard, 1); Grid.SetColumnSpan(bandCard, 2); Children.Add(bandCard);
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
            name.IsEnabled = minutes.IsEnabled = mode.IsEnabled = downMode.IsEnabled = upMode.IsEnabled = active == null; downMode.SetResourceReference(Button.BackgroundProperty, mode.SelectedIndex == 0 ? "Soft" : "Surface"); upMode.SetResourceReference(Button.BackgroundProperty, mode.SelectedIndex == 1 ? "Soft" : "Surface"); start.IsEnabled = active == null; pause.IsEnabled = finish.IsEnabled = active != null; durationRow.Visibility = mode.SelectedIndex == 1 ? Visibility.Collapsed : Visibility.Visible;
            pause.Content = active == null || app.FocusRunning ? "暂停" : "继续"; AutomationProperties.SetName(pause, pause.Content.ToString());
            if (active != null) { countdown.Text = Study.FormatDuration(active.IsCountUp ? app.FocusSeconds : Math.Ceiling(Math.Max(0, active.TargetSeconds - app.FocusSeconds))); phase.Text = app.FocusRunning ? active.IsCountUp ? "正向计时中 · 随时结束并记录" : "专注中 · 一次只做一件事" : "已暂停 · 可继续本次学习"; }
            else { int value; countdown.Text = Study.FormatDuration(mode.SelectedIndex == 0 && Int32.TryParse(minutes.Text, out value) && value > 0 && value <= 720 ? value * 60 : 0); phase.Text = mode.SelectedIndex == 1 ? "无需预设时长，从 00:00 开始" : "准备开始"; }
            var today = DateTime.Today; dayLabel.Text = today.ToString("M月d日 dddd");
            var records = app.Store.State.FocusHistory.Where(f => Study.SecondsOn(f, today) > 0 || DateTimeOffset.ParseExact(f.EndedAt, "o", CultureInfo.InvariantCulture).Date == today).ToList();
            double seconds = records.Sum(f => Study.SecondsOn(f, today)); var current = app.FocusSnapshot(); if (current != null) seconds += Study.SecondsOn(current, today);
            total.Text = Study.FormatDuration(seconds);
            int target = app.Store.State.DailyGoalMinutes; goalProgress.Value = target == 0 ? 0 : Math.Min(100, seconds / (target * 60) * 100); goalProgress.Visibility = target == 0 ? Visibility.Collapsed : Visibility.Visible;
            goalStatus.Text = target == 0 ? "未设置目标 · 填 0 可关闭" : seconds >= target * 60 ? "今日目标已达成 ✓" : "已学 " + (int)(seconds / 60) + " 分 · 还差 " + (int)Math.Ceiling(target - seconds / 60) + " 分";
            if (!goalMinutes.IsKeyboardFocusWithin) goalMinutes.Text = target.ToString();
            RefreshTasksAndTrend(today, current);
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
        void RefreshTasksAndTrend(DateTime today, FocusSession current)
        {
            if (Object.ReferenceEquals(taskState, app.Store.State) && taskDay == today) return;
            taskState = app.Store.State; taskDay = today; taskList.Children.Clear();
            var tasks = app.Store.State.Items.Where(i => !i.Done && i.Date == Dates.Key(today)).OrderByDescending(i => i.Important).ThenBy(i => i.Time ?? "99:99").ToList();
            if (tasks.Count == 0) taskList.Children.Add(Ui.Text("今天暂无待办，可直接填写学习名称", 12, "Muted"));
            foreach (var task in tasks) { var button = Ui.Button("", () => { if (app.Store.State.ActiveFocus == null) name.Text = task.Title; }, "Quiet"); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; var row = new StackPanel(); row.Children.Add(Ui.Text((task.Important ? "★ " : "○ ") + task.Title, 14, "Text", true)); row.Children.Add(Ui.Text((task.Time ?? "未指定时间") + (String.IsNullOrWhiteSpace(task.Location) ? "" : " · " + task.Location), 11, "Muted")); button.Content = row; button.Margin = new Thickness(0, 6, 0, 0); button.ToolTip = "点击填入学习名称；不会自动完成待办"; AutomationProperties.SetName(button, "学习任务 " + task.Title); taskList.Children.Add(button); }
            trend.Children.Clear(); var all = app.Store.State.FocusHistory.ToList(); if (current != null) all.Add(current); var report = Study.Summarize(all, today.AddDays(-6), today); double max = Math.Max(60, report.SecondsByDay.Values.Max());
            for (int n = 0; n < 7; n++) { var day = today.AddDays(n - 6); double value = report.SecondsByDay[Dates.Key(day)]; var column = new StackPanel { Margin = new Thickness(7, 0, 7, 0) }; var t = Ui.Text((value / 60).ToString("0.#"), 9, "Muted"); t.TextAlignment = TextAlignment.Center; column.Children.Add(t); var plot = new Grid { Height = 52 }; var bar = new Border { Height = value / max * 52, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(3, 3, 0, 0) }; bar.SetResourceReference(Border.BackgroundProperty, "Accent"); plot.Children.Add(bar); column.Children.Add(plot); var date = Ui.Text(day.ToString("M/d"), 10, "Muted"); date.TextAlignment = TextAlignment.Center; column.Children.Add(date); trend.Children.Add(column); }
        }
    }
}
