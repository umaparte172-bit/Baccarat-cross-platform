using System.Text.Json;
using System.Text.Json.Serialization;
using Baccarat.Core;

namespace Baccarat.Infrastructure.Automation;

public sealed record LiveOutcomeRule(string Contains, Outcome Outcome);
public sealed record LivePairRule(string Contains, bool Banker);
// A message from a canvas table's WebSocket that carries a finished round (regular expression over the message text).
public sealed record LiveSocketRule(string Pattern, Outcome Outcome, bool BankerPair = false, bool PlayerPair = false);
// A chip that cannot be read from the page (canvas tables): its value and where to click it, e.g. "point:canvas@0.40,0.90".
public sealed record LiveChip(decimal Value, string Click);

// Selector syntax: CSS by default, or "xpath:<expression>". Describes how to read and operate one table page.
// Nothing is guessed: an empty selector means the capability is not configured and the adapter refuses to use it.
public sealed record LiveTableProfile
{
    public string Name { get; init; } = "";
    public string StartUrl { get; init; } = "";
    // Optional restriction to sites whose host contains this text. When empty the app is not tied to any site:
    // it recognises a table by its page content (DetectText / DetectSelector) and then locks to that tab's host.
    public string HostContains { get; init; } = "";
    // The table is detected when every text below appears in a page or frame (case-insensitive)...
    public string[] DetectText { get; init; } = ["BANKER", "PLAYER"];
    // ...and, when set, this selector matches at least one element.
    public string DetectSelector { get; init; } = "";
    // Table rendered inside an iframe (same-origin or cross-origin); matched against the frame URL.
    public string? FrameUrlContains { get; init; }
    public string StatusSelector { get; init; } = "";
    public string OpenText { get; init; } = "PLACE YOUR BETS";
    public string[] ClosedText { get; init; } = ["WAIT FOR NEXT GAME", "NO MORE BETS"];
    public string ChipSelector { get; init; } = "";
    public LiveChip[] StaticChips { get; init; } = [];
    public LiveSocketRule[] SocketResultRules { get; init; } = [];
    // Socket.io style traffic is normalised to "EVENT_NAME {json payload}" before these regular expressions run.
    public string SocketPlayerPairPattern { get; init; } = "";
    public string SocketBankerPairPattern { get; init; } = "";
    // Betting window from messages: open while the last matching message is an "open" one.
    public string SocketOpenPattern { get; init; } = "";
    public string SocketClosedPattern { get; init; } = "";
    // Placement confirmation from messages: the gate pattern must match, and the amount pattern (named groups
    // "side" and "amount") lists what the table accepted. Nothing is reported Accepted without it (or a bet-total selector).
    public string SocketBetAckPattern { get; init; } = "";
    public string SocketBetAmountPattern { get; init; } = "";
    // Clicked once after connecting (for example a "touch anywhere to start" screen on a canvas).
    public string StartClick { get; init; } = "";
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
    // A bet approved while the table is between rounds waits this long for the next betting window (0 = refuse at once).
    public int PlacementWaitSeconds { get; init; } = 25;
    public int PollMilliseconds { get; init; } = 500;
    public int ClickDelayMilliseconds { get; init; } = 120;

    public bool CanRead => (ResultSelector.Length > 0 && ResultRules.Length > 0) || SocketResultRules.Length > 0;
    public bool CanBet => (ChipSelector.Length > 0 || StaticChips.Length > 0) && PlayerSelector.Length > 0 && BankerSelector.Length > 0 &&
        (StatusSelector.Length > 0 || SocketOpenPattern.Length > 0) &&
        (BetTotalSelector.Length > 0 || (SocketBetAckPattern.Length > 0 && SocketBetAmountPattern.Length > 0));

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Profile needs a name.");
        if (HostContains.Contains('/')) throw new InvalidDataException("Host filter must be a host name, not a URL.");
        if (DetectText.Length == 0 && DetectSelector.Length == 0)
            throw new InvalidDataException("Profile needs DetectText or DetectSelector so a table can be recognised.");
        if (StartUrl.Length > 0 && (!Uri.TryCreate(StartUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            (HostContains.Length > 0 && !uri.Host.Contains(HostContains, StringComparison.OrdinalIgnoreCase))))
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
        Directory = directory ?? Environment.GetEnvironmentVariable("BACCARAT_LIVE_PROFILES") ?? AppDataLocation.Combine("BaccaratWorkspace", "live-profiles");
    }

    // Why the last Load() skipped or could not write something (shown to the user instead of an empty list).
    public IReadOnlyList<string> Problems { get; private set; } = [];

    public IReadOnlyList<LiveTableProfile> Load()
    {
        var problems = new List<string>();
        var builtIns = new (string File, LiveTableProfile Profile)[]
        {
            ("template-calibrate-me.json", Template),
            ("evolution-baccarat-results-only.json", EvolutionResultsOnly),
            ("evolution-baccarat-live-betting.json", EvolutionBetting),
            ("multiplier-baccarat-demo-canvas.json", MultiplierBaccaratDemo)
        };
        var profiles = new List<LiveTableProfile>();
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            foreach (var (name, profile) in builtIns)
            {
                var path = System.IO.Path.Combine(Directory, name);
                // The stock template is rewritten when an older build left one that is tied to a placeholder site.
                var stale = name.StartsWith("template", StringComparison.Ordinal) && File.Exists(path) &&
                    File.ReadAllText(path).Contains("example.com", StringComparison.OrdinalIgnoreCase);
                if (!File.Exists(path) || stale) File.WriteAllText(path, JsonSerializer.Serialize(profile, LiveTableProfile.Json));
            }
            foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json").OrderBy(file => file, StringComparer.Ordinal))
            {
                try
                {
                    var profile = JsonSerializer.Deserialize<LiveTableProfile>(File.ReadAllText(file), LiveTableProfile.Json);
                    if (profile is null) continue;
                    profile.Validate();
                    profiles.Add(profile);
                }
                catch (Exception ex)
                {
                    problems.Add($"{System.IO.Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            problems.Add($"Profile folder {Directory} is not usable: {ex.Message}");
        }
        // The built-in profiles are always offered, even when the folder cannot be read or written.
        foreach (var (_, profile) in builtIns)
            if (!profiles.Any(p => p.Name == profile.Name)) profiles.Add(profile);
        Problems = problems;
        return profiles;
    }

    public static LiveTableProfile EvolutionResultsOnly { get; } = new()
    {
        Name = "Evolution baccarat (results only)",
        DetectText = ["BANKER", "PLAYER"],
        DetectSelector = "[data-role=\"Bead-road\"]",
        ResultSelector = "[data-role=\"Bead-road\"] [data-role=\"roadItem\"]",
        NewestResult = "last",
        ResultRules = [new("=p", Outcome.Player), new("=b", Outcome.Banker), new("=t", Outcome.Tie)]
    };

    // Calibrated from an inspection of Evolution Speed Baccarat on a live table (data-role attributes).
    // The betting-open indicator is the footer subtitle text; it was empty in the captured round, so it is unverified:
    // until it reads "PLACE YOUR BETS" the adapter refuses to place anything.
    public static LiveTableProfile EvolutionBetting { get; } = EvolutionResultsOnly with
    {
        Name = "Evolution baccarat (live betting)",
        StatusSelector = "[data-role=\"footer-subtitles\"]",
        OpenText = "PLACE YOUR BETS",
        ClosedText = ["WAIT FOR NEXT GAME", "NO MORE BETS", "BETS CLOSED", "PLEASE WAIT"],
        ChipSelector = "[data-role=\"expanded-chip-stack-wrapper\"] [data-role=\"chip\"]",
        ChipValueSource = "data-value",
        PlayerSelector = "[data-role=\"bet-spot-Player\"]",
        BankerSelector = "[data-role=\"bet-spot-Banker\"]",
        TieSelector = "[data-role=\"bet-spot-Tie\"]",
        BetTotalSelector = "[data-role=\"total-bet-label-value\"]",
        BalanceSelector = "[data-role=\"balance-label-value\"]"
    };

    // Calibrated from the free play-money demo (FUN currency): everything is drawn on one canvas, so state comes from the
    // table's Socket.io messages and clicks are canvas points (fractions of the canvas box).
    public static LiveTableProfile MultiplierBaccaratDemo { get; } = new()
    {
        Name = "Multiplier Baccarat (demo canvas)",
        DetectText = [],
        DetectSelector = "canvas",
        StartClick = "point:canvas@0.5,0.3",
        StaticChips =
        [
            new(1, "point:canvas@0.400,0.926"), new(5, "point:canvas@0.450,0.926"), new(10, "point:canvas@0.500,0.926"),
            new(50, "point:canvas@0.550,0.926"), new(100, "point:canvas@0.600,0.926")
        ],
        PlayerSelector = "point:canvas@0.415,0.62",
        BankerSelector = "point:canvas@0.582,0.62",
        TieSelector = "point:canvas@0.500,0.67",
        SocketOpenPattern = "^ROUND_BETTING \\{",
        SocketClosedPattern = "^(ROUND_BETTING_END|ROUND_DEALCARD|ROUND_SETTLEMENT|ROUND_RESULT)\\b",
        SocketBetAckPattern = "^RES_BET .*\"result_code\":0",
        SocketBetAmountPattern = "\"type\":\"(?<side>PLAYER|BANKER)\",\"amount\":(?<amount>[0-9.]+)",
        SocketPlayerPairPattern = "\"is_player_pair\":true",
        SocketBankerPairPattern = "\"is_banker_pair\":true",
        SocketResultRules =
        [
            new("^ROUND_SETTLEMENT .*\"winner\":0\\b", Outcome.Player),
            new("^ROUND_SETTLEMENT .*\"winner\":3\\b", Outcome.Banker),
            new("^ROUND_SETTLEMENT .*\"winner\":2\\b", Outcome.Tie)
        ]
    };

    // Deliberately unconfigured: selectors must come from inspecting the real table page.
    public static LiveTableProfile Template { get; } = new()
    {
        Name = "Template (calibrate before use)",
        HostContains = "",
        StartUrl = "",
        FrameUrlContains = null,
        ResultRules =
        [
            new("player", Outcome.Player), new("banker", Outcome.Banker), new("tie", Outcome.Tie)
        ],
        PairRules = [new("banker-pair", true), new("player-pair", false)]
    };
}
