using System.Net;
using System.Net.Http.Json;
using WorkTime.Tests.Infrastructure;
using WorkTime.Web.Api;

namespace WorkTime.Tests;

public class TimerTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    [Fact]
    public async Task Started_timer_is_shown_on_employee_and_counts_elapsed_time()
    {
        var employee = await AddEmployee();

        var response = await StartTimer(employee.Id, "  Подготовка отчёта  ");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Clock.Advance(TimeSpan.FromMinutes(5));

        var listed = Assert.Single((await Client.GetFromJsonAsync<List<EmployeeDto>>("/api/employees"))!);
        Assert.Equal("Подготовка отчёта", listed.ActiveTimer!.Description);
        Assert.Equal(Start, listed.ActiveTimer.StartedAt);
        Assert.Equal(300, listed.ActiveTimer.ElapsedSeconds);
    }

    [Fact]
    public async Task Stop_records_end_time_and_duration()
    {
        var employee = await AddEmployee();
        await StartTimer(employee.Id);
        Clock.Advance(TimeSpan.FromMinutes(90));

        var response = await StopTimer(employee.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = (await response.Content.ReadFromJsonAsync<TimeEntryDto>())!;
        Assert.False(entry.IsRunning);
        Assert.Equal(Start.AddMinutes(90), entry.EndedAt);
        Assert.Equal(5400, entry.SecondsInPeriod);
    }

    [Fact]
    public async Task Fractions_of_second_do_not_accumulate_into_lost_time()
    {
        var employee = await AddEmployee();

        // 10 записей по 1,9 с, идущих подряд: всего 19 с. При округлении длительности каждой записи вышло бы 10 с.
        for (var i = 0; i < 10; i++)
        {
            await AddFinishedEntry(employee.Id, Start.AddMilliseconds(i * 1900), TimeSpan.FromMilliseconds(1900));
        }

        var report = await GetReport(Start, Start.AddMinutes(1));

        Assert.Equal(19, report.TotalSeconds);
    }

    [Fact]
    public async Task Stopped_timer_is_no_longer_shown_as_running()
    {
        var employee = await AddEmployee();
        await StartTimer(employee.Id);
        await StopTimer(employee.Id);

        var listed = Assert.Single((await Client.GetFromJsonAsync<List<EmployeeDto>>("/api/employees"))!);

        Assert.Null(listed.ActiveTimer);
    }

    [Fact]
    public async Task Second_start_is_rejected_and_keeps_running_timer_untouched()
    {
        var employee = await AddEmployee();
        await StartTimer(employee.Id, "Первая задача");
        Clock.Advance(TimeSpan.FromMinutes(10));

        var second = await StartTimer(employee.Id, "Вторая задача");

        await AssertProblem(second, HttpStatusCode.Conflict);
        var listed = Assert.Single((await Client.GetFromJsonAsync<List<EmployeeDto>>("/api/employees"))!);
        Assert.Equal("Первая задача", listed.ActiveTimer!.Description);
        Assert.Equal(Start, listed.ActiveTimer.StartedAt);
    }

    [Fact]
    public async Task Parallel_starts_create_exactly_one_running_timer()
    {
        var employee = await AddEmployee();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => StartTimer(employee.Id)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Parallel_stops_succeed_exactly_once()
    {
        var employee = await AddEmployee();
        await StartTimer(employee.Id);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => StopTimer(employee.Id)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Timer_can_be_started_again_after_stop()
    {
        var employee = await AddEmployee();
        await StartTimer(employee.Id);
        await StopTimer(employee.Id);

        var response = await StartTimer(employee.Id, "Новая задача");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Different_employees_can_run_timers_at_the_same_time()
    {
        var first = await AddEmployee("Анна");
        var second = await AddEmployee("Борис");

        Assert.Equal(HttpStatusCode.OK, (await StartTimer(first.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await StartTimer(second.Id)).StatusCode);
    }

    [Fact]
    public async Task Stop_without_running_timer_is_rejected()
    {
        var employee = await AddEmployee();

        await AssertProblem(await StopTimer(employee.Id), HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Start_without_description_is_rejected(string? description)
    {
        var employee = await AddEmployee();

        await AssertProblem(await StartTimer(employee.Id, description), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Too_long_description_is_rejected()
    {
        var employee = await AddEmployee();

        await AssertProblem(await StartTimer(employee.Id, new string('я', 501)), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Description_with_nul_character_is_rejected()
    {
        var employee = await AddEmployee();

        await AssertProblem(await StartTimer(employee.Id, "а\0б"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Timer_actions_for_unknown_employee_return_not_found()
    {
        await AssertProblem(await StartTimer(999), HttpStatusCode.NotFound);
        await AssertProblem(await StopTimer(999), HttpStatusCode.NotFound);
    }
}
