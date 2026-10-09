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
                Check(roundtrip.Version == 2 && roundtrip.Items[0].Time == "14:35" && roundtrip.Items[0].Location == "会议室 A", "time location backup roundtrip and new format");
                JsonFile.Write(timedPath, legacyState);
                timedStore.Import(timedPath);
                Check(DataStore.ReadState(Path.Combine(timedStore.DirectoryPath, "tasks.json")).Version == 2, "saving legacy data upgrades format without losing records");
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
