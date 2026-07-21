using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Mongo.Helpers;


/// <summary>
/// Extension methods for <see cref="Expression{TDelegate}"/>
/// </summary>
public static class ExpressionHelper
{
    private const string EXPRESSION_CANNOT_BE_NULL_MESSAGE = "The expression cannot be null";
    private const string INVALID_EXPRESSION_MESSAGE = "Invalid expression";

    /// <summary>
    /// Combines two expressions with the AND operator
    /// </summary>
    /// <typeparam name="T">Expression Type</typeparam>
    /// <param name="expr1">First Expression</param>
    /// <param name="expr2">Second Expression</param>
    /// <returns>Combined expression</returns>
    public static Expression<Func<T, bool>> AndAlso<T>(this Expression<Func<T, bool>> expr1, Expression<Func<T, bool>> expr2)
    {
        var parameter = Expression.Parameter(typeof(T));

        var leftVisitor = new ReplaceExpressionVisitor(expr1.Parameters[0], parameter);
        var left = leftVisitor.Visit(expr1.Body);

        var rightVisitor = new ReplaceExpressionVisitor(expr2.Parameters[0], parameter);
        var right = rightVisitor.Visit(expr2.Body);

        if (left == null)
        {
            throw new ArgumentNullException(nameof(left));
        }

        if (right == null)
        {
            throw new ArgumentNullException(nameof(right));
        }

        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left, right), parameter);
    }

    /// <summary>
    /// Gets the Member Name for the given expression
    /// </summary>
    /// <typeparam name="T">Object Type</typeparam>
    /// <param name="expression">Expression</param>
    /// <param name="value">Object Instance</param>
    /// <returns>Value for the property</returns>
    public static object? GetMemberValue<T>(Expression<Func<T, object>> expression, T value)
    {
        if (value == null)
        {
            return null;
        }

        var memberType = value.GetType();
        var memberName = GetMemberName(expression);
        var property = memberType.GetProperty(memberName);

        return property?.GetValue(value) ?? null;
    }

    /// <summary>
    /// Gets the Member Name for the given expression
    /// </summary>
    /// <typeparam name="T">Object Type</typeparam>
    /// <param name="expression">Expression</param>
    /// <returns>Member Name</returns>
    public static string GetMemberName<T>(Expression<Func<T, object>> expression) => GetMemberName(expression.Body);

    /// <summary>
    /// Gets the Member Name for the given expression
    /// </summary>
    /// <typeparam name="T">Object Type</typeparam>
    /// <param name="expression">Expression</param>
    /// <returns>Member Name</returns>
    public static Type? GetMemberType<T>(Expression<Func<T, object>> expression) => GetMemberType(expression.Body);

    /// <summary>
    /// Gets the Member Names for the given expression
    /// </summary>
    /// <typeparam name="T">Object Type</typeparam>
    /// <param name="expressions">Expressions</param>
    /// <returns>List of Member Names</returns>
    public static List<string> GetMemberNames<T>(params Expression<Func<T, object>>[] expressions) => expressions.Select(exp => GetMemberName(exp.Body)).ToList();

    /// <summary>
    /// Gets the Member Name for the given expression
    /// </summary>
    /// <typeparam name="T">Object Type</typeparam>
    /// <param name="expression">Expression</param>
    /// <returns>Member Name</returns>
    public static string GetMemberName<T>(Expression<Action<T>> expression) => GetMemberName(expression.Body);

    /// <summary>
    /// Gets the Member Name for the given expression
    /// </summary>
    /// <returns>Member Name</returns>
    public static string GetMemberName<TEntity, TTarget>(Expression<Func<TEntity, TTarget>> targetEntityField) where TEntity : class => GetMemberName(targetEntityField.Body);

    #region Private Helpers

    private static string GetMemberName(Expression expression)
    {
        switch (expression)
        {
            case null:
                throw new ArgumentException(EXPRESSION_CANNOT_BE_NULL_MESSAGE);

            // Reference type property or field
            case MemberExpression memberExpression:
                return memberExpression.Member.Name;

            // Reference type method
            case MethodCallExpression methodCallExpression:
                return methodCallExpression.Method.Name;

            // Property, field of method returning value type
            case UnaryExpression unaryExpression:
                return GetMemberName(unaryExpression);
        }

        throw new ArgumentException(INVALID_EXPRESSION_MESSAGE);
    }

    private static string GetMemberName(UnaryExpression unaryExpression)
    {
        if (!(unaryExpression.Operand is MethodCallExpression methodExpression))
        {
            return ((MemberExpression)unaryExpression.Operand).Member.Name;
        }

        return methodExpression.Method.Name;

    }

    private static Type? GetMemberType(Expression expression)
    {
        while (true)
        {
            switch (expression)
            {
                case null:
                    throw new ArgumentException(EXPRESSION_CANNOT_BE_NULL_MESSAGE);

                // Reference type property or field
                case MemberExpression memberExpression:
                    return memberExpression.Member.DeclaringType;

                // Reference type method
                case MethodCallExpression methodCallExpression:
                    return methodCallExpression.Method.DeclaringType;

                // Property, field of method returning value type
                case UnaryExpression unaryExpression:
                    expression = unaryExpression;
                    continue;
            }

            throw new ArgumentException(INVALID_EXPRESSION_MESSAGE);
        }
    }

    #endregion
}

internal class ReplaceExpressionVisitor : ExpressionVisitor
{
    private readonly Expression newValue;
    private readonly Expression oldValue;

    public ReplaceExpressionVisitor(Expression oldValue, Expression newValue)
    {
        this.oldValue = oldValue;
        this.newValue = newValue;
    }

    public override Expression? Visit(Expression? node) => node == this.oldValue ? this.newValue : base.Visit(node);
}