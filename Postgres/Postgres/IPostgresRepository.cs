using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Postgres.Helpers;
using SortDirection = Postgres.Helpers.SortDirection;

namespace Postgres;
#nullable enable

/// <summary>
/// Empty Interface: Postgres Repository
/// </summary>
public interface IPostgresRepository { }

/// <summary>
/// Interface: Repository for PostgreSQL
/// </summary>
/// <typeparam name="T">Entity Type</typeparam>
public interface IPostgresRepository<T> : IPostgresRepository where T : class, IPostgresEntity
{
    /// <summary>
    /// Creates an index on the specified field
    /// </summary>
    Task CreateIndexAsync(Expression<Func<T, object>> field);

    /// <summary>
    /// Creates an index on the specified field. TTL/expiry not supported in PostgreSQL; expiresAfter is ignored.
    /// </summary>
    Task CreateIndexAsync(Expression<Func<T, object>> field, TimeSpan expiresAfter);

    /// <summary>
    /// Creates an index on the specified fields (composite index)
    /// </summary>
    /// <param name="fields">Index fields</param>
    /// <param name="filter">Optional filter for a partial index (WHERE clause)</param>
    /// <param name="unique">Whether the index is unique</param>
    Task CreateIndexAsync(IEnumerable<Expression<Func<T, object>>> fields, Expression<Func<T, bool>>? filter = null, bool unique = false);

    /// <summary>
    /// Creates an index on the specified fields with sort direction per field
    /// </summary>
    Task CreateIndexAsync(
        IEnumerable<(Expression<Func<T, object>> PropertyExpression, SortDirection Direction)> fields,
        Expression<Func<T, bool>>? filter = null,
        bool unique = false);

    /// <summary>
    /// Removes an index on the specified field
    /// </summary>
    Task RemoveIndexAsync(Expression<Func<T, object>> field);

    /// <summary>
    /// Removes an index on the specified fields
    /// </summary>
    Task RemoveIndexAsync(IEnumerable<Expression<Func<T, object>>> fields, Expression<Func<T, bool>>? filter = null, bool unique = false);

    /// <summary>Inserts an entity. Ensures the mapped table exists first.</summary>
    Task InsertAsync(T entity);

    /// <summary>Inserts a collection of entities. Ensures the mapped table exists first.</summary>
    Task InsertAsync(ICollection<T> entities);

    /// <summary>Updates an entity. Ensures the mapped table exists first.</summary>
    Task UpdateAsync(T entity);

    /// <summary>
    /// Updates many entities with the same key-value pairs. Ensures the mapped table exists first.
    /// </summary>
    Task UpdateManyAsync(ICollection<T>? entities, Dictionary<string, object> updatedKeyValues);

    /// <summary>
    /// Removes items from a collection property that match the given predicate (in-memory update for EF).
    /// Ensures the mapped table exists first.
    /// </summary>
    Task PullAsync<TItem>(
        Expression<Func<T, IEnumerable<TItem>>> field,
        Expression<Func<TItem, bool>>? fieldFilter = null,
        Expression<Func<T, bool>>? documentPredicate = null,
        bool includeDeleted = false);

    /// <summary>Deletes an entity by ID. Ensures the mapped table exists first.</summary>
    /// <param name="id">Entity ID</param>
    /// <param name="hardDelete">If true, physically deletes; otherwise soft delete</param>
    Task DeleteByIdAsync(string id, bool hardDelete = false);

    /// <summary>Deletes an entity. Ensures the mapped table exists first.</summary>
    Task DeleteAsync(T entity, bool hardDelete = false);

    /// <summary>Deletes one entity matching the predicate. Ensures the mapped table exists first.</summary>
    Task DeleteOneAsync(Expression<Func<T, bool>> predicate, bool hardDelete = false);

    /// <summary>Deletes entities matching the predicate. Returns the list of deleted entities. Ensures the mapped table exists first.</summary>
    Task<List<T>> DeleteManyAsync(Expression<Func<T, bool>> predicate, bool hardDelete = false);

    /// <summary>Deletes a collection of entities. Ensures the mapped table exists first.</summary>
    Task DeleteManyAsync(ICollection<T> items, bool hardDelete = false);

    /// <summary>
    /// Removes all rows from the mapped table via <c>TRUNCATE</c> (faster than bulk delete; bypasses soft-delete and EF change tracking for existing rows).
    /// Ensures the mapped table exists first.
    /// </summary>
    /// <param name="restartIdentity">When true, resets any <c>IDENTITY</c> sequences on the table.</param>
    /// <param name="cascade">When true, truncates tables that reference this one via foreign keys (PostgreSQL <c>CASCADE</c>).</param>
    Task TruncateTableAsync(bool restartIdentity = false, bool cascade = false);

    /// <summary>Restores a soft-deleted entity. Ensures the mapped table exists first.</summary>
    Task RestoreAsync(T entity);

    /// <summary>Restores a soft-deleted entity by ID. Ensures the mapped table exists first.</summary>
    Task RestoreAsync(string id);

    /// <summary>Gets the count of entities matching the predicate</summary>
    Task<long> CountAsync(Expression<Func<T, bool>> predicate);

    /// <summary>Returns true if any entity matches the predicate</summary>
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate);

    /// <summary>Returns true if the mapped table for <typeparamref name="T"/> exists in the database (current schema).</summary>
    Task<bool> TableExistsAsync();

    /// <summary>Returns true if a table with the given name exists. Names are compared case-insensitively (PostgreSQL unquoted identifiers).</summary>
    /// <param name="tableName">Table name (e.g. from <see cref="PostgresEntityExtensions.GetTableName{T}"/>).</param>
    /// <param name="schema">Optional schema; when null, <c>current_schema()</c> is used.</param>
    Task<bool> TableExistsAsync(string tableName, string? schema = null);

    /// <summary>
    /// Ensures the mapped table exists, creating it from the current model when missing.
    /// </summary>
    Task EnsureTableAsync();

    /// <summary>Gets an entity by ID</summary>
    Task<T?> GetByIdAsync(string id);

    /// <summary>
    /// Gets all entities matching the predicate, optionally ordered
    /// </summary>
    Task<List<T>> GetAllAsync(
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeDeletes = false);

    /// <summary>
    /// Gets all entities with a projection
    /// </summary>
    Task<List<TProjection>> GetAllAsync<TProjection>(
        Expression<Func<T, TProjection>> projection,
        Expression<Func<T, bool>>? predicate = null,
        bool includeDeletes = false);

    /// <summary>
    /// Gets a paged list of entities
    /// </summary>
    Task<IPagedList<T>> GetPagedListAsync(
        int pageIndex = 1,
        int pageSize = 50,
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeDeletes = false);

    /// <summary>
    /// Gets a paged list with multiple sort expressions
    /// </summary>
    Task<IPagedList<T>> GetPagedListAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<T, bool>>? predicate,
        SortExpression<T>[] orderBy,
        bool includeDeletes = false);

    /// <summary>Gets the first entity matching the predicate or null</summary>
    Task<T?> GetFirstOrDefaultAsync(
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeDeleted = false);

    /// <summary>Gets the single entity matching the predicate or null</summary>
    Task<T?> GetSingleOrDefaultAsync(Expression<Func<T, bool>> predicate);
}
