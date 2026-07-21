using System.Reflection;
using Postgres.Helpers;

namespace Postgres.Tests;

[TestFixture]
public sealed class PostgresEntityExtensionsTests
{
    [Test]
    public void GetTableName_NoAttribute_RemovesEntityAndUsesSnakeCase()
    {
        Assert.That(
            PostgresEntityExtensions.GetTableName<CustomerOrderEntity>(),
            Is.EqualTo("customer_order"));
    }

    [Test]
    public void GetTableName_InheritedAttribute_UsesAttributedName()
    {
        Assert.That(
            PostgresEntityExtensions.GetTableName<InheritedTableRecord>(),
            Is.EqualTo("audit_records"));
    }

    [Test]
    public void GetTableName_EntitySubstringAndEmptyAttribute_AreHandledLiterally()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PostgresEntityExtensions.GetTableName<IdentityEntity>(), Is.EqualTo("identity"));
            Assert.That(PostgresEntityExtensions.GetTableName<EmptyTableRecord>(), Is.Empty);
        });
    }

    [TestCase("HTTPServerValue", "h_t_t_p_server_value")]
    [TestCase("already_snake", "already_snake")]
    [TestCase("", "")]
    public void ToSnakeCase_RepresentativeNames_ReturnExpectedValue(string input, string expected)
    {
        var method = typeof(PostgresEntityExtensions).GetMethod(
            "ToSnakeCase",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.That(method.Invoke(null, [input]), Is.EqualTo(expected));
    }

    private sealed class CustomerOrderEntity : PostgresEntity;

    private sealed class IdentityEntity : PostgresEntity;

    [TableName("AuditRecords")]
    private class AttributedBaseRecord : PostgresEntity;

    private sealed class InheritedTableRecord : AttributedBaseRecord;

    [TableName("")]
    private sealed class EmptyTableRecord : PostgresEntity;
}
