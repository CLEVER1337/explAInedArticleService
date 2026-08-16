using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Moq;

public class ArticleControllerTests
{
    private const string AuthorId = "author-123";
    private const string OtherUserId = "someone-else";

    private static ArticleController Build(IArticleService service, string? sub = AuthorId, IUserEventProducer? userEventProducer = null)
    {
        var controller = new ArticleController(service, userEventProducer ?? new Mock<IUserEventProducer>().Object);

        var claims = sub is null ? Array.Empty<Claim>() : [new Claim(JwtRegisteredClaimNames.Sub, sub)];
        var identity = sub is null ? new ClaimsIdentity() : new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        return controller;
    }

    private static Article SampleArticle(string? authorId = AuthorId) => new()
    {
        Id = "article-1",
        Title = "t",
        Content = "one two three",
        Description = "d",
        Tags = "tag",
        AccessLevel = AccessLevel.Public,
        Status = ArticleStatus.Published,
        AuthorId = authorId ?? AuthorId,
    };

    private static int StatusOf(IResult result) => result switch
    {
        ForbidHttpResult => StatusCodes.Status403Forbidden,
        IStatusCodeHttpResult s => s.StatusCode ?? 200,
        _ => throw new InvalidOperationException($"Result {result.GetType().Name} has no status code"),
    };

    [Fact]
    public async Task SearchArticles_ReturnsOk_WhenHits()
    {
        var svc = new Mock<IArticleService>();
        svc.Setup(s => s.GetArticlesByQueryAsync("q")).ReturnsAsync(new[] { SampleArticle() });
        var controller = Build(svc.Object);

        var result = await controller.SearchArticles("q");

        Assert.IsAssignableFrom<Ok<IEnumerable<Article>>>(result);
    }

    [Fact]
    public async Task SearchArticles_ReturnsNotFound_WhenEmpty()
    {
        var svc = new Mock<IArticleService>();
        svc.Setup(s => s.GetArticlesByQueryAsync("q")).ReturnsAsync(Array.Empty<Article>());
        var controller = Build(svc.Object);

        var result = await controller.SearchArticles("q");

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task GetArticleById_ReturnsOk_WhenFound()
    {
        var svc = new Mock<IArticleService>();
        svc.Setup(s => s.GetArticleByIdAsync("article-1")).ReturnsAsync(SampleArticle());
        var controller = Build(svc.Object);

        var result = await controller.GetArticleById("article-1");

        Assert.IsAssignableFrom<Ok<Article>>(result);
    }

    [Fact]
    public async Task GetArticleById_ReturnsNotFound_WhenServiceThrows()
    {
        var svc = new Mock<IArticleService>();
        svc.Setup(s => s.GetArticleByIdAsync("missing")).ThrowsAsync(new Exception("nope"));
        var controller = Build(svc.Object);

        var result = await controller.GetArticleById("missing");

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task CreateArticle_ReturnsUnauthorized_WhenNoSubClaim()
    {
        var svc = new Mock<IArticleService>();
        var controller = Build(svc.Object, sub: null);

        var dto = new CreateArticleDto("t", "c", "d", "x", "Public");
        var result = await controller.CreateArticle(dto);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusOf(result));
        svc.Verify(s => s.SaveArticleAsync(It.IsAny<Article>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateArticle_ReturnsBadRequest_WhenAccessLevelInvalid()
    {
        var svc = new Mock<IArticleService>();
        var controller = Build(svc.Object);

        var dto = new CreateArticleDto("t", "c", "d", "x", "Garbage");
        var result = await controller.CreateArticle(dto);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        svc.Verify(s => s.SaveArticleAsync(It.IsAny<Article>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateArticle_PersistsAndReturnsCreated()
    {
        var svc = new Mock<IArticleService>();
        svc.Setup(s => s.SaveArticleAsync(It.IsAny<Article>(), AuthorId)).ReturnsAsync("new-id");
        var controller = Build(svc.Object);

        var dto = new CreateArticleDto("title", "hello world", "desc", "a,b", "Private");
        var result = await controller.CreateArticle(dto);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
        svc.Verify(s => s.SaveArticleAsync(
            It.Is<Article>(a =>
                a.Title == "title" &&
                a.Content == "hello world" &&
                a.AccessLevel == AccessLevel.Private),
            AuthorId), Times.Once);
    }

    [Fact]
    public async Task CreateArticle_Returns500_WhenServiceThrows()
    {
        var svc = new Mock<IArticleService>();
        svc.Setup(s => s.SaveArticleAsync(It.IsAny<Article>(), It.IsAny<string>()))
           .ThrowsAsync(new Exception("boom"));
        var controller = Build(svc.Object);

        var dto = new CreateArticleDto("t", "c", "d", "x", "Public");
        var result = await controller.CreateArticle(dto);

        Assert.Equal(StatusCodes.Status500InternalServerError, StatusOf(result));
    }

    [Fact]
    public async Task UpdateArticle_Unauthorized_WhenNoSub()
    {
        var svc = new Mock<IArticleService>();
        var controller = Build(svc.Object, sub: null);

        var dto = new UpdateArticleDto("t", "c", "d", "x", "Public", "Published");
        var result = await controller.UpdateArticle("id", dto);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusOf(result));
    }

    [Fact]
    public async Task UpdateArticle_BadRequest_WhenStatusInvalid()
    {
        var svc = new Mock<IArticleService>();
        var controller = Build(svc.Object);

        var dto = new UpdateArticleDto("t", "c", "d", "x", "Public", "Nonsense");
        var result = await controller.UpdateArticle("id", dto);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    [Fact]
    public async Task UpdateArticle_NotFound_WhenArticleMissing()
    {
        var svc = new Mock<IArticleService>();
        svc.Setup(s => s.GetArticleByIdAsync("id")).ThrowsAsync(new Exception("missing"));
        var controller = Build(svc.Object);

        var dto = new UpdateArticleDto("t", "c", "d", "x", "Public", "Published");
        var result = await controller.UpdateArticle("id", dto);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task UpdateArticle_Forbidden_WhenAuthorMismatch()
    {
        var svc = new Mock<IArticleService>();
        svc.Setup(s => s.GetArticleByIdAsync("id")).ReturnsAsync(SampleArticle(OtherUserId));
        var controller = Build(svc.Object);

        var dto = new UpdateArticleDto("t", "c", "d", "x", "Public", "Published");
        var result = await controller.UpdateArticle("id", dto);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        svc.Verify(s => s.UpdateArticleAsync(It.IsAny<Article>()), Times.Never);
    }

    [Fact]
    public async Task UpdateArticle_NoContent_OnSuccess()
    {
        var svc = new Mock<IArticleService>();
        svc.Setup(s => s.GetArticleByIdAsync("id")).ReturnsAsync(SampleArticle());
        var controller = Build(svc.Object);

        var dto = new UpdateArticleDto("new", "a b c d", "desc", "tag", "Protected", "Archived");
        var result = await controller.UpdateArticle("id", dto);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        svc.Verify(s => s.UpdateArticleAsync(It.Is<Article>(a =>
            a.Title == "new" &&
            a.WordCount == 4 &&
            a.AccessLevel == AccessLevel.Protected &&
            a.Status == ArticleStatus.Archived)), Times.Once);
    }

    [Fact]
    public async Task DeleteArticle_Unauthorized_WhenNoSub()
    {
        var svc = new Mock<IArticleService>();
        var controller = Build(svc.Object, sub: null);

        var result = await controller.DeleteArticle("id");

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusOf(result));
    }

    [Fact]
    public async Task DeleteArticle_NotFound_WhenMissing()
    {
        var svc = new Mock<IArticleService>();
        svc.Setup(s => s.GetArticleByIdAsync("id")).ThrowsAsync(new Exception("missing"));
        var controller = Build(svc.Object);

        var result = await controller.DeleteArticle("id");

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task DeleteArticle_Forbidden_WhenAuthorMismatch()
    {
        var svc = new Mock<IArticleService>();
        svc.Setup(s => s.GetArticleByIdAsync("id")).ReturnsAsync(SampleArticle(OtherUserId));
        var controller = Build(svc.Object);

        var result = await controller.DeleteArticle("id");

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        svc.Verify(s => s.ArchiveArticleAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeleteArticle_NoContent_OnSuccess()
    {
        var svc = new Mock<IArticleService>();
        svc.Setup(s => s.GetArticleByIdAsync("id")).ReturnsAsync(SampleArticle());
        var controller = Build(svc.Object);

        var result = await controller.DeleteArticle("id");

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        svc.Verify(s => s.ArchiveArticleAsync("id"), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(",,")]
    public async Task GetArticlesByIds_BadRequest_WhenIdsMissing(string? ids)
    {
        var svc = new Mock<IArticleService>();
        var controller = Build(svc.Object);

        var result = await controller.GetArticlesByIds(ids);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        svc.Verify(s => s.GetArticlesByIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task GetArticlesByIds_BadRequest_WhenOverLimit()
    {
        var svc = new Mock<IArticleService>();
        var controller = Build(svc.Object);

        var result = await controller.GetArticlesByIds(string.Join(',', Enumerable.Range(0, 101).Select(i => $"a{i}")));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        svc.Verify(s => s.GetArticlesByIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task GetArticlesByIds_PassesTrimmedIds_InOrder()
    {
        var svc = new Mock<IArticleService>();
        IEnumerable<string>? captured = null;
        svc.Setup(s => s.GetArticlesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .Callback<IEnumerable<string>>(ids => captured = ids)
            .ReturnsAsync(new[] { SampleArticle() });
        var controller = Build(svc.Object);

        var result = await controller.GetArticlesByIds("b , a,c");

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Equal(new[] { "b", "a", "c" }, captured);
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(0, 1)]
    [InlineData(9999, 100)]
    [InlineData(5, 5)]
    public async Task GetRecentArticles_ClampsLimit(int? limit, int expected)
    {
        var svc = new Mock<IArticleService>();
        svc.Setup(s => s.GetRecentArticlesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<Article>());
        var controller = Build(svc.Object);

        var result = await controller.GetRecentArticles(limit, offset: null);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        svc.Verify(s => s.GetRecentArticlesAsync(expected, 0), Times.Once);
    }

    [Fact]
    public async Task GetRecentArticles_ClampsNegativeOffset()
    {
        var svc = new Mock<IArticleService>();
        svc.Setup(s => s.GetRecentArticlesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<Article>());
        var controller = Build(svc.Object);

        await controller.GetRecentArticles(limit: 10, offset: -5);

        svc.Verify(s => s.GetRecentArticlesAsync(10, 0), Times.Once);
    }
}
