using Postgres.Helpers;

namespace Postgres.Tests;

[TestFixture]
public sealed class PagedListTests
{
    [Test]
    public void Constructor_CopiesItemsAndInitializesAllMetadata()
    {
        var source = new List<int> { 1, 2, 3 };

        var page = new PagedList<int>(source, 2, 3, 4, 10);
        source.Add(99);

        Assert.Multiple(() =>
        {
            Assert.That(page.PageIndex, Is.EqualTo(2));
            Assert.That(page.PageSize, Is.EqualTo(3));
            Assert.That(page.TotalPages, Is.EqualTo(4));
            Assert.That(page.TotalCount, Is.EqualTo(10));
            Assert.That(page.Items, Is.EqualTo(new[] { 1, 2, 3 }));
        });
    }

    [TestCase(1, 3, false, true)]
    [TestCase(2, 3, true, true)]
    [TestCase(3, 3, true, false)]
    public void NavigationFlags_PagePosition_ReturnExpectedValues(
        int pageIndex,
        long totalPages,
        bool hasPrevious,
        bool hasNext)
    {
        var page = new PagedList<int>([], pageIndex, 10, totalPages, 25);

        Assert.Multiple(() =>
        {
            Assert.That(page.HasPreviousPage, Is.EqualTo(hasPrevious));
            Assert.That(page.HasNextPage, Is.EqualTo(hasNext));
        });
    }

    [Test]
    public void MutableProperties_CanBeUpdatedThroughConcreteType()
    {
        var page = new PagedList<int>([], 1, 1, 0, 0)
        {
            PageIndex = 5,
            PageSize = 20,
            TotalPages = 8,
            TotalCount = 155,
            Items = new List<int> { 42 }
        };

        IPagedList<int> contract = page;
        Assert.Multiple(() =>
        {
            Assert.That(contract.PageIndex, Is.EqualTo(5));
            Assert.That(contract.PageSize, Is.EqualTo(20));
            Assert.That(contract.TotalPages, Is.EqualTo(8));
            Assert.That(contract.TotalCount, Is.EqualTo(155));
            Assert.That(contract.Items, Is.EqualTo(new[] { 42 }));
        });
    }
}
