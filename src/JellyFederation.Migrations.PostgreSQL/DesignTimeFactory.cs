using JellyFederation.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JellyFederation.Migrations.PostgreSQL;

public class DesignTimeFactory : IDesignTimeDbContextFactory<FederationDbContext>
{
    public FederationDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                      ?? "Host=localhost;Database=jellyfederation;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<FederationDbContext>()
            .UseNpgsql(connStr, x => x.MigrationsAssembly("JellyFederation.Migrations.PostgreSQL"))
            .Options;
        return new FederationDbContext(options);
    }
}
