using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Baccarat.Core;
using Baccarat.Core.Automation;
using Baccarat.Core.Strategies;

namespace Baccarat.Infrastructure.Automation;

public sealed record LiveBrowserOptions(int DebugPort = 9014, string? ProfileDirectory = null, string? BrowserPath = null, bool LaunchBrowser = true);

// Drives a real table page in the user's own Chrome/Edge (remote debugging on loopback, dedicated profile).
// The user signs in and opens the table themselves; this class never sees credentials. It only touches a page
// whose host matches the profile, re-checks that host before every read and click, and never retries a placement.
public sealed partial class LiveBrowserTableAdapter : IAutomationTableAdapter, ILiveTableSource, IAsyncDisposable
{
    private readonly LiveTableProfile profile;
    private readonly LiveBrowserOptions options;
    private readonly Dictionary<Guid, BetPlacementResult> completed = [];
    private readonly List<decimal> selection = [];
    private readonly Dictionary<string, string> frames = []; // child session -> frame url
    private readonly Dictionary<string, string> parents = []; // child session -> parent session
    private readonly Dictionary<string, string> targetIds = []; // child session -> its root frame id
    private readonly Dictionary<string, string> parentFrameIds = []; // child session -> frame (in the parent session) holding it
    public event Action<string>? Progress;
    private readonly List<string> socketLog = [];
    private readonly Dictionary<string, string> socketUrls = [];
    private int socketResults;
    private bool? socketOpen;
    private decimal socketStake;
    private int ackCount;
    private readonly Dictionary<string, decimal> ackTotals = []; // side -> total the table confirmed since the window opened
    private CdpConnection? cdp;
    private Process? launched;
    private string? pageSession;
    private string? lockedHost;
    public string? DetectedHost => lockedHost;
    private List<(decimal Value, int Index)> chipIndex = [];
    private CancellationTokenSource? polling;
    private string? lastResultSignature;
    private bool? bettingOpen;
    private string? balance;
    private readonly SemaphoreSlim page = new(1, 1);

    public LiveBrowserTableAdapter(LiveTableProfile profile, LiveBrowserOptions? options = null)
    {
        profile.Validate();
        this.profile = profile;
        this.options = options ?? new LiveBrowserOptions();
        if (this.options.DebugPort is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(options));
    }

    public string Name => "Live table · " + profile.Name;
    public bool IsSimulation => false;
    public bool IsConnected => cdp?.IsOpen == true && pageSession is not null;
    public bool? IsBettingOpen => bettingOpen;
    public string? Balance => balance;
    public event Action<LiveRoundResult>? ResultObserved;
    public event Action<string>? Faulted;

    // Starts the dedicated browser (or reuses one already listening) on the profile's start page.
    public async Task OpenBrowserAsync(CancellationToken token = default, string? pageUrl = null)
    {
        if (pageUrl is not null && (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var wanted) || wanted.Scheme is not ("http" or "https")))
            throw new InvalidOperationException("Enter a full http(s) address, for example https://example.com/table.");
        if (await TryVersionAsync(token) is not null)
        {
            // Browser already running: open the requested page in a new tab of it.
            if (pageUrl is not null)
            {
                using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
                using var request = new HttpRequestMessage(HttpMethod.Put, $"http://127.0.0.1:{options.DebugPort}/json/new?{pageUrl}");
                using var response = await client.SendAsync(request, token);
                response.EnsureSuccessStatusCode();
            }
            return;
        }
        if (!options.LaunchBrowser) throw new InvalidOperationException("No browser is listening on the debugging port.");
        var executable = options.BrowserPath ?? BrowserLocator.FindInstalled() ?? throw new InvalidOperationException(BrowserLocator.MissingBrowserMessage);
        var directory = options.ProfileDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BaccaratWorkspace", "live-browser-profile");
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var arg in new[] { $"--remote-debugging-port={options.DebugPort}", "--remote-debugging-address=127.0.0.1",
            "--user-data-dir=" + directory, "--no-first-run", "--no-default-browser-check", "--new-window",
            // Games on a canvas only advance while the page is rendering: keep it alive even when another window covers it.
            "--disable-backgrounding-occluded-windows", "--disable-renderer-backgrounding", "--disable-background-timer-throttling", "--disable-features=CalculateNativeWinOcclusion",
            pageUrl ?? (profile.StartUrl.Length > 0 ? profile.StartUrl : "about:blank") })
            start.ArgumentList.Add(arg);
        launched = Process.Start(start) ?? throw new InvalidOperationException("Could not start the browser.");
        for (var i = 0; i < 100; i++)
        {
            if (await TryVersionAsync(token) is not null) return;
            await Task.Delay(150, token);
        }
        throw new InvalidOperationException("The browser did not open its local debugging port.");
    }

    private async Task<JsonElement?> TryVersionAsync(CancellationToken token)
    {
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
            using var doc = JsonDocument.Parse(await client.GetStringAsync($"http://127.0.0.1:{options.DebugPort}/json/version", token));
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return null; }
    }

    private async Task EnsureBrowserConnectionAsync(CancellationToken token)
    {
        if (cdp?.IsOpen == true) return;
        if (cdp is not null) { await cdp.DisposeAsync(); cdp = null; }
        var version = await TryVersionAsync(token) ?? throw new AutomationDisconnectedException(
            "Open the live browser first, then sign in and open any live table in it.");
        var endpoint = new Uri(version.GetProperty("webSocketDebuggerUrl").GetString()!);
        if (endpoint.Port != options.DebugPort) throw new AutomationDisconnectedException("Unexpected debugger address.");
        var connection = new CdpConnection();
        connection.Event += OnEvent;
        connection.Closed += _ =>
        {
            if (cdp != connection) return;
            Volatile.Write(ref pageSession, null);
            Faulted?.Invoke("The live browser disconnected. No action will be repeated.");
        };
        try { await connection.ConnectAsync(endpoint, token); }
        catch { await connection.DisposeAsync(); throw; }
        cdp = connection;
    }

    // Watches every open tab until one shows a baccarat table (by the profile's detection rules), then locks to it.
    // The user may open the table at any time and on any site; nothing is navigated or clicked while searching.
    public async Task WaitForTableAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await EnsureBrowserConnectionAsync(token);
            if (pageSession is not null) return;
            if (await ScanAsync(token)) return;
            await Task.Delay(1000, token);
        }
        token.ThrowIfCancellationRequested();
    }

    private async Task<bool> ScanAsync(CancellationToken token)
    {
        var targets = await cdp!.SendAsync("Target.getTargets");
        var seen = new List<string>();
        var frameReport = new List<string>();
        var maxDepth = 0;
        var unusable = new List<string>();
        foreach (var target in targets.GetProperty("targetInfos").EnumerateArray())
        {
            if (target.GetProperty("type").GetString() != "page") continue;
            var url = target.GetProperty("url").GetString();
            if (!Eligible(url))
            {
                var blocked = Uri.TryCreate(url, UriKind.Absolute, out var skipped) && skipped.Scheme is "http" or "https";
                unusable.Add(blocked ? $"{skipped!.Host} (skipped: this profile only allows hosts containing \"{profile.HostContains}\")" : Describe(url));
                continue;
            }
            string? session = null;
            seen.Add(new Uri(url!).Host);
            try
            {
                var attached = await cdp.SendAsync("Target.attachToTarget", new { targetId = target.GetProperty("targetId").GetString(), flatten = true });
                session = attached.GetProperty("sessionId").GetString()!;
                lock (frames) { frames.Clear(); parents.Clear(); targetIds.Clear(); parentFrameIds.Clear(); }
                await cdp.SendAsync("Target.setAutoAttach", new { autoAttach = true, waitForDebuggerOnStart = false, flatten = true }, session);
                for (var settle = -1; ;)
                {
                    await Task.Delay(400, token);
                    int count; lock (frames) count = frames.Count;
                    if (count == settle) break;
                    settle = count;
                }
                var all = await EnumerateFramesAsync(session);
                frameReport.AddRange(all.Where(f => f.Depth > 0 || f.Session != session).Select(f => f.Host).Where(h => h.Length > 0));
                maxDepth = Math.Max(maxDepth, all.Count == 0 ? 0 : all.Max(f => f.Depth));
                if (await DetectFrameAsync(all) is { } found)
                {
                    lockedHost = new Uri(url!).Host;
                    pageSession = session;
                    tableFrame = found;
                    try
                    {
                        await cdp.SendAsync("Network.enable", null, session);
                        string[] children; lock (frames) children = frames.Keys.ToArray();
                        foreach (var child in children) await cdp.SendAsync("Network.enable", null, child);
                    }
                    catch (Exception ex) when (ex is CdpException or IOException) { }
                    return true;
                }
            }
            catch (Exception ex) when (ex is CdpException or InvalidOperationException) { }
            if (session is not null && pageSession != session)
            {
                try { await cdp.SendAsync("Target.detachFromTarget", new { sessionId = session }); } catch (Exception ex) when (ex is CdpException or IOException) { }
            }
        }
        Progress?.Invoke(seen.Count == 0
            ? "Watching… the live browser (the separate window opened by Open live browser) has no website open"
              + (unusable.Count == 0 ? "." : $"; its tabs show: {string.Join(", ", unusable.Distinct())}.")
              + " Your normal Chrome cannot be watched: open the table in that window, or paste its address above and press Open live browser."
            : $"Watching… {seen.Count} tab(s) checked ({string.Join(", ", seen.Distinct())}); {frameReport.Count} embedded frame(s), nested up to {maxDepth} level(s) ({string.Join(", ", frameReport.Distinct().Take(8))}); no table text found yet. If the site needs a sign-in to show the table, sign in inside that window.");
        return false;
    }

    // Human description of a tab we cannot read (blank, browser page, failed load).
    public static string Describe(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "(empty)";
        if (url.StartsWith("chrome-error:", StringComparison.OrdinalIgnoreCase)) return "a page that failed to load (network, VPN or proxy?)";
        if (url == "about:blank") return "a blank page";
        return Uri.TryCreate(url, UriKind.Absolute, out var u) ? (u.Scheme is "http" or "https" ? u.Host : u.Scheme + ": page") : "an unknown page";
    }

    public sealed record BrowserTab(string Id, string Type, string Url, string Title);

    // Plain view of what the dedicated browser has open (does not attach to anything).
    public async Task<IReadOnlyList<BrowserTab>> ListTabsAsync(CancellationToken token = default)
    {
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
            using var doc = JsonDocument.Parse(await client.GetStringAsync($"http://127.0.0.1:{options.DebugPort}/json/list", token));
            return doc.RootElement.EnumerateArray()
                .Where(t => t.GetProperty("type").GetString() == "page")
                .Select(t => new BrowserTab(t.GetProperty("id").GetString() ?? "", "page", t.GetProperty("url").GetString() ?? "",
                    t.TryGetProperty("title", out var title) ? title.GetString() ?? "" : ""))
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return []; }
    }

    // Brings a tab's window to the front so the user can see which browser the app is watching.
    public async Task ActivateTabAsync(string id, CancellationToken token = default)
    {
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
            await client.GetStringAsync($"http://127.0.0.1:{options.DebugPort}/json/activate/{Uri.EscapeDataString(id)}", token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
    }

    private bool Eligible(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
        (profile.HostContains.Length == 0 || uri.Host.Contains(profile.HostContains, StringComparison.OrdinalIgnoreCase));

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        await EnsureBrowserConnectionAsync(cancellationToken);
        if (pageSession is null && !await ScanAsync(cancellationToken))
            throw new AutomationDisconnectedException("No open tab shows a baccarat table yet. Open a live table in the live browser.");
        polling?.Cancel();
        lastResultSignature = null;
        socketOpen = null;
        if (profile.StartClick.Length > 0)
        {
            // e.g. "touch anywhere to start": one harmless click before reading begins.
            await page.WaitAsync(cancellationToken);
            try { await ClickAsync(profile.StartClick, 0); }
            catch (Exception ex) when (ex is InvalidOperationException or CdpException) { }
            finally { page.Release(); }
        }
        await PollOnceAsync(cancellationToken, announce: false);
        polling = new CancellationTokenSource();
        var token = polling.Token;
        _ = Task.Run(() => PollLoopAsync(token));
    }

    // Host of the page itself (not a cross-origin table frame) must still be the one we locked to.
    private async Task EnsurePageHostAsync()
    {
        var host = await cdp!.EvaluateAsync("location.hostname", pageSession);
        if (host.ValueKind != JsonValueKind.String || !string.Equals(host.GetString(), lockedHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The table page left the site it was detected on.");
    }

    private void OnEvent(string method, JsonElement args, string? session)
    {
        if (method.StartsWith("Network.webSocket", StringComparison.Ordinal)) { OnSocketEvent(method, args); return; }
        if (method == "Target.attachedToTarget")
        {
            var info = args.GetProperty("targetInfo");
            var child = args.GetProperty("sessionId").GetString()!;
            if (info.GetProperty("type").GetString() == "iframe")
            {
                lock (frames) { frames[child] = info.GetProperty("url").GetString() ?? ""; targetIds[child] = info.GetProperty("targetId").GetString() ?? ""; parentFrameIds[child] = info.TryGetProperty("parentFrameId", out var pf) ? pf.GetString() ?? "" : ""; if (session is not null) parents[child] = session; }
                // Providers nest frames (site -> provider -> game); every level needs its own auto-attach.
                _ = AttachNestedAsync(child);
            }
        }
        else if (method == "Target.detachedFromTarget")
            lock (frames) { var gone = args.GetProperty("sessionId").GetString()!; frames.Remove(gone); parents.Remove(gone); targetIds.Remove(gone); parentFrameIds.Remove(gone); }
    }

    // Canvas tables draw everything on a canvas, so the only readable state is the traffic of their WebSocket. Keep the
    // most recent messages for result rules and for calibration (Inspect page saves them).
    private void OnSocketEvent(string method, JsonElement args)
    {
        if (method == "Network.webSocketCreated")
        {
            lock (socketLog) socketUrls[args.GetProperty("requestId").GetString() ?? ""] = args.GetProperty("url").GetString() ?? "";
            return;
        }
        if (method is not ("Network.webSocketFrameReceived" or "Network.webSocketFrameSent")) return;
        var received = method.EndsWith("Received", StringComparison.Ordinal);
        var response = args.GetProperty("response");
        var payload = response.GetProperty("payloadData").GetString() ?? "";
        var text = response.GetProperty("opcode").GetInt32() == 2 ? "[binary, base64] " + payload[..Math.Min(payload.Length, 240)] : NormalizeSocketText(payload);
        lock (socketLog)
        {
            socketUrls.TryGetValue(args.GetProperty("requestId").GetString() ?? "", out var url);
            var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "";
            socketLog.Add($"{DateTime.UtcNow:HH:mm:ss.fff} {(received ? "recv" : "send")} {host} {text[..Math.Min(text.Length, 700)]}");
            if (socketLog.Count > 400) socketLog.RemoveRange(0, socketLog.Count - 400);
        }
        if (received) TrackSocketState(text);
        if (received && profile.SocketResultRules.Length > 0) HandleSocketResult(text);
    }

    // PNG of the watched tab (for calibration of canvas tables, where there is no DOM to read).
    public async Task<byte[]> ScreenshotAsync(CancellationToken token = default)
    {
        EnsureConnected(token);
        var shot = await cdp!.SendAsync("Page.captureScreenshot", new { format = "png" }, pageSession);
        return Convert.FromBase64String(shot.GetProperty("data").GetString() ?? "");
    }

    // Recent WebSocket messages (newest last): for calibration and for tests.
    public IReadOnlyList<string> SocketMessages()
    {
        lock (socketLog) return socketLog.ToArray();
    }

    // Socket.io frames look like 42["EVENT","{...json...}"]: turn them into  EVENT {...json...}  so patterns stay readable.
    internal static string NormalizeSocketText(string payload)
    {
        if (!payload.StartsWith("42[", StringComparison.Ordinal)) return payload;
        try
        {
            using var doc = JsonDocument.Parse(payload[2..]);
            var items = doc.RootElement;
            var name = items[0].GetString() ?? "";
            if (items.GetArrayLength() < 2) return name;
            var data = items[1];
            return name + " " + (data.ValueKind == JsonValueKind.String ? data.GetString() : data.GetRawText());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return payload; }
    }

    private static bool Matches(string pattern, string text)
    {
        if (pattern.Length == 0) return false;
        try { return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    // Betting window and bet acknowledgements as announced by the table.
    private void TrackSocketState(string text)
    {
        if (Matches(profile.SocketOpenPattern, text))
        {
            if (socketOpen != true) { socketStake = 0; lock (ackTotals) ackTotals.Clear(); }
            socketOpen = true;
        }
        else if (Matches(profile.SocketClosedPattern, text)) socketOpen = false;
        if (profile.SocketBetAckPattern.Length > 0 && Matches(profile.SocketBetAckPattern, text))
        {
            var amounts = new List<(string, decimal)>();
            foreach (Match m in Regex.Matches(text, profile.SocketBetAmountPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)))
                if (decimal.TryParse(m.Groups["amount"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                    amounts.Add((m.Groups["side"].Value.ToUpperInvariant(), amount));
            lock (ackTotals)
            {
                // The table reports the running total on each side (seen on the demo: 5, then 6 after one more chip).
                foreach (var (side, amount) in amounts) ackTotals[side] = amount;
                socketStake = ackTotals.Values.Sum();
            }
            Interlocked.Increment(ref ackCount);
        }
    }

    private void HandleSocketResult(string text)
    {
        foreach (var rule in profile.SocketResultRules)
        {
            bool matched;
            try { matched = Regex.IsMatch(text, rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)); }
            catch (RegexMatchTimeoutException) { matched = false; }
            if (!matched) continue;
            var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16] + ":" + Interlocked.Increment(ref socketResults);
            ResultObserved?.Invoke(new LiveRoundResult(key, rule.Outcome,
                rule.BankerPair || Matches(profile.SocketBankerPairPattern, text), rule.PlayerPair || Matches(profile.SocketPlayerPairPattern, text)));
            return;
        }
    }

    private async Task AttachNestedAsync(string child)
    {
        try
        {
            await cdp!.SendAsync("Network.enable", null, child);
            await cdp.SendAsync("Target.setAutoAttach", new { autoAttach = true, waitForDebuggerOnStart = false, flatten = true }, child);
        }
        catch (Exception ex) when (ex is CdpException or IOException or NullReferenceException) { }
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default) => await DisconnectCoreAsync();

    private async Task DisconnectCoreAsync()
    {
        polling?.Cancel();
        polling = null;
        pageSession = null;
        lockedHost = null;
        tableFrame = null;
        contexts.Clear();
        pagesEnabled.Clear();
        selection.Clear();
        if (cdp is not null) { await cdp.DisposeAsync(); cdp = null; }
    }

    // Only for browsers this adapter started; a user-opened browser is left running.
    public async Task CloseBrowserAsync()
    {
        if (cdp?.IsOpen == true) { try { await cdp.SendAsync("Browser.close"); } catch (IOException) { } }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectCoreAsync();
        launched?.Dispose();
    }

    // ---- Reading ------------------------------------------------------------------------------------------

    private async Task PollLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(Math.Clamp(profile.PollMilliseconds, 100, 5000), token);
                await PollOnceAsync(token, announce: true);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or CdpException or InvalidOperationException)
        {
            Faulted?.Invoke("Lost the live table: " + ex.Message);
        }
    }

    private async Task PollOnceAsync(CancellationToken token, bool announce)
    {
        await page.WaitAsync(token);
        try
        {
            var state = await EvaluateInTableAsync($$"""
                const status = {{Js(profile.StatusSelector)}} ? all({{Js(profile.StatusSelector)}}).map(e => (e.textContent||'').trim()).join(' | ') : null;
                const results = {{Js(profile.ResultSelector)}} ? all({{Js(profile.ResultSelector)}}).map(blob) : [];
                const bal = {{Js(profile.BalanceSelector)}} ? (all({{Js(profile.BalanceSelector)}})[0]?.textContent||'').trim() : null;
                return {status, results, bal};
                """);
            await EnsurePageHostAsync();
            bettingOpen = profile.StatusSelector.Length == 0 && profile.SocketOpenPattern.Length > 0 ? socketOpen
                : state.GetProperty("status").ValueKind == JsonValueKind.Null ? null : ParseOpen(state.GetProperty("status").GetString()!);
            balance = state.GetProperty("bal").ValueKind == JsonValueKind.String ? state.GetProperty("bal").GetString() : null;
            var results = state.GetProperty("results").EnumerateArray().Select(r => r.GetString() ?? "").ToArray();
            var signature = string.Join('\n', results);
            var previous = lastResultSignature;
            lastResultSignature = signature;
            if (!announce || previous is null || previous == signature || results.Length == 0) return;
            var newest = profile.NewestResult.Equals("first", StringComparison.OrdinalIgnoreCase) ? results[0] : results[^1];
            if (ParseResult(newest, results.Length, signature) is { } result) ResultObserved?.Invoke(result);
        }
        finally { page.Release(); }
    }

    private bool? ParseOpen(string status)
    {
        if (profile.ClosedText.Any(t => status.Contains(t, StringComparison.OrdinalIgnoreCase))) return false;
        return status.Contains(profile.OpenText, StringComparison.OrdinalIgnoreCase) ? true : null;
    }

    private LiveRoundResult? ParseResult(string blob, int count, string signature)
    {
        blob = blob.ToLowerInvariant();
        var text = blob.Split('|')[0].Trim();
        bool Matches(string pattern) => pattern.StartsWith('=') ? text == pattern[1..].Trim().ToLowerInvariant() : blob.Contains(pattern.ToLowerInvariant());
        var rule = profile.ResultRules.FirstOrDefault(r => Matches(r.Contains));
        if (rule is null) return null;
        var bankerPair = profile.PairRules.Any(p => p.Banker && Matches(p.Contains));
        var playerPair = profile.PairRules.Any(p => !p.Banker && Matches(p.Contains));
        return new LiveRoundResult(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(signature)))[..16] + ":" + count, rule.Outcome, bankerPair, playerPair);
    }

    // ---- Betting ------------------------------------------------------------------------------------------

    public async ValueTask<IReadOnlyList<decimal>> GetChipsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected(cancellationToken);
        if (profile.StaticChips.Length > 0)
        {
            chipIndex = profile.StaticChips.Select((chip, i) => (chip.Value, i)).Where(c => c.Value > 0).ToList();
            return chipIndex.Select(c => c.Value).Distinct().OrderBy(v => v).ToArray();
        }
        if (profile.ChipSelector.Length == 0) throw new InvalidOperationException("This profile has no chip selector.");
        await page.WaitAsync(cancellationToken);
        try
        {
            var raw = await EvaluateInTableAsync($$"""
                return all({{Js(profile.ChipSelector)}}).map(e => {{ChipValueJs()}});
                """);
            chipIndex = raw.EnumerateArray().Select((e, i) => (Value: ParseChip(e.GetString()), Index: i))
                .Where(c => c.Value is > 0).Select(c => (c.Value!.Value, c.Index)).ToList();
            return chipIndex.Select(c => c.Value).Distinct().OrderBy(v => v).ToArray();
        }
        finally { page.Release(); }
    }

    private string ChipValueJs() => profile.ChipValueSource.Equals("text", StringComparison.OrdinalIgnoreCase)
        ? "(e.textContent||'') + ' ' + (e.getAttribute('aria-label')||'')"
        : $"(e.getAttribute({Js(profile.ChipValueSource)})||'')";

    // "25" -> 25, "1K" -> 1000, "2.5" -> 2.5. Anything ambiguous is ignored rather than guessed.
    internal static decimal? ParseChip(string? text)
    {
        if (text is null) return null;
        var match = Regex.Match(text.Replace(",", ""), @"(?<![\d.])(\d+(?:\.\d+)?)\s*([kKmM]?)(?![\w.])");
        if (!match.Success || Regex.Matches(text.Replace(",", ""), @"\d+(?:\.\d+)?").Count != 1) return null;
        if (!decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return null;
        return match.Groups[2].Value.ToLowerInvariant() switch { "k" => value * 1_000m, "m" => value * 1_000_000m, _ => value };
    }

    public ValueTask ClearSelectionAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected(cancellationToken);
        selection.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask SelectChipAsync(decimal chip, CancellationToken cancellationToken = default)
    {
        EnsureConnected(cancellationToken);
        if (!chipIndex.Any(c => c.Value == chip)) throw new InvalidOperationException("The requested chip is not on the table.");
        selection.Add(chip);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<BetPlacementResult> PlaceBetAsync(AutomationBetRequest request, CancellationToken cancellationToken = default)
    {
        EnsureConnected(cancellationToken);
        if (completed.TryGetValue(request.OperationId, out var existing)) return existing;
        if (!profile.CanBet)
            return Remember(request, BetPlacementStatus.Rejected, "This profile is not calibrated for betting (needs status, chip, Player, Banker and bet-total selectors).");
        if (!selection.SequenceEqual(request.Chips) || selection.Sum() != request.Stake)
            return Remember(request, BetPlacementStatus.Rejected, "Selected chips do not match the requested stake.");
        var spot = request.Side == BetSide.Player ? profile.PlayerSelector : profile.BankerSelector;
        await page.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadStateAsync();
            // Results arrive before the next betting window opens: hold an approved bet until it does (never clicking while closed).
            var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(profile.PlacementWaitSeconds, 0, 120));
            while (state.Open != true && DateTime.UtcNow < deadline)
            {
                await Task.Delay(300, cancellationToken);
                state = await ReadStateAsync();
            }
            if (state.Open != true)
                return Remember(request, BetPlacementStatus.Rejected, profile.PlacementWaitSeconds > 0
                    ? $"The betting window did not open within {profile.PlacementWaitSeconds} seconds; nothing was placed."
                    : "The betting window is not open; nothing was placed.");
            if (state.Total != 0m)
                return Remember(request, BetPlacementStatus.Rejected, "The table already shows a stake this round; nothing was added.");
            // From here clicks may reach the table, so this operation can never be repeated.
            completed[request.OperationId] = new BetPlacementResult(BetPlacementStatus.Indeterminate, "Placement started; confirm on the table.");
            var ackBefore = Volatile.Read(ref ackCount);
            // A canvas table may still show its "touch anywhere to start" screen, which swallows the first click.
            if (profile.StartClick.Length > 0) { await ClickAsync(profile.StartClick, 0); await Task.Delay(900, cancellationToken); }
            string? lastChip = null;
            foreach (var chip in request.Chips)
            {
                var index = chipIndex.First(c => c.Value == chip).Index;
                var chipKey = chip.ToString(CultureInfo.InvariantCulture);
                if (lastChip != chipKey) { await (profile.StaticChips.Length > 0 ? ClickAsync(profile.StaticChips[index].Click, 0) : ClickAsync(profile.ChipSelector, index)); lastChip = chipKey; }
                await ClickAsync(spot, 0);
            }
            if (profile.BetTotalSelector.Length == 0)
            {
                // No readable total (canvas): the only proof is the table's own acknowledgement messages.
                var side = request.Side == BetSide.Player ? "PLAYER" : "BANKER";
                decimal Confirmed() { lock (ackTotals) return ackTotals.GetValueOrDefault(side); }
                for (var waited = 0; waited < 8000 && Confirmed() < request.Stake; waited += 100)
                    await Task.Delay(100, cancellationToken);
                await Task.Delay(400, cancellationToken); // let a late extra confirmation show up
                var confirmed = Confirmed();
                var confirmation = confirmed == request.Stake
                    ? new BetPlacementResult(BetPlacementStatus.Accepted, $"Table confirmed {request.Stake:0.##} on {request.Side}.")
                    : new BetPlacementResult(BetPlacementStatus.Indeterminate, confirmed == 0
                        ? "Clicks were sent but the table sent no confirmation. Check the table; this bet will not be retried."
                        : $"The table confirmed {confirmed:0.##} on {side}, expected {request.Stake:0.##}. Check the table; this bet will not be retried.");
                completed[request.OperationId] = confirmation;
                return confirmation;
            }
            await Task.Delay(Math.Max(300, profile.PollMilliseconds), cancellationToken);
            var after = await ReadStateAsync();
            var result = after.Total == request.Stake
                ? new BetPlacementResult(BetPlacementStatus.Accepted, $"Table shows {request.Stake:0.##} on {request.Side}.")
                : new BetPlacementResult(BetPlacementStatus.Indeterminate,
                    $"Clicks were sent but the table shows {after.Total?.ToString("0.##") ?? "no stake"}, expected {request.Stake:0.##}. Check the table; this bet will not be retried.");
            completed[request.OperationId] = result;
            return result;
        }
        catch (Exception ex) when (ex is CdpException or InvalidOperationException)
        {
            var result = new BetPlacementResult(BetPlacementStatus.Indeterminate, ex.Message + " Check the table; this bet will not be retried.");
            completed[request.OperationId] = result;
            return result;
        }
        finally { page.Release(); }
    }

    private BetPlacementResult Remember(AutomationBetRequest request, BetPlacementStatus status, string message)
    {
        var result = new BetPlacementResult(status, message);
        completed[request.OperationId] = result;
        return result;
    }

    private async Task<(bool? Open, decimal? Total)> ReadStateAsync()
    {
        if (profile.StatusSelector.Length == 0 && profile.BetTotalSelector.Length == 0)
        {
            await EnsurePageHostAsync();
            return (socketOpen, socketStake);
        }
        var state = await EvaluateInTableAsync($$"""
            const status = all({{Js(profile.StatusSelector)}}).map(e => (e.textContent||'').trim()).join(' | ');
            const total = all({{Js(profile.BetTotalSelector)}})[0];
            return {status, total: total ? (total.textContent||'').trim() : null};
            """);
        await EnsurePageHostAsync();
        var totalText = state.GetProperty("total").ValueKind == JsonValueKind.String ? state.GetProperty("total").GetString() : null;
        decimal? total = null;
        if (totalText is not null)
        {
            var digits = Regex.Match(totalText.Replace(",", ""), @"\d+(?:\.\d+)?");
            total = digits.Success ? decimal.Parse(digits.Value, CultureInfo.InvariantCulture) : 0m;
        }
        return (ParseOpen(state.GetProperty("status").GetString() ?? ""), total);
    }

    // ---- Page access --------------------------------------------------------------------------------------

    // One click at a selector (CSS, "xpath:..." or "point:css@x,y"), with the same host and overlay checks as placements.
    public async Task ClickSelectorAsync(string selector, CancellationToken token = default)
    {
        EnsureConnected(token);
        await page.WaitAsync(token);
        try { await ClickAsync(selector, 0); }
        finally { page.Release(); }
    }

    private void EnsureConnected(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsConnected) throw new AutomationDisconnectedException("The live table is disconnected.");
    }

    // Writes a structure report (frame tree, data-role/aria/id candidates, short texts) to calibrate a profile.
    public async Task<string> InspectAsync(CancellationToken token = default)
    {
        EnsureConnected(token);
        await page.WaitAsync(token);
        try
        {
            var report = await EvaluateInTableAsync("""
                const skip = '[data-role="messages-winners-chat"],[data-role="winners-list-container"],[data-role="toasts-container"],[data-role="toast-drawer-wrapper"],[data-role="quick-chat"],[data-role="loading-screen"]';
                const rows = [];
                const perParent = new Map();
                for (const e of doc.querySelectorAll('*')) {
                  const interesting = e.hasAttribute('data-role') || e.id || e.hasAttribute('aria-label') || e.hasAttribute('data-testid') ||
                    e.tagName === 'BUTTON' || e.tagName === 'INPUT' || /chip|bet|spot|status|timer|countdown|balance|result|road/i.test(typeof e.className === 'string' ? e.className : '');
                  if (!interesting || e.closest(skip)) continue;
                  const role = e.getAttribute('data-role');
                  if (role === 'roadItem' || role === 'roadItemColor') { const n = (perParent.get(role) || 0) + 1; perParent.set(role, n); if (n > 4) continue; }
                  const r = e.getBoundingClientRect(); const st = getComputedStyle(e);
                  rows.push({tag: e.tagName.toLowerCase(), id: e.id || null, cls: (typeof e.className === 'string' ? e.className : '').slice(0, 60),
                    role, testid: e.getAttribute('data-testid'), aria: e.getAttribute('aria-label'), disabled: e.disabled === true || e.getAttribute('aria-disabled') === 'true' || null,
                    visible: r.width > 1 && r.height > 1 && st.visibility !== 'hidden' && st.display !== 'none',
                    box: [Math.round(r.left), Math.round(r.top), Math.round(r.width), Math.round(r.height)],
                    text: (e.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 40),
                    data: Object.fromEntries([...e.attributes].filter(a => a.name.startsWith('data-') && a.name !== 'data-role').slice(0, 6).map(a => [a.name, a.value.slice(0, 40)]))});
                  if (rows.length >= 1500) break;
                }
                return {url: location.href, title: document.title, count: rows.length, elements: rows};
                """);
            var all = await EnumerateFramesAsync(pageSession!);
            var tree = all.Select(f => new { f.Depth, f.Url, tableFrame = tableFrame is { } t && t.Session == f.Session && t.FrameId == f.FrameId }).ToArray();
            string[] sockets; lock (socketLog) sockets = socketLog.TakeLast(200).ToArray();
            var canvases = await EvaluateInTableAsync("return [...doc.querySelectorAll('canvas')].map(c => { const r = c.getBoundingClientRect(); return {id: c.id || null, cls: String(c.className).slice(0, 40), pixels: [c.width, c.height], box: [Math.round(r.left), Math.round(r.top), Math.round(r.width), Math.round(r.height)]}; });");
            return JsonSerializer.Serialize(new { inspected = DateTime.UtcNow, frames = tree, canvases, socketFrames = sockets, page = report }, LiveTableProfile.Json);
        }
        finally { page.Release(); }
    }
}

// Test seam for pure parsing helpers.
public static class LiveTableChecksAccess
{
    public static decimal? ParseChip(string? text) => LiveBrowserTableAdapter.ParseChip(text);
    public static string NormalizeSocketText(string payload) => LiveBrowserTableAdapter.NormalizeSocketText(payload);
}
