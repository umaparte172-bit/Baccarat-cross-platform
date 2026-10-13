using System.Net.Mail;
using Baccarat.Core.Diagnostics;

namespace Baccarat.Core.Authentication;

public sealed class AuthenticationController
{
    private readonly IAuthenticationService service;
    private readonly IAuthenticationSessionStore sessionStore;
    private readonly IDiagnosticSink diagnostics;
    private readonly TimeProvider timeProvider;

    public AuthenticationController(
        IAuthenticationService service,
        IDiagnosticSink? diagnostics = null,
        IAuthenticationSessionStore? sessionStore = null,
        TimeProvider? timeProvider = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.diagnostics = diagnostics ?? NullDiagnosticSink.Instance;
        this.sessionStore = sessionStore ?? NullAuthenticationSessionStore.Instance;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IsBusy { get; private set; }
    public AuthenticatedSession? Session { get; private set; }
    public AuthenticationResult LastResult { get; private set; } =
        new(AuthenticationOutcome.Unavailable, "Not connected.");

    public Task<AuthenticationResult> SignInAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            return Task.FromResult(LastResult);

        var validation = ValidateSignIn(email, password);
        if (validation is not null)
        {
            diagnostics.Write(DiagnosticLevel.Warning, "authentication.validation_failed", validation.Message);
            LastResult = validation;
            return Task.FromResult(validation);
        }

        return ExecuteAsync(
            token => service.SignInAsync(new SignInRequest(email!.Trim(), password!), token),
            cancellationToken);
    }

    public Task<AuthenticationResult> SignUpAsync(
        string? name,
        string? email,
        string? password,
        string? phone,
        string? country,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            return Task.FromResult(LastResult);

        var validation = ValidateSignUp(name, email, password, phone, country);
        if (validation is not null)
        {
            diagnostics.Write(DiagnosticLevel.Warning, "authentication.validation_failed", validation.Message);
            LastResult = validation;
            return Task.FromResult(validation);
        }

        return ExecuteAsync(
            token => service.SignUpAsync(
                new SignUpRequest(name!.Trim(), email!.Trim(), password!, phone!.Trim(), country!.Trim()),
                token),
            cancellationToken);
    }

    private async Task<AuthenticationResult> ExecuteAsync(
        Func<CancellationToken, Task<AuthenticationResult>> operation,
        CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var result = await operation(cancellationToken);
            LastResult = result ?? new(
                AuthenticationOutcome.EmptyResponse,
                "The authentication service returned no result.");
            LastResult = Normalize(LastResult);
            Session = LastResult.IsAuthenticated ? LastResult.Session : null;
            if (Session is not null)
                await SaveSessionAsync(Session, cancellationToken);
            else
                await ClearStoredSessionAsync(cancellationToken);
            diagnostics.Write(
                LastResult.IsAuthenticated ? DiagnosticLevel.Information : DiagnosticLevel.Warning,
                "authentication.completed",
                $"Outcome={LastResult.Outcome}");
            return LastResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            Session = null;
            diagnostics.Write(DiagnosticLevel.Error, "authentication.failed", "The authentication service threw an exception.");
            return LastResult = new(
                AuthenticationOutcome.UnexpectedError,
                "Authentication failed because the service response could not be processed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<AuthenticationResult> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            return LastResult;

        IsBusy = true;
        try
        {
            var stored = await sessionStore.ReadAsync(cancellationToken);
            if (stored is null)
                return LastResult = new(AuthenticationOutcome.Unavailable, "Not signed in.");

            var restored = Normalize(new(AuthenticationOutcome.Authenticated, "Session restored.", stored));
            if (!restored.IsAuthenticated)
            {
                Session = null;
                await ClearStoredSessionAsync(cancellationToken);
                return LastResult = restored;
            }

            Session = stored;
            diagnostics.Write(DiagnosticLevel.Information, "authentication.session_restored", "A saved session was restored.");
            return LastResult = restored;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            Session = null;
            diagnostics.Write(DiagnosticLevel.Error, "authentication.session_restore_failed", "The saved session could not be restored.");
            return LastResult = new(AuthenticationOutcome.UnexpectedError, "The saved session could not be restored.");
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
        await ClearStoredSessionAsync(cancellationToken);
        if (current is null) return;

        try
        {
            await service.SignOutAsync(current, cancellationToken);
            diagnostics.Write(DiagnosticLevel.Information, "authentication.signed_out", "The session was cleared.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            diagnostics.Write(DiagnosticLevel.Error, "authentication.sign_out_failed", "Remote sign-out was not confirmed.");
            LastResult = new(
                AuthenticationOutcome.UnexpectedError,
                "The local session was cleared, but the remote sign-out could not be confirmed.");
        }
    }

    private AuthenticationResult Normalize(AuthenticationResult result)
    {
        if (result.Outcome != AuthenticationOutcome.Authenticated)
            return result with { Session = null };

        var session = result.Session;
        if (session is null || string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.AccessToken))
            return new(AuthenticationOutcome.MalformedResponse, "The authentication service returned an invalid session.");

        if (session.ExpiresAt is { } expiry && expiry <= timeProvider.GetUtcNow())
            return new(AuthenticationOutcome.Expired, "The saved session has expired. Sign in again.");

        return result;
    }

    private async ValueTask SaveSessionAsync(AuthenticatedSession session, CancellationToken cancellationToken)
    {
        try
        {
            await sessionStore.SaveAsync(session, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            diagnostics.Write(DiagnosticLevel.Warning, "authentication.session_save_failed", "The session is active but could not be saved.");
        }
    }

    private async ValueTask ClearStoredSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await sessionStore.ClearAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            diagnostics.Write(DiagnosticLevel.Warning, "authentication.session_clear_failed", "The saved session could not be cleared.");
        }
    }

    private static AuthenticationResult? ValidateSignIn(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            return new(AuthenticationOutcome.ValidationFailed, "Email and password are required.");

        if (!MailAddress.TryCreate(email.Trim(), out var address) ||
            !string.Equals(address.Address, email.Trim(), StringComparison.OrdinalIgnoreCase))
            return new(AuthenticationOutcome.ValidationFailed, "Enter a valid email address.");

        return null;
    }

    private static AuthenticationResult? ValidateSignUp(
        string? name,
        string? email,
        string? password,
        string? phone,
        string? country)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(country))
            return new(AuthenticationOutcome.ValidationFailed, "Name, phone and country are required.");

        return ValidateSignIn(email, password);
    }
}

