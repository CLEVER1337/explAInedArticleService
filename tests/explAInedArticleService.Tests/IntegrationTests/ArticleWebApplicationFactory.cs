using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public class ArticleWebApplicationFactory : WebApplicationFactory<Program>
{
    // A name per factory instance, so two test classes running side by side cannot see each
    // other's rows. The service under test is faked below, but Program.cs still builds the
    // real DbContext and calls EnsureCreated on it at startup.
    public string InMemoryDbName { get; } = $"explAIned-articles-tests-{Guid.NewGuid()}";

    public InMemoryArticleService FakeService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Without this block the host builds ApplicationDbContext with Npgsql and Program.cs
        // runs db.Database.Migrate() against whatever ConnectionStrings:PostgreSQL points at
        // — 127.0.0.1:5432. On a developer machine that resolves, so the suite passed while
        // quietly reading and writing the real `articles` database; on a CI runner with no
        // PostgreSQL every host-backed test failed at construction.
        //
        // Cache:Provider matters for the same reason: Redis on localhost, present in one
        // place and absent in the other.
        //
        // This mirrors ProfileWebApplicationFactory, and the reason both must look like this
        // is in CLAUDE.md — the provider switch reads from the service provider rather than
        // from builder.Configuration precisely so that an override placed here wins.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "InMemory",
                ["Database:InMemoryName"] = InMemoryDbName,
                ["Cache:Provider"] = "Memory",
                ["Jwt:Key"] = "JwtSecretPlaceHolder_explAIned32",
                ["Jwt:Issuer"] = "http://localhost:5125/",
                ["Jwt:Audience"] = "http://localhost:5125/",
            });
        });

        builder.ConfigureServices(services =>
        {
            var existing = services.Where(d => d.ServiceType == typeof(IArticleService)).ToList();
            foreach (var d in existing) services.Remove(d);

            services.AddSingleton<IArticleService>(FakeService);

            // The outbox publisher and the Elasticsearch indexer are registered
            // unconditionally in Program.cs and would spend the test run dialling a Kafka
            // and an Elasticsearch that are not there. Nothing under test goes through
            // them — the service they would publish for is faked above.
            var hosted = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                            && (d.ImplementationType == typeof(OutboxPublisherHostedService)
                                || d.ImplementationType == typeof(ElasticsearchIndexerHostedService)))
                .ToList();
            foreach (var d in hosted) services.Remove(d);
        });
    }
}
