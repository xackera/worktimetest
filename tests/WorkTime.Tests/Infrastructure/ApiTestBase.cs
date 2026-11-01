using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using WorkTime.Web.Api;

namespace WorkTime.Tests.Infrastructure;

[Collection(PostgresCollection.Name)]
public abstract class ApiTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected static readonly DateTimeOffset Start = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);

    protected FakeClock Clock { get; } = new(Start);
    protected string ConnectionString { get; private set; } = "";
    protected TestApp App { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        ConnectionString = await postgres.CreateDatabaseAsync();
        App = new TestApp(ConnectionString, Clock);
        Client = App.CreateClient();
    }

    public async Task DisposeAsync() => await App.DisposeAsync();

    protected async Task<EmployeeDto> AddEmployee(string name = "Анна Тестова")
    {
        var response = await Client.PostAsJsonAsync("/api/employees", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EmployeeDto>())!;
    }

    protected Task<HttpResponseMessage> StartTimer(int employeeId, string? description = "Задача") =>
        Client.PostAsJsonAsync($"/api/employees/{employeeId}/timer/start", new { description });

    protected Task<HttpResponseMessage> StopTimer(int employeeId) =>
        Client.PostAsync($"/api/employees/{employeeId}/timer/stop", null);

    /// <summary>Запись длиной <paramref name="duration"/>, начатая в момент <paramref name="startedAt"/>.</summary>
    protected async Task AddFinishedEntry(int employeeId, DateTimeOffset startedAt, TimeSpan duration, string description = "Задача")
    {
        Clock.Now = startedAt;
        (await StartTimer(employeeId, description)).EnsureSuccessStatusCode();
        Clock.Now = startedAt + duration;
        (await StopTimer(employeeId)).EnsureSuccessStatusCode();
    }

    protected Task<HttpResponseMessage> RequestReport(DateTimeOffset from, DateTimeOffset to, int? employeeId = null)
    {
        var url = $"/api/reports/time?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
        return Client.GetAsync(employeeId is null ? url : $"{url}&employeeId={employeeId}");
    }

    protected async Task<ReportDto> GetReport(DateTimeOffset from, DateTimeOffset to, int? employeeId = null)
    {
        var response = await RequestReport(from, to, employeeId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReportDto>())!;
    }

    protected static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal((int)status, problem!.Status);
    }
}
