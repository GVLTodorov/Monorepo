using System.Collections;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Sqlite;
using Sqlite.Helpers;

namespace Sqlite.Tests;

[TableName("CoverageRecords")]
internal sealed class CoverageRecord : SqliteEntity
{
    public string Name { get; set; } = string.Empty;
    public bool BoolValue { get; set; }
    public byte ByteValue { get; set; }
    public short ShortValue { get; set; }
    public int IntValue { get; set; }
    public int OtherIntValue { get; set; }
    public int? NullableInt { get; set; }
    public long LongValue { get; set; }
    public float FloatValue { get; set; }
    public double DoubleValue { get; set; }
    public decimal DecimalValue { get; set; }
    public char CharValue { get; set; }
    public Guid GuidValue { get; set; }
    public DateTime DateTimeValue { get; set; }
    public DateTimeOffset DateTimeOffsetValue { get; set; }
    public byte[] Bytes { get; set; } = [];
    public int[] Numbers { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public CoveragePayload Payload { get; set; } = new();
    public CoverageKind Kind { get; set; }
}

internal sealed class CoveragePayload
{
    public string Value { get; set; } = string.Empty;
}

internal enum CoverageKind
{
    First = 1,
    Second = 2
}

internal sealed class CoverageRepository(DbConnection connection)
    : SqliteRepository<CoverageRecord>(connection)
{
    public bool UsesProvider => UsesSqliteProvider;
}

[TestFixture]
public sealed class RepositoryCoreCoverageTests
{
    private const string RepositoryNamespace = "Sqlite";
    private static readonly Assembly RepositoryAssembly = typeof(SqliteRepository<>).Assembly;

    [SetUp]
    public void InitializeSqliteProvider() =>
        RepositoryAssembly
            .GetType("Sqlite.SqliteProviderInitializer", throwOnError: true)!
            .GetMethod("EnsureInitialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, null);

    [Test]
    public async Task RichValues_RoundTripThroughSqliteStorage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var repository = new CoverageRepository(connection);
        var expected = new CoverageRecord
        {
            Id = "rich",
            Name = "Alpha",
            BoolValue = true,
            ByteValue = 2,
            ShortValue = 3,
            IntValue = 4,
            OtherIntValue = 5,
            NullableInt = 6,
            LongValue = 7,
            FloatValue = 8.5f,
            DoubleValue = 9.5,
            DecimalValue = 10.5m,
            CharValue = 'Z',
            GuidValue = Guid.Parse("b17031f4-7ae3-476d-b30f-4f2e64d767b7"),
            DateTimeValue = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc),
            DateTimeOffsetValue = new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero),
            Bytes = [1, 2, 3],
            Numbers = [4, 5, 6],
            Tags = ["one", "two"],
            Payload = new CoveragePayload { Value = "payload" },
            Kind = CoverageKind.Second
        };

        await repository.EnsureTableAsync();
        await repository.InsertAsync(expected);
        var actual = await repository.GetByIdAsync(expected.Id);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual!.Name, Is.EqualTo(expected.Name));
            Assert.That(actual.BoolValue, Is.True);
            Assert.That(actual.ByteValue, Is.EqualTo(expected.ByteValue));
            Assert.That(actual.ShortValue, Is.EqualTo(expected.ShortValue));
            Assert.That(actual.NullableInt, Is.EqualTo(expected.NullableInt));
            Assert.That(actual.LongValue, Is.EqualTo(expected.LongValue));
            Assert.That(actual.FloatValue, Is.EqualTo(expected.FloatValue));
            Assert.That(actual.DoubleValue, Is.EqualTo(expected.DoubleValue));
            Assert.That(actual.DecimalValue, Is.EqualTo(expected.DecimalValue));
            Assert.That(actual.CharValue, Is.EqualTo(expected.CharValue));
            Assert.That(actual.GuidValue, Is.EqualTo(expected.GuidValue));
            Assert.That(actual.DateTimeValue, Is.EqualTo(expected.DateTimeValue));
            Assert.That(actual.DateTimeOffsetValue, Is.EqualTo(expected.DateTimeOffsetValue));
            Assert.That(actual.Bytes, Is.EqualTo(expected.Bytes));
            Assert.That(actual.Numbers, Is.EqualTo(expected.Numbers));
            Assert.That(actual.Tags, Is.EqualTo(expected.Tags));
            Assert.That(actual.Payload.Value, Is.EqualTo("payload"));
            Assert.That(actual.Kind, Is.EqualTo(CoverageKind.Second));
        });

        await repository.InsertAsync(new CoverageRecord { Id = "nulls", Name = "Nulls", NullableInt = null });
        Assert.That((await repository.GetByIdAsync("nulls"))!.NullableInt, Is.Null);
    }

    [Test]
    public void SqlTypeMapping_CoversEverySupportedTypeAndDialect()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        var sqlite = CreateCore(connection, "Sqlite");
        var postgres = CreateCore(connection, "PostgreSql");

        var commonExpectations = new Dictionary<Type, string>
        {
            [typeof(string)] = "TEXT",
            [typeof(char)] = "TEXT",
            [typeof(Guid)] = "TEXT",
            [typeof(byte)] = "INTEGER",
            [typeof(short)] = "INTEGER",
            [typeof(int)] = "INTEGER",
            [typeof(int?)] = "INTEGER",
            [typeof(long)] = "BIGINT",
            [typeof(decimal)] = "NUMERIC",
            [typeof(CoverageKind)] = "INTEGER",
            [typeof(CoveragePayload)] = "TEXT"
        };

        foreach (var (type, expected) in commonExpectations)
        {
            Assert.That(InvokePrivate(sqlite, "GetSqlType", type), Is.EqualTo(expected));
            Assert.That(InvokePrivate(postgres, "GetSqlType", type), Is.EqualTo(expected));
        }

        Assert.Multiple(() =>
        {
            Assert.That(InvokePrivate(sqlite, "GetSqlType", typeof(bool)), Is.EqualTo("INTEGER"));
            Assert.That(InvokePrivate(postgres, "GetSqlType", typeof(bool)), Is.EqualTo("BOOLEAN"));
            Assert.That(InvokePrivate(sqlite, "GetSqlType", typeof(float)), Is.EqualTo("REAL"));
            Assert.That(InvokePrivate(postgres, "GetSqlType", typeof(double)), Is.EqualTo("DOUBLE PRECISION"));
            Assert.That(InvokePrivate(sqlite, "GetSqlType", typeof(DateTime)), Is.EqualTo("TEXT"));
            Assert.That(InvokePrivate(postgres, "GetSqlType", typeof(DateTimeOffset)), Is.EqualTo("TIMESTAMPTZ"));
            Assert.That(InvokePrivate(sqlite, "GetSqlType", typeof(byte[])), Is.EqualTo("BLOB"));
            Assert.That(InvokePrivate(postgres, "GetSqlType", typeof(byte[])), Is.EqualTo("BYTEA"));
            Assert.That(InvokePrivate(sqlite, "GetSqlType", typeof(int[])), Is.EqualTo("TEXT"));
            Assert.That(InvokePrivate(postgres, "GetSqlType", typeof(int[])), Is.EqualTo("INTEGER[]"));
            Assert.That(InvokePrivate(sqlite, "GetSqlType", typeof(List<string>)), Is.EqualTo("TEXT"));
            Assert.That(InvokePrivate(postgres, "GetSqlType", typeof(List<string>)), Is.EqualTo("TEXT[]"));
        });
    }

    [Test]
    public void StorageConversion_CoversScalarCollectionJsonAndNullValues()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        var sqlite = CreateCore(connection, "Sqlite");
        var postgres = CreateCore(connection, "PostgreSql");
        var guid = Guid.Parse("88b303a0-6cb4-4781-b1be-b17fe25916a1");
        var localDate = DateTime.SpecifyKind(new DateTime(2025, 1, 2, 3, 4, 5), DateTimeKind.Local);
        var offset = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));

        Assert.Multiple(() =>
        {
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", [null]), Is.EqualTo(DBNull.Value));
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", CoverageKind.Second), Is.EqualTo(2));
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", guid), Is.EqualTo(guid.ToString()));
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", 'Q'), Is.EqualTo("Q"));
            Assert.That(((DateTime)InvokePrivate(sqlite, "ConvertToStorage", localDate)!).Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", offset), Is.EqualTo(offset.ToUniversalTime()));
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", "text"), Is.EqualTo("text"));
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", true), Is.True);
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", (byte)1), Is.EqualTo((byte)1));
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", (short)2), Is.EqualTo((short)2));
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", 3), Is.EqualTo(3));
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", 4L), Is.EqualTo(4L));
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", 5f), Is.EqualTo(5f));
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", 6d), Is.EqualTo(6d));
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", 7m), Is.EqualTo(7m));
            Assert.That(InvokePrivate(sqlite, "ConvertToStorage", new byte[] { 8 }), Is.EqualTo(new byte[] { 8 }));
            Assert.That(InvokePrivate(postgres, "ConvertToStorage", new List<int> { 1, 2 }), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(
                InvokePrivate(sqlite, "ConvertToStorage", new CoveragePayload { Value = "json" }),
                Is.EqualTo("{\"Value\":\"json\"}"));
            Assert.That(
                InvokePrivate(sqlite, "ConvertToStorage", new List<int> { 1, 2 }),
                Is.EqualTo("[1,2]"));
        });
    }

    [Test]
    public void StorageMaterialization_CoversEveryDestinationShape()
    {
        var dateText = "2025-03-04T05:06:07Z";
        var offset = new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.FromHours(2));
        var guid = Guid.Parse("445ceae7-2851-46d5-9435-7e171fb42328");

        Assert.Multiple(() =>
        {
            Assert.That(ConvertFromStorage("same", typeof(string)), Is.EqualTo("same"));
            Assert.That(ConvertFromStorage(offset, typeof(DateTime)), Is.EqualTo(offset.UtcDateTime));
            Assert.That(((DateTime)ConvertFromStorage(dateText, typeof(DateTime))!).Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(ConvertFromStorage(dateText, typeof(DateTimeOffset)), Is.EqualTo(DateTimeOffset.Parse(dateText)));
            Assert.That(ConvertFromStorage(guid.ToString(), typeof(Guid)), Is.EqualTo(guid));
            Assert.That(ConvertFromStorage(2L, typeof(CoverageKind)), Is.EqualTo(CoverageKind.Second));
            Assert.That(ConvertFromStorage(new object?[] { "1", 2L }, typeof(int[])), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ConvertFromStorage(new object?[] { "3", 4L }, typeof(List<int>)), Is.EqualTo(new[] { 3, 4 }));
            Assert.That(
                ((CoveragePayload)ConvertFromStorage("{\"Value\":\"json\"}", typeof(CoveragePayload))!).Value,
                Is.EqualTo("json"));
            Assert.That(ConvertFromStorage("42", typeof(int?)), Is.EqualTo(42));
            Assert.That(
                ConvertFromStorage(new object?[] { null, "same", 42 }, typeof(List<string>)),
                Is.EqualTo(new string?[] { null, "same", "42" }));
            Assert.That(ConvertFromStorage(42, typeof(string)), Is.EqualTo("42"));
        });

        Assert.That(
            () => ConvertFromStorage(new object?[] { 1, 2 }, typeof(HashSet<int>)),
            Throws.TypeOf<TargetInvocationException>());
    }

    [Test]
    public void PredicateTranslation_CoversLogicalComparisonNullStringAndContainsBranches()
    {
        var ids = new List<int> { 2, 4 };
        var empty = Array.Empty<int>();

        Assert.Multiple(() =>
        {
            Assert.That(Translate(record => record.BoolValue), Is.EqualTo("\"BoolValue\" = @p0"));
            Assert.That(Translate(record => !record.BoolValue), Does.StartWith("NOT ("));
            Assert.That(Translate(record => true), Is.EqualTo("1 = 1"));
            Assert.That(Translate(record => false), Is.EqualTo("1 = 0"));
            Assert.That(
                Translate(record => record.IntValue >= 2 && record.Name != "x"),
                Is.EqualTo("(\"IntValue\" >= @p0) AND (\"Name\" <> @p1)"));
            Assert.That(
                Translate(record => record.IntValue < 2 || record.OtherIntValue <= 3),
                Is.EqualTo("(\"IntValue\" < @p0) OR (\"OtherIntValue\" <= @p1)"));
            Assert.That(Translate(record => 5 < record.IntValue), Is.EqualTo("\"IntValue\" > @p0"));
            Assert.That(Translate(record => record.IntValue > 2), Is.EqualTo("\"IntValue\" > @p0"));
            Assert.That(Translate(record => record.IntValue >= 2), Is.EqualTo("\"IntValue\" >= @p0"));
            Assert.That(Translate(record => record.IntValue < 2), Is.EqualTo("\"IntValue\" < @p0"));
            Assert.That(Translate(record => record.IntValue <= 2), Is.EqualTo("\"IntValue\" <= @p0"));
            Assert.That(Translate(record => 5 <= record.IntValue), Is.EqualTo("\"IntValue\" >= @p0"));
            Assert.That(Translate(record => 5 > record.IntValue), Is.EqualTo("\"IntValue\" < @p0"));
            Assert.That(Translate(record => 5 >= record.IntValue), Is.EqualTo("\"IntValue\" <= @p0"));
            Assert.That(Translate(record => 2 == record.IntValue), Is.EqualTo("\"IntValue\" = @p0"));
            Assert.That(Translate(record => 2 != record.IntValue), Is.EqualTo("\"IntValue\" <> @p0"));
            Assert.That(
                Translate(record => record.IntValue == record.OtherIntValue),
                Is.EqualTo("\"IntValue\" = \"OtherIntValue\""));
            Assert.That(Translate(record => record.NullableInt == null), Is.EqualTo("\"NullableInt\" IS NULL"));
            Assert.That(Translate(record => record.NullableInt != null), Is.EqualTo("\"NullableInt\" IS NOT NULL"));
            Assert.That(Translate(record => record.NullableInt.HasValue == true), Is.EqualTo("\"NullableInt\" = @p0"));
            Assert.That(Translate(record => record.Name.StartsWith(@"a\%_")), Does.Contain("LIKE @p0 ESCAPE"));
            Assert.That(Translate(record => record.Name.EndsWith("z")), Does.Contain("LIKE @p0 ESCAPE"));
            Assert.That(Translate(record => record.Name.Contains("mid")), Does.Contain("LIKE @p0 ESCAPE"));
            Assert.That(Translate(record => ids.Contains(record.IntValue)), Is.EqualTo("\"IntValue\" IN (@p0, @p1)"));
            Assert.That(Translate(record => Enumerable.Contains(ids, record.IntValue)), Is.EqualTo("\"IntValue\" IN (@p0, @p1)"));
            Assert.That(Translate(record => empty.Contains(record.IntValue)), Is.EqualTo("1 = 0"));
            Assert.That(Translate(ConstantComparison(true)), Is.EqualTo("1 = 1"));
            Assert.That(Translate(ConstantComparison(false)), Is.EqualTo("1 = 0"));
        });

        var (_, values) = TranslateWithParameters(record => record.Name.StartsWith(@"a\%_"));
        Assert.That(values, Is.EqualTo(new object?[] { @"a\\\%\_%" }));

        string? nullPrefix = null;
        var (_, nullValues) = TranslateWithParameters(record => record.Name.StartsWith(nullPrefix!));
        Assert.That(nullValues, Is.EqualTo(new object?[] { "%" }));
    }

    [Test]
    public void PredicateTranslation_RejectsUnsupportedShapes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => Translate(record => record.Name.Equals("x")),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(
                () => Translate(record => record.Tags.Contains(record.Name)),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(
                () => Translate(record => record.IntValue == record.IntValue + 1),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(
                () => Translate(UnsupportedNullComparison()),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(
                () => Translate(UnsupportedExclusiveOr()),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(
                () => Translate(UnsupportedTopLevelExpression()),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(
                () => Translate(record => object.Equals(record.Name, "x")),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(
                () => Translate(record => CoveragePredicates.Contains(record.Name)),
                Throws.TypeOf<NotSupportedException>());
            var ids = new List<int> { 1, 2 };
            Assert.That(
                () => Translate(record => ids.Contains(1)),
                Throws.TypeOf<NotSupportedException>());
            var nonEnumerable = new NonEnumerableContains();
            Assert.That(
                () => Translate(record => nonEnumerable.Contains(record.IntValue)),
                Throws.TypeOf<NotSupportedException>());
        });
    }

    [Test]
    public async Task PublicRepository_CoversValidationEmptyAndMultiplicityBranches()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var repository = new CoverageRepository(connection);
        await repository.EnsureTableAsync();

        await repository.InsertAsync((ICollection<CoverageRecord>?)null);
        await repository.InsertAsync(Array.Empty<CoverageRecord>());
        await repository.UpdateManyAsync(null, new Dictionary<string, object>());
        await repository.UpdateManyAsync([], new Dictionary<string, object>());
        await repository.DeleteManyAsync(Array.Empty<CoverageRecord>());
        await repository.DeleteByIdAsync("missing");
        await repository.DeleteOneAsync(record => record.Id == "missing");
        Assert.That(await repository.DeleteManyAsync(record => record.Id == "missing"), Is.Empty);

        await repository.InsertAsync(new[]
        {
            new CoverageRecord { Id = "one", Name = "duplicate", IntValue = 1 },
            new CoverageRecord { Id = "two", Name = "duplicate", IntValue = 2 }
        });
        await repository.CreateIndexAsync(record => record.Name, TimeSpan.FromMinutes(1));
        await repository.CreateIndexAsync(
            new[]
            {
                ((Expression<Func<CoverageRecord, object>>)(record => record.Name), SortDirection.Descending),
                ((Expression<Func<CoverageRecord, object>>)(record => record.IntValue), SortDirection.Ascending)
            });
        await repository.RemoveIndexAsync(
            new Expression<Func<CoverageRecord, object>>[]
            {
                record => record.Name,
                record => record.IntValue
            });
        var defaultPage = await repository.GetPagedListAsync(1, 10);
        var orderedPage = await repository.GetPagedListAsync(
            1,
            10,
            predicate: null,
            orderBy: record => record.Name);
        var emptySortPage = await repository.GetPagedListAsync(
            1,
            10,
            predicate: null,
            orderBy: Array.Empty<SortExpression<CoverageRecord>>(),
            includeDeletes: true);
        var nullSortPage = await repository.GetPagedListAsync(
            1,
            10,
            predicate: null,
            orderBy: null!,
            includeDeletes: true);
        await repository.PullAsync(record => record.Numbers, number => number > 0);
        await repository.PullAsync(record => record.Tags.Where(tag => tag.Length > 0));

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await repository.GetSingleOrDefaultAsync(record => record.Name == "duplicate"),
                Throws.InvalidOperationException);
            Assert.That(
                async () => await repository.GetPagedListAsync(1, 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => repository.CreateIndexAsync(Array.Empty<Expression<Func<CoverageRecord, object>>>()),
                Throws.ArgumentException);
            Assert.That(
                () => repository.RemoveIndexAsync(Array.Empty<Expression<Func<CoverageRecord, object>>>()),
                Throws.ArgumentException);
            Assert.That(
                () => repository.CreateIndexAsync(record => new object()),
                Throws.ArgumentException);
            Assert.That(
                async () => await repository.UpdateManyAsync([], null!),
                Throws.ArgumentNullException);
            Assert.That(
                async () => await repository.PullAsync<string>(null!),
                Throws.ArgumentNullException);
            Assert.That(
                async () => await repository.DeleteAsync(null!),
                Throws.ArgumentNullException);
            Assert.That(
                async () => await repository.DeleteManyAsync((ICollection<CoverageRecord>)null!),
                Throws.ArgumentNullException);
            Assert.That(
                async () => await repository.RestoreAsync((CoverageRecord)null!),
                Throws.ArgumentNullException);
            Assert.That(defaultPage.TotalCount, Is.EqualTo(2));
            Assert.That(emptySortPage.TotalCount, Is.EqualTo(2));
            Assert.That(orderedPage.TotalCount, Is.EqualTo(2));
            Assert.That(nullSortPage.TotalCount, Is.EqualTo(2));
            Assert.That(
                () => new CoverageRepository(null!),
                Throws.ArgumentNullException);
        });

        await repository.DeleteAsync((await repository.GetByIdAsync("one"))!, hardDelete: true);
        await repository.DeleteOneAsync(record => record.Id == "two");
        Assert.That(
            async () => await repository.InsertAsync(new[]
            {
                new CoverageRecord { Id = "duplicate-key" },
                new CoverageRecord { Id = "duplicate-key" }
            }),
            Throws.Exception);

        Assert.That(repository.UsesProvider, Is.True);
        await using var owned = new SqliteRepository<CoverageRecord>("Data Source=:memory:");
        await owned.DisposeAsync();
    }

    [Test]
    public async Task CoreTransactionAndPostgresDialectFailurePaths_AreExecuted()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var sqlite = CreateCore(connection, "Sqlite");
        var postgres = CreateCore(connection, "PostgreSql");

        var failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await InvokeTask(sqlite, "ExecuteInTransactionAsync",
                (Func<DbTransaction, Task>)(_ => throw new InvalidOperationException("rollback"))));
        Assert.That(failure!.Message, Is.EqualTo("rollback"));

        Assert.That(
            async () => await InvokeTask(postgres, "TableExistsAsync", "records", null),
            Throws.Exception);
        Assert.That(
            async () => await InvokeTask(postgres, "TableExistsAsync", "records", "public"),
            Throws.Exception);

        SetPrivateField(postgres, "_tableEnsured", true);
        Assert.That(
            async () => await InvokeTask(postgres, "TruncateAsync", true, true),
            Throws.Exception);
        Assert.That(
            async () => await InvokeTask(postgres, "TruncateAsync", false, false),
            Throws.Exception);

        SetPrivateField(sqlite, "_tableEnsured", true);
        Assert.That(
            async () => await InvokeGetAllWithOffset(sqlite),
            Throws.Exception);
        Assert.That(
            () => InvokePublic(sqlite, "RemoveIndexAsync", [Array.Empty<string>()]),
            Throws.TypeOf<TargetInvocationException>());
        Assert.That(
            () => InvokePublic(sqlite, "GetMemberName", [null]),
            Throws.TypeOf<TargetInvocationException>());
        Assert.That(
            () => InvokePublic(
                sqlite,
                "GetMemberName",
                (Expression<Func<CoverageRecord, object>>)(record => record.Name.Length)),
            Throws.TypeOf<TargetInvocationException>());
        Assert.That(
            () => InvokePrivate(sqlite, "GetProperty", "missing"),
            Throws.TypeOf<TargetInvocationException>());
        Assert.That(
            sqlite.GetType().GetProperty("Connection")!.GetValue(sqlite),
            Is.SameAs(connection));
        Assert.That(InvokePrivateStatic(sqlite, "TryGetCollectionElementType", typeof(string)), Is.Null);
        Assert.That(InvokePrivateStatic(sqlite, "TryGetCollectionElementType", typeof(byte[])), Is.Null);
        Assert.That(
            () => CreateCore(null!, "Sqlite"),
            Throws.TypeOf<TargetInvocationException>());
        Assert.That(
            () => CreateCore(connection, "Sqlite", " "),
            Throws.TypeOf<TargetInvocationException>());

        var constant = Expression.Constant(1);
        var nestedConvert = Expression.Convert(
            Expression.Convert(constant, typeof(long)),
            typeof(object));
        Assert.That(InvokeRepositoryStripConvert(constant), Is.SameAs(constant));
        Assert.That(InvokeRepositoryStripConvert(nestedConvert), Is.SameAs(constant));
    }

    private static object CreateCore(DbConnection connection, string dialectName, string tableName = "CoverageRecords")
    {
        var dialectType = RepositoryAssembly.GetType($"{RepositoryNamespace}.SqlDialect", throwOnError: true)!;
        var coreType = RepositoryAssembly
            .GetType($"{RepositoryNamespace}.SqliteRepositoryCore`1", throwOnError: true)!
            .MakeGenericType(typeof(CoverageRecord));
        var dialect = Enum.Parse(dialectType, dialectName);

        return Activator.CreateInstance(
            coreType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [connection, dialect, tableName, null],
            culture: null)!;
    }

    private static object? InvokePrivate(object target, string methodName, params object?[] arguments) =>
        target.GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, arguments);

    private static object? InvokePrivateStatic(object target, string methodName, params object?[] arguments) =>
        target.GetType()
            .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments);

    private static object? InvokePublic(object target, string methodName, params object?[] arguments) =>
        target.GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(target, arguments);

    private static Expression InvokeRepositoryStripConvert(Expression expression) =>
        (Expression)typeof(SqliteRepository<CoverageRecord>)
            .GetMethod("StripConvert", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;

    private static object? ConvertFromStorage(object value, Type destinationType) =>
        RepositoryAssembly
            .GetType($"{RepositoryNamespace}.SqliteRepositoryCore`1", throwOnError: true)!
            .MakeGenericType(typeof(CoverageRecord))
            .GetMethod("ConvertFromStorage", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [value, destinationType]);

    private static string Translate(Expression<Func<CoverageRecord, bool>> expression)
    {
        try
        {
            return TranslateWithParameters(expression).Sql;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private static (string Sql, object?[] Values) TranslateWithParameters(
        Expression<Func<CoverageRecord, bool>> expression)
    {
        var translatorType = RepositoryAssembly
            .GetType($"{RepositoryNamespace}.PredicateSqlTranslator`1", throwOnError: true)!
            .MakeGenericType(typeof(CoverageRecord));
        var translator = Activator.CreateInstance(
            translatorType,
            [new Func<string, string>(identifier => $"\"{identifier}\"")])!;
        var sql = (string)translatorType.GetMethod("Translate")!.Invoke(translator, [expression])!;
        var parameters = (IEnumerable)translatorType.GetProperty("Parameters")!.GetValue(translator)!;
        var values = parameters.Cast<object>()
            .Select(parameter => parameter.GetType().GetProperty("Value")!.GetValue(parameter))
            .ToArray();

        return (sql, values);
    }

    private static Expression<Func<CoverageRecord, bool>> ConstantComparison(bool value)
    {
        var parameter = Expression.Parameter(typeof(CoverageRecord), "record");
        var comparison = value
            ? Expression.Equal(Expression.Constant(1), Expression.Constant(1))
            : Expression.NotEqual(Expression.Constant(1), Expression.Constant(1));

        return Expression.Lambda<Func<CoverageRecord, bool>>(comparison, parameter);
    }

    private static Expression<Func<CoverageRecord, bool>> UnsupportedNullComparison()
    {
        var parameter = Expression.Parameter(typeof(CoverageRecord), "record");
        var member = Expression.Property(parameter, nameof(CoverageRecord.NullableInt));
        var comparison = Expression.GreaterThan(member, Expression.Constant(null, typeof(int?)));

        return Expression.Lambda<Func<CoverageRecord, bool>>(comparison, parameter);
    }

    private static Expression<Func<CoverageRecord, bool>> UnsupportedExclusiveOr()
    {
        var parameter = Expression.Parameter(typeof(CoverageRecord), "record");
        var member = Expression.Property(parameter, nameof(CoverageRecord.BoolValue));

        return Expression.Lambda<Func<CoverageRecord, bool>>(
            Expression.ExclusiveOr(member, Expression.Constant(true)),
            parameter);
    }

    private static Expression<Func<CoverageRecord, bool>> UnsupportedTopLevelExpression()
    {
        var parameter = Expression.Parameter(typeof(CoverageRecord), "record");

        return Expression.Lambda<Func<CoverageRecord, bool>>(
            Expression.Block(Expression.Constant(true)),
            parameter);
    }

    private static async Task InvokeTask(object target, string methodName, params object?[] arguments)
    {
        var task = (Task)target.GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(target, arguments)!;
        await task;
    }

    private static async Task InvokeGetAllWithOffset(object target)
    {
        var method = target.GetType().GetMethod(
            "GetAllAsync",
            BindingFlags.Instance | BindingFlags.Public,
            [
                typeof(Expression<Func<CoverageRecord, bool>>),
                typeof(string),
                typeof(bool),
                typeof(bool),
                typeof(int?),
                typeof(int?)
            ])!;
        await (Task)method.Invoke(target, [null, "Name", false, true, 1, null])!;
    }

    private static void SetPrivateField(object target, string fieldName, object value) =>
        target.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}

internal static class CoveragePredicates
{
    public static bool Contains(string value) => value.Length > 0;
}

internal sealed class NonEnumerableContains
{
    public bool Contains(int value) => value > 0;
}
