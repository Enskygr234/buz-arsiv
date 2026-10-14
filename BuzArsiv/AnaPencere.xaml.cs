using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace BuzArsiv
{
    /// <summary>Zaman çizelgesi, Silinenler ve Ayarlar.</summary>
    public partial class AnaPencere : Window
    {
        public enum Bolum { Zaman, Silinenler, Ayarlar }

        static readonly int[] BoyutSecenekleri = { 10, 25, 50, 100, 250 };
        static readonly int[] AlanSecenekleri = { 1, 2, 5, 10, 20 };
        static readonly int[] GunSecenekleri = { 7, 14, 30, 60, 90 };

        readonly DispatcherTimer yenileme = new() { Interval = TimeSpan.FromSeconds(1) };
        string? seciliKok;
        bool kuruluyor;

        public AnaPencere()
        {
            InitializeComponent();
            yenileme.Tick += (_, _) => { yenileme.Stop(); Yenile(); };
            SilinenAra.TextChanged += (_, _) =>
            {
                SilinenAraYer.Visibility = SilinenAra.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                SilinenleriYenile();
            };
            ZamanListesi.MouseDoubleClick += (_, _) => { if (ZamanListesi.SelectedItem is DosyaSatiri s) Kabuk.Ac(s.Kayit.Yol); };
            SourceInitialized += (_, _) => KoyuBaslik();
            Duraklat.Click += (_, _) => { App.Mevcut.Ayarlar.KorumaDuraklatildi = Duraklat.IsChecked == true; App.Mevcut.AyarlariUygula(); DurumYenile(); };
            Baslangic.Click += (_, _) =>
            {
                try { Ayarlar.WindowsIleBasliyor = Baslangic.IsChecked == true; }
                catch (Exception) { Baslangic.IsChecked = Ayarlar.WindowsIleBasliyor; }
            };
        }

        /// <summary>Kapatınca kapanmaz, gizlenir: program bildirim alanında çalışmaya devam eder.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!App.Mevcut.Kapaniyor)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }

        public void Ac(Bolum bolum, string? arama = null)
        {
            if (arama != null) SilinenAra.Text = arama.Trim();
            (bolum switch { Bolum.Silinenler => SekmeSilinen, Bolum.Ayarlar => SekmeAyar, _ => SekmeZaman }).IsChecked = true;
            Yenile();
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        /// <summary>Dizin ya da depo değişti: görünürsek kısa bir gecikmeyle yenile (art arda gelenleri topla).</summary>
        public void DegisiklikVar()
        {
            if (!IsVisible) return;
            yenileme.Stop();
            yenileme.Start();
        }

        void SekmeDegisti(object sender, RoutedEventArgs e)
        {
            PanelZaman.Visibility = SekmeZaman.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelSilinen.Visibility = SekmeSilinen.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelAyar.Visibility = SekmeAyar.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            Yenile();
        }

        void Yenile()
        {
            DurumYenile();
            if (SekmeZaman.IsChecked == true) ZamaniYenile();
            else if (SekmeSilinen.IsChecked == true) SilinenleriYenile();
            else if (SekmeAyar.IsChecked == true) AyarlariGoster();
        }

        void DurumYenile()
        {
            var (bayt, korunan, _, bekleyen) = App.Mevcut.Depo.Durum();
            var ayar = App.Mevcut.Ayarlar;
            DurumKoruma.Text = ayar.KorumaDuraklatildi ? "⏸ Koruma duraklatıldı"
                : $"🛡 {korunan.ToString("N0", Metin.Tr)} dosya korunuyor" + (bekleyen > 0 ? $"\n   {bekleyen.ToString("N0", Metin.Tr)} dosya sırada" : "");
            DurumAlan.Text = $"🧊 {Metin.Boyut(bayt)} / {ayar.DepoSiniriGB} GB";
        }

        // ---- Zaman çizelgesi ----

        void CipleriKur()
        {
            Cipler.Children.Clear();
            void Ekle(string ad, string? kok)
            {
                var c = new RadioButton { Style = (Style)FindResource("Cip"), Content = ad, GroupName = "kok", IsChecked = string.Equals(kok, seciliKok, StringComparison.OrdinalIgnoreCase) };
                c.Checked += (_, _) => { seciliKok = kok; ZamaniYenile(); };
                Cipler.Children.Add(c);
            }
            Ekle("Hepsi", null);
            foreach (var k in App.Mevcut.Dizin.Klasorler) Ekle(Ayarlar.Etiket(k), k);
        }

        void ZamaniYenile()
        {
            if (Cipler.Children.Count != App.Mevcut.Dizin.Klasorler.Count + 1) CipleriKur();
            var simdi = DateTime.Now;
            var satirlar = App.Mevcut.Dizin.Son(400, seciliKok)
                .Select(k => new DosyaSatiri(k, App.Mevcut.Ayarlar, simdi)).ToList();
            var gorunum = new ListCollectionView(satirlar);
            gorunum.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DosyaSatiri.Grup)));
            ZamanListesi.ItemsSource = gorunum;
            ZamanBos.Visibility = satirlar.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (satirlar.Count == 0 && !App.Mevcut.Dizin.Hazir) ZamanBos.Text = "Dosyaların taranıyor…";
            else ZamanBos.Text = "Son bir ayda burada kaydedilen dosya yok.";
        }

        DosyaSatiri? ZamanSecili => ZamanListesi.SelectedItem as DosyaSatiri;
        void ZamanAc(object sender, RoutedEventArgs e) { if (ZamanSecili is { } s) Kabuk.Ac(s.Kayit.Yol); }
        void ZamanKlasor(object sender, RoutedEventArgs e) { if (ZamanSecili is { } s) Kabuk.KlasorunuAc(s.Kayit.Yol); }
        void ZamanKopyala(object sender, RoutedEventArgs e) { if (ZamanSecili is { } s) Clipboard.SetText(s.Kayit.Yol); }

        // ---- Silinenler ----

        void SilinenleriYenile()
        {
            var ayar = App.Mevcut.Ayarlar;
            SilinenAciklama.Text = $"İzlenen klasörlerden silinen dosyalar {ayar.SaklamaGun} gün burada bekler. " +
                                   "Çöp kutusunu boşaltsan bile buradan geri getirebilirsin.";
            var simdi = DateTime.Now;
            var kelimeler = Metin.Katla(SilinenAra.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var satirlar = App.Mevcut.Depo.Silinenler()
                .Where(s => kelimeler.All(k => Metin.Katla(s.Yol).Contains(k)))
                .Select(s => new SilinenSatiri(s, ayar, simdi)).ToList();
            SilinenListesi.ItemsSource = satirlar;
            SilinenBos.Visibility = satirlar.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SilinenBosAciklama.Text = kelimeler.Length > 0
                ? "Aradığın adla silinen bir dosya yok. Buz Arşiv yalnızca kurulduktan sonra silinen dosyaları saklayabilir."
                : $"İzlenen klasörlerden bir dosya silindiğinde son hâli burada görünür ve {ayar.SaklamaGun} gün saklanır. " +
                  $"{ayar.EnBuyukDosyaMB} MB'tan büyük dosyalar saklanmaz.";
        }

        static SilinenSatiri? Satir(object sender) => (sender as FrameworkElement)?.Tag as SilinenSatiri;

        void GeriGetir(object sender, RoutedEventArgs e)
        {
            if (Satir(sender) is not { } s) return;
            try
            {
                var yol = App.Mevcut.Depo.GeriGetir(s.Kayit);
                App.Mevcut.Bildir("Geri getirildi ❄", Path.GetFileName(yol));
                Kabuk.KlasorunuAc(yol);
            }
            catch (Exception ex) { MessageBox.Show(this, "Geri getirilemedi:\n" + ex.Message, "Buz Arşiv", MessageBoxButton.OK, MessageBoxImage.Warning); }
            SilinenleriYenile();
        }

        void FarkliKaydet(object sender, RoutedEventArgs e)
        {
            if (Satir(sender) is not { } s) return;
            var d = new SaveFileDialog
            {
                FileName = s.Ad,
                InitialDirectory = Directory.Exists(s.Kayit.Klasor) ? s.Kayit.Klasor : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Filter = "Bütün dosyalar|*.*",
                OverwritePrompt = true,
            };
            if (d.ShowDialog(this) != true) return;
            try
            {
                if (File.Exists(d.FileName)) File.Delete(d.FileName);   // üstüne yazmayı kullanıcı onayladı
                var yol = App.Mevcut.Depo.GeriGetir(s.Kayit, d.FileName);
                Kabuk.KlasorunuAc(yol);
            }
            catch (Exception ex) { MessageBox.Show(this, "Kaydedilemedi:\n" + ex.Message, "Buz Arşiv", MessageBoxButton.OK, MessageBoxImage.Warning); }
            SilinenleriYenile();
        }

        void KaliciSil(object sender, RoutedEventArgs e)
        {
            if (Satir(sender) is not { } s) return;
            var cevap = MessageBox.Show(this,
                $"\"{s.Ad}\" Buz Arşiv'den kalıcı olarak silinsin mi?\nBundan sonra geri getirilemez.",
                "Buz Arşiv", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (cevap != MessageBoxResult.Yes) return;
            App.Mevcut.Depo.KaliciSil(s.Kayit);
            SilinenleriYenile();
        }

        // ---- Ayarlar ----

        void AyarlariGoster()
        {
            kuruluyor = true;
            var a = App.Mevcut.Ayarlar;
            KlasorListesi.ItemsSource = a.Klasorler.Select(k => new { Etiket = Ayarlar.Etiket(k), Yol = k }).ToList();
            SecimKur(SecimBoyut, BoyutSecenekleri, a.EnBuyukDosyaMB, v => v + " MB", v => a.EnBuyukDosyaMB = v);
            SecimKur(SecimAlan, AlanSecenekleri, a.DepoSiniriGB, v => v + " GB", v => a.DepoSiniriGB = v);
            SecimKur(SecimGun, GunSecenekleri, a.SaklamaGun, v => v + " gün", v => a.SaklamaGun = v);
            Duraklat.IsChecked = a.KorumaDuraklatildi;
            try { Baslangic.IsChecked = Ayarlar.WindowsIleBasliyor; } catch (Exception) { }
            KisayolYazisi.Text = App.Mevcut.KisayolMetni;
            kuruluyor = false;
        }

        void SecimKur(WrapPanel panel, int[] secenekler, int secili, Func<int, string> yazi, Action<int> uygula)
        {
            panel.Children.Clear();
            var grup = Guid.NewGuid().ToString();
            foreach (var v in secenekler)
            {
                var c = new RadioButton { Style = (Style)FindResource("Cip"), Content = yazi(v), GroupName = grup, IsChecked = v == secili };
                c.Checked += (_, _) =>
                {
                    if (kuruluyor) return;
                    uygula(v);
                    App.Mevcut.AyarlariUygula();
                    DurumYenile();
                };
                panel.Children.Add(c);
            }
        }

        void KlasorEkle(object sender, RoutedEventArgs e)
        {
            using var d = new WF.FolderBrowserDialog { Description = "Buz Arşiv'in izleyeceği klasörü seç", UseDescriptionForTitle = true };
            if (d.ShowDialog() != WF.DialogResult.OK) return;
            var yol = d.SelectedPath.TrimEnd('\\');
            var a = App.Mevcut.Ayarlar;
            if (a.Klasorler.Any(k => yol.StartsWith(k.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || string.Equals(k, yol, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "Bu klasör zaten izleniyor (ya da izlenen bir klasörün içinde).", "Buz Arşiv");
                return;
            }
            // Yeni klasör eskilerden birini kapsıyorsa eskileri çıkar
            a.Klasorler.RemoveAll(k => k.StartsWith(yol + "\\", StringComparison.OrdinalIgnoreCase));
            a.Klasorler.Add(yol);
            App.Mevcut.KlasorleriYenile();
            seciliKok = null;
            AyarlariGoster();
        }

        void KlasorCikar(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string yol) return;
            var a = App.Mevcut.Ayarlar;
            if (a.Klasorler.Count == 1)
            {
                MessageBox.Show(this, "En az bir klasör izlenmeli.", "Buz Arşiv");
                return;
            }
            a.Klasorler.RemoveAll(k => string.Equals(k, yol, StringComparison.OrdinalIgnoreCase));
            App.Mevcut.KlasorleriYenile();
            seciliKok = null;
            AyarlariGoster();
        }

        void DepoKlasorunuAc(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(Ayarlar.DepoKlasoru);
            Process.Start(new ProcessStartInfo(Ayarlar.DepoKlasoru) { UseShellExecute = true });
        }

        void AramaTiklandi(object sender, RoutedEventArgs e) => App.Mevcut.Arama.Goster();

        // ---- koyu başlık çubuğu (Windows 10 20H1+ / 11) ----

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void KoyuBaslik()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int evet = 1;
            DwmSetWindowAttribute(hwnd, 20, ref evet, sizeof(int));
        }
    }
}
