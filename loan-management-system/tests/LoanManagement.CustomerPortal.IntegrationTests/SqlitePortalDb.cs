using LoanManagement.CustomerPortal.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LoanManagement.CustomerPortal.IntegrationTests;

/// <summary>In-memory SQLite-backed CustomerPortalDbContext (real schema + constraints).</summary>
public sealed class SqlitePortalDb : IDisposable
{
    private readonly SqliteConnection _connection;
    public CustomerPortalDbContext Context { get; }

    public SqlitePortalDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<CustomerPortalDbContext>().UseSqlite(_connection).Options;
        Context = new CustomerPortalDbContext(options);
        Context.Database.EnsureCreated();
    }

    public CustomerPortalDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<CustomerPortalDbContext>().UseSqlite(_connection).Options;
        return new CustomerPortalDbContext(options);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
