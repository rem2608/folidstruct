using System.Runtime.InteropServices;

namespace FolidStruct.App;

/// <summary>Win32 calls that WinUI does not wrap.</summary>
internal static partial class Native
{
    private const uint MB_OK = 0x0, MB_ICONERROR = 0x10, MB_SETFOREGROUND = 0x10000;

    /// <summary>An error message without a parent window (like ui.py on Linux).</summary>
    public static void ShowError(string title, string message) =>
        MessageBox(IntPtr.Zero, message, title, MB_OK | MB_ICONERROR | MB_SETFOREGROUND);

    public static void BringToFront(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
            ShowWindow(hwnd, 9); // SW_RESTORE
        SetForegroundWindow(hwnd);
    }

    /// <summary>The display scale of a window (1.0 at 96 DPI, 2.0 at 200%).</summary>
    public static double ScaleFactor(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr hwnd, string text, string caption, uint type);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hwnd, int command);
}
