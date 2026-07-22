using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Sqlite.Helpers;

/// <summary>
/// Extension methods for <see cref="Expression{TDelegate}"/>
/// </summary>
public static class ExpressionHelper
{
    private const string ExpressionCannotBeNullMessage = "The expression cannot be null";
    private const string InvalidExpressionMessage = "Invalid expression";

    /// <summary>
    /// Combines two expressions with the AND operator
    /// </summary>
    public static Expression<Func<T, bool>> AndAlso<T>(this Expression<Func<T, bool>> expr1, Expression<Func<T, bool>> expr2)
    {
        var parameter = Expression.Parameter(typeof(T));

        var leftVisitor = new ReplaceExpressionVisitor(expr1.Parameters[0], parameter);
        var left = leftVisitor.Visit(expr1.Body);

        var rightVisitor = new ReplaceExpressionVisitor(expr2.Parameters[0], parameter);
        var right = rightVisitor.Visit(expr2.Body);

        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left!, right!), parameter);
    }

    /// <summary>Gets the Member Name for the given expression</summary>
    public static object? GetMemberValue<T>(Expression<Func<T, object>> expression, T value)
    {
        if (value == null) return null;
        var memberName = GetMemberName(expression);
        var property = value.GetType().GetProperty(memberName);
        return property?.GetValue(value);
    }

    /// <summary>Gets the Member Name for the given expression</summary>
    public static string GetMemberName<T>(Expression<Func<T, object>> expression) => GetMemberName(expression.Body);

    /// <summary>Gets the Member Type for the given expression</summary>
    public static Type? GetMemberType<T>(Expression<Func<T, object>> expression) => GetMemberType(expression.Body);

    /// <summary>Gets the Member Names for the given expressions</summary>
    public static List<string> GetMemberNames<T>(params Expression<Func<T, object>>[] expressions) =>
        expressions.Select(exp => GetMemberName(exp.Body)).ToList();

    /// <summary>Gets the Member Name for the given expression</summary>
    public static string GetMemberName<T>(Expression<Action<T>> expression) => GetMemberName(expression.Body);

    /// <summary>Gets the Member Name for the given expression</summary>
    public static string GetMemberName<TEntity, TTarget>(Expression<Func<TEntity, TTarget>> targetEntityField) where TEntity : class =>
        GetMemberName(targetEntityField.Body);

    private static string GetMemberName(Expression expression)
    {
        return expression switch
        {
            null => throw new ArgumentException(ExpressionCannotBeNullMessage),
            MemberExpression memberExpression => memberExpression.Member.Name,
            MethodCallExpression methodCallExpression => methodCallExpression.Method.Name,
            UnaryExpression unaryExpression => GetMemberName(unaryExpression),
            _ => throw new ArgumentException(InvalidExpressionMessage)
        };
    }

    private static string GetMemberName(UnaryExpression unaryExpression)
    {
        return unaryExpression.Operand is MethodCallExpression methodExpression
            ? methodExpression.Method.Name
            : ((MemberExpression)unaryExpression.Operand).Member.Name;
    }

    private static Type? GetMemberType(Expression expression)
    {
        while (true)
        {
            switch (expression)
            {
                case null:
                    throw new ArgumentException(ExpressionCannotBeNullMessage);
                case MemberExpression memberExpression:
                    return memberExpression.Member.DeclaringType;
                case MethodCallExpression methodCallExpression:
                    return methodCallExpression.Method.DeclaringType;
                case UnaryExpression unaryExpression:
                    expression = unaryExpression.Operand;
                    continue;
            }

            throw new ArgumentException(InvalidExpressionMessage);
        }
    }
}

internal class ReplaceExpressionVisitor : ExpressionVisitor
{
    private readonly Expression _newValue;
    private readonly Expression _oldValue;

    public ReplaceExpressionVisitor(Expression oldValue, Expression newValue)
    {
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public override Expression? Visit(Expression? node) => node == _oldValue ? _newValue : base.Visit(node);
}
