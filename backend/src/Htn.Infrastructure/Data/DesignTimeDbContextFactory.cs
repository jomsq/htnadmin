using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Htn.Infrastructure.Data;

/// <summary>Used by <c>dotnet ef</c> only. Migrations are generated without connecting to a database.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HtnDbContext>
{
    public HtnDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Db") ?? "Host=localhost;Database=htn_design";
        var builder = new DbContextOptionsBuilder<HtnDbContext>();
        HtnDbContextOptions.Configure(builder, cs);
        return new HtnDbContext(builder.Options);
    }
}
