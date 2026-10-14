using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Baccarat.Core.Authentication;
using Baccarat.Core.Diagnostics;
using Baccarat.Infrastructure.Authentication;

namespace Baccarat.Desktop.Views;

public sealed class AccountView : Grid
{
    private readonly AuthenticationController controller;
    private readonly OptInAuthenticationSessionStore sessionStore;
    private readonly TextBox name = Field(AuthenticationController.MaximumNameLength);
    private readonly TextBox email = Field(AuthenticationController.MaximumEmailLength);
    private readonly TextBox password = Field(AuthenticationController.MaximumPasswordLength);
    private readonly TextBox phone = Field(AuthenticationController.MaximumPhoneLength);
    private readonly TextBox country = Field(AuthenticationController.MaximumCountryLength);
    private readonly TextBlock result = LegacyTheme.TextBlock(string.Empty, 14, LegacyTheme.Gold);
    private readonly CheckBox rememberMe = Check("REMEMBER ME");
    private readonly CheckBox showSignInPassword = Check("SHOW PASSWORD");
    private readonly CheckBox showSignUpPassword = Check("Show Password");
    private readonly Button signIn = PrimaryButton("SIGN IN");
    private readonly Button signUp = PrimaryButton("SIGN UP");
    private readonly Button signOut = SecondaryButton("SIGN OUT");
    private Canvas? activeForm;
    private bool showingSignUp;
    private readonly bool demoMode;

    public event Action? Authenticated;
    public event Action? SignedOut;

    public AccountView(IAuthenticationService service, IDiagnosticSink? diagnostics = null)
        : this(service, diagnostics, null)
    {
    }

    public AccountView(
        IAuthenticationService service,
        IDiagnosticSink? diagnostics,
        IAuthenticationSessionStore? sessionStore)
    {
        this.sessionStore = new OptInAuthenticationSessionStore(sessionStore ?? NullAuthenticationSessionStore.Instance);
        controller = new AuthenticationController(service, diagnostics, this.sessionStore);
        demoMode = service is DemoAuthenticationService;
        Background = LegacyTheme.Window;
        MinHeight = 590;
        password.PasswordChar = '•';

        showSignInPassword.Click += (_, _) => SetPasswordVisibility(showSignInPassword.IsChecked == true);
        showSignUpPassword.Click += (_, _) => SetPasswordVisibility(showSignUpPassword.IsChecked == true);
        signIn.Click += async (_, _) => await SignInAsync();
        signUp.Click += async (_, _) => await SignUpAsync();
        signOut.Click += async (_, _) => await SignOutAsync();
        AttachedToVisualTree += async (_, _) => await RestoreSessionAsync();

        ShowSignIn();
    }

    public void ShowSignIn()
    {
        showingSignUp = false;
        BuildForm();
    }

    public void ShowSignUp()
    {
        showingSignUp = true;
        BuildForm();
    }

    private void BuildForm()
    {
        activeForm?.Children.Clear();
        Children.Clear();
        var shell = new StackPanel
        {
            Width = 724,
            Spacing = 15,
            Margin = new Thickness(24, 8, 24, 24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        shell.Children.Add(LegacyTheme.Brand(170, 102));

        var panel = new Canvas { Width = 724, Height = showingSignUp ? 490 : 318 };
        activeForm = showingSignUp ? BuildSignUpForm() : BuildSignInForm();
        panel.Children.Add(activeForm);
        shell.Children.Add(new Border { Background = LegacyTheme.Panel, Child = panel });
        if (demoMode)
            shell.Children.Add(LegacyTheme.TextBlock(
                $"LOCAL DEMO ONLY · {DemoAuthenticationService.Email} · No server account is created.",
                12, LegacyTheme.Muted));
        Children.Add(shell);
        Refresh();
    }

    private Canvas BuildSignInForm()
    {
        var canvas = new Canvas { Width = 724, Height = 318 };
        Place(canvas, Caption("EMAIL :"), 84, 36, 90);
        Place(canvas, email, 226, 30, 396);
        Place(canvas, Caption("PASSWORD :"), 44, 99, 130);
        Place(canvas, password, 226, 93, 396);
        Place(canvas, rememberMe, 226, 148, 190);
        Place(canvas, showSignInPassword, 496, 148, 170);
        Place(canvas, signIn, 226, 181, 396);

        var goToSignUp = SecondaryButton("SIGN UP");
        goToSignUp.Click += (_, _) => ShowSignUp();
        Place(canvas, goToSignUp, 357, 251, 136);
        Place(canvas, result, 226, 294, 440);
        Place(canvas, signOut, 575, 251, 136);
        return canvas;
    }

    private Canvas BuildSignUpForm()
    {
        var canvas = new Canvas { Width = 724, Height = 490 };
        Place(canvas, Caption("NAME :"), 118, 36, 80);
        Place(canvas, name, 244, 30, 396);
        Place(canvas, Caption("EMAIL :"), 117, 99, 80);
        Place(canvas, email, 244, 93, 396);
        Place(canvas, Caption("PASSWORD :"), 77, 162, 120);
        Place(canvas, password, 244, 156, 396);
        Place(canvas, showSignUpPassword, 244, 201, 160);
        Place(canvas, Caption("PHONE NO :"), 81, 243, 116);
        Place(canvas, phone, 244, 238, 396);
        Place(canvas, Caption("COUNTRY :"), 90, 306, 107);
        Place(canvas, country, 244, 301, 396);
        Place(canvas, signUp, 244, 370, 396);

        var goToSignIn = SecondaryButton("SIGN IN");
        goToSignIn.Click += (_, _) => ShowSignIn();
        Place(canvas, goToSignIn, 369, 437, 136);
        Place(canvas, result, 244, 420, 396);
        Place(canvas, signOut, 520, 437, 136);
        return canvas;
    }

    private void SetPasswordVisibility(bool visible)
    {
        password.PasswordChar = visible ? '\0' : '•';
        showSignInPassword.IsChecked = showSignUpPassword.IsChecked = visible;
    }

    private async Task RestoreSessionAsync()
    {
        if (controller.Session is not null) return;
        var authentication = await controller.RestoreSessionAsync();
        rememberMe.IsChecked = sessionStore.Remember;
        result.Text = authentication.Message;
        Refresh();
        if (authentication.IsAuthenticated) Dispatcher.UIThread.Post(() => Authenticated?.Invoke());
    }

    private async Task SignInAsync()
    {
        signIn.IsEnabled = false;
        result.Text = "Connecting…";
        sessionStore.Remember = rememberMe.IsChecked == true;
        var authentication = await controller.SignInAsync(email.Text, password.Text);
        password.Text = string.Empty;
        result.Text = authentication.Message;
        Refresh();
        if (authentication.IsAuthenticated) Authenticated?.Invoke();
    }

    private async Task SignUpAsync()
    {
        signUp.IsEnabled = false;
        result.Text = "Creating account…";
        sessionStore.Remember = false;
        var authentication = await controller.SignUpAsync(name.Text, email.Text, password.Text, phone.Text, country.Text);
        password.Text = string.Empty;
        result.Text = authentication.Message;
        Refresh();
        if (authentication.IsAuthenticated) Authenticated?.Invoke();
    }

    public async Task SignOutAsync()
    {
        await controller.SignOutAsync();
        rememberMe.IsChecked = false;
        result.Text = controller.LastResult.Message;
        ShowSignIn();
        SignedOut?.Invoke();
    }

    private void Refresh()
    {
        var authenticated = controller.Session is not null;
        name.IsEnabled = email.IsEnabled = password.IsEnabled = phone.IsEnabled = country.IsEnabled = !authenticated;
        signIn.IsEnabled = signUp.IsEnabled = !authenticated && !controller.IsBusy;
        signOut.IsEnabled = authenticated && !controller.IsBusy;
        signOut.IsVisible = authenticated;
        rememberMe.IsEnabled = !authenticated;
        if (string.IsNullOrWhiteSpace(result.Text)) result.Text = controller.LastResult.Message;
        result.Foreground = authenticated ? Brush.Parse("#7BD88F") : LegacyTheme.Gold;
    }

    private static void Place(Canvas canvas, Control control, double left, double top, double width)
    {
        control.Width = width;
        Canvas.SetLeft(control, left);
        Canvas.SetTop(control, top);
        canvas.Children.Add(control);
    }

    private static TextBlock Caption(string text) => LegacyTheme.TextBlock(text, 16, LegacyTheme.Gold);

    private static TextBox Field(int maxLength) => new() { Height = 33, MaxLength = maxLength };

    private static CheckBox Check(string text) => new()
    {
        Content = text,
        Foreground = Brush.Parse("#6A98F6"),
        FontSize = 12
    };

    private static Button PrimaryButton(string text) => new()
    {
        Content = text,
        Height = 46,
        Background = LegacyTheme.Green,
        Foreground = LegacyTheme.Ink,
        BorderBrush = LegacyTheme.Blue,
        BorderThickness = new Thickness(1),
        FontSize = 20,
        FontWeight = FontWeight.Bold,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static Button SecondaryButton(string text) => new()
    {
        Content = text,
        Height = 43,
        Background = Brushes.Transparent,
        Foreground = LegacyTheme.Blue,
        BorderBrush = LegacyTheme.Blue,
        BorderThickness = new Thickness(1),
        HorizontalContentAlignment = HorizontalAlignment.Center
    };
}
