using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sqlite.Helpers;

namespace Sqlite.Tests;

[TableName("TestRecords")]
internal sealed class TestRecord : SqliteEntity
{
    public string Name { get; set; } = string.Empty;

    public int Score { get; set; }

    public string Category { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    [SensitiveData]
    public string Secret { get; set; } = string.Empty;
}

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private TestDatabase(
        SqliteConnection connection,
        SqliteDbContext<TestRecord> context)
    {
        _connection = connection;
        Context = context;
    }

    public SqliteDbContext<TestRecord> Context { get; }

    public SqliteConnection Connection => _connection;

    public static async Task<TestDatabase> CreateAsync(
        bool createEntityTable = true,
        params IInterceptor[] interceptors)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var optionsBuilder = new DbContextOptionsBuilder<SqliteDbContext<TestRecord>>()
            .UseSqlite(connection);
        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        var context = new SqliteDbContext<TestRecord>(optionsBuilder.Options);
        if (createEntityTable)
        {
            await context.Database.EnsureCreatedAsync();
        }

        return new TestDatabase(connection, context);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

internal sealed class ProviderProbeRepository(string connectionString)
    : SqliteRepository<TestRecord>(connectionString)
{
    public bool IsUsingSqlite => UsesSqliteProvider;
}

internal sealed class CommandCaptureInterceptor : DbCommandInterceptor
{
    public List<string> Commands { get; } = [];

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);

        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}

internal sealed class DelayCreateTableInterceptor : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.StartsWith("CREATE TABLE", StringComparison.Ordinal))
        {
            await Task.Delay(50, cancellationToken);
        }

        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}

internal sealed class ThrowingSaveChangesInterceptor : SaveChangesInterceptor
{
    public bool Enabled { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Enabled)
        {
            throw new InvalidOperationException("Injected save failure.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

internal static class TestRecordFactory
{
    public static TestRecord Create(
        string id,
        string name,
        int score,
        DateTime created,
        string category = "default",
        DateTime? deleted = null,
        params string[] tags) => new()
        {
            Id = id,
            Name = name,
            Score = score,
            Category = category,
            CreatedDateTime = created,
            DeletedDateTime = deleted,
            Tags = [.. tags],
            Secret = $"secret-{id}"
        };
}
