using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BUMirror.Converters
{
    /// <summary>bool → Visibility (True=Visible, False=Collapsed)</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
            value is Visibility v && v == Visibility.Visible;
    }

    /// <summary>ExistsIn 文字列 ("A" or "B") → 背景ブラシ</summary>
    public class SideToBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush BrA = new(System.Windows.Media.Color.FromArgb(0x38, 0xB4, 0x50, 0x50));
        private static readonly SolidColorBrush BrB = new(System.Windows.Media.Color.FromArgb(0x38, 0x50, 0x78, 0xB4));

        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is string s && s == "A" ? BrA : BrB;
        public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }

    /// <summary>ExistsIn "A"/"B" → バッジ文字列</summary>
    public class SideToBadgeConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is string s ? $"📂 {s} のみ" : string.Empty;
        public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }

    /// <summary>ExistsIn "A"/"B" → バッジ背景色</summary>
    public class SideToBadgeBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush BrA = new(System.Windows.Media.Color.FromArgb(0xCC, 0x9B, 0x35, 0x35));
        private static readonly SolidColorBrush BrB = new(System.Windows.Media.Color.FromArgb(0xCC, 0x35, 0x5C, 0x9B));
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is string s && s == "A" ? BrA : BrB;
        public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }

    /// <summary>ゼロ以外 → Visibility</summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
            throw new NotImplementedException();
    }

    /// <summary>
    /// リストの小さいサムネイル用コンバーター。
    /// 右側の大きなプレビューと同じ Windows シェルのサムネイルを使うことで、
    /// jfif などの画像形式や、動画・exe などのアイコンも一貫して正しく表示する。
    /// </summary>
    public class FileToThumbnailConverter : IValueConverter
    {
        private const int ThumbnailSize = 48;

        /// <summary>パス → サムネイル のキャッシュ (スクロール時の再生成を抑える)</summary>
        private static readonly ConcurrentDictionary<string, ImageSource?> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>キャッシュの上限件数。超えたら全消しして肥大化を防ぐ。</summary>
        private const int MaxCache = 2000;

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            if (Cache.TryGetValue(path, out var cached))
                return cached;

            var image = ShellThumbnail.GetThumbnail(path, ThumbnailSize);

            if (Cache.Count >= MaxCache) Cache.Clear();
            Cache[path] = image;
            return image;
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    /// <summary>値が null のとき Visible、それ以外は Collapsed</summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value == null ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    /// <summary>
    /// ファイルパスから大きなプレビュー用サムネイルを取得するコンバーター。
    /// Windows シェルのサムネイル (IShellItemImageFactory) を使うため、
    /// 画像だけでなく動画・PDF などシェルが扱える形式のサムネイルも表示できる。
    /// </summary>
    public class FileToPreviewImageConverter : IValueConverter
    {
        private const int DefaultSize = 512;
        private const int MaxCache = 32;

        // パス+サイズ → サムネイル のキャッシュ (再選択を高速化)
        private static readonly ConcurrentDictionary<string, ImageSource?> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            int size = DefaultSize;
            if (parameter is string s && int.TryParse(s, out var parsed) && parsed > 0)
                size = parsed;

            string key = $"{path}|{size}";
            if (Cache.TryGetValue(key, out var cached))
                return cached;

            var image = ShellThumbnail.GetThumbnail(path, size);

            if (Cache.Count >= MaxCache) Cache.Clear();
            Cache[key] = image;
            return image;
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    /// <summary>Windows シェルのサムネイルを取得するヘルパー (画像・動画などに対応)</summary>
    internal static class ShellThumbnail
    {
        [Flags]
        private enum SIIGBF
        {
            RESIZETOFIT = 0x00,
            BIGGERSIZEOK = 0x01,
            MEMORYONLY = 0x02,
            ICONONLY = 0x04,
            THUMBNAILONLY = 0x08,
            INCACHEONLY = 0x10,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
            public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            string pszPath, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// 指定サイズのサムネイルを取得する。サムネイルが無い形式ではアイコンにフォールバックする。
        /// </summary>
        public static ImageSource? GetThumbnail(string path, int size)
        {
            IntPtr hbm = IntPtr.Zero;
            IShellItemImageFactory? factory = null;
            try
            {
                Guid iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
                if (factory == null) return null;

                int hr = factory.GetImage(new SIZE(size, size), SIIGBF.BIGGERSIZEOK, out hbm);
                if (hr != 0 || hbm == IntPtr.Zero) return null;

                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                if (factory != null) Marshal.ReleaseComObject(factory);
            }
        }
    }
}
