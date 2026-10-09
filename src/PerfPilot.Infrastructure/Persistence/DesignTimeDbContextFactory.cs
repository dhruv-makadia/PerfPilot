using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PerfPilot.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add … -p src/PerfPilot.Infrastructure -s src/PerfPilot.Infrastructure</c> work without
/// building or starting the API (e.g. while it's running). Creating a migration doesn't connect to the database;
/// set PERFPILOT_DESIGN_CONNECTION to use commands that do (database update).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PerfPilotDbContext>
{
    public PerfPilotDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PERFPILOT_DESIGN_CONNECTION")
            ?? "Server=localhost;Database=PerfPilot;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<PerfPilotDbContext>().UseSqlServer(connectionString).Options;
        return new PerfPilotDbContext(options);
    }
}
