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
            var study = new TodoState();
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
                Check(roundtrip.Version == 4 && roundtrip.Items[0].Time == "14:35" && roundtrip.Items[0].Location == "会议室 A", "time location backup roundtrip and new format");
                JsonFile.Write(timedPath, legacyState);
                timedStore.Import(timedPath);
                Check(DataStore.ReadState(Path.Combine(timedStore.DirectoryPath, "tasks.json")).Version == 4, "saving legacy data upgrades format without losing records");
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
