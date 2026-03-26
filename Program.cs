using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AppIdRepro;

public class Program
{
    private static IntPtr _redirectEventHandle = IntPtr.Zero;

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var isRedirect = DecideRedirection();

        if (!isRedirect)
        {
            Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
        }

        return 0;
    }

    private static bool DecideRedirection()
    {
        var isRedirect = false;
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();

        var keyInstance = AppInstance.FindOrRegisterForKey("MySingleInstanceApp");
        if (keyInstance.IsCurrent)
        {
            keyInstance.Activated += OnActivated;
        }
        else
        {
            isRedirect = true;
            RedirectActivationTo(args, keyInstance);
        }

        return isRedirect;
    }

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        _ = args.Kind;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

    [DllImport("kernel32.dll")]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, ulong nHandles, IntPtr[] pHandles, out uint dwIndex);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // Do the redirection on another thread, and use a non-blocking
    // wait method to wait for the redirection to complete.
    public static void RedirectActivationTo(AppActivationArguments args, AppInstance keyInstance)
    {
        _redirectEventHandle = CreateEvent(IntPtr.Zero, true, false, null);
        Task.Run(() =>
        {
            keyInstance.RedirectActivationToAsync(args).AsTask().Wait();
            SetEvent(_redirectEventHandle);
        });

        uint CWMO_DEFAULT = 0;
        uint INFINITE = 0xFFFFFFFF;
        _ = CoWaitForMultipleObjects(CWMO_DEFAULT, INFINITE, 1, [_redirectEventHandle], out uint handleIndex);

        // Bring the window to the foreground
        var process = Process.GetProcessById((int)keyInstance.ProcessId);
        SetForegroundWindow(process.MainWindowHandle);
    }
}
