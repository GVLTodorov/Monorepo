using Postgres.Helpers;

namespace Postgres.Tests;

[TestFixture]
public sealed class AttributeAndSortExpressionTests
{
    [Test]
    public void TableNameAttribute_NameAndUsage_AreRestrictedToClassesAndInherited()
    {
        var attribute = new TableNameAttribute("orders");
        var usage = (AttributeUsageAttribute)typeof(TableNameAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Single();

        Assert.Multiple(() =>
        {
            Assert.That(attribute.Name, Is.EqualTo("orders"));
            Assert.That(usage.ValidOn, Is.EqualTo(AttributeTargets.Class));
            Assert.That(usage.Inherited, Is.True);
            Assert.That(usage.AllowMultiple, Is.False);
        });
    }

    [Test]
    public void SensitiveDataAttribute_UsageAndReflection_AreRestrictedToProperties()
    {
        var instance = new SensitiveDataAttribute();
        var usage = (AttributeUsageAttribute)typeof(SensitiveDataAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Single();
        var propertyAttribute = typeof(TestRecord).GetProperty(nameof(TestRecord.Secret))!
            .GetCustomAttributes(typeof(SensitiveDataAttribute), true);

        Assert.Multiple(() =>
        {
            Assert.That(instance, Is.Not.Null);
            Assert.That(usage.ValidOn, Is.EqualTo(AttributeTargets.Property));
            Assert.That(propertyAttribute, Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void SortExpression_StoresExpressionAndBothEnumValues()
    {
        var sort = new SortExpression<TestRecord>
        {
            Expression = record => record.Score,
            SortDirection = SortDirection.Descending
        };

        var compiled = sort.Expression.Compile();

        Assert.Multiple(() =>
        {
            Assert.That(compiled(new TestRecord { Score = 17 }), Is.EqualTo(17));
            Assert.That(sort.SortDirection, Is.EqualTo(SortDirection.Descending));
            Assert.That((int)SortDirection.Ascending, Is.Zero);
            Assert.That((int)SortDirection.Descending, Is.EqualTo(1));
        });
    }
}
