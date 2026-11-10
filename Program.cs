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
        if (!created) return;
        Native.SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // Arranque automático com o Windows (--auto) começa desligado, para não interferir
        Application.Run(new TrayContext(!args.Contains("--auto")));
    }
}

// Janela pequena, sempre visível e que não rouba o foco, com o layout das teclas
sealed class LayoutForm : Form
{
    static readonly string[] Rows = { "qwertyuiop", "asdfghjkl", "zxcvbnm" };

    public LayoutForm()
    {
        Text = "ko-phonetic";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        BackColor = Color.White;
        ClientSize = new Size(540, 180);
        StartPosition = FormStartPosition.Manual;
        var wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
            return cp;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var fs = new Font("Segoe UI", 8f);
        using var fj = new Font("Malgun Gothic", 15f, FontStyle.Bold);
        using var fu = new Font("Malgun Gothic", 8f);
        using var pen = new Pen(Color.Silver);
        using var cap = new SolidBrush(Color.FromArgb(245, 246, 248));
        using var ink = new SolidBrush(Color.FromArgb(30, 36, 48));
        using var mute = new SolidBrush(Color.Gray);
        using var acc = new SolidBrush(Color.FromArgb(47, 75, 208));
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int r = 0; r < Rows.Length; r++)
        {
            for (int i = 0; i < Rows[r].Length; i++)
            {
                char ch = Rows[r][i];
                var rc = new Rectangle(8 + r * 26 + i * 52, 8 + r * 56, 48, 52);
                g.FillRectangle(cap, rc);
                g.DrawRectangle(pen, rc);
                g.DrawString(ch.ToString(), fs, mute, rc.X + 4, rc.Y + 2);
                g.DrawString(Engine.Jamo(ch), fj, ink, new Rectangle(rc.X, rc.Y + 8, rc.Width, rc.Height - 8), center);
                string up = Engine.Jamo(char.ToUpperInvariant(ch));
                if (up != "") g.DrawString(up, fu, acc, rc.Right - 16, rc.Y + 3);
            }
        }
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
    readonly LayoutForm layout = new();
    bool enabled;
    bool toggleHeld;

    public TrayContext(bool startOn)
    {
        enabled = startOn;
        eng.Emit = Native.Send;
        iconOn = MakeIcon(Color.FromArgb(47, 75, 208), "한");
        iconOff = MakeIcon(Color.FromArgb(110, 110, 110), "A");

        var menu = new ContextMenuStrip();
        miOn = new ToolStripMenuItem("Ativado (Scroll Lock ou tecla 한/영)");
        miOn.Click += (s, e) => Toggle();
        menu.Items.Add(miOn);

        var miLayout = new ToolStripMenuItem("Mostrar layout");
        miLayout.Click += (s, e) =>
        {
            if (layout.Visible) layout.Hide(); else layout.Show();
            miLayout.Checked = layout.Visible;
        };
        menu.Items.Add(miLayout);

        var miRun = new ToolStripMenuItem("Iniciar com o Windows") { Checked = IsAutorun() };
        miRun.Click += (s, e) => { SetAutorun(!miRun.Checked); miRun.Checked = IsAutorun(); };
        menu.Items.Add(miRun);

        menu.Items.Add(new ToolStripSeparator());
        var miExit = new ToolStripMenuItem("Sair");
        miExit.Click += (s, e) => Quit();
        menu.Items.Add(miExit);

        ni.ContextMenuStrip = menu;
        ni.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
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

        ni.ShowBalloonTip(3000, "ko-phonetic",
            enabled ? "Ativo. Scroll Lock ou clique no ícone para ligar/desligar."
                    : "Desligado. Scroll Lock ou clique no ícone para ativar.",
            ToolTipIcon.Info);
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
        ni.Text = enabled ? "ko-phonetic: ativo" : "ko-phonetic: desativado";
        miOn.Checked = enabled;
    }

    void Quit()
    {
        Native.UnhookWindowsHookEx(kbHook);
        Native.UnhookWindowsHookEx(msHook);
        ni.Visible = false;
        ni.Dispose();
        layout.Dispose();
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
        if (vk == 0x91 || vk == 0x15)   // Scroll Lock ou 한/영 ligam/desligam
        {
            if (down && !toggleHeld) { toggleHeld = true; Toggle(); }
            if (up) toggleHeld = false;
            return false;
        }

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
