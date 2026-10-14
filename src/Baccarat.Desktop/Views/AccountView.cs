using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Baccarat.Core.Authentication;
using Baccarat.Core.Diagnostics;

namespace Baccarat.Desktop.Views;

public sealed class AccountView : Grid
{
    private readonly AuthenticationController controller;
    private readonly TextBox name = Field(AuthenticationController.MaximumNameLength);
    private readonly TextBox email = Field(AuthenticationController.MaximumEmailLength);
    private readonly TextBox password = Field(AuthenticationController.MaximumPasswordLength);
    private readonly TextBox phone = Field(AuthenticationController.MaximumPhoneLength);
    private readonly TextBox country = Field(AuthenticationController.MaximumCountryLength);
    private readonly TextBlock result = LegacyTheme.TextBlock(string.Empty, 14, LegacyTheme.Gold);
    private readonly Button signIn = SecondaryButton("SIGN IN");
    private readonly Button signUp = PrimaryButton("SIGN UP");
    private readonly Button signOut = SecondaryButton("SIGN OUT");
    private readonly CheckBox showPassword = new()
    {
        Content = "Show Password",
        Foreground = LegacyTheme.Blue,
        FontSize = 12
    };

    public AccountView(IAuthenticationService service, IDiagnosticSink? diagnostics = null)
        : this(service, diagnostics, null)
    {
    }

    public AccountView(
        IAuthenticationService service,
        IDiagnosticSink? diagnostics,
        IAuthenticationSessionStore? sessionStore)
    {
        controller = new AuthenticationController(service, diagnostics, sessionStore);
        Background = LegacyTheme.Window;
        MinHeight = 590;

        password.PasswordChar = '•';
        var shell = new StackPanel
        {
            Width = 724,
            Margin = new Thickness(26, 8, 26, 30),
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        shell.Children.Add(LegacyTheme.Brand());

        var form = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("180,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 24,
            RowSpacing = 18
        };
        AddField(form, 0, "NAME :", name);
        AddField(form, 1, "EMAIL :", email);

        var passwordArea = new StackPanel { Spacing = 6 };
        passwordArea.Children.Add(password);
        passwordArea.Children.Add(showPassword);
        AddField(form, 2, "PASSWORD :", passwordArea);
        AddField(form, 3, "PHONE NO :", phone);
        AddField(form, 4, "COUNTRY :", country);

        signUp.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(signUp, 1);
        Grid.SetRow(signUp, 5);
        form.Children.Add(signUp);

        signIn.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(signIn, 1);
        Grid.SetRow(signIn, 6);
        form.Children.Add(signIn);

        var feedback = new StackPanel { Spacing = 10 };
        feedback.Children.Add(result);
        feedback.Children.Add(signOut);
        Grid.SetColumn(feedback, 1);
        Grid.SetRow(feedback, 7);
        form.Children.Add(feedback);

        shell.Children.Add(new Border
        {
            Background = LegacyTheme.Panel,
            Padding = new Thickness(54, 36, 84, 24),
            Child = form
        });
        Children.Add(shell);

        showPassword.Click += (_, _) => password.PasswordChar = showPassword.IsChecked == true ? '\0' : '•';
        signIn.Click += async (_, _) => await SignInAsync();
        signUp.Click += async (_, _) => await SignUpAsync();
        signOut.Click += async (_, _) => await SignOutAsync();
        AttachedToVisualTree += async (_, _) => await RestoreSessionAsync();
        Refresh();
    }

    private async Task RestoreSessionAsync()
    {
        var authentication = await controller.RestoreSessionAsync();
        result.Text = authentication.Message;
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

    private async Task SignUpAsync()
    {
        signUp.IsEnabled = false;
        result.Text = "Creating account…";
        var authentication = await controller.SignUpAsync(
            name.Text,
            email.Text,
            password.Text,
            phone.Text,
            country.Text);
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
        name.IsEnabled = email.IsEnabled = password.IsEnabled = phone.IsEnabled = country.IsEnabled = !authenticated;
        signIn.IsEnabled = !authenticated && !controller.IsBusy;
        signUp.IsEnabled = !authenticated && !controller.IsBusy;
        signOut.IsEnabled = authenticated && !controller.IsBusy;
        signOut.IsVisible = authenticated;
        if (string.IsNullOrWhiteSpace(result.Text))
            result.Text = controller.LastResult.Message;
        result.Foreground = authenticated ? Brush.Parse("#7BD88F") : LegacyTheme.Gold;
    }

    private static void AddField(Grid grid, int row, string caption, Control field)
    {
        var label = LegacyTheme.TextBlock(caption, 16, LegacyTheme.Gold);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(label, row);
        grid.Children.Add(label);

        Grid.SetColumn(field, 1);
        Grid.SetRow(field, row);
        grid.Children.Add(field);
    }

    private static TextBox Field(int maxLength) => new()
    {
        Height = 34,
        MaxLength = maxLength,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static Button PrimaryButton(string content) => new()
    {
        Content = content,
        Height = 46,
        Background = LegacyTheme.Green,
        Foreground = LegacyTheme.Ink,
        BorderBrush = LegacyTheme.Blue,
        BorderThickness = new Thickness(1),
        FontSize = 18,
        FontWeight = FontWeight.Bold
    };

    private static Button SecondaryButton(string content) => new()
    {
        Content = content,
        MinWidth = 136,
        Height = 43,
        Background = Brushes.Transparent,
        Foreground = LegacyTheme.Blue,
        BorderBrush = LegacyTheme.Blue,
        BorderThickness = new Thickness(1)
    };
}
