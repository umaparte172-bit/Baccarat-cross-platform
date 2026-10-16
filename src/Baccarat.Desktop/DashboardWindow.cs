using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Baccarat.Desktop.Views;

namespace Baccarat.Desktop;

// Single shell: retained feature views share the selected dashboard session.
public sealed partial class DashboardWindow : Window
{
    private readonly Dictionary<Guid, SessionTools> tools = new();
    private readonly ApplicationServices services;
    private Guid? activeSession;
    private AccountView? account;
    private bool closed;
    private readonly Grid layout = new();
    private readonly ContentControl workspaceContent = new();
    private readonly Border toolsSidebar = new()
    {
        Name = "ToolsSidebar",
        Width = 320,
        HorizontalAlignment = HorizontalAlignment.Right,
        Background = DashboardUi.Surface,
        BorderBrush = DashboardUi.Line,
        BorderThickness = new Thickness(1),
        IsVisible = false
    };
    public DashboardView Dashboard { get; }
    public DashboardWindow(ApplicationServices? services = null, bool persistenceEnabled = true, string? workspacePath = null)
    {
        this.services = services ?? ApplicationServices.CreateDefault();
        Title = "Baccarat Workspace";
        using (var icon = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Baccarat.Desktop/Assets/wetalkbet-logo.png")))
            Icon = new WindowIcon(icon);
        Width = 1280; Height = 860; MinWidth = 800; MinHeight = 600;
        RequestedThemeVariant = ThemeVariant.Light;
        Background = DashboardUi.Surface;
        Foreground = DashboardUi.Ink;
        Dashboard = new DashboardView(ShowTools, LoadWorkspace(persistenceEnabled, workspacePath));
        Dashboard.SetStorageStatus(storageStatus);
        Dashboard.WorkspaceChanged += RefreshTools;
        Dashboard.WorkspaceChanged += QueueSave;
        saveTimer.Tick += async (_, _) => await SaveWorkspaceAsync();
        Closing += OnClosing;
        workspaceContent.Content = Dashboard;
        layout.Children.Add(workspaceContent);
        layout.Children.Add(toolsSidebar);
        Content = layout;
        Closed += (_, _) =>
        {
            closed = true;
            Dashboard.WorkspaceChanged -= RefreshTools;
            Dashboard.WorkspaceChanged -= QueueSave;
            saveTimer.Stop();
            store?.Dispose();
            foreach (var context in tools.Values) context.Dispose();
        };
    }

    private void RefreshTools()
    {
        if (closed) return;
        var selected = Dashboard.Workspace.Selected;
        if (activeSession != selected?.Id)
        {
            if (activeSession is { } old && tools.TryGetValue(old, out var previous))
                previous.Pause("Session changed. Review this session before resuming simulation.");
            activeSession = selected?.Id;
            if (toolsSidebar.IsVisible)
            {
                toolsSidebar.IsVisible = false;
                ShowTools();
            }
        }
        if (selected is not null && tools.TryGetValue(selected.Id, out var current)) current.Refresh();
    }

    private void ShowDashboard()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Background = DashboardUi.Surface;
        toolsSidebar.IsVisible = false;
        workspaceContent.Content = Dashboard;
        Dashboard.RefreshSharedState();
        Title = "Baccarat Workspace";
    }

    private SessionTools? CurrentTools()
    {
        if (Dashboard.Workspace.Selected is not { } selected) return null;
        if (!tools.TryGetValue(selected.Id, out var current))
        {
            current = new SessionTools(selected, services.Diagnostics, Dashboard.RefreshSharedState);
            tools.Add(selected.Id, current);
        }
        return current;
    }

    private void ShowTools()
    {
        if (toolsSidebar.IsVisible) { toolsSidebar.IsVisible = false; return; }
        var current = CurrentTools();
        var page = WorkspaceUi.Page("TOOLS", "Session tools",
            Dashboard.Workspace.Selected is { } selected ? $"{selected.Title} · Same session, same records and strategy state." :
            "Create a session on the dashboard to use strategies, automation and the viewer.");
        page.Children.Add(WorkspaceUi.Text(storageStatus, 12));
        var close = WorkspaceUi.Action("Close Tools");
        close.Click += (_, _) => toolsSidebar.IsVisible = false;
        page.Children.Insert(0, close);
        page.Children.Add(WorkspaceUi.Text("Strategies update this session's recommendations. Automation uses those recommendations. Viewer shows its records and status.", 12));
        page.Children.Add(WorkspaceUi.Text("Live connection unavailable: the recovered account API serves a domain-parking page; casino protocol recovery is incomplete. No live bets are sent.", 12));
        if (store is not null)
        {
            var save = WorkspaceUi.Action(needsRecovery ? "Save recovered workspace" : "Save workspace now");
            save.IsEnabled = !writesBlocked;
            save.Click += async (_, _) => { await SaveWorkspaceAsync(needsRecovery); toolsSidebar.IsVisible = false; ShowTools(); };
            page.Children.Add(save);
        }
        foreach (var (label, action) in new (string, Action)[]
        {
            ("Session dashboard", ShowDashboard),
            ("Strategies", () => ShowTool("Strategies", current!.Strategies)),
            ("Automation", () => ShowTool("Automation", current!.Automation)),
            ("Viewer", () => ShowTool("Viewer", current!.Viewer)),
            ("Clear session…", ConfirmClear),
            ("Sing In / Up", ShowAccount)
        })
        {
            var button = WorkspaceUi.Action(label);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.IsEnabled = current is not null || label is "Sing In / Up" or "Session dashboard";
            button.Click += (_, _) => action();
            page.Children.Add(button);
        }
        toolsSidebar.Child = new ScrollViewer { Content = page };
        toolsSidebar.IsVisible = true;
    }

    private void ConfirmClear()
    {
        if (Dashboard.Workspace.Selected is not { } selected) return;
        var page = WorkspaceUi.Page("CONFIRM CLEAR", "Clear this session?",
            "This removes recorded results, pairs and calculated profits from this session. Export first if you want to keep them.");
        var confirm = WorkspaceUi.Action("Clear selected session");
        confirm.Click += (_, _) =>
        {
            selected.Clear();
            if (tools.Remove(selected.Id, out var clearedTools)) clearedTools.Dispose();
            Dashboard.RefreshSharedState();
            ShowDashboard();
        };
        var cancel = WorkspaceUi.Action("Cancel");
        cancel.Click += (_, _) => { ShowDashboard(); ShowTools(); };
        page.Children.Add(confirm);
        page.Children.Add(cancel);
        ShowTool("Clear session", page);
    }

    private void ShowTool(string name, Control view, bool showBack = true)
    {
        toolsSidebar.IsVisible = false;
        RequestedThemeVariant = ThemeVariant.Light;
        Background = DashboardUi.Surface;
        var shell = new DockPanel { Margin = new Thickness(16) };
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        var back = WorkspaceUi.Action("Back to dashboard");
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Click += (_, _) => ShowDashboard();
        actions.Children.Add(back);
        if (showBack)
        {
            var menu = WorkspaceUi.Action("Tools");
            menu.Name = "ToggleTools";
            Avalonia.Automation.AutomationProperties.SetName(menu, "Show or hide session tools");
            menu.Click += (_, _) => ShowTools();
            Grid.SetColumn(menu, 1);
            actions.Children.Add(menu);
        }
        DockPanel.SetDock(actions, Dock.Top);
        shell.Children.Add(actions);
        if (view.Parent is ContentControl parent) parent.Content = null;
        shell.Children.Add(new ScrollViewer { Content = view });
        workspaceContent.Content = shell;
        Title = $"Baccarat Workspace — {name}";
    }

    private void ShowAccount()
    {
        if (account is null)
        {
            account = new AccountView(services.Authentication, services.Diagnostics, services.AuthenticationSessions);
            account.Authenticated += ShowTools;
            account.SignedOut += () =>
            {
                foreach (var context in tools.Values) context.Pause("Signed out. Pending simulation cancelled.");
                ShowAccount();
            };
        }
        ShowTool("Sing In / Up", account);
    }
}
