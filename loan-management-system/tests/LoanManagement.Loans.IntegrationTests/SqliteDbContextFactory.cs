using LoanManagement.Loans.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LoanManagement.Loans.IntegrationTests;

/// <summary>
/// Creates a LoansDbContext backed by an in-memory SQLite database. Unlike the EF
/// in-memory provider, SQLite enforces real transactions and the unique ContractId
/// index — which matters for proving funding idempotency at the storage layer.
/// The connection is kept open for the lifetime of the context (in-memory SQLite is
/// discarded when the last connection closes).
/// </summary>
public sealed class SqliteLoansDb : IDisposable
{
    private readonly SqliteConnection _connection;
    public LoansDbContext Context { get; }

    public SqliteLoansDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LoansDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new LoansDbContext(options);
        Context.Database.EnsureCreated();
    }

    /// <summary>Opens a fresh context over the same connection (fresh change tracker).</summary>
    public LoansDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LoansDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new LoansDbContext(options);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
