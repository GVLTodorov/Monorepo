using Microsoft.EntityFrameworkCore;

namespace Sqlite.Tests;

[TestFixture]
public sealed class SqliteDbContextTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ConnectionStringConstructor_NullOrWhitespace_ThrowsArgumentException(string? connectionString)
    {
        Assert.That(
            () => new SqliteDbContext<TestRecord>(connectionString!),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void ConnectionStringConstructor_ValidString_ConfiguresSqliteWithoutConnecting()
    {
        using var context = new SqliteDbContext<TestRecord>("Data Source=:memory:");

        Assert.That(context.Database.ProviderName, Is.EqualTo("Microsoft.EntityFrameworkCore.Sqlite"));
    }

    [Test]
    public void OptionsConstructor_UnconfiguredOptions_ThrowsDescriptiveException()
    {
        using var context = new SqliteDbContext<TestRecord>(
            new DbContextOptionsBuilder<SqliteDbContext<TestRecord>>().Options);

        Assert.That(
            () => _ = context.Database.ProviderName,
            Throws.InvalidOperationException.With.Message.Contains("connection string or configured DbContextOptions"));
    }

    [Test]
    public async Task OptionsConstructor_ConfiguredSqlite_UsesOptionsAndExposesEntitySet()
    {
        await using var database = await TestDatabase.CreateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(database.Context.Database.ProviderName, Is.EqualTo("Microsoft.EntityFrameworkCore.Sqlite"));
            Assert.That(database.Context.Entities, Is.SameAs(database.Context.Set<TestRecord>()));
        });
    }

    [Test]
    public async Task Model_MapsTableKeyAndAuditProperties()
    {
        await using var database = await TestDatabase.CreateAsync();
        var entity = database.Context.Model.FindEntityType(typeof(TestRecord))!;

        Assert.Multiple(() =>
        {
            Assert.That(entity.GetTableName(), Is.EqualTo("test_records"));
            Assert.That(entity.FindPrimaryKey()!.Properties.Select(property => property.Name),
                Is.EqualTo(new[] { nameof(TestRecord.Id) }));
            Assert.That(entity.FindProperty(nameof(TestRecord.Id))!.GetMaxLength(), Is.EqualTo(64));
            Assert.That(entity.FindProperty(nameof(TestRecord.CreatedDateTime))!.IsNullable, Is.False);
            Assert.That(entity.FindProperty(nameof(TestRecord.UpdatedDateTime))!.IsNullable, Is.True);
            Assert.That(entity.FindProperty(nameof(TestRecord.DeletedDateTime))!.IsNullable, Is.True);
            Assert.That(entity.FindProperty(nameof(TestRecord.IsDeleted)), Is.Null);
        });
    }
}
