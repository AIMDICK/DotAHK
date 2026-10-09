using System.Runtime.InteropServices;
using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Win32 implementation of <see cref="IGlobalHotkeyService"/>. A hidden window is
/// created lazily on the UI thread; because it lives on that thread the existing
/// WinUI message pump delivers <c>WM_HOTKEY</c> notifications with no extra loop.
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService, IDisposable
{
    private const uint WmHotkey = 0x0312;

    /// <summary>MOD_NOREPEAT: ignore auto-repeat while the key is held down.</summary>
    private const uint ModNoRepeat = 0x4000;

    /// <summary>First hotkey id handed out; the OS does not care about the value.</summary>
    private const int FirstHotkeyId = 0x4000;

    private readonly WndProc _wndProc;
    private readonly string _className = "DotAHK.HotkeyWindow." + Guid.NewGuid().ToString("N");
    private readonly nint _instance;
    private readonly object _sync = new();

    private readonly Dictionary<int, Registration> _byId = new();
    private readonly Dictionary<string, int> _byScript = new(StringComparer.OrdinalIgnoreCase);

    private nint _window;
    private int _nextId = FirstHotkeyId;
    private bool _classRegistered;
    private bool _disposed;

    public GlobalHotkeyService()
    {
        _instance = GetModuleHandle(null);
        _wndProc = WindowProc;
    }

    public event EventHandler<string>? HotkeyPressed;

    public bool TryRegister(string scriptKey, HotkeyGesture gesture, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(scriptKey))
        {
            error = "The script key is empty.";
            return false;
        }

        if (gesture is null || gesture.VirtualKey <= 0 || !gesture.HasModifier)
        {
            error = "A hotkey needs at least one modifier and a key.";
            return false;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                error = "The hotkey service is shut down.";
                return false;
            }

            // A gesture can only be owned by one script.
            foreach (var existing in _byId.Values)
            {
                if (existing.Gesture.Equals(gesture.DisplayText, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(existing.ScriptKey, scriptKey, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Already assigned to \"{existing.ScriptName}\".";
                    return false;
                }
            }

            // Replace any previous binding for this script.
            UnregisterLocked(scriptKey, out _);

            if (!EnsureWindow())
            {
                error = "Could not create the hotkey window.";
                return false;
            }

            var id = _nextId++;
            var modifiers = (uint)gesture.ModifierFlags | ModNoRepeat;

            if (!RegisterHotKey(_window, id, modifiers, (uint)gesture.VirtualKey))
            {
                var code = Marshal.GetLastWin32Error();
                error = code == 1409 // ERROR_HOTKEY_ALREADY_REGISTERED
                    ? "That combination is already used by another application."
                    : $"Windows refused the hotkey (error {code}).";
                return false;
            }

            var name = System.IO.Path.GetFileName(scriptKey);
            _byId[id] = new Registration(scriptKey, name, gesture.DisplayText);
            _byScript[scriptKey] = id;
            return true;
        }
    }

    public void Unregister(string scriptKey)
    {
        lock (_sync)
        {
            UnregisterLocked(scriptKey, out _);
        }
    }

    public void UnregisterAll()
    {
        lock (_sync)
        {
            foreach (var id in _byId.Keys.ToArray())
            {
                if (_window != 0)
                {
                    UnregisterHotKey(_window, id);
                }
            }

            _byId.Clear();
            _byScript.Clear();
        }
    }

    private void UnregisterLocked(string scriptKey, out bool removed)
    {
        removed = false;
        if (!_byScript.Remove(scriptKey, out var id))
        {
            return;
        }

        if (_window != 0)
        {
            UnregisterHotKey(_window, id);
        }

        _byId.Remove(id);
        removed = true;
    }

    private bool EnsureWindow()
    {
        if (_window != 0)
        {
            return true;
        }

        if (!_classRegistered)
        {
            var windowClass = new WindowClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = _instance,
                lpszClassName = _className,
            };

            _classRegistered = RegisterClassEx(ref windowClass) != 0;
        }

        _window = CreateWindowEx(0, _className, _className, 0, 0, 0, 0, 0, 0, 0, _instance, 0);
        return _window != 0;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var id in _byId.Keys.ToArray())
            {
                if (_window != 0)
                {
                    UnregisterHotKey(_window, id);
                }
            }

            _byId.Clear();
            _byScript.Clear();

            if (_window != 0)
            {
                DestroyWindow(_window);
                _window = 0;
            }

            if (_classRegistered)
            {
                UnregisterClass(_className, _instance);
                _classRegistered = false;
            }
        }
    }

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (msg == WmHotkey)
            {
                var id = (int)wParam;
                string? scriptKey = null;
                lock (_sync)
                {
                    if (_byId.TryGetValue(id, out var registration))
                    {
                        scriptKey = registration.ScriptKey;
                    }
                }

                if (scriptKey is not null)
                {
                    HotkeyPressed?.Invoke(this, scriptKey);
                }

                return 0;
            }
        }
        catch
        {
            // Never let an exception escape into the native message pump.
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private sealed record Registration(string ScriptKey, string ScriptName, string Gesture);

    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}

