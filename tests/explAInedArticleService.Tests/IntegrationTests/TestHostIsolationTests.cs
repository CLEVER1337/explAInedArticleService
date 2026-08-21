using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

// The guard the profile service has had all along, and that this one did not.
//
// Without it ArticleWebApplicationFactory quietly built a host on Npgsql and Redis: every
// host-backed test in this project ran against the developer's real `articles` database and
// passed, because a developer machine has PostgreSQL on 127.0.0.1:5432. The first CI runner
// without one turned 23 of them red at once.
//
// These four assertions cost milliseconds and fail loudly the moment that regresses.
public class TestHostIsolationTests : IClassFixture<ArticleWebApplicationFactory>
{
    private readonly ArticleWebApplicationFactory _factory;

    public TestHostIsolationTests(ArticleWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void TheDatabaseIsInMemory()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.False(db.Database.IsRelational());
        Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", db.Database.ProviderName);
    }

    [Fact]
    public void EachFactoryGetsItsOwnDatabase()
    {
        using var other = new ArticleWebApplicationFactory();

        Assert.NotEqual(_factory.InMemoryDbName, other.InMemoryDbName);
    }

    [Fact]
    public void TheCacheIsInProcess()
    {
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

        Assert.IsType<MemoryDistributedCache>(cache);
    }

    [Fact]
    public void TheArticleServiceIsTheTestDouble()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.Same(_factory.FakeService, scope.ServiceProvider.GetRequiredService<IArticleService>());
    }
}
