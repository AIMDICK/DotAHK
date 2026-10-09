using System.Runtime.InteropServices;

namespace DotAHK.Services;

/// <summary>
/// A minimal, dependency-free system tray icon built directly on
/// <c>Shell_NotifyIcon</c>. The icon is hosted on a hidden window created on the
/// calling (UI) thread, so the existing WinUI message pump delivers its
/// notifications and context menu commands without any extra message loop.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint CallbackMessage = WmApp + 1;
    private const uint IconId = 1;

    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmNull = 0x0000;

    private const uint MfString = 0x00000000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;

    private const int MenuOpenId = 1;
    private const int MenuExitId = 2;

    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;

    private const int SmCxSmIcon = 49;
    private const int SmCySmIcon = 50;

    private const int IdiApplication = 32512;

    private readonly string _className;
    private readonly WndProc _wndProc;
    private readonly nint _instance;
    private readonly uint _taskbarCreatedMessage;

    private nint _window;
    private nint _icon;
    private bool _added;
    private bool _disposed;

    private NotifyIconData _data;

    /// <summary>Raised when the user asks to bring the app back (left click or menu).</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Raised when the user chooses "Exit" from the tray menu.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>
    /// Creates and immediately shows the tray icon. Must be called on the UI
    /// thread because the backing window is created there.
    /// </summary>
    /// <param name="iconPath">Full path to a .ico file; the stock system icon is used as a fallback.</param>
    /// <param name="toolTip">Hover tooltip shown next to the icon.</param>
    public TrayIcon(string iconPath, string toolTip)
    {
        _instance = GetModuleHandle(null);
        _className = "DotAHK.TrayIcon." + Guid.NewGuid().ToString("N");
        _wndProc = WindowProc;
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        RegisterWindowClass();

        _window = CreateWindowEx(
            0, _className, _className, 0, 0, 0, 0, 0, 0, 0, _instance, 0);

        if (_window == 0)
        {
            UnregisterWindowClass();
            throw new InvalidOperationException("Failed to create the tray icon window.");
        }

        _icon = LoadTrayIcon(iconPath);

        _data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _window,
            uID = IconId,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = toolTip,
        };

        _added = Shell_NotifyIcon(NimAdd, ref _data);
    }

    private void RegisterWindowClass()
    {
        var windowClass = new WindowClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _instance,
            lpszClassName = _className,
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException("Failed to register the tray icon window class.");
        }
    }

    private void UnregisterWindowClass()
    {
        if (_instance != 0)
        {
            UnregisterClass(_className, _instance);
        }
    }

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (msg == CallbackMessage)
            {
                var mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);
                switch (mouseMessage)
                {
                    case WmLButtonUp:
                        OpenRequested?.Invoke(this, EventArgs.Empty);
                        break;
                    case WmRButtonUp:
                        ShowContextMenu();
                        break;
                }

                return 0;
            }

            if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
            {
                // Windows Explorer restarted: put the icon back.
                _added = Shell_NotifyIcon(NimAdd, ref _data);
            }
        }
        catch
        {
            // Never let an exception escape into the native message pump.
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, MenuOpenId, "Open DotAHK");
            AppendMenu(menu, MfString, MenuExitId, "Exit");

            GetCursorPos(out var point);

            // Required so the popup dismisses correctly when clicking elsewhere.
            SetForegroundWindow(_window);

            var command = TrackPopupMenuEx(
                menu, TpmRightButton | TpmReturnCmd, point.X, point.Y, _window, 0);

            PostMessage(_window, WmNull, 0, 0);

            switch (command)
            {
                case MenuOpenId:
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case MenuExitId:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static nint LoadTrayIcon(string iconPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                var icon = LoadImage(
                    0,
                    iconPath,
                    ImageIcon,
                    GetSystemMetrics(SmCxSmIcon),
                    GetSystemMetrics(SmCySmIcon),
                    LrLoadFromFile);

                if (icon != 0)
                {
                    return icon;
                }
            }
        }
        catch
        {
            // Fall through to the stock system icon.
        }

        return LoadIcon(0, IdiApplication);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_added)
        {
            Shell_NotifyIcon(NimDelete, ref _data);
            _added = false;
        }

        if (_window != 0)
        {
            DestroyWindow(_window);
            _window = 0;
        }

        UnregisterWindowClass();

        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }
    }

    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;

        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string lpClassName, nint hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadIcon(nint hInstance, nint lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, nint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenuEx(nint hMenu, uint fuFlags, int x, int y, nint hwnd, nint lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);
}
