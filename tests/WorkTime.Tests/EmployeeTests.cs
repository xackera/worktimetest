using System.Net;
using System.Net.Http.Json;
using WorkTime.Tests.Infrastructure;
using WorkTime.Web.Api;

namespace WorkTime.Tests;

public class EmployeeTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    [Fact]
    public async Task Added_employee_appears_in_list_with_trimmed_name()
    {
        await AddEmployee("  Борис Вымышленный  ");

        var employees = await Client.GetFromJsonAsync<List<EmployeeDto>>("/api/employees");

        var employee = Assert.Single(employees!);
        Assert.Equal("Борис Вымышленный", employee.Name);
        Assert.Null(employee.ActiveTimer);
    }

    [Fact]
    public async Task List_is_sorted_alphabetically_ignoring_case()
    {
        await AddEmployee("яна");
        await AddEmployee("Борис");
        await AddEmployee("анна");

        var employees = await Client.GetFromJsonAsync<List<EmployeeDto>>("/api/employees");

        Assert.Equal(["анна", "Борис", "яна"], employees!.Select(e => e.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Empty_name_is_rejected(string? name)
    {
        var response = await Client.PostAsJsonAsync("/api/employees", new { name });

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Too_long_name_is_rejected()
    {
        var response = await Client.PostAsJsonAsync("/api/employees", new { name = new string('я', 101) });

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Name_with_nul_character_is_rejected()
    {
        var response = await Client.PostAsJsonAsync("/api/employees", new { name = "а\0б" });

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Malformed_json_returns_bad_request()
    {
        var content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/employees", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
