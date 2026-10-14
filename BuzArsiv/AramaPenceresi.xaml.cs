using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace BuzArsiv
{
    /// <summary>Kısayolla açılan buzlu arama çubuğu.</summary>
    public partial class AramaPenceresi : Window
    {
        const int EnFazlaSonuc = 30;
        readonly DispatcherTimer gecikme = new() { Interval = TimeSpan.FromMilliseconds(70) };
        readonly DispatcherTimer taramaSaati = new() { Interval = TimeSpan.FromMilliseconds(800) };
        readonly List<(Ellipse e, double hiz, double salinim)> taneler = new();
        readonly Random zar = new();
        DateTime oncekiKare;
        int istekNo;

        public AramaPenceresi()
        {
            InitializeComponent();
            gecikme.Tick += (_, _) => { gecikme.Stop(); Yenile(); };
            // İlk tarama sürerken liste kendiliğinden dolsun
            taramaSaati.Tick += (_, _) => { if (App.Mevcut.Dizin.Hazir) taramaSaati.Stop(); Yenile(); };
            Kutu.TextChanged += (_, _) =>
            {
                Yer.Visibility = Kutu.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                gecikme.Stop();
                gecikme.Start();
            };
            PreviewKeyDown += Tus;
            Deactivated += (_, _) => Gizle();
            Liste.MouseDoubleClick += (_, _) => SeciliyiAc(klasor: false);
            Loaded += (_, _) => KarKur();
        }

        public void AcKapa()
        {
            if (IsVisible) Gizle(); else Goster();
        }

        public void Goster(string metin = "")
        {
            Konumla();
            Kutu.Text = metin;
            Kutu.CaretIndex = metin.Length;
            Yenile();
            if (!App.Mevcut.Dizin.Hazir) taramaSaati.Start();

            Opacity = 0;
            Show();
            Activate();
            Kutu.Focus();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            CompositionTarget.Rendering -= KarKaresi;
            CompositionTarget.Rendering += KarKaresi;
            oncekiKare = DateTime.Now;
        }

        public void Gizle()
        {
            if (!IsVisible) return;
            taramaSaati.Stop();
            CompositionTarget.Rendering -= KarKaresi;
            Hide();
        }

        /// <summary>Farenin olduğu ekranın üst-ortasına yerleştir.</summary>
        void Konumla()
        {
            var alan = WF.Screen.FromPoint(WF.Cursor.Position).WorkingArea;
            var dpi = VisualTreeHelper.GetDpi(this);
            double sol = alan.Left / dpi.DpiScaleX, ust = alan.Top / dpi.DpiScaleY;
            double en = alan.Width / dpi.DpiScaleX, boy = alan.Height / dpi.DpiScaleY;
            Left = sol + (en - Width) / 2;
            Top = ust + boy * 0.16;
        }

        async void Yenile()
        {
            var sorgu = Kutu.Text;
            var no = ++istekNo;
            var dizin = App.Mevcut.Dizin;
            bool bos = string.IsNullOrWhiteSpace(sorgu);

            var sonuc = await Task.Run(() => bos ? dizin.Son(8) : dizin.Ara(sorgu, EnFazlaSonuc));
            if (no != istekNo) return;   // bu arada yeni harf yazıldı

            var simdi = DateTime.Now;
            var ayarlar = App.Mevcut.Ayarlar;
            Liste.ItemsSource = sonuc.Select(k => new DosyaSatiri(k, ayarlar, simdi)).ToList();
            if (sonuc.Count > 0) Liste.SelectedIndex = 0;

            Baslik.Text = bos ? "SON KAYDETTİKLERİN"
                : sonuc.Count == 0 ? ""
                : sonuc.Count >= EnFazlaSonuc ? $"İLK {EnFazlaSonuc} SONUÇ" : $"{sonuc.Count} SONUÇ";
            Baslik.Visibility = Baslik.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (sonuc.Count == 0)
            {
                Bos.Text = !dizin.Hazir ? $"Dosyaların taranıyor… şimdiye kadar {dizin.Sayi.ToString("N0", Metin.Tr)} dosya."
                    : bos ? "Son bir ayda kaydedilen dosya yok."
                    : $"\"{sorgu.Trim()}\" bulunamadı. Adının başka bir parçasını dene ya da silindiyse Silinenler'e bak.";
                Bos.Visibility = Visibility.Visible;
            }
            else Bos.Visibility = Visibility.Collapsed;
        }

        void Tus(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Gizle();
                    e.Handled = true;
                    break;
                case Key.Down:
                case Key.Up:
                    if (Liste.Items.Count == 0) break;
                    var yeni = Liste.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
                    Liste.SelectedIndex = Math.Clamp(yeni, 0, Liste.Items.Count - 1);
                    Liste.ScrollIntoView(Liste.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    SeciliyiAc(klasor: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                    e.Handled = true;
                    break;
            }
        }

        DosyaSatiri? Secili => Liste.SelectedItem as DosyaSatiri;

        void SeciliyiAc(bool klasor)
        {
            if (Secili is not { } s) return;
            Gizle();
            if (klasor) Kabuk.KlasorunuAc(s.Kayit.Yol); else Kabuk.Ac(s.Kayit.Yol);
        }

        void MenuAc(object sender, RoutedEventArgs e) => SeciliyiAc(klasor: false);
        void MenuKlasor(object sender, RoutedEventArgs e) => SeciliyiAc(klasor: true);
        void MenuKopyala(object sender, RoutedEventArgs e)
        {
            if (Secili is { } s) Clipboard.SetText(s.Kayit.Yol);
        }

        void ZamanTiklandi(object sender, RoutedEventArgs e) { Gizle(); App.Mevcut.AnaPencereyiAc(AnaPencere.Bolum.Zaman); }
        void SilinenlerTiklandi(object sender, RoutedEventArgs e) { Gizle(); App.Mevcut.AnaPencereyiAc(AnaPencere.Bolum.Silinenler, Kutu.Text); }

        // ---- çubuğun içinde usulca yağan kar ----

        void KarKur()
        {
            for (int i = 0; i < 26; i++)
            {
                double r = 1 + zar.NextDouble() * 2.2;
                var e = new Ellipse { Width = r, Height = r, Fill = Brushes.White, Opacity = 0.15 + zar.NextDouble() * 0.35 };
                Canvas.SetLeft(e, zar.NextDouble() * 720);
                Canvas.SetTop(e, zar.NextDouble() * 500);
                Kar.Children.Add(e);
                taneler.Add((e, 8 + zar.NextDouble() * 14, zar.NextDouble() * 6.28));
            }
        }

        void KarKaresi(object? sender, EventArgs e)
        {
            var simdi = DateTime.Now;
            double dt = Math.Min((simdi - oncekiKare).TotalSeconds, 0.05);
            oncekiKare = simdi;
            double boy = Kar.ActualHeight, en = Kar.ActualWidth;
            if (boy <= 0) return;
            for (int i = 0; i < taneler.Count; i++)
            {
                var (t, hiz, faz) = taneler[i];
                faz += dt;
                double y = Canvas.GetTop(t) + hiz * dt;
                double x = Canvas.GetLeft(t) + Math.Sin(faz) * 6 * dt;
                if (y > boy) { y = -4; x = zar.NextDouble() * en; }
                Canvas.SetTop(t, y);
                Canvas.SetLeft(t, x);
                taneler[i] = (t, hiz, faz);
            }
        }
    }
}
