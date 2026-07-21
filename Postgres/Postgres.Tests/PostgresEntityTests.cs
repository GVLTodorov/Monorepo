using Postgres.Helpers;

namespace Postgres.Tests;

[TestFixture]
public sealed class PostgresEntityTests
{
    [Test]
    public void Constructor_GeneratesDistinctGuidIdentifiers()
    {
        var first = new PostgresEntity();
        var second = new PostgresEntity();

        Assert.Multiple(() =>
        {
            Assert.That(Guid.TryParse(first.Id, out _), Is.True);
            Assert.That(Guid.TryParse(second.Id, out _), Is.True);
            Assert.That(second.Id, Is.Not.EqualTo(first.Id));
        });
    }

    [Test]
    public void AuditProperties_SetAndSoftDeleteState_FollowDeletedTimestamp()
    {
        var created = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var updated = created.AddMinutes(1);
        var entity = new PostgresEntity
        {
            Id = "fixed-id",
            CreatedDateTime = created,
            UpdatedDateTime = updated
        };

        Assert.Multiple(() =>
        {
            Assert.That(entity.Id, Is.EqualTo("fixed-id"));
            Assert.That(entity.CreatedDateTime, Is.EqualTo(created));
            Assert.That(entity.UpdatedDateTime, Is.EqualTo(updated));
            Assert.That(entity.DeletedDateTime, Is.Null);
            Assert.That(entity.IsDeleted, Is.False);
        });

        entity.DeletedDateTime = updated.AddMinutes(1);

        Assert.Multiple(() =>
        {
            Assert.That(entity.DeletedDateTime, Is.EqualTo(updated.AddMinutes(1)));
            Assert.That(entity.IsDeleted, Is.True);
        });
    }

    [Test]
    public void Interface_ExposesEntityContract()
    {
        IPostgresEntity entity = new PostgresEntity();
        var timestamp = DateTime.UtcNow;

        entity.Id = "contract-id";
        entity.CreatedDateTime = timestamp;
        entity.UpdatedDateTime = timestamp;
        entity.DeletedDateTime = timestamp;

        Assert.Multiple(() =>
        {
            Assert.That(entity.Id, Is.EqualTo("contract-id"));
            Assert.That(entity.CreatedDateTime, Is.EqualTo(timestamp));
            Assert.That(entity.UpdatedDateTime, Is.EqualTo(timestamp));
            Assert.That(entity.DeletedDateTime, Is.EqualTo(timestamp));
            Assert.That(entity.IsDeleted, Is.True);
        });
    }
}
