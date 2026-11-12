using AiPlatform.Application.Common.Ingestion;

namespace AiPlatform.Application.Tests.Ingestion;

public class IngestionPointIdTests
{
    [Fact]
    public void SameDocAndIndex_ProducesSameId()
    {
        var doc = Guid.NewGuid();
        Assert.Equal(IngestionPointId.For(doc, 3), IngestionPointId.For(doc, 3));
    }

    [Fact]
    public void DifferentIndex_ProducesDifferentId()
    {
        var doc = Guid.NewGuid();
        Assert.NotEqual(IngestionPointId.For(doc, 0), IngestionPointId.For(doc, 1));
    }

    [Fact]
    public void DifferentDoc_ProducesDifferentId()
    {
        Assert.NotEqual(IngestionPointId.For(Guid.NewGuid(), 0), IngestionPointId.For(Guid.NewGuid(), 0));
    }

    [Fact]
    public void ProducesValidGuid()
    {
        Assert.True(Guid.TryParse(IngestionPointId.For(Guid.NewGuid(), 5), out _));
    }
}
