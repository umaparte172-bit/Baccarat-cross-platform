using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Baccarat.Core.Communication;
using Baccarat.Infrastructure.Communication;

namespace Baccarat.Desktop.Views;

public sealed class ViewerView : StackPanel
{
    private readonly ViewerCommunicationModule communication;
    private readonly TextBlock connection = Text("Local communication ready", 17, "#7BD88F");
    private readonly TextBlock session = Text("No session document received.", 17, "#A8B5CA");
    private readonly TextBlock strategy = Text("No strategy document received.", 17, "#A8B5CA");
    private readonly TextBlock automation = Text("No automation document received.", 17, "#A8B5CA");
    private readonly StackPanel history = new() { Spacing = 6 };

    public ViewerView(ViewerCommunicationModule communication)
    {
        this.communication = communication ?? throw new ArgumentNullException(nameof(communication));
        Margin = new Thickness(0);
        Spacing = 0;
        var page = WorkspaceUi.Page("VIEWER WORKSPACE", "Session viewer",
            "Live local snapshots from the session, strategy, and automation workspaces.");
        Children.Add(page);
        page.Children.Add(Text(
            $"Protocol {ViewerDocumentContract.EnvelopeVersion} · Session document {ViewerDocumentContract.SessionSnapshotVersion} · Strategy document {ViewerDocumentContract.StrategySnapshotVersion} · Automation document {ViewerDocumentContract.AutomationSnapshotVersion}",
            14,
            "#91A1B9"));
        page.Children.Add(WorkspaceUi.Card("COMMUNICATION", connection));
        page.Children.Add(WorkspaceUi.Card("SESSION", session));
        page.Children.Add(WorkspaceUi.Card("STRATEGY", strategy));
        page.Children.Add(WorkspaceUi.Card("AUTOMATION", automation));
        page.Children.Add(WorkspaceUi.Card("DOCUMENT HISTORY", history));

        communication.DocumentReceived += OnDocumentReceived;
        RefreshHistory();
    }

    private void OnDocumentReceived(ViewerDocument document)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnDocumentReceived(document));
            return;
        }

        switch (document.Payload)
        {
            case SessionSnapshotPayload value:
                session.Text = $"Rounds {value.RoundCount} · Player {value.PlayerCount} · Banker {value.BankerCount} · Tie {value.TieCount}";
                break;
            case StrategySnapshotPayload value:
                strategy.Text = $"{value.StrategyName} · Profit {value.Profit:0.00} · {value.State} · {value.Recommendation}";
                break;
            case CommunicationStatusPayload value:
                connection.Text = $"{value.State}: {value.Message}";
                break;
            case AutomationSnapshotPayload value:
                automation.Text = $"{value.State} · Auto {(value.AutoBetEnabled ? "enabled" : "disabled")} · " +
                                  $"{(value.IsSimulation ? "Simulation" : "External")} · Accepted {value.AcceptedBets} · " +
                                  $"Stake {value.SessionStake:0.00} · {value.Message}";
                break;
        }

        RefreshHistory();
    }

    private void RefreshHistory()
    {
        history.Children.Clear();
        var documents = communication.Documents;
        if (documents.Count == 0)
        {
            history.Children.Add(Text("No versioned documents received.", 15, "#91A1B9"));
            return;
        }

        foreach (var document in documents.Reverse().Take(30))
            history.Children.Add(WorkspaceUi.HistoryRow(
                $"#{document.Sequence:000} · {document.DocumentType} v{document.DocumentVersion} · {document.CreatedAt:HH:mm:ss} UTC"));
    }

    private static TextBlock Text(string value, double size, string color = "#E9EEF6") =>
        new() { Text = value, FontSize = size, Foreground = Brush.Parse(color), TextWrapping = TextWrapping.Wrap };
}
