using Sqlite.Helpers;

namespace Sqlite.Benchmark;

[TableName("RepositoryBenchmarks")]
public sealed class BenchmarkEntity : SqliteEntity
{
    public string ExternalId { get; set; } = string.Empty;

    public string TestField { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public int Score { get; set; }

    public List<string> Tags { get; set; } = [];

    public DateTime ExpiresAtUtc { get; set; }

    [SensitiveData]
    public string SecretNote { get; set; } = string.Empty;

    public static List<BenchmarkEntity> CreateMany(int count)
    {
        var expiry = DateTime.UtcNow.AddDays(7);
        var entities = Enumerable.Range(0, count)
            .Select(index => new BenchmarkEntity
            {
                ExternalId = $"sqlite-{index:D4}",
                TestField = $"Value {index:D4}",
                Category = index % 2 == 0 ? "Even" : "Odd",
                Score = index * 37 % count,
                Tags = ["benchmark", $"group-{index % 10}"],
                ExpiresAtUtc = expiry,
                SecretNote = "Benchmark secret"
            })
            .ToList();

        return entities;
    }
}
