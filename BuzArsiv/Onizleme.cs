using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BuzArsiv
{
    /// <summary>
    /// README görselleri: geçici bir klasörde örnek dosyalar oluşturur, pencereleri
    /// ekran dışında açıp PNG olarak kaydeder. Gerçek klasörlere ve ayarlara dokunmaz.
    /// Kullanım: BuzArsiv.exe --onizleme cikti-klasoru
    /// </summary>
    static class Onizleme
    {
        public static async Task Calistir(App app, string cikti)
        {
            var kok = Path.Combine(Path.GetTempPath(), "buzarsiv-onizleme-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            try
            {
                Directory.CreateDirectory(cikti);
                Ayarlar.VeriKlasoru = Path.Combine(kok, "veri");
                var simdi = DateTime.Now;
                var klasorler = new List<string>();
                string Dosya(string goreli, double gunOnce, int saat = 0)
                {
                    var yol = Path.Combine(kok, goreli);
                    Directory.CreateDirectory(Path.GetDirectoryName(yol)!);
                    File.WriteAllText(yol, "örnek " + goreli);
                    var t = simdi.Date.AddDays(-Math.Floor(gunOnce)).AddHours(saat == 0 ? simdi.Hour - 1 : saat).AddMinutes(gunOnce * 37 % 60);
                    if (t > simdi) t = simdi.AddMinutes(-gunOnce * 10 - 3);
                    File.SetCreationTime(yol, t);
                    File.SetLastWriteTime(yol, t);
                    return yol;
                }

                Dosya(@"Belgeler\Okul\Tarih\Ödev - Osmanlı Kuruluş Dönemi.docx", 0, 10);
                Dosya(@"İndirilenler\SonKis-v1.0-win64.zip", 0.1);
                Dosya(@"Masaüstü\ekran görüntüsü 2026-10-14.png", 0.2);
                Dosya(@"Belgeler\Okul\Matematik\Türev çalışma kağıdı.pdf", 0, 9);
                Dosya(@"İndirilenler\Ayazle.apk", 0, 8);
                Dosya(@"Belgeler\Okul\Fizik\Deney raporu taslak.docx", 1, 21);
                Dosya(@"Masaüstü\Kar tatili planı.txt", 1, 17);
                Dosya(@"İndirilenler\ders-programi-2026.pdf", 2, 13);
                Dosya(@"Belgeler\Okul\Tarih\Ödev kaynakça.txt", 3, 20);
                Dosya(@"Belgeler\Okul\Edebiyat\Ödev - Divan şiiri.docx", 5, 19);
                Dosya(@"Resimler\Kızak\IMG_2041.jpg", 6, 15);
                Dosya(@"Resimler\Nardugan\ateş başı.jpg", 8, 22);
                Dosya(@"Belgeler\Bütçe 2026.xlsx", 10, 11);
                Dosya(@"Belgeler\sunum - kış masalları.pptx", 12, 14);
                var silinecek = new[]
                {
                    Dosya(@"Belgeler\Okul\Tarih\Ödev - Kurtuluş Savaşı.docx", 2, 18),
                    Dosya(@"Resimler\aile fotoğrafı.jpg", 9, 12),
                    Dosya(@"Masaüstü\notlar.txt", 4, 9),
                };
                foreach (var k in new[] { "Belgeler", "İndirilenler", "Masaüstü", "Resimler" }) klasorler.Add(Path.Combine(kok, k));

                app.Hazirla(new Ayarlar { Klasorler = klasorler });
                app.Depo.Sessizlik = TimeSpan.FromMilliseconds(200);
                app.Depo.SilmeBeklemesi = TimeSpan.FromMilliseconds(500);
                while (!app.Dizin.Hazir || app.Depo.Durum().korunan < 17) await Task.Delay(100);
                foreach (var s in silinecek) File.Delete(s);
                while (app.Depo.Silinenler().Count < silinecek.Length) await Task.Delay(100);

                // Arama çubuğu
                app.Arama.Left = -20000;
                app.Arama.Goster("ödev");
                app.Arama.Left = -20000;
                await Task.Delay(700);
                Kaydet(app.Arama, Path.Combine(cikti, "arama.png"));
                app.Arama.Gizle();

                // Ana pencere: üç bölüm
                app.AnaPencereyiAc(AnaPencere.Bolum.Zaman);
                var ana = app.Windows.OfType<AnaPencere>().First();
                ana.Left = -20000;
                await Task.Delay(700);
                Kaydet(ana, Path.Combine(cikti, "zaman.png"));
                app.AnaPencereyiAc(AnaPencere.Bolum.Silinenler);
                await Task.Delay(500);
                Kaydet(ana, Path.Combine(cikti, "silinenler.png"));
                app.AnaPencereyiAc(AnaPencere.Bolum.Ayarlar);
                await Task.Delay(500);
                Kaydet(ana, Path.Combine(cikti, "ayarlar.png"));
            }
            finally
            {
                app.Dizin?.Dispose();
                app.Depo?.Dispose();
                try { Directory.Delete(kok, true); } catch { }
                app.Shutdown();
            }
        }

        /// <summary>Pencerenin içini (gölgesi ve saydamlığıyla) PNG olarak kaydeder.</summary>
        static void Kaydet(Window pencere, string yol)
        {
            // Saydam pencerede gölgeyle birlikte bütün pencere; normal pencerede çerçevesiz içerik
            var oge = pencere.AllowsTransparency ? (FrameworkElement)pencere : (FrameworkElement)pencere.Content;
            const double olcek = 1.5;
            var bmp = new RenderTargetBitmap((int)(oge.ActualWidth * olcek), (int)(oge.ActualHeight * olcek), 96 * olcek, 96 * olcek, PixelFormats.Pbgra32);
            bmp.Render(oge);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using var f = File.Create(yol);
            png.Save(f);
        }
    }
}
