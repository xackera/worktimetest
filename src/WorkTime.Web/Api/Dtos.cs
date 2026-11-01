using WorkTime.Web.Domain;

namespace WorkTime.Web.Api;

public record CreateEmployeeRequest(string? Name);

public record StartTimerRequest(string? Description);

public record EmployeeDto(int Id, string Name, ActiveTimerDto? ActiveTimer);

/// <summary>
/// ElapsedSeconds считает сервер: так счётчик в браузере не зависит от точности часов клиента.
/// </summary>
public record ActiveTimerDto(int EntryId, string Description, DateTimeOffset StartedAt, long ElapsedSeconds)
{
    public static ActiveTimerDto From(TimeEntry entry, DateTimeOffset now) =>
        new(entry.Id, entry.Description, entry.StartedAt, entry.SecondsWithin(entry.StartedAt, now, now));
}

public record TimeEntryDto(
    int Id,
    int EmployeeId,
    string Description,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    bool IsRunning,
    long SecondsInPeriod)
{
    public static TimeEntryDto From(TimeEntry entry, long secondsInPeriod) =>
        new(entry.Id, entry.EmployeeId, entry.Description, entry.StartedAt, entry.EndedAt, entry.IsRunning, secondsInPeriod);
}

public record EmployeeReportDto(
    int EmployeeId,
    string EmployeeName,
    long TotalSeconds,
    IReadOnlyList<TimeEntryDto> Entries);

public record ReportDto(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset GeneratedAt,
    long TotalSeconds,
    IReadOnlyList<EmployeeReportDto> Employees);
