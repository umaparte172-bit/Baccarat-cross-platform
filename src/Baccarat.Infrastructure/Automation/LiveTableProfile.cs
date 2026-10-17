using System.Text.Json;
using System.Text.Json.Serialization;
using Baccarat.Core;

namespace Baccarat.Infrastructure.Automation;

public sealed record LiveOutcomeRule(string Contains, Outcome Outcome);
public sealed record LivePairRule(string Contains, bool Banker);

// Selector syntax: CSS by default, or "xpath:<expression>". Describes how to read and operate one table page.
// Nothing is guessed: an empty selector means the capability is not configured and the adapter refuses to use it.
public sealed record LiveTableProfile
{
    public string Name { get; init; } = "";
    public string StartUrl { get; init; } = "";
    // The page host must contain this text; every read and click re-checks it. Required.
    public string HostContains { get; init; } = "";
    // Table rendered inside an iframe (same-origin or cross-origin); matched against the frame URL.
    public string? FrameUrlContains { get; init; }
    public string StatusSelector { get; init; } = "";
    public string OpenText { get; init; } = "PLACE YOUR BETS";
    public string[] ClosedText { get; init; } = ["WAIT FOR NEXT GAME", "NO MORE BETS"];
    public string ChipSelector { get; init; } = "";
    // "text" or the name of an attribute holding the chip value, e.g. data-value.
    public string ChipValueSource { get; init; } = "text";
    public string PlayerSelector { get; init; } = "";
    public string BankerSelector { get; init; } = "";
    public string TieSelector { get; init; } = "";
    // Shows the stake currently on the table; used to confirm a placement before reporting it accepted.
    public string BetTotalSelector { get; init; } = "";
    public string ResultSelector { get; init; } = "";
    // Which element of ResultSelector is the newest result.
    public string NewestResult { get; init; } = "last";
    public LiveOutcomeRule[] ResultRules { get; init; } = [];
    public LivePairRule[] PairRules { get; init; } = [];
    public string BalanceSelector { get; init; } = "";
    public int PollMilliseconds { get; init; } = 500;
    public int ClickDelayMilliseconds { get; init; } = 120;

    public bool CanRead => ResultSelector.Length > 0 && ResultRules.Length > 0;
    public bool CanBet => ChipSelector.Length > 0 && PlayerSelector.Length > 0 && BankerSelector.Length > 0 &&
        StatusSelector.Length > 0 && BetTotalSelector.Length > 0;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Profile needs a name.");
        if (string.IsNullOrWhiteSpace(HostContains) || HostContains.Contains('/'))
            throw new InvalidDataException("Profile needs a host name filter (for example: example.com).");
        if (StartUrl.Length > 0 && (!Uri.TryCreate(StartUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !uri.Host.Contains(HostContains, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Start URL must be http(s) and match the host filter.");
    }

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };
}

// Profiles live as JSON next to the workspace so they can be calibrated without rebuilding the app.
public sealed class LiveTableProfileStore
{
    public string Directory { get; }

    public LiveTableProfileStore(string? directory = null)
    {
        Directory = directory ?? Environment.GetEnvironmentVariable("BACCARAT_LIVE_PROFILES") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BaccaratWorkspace", "live-profiles");
    }

    public IReadOnlyList<LiveTableProfile> Load()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var template = System.IO.Path.Combine(Directory, "template-calibrate-me.json");
        if (!File.Exists(template)) File.WriteAllText(template, JsonSerializer.Serialize(Template, LiveTableProfile.Json));
        var profiles = new List<LiveTableProfile>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json").Order())
        {
            try
            {
                var profile = JsonSerializer.Deserialize<LiveTableProfile>(File.ReadAllText(file), LiveTableProfile.Json);
                if (profile is null) continue;
                profile.Validate();
                profiles.Add(profile);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException) { }
        }
        return profiles;
    }

    // Deliberately unconfigured: selectors must come from inspecting the real table page.
    public static LiveTableProfile Template { get; } = new()
    {
        Name = "Template (calibrate before use)",
        HostContains = "example.com",
        StartUrl = "https://example.com/",
        FrameUrlContains = null,
        ResultRules =
        [
            new("player", Outcome.Player), new("banker", Outcome.Banker), new("tie", Outcome.Tie)
        ],
        PairRules = [new("banker-pair", true), new("player-pair", false)]
    };
}
