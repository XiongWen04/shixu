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
        public TodoItem Copy() { return (TodoItem)MemberwiseClone(); }
    }

    [DataContract]
    public class TodoState
    {
        [DataMember(IsRequired = true)] public int Version = 2;
        [DataMember(IsRequired = true)] public List<TodoItem> Items = new List<TodoItem>();
        public TodoState Copy() { return new TodoState { Items = Items.Select(i => i.Copy()).ToList() }; }
        public void Validate()
        {
            if (Version != 1 && Version != 2) throw new InvalidDataException("不支持这个数据版本，请使用对应版本的软件。");
            if (Items == null || Items.Count > 10000) throw new InvalidDataException("待办列表无效或超过 10000 条。");
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
            }
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
            next.Validate(); next.Version = 2; JsonFile.Write(FilePath, next); State = next;
        }
        public void Change(Action<List<TodoItem>> edit) { var next = State.Copy(); edit(next.Items); Commit(next); }
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
