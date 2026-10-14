using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BuzArsiv;

// Buz Arşiv çekirdek testleri: geçici bir klasörde gerçek dosyalar oluşturup siler,
// dizinin ve depomun doğru davrandığını denetler.

var kok = Path.Combine(Path.GetTempPath(), "buzarsiv-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
var izlenen = Path.Combine(kok, "Belgeler");
var depoKlasoru = Path.Combine(kok, "depo");
Directory.CreateDirectory(Path.Combine(izlenen, "Okul", "Tarih"));
Directory.CreateDirectory(Path.Combine(izlenen, "Tatil"));

int gecen = 0, kalan = 0;
void Dogrula(bool kosul, string ad)
{
    if (kosul) { gecen++; Console.WriteLine("  ✓ " + ad); }
    else { kalan++; Console.WriteLine("  ✗ " + ad); }
}
bool Bekle(Func<bool> kosul, int ms = 8000)
{
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < ms) { if (kosul()) return true; Thread.Sleep(50); }
    return kosul();
}
string Yaz(string goreli, string icerik)
{
    var yol = Path.Combine(izlenen, goreli);
    File.WriteAllText(yol, icerik, Encoding.UTF8);
    return yol;
}

Console.OutputEncoding = Encoding.UTF8;
try
{
    // ---- hazırlık ----
    var odev = Yaz(@"Okul\Tarih\Ödev Osmanlı Tarihi.txt", "ilk sürüm");
    Yaz(@"Okul\matematik notları.txt", "türev integral");
    Yaz(@"Tatil\kar tatili planı.txt", "kızak, çay, kitap");
    Yaz(@"Tatil\~$gecici.docx", "geçici");                    // atlanmalı
    var buyuk = Path.Combine(izlenen, "büyük video.bin");
    File.WriteAllBytes(buyuk, new byte[2 * 1024 * 1024]);       // sınırdan büyük
    Directory.CreateDirectory(Path.Combine(izlenen, "node_modules"));
    Yaz(@"node_modules\paket.js", "x");                         // atlanmalı

    using var dizin = new Dizin(new[] { izlenen });
    using var depo = new Depo(depoKlasoru)
    {
        EnBuyukDosya = 1024 * 1024,
        Sessizlik = TimeSpan.FromMilliseconds(300),
        SilmeBeklemesi = TimeSpan.FromSeconds(1.5),
    };
    dizin.DosyaDegisti += depo.DosyaDegisti;
    dizin.DosyaSilindi += depo.DosyaSilindi;
    dizin.DosyaTasindi += depo.DosyaTasindi;
    dizin.Baslat();

    Console.WriteLine("Tarama");
    Dogrula(Bekle(() => dizin.Hazir), "tarama bitti");
    Dogrula(dizin.Sayi == 4, $"4 dosya bulundu (bulunan: {dizin.Sayi}; geçici ve node_modules atlandı)");

    Console.WriteLine("Arama");
    Dogrula(dizin.Ara("odev").FirstOrDefault()?.Yol == odev, "\"odev\" → Ödev Osmanlı Tarihi.txt (Türkçe harf farkı yok sayıldı)");
    Dogrula(dizin.Ara("ÖDEV osmanli").Count == 1, "\"ÖDEV osmanli\" iki kelimeyle buldu");
    Dogrula(dizin.Ara("tarih ödev").Count == 1, "kelime sırası önemsiz");
    Dogrula(dizin.Ara("okul").Count == 2, "klasör adıyla da bulur (okul → 2 dosya)");
    Dogrula(dizin.Ara("plan").FirstOrDefault()?.Ad == "kar tatili planı.txt", "\"plan\" → kar tatili planı.txt");
    Dogrula(dizin.Ara("yokboyledosya").Count == 0, "olmayan şey boş döner");
    var mat = dizin.Ara("mat");
    Dogrula(mat.Count >= 1 && mat[0].Ad.StartsWith("matematik"), "adın başında geçen üstte");

    Console.WriteLine("İlk koruma");
    depo.IlkKorumayiBaslat(dizin.TumYollar());
    Dogrula(Bekle(() => depo.Durum().korunan == 3), $"küçük 3 dosya buzdolabında (korunan: {depo.Durum().korunan})");
    Dogrula(!depo.Korunuyor(buyuk), "sınırdan büyük dosya korunmadı");

    Console.WriteLine("Silme ve geri getirme");
    File.Delete(odev);
    Dogrula(Bekle(() => dizin.Ara("odev").Count == 0), "silinen dosya aramadan çıktı");
    Dogrula(Bekle(() => depo.Silinenler().Count == 1), "Silinenler'de göründü");
    var s = depo.Silinenler().Single();
    Dogrula(s.Ad == "Ödev Osmanlı Tarihi.txt", "doğru ad: " + s.Ad);
    var geri = depo.GeriGetir(s);
    Dogrula(geri == odev && File.ReadAllText(geri) == "ilk sürüm", "aynı yere, aynı içerikle geri geldi");
    Dogrula(depo.Silinenler().Count == 0, "geri gelince listeden çıktı");
    Dogrula(Bekle(() => dizin.Ara("odev").Count == 1), "geri gelen dosya aramada yine var");

    Console.WriteLine("Değiştirip silme: son hâli saklanır");
    Thread.Sleep(1100);
    File.WriteAllText(odev, "ikinci sürüm", Encoding.UTF8);
    Thread.Sleep(1000);
    File.Delete(odev);
    Dogrula(Bekle(() => depo.Silinenler().Count == 1), "silinen listede");
    var hedef2 = Path.Combine(kok, "geri.txt");
    depo.GeriGetir(depo.Silinenler()[0], hedef2);
    Dogrula(File.ReadAllText(hedef2) == "ikinci sürüm", "son hâli geri geldi (farklı yere kaydet ile)");

    Console.WriteLine("Taşıma ve ad değiştirme silme sayılmaz");
    var plan = Path.Combine(izlenen, @"Tatil\kar tatili planı.txt");
    var planYeni = Path.Combine(izlenen, @"Tatil\kış tatili planı.txt");
    File.Move(plan, planYeni);
    Dogrula(Bekle(() => dizin.Ara("kis tatili").Count == 1), "yeni adla bulunuyor");
    var notlar = Path.Combine(izlenen, @"Okul\matematik notları.txt");
    var notlarYeni = Path.Combine(izlenen, @"Tatil\matematik notları.txt");
    File.Move(notlar, notlarYeni);
    Thread.Sleep(4000);
    Dogrula(depo.Silinenler().Count == 0, "başka klasöre taşınan dosya Silinenler'e düşmedi");
    Dogrula(Bekle(() => depo.Korunuyor(notlarYeni) && depo.Korunuyor(planYeni)), "taşınan dosyalar yeni yerinde korunuyor");

    Console.WriteLine("Word gibi kaydetme (sil + aynı adla yeniden yaz)");
    var rapor = Yaz(@"Okul\rapor.txt", "taslak");
    Dogrula(Bekle(() => depo.Korunuyor(rapor)), "rapor korunuyor");
    var yedek = Path.Combine(izlenen, @"Okul\~WRL0001.tmp");
    File.Move(rapor, yedek);                                    // eski hâl geçici ada
    File.WriteAllText(rapor, "son hâl", Encoding.UTF8);         // yeni hâl asıl ada
    File.Delete(yedek);
    Thread.Sleep(3500);
    Dogrula(depo.Silinenler().Count == 0, "kaydetme silme sayılmadı");

    Console.WriteLine("Klasör silme");
    var fotolar = Path.Combine(izlenen, "Fotoğraflar");
    Directory.CreateDirectory(fotolar);
    File.WriteAllText(Path.Combine(fotolar, "a.txt"), "a");
    File.WriteAllText(Path.Combine(fotolar, "b.txt"), "b");
    Dogrula(Bekle(() => depo.Korunuyor(Path.Combine(fotolar, "b.txt"))), "klasördeki dosyalar korunuyor");
    Directory.Delete(fotolar, recursive: true);
    Dogrula(Bekle(() => depo.Silinenler().Count == 2), $"klasörle silinen 2 dosya listede ({depo.Silinenler().Count})");

    Console.WriteLine("Asıl yer doluysa yeni ad");
    var dolu = Yaz(@"Tatil\dolu.txt", "eski");
    Dogrula(Bekle(() => depo.Korunuyor(dolu)), "korunuyor");
    File.Delete(dolu);
    Dogrula(Bekle(() => depo.Silinenler().Any(x => x.Ad == "dolu.txt")), "silindi");
    File.WriteAllText(dolu, "yeni", Encoding.UTF8);
    Thread.Sleep(2500);
    var kayit = depo.Silinenler().FirstOrDefault(x => x.Ad == "dolu.txt");
    Dogrula(kayit != null, "aynı adla yeni dosya geldi ama eski silme (kesinleştikten sonra) duruyor");
    if (kayit != null)
    {
        var y = depo.GeriGetir(kayit);
        Dogrula(Path.GetFileName(y) == "dolu (geri getirildi).txt" && File.ReadAllText(y) == "eski" && File.ReadAllText(dolu) == "yeni",
            "üstüne yazmadı: " + Path.GetFileName(y));
    }

    Console.WriteLine("Kalıcı silme ve kalıcılık");
    var ilk = depo.Silinenler().First();
    depo.KaliciSil(ilk);
    var kalanSayi = depo.Silinenler().Count;
    depo.Kaydet();
    using (var ikinci = new Depo(Path.Combine(kok, "depo")))
    {
        Dogrula(ikinci.Silinenler().Count == kalanSayi, $"yeniden açınca {kalanSayi} silinen kayıt geri yüklendi");
        Dogrula(ikinci.Durum().korunan == depo.Durum().korunan, "korunan dosya sayısı da aynı");
    }

    Console.WriteLine("Alan sınırı");
    depo.DepoSiniri = 1;   // her şeyi zorla temizlesin
    Dogrula(Bekle(() => depo.Silinenler().Count == 0), "sınır aşılınca en eski silinenler atıldı");
    var nesneSayisi = Directory.EnumerateFiles(Path.Combine(depoKlasoru, "nesneler"), "*", SearchOption.AllDirectories).Count();
    Dogrula(Bekle(() => depo.Durum().bayt <= 1), $"depo boşaldı (kalan kopya dosyası: {nesneSayisi})");

    Console.WriteLine("Zaman çizelgesi");
    var son = dizin.Son(10);
    Dogrula(son.Count > 0 && son.Zip(son.Skip(1)).All(p => p.First.SonIslem >= p.Second.SonIslem), "yeniden eskiye sıralı");
    Dogrula(dizin.Son(10, Path.Combine(izlenen, "Tatil")).All(k => k.Klasor.EndsWith("Tatil")), "klasöre göre süzülüyor");
}
finally
{
    Console.WriteLine();
    Console.WriteLine($"{gecen} geçti, {kalan} kaldı");
    try { Directory.Delete(kok, true); } catch { }
}
return kalan == 0 ? 0 : 1;
