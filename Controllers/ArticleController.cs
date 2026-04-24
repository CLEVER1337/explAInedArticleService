using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("articles")]
public class ArticleController : Controller
{
    private readonly IArticleService _articleService;

    public ArticleController(IArticleService articleService)
    {
        _articleService = articleService;
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
}
