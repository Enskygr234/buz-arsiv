using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BuzArsiv
{
    /// <summary>Sistem genelinde çalışan kısayol tuşu (RegisterHotKey).</summary>
    public sealed class KisayolTusu : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4;
        const int WM_HOTKEY = 0x0312;
        const int Kimlik = 0xB42A;
        const uint MOD_NOREPEAT = 0x4000;

        readonly HwndSource kaynak;
        bool kayitli;

        public event Action? Basildi;

        public KisayolTusu()
        {
            // Görünmez, yalnızca mesaj alan pencere.
            var p = new HwndSourceParameters("BuzArsivKisayol")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ParentWindow = new IntPtr(-3) // HWND_MESSAGE
            };
            kaynak = new HwndSource(p);
            kaynak.AddHook(Kanca);
        }

        public bool Kaydet(uint degistiriciler, int tus)
        {
            Birak();
            kayitli = RegisterHotKey(kaynak.Handle, Kimlik, degistiriciler | MOD_NOREPEAT, (uint)tus);
            return kayitli;
        }

        void Birak()
        {
            if (!kayitli) return;
            UnregisterHotKey(kaynak.Handle, Kimlik);
            kayitli = false;
        }

        IntPtr Kanca(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == Kimlik)
            {
                Basildi?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Birak();
            kaynak.Dispose();
        }
    }
}
