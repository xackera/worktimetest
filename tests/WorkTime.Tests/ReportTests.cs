using System.Net;
using WorkTime.Tests.Infrastructure;

namespace WorkTime.Tests;

public class ReportTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static readonly DateTimeOffset Day = new(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextDay = Day.AddDays(1);

    [Fact]
    public async Task Report_without_employees_is_empty()
    {
        var report = await GetReport(Day, NextDay);

        Assert.Empty(report.Employees);
        Assert.Equal(0, report.TotalSeconds);
    }

    [Fact]
    public async Task Employee_without_entries_is_listed_with_zero_total()
    {
        var employee = await AddEmployee();

        var report = await GetReport(Day, NextDay);

        var row = Assert.Single(report.Employees);
        Assert.Equal(employee.Id, row.EmployeeId);
        Assert.Equal(0, row.TotalSeconds);
        Assert.Empty(row.Entries);
    }

    [Fact]
    public async Task Period_without_entries_gives_empty_report_even_if_entries_exist_elsewhere()
    {
        var employee = await AddEmployee();
        await AddFinishedEntry(employee.Id, Day.AddHours(9), TimeSpan.FromHours(1));

        var report = await GetReport(Day.AddDays(5), Day.AddDays(6));

        Assert.Empty(Assert.Single(report.Employees).Entries);
        Assert.Equal(0, report.TotalSeconds);
    }

    [Fact]
    public async Task Totals_are_summed_per_employee_and_overall()
    {
        var anna = await AddEmployee("Анна");
        var boris = await AddEmployee("Борис");
        await AddFinishedEntry(anna.Id, Day.AddHours(9), TimeSpan.FromHours(1));
        await AddFinishedEntry(anna.Id, Day.AddHours(11), TimeSpan.FromMinutes(30));
        await AddFinishedEntry(boris.Id, Day.AddHours(9), TimeSpan.FromMinutes(15));

        var report = await GetReport(Day, NextDay);

        var annaRow = report.Employees.Single(r => r.EmployeeId == anna.Id);
        Assert.Equal(5400, annaRow.TotalSeconds);
        Assert.Equal(2, annaRow.Entries.Count);
        Assert.Equal(900, report.Employees.Single(r => r.EmployeeId == boris.Id).TotalSeconds);
        Assert.Equal(6300, report.TotalSeconds);
    }

    [Fact]
    public async Task Entries_are_sorted_by_start_time()
    {
        var employee = await AddEmployee();
        await AddFinishedEntry(employee.Id, Day.AddHours(9), TimeSpan.FromHours(1), "Первая");
        await AddFinishedEntry(employee.Id, Day.AddHours(12), TimeSpan.FromHours(1), "Вторая");

        var report = await GetReport(Day, NextDay);

        Assert.Equal(["Первая", "Вторая"], Assert.Single(report.Employees).Entries.Select(e => e.Description));
    }

    [Fact]
    public async Task Entry_crossing_period_boundary_counts_only_its_part_in_period()
    {
        var employee = await AddEmployee();
        await AddFinishedEntry(employee.Id, NextDay.AddHours(-1), TimeSpan.FromHours(3));

        var firstDay = await GetReport(Day, NextDay);
        var secondDay = await GetReport(NextDay, NextDay.AddDays(1));

        Assert.Equal(3600, firstDay.TotalSeconds);
        Assert.Equal(7200, secondDay.TotalSeconds);
    }

    [Fact]
    public async Task Entry_ending_exactly_at_period_start_is_not_included()
    {
        var employee = await AddEmployee();
        await AddFinishedEntry(employee.Id, Day.AddHours(-1), TimeSpan.FromHours(1));

        var report = await GetReport(Day, NextDay);

        Assert.Empty(Assert.Single(report.Employees).Entries);
    }

    [Fact]
    public async Task Entry_starting_exactly_at_period_end_is_not_included()
    {
        var employee = await AddEmployee();
        await AddFinishedEntry(employee.Id, NextDay, TimeSpan.FromHours(1));

        var report = await GetReport(Day, NextDay);

        Assert.Empty(Assert.Single(report.Employees).Entries);
    }

    [Fact]
    public async Task Running_timer_counts_until_now()
    {
        var employee = await AddEmployee();
        Clock.Now = Day.AddHours(9);
        await StartTimer(employee.Id);
        Clock.Advance(TimeSpan.FromMinutes(20));

        var report = await GetReport(Day, NextDay);

        var entry = Assert.Single(Assert.Single(report.Employees).Entries);
        Assert.True(entry.IsRunning);
        Assert.Null(entry.EndedAt);
        Assert.Equal(1200, entry.SecondsInPeriod);
    }

    [Fact]
    public async Task Running_timer_is_clipped_to_period_end()
    {
        var employee = await AddEmployee();
        Clock.Now = NextDay.AddHours(-1);
        await StartTimer(employee.Id);
        Clock.Advance(TimeSpan.FromHours(5));

        var report = await GetReport(Day, NextDay);

        Assert.Equal(3600, report.TotalSeconds);
    }

    [Fact]
    public async Task Running_timer_is_not_included_in_future_period()
    {
        var employee = await AddEmployee();
        Clock.Now = Day.AddHours(9);
        await StartTimer(employee.Id);

        var report = await GetReport(Day.AddDays(7), Day.AddDays(8));

        Assert.Empty(Assert.Single(report.Employees).Entries);
    }

    [Fact]
    public async Task Period_boundaries_with_non_utc_offset_are_respected()
    {
        var employee = await AddEmployee();
        await AddFinishedEntry(employee.Id, Day.AddHours(22), TimeSpan.FromHours(1));
        var moscow = TimeSpan.FromHours(3);

        // 22:00 UTC 10 марта — это уже 01:00 11 марта по Москве.
        var march10 = await GetReport(new DateTimeOffset(2026, 3, 10, 0, 0, 0, moscow), new DateTimeOffset(2026, 3, 11, 0, 0, 0, moscow));
        var march11 = await GetReport(new DateTimeOffset(2026, 3, 11, 0, 0, 0, moscow), new DateTimeOffset(2026, 3, 12, 0, 0, 0, moscow));

        Assert.Equal(0, march10.TotalSeconds);
        Assert.Equal(3600, march11.TotalSeconds);
    }

    [Fact]
    public async Task Filter_returns_only_selected_employee()
    {
        var anna = await AddEmployee("Анна");
        var boris = await AddEmployee("Борис");
        await AddFinishedEntry(anna.Id, Day.AddHours(9), TimeSpan.FromHours(1));
        await AddFinishedEntry(boris.Id, Day.AddHours(9), TimeSpan.FromHours(2));

        var report = await GetReport(Day, NextDay, boris.Id);

        Assert.Equal(boris.Id, Assert.Single(report.Employees).EmployeeId);
        Assert.Equal(7200, report.TotalSeconds);
    }

    [Fact]
    public async Task Filter_by_unknown_employee_returns_not_found()
    {
        await AssertProblem(await RequestReport(Day, NextDay, 999), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Period_with_end_not_after_start_is_rejected()
    {
        await AssertProblem(await RequestReport(NextDay, Day), HttpStatusCode.BadRequest);
        await AssertProblem(await RequestReport(Day, Day), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/reports/time")]
    [InlineData("/api/reports/time?from=2026-03-10T00:00:00Z")]
    [InlineData("/api/reports/time?to=2026-03-10T00:00:00Z")]
    public async Task Missing_period_is_rejected(string url)
    {
        await AssertProblem(await Client.GetAsync(url), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Malformed_date_returns_bad_request()
    {
        var response = await Client.GetAsync("/api/reports/time?from=вчера&to=2026-03-10T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
