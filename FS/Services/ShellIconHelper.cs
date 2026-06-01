using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterFileSys.Services
{
    /// <summary>
    /// Utility class to extract high-fidelity Windows shell icons using SHGetFileInfo
    /// </summary>
    public static class ShellIconHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_LARGEICON = 0x0; // 32x32 pixels
        private const uint SHGFI_SMALLICON = 0x1; // 16x16 pixels
        private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new();

        /// <summary>
        /// Extract standard Windows icon for a given file or folder path
        /// </summary>
        public static ImageSource? GetIconForPath(string path, bool large = false)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string cacheKey = $"{path.ToLowerInvariant()}_{(large ? "l" : "s")}";
            if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
            {
                return cachedIcon;
            }

            try
            {
                var shinfo = new SHFILEINFO();
                uint flags = SHGFI_ICON | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);
                
                IntPtr hImg = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
                if (hImg != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    ImageSource img = Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    
                    img.Freeze();
                    DestroyIcon(shinfo.hIcon);
                    _iconCache.TryAdd(cacheKey, img);
                    return img;
                }
            }
            catch
            {
                // Fallback
            }

            return null;
        }

        /// <summary>
        /// Extract icon for a file extension without hitting the disk
        /// </summary>
        public static ImageSource? GetIconForExtension(string extension, bool isDirectory, bool large = false)
        {
            string cacheKey = $"ext_{extension.ToLowerInvariant()}_{(isDirectory ? "dir" : "file")}_{(large ? "l" : "s")}";
            if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
            {
                return cachedIcon;
            }

            try
            {
                var shinfo = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);
                uint attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

                IntPtr hImg = SHGetFileInfo(extension, attributes, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
                if (hImg != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    ImageSource img = Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    
                    img.Freeze();
                    DestroyIcon(shinfo.hIcon);
                    _iconCache.TryAdd(cacheKey, img);
                    return img;
                }
            }
            catch
            {
                // Fallback
            }

            return null;
        }
    }
}
