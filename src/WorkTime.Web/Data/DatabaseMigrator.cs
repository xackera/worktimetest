using System.Reflection;
using DbUp;
using DbUp.Engine;

namespace WorkTime.Web.Data;

public static class DatabaseMigrator
{
    /// <summary>
    /// Применяет SQL-скрипты из Data/Migrations, которых ещё нет в журнале schemaversions.
    /// Скрипты выполняются по порядку имён, каждый в своей транзакции.
    /// </summary>
    public static DatabaseUpgradeResult Apply(string connectionString) =>
        DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build()
            .PerformUpgrade();
}
