using ApiDuplicateDetector.Models;

namespace ApiDuplicateDetector.Services;

/// <summary>
/// Service interface for storing and searching API embeddings in Cosmos DB.
/// </summary>
public interface IVectorStoreService
{
    /// <summary>
    /// Stores or updates an API embedding in the vector store.
    /// </summary>
    /// <param name="apiEmbedding">The API embedding to store.</param>
    Task UpsertApiEmbeddingAsync(ApiEmbedding apiEmbedding);
    
    /// <summary>
    /// Finds semantically similar APIs using vector search.
    /// </summary>
    /// <param name="queryEmbedding">The embedding vector to search with.</param>
    /// <param name="topK">Number of top results to return.</param>
    /// <param name="excludeApiName">API name to exclude from results (e.g., the query API itself).</param>
    /// <returns>List of semantic matches ordered by similarity.</returns>
    Task<List<SemanticMatch>> FindSimilarApisAsync(float[] queryEmbedding, int topK = 10, string? excludeApiName = null);
    
    /// <summary>
    /// Gets an API embedding by name.
    /// </summary>
    /// <param name="apiName">The API name.</param>
    /// <returns>The API embedding if found, null otherwise.</returns>
    Task<ApiEmbedding?> GetApiEmbeddingAsync(string apiName);
    
    /// <summary>
    /// Gets all API embeddings from the store.
    /// </summary>
    /// <returns>List of all API embeddings.</returns>
    Task<List<ApiEmbedding>> GetAllApiEmbeddingsAsync();
    
    /// <summary>
    /// Deletes an API embedding from the store.
    /// </summary>
    /// <param name="apiName">The API name.</param>
    Task DeleteApiEmbeddingAsync(string apiName);
    
    /// <summary>
    /// Initializes the vector store (creates database/container if needed).
    /// </summary>
    Task InitializeAsync();
}
