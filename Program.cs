using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KoPhonetic;

static class Native
{
    public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    static INPUT Key(ushort vk, ushort scan, uint flags) => new INPUT
    {
        type = 1,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } }
    };

    // Apaga 'bs' caracteres e escreve 'add' (Unicode, independente do layout do teclado)
    public static void Send(int bs, string add)
    {
        var list = new List<INPUT>();
        for (int i = 0; i < bs; i++)
        {
            list.Add(Key(0x08, 0, 0));
            list.Add(Key(0x08, 0, 2));
        }
        foreach (char c in add)
        {
            list.Add(Key(0, c, 0x4));          // KEYEVENTF_UNICODE
            list.Add(Key(0, c, 0x4 | 0x2));    // + KEYUP
        }
        if (list.Count > 0)
            SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>());
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "KoPhonetic-Windows-Single", out bool created);
        var showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "KoPhonetic-Windows-Show");
        if (!created) { showEvt.Set(); return; }
        Native.SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // Arranque automático com o Windows (--auto) começa desligado, para não interferir
        bool auto = args.Contains("--auto");
        Application.Run(new TrayContext(!auto, !auto, showEvt));
    }
}

// Desenha as teclas do layout (a azul, o que sai com Shift) e acende a tecla premida
sealed class LayoutPanel : Control
{
    static readonly string[] Rows = { "qwertyuiop", "asdfghjkl", "zxcvbnm" };
    readonly HashSet<char> lit = new HashSet<char>();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 160 };

    public LayoutPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.White;
        timer.Tick += (s, e) => { timer.Stop(); lit.Clear(); Invalidate(); };
    }

    public void Flash(char c)
    {
        lit.Add(char.ToLowerInvariant(c));
        Invalidate();
        timer.Stop();
        timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        float kw = Math.Min(64f, (Width - 16f) / 10f);
        float kh = Math.Min(kw * 1.1f, (Height - 16f) / 3f);
        if (kw < 10f || kh < 10f) return;
        float x0 = (Width - 10 * kw) / 2f, y0 = 8f;
        using var fs = new Font("Segoe UI", kh * 0.18f, GraphicsUnit.Pixel);
        using var fj = new Font("Malgun Gothic", kh * 0.36f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var fu = new Font("Malgun Gothic", kh * 0.18f, GraphicsUnit.Pixel);
        using var pen = new Pen(Color.Silver);
        using var cap = new SolidBrush(Color.FromArgb(245, 246, 248));
        using var hot = new SolidBrush(Color.FromArgb(207, 216, 255));
        using var ink = new SolidBrush(Color.FromArgb(30, 36, 48));
        using var mute = new SolidBrush(Color.Gray);
        using var acc = new SolidBrush(Color.FromArgb(47, 75, 208));
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int r = 0; r < Rows.Length; r++)
        {
            for (int i = 0; i < Rows[r].Length; i++)
            {
                char ch = Rows[r][i];
                var rc = new RectangleF(x0 + r * 0.5f * kw + i * kw + 2, y0 + r * kh + 2, kw - 4, kh - 4);
                g.FillRectangle(lit.Contains(ch) ? hot : cap, rc);
                g.DrawRectangle(pen, rc.X, rc.Y, rc.Width, rc.Height);
                g.DrawString(ch.ToString(), fs, mute, rc.X + 4, rc.Y + 2);
                g.DrawString(Engine.Jamo(ch), fj, ink,
                    new RectangleF(rc.X, rc.Y + kh * 0.12f, rc.Width, rc.Height - kh * 0.12f), center);
                string up = Engine.Jamo(char.ToUpperInvariant(ch));
                if (up != "") g.DrawString(up, fu, acc, rc.Right - kh * 0.30f, rc.Y + 2);
            }
        }
        float capY = y0 + 3 * kh + 2;
        if (Height - capY > 16f)
            g.DrawString("Azul = com Shift. A tecla premida acende.", fs, mute, x0, capY);
    }
}

// Janela aberta ao clicar no ícone: área para testar a escrita e, por baixo, as teclas
sealed class MainForm : Form
{
    readonly Label mode = new Label();
    readonly TextBox box = new TextBox();
    readonly LayoutPanel keys = new LayoutPanel();

    public MainForm()
    {
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "ko-phonetic";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(600, 440);
        MinimumSize = new Size(420, 340);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        box.Multiline = true;
        box.Dock = DockStyle.Fill;
        box.ScrollBars = ScrollBars.Vertical;
        box.AcceptsReturn = true;
        box.Font = new Font("Malgun Gothic", 18f);

        keys.Dock = DockStyle.Bottom;
        keys.Height = 200;

        mode.Dock = DockStyle.Top;
        mode.Height = 40;
        mode.TextAlign = ContentAlignment.MiddleLeft;
        mode.Padding = new Padding(12, 0, 0, 0);
        mode.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        mode.ForeColor = Color.White;

        Controls.Add(box);
        Controls.Add(keys);
        Controls.Add(mode);
    }

    public void SetMode(bool hangul)
    {
        mode.Text = hangul ? "한  Hangul  —  toque em AltGr para voltar ao Latin"
                           : "A  Latin  —  toque em AltGr para escrever em Hangul";
        mode.BackColor = hangul ? Color.FromArgb(47, 75, 208) : Color.FromArgb(110, 110, 110);
    }

    public void FlashKey(char c)
    {
        if (Visible) keys.Flash(c);
    }

    public void Bring()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        TopMost = true;
        TopMost = false;
        Activate();
        box.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }
}

sealed class TrayContext : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "KoPhoneticWin";

    readonly Engine eng = new();
    readonly NotifyIcon ni = new();
    readonly Icon iconOn, iconOff;
    readonly Native.HookProc kbProc, msProc;
    readonly IntPtr kbHook, msHook;
    readonly HashSet<uint> swallowed = new();
    readonly ToolStripMenuItem miOn;
    readonly MainForm main = new MainForm();
    readonly Control inv = new Control();
    readonly EventWaitHandle showEvt;
    bool enabled;
    bool altgrHeld, altgrTap;   // AltGr = Alt direito

    public TrayContext(bool startOn, bool showWindow, EventWaitHandle showEvt)
    {
        enabled = startOn;
        this.showEvt = showEvt;
        eng.Emit = Native.Send;
        iconOn = MakeIcon(Color.FromArgb(47, 75, 208), "한");
        iconOff = MakeIcon(Color.FromArgb(110, 110, 110), "A");

        var menu = new ContextMenuStrip();
        miOn = new ToolStripMenuItem("Escrever em Hangul (toque em AltGr)");
        miOn.Click += (s, e) => Toggle();
        menu.Items.Add(miOn);

        var miWin = new ToolStripMenuItem("Abrir janela de teste") { Font = new Font(SystemFonts.MenuFont, FontStyle.Bold) };
        miWin.Click += (s, e) => ShowMain();
        menu.Items.Add(miWin);

        var miRun = new ToolStripMenuItem("Iniciar com o Windows") { Checked = IsAutorun() };
        miRun.Click += (s, e) => { SetAutorun(!miRun.Checked); miRun.Checked = IsAutorun(); };
        menu.Items.Add(miRun);

        menu.Items.Add(new ToolStripSeparator());
        var miExit = new ToolStripMenuItem("Sair");
        miExit.Click += (s, e) => Quit();
        menu.Items.Add(miExit);

        ni.ContextMenuStrip = menu;
        ni.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
        ni.DoubleClick += (s, e) => ShowMain();
        UpdateUi();
        ni.Visible = true;

        kbProc = KbProc;
        msProc = MsProc;
        IntPtr mod = Native.GetModuleHandle(null);
        kbHook = Native.SetWindowsHookEx(13, kbProc, mod, 0);   // WH_KEYBOARD_LL
        msHook = Native.SetWindowsHookEx(14, msProc, mod, 0);   // WH_MOUSE_LL
        if (kbHook == IntPtr.Zero)
            MessageBox.Show("Não foi possível instalar o hook de teclado.", "ko-phonetic",
                MessageBoxButtons.OK, MessageBoxIcon.Error);

        if (!showWindow)
        ni.ShowBalloonTip(3000, "ko-phonetic",
            enabled ? "Hangul ativo. Toque em AltGr para voltar ao Latin."
                    : "Modo Latin. Toque em AltGr para escrever em Hangul.",
            ToolTipIcon.Info);

        var h = inv.Handle;   // cria o handle (necessário para BeginInvoke a partir de outra thread)
        ThreadPool.RegisterWaitForSingleObject(showEvt,
            (st, timedOut) => inv.BeginInvoke(new Action(ShowMain)), null, -1, false);
        if (showWindow) ShowMain();
    }

    void ShowMain()
    {
        main.Bring();
    }

    void Toggle()
    {
        enabled = !enabled;
        eng.Reset();
        UpdateUi();
    }

    void UpdateUi()
    {
        ni.Icon = enabled ? iconOn : iconOff;
        ni.Text = enabled ? "ko-phonetic: Hangul (AltGr alterna)" : "ko-phonetic: Latin (AltGr alterna)";
        miOn.Checked = enabled;
        main.SetMode(enabled);
    }

    void Quit()
    {
        Native.UnhookWindowsHookEx(kbHook);
        Native.UnhookWindowsHookEx(msHook);
        ni.Visible = false;
        ni.Dispose();
        main.Dispose();
        inv.Dispose();
        ExitThread();
    }

    static bool IsModifier(uint vk) =>
        (vk >= 0x10 && vk <= 0x12) || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C ||
        vk == 0x14 || vk == 0x90 || vk == 0x91 || vk == 0x15;

    static bool Held(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    IntPtr KbProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            if ((k.flags & 0x10) == 0)   // ignorar teclas injetadas (as nossas incluídas)
            {
                int w = wParam.ToInt32();
                bool down = w == 0x100 || w == 0x104;
                bool up = w == 0x101 || w == 0x105;
                if (HandleKey(k.vkCode, down, up)) return (IntPtr)1;
            }
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    bool HandleKey(uint vk, bool down, bool up)
    {
        if (vk == 0xA5)   // AltGr (Alt direito): um toque sozinho alterna Latin <-> Hangul
        {
            if (down) { if (!altgrHeld) { altgrHeld = true; altgrTap = true; } }
            else if (up)
            {
                if (altgrHeld && altgrTap) Toggle();
                altgrHeld = false; altgrTap = false;
            }
            return false;   // a tecla passa sempre, para não estragar AltGr+tecla (@, €, etc.)
        }
        if (down && altgrHeld && !IsModifier(vk)) altgrTap = false;   // AltGr usado em combinação

        if (swallowed.Contains(vk))
        {
            if (up) { swallowed.Remove(vk); return true; }
            if (down && vk == 0x08)   // repetição do Backspace ao manter premido
            {
                if (!eng.Back()) { swallowed.Remove(vk); return false; }
            }
            return true;
        }

        if (!enabled || !down) return false;
        if (IsModifier(vk)) return false;
        if (Held(0x11) || Held(0x12) || Held(0x5B) || Held(0x5C)) { eng.Reset(); return false; }

        if (vk >= 0x41 && vk <= 0x5A)   // A..Z
        {
            // maiúscula = Shift XOR Caps Lock; sem letra especial dá a letra normal
            bool upper = Held(0x10) ^ Control.IsKeyLocked(Keys.CapsLock);
            swallowed.Add(vk);
            eng.Type((char)vk, upper);
            main.FlashKey((char)vk);
            return true;
        }
        if (vk == 0x08)   // Backspace: apaga um jamo de cada vez
        {
            if (eng.Back()) { swallowed.Add(vk); return true; }
            return false;
        }
        if (vk == 0x20) { eng.Space(); return false; }   // o espaço real passa
        eng.Reset();                                      // outras teclas encerram a composição
        return false;
    }

    IntPtr MsProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int w = wParam.ToInt32();
            if (w == 0x201 || w == 0x204 || w == 0x207) eng.Reset();   // clique
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    static Icon MakeIcon(Color bg, string txt)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using (var br = new SolidBrush(bg)) g.FillEllipse(br, 1, 1, 30, 30);
            using var f = new Font("Malgun Gothic", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
            var sz = g.MeasureString(txt, f);
            g.DrawString(txt, f, Brushes.White, (32 - sz.Width) / 2f, (32 - sz.Height) / 2f);
        }
        IntPtr h = bmp.GetHicon();
        using var tmp = Icon.FromHandle(h);
        var ic = (Icon)tmp.Clone();
        Native.DestroyIcon(h);
        return ic;
    }

    static bool IsAutorun()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunName) != null;
    }

    static void SetAutorun(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (k == null) return;
        if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --auto");
        else k.DeleteValue(RunName, false);
    }
}
