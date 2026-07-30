using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace Sqlite;

internal enum SqlDialect
{
    PostgreSql,
    Sqlite
}

internal sealed record SqlParameterValue(string Name, object? Value);

internal sealed class SqliteRepositoryCore<T> where T : class
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);
    private static readonly PropertyInfo[] Properties = typeof(T)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property =>
            property.CanRead &&
            property.CanWrite &&
            property.GetIndexParameters().Length == 0 &&
            property.Name != "IsDeleted")
        .ToArray();
    private static readonly IReadOnlyDictionary<string, PropertyInfo> PropertiesByName =
        Properties.ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);

    private readonly DbConnection _connection;
    private readonly SqlDialect _dialect;
    private readonly string _tableName;
    private readonly Action<string>? _commandObserver;
    private readonly SemaphoreSlim _ensureTableLock = new(1, 1);
    private bool _tableEnsured;

    public SqliteRepositoryCore(
        DbConnection connection,
        SqlDialect dialect,
        string tableName,
        Action<string>? commandObserver = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _dialect = dialect;
        _tableName = string.IsNullOrWhiteSpace(tableName)
            ? throw new ArgumentException("Table name is required.", nameof(tableName))
            : tableName;
        _commandObserver = commandObserver;
    }

    public DbConnection Connection => _connection;

    public string TableName => _tableName;

    public string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"")}\"";

    public string GetMemberName(LambdaExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var body = StripConvert(expression.Body);

        return body is MemberExpression { Expression: ParameterExpression } member
            ? member.Member.Name
            : throw new ArgumentException("Expression must select an entity property.", nameof(expression));
    }

    public async Task CreateIndexAsync(
        IReadOnlyList<(string PropertyName, bool Descending)> fields,
        bool unique)
    {
        if (fields.Count == 0)
        {
            throw new ArgumentException("Index must have at least one field.", nameof(fields));
        }

        await EnsureTableAsync();
        var names = fields.Select(field => field.PropertyName).ToArray();
        foreach (var name in names)
        {
            GetProperty(name);
        }

        var indexName = $"idx_{_tableName}_{string.Join("_", names)}";
        var columns = string.Join(", ", fields.Select(field =>
            $"{QuoteIdentifier(field.PropertyName)} {(field.Descending ? "DESC" : "ASC")}"));
        var sql = $"CREATE {(unique ? "UNIQUE " : "")}INDEX IF NOT EXISTS {QuoteIdentifier(indexName)} " +
                  $"ON {QuoteIdentifier(_tableName)} ({columns});";
        await ExecuteNonQueryAsync(sql);
    }

    public Task RemoveIndexAsync(IReadOnlyList<string> fields)
    {
        if (fields.Count == 0)
        {
            throw new ArgumentException("Index must have at least one field.", nameof(fields));
        }

        foreach (var name in fields)
        {
            GetProperty(name);
        }

        var indexName = $"idx_{_tableName}_{string.Join("_", fields)}";

        return ExecuteNonQueryAsync($"DROP INDEX IF EXISTS {QuoteIdentifier(indexName)};");
    }

    public async Task<List<T>> GetAllAsync(
        Expression<Func<T, bool>>? predicate,
        string? orderBy,
        bool descending,
        bool includeDeleted,
        int? offset = null,
        int? limit = null)
    {
        await EnsureTableAsync();
        var (where, parameters) = BuildWhere(predicate, includeDeleted);
        var ordering = QuoteIdentifier(orderBy ?? "CreatedDateTime");
        var sql = $"SELECT {SelectColumns()} FROM {QuoteIdentifier(_tableName)}{where} " +
                  $"ORDER BY {ordering} {(descending ? "DESC" : "ASC")}";

        if (limit is not null)
        {
            sql += " LIMIT @__limit";
            parameters.Add(new SqlParameterValue("@__limit", limit.Value));
        }

        if (offset is not null)
        {
            sql += " OFFSET @__offset";
            parameters.Add(new SqlParameterValue("@__offset", offset.Value));
        }

        var entities = await QueryAsync(sql, parameters);

        return entities;
    }

    public async Task<List<T>> GetAllAsync(
        Expression<Func<T, bool>>? predicate,
        IReadOnlyList<(string PropertyName, bool Descending)> orderBy,
        bool includeDeleted,
        int? offset = null,
        int? limit = null)
    {
        await EnsureTableAsync();
        var (where, parameters) = BuildWhere(predicate, includeDeleted);
        var ordering = orderBy.Count == 0
            ? QuoteIdentifier("CreatedDateTime") + " ASC"
            : string.Join(", ", orderBy.Select(item =>
                $"{QuoteIdentifier(item.PropertyName)} {(item.Descending ? "DESC" : "ASC")}"));
        var sql = $"SELECT {SelectColumns()} FROM {QuoteIdentifier(_tableName)}{where} ORDER BY {ordering}";

        if (limit is not null)
        {
            sql += " LIMIT @__limit";
            parameters.Add(new SqlParameterValue("@__limit", limit.Value));
        }

        if (offset is not null)
        {
            sql += " OFFSET @__offset";
            parameters.Add(new SqlParameterValue("@__offset", offset.Value));
        }

        var entities = await QueryAsync(sql, parameters);

        return entities;
    }

    public async Task<long> CountAsync(
        Expression<Func<T, bool>> predicate,
        bool includeDeleted)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        await EnsureTableAsync();
        var (where, parameters) = BuildWhere(predicate, includeDeleted);
        var scalar = await ExecuteScalarAsync(
            $"SELECT COUNT(*) FROM {QuoteIdentifier(_tableName)}{where};",
            parameters);

        return Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
    }

    public async Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate,
        bool includeDeleted)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        await EnsureTableAsync();
        var (where, parameters) = BuildWhere(predicate, includeDeleted);
        var scalar = await ExecuteScalarAsync(
            $"SELECT EXISTS(SELECT 1 FROM {QuoteIdentifier(_tableName)}{where});",
            parameters);

        return Convert.ToBoolean(scalar, CultureInfo.InvariantCulture);
    }

    public async Task<bool> TableExistsAsync(string tableName, string? schema = null)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name is required.", nameof(tableName));
        }

        if (_dialect == SqlDialect.Sqlite)
        {
            const string sqliteSql =
                "SELECT EXISTS(SELECT 1 FROM sqlite_master " +
                "WHERE type = 'table' AND lower(name) = lower(@name));";
            var value = await ExecuteScalarAsync(
                sqliteSql,
                [new SqlParameterValue("@name", tableName)]);

            return Convert.ToInt64(value, CultureInfo.InvariantCulture) == 1;
        }

        var postgresSql = schema is null
            ? """
              SELECT EXISTS(
                  SELECT 1 FROM information_schema.tables
                  WHERE lower(table_name) = lower(@name)
                    AND table_schema = current_schema());
              """
            : """
              SELECT EXISTS(
                  SELECT 1 FROM information_schema.tables
                  WHERE lower(table_name) = lower(@name)
                    AND lower(table_schema) = lower(@schema));
              """;
        var parameters = new List<SqlParameterValue> { new("@name", tableName) };
        if (schema is not null)
        {
            parameters.Add(new SqlParameterValue("@schema", schema));
        }

        var scalar = await ExecuteScalarAsync(postgresSql, parameters);

        return Convert.ToBoolean(scalar, CultureInfo.InvariantCulture);
    }

    public async Task EnsureTableAsync()
    {
        if (_tableEnsured)
        {
            return;
        }

        await _ensureTableLock.WaitAsync();
        try
        {
            if (_tableEnsured)
            {
                return;
            }

            if (!await TableExistsAsync(_tableName))
            {
                await ExecuteNonQueryAsync(BuildCreateTableSql());
            }

            _tableEnsured = true;
        }
        finally
        {
            _ensureTableLock.Release();
        }
    }

    public async Task InsertAsync(T entity, DbTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await EnsureTableAsync();
        var columns = string.Join(", ", Properties.Select(property => QuoteIdentifier(property.Name)));
        var placeholders = string.Join(", ", Properties.Select((_, index) => $"@p{index}"));
        var parameters = Properties.Select((property, index) =>
            new SqlParameterValue($"@p{index}", property.GetValue(entity))).ToList();
        await ExecuteNonQueryAsync(
            $"INSERT INTO {QuoteIdentifier(_tableName)} ({columns}) VALUES ({placeholders});",
            parameters,
            transaction);
    }

    public async Task InsertManyAsync(ICollection<T> entities)
    {
        if (entities.Count == 0)
        {
            return;
        }

        await EnsureTableAsync();
        await using var transaction = await BeginTransactionAsync();
        try
        {
            foreach (var entity in entities)
            {
                await InsertAsync(entity, transaction);
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task UpdateAsync(T entity, DbTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await EnsureTableAsync();
        var mutable = Properties.Where(property => !property.Name.Equals("Id", StringComparison.Ordinal)).ToArray();
        var assignments = string.Join(", ", mutable.Select((property, index) =>
            $"{QuoteIdentifier(property.Name)} = @p{index}"));
        var parameters = mutable.Select((property, index) =>
            new SqlParameterValue($"@p{index}", property.GetValue(entity))).ToList();
        parameters.Add(new SqlParameterValue("@id", GetProperty("Id").GetValue(entity)));
        await ExecuteNonQueryAsync(
            $"UPDATE {QuoteIdentifier(_tableName)} SET {assignments} WHERE {QuoteIdentifier("Id")} = @id;",
            parameters,
            transaction);
    }

    public async Task DeleteByIdAsync(string id, DbTransaction? transaction = null)
    {
        await EnsureTableAsync();
        await ExecuteNonQueryAsync(
            $"DELETE FROM {QuoteIdentifier(_tableName)} WHERE {QuoteIdentifier("Id")} = @id;",
            [new SqlParameterValue("@id", id)],
            transaction);
    }

    public async Task TruncateAsync(bool restartIdentity, bool cascade)
    {
        await EnsureTableAsync();
        if (_dialect == SqlDialect.PostgreSql)
        {
            var suffix = (restartIdentity ? " RESTART IDENTITY" : "") + (cascade ? " CASCADE" : "");
            await ExecuteNonQueryAsync($"TRUNCATE TABLE {QuoteIdentifier(_tableName)}{suffix};");

            return;
        }

        await ExecuteNonQueryAsync($"DELETE FROM {QuoteIdentifier(_tableName)};");
        if (restartIdentity && await TableExistsAsync("sqlite_sequence"))
        {
            await ExecuteNonQueryAsync(
                "DELETE FROM sqlite_sequence WHERE name = @name;",
                [new SqlParameterValue("@name", _tableName)]);
        }
    }

    public async Task ExecuteInTransactionAsync(Func<DbTransaction, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await EnsureTableAsync();
        await using var transaction = await BeginTransactionAsync();
        try
        {
            await operation(transaction);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public PropertyInfo? TryGetProperty(string propertyName) =>
        PropertiesByName.GetValueOrDefault(propertyName);

    public async Task ExecuteNonQueryAsync(
        string sql,
        IReadOnlyCollection<SqlParameterValue>? parameters = null,
        DbTransaction? transaction = null)
    {
        await EnsureOpenAsync();
        await using var command = CreateCommand(sql, parameters, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ExecuteScalarAsync(
        string sql,
        IReadOnlyCollection<SqlParameterValue>? parameters = null,
        DbTransaction? transaction = null)
    {
        await EnsureOpenAsync();
        await using var command = CreateCommand(sql, parameters, transaction);
        var scalar = await command.ExecuteScalarAsync();

        return scalar;
    }

    private async Task<List<T>> QueryAsync(
        string sql,
        IReadOnlyCollection<SqlParameterValue>? parameters = null,
        DbTransaction? transaction = null)
    {
        await EnsureOpenAsync();
        await using var command = CreateCommand(sql, parameters, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        var results = new List<T>();
        while (await reader.ReadAsync())
        {
            results.Add(Materialize(reader));
        }

        return results;
    }

    private DbCommand CreateCommand(
        string sql,
        IReadOnlyCollection<SqlParameterValue>? parameters,
        DbTransaction? transaction)
    {
        _commandObserver?.Invoke(sql);
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        if (parameters is not null)
        {
            foreach (var parameterValue in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = parameterValue.Name;
                parameter.Value = ConvertToStorage(parameterValue.Value);
                command.Parameters.Add(parameter);
            }
        }

        return command;
    }

    private async Task<DbTransaction> BeginTransactionAsync()
    {
        await EnsureOpenAsync();
        var transaction = await _connection.BeginTransactionAsync();

        return transaction;
    }

    private async Task EnsureOpenAsync()
    {
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync();
        }
    }

    private (string Sql, List<SqlParameterValue> Parameters) BuildWhere(
        Expression<Func<T, bool>>? predicate,
        bool includeDeleted)
    {
        var translator = new PredicateSqlTranslator<T>(QuoteIdentifier);
        var clauses = new List<string>();
        if (!includeDeleted)
        {
            clauses.Add($"{QuoteIdentifier("DeletedDateTime")} IS NULL");
        }

        if (predicate is not null)
        {
            clauses.Add(translator.Translate(predicate));
        }

        return (
            clauses.Count == 0 ? "" : " WHERE " + string.Join(" AND ", clauses.Select(clause => $"({clause})")),
            translator.Parameters);
    }

    private string SelectColumns() =>
        string.Join(", ", Properties.Select(property => QuoteIdentifier(property.Name)));

    private string BuildCreateTableSql()
    {
        var definitions = new List<string>();
        foreach (var property in Properties)
        {
            var definition = $"{QuoteIdentifier(property.Name)} {GetSqlType(property.PropertyType)}";
            if (property.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
            {
                definition += " PRIMARY KEY";
            }

            definitions.Add(definition);
        }

        return $"CREATE TABLE IF NOT EXISTS {QuoteIdentifier(_tableName)} ({string.Join(", ", definitions)});";
    }

    private string GetSqlType(Type propertyType)
    {
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid))
        {
            return "TEXT";
        }

        if (type == typeof(bool))
        {
            return _dialect == SqlDialect.PostgreSql ? "BOOLEAN" : "INTEGER";
        }

        if (type == typeof(byte) || type == typeof(short) || type == typeof(int))
        {
            return "INTEGER";
        }

        if (type == typeof(long))
        {
            return "BIGINT";
        }

        if (type == typeof(float) || type == typeof(double))
        {
            return _dialect == SqlDialect.PostgreSql ? "DOUBLE PRECISION" : "REAL";
        }

        if (type == typeof(decimal))
        {
            return "NUMERIC";
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return _dialect == SqlDialect.PostgreSql ? "TIMESTAMPTZ" : "TEXT";
        }

        if (type == typeof(byte[]))
        {
            return _dialect == SqlDialect.PostgreSql ? "BYTEA" : "BLOB";
        }

        if (TryGetCollectionElementType(type) is { } elementType)
        {
            return _dialect == SqlDialect.PostgreSql
                ? GetSqlType(elementType) + "[]"
                : "TEXT";
        }

        if (type.IsEnum)
        {
            return "INTEGER";
        }

        return "TEXT";
    }

    private object ConvertToStorage(object? value)
    {
        if (value is null)
        {
            return DBNull.Value;
        }

        var type = value.GetType();
        if (type.IsEnum)
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        if (value is Guid guid)
        {
            return guid.ToString();
        }

        if (value is char character)
        {
            return character.ToString();
        }

        if (value is DateTime dateTime)
        {
            return dateTime.Kind == DateTimeKind.Utc ? dateTime : dateTime.ToUniversalTime();
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToUniversalTime();
        }

        if (value is string or bool or byte or short or int or long or float or double or decimal or byte[])
        {
            return value;
        }

        if (_dialect == SqlDialect.PostgreSql &&
            value is IEnumerable enumerable &&
            TryGetCollectionElementType(type) is { } elementType)
        {
            var values = enumerable.Cast<object?>().ToArray();
            var array = Array.CreateInstance(elementType, values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                array.SetValue(values[index], index);
            }

            return array;
        }

        return JsonSerializer.Serialize(value, type, JsonOptions);
    }

    private static T Materialize(DbDataReader reader)
    {
        var entity = Activator.CreateInstance<T>()
            ?? throw new InvalidOperationException($"{typeof(T).FullName} must have a parameterless constructor.");
        foreach (var property in Properties)
        {
            var ordinal = reader.GetOrdinal(property.Name);
            if (reader.IsDBNull(ordinal))
            {
                property.SetValue(entity, null);
                continue;
            }

            property.SetValue(entity, ConvertFromStorage(reader.GetValue(ordinal), property.PropertyType));
        }

        return entity;
    }

    private static object? ConvertFromStorage(object value, Type destinationType)
    {
        var nullableType = Nullable.GetUnderlyingType(destinationType);
        var targetType = nullableType ?? destinationType;
        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (targetType == typeof(DateTime))
        {
            if (value is DateTimeOffset offset)
            {
                return offset.UtcDateTime;
            }

            return DateTime.Parse(
                Convert.ToString(value, CultureInfo.InvariantCulture)!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        if (targetType == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(
                Convert.ToString(value, CultureInfo.InvariantCulture)!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        if (targetType == typeof(Guid))
        {
            return value is Guid guid ? guid : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        }

        if (targetType.IsEnum)
        {
            return Enum.ToObject(targetType, Convert.ToInt32(value, CultureInfo.InvariantCulture));
        }

        if (TryGetCollectionElementType(targetType) is { } elementType &&
            value is IEnumerable enumerableValue &&
            value is not string)
        {
            var values = enumerableValue.Cast<object?>()
                .Select(item => item is null
                    ? null
                    : elementType.IsInstanceOfType(item)
                        ? item
                        : Convert.ChangeType(item, elementType, CultureInfo.InvariantCulture))
                .ToArray();
            if (targetType.IsArray)
            {
                var array = Array.CreateInstance(elementType, values.Length);
                for (var index = 0; index < values.Length; index++)
                {
                    array.SetValue(values[index], index);
                }

                return array;
            }

            if (Activator.CreateInstance(targetType) is IList list)
            {
                foreach (var item in values)
                {
                    list.Add(item);
                }

                return list;
            }
        }

        if (targetType != typeof(string) &&
            (typeof(IEnumerable).IsAssignableFrom(targetType) || targetType.IsClass))
        {
            return JsonSerializer.Deserialize(
                Convert.ToString(value, CultureInfo.InvariantCulture)!,
                targetType,
                JsonOptions);
        }

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private PropertyInfo GetProperty(string propertyName) =>
        TryGetProperty(propertyName)
        ?? throw new ArgumentException(
            $"Property '{propertyName}' does not exist on {typeof(T).Name}.",
            nameof(propertyName));

    private static Type? TryGetCollectionElementType(Type type)
    {
        if (type == typeof(string) || type == typeof(byte[]))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        return type.GetInterfaces()
            .Append(type)
            .FirstOrDefault(candidate =>
                candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }
}

internal sealed class PredicateSqlTranslator<T>
{
    private readonly Func<string, string> _quote;
    private int _parameterIndex;

    public PredicateSqlTranslator(Func<string, string> quote)
    {
        _quote = quote;
    }

    public List<SqlParameterValue> Parameters { get; } = [];

    public string Translate(Expression<Func<T, bool>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return Visit(expression.Body, expression.Parameters[0]);
    }

    private string Visit(Expression expression, ParameterExpression entityParameter)
    {
        expression = StripConvert(expression);

        return expression switch
        {
            BinaryExpression binary => VisitBinary(binary, entityParameter),
            UnaryExpression { NodeType: ExpressionType.Not } unary =>
                $"NOT ({Visit(unary.Operand, entityParameter)})",
            MethodCallExpression methodCall => VisitMethodCall(methodCall, entityParameter),
            MemberExpression member when IsEntityMember(member, entityParameter) =>
                $"{_quote(member.Member.Name)} = {AddParameter(true)}",
            ConstantExpression { Value: bool boolean } => boolean ? "1 = 1" : "1 = 0",
            _ => throw new NotSupportedException(
                $"Predicate expression '{expression}' is not supported by the SQL translator.")
        };
    }

    private string VisitBinary(BinaryExpression binary, ParameterExpression entityParameter)
    {
        if (binary.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse)
        {
            var logicalOperation = binary.NodeType == ExpressionType.AndAlso ? "AND" : "OR";

            return $"({Visit(binary.Left, entityParameter)}) {logicalOperation} ({Visit(binary.Right, entityParameter)})";
        }

        var left = StripConvert(binary.Left);
        var right = StripConvert(binary.Right);
        var leftColumn = TryGetEntityColumn(left, entityParameter);
        var rightColumn = TryGetEntityColumn(right, entityParameter);

        if (leftColumn is null && rightColumn is null)
        {
            var result = Expression.Lambda<Func<bool>>(binary).Compile(preferInterpretation: true).Invoke();

            return result ? "1 = 1" : "1 = 0";
        }

        var operation = binary.NodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "<>",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            _ => throw new NotSupportedException($"Binary operator '{binary.NodeType}' is not supported.")
        };

        if (leftColumn is not null && rightColumn is not null)
        {
            return $"{leftColumn} {operation} {rightColumn}";
        }

        var column = leftColumn ?? rightColumn!;
        var valueExpression = leftColumn is not null ? right : left;
        var value = Evaluate(valueExpression, entityParameter);
        if (value is null)
        {
            return binary.NodeType switch
            {
                ExpressionType.Equal => $"{column} IS NULL",
                ExpressionType.NotEqual => $"{column} IS NOT NULL",
                _ => throw new NotSupportedException("Only equality comparisons can use null.")
            };
        }

        if (leftColumn is null)
        {
            operation = operation switch
            {
                ">" => "<",
                ">=" => "<=",
                "<" => ">",
                "<=" => ">=",
                _ => operation
            };
        }

        return $"{column} {operation} {AddParameter(value)}";
    }

    private string VisitMethodCall(MethodCallExpression call, ParameterExpression entityParameter)
    {
        if (call.Object is not null &&
            TryGetEntityColumn(call.Object, entityParameter) is { } stringColumn &&
            call.Method.DeclaringType == typeof(string))
        {
            var value = Convert.ToString(Evaluate(call.Arguments[0], entityParameter), CultureInfo.InvariantCulture) ?? "";

            return call.Method.Name switch
            {
                nameof(string.StartsWith) =>
                    $"{stringColumn} LIKE {AddParameter(EscapeLike(value) + "%")} ESCAPE '\\'",
                nameof(string.EndsWith) =>
                    $"{stringColumn} LIKE {AddParameter("%" + EscapeLike(value))} ESCAPE '\\'",
                nameof(string.Contains) =>
                    $"{stringColumn} LIKE {AddParameter("%" + EscapeLike(value) + "%")} ESCAPE '\\'",
                _ => throw new NotSupportedException($"String method '{call.Method.Name}' is not supported.")
            };
        }

        if (call.Method.Name == nameof(Enumerable.Contains))
        {
            Expression collectionExpression;
            Expression valueExpression;
            if (call.Object is not null)
            {
                collectionExpression = call.Object;
                valueExpression = call.Arguments[0];
            }
            else if (call.Arguments.Count == 2)
            {
                collectionExpression = call.Arguments[0];
                valueExpression = call.Arguments[1];
            }
            else
            {
                throw new NotSupportedException($"Contains expression '{call}' is not supported.");
            }

            // Modern C# may bind array.Contains(value) through a ReadOnlySpan<T>
            // conversion. Unwrap that conversion because ref structs cannot be boxed
            // for expression evaluation.
            if (collectionExpression is MethodCallExpression
                {
                    Method.Name: "op_Implicit",
                    Arguments.Count: 1
                } spanConversion)
            {
                collectionExpression = spanConversion.Arguments[0];
            }

            var column = TryGetEntityColumn(valueExpression, entityParameter)
                ?? throw new NotSupportedException("Contains must compare a constant collection to an entity property.");
            var collection = Evaluate(collectionExpression, entityParameter) as IEnumerable
                ?? throw new NotSupportedException("Contains collection could not be evaluated.");
            var placeholders = collection.Cast<object?>().Select(AddParameter).ToArray();

            return placeholders.Length == 0 ? "1 = 0" : $"{column} IN ({string.Join(", ", placeholders)})";
        }

        throw new NotSupportedException(
            $"Method call '{call.Method.DeclaringType?.Name}.{call.Method.Name}' is not supported.");
    }

    private string? TryGetEntityColumn(Expression expression, ParameterExpression entityParameter)
    {
        expression = StripConvert(expression);
        if (expression is MemberExpression member && IsEntityMember(member, entityParameter))
        {
            return _quote(member.Member.Name);
        }

        if (expression is MemberExpression
            {
                Member.Name: "HasValue",
                Expression: MemberExpression nullableMember
            } && IsEntityMember(nullableMember, entityParameter))
        {
            return _quote(nullableMember.Member.Name);
        }

        return null;
    }

    private static bool IsEntityMember(MemberExpression member, ParameterExpression entityParameter) =>
        StripConvert(member.Expression!) == entityParameter;

    private object? Evaluate(Expression expression, ParameterExpression entityParameter)
    {
        if (ReferencesParameter(expression, entityParameter))
        {
            throw new NotSupportedException($"Expression '{expression}' cannot be evaluated as a SQL parameter.");
        }

        var converted = Expression.Convert(expression, typeof(object));

        return Expression.Lambda<Func<object?>>(converted).Compile(preferInterpretation: true).Invoke();
    }

    private string AddParameter(object? value)
    {
        var name = $"@p{_parameterIndex++}";
        Parameters.Add(new SqlParameterValue(name, value));

        return name;
    }

    private static bool ReferencesParameter(Expression expression, ParameterExpression parameter)
    {
        var finder = new ParameterFinder(parameter);
        finder.Visit(expression);

        return finder.Found;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private sealed class ParameterFinder(ParameterExpression parameter) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == parameter)
            {
                Found = true;
            }

            return base.VisitParameter(node);
        }
    }
}
