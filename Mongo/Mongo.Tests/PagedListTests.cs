using Mongo.Helpers;

namespace Mongo.Tests;

[TestFixture]
public class PagedListTests
{
    [Test]
    public void PagedList_Initializes_WithCorrectValues()
    {
        var items = new List<int> { 1, 2, 3 };
        var pagedList = new PagedList<int>(items, 1, 10, 1, 3);
        
        Assert.That(pagedList.PageIndex, Is.EqualTo(1));
        Assert.That(pagedList.PageSize, Is.EqualTo(10));
        Assert.That(pagedList.TotalCount, Is.EqualTo(3));
        Assert.That(pagedList.TotalPages, Is.EqualTo(1));
        Assert.That(pagedList.Items, Has.Count.EqualTo(3));
    }

    [Test]
    public void HasPreviousPage_ReturnsFalse_WhenPageIndexIsOne()
    {
        var items = new List<int> { 1, 2, 3 };
        var pagedList = new PagedList<int>(items, 1, 10, 1, 3);
        
        Assert.That(pagedList.HasPreviousPage, Is.False);
    }

    [Test]
    public void HasPreviousPage_ReturnsTrue_WhenPageIndexIsGreaterThanOne()
    {
        var items = new List<int> { 1, 2, 3 };
        var pagedList = new PagedList<int>(items, 2, 10, 2, 20);
        
        Assert.That(pagedList.HasPreviousPage, Is.True);
    }

    [Test]
    public void HasNextPage_ReturnsFalse_WhenOnLastPage()
    {
        var items = new List<int> { 1, 2, 3 };
        var pagedList = new PagedList<int>(items, 2, 10, 2, 20);
        
        Assert.That(pagedList.HasNextPage, Is.False);
    }

    [Test]
    public void HasNextPage_ReturnsTrue_WhenNotOnLastPage()
    {
        var items = new List<int> { 1, 2, 3 };
        var pagedList = new PagedList<int>(items, 1, 10, 2, 20);
        
        Assert.That(pagedList.HasNextPage, Is.True);
    }

    [Test]
    public void Items_ContainsSourceItems()
    {
        var items = new List<int> { 1, 2, 3, 4, 5 };
        var pagedList = new PagedList<int>(items, 1, 10, 1, 5);
        
        Assert.That(pagedList.Items, Is.EqualTo(items));
    }
}

