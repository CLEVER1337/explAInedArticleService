using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;

public class ArticleService
{
    private readonly ElasticsearchClient _elasticsearchClient;

    public ArticleService(ElasticsearchClient elasticsearchClient)
    {
        _elasticsearchClient = elasticsearchClient;
    }

    public async Task<IEnumerable<Article>> GetArticlesByQueryAsync(string query)
    {
        var response = await _elasticsearchClient.SearchAsync<Article>(s => s
            .Index("articles")
            .Query(q => q
                .Bool(b => b
                    .Must(m => m
                        .SimpleQueryString(sqs => sqs
                            .Query(query)
                            .Fields(new[] { "title^3", "content", "description", "tags^2" })
                            .DefaultOperator(Operator.And)
                        )
                    )
                    .Filter(f => f
                        .Term(t => t.Field("status").Value(ArticleStatus.Published.ToString()))
                    )
                )
            )
        );
        return response.Documents;
    }

    public async Task<Article> GetArticleByIdAsync(string id)
    {
        var response = await _elasticsearchClient.GetAsync<Article>(id, g => g.Index("articles"));

        if (!response.IsValidResponse)
        {
            throw new Exception($"Failed to get article with id {id}");
        }

        return response.Source;
    }

    public async Task<string> SaveArticleAsync(Article article, string authorId)
    {
        article.Id = Guid.NewGuid().ToString(); 
        article.PublishedAt = DateTime.UtcNow;
        article.UpdatedAt = DateTime.UtcNow;
        article.ArchivedAt = null;
        article.ComplexityScore = 0;
        article.WordCount = article.Content.Split(' ').Length;
        article.AuthorId = authorId;
        article.Status = ArticleStatus.Draft;

        var response = await _elasticsearchClient.IndexAsync(article, i => i.Index("articles"));

        if (!response.IsValidResponse)
        {
            throw new Exception($"Failed to save article with id {article.Id}");
        }

        return article.Id;
    }

    public async Task UpdateArticleAsync(Article article)
    {
        var response = await _elasticsearchClient.UpdateAsync<Article, Article>("articles", article.Id, u => u.Doc(article));

        if (!response.IsValidResponse)
        {
            throw new Exception($"Failed to update article with id {article.Id}");
        }
    }

    public async Task ArchiveArticleAsync(string id)
    {
        var article = await GetArticleByIdAsync(id);
        article.ArchivedAt = DateTime.UtcNow;
        article.Status = ArticleStatus.Archived;
        await UpdateArticleAsync(article);
    }

    private async Task DeleteArticleAsync(string id)
    {
        var response = await _elasticsearchClient.DeleteAsync(id, d => d.Index("articles"));

        if (!response.IsValidResponse)
        {
            throw new Exception($"Failed to delete article with id {id}");
        }
    }
}