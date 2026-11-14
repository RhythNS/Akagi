using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Kanban;
using Akagi.Life;
using Akagi.Users;
using Microsoft.Extensions.Logging;

namespace Akagi.Communication.SocketComs.Transmissions;

/// <summary>
/// The website's Kanban boards and plans. Every change answers with the whole project, so the page shows what the
/// bot stored, including changes characters made in between.
/// </summary>
internal class KanbanRequestHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(KanbanRequestTransmission);

    private readonly KanbanService _service;
    private readonly ILifeProfileDatabase _profiles;
    private readonly ILogger<KanbanRequestHandler> _logger;

    public KanbanRequestHandler(KanbanService service, ILifeProfileDatabase profiles, ILogger<KanbanRequestHandler> logger)
    {
        _service = service;
        _profiles = profiles;
        _logger = logger;
    }

    public override Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        User user = context.User ?? throw new ArgumentNullException(nameof(context), "User cannot be null in KanbanRequestHandler");
        KanbanRequestTransmission request = GetTransmission<KanbanRequestTransmission>(transmissionWrapper);
        SocketSession session = context.Session;
        _ = Task.Run(async () => session.SendTransmission(await ProcessAsync(user.Id!, user.Name, request)));
        return Task.CompletedTask;
    }

    internal async Task<KanbanResponseTransmission> ProcessAsync(string userId, string userName, KanbanRequestTransmission request)
    {
        KanbanResponseTransmission response = new() { RequestId = request.RequestId };
        try
        {
            LifeProfile profile = await _profiles.GetOrCreate(userId);
            DateOnly today = DateOnly.FromDateTime(profile.ToLocal(DateTime.UtcNow));
            response.Today = KanbanViews.Day(today);

            KanbanBoard? board = null;
            switch (request.Action)
            {
                case KanbanAction.ListProjects:
                    response.Projects = [.. await _service.SummariesAsync(userId, today)];
                    break;

                case KanbanAction.Load:
                    board = await _service.LoadAsync(userId, request.ProjectId);
                    break;

                case KanbanAction.Peek:
                    response.Project = (await _service.LoadAsync(userId, request.ProjectId)).ProjectView();
                    break;

                case KanbanAction.SaveProject:
                    board = await _service.SaveProjectAsync(userId, request.Project ?? throw new KanbanException("The project is missing."));
                    break;

                case KanbanAction.DeleteProject:
                    await _service.DeleteProjectAsync(userId, request.ProjectId);
                    response.Projects = [.. await _service.SummariesAsync(userId, today)];
                    break;

                case KanbanAction.SaveTicket:
                {
                    Bridge.Chat.Models.KanbanTicketView ticket = request.Ticket ?? throw new KanbanException("The ticket is missing.");
                    if (string.IsNullOrEmpty(ticket.Id))
                    {
                        KanbanTicket created = await _service.CreateTicketAsync(userId, request.ProjectId, ticket.Title, ticket.Description,
                            ticket.ColumnId, ticket.RequiredDays, ticket.RemainingDays, notBefore: ticket.NotBefore);
                        response.TicketId = created.Id;
                        board = await _service.LoadAsync(userId, created.ProjectId);
                    }
                    else
                    {
                        (board, KanbanTicket changed) = await _service.UpdateTicketAsync(userId, ticket.Id, new KanbanTicketChange
                        {
                            Title = ticket.Title,
                            Description = ticket.Description,
                            ColumnId = ticket.ColumnId,
                            RequiredDays = ticket.RequiredDays,
                            RemainingDays = ticket.RemainingDays,
                            SetNotBefore = true,
                            NotBefore = ticket.NotBefore
                        });
                        response.TicketId = changed.Id;
                    }
                    break;
                }

                case KanbanAction.DeleteTicket:
                    board = await _service.DeleteTicketAsync(userId, request.TicketId);
                    break;

                case KanbanAction.MoveTicket:
                    (board, _) = await _service.MoveTicketAsync(userId, request.TicketId, request.ColumnId, request.OrderedIds);
                    response.TicketId = request.TicketId;
                    break;

                case KanbanAction.Reorder:
                    board = await _service.ReorderAsync(userId, request.ProjectId, request.OrderedIds);
                    break;

                case KanbanAction.AddUpdate:
                    (board, _) = await _service.AddUpdateAsync(userId, request.TicketId, userName, request.Text, request.RemainingDays);
                    response.TicketId = request.TicketId;
                    break;

                case KanbanAction.EditUpdate:
                    (board, _) = await _service.EditUpdateAsync(userId, request.TicketId, request.UpdateId, request.Text);
                    response.TicketId = request.TicketId;
                    break;

                case KanbanAction.DeleteUpdate:
                    (board, _) = await _service.DeleteUpdateAsync(userId, request.TicketId, request.UpdateId);
                    response.TicketId = request.TicketId;
                    break;

                case KanbanAction.Link:
                    board = await _service.LinkAsync(userId, request.TicketId, request.OtherTicketId);
                    break;

                case KanbanAction.Unlink:
                    board = await _service.UnlinkAsync(userId, request.TicketId, request.OtherTicketId);
                    break;

                case KanbanAction.Plan:
                    board = await _service.PlanAsync(userId, request.ProjectId, request.Estimates);
                    break;

                default:
                    response.Error = "Unknown Kanban request.";
                    break;
            }

            if (board != null)
            {
                response.Project = board.ProjectView();
                response.Tickets = board.TicketViews();
            }
        }
        catch (KanbanException ex)
        {
            response.Error = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kanban request {Action} failed for user {UserId}", request.Action, userId);
            response.Error = "Something went wrong. Try again in a moment.";
        }
        return response;
    }
}
