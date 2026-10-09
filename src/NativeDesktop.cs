using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace DeskTodo
{
    public static class NativeDesktop
    {
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        delegate bool EnumCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr window, ref Point point);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder result, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SystemParametersInfo(uint action, uint size, StringBuilder value, uint flags);
        static IntPtr Handle(Window window) { return new WindowInteropHelper(window).Handle; }
        static string Class(IntPtr window) { var name = new StringBuilder(128); GetClassName(window, name, name.Capacity); return name.ToString(); }
        static IntPtr DesktopHost()
        {
            IntPtr found = IntPtr.Zero;
            // ponytail: Explorer 的桌面层没有公开小组件 API；系统更改窗口结构时需更新此查找，失败则明确降级。
            EnumWindows((window, parameter) =>
            {
                string cls = Class(window);
                if ((cls == "Progman" || cls == "WorkerW") && FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) { found = window; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
        public static bool IsAlive(Window window) { return IsWindow(Handle(window)); }
        public static bool IsAttached(Window window)
        {
            IntPtr hwnd = Handle(window), parent = GetParent(hwnd);
            return hwnd != IntPtr.Zero && IsWindow(hwnd) && parent != IntPtr.Zero && (Class(parent) == "Progman" || Class(parent) == "WorkerW") && FindWindowEx(parent, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
        }
        public static string ParentClass(Window window) { return Class(GetParent(Handle(window))); }
        public static bool Attach(Window window)
        {
            IntPtr hwnd = Handle(window), parent = DesktopHost();
            if (hwnd == IntPtr.Zero || parent == IntPtr.Zero) return false;
            if (IsAttached(window)) return true;
            var rect = Bounds(window);
            long style = GetWindowLongPtr(hwnd, -16).ToInt64();
            // WPF Window 必须保留顶层绘制；用桌面所有者和最低层级绑定，避免跨进程子窗口黑屏。
            SetWindowLongPtr(hwnd, -16, new IntPtr((style & ~0x40000000L) | 0x80000000L));
            long extended = GetWindowLongPtr(hwnd, -20).ToInt64();
            SetWindowLongPtr(hwnd, -20, new IntPtr((extended | 0x80L) & ~0x40000L));
            SetWindowLongPtr(hwnd, -8, parent);
            if (GetParent(hwnd) != parent) { SetWindowLongPtr(hwnd, -16, new IntPtr(style)); return false; }
            Move(window, rect.Left, rect.Top); Lower(window); return IsAttached(window);
        }
        public static void Detach(Window window)
        {
            IntPtr hwnd = Handle(window);
            if (!IsWindow(hwnd)) return;
            SetWindowLongPtr(hwnd, -8, IntPtr.Zero);
        }
        public static Rect Bounds(Window window) { Rect rect; GetWindowRect(Handle(window), out rect); return rect; }
        public static Point CursorPosition() { Point p; GetCursorPos(out p); return p; }
        public static double Scale(Window window)
        {
            var source = HwndSource.FromHwnd(Handle(window));
            return source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
        }
        public static void Move(Window window, int screenX, int screenY)
        {
            IntPtr hwnd = Handle(window), parent = GetParent(hwnd);
            var point = new Point { X = screenX, Y = screenY };
            if (parent != IntPtr.Zero && (GetWindowLongPtr(hwnd, -16).ToInt64() & 0x40000000L) != 0) ScreenToClient(parent, ref point);
            double scale = Scale(window);
            SetWindowPos(hwnd, IntPtr.Zero, point.X, point.Y, (int)Math.Round(window.Width * scale), (int)Math.Round(window.Height * scale), 0x0010 | 0x0004 | 0x0200);
        }
        public static void Lower(Window window) { SetWindowPos(Handle(window), new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200); }
        public static string WallpaperPath() { var path = new StringBuilder(4096); return SystemParametersInfo(0x73, (uint)path.Capacity, path, 0) ? path.ToString() : ""; }
        public static void Position(Window window, double x, double y)
        {
            double scale = Scale(window);
            int width = (int)(window.Width * scale), height = (int)(window.Height * scale);
            var screen = Forms.Screen.PrimaryScreen;
            bool reasonable = Math.Abs(x) < 90000 && Math.Abs(y) < 90000;
            if (reasonable)
            {
                foreach (var s in Forms.Screen.AllScreens) if (s.WorkingArea.IntersectsWith(new System.Drawing.Rectangle((int)x, (int)y, width, height))) { screen = s; break; }
            }
            var work = screen.WorkingArea;
            if (width > work.Width) { window.Width = Math.Max(window.MinWidth, work.Width / scale); width = (int)(window.Width * scale); }
            if (height > work.Height) { window.Height = Math.Max(window.MinHeight, work.Height / scale); height = (int)(window.Height * scale); }
            int px = reasonable ? (int)x : work.Right - width - (int)(24 * scale), py = reasonable ? (int)y : work.Top + (int)(80 * scale);
            px = Math.Max(work.Left, Math.Min(work.Right - width, px)); py = Math.Max(work.Top, Math.Min(work.Bottom - height, py));
            Move(window, px, py);
        }
        public static void DarkTitle(Window window, bool dark)
        {
            int value = dark ? 1 : 0;
            try
            {
                IntPtr hwnd = Handle(window);
                DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
                Color surface = ((SolidColorBrush)Application.Current.Resources["Surface"]).Color;
                Color text = ((SolidColorBrush)Application.Current.Resources["Text"]).Color;
                int caption = surface.R | (surface.G << 8) | (surface.B << 16), foreground = text.R | (text.G << 8) | (text.B << 16);
                DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
                DwmSetWindowAttribute(hwnd, 36, ref foreground, sizeof(int));
            }
            catch (DllNotFoundException) {} catch (EntryPointNotFoundException) {}
        }
        public static void RegisterActivation(Window window, string root, Action show)
        {
            uint message = RegisterWindowMessage("DeskTodo.Show." + Program.Hash(root));
            HwndSource.FromHwnd(Handle(window)).AddHook((IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) =>
            {
                if (msg == message) { window.Dispatcher.BeginInvoke(show); handled = true; }
                return IntPtr.Zero;
            });
        }
        public static void ActivateMain(string root)
        {
            uint message = RegisterWindowMessage("DeskTodo.Show." + Program.Hash(root));
            PostMessage(new IntPtr(0xffff), message, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
