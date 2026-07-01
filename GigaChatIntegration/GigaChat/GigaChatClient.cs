using GigaChatIntegration.GigaChat.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GigaChatIntegration.GigaChat
{
    internal class GigaChatClient
    {
        private const string OauthUrl = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";
        private const string ChatUrl = "https://gigachat.devices.sberbank.ru/api/v1/chat/completions";
        private const string EmbeddingsUrl = "https://gigachat.devices.sberbank.ru/api/v1/embeddings";

        // camelCase + НЕ сериализуем null-поля (function_call/functions/name), иначе GigaChat
        // получит лишние null и капризничает. AllowTrailingCommas/ReadComment — терпимее к ответу.
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        // Обход проверки сертификата — ТОЛЬКО для учёбы (как в Дни 1–3). В проде ставят
        // корневой сертификат НУЦ Минцифры, а этот обход убирают.
        private readonly HttpClient http = new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        });

        public GigaChatClient(string authKey)
        {
            string token = GetAccessToken(authKey);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // ТЕКСТЫ → ВЕКТОРЫ(День 3).
        public float[][] Embed(List<string> texts)
        {
            var result = Post<EmbeddingResponse>(EmbeddingsUrl, new EmbeddingRequest("Embeddings", texts));
            return result.Data.OrderBy(d => d.Index).Select(d => d.Embedding).ToArray();
        }

        // Чат С функциями (День 2): модель вернёт либо текст, либо запрос вызвать нашу функцию.
        public ChatMessage ChatWithFunctions(List<ChatMessage> messages, List<FunctionDef> functions)
        {
            var body = new ChatRequest("GigaChat", messages, functions, FunctionCallMode: "auto");
            return Post<ChatResponse>(ChatUrl, body).Choices[0].Message;
        }

        // Обычный чат БЕЗ функций — для финального ответа, чтобы модель не зациклилась на вызовах.
        public string Chat(List<ChatMessage> messages)
        {
            var result = Post<ChatResponse>(ChatUrl, new ChatRequest("GigaChat", messages));
            return result.Choices[0].Message.Content ?? "";
        }

        // Общий POST: сериализуем тело, шлём, разбираем ответ в T.
        private T Post<T>(string url, object body)
        {
            string json = JsonSerializer.Serialize(body, JsonOpts);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            using var response = http.Send(request);
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<T>(ReadBody(response), JsonOpts)!;
        }

        static string ReadBody(HttpResponseMessage response)
        {
            using var stream = response.Content.ReadAsStream();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }


        private static string GetAccessToken(string authKey)
        {
            string authUrl = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";

            using var request = new HttpRequestMessage(HttpMethod.Post, authUrl);

            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authKey);
            request.Headers.Add("RqUID", Guid.NewGuid().ToString());

            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["scope"] = "GIGACHAT_API_PERS",
            });

            HttpClient httpClient = new(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });

            using var response = httpClient.Send(request);
            response.EnsureSuccessStatusCode();

            var token = JsonSerializer.Deserialize<TokenResponse>(ReadBody(response), JsonOpts);

            return token.AccessToken;
        }
    }
}