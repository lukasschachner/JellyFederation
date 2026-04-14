using JellyFederation.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JellyFederation.Migrations.Sqlite;

public class DesignTimeFactory : IDesignTimeDbContextFactory<FederationDbContext>
{
    public FederationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FederationDbContext>()
            .UseSqlite("Data Source=federation-design.db",
                x => x.MigrationsAssembly("JellyFederation.Migrations.Sqlite"))
            .Options;
        return new FederationDbContext(options);
    }
}
