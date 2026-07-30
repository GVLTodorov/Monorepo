using System.Data.Common;
using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Postgres;
using Postgres.Helpers;
using SortDirection = Postgres.Helpers.SortDirection;
namespace Postgres.Tests;

[TableName("TestRecords")]
internal sealed class TestRecord : PostgresEntity
{
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }
    public string Category { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];

    [SensitiveData]
    public string Secret { get; set; } = string.Empty;
}

internal sealed class TestRepository(DbConnection connection) : PostgresRepository<TestRecord>(connection)
{
    public List<string> Commands { get; } = [];

    protected override void OnCommandExecuting(string commandText) => Commands.Add(commandText);
}

internal sealed class TestDatabase : IAsyncDisposable
{
    private TestDatabase(SqliteConnection connection, TestRepository repository)
    {
        Connection = connection;
        Repository = repository;
    }

    public SqliteConnection Connection { get; }
    public TestRepository Repository { get; }

    public static async Task<TestDatabase> CreateAsync(bool ensureTable = true)
    {
        SqliteTestProvider.EnsureInitialized();
        var connection = new SqliteConnection("Data Source=:memory:");
        var repository = new TestRepository(connection);
        if (ensureTable)
        {
            await repository.EnsureTableAsync();
        }

        return new TestDatabase(connection, repository);
    }

    public async ValueTask DisposeAsync()
    {
        await Repository.DisposeAsync();
        await Connection.DisposeAsync();
    }
}

internal static class SqliteTestProvider
{
    static SqliteTestProvider()
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
        SQLitePCL.raw.FreezeProvider();
    }

    public static void EnsureInitialized()
    {
    }
}

[TestFixture]
public sealed class PostgresRepositoryTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void ConnectionStringConstructorRejectsInvalidValues(string? connectionString)
    {
        Assert.That(
            () => new PostgresRepository<TestRecord>(connectionString!),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task EnsureTableIsIdempotentAndSafeForConcurrentCallers()
    {
        await using var database = await TestDatabase.CreateAsync(ensureTable: false);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => database.Repository.EnsureTableAsync()));

        Assert.Multiple(async () =>
        {
            Assert.That(await database.Repository.TableExistsAsync(), Is.True);
            Assert.That(await database.Repository.TableExistsAsync("TEST_RECORDS"), Is.True);
            Assert.That(await database.Repository.TableExistsAsync("missing"), Is.False);
            Assert.That(
                database.Repository.Commands.Count(command =>
                    command.StartsWith("CREATE TABLE", StringComparison.Ordinal)),
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CrudQueriesProjectionPagingAndJsonCollectionsWork()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = database.Repository;

        var descending = await repository.GetAllAsync(
            record => record.Category == "x" && record.Score >= 10,
            record => record.Score,
            SortDirection.Descending);
        var projected = await repository.GetAllAsync(
            record => new { record.Id, record.Name },
            record => record.Category == "x");
        var first = await repository.GetFirstOrDefaultAsync(
            record => record.Name.StartsWith("A"),
            record => record.Score);
        var ids = new[] { "alpha", "delta" };
        var selected = await repository.GetAllAsync(
            (Expression<Func<TestRecord, bool>>)(record => ids.Contains(record.Id)),
            orderBy: null,
            sortDirection: SortDirection.Ascending);
        var page = await repository.GetPagedListAsync(
            1,
            2,
            null,
            [
                new SortExpression<TestRecord>
                {
                    Expression = record => record.Category,
                    SortDirection = SortDirection.Ascending
                },
                new SortExpression<TestRecord>
                {
                    Expression = record => record.Score,
                    SortDirection = SortDirection.Descending
                }
            ],
            includeDeletes: true);

        Assert.Multiple(() =>
        {
            Assert.That(descending.Select(record => record.Id), Is.EqualTo(new[] { "beta", "alpha" }));
            Assert.That(projected.Select(record => record.Id), Is.EquivalentTo(new[] { "alpha", "beta" }));
            Assert.That(first?.Id, Is.EqualTo("alpha"));
            Assert.That(selected.Select(record => record.Id), Is.EquivalentTo(ids));
            Assert.That(page.TotalCount, Is.EqualTo(4));
            Assert.That(page.TotalPages, Is.EqualTo(2));
            Assert.That(page.Items, Has.Count.EqualTo(2));
            Assert.That(repository.CountAsync(record => record.Category == "x").Result, Is.EqualTo(3));
            Assert.That(repository.ExistsAsync(record => record.Id == "gamma").Result, Is.False);
        });

        var alpha = await repository.GetByIdAsync("alpha");
        Assert.That(alpha?.Tags, Is.EqualTo(new[] { "one", "common" }));
        Assert.That(await repository.GetSingleOrDefaultAsync(record => record.Id == "missing"), Is.Null);
    }

    [Test]
    public async Task UpdatesPullSoftDeleteRestoreAndHardDeleteWork()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = database.Repository;
        var alpha = (await repository.GetByIdAsync("alpha"))!;
        alpha.Name = "Updated";

        await repository.UpdateAsync(alpha);
        var batch = await repository.GetAllAsync(
            (Expression<Func<TestRecord, bool>>)(record => record.Category == "x"),
            orderBy: null,
            sortDirection: SortDirection.Ascending);
        await repository.UpdateManyAsync(batch, new Dictionary<string, object>
        {
            [nameof(TestRecord.Category)] = "changed",
            ["Unknown"] = "ignored"
        });
        await repository.PullAsync(
            record => record.Tags,
            tag => tag == "common",
            record => record.Category == "changed");
        await repository.DeleteByIdAsync("alpha");

        Assert.Multiple(async () =>
        {
            Assert.That((await repository.GetAllAsync(
                (Expression<Func<TestRecord, bool>>)(record => record.Category == "changed"),
                orderBy: null,
                sortDirection: SortDirection.Ascending,
                includeDeletes: true)).Count, Is.EqualTo(2));
            Assert.That((await repository.GetAllAsync(
                (Expression<Func<TestRecord, bool>>)(record => record.Id == "alpha"),
                orderBy: null,
                sortDirection: SortDirection.Ascending,
                includeDeletes: true)).Single().IsDeleted, Is.True);
            Assert.That(await repository.GetByIdAsync("alpha"), Is.Null);
        });

        await repository.RestoreAsync("alpha");
        var restored = await repository.GetByIdAsync("alpha");
        Assert.Multiple(() =>
        {
            Assert.That(restored?.Name, Is.EqualTo("Updated"));
            Assert.That(restored?.Tags, Is.EqualTo(new[] { "one" }));
            Assert.That(restored?.UpdatedDateTime, Is.Not.Null);
        });

        var deleted = await repository.DeleteManyAsync(record => record.Category == "changed");
        Assert.That(deleted, Has.Count.EqualTo(2));
        await repository.DeleteManyAsync(deleted, hardDelete: true);
        Assert.That(await repository.CountAsync(record => record.Category == "changed"), Is.Zero);
    }

    [Test]
    public async Task IndexAndTruncateCommandsUseParameterizedSql()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = database.Repository;

        await repository.CreateIndexAsync(record => record.Name);
        await repository.CreateIndexAsync(
            new[]
            {
                ((Expression<Func<TestRecord, object>>)(record => record.Category), SortDirection.Ascending),
                ((Expression<Func<TestRecord, object>>)(record => record.Score), SortDirection.Descending)
            },
            unique: true);
        await repository.RemoveIndexAsync(record => record.Name);
        await repository.TruncateTableAsync(restartIdentity: true);

        Assert.Multiple(() =>
        {
            Assert.That(repository.Commands, Has.Some.Contains("CREATE INDEX IF NOT EXISTS"));
            Assert.That(repository.Commands, Has.Some.Contains("CREATE UNIQUE INDEX IF NOT EXISTS"));
            Assert.That(repository.Commands, Has.Some.Contains("\"Category\" ASC, \"Score\" DESC"));
            Assert.That(repository.Commands, Has.Some.Contains("DROP INDEX IF EXISTS"));
        });
        Assert.That(await repository.CountAsync(_ => true), Is.Zero);
    }

    [Test]
    public async Task RequiredArgumentsAreValidated()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = database.Repository;

        Assert.Multiple(() =>
        {
            Assert.That(async () => await repository.GetAllAsync<string>(null!), Throws.ArgumentNullException);
            Assert.That(async () => await repository.GetSingleOrDefaultAsync(null!), Throws.ArgumentNullException);
            Assert.That(async () => await repository.CountAsync(null!), Throws.ArgumentNullException);
            Assert.That(async () => await repository.ExistsAsync(null!), Throws.ArgumentNullException);
            Assert.That(async () => await repository.InsertAsync((TestRecord)null!), Throws.ArgumentNullException);
            Assert.That(async () => await repository.UpdateAsync(null!), Throws.ArgumentNullException);
            Assert.That(async () => await repository.TableExistsAsync(" "), Throws.ArgumentException);
            Assert.That(async () => await repository.GetPagedListAsync(0, 10), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    private static async Task<TestDatabase> CreateSeededDatabaseAsync()
    {
        var database = await TestDatabase.CreateAsync();
        await database.Repository.InsertAsync(new List<TestRecord>
        {
            Create("alpha", "Alpha", 10, "x", null, "one", "common"),
            Create("gamma", "Gamma", 20, "x", DateTime.UtcNow, "deleted"),
            Create("beta", "Beta", 30, "x", null, "two", "common"),
            Create("delta", "Delta", 40, "y", null, "three")
        });
        return database;
    }

    private static TestRecord Create(
        string id,
        string name,
        int score,
        string category,
        DateTime? deleted,
        params string[] tags) => new()
        {
            Id = id,
            Name = name,
            Score = score,
            Category = category,
            DeletedDateTime = deleted,
            Tags = [.. tags],
            Secret = $"secret-{id}"
        };
}
