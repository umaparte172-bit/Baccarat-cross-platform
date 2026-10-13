using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Baccarat.Desktop;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = ApplicationServices.CreateDefault();
            desktop.MainWindow = new MainWindow(
                services.Authentication,
                services.Diagnostics,
                services.AuthenticationSessions);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
