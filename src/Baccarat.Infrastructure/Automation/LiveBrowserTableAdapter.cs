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
public sealed class LiveBrowserTableAdapter : IAutomationTableAdapter, ILiveTableSource, IAsyncDisposable
{
    private readonly LiveTableProfile profile;
    private readonly LiveBrowserOptions options;
    private readonly Dictionary<Guid, BetPlacementResult> completed = [];
    private readonly List<decimal> selection = [];
    private readonly Dictionary<string, string> frames = []; // child session -> frame url
    private CdpConnection? cdp;
    private Process? launched;
    private string? pageSession;
    private string? lockedHost;
    private string? frameKey;
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
    public async Task OpenBrowserAsync(CancellationToken token = default)
    {
        if (await TryVersionAsync(token) is not null) return;
        if (!options.LaunchBrowser) throw new InvalidOperationException("No browser is listening on the debugging port.");
        var executable = options.BrowserPath ?? FindBrowser() ?? throw new InvalidOperationException("Install Chrome or Edge to use a live table.");
        var directory = options.ProfileDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BaccaratWorkspace", "live-browser-profile");
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var arg in new[] { $"--remote-debugging-port={options.DebugPort}", "--remote-debugging-address=127.0.0.1",
            "--user-data-dir=" + directory, "--no-first-run", "--no-default-browser-check", "--new-window",
            profile.StartUrl.Length > 0 ? profile.StartUrl : "about:blank" })
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

    private static string? FindBrowser()
    {
        var candidates = OperatingSystem.IsWindows() ? new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google/Chrome/Application/chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google/Chrome/Application/chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft/Edge/Application/msedge.exe") } :
            new[] { "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge", "/usr/bin/google-chrome", "/usr/bin/chromium" };
        return candidates.FirstOrDefault(File.Exists);
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
        foreach (var target in targets.GetProperty("targetInfos").EnumerateArray())
        {
            if (target.GetProperty("type").GetString() != "page") continue;
            var url = target.GetProperty("url").GetString();
            if (!Eligible(url)) continue;
            string? session = null;
            try
            {
                var attached = await cdp.SendAsync("Target.attachToTarget", new { targetId = target.GetProperty("targetId").GetString(), flatten = true });
                session = attached.GetProperty("sessionId").GetString()!;
                lock (frames) frames.Clear();
                await cdp.SendAsync("Target.setAutoAttach", new { autoAttach = true, waitForDebuggerOnStart = false, flatten = true }, session);
                await Task.Delay(400, token);
                if (await DetectAsync(session) is { } found)
                {
                    await cdp.SendAsync("Runtime.enable", null, session);
                    lockedHost = new Uri(url!).Host;
                    frameKey = found.Length == 0 ? null : found;
                    pageSession = session;
                    return true;
                }
            }
            catch (Exception ex) when (ex is CdpException or InvalidOperationException) { }
            if (session is not null && pageSession != session)
            {
                try { await cdp.SendAsync("Target.detachFromTarget", new { sessionId = session }); } catch (Exception ex) when (ex is CdpException or IOException) { }
            }
        }
        return false;
    }

    // Returns null when this tab shows no table; otherwise the frame key ("" = the page itself).
    private async Task<string?> DetectAsync(string session)
    {
        var texts = JsonSerializer.Serialize(profile.DetectText);
        var test = $$"""
            const need = {{texts}}, sel = {{Js(profile.DetectSelector)}};
            const check = (d) => { try { const t = (d.body ? d.body.innerText : '').toUpperCase();
              if (!need.every(x => t.includes(x.toUpperCase()))) return false;
              if (!sel) return true;
              if (sel.startsWith('xpath:')) return d.evaluate(sel.slice(6), d, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null).snapshotLength > 0;
              return d.querySelector(sel) !== null; } catch (e) { return false; } };
            """;
        var topResult = await cdp!.EvaluateAsync("(() => {" + test + """
            if (check(document)) return '';
            for (const f of document.querySelectorAll('iframe')) { try { if (f.contentDocument && check(f.contentDocument)) return f.src || null; } catch (e) {} }
            return null; })()
            """, session);
        if (topResult.ValueKind == JsonValueKind.String) return topResult.GetString();
        KeyValuePair<string, string>[] children;
        lock (frames) children = frames.ToArray();
        foreach (var child in children)
        {
            try
            {
                var inner = await cdp.EvaluateAsync("(() => {" + test + "return check(document); })()", child.Key);
                if (inner.ValueKind == JsonValueKind.True) return child.Value;
            }
            catch (Exception ex) when (ex is CdpException) { }
        }
        return null;
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
        if (method == "Target.attachedToTarget")
        {
            var info = args.GetProperty("targetInfo");
            if (info.GetProperty("type").GetString() == "iframe")
                lock (frames) frames[args.GetProperty("sessionId").GetString()!] = info.GetProperty("url").GetString() ?? "";
        }
        else if (method == "Target.detachedFromTarget")
            lock (frames) frames.Remove(args.GetProperty("sessionId").GetString()!);
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default) => await DisconnectCoreAsync();

    private async Task DisconnectCoreAsync()
    {
        polling?.Cancel();
        polling = null;
        pageSession = null;
        lockedHost = null;
        frameKey = null;
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
            bettingOpen = state.GetProperty("status").ValueKind == JsonValueKind.Null ? null : ParseOpen(state.GetProperty("status").GetString()!);
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
        var rule = profile.ResultRules.FirstOrDefault(r => blob.Contains(r.Contains.ToLowerInvariant()));
        if (rule is null) return null;
        var bankerPair = profile.PairRules.Any(p => p.Banker && blob.Contains(p.Contains.ToLowerInvariant()));
        var playerPair = profile.PairRules.Any(p => !p.Banker && blob.Contains(p.Contains.ToLowerInvariant()));
        return new LiveRoundResult(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(signature)))[..16] + ":" + count, rule.Outcome, bankerPair, playerPair);
    }

    // ---- Betting ------------------------------------------------------------------------------------------

    public async ValueTask<IReadOnlyList<decimal>> GetChipsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected(cancellationToken);
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
            if (state.Open != true)
                return Remember(request, BetPlacementStatus.Rejected, "The betting window is not open; nothing was placed.");
            if (state.Total != 0m)
                return Remember(request, BetPlacementStatus.Rejected, "The table already shows a stake this round; nothing was added.");
            // From here clicks may reach the table, so this operation can never be repeated.
            completed[request.OperationId] = new BetPlacementResult(BetPlacementStatus.Indeterminate, "Placement started; confirm on the table.");
            string? lastChip = null;
            foreach (var chip in request.Chips)
            {
                var index = chipIndex.First(c => c.Value == chip).Index;
                var chipKey = chip.ToString(CultureInfo.InvariantCulture);
                if (lastChip != chipKey) { await ClickAsync(profile.ChipSelector, index); lastChip = chipKey; }
                await ClickAsync(spot, 0);
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

    private static string Js(string value) => JsonSerializer.Serialize(value);

    private (string Session, string? FrameArg, string? ChildUrl) Context()
    {
        var session = pageSession ?? throw new AutomationDisconnectedException("The live table is disconnected.");
        var key = string.IsNullOrEmpty(profile.FrameUrlContains) ? frameKey : profile.FrameUrlContains;
        if (string.IsNullOrEmpty(key)) return (session, null, null);
        lock (frames)
        {
            var child = frames.FirstOrDefault(f => f.Value.Contains(key, StringComparison.OrdinalIgnoreCase) || key.Contains(f.Value, StringComparison.OrdinalIgnoreCase));
            return child.Key is not null ? (child.Key, null, child.Value) : (session, key, null);
        }
    }

    private const string Prelude = """
        const frameUrl = FRAME;
        const doc = (() => { if (!frameUrl) return document;
          for (const f of document.querySelectorAll('iframe')) { try { if ((f.src||'').includes(frameUrl) && f.contentDocument) return f.contentDocument; } catch (e) {} }
          return document; })();
        const all = (s) => { if (!s) return []; if (s.startsWith('xpath:')) { const r = doc.evaluate(s.slice(6), doc, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null);
          const a = []; for (let i = 0; i < r.snapshotLength; i++) a.push(r.snapshotItem(i)); return a; } return [...doc.querySelectorAll(s)]; };
        const blob = (e) => [e.textContent, (e.className && e.className.baseVal !== undefined) ? e.className.baseVal : e.className, e.getAttribute('aria-label'),
          ...[...e.attributes].filter(a => a.name.startsWith('data-')).map(a => a.name + '=' + a.value)].join('|').toLowerCase();
        """;

    private async Task<JsonElement> EvaluateInTableAsync(string body)
    {
        var (session, frameArg, _) = Context();
        var script = "(() => {" + Prelude.Replace("FRAME", frameArg is null ? "null" : Js(frameArg)) + body + "})()";
        return await cdp!.EvaluateAsync(script, session);
    }

    private async Task ClickAsync(string selector, int index)
    {
        await EnsurePageHostAsync();
        var (session, frameArg, childUrl) = Context();
        var rect = await EvaluateInTableAsync($$"""
            const e = all({{Js(selector)}})[{{index}}];
            if (!e) return null;
            e.scrollIntoView({block: 'center', inline: 'center'});
            const r = e.getBoundingClientRect();
            const style = getComputedStyle(e);
            if (r.width < 2 || r.height < 2 || style.visibility === 'hidden' || style.display === 'none' || e.disabled) return null;
            return {x: r.left + r.width / 2, y: r.top + r.height / 2};
            """);
        if (rect.ValueKind == JsonValueKind.Null) throw new InvalidOperationException("A table control was not found or is not clickable.");
        double x = rect.GetProperty("x").GetDouble(), y = rect.GetProperty("y").GetDouble();
        if (frameArg is not null || childUrl is not null)
        {
            // Mouse input is dispatched in top-level page coordinates; add the frame's offset.
            var key = frameArg ?? childUrl ?? frameKey!;
            var offset = await cdp!.EvaluateAsync($$"""
                (() => { for (const f of document.querySelectorAll('iframe')) if ((f.src||'').includes({{Js(key)}}) || {{Js(key)}}.includes(f.src||'\u0000')) { const r = f.getBoundingClientRect(); return {x: r.left, y: r.top}; } return null; })()
                """, pageSession);
            if (offset.ValueKind == JsonValueKind.Null) throw new InvalidOperationException("The table frame is not visible.");
            x += offset.GetProperty("x").GetDouble();
            y += offset.GetProperty("y").GetDouble();
        }
        foreach (var type in new[] { "mouseMoved", "mousePressed", "mouseReleased" })
            await cdp!.SendAsync("Input.dispatchMouseEvent", new { type, x, y, button = "left", clickCount = type == "mouseMoved" ? 0 : 1 }, pageSession);
        await Task.Delay(Math.Clamp(profile.ClickDelayMilliseconds, 20, 2000));
    }

    private void EnsureConnected(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsConnected) throw new AutomationDisconnectedException("The live table is disconnected.");
    }

    // Writes a structure report (frames, data-role/aria/id candidates, short texts) to calibrate a profile.
    public async Task<string> InspectAsync(CancellationToken token = default)
    {
        EnsureConnected(token);
        await page.WaitAsync(token);
        try
        {
            var report = await EvaluateInTableAsync("""
                const pick = [...doc.querySelectorAll('[data-role],[data-testid],[aria-label],button,[class*=chip],[class*=bet],[class*=result],[class*=status],[id]')].slice(0, 400)
                  .map(e => ({tag: e.tagName.toLowerCase(), id: e.id || null, cls: (typeof e.className === 'string' ? e.className : '').slice(0, 80),
                    role: e.getAttribute('data-role'), testid: e.getAttribute('data-testid'), aria: e.getAttribute('aria-label'),
                    text: (e.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 40)}));
                return {url: location.href, title: document.title, frames: [...document.querySelectorAll('iframe')].map(f => f.src), elements: pick};
                """);
            List<string> childFrames;
            lock (frames) childFrames = frames.Values.ToList();
            return JsonSerializer.Serialize(new { inspected = DateTime.UtcNow, childFrames, page = report }, LiveTableProfile.Json);
        }
        finally { page.Release(); }
    }
}

// Test seam for pure parsing helpers.
public static class LiveTableChecksAccess
{
    public static decimal? ParseChip(string? text) => LiveBrowserTableAdapter.ParseChip(text);
}
