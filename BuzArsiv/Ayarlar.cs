using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace BuzArsiv
{
    /// <summary>Kullanıcı ayarları: %LocalAppData%\BuzArsiv\ayarlar.json</summary>
    public sealed class Ayarlar
    {
        public List<string> Klasorler { get; set; } = new();
        public int EnBuyukDosyaMB { get; set; } = 50;
        public int DepoSiniriGB { get; set; } = 5;
        public int SaklamaGun { get; set; } = 30;
        public bool KorumaDuraklatildi { get; set; }

        public static string VeriKlasoru { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuzArsiv");

        public static string DepoKlasoru => Path.Combine(VeriKlasoru, "buzdolabi");
        static string DosyaYolu => Path.Combine(VeriKlasoru, "ayarlar.json");

        public static bool IlkCalisma => !File.Exists(DosyaYolu);

        public static Ayarlar Yukle()
        {
            Ayarlar? a = null;
            try
            {
                if (File.Exists(DosyaYolu)) a = JsonSerializer.Deserialize<Ayarlar>(File.ReadAllText(DosyaYolu));
            }
            catch (Exception) { /* bozuksa varsayılanlarla başla */ }
            a ??= new Ayarlar();
            if (a.Klasorler.Count == 0) a.Klasorler = VarsayilanKlasorler();
            return a;
        }

        public void Kaydet()
        {
            Directory.CreateDirectory(VeriKlasoru);
            File.WriteAllText(DosyaYolu, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static List<string> VarsayilanKlasorler() => new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            IndirilenlerKlasoru(),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        }.Where(k => !string.IsNullOrEmpty(k) && Directory.Exists(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

        public static string IndirilenlerKlasoru()
        {
            var indirilenler = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            if (SHGetKnownFolderPath(indirilenler, 0, IntPtr.Zero, out var p) == 0)
            {
                try { return Marshal.PtrToStringUni(p) ?? ""; }
                finally { Marshal.FreeCoTaskMem(p); }
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        /// <summary>Bilinen klasörlerin Türkçe adı ("Belgeler", "İndirilenler"...).</summary>
        public static string Etiket(string kok)
        {
            bool Ayni(string a) => !string.IsNullOrEmpty(a) && string.Equals(Path.GetFullPath(a).TrimEnd('\\'), kok.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            if (Ayni(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))) return "Masaüstü";
            if (Ayni(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))) return "Belgeler";
            if (Ayni(IndirilenlerKlasoru())) return "İndirilenler";
            if (Ayni(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures))) return "Resimler";
            if (Ayni(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos))) return "Videolar";
            if (Ayni(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic))) return "Müzik";
            return Path.GetFileName(kok.TrimEnd('\\'));
        }

        /// <summary>"C:\Users\ens\Documents\Okul\Tarih" → "Belgeler › Okul › Tarih"</summary>
        public string GosterimYolu(string klasor)
        {
            foreach (var kok in Klasorler)
            {
                var k = kok.TrimEnd('\\');
                if (string.Equals(klasor, k, StringComparison.OrdinalIgnoreCase)) return Etiket(k);
                if (klasor.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase))
                    return Etiket(k) + " › " + klasor.Substring(k.Length + 1).Replace("\\", " › ");
            }
            return klasor;
        }

        const string BaslangicAnahtari = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool WindowsIleBasliyor
        {
            get
            {
                using var a = Registry.CurrentUser.OpenSubKey(BaslangicAnahtari);
                return a?.GetValue("BuzArsiv") != null;
            }
            set
            {
                using var a = Registry.CurrentUser.OpenSubKey(BaslangicAnahtari, writable: true)!;
                if (value) a.SetValue("BuzArsiv", $"\"{Environment.ProcessPath}\" --gizli");
                else a.DeleteValue("BuzArsiv", throwOnMissingValue: false);
            }
        }
    }
}
