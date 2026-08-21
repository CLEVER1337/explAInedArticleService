using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

public interface IUserEventProducer
{
    Task EmitAsync(
        string eventType,
        string userId,
        string articleId,
        IDictionary<string, object?>? metadata = null,
        CancellationToken ct = default);
}

public class UserEventProducer : IUserEventProducer
{
    private const string Source = "article-service";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly IProducer<string, string> _producer;
    private readonly KafkaSettings _settings;
    private readonly ILogger<UserEventProducer> _logger;

    public UserEventProducer(
        IProducer<string, string> producer,
        IOptions<KafkaSettings> settings,
        ILogger<UserEventProducer> logger)
    {
        _producer = producer;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task EmitAsync(
        string eventType,
        string userId,
        string articleId,
        IDictionary<string, object?>? metadata = null,
        CancellationToken ct = default)
    {
        var envelope = new UserEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: eventType,
            UserId: userId,
            ArticleId: articleId,
            OccurredAt: DateTime.UtcNow,
            Source: Source,
            Metadata: metadata);

        var json = JsonSerializer.Serialize(envelope, JsonOptions);

        var message = new Message<string, string>
        {
            Key = userId,
            Value = json,
            Headers = new Headers
            {
                new Header("eventType", Encoding.UTF8.GetBytes(eventType)),
                new Header("source", Encoding.UTF8.GetBytes(Source)),
            },
        };

        try
        {
            await _producer.ProduceAsync(_settings.UserEventsTopic, message, ct);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogWarning(ex, "Failed to produce user event {EventType} for user {UserId} article {ArticleId}", eventType, userId, articleId);
        }
        catch (KafkaException ex)
        {
            _logger.LogWarning(ex, "Kafka error producing user event {EventType}", eventType);
        }
        catch (OperationCanceledException)
        {
            // request cancelled — swallow
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error producing user event {EventType}", eventType);
        }
    }
}
