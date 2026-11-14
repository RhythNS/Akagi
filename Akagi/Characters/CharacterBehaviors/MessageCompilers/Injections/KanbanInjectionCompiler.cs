using Akagi.Bridge.Attributes;
using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Kanban;
using Akagi.Characters.Conversations;
using Akagi.Kanban;
using Akagi.Life;
using Akagi.Receivers;
using System.Text;
using TextMessage = Akagi.Characters.Conversations.TextMessage;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;

/// <summary>
/// Injects what a character should know about the user's Kanban projects: their descriptions, what is being worked on,
/// what comes next and when each plan finishes.
/// </summary>
[NodeDocu("Injects the user's Kanban projects: description, work in progress, next tickets and the planned finish.")]
internal class KanbanInjectionCompiler : InjectionCompiler
{
    public enum DetailLevel
    {
        /// <summary>One line per project, plus the tickets in progress.</summary>
        Overview,
        /// <summary>Description and every open ticket with days, dependencies and plan.</summary>
        Board,
        /// <summary>Like Board, plus the descriptions and latest updates of the tickets in progress.</summary>
        Full
    }

    private string _projects = string.Empty;
    private DetailLevel _detail = DetailLevel.Overview;
    private string _header = "### {{user}}'s projects";
    private int _nextShown = 5;
    private int _descriptionLength = 1500;

    [NodeDocu("Project names separated by commas; empty injects every project that is not archived.")]
    public string Projects
    {
        get => _projects;
        set => SetProperty(ref _projects, value);
    }
    [NodeDocu("How much to inject: Overview, Board or Full.")]
    public DetailLevel Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }
    [NodeDocu("Heading above the injected block. {{user}} and {{character}} are replaced with names.")]
    public string Header
    {
        get => _header;
        set => SetProperty(ref _header, value);
    }
    [NodeDocu("Overview: how many upcoming to-do tickets to name per project.")]
    public int NextShown
    {
        get => _nextShown;
        set => SetProperty(ref _nextShown, value);
    }
    [NodeDocu("Longest project description to inject, in characters (0 leaves descriptions out).")]
    public int DescriptionLength
    {
        get => _descriptionLength;
        set => SetProperty(ref _descriptionLength, value);
    }

    protected override async Task<Message[]> GetInjectionMessagesAsync(Context context)
    {
        if (string.IsNullOrEmpty(context.User.Id))
        {
            return [];
        }

        KanbanService service = new(
            context.DatabaseFactory.GetDatabase<IKanbanProjectDatabase>(),
            context.DatabaseFactory.GetDatabase<IKanbanTicketDatabase>());
        List<KanbanProject> projects = Select(await service.ProjectsAsync(context.User.Id), Projects);
        if (projects.Count == 0)
        {
            return [];
        }

        LifeProfile profile = await context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>().GetOrCreate(context.User.Id);
        DateOnly today = DateOnly.FromDateTime(profile.ToLocal(DateTime.UtcNow));
        List<KanbanBoard> boards = [];
        foreach (KanbanProject project in projects)
        {
            boards.Add(await service.LoadAsync(context.User.Id, project.Id!));
        }

        string body = Render(boards, today, Detail, NextShown, DescriptionLength);
        string header = Header
            .Replace("{{user}}", context.User.Name)
            .Replace("{{character}}", context.Character.Name);
        return
        [
            new TextMessage
            {
                From = MessageType,
                Time = DateTime.UtcNow,
                Text = string.IsNullOrWhiteSpace(header) ? body : header + Environment.NewLine + body,
                CharacterId = context.Character.Id
            }
        ];
    }

    internal static List<KanbanProject> Select(IEnumerable<KanbanProject> projects, string names)
    {
        string[] wanted = names.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return wanted.Length == 0
            ? [.. projects.Where(p => !p.Archived)]
            : [.. projects.Where(p => wanted.Any(w => string.Equals(w, p.Name, StringComparison.OrdinalIgnoreCase)))];
    }

    internal static string Render(IReadOnlyList<KanbanBoard> boards, DateOnly today, DetailLevel detail, int nextShown, int descriptionLength)
    {
        StringBuilder text = new();
        foreach (KanbanBoard board in boards)
        {
            KanbanProject project = board.Project;
            KanbanPlan plan = board.Plan(today);
            List<KanbanTicket> open = [.. board.Tickets.Where(t => project.KindOf(t.ColumnId) != KanbanColumnKind.Done)];
            string finish = plan.FinishDate is DateOnly day ? $", plan finishes {KanbanText.Date(day)}" : string.Empty;
            text.AppendLine($"Project {project.Name}: {open.Count} open of {board.Tickets.Count} tickets, {KanbanText.Days(plan.RemainingDays)} of work left{finish}.");

            if (descriptionLength > 0 && !string.IsNullOrWhiteSpace(project.Description) && detail != DetailLevel.Overview)
            {
                string description = project.Description.Trim();
                text.AppendLine(description.Length <= descriptionLength ? description : description[..descriptionLength].TrimEnd() + " ...");
            }

            if (detail == DetailLevel.Overview)
            {
                if (descriptionLength > 0 && !string.IsNullOrWhiteSpace(project.Description))
                {
                    string firstLine = project.Description.Trim().Split('\n')[0].Trim();
                    text.AppendLine("About: " + (firstLine.Length <= 200 ? firstLine : firstLine[..200] + " ..."));
                }
                List<KanbanTicket> working = [.. open.Where(t => project.KindOf(t.ColumnId) == KanbanColumnKind.InProgress).OrderBy(t => t.Rank)];
                if (working.Count > 0)
                {
                    text.AppendLine("In progress: " + string.Join("; ", working.Select(t =>
                        $"{t.Reference} {t.Title} ({project.Column(t.ColumnId)?.Name}, {KanbanText.Days(t.RemainingDays)} left)")));
                }
                List<KanbanPlannedTicket> next = [.. plan.Items.Where(i => !i.IsHistory && i.Kind == KanbanColumnKind.Todo).Take(Math.Max(0, nextShown))];
                if (next.Count > 0)
                {
                    text.AppendLine("Next: " + string.Join("; ", next.Select(i => $"{i.Ticket.Reference} {i.Ticket.Title}")));
                }
            }
            else
            {
                text.AppendLine(KanbanText.Board(board, plan, doneShown: 3));
                if (detail == DetailLevel.Full)
                {
                    foreach (KanbanTicket ticket in open.Where(t => project.KindOf(t.ColumnId) == KanbanColumnKind.InProgress).OrderBy(t => t.Rank))
                    {
                        text.AppendLine();
                        text.AppendLine($"{ticket.Reference} {ticket.Title} in detail:");
                        if (!string.IsNullOrWhiteSpace(ticket.Description))
                        {
                            text.AppendLine(ticket.Description.Trim());
                        }
                        foreach (KanbanUpdate update in ticket.Updates.TakeLast(2))
                        {
                            text.AppendLine($"Update {KanbanText.Date(update.At)} by {update.Author}: {update.Text.Trim()}");
                        }
                    }
                }
            }
            text.AppendLine();
        }
        return text.ToString().TrimEnd();
    }
}
