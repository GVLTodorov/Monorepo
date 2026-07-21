using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mongo.Helpers;
using MongoDB.Driver;
using SortDirection = Mongo.Helpers.SortDirection;

namespace Mongo;
#nullable enable

/// <summary>
/// Empty Interface: Mongo Repository
/// </summary>
public interface IMongoRepository { }

/// <summary>
/// Interface: Repository
/// </summary>
/// <typeparam name="T">Entity Type</typeparam>
public interface IMongoRepository<T> : IMongoRepository where T : class, IMongoEntity
{
    /// <summary>
    /// Creates an index on the specified field
    /// </summary>
    /// <param name="field">Index Field</param>
    /// <returns><see cref="Task"/></returns>
    Task CreateIndexAsync(Expression<Func<T, object>> field);

    /// <summary>
    /// Creates an index on the specified field with unique and expires after values
    /// </summary>
    /// <param name="field">Index Field</param>
    /// <param name="expiresAfter">Use only on DateTime fields without string representation.
    /// Filter to remove entity which index time field passed. TimeSpan.FromSeconds(0) = immediately</param>
    /// <returns><see cref="Task"/></returns>
    Task CreateIndexAsync(Expression<Func<T, object>> field, TimeSpan expiresAfter);

    /// <summary>
    /// Creates an index on the specified fields
    /// </summary>
    /// <param name="fields">Index Fields</param>
    /// <param name="filter">Optional filter to make this a Partial Index</param>
    /// <param name="unique">Indicates if this is a unique index</param>
    /// <returns></returns>
    Task CreateIndexAsync(IEnumerable<Expression<Func<T, object>>> fields, Expression<Func<T, bool>>? filter = null, bool unique = false);

    /// <summary>
    /// Creates an index on the specified fields
    /// </summary>
    /// <param name="fields">Index Fields with sorting direction</param>
    /// <param name="filter">Optional filter to make this a Partial Index</param>
    /// <param name="unique">Indicates if this is a unique index</param>
    /// <returns></returns>
    Task CreateIndexAsync(
        IEnumerable<(Expression<Func<T, object>> PropertyExpression, SortDirection Direction)> fields,
        Expression<Func<T, bool>>? filter = null,
        bool unique = false);

    /// <summary>
    /// Removes an index on the specified field
    /// </summary>
    /// <param name="field">Index Field</param>
    /// <returns><see cref="Task"/></returns>
    Task RemoveIndexAsync(Expression<Func<T, object>> field);

    /// <summary>
    /// Removes an index on the specified fields
    /// </summary>
    /// <param name="fields">Index Fields</param>
    /// <param name="filter"></param>
    /// <param name="unique">Indicates if this is a unique index</param>
    /// <returns><see cref="Task"/></returns>
    Task RemoveIndexAsync(IEnumerable<Expression<Func<T, object>>> fields, Expression<Func<T, bool>>? filter = null, bool unique = false);

    /// <summary>
    /// Inserts an entity
    /// </summary>
    /// <param name="entity">Entity to Insert</param>
    /// <returns><see cref="Task"/></returns>
    Task InsertAsync(T entity);

    /// <summary>
    /// Inserts a collection of entities
    /// </summary>
    /// <param name="entities">Entities to Insert</param>
    /// <returns><see cref="Task"/></returns>
    Task InsertAsync(ICollection<T> entities);

    /// <summary>
    /// Update an entity
    /// </summary>
    /// <param name="entity">Entity to Update</param>
    /// <returns><see cref="Task"/></returns>
    Task UpdateAsync(T entity);

    /// <summary>
    /// Update collection of entities with the updatedKeyValues
    /// </summary>
    /// <param name="entities">Entities to updated</param>
    /// <param name="updatedKeyValues">Key Value pairs that will be applied to all entities</param>
    /// <returns></returns>
    Task UpdateManyAsync(ICollection<T>? entities, Dictionary<string, object> updatedKeyValues);

    /// <summary>
    /// To be used to remove values from arrays
    /// Finds a match between the field definition and the value and removes it from the array
    /// </summary>
    /// <param name="field"> Targeted Field (array) </param>
    /// <param name="value"> Value to be removed from array</param>
    /// <param name="entitiesToBeUpdated"> Optional, if passed, only update entitiesToBeUpdated in db else find and update every collection </param>
    /// <param name="fieldExistsFilter">Optional, check if field exists in document to be updated</param>
    /// <returns></returns>
    Task PullAsync(FieldDefinition<T> field, 
        object value,
        ICollection<T>? entitiesToBeUpdated = null, 
        Expression<Func<T, object>>? fieldExistsFilter = null);

    /// <summary>
    /// Removes an item from an array that matches the field predicate.
    /// </summary>
    /// <typeparam name="TItem">The type of the field within the array.</typeparam>
    /// <param name="field">The array to remove from.</param>
    /// <param name="fieldFilter">A predicate to match items in the array.</param>
    /// <param name="documentPredicate">A predicate to filter down the number of documents to run the query on.</param>
    /// <param name="includeDeleted">Whether to include deleted items in the query.</param>
    /// <returns></returns>
    Task PullAsync<TItem>(Expression<Func<T, IEnumerable<TItem>>> field, Expression<Func<TItem, bool>>? fieldFilter = null, Expression<Func<T, bool>>? documentPredicate = null, bool includeDeleted = false);

    /// <summary>
    /// Deletes an entity by ID
    /// </summary>
    /// <param name="id">Entity ID</param>
    /// <param name="hardDelete">Forces deletion if set to true</param>
    /// <returns><see cref="Task"/></returns>
    Task DeleteByIdAsync(string id, bool hardDelete = false);

    /// <summary>
    /// Deletes an entity
    /// </summary>
    /// <param name="entity">Entity to Delete</param>
    /// <param name="hardDelete">Forces deletion if set to true</param>
    /// <returns><see cref="Task"/></returns>
    Task DeleteAsync(T entity, bool hardDelete = false);

    /// <summary>
    /// Deletes an entity based on a predicate
    /// </summary>
    /// <param name="predicate">Query Predicate</param>
    /// <param name="hardDelete">Forces deletion if set to true</param>
    /// <returns><see cref="Task"/></returns>
    Task DeleteOneAsync(Expression<Func<T, bool>> predicate, bool hardDelete = false);

    /// <summary>
    /// Deletes entities based on a predicate
    /// </summary>
    /// <param name="predicate">Query Predicate</param>
    /// <param name="hardDelete">Forces deletion if set to true</param>
    /// <returns><see cref="Task"/></returns>
    Task<List<T>> DeleteManyAsync(Expression<Func<T, bool>> predicate, bool hardDelete = false);

    /// <summary>
    /// Deletes a collection of entities
    /// </summary>
    /// <param name="items">Entity Collection</param>
    /// <param name="hardDelete">Forces deletion if set to true</param>
    /// <returns><see cref="Task"/></returns>
    Task DeleteManyAsync(ICollection<T> items, bool hardDelete = false);

    /// <summary>
    /// Restores an entity (Resets the IsDeleted &amp; Deleted Date values for soft deletes)
    /// </summary>
    /// <param name="entity">Entity</param>
    /// <returns><see cref="Task"/></returns>
    Task RestoreAsync(T entity);

    /// <summary>
    /// Restores an entity (Resets the IsDeleted &amp; Deleted Date values for soft deletes)
    /// </summary>
    /// <param name="id">Entity Id</param>
    /// <returns><see cref="Task"/></returns>
    Task RestoreAsync(string id);

    /// <summary>
    /// Gets a count of entities based on a predicate
    /// </summary>
    /// <param name="predicate">Query Predicate</param>
    /// <returns><see cref="Task"/></returns>
    Task<long> CountAsync(Expression<Func<T, bool>> predicate);

    /// <summary>
    /// Gets a value indicating whether a record exists based on a predicate
    /// </summary>
    /// <param name="predicate">Query Predicate</param>
    /// <returns>True if the collection matches a record</returns>
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate);

    /// <summary>
    /// Gets an entity by ID
    /// </summary>
    /// <param name="id">Entity ID</param>
    /// <returns><typeparamref name="T"/></returns>
    Task<T?> GetByIdAsync(string id);

    /// <summary>
    /// Gets a list of all documents in the collection
    /// </summary>
    /// <remarks>Exercise caution with this method - it will return ALL records from a collection which can 
    /// impact performance. Consider using the <see cref="o:GetPagedListAsync"/> 
    /// method instead</remarks>
    /// <param name="predicate">Query Predicate</param>
    /// <param name="orderBy">Order By Expression</param>
    /// <param name="sortDirection">Sort Direction</param>
    /// <param name="includeDeletes">Include IsDeleted = true</param>
    /// <returns><see cref="List{T}"/></returns>
    Task<List<T>> GetAllAsync(
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending, 
        bool includeDeletes = false);

    /// <summary>
    /// Gets a list of all documents in the collection with projection
    /// </summary>
    /// <typeparam name="TProjection"></typeparam>
    /// <param name="projection"></param>
    /// <param name="predicate"></param>
    /// <param name="includeDeletes"></param>
    /// <returns></returns>
    Task<List<TProjection>> GetAllAsync<TProjection>(
        ProjectionDefinition<T, TProjection> projection,
        Expression<Func<T, bool>>? predicate = null,
        bool includeDeletes = false);

    /// <summary>
    /// Gets a list of all documents in the collection
    /// </summary>
    /// <remarks>Exercise caution with this method - it will return ALL records from a collection which can impact performance</remarks>
    /// <param name="predicate">Query Predicate</param>
    /// <param name="orderBy">Order By Predicate</param>
    /// <param name="pageIndex">Page Index</param>
    /// <param name="pageSize">Page Size</param>
    /// <param name="sortDirection">Sort Direction</param>
    /// <param name="includeDeletes">Include IsDeleted = true</param>
    /// <returns><see cref="List{T}"/></returns>
    Task<IPagedList<T>> GetPagedListAsync(
        int pageIndex = 1, 
        int pageSize = 50, 
        Expression<Func<T, bool>>? predicate = null, 
        Expression<Func<T, object>>? orderBy = null, 
        SortDirection sortDirection = SortDirection.Ascending, 
        bool includeDeletes = false);

    /// <summary>
    /// Get a page of documents from the collection
    /// </summary>
    /// <param name="pageIndex"></param>
    /// <param name="pageSize"></param>
    /// <param name="predicate"></param>
    /// <param name="orderBy"></param>
    /// <param name="includeDeletes"></param>
    /// <returns></returns>
    Task<IPagedList<T>> GetPagedListAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<T, bool>>? predicate,
        SortExpression<T>[] orderBy,
        bool includeDeletes = false);

    /// <summary>
    /// Gets the FirstOrDefault entity matching the predicate
    /// </summary>
    /// <param name="predicate">Query Predicate</param>
    /// <param name="orderBy">Order By Predicate</param>
    /// <param name="sortDirection">Sort Direction</param>
    /// <param name="includeDeleted">Include Deleted Records</param>
    /// <returns><typeparamref name="T"/></returns>
    Task<T?> GetFirstOrDefaultAsync(
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending, 
        bool includeDeleted = false);

    /// <summary>
    /// Gets the SingleOrDefault entity matching the predicate
    /// </summary>
    /// <param name="predicate">Query Predicate</param>
    /// <returns><typeparamref name="T"/></returns>
    Task<T?> GetSingleOrDefaultAsync(Expression<Func<T, bool>> predicate);
}
