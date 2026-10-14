using System;
using System.Globalization;
using System.Text;

namespace BuzArsiv
{
    /// <summary>Türkçe arama ve okunabilir tarih / boyut metinleri.</summary>
    public static class Metin
    {
        public static readonly CultureInfo Tr = new("tr-TR");

        /// <summary>
        /// Aramada büyük/küçük harf ve Türkçe karakter farkını yok sayar:
        /// "ÖDEV", "ödev" ve "odev" aynı sonucu verir.
        /// </summary>
        public static string Katla(string s)
        {
            var kucuk = s.ToLower(Tr);
            var sb = new StringBuilder(kucuk.Length);
            foreach (var c in kucuk)
            {
                sb.Append(c switch
                {
                    'ı' => 'i',
                    'ğ' => 'g',
                    'ü' => 'u',
                    'ş' => 's',
                    'ö' => 'o',
                    'ç' => 'c',
                    'â' => 'a',
                    'î' => 'i',
                    'û' => 'u',
                    _ => c
                });
            }
            return sb.ToString();
        }

        public static string Boyut(long bayt)
        {
            if (bayt < 1024) return bayt + " B";
            if (bayt < 1024 * 1024) return (bayt / 1024.0).ToString("0", Tr) + " KB";
            if (bayt < 1024L * 1024 * 1024) return (bayt / 1048576.0).ToString("0.#", Tr) + " MB";
            return (bayt / 1073741824.0).ToString("0.##", Tr) + " GB";
        }

        /// <summary>"bugün 14:05", "dün 09:12", "Salı 18:40", "3 Eki 2026" gibi.</summary>
        public static string Zaman(DateTime t, DateTime simdi)
        {
            var gun = (simdi.Date - t.Date).Days;
            if (gun == 0) return "bugün " + t.ToString("HH:mm", Tr);
            if (gun == 1) return "dün " + t.ToString("HH:mm", Tr);
            if (gun > 1 && gun < 7) return t.ToString("dddd HH:mm", Tr);
            if (t.Year == simdi.Year) return t.ToString("d MMM HH:mm", Tr);
            return t.ToString("d MMM yyyy", Tr);
        }

        /// <summary>Zaman çizelgesindeki başlık.</summary>
        public static string Grup(DateTime t, DateTime simdi)
        {
            var gun = (simdi.Date - t.Date).Days;
            if (gun <= 0) return "Bugün";
            if (gun == 1) return "Dün";
            if (gun < 7) return "Bu hafta";
            if (gun < 31) return "Bu ay";
            return "Daha eski";
        }
    }
}
