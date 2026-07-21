using System.Linq.Expressions;
using System.Reflection;
using Postgres.Helpers;

namespace Postgres.Tests;

[TestFixture]
public sealed class ExpressionHelperTests
{
    [Test]
    public void AndAlso_IndependentParameters_RequiresBothPredicates()
    {
        Expression<Func<ExpressionSubject, bool>> adult = person => person.Age >= 18;
        Expression<Func<ExpressionSubject, bool>> namedAda = candidate => candidate.Name == "Ada";

        var predicate = adult.AndAlso(namedAda).Compile();

        Assert.Multiple(() =>
        {
            Assert.That(predicate(new ExpressionSubject { Name = "Ada", Age = 20 }), Is.True);
            Assert.That(predicate(new ExpressionSubject { Name = "Grace", Age = 20 }), Is.False);
            Assert.That(predicate(new ExpressionSubject { Name = "Ada", Age = 17 }), Is.False);
        });
    }

    [Test]
    public void AndAlso_NullExpression_ThrowsNullReferenceException()
    {
        Expression<Func<ExpressionSubject, bool>> valid = value => value.Age > 0;

        Assert.Multiple(() =>
        {
            Assert.That(() => ((Expression<Func<ExpressionSubject, bool>>)null!).AndAlso(valid),
                Throws.TypeOf<NullReferenceException>());
            Assert.That(() => valid.AndAlso(null!), Throws.TypeOf<NullReferenceException>());
        });
    }

    [Test]
    public void MemberDiscovery_PropertiesMethodsAndActions_ReturnsExactNamesAndTypes()
    {
        Expression<Func<ExpressionSubject, object>> referenceProperty = value => value.Name;
        Expression<Func<ExpressionSubject, object>> valueProperty = value => value.Age;
        Expression<Func<ExpressionSubject, object>> method = value => value.Name.ToUpperInvariant();
        Expression<Func<ExpressionSubject, object>> boxedValueMethod = value => value.GetAge();
        Expression<Action<ExpressionSubject>> action = value => value.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(ExpressionHelper.GetMemberName(referenceProperty), Is.EqualTo(nameof(ExpressionSubject.Name)));
            Assert.That(ExpressionHelper.GetMemberName(valueProperty), Is.EqualTo(nameof(ExpressionSubject.Age)));
            Assert.That(ExpressionHelper.GetMemberName(method), Is.EqualTo(nameof(string.ToUpperInvariant)));
            Assert.That(ExpressionHelper.GetMemberName(boxedValueMethod), Is.EqualTo(nameof(ExpressionSubject.GetAge)));
            Assert.That(ExpressionHelper.GetMemberName(action), Is.EqualTo(nameof(ExpressionSubject.Reset)));
            Assert.That(
                ExpressionHelper.GetMemberName<ExpressionSubject, string>(value => value.Name),
                Is.EqualTo(nameof(ExpressionSubject.Name)));
            Assert.That(ExpressionHelper.GetMemberType(referenceProperty), Is.EqualTo(typeof(ExpressionSubject)));
            Assert.That(ExpressionHelper.GetMemberType(valueProperty), Is.EqualTo(typeof(ExpressionSubject)));
            Assert.That(ExpressionHelper.GetMemberType(method), Is.EqualTo(typeof(string)));
            Assert.That(ExpressionHelper.GetMemberType(boxedValueMethod), Is.EqualTo(typeof(ExpressionSubject)));
        });
    }

    [Test]
    public void GetMemberNames_MultipleExpressions_PreservesOrder()
    {
        var names = ExpressionHelper.GetMemberNames<ExpressionSubject>(
            value => value.Age,
            value => value.Name);
        var empty = ExpressionHelper.GetMemberNames<ExpressionSubject>();

        Assert.Multiple(() =>
        {
            Assert.That(names, Is.EqualTo(new[] { nameof(ExpressionSubject.Age), nameof(ExpressionSubject.Name) }));
            Assert.That(empty, Is.Empty);
        });
    }

    [Test]
    public void GetMemberValue_ValueNullMissingOrPresent_ReturnsExpectedValue()
    {
        var subject = new ExpressionSubject { Name = "Ada", Age = 37 };

        Assert.Multiple(() =>
        {
            Assert.That(ExpressionHelper.GetMemberValue<ExpressionSubject>(value => value.Name, subject), Is.EqualTo("Ada"));
            Assert.That(ExpressionHelper.GetMemberValue<ExpressionSubject>(value => value.Age, subject), Is.EqualTo(37));
            Assert.That(ExpressionHelper.GetMemberValue<ExpressionSubject>(value => value.Name, null!), Is.Null);
            Assert.That(ExpressionHelper.GetMemberValue<ExpressionSubject>(value => value.Name.Length, subject), Is.Null);
        });
    }

    [Test]
    public void MemberDiscovery_InvalidExpression_ThrowsArgumentException()
    {
        Expression<Func<ExpressionSubject, object>> expression = value => new { value.Name, value.Age };

        Assert.Multiple(() =>
        {
            Assert.That(() => ExpressionHelper.GetMemberName(expression),
                Throws.ArgumentException.With.Message.EqualTo("Invalid expression"));
            Assert.That(() => ExpressionHelper.GetMemberType(expression),
                Throws.ArgumentException.With.Message.EqualTo("Invalid expression"));
        });
    }

    [Test]
    public void PrivateMemberDiscovery_NullExpression_ThrowsDocumentedArgumentException()
    {
        var methods = typeof(ExpressionHelper).GetMethods(BindingFlags.NonPublic | BindingFlags.Static);
        var getName = methods.Single(method => method.Name == "GetMemberName" && method.GetParameters()[0].ParameterType == typeof(Expression));
        var getType = methods.Single(method => method.Name == "GetMemberType");

        var nameException = Assert.Throws<TargetInvocationException>(() => getName.Invoke(null, [null]));
        var typeException = Assert.Throws<TargetInvocationException>(() => getType.Invoke(null, [null]));

        Assert.Multiple(() =>
        {
            Assert.That(nameException!.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(nameException.InnerException!.Message, Does.StartWith("The expression cannot be null"));
            Assert.That(typeException!.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(typeException.InnerException!.Message, Does.StartWith("The expression cannot be null"));
        });
    }

    private sealed class ExpressionSubject
    {
        public string Name { get; set; } = string.Empty;

        public int Age { get; set; }

        public void Reset()
        {
            Age = 0;
        }

        public int GetAge() => Age;
    }
}
