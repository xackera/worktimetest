namespace WorkTime.Web.Domain;

public class TimeEntry
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public required string Description { get; set; }
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>null — таймер ещё идёт.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    public bool IsRunning => EndedAt is null;

    /// <summary>
    /// Сколько целых секунд записи попадает в период [from, to).
    /// Запись может начаться до периода или закончиться после него, поэтому считается только пересечение.
    /// Незавершённая запись длится до текущего момента.
    /// </summary>
    public long SecondsWithin(DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
    {
        var start = StartedAt > from ? StartedAt : from;
        var end = EndedAt ?? now;
        if (end > to)
        {
            end = to;
        }

        return end > start ? (long)(end - start).TotalSeconds : 0;
    }
}
