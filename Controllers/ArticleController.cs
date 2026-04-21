using Microsoft.AspNetCore.Mvc;

public class ArticleController : Controller
{
    private readonly ArticleService _articleService;

    public ArticleController(ArticleService articleService)
    {
        _articleService = articleService;
    }
    
    [HttpGet]
    [Route("articles/search?query={query}")]
    public async Task<IResult> SearchArticles([FromQuery] string query)
    {
        var articles = await _articleService.GetArticlesByQueryAsync(query);

        if (articles.Count() == 0)
        {
            return Results.NotFound("No articles found");
        }

        return Results.Ok(articles);
    }

    [HttpGet]
    [Route("article/{id}")]
    public async Task<IResult> GetArticleById([FromRoute] string id)
    {
        try{
            var article = await _articleService.GetArticleByIdAsync(id);
            return Results.Ok(article);
        }
        catch (Exception ex)
        {
            return Results.NotFound(ex.Message);
        }
    }
}