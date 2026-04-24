using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

public class ArticleWebApplicationFactory : WebApplicationFactory<Program>
{
    public InMemoryArticleService FakeService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            var existing = services.Where(d => d.ServiceType == typeof(IArticleService)).ToList();
            foreach (var d in existing) services.Remove(d);

            services.AddSingleton<IArticleService>(FakeService);
        });
    }
}
