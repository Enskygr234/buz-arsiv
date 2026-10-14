using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BuzArsiv
{
    /// <summary>Listelerde gösterilen bir dosya satırı.</summary>
    public sealed class DosyaSatiri
    {
        public DosyaSatiri(DosyaKaydi k, Ayarlar ayarlar, DateTime simdi)
        {
            Kayit = k;
            Ad = k.Ad;
            Klasor = ayarlar.GosterimYolu(k.Klasor);
            Zaman = Metin.Zaman(k.SonIslem, simdi);
            Grup = Metin.Grup(k.SonIslem, simdi);
            Boyut = Metin.Boyut(k.Boyut);
            Simge = Simgeler.Getir(k.Ad);
        }

        public DosyaKaydi Kayit { get; }
        public string Ad { get; }
        public string Klasor { get; }
        public string Zaman { get; }
        public string Grup { get; }
        public string Boyut { get; }
        public ImageSource? Simge { get; }
    }

    /// <summary>Silinenler listesindeki bir satır.</summary>
    public sealed class SilinenSatiri
    {
        public SilinenSatiri(SilinenDosya s, Ayarlar ayarlar, DateTime simdi)
        {
            Kayit = s;
            Ad = s.Ad;
            Klasor = ayarlar.GosterimYolu(s.Klasor);
            Bilgi = Metin.Zaman(s.Silindi, simdi) + " silindi · " + Metin.Boyut(s.Boyut);
            Simge = Simgeler.Getir(s.Ad);
        }

        public SilinenDosya Kayit { get; }
        public string Ad { get; }
        public string Klasor { get; }
        public string Bilgi { get; }
        public ImageSource? Simge { get; }
    }

    /// <summary>Dosya türünün Windows'taki simgesi (uzantıya göre önbellekli).</summary>
    public static class Simgeler
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0, SHGFI_USEFILEATTRIBUTES = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

        static readonly Dictionary<string, ImageSource?> onbellek = new(StringComparer.OrdinalIgnoreCase);

        public static ImageSource? Getir(string ad)
        {
            var uzanti = Path.GetExtension(ad);
            if (string.IsNullOrEmpty(uzanti)) uzanti = ".dosya";
            if (onbellek.TryGetValue(uzanti, out var hazir)) return hazir;

            ImageSource? simge = null;
            var bilgi = new SHFILEINFO();
            // Dosyanın var olması gerekmez: Windows uzantıya bakar (silinen dosyalar için de çalışır)
            SHGetFileInfo("dosya" + uzanti, FILE_ATTRIBUTE_NORMAL, ref bilgi, (uint)Marshal.SizeOf(bilgi),
                SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
            if (bilgi.hIcon != IntPtr.Zero)
            {
                try
                {
                    var bmp = Imaging.CreateBitmapSourceFromHIcon(bilgi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bmp.Freeze();
                    simge = bmp;
                }
                finally { DestroyIcon(bilgi.hIcon); }
            }
            onbellek[uzanti] = simge;
            return simge;
        }
    }

    /// <summary>Dosyayı ya da klasörünü açan kısa yollar.</summary>
    public static class Kabuk
    {
        public static void Ac(string yol)
        {
            try { Process.Start(new ProcessStartInfo(yol) { UseShellExecute = true }); }
            catch (Exception e) { MessageBox.Show("Dosya açılamadı:\n" + e.Message, "Buz Arşiv", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        /// <summary>Dosyanın klasörünü açar ve dosyayı seçili getirir.</summary>
        public static void KlasorunuAc(string yol)
        {
            try
            {
                if (File.Exists(yol)) Process.Start("explorer.exe", $"/select,\"{yol}\"");
                else Process.Start(new ProcessStartInfo(Path.GetDirectoryName(yol)!) { UseShellExecute = true });
            }
            catch (Exception) { }
        }
    }
}
