using System.Text.Json;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.EntityFrameworkCore;

public interface IArticleService
{
    Task<IEnumerable<Article>> GetArticlesByQueryAsync(string query);
    Task<Article> GetArticleByIdAsync(string id);
    Task<string> SaveArticleAsync(Article article, string authorId);
    Task UpdateArticleAsync(Article article);
    Task ArchiveArticleAsync(string id);
}

public class ArticleService : IArticleService
{
    private const string IndexName = "articles";

    private readonly ApplicationDbContext _db;
    private readonly ElasticsearchClient _elasticsearchClient;

    public ArticleService(ApplicationDbContext db, ElasticsearchClient elasticsearchClient)
    {
        _db = db;
        _elasticsearchClient = elasticsearchClient;
    }

    public async Task<IEnumerable<Article>> GetArticlesByQueryAsync(string query)
    {
        var response = await _elasticsearchClient.SearchAsync<Article>(s => s
            .Index(IndexName)
            .Source(new SourceConfig(false))
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

        var orderedIds = response.Hits.Select(h => h.Id).Where(id => !string.IsNullOrEmpty(id)).ToList();
        if (orderedIds.Count == 0) return Array.Empty<Article>();

        var articles = await _db.Articles.AsNoTracking()
            .Where(a => orderedIds.Contains(a.Id))
            .ToListAsync();

        var map = articles.ToDictionary(a => a.Id);
        return orderedIds.Where(map.ContainsKey).Select(id => map[id]);
    }

    public async Task<Article> GetArticleByIdAsync(string id)
    {
        var article = await _db.Articles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (article == null)
        {
            throw new KeyNotFoundException($"Article with id {id} not found");
        }
        return article;
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

        await WriteWithOutboxAsync(() => _db.Articles.Add(article), "ArticleCreated", article.Id);

        return article.Id;
    }

    public async Task UpdateArticleAsync(Article article)
    {
        article.UpdatedAt = DateTime.UtcNow;
        await WriteWithOutboxAsync(() => _db.Articles.Update(article), "ArticleUpdated", article.Id);
    }

    public async Task ArchiveArticleAsync(string id)
    {
        var article = await _db.Articles.FirstOrDefaultAsync(a => a.Id == id);
        if (article == null)
        {
            throw new KeyNotFoundException($"Article with id {id} not found");
        }

        article.ArchivedAt = DateTime.UtcNow;
        article.UpdatedAt = DateTime.UtcNow;
        article.Status = ArticleStatus.Archived;

        await WriteWithOutboxAsync(() => { }, "ArticleArchived", article.Id);
    }

    private async Task WriteWithOutboxAsync(Action mutate, string eventType, string aggregateId)
    {
        var supportsTransactions = _db.Database.IsRelational();
        var tx = supportsTransactions ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            mutate();
            _db.OutboxEvents.Add(new OutboxEvent
            {
                EventType = eventType,
                AggregateId = aggregateId,
                Payload = JsonSerializer.Serialize(new { id = aggregateId, eventType }),
                CreatedAt = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }
    }
}
