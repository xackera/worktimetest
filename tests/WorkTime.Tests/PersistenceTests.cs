using System.Net;
using System.Net.Http.Json;
using WorkTime.Tests.Infrastructure;
using WorkTime.Web.Api;

namespace WorkTime.Tests;

public class PersistenceTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    [Fact]
    public async Task Data_and_running_timer_survive_application_restart()
    {
        var anna = await AddEmployee("Анна");
        var boris = await AddEmployee("Борис");
        await AddFinishedEntry(anna.Id, Start, TimeSpan.FromHours(1), "Завершённая задача");
        Clock.Now = Start.AddHours(2);
        await StartTimer(boris.Id, "Идущая задача");

        await App.DisposeAsync();

        Clock.Advance(TimeSpan.FromMinutes(10));
        await using var restarted = new TestApp(ConnectionString, Clock);
        var client = restarted.CreateClient();

        var employees = await client.GetFromJsonAsync<List<EmployeeDto>>("/api/employees");
        Assert.Equal(2, employees!.Count);
        var running = employees.Single(e => e.Id == boris.Id).ActiveTimer!;
        Assert.Equal("Идущая задача", running.Description);
        Assert.Equal(600, running.ElapsedSeconds);

        var stop = await client.PostAsync($"/api/employees/{boris.Id}/timer/stop", null);
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);

        var from = Uri.EscapeDataString(Start.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(Start.AddDays(1).ToString("O"));
        var report = await client.GetFromJsonAsync<ReportDto>($"/api/reports/time?from={from}&to={to}");
        Assert.Equal(3600, report!.Employees.Single(r => r.EmployeeId == anna.Id).TotalSeconds);
        Assert.Equal(600, report.Employees.Single(r => r.EmployeeId == boris.Id).TotalSeconds);
    }
}
