using System.Net.Mail;

namespace Baccarat.Core.Authentication;

public sealed class AuthenticationController
{
    private readonly IAuthenticationService service;

    public AuthenticationController(IAuthenticationService service) =>
        this.service = service ?? throw new ArgumentNullException(nameof(service));

    public bool IsBusy { get; private set; }
    public AuthenticatedSession? Session { get; private set; }
    public AuthenticationResult LastResult { get; private set; } =
        new(AuthenticationOutcome.Unavailable, "Not connected.");

    public async Task<AuthenticationResult> SignInAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            return LastResult;

        var validation = Validate(email, password);
        if (validation is not null)
            return LastResult = validation;

        IsBusy = true;
        try
        {
            var result = await service.SignInAsync(
                new SignInRequest(email!.Trim(), password!), cancellationToken);
            LastResult = result ?? new(
                AuthenticationOutcome.EmptyResponse,
                "The authentication service returned no result.");
            Session = LastResult.IsAuthenticated ? LastResult.Session : null;
            return LastResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            Session = null;
            return LastResult = new(
                AuthenticationOutcome.UnexpectedError,
                "Authentication failed because the service response could not be processed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        var current = Session;
        Session = null;
        LastResult = new(AuthenticationOutcome.Unavailable, "Signed out.");
        if (current is null)
            return;

        try
        {
            await service.SignOutAsync(current, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            LastResult = new(
                AuthenticationOutcome.UnexpectedError,
                "The local session was cleared, but the remote sign-out could not be confirmed.");
        }
    }

    private static AuthenticationResult? Validate(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            return new(AuthenticationOutcome.ValidationFailed, "Email and password are required.");

        if (!MailAddress.TryCreate(email.Trim(), out var address) ||
            !string.Equals(address.Address, email.Trim(), StringComparison.OrdinalIgnoreCase))
            return new(AuthenticationOutcome.ValidationFailed, "Enter a valid email address.");

        return null;
    }
}

