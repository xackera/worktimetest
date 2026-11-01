using Dapper;
using Npgsql;
using WorkTime.Web.Api;
using WorkTime.Web.Domain;

namespace WorkTime.Web.Services;

public class ReportService(NpgsqlDataSource dataSource, TimeProvider clock)
{
    private const string EmployeesSql = """
        SELECT id, name FROM employees
        WHERE @EmployeeId::integer IS NULL OR id = @EmployeeId
        """;

    // Попадают записи, пересекающие период [Start, End). Незавершённая запись длится до текущего момента.
    private const string EntriesSql = """
        SELECT id, employee_id, description, started_at, ended_at
        FROM time_entries
        WHERE (@EmployeeId::integer IS NULL OR employee_id = @EmployeeId)
          AND started_at < @End
          AND COALESCE(ended_at, @Now) > @Start
        ORDER BY started_at, id
        """;

    /// <summary>
    /// Отчёт за период [from, to). Границы — моменты времени, а не даты: где начинаются сутки,
    /// решает клиент, потому что только он знает часовой пояс пользователя.
    /// </summary>
    public async Task<ReportDto> BuildAsync(
        DateTimeOffset? from, DateTimeOffset? to, int? employeeId, CancellationToken ct)
    {
        if (from is null || to is null)
        {
            throw new InvalidInputException("Укажите начало и конец периода.");
        }

        if (to <= from)
        {
            throw new InvalidInputException("Конец периода должен быть позже начала.");
        }

        // Npgsql принимает для timestamptz только значения со смещением 0.
        var start = from.Value.ToUniversalTime();
        var end = to.Value.ToUniversalTime();
        var now = clock.GetUtcNow();

        await using var connection = dataSource.CreateConnection();

        var employees = (await connection.QueryAsync<Employee>(
            new CommandDefinition(EmployeesSql, new { EmployeeId = employeeId }, cancellationToken: ct))).ToList();

        if (employeeId is not null && employees.Count == 0)
        {
            throw new NotFoundException($"Сотрудник с id {employeeId} не найден.");
        }

        var entries = await connection.QueryAsync<TimeEntry>(new CommandDefinition(
            EntriesSql,
            new { EmployeeId = employeeId, Start = start, End = end, Now = now },
            cancellationToken: ct));

        var entriesByEmployee = entries.ToLookup(e => e.EmployeeId);

        var rows = employees
            .OrderBy(e => e.Name, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(e => e.Id)
            .Select(employee =>
            {
                var items = entriesByEmployee[employee.Id]
                    .Select(e => TimeEntryDto.From(e, e.SecondsWithin(start, end, now)))
                    .ToList();
                return new EmployeeReportDto(employee.Id, employee.Name, items.Sum(i => i.SecondsInPeriod), items);
            })
            .ToList();

        return new ReportDto(start, end, now, rows.Sum(r => r.TotalSeconds), rows);
    }
}
