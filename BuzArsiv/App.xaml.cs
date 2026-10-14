using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using WF = System.Windows.Forms;

namespace BuzArsiv
{
    public partial class App : Application
    {
        const string KopyaAdi = "BuzArsiv_TekKopya";
        const string GosterAdi = "BuzArsiv_Goster";
        const int TusF = 0x46;

        public static App Mevcut => (App)Current;

        public Ayarlar Ayarlar { get; private set; } = null!;
        public Dizin Dizin { get; private set; } = null!;
        public Depo Depo { get; private set; } = null!;
        public AramaPenceresi Arama { get; private set; } = null!;
        public bool Kapaniyor { get; private set; }
        public string KisayolMetni { get; private set; } = "Ctrl + Shift + F";

        Mutex? tekKopya;
        EventWaitHandle? gosterSinyali;
        WF.NotifyIcon? tepsi;
        WF.ToolStripMenuItem? duraklatMenusu;
        KisayolTusu? kisayol;
        AnaPencere? ana;

        protected override void OnStartup(StartupEventArgs e)
        {
            var onizleme = Array.IndexOf(e.Args, "--onizleme");
            if (onizleme >= 0)
            {
                // README görsellerini örnek dosyalarla üretir; gerçek klasörlere dokunmaz.
                base.OnStartup(e);
                _ = Onizleme.Calistir(this, e.Args.ElementAtOrDefault(onizleme + 1) ?? "onizleme");
                return;
            }

            tekKopya = new Mutex(true, KopyaAdi, out bool yeni);
            if (!yeni)
            {
                // Zaten açık: var olan kopyaya "aramayı göster" de ve çık.
                try { EventWaitHandle.OpenExisting(GosterAdi).Set(); } catch { }
                Shutdown();
                return;
            }

            base.OnStartup(e);
            bool ilk = Ayarlar.IlkCalisma;
            Hazirla(Ayarlar.Yukle());
            if (ilk) Ayarlar.Kaydet();

            TepsiKur();
            kisayol = new KisayolTusu();
            kisayol.Basildi += () => Arama.AcKapa();
            bool tamam = kisayol.Kaydet(KisayolTusu.Ctrl | KisayolTusu.Shift, TusF);
            if (!tamam && kisayol.Kaydet(KisayolTusu.Ctrl | KisayolTusu.Alt | KisayolTusu.Shift, TusF))
            {
                tamam = true;
                KisayolMetni = "Ctrl + Alt + Shift + F";
            }
            if (tepsi != null) tepsi.Text = $"Buz Arşiv ({KisayolMetni})";
            GosterSinyaliniDinle();

            if (!tamam)
                Bildir("Kısayol kullanılamadı", "Ctrl + Shift + F başka bir programda. Aramayı bildirim alanındaki simgeden açabilirsin.");
            else if (ilk)
                Bildir("Buz Arşiv çalışıyor ❄", $"Bir dosyayı bulmak için {KisayolMetni} tuşlarına bas. Silinen dosyalar artık korunuyor.");

            if (!e.Args.Contains("--gizli")) Arama.Goster();
        }

        /// <summary>Ayarlara göre dizini, depoyu ve pencereleri kurar.</summary>
        internal void Hazirla(Ayarlar ayarlar)
        {
            Ayarlar = ayarlar;
            Depo = new Depo(Ayarlar.DepoKlasoru);
            Depo.Guncellendi += () => Dispatcher.InvokeAsync(() => ana?.DegisiklikVar());
            AyarlariUygula();
            DiziniKur();
            Arama = new AramaPenceresi();
        }

        void DiziniKur()
        {
            Dizin = new Dizin(Ayarlar.Klasorler);
            Dizin.DosyaDegisti += Depo.DosyaDegisti;
            Dizin.DosyaSilindi += Depo.DosyaSilindi;
            Dizin.DosyaTasindi += Depo.DosyaTasindi;
            Dizin.TaramaBitti += () => Depo.IlkKorumayiBaslat(Dizin.TumYollar());
            Dizin.Guncellendi += () => Dispatcher.InvokeAsync(() => ana?.DegisiklikVar());
            Dizin.Baslat();
        }

        /// <summary>İzlenen klasörler değişti: dizini baştan kur.</summary>
        public void KlasorleriYenile()
        {
            Ayarlar.Kaydet();
            Dizin.Dispose();
            DiziniKur();
        }

        public void AyarlariUygula()
        {
            Depo.EnBuyukDosya = Ayarlar.EnBuyukDosyaMB * 1024L * 1024;
            Depo.DepoSiniri = Ayarlar.DepoSiniriGB * 1024L * 1024 * 1024;
            Depo.SaklamaGun = Ayarlar.SaklamaGun;
            Depo.Duraklatildi = Ayarlar.KorumaDuraklatildi;
            if (duraklatMenusu != null) duraklatMenusu.Checked = Ayarlar.KorumaDuraklatildi;
            try { Ayarlar.Kaydet(); } catch (IOException) { }
        }

        public void AnaPencereyiAc(AnaPencere.Bolum bolum, string? arama = null)
        {
            ana ??= new AnaPencere();
            ana.Ac(bolum, arama);
        }

        void TepsiKur()
        {
            System.Drawing.Icon ikon;
            try
            {
                var akis = GetResourceStream(new Uri("pack://application:,,,/app.ico"))!.Stream;
                ikon = new System.Drawing.Icon(akis, WF.SystemInformation.SmallIconSize);
            }
            catch
            {
                ikon = System.Drawing.SystemIcons.Application;
            }

            var menu = new WF.ContextMenuStrip();
            var ara = new WF.ToolStripMenuItem("Ara") { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) };
            ara.Click += (_, _) => Arama.Goster();
            var zaman = new WF.ToolStripMenuItem("Zaman çizelgesi");
            zaman.Click += (_, _) => AnaPencereyiAc(AnaPencere.Bolum.Zaman);
            var silinen = new WF.ToolStripMenuItem("Silinenler");
            silinen.Click += (_, _) => AnaPencereyiAc(AnaPencere.Bolum.Silinenler);
            var ayar = new WF.ToolStripMenuItem("Ayarlar");
            ayar.Click += (_, _) => AnaPencereyiAc(AnaPencere.Bolum.Ayarlar);
            duraklatMenusu = new WF.ToolStripMenuItem("Korumayı duraklat") { CheckOnClick = true, Checked = Ayarlar.KorumaDuraklatildi };
            duraklatMenusu.CheckedChanged += (_, _) =>
            {
                if (Ayarlar.KorumaDuraklatildi == duraklatMenusu.Checked) return;
                Ayarlar.KorumaDuraklatildi = duraklatMenusu.Checked;
                AyarlariUygula();
            };
            var cikis = new WF.ToolStripMenuItem("Çıkış");
            cikis.Click += (_, _) => Kapat();

            menu.Items.AddRange(new WF.ToolStripItem[]
            {
                ara, zaman, silinen, ayar, new WF.ToolStripSeparator(), duraklatMenusu, new WF.ToolStripSeparator(), cikis
            });

            tepsi = new WF.NotifyIcon { Icon = ikon, Text = "Buz Arşiv", ContextMenuStrip = menu, Visible = true };
            tepsi.MouseClick += (_, a) =>
            {
                if (a.Button == WF.MouseButtons.Left) Arama.AcKapa();
            };
        }

        void GosterSinyaliniDinle()
        {
            gosterSinyali = new EventWaitHandle(false, EventResetMode.AutoReset, GosterAdi);
            var izleyici = new Thread(() =>
            {
                while (true)
                {
                    try { gosterSinyali.WaitOne(); }
                    catch { return; }
                    Dispatcher.InvokeAsync(() => Arama.Goster());
                }
            })
            { IsBackground = true, Name = "GosterSinyali" };
            izleyici.Start();
        }

        public void Bildir(string baslik, string metin)
        {
            if (tepsi == null) return;
            tepsi.BalloonTipTitle = baslik;
            tepsi.BalloonTipText = metin;
            tepsi.ShowBalloonTip(4000);
        }

        void Kapat()
        {
            Kapaniyor = true;
            if (tepsi != null)
            {
                tepsi.Visible = false;
                tepsi.Dispose();
            }
            kisayol?.Dispose();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Kapaniyor = true;
            if (tepsi != null) tepsi.Visible = false;
            Dizin?.Dispose();
            Depo?.Dispose();   // kayıtları diske yazar
            try { tekKopya?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }
    }
}
