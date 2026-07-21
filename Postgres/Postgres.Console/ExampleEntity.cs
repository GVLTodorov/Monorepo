using Postgres.Helpers;

namespace Postgres.Console;

[TableName("FeatureExamples")]
public sealed class ExampleEntity : PostgresEntity
{
    public string ExternalId { get; set; } = string.Empty;

    public string TestField { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public int Score { get; set; }

    public List<string> Tags { get; set; } = [];

    public DateTime ExpiresAtUtc { get; set; }

    [SensitiveData]
    public string SecretNote { get; set; } = string.Empty;
}
