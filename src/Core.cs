using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DeskTodo
{
    [DataContract]
    public class TodoItem
    {
        [DataMember(IsRequired = true)] public string Id;
        [DataMember(IsRequired = true)] public string Title;
        [DataMember] public string Date;
        [DataMember(EmitDefaultValue = false)] public string Time;
        [DataMember(EmitDefaultValue = false)] public string Location;
        [DataMember] public string Note = "";
        [DataMember] public string CreatedUtc;
        [DataMember] public bool Done;
        [DataMember(EmitDefaultValue = false)] public bool Important;
        [DataMember(EmitDefaultValue = false)] public bool Reminder;
        [DataMember(EmitDefaultValue = false)] public string NotifiedFor;
        [DataMember(EmitDefaultValue = false)] public string SeriesId;
        public TodoItem Copy() { return (TodoItem)MemberwiseClone(); }
    }

    [DataContract]
    public class TodoState
    {
        [DataMember(IsRequired = true)] public int Version = 4;
        [DataMember(IsRequired = true)] public List<TodoItem> Items = new List<TodoItem>();
        [DataMember] public List<RepeatSeries> Series = new List<RepeatSeries>();
        [DataMember] public List<FocusSession> FocusHistory = new List<FocusSession>();
        [DataMember(EmitDefaultValue = false)] public FocusSession ActiveFocus;
        [OnDeserializing] void Defaults(StreamingContext c) { Series = new List<RepeatSeries>(); FocusHistory = new List<FocusSession>(); }
        public TodoState Copy() { return new TodoState { Items = Items.Select(i => i.Copy()).ToList(), Series = Series.Select(s => s.Copy()).ToList(), FocusHistory = FocusHistory.Select(f => f.Copy()).ToList(), ActiveFocus = ActiveFocus == null ? null : ActiveFocus.Copy() }; }
        public void Validate()
        {
            if (Version < 1 || Version > 4) throw new InvalidDataException("不支持这个数据版本，请使用对应版本的软件。");
            if (FocusHistory == null || FocusHistory.Count > 10000) throw new InvalidDataException("学习记录无效或超过 10000 次，请先导出备份。");
            var focusIds = new HashSet<string>();
            foreach (var focus in FocusHistory) { if (focus == null || !focusIds.Add(focus.Id)) throw new InvalidDataException("学习记录重复或无效。"); focus.Validate(false); }
            if (ActiveFocus != null) { if (!focusIds.Add(ActiveFocus.Id)) throw new InvalidDataException("进行中的学习记录重复。"); ActiveFocus.Validate(true); }
            if (Items == null || Items.Count > 10000) throw new InvalidDataException("待办列表无效或超过 10000 条。");
            if (Series == null || Series.Count > 1000) throw new InvalidDataException("重复规则无效或超过 1000 条。");
            var seriesIds = new HashSet<string>();
            foreach (var series in Series)
            {
                Guid ruleId;
                if (series == null || !Guid.TryParse(series.Id, out ruleId) || !seriesIds.Add(series.Id) || series.Template == null || series.Template.Date == null || series.NextIndex < 1 || series.NextIndex > 3652059 || (series.Kind != "daily" && series.Kind != "weekly" && series.Kind != "monthly")) throw new InvalidDataException("重复规则无效。");
                new TodoState { Items = new List<TodoItem> { series.Template } }.Validate();
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var i in Items)
            {
                Guid id;
                if (i == null || !Guid.TryParse(i.Id, out id) || !ids.Add(i.Id)) throw new InvalidDataException("待办编号无效或重复。");
                if (String.IsNullOrWhiteSpace(i.Title) || i.Title.Length > 200) throw new InvalidDataException("标题不能为空，最多 200 字。");
                if (i.Note != null && i.Note.Length > 4000) throw new InvalidDataException("备注最多 4000 字。");
                DateTime parsed;
                if (i.Date != null && !DateTime.TryParseExact(i.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new InvalidDataException("待办日期无效。");
                if (i.Time != null && (i.Date == null || !DateTime.TryParseExact(i.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))) throw new InvalidDataException("时间需要指定日期，并使用 00:00 至 23:59 的格式。");
                if (i.Location != null && i.Location.Length > 200) throw new InvalidDataException("地点最多 200 字。");
                if (i.Reminder && i.Time == null) throw new InvalidDataException("到点提醒需要日期和时间。");
                if (i.NotifiedFor != null && !DateTime.TryParseExact(i.NotifiedFor, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new InvalidDataException("提醒记录无效。");
                if (i.SeriesId != null && !seriesIds.Contains(i.SeriesId)) throw new InvalidDataException("待办关联的重复规则不存在。");
            }
        }
    }

    [DataContract]
    public class FocusSlice
    {
        [DataMember(IsRequired = true)] public string StartedAt;
        [DataMember(IsRequired = true)] public double Seconds;
        public FocusSlice Copy() { return (FocusSlice)MemberwiseClone(); }
    }

    [DataContract]
    public class FocusSession
    {
        [DataMember(IsRequired = true)] public string Id;
        [DataMember(IsRequired = true)] public string Title;
        [DataMember(IsRequired = true)] public int TargetSeconds;
        [DataMember(IsRequired = true)] public string StartedAt;
        [DataMember(EmitDefaultValue = false)] public string EndedAt;
        [DataMember] public bool Completed;
        [DataMember(IsRequired = true)] public List<FocusSlice> Slices = new List<FocusSlice>();
        public double FocusedSeconds { get { return Slices.Sum(s => s.Seconds); } }
        public FocusSession Copy() { return new FocusSession { Id = Id, Title = Title, TargetSeconds = TargetSeconds, StartedAt = StartedAt, EndedAt = EndedAt, Completed = Completed, Slices = Slices.Select(s => s.Copy()).ToList() }; }
        public void Validate(bool active)
        {
            Guid id; DateTimeOffset parsed;
            if (!Guid.TryParse(Id, out id) || String.IsNullOrWhiteSpace(Title) || Title.Length > 200 || TargetSeconds < 60 || TargetSeconds > 43200 || !DateTimeOffset.TryParseExact(StartedAt, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed) || Slices == null || Slices.Count > 2000) throw new InvalidDataException("学习记录的名称、时间或时长无效。");
            foreach (var slice in Slices)
            {
                if (slice == null || Double.IsNaN(slice.Seconds) || Double.IsInfinity(slice.Seconds) || slice.Seconds <= 0 || slice.Seconds > TargetSeconds || !DateTimeOffset.TryParseExact(slice.StartedAt, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new InvalidDataException("学习计时片段无效。");
                try { parsed.AddTicks((long)Math.Ceiling(slice.Seconds * TimeSpan.TicksPerSecond)); } catch (ArgumentOutOfRangeException) { throw new InvalidDataException("学习计时片段超出日期范围。"); }
            }
            if (FocusedSeconds > TargetSeconds + 0.000001 || (active && (EndedAt != null || Completed)) || (!active && !DateTimeOffset.TryParseExact(EndedAt, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) || (Completed && FocusedSeconds < TargetSeconds - 0.000001)) throw new InvalidDataException("学习记录状态无效。");
        }
    }

    public static class Study
    {
        public static void Start(TodoState state, string title, int minutes, DateTimeOffset now)
        {
            if (state.ActiveFocus != null) throw new InvalidOperationException("请先结束当前学习。");
            if (String.IsNullOrWhiteSpace(title) || title.Trim().Length > 200 || minutes < 1 || minutes > 720) throw new InvalidDataException("请填写学习名称和 1 至 720 分钟的时长。");
            if (state.FocusHistory.Count >= 10000) throw new InvalidDataException("学习记录已达 10000 次，请先导出备份。");
            state.ActiveFocus = new FocusSession { Id = Guid.NewGuid().ToString("N"), Title = title.Trim(), TargetSeconds = minutes * 60, StartedAt = now.ToString("o", CultureInfo.InvariantCulture) };
        }
        public static void AddTime(TodoState state, DateTimeOffset segmentStart, double seconds)
        {
            if (state.ActiveFocus == null) throw new InvalidOperationException("没有进行中的学习。");
            if (Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds < 0) throw new InvalidDataException("计时时长无效。");
            var focus = state.ActiveFocus;
            double add = Math.Min(seconds, Math.Max(0, focus.TargetSeconds - focus.FocusedSeconds));
            if (add <= 0) return;
            string start = segmentStart.ToString("o", CultureInfo.InvariantCulture);
            var last = focus.Slices.LastOrDefault();
            if (last != null && last.StartedAt == start) last.Seconds += add;
            else { if (focus.Slices.Count >= 2000) throw new InvalidDataException("这次学习的暂停次数过多，请结束后新建。"); focus.Slices.Add(new FocusSlice { StartedAt = start, Seconds = add }); }
        }
        public static void Finish(TodoState state, DateTimeOffset now, bool completed)
        {
            if (state.ActiveFocus == null) throw new InvalidOperationException("没有进行中的学习。");
            var focus = state.ActiveFocus;
            if (completed && focus.FocusedSeconds < focus.TargetSeconds - 0.000001) throw new InvalidOperationException("倒计时尚未结束。");
            focus.EndedAt = now.ToString("o", CultureInfo.InvariantCulture); focus.Completed = completed;
            state.FocusHistory.Add(focus); state.ActiveFocus = null;
        }
        static IEnumerable<KeyValuePair<DateTime, double>> Parts(FocusSession focus)
        {
            foreach (var slice in focus.Slices)
            {
                var start = DateTimeOffset.ParseExact(slice.StartedAt, "o", CultureInfo.InvariantCulture);
                double remaining = slice.Seconds;
                while (remaining > 0)
                {
                    double chunk = Math.Min(remaining, 86400 - start.TimeOfDay.TotalSeconds);
                    yield return new KeyValuePair<DateTime, double>(start.Date, chunk);
                    remaining -= chunk; if (remaining <= 0) break; start = new DateTimeOffset(start.Date.AddDays(1), start.Offset);
                }
            }
        }
        public static double SecondsOn(FocusSession focus, DateTime day) { return Parts(focus).Where(p => p.Key == day.Date).Sum(p => p.Value); }
        public static StudyReport Summarize(IEnumerable<FocusSession> sessions, DateTime from, DateTime through)
        {
            from = from.Date; through = through.Date;
            if (through < from || (through - from).TotalDays > 365) throw new ArgumentException("请选择不超过 366 天的有效统计日期范围。");
            var report = new StudyReport();
            int days = (int)(through - from).TotalDays + 1;
            for (int index = 0; index < days; index++) report.SecondsByDay.Add(Dates.Key(from.AddDays(index)), 0);
            foreach (var session in sessions)
            {
                double seconds = 0;
                foreach (var part in Parts(session))
                    if (part.Key >= from && part.Key <= through) { report.SecondsByDay[Dates.Key(part.Key)] += part.Value; seconds += part.Value; }
                if (seconds > 0) { if (!report.SecondsByTitle.ContainsKey(session.Title)) report.SecondsByTitle.Add(session.Title, 0); report.SecondsByTitle[session.Title] += seconds; }
                if (session.Completed)
                {
                    var ended = DateTimeOffset.ParseExact(session.EndedAt, "o", CultureInfo.InvariantCulture).Date;
                    if (ended >= from && ended <= through) report.Completed++;
                }
            }
            return report;
        }
        public static string FormatDuration(double seconds) { int value = (int)Math.Max(0, Math.Floor(seconds)); return (value / 60).ToString("D2") + ":" + (value % 60).ToString("D2"); }
    }

    public class StudyReport
    {
        public readonly Dictionary<string, double> SecondsByDay = new Dictionary<string, double>();
        public readonly Dictionary<string, double> SecondsByTitle = new Dictionary<string, double>(StringComparer.Ordinal);
        public int Completed;
        public double TotalSeconds { get { return SecondsByDay.Values.Sum(); } }
    }

    [DataContract]
    public class RepeatSeries
    {
        [DataMember(IsRequired = true)] public string Id;
        [DataMember(IsRequired = true)] public string Kind;
        [DataMember(IsRequired = true)] public TodoItem Template;
        [DataMember] public int NextIndex;
        public RepeatSeries Copy() { return new RepeatSeries { Id = Id, Kind = Kind, Template = Template.Copy(), NextIndex = NextIndex }; }
    }

    public static class Schedule
    {
        public static void AddSeries(TodoState state, TodoItem template, string kind)
        {
            if (template.Date == null || (kind != "daily" && kind != "weekly" && kind != "monthly")) throw new InvalidDataException("重复任务需要日期和有效周期。");
            var copy = template.Copy(); copy.Done = false; copy.SeriesId = null; copy.NotifiedFor = null;
            new TodoState { Items = new List<TodoItem> { copy } }.Validate();
            var rule = new RepeatSeries { Id = Guid.NewGuid().ToString("N"), Kind = kind, Template = copy, NextIndex = 1 };
            state.Series.Add(rule); template.SeriesId = rule.Id; state.Items.Add(template);
        }
        static DateTime? DateAt(RepeatSeries rule, int index)
        {
            var anchor = DateTime.ParseExact(rule.Template.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            try { return rule.Kind == "monthly" ? anchor.AddMonths(index) : anchor.AddDays((double)index * (rule.Kind == "weekly" ? 7 : 1)); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        public static bool Expand(TodoState state, DateTime through)
        {
            bool changed = false;
            foreach (var rule in state.Series)
            {
                DateTime? date;
                while ((date = DateAt(rule, rule.NextIndex)).HasValue && date.Value <= through.Date)
                {
                    if (state.Items.Count >= 10000) throw new InvalidDataException("待办已达 10000 条，无法生成更多重复任务；请先导出并清理历史任务。");
                    var item = rule.Template.Copy(); item.Id = Guid.NewGuid().ToString("N"); item.SeriesId = rule.Id; item.Date = Dates.Key(date.Value); item.Done = false; item.NotifiedFor = null;
                    state.Items.Add(item); rule.NextIndex++; changed = true;
                }
            }
            return changed;
        }
        public static string ReminderKey(TodoItem item) { return item.Date + " " + item.Time; }
        public static bool IsDue(TodoItem item, DateTime now)
        {
            if (item.Done || !item.Reminder || item.Time == null || item.Date != Dates.Key(now) || item.NotifiedFor == ReminderKey(item)) return false;
            DateTime due;
            return DateTime.TryParseExact(ReminderKey(item), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out due) && due <= now;
        }
    }

    [DataContract]
    public class AppSettings
    {
        [DataMember] public string Theme = "warm";
        [DataMember] public string DataPath = "";
        [DataMember] public double WidgetX = -100000;
        [DataMember] public double WidgetY = -100000;
        [DataMember] public double WidgetWidth = 320;
        [DataMember] public double WidgetHeight = 460;
        [DataMember] public bool WidgetVisible = true;
        [OnDeserializing] void Defaults(StreamingContext c)
        {
            Theme = "warm"; DataPath = ""; WidgetX = WidgetY = -100000;
            WidgetWidth = 320; WidgetHeight = 460; WidgetVisible = true;
        }
        public AppSettings Copy() { return (AppSettings)MemberwiseClone(); }
        public void Validate()
        {
            if (Theme != "light" && Theme != "warm" && Theme != "dark") throw new InvalidDataException("主题设置无效。");
            if (DataPath == null || (DataPath != "" && !Path.IsPathRooted(DataPath))) throw new InvalidDataException("数据路径设置无效。");
            double[] values = { WidgetX, WidgetY, WidgetWidth, WidgetHeight };
            if (values.Any(v => Double.IsNaN(v) || Double.IsInfinity(v)) || WidgetWidth < 260 || WidgetWidth > 1400 || WidgetHeight < 280 || WidgetHeight > 1800) throw new InvalidDataException("小组件大小设置无效。");
        }
    }

    public static class JsonFile
    {
        const long MaxBytes = 20 * 1024 * 1024;
        public static T Read<T>(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaxBytes) throw new InvalidDataException("文件超过 20 MB，无法安全读取。");
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
        }
        public static void Write<T>(string path, T value, bool keepExistingBackup = false)
        {
            string full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            string temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                    if (stream.Length > MaxBytes) throw new InvalidDataException("数据超过 20 MB，本次修改未保存。请缩短备注或减少待办。");
                    stream.Flush(true);
                }
                if (File.Exists(full)) File.Replace(temp, full, keepExistingBackup ? null : full + ".bak", true);
                else
                {
                    // 首次保存也保留有效备份，断电发生在改名之前时可明确恢复。
                    File.Copy(temp, full + ".bak", true);
                    File.Move(temp, full);
                }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static void CheckWritable(string directory)
        {
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, ".write-" + Guid.NewGuid().ToString("N"));
            using (var f = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.WriteByte(0); f.Flush(true); }
            File.Delete(probe);
        }
    }

    public static class SettingsFile
    {
        public static void Save(string path, AppSettings settings) { settings.Validate(); JsonFile.Write(path, settings); }
        public static AppSettings Load(string path)
        {
            if (!File.Exists(path))
            {
                if (File.Exists(path + ".bak")) throw new InvalidDataException("设置文件缺失，但存在备份。");
                return new AppSettings();
            }
            var result = JsonFile.Read<AppSettings>(path);
            if (result == null) throw new InvalidDataException("设置文件为空。");
            result.Validate(); return result;
        }
    }

    public static class Dates
    {
        public static DateTime GridStart(DateTime month)
        {
            var first = new DateTime(month.Year, month.Month, 1);
            return first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        }
        public static string Key(DateTime date) { return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        public static bool Overdue(TodoItem item, DateTime today) { return !item.Done && item.Date != null && String.CompareOrdinal(item.Date, Key(today)) < 0; }
    }

    public class DataStore : IDisposable
    {
        FileStream lease;
        public string DirectoryPath { get; private set; }
        public TodoState State { get; private set; }
        string FilePath { get { return Path.Combine(DirectoryPath, "tasks.json"); } }
        DataStore() {}
        public static DataStore Open(string path)
        {
            string full = Path.GetFullPath(path);
            JsonFile.CheckWritable(full);
            var store = new DataStore { DirectoryPath = full };
            try
            {
                store.lease = new FileStream(Path.Combine(full, ".todo.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                if (!File.Exists(store.FilePath) && File.Exists(store.FilePath + ".bak")) throw new InvalidDataException("待办文件缺失，但存在备份；请明确恢复备份。");
                store.State = File.Exists(store.FilePath) ? ReadState(store.FilePath) : new TodoState();
                return store;
            }
            catch { store.Dispose(); throw; }
        }
        public static TodoState ReadState(string path)
        {
            var state = JsonFile.Read<TodoState>(path);
            if (state == null) throw new InvalidDataException("待办文件为空。");
            state.Validate(); return state;
        }
        void Commit(TodoState next)
        {
            if (lease == null) throw new ObjectDisposedException("DataStore");
            next.Validate(); next.Version = 4; JsonFile.Write(FilePath, next); State = next;
        }
        public void Change(Action<List<TodoItem>> edit) { var next = State.Copy(); edit(next.Items); Commit(next); }
        public void ChangeState(Action<TodoState> edit) { var next = State.Copy(); edit(next); Commit(next); }
        public bool ExpandThrough(DateTime through) { var next = State.Copy(); if (!Schedule.Expand(next, through)) return false; Commit(next); return true; }
        public void Import(string path) { Commit(ReadState(path)); }
        public void Export(string path)
        {
            string full = Path.GetFullPath(path);
            if (String.Equals(full, FilePath, StringComparison.OrdinalIgnoreCase) || String.Equals(full, FilePath + ".bak", StringComparison.OrdinalIgnoreCase)) throw new IOException("请选择数据目录以外的备份文件。");
            JsonFile.Write(full, State);
        }
        public DataStore MoveTo(string directory)
        {
            string full = Path.GetFullPath(directory);
            if (String.Equals(full.TrimEnd(Path.DirectorySeparatorChar), DirectoryPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new IOException("这已经是当前数据目录。");
            string target = Path.Combine(full, "tasks.json");
            if (File.Exists(target) || File.Exists(target + ".bak")) throw new IOException("目标目录已有待办数据，请选择一个空目录；未覆盖任何文件。");
            var next = Open(full);
            try { next.Commit(State.Copy()); ReadState(target); return next; }
            catch { next.Dispose(); throw; }
        }
        public static void Recover(string directory)
        {
            string full = Path.GetFullPath(directory), path = Path.Combine(full, "tasks.json");
            using (var recoveryLease = new FileStream(Path.Combine(full, ".todo.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var state = ReadState(path + ".bak");
                if (File.Exists(path)) File.Copy(path, Path.Combine(full, "tasks.corrupt." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json"));
                JsonFile.Write(path, state, true);
            }
        }
        public void Dispose() { if (lease != null) { lease.Dispose(); lease = null; } }
    }
}
