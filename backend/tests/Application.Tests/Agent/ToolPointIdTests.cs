using AiPlatform.Infrastructure.Agent;

namespace AiPlatform.Application.Tests.Agent;

public class ToolPointIdTests
{
    [Fact]
    public void SameName_SameId() =>
        Assert.Equal(ToolPointId.For("get_sales"), ToolPointId.For("get_sales"));

    [Fact]
    public void CaseInsensitive() =>
        Assert.Equal(ToolPointId.For("Get_Sales"), ToolPointId.For("get_sales"));

    [Fact]
    public void DifferentNames_DifferentIds() =>
        Assert.NotEqual(ToolPointId.For("get_sales"), ToolPointId.For("get_stock"));
}
