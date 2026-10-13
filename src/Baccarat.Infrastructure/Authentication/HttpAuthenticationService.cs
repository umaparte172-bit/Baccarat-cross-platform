using System.Net;
using Baccarat.Core.Authentication;
using Baccarat.Core.Diagnostics;

namespace Baccarat.Infrastructure.Authentication;

public sealed class HttpAuthenticationService : IAuthenticationService, IDisposable
{
    private readonly Uri baseUri;
    private readonly TimeSpan timeout;
    private readonly IAuthenticationHttpProtocol protocol;
    private readonly IDiagnosticSink diagnostics;
    private readonly HttpClient client;
    private bool disposed;

    public HttpAuthenticationService(
        Uri baseUri,
        TimeSpan timeout,
        IAuthenticationHttpProtocol protocol,
        IDiagnosticSink? diagnostics = null,
        HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri || !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Authentication requires an absolute HTTPS base URI.", nameof(baseUri));
        if (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be between 1 second and 2 minutes.");

        this.baseUri = EnsureTrailingSlash(baseUri);
        this.timeout = timeout;
        this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        this.diagnostics = diagnostics ?? NullDiagnosticSink.Instance;
        client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false }, disposeHandler: true);
    }

    public Task<AuthenticationResult> SignInAsync(
        SignInRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAuthenticationAsync(
            () => protocol.CreateSignInRequest(request),
            protocol.ReadSignInResponseAsync,
            cancellationToken);
    }

    public Task<AuthenticationResult> SignUpAsync(
        SignUpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAuthenticationAsync(
            () => protocol.CreateSignUpRequest(request),
            protocol.ReadSignUpResponseAsync,
            cancellationToken);
    }

    public async Task SignOutAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(session);

        using var message = protocol.CreateSignOutRequest(session);
        Prepare(message);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        diagnostics.Write(DiagnosticLevel.Information, "authentication.http_request", $"Method={message.Method}");
        using var response = await client.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            linkedSource.Token);
        await protocol.ReadSignOutResponseAsync(response, linkedSource.Token);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        client.Dispose();
    }

    private async Task<AuthenticationResult> SendAuthenticationAsync(
        Func<HttpRequestMessage> createRequest,
        Func<HttpResponseMessage, CancellationToken, ValueTask<AuthenticationResult>> readResponse,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        try
        {
            using var message = createRequest();
            Prepare(message);
            using var timeoutSource = new CancellationTokenSource(timeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            diagnostics.Write(DiagnosticLevel.Information, "authentication.http_request", $"Method={message.Method}");
            using var response = await client.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                linkedSource.Token);
            var standard = ClassifyStandardResponse(response.StatusCode);
            if (standard is not null) return standard;

            var result = await readResponse(response, linkedSource.Token);
            return result ?? new(AuthenticationOutcome.EmptyResponse, "The authentication service returned no result.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            diagnostics.Write(DiagnosticLevel.Warning, "authentication.http_timeout", "The authentication request timed out.");
            return new(AuthenticationOutcome.Unavailable, "The authentication service timed out.");
        }
        catch (HttpRequestException)
        {
            diagnostics.Write(DiagnosticLevel.Warning, "authentication.http_unavailable", "The authentication service could not be reached.");
            return new(AuthenticationOutcome.Unavailable, "The authentication service is unavailable.");
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException)
        {
            diagnostics.Write(DiagnosticLevel.Warning, "authentication.http_malformed", "The authentication response was malformed.");
            return new(AuthenticationOutcome.MalformedResponse, "The authentication service returned an invalid response.");
        }
    }

    private void Prepare(HttpRequestMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.RequestUri is null)
            throw new InvalidOperationException("The authentication protocol did not provide a request URI.");

        var target = message.RequestUri.IsAbsoluteUri ? message.RequestUri : new Uri(baseUri, message.RequestUri);
        if (!IsSameOrigin(target, baseUri))
            throw new InvalidOperationException("The authentication protocol attempted to use a different origin.");

        message.RequestUri = target;
    }

    private static AuthenticationResult? ClassifyStandardResponse(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new(AuthenticationOutcome.Rejected, "The email or password was not accepted."),
        HttpStatusCode.NoContent =>
            new(AuthenticationOutcome.EmptyResponse, "The authentication service returned an empty response."),
        HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError =>
            new(AuthenticationOutcome.Unavailable, "The authentication service is temporarily unavailable."),
        >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest =>
            new(AuthenticationOutcome.Unavailable, "The authentication service returned an unsupported redirect."),
        _ => null
    };

    private static Uri EnsureTrailingSlash(Uri value) =>
        value.AbsolutePath.EndsWith('/') ? value : new Uri(value.AbsoluteUri + "/");

    private static bool IsSameOrigin(Uri candidate, Uri expected) =>
        string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candidate.Host, expected.Host, StringComparison.OrdinalIgnoreCase) &&
        candidate.Port == expected.Port;
}
