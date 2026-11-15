using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Weather.Infrastructure.WeatherHistory;

// EF tooling uses environment configuration; it never loads a host's local secrets.
public sealed class WeatherHistoryContextFactory : IDesignTimeDbContextFactory<WeatherHistoryContext>
{
    public WeatherHistoryContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__sql")
            ?? "Server=localhost;Database=weather;Integrated Security=true;TrustServerCertificate=true";
        return new WeatherHistoryContext(new DbContextOptionsBuilder<WeatherHistoryContext>()
            .UseSqlServer(connection, sql => sql.CommandTimeout(30)).Options);
    }
}
