using AiPlatform.Application.Common.Ingestion;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Results;
using AiPlatform.Application.Features.Documents.Delete;
using AiPlatform.Application.Features.Documents.List;
using AiPlatform.Application.Features.Documents.Upload;
using AiPlatform.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AiPlatform.Application.Tests.Documents;

public class DocumentsTests
{
    private readonly IDocumentRepository _repo = Substitute.For<IDocumentRepository>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();

    private DeleteDocumentCommandHandler DeleteHandler() =>
        new(_repo, _vectorStore, NullLogger<DeleteDocumentCommandHandler>.Instance);

    [Fact]
    public async Task List_MapsItems_AndComputesTotalPages()
    {
        var docs = new List<Document> { new() { FileName = "a.txt" }, new() { FileName = "b.txt" } };
        _repo.ListAsync(1, 20, Arg.Any<CancellationToken>()).Returns(((IReadOnlyList<Document>)docs, 45));

        var result = await new ListDocumentsQueryHandler(_repo).Handle(new ListDocumentsQuery(1, 20), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal(45, result.Value.TotalCount);
        Assert.Equal(3, result.Value.TotalPages); // ceil(45/20)
    }

    [Fact]
    public async Task Delete_ExistingDocument_MarksDeleted_AndSaves()
    {
        var doc = new Document { FileName = "a.txt", TotalChunks = 3 };
        _repo.GetAsync(doc.Id, Arg.Any<CancellationToken>()).Returns(doc);

        var result = await DeleteHandler().Handle(new DeleteDocumentCommand(doc.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(doc.IsDeleted);
        Assert.NotNull(doc.DeletedAtUtc);
        await _repo.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // SQL soft-delete-ই যথেষ্ট ছিল না — chunk রয়ে গেলে মোছা ডকুমেন্ট RAG-এ সূত্র হিসেবে ফিরে আসত।
    [Fact]
    public async Task Delete_AlsoRemovesTheDocumentsChunksFromTheVectorStore()
    {
        var doc = new Document { FileName = "a.txt", TotalChunks = 3 };
        _repo.GetAsync(doc.Id, Arg.Any<CancellationToken>()).Returns(doc);

        await DeleteHandler().Handle(new DeleteDocumentCommand(doc.Id), CancellationToken.None);

        var expected = IngestionPointId.ForDocument(doc.Id, 3).ToList();
        await _vectorStore.Received().DeleteAsync(
            Arg.Is<IEnumerable<string>>(ids => ids.SequenceEqual(expected)), Arg.Any<CancellationToken>());
    }

    // vector delete ব্যর্থ হলে SQL-এ মোছা দেখিয়ে chunk রেখে দেওয়া সবচেয়ে খারাপ ফল —
    // তখন আর কেউ ওগুলোর খোঁজ রাখে না। সারি থাকলে আবার চেষ্টা করা যায়।
    [Fact]
    public async Task Delete_WhenVectorStoreFails_DoesNotMarkDeleted()
    {
        var doc = new Document { FileName = "a.txt", TotalChunks = 2 };
        _repo.GetAsync(doc.Id, Arg.Any<CancellationToken>()).Returns(doc);
        _vectorStore.DeleteAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("qdrant down")));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            DeleteHandler().Handle(new DeleteDocumentCommand(doc.Id), CancellationToken.None));

        Assert.False(doc.IsDeleted);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // কখনো ingest হয়নি (TotalChunks = 0) → মোছার id নেই, কিন্তু delete সফল হওয়া উচিত।
    [Fact]
    public async Task Delete_NeverIngestedDocument_StillSucceeds()
    {
        var doc = new Document { FileName = "a.txt", TotalChunks = 0 };
        _repo.GetAsync(doc.Id, Arg.Any<CancellationToken>()).Returns(doc);

        var result = await DeleteHandler().Handle(new DeleteDocumentCommand(doc.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(doc.IsDeleted);
    }

    [Fact]
    public async Task Delete_MissingDocument_ReturnsNotFound()
    {
        _repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Document?)null);

        var result = await DeleteHandler().Handle(new DeleteDocumentCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_StampsCurrentTenantId()
    {
        var tenant = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.TenantId.Returns(tenant);
        var queue = Substitute.For<IIngestionQueue>();

        Document? saved = null;
        await _repo.AddAsync(Arg.Do<Document>(d => saved = d), Arg.Any<CancellationToken>());

        await new UploadDocumentCommandHandler(_repo, queue, currentUser)
            .Handle(new UploadDocumentCommand("f.txt", "hi"), CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal(tenant, saved!.TenantId);
        await queue.Received().EnqueueAsync(saved.Id, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 20, false)]   // page < 1
    [InlineData(1, 0, false)]    // pageSize < 1
    [InlineData(1, 101, false)]  // pageSize > 100
    [InlineData(1, 20, true)]    // valid
    public void ListValidator_EnforcesBounds(int page, int pageSize, bool valid)
    {
        var result = new ListDocumentsQueryValidator().Validate(new ListDocumentsQuery(page, pageSize));
        Assert.Equal(valid, result.IsValid);
    }
}
