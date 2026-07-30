using System.Linq.Expressions;
using System.Reflection;
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
    public void GetMemberNames_WithNoExpressions_ReturnsEmptyList()
    {
        var names = ExpressionHelper.GetMemberNames<TestEntityForExpression>();

        Assert.That(names, Is.Empty);
    }

    [Test]
    public void GetMemberName_CoversActionGenericTargetAndMethodExpressions()
    {
        Expression<Action<TestEntityForExpression>> action = entity => entity.Clear();
        Expression<Func<TestEntityForExpression, string>> target = entity => entity.Name;
        Expression<Func<TestEntityForExpression, object>> method = entity => entity.Describe();

        Assert.Multiple(() =>
        {
            Assert.That(ExpressionHelper.GetMemberName(action), Is.EqualTo(nameof(TestEntityForExpression.Clear)));
            Assert.That(
                ExpressionHelper.GetMemberName<TestEntityForExpression, string>(target),
                Is.EqualTo(nameof(TestEntityForExpression.Name)));
            Assert.That(ExpressionHelper.GetMemberName(method), Is.EqualTo(nameof(TestEntityForExpression.Describe)));
        });
    }

    [Test]
    public void GetMemberName_PrivateParser_CoversNullInvalidAndConvertedMethod()
    {
        var parser = typeof(ExpressionHelper).GetMethod(
            "GetMemberName",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(Expression)])!;
        var unaryParser = typeof(ExpressionHelper).GetMethod(
            "GetMemberName",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(UnaryExpression)])!;
        var parameter = Expression.Parameter(typeof(TestEntityForExpression), "entity");
        var method = Expression.Call(parameter, nameof(TestEntityForExpression.Describe), Type.EmptyTypes);
        var convertedMethod = Expression.Convert(method, typeof(object));

        var nullFailure = Assert.Throws<TargetInvocationException>(() => parser.Invoke(null, [null]));
        var invalidFailure = Assert.Throws<TargetInvocationException>(() =>
            parser.Invoke(null, [Expression.Constant("invalid")]));

        Assert.Multiple(() =>
        {
            Assert.That(nullFailure!.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(invalidFailure!.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(
                unaryParser.Invoke(null, [convertedMethod]),
                Is.EqualTo(nameof(TestEntityForExpression.Describe)));
        });
    }

    [Test]
    public void GetMemberType_CoversMemberMethodNullAndInvalidExpressions()
    {
        Expression<Func<TestEntityForExpression, object>> member = entity => entity.Name;
        Expression<Func<TestEntityForExpression, object>> method = entity => entity.Describe();
        var parser = typeof(ExpressionHelper).GetMethod(
            "GetMemberType",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(Expression)])!;

        var nullFailure = Assert.Throws<TargetInvocationException>(() => parser.Invoke(null, [null]));
        var invalidFailure = Assert.Throws<TargetInvocationException>(() =>
            parser.Invoke(null, [Expression.Constant("invalid")]));

        Assert.Multiple(() =>
        {
            Assert.That(ExpressionHelper.GetMemberType(member), Is.EqualTo(typeof(TestEntityForExpression)));
            Assert.That(ExpressionHelper.GetMemberType(method), Is.EqualTo(typeof(TestEntityForExpression)));
            Assert.That(nullFailure!.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(invalidFailure!.InnerException, Is.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void GetMemberValue_CoversNullPropertyValueAndMissingProperty()
    {
        var entity = new TestEntityForExpression { Name = null! };
        Expression<Func<TestEntityForExpression, object>> property = value => value.Name;
        Expression<Func<TestEntityForExpression, object>> method = value => value.Describe();

        Assert.Multiple(() =>
        {
            Assert.That(ExpressionHelper.GetMemberValue(property, entity), Is.Null);
            Assert.That(ExpressionHelper.GetMemberValue(method, entity), Is.Null);
        });
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

    public string Describe() => Name;

    public void Clear() => Name = string.Empty;
}

