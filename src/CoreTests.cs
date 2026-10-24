using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using DeskTodo;

class CoreTests
{
    static int passed;
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    static void Throws(Action action, string name) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, name); }
    static TodoItem Item(string title, string date) { return new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = title, Date = date, Note = "中文备注", CreatedUtc = DateTime.UtcNow.ToString("o") }; }
    static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetFullPath(args[0]), "core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Check(typeof(TodoState).GetField("Expenses") != null, "state exposes independent expense records");
            var expenses = new TodoState();
            expenses.Expenses.Add(new ExpenseRecord { Id = Guid.NewGuid().ToString("N"), Date = "2026-10-24", Amount = 0.1m, Category = "餐饮", Note = "示例" });
            expenses.Expenses.Add(new ExpenseRecord { Id = Guid.NewGuid().ToString("N"), Date = "2026-10-24", Amount = 0.2m, Category = "其他" });
            expenses.Validate(); Check(expenses.Expenses.Sum(e => e.Amount) == 0.3m, "expense decimal totals exact");
            var expenseCopy = expenses.Copy(); expenseCopy.Expenses[0].Note = "副本";
            Check(expenses.Expenses[0].Note == "示例", "expense snapshots deep copied");
            foreach (decimal invalid in new[] { 0m, -1m, .001m, 100000000m }) { expenseCopy.Expenses[0].Amount = invalid; Throws(() => expenseCopy.Validate(), "invalid expense amount rejected " + invalid); }
            expenseCopy.Expenses[0].Amount = 99999999.99m; expenseCopy.Validate(); Check(true, "maximum expense amount accepted");
            expenseCopy.Expenses[0].Category = "无效"; Throws(() => expenseCopy.Validate(), "invalid expense category rejected");
            expenseCopy = expenses.Copy(); expenseCopy.Expenses[0].Date = "2026-02-30"; Throws(() => expenseCopy.Validate(), "invalid expense date rejected");
            expenseCopy = expenses.Copy(); expenseCopy.Expenses[0].Note = new string('字', 201); Throws(() => expenseCopy.Validate(), "long expense note rejected");
            expenseCopy = expenses.Copy(); expenseCopy.Expenses[0].Id = "bad"; Throws(() => expenseCopy.Validate(), "invalid expense id rejected");
            expenseCopy = expenses.Copy(); expenseCopy.Expenses.Add(expenseCopy.Expenses[0].Copy()); Throws(() => expenseCopy.Validate(), "duplicate expense id rejected");
            expenseCopy = expenses.Copy(); expenseCopy.Expenses.Add(null); Throws(() => expenseCopy.Validate(), "null expense record rejected");
            expenseCopy = expenses.Copy(); expenseCopy.Expenses = null; Throws(() => expenseCopy.Validate(), "null expense list rejected");
            expenseCopy = expenses.Copy(); expenseCopy.Expenses = Enumerable.Range(0, 10001).Select(n => expenses.Expenses[0].Copy()).ToList(); Throws(() => expenseCopy.Validate(), "expense count limit enforced");
            string legacyExpenses = Path.Combine(root, "expenses-v8.json"); File.WriteAllText(legacyExpenses, "{\"Version\":8,\"Items\":[]}");
            Check(DataStore.ReadState(legacyExpenses).Expenses.Count == 0, "v8 defaults to empty expense list");
            using (var expenseStore = DataStore.Open(Path.Combine(root, "expenses")))
            {
                expenseStore.ChangeState(state => state.Expenses = expenses.Expenses.Select(e => e.Copy()).ToList());
                expenseStore.Change(items => items.Add(Item("普通待办", null)));
                Check(expenseStore.State.Expenses.Count == 2, "todo mutation preserves expenses");
                string saved = File.ReadAllText(Path.Combine(expenseStore.DirectoryPath, "tasks.json"));
                Throws(() => expenseStore.ChangeState(state => state.Expenses[0].Amount = -1), "invalid expense save rejected atomically");
                Check(File.ReadAllText(Path.Combine(expenseStore.DirectoryPath, "tasks.json")) == saved && expenseStore.State.Expenses[0].Amount == .1m, "failed expense save preserves memory and disk");
                string expenseExport = Path.Combine(root, "expenses-export.json"); expenseStore.Export(expenseExport);
                expenseStore.ChangeState(state => state.Expenses.Clear()); expenseStore.Import(expenseExport);
                Check(expenseStore.State.Expenses.Count == 2 && expenseStore.State.Version == 9, "expense backup import restores v9 records");
                using (var expenseMoved = expenseStore.MoveTo(Path.Combine(root, "expenses-moved"))) Check(expenseMoved.State.Expenses.Sum(e => e.Amount) == .3m, "data migration preserves expenses");
            }
            var credits = new TodoState();
            var activity = Item("综测任务", "2026-10-23"); activity.CreditPoints = 0.5m; credits.Items.Add(activity);
            TaskCredits.Sync(credits, activity, DateTime.Today); Check(credits.Credits.Count == 0, "unfinished activity earns no credit");
            activity.Done = true; TaskCredits.Sync(credits, activity, DateTime.Today); TaskCredits.Sync(credits, activity, DateTime.Today);
            Check(credits.Credits.Count == 1 && credits.Credits[0].Points == 0.5m, "completion adds exactly one linked credit");
            activity.CreditPoints = 1.25m; activity.Title = "修改后的活动"; TaskCredits.Sync(credits, activity, DateTime.Today);
            Check(credits.Credits.Single().Title == activity.Title && credits.Credits.Single().Points == 1.25m, "editing completed activity updates its credit");
            activity.Done = false; TaskCredits.Sync(credits, activity, DateTime.Today); Check(credits.Credits.Count == 0, "undo completion retracts credit");
            activity.Date = null; activity.Done = true; TaskCredits.Sync(credits, activity, new DateTime(2026, 10, 24));
            Check(credits.Credits.Single().Date == "2026-10-24", "undated activity uses completion date");
            activity.CreditPoints = 0; TaskCredits.Sync(credits, activity, DateTime.Today); Check(credits.Credits.Count == 0, "ordinary completed tasks earn no credit");
            activity.CreditPoints = -1; Throws(() => credits.Validate(), "negative task credit rejected"); activity.CreditPoints = 10001; Throws(() => credits.Validate(), "excessive task credit rejected");
            activity.CreditPoints = .5m; activity.Date = "2026-10-23"; activity.Done = false;
            var repeated = new TodoState(); Schedule.AddSeries(repeated, activity.Copy(), "daily"); Schedule.Expand(repeated, new DateTime(2026, 10, 25));
            foreach (var occurrence in repeated.Items) { occurrence.Done = true; TaskCredits.Sync(repeated, occurrence, DateTime.Today); }
            Check(repeated.Credits.Count == 3 && repeated.Credits.Sum(c => c.Points) == 1.5m, "repeat occurrences receive separate credits");
            using (var linkedStore = DataStore.Open(Path.Combine(root, "linked")))
            {
                linkedStore.ChangeState(state => { state.Items = repeated.Items; state.Series = repeated.Series; state.Credits = repeated.Credits; });
                var linkedRestored = DataStore.ReadState(Path.Combine(linkedStore.DirectoryPath, "tasks.json"));
                Check(linkedRestored.Items.All(i => i.CreditPoints == .5m) && linkedRestored.Credits.All(c => c.SourceTaskId != null), "task credit settings and links persist");
                Throws(() => linkedStore.ChangeState(state => { state.Credits[0].Points = -1; state.Items[0].Done = false; }), "task and credit mutation fails atomically");
                Check(linkedStore.State.Items[0].Done && linkedStore.State.Credits[0].Points == .5m, "failed transaction preserves task and credit");
            }
            credits.Items.Clear();
            credits.Credits.Add(new CreditRecord { Id = Guid.NewGuid().ToString("N"), Title = "志愿服务", Date = "2026-10-22", Points = 0.1m });
            credits.Credits.Add(new CreditRecord { Id = Guid.NewGuid().ToString("N"), Title = "学术讲座", Date = "2026-10-21", Points = 0.2m });
            credits.Validate(); Check(credits.Credits.Sum(c => c.Points) == 0.3m, "credit decimal total is exact");
            var creditsCopy = credits.Copy(); creditsCopy.Credits[0].Title = "副本";
            Check(credits.Credits[0].Title == "志愿服务", "credit snapshots are deep copied");
            creditsCopy.Credits[0].Points = -1; Throws(() => creditsCopy.Validate(), "negative credits rejected");
            creditsCopy.Credits[0].Points = 0; Throws(() => creditsCopy.Validate(), "zero credits rejected");
            creditsCopy.Credits[0].Points = 1; creditsCopy.Credits[0].Title = " "; Throws(() => creditsCopy.Validate(), "empty credit activity rejected");
            creditsCopy.Credits[0].Title = "测试"; creditsCopy.Credits[0].Date = "2026-02-30"; Throws(() => creditsCopy.Validate(), "invalid credit date rejected");
            creditsCopy = credits.Copy(); creditsCopy.Credits.Add(creditsCopy.Credits[0].Copy()); Throws(() => creditsCopy.Validate(), "duplicate credit IDs rejected");
            using (var creditStore = DataStore.Open(Path.Combine(root, "credits")))
            {
                creditStore.ChangeState(state => state.Credits = credits.Credits.Select(c => c.Copy()).ToList());
                creditStore.Change(items => items.Add(Item("普通待办", null)));
                string creditExport = Path.Combine(root, "credits-export.json"); creditStore.Export(creditExport);
                var creditRestored = DataStore.ReadState(creditExport);
                Check(creditRestored.Version == 9 && creditRestored.Credits.Sum(c => c.Points) == 0.3m && creditRestored.Items.Count == 1, "todo saves and exports preserve credits");
                creditStore.ChangeState(state => state.Credits.Clear()); creditStore.Import(creditExport);
                Check(creditStore.State.Credits.Count == 2, "import replaces and restores credits");
                Throws(() => creditStore.ChangeState(state => state.Credits[0].Points = -1), "invalid credit save fails atomically");
                Check(DataStore.ReadState(Path.Combine(creditStore.DirectoryPath, "tasks.json")).Credits[0].Points == 0.1m, "failed credit save leaves disk unchanged");
            }
            string legacyCredits = Path.Combine(root, "credits-v6.json"); File.WriteAllText(legacyCredits, "{\"Version\":6,\"Items\":[]}");
            Check(DataStore.ReadState(legacyCredits).Credits.Count == 0, "v6 data defaults to empty credit list");
            var skinSettings = new AppSettings(); Check(skinSettings.BuiltInSkin == "", "existing defaults do not select a built-in skin");
            skinSettings.BuiltInSkin = "mountain"; skinSettings.Validate();
            string skinPath = Path.Combine(root, "skin-settings.json"); SettingsFile.Save(skinPath, skinSettings);
            Check(SettingsFile.Load(skinPath).BuiltInSkin == "mountain", "built-in skin persists");
            skinSettings.BuiltInSkin = "sunlit"; skinSettings.Validate();
            skinSettings.BuiltInSkin = "../private"; Throws(() => skinSettings.Validate(), "unknown built-in skin rejected");
            skinSettings.BuiltInSkin = null; Throws(() => skinSettings.Validate(), "null built-in skin rejected");
            File.WriteAllText(skinPath, "{\"Theme\":\"warm\"}"); Check(SettingsFile.Load(skinPath).BuiltInSkin == "", "legacy settings default to no built-in skin");
            var backgroundSettings = new AppSettings(); Check(backgroundSettings.BackgroundFile == "" && backgroundSettings.BackgroundShade == 0.65, "default background remains disabled with readable shade");
            backgroundSettings.BackgroundFile = "../private.png"; Throws(() => backgroundSettings.Validate(), "background settings reject paths outside managed image directory");
            backgroundSettings.BackgroundFile = "background-" + Guid.NewGuid().ToString("N") + ".png"; backgroundSettings.Validate();
            backgroundSettings.BackgroundShade = Double.NaN; Throws(() => backgroundSettings.Validate(), "background opacity rejects nonfinite values");
            backgroundSettings.BackgroundShade = 0.1; Throws(() => backgroundSettings.Validate(), "background shade retains minimum text contrast layer");
            var tagged = Item("组会", "2026-10-11"); tagged.Tags = TodoSearch.ParseTags("科研，生活, 科研 ; Paper;paper"); tagged.Location = "B528";
            Check(tagged.Tags.SequenceEqual(new[] { "科研", "生活", "Paper" }), "tag input trims and deduplicates Chinese and English separators");
            Check(TodoSearch.Matches(tagged, "B528", "科研") && TodoSearch.Matches(tagged, "paper", ""), "todo search includes location and case insensitive tags");
            Check(!TodoSearch.Matches(tagged, "组会", "课程") && !TodoSearch.Matches(tagged, "不存在", "科研"), "text and tag filters combine without unrelated matches");
            var taggedState = new TodoState { DailyGoalMinutes = 120, Items = new List<TodoItem> { tagged } }; var tagCopy = taggedState.Copy(); tagCopy.Items[0].Tags.Add("副本");
            Check(tagged.Tags.Count == 3 && tagCopy.DailyGoalMinutes == 120, "atomic state copies deep copy tags and preserve goal");
            string taggedPath = Path.Combine(root, "tagged.json"); JsonFile.Write(taggedPath, taggedState); var tagRestore = DataStore.ReadState(taggedPath);
            Check(tagRestore.DailyGoalMinutes == 120 && tagRestore.Items[0].Tags.SequenceEqual(tagged.Tags), "goals and tags survive export and reload");
            taggedState.DailyGoalMinutes = 1441; Throws(() => taggedState.Validate(), "goal rejects more than one day"); taggedState.DailyGoalMinutes = 0; taggedState.Validate();
            tagged.Tags.Add(new string('字', 21)); Throws(() => taggedState.Validate(), "oversized tags are rejected at storage boundary"); tagged.Tags.RemoveAt(3);
            var weekRange = Study.PeriodRange(new DateTime(2026, 10, 11), false);
            Check(weekRange[0] == new DateTime(2026, 10, 5) && weekRange[1] == new DateTime(2026, 10, 11), "weekly reports run Monday through Sunday");
            var monthRange = Study.PeriodRange(new DateTime(2024, 2, 12), true);
            Check(monthRange[0] == new DateTime(2024, 2, 1) && monthRange[1] == new DateTime(2024, 2, 29), "monthly reports use leap year calendar boundaries");
            var yearWeek = Study.PeriodRange(new DateTime(2027, 1, 1), false);
            Check(yearWeek[0] == new DateTime(2026, 12, 28) && yearWeek[1] == new DateTime(2027, 1, 3), "weekly report crosses year boundaries");
            var repeatedA = Item("刷网课", "2026-10-11"); var repeatedB = Item("刷网课", "2026-10-12");
            Check(TodoGroups.Key(repeatedA) == TodoGroups.Key(repeatedB), "identical dated tasks share display group without changing records");
            repeatedB.Location = "不同地点"; Check(TodoGroups.Key(repeatedA) != TodoGroups.Key(repeatedB), "same title with different task content stays separate");
            repeatedA.SeriesId = repeatedB.SeriesId = Guid.NewGuid().ToString("N"); Check(TodoGroups.Key(repeatedA) == TodoGroups.Key(repeatedB), "recurrence series remains grouped after individual edits");
            repeatedA.SeriesId = null; repeatedA.Date = null; repeatedB.SeriesId = null; repeatedB.Date = null;
            Check(TodoGroups.Key(repeatedA) != TodoGroups.Key(repeatedB), "undated tasks are not silently merged");
            var study = new TodoState();
            var openStudy = new TodoState(); var openStart = new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.FromHours(8));
            Study.Start(openStudy, "自由阅读", 0, openStart, true); Study.AddTime(openStudy, openStart, 50000);
            Check(openStudy.ActiveFocus.TargetSeconds == 0 && openStudy.ActiveFocus.FocusedSeconds == 50000, "count-up focus has no planned duration and is not clamped at twelve hours");
            openStudy.Validate(); Study.Finish(openStudy, openStart.AddSeconds(50000), true);
            Check(openStudy.FocusHistory[0].Completed && Study.Summarize(openStudy.FocusHistory, openStart.Date, openStart.Date).TotalSeconds == 50000, "ending count-up records a completed session with actual time");
            string openPath = Path.Combine(root, "count-up.json"); JsonFile.Write(openPath, openStudy);
            Check(DataStore.ReadState(openPath).FocusHistory[0].TargetSeconds == 0, "count-up mode survives serialization");
            var studyStart = new DateTimeOffset(2026, 10, 9, 23, 55, 0, TimeSpan.FromHours(8));
            Study.Start(study, "阅读论文", 25, studyStart);
            Study.AddTime(study, studyStart, 300);
            Study.AddTime(study, studyStart.AddMinutes(10), 1200);
            Check(study.ActiveFocus.FocusedSeconds == 1500 && study.ActiveFocus.Slices.Count == 2, "focus counts active segments and excludes gaps while paused");
            Check(Study.SecondsOn(study.ActiveFocus, new DateTime(2026, 10, 9)) == 300 && Study.SecondsOn(study.ActiveFocus, new DateTime(2026, 10, 10)) == 1200, "study time crossing midnight is assigned to each day");
            Study.AddTime(study, studyStart.AddHours(1), 100);
            Check(study.ActiveFocus.FocusedSeconds == 1500, "focus never counts beyond planned duration");
            Study.Finish(study, studyStart.AddMinutes(30), true);
            Check(study.ActiveFocus == null && study.FocusHistory.Count == 1 && study.FocusHistory[0].Completed, "completed focus session is recorded once");
            Throws(() => Study.Finish(study, studyStart.AddMinutes(31), true), "cannot record the same focus twice");
            Study.Start(study, "复习", 10, studyStart); Study.AddTime(study, studyStart, 90); Study.Finish(study, studyStart.AddMinutes(2), false);
            Check(!study.FocusHistory.Last().Completed && study.FocusHistory.Last().FocusedSeconds == 90, "ending early records actual time rather than planned time");
            Throws(() => Study.Start(study, " ", 25, studyStart), "focus name is required");
            Throws(() => Study.Start(study, "复习", 0, studyStart), "focus duration must be positive");
            Throws(() => Study.Start(study, "复习", 721, studyStart), "focus duration has a bounded maximum");
            Study.Start(study, "读书", 25, studyStart); Study.AddTime(study, studyStart, 15.5);
            Throws(() => Study.Start(study, "另一项", 25, studyStart), "only one active focus is allowed");
            string studyPath = Path.Combine(root, "study.json"); JsonFile.Write(studyPath, study);
            var studyReload = DataStore.ReadState(studyPath);
            Check(studyReload.ActiveFocus.Title == "读书" && studyReload.ActiveFocus.FocusedSeconds == 15.5 && studyReload.FocusHistory.Count == 2, "active progress and study history persist through backup roundtrip");
            Check(Study.FormatDuration(1500) == "25:00" && Study.FormatDuration(3661) == "61:01", "focus duration display supports long sessions");
            var report = Study.Summarize(study.FocusHistory, new DateTime(2026, 10, 9), new DateTime(2026, 10, 10));
            Check(report.SecondsByDay.Count == 2 && report.SecondsByDay["2026-10-09"] == 390 && report.SecondsByDay["2026-10-10"] == 1200, "charts use actual time and split sessions at midnight");
            Check(report.SecondsByTitle["阅读论文"] == 1500 && report.SecondsByTitle["复习"] == 90 && report.Completed == 1, "subject ranking includes early endings and counts completed sessions separately");
            var sameName = study.FocusHistory.Last().Copy(); sameName.Id = Guid.NewGuid().ToString("N"); sameName.Title = "阅读论文";
            var groupedReport = Study.Summarize(study.FocusHistory.Concat(new[] { sameName }), new DateTime(2026, 10, 9), new DateTime(2026, 10, 9));
            Check(groupedReport.SecondsByTitle["阅读论文"] == 390 && groupedReport.TotalSeconds == 480, "same-name study ranking sums only time within selected range");
            var emptyReport = Study.Summarize(new FocusSession[0], new DateTime(2026, 10, 1), new DateTime(2026, 10, 7));
            Check(emptyReport.SecondsByDay.Count == 7 && emptyReport.TotalSeconds == 0 && emptyReport.Completed == 0, "zero-study dates remain present in charts");
            Check(Study.Summarize(new[] { study.ActiveFocus }, new DateTime(2026, 10, 9), new DateTime(2026, 10, 9)).TotalSeconds == 15.5, "current unfinished study can be included in statistics");
            Throws(() => Study.Summarize(study.FocusHistory, new DateTime(2026, 10, 10), new DateTime(2026, 10, 9)), "inverted chart ranges are rejected");
            var boundaryStudy = new TodoState(); var boundaryStart = studyStart.Date.AddDays(1).AddTicks(-1);
            Study.Start(boundaryStudy, "跨日", 1, new DateTimeOffset(boundaryStart, studyStart.Offset)); Study.AddTime(boundaryStudy, new DateTimeOffset(boundaryStart, studyStart.Offset), 1);
            Check(Study.SecondsOn(boundaryStudy.ActiveFocus, boundaryStart.Date) < 0.001 && Study.SecondsOn(boundaryStudy.ActiveFocus, boundaryStart.Date.AddDays(1)) > 0.999, "submillisecond midnight boundary does not stall day statistics");
            var invalidFocus = study.ActiveFocus.Copy(); invalidFocus.Slices[0].Seconds = Double.NaN;
            Throws(() => new TodoState { ActiveFocus = invalidFocus }.Validate(), "invalid nonfinite study duration is rejected");
            var duplicateStudy = study.Copy(); duplicateStudy.FocusHistory.Add(duplicateStudy.FocusHistory[0].Copy());
            Throws(() => duplicateStudy.Validate(), "duplicate study record IDs are rejected");
            using (var focusStore = DataStore.Open(Path.Combine(root, "专注保存")))
            {
                focusStore.ChangeState(state => { Study.Start(state, "保存保护", 25, studyStart); Study.AddTime(state, studyStart, 100); });
                string savedFocus = File.ReadAllText(Path.Combine(focusStore.DirectoryPath, "tasks.json"));
                using (var locked = new FileStream(Path.Combine(focusStore.DirectoryPath, "tasks.json"), FileMode.Open, FileAccess.Read, FileShare.None))
                    Throws(() => focusStore.ChangeState(state => Study.Finish(state, studyStart.AddMinutes(2), false)), "study completion save failure is reported");
                Check(focusStore.State.ActiveFocus != null && focusStore.State.FocusHistory.Count == 0 && File.ReadAllText(Path.Combine(focusStore.DirectoryPath, "tasks.json")) == savedFocus, "failed study save retains active progress and original records");
                focusStore.ChangeState(state => Study.Finish(state, studyStart.AddMinutes(2), false));
                string focusExport = Path.Combine(root, "focus-export.json"); focusStore.Export(focusExport);
                focusStore.ChangeState(state => state.FocusHistory.Clear()); focusStore.Import(focusExport);
                Check(focusStore.State.FocusHistory.Count == 1 && focusStore.State.FocusHistory[0].FocusedSeconds == 100, "study history participates in export and import without loss");
            }
            var repeating = new TodoState();
            var template = Item("月末组会", "2024-01-31"); template.Time = "14:30"; template.Location = "会议室"; template.Important = true; template.Reminder = true;
            Schedule.AddSeries(repeating, template, "monthly");
            Schedule.Expand(repeating, new DateTime(2024, 3, 31));
            Check(repeating.Items.Select(i => i.Date).SequenceEqual(new[] { "2024-01-31", "2024-02-29", "2024-03-31" }), "monthly recurrence uses original day after short months");
            Check(repeating.Items.All(i => i.Time == "14:30" && i.Location == "会议室" && i.Important && i.Reminder && !i.Done), "recurrences retain time location reminder and importance");
            repeating.Items[0].Done = true; repeating.Items.RemoveAt(1);
            Schedule.Expand(repeating, new DateTime(2024, 4, 30));
            Check(repeating.Items.Count == 3 && repeating.Items.Last().Date == "2024-04-30", "deleted occurrence is not regenerated and completion does not control recurrence");
            Schedule.Expand(repeating, new DateTime(2024, 4, 30));
            Check(repeating.Items.Count == 3, "recurrence expansion is idempotent");
            var weekly = new TodoState(); Schedule.AddSeries(weekly, Item("组会", "2026-10-09"), "weekly"); Schedule.Expand(weekly, new DateTime(2026, 10, 23));
            Check(weekly.Items.Select(i => i.Date).SequenceEqual(new[] { "2026-10-09", "2026-10-16", "2026-10-23" }), "weekly recurs on original weekday");
            var daily = new TodoState(); Schedule.AddSeries(daily, Item("读书", "2026-10-09"), "daily"); Schedule.Expand(daily, new DateTime(2026, 10, 11));
            Check(daily.Items.Count == 3 && daily.Items.All(i => !i.Done), "daily occurrences appear without completing earlier tasks");
            var notification = Item("提醒", "2026-10-09"); notification.Time = "14:30"; notification.Reminder = true;
            Check(!Schedule.IsDue(notification, new DateTime(2026, 10, 9, 14, 29, 59)) && Schedule.IsDue(notification, new DateTime(2026, 10, 9, 14, 30, 0)), "reminder fires only once time is reached");
            notification.NotifiedFor = Schedule.ReminderKey(notification);
            Check(!Schedule.IsDue(notification, new DateTime(2026, 10, 9, 15, 0, 0)), "persisted acknowledgement suppresses duplicate reminder after restart");
            notification.Time = "16:00";
            Check(Schedule.IsDue(notification, new DateTime(2026, 10, 9, 16, 0, 0)), "rescheduling enables a new reminder");
            notification.Done = true;
            Check(!Schedule.IsDue(notification, new DateTime(2026, 10, 9, 16, 0, 0)), "completed tasks are not reminded");
            notification.Done = false;
            Check(!Schedule.IsDue(notification, new DateTime(2026, 10, 10, 16, 0, 0)), "old overdue dates are not reminded on startup");
            string recurringPath = Path.Combine(root, "recurring.json"); JsonFile.Write(recurringPath, repeating);
            var repeatReload = DataStore.ReadState(recurringPath); Schedule.Expand(repeatReload, new DateTime(2024, 5, 31));
            Check(repeatReload.Items.Last().Date == "2024-05-31" && repeatReload.Series.Count == 1, "repeat rule and generation cursor survive reload");
            Throws(() => Schedule.AddSeries(new TodoState(), Item("无日期", null), "daily"), "repeating task requires a date");
            var invalidReminder = Item("无时间", "2026-10-09"); invalidReminder.Reminder = true;
            Throws(() => new TodoState { Items = new List<TodoItem> { invalidReminder } }.Validate(), "reject reminder without a time");
            using (var repeatStore = DataStore.Open(Path.Combine(root, "重复保存")))
            {
                repeatStore.ChangeState(state => { Schedule.AddSeries(state, Item("每日任务", "2026-10-09"), "daily"); Schedule.Expand(state, new DateTime(2026, 10, 11)); });
                string savedRepeat = File.ReadAllText(Path.Combine(repeatStore.DirectoryPath, "tasks.json"));
                using (var locked = new FileStream(Path.Combine(repeatStore.DirectoryPath, "tasks.json"), FileMode.Open, FileAccess.Read, FileShare.None))
                    Throws(() => repeatStore.ExpandThrough(new DateTime(2026, 10, 15)), "recurrence write failure is reported");
                Check(repeatStore.State.Items.Count == 3 && repeatStore.State.Series[0].NextIndex == 3 && File.ReadAllText(Path.Combine(repeatStore.DirectoryPath, "tasks.json")) == savedRepeat, "failed recurrence generation preserves data and generation cursor");
                repeatStore.ExpandThrough(new DateTime(2026, 10, 15));
                Check(repeatStore.State.Items.Count == 7, "recurrence generation retries without skips after failed save");
            }
            string timedPath = Path.Combine(root, "time-location.json");
            string timedJson = "{\"Version\":2,\"Items\":[{\"Id\":\"7e9c8ec94c1c4d7b958a64899f5aca91\",\"Title\":\"开会\",\"Date\":\"2026-10-08\",\"Time\":\"14:35\",\"Location\":\"会议室 A\",\"Done\":false}]}";
            File.WriteAllText(timedPath, timedJson);
            var timedState = DataStore.ReadState(timedPath);
            Check(timedState.Items[0].Time == "14:35" && timedState.Items[0].Location == "会议室 A", "read time and location without loss");
            File.WriteAllText(timedPath, timedJson.Replace("14:35", "00:00"));
            Check(DataStore.ReadState(timedPath).Items[0].Time == "00:00", "midnight is preserved");
            File.WriteAllText(timedPath, timedJson.Replace("14:35", "23:59"));
            Check(DataStore.ReadState(timedPath).Items[0].Time == "23:59", "last minute of day supported");
            File.WriteAllText(timedPath, timedJson.Replace("14:35", "24:00"));
            Throws(() => DataStore.ReadState(timedPath), "reject invalid hour");
            File.WriteAllText(timedPath, timedJson.Replace("14:35", "14:60"));
            Throws(() => DataStore.ReadState(timedPath), "reject invalid minute");
            File.WriteAllText(timedPath, timedJson.Replace("14:35", "9:05"));
            Throws(() => DataStore.ReadState(timedPath), "reject noncanonical time");
            File.WriteAllText(timedPath, timedJson.Replace("\"Date\":\"2026-10-08\"", "\"Date\":null"));
            Throws(() => DataStore.ReadState(timedPath), "time requires a date");
            File.WriteAllText(timedPath, timedJson.Replace("会议室 A", new string('x', 201)));
            Throws(() => DataStore.ReadState(timedPath), "reject oversized location");
            File.WriteAllText(timedPath, timedJson.Replace("\"Version\":2", "\"Version\":1").Replace(",\"Time\":\"14:35\",\"Location\":\"会议室 A\"", ""));
            var legacyState = DataStore.ReadState(timedPath);
            Check(legacyState.Items.Count == 1 && legacyState.Items[0].Title == "开会" && legacyState.Items[0].Time == null && legacyState.Items[0].Location == null, "legacy records remain readable");
            File.WriteAllText(timedPath, timedJson);
            using (var timedStore = DataStore.Open(Path.Combine(root, "时间地点")))
            {
                timedStore.Import(timedPath);
                string timedExport = Path.Combine(root, "时间地点备份.json"); timedStore.Export(timedExport);
                var roundtrip = DataStore.ReadState(timedExport);
                Check(roundtrip.Version == 9 && roundtrip.Items[0].Time == "14:35" && roundtrip.Items[0].Location == "会议室 A", "time location backup roundtrip and new format");
                JsonFile.Write(timedPath, legacyState);
                timedStore.Import(timedPath);
                Check(DataStore.ReadState(Path.Combine(timedStore.DirectoryPath, "tasks.json")).Version == 9, "saving legacy data upgrades format without losing records");
            }
            Check(Dates.GridStart(new DateTime(2026, 10, 1)) == new DateTime(2026, 9, 28), "October Monday-first grid");
            Check(Dates.GridStart(new DateTime(2027, 1, 1)) == new DateTime(2026, 12, 28), "calendar crosses year");
            Check(Dates.GridStart(new DateTime(2024, 2, 1)).AddDays(31) == new DateTime(2024, 2, 29), "leap day");
            Check(!Dates.Overdue(Item("无日期", null), new DateTime(2026, 10, 8)), "undated not overdue");
            var done = Item("完成", "2026-10-01"); done.Done = true;
            Check(!Dates.Overdue(done, new DateTime(2026, 10, 8)), "completed not overdue");
            Check(Dates.Overdue(Item("过期", "2026-10-07"), new DateTime(2026, 10, 8)), "deadline determines overdue");
            string original = Path.Combine(root, "中文 空格", "Data"), moved = Path.Combine(root, "迁移 数据");
            var store = DataStore.Open(original);
            store.Change(items => items.Add(Item("第一条任务", "2026-10-08")));
            Check(File.Exists(Path.Combine(original, "tasks.json")), "autosave real file");
            store.Change(items => items[0].Done = true);
            Check(File.Exists(Path.Combine(original, "tasks.json.bak")), "previous valid backup");
            Throws(() => store.Change(items => items.Add(Item("   ", null))), "reject blank title");
            Check(store.State.Items.Count == 1, "failed validation leaves memory unchanged");
            Throws(() => store.Change(items => items.Add(Item("无效日期", "2026-02-30"))), "reject impossible date");
            Throws(() => store.Change(items => items.Add(items[0].Copy())), "reject duplicate IDs");
            Throws(() => { using (var other = DataStore.Open(original)) {} }, "exclusive data lock");
            string export = Path.Combine(root, "备份.json"); store.Export(export);
            store.Change(items => items.Clear());
            store.Import(export);
            Check(store.State.Items.Count == 1 && store.State.Items[0].Done, "export import roundtrip");
            File.WriteAllText(Path.Combine(root, "bad.json"), "{bad json");
            Throws(() => store.Import(Path.Combine(root, "bad.json")), "invalid import refused");
            Check(store.State.Items.Count == 1, "invalid import preserves state");
            string dataBeforeOversize = File.ReadAllText(Path.Combine(original, "tasks.json"));
            string backupBeforeOversize = File.ReadAllText(Path.Combine(original, "tasks.json.bak"));
            string largeNote = new string('x', 4000);
            Throws(() => store.Change(items => { items.Clear(); for (int n = 0; n < 6000; n++) { var item = Item("任务 " + n, null); item.Note = largeNote; items.Add(item); } }), "oversized serialized data refused");
            Check(store.State.Items.Count == 1 && File.ReadAllText(Path.Combine(original, "tasks.json")) == dataBeforeOversize && File.ReadAllText(Path.Combine(original, "tasks.json.bak")) == backupBeforeOversize, "oversized save preserves memory data and backup");
            var migrated = store.MoveTo(moved);
            Check(migrated.State.Items.Count == 1 && File.Exists(Path.Combine(original, "tasks.json")), "migration copies without deleting old data");
            store.Dispose(); store = migrated;
            Throws(() => store.MoveTo(original), "migration conflict refused");
            string before = File.ReadAllText(Path.Combine(moved, "tasks.json"));
            using (var locked = new FileStream(Path.Combine(moved, "tasks.json"), FileMode.Open, FileAccess.Read, FileShare.None))
                Throws(() => store.Change(items => items[0].Title = "不应保存"), "write failure reported");
            Check(store.State.Items[0].Title == "第一条任务" && File.ReadAllText(Path.Combine(moved, "tasks.json")) == before, "write failure preserves memory and file");
            store.Dispose();
            using (var reopened = DataStore.Open(moved)) Check(reopened.State.Items[0].Title == "第一条任务", "restart persistence");
            File.WriteAllText(Path.Combine(moved, "tasks.json"), "broken");
            Throws(() => { using (var invalid = DataStore.Open(moved)) {} }, "corrupt data does not load as empty");
            Check(File.ReadAllText(Path.Combine(moved, "tasks.json")) == "broken", "corrupt file not overwritten");
            DataStore.Recover(moved);
            using (var recovered = DataStore.Open(moved)) Check(recovered.State.Items.Count == 1, "explicit backup recovery");
            Check(DataStore.ReadState(Path.Combine(moved, "tasks.json.bak")).Items.Count == 1, "recovery leaves a valid backup");
            Check(Directory.GetFiles(moved, "tasks.corrupt.*.json").Length == 1, "recovery preserves corrupt original");
            string unknown = Path.Combine(root, "unknown.json");
            File.WriteAllText(unknown, "{\"Version\":99,\"Items\":[]}");
            Throws(() => DataStore.ReadState(unknown), "unknown version refused");
            string prefs = Path.Combine(root, "prefs.json");
            var settings = new AppSettings { Theme = "dark", DataPath = moved, WidgetX = 120, WidgetY = 230 };
            SettingsFile.Save(prefs, settings);
            var restored = SettingsFile.Load(prefs);
            Check(restored.Theme == "dark" && restored.DataPath == moved && restored.WidgetX == 120, "path theme position persistence");
            Check(new AppSettings().DataPath == "", "default relative data path remains portable");
            Console.WriteLine("ALL PASS: " + passed);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
}
