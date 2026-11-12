using System.Runtime.InteropServices;
using FolidStruct.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace FolidStruct.App;

public static partial class Program
{
    public static LaunchArgs Args { get; private set; } = new();

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Args = LaunchArgs.Parse(args);
        UseLocalState();

        if (!Args.NewProject)
        {
            // A single manager window: a second launch brings the first one forward.
            var key = AppInstance.FindOrRegisterForKey("manager");
            if (!key.IsCurrent)
            {
                RedirectActivation(key);
                return 0;
            }
            key.Activated += (_, _) => App.Current?.ShowManager();
        }

        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
        return 0;
    }

    /// <summary>
    /// Keep the files in the package's LocalState folder, where the File Explorer
    /// menu finds them (%LOCALAPPDATA%\Packages\&lt;family name&gt;\LocalState).
    /// Unpackaged runs (development) keep the default, %APPDATA%\FolidStruct.
    /// </summary>
    private static void UseLocalState()
    {
        if (Environment.GetEnvironmentVariable("FOLIDSTRUCT_CONFIG_DIR") is { Length: > 0 })
            return;
        try
        {
            Storage.ConfigDir = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch (InvalidOperationException)
        {
            // Not packaged.
        }
        catch (COMException)
        {
            // Not packaged.
        }
    }

    // Wait for the redirection without blocking COM calls on this STA thread
    // (the pattern of the Windows App SDK documentation).
    private static void RedirectActivation(AppInstance key)
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var done = CreateEvent(IntPtr.Zero, true, false, null);
        Task.Run(() =>
        {
            key.RedirectActivationToAsync(activation).AsTask().Wait();
            SetEvent(done);
        });
        _ = CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, [done], out _);
        CloseHandle(done);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateEventW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateEvent(IntPtr attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset,
                                              [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetEvent(IntPtr handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("ole32.dll")]
    private static partial uint CoWaitForMultipleObjects(uint flags, uint timeout, uint count, IntPtr[] handles,
                                                         out uint index);
}
