using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Sqlite.Helpers;
using System.Data;

namespace Sqlite.Tests;

[TestFixture]
public sealed class SqliteRepositoryQueryTests
{
    private static readonly DateTime Epoch = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void ConnectionStringConstructor_InvalidValue_ThrowsArgumentException(string? value)
    {
        Assert.That(
            () => new SqliteRepository<TestRecord>(value!),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task Constructors_ValidDependencies_CreateRepositoryContract()
    {
        await using var database = await TestDatabase.CreateAsync();

        var configured = new SqliteRepository<TestRecord>(database.Context);
        var connectionString = new SqliteRepository<TestRecord>("Data Source=:memory:");
        var providerProbe = new ProviderProbeRepository("Data Source=:memory:");

        Assert.Multiple(() =>
        {
            Assert.That(configured, Is.AssignableTo<ISqliteRepository>());
            Assert.That(configured, Is.AssignableTo<ISqliteRepository<TestRecord>>());
            Assert.That(connectionString, Is.Not.Null);
            Assert.That(providerProbe.IsUsingSqlite, Is.True);
            Assert.That(
                () => new SqliteRepository<TestRecord>((SqliteDbContext<TestRecord>)null!),
                Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task GetAllAsync_DefaultFilterOrderingAndIncludeDeleted_AreApplied()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = new SqliteRepository<TestRecord>(database.Context);

        var active = await repository.GetAllAsync();
        var filteredDescending = await repository.GetAllAsync(
            record => record.Category == "x",
            record => record.Score,
            SortDirection.Descending);
        var filteredAscending = await repository.GetAllAsync(
            record => record.Category == "x",
            record => record.Score,
            SortDirection.Ascending);
        var includingDeleted = await repository.GetAllAsync(includeDeletes: true);

        Assert.Multiple(() =>
        {
            Assert.That(active.Select(record => record.Id), Is.EqualTo(new[] { "alpha", "beta", "delta" }));
            Assert.That(filteredDescending.Select(record => record.Id), Is.EqualTo(new[] { "beta", "alpha" }));
            Assert.That(filteredAscending.Select(record => record.Id), Is.EqualTo(new[] { "alpha", "beta" }));
            Assert.That(includingDeleted.Select(record => record.Id),
                Is.EqualTo(new[] { "alpha", "gamma", "beta", "delta" }));
        });
    }

    [Test]
    public async Task GetAllProjectionAsync_PredicateAndSoftDelete_ReturnExpectedValues()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = new SqliteRepository<TestRecord>(database.Context);

        var active = await repository.GetAllAsync(
            record => new { record.Id, record.Score },
            record => record.Category == "x");
        var all = await repository.GetAllAsync(
            record => record.Name,
            includeDeletes: true);

        Assert.Multiple(() =>
        {
            Assert.That(active.Select(value => value.Id), Is.EquivalentTo(new[] { "alpha", "beta" }));
            Assert.That(active.Select(value => value.Score), Is.EquivalentTo(new[] { 10, 30 }));
            Assert.That(all, Is.EquivalentTo(new[] { "Alpha", "Beta", "Gamma", "Delta" }));
        });
    }

    [Test]
    public async Task FirstSingleAndIdQueries_ReturnExpectedRecordsAndNulls()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = new SqliteRepository<TestRecord>(database.Context);

        var first = await repository.GetFirstOrDefaultAsync();
        var highest = await repository.GetFirstOrDefaultAsync(
            record => record.Category == "x",
            record => record.Score,
            SortDirection.Descending);
        var lowest = await repository.GetFirstOrDefaultAsync(
            record => record.Category == "x",
            record => record.Score,
            SortDirection.Ascending);
        var deleted = await repository.GetFirstOrDefaultAsync(
            record => record.Id == "gamma",
            includeDeleted: true);
        var single = await repository.GetSingleOrDefaultAsync(record => record.Id == "delta");
        var missingSingle = await repository.GetSingleOrDefaultAsync(record => record.Id == "missing");
        var byId = await repository.GetByIdAsync("beta");
        var deletedById = await repository.GetByIdAsync("gamma");

        Assert.Multiple(() =>
        {
            Assert.That(first!.Id, Is.EqualTo("alpha"));
            Assert.That(highest!.Id, Is.EqualTo("beta"));
            Assert.That(lowest!.Id, Is.EqualTo("alpha"));
            Assert.That(deleted!.Id, Is.EqualTo("gamma"));
            Assert.That(single!.Id, Is.EqualTo("delta"));
            Assert.That(missingSingle, Is.Null);
            Assert.That(byId!.Id, Is.EqualTo("beta"));
            Assert.That(deletedById, Is.Null);
        });
    }

    [Test]
    public async Task CountAndExists_PredicatesAndSoftDelete_ReturnExpectedValues()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = new SqliteRepository<TestRecord>(database.Context);

        var count = await repository.CountAsync(record => record.Category == "x");
        var activeExists = await repository.ExistsAsync(record => record.Id == "beta");
        var deletedExists = await repository.ExistsAsync(record => record.Id == "gamma");

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(3));
            Assert.That(activeExists, Is.True);
            Assert.That(deletedExists, Is.False);
        });
    }

    [Test]
    public async Task NullRequiredQueryArguments_ThrowArgumentNullException()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = new SqliteRepository<TestRecord>(database.Context);

        Assert.Multiple(() =>
        {
            Assert.That(async () => await repository.GetAllAsync<string>(null!), Throws.ArgumentNullException);
            Assert.That(async () => await repository.GetSingleOrDefaultAsync(null!), Throws.ArgumentNullException);
            Assert.That(async () => await repository.CountAsync(null!), Throws.ArgumentNullException);
            Assert.That(async () => await repository.ExistsAsync(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task GetPagedListAsync_DefaultAndMultiColumnOrdering_ReturnMetadataAndItems()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = new SqliteRepository<TestRecord>(database.Context);

        var defaultPage = await repository.GetPagedListAsync(pageIndex: 2, pageSize: 2);
        var singleSortedPage = await repository.GetPagedListAsync(
            pageIndex: 1,
            pageSize: 2,
            orderBy: record => record.Score,
            sortDirection: SortDirection.Descending);
        var sortedPage = await repository.GetPagedListAsync(
            1,
            3,
            null,
            [
                new SortExpression<TestRecord> { Expression = record => record.Category, SortDirection = SortDirection.Ascending },
                new SortExpression<TestRecord> { Expression = record => record.Score, SortDirection = SortDirection.Descending }
            ],
            includeDeletes: true);
        var reverseSortedPage = await repository.GetPagedListAsync(
            1,
            4,
            null,
            [
                new SortExpression<TestRecord> { Expression = record => record.Category, SortDirection = SortDirection.Descending },
                new SortExpression<TestRecord> { Expression = record => record.Score, SortDirection = SortDirection.Ascending }
            ],
            includeDeletes: true);
        var nullSortPage = await repository.GetPagedListAsync(
            1,
            1,
            null,
            null!,
            includeDeletes: false);

        Assert.Multiple(() =>
        {
            Assert.That(defaultPage.TotalCount, Is.EqualTo(3));
            Assert.That(defaultPage.TotalPages, Is.EqualTo(2));
            Assert.That(defaultPage.Items.Select(record => record.Id), Is.EqualTo(new[] { "delta" }));
            Assert.That(defaultPage.HasPreviousPage, Is.True);
            Assert.That(defaultPage.HasNextPage, Is.False);
            Assert.That(singleSortedPage.Items.Select(record => record.Id), Is.EqualTo(new[] { "delta", "beta" }));
            Assert.That(sortedPage.TotalCount, Is.EqualTo(4));
            Assert.That(sortedPage.Items.Select(record => record.Id),
                Is.EqualTo(new[] { "beta", "gamma", "alpha" }));
            Assert.That(reverseSortedPage.Items.Select(record => record.Id),
                Is.EqualTo(new[] { "delta", "alpha", "gamma", "beta" }));
            Assert.That(nullSortPage.Items.Select(record => record.Id), Is.EqualTo(new[] { "alpha" }));
        });
    }

    [Test]
    public async Task GetPagedListAsync_EmptySortPredicateAndEmptyResult_UseFallbackOrdering()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = new SqliteRepository<TestRecord>(database.Context);

        var page = await repository.GetPagedListAsync(
            1,
            5,
            record => record.Score > 100,
            [],
            includeDeletes: false);

        Assert.Multiple(() =>
        {
            Assert.That(page.TotalCount, Is.Zero);
            Assert.That(page.TotalPages, Is.Zero);
            Assert.That(page.Items, Is.Empty);
        });
    }

    [TestCase(0, 10, "pageIndex")]
    [TestCase(1, 0, "pageSize")]
    public async Task GetPagedListAsync_InvalidBounds_ThrowsArgumentOutOfRangeException(
        int pageIndex,
        int pageSize,
        string parameterName)
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = new SqliteRepository<TestRecord>(database.Context);

        var exception = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await repository.GetPagedListAsync(pageIndex, pageSize));

        Assert.That(exception!.ParamName, Is.EqualTo(parameterName));
    }

    [Test]
    public async Task TableExistsAndEnsureTable_ExistingMissingAndCreationPathsWork()
    {
        await using var existingDatabase = await TestDatabase.CreateAsync();
        var existingRepository = new SqliteRepository<TestRecord>(existingDatabase.Context);

        Assert.Multiple(async () =>
        {
            Assert.That(await existingRepository.TableExistsAsync(), Is.True);
            Assert.That(await existingRepository.TableExistsAsync("TEST_RECORDS", "ignored"), Is.True);
            Assert.That(await existingRepository.TableExistsAsync("missing"), Is.False);
        });

        await using var missingDatabase = await TestDatabase.CreateAsync(createEntityTable: false);
        var missingRepository = new SqliteRepository<TestRecord>(missingDatabase.Context);

        Assert.That(await missingRepository.TableExistsAsync(), Is.False);
        await missingRepository.EnsureTableAsync();
        await missingRepository.EnsureTableAsync();
        Assert.That(await missingRepository.TableExistsAsync(), Is.True);
    }

    [Test]
    public async Task TableExistsAsync_ClosedConnection_OpensConnectionAndFindsTable()
    {
        const string connectionString = "Data Source=table-exists-tests;Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteDbContext<TestRecord>>()
            .UseSqlite(connectionString)
            .Options;
        await using var context = new SqliteDbContext<TestRecord>(options);
        await context.Database.EnsureCreatedAsync();
        var repository = new SqliteRepository<TestRecord>(context);

        Assert.That(context.Database.GetDbConnection().State, Is.EqualTo(ConnectionState.Closed));
        Assert.That(await repository.TableExistsAsync(), Is.True);
        Assert.That(context.Database.GetDbConnection().State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public async Task TableExistsAndEnsureTable_NonRelationalProvider_UseConnectivityAndEnsureCreated()
    {
        var options = new DbContextOptionsBuilder<SqliteDbContext<TestRecord>>()
            .UseInMemoryDatabase($"sqlite-tests-{Guid.NewGuid()}")
            .Options;
        await using var context = new SqliteDbContext<TestRecord>(options);
        var repository = new SqliteRepository<TestRecord>(context);

        Assert.That(await repository.TableExistsAsync("logical_table"), Is.True);
        await repository.EnsureTableAsync();
        Assert.That(context.Model.FindEntityType(typeof(TestRecord)), Is.Not.Null);
    }

    [Test]
    public async Task EnsureTableAsync_ConcurrentCallers_CreateOnceAndObserveEnsuredState()
    {
        await using var database = await TestDatabase.CreateAsync(
            createEntityTable: false,
            interceptors: new DelayCreateTableInterceptor());
        var repository = new SqliteRepository<TestRecord>(database.Context);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => repository.EnsureTableAsync()));

        Assert.That(await repository.TableExistsAsync(), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task TableExistsAsync_InvalidTableName_ThrowsArgumentException(string? tableName)
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = new SqliteRepository<TestRecord>(database.Context);

        Assert.That(
            async () => await repository.TableExistsAsync(tableName!),
            Throws.ArgumentException);
    }

    private static async Task<TestDatabase> CreateSeededDatabaseAsync()
    {
        var database = await TestDatabase.CreateAsync();
        database.Context.Entities.AddRange(
            TestRecordFactory.Create("alpha", "Alpha", 10, Epoch, "x", tags: ["one", "common"]),
            TestRecordFactory.Create("gamma", "Gamma", 20, Epoch.AddDays(1), "x", Epoch.AddHours(1), "deleted"),
            TestRecordFactory.Create("beta", "Beta", 30, Epoch.AddDays(2), "x", tags: ["two", "common"]),
            TestRecordFactory.Create("delta", "Delta", 40, Epoch.AddDays(3), "y", tags: ["three"]));
        await database.Context.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();
        return database;
    }
}
