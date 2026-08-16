using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

public class ArticleEndpointsTests : IClassFixture<ArticleWebApplicationFactory>
{
    private const string JwtKey = "JwtSecretPlaceHolder_explAIned32";
    private const string Issuer = "http://localhost:5125/";
    private const string Audience = "http://localhost:5125/";

    private const string AuthorId = "author-1";
    private const string StrangerId = "stranger-2";

    private readonly ArticleWebApplicationFactory _factory;

    public ArticleEndpointsTests(ArticleWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.FakeService.Store.Clear();
    }

    private HttpClient CreateClient(string? userId = null)
    {
        var client = _factory.CreateClient();
        if (userId is not null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateAccessToken(userId));
        }
        return client;
    }

    private static string CreateAccessToken(string userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(JwtRegisteredClaimNames.Email, $"{userId}@test"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Typ, "access"),
            },
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static CreateArticleDto Valid(string access = "Public") =>
        new("Title", "hello world", "desc", "tag", access);

    private sealed record CreatedResponse(
        [property: JsonPropertyName("id")] string Id);

    [Fact]
    public async Task Search_NoMatches_Returns404()
    {
        var client = CreateClient();

        var resp = await client.GetAsync("/articles/search?query=anything");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Search_WithMatches_Returns200()
    {
        _factory.FakeService.Seed(new Article
        {
            Title = "Dotnet rocks",
            Content = "asp.net core",
            Description = "d",
            Tags = "t",
            Status = ArticleStatus.Published,
            AuthorId = AuthorId,
        });
        var client = CreateClient();

        var resp = await client.GetAsync("/articles/search?query=dotnet");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetById_Existing_Returns200()
    {
        var seeded = _factory.FakeService.Seed(new Article
        {
            Title = "t", Content = "c", Description = "d", Tags = "x",
            Status = ArticleStatus.Draft, AuthorId = AuthorId,
        });
        var client = CreateClient();

        var resp = await client.GetAsync($"/articles/{seeded.Id}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var client = CreateClient();

        var resp = await client.GetAsync("/articles/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var client = CreateClient();

        var resp = await client.PostAsJsonAsync("/articles", Valid());

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Create_WithInvalidToken_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "garbage");

        var resp = await client.PostAsJsonAsync("/articles", Valid());

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Create_WithValidToken_Returns201AndPersists()
    {
        var client = CreateClient(AuthorId);

        var resp = await client.PostAsJsonAsync("/articles", Valid());

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<CreatedResponse>();
        Assert.NotNull(body);
        Assert.True(_factory.FakeService.Store.ContainsKey(body!.Id));
        Assert.Equal(AuthorId, _factory.FakeService.Store[body.Id].AuthorId);
    }

    [Fact]
    public async Task Create_WithInvalidAccessLevel_Returns400()
    {
        var client = CreateClient(AuthorId);

        var resp = await client.PostAsJsonAsync("/articles", Valid("NotALevel"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_AsAuthor_Returns204()
    {
        var seeded = _factory.FakeService.Seed(new Article
        {
            Title = "old", Content = "old", Description = "d", Tags = "t",
            Status = ArticleStatus.Draft, AuthorId = AuthorId,
        });
        var client = CreateClient(AuthorId);

        var dto = new UpdateArticleDto("new title", "a b c", "new desc", "tag", "Private", "Published");
        var resp = await client.PutAsJsonAsync($"/articles/{seeded.Id}", dto);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        var updated = _factory.FakeService.Store[seeded.Id];
        Assert.Equal("new title", updated.Title);
        Assert.Equal(AccessLevel.Private, updated.AccessLevel);
        Assert.Equal(ArticleStatus.Published, updated.Status);
    }

    [Fact]
    public async Task Update_AsOtherUser_Returns403()
    {
        var seeded = _factory.FakeService.Seed(new Article
        {
            Title = "t", Content = "c", Description = "d", Tags = "x",
            Status = ArticleStatus.Draft, AuthorId = AuthorId,
        });
        var client = CreateClient(StrangerId);

        var dto = new UpdateArticleDto("x", "y", "z", "q", "Public", "Published");
        var resp = await client.PutAsJsonAsync($"/articles/{seeded.Id}", dto);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Update_MissingArticle_Returns404()
    {
        var client = CreateClient(AuthorId);

        var dto = new UpdateArticleDto("x", "y", "z", "q", "Public", "Published");
        var resp = await client.PutAsJsonAsync("/articles/none", dto);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_AsAuthor_Returns204AndArchives()
    {
        var seeded = _factory.FakeService.Seed(new Article
        {
            Title = "t", Content = "c", Description = "d", Tags = "x",
            Status = ArticleStatus.Published, AuthorId = AuthorId,
        });
        var client = CreateClient(AuthorId);

        var resp = await client.DeleteAsync($"/articles/{seeded.Id}");

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Equal(ArticleStatus.Archived, _factory.FakeService.Store[seeded.Id].Status);
    }

    [Fact]
    public async Task Delete_AsOtherUser_Returns403()
    {
        var seeded = _factory.FakeService.Seed(new Article
        {
            Title = "t", Content = "c", Description = "d", Tags = "x",
            Status = ArticleStatus.Published, AuthorId = AuthorId,
        });
        var client = CreateClient(StrangerId);

        var resp = await client.DeleteAsync($"/articles/{seeded.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutToken_Returns401()
    {
        var client = CreateClient();

        var resp = await client.DeleteAsync("/articles/anything");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Batch_ReturnsArticles_InRequestedOrder()
    {
        var first = SeedPublished("first");
        var second = SeedPublished("second");
        var client = CreateClient();

        var resp = await client.GetAsync($"/articles/batch?ids={second.Id},{first.Id}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var articles = await resp.Content.ReadFromJsonAsync<List<Article>>();
        Assert.NotNull(articles);
        Assert.Equal(new[] { second.Id, first.Id }, articles!.Select(a => a.Id));
    }

    [Fact]
    public async Task Batch_WithoutIds_Returns400()
    {
        var client = CreateClient();

        var resp = await client.GetAsync("/articles/batch");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Batch_SkipsNonPublicArticles()
    {
        var published = SeedPublished("visible");
        var draft = _factory.FakeService.Seed(new Article
        {
            Title = "draft",
            Content = "c",
            Description = "d",
            Tags = "t",
            Status = ArticleStatus.Draft,
            AccessLevel = AccessLevel.Public,
            AuthorId = AuthorId,
        });
        var client = CreateClient();

        var resp = await client.GetAsync($"/articles/batch?ids={draft.Id},{published.Id}");

        var articles = await resp.Content.ReadFromJsonAsync<List<Article>>();
        Assert.NotNull(articles);
        Assert.Equal(new[] { published.Id }, articles!.Select(a => a.Id));
    }

    [Fact]
    public async Task Recent_ReturnsNewestFirst()
    {
        var older = SeedPublished("older", DateTime.UtcNow.AddDays(-2));
        var newer = SeedPublished("newer", DateTime.UtcNow);
        var client = CreateClient();

        var resp = await client.GetAsync("/articles/recent?limit=10");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var articles = await resp.Content.ReadFromJsonAsync<List<Article>>();
        Assert.NotNull(articles);
        Assert.Equal(new[] { newer.Id, older.Id }, articles!.Select(a => a.Id));
    }

    private Article SeedPublished(string title, DateTime? publishedAt = null) =>
        _factory.FakeService.Seed(new Article
        {
            Title = title,
            Content = "c",
            Description = "d",
            Tags = "t",
            Status = ArticleStatus.Published,
            AccessLevel = AccessLevel.Public,
            AuthorId = AuthorId,
            PublishedAt = publishedAt ?? DateTime.UtcNow,
        });
}
