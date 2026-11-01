using Dapper;
using Npgsql;
using WorkTime.Web.Api;
using WorkTime.Web.Domain;

namespace WorkTime.Web.Services;

public class TimeTrackingService(NpgsqlDataSource dataSource, TimeProvider clock)
{
    private const int MaxNameLength = 100;
    private const int MaxDescriptionLength = 500;

    public async Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT e.id, e.name, t.id, t.employee_id, t.description, t.started_at, t.ended_at
            FROM employees e
            LEFT JOIN time_entries t ON t.employee_id = e.id AND t.ended_at IS NULL
            """;

        await using var connection = dataSource.CreateConnection();
        var rows = await connection.QueryAsync<Employee, TimeEntry?, (Employee Employee, TimeEntry? Running)>(
            new CommandDefinition(sql, cancellationToken: ct),
            (employee, running) => (employee, running),
            splitOn: "id");

        var now = clock.GetUtcNow();
        return rows
            .OrderBy(r => r.Employee.Name, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(r => r.Employee.Id)
            .Select(r => new EmployeeDto(r.Employee.Id, r.Employee.Name, r.Running is null ? null : ActiveTimerDto.From(r.Running, now)))
            .ToList();
    }

    public async Task<EmployeeDto> CreateEmployeeAsync(string? name, CancellationToken ct)
    {
        const string sql = "INSERT INTO employees (name) VALUES (@Name) RETURNING id";

        var text = RequireText(name, "Имя сотрудника", MaxNameLength);

        await using var connection = dataSource.CreateConnection();
        var id = await connection.QuerySingleAsync<int>(new CommandDefinition(sql, new { Name = text }, cancellationToken: ct));
        return new EmployeeDto(id, text, null);
    }

    public async Task<TimeEntryDto> StartTimerAsync(int employeeId, string? description, CancellationToken ct)
    {
        // Один атомарный запрос: между проверкой и вставкой нет окна, в которое мог бы попасть параллельный старт.
        // Строка не вставится, если таймер уже идёт (ON CONFLICT) или сотрудника нет (пустой SELECT).
        const string sql = """
            INSERT INTO time_entries (employee_id, description, started_at)
            SELECT id, @Description, @StartedAt FROM employees WHERE id = @EmployeeId
            ON CONFLICT (employee_id) WHERE ended_at IS NULL DO NOTHING
            RETURNING id, employee_id, description, started_at, ended_at
            """;

        var text = RequireText(description, "Описание задачи", MaxDescriptionLength);

        await using var connection = dataSource.CreateConnection();
        var parameters = new { EmployeeId = employeeId, Description = text, StartedAt = CurrentSecond() };
        var entry = await connection.QuerySingleOrDefaultAsync<TimeEntry>(new CommandDefinition(sql, parameters, cancellationToken: ct));
        if (entry is not null)
        {
            return TimeEntryDto.From(entry, secondsInPeriod: 0);
        }

        await EnsureEmployeeExistsAsync(connection, employeeId, ct);

        var running = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT description FROM time_entries WHERE employee_id = @EmployeeId AND ended_at IS NULL",
            new { EmployeeId = employeeId },
            cancellationToken: ct));
        throw new ConflictException($"Таймер уже запущен: «{running}». Сначала остановите его.");
    }

    public async Task<TimeEntryDto> StopTimerAsync(int employeeId, CancellationToken ct)
    {
        // Условие ended_at IS NULL гарантирует, что из двух одновременных запросов сработает только один.
        const string sql = """
            UPDATE time_entries SET ended_at = @EndedAt
            WHERE employee_id = @EmployeeId AND ended_at IS NULL
            RETURNING id, employee_id, description, started_at, ended_at
            """;

        var now = CurrentSecond();

        await using var connection = dataSource.CreateConnection();
        var entry = await connection.QuerySingleOrDefaultAsync<TimeEntry>(
            new CommandDefinition(sql, new { EmployeeId = employeeId, EndedAt = now }, cancellationToken: ct));
        if (entry is not null)
        {
            return TimeEntryDto.From(entry, entry.SecondsWithin(entry.StartedAt, now, now));
        }

        await EnsureEmployeeExistsAsync(connection, employeeId, ct);
        throw new ConflictException("Нет запущенного таймера.");
    }

    /// <summary>
    /// Текущий момент без долей секунды. Если хранить доли, а длительность показывать в целых секундах,
    /// итог каждой записи занижается, и потери накапливаются в сумме за период.
    /// </summary>
    private DateTimeOffset CurrentSecond()
    {
        var now = clock.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
    }

    private static async Task EnsureEmployeeExistsAsync(NpgsqlConnection connection, int employeeId, CancellationToken ct)
    {
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM employees WHERE id = @EmployeeId)",
            new { EmployeeId = employeeId },
            cancellationToken: ct));
        if (!exists)
        {
            throw new NotFoundException($"Сотрудник с id {employeeId} не найден.");
        }
    }

    private static string RequireText(string? value, string fieldName, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidInputException($"{fieldName}: значение не может быть пустым.");
        }

        if (text.Length > maxLength)
        {
            throw new InvalidInputException($"{fieldName}: максимум {maxLength} символов.");
        }

        // PostgreSQL не умеет хранить символ NUL в тексте и ответил бы ошибкой на вставку.
        if (text.Contains('\0'))
        {
            throw new InvalidInputException($"{fieldName}: недопустимые символы.");
        }

        return text;
    }
}
