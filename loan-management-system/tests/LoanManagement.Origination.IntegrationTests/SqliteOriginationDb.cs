using LoanManagement.Origination.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LoanManagement.Origination.IntegrationTests;

/// <summary>In-memory SQLite-backed OriginationDbContext (real schema + outbox behaviour).</summary>
public sealed class SqliteOriginationDb : IDisposable
{
    private readonly SqliteConnection _connection;
    public OriginationDbContext Context { get; }

    public SqliteOriginationDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OriginationDbContext>().UseSqlite(_connection).Options;
        Context = new OriginationDbContext(options);
        Context.Database.EnsureCreated();
    }

    public OriginationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<OriginationDbContext>().UseSqlite(_connection).Options;
        return new OriginationDbContext(options);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
