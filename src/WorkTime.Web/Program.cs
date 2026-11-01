using Dapper;
using Npgsql;
using WorkTime.Web.Api;
using WorkTime.Web.Data;
using WorkTime.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Столбцы в базе называются в snake_case, свойства в C# — в PascalCase.
DefaultTypeMap.MatchNamesWithUnderscores = true;

// Строка подключения читается при создании источника, чтобы её можно было переопределить в тестах.
builder.Services.AddSingleton(services => NpgsqlDataSource.Create(GetConnectionString(services)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TimeTrackingService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddProblemDetails();

// В Development ошибки привязки параметров иначе становятся исключениями, а не ответом 400.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapApi();

var migration = DatabaseMigrator.Apply(GetConnectionString(app.Services));
if (!migration.Successful)
{
    app.Logger.LogCritical(
        migration.Error,
        "Не удалось подготовить базу данных. Проверьте, что PostgreSQL запущен: docker compose up -d db");
    return 1;
}

await app.RunAsync();
return 0;

static string GetConnectionString(IServiceProvider services) =>
    services.GetRequiredService<IConfiguration>().GetConnectionString("Default")
    ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Default.");

public partial class Program;
