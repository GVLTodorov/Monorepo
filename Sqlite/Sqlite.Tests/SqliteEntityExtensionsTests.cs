using System.Reflection;
using Sqlite.Helpers;

namespace Sqlite.Tests;

[TestFixture]
public sealed class SqliteEntityExtensionsTests
{
    [Test]
    public void GetTableName_NoAttribute_RemovesEntityAndUsesSnakeCase()
    {
        Assert.That(
            SqliteEntityExtensions.GetTableName<CustomerOrderEntity>(),
            Is.EqualTo("customer_order"));
    }

    [Test]
    public void GetTableName_InheritedAttribute_UsesAttributedName()
    {
        Assert.That(
            SqliteEntityExtensions.GetTableName<InheritedTableRecord>(),
            Is.EqualTo("audit_records"));
    }

    [Test]
    public void GetTableName_EntitySubstringAndEmptyAttribute_AreHandledLiterally()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SqliteEntityExtensions.GetTableName<IdentityEntity>(), Is.EqualTo("identity"));
            Assert.That(SqliteEntityExtensions.GetTableName<EmptyTableRecord>(), Is.Empty);
        });
    }

    [TestCase("HTTPServerValue", "h_t_t_p_server_value")]
    [TestCase("already_snake", "already_snake")]
    [TestCase("", "")]
    public void ToSnakeCase_RepresentativeNames_ReturnExpectedValue(string input, string expected)
    {
        var method = typeof(SqliteEntityExtensions).GetMethod(
            "ToSnakeCase",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.That(method.Invoke(null, [input]), Is.EqualTo(expected));
    }

    private sealed class CustomerOrderEntity : SqliteEntity;

    private sealed class IdentityEntity : SqliteEntity;

    [TableName("AuditRecords")]
    private class AttributedBaseRecord : SqliteEntity;

    private sealed class InheritedTableRecord : AttributedBaseRecord;

    [TableName("")]
    private sealed class EmptyTableRecord : SqliteEntity;
}
