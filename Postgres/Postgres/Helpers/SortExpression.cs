using System;
using System.Linq.Expressions;

namespace Postgres.Helpers;

/// <summary>
/// Defines a property and direction to sort by
/// </summary>
public class SortExpression<T>
{
    /// <summary>Gets or sets the sort direction</summary>
    public SortDirection SortDirection { get; set; }

    /// <summary>Gets or sets an expression for the property to sort by</summary>
    public required Expression<Func<T, object>> Expression { get; set; }
}

/// <summary>
/// Sort Direction for Ordering
/// </summary>
public enum SortDirection
{
    /// <summary>Ascending Order</summary>
    Ascending,

    /// <summary>Descending Order</summary>
    Descending
}
