using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace BuzArsiv
{
    /// <summary>Silinmiş ama Buz Arşiv'de kopyası duran bir dosya.</summary>
    public sealed class SilinenDosya
    {
        public string Yol { get; set; } = "";
        public string Ozet { get; set; } = "";
        public long Boyut { get; set; }
        public DateTime Degisti { get; set; }
        public DateTime Silindi { get; set; }
        /// <summary>Silme kesinleşti mi? Kaydederken dosyayı silip yeniden yazan programlar yüzünden birkaç saniye beklenir.</summary>
        public bool Kesin { get; set; }

        public string Ad => Path.GetFileName(Yol);
        public string Klasor => Path.GetDirectoryName(Yol) ?? "";
    }

    /// <summary>
    /// "Buzdolabı": izlenen klasörlerdeki dosyaların son hâlinin kopyası. Bir dosya silinince
    /// kopyası "Silinenler"e geçer ve oradan geri getirilebilir. Aynı içerik bir kez saklanır.
    /// </summary>
    public sealed class Depo : IDisposable
    {
        sealed class Kopya
        {
            public string Yol { get; set; } = "";
            public string Ozet { get; set; } = "";
            public long Boyut { get; set; }
            public DateTime Degisti { get; set; }
        }

        sealed class KayitDosyasi
        {
            public List<Kopya> Kopyalar { get; set; } = new();
            public List<SilinenDosya> Silinenler { get; set; } = new();
        }

        readonly object kilit = new();
        readonly string kok, nesneler, gecici, kayitYolu;
        readonly Dictionary<string, Kopya> canli = new(StringComparer.OrdinalIgnoreCase);
        readonly List<SilinenDosya> silinenler = new();
        readonly Dictionary<string, int> baglanti = new();      // özet -> kaç kayıt kullanıyor
        readonly Dictionary<string, long> nesneBoyutu = new();  // özet -> bayt
        readonly Dictionary<string, (DateTime zaman, int deneme)> bekleyen = new(StringComparer.OrdinalIgnoreCase);
        readonly Queue<string> ilkKoruma = new();
        readonly AutoResetEvent uyandir = new(false);
        readonly Thread isci;
        volatile bool kapaniyor;
        bool kirli;
        DateTime sonKayit = DateTime.MinValue;

        public Depo(string kokKlasor)
        {
            kok = kokKlasor;
            nesneler = Path.Combine(kok, "nesneler");
            gecici = Path.Combine(kok, "gecici");
            kayitYolu = Path.Combine(kok, "kayitlar.json");
            Directory.CreateDirectory(nesneler);
            Directory.CreateDirectory(gecici);
            foreach (var f in Directory.GetFiles(gecici)) TryDelete(f);
            Yukle();

            isci = new Thread(Calis) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Depo" };
            isci.Start();
        }

        // ---- ayarlar ----
        public long EnBuyukDosya { get; set; } = 50L * 1024 * 1024;
        public long DepoSiniri { get; set; } = 5L * 1024 * 1024 * 1024;
        public int SaklamaGun { get; set; } = 30;
        public bool Duraklatildi { get; set; }
        /// <summary>Dosya son değiştikten sonra kopyalamadan önce beklenecek süre (yazma bitsin diye).</summary>
        public TimeSpan Sessizlik { get; set; } = TimeSpan.FromSeconds(2);
        /// <summary>Silmenin kesinleşmesi için beklenecek süre (kaydetme ya da taşıma olabilir).</summary>
        public TimeSpan SilmeBeklemesi { get; set; } = TimeSpan.FromSeconds(10);
        public Func<DateTime> Saat { get; set; } = () => DateTime.Now;

        /// <summary>Silinenler listesi değişti.</summary>
        public event Action? Guncellendi;

        // ---- dizinden gelen bildirimler ----

        public void DosyaDegisti(string yol)
        {
            lock (kilit)
            {
                // Aynı yolda yeni dosya: az önceki "silme" aslında kaydetmeymiş (Word, Excel böyle yapar)
                SilinenleriKaldir(s => !s.Kesin && string.Equals(s.Yol, yol, StringComparison.OrdinalIgnoreCase));
                bekleyen[yol] = (Saat() + Sessizlik, 0);
            }
            uyandir.Set();
        }

        public void DosyaSilindi(string yol)
        {
            lock (kilit)
            {
                bekleyen.Remove(yol);
                if (!canli.Remove(yol, out var k)) return;   // korunmuyordu (çok büyük ya da daha kopyalanmamıştı)
                silinenler.Add(new SilinenDosya { Yol = k.Yol, Ozet = k.Ozet, Boyut = k.Boyut, Degisti = k.Degisti, Silindi = Saat() });
                kirli = true;
            }
            uyandir.Set();
        }

        public void DosyaTasindi(string eski, string yeni)
        {
            lock (kilit)
            {
                if (canli.Remove(eski, out var k))
                {
                    k.Yol = yeni;
                    if (canli.TryGetValue(yeni, out var ustune)) Birak(ustune.Ozet);
                    canli[yeni] = k;
                    kirli = true;
                }
                if (bekleyen.Remove(eski, out var b)) bekleyen[yeni] = b;
            }
        }

        /// <summary>Açılışta var olan dosyaları sırayla, yavaşça korumaya al.</summary>
        public void IlkKorumayiBaslat(IEnumerable<string> yollar)
        {
            lock (kilit)
            {
                foreach (var y in yollar) ilkKoruma.Enqueue(y);
            }
            uyandir.Set();
        }

        // ---- arayüz için ----

        public List<SilinenDosya> Silinenler()
        {
            lock (kilit) return silinenler.Where(s => s.Kesin).OrderByDescending(s => s.Silindi).ToList();
        }

        public (long bayt, int korunan, int silinen, int bekleyen) Durum()
        {
            lock (kilit) return (nesneBoyutu.Values.Sum(), canli.Count, silinenler.Count(s => s.Kesin), ilkKoruma.Count + bekleyen.Count);
        }

        public bool Korunuyor(string yol)
        {
            lock (kilit) return canli.ContainsKey(yol);
        }

        /// <summary>Silinen dosyayı geri getirir; geri getirildiği yolu döndürür.</summary>
        public string GeriGetir(SilinenDosya s, string? hedef = null)
        {
            string nesne;
            lock (kilit)
            {
                if (!silinenler.Contains(s)) throw new InvalidOperationException("Bu kayıt artık yok.");
                nesne = NesneYolu(s.Ozet);
            }

            hedef ??= BosYol(s.Yol);
            Directory.CreateDirectory(Path.GetDirectoryName(hedef)!);
            File.Copy(nesne, hedef, overwrite: false);
            try { File.SetLastWriteTime(hedef, s.Degisti); } catch (IOException) { }

            lock (kilit)
            {
                silinenler.Remove(s);
                // Kopya artık yeni dosyanın korumasıdır: aynı içeriği yeniden kopyalamaya gerek yok
                if (canli.TryGetValue(hedef, out var eski)) Birak(eski.Ozet);
                canli[hedef] = new Kopya { Yol = hedef, Ozet = s.Ozet, Boyut = s.Boyut, Degisti = File.GetLastWriteTime(hedef) };
                bekleyen.Remove(hedef);
                kirli = true;
            }
            Guncellendi?.Invoke();
            return hedef;
        }

        /// <summary>Asıl yer doluysa "ad (geri getirildi).uzantı" gibi boş bir ad bulur.</summary>
        static string BosYol(string yol)
        {
            if (!File.Exists(yol)) return yol;
            var klasor = Path.GetDirectoryName(yol)!;
            var ad = Path.GetFileNameWithoutExtension(yol);
            var uzanti = Path.GetExtension(yol);
            for (int i = 1; ; i++)
            {
                var ek = i == 1 ? " (geri getirildi)" : $" (geri getirildi {i})";
                var aday = Path.Combine(klasor, ad + ek + uzanti);
                if (!File.Exists(aday)) return aday;
            }
        }

        /// <summary>Silinen dosyanın Buz Arşiv'deki kopyasını kalıcı olarak siler.</summary>
        public void KaliciSil(SilinenDosya s)
        {
            lock (kilit)
            {
                if (silinenler.Remove(s)) { Birak(s.Ozet); kirli = true; }
            }
            Guncellendi?.Invoke();
        }

        // ---- iç işleyiş ----

        void Calis()
        {
            while (!kapaniyor)
            {
                uyandir.WaitOne(500);
                if (kapaniyor) break;
                try
                {
                    SilmeleriKesinlestir();
                    BekleyenleriKoru();
                    IlkKorumadanBirParti();
                    Temizle();
                    if (kirli && Saat() - sonKayit > TimeSpan.FromSeconds(30)) Kaydet();
                }
                catch (Exception) { /* bir dosyadaki sorun ötekileri durdurmasın */ }
            }
        }

        void SilmeleriKesinlestir()
        {
            bool degisti = false;
            lock (kilit)
            {
                var sinir = Saat() - SilmeBeklemesi;
                foreach (var s in silinenler)
                    if (!s.Kesin && s.Silindi <= sinir) { s.Kesin = true; degisti = true; kirli = true; }
            }
            if (degisti) Guncellendi?.Invoke();
        }

        void BekleyenleriKoru()
        {
            List<(string yol, int deneme)> hazir;
            lock (kilit)
            {
                var simdi = Saat();
                hazir = bekleyen.Where(b => b.Value.zaman <= simdi).Select(b => (b.Key, b.Value.deneme)).ToList();
                foreach (var h in hazir) bekleyen.Remove(h.yol);
            }
            foreach (var (yol, deneme) in hazir)
            {
                if (kapaniyor) return;
                if (!Koru(yol) && deneme < 3)
                {
                    // Dosya hâlâ açık ve kilitliyse biraz sonra yeniden dene
                    lock (kilit) bekleyen[yol] = (Saat().AddSeconds(10), deneme + 1);
                }
            }
        }

        void IlkKorumadanBirParti()
        {
            for (int i = 0; i < 25 && !kapaniyor; i++)
            {
                string yol;
                lock (kilit)
                {
                    if (bekleyen.Count > 0 || ilkKoruma.Count == 0) return;   // yeni değişiklikler önce gelir
                    yol = ilkKoruma.Dequeue();
                }
                Koru(yol);
            }
            lock (kilit) if (ilkKoruma.Count > 0) uyandir.Set();
        }

        /// <summary>Dosyanın şimdiki hâlini kopyalar. Kilitliyse false döner.</summary>
        bool Koru(string yol)
        {
            if (Duraklatildi) return true;
            FileInfo fi;
            try { fi = new FileInfo(yol); if (!fi.Exists) return true; }
            catch (Exception) { return true; }

            lock (kilit)
            {
                if (fi.Length > EnBuyukDosya)
                {
                    if (canli.Remove(yol, out var eski)) { Birak(eski.Ozet); kirli = true; }
                    return true;
                }
                if (canli.TryGetValue(yol, out var mevcut) && mevcut.Boyut == fi.Length && mevcut.Degisti == fi.LastWriteTime) return true;
            }

            var gec = Path.Combine(gecici, Guid.NewGuid().ToString("N"));
            string ozet;
            try
            {
                using (var kaynak = new FileStream(yol, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var hedef = new FileStream(gec, FileMode.CreateNew, FileAccess.Write))
                using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var tampon = new byte[81920];
                    int n;
                    while ((n = kaynak.Read(tampon, 0, tampon.Length)) > 0)
                    {
                        sha.AppendData(tampon, 0, n);
                        hedef.Write(tampon, 0, n);
                    }
                    ozet = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                }
            }
            catch (FileNotFoundException) { TryDelete(gec); return true; }
            catch (DirectoryNotFoundException) { TryDelete(gec); return true; }
            catch (IOException) { TryDelete(gec); return false; }
            catch (UnauthorizedAccessException) { TryDelete(gec); return true; }

            var nesne = NesneYolu(ozet);
            lock (kilit)
            {
                if (File.Exists(nesne)) TryDelete(gec);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(nesne)!);
                    File.Move(gec, nesne);
                }

                Tut(ozet, fi.Length);
                if (canli.TryGetValue(yol, out var onceki)) Birak(onceki.Ozet);
                canli[yol] = new Kopya { Yol = yol, Ozet = ozet, Boyut = fi.Length, Degisti = fi.LastWriteTime };

                // Az önce "silinen" aynı içerik başka bir yerde belirdiyse bu bir taşımaydı
                var simdi = Saat();
                SilinenleriKaldir(s => !s.Kesin && s.Ozet == ozet && simdi - s.Silindi < SilmeBeklemesi + TimeSpan.FromSeconds(5));
                kirli = true;
            }
            return true;
        }

        void Temizle()
        {
            bool degisti = false;
            lock (kilit)
            {
                // Süresi dolan silinenler
                var sinir = Saat().AddDays(-SaklamaGun);
                foreach (var s in silinenler.Where(s => s.Silindi < sinir).ToList())
                {
                    silinenler.Remove(s);
                    Birak(s.Ozet);
                    degisti = true;
                }

                // Alan sınırı: önce en eski silinenler, sonra en uzun süredir dokunulmamış dosyaların kopyaları
                long toplam = nesneBoyutu.Values.Sum();
                if (toplam > DepoSiniri)
                {
                    foreach (var s in silinenler.OrderBy(s => s.Silindi).ToList())
                    {
                        if (toplam <= DepoSiniri) break;
                        silinenler.Remove(s);
                        toplam -= Birak(s.Ozet, out var serbest) ? serbest : 0;
                        degisti = true;
                    }
                    foreach (var k in canli.Values.OrderBy(k => k.Degisti).ToList())
                    {
                        if (toplam <= DepoSiniri) break;
                        canli.Remove(k.Yol);
                        toplam -= Birak(k.Ozet, out var serbest) ? serbest : 0;
                        kirli = true;
                    }
                }
                if (degisti) kirli = true;
            }
            if (degisti) Guncellendi?.Invoke();
        }

        string NesneYolu(string ozet) => Path.Combine(nesneler, ozet.Substring(0, 2), ozet);

        void Tut(string ozet, long boyut)
        {
            baglanti[ozet] = baglanti.TryGetValue(ozet, out var n) ? n + 1 : 1;
            nesneBoyutu[ozet] = boyut;
        }

        void Birak(string ozet) => Birak(ozet, out _);

        /// <summary>Koşula uyan silinen kayıtlarını çıkarır ve kopyalarını bırakır.</summary>
        void SilinenleriKaldir(Predicate<SilinenDosya> kosul)
        {
            foreach (var s in silinenler.FindAll(kosul))
            {
                silinenler.Remove(s);
                Birak(s.Ozet);
                kirli = true;
            }
        }

        /// <summary>Bir kaydın kopyaya bağlantısını bırakır; kimse kullanmıyorsa kopyayı siler.</summary>
        bool Birak(string ozet, out long serbest)
        {
            serbest = 0;
            if (!baglanti.TryGetValue(ozet, out var n)) return false;
            if (n > 1) { baglanti[ozet] = n - 1; return false; }
            baglanti.Remove(ozet);
            nesneBoyutu.Remove(ozet, out serbest);
            TryDelete(NesneYolu(ozet));
            return true;
        }

        void Yukle()
        {
            if (!File.Exists(kayitYolu)) return;
            try
            {
                var veri = JsonSerializer.Deserialize<KayitDosyasi>(File.ReadAllText(kayitYolu));
                if (veri == null) return;
                foreach (var k in veri.Kopyalar)
                    if (File.Exists(NesneYolu(k.Ozet))) { canli[k.Yol] = k; Tut(k.Ozet, k.Boyut); }
                foreach (var s in veri.Silinenler)
                    if (File.Exists(NesneYolu(s.Ozet))) { s.Kesin = true; silinenler.Add(s); Tut(s.Ozet, s.Boyut); }
            }
            catch (Exception) { /* bozuk kayıt dosyası: baştan başla */ }

            // Kayıtta olmayan artık kopyaları temizle
            foreach (var dosya in Directory.EnumerateFiles(nesneler, "*", SearchOption.AllDirectories))
                if (!baglanti.ContainsKey(Path.GetFileName(dosya))) TryDelete(dosya);
        }

        public void Kaydet()
        {
            string json;
            lock (kilit)
            {
                json = JsonSerializer.Serialize(new KayitDosyasi { Kopyalar = canli.Values.ToList(), Silinenler = silinenler.ToList() });
                kirli = false;
                sonKayit = Saat();
            }
            var gec = kayitYolu + ".yeni";
            File.WriteAllText(gec, json);
            File.Move(gec, kayitYolu, overwrite: true);
        }

        static void TryDelete(string yol)
        {
            try { File.Delete(yol); } catch (Exception) { }
        }

        public void Dispose()
        {
            kapaniyor = true;
            uyandir.Set();
            isci.Join(3000);
            try { Kaydet(); } catch (Exception) { }
        }
    }
}
