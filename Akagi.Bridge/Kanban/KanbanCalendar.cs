namespace Akagi.Bridge.Kanban;

/// <summary>
/// Converts between work days counted from a first day and calendar positions. Work day positions are fractional:
/// 0 is the start of the first work day on or after <see cref="Origin"/>, 1.5 is the middle of the second one.
/// Calendar positions are days since <see cref="Origin"/> at midnight, so weekends show up as gaps.
/// </summary>
public sealed class KanbanCalendar
{
    private readonly bool[] _workDays = new bool[7];
    private readonly List<int> _offsets = [];

    public KanbanCalendar(DateOnly origin, int workDayMask)
    {
        Origin = origin;
        bool any = false;
        for (int day = 0; day < 7; day++)
        {
            _workDays[day] = (workDayMask & (1 << day)) != 0;
            any |= _workDays[day];
        }
        if (!any)
        {
            // A week without work days would never finish anything; every day counts instead
            Array.Fill(_workDays, true);
        }
    }

    public DateOnly Origin { get; }

    public bool IsWorkDay(DateOnly date) => _workDays[(int)date.DayOfWeek];

    /// <summary>
    /// Calendar offset of the n-th work day (0 based) from <see cref="Origin"/>.
    /// </summary>
    public int WorkDayOffset(int index)
    {
        if (index < 0)
        {
            return index;
        }
        int next = _offsets.Count == 0 ? 0 : _offsets[^1] + 1;
        while (_offsets.Count <= index)
        {
            if (IsWorkDay(Origin.AddDays(next)))
            {
                _offsets.Add(next);
            }
            next++;
        }
        return _offsets[index];
    }

    public DateOnly WorkDate(int index) => Origin.AddDays(WorkDayOffset(index));

    /// <summary>
    /// Where a piece of work that starts at this work day position begins on the calendar.
    /// </summary>
    public double StartToCalendar(double work)
    {
        int whole = (int)Math.Floor(work);
        return WorkDayOffset(whole) + (work - whole);
    }

    /// <summary>
    /// Where a piece of work that ends at this work day position ends on the calendar: a whole number ends with the
    /// work day before it instead of at the start of the next one, so Friday's work does not reach into Monday.
    /// </summary>
    public double EndToCalendar(double work)
    {
        int whole = (int)Math.Floor(work);
        double fraction = work - whole;
        if (fraction == 0 && whole > 0)
        {
            whole--;
            fraction = 1;
        }
        return WorkDayOffset(whole) + fraction;
    }

    /// <summary>
    /// The work day position at a calendar position. A position on a day off counts as the end of the work day
    /// before it.
    /// </summary>
    public double CalendarToWork(double calendar)
    {
        if (calendar <= 0)
        {
            return 0;
        }
        int day = (int)Math.Floor(calendar);
        double fraction = calendar - day;
        int before = 0;
        for (int offset = 0; offset < day; offset++)
        {
            if (IsWorkDay(Origin.AddDays(offset)))
            {
                before++;
            }
        }
        return IsWorkDay(Origin.AddDays(day)) ? before + fraction : before;
    }

    /// <summary>
    /// The work day position at the start of this date: the date itself when it is a work day, otherwise the next
    /// work day.
    /// </summary>
    public double WorkAt(DateOnly date) => CalendarToWork(date.DayNumber - Origin.DayNumber);

    /// <summary>
    /// The date a piece of work starting at this position starts on.
    /// </summary>
    public DateOnly StartDate(double work) => Origin.AddDays((int)Math.Floor(StartToCalendar(work)));

    /// <summary>
    /// The last date a piece of work ending at this position is worked on.
    /// </summary>
    public DateOnly EndDate(double work) => Origin.AddDays((int)Math.Ceiling(EndToCalendar(work)) - 1);

    /// <summary>
    /// Work days from the first date to the last one, both included.
    /// </summary>
    public int CountWorkDays(DateOnly from, DateOnly to)
    {
        int count = 0;
        for (DateOnly day = from; day <= to; day = day.AddDays(1))
        {
            if (IsWorkDay(day))
            {
                count++;
            }
        }
        return count;
    }
}
