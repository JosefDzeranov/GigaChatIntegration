namespace GigaChatIntegration.GigaChat.Models
{

    internal record EmbeddingRequest(string Model, List<string> Input);
    internal record EmbeddingResponse(List<EmbeddingData> Data);
    internal record EmbeddingData(float[] Embedding, int Index);
}
