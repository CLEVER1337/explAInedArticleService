using System.Text.Json;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.EntityFrameworkCore;

public interface IArticleService
{
    Task<IEnumerable<Article>> GetArticlesByQueryAsync(string query);
    Task<Article> GetArticleByIdAsync(string id);
    Task<IEnumerable<Article>> GetArticlesByIdsAsync(IEnumerable<string> ids);
    Task<IEnumerable<Article>> GetRecentArticlesAsync(int limit, int offset);
    Task<IEnumerable<Article>> GetArticlesByAuthorAsync(string authorId, int limit, int offset);
    Task<int> CountArticlesByAuthorAsync(string authorId);
    Task<string> SaveArticleAsync(Article article, string authorId);
    Task UpdateArticleAsync(Article article);
    Task ArchiveArticleAsync(string id);
}

public class ArticleService : IArticleService
{
    private const string IndexName = "articles";
    private const int CacheTtlSeconds = 60;
    private const int SearchCacheTtlSeconds = 30;
    private const int RecentCacheTtlSeconds = 30;
    private const int ByAuthorCacheTtlSeconds = 30;

    private readonly ApplicationDbContext _db;
    private readonly ElasticsearchClient _elasticsearchClient;
    private readonly CacheService _cache;
    private readonly ILogger<ArticleService> _logger;

    public ArticleService(
        ApplicationDbContext db,
        ElasticsearchClient elasticsearchClient,
        CacheService cache,
        ILogger<ArticleService> logger)
    {
        _db = db;
        _elasticsearchClient = elasticsearchClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IEnumerable<Article>> GetArticlesByQueryAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<Article>();
        }

        var key = SearchCacheKey(query);

        try
        {
            var cached = await _cache.GetValue(key);
            if (cached is not null)
            {
                var deserialized = JsonSerializer.Deserialize<List<Article>>(cached);
                if (deserialized is not null) return deserialized;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache read failed for {Key}", key);
        }

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

        var ordered = await FetchOrderedAsync(orderedIds!, publicPublishedOnly: false);

        try
        {
            await _cache.SetValue(key, JsonSerializer.Serialize(ordered), TimeSpan.FromSeconds(SearchCacheTtlSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache write failed for {Key}", key);
        }

        return ordered;
    }

    public async Task<Article> GetArticleByIdAsync(string id)
    {
        var key = ArticleCacheKey(id);

        try
        {
            var cached = await _cache.GetValue(key);
            if (cached is not null)
            {
                var deserialized = JsonSerializer.Deserialize<Article>(cached);
                if (deserialized is not null) return deserialized;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache read failed for {Key}", key);
        }

        var article = await _db.Articles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (article == null)
        {
            throw new KeyNotFoundException($"Article with id {id} not found");
        }

        try
        {
            await _cache.SetValue(key, JsonSerializer.Serialize(article), TimeSpan.FromSeconds(CacheTtlSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache write failed for {Key}", key);
        }

        return article;
    }

    public async Task<IEnumerable<Article>> GetArticlesByIdsAsync(IEnumerable<string> ids)
    {
        var orderedIds = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();

        if (orderedIds.Count == 0)
        {
            return Array.Empty<Article>();
        }

        return await FetchOrderedAsync(orderedIds, publicPublishedOnly: true);
    }

    public async Task<IEnumerable<Article>> GetRecentArticlesAsync(int limit, int offset)
    {
        var key = RecentCacheKey(limit, offset);

        try
        {
            var cached = await _cache.GetValue(key);
            if (cached is not null)
            {
                var deserialized = JsonSerializer.Deserialize<List<Article>>(cached);
                if (deserialized is not null) return deserialized;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache read failed for {Key}", key);
        }

        var articles = await _db.Articles.AsNoTracking()
            .Where(a => a.Status == ArticleStatus.Published && a.AccessLevel == AccessLevel.Public)
            .OrderByDescending(a => a.PublishedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        try
        {
            await _cache.SetValue(key, JsonSerializer.Serialize(articles), TimeSpan.FromSeconds(RecentCacheTtlSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache write failed for {Key}", key);
        }

        return articles;
    }

    public async Task<IEnumerable<Article>> GetArticlesByAuthorAsync(string authorId, int limit, int offset)
    {
        var key = ByAuthorCacheKey(authorId, limit, offset);

        try
        {
            var cached = await _cache.GetValue(key);
            if (cached is not null)
            {
                var deserialized = JsonSerializer.Deserialize<List<Article>>(cached);
                if (deserialized is not null) return deserialized;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache read failed for {Key}", key);
        }

        var articles = await ByAuthorQuery(authorId)
            .OrderByDescending(a => a.PublishedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        try
        {
            await _cache.SetValue(key, JsonSerializer.Serialize(articles), TimeSpan.FromSeconds(ByAuthorCacheTtlSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache write failed for {Key}", key);
        }

        return articles;
    }

    public Task<int> CountArticlesByAuthorAsync(string authorId) => ByAuthorQuery(authorId).CountAsync();

    private IQueryable<Article> ByAuthorQuery(string authorId) =>
        _db.Articles.AsNoTracking()
            .Where(a => a.AuthorId == authorId
                        && a.Status == ArticleStatus.Published
                        && a.AccessLevel == AccessLevel.Public);

    private async Task<List<Article>> FetchOrderedAsync(IReadOnlyList<string> orderedIds, bool publicPublishedOnly)
    {
        var query = _db.Articles.AsNoTracking().Where(a => orderedIds.Contains(a.Id));

        if (publicPublishedOnly)
        {
            query = query.Where(a => a.Status == ArticleStatus.Published && a.AccessLevel == AccessLevel.Public);
        }

        var articles = await query.ToListAsync();

        var map = articles.ToDictionary(a => a.Id);

        return orderedIds.Where(map.ContainsKey).Select(id => map[id]).ToList();
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
        await InvalidateArticleCache(article.Id);
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
        await InvalidateArticleCache(article.Id);
    }

    private async Task InvalidateArticleCache(string id)
    {
        try
        {
            await _cache.RemoveValue(ArticleCacheKey(id));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache invalidation failed for article {Id}", id);
        }
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

    private static string ArticleCacheKey(string id) => $"articles:{id}";
    private static string SearchCacheKey(string query) => $"articles:search:{query.Trim().ToLowerInvariant()}";

    private static string RecentCacheKey(int limit, int offset) => $"articles:recent:{limit}:{offset}";

    private static string ByAuthorCacheKey(string authorId, int limit, int offset) =>
        $"articles:by-author:{authorId}:{limit}:{offset}";
}
