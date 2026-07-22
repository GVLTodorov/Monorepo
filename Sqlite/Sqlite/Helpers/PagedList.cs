using System.Collections.Generic;
using System.Linq;

namespace Sqlite.Helpers;

/// <summary>
/// Represents the default implementation of the <see cref="IPagedList{T}" /> interface
/// </summary>
/// <typeparam name="T">The type of the data to page</typeparam>
public class PagedList<T> : IPagedList<T>
{
    /// <inheritdoc />
    public int PageIndex { get; set; }

    /// <inheritdoc />
    public int PageSize { get; set; }

    /// <inheritdoc />
    public long TotalCount { get; set; }

    /// <inheritdoc />
    public long TotalPages { get; set; }

    /// <inheritdoc />
    public bool HasPreviousPage => PageIndex > 1;

    /// <inheritdoc />
    public bool HasNextPage => PageIndex < TotalPages;

    /// <inheritdoc />
    public ICollection<T> Items { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PagedList{T}" /> class.
    /// </summary>
    public PagedList(ICollection<T> source, int pageIndex, int pageSize, long totalPages, long totalCount)
    {
        PageIndex = pageIndex;
        PageSize = pageSize;
        TotalCount = totalCount;
        TotalPages = totalPages;
        Items = source.ToList();
    }
}

/// <summary>
/// Provides the interface(s) for paged list of any type
/// </summary>
/// <typeparam name="T">The type for paging.</typeparam>
public interface IPagedList<T>
{
    /// <summary>Gets the Page Index</summary>
    int PageIndex { get; }

    /// <summary>Gets the Page Size</summary>
    int PageSize { get; }

    /// <summary>Gets the Total Count of the list of <typeparamref name="T" /></summary>
    long TotalCount { get; }

    /// <summary>Gets the Total Pages</summary>
    long TotalPages { get; }

    /// <summary>Gets a value indicating whether the paged list has a previous page</summary>
    bool HasPreviousPage { get; }

    /// <summary>Gets a value indicating whether the paged list has a next page</summary>
    bool HasNextPage { get; }

    /// <summary>Gets the Current Page Items</summary>
    ICollection<T> Items { get; }
}
