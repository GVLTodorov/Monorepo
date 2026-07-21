using System.Linq.Expressions;
using Mongo.Helpers;

namespace Mongo.Tests;

[TestFixture]
public class ExpressionHelperTests
{
    [Test]
    public void GetMemberName_ReturnsPropertyName_ForPropertyExpression()
    {
        Expression<Func<TestEntityForExpression, object>> expression = x => x.Name;
        
        var memberName = ExpressionHelper.GetMemberName(expression);
        
        Assert.That(memberName, Is.EqualTo("Name"));
    }

    [Test]
    public void GetMemberName_ReturnsPropertyName_ForValueTypeProperty()
    {
        Expression<Func<TestEntityForExpression, object>> expression = x => x.Age;
        
        var memberName = ExpressionHelper.GetMemberName(expression);
        
        Assert.That(memberName, Is.EqualTo("Age"));
    }

    [Test]
    public void GetMemberValue_ReturnsPropertyValue_WhenValueExists()
    {
        var entity = new TestEntityForExpression { Name = "Test", Age = 25 };
        Expression<Func<TestEntityForExpression?, object>> expression = x => x!.Name;
        
        var value = ExpressionHelper.GetMemberValue(expression, entity);
        
        Assert.That(value, Is.EqualTo("Test"));
    }

    [Test]
    public void GetMemberValue_ReturnsNull_WhenValueIsNull()
    {
        Expression<Func<TestEntityForExpression, object>> expression = x => x.Name;
        
        var value = ExpressionHelper.GetMemberValue<TestEntityForExpression>(expression, null!);
        
        Assert.That(value, Is.Null);
    }

    [Test]
    public void GetMemberNames_ReturnsMultiplePropertyNames()
    {
        var names = ExpressionHelper.GetMemberNames<TestEntityForExpression>(
            x => x.Name,
            x => x.Age);
        
        Assert.That(names, Has.Count.EqualTo(2));
        Assert.That(names, Contains.Item("Name"));
        Assert.That(names, Contains.Item("Age"));
    }

    [Test]
    public void AndAlso_CombinesTwoExpressions_WithAndOperator()
    {
        Expression<Func<TestEntityForExpression, bool>> expr1 = x => x.Age > 18;
        Expression<Func<TestEntityForExpression, bool>> expr2 = x => x.Name == "Test";
        
        var combined = expr1.AndAlso(expr2);
        
        var entity1 = new TestEntityForExpression { Name = "Test", Age = 25 };
        var entity2 = new TestEntityForExpression { Name = "Other", Age = 25 };
        
        Assert.That(combined.Compile()(entity1), Is.True);
        Assert.That(combined.Compile()(entity2), Is.False);
    }

    [Test]
    public void AndAlso_ThrowsArgumentNullException_WhenLeftIsNull()
    {
        Expression<Func<TestEntityForExpression, bool>> expr1 = null!;
        Expression<Func<TestEntityForExpression, bool>> expr2 = x => x.Name == "Test";
        
        Assert.Throws<NullReferenceException>(() => expr1.AndAlso(expr2));
    }
}

public class TestEntityForExpression
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
}

