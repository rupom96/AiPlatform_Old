using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AiPlatform.Api.IntegrationTests;

// Phase 7-এর যে জিনিসগুলো unit test ধরতেই পারে না সেগুলোর প্রমাণ: EF global query filter
// (soft-delete + tenant isolation) আসল SQL Server-এ কেমন আচরণ করে, আর Redis idempotency slot কাজ করে কিনা।
[Collection(nameof(ApiCollection))]
public class DocumentsTests(ApiFactory factory)
{
    [Fact]
    public async Task Upload_ThenList_ReturnsDocument()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var documentId = await UploadAsync(client, "notes.txt", "AiPlatform integration test content.");

        var list = await client.GetAsync("/api/v1/documents");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var body = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 1);

        var item = body.GetProperty("items").EnumerateArray()
            .Single(d => d.GetProperty("id").GetGuid() == documentId);
        Assert.Equal("notes.txt", item.GetProperty("fileName").GetString());
    }

    // soft-delete-এর আসল দাবি দুটো: list থেকে হারায়, কিন্তু সারি থাকে। দ্বিতীয়টা DB না দেখে প্রমাণ হয় না।
    [Fact]
    public async Task Delete_HidesFromList_ButRowSurvives()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var documentId = await UploadAsync(client, "doomed.txt", "delete me");

        var delete = await client.DeleteAsync($"/api/v1/documents/{documentId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var list = await client.GetAsync("/api/v1/documents");
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items");
        Assert.DoesNotContain(items.EnumerateArray(), d => d.GetProperty("id").GetGuid() == documentId);

        var status = await client.GetAsync($"/api/v1/documents/{documentId}");
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);

        // IgnoreQueryFilters — filter বন্ধ করে দেখি সারি সত্যিই আছে, শুধু flag করা
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Documents.IgnoreQueryFilters().SingleOrDefaultAsync(d => d.Id == documentId);

        Assert.NotNull(row);
        Assert.True(row.IsDeleted);
        Assert.NotNull(row.DeletedAtUtc);
    }

    // security boundary — এটা ভাঙলে এক গ্রাহকের ডেটা আরেকজন দেখে। টেস্ট না থাকা মানে অন্ধ থাকা।
    [Fact]
    public async Task Documents_AreIsolatedBetweenTenants()
    {
        var alice = await factory.CreateAuthenticatedClientAsync();
        var bob = await factory.CreateAuthenticatedClientAsync();

        var aliceDocumentId = await UploadAsync(alice, "alice-secret.txt", "alice private notes");

        // আগে প্রমাণ করি document-টা সত্যিই দৃশ্যমান — নইলে নিচের "Bob দেখে না" ভুল কারণেও পাস করতে পারত
        var aliceList = await alice.GetAsync("/api/v1/documents");
        var aliceItems = (await aliceList.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items");
        Assert.Contains(aliceItems.EnumerateArray(), d => d.GetProperty("id").GetGuid() == aliceDocumentId);

        // Bob নতুন tenant → তার list পুরো খালি থাকার কথা। "DoesNotContain"-এর চেয়ে কড়া:
        // ফিল্টার ফাঁস করলে Bob অন্য tenant-এর সব document দেখত, page-এ থাকুক বা না থাকুক ধরা পড়ত।
        var bobList = await bob.GetAsync("/api/v1/documents");
        var bobBody = await bobList.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, bobBody.GetProperty("totalCount").GetInt32());
        Assert.Empty(bobBody.GetProperty("items").EnumerateArray());

        // id জেনে সরাসরি চাইলেও নয় — 403 নয়, 404: document আছে কিনা সেটাও ফাঁস করি না
        var bobDirect = await bob.GetAsync($"/api/v1/documents/{aliceDocumentId}");
        Assert.Equal(HttpStatusCode.NotFound, bobDirect.StatusCode);
    }

    // একই Idempotency-Key-তে retry (network hiccup, double click) → দুটো document নয়
    [Fact]
    public async Task Upload_WithSameIdempotencyKey_CreatesOneDocument()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var idempotencyKey = Guid.NewGuid().ToString();

        var first = await UploadAsync(client, "retry.txt", "same payload", idempotencyKey);
        var second = await UploadAsync(client, "retry.txt", "same payload", idempotencyKey);

        Assert.Equal(first, second);

        var list = await client.GetAsync("/api/v1/documents");
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items");
        Assert.Single(items.EnumerateArray(), d => d.GetProperty("id").GetGuid() == first);
    }

    [Fact]
    public async Task Upload_WithUnsupportedExtension_Returns400()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await PostFileAsync(client, "malware.exe", "MZ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0, 20)]     // page শূন্য
    [InlineData(1, 0)]      // pageSize শূন্য
    [InlineData(1, 101)]    // pageSize সীমার বাইরে → ইচ্ছাকৃত/দুর্ঘটনাজনিত বড় query আটকায়
    public async Task List_WithInvalidPaging_Returns400(int page, int pageSize)
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync($"/api/v1/documents?page={page}&pageSize={pageSize}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Documents_WithoutToken_Returns401()
    {
        var anonymous = factory.CreateClient();

        var list = await anonymous.GetAsync("/api/v1/documents");
        var upload = await PostFileAsync(anonymous, "sneaky.txt", "no token");

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, upload.StatusCode);
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string fileName, string content, string? idempotencyKey = null)
    {
        var response = await PostFileAsync(client, fileName, content, idempotencyKey);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
    }

    private static async Task<HttpResponseMessage> PostFileAsync(
        HttpClient client, string fileName, string content, string? idempotencyKey = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", fileName);   // "file" = controller action-এর parameter নাম

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documents") { Content = form };
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);

        return await client.SendAsync(request);
    }
}
