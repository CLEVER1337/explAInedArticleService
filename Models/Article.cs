public class Article
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public string Description { get; set; }
    public string Tags { get; set; }
    public ArticleStatus Status { get; set; }
    public string AuthorId { get; set; }
    public DateTime PublishedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public float ComplexityScore { get; set; }
    public int WordCount { get; set; }
}

public enum ArticleStatus
{
    Draft,
    Published,
    Archived
}
