using Akagi.Bridge.Chat.Models;

namespace Akagi.Bridge.Kanban;

/// <summary>
/// Where a ticket lies in the plan. Positions are work days from the plan's first day, see <see cref="KanbanCalendar"/>.
/// </summary>
public sealed class KanbanPlannedTicket
{
    public required KanbanTicketView Ticket { get; init; }
    public KanbanColumnKind Kind { get; init; }
    /// <summary>Work day position the work starts at.</summary>
    public double Start { get; init; }
    /// <summary>Work day position the work ends at.</summary>
    public double End { get; init; }
    /// <summary>Calendar position (days since the plan's first day) the bar starts at; negative for finished work.</summary>
    public double CalendarStart { get; init; }
    public double CalendarEnd { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    /// <summary>The earliest it could start: when everything it waits for is finished, or its not-before day.</summary>
    public double ReadyAt { get; init; }
    /// <summary>It waits for tickets that also wait for it; those links were ignored to place it.</summary>
    public bool InCycle { get; init; }
    /// <summary>It starts later than it could because the people working are busy with more important tickets.</summary>
    public bool WaitsForCapacity { get; init; }
    /// <summary>A finished ticket with known start and end, shown as history.</summary>
    public bool IsHistory { get; init; }
    /// <summary>Delaying it delays the end of the whole plan.</summary>
    public bool Critical { get; set; }
}

public sealed class KanbanPlan
{
    public required KanbanCalendar Calendar { get; init; }
    /// <summary>Open tickets in the order they are planned (by priority), then finished ones with history.</summary>
    public required IReadOnlyList<KanbanPlannedTicket> Items { get; init; }
    /// <summary>Work day position the last open ticket ends at.</summary>
    public double Finish { get; init; }
    public DateOnly? FinishDate { get; init; }
    public double RemainingDays { get; init; }

    public KanbanPlannedTicket? Find(string ticketId) => Items.FirstOrDefault(i => i.Ticket.Id == ticketId);
}

/// <summary>
/// Plans the open tickets of a project from today on: in progress before to do, then by rank. A ticket starts when
/// everything it waits for is finished, not before its not-before day, and only while fewer than
/// <see cref="KanbanProjectView.Parallel"/> tickets are being worked on. Tickets that others wait for inherit their
/// priority, so a blocker of an important ticket is done early.
/// </summary>
public static class KanbanScheduler
{
    public static KanbanPlan Plan(KanbanProjectView project, IEnumerable<KanbanTicketView> tickets, DateOnly today)
    {
        DateOnly origin = project.StartDate is DateTime start && DateOnly.FromDateTime(start) > today
            ? DateOnly.FromDateTime(start)
            : today;
        KanbanCalendar calendar = new(origin, project.WorkDays);

        List<KanbanTicketView> all = [.. tickets];
        List<KanbanTicketView> open = [.. all
            .Where(t => project.KindOf(t.ColumnId) != KanbanColumnKind.Done)
            .OrderBy(t => project.KindOf(t.ColumnId) == KanbanColumnKind.InProgress ? 0 : 1)
            .ThenBy(t => t.Rank)
            .ThenBy(t => t.Number)];
        HashSet<string> openIds = [.. open.Select(t => t.Id)];

        // Only links between open tickets constrain the plan; finished work is already done
        Dictionary<string, List<string>> predecessors = open.ToDictionary(
            t => t.Id,
            t => t.After.Where(id => id != t.Id && openIds.Contains(id)).Distinct().ToList());
        Dictionary<string, List<string>> successors = open.ToDictionary(t => t.Id, _ => new List<string>());
        foreach ((string id, List<string> before) in predecessors)
        {
            before.ForEach(p => successors[p].Add(id));
        }

        Dictionary<string, int> priority = EffectivePriority(open, successors);

        int capacity = Math.Clamp(project.Parallel, 1, 50);
        double[] slots = new double[capacity];
        Dictionary<string, (double Start, double End)> placed = [];
        HashSet<string> cyclic = [];
        HashSet<string> waited = [];
        Dictionary<string, double> readyAt = [];
        List<KanbanTicketView> pending = [.. open];

        while (pending.Count > 0)
        {
            List<KanbanTicketView> ready = [.. pending.Where(t => predecessors[t.Id].All(placed.ContainsKey))];
            if (ready.Count == 0)
            {
                // Only cycles are left: break one at its most important ticket
                KanbanTicketView forced = pending.OrderBy(t => priority[t.Id]).First();
                cyclic.Add(forced.Id);
                ready.Add(forced);
            }

            Dictionary<string, double> readyTimes = ready.ToDictionary(t => t.Id, t => ReadyTime(t, predecessors, placed, calendar));
            int slot = Array.IndexOf(slots, slots.Min());
            double now = slots[slot];
            List<KanbanTicketView> available = [.. ready.Where(t => readyTimes[t.Id] <= now)];
            if (available.Count == 0)
            {
                now = readyTimes.Values.Min();
                available = [.. ready.Where(t => readyTimes[t.Id] <= now)];
            }

            KanbanTicketView next = available.OrderBy(t => priority[t.Id]).First();
            double begin = Math.Max(now, readyTimes[next.Id]);
            double end = begin + Math.Max(0, next.RemainingDays);
            if (begin > readyTimes[next.Id] + 1e-9)
            {
                waited.Add(next.Id);
            }
            slots[slot] = end;
            placed[next.Id] = (begin, end);
            readyAt[next.Id] = readyTimes[next.Id];
            pending.Remove(next);
        }

        List<KanbanPlannedTicket> items = [];
        foreach (KanbanTicketView ticket in open.OrderBy(t => placed[t.Id].Start).ThenBy(t => priority[t.Id]))
        {
            (double begin, double end) = placed[ticket.Id];
            DateOnly startDate = calendar.StartDate(begin);
            DateOnly endDate = calendar.EndDate(end);
            items.Add(new KanbanPlannedTicket
            {
                Ticket = ticket,
                Kind = project.KindOf(ticket.ColumnId),
                Start = begin,
                End = end,
                CalendarStart = calendar.StartToCalendar(begin),
                CalendarEnd = end > begin ? calendar.EndToCalendar(end) : calendar.StartToCalendar(begin),
                StartDate = startDate,
                EndDate = endDate < startDate ? startDate : endDate,
                ReadyAt = readyAt[ticket.Id],
                InCycle = cyclic.Contains(ticket.Id),
                WaitsForCapacity = waited.Contains(ticket.Id)
            });
        }

        double finish = items.Count == 0 ? 0 : items.Max(i => i.End);
        MarkCritical(items, predecessors, finish);

        foreach (KanbanTicketView ticket in all.Where(t => project.KindOf(t.ColumnId) == KanbanColumnKind.Done && t.DoneAt.HasValue)
                     .OrderBy(t => t.DoneAt))
        {
            DateOnly doneDate = DateOnly.FromDateTime(ticket.DoneAt!.Value);
            DateOnly startDate = ticket.StartedAt is DateTime started ? DateOnly.FromDateTime(started) : doneDate;
            if (startDate > doneDate)
            {
                startDate = doneDate;
            }
            items.Add(new KanbanPlannedTicket
            {
                Ticket = ticket,
                Kind = KanbanColumnKind.Done,
                CalendarStart = startDate.DayNumber - origin.DayNumber,
                CalendarEnd = doneDate.DayNumber - origin.DayNumber + 1,
                StartDate = startDate,
                EndDate = doneDate,
                IsHistory = true
            });
        }

        return new KanbanPlan
        {
            Calendar = calendar,
            Items = items,
            Finish = finish,
            FinishDate = open.Count == 0 ? null : calendar.EndDate(finish) < origin ? origin : calendar.EndDate(finish),
            RemainingDays = open.Sum(t => Math.Max(0, t.RemainingDays))
        };
    }

    private static double ReadyTime(KanbanTicketView ticket, Dictionary<string, List<string>> predecessors,
        Dictionary<string, (double Start, double End)> placed, KanbanCalendar calendar)
    {
        double ready = 0;
        foreach (string id in predecessors[ticket.Id])
        {
            if (placed.TryGetValue(id, out (double Start, double End) before))
            {
                ready = Math.Max(ready, before.End);
            }
        }
        if (ticket.NotBefore is DateTime notBefore)
        {
            ready = Math.Max(ready, calendar.WorkAt(DateOnly.FromDateTime(notBefore)));
        }
        return ready;
    }

    /// <summary>
    /// A ticket's place in line: its own, or that of the most important ticket waiting for it, whichever comes first.
    /// </summary>
    private static Dictionary<string, int> EffectivePriority(List<KanbanTicketView> open, Dictionary<string, List<string>> successors)
    {
        Dictionary<string, int> priority = [];
        for (int i = 0; i < open.Count; i++)
        {
            priority[open[i].Id] = i;
        }
        // Relax along the links; a cycle cannot lower anything below its own best, so this ends
        bool changed = true;
        for (int round = 0; changed && round <= open.Count; round++)
        {
            changed = false;
            foreach (KanbanTicketView ticket in open)
            {
                foreach (string successor in successors[ticket.Id])
                {
                    if (priority[successor] < priority[ticket.Id])
                    {
                        priority[ticket.Id] = priority[successor];
                        changed = true;
                    }
                }
            }
        }
        // Ties keep the original order
        return open
            .Select((t, index) => (t.Id, Key: (long)priority[t.Id] * (open.Count + 1) + index))
            .OrderBy(p => p.Key)
            .Select((p, rank) => (p.Id, rank))
            .ToDictionary(p => p.Id, p => p.rank);
    }

    /// <summary>
    /// Walks back from the tickets that end the plan through whatever made them start when they did: a ticket they
    /// waited for that ended right then, or the ticket the same person finished right then.
    /// </summary>
    private static void MarkCritical(List<KanbanPlannedTicket> items, Dictionary<string, List<string>> predecessors, double finish)
    {
        if (items.Count == 0)
        {
            return;
        }
        const double Epsilon = 1e-6;
        Dictionary<string, KanbanPlannedTicket> byId = items.ToDictionary(i => i.Ticket.Id);
        Stack<KanbanPlannedTicket> stack = new(items.Where(i => Math.Abs(i.End - finish) < Epsilon));
        while (stack.Count > 0)
        {
            KanbanPlannedTicket item = stack.Pop();
            if (item.Critical)
            {
                continue;
            }
            item.Critical = true;
            if (item.Start < Epsilon)
            {
                continue;
            }
            List<KanbanPlannedTicket> causes = [.. predecessors[item.Ticket.Id]
                .Select(id => byId[id])
                .Where(p => Math.Abs(p.End - item.Start) < Epsilon)];
            if (causes.Count == 0 && item.WaitsForCapacity)
            {
                causes = [.. items.Where(p => p != item && Math.Abs(p.End - item.Start) < Epsilon && p.End > p.Start)];
            }
            causes.ForEach(stack.Push);
        }
    }
}
