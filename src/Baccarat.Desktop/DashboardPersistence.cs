using Avalonia.Controls;
using Avalonia.Threading;
using Baccarat.Desktop.ViewModels;
using Baccarat.Infrastructure.Persistence;

namespace Baccarat.Desktop;

public sealed partial class DashboardWindow
{
    private WorkspaceFileStore? store;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private string storageStatus = "In-memory test workspace · Export before closing.";
    private bool writesBlocked, needsRecovery, allowClose, closingSave;
    private long editRevision, savedRevision;
    private byte[]? savedBytes;
    private Task<bool>? saveTask;

    private DashboardWorkspace LoadWorkspace(bool enabled, string? path)
    {
        if (!enabled) return new DashboardWorkspace();
        path ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BaccaratWorkspace", "workspace-v1.json");
        try
        {
            store = new WorkspaceFileStore(path);
            var data = store.Read();
            if (data is null && store.Read(backup: true) is not null)
                throw new InvalidDataException("Primary workspace is missing.");
            var workspace = data is null ? new DashboardWorkspace() : DashboardWorkspace.Deserialize(data);
            savedBytes = workspace.Serialize();
            storageStatus = $"Local autosave ready · {store.FilePath}";
            return workspace;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            if (store is not null)
            {
                try
                {
                    var backup = store.Read(backup: true);
                    if (backup is not null)
                    {
                        var recovered = DashboardWorkspace.Deserialize(backup);
                        needsRecovery = true;
                        storageStatus = "Primary workspace could not be loaded. A validated backup is open. Tools → Save recovered workspace preserves the rejected original before restoring.";
                        return recovered;
                    }
                }
                catch (Exception backupError) when (IsStorageFailure(backupError)) { }
            }
            writesBlocked = true;
            storageStatus = "Local save unavailable: file invalid, inaccessible, or open in another instance. Existing files are untouched. This is an unsaved workspace; export results before closing. File: " + path;
            return new DashboardWorkspace();
        }
    }

    private static bool IsStorageFailure(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException;

    private void QueueSave()
    {
        if (store is null && !writesBlocked) return; // Explicit in-memory test mode.
        editRevision++;
        if (writesBlocked || needsRecovery) return;
        SetStorageStatus("Unsaved changes · Saving locally…");
        saveTimer.Stop();
        saveTimer.Start();
    }

    private void SetStorageStatus(string text)
    {
        storageStatus = text;
        Dashboard.SetStorageStatus(text);
    }

    public Task<bool> SaveWorkspaceAsync(bool recover = false)
    {
        if (saveTask is { IsCompleted: false }) return saveTask;
        saveTask = SaveCoreAsync(recover);
        return saveTask;
    }

    private async Task<bool> SaveCoreAsync(bool recover)
    {
        saveTimer.Stop();
        if (writesBlocked || (needsRecovery && !recover)) return false;
        if (store is null) return true;
        var revision = editRevision;
        try
        {
            // Capture on the UI thread; only the immutable bytes cross to the disk worker.
            var data = Dashboard.Workspace.Serialize();
            if (needsRecovery || savedBytes is null || !data.AsSpan().SequenceEqual(savedBytes))
                await Task.Run(() => store.Save(data, needsRecovery && recover));
            savedBytes = data;
            savedRevision = revision;
            needsRecovery = false;
            SetStorageStatus($"Saved locally · {store.FilePath}");
            if (editRevision != savedRevision) { SetStorageStatus("Newer changes pending local save…"); saveTimer.Start(); }
            return true;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            SetStorageStatus("Save failed. Changes remain in memory; existing files are preserved. Free disk space/check permissions, then use Tools → Save workspace now, or export CSV.");
            return false;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (allowClose || (store is null && !writesBlocked)) return;
        if (!needsRecovery && editRevision == savedRevision && saveTask is not { IsCompleted: false }) return;
        e.Cancel = true;
        if (closingSave) return;
        closingSave = true;
        foreach (var context in tools.Values) context.Pause("Closing. Pending simulation cancelled.");
        IsEnabled = false;
        var saved = await SaveWorkspaceAsync();
        // A previously running save may have captured an earlier revision.
        if (saved && editRevision != savedRevision) saved = await SaveWorkspaceAsync();
        IsEnabled = true;
        closingSave = false;
        if (saved && !needsRecovery && editRevision == savedRevision)
        {
            allowClose = true;
            Dispatcher.UIThread.Post(Close);
            return;
        }
        var page = WorkspaceUi.Page("UNSAVED WORKSPACE", "Keep this window open to protect your changes",
            storageStatus + " Closing without saving discards changes since the last successful save.");
        var back = WorkspaceUi.Action("Return to workspace");
        back.Click += (_, _) => ShowDashboard();
        var discard = WorkspaceUi.Action("Close without saving");
        discard.Click += (_, _) => { allowClose = true; Close(); };
        page.Children.Add(back);
        page.Children.Add(discard);
        ShowTool("Unsaved workspace", page);
    }
}
