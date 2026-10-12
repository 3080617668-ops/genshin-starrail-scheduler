using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Program
{
    static readonly Guid Context = Guid.NewGuid();
    static readonly List<AudioState> Audio = new List<AudioState>();
    static readonly List<Form> Covers = new List<Form>();
    static IntPtr Hook;
    static HookProc Callback = OnKey;
    static Form MainForm;
    static bool Closing;
    static bool GameMode;

    [STAThread]
    static void Main(string[] args)
    {
        bool test = args.Length == 2 && args[0] == "--self-test";
        bool captureTest = args.Length == 2 && args[0] == "--capture-test";
        GameMode = captureTest || (args.Length > 0 && args[0] == "--game-mode");
        var errors = new List<string>();
        bool cursorHidden = false;
        using (var mutex = new System.Threading.Mutex(false, "Local\\BlackScreenMute_Game_WZY"))
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (System.Threading.AbandonedMutexException) { acquired = true; }
            if (!acquired) return;
            try
            {
                Application.EnableVisualStyles();
                ReadAudio();
                foreach (Screen screen in Screen.AllScreens)
                {
                    var form = GameMode ? new GameCover() : new Form();
                    form.FormBorderStyle = FormBorderStyle.None;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Bounds = screen.Bounds;
                    form.BackColor = Color.Black;
                    form.TopMost = true;
                    form.ShowInTaskbar = false;
                    form.KeyPreview = true;
                    form.AutoScaleMode = AutoScaleMode.None;
                    if (!GameMode) form.KeyDown += delegate(object sender, KeyEventArgs e) {
                        if (e.KeyCode == Keys.Escape) Stop();
                    };
                    Covers.Add(form);
                }
                MainForm = Covers[0];
                // Create a handle before the hook can dispatch an exit request.
                IntPtr handle = MainForm.Handle;
                if (GameMode) foreach (Form cover in Covers)
                {
                    if (!SetWindowDisplayAffinity(cover.Handle, 0x11))
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "系统不支持黑屏窗口从截图中排除，无法安全启用游戏黑屏。");
                    SetLayeredWindowAttributes(cover.Handle, 0, 255, 2);
                }
                Hook = SetWindowsHookEx(13, Callback, GetModuleHandle(null), 0);
                if (Hook == IntPtr.Zero)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                if (test)
                {
                    File.WriteAllText(args[1], "PASS\r\nScreens: " + Covers.Count +
                        "\r\nActive audio outputs: " + Audio.Count +
                        "\r\nAudio mute state read successfully; keyboard hook installed.\r\n" +
                        "No windows shown and no audio state changed.\r\n");
                    return;
                }
                if (captureTest)
                {
                    using (var bg = new Form())
                    {
                        bg.StartPosition = FormStartPosition.Manual;
                        bg.Bounds = new Rectangle(Screen.PrimaryScreen.Bounds.Left + 100, Screen.PrimaryScreen.Bounds.Top + 100, 320, 240);
                        bg.FormBorderStyle = FormBorderStyle.None;
                        bg.BackColor = Color.Magenta;
                        bg.Show(); bg.Activate(); Application.DoEvents();
                        IntPtr beforeForeground = GetForegroundWindow();
                        foreach (Form cover in Covers) cover.Show();
                        Application.DoEvents(); System.Threading.Thread.Sleep(250);
                        if (GetForegroundWindow() != beforeForeground) throw new Exception("黑屏窗口改变了前台焦点。之前：" + beforeForeground + "，之后：" + GetForegroundWindow());
                        foreach(Form cover in Covers) { uint affinity; if(!cover.Visible || !GetWindowDisplayAffinity(cover.Handle,out affinity) || affinity != 0x11) throw new Exception("黑屏截图排除属性未生效。"); }
                        if (ShouldExitOnEsc(0x10) || ShouldExitOnEsc(0x02) || !ShouldExitOnEsc(0)) throw new Exception("Esc 注入过滤验证失败。");
                        using (var bitmap = new Bitmap(1, 1))
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            graphics.CopyFromScreen(bg.Left + 160, bg.Top + 120, 0, 0, new Size(1, 1), CopyPixelOperation.SourceCopy);
                            Color got = bitmap.GetPixel(0, 0);
                            if (got.R < 240 || got.B < 240 || got.G > 15) throw new Exception("截图排除验证失败：" + got);
                            File.WriteAllText(args[1], "PASS\r\n黑屏窗口已显示、截图排除属性生效；前台焦点保持不变。\r\nGDI 截图看到了背后的测试画面。\r\nEsc 过滤通过：忽略注入键，仅响应实际键盘 Esc。\r\n本次未修改声音状态，测试窗口已自动关闭。\r\n");
                        }
                        foreach(Form cover in Covers) cover.Hide();
                    }
                    return;
                }
                foreach (AudioState state in Audio)
                {
                    state.Changed = true;
                    Guid context = Context;
                    Check(state.Volume.SetMute(true, ref context));
                }
                Cursor.Hide();
                cursorHidden = true;
                // Keep Windows awake while the blackout screen is active.
                if (SetThreadExecutionState(0x80000000u | 0x00000001u | 0x00000002u) == 0)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                foreach (Form cover in Covers) cover.Show();
                if (!GameMode) MainForm.Activate();
                Application.DoEvents();
                if (GameMode && args.Length == 2) File.WriteAllText(args[1], "READY");
                Application.Run(MainForm);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
                if (test || captureTest) File.WriteAllText(args[1], "FAIL\r\n" + ex.ToString());
                else if (GameMode && args.Length == 2) File.WriteAllText(args[1], "FAIL\r\n" + ex.Message);
            }
            finally
            {
                if (Hook != IntPtr.Zero) { UnhookWindowsHookEx(Hook); Hook = IntPtr.Zero; }
                SetThreadExecutionState(0x80000000u);
                foreach (AudioState state in Audio)
                {
                    try
                    {
                        if (state.Changed)
                        {
                            Guid context = Context;
                            Check(state.Volume.SetMute(state.WasMuted, ref context));
                        }
                    }
                    catch (Exception ex) { errors.Add("恢复声音失败：" + ex.Message); }
                    finally { Marshal.ReleaseComObject(state.Volume); }
                }
                foreach (Form cover in Covers) cover.Dispose();
                if (cursorHidden) Cursor.Show();
                mutex.ReleaseMutex();
            }
        }
        if (!test && !captureTest && errors.Count > 0)
            MessageBox.Show(String.Join("\r\n", errors.ToArray()), "黑屏静音", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Environment.ExitCode = errors.Count > 0 ? 1 : 0;
    }

    static void Stop()
    {
        if (Closing) return;
        Closing = true;
        MainForm.BeginInvoke((Action)delegate {
            foreach (Form cover in Covers) cover.Close();
            Application.ExitThread();
        });
    }

    static IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (message == (IntPtr)0x100 || message == (IntPtr)0x104))
        {
            int key = Marshal.ReadInt32(data);
            int flags = Marshal.ReadInt32(data, 8);
            if (key == 0x1B && ShouldExitOnEsc(flags)) { Stop(); return (IntPtr)1; }
        }
        return CallNextHookEx(Hook, code, message, data);
    }

    static void Check(int result) { Marshal.ThrowExceptionForHR(result); }
    static bool ShouldExitOnEsc(int flags) { return !GameMode || (flags & 0x12) == 0; }
    static void ReadAudio()
    {
        IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDeviceCollection devices = null;
        try
        {
            Check(enumerator.EnumAudioEndpoints(0, 1, out devices));
            uint count;
            Check(devices.GetCount(out count));
            for (uint i = 0; i < count; i++)
            {
                IMMDevice device = null;
                object endpoint = null;
                try
                {
                    Check(devices.Item(i, out device));
                    Guid iid = typeof(IAudioEndpointVolume).GUID;
                    Check(device.Activate(ref iid, 23, IntPtr.Zero, out endpoint));
                    var volume = (IAudioEndpointVolume)endpoint;
                    bool muted;
                    Check(volume.GetMute(out muted));
                    Audio.Add(new AudioState { Volume = volume, WasMuted = muted });
                    endpoint = null;
                }
                finally
                {
                    if (endpoint != null) Marshal.ReleaseComObject(endpoint);
                    if (device != null) Marshal.ReleaseComObject(device);
                }
            }
        }
        finally
        {
            if (devices != null) Marshal.ReleaseComObject(devices);
            Marshal.ReleaseComObject(enumerator);
        }
    }
    class AudioState { public IAudioEndpointVolume Volume; public bool WasMuted; public bool Changed; }
    class GameCover : Form
    {
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00080000 | 0x20; return p; }
        }
    }
    delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint color, byte alpha, uint flags);
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator { [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices); }
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice device);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice { [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance); }
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
    [PreserveSig] int GetMasterVolumeLevel(out float level);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}
