using System.ClientModel;
using System.Text;
using ApiDuplicateDetector.Models;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;

namespace ApiDuplicateDetector.Services;

/// <summary>
/// Service for generating embeddings using Azure OpenAI.
/// Uses text-embedding-ada-002 or text-embedding-3-small model.
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _embeddingClient;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly string _modelName;

    public EmbeddingService(ILogger<EmbeddingService> logger)
    {
        _logger = logger;
        
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT not configured");
        var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
            ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY not configured");
        _modelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL") 
            ?? "text-embedding-ada-002";

        // Create the Azure OpenAI client
        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new ApiKeyCredential(apiKey));
        
        _embeddingClient = azureClient.GetEmbeddingClient(_modelName);
        
        _logger.LogInformation("EmbeddingService initialized with model: {Model}", _modelName);
    }

    /// <inheritdoc/>
    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Empty text provided for embedding generation");
            return Array.Empty<float>();
        }

        try
        {
            // Truncate text if too long (max 8192 tokens)
            var truncatedText = TruncateText(text, 8000);
            
            var response = await _embeddingClient.GenerateEmbeddingAsync(truncatedText);
            var embedding = response.Value.ToFloats().ToArray();
            
            _logger.LogDebug("Generated embedding with {Dimensions} dimensions", embedding.Length);
            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding for text");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts)
    {
        var textList = texts.ToList();
        if (!textList.Any())
            return new List<float[]>();

        try
        {
            // Process in batches of 100 (Azure OpenAI limit)
            var embeddings = new List<float[]>();
            var batches = textList.Chunk(100);

            foreach (var batch in batches)
            {
                var truncatedBatch = batch.Select(t => TruncateText(t, 8000)).ToList();
                var response = await _embeddingClient.GenerateEmbeddingsAsync(truncatedBatch);
                
                foreach (var item in response.Value)
                {
                    embeddings.Add(item.ToFloats().ToArray());
                }
            }

            _logger.LogInformation("Generated {Count} embeddings", embeddings.Count);
            return embeddings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating batch embeddings");
            throw;
        }
    }

    /// <inheritdoc/>
    public string CreateEmbeddingText(ApiInfo apiInfo)
    {
        var sb = new StringBuilder();
        
        // Add API metadata
        sb.AppendLine($"API Name: {apiInfo.Name}");
        if (!string.IsNullOrEmpty(apiInfo.Title))
            sb.AppendLine($"Title: {apiInfo.Title}");
        if (!string.IsNullOrEmpty(apiInfo.Description))
            sb.AppendLine($"Description: {apiInfo.Description}");
        if (!string.IsNullOrEmpty(apiInfo.Kind))
            sb.AppendLine($"Type: {apiInfo.Kind}");
        
        // Add endpoints with details
        if (apiInfo.Endpoints.Any())
        {
            sb.AppendLine("\nEndpoints:");
            foreach (var endpoint in apiInfo.Endpoints)
            {
                sb.AppendLine($"- {endpoint.Method} {endpoint.Path}");
                if (!string.IsNullOrEmpty(endpoint.Summary))
                    sb.AppendLine($"  Summary: {endpoint.Summary}");
                if (!string.IsNullOrEmpty(endpoint.Description))
                    sb.AppendLine($"  Description: {endpoint.Description}");
                if (!string.IsNullOrEmpty(endpoint.OperationId))
                    sb.AppendLine($"  OperationId: {endpoint.OperationId}");
            }
        }
        
        // Add schemas
        if (apiInfo.Schemas.Any())
        {
            sb.AppendLine("\nData Models:");
            sb.AppendLine(string.Join(", ", apiInfo.Schemas));
        }
        
        return sb.ToString();
    }

    /// <inheritdoc/>
    public double CalculateCosineSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1.Length != embedding2.Length || embedding1.Length == 0)
            return 0;

        double dotProduct = 0;
        double magnitude1 = 0;
        double magnitude2 = 0;

        for (int i = 0; i < embedding1.Length; i++)
        {
            dotProduct += embedding1[i] * embedding2[i];
            magnitude1 += embedding1[i] * embedding1[i];
            magnitude2 += embedding2[i] * embedding2[i];
        }

        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0;

        // Cosine similarity ranges from -1 to 1, normalize to 0 to 1
        var similarity = dotProduct / (magnitude1 * magnitude2);
        return (similarity + 1) / 2; // Normalize to 0-1 range
    }

    /// <summary>
    /// Truncates text to approximately the specified number of characters.
    /// </summary>
    private string TruncateText(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;
        
        return text[..maxChars] + "...";
    }
}
