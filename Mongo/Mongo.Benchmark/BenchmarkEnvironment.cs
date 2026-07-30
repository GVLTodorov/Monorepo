namespace Mongo.Benchmark;

internal static class BenchmarkEnvironment
{
    private const string ConnectionStringVariable = "MONGO_BENCHMARK_CONNECTION_STRING";
    private const string DatabaseNameVariable = "MONGO_BENCHMARK_DATABASE_NAME";
    private const string TargetIdVariable = "MONGO_BENCHMARK_TARGET_ID";

    public static void Configure(string connectionString, string databaseName, string targetId)
    {
        Environment.SetEnvironmentVariable(ConnectionStringVariable, connectionString);
        Environment.SetEnvironmentVariable(DatabaseNameVariable, databaseName);
        Environment.SetEnvironmentVariable(TargetIdVariable, targetId);
    }

    public static string GetConnectionString() => GetRequiredVariable(ConnectionStringVariable);

    public static string GetDatabaseName() => GetRequiredVariable(DatabaseNameVariable);

    public static string GetTargetId() => GetRequiredVariable(TargetIdVariable);

    private static string GetRequiredVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required benchmark environment variable '{name}' is missing.");
        }

        return value;
    }
}
