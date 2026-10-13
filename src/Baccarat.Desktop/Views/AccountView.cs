using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Baccarat.Core.Authentication;
using Baccarat.Core.Diagnostics;

namespace Baccarat.Desktop.Views;

public sealed class AccountView : StackPanel
{
    private readonly AuthenticationController controller;
    private readonly TextBox email = new() { Watermark = "Email", MaxWidth = 460 };
    private readonly TextBox password = new() { Watermark = "Password", PasswordChar = '•', MaxWidth = 460 };
    private readonly TextBlock result = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 680 };
    private readonly Button signIn = new() { Content = "Sign in", Padding = new Thickness(18, 12) };
    private readonly Button signOut = new() { Content = "Sign out", Padding = new Thickness(18, 12) };

    public AccountView(IAuthenticationService service, IDiagnosticSink? diagnostics = null)
    {
        controller = new AuthenticationController(service, diagnostics);
        Margin = new Thickness(24);
        Spacing = 16;

        Children.Add(Text("Account connection", 30));
        Children.Add(Text(
            "Authentication is isolated behind a service contract. Until the original protocol is recovered, sign-in remains safely offline.",
            15,
            "#A8B5CA"));
        Children.Add(email);
        Children.Add(password);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(signIn);
        actions.Children.Add(signOut);
        Children.Add(actions);
        Children.Add(result);

        signIn.Click += async (_, _) => await SignInAsync();
        signOut.Click += async (_, _) => await SignOutAsync();
        Refresh();
    }

    private async Task SignInAsync()
    {
        signIn.IsEnabled = false;
        result.Text = "Connecting…";
        var authentication = await controller.SignInAsync(email.Text, password.Text);
        password.Text = string.Empty;
        result.Text = authentication.Message;
        Refresh();
    }

    private async Task SignOutAsync()
    {
        await controller.SignOutAsync();
        result.Text = controller.LastResult.Message;
        Refresh();
    }

    private void Refresh()
    {
        var authenticated = controller.Session is not null;
        email.IsEnabled = password.IsEnabled = !authenticated;
        signIn.IsEnabled = !authenticated && !controller.IsBusy;
        signOut.IsEnabled = authenticated && !controller.IsBusy;
        if (string.IsNullOrWhiteSpace(result.Text))
            result.Text = controller.LastResult.Message;
        result.Foreground = Brush.Parse(authenticated ? "#7BD88F" : "#E9BC72");
    }

    private static TextBlock Text(string value, double size, string color = "#E9EEF6") =>
        new() { Text = value, FontSize = size, Foreground = Brush.Parse(color), TextWrapping = TextWrapping.Wrap };
}

