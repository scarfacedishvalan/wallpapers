using System.Windows;

namespace WallpaperUtility;

public partial class App : Application
{
    private const string InstanceName = @"Local\WallpaperUtility.SingleInstance";
    private Mutex? instanceMutex;
    private EventWaitHandle? showRequest;

    private void AppStartup(object sender, StartupEventArgs e)
    {
        bool background = e.Args.Any(arg =>
            string.Equals(arg, StartupRegistration.BackgroundArgument, StringComparison.OrdinalIgnoreCase));

        // Only one copy may rotate wallpapers. A second launch brings the first one forward.
        instanceMutex = new Mutex(initiallyOwned: true, InstanceName, out bool firstInstance);
        if (!firstInstance)
        {
            if (!background && EventWaitHandle.TryOpenExisting(InstanceName + ".Show", out EventWaitHandle? existing))
            {
                existing.Set();
                existing.Dispose();
            }

            Shutdown();
            return;
        }

        var window = new MainWindow(background);
        MainWindow = window;
        showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Show");
        ThreadPool.RegisterWaitForSingleObject(showRequest,
            (_, _) => Dispatcher.BeginInvoke(window.ShowFromBackground), null, Timeout.Infinite, executeOnlyOnce: false);
        if (!background)
        {
            window.Show();
        }
    }
}

