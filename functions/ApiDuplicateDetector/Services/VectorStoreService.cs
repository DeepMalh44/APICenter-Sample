using System.Text.Json;
using ApiDuplicateDetector.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace ApiDuplicateDetector.Services;

/// <summary>
/// Service for storing and searching API embeddings in Azure Cosmos DB.
/// Uses manual cosine similarity calculation since vector search may not be available.
/// </summary>
public class VectorStoreService : IVectorStoreService
{
    private readonly CosmosClient _cosmosClient;
    private readonly ILogger<VectorStoreService> _logger;
    private readonly string _databaseName;
    private readonly string _containerName;
    private Container? _container;
    private bool _initialized = false;

    public VectorStoreService(ILogger<VectorStoreService> logger)
    {
        _logger = logger;
        
        var connectionString = Environment.GetEnvironmentVariable("COSMOS_DB_CONNECTION_STRING")
            ?? throw new InvalidOperationException("COSMOS_DB_CONNECTION_STRING not configured");
        
        _databaseName = Environment.GetEnvironmentVariable("COSMOS_DB_DATABASE_NAME") ?? "ApiDuplicateDetector";
        _containerName = Environment.GetEnvironmentVariable("COSMOS_DB_CONTAINER_NAME") ?? "ApiEmbeddings";

        _cosmosClient = new CosmosClient(connectionString, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        });
        
        _logger.LogInformation("VectorStoreService initialized");
    }

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        try
        {
            var databaseResponse = await _cosmosClient.CreateDatabaseIfNotExistsAsync(_databaseName);
            var database = databaseResponse.Database;
            
            var containerProperties = new ContainerProperties(_containerName, "/apiName");
            var containerResponse = await database.CreateContainerIfNotExistsAsync(containerProperties);
            _container = containerResponse.Container;
            _initialized = true;
            
            _logger.LogInformation("Vector store initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing vector store");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task UpsertApiEmbeddingAsync(ApiEmbedding apiEmbedding)
    {
        await EnsureInitializedAsync();
        
        try
        {
            apiEmbedding.Timestamp = DateTime.UtcNow;
            await _container!.UpsertItemAsync(apiEmbedding, new PartitionKey(apiEmbedding.ApiName));
            _logger.LogInformation("Upserted embedding for API: {ApiName}", apiEmbedding.ApiName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting API embedding: {ApiName}", apiEmbedding.ApiName);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<List<SemanticMatch>> FindSimilarApisAsync(float[] queryEmbedding, int topK = 10, string? excludeApiName = null)
    {
        await EnsureInitializedAsync();
        
        // Use manual similarity calculation
        var allEmbeddings = await GetAllApiEmbeddingsAsync();
        var results = new List<SemanticMatch>();

        foreach (var apiEmbedding in allEmbeddings)
        {
            if (apiEmbedding.ApiName == excludeApiName || apiEmbedding.Embedding.Length == 0)
                continue;

            var similarity = CalculateCosineSimilarity(queryEmbedding, apiEmbedding.Embedding);
            results.Add(new SemanticMatch
            {
                ApiEmbedding = apiEmbedding,
                SimilarityScore = similarity
            });
        }

        return results.OrderByDescending(r => r.SimilarityScore).Take(topK).ToList();
    }

    private double CalculateCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0;

        double dotProduct = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        var magnitude = Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB);
        return magnitude == 0 ? 0 : dotProduct / magnitude;
    }

    /// <inheritdoc/>
    public async Task<ApiEmbedding?> GetApiEmbeddingAsync(string apiName)
    {
        await EnsureInitializedAsync();
        
        try
        {
            var response = await _container!.ReadItemAsync<ApiEmbedding>(
                apiName, new PartitionKey(apiName));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting API embedding: {ApiName}", apiName);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<List<ApiEmbedding>> GetAllApiEmbeddingsAsync()
    {
        await EnsureInitializedAsync();
        
        var results = new List<ApiEmbedding>();

        try
        {
            var query = new QueryDefinition("SELECT * FROM c");
            var iterator = _container!.GetItemQueryIterator<ApiEmbedding>(query);
            
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all API embeddings");
            throw;
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task DeleteApiEmbeddingAsync(string apiName)
    {
        await EnsureInitializedAsync();
        
        try
        {
            await _container!.DeleteItemAsync<ApiEmbedding>(apiName, new PartitionKey(apiName));
            _logger.LogInformation("Deleted embedding for API: {ApiName}", apiName);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("API embedding not found for deletion: {ApiName}", apiName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting API embedding: {ApiName}", apiName);
            throw;
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_initialized)
        {
            await InitializeAsync();
        }
    }
}
