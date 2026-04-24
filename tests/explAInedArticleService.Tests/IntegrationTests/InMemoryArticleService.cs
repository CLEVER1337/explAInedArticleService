using System.Collections.Concurrent;

public class InMemoryArticleService : IArticleService
{
    public ConcurrentDictionary<string, Article> Store { get; } = new();

    public Task<IEnumerable<Article>> GetArticlesByQueryAsync(string query)
    {
        var matches = Store.Values.Where(a =>
            a.Status == ArticleStatus.Published &&
            ((a.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
             (a.Content?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
             (a.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
             (a.Tags?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)));
        return Task.FromResult<IEnumerable<Article>>(matches.ToList());
    }

    public Task<Article> GetArticleByIdAsync(string id)
    {
        if (!Store.TryGetValue(id, out var article))
            throw new Exception($"Failed to get article with id {id}");
        return Task.FromResult(article);
    }

    public Task<string> SaveArticleAsync(Article article, string authorId)
    {
        article.Id = Guid.NewGuid().ToString();
        article.AuthorId = authorId;
        article.PublishedAt = DateTime.UtcNow;
        article.UpdatedAt = DateTime.UtcNow;
        article.Status = ArticleStatus.Draft;
        article.WordCount = (article.Content ?? string.Empty).Split(' ').Length;
        Store[article.Id] = article;
        return Task.FromResult(article.Id);
    }

    public Task UpdateArticleAsync(Article article)
    {
        Store[article.Id] = article;
        return Task.CompletedTask;
    }

    public Task ArchiveArticleAsync(string id)
    {
        if (Store.TryGetValue(id, out var a))
        {
            a.Status = ArticleStatus.Archived;
            a.ArchivedAt = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Article Seed(Article article)
    {
        if (string.IsNullOrEmpty(article.Id)) article.Id = Guid.NewGuid().ToString();
        Store[article.Id] = article;
        return article;
    }
}
