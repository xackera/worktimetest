using WorkTime.Web.Services;

namespace WorkTime.Web.Api;

public static class Endpoints
{
    public static void MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter(MapDomainErrors);

        api.MapGet("/employees", (TimeTrackingService service, CancellationToken ct) =>
            service.GetEmployeesAsync(ct));

        api.MapPost("/employees", async (CreateEmployeeRequest request, TimeTrackingService service, CancellationToken ct) =>
        {
            var employee = await service.CreateEmployeeAsync(request.Name, ct);
            return Results.Created((string?)null, employee);
        });

        api.MapPost("/employees/{id:int}/timer/start", (int id, StartTimerRequest request, TimeTrackingService service, CancellationToken ct) =>
            service.StartTimerAsync(id, request.Description, ct));

        api.MapPost("/employees/{id:int}/timer/stop", (int id, TimeTrackingService service, CancellationToken ct) =>
            service.StopTimerAsync(id, ct));

        api.MapGet("/reports/time", (DateTimeOffset? from, DateTimeOffset? to, int? employeeId, ReportService service, CancellationToken ct) =>
            service.BuildAsync(from, to, employeeId, ct));
    }

    /// <summary>
    /// Превращает ожидаемые ошибки в ProblemDetails здесь, а не в UseExceptionHandler:
    /// тот записал бы каждый обычный 409 в лог как необработанное исключение.
    /// </summary>
    private static async ValueTask<object?> MapDomainErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (DomainException ex)
        {
            var status = ex switch
            {
                NotFoundException => StatusCodes.Status404NotFound,
                ConflictException => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            };
            return Results.Problem(detail: ex.Message, statusCode: status);
        }
    }
}
