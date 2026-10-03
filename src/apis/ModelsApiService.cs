using System.Net.Http;
using System.Text.Json;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator.apis
{
    /// <summary>
    /// Service for fetching model lists from APIs that expose a models endpoint
    /// (LMStudio, Ollama, etc.).
    /// </summary>
    public static class ModelsApiService
    {
        private static readonly HttpClient client = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        /// <summary>
        /// APIs that support fetching models from an endpoint.
        /// </summary>
        public static readonly List<string> APIs_WITH_MODELS_ENDPOINT = new()
        {
            "LMStudio",
            "Ollama"
        };

        /// <summary>
        /// Gets the models endpoint URL for an API.
        /// </summary>
        public static string GetModelsEndpoint(string apiName, string baseUrl)
        {
            return apiName switch
            {
                "LMStudio" => TextUtil.NormalizeUrl(baseUrl) + "/models",
                "Ollama" => TextUtil.NormalizeUrl(baseUrl) + "/api/tags",
                _ => null
            };
        }

        /// <summary>
        /// Gets the list of available models from the API.
        /// </summary>
        /// <param name="apiName">API name (LMStudio, Ollama, etc.)</param>
        /// <param name="baseUrl">Base URL of the API</param>
        /// <returns>List of model identifiers usable in chat, or an empty list on failure.</returns>
        public static async Task<List<ModelInfo>> FetchModelsAsync(string apiName, string baseUrl, CancellationToken token = default)
        {
            string endpoint = GetModelsEndpoint(apiName, baseUrl);
            if (string.IsNullOrEmpty(endpoint))
                return new List<ModelInfo>();

            try
            {
                var response = await client.GetAsync(endpoint, token);
                if (!response.IsSuccessStatusCode)
                {
                    FileLogger.Warn($"Failed to fetch models from {endpoint}: HTTP {(int)response.StatusCode}");
                    return new List<ModelInfo>();
                }

                string json = await response.Content.ReadAsStringAsync(token);

                return apiName switch
                {
                    "LMStudio" => ParseLMStudioModels(json),
                    "Ollama" => ParseOllamaModels(json),
                    _ => new List<ModelInfo>()
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                FileLogger.Warn($"Failed to fetch models from {endpoint}: {ex.Message}");
                return new List<ModelInfo>();
            }
        }

        public class ModelInfo
        {
            public string Id { get; set; }
            public string DisplayName { get; set; }
        }

        private static List<ModelInfo> ParseLMStudioModels(string json)
        {
            var result = new List<ModelInfo>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("models", out var modelsArray))
                    return result;

                foreach (var model in modelsArray.EnumerateArray())
                {
                    string type = model.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                    if (type != "llm")
                        continue;

                    string key = model.TryGetProperty("key", out var keyProp) ? keyProp.GetString() : null;
                    if (string.IsNullOrEmpty(key))
                        continue;

                    string displayName = model.TryGetProperty("display_name", out var dnProp) ? dnProp.GetString() : key;

                    result.Add(new ModelInfo { Id = key, DisplayName = displayName ?? key });
                }
            }
            catch (Exception ex)
            {
                FileLogger.Warn($"Failed to parse LMStudio models response: {ex.Message}");
            }

            return result;
        }

        private static List<ModelInfo> ParseOllamaModels(string json)
        {
            var result = new List<ModelInfo>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("models", out var modelsArray))
                    return result;

                foreach (var model in modelsArray.EnumerateArray())
                {
                    string name = model.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    if (string.IsNullOrEmpty(name))
                        continue;

                    result.Add(new ModelInfo { Id = name, DisplayName = name });
                }
            }
            catch (Exception ex)
            {
                FileLogger.Warn($"Failed to parse Ollama models response: {ex.Message}");
            }

            return result;
        }
    }
}
