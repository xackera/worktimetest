using System.Net;
using System.Net.Http.Json;
using Dapper;
using Npgsql;
using WorkTime.Tests.Infrastructure;
using WorkTime.Web.Api;

namespace WorkTime.Tests;

/// <summary>
/// Старт и стоп — один атомарный SQL-запрос, поэтому окна между проверкой и записью в коде нет.
/// Здесь проверяются правила самой базы и то, что второй запрос не меняет результат первого.
/// </summary>
public class ConcurrencyTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string InsertRunningEntry = """
        INSERT INTO time_entries (employee_id, description, started_at)
        VALUES (@EmployeeId, 'Напрямую в базу', now())
        """;

    [Fact]
    public async Task Database_itself_forbids_second_running_timer()
    {
        var employee = await AddEmployee();
        await StartTimer(employee.Id);
        await using var connection = new NpgsqlConnection(ConnectionString);

        var error = await Assert.ThrowsAsync<PostgresException>(
            () => connection.ExecuteAsync(InsertRunningEntry, new { EmployeeId = employee.Id }));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("ux_time_entries_one_running", error.ConstraintName);
    }

    [Fact]
    public async Task Finished_entries_do_not_block_new_running_timer_in_database()
    {
        var employee = await AddEmployee();
        await StartTimer(employee.Id);
        await StopTimer(employee.Id);
        await using var connection = new NpgsqlConnection(ConnectionString);

        await connection.ExecuteAsync(InsertRunningEntry, new { EmployeeId = employee.Id });
    }

    [Fact]
    public async Task Second_stop_is_rejected_and_keeps_first_end_time()
    {
        var employee = await AddEmployee();
        await StartTimer(employee.Id);
        var firstStop = Start.AddMinutes(1);
        Clock.Now = firstStop;
        await StopTimer(employee.Id);
        Clock.Now = firstStop.AddMinutes(1);

        await AssertProblem(await StopTimer(employee.Id), HttpStatusCode.Conflict);

        var report = await GetReport(Start, Start.AddHours(1));
        Assert.Equal(firstStop, Assert.Single(Assert.Single(report.Employees).Entries).EndedAt);
    }

    [Fact]
    public async Task Parallel_start_and_stop_never_leave_more_than_one_running_timer()
    {
        var employee = await AddEmployee();

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => i % 2 == 0 ? StartTimer(employee.Id) : StopTimer(employee.Id)));

        await using var connection = new NpgsqlConnection(ConnectionString);
        var running = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM time_entries WHERE employee_id = @EmployeeId AND ended_at IS NULL",
            new { EmployeeId = employee.Id });
        Assert.InRange(running, 0, 1);

        var listed = Assert.Single((await Client.GetFromJsonAsync<List<EmployeeDto>>("/api/employees"))!);
        Assert.Equal(running == 1, listed.ActiveTimer is not null);
    }
}
