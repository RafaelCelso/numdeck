using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NumDeck.App.Services;

/// <summary>Icons of files and Start menu apps (shell:AppsFolder\…), as the shell draws them.</summary>
public static class ShellIcons
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Call from an STA thread. Returns a frozen image, or null.</summary>
    public static ImageSource? Get(string? path, int size = 32)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return Cache.GetOrAdd($"{size}|{path}", _ => Load(path, size));
    }

    public static bool TryGetCached(string? path, int size, out ImageSource? icon)
    {
        icon = null;
        return !string.IsNullOrWhiteSpace(path) && Cache.TryGetValue($"{size}|{path}", out icon);
    }

    private static ImageSource? Load(string path, int size)
    {
        object? obj = null;
        nint hbm = 0;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, 0, ref iid, out obj) != 0 || obj is not IShellItemImageFactory factory) return null;
            if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hbm) != 0 || hbm == 0) return null;
            var image = ToBitmapSource(hbm);
            image?.Freeze();
            return image;
        }
        catch { return null; }
        finally
        {
            if (hbm != 0) DeleteObject(hbm);
            if (obj is not null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj);
        }
    }

    /// <summary>Copies a 32-bit DIB section keeping its alpha channel (CreateBitmapSourceFromHBitmap drops it).</summary>
    private static BitmapSource? ToBitmapSource(nint hbm)
    {
        var size = Marshal.SizeOf<DIBSECTION>();
        if (GetObject(hbm, size, out var ds) == size && ds.dsBm.bmBits != 0 && ds.dsBm.bmBitsPixel == 32)
        {
            int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight, stride = ds.dsBm.bmWidthBytes;
            var pixels = new byte[stride * h];
            Marshal.Copy(ds.dsBm.bmBits, pixels, 0, pixels.Length);
            if (ds.dsBmih.biHeight > 0) // bottom-up: flip
            {
                var flipped = new byte[pixels.Length];
                for (int y = 0; y < h; y++) Buffer.BlockCopy(pixels, y * stride, flipped, (h - 1 - y) * stride, stride);
                pixels = flipped;
            }
            return BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        }
        return Imaging.CreateBitmapSourceFromHBitmap(hbm, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
    }

    private const int SIIGBF_BIGGERSIZEOK = 0x1;
    private const int SIIGBF_ICONONLY = 0x4;

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfields0, dsBitfields1, dsBitfields2;
        public nint dshSection;
        public uint dsOffset;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out nint phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object? ppv);

    [DllImport("gdi32.dll")] private static extern int GetObject(nint h, int size, out DIBSECTION ds);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint h);
}
