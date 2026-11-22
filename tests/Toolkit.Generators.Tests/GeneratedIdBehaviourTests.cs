using System.Text.Json;
using Toolkit.Generators.Tests.Sample;

namespace Toolkit.Generators.Tests;

/// <summary>Uses the ids in Sample/ the way application code would, to check the generated members work.</summary>
public class GeneratedIdBehaviourTests
{
    [Fact]
    public void New_guid_ids_are_unique_and_Empty_is_default()
    {
        Assert.NotEqual(OrderId.New(), OrderId.New());
        Assert.Equal(default, OrderId.Empty);
    }

    [Fact]
    public void Ids_are_equal_by_value()
    {
        var guid = Guid.NewGuid();

        Assert.Equal(new OrderId(guid), new OrderId(guid));
        Assert.Equal(new Sku("ABC-1"), new Sku("ABC-1"));
    }

    [Fact]
    public void Serializes_as_the_plain_value_and_back()
    {
        var order = new Order(new OrderId(Guid.Parse("3f2a9c1e-5b7d-4e8a-9c21-7d4e5f6a8b90")), new CustomerNumber(42), new Sku("ABC-1"));

        var json = JsonSerializer.Serialize(order);

        Assert.Equal("""{"Id":"3f2a9c1e-5b7d-4e8a-9c21-7d4e5f6a8b90","Customer":42,"Sku":"ABC-1"}""", json);
        Assert.Equal(order, JsonSerializer.Deserialize<Order>(json));
    }

    [Fact]
    public void Parses_and_formats_like_the_underlying_value()
    {
        Assert.Equal(new CustomerNumber(42), CustomerNumber.Parse("42"));
        Assert.Equal("42", new CustomerNumber(42).ToString());

        Assert.True(OrderId.TryParse("3f2a9c1e-5b7d-4e8a-9c21-7d4e5f6a8b90", out var id));
        Assert.Equal("3f2a9c1e-5b7d-4e8a-9c21-7d4e5f6a8b90", id.ToString());

        Assert.False(OrderId.TryParse("not-a-guid", out _));
        Assert.False(CustomerNumber.TryParse(null, out _));
    }

    [Fact]
    public void Sorts_by_the_underlying_value()
    {
        CustomerNumber[] numbers = [new(3), new(1), new(2)];

        Assert.Equal([new(1), new(2), new(3)], numbers.Order());
    }
}
