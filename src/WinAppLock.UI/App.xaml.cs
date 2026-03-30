using System.Windows;

namespace WinAppLock.UI;

/// <summary>
/// Uygulama giriş noktası.
/// Tek instance kontrolü ve global exception yönetimi.
/// </summary>
public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Tek instance kontrolü — aynı anda birden fazla WinAppLock UI açılmasını engeller
        const string MUTEX_NAME = "WinAppLock_UI_SingleInstance";
        _mutex = new Mutex(true, MUTEX_NAME, out bool isNewInstance);

        if (!isNewInstance)
        {
            MessageBox.Show("WinAppLock zaten çalışıyor!", "WinAppLock",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Current.Shutdown();
            return;
        }

        // Global exception handler
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Beklenmeyen bir hata oluştu:\n{args.Exception.Message}",
                "WinAppLock Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
