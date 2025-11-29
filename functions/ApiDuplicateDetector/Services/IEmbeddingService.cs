using ApiDuplicateDetector.Models;

namespace ApiDuplicateDetector.Services;

/// <summary>
/// Service interface for generating and managing embeddings using Azure OpenAI.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generates an embedding vector for the given text using Azure OpenAI.
    /// </summary>
    /// <param name="text">The text to generate an embedding for.</param>
    /// <returns>The embedding vector (1536 dimensions for text-embedding-ada-002).</returns>
    Task<float[]> GenerateEmbeddingAsync(string text);
    
    /// <summary>
    /// Generates embeddings for multiple texts in a batch.
    /// </summary>
    /// <param name="texts">The texts to generate embeddings for.</param>
    /// <returns>List of embedding vectors.</returns>
    Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts);
    
    /// <summary>
    /// Creates a combined text representation of an API for embedding generation.
    /// </summary>
    /// <param name="apiInfo">The API information.</param>
    /// <returns>A single text string combining all relevant API information.</returns>
    string CreateEmbeddingText(ApiInfo apiInfo);
    
    /// <summary>
    /// Calculates cosine similarity between two embedding vectors.
    /// </summary>
    double CalculateCosineSimilarity(float[] embedding1, float[] embedding2);
}
