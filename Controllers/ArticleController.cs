using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("articles")]
public class ArticleController : Controller
{
    private const int BatchMaxIds = 100;

    private readonly IArticleService _articleService;
    private readonly IUserEventProducer _userEventProducer;

    public ArticleController(IArticleService articleService, IUserEventProducer userEventProducer)
    {
        _articleService = articleService;
        _userEventProducer = userEventProducer;
    }

    [HttpGet]
    [Route("search")]
    public async Task<IResult> SearchArticles([FromQuery] string query)
    {
        var articles = await _articleService.GetArticlesByQueryAsync(query);

        if (!articles.Any())
        {
            return Results.NotFound("No articles found");
        }

        return Results.Ok(articles);
    }

    [HttpGet]
    [Route("batch")]
    public async Task<IResult> GetArticlesByIds([FromQuery] string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
        {
            return Results.BadRequest("ids is required");
        }

        var idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (idList.Length == 0)
        {
            return Results.BadRequest("ids is required");
        }

        if (idList.Length > BatchMaxIds)
        {
            return Results.BadRequest($"at most {BatchMaxIds} ids per request");
        }

        var articles = await _articleService.GetArticlesByIdsAsync(idList);

        return Results.Ok(articles);
    }

    [HttpGet]
    [Route("recent")]
    public async Task<IResult> GetRecentArticles([FromQuery] int? limit, [FromQuery] int? offset)
    {
        var effectiveLimit = Math.Clamp(limit ?? 20, 1, 100);
        var effectiveOffset = Math.Max(offset ?? 0, 0);

        var articles = await _articleService.GetRecentArticlesAsync(effectiveLimit, effectiveOffset);

        return Results.Ok(articles);
    }

    [HttpGet]
    [Route("{id}")]
    public async Task<IResult> GetArticleById([FromRoute] string id)
    {
        try
        {
            var article = await _articleService.GetArticleByIdAsync(id);
            return Results.Ok(article);
        }
        catch (Exception ex)
        {
            return Results.NotFound(ex.Message);
        }
    }

    [HttpPost]
    [Authorize]
    public async Task<IResult> CreateArticle([FromBody] CreateArticleDto dto)
    {
        var authorId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (authorId is null)
        {
            return Results.Unauthorized();
        }

        AccessLevel accessLevel;
        if (!Enum.TryParse(dto.AccessLevel, true, out accessLevel))
        {
            return Results.BadRequest("Invalid access level");
        }

        var article = new Article
        {
            Title = dto.Title,
            Content = dto.Content,
            Description = dto.Description,
            Tags = dto.Tags,
            AccessLevel = accessLevel,
        };

        try
        {
            var id = await _articleService.SaveArticleAsync(article, authorId);
            return Results.Created($"/articles/{id}", new { Id = id });
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    [HttpPut]
    [Route("{id}")]
    [Authorize]
    public async Task<IResult> UpdateArticle([FromRoute] string id, [FromBody] UpdateArticleDto dto)
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        AccessLevel accessLevel;
        if (!Enum.TryParse(dto.AccessLevel, true, out accessLevel))
        {
            return Results.BadRequest("Invalid access level");
        }

        ArticleStatus status;
        if (!Enum.TryParse(dto.Status, true, out status))
        {
            return Results.BadRequest("Invalid status");
        }

        Article existing;
        try
        {
            existing = await _articleService.GetArticleByIdAsync(id);
        }
        catch (Exception ex)
        {
            return Results.NotFound(ex.Message);
        }

        if (existing.AuthorId != userId)
        {
            return Results.Forbid();
        }

        existing.Title = dto.Title;
        existing.Content = dto.Content;
        existing.Description = dto.Description;
        existing.Tags = dto.Tags;
        existing.AccessLevel = accessLevel;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.WordCount = dto.Content.Split(' ').Length;
        existing.Status = status;

        try
        {
            await _articleService.UpdateArticleAsync(existing);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    [HttpDelete]
    [Route("{id}")]
    [Authorize]
    public async Task<IResult> DeleteArticle([FromRoute] string id)
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        Article existing;
        try
        {
            existing = await _articleService.GetArticleByIdAsync(id);
        }
        catch (Exception ex)
        {
            return Results.NotFound(ex.Message);
        }

        if (existing.AuthorId != userId)
        {
            return Results.Forbid();
        }

        try
        {
            await _articleService.ArchiveArticleAsync(id);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    [HttpPost]
    [Route("{id}/click")]
    [Authorize]
    public Task<IResult> Click([FromRoute] string id, CancellationToken ct)
        => EmitUserEvent(id, "ArticleClicked", metadata: null, success: Results.Accepted(), ct);

    [HttpPost]
    [Route("{id}/read")]
    [Authorize]
    public Task<IResult> Read([FromRoute] string id, CancellationToken ct)
        => EmitUserEvent(id, "ArticleRead", metadata: null, success: Results.Accepted(), ct);

    [HttpPost]
    [Route("{id}/like")]
    [Authorize]
    public Task<IResult> Like([FromRoute] string id, CancellationToken ct)
        => EmitUserEvent(id, "ArticleLiked", metadata: null, success: Results.NoContent(), ct);

    [HttpPost]
    [Route("{id}/dislike")]
    [Authorize]
    public Task<IResult> Dislike([FromRoute] string id, CancellationToken ct)
        => EmitUserEvent(id, "ArticleDisliked", metadata: null, success: Results.NoContent(), ct);

    [HttpPost]
    [Route("{id}/share")]
    [Authorize]
    public Task<IResult> Share([FromRoute] string id, [FromBody] ShareArticleDto? dto, CancellationToken ct)
    {
        IDictionary<string, object?>? metadata = null;
        if (!string.IsNullOrWhiteSpace(dto?.Channel))
        {
            metadata = new Dictionary<string, object?> { ["channel"] = dto.Channel };
        }
        return EmitUserEvent(id, "ArticleShared", metadata, success: Results.NoContent(), ct);
    }

    private async Task<IResult> EmitUserEvent(
        string articleId,
        string eventType,
        IDictionary<string, object?>? metadata,
        IResult success,
        CancellationToken ct)
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            await _articleService.GetArticleByIdAsync(articleId);
        }
        catch (Exception ex)
        {
            return Results.NotFound(ex.Message);
        }

        await _userEventProducer.EmitAsync(eventType, userId, articleId, metadata, ct);
        return success;
    }
}

public class ShareArticleDto
{
    public string? Channel { get; set; }
}
