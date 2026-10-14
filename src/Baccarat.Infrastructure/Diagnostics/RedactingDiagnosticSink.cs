using System.Text.RegularExpressions;
using Baccarat.Core.Diagnostics;

namespace Baccarat.Infrastructure.Diagnostics;

public sealed class RedactingDiagnosticSink : IDiagnosticSink
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex BearerToken = new(
        "(bearer\\s+)[A-Za-z0-9._~+\\-/]+=*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex SensitiveAssignment = new(
        "(password|token|secret|api[_-]?key)\\s*[:=]\\s*[^,;\\s]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex EmailAddress = new(
        "\\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}\\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private readonly Action<string> writer;

    public RedactingDiagnosticSink(Action<string> writer) =>
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public void Write(DiagnosticLevel level, string eventName, string message)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("The event name is required.", nameof(eventName));
        writer($"{level}|{eventName}|{Redact(message ?? string.Empty)}");
    }

    public static string Redact(string value)
    {
        var redacted = BearerToken.Replace(value, "$1[REDACTED]");
        redacted = SensitiveAssignment.Replace(redacted, "$1[REDACTED]");
        return EmailAddress.Replace(redacted, "[REDACTED_EMAIL]");
    }
}
