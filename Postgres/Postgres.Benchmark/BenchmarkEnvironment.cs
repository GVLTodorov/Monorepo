namespace Postgres.Benchmark;

internal static class BenchmarkEnvironment
{
    private const string ConnectionStringVariable = "POSTGRES_BENCHMARK_CONNECTION_STRING";
    private const string TargetIdVariable = "POSTGRES_BENCHMARK_TARGET_ID";

    public static void Configure(string connectionString, string targetId)
    {
        Environment.SetEnvironmentVariable(ConnectionStringVariable, connectionString);
        Environment.SetEnvironmentVariable(TargetIdVariable, targetId);
    }

    public static string GetConnectionString() => GetRequiredVariable(ConnectionStringVariable);

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
