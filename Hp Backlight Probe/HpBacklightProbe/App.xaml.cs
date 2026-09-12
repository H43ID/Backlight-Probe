using System.Windows;
using System.Windows.Threading;

namespace HpBacklightProbe;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\n" +
            "If this happened while turning the backlight on/off, make sure this app " +
            "is running as Administrator - HP's BIOS WMI writes require elevation.",
            "HP Backlight Controller - Error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        // Prevent the whole app from silently crashing on a single failed WMI call -
        // the user can see what went wrong and keep using the rest of the app.
        e.Handled = true;
    }
}
