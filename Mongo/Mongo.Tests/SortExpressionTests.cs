using Mongo.Helpers;

namespace Mongo.Tests;

[TestFixture]
public class SortExpressionTests
{
    [Test]
    public void SortExpression_Initializes_WithExpression()
    {
        var sortExpression = new SortExpression<TestEntityForSort>
        {
            Expression = x => x.Name,
            SortDirection = SortDirection.Ascending
        };
        
        Assert.That(sortExpression.Expression, Is.Not.Null);
        Assert.That(sortExpression.SortDirection, Is.EqualTo(SortDirection.Ascending));
    }

    [Test]
    public void SortDirection_Enum_HasCorrectValues()
    {
        Assert.That((int)SortDirection.Ascending, Is.EqualTo(0));
        Assert.That((int)SortDirection.Descending, Is.EqualTo(1));
    }
}

public class TestEntityForSort
{
    public string Name { get; set; } = string.Empty;
}

