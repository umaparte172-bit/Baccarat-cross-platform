namespace Baccarat.Core.Configuration;

public sealed record NetworkClientOptions(
    Uri? HttpBaseUri,
    Uri? WebSocketUri,
    TimeSpan RequestTimeout)
{
    public static NetworkClientOptions Disconnected { get; } = new(null, null, TimeSpan.FromSeconds(20));
    public bool IsConfigured => HttpBaseUri is not null && WebSocketUri is not null;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if ((HttpBaseUri is null) != (WebSocketUri is null))
            errors.Add("HTTP and WebSocket endpoints must be configured together.");
        if (HttpBaseUri is not null && !string.Equals(HttpBaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            errors.Add("The HTTP endpoint must use HTTPS.");
        if (WebSocketUri is not null && !string.Equals(WebSocketUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            errors.Add("The WebSocket endpoint must use WSS.");
        if (RequestTimeout < TimeSpan.FromSeconds(1) || RequestTimeout > TimeSpan.FromMinutes(2))
            errors.Add("Request timeout must be between 1 second and 2 minutes.");
        return errors;
    }
}
