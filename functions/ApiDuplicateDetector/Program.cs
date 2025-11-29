using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.Identity;
using ApiDuplicateDetector.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        
        // Register Azure credential for managed identity
        services.AddSingleton(new DefaultAzureCredential());
        
        // Check if semantic analysis is enabled
        var enableSemantic = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT"));
        
        if (enableSemantic)
        {
            // Register semantic analysis services (Azure OpenAI + Cosmos DB)
            services.AddSingleton<IEmbeddingService, EmbeddingService>();
            services.AddSingleton<IVectorStoreService, VectorStoreService>();
            
            // Register similarity service with semantic support
            services.AddSingleton<IApiSimilarityService>(sp => 
                new ApiSimilarityService(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ApiSimilarityService>>(),
                    sp.GetRequiredService<IEmbeddingService>(),
                    sp.GetRequiredService<IVectorStoreService>()));
        }
        else
        {
            // Register similarity service without semantic support (structural only)
            services.AddSingleton<IApiSimilarityService, ApiSimilarityService>();
        }
        
        // Register other services
        services.AddSingleton<IApiCenterService, ApiCenterService>();
        services.AddSingleton<INotificationService, NotificationService>();
    })
    .Build();

host.Run();
