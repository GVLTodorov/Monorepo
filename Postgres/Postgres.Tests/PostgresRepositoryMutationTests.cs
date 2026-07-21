using Microsoft.EntityFrameworkCore;
using Postgres.Helpers;

namespace Postgres.Tests;

[TestFixture]
public sealed class PostgresRepositoryMutationTests
{
    private static readonly DateTime Epoch = new(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task InsertAsync_SingleAndBulk_SetCreatedTimestampAndPersistRows()
    {
        await using var database = await TestDatabase.CreateAsync(createEntityTable: false);
        var repository = new PostgresRepository<TestRecord>(database.Context);
        var single = TestRecordFactory.Create("one", "One", 1, default);
        var bulk = new List<TestRecord>
        {
            TestRecordFactory.Create("two", "Two", 2, default),
            TestRecordFactory.Create("three", "Three", 3, default)
        };
        var before = DateTime.UtcNow;

        await repository.InsertAsync(single);
        await repository.InsertAsync(bulk);
        var after = DateTime.UtcNow;

        Assert.Multiple(() =>
        {
            Assert.That(single.CreatedDateTime, Is.InRange(before, after));
            Assert.That(bulk.All(entity => entity.CreatedDateTime >= before && entity.CreatedDateTime <= after), Is.True);
            Assert.That(database.Context.Entities.Count(), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task InsertAsync_NullEntityThrowsAndNullOrEmptyCollectionIsNoOp()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = new PostgresRepository<TestRecord>(database.Context);

        Assert.That(async () => await repository.InsertAsync((TestRecord)null!), Throws.ArgumentNullException);
        await repository.InsertAsync((ICollection<TestRecord>?)null);
        await repository.InsertAsync(new List<TestRecord>());

        Assert.That(await database.Context.Entities.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task UpdateAsync_SetsTimestampAndPersistsChanges()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = new PostgresRepository<TestRecord>(database.Context);
        var entity = await database.Context.Entities.SingleAsync(record => record.Id == "alpha");
        entity.Name = "Updated";
        var before = DateTime.UtcNow;

        await repository.UpdateAsync(entity);

        database.Context.ChangeTracker.Clear();
        var persisted = await database.Context.Entities.SingleAsync(record => record.Id == "alpha");
        Assert.Multiple(() =>
        {
            Assert.That(persisted.Name, Is.EqualTo("Updated"));
            Assert.That(persisted.UpdatedDateTime, Is.GreaterThanOrEqualTo(before));
        });
        Assert.That(async () => await repository.UpdateAsync(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task UpdateManyAsync_UpdatesWritablePropertiesIgnoresUnknownAndSharesTimestamp()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = new PostgresRepository<TestRecord>(database.Context);
        var entities = await database.Context.Entities
            .Where(record => record.Category == "x" && record.DeletedDateTime == null)
            .ToListAsync();

        await repository.UpdateManyAsync(entities, new Dictionary<string, object>
        {
            [nameof(TestRecord.Name)] = "Bulk",
            [nameof(TestRecord.Score)] = 99,
            ["UnknownProperty"] = "ignored"
        });

        Assert.Multiple(() =>
        {
            Assert.That(entities.Select(entity => entity.Name), Is.All.EqualTo("Bulk"));
            Assert.That(entities.Select(entity => entity.Score), Is.All.EqualTo(99));
            Assert.That(entities.Select(entity => entity.UpdatedDateTime).Distinct().ToList(), Has.Count.EqualTo(1));
            Assert.That(entities[0].UpdatedDateTime, Is.Not.Null);
        });
    }

    [Test]
    public async Task UpdateManyAsync_NullArgumentsNoOpAndFailurePathsBehaveAsDocumented()
    {
        var failure = new ThrowingSaveChangesInterceptor();
        await using var database = await TestDatabase.CreateAsync(interceptors: failure);
        var repository = new PostgresRepository<TestRecord>(database.Context);

        Assert.That(
            async () => await repository.UpdateManyAsync(null, null!),
            Throws.ArgumentNullException);

        await repository.UpdateManyAsync(null, new Dictionary<string, object>());
        await repository.UpdateManyAsync([], new Dictionary<string, object>());

        var entity = TestRecordFactory.Create("failure", "Before", 1, Epoch);
        database.Context.Entities.Add(entity);
        await database.Context.SaveChangesAsync();
        failure.Enabled = true;

        Assert.That(
            async () => await repository.UpdateManyAsync(
                [entity],
                new Dictionary<string, object> { [nameof(TestRecord.Name)] = "After" }),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Injected save failure."));
    }

    [Test]
    public async Task PullAsync_FiltersDocumentsItemsDeletedRowsAndDefaultFilter()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = new PostgresRepository<TestRecord>(database.Context);

        await repository.PullAsync(
            record => record.Tags,
            tag => tag == "common",
            record => record.Category == "x");
        await repository.PullAsync(
            record => record.Tags,
            documentPredicate: record => record.Id == "delta");
        await repository.PullAsync(
            record => record.Tags,
            tag => tag == "deleted",
            record => record.Id == "gamma",
            includeDeleted: true);
        await repository.PullAsync(
            record => (IEnumerable<string>)record.Tags,
            tag => tag == "not-present",
            record => record.Id == "beta");

        database.Context.ChangeTracker.Clear();
        var records = await database.Context.Entities.ToDictionaryAsync(record => record.Id);
        Assert.Multiple(() =>
        {
            Assert.That(records["alpha"].Tags, Is.EqualTo(new[] { "one" }));
            Assert.That(records["beta"].Tags, Is.EqualTo(new[] { "two" }));
            Assert.That(records["delta"].Tags, Is.Empty);
            Assert.That(records["gamma"].Tags, Is.Empty);
            Assert.That(records.Values.Where(record => record.Id != "gamma").Select(record => record.UpdatedDateTime),
                Is.All.Not.Null);
        });
    }

    [Test]
    public async Task PullAsync_NonPropertyCollectionExpression_DoesNotReplaceCollection()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = new PostgresRepository<TestRecord>(database.Context);

        await repository.PullAsync(
            record => record.Tags.Where(tag => tag.Length > 0),
            tag => tag == "common",
            record => record.Id == "alpha");

        database.Context.ChangeTracker.Clear();
        var alpha = await database.Context.Entities.SingleAsync(record => record.Id == "alpha");
        Assert.That(alpha.Tags, Is.EqualTo(new[] { "one", "common" }));
        Assert.That(alpha.UpdatedDateTime, Is.Not.Null);
    }

    [Test]
    public async Task DeleteAndRestoreAsync_SoftHardMissingAndOverloadsWork()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = new PostgresRepository<TestRecord>(database.Context);

        await repository.DeleteByIdAsync("alpha");
        var alpha = await database.Context.Entities.SingleAsync(record => record.Id == "alpha");
        Assert.That(alpha.DeletedDateTime, Is.Not.Null);

        await repository.DeleteByIdAsync("alpha");
        await repository.RestoreAsync(alpha);
        Assert.That(alpha.DeletedDateTime, Is.Null);

        var delta = await database.Context.Entities.SingleAsync(record => record.Id == "delta");
        await repository.DeleteAsync(delta, hardDelete: true);
        Assert.That(await database.Context.Entities.AnyAsync(record => record.Id == "delta"), Is.False);

        await repository.DeleteOneAsync(record => record.Id == "missing");
        await repository.RestoreAsync("missing");

        Assert.Multiple(() =>
        {
            Assert.That(async () => await repository.DeleteAsync(null!), Throws.ArgumentNullException);
            Assert.That(async () => await repository.DeleteOneAsync(null!), Throws.ArgumentNullException);
            Assert.That(() => repository.RestoreAsync((TestRecord)null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task DeleteManyAsync_SoftHardCollectionEmptyAndValidationPathsWork()
    {
        await using var database = await CreateSeededDatabaseAsync();
        var repository = new PostgresRepository<TestRecord>(database.Context);

        var softDeleted = await repository.DeleteManyAsync(record => record.Category == "x");
        Assert.That(softDeleted.Select(record => record.Id), Is.EquivalentTo(new[] { "alpha", "beta" }));
        Assert.That(softDeleted.Select(record => record.DeletedDateTime).Distinct().ToList(), Has.Count.EqualTo(1));

        var hardDeleted = await repository.DeleteManyAsync(record => record.Category == "x", hardDelete: true);
        Assert.That(hardDeleted.Select(record => record.Id), Is.EquivalentTo(new[] { "alpha", "beta", "gamma" }));

        var delta = await database.Context.Entities.SingleAsync(record => record.Id == "delta");
        await repository.DeleteManyAsync([delta], hardDelete: true);
        Assert.That(await database.Context.Entities.AnyAsync(), Is.False);

        var empty = await repository.DeleteManyAsync(record => true);
        Assert.That(empty, Is.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(async () => await repository.DeleteManyAsync((System.Linq.Expressions.Expression<Func<TestRecord, bool>>)null!),
                Throws.ArgumentNullException);
            Assert.That(async () => await repository.DeleteManyAsync((ICollection<TestRecord>)null!),
                Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task DeleteManyAsync_SaveFailure_RollsBackAndReturnsEmptyList()
    {
        var failure = new ThrowingSaveChangesInterceptor();
        await using var database = await TestDatabase.CreateAsync(interceptors: failure);
        var entity = TestRecordFactory.Create("failure", "Failure", 1, Epoch);
        database.Context.Entities.Add(entity);
        await database.Context.SaveChangesAsync();
        var repository = new PostgresRepository<TestRecord>(database.Context);
        failure.Enabled = true;

        var result = await repository.DeleteManyAsync(record => record.Id == "failure");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task IndexMethods_CreateExpectedSqlAndValidateArguments()
    {
        var commands = new CommandCaptureInterceptor();
        await using var database = await TestDatabase.CreateAsync(interceptors: commands);
        var repository = new PostgresRepository<TestRecord>(database.Context);

        await repository.CreateIndexAsync(record => record.Name);
        await repository.CreateIndexAsync(record => record.Score, TimeSpan.FromMinutes(1));
        await repository.CreateIndexAsync(
            new System.Linq.Expressions.Expression<Func<TestRecord, object>>[] { record => record.Name, record => record.Score },
            unique: true);
        await repository.CreateIndexAsync(
            new[]
            {
                ((System.Linq.Expressions.Expression<Func<TestRecord, object>>)(record => record.Category), SortDirection.Ascending),
                ((System.Linq.Expressions.Expression<Func<TestRecord, object>>)(record => record.Score), SortDirection.Descending)
            });
        await repository.CreateIndexAsync(
            new[]
            {
                ((System.Linq.Expressions.Expression<Func<TestRecord, object>>)(record => record.Category), SortDirection.Descending),
                ((System.Linq.Expressions.Expression<Func<TestRecord, object>>)(record => record.Score), SortDirection.Ascending)
            },
            unique: true);
        await repository.RemoveIndexAsync(record => record.Name);
        await repository.RemoveIndexAsync(
            new System.Linq.Expressions.Expression<Func<TestRecord, object>>[] { record => record.Name, record => record.Score });

        Assert.Multiple(() =>
        {
            Assert.That(commands.Commands, Has.Some.Contains("CREATE INDEX IF NOT EXISTS \"idx_test_records_Name\""));
            Assert.That(commands.Commands, Has.Some.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"idx_test_records_Name_Score\""));
            Assert.That(commands.Commands, Has.Some.Contains("\"Category\" ASC, \"Score\" DESC"));
            Assert.That(commands.Commands, Has.Some.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"idx_test_records_Category_Score\""));
            Assert.That(commands.Commands, Has.Some.Contains("\"Category\" DESC, \"Score\" ASC"));
            Assert.That(commands.Commands, Has.Some.Contains("DROP INDEX IF EXISTS \"idx_test_records_Name\""));
            Assert.That(commands.Commands, Has.Some.Contains("DROP INDEX IF EXISTS \"idx_test_records_Name_Score\""));
        });

        Assert.Multiple(() =>
        {
            Assert.That(async () => await repository.CreateIndexAsync(
                    (IEnumerable<System.Linq.Expressions.Expression<Func<TestRecord, object>>>)null!),
                Throws.ArgumentNullException);
            Assert.That(async () => await repository.CreateIndexAsync(
                    Array.Empty<System.Linq.Expressions.Expression<Func<TestRecord, object>>>()),
                Throws.ArgumentException);
            Assert.That(async () => await repository.CreateIndexAsync(
                    (IEnumerable<(System.Linq.Expressions.Expression<Func<TestRecord, object>>, SortDirection)>)null!),
                Throws.ArgumentNullException);
            Assert.That(async () => await repository.CreateIndexAsync(
                    Array.Empty<(System.Linq.Expressions.Expression<Func<TestRecord, object>>, SortDirection)>()),
                Throws.ArgumentException);
            Assert.That(async () => await repository.RemoveIndexAsync(
                    (System.Linq.Expressions.Expression<Func<TestRecord, object>>)null!),
                Throws.ArgumentNullException);
            Assert.That(async () => await repository.RemoveIndexAsync(
                    (IEnumerable<System.Linq.Expressions.Expression<Func<TestRecord, object>>>)null!),
                Throws.ArgumentNullException);
            Assert.That(async () => await repository.RemoveIndexAsync(
                    Array.Empty<System.Linq.Expressions.Expression<Func<TestRecord, object>>>()),
                Throws.ArgumentException);
        });
    }

    [Test]
    public async Task TruncateTableAsync_AllOptionCombinations_GenerateExpectedSql()
    {
        var commands = new CommandCaptureInterceptor();
        await using var database = await TestDatabase.CreateAsync(interceptors: commands);
        var repository = new PostgresRepository<TestRecord>(database.Context);

        await repository.TruncateTableAsync();
        await repository.TruncateTableAsync(restartIdentity: true);
        await repository.TruncateTableAsync(cascade: true);
        await repository.TruncateTableAsync(restartIdentity: true, cascade: true);

        Assert.That(commands.Commands.Where(command => command.StartsWith("TRUNCATE TABLE")), Is.EqualTo(new[]
        {
            "TRUNCATE TABLE \"test_records\";",
            "TRUNCATE TABLE \"test_records\" RESTART IDENTITY;",
            "TRUNCATE TABLE \"test_records\" CASCADE;",
            "TRUNCATE TABLE \"test_records\" RESTART IDENTITY CASCADE;"
        }));
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
