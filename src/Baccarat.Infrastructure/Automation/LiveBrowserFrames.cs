using System.Text.Json;
using Baccarat.Core.Automation;

namespace Baccarat.Infrastructure.Automation;

// Frame access that works at any nesting depth: cross-process (out-of-process) frames are separate DevTools
// sessions, same-process frames (even cross-origin ones) are reached through isolated execution worlds.
public sealed partial class LiveBrowserTableAdapter
{
    private sealed record FrameInfo(string Session, string FrameId, string? ParentId, string Url, int Depth)
    {
        public string Host => Uri.TryCreate(Url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? u.Host : "";
    }

    private sealed record TableFrame(string Session, string FrameId);

    private TableFrame? tableFrame;
    private readonly Dictionary<(string Session, string FrameId), int> contexts = [];
    private readonly HashSet<string> pagesEnabled = [];

    // Every frame reachable from one tab: its own tree plus the trees of all attached child sessions, with total depth.
    private async Task<List<FrameInfo>> EnumerateFramesAsync(string root)
    {
        var sessions = new List<string> { root };
        lock (frames) sessions.AddRange(frames.Keys);
        var found = new List<(string Session, string Id, string? Parent, string Url)>();
        foreach (var session in sessions)
        {
            try
            {
                if (pagesEnabled.Add(session)) await cdp!.SendAsync("Page.enable", null, session);
                var tree = await cdp!.SendAsync("Page.getFrameTree", null, session);
                void Walk(JsonElement node, string? parent)
                {
                    var frame = node.GetProperty("frame");
                    var id = frame.GetProperty("id").GetString()!;
                    found.Add((session, id, parent, frame.TryGetProperty("url", out var u) ? u.GetString() ?? "" : ""));
                    if (node.TryGetProperty("childFrames", out var children))
                        foreach (var child in children.EnumerateArray()) Walk(child, id);
                }
                Walk(tree.GetProperty("frameTree"), null);
            }
            catch (Exception ex) when (ex is CdpException or IOException) { }
        }
        // Cross-process frames are not listed in their parent's tree, so depth = ancestor sessions + depth inside the session.
        int SessionDepth(string session)
        {
            var depth = 0;
            for (; session != root && depth < 32; depth++)
            {
                lock (frames) { if (!parents.TryGetValue(session, out var parent)) break; session = parent; }
            }
            return depth;
        }
        int DepthOf(string session, string id)
        {
            var inside = 0;
            for (var current = id; inside < 32; inside++)
            {
                var self = found.FirstOrDefault(f => f.Session == session && f.Id == current);
                if (self.Id is null || self.Parent is null) break;
                current = self.Parent;
            }
            return SessionDepth(session) + inside;
        }
        return found
            .Where(f => !IsStub(f.Session, f.Id))
            .Select(f => new FrameInfo(f.Session, f.Id, f.Parent, f.Url, DepthOf(f.Session, f.Id)))
            .ToList();
    }

    // True when the frame id belongs to a child session other than the session listing it (a stub in the parent tree).
    private bool IsStub(string session, string frameId)
    {
        lock (frames) return targetIds.Any(kv => kv.Value == frameId && kv.Key != session);
    }

    private async Task<int> ContextForAsync(string session, string frameId)
    {
        lock (contexts) if (contexts.TryGetValue((session, frameId), out var cached)) return cached;
        var world = await cdp!.SendAsync("Page.createIsolatedWorld", new { frameId, worldName = "baccarat-reader", grantUniveralAccess = false }, session);
        var id = world.GetProperty("executionContextId").GetInt32();
        lock (contexts) contexts[(session, frameId)] = id;
        return id;
    }

    private void ForgetContext(string session, string frameId)
    {
        lock (contexts) contexts.Remove((session, frameId));
    }

    // The table is the deepest frame whose text (and optional selector) matches the profile's detection rules.
    private async Task<TableFrame?> DetectFrameAsync(List<FrameInfo> all)
    {
        var script = $$"""
            (() => { const need = {{JsonSerializer.Serialize(profile.DetectText)}}, sel = {{Js(profile.DetectSelector)}}, d = document;
              try { const t = (d.body ? (d.body.innerText || '') + ' ' + (d.body.textContent || '') : '').toUpperCase();
                if (!need.every(x => t.includes(x.toUpperCase()))) return false;
                if (!sel) return true;
                if (sel.startsWith('xpath:')) return d.evaluate(sel.slice(6), d, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null).snapshotLength > 0;
                return d.querySelector(sel) !== null; } catch (e) { return false; } })()
            """;
        FrameInfo? best = null;
        foreach (var frame in all)
        {
            if (!string.IsNullOrEmpty(profile.FrameUrlContains) && !frame.Url.Contains(profile.FrameUrlContains, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var context = await ContextForAsync(frame.Session, frame.FrameId);
                var result = await cdp!.EvaluateAsync(script, frame.Session, context);
                if (result.ValueKind == JsonValueKind.True && (best is null || frame.Depth > best.Depth)) best = frame;
            }
            catch (Exception ex) when (ex is CdpException or IOException) { ForgetContext(frame.Session, frame.FrameId); }
        }
        return best is null ? null : new TableFrame(best.Session, best.FrameId);
    }

    private static string Js(string value) => JsonSerializer.Serialize(value);

    private const string Prelude = """
        const doc = document;
        const all = (s) => { if (!s) return []; if (s.startsWith('xpath:')) { const r = doc.evaluate(s.slice(6), doc, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null);
          const a = []; for (let i = 0; i < r.snapshotLength; i++) a.push(r.snapshotItem(i)); return a; } return [...doc.querySelectorAll(s)]; };
        const blob = (e) => [e.textContent, (e.className && e.className.baseVal !== undefined) ? e.className.baseVal : e.className, e.getAttribute('aria-label'),
          ...[...e.attributes].filter(a => a.name.startsWith('data-')).map(a => a.name + '=' + a.value)].join('|').toLowerCase();
        """;

    private async Task<JsonElement> EvaluateInTableAsync(string body)
    {
        var script = "(() => {" + Prelude + body + "})()";
        for (var attempt = 0; ; attempt++)
        {
            var frame = tableFrame ?? throw new AutomationDisconnectedException("The live table is disconnected.");
            try
            {
                var context = await ContextForAsync(frame.Session, frame.FrameId);
                return await cdp!.EvaluateAsync(script, frame.Session, context);
            }
            catch (CdpException) when (attempt == 0)
            {
                // The frame reloaded or was replaced: find the table again inside the same tab, once.
                ForgetContext(frame.Session, frame.FrameId);
                var found = await DetectFrameAsync(await EnumerateFramesAsync(pageSession!));
                tableFrame = found ?? throw new AutomationDisconnectedException("The table frame disappeared from the page.");
            }
        }
    }

    private async Task ClickAsync(string selector, int index)
    {
        await EnsurePageHostAsync();
        // Measure, let scrolling/layout settle, measure again: click only where the control has stopped moving.
        async Task<JsonElement> MeasureAsync()
        {
            var measured = await EvaluateInTableAsync($$"""
                // "point:<css>@x,y" targets a spot on a canvas by fractions of its box (canvas tables have no buttons).
                let sel = {{Js(selector)}}, fx = 0.5, fy = 0.5;
                if (sel.startsWith('point:')) { const parts = sel.slice(6).split('@'); sel = parts[0];
                  if (parts[1]) { const p = parts[1].split(','); fx = parseFloat(p[0]); fy = parseFloat(p[1]); if (!(fx >= 0 && fx <= 1 && fy >= 0 && fy <= 1)) return null; } }
                const e = all(sel)[{{index}}];
                if (!e) return null;
                e.scrollIntoView({block: 'center', inline: 'center'});
                const r = e.getBoundingClientRect();
                const style = getComputedStyle(e);
                if (r.width < 2 || r.height < 2 || style.visibility === 'hidden' || style.display === 'none' || e.disabled) return null;
                const cx = r.left + r.width * fx, cy = r.top + r.height * fy;
                const hit = doc.elementFromPoint(cx, cy);
                if (!hit || !(e === hit || e.contains(hit))) return {covered: hit ? (hit.getAttribute('data-role') || hit.tagName.toLowerCase()) : 'nothing'};
                return {x: cx, y: cy};
                """);
            // Never click through a pop-up or overlay (for example a "Low balance" message): that would hit the wrong control.
            if (measured.ValueKind == JsonValueKind.Object && measured.TryGetProperty("covered", out var coveredBy))
                throw new InvalidOperationException($"The control is covered by another element ({coveredBy.GetString()}), for example a pop-up; nothing was clicked.");
            return measured;
        }
        var rect = await MeasureAsync();
        for (var settle = 0; settle < 5 && rect.ValueKind != JsonValueKind.Null; settle++)
        {
            await Task.Delay(70);
            var again = await MeasureAsync();
            if (again.ValueKind == JsonValueKind.Null) { rect = again; break; }
            var moved = Math.Abs(again.GetProperty("x").GetDouble() - rect.GetProperty("x").GetDouble()) > 0.5 ||
                        Math.Abs(again.GetProperty("y").GetDouble() - rect.GetProperty("y").GetDouble()) > 0.5;
            rect = again;
            if (!moved) break;
        }
        if (rect.ValueKind == JsonValueKind.Null) throw new InvalidOperationException("A table control was not found or is not clickable.");
        var (ox, oy) = await FrameOffsetAsync(tableFrame!);
        double x = rect.GetProperty("x").GetDouble() + ox, y = rect.GetProperty("y").GetDouble() + oy;
        // A page in a background or covered window may ignore input: behave as focused and visible, like a real click would.
        await cdp!.SendAsync("Emulation.setFocusEmulationEnabled", new { enabled = true }, pageSession);
        await cdp.SendAsync("Page.bringToFront", null, pageSession);
        await cdp.SendAsync("Input.dispatchMouseEvent", new { type = "mouseMoved", x, y, button = "none", buttons = 0 }, pageSession);
        await Task.Delay(40);
        await cdp.SendAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", buttons = 1, clickCount = 1 }, pageSession);
        await Task.Delay(40);
        await cdp.SendAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", buttons = 0, clickCount = 1 }, pageSession);
        await Task.Delay(Math.Clamp(profile.ClickDelayMilliseconds, 20, 2000));
    }

    // Position of a frame's content area inside the top-level page: sum the owner iframe's box at every level.
    private async Task<(double X, double Y)> FrameOffsetAsync(TableFrame target)
    {
        var all = await EnumerateFramesAsync(pageSession!);
        var current = all.FirstOrDefault(f => f.Session == target.Session && f.FrameId == target.FrameId)
            ?? throw new InvalidOperationException("The table frame is no longer on the page.");
        double x = 0, y = 0;
        for (var hop = 0; hop < 32; hop++)
        {
            string ownerSession, ownerFrame, parentSession;
            string? parentFrame;
            if (current.ParentId is not null)
            {
                ownerSession = parentSession = current.Session; ownerFrame = current.FrameId; parentFrame = current.ParentId;
            }
            else if (current.Session != pageSession)
            {
                string rootFrame, holder;
                lock (frames)
                {
                    if (!parents.TryGetValue(current.Session, out parentSession!) || !targetIds.TryGetValue(current.Session, out rootFrame!) ||
                        !parentFrameIds.TryGetValue(current.Session, out holder!))
                        throw new InvalidOperationException("The table frame position is unknown.");
                }
                ownerSession = parentSession; ownerFrame = rootFrame;
                parentFrame = holder.Length == 0 ? null : holder;
            }
            else break;
            var box = await OwnerBoxAsync(ownerSession, ownerFrame);
            x += box.X; y += box.Y;
            if (parentFrame is null) break;
            current = all.FirstOrDefault(f => f.Session == parentSession && f.FrameId == parentFrame)
                ?? throw new InvalidOperationException("A parent frame of the table is not reachable.");
        }
        return (x, y);
    }

    private static string? FindParent(JsonElement node, string id, string? parent)
    {
        var frame = node.GetProperty("frame");
        if (frame.GetProperty("id").GetString() == id) return parent;
        if (node.TryGetProperty("childFrames", out var children))
            foreach (var child in children.EnumerateArray())
                if (FindParent(child, id, frame.GetProperty("id").GetString()) is { } found) return found;
        return null;
    }

    // Where the content of the iframe element that owns `frameId` sits inside its parent frame's viewport.
    private async Task<(double X, double Y)> OwnerBoxAsync(string session, string frameId)
    {
        await cdp!.SendAsync("DOM.enable", null, session);
        var owner = await cdp.SendAsync("DOM.getFrameOwner", new { frameId }, session);
        var node = await cdp.SendAsync("DOM.resolveNode", new { backendNodeId = owner.GetProperty("backendNodeId").GetInt32() }, session);
        var objectId = node.GetProperty("object").GetProperty("objectId").GetString();
        var result = await cdp.SendAsync("Runtime.callFunctionOn", new
        {
            objectId,
            returnByValue = true,
            functionDeclaration = """
                function () { const r = this.getBoundingClientRect(), s = getComputedStyle(this);
                  return {x: r.left + this.clientLeft + parseFloat(s.paddingLeft || 0), y: r.top + this.clientTop + parseFloat(s.paddingTop || 0)}; }
                """
        }, session);
        var value = result.GetProperty("result").GetProperty("value");
        return (value.GetProperty("x").GetDouble(), value.GetProperty("y").GetDouble());
    }
}
