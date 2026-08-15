using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk.Tests;

public sealed class QueryStringBuilderTests
{
    [Test]
    public async Task Value_Should_Be_Empty_When_Nothing_Was_Added()
    {
        var query = new QueryStringBuilder();

        await Assert.That(query.Value).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task AddText_Should_Skip_Null_Values()
    {
        var query = new QueryStringBuilder();

        query.AddText("search", null);

        await Assert.That(query.Value).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task AddText_Should_Join_Values_With_Query_Separators()
    {
        var query = new QueryStringBuilder();

        query.AddText("search", "alpha");
        query.AddText("cursor", "beta");

        await Assert.That(query.Value).IsEqualTo("?search=alpha&cursor=beta");
    }

    [Test]
    public async Task AddText_Should_Escape_The_Value()
    {
        var query = new QueryStringBuilder();

        query.AddText("search", "a b&c=d");

        await Assert.That(query.Value).IsEqualTo("?search=a%20b%26c%3Dd");
    }

    [Test]
    public async Task AddText_Should_Escape_The_Name()
    {
        var query = new QueryStringBuilder();

        query.AddText("odd name&x", "v");

        await Assert.That(query.Value).IsEqualTo("?odd%20name%26x=v");
    }

    [Test]
    public async Task AddCount_Should_Write_The_Invariant_Wire_String()
    {
        var query = new QueryStringBuilder();

        query.AddCount("limit", 50, "request");

        await Assert.That(query.Value).IsEqualTo("?limit=50");
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task AddCount_Should_Refuse_A_Non_Positive_Value(int limit)
    {
        var query = new QueryStringBuilder();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => query.AddCount("limit", limit, "request"));

        await Assert.That(exception.ParamName).IsEqualTo("request");
        await Assert.That(exception.Message).Contains("positive");
    }

    [Test]
    [Arguments(ListOrder.Ascending, "?order=asc")]
    [Arguments(ListOrder.Descending, "?order=desc")]
    public async Task AddOrder_Should_Write_The_Wire_Spelling(ListOrder order, string expected)
    {
        var query = new QueryStringBuilder();

        query.AddOrder("order", order);

        await Assert.That(query.Value).IsEqualTo(expected);
    }

    [Test]
    public async Task AddOrder_Should_Refuse_An_Undefined_Value()
    {
        var query = new QueryStringBuilder();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => query.AddOrder("order", (ListOrder)7));
        await Task.CompletedTask;
    }

    [Test]
    public async Task AddParentFilter_Should_Write_The_Wire_Value()
    {
        var query = new QueryStringBuilder();

        query.AddParentFilter("parentID", SessionParentFilter.RootOnly);
        query.AddParentFilter("other", SessionParentFilter.Of("ses_1"));

        await Assert.That(query.Value).IsEqualTo("?parentID=null&other=ses_1");
    }

    [Test]
    public async Task Add_Should_Skip_Every_Null_Optional()
    {
        var query = new QueryStringBuilder();

        query.AddCount("limit", null, "request");
        query.AddOrder("order", null);
        query.AddParentFilter("parentID", null);

        await Assert.That(query.Value).IsEqualTo(string.Empty);
    }
}
