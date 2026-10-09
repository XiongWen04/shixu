using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace DeskTodo
{
    public class WidgetSurface : Grid
    {
        readonly WidgetWindow window;
        readonly ImageBrush wallpaper = new ImageBrush { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute };
        string loadedPath;
        public bool HasWallpaper { get { return wallpaper.ImageSource != null; } }
        public WidgetSurface(WidgetWindow owner, Border content)
        {
            window = owner;
            Children.Add(new Border { Background = wallpaper, Effect = new BlurEffect { Radius = 22 }, IsHitTestVisible = false });
            var tint = new Border { IsHitTestVisible = false }; tint.SetResourceReference(Border.BackgroundProperty, "GlassTint"); Children.Add(tint);
            content.Background = Brushes.Transparent; content.SetResourceReference(Border.BorderBrushProperty, "GlassBorder"); Children.Add(content);
            SizeChanged += (s, e) => Update();
        }
        public void Update()
        {
            if (!window.IsLoaded || ActualWidth <= 0 || ActualHeight <= 0) return;
            var bounds = NativeDesktop.Bounds(window);
            string path = NativeDesktop.WallpaperPath();
            if (path != loadedPath)
            {
                loadedPath = path; wallpaper.ImageSource = null;
                if (File.Exists(path))
                {
                    try
                    {
                        using (var stream = File.OpenRead(path))
                        {
                            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = stream; bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 3840; bitmap.EndInit(); bitmap.Freeze(); wallpaper.ImageSource = bitmap;
                        }
                    }
                    catch (IOException) {} catch (NotSupportedException) {} catch (FileFormatException) {}
                }
            }
            if (wallpaper.ImageSource != null)
            {
                // shortcut: 按填充布局映射同一张壁纸，多屏不同壁纸或其他布局需要改用每屏背景映射。
                var screen = Forms.Screen.FromRectangle(new System.Drawing.Rectangle(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top)).Bounds;
                double ratio = Math.Max(screen.Width / wallpaper.ImageSource.Width, screen.Height / wallpaper.ImageSource.Height);
                double offsetX = (wallpaper.ImageSource.Width * ratio - screen.Width) / 2, offsetY = (wallpaper.ImageSource.Height * ratio - screen.Height) / 2;
                wallpaper.Viewbox = new System.Windows.Rect((bounds.Left - screen.Left + offsetX) / ratio, (bounds.Top - screen.Top + offsetY) / ratio, (bounds.Right - bounds.Left) / ratio, (bounds.Bottom - bounds.Top) / ratio);
            }
            Clip = new RectangleGeometry(new System.Windows.Rect(0, 0, ActualWidth, ActualHeight), 10, 10);
        }
    }
}
