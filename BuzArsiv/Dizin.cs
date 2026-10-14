using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading;

namespace BuzArsiv
{
    /// <summary>Dizindeki bir dosya.</summary>
    public sealed class DosyaKaydi
    {
        public DosyaKaydi(string yol, long boyut, DateTime degisti, DateTime olusturuldu)
        {
            YolAyarla(yol);
            Boyut = boyut;
            Degisti = degisti;
            Olusturuldu = olusturuldu;
        }

        public string Yol { get; private set; } = "";
        public string Ad { get; private set; } = "";
        public string Klasor { get; private set; } = "";
        public long Boyut { get; internal set; }
        public DateTime Degisti { get; internal set; }
        public DateTime Olusturuldu { get; internal set; }

        /// <summary>Kaydedildiği ya da indirildiği an: ikisinden hangisi yeniyse.</summary>
        public DateTime SonIslem => Degisti > Olusturuldu ? Degisti : Olusturuldu;

        internal string KatliAd = "";
        internal string KatliKlasor = "";

        internal void YolAyarla(string yol)
        {
            Yol = yol;
            Ad = Path.GetFileName(yol);
            Klasor = Path.GetDirectoryName(yol) ?? "";
            KatliAd = Metin.Katla(Ad);
            KatliKlasor = Metin.Katla(Klasor);
        }
    }

    /// <summary>
    /// İzlenen klasörlerdeki bütün dosyaların bellekteki listesi. Açılışta bir kez tarar,
    /// sonra Windows'un dosya bildirimleriyle güncel kalır.
    /// </summary>
    public sealed class Dizin : IDisposable
    {
        static readonly HashSet<string> AtlananKlasorler = new(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", ".svn", ".vs", ".idea", "__pycache__", "$recycle.bin"
        };

        readonly object kilit = new();
        readonly Dictionary<string, DosyaKaydi> kayitlar = new(StringComparer.OrdinalIgnoreCase);
        readonly List<FileSystemWatcher> izleyiciler = new();
        DosyaKaydi[]? anlik;
        int taramaSayaci;

        public Dizin(IEnumerable<string> klasorler)
        {
            Klasorler = klasorler
                .Where(Directory.Exists)
                .Select(k => Path.GetFullPath(k).TrimEnd('\\'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<string> Klasorler { get; }
        public bool Hazir { get; private set; }
        public int Sayi { get { lock (kilit) return kayitlar.Count; } }

        /// <summary>Bir dosya oluştu ya da değişti.</summary>
        public event Action<string>? DosyaDegisti;
        /// <summary>Bir dosya silindi (ya da izlenen klasörlerin dışına taşındı).</summary>
        public event Action<string>? DosyaSilindi;
        /// <summary>Bir dosyanın adı ya da yeri değişti (eski yol, yeni yol).</summary>
        public event Action<string, string>? DosyaTasindi;
        /// <summary>Liste değişti; arayüz yenilenebilir.</summary>
        public event Action? Guncellendi;
        /// <summary>İlk tarama bitti.</summary>
        public event Action? TaramaBitti;

        public void Baslat()
        {
            // Önce izlemeye başla, sonra tara: tarama sırasında olan değişiklikler kaçmasın.
            foreach (var k in Klasorler) IzleyiciKur(k);
            TaramayiBaslat();
        }

        void TaramayiBaslat()
        {
            var no = Interlocked.Increment(ref taramaSayaci);
            new Thread(() => Tara(no)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Tarama" }.Start();
        }

        void Tara(int no)
        {
            var bulunan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kok in Klasorler)
            {
                var parti = new List<DosyaKaydi>(512);
                foreach (var k in Listele(kok))
                {
                    if (no != Volatile.Read(ref taramaSayaci)) return;   // daha yeni bir tarama başladı
                    parti.Add(k);
                    bulunan.Add(k.Yol);
                    if (parti.Count == 512) { Ekle(parti); parti.Clear(); }
                }
                Ekle(parti);
                Guncellendi?.Invoke();
            }

            // Yeniden taramada artık olmayanları çıkar
            lock (kilit)
            {
                foreach (var eski in kayitlar.Keys.Where(y => !bulunan.Contains(y)).ToList()) kayitlar.Remove(eski);
                anlik = null;
            }
            bool ilk = !Hazir;
            Hazir = true;
            if (ilk) TaramaBitti?.Invoke();
            Guncellendi?.Invoke();
        }

        void Ekle(List<DosyaKaydi> parti)
        {
            lock (kilit)
            {
                foreach (var k in parti) kayitlar[k.Yol] = k;
                anlik = null;
            }
        }

        static IEnumerable<DosyaKaydi> Listele(string kok)
        {
            var secenek = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            };
            return new FileSystemEnumerable<DosyaKaydi>(kok,
                (ref FileSystemEntry g) => new DosyaKaydi(g.ToFullPath(), g.Length,
                    g.LastWriteTimeUtc.LocalDateTime, g.CreationTimeUtc.LocalDateTime), secenek)
            {
                ShouldIncludePredicate = (ref FileSystemEntry g) => !g.IsDirectory && !GeciciDosya(g.FileName.ToString()),
                ShouldRecursePredicate = (ref FileSystemEntry g) => !AtlananKlasorler.Contains(g.FileName.ToString()),
            };
        }

        /// <summary>Programların kaydederken bıraktığı geçici dosyalar.</summary>
        internal static bool GeciciDosya(string ad)
        {
            if (ad.StartsWith("~", StringComparison.Ordinal)) return true;
            if (ad.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || ad.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase)) return true;
            var uzanti = Path.GetExtension(ad).ToLowerInvariant();
            return uzanti is ".tmp" or ".temp" or ".crdownload" or ".part" or ".partial" or ".download" or ".lock";
        }

        static bool Atlanmali(string yol)
        {
            if (GeciciDosya(Path.GetFileName(yol))) return true;
            foreach (var parca in yol.Split('\\'))
                if (AtlananKlasorler.Contains(parca)) return true;
            return false;
        }

        void IzleyiciKur(string kok)
        {
            var w = new FileSystemWatcher(kok)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            w.Created += (_, e) => Olustu(e.FullPath, yeni: true);
            w.Changed += (_, e) => Olustu(e.FullPath, yeni: false);
            w.Deleted += (_, e) => Silindi(e.FullPath);
            w.Renamed += (_, e) => Tasindi(e.OldFullPath, e.FullPath);
            // Çok hızlı değişiklikte Windows'un bildirim tamponu taşabilir: o zaman baştan tara.
            w.Error += (_, _) => TaramayiBaslat();
            w.EnableRaisingEvents = true;
            izleyiciler.Add(w);
        }

        void Olustu(string yol, bool yeni)
        {
            if (Atlanmali(yol)) return;
            try
            {
                var fi = new FileInfo(yol);
                if (!fi.Exists)
                {
                    // Dışarıdan bir klasör taşındıysa içindekiler tek tek bildirilmez
                    if (yeni && Directory.Exists(yol)) KlasorEklendi(yol);
                    return;
                }
                if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return;
                var k = new DosyaKaydi(yol, fi.Length, fi.LastWriteTime, fi.CreationTime);
                lock (kilit) { kayitlar[yol] = k; anlik = null; }
                DosyaDegisti?.Invoke(yol);
                Guncellendi?.Invoke();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        void KlasorEklendi(string klasor)
        {
            var liste = Listele(klasor).ToList();
            Ekle(liste);
            foreach (var k in liste) DosyaDegisti?.Invoke(k.Yol);
            Guncellendi?.Invoke();
        }

        void Silindi(string yol)
        {
            var silinen = new List<string>();
            lock (kilit)
            {
                if (kayitlar.Remove(yol)) silinen.Add(yol);
                else
                {
                    // Bir klasör silindiyse Windows yalnızca klasörü bildirir; içindekileri biz buluruz.
                    var on = yol.TrimEnd('\\') + "\\";
                    foreach (var anahtar in kayitlar.Keys.Where(a => a.StartsWith(on, StringComparison.OrdinalIgnoreCase)).ToList())
                    {
                        kayitlar.Remove(anahtar);
                        silinen.Add(anahtar);
                    }
                }
                if (silinen.Count > 0) anlik = null;
            }
            foreach (var s in silinen) DosyaSilindi?.Invoke(s);
            if (silinen.Count > 0) Guncellendi?.Invoke();
        }

        void Tasindi(string eski, string yeni)
        {
            var tasinan = new List<(string, string)>();
            bool dosyaydi;
            lock (kilit)
            {
                dosyaydi = kayitlar.TryGetValue(eski, out var k);
                if (dosyaydi && k != null)
                {
                    kayitlar.Remove(eski);
                    if (!Atlanmali(yeni))
                    {
                        k.YolAyarla(yeni);
                        kayitlar[yeni] = k;
                        tasinan.Add((eski, yeni));
                    }
                }
                else
                {
                    // Klasörün adı değiştiyse içindekilerin yolu da değişir
                    var on = eski.TrimEnd('\\') + "\\";
                    foreach (var anahtar in kayitlar.Keys.Where(a => a.StartsWith(on, StringComparison.OrdinalIgnoreCase)).ToList())
                    {
                        var kayit = kayitlar[anahtar];
                        var yeniYol = yeni.TrimEnd('\\') + "\\" + anahtar.Substring(on.Length);
                        kayitlar.Remove(anahtar);
                        kayit.YolAyarla(yeniYol);
                        kayitlar[yeniYol] = kayit;
                        tasinan.Add((anahtar, yeniYol));
                    }
                }
                anlik = null;
            }

            if (dosyaydi && tasinan.Count == 0) DosyaSilindi?.Invoke(eski);   // geçici bir ada çevrildi
            foreach (var (e, y) in tasinan) DosyaTasindi?.Invoke(e, y);
            // Geçici adla kaydedilip asıl ada çevrilen dosyalar (Word, Excel böyle kaydeder)
            if (!dosyaydi && tasinan.Count == 0) Olustu(yeni, yeni: true);
            Guncellendi?.Invoke();
        }

        DosyaKaydi[] Anlik()
        {
            lock (kilit) return anlik ??= kayitlar.Values.ToArray();
        }

        public DosyaKaydi? Bul(string yol)
        {
            lock (kilit) return kayitlar.TryGetValue(yol, out var k) ? k : null;
        }

        /// <summary>
        /// Dosya adında (ya da klasöründe) geçen kelimelerle arar. Her kelime bulunmalı;
        /// adın başında geçen, tam eşleşen ve yeni olan dosyalar üstte çıkar.
        /// </summary>
        public List<DosyaKaydi> Ara(string sorgu, int enFazla = 40)
        {
            var kelimeler = Metin.Katla(sorgu).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (kelimeler.Length == 0) return new List<DosyaKaydi>();
            var simdi = DateTime.Now;
            var sonuc = new List<(DosyaKaydi k, int puan)>();

            foreach (var k in Anlik())
            {
                int puan = 0;
                bool hepsi = true;
                foreach (var kelime in kelimeler)
                {
                    int yer = k.KatliAd.IndexOf(kelime, StringComparison.Ordinal);
                    if (yer == 0) puan += 60;
                    else if (yer > 0) puan += KelimeBasi(k.KatliAd, yer) ? 40 : 20;
                    else if (k.KatliKlasor.Contains(kelime, StringComparison.Ordinal)) puan += 5;
                    else { hepsi = false; break; }
                }
                if (!hepsi) continue;
                if (Path.GetFileNameWithoutExtension(k.KatliAd) == string.Join(" ", kelimeler)) puan += 50;
                var gun = (simdi - k.SonIslem).TotalDays;
                puan += gun < 1 ? 15 : gun < 7 ? 10 : gun < 30 ? 5 : 0;
                sonuc.Add((k, puan));
            }

            return sonuc
                .OrderByDescending(s => s.puan)
                .ThenByDescending(s => s.k.SonIslem)
                .Take(enFazla)
                .Select(s => s.k)
                .ToList();
        }

        static bool KelimeBasi(string ad, int yer)
        {
            var onceki = ad[yer - 1];
            return !char.IsLetterOrDigit(onceki);
        }

        /// <summary>Son kaydedilen / indirilen dosyalar, yeniden eskiye.</summary>
        public List<DosyaKaydi> Son(int enFazla, string? kok = null, int gun = 31)
        {
            var sinir = DateTime.Now.AddDays(-gun);
            var on = kok == null ? null : kok.TrimEnd('\\') + "\\";
            return Anlik()
                .Where(k => k.SonIslem >= sinir && (on == null || k.Yol.StartsWith(on, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(k => k.SonIslem)
                .Take(enFazla)
                .ToList();
        }

        public IEnumerable<string> TumYollar() => Anlik().Select(k => k.Yol);

        public void Dispose()
        {
            Interlocked.Increment(ref taramaSayaci);
            foreach (var w in izleyiciler) w.Dispose();
            izleyiciler.Clear();
        }
    }
}
