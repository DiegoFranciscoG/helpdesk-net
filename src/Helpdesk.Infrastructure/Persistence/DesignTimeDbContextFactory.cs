using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Helpdesk.Infrastructure.Persistence;

/// <summary>
/// Solo para la CLI de EF (<c>dotnet ef migrations add</c>): generar migraciones no abre conexión, así que
/// basta una cadena sin credenciales. Las herramientas que sí conectan leen <c>HELPDESK_DESIGN_CONNECTION</c>.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HelpdeskDbContext>
{
    public HelpdeskDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("HELPDESK_DESIGN_CONNECTION")
            ?? "Host=localhost;Database=helpdesk_design";
        var options = new DbContextOptionsBuilder<HelpdeskDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new HelpdeskDbContext(options);
    }
}
