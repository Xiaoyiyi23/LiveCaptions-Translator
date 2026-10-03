using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator.apis
{
    public static class TranslateAPI
    {
        /*
         * The key of this field is used as the content for `translateAPIBox` in the `SettingPage`.
         * If you'd like to add a new API, please insert the key-value pair here.
         */
        public static readonly Dictionary<string, Func<string, CancellationToken, Task<string>>>
            TRANSLATE_FUNCTIONS = new()
        {
            { "Google", Google },
            { "Google2", Google2 },
            { "Ollama", Ollama },
            { "OpenAI", OpenAI },
            { "LMStudio", LMStudio },
            { "DeepL", DeepL },
            { "OpenRouter", OpenRouter },
            { "Youdao", Youdao },
            { "MTranServer", MTranServer },
            { "Baidu", Baidu },
            { "LibreTranslate", LibreTranslate },
        };
        public static readonly List<string> LLM_BASED_APIS = new()
        {
            "Ollama", "OpenAI", "OpenRouter", "LMStudio"
        };
        public static readonly List<string> NO_CONFIG_APIS = new()
        {
            "Google", "Google2"
        };

        public static Func<string, CancellationToken, Task<string>> TranslateFunction =>
            TRANSLATE_FUNCTIONS[Translator.Setting.ApiName];
        public static bool IsLLMBased => LLM_BASED_APIS.Contains(Translator.Setting.ApiName);
        public static string Prompt => Translator.Setting.Prompt;

        private static readonly HttpClient client = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        // Index of the request format that last worked; guarded because
        // concurrent translations read and update it.
        private static readonly object openaiFallbackLock = new();
        private static int openai_fallback_index = 0;

        /*
         * Shared request helpers. Authentication headers must be set on each
         * `HttpRequestMessage` instead of `client.DefaultRequestHeaders`, which is
         * not safe to mutate while concurrent translations are in flight.
         */

        private static async Task<(HttpResponseMessage? response, string? error)> TrySendAsync(
            HttpRequestMessage request, CancellationToken token)
        {
            try
            {
                return (await client.SendAsync(request, token), null);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return (null, "[ERROR] " + LocalizationService.Get("Api.ErrTimeout"));
            }
            catch (Exception ex)
            {
                return (null, "[ERROR] " + string.Format(LocalizationService.Get("Api.ErrGeneric"), ex.Message));
            }
        }

        private static async Task<string> ReadAndParseResponse(HttpResponseMessage response,
            Func<string, string> parseResponse, CancellationToken token, bool includeBodyInError = false)
        {
            if (!response.IsSuccessStatusCode)
            {
                if (includeBodyInError)
                {
                    string errorBody = await response.Content.ReadAsStringAsync(token);
                    return "[ERROR] " + string.Format(
                        LocalizationService.Get("Api.ErrHttpWithBody"), response.StatusCode, errorBody);
                }
                return "[ERROR] " + string.Format(LocalizationService.Get("Api.ErrHttp"), response.StatusCode);
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(token);
            }
            catch (Exception ex)
            {
                return "[ERROR] " + string.Format(LocalizationService.Get("Api.ErrGeneric"), ex.Message);
            }

            try
            {
                return parseResponse(body);
            }
            catch (Exception ex)
            {
                return "[ERROR] " + string.Format(LocalizationService.Get("Api.ErrGeneric"), ex.Message);
            }
        }

        private static async Task<string> SendTranslationRequest(HttpRequestMessage request,
            Func<string, string> parseResponse, CancellationToken token, bool includeBodyInError = false)
        {
            var (response, error) = await TrySendAsync(request, token);
            if (error != null)
            {
                FileLogger.Info(error);
                return error;
            }

            string result = await ReadAndParseResponse(response!, parseResponse, token, includeBodyInError);
            if (result.StartsWith("[ERROR]"))
                FileLogger.Info(result);
            return result;
        }

        private static HttpRequestMessage BuildJsonRequest(HttpMethod method, string url, string jsonContent)
        {
            var request = new HttpRequestMessage(method, TextUtil.NormalizeUrl(url))
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };
            return request;
        }

        private static string SerializeRequest(object requestData) =>
            JsonSerializer.Serialize(requestData, requestData.GetType());

        private static string ComputeSign(string input)
        {
            return BitConverter.ToString(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(input)))
                .Replace("-", "").ToLower();
        }

        private static string ResolveTargetLanguage(Dictionary<string, string> supportedLanguages)
        {
            return supportedLanguages.TryGetValue(
                Translator.Setting.TargetLanguage, out var langValue) ? langValue : Translator.Setting.TargetLanguage;
        }

        private static List<BaseLLMConfig.Message> BuildChatMessages(string text, string language)
        {
            var messages = new List<BaseLLMConfig.Message>
            {
                new BaseLLMConfig.Message { role = "system", content = string.Format(Prompt, language) },
                new BaseLLMConfig.Message { role = "user", content = $"🔤 {text} 🔤" }
            };

            if (Translator.Setting.ContextAware)
            {
                foreach (var entry in Translator.Caption.AwareContexts)
                {
                    string translatedText = entry.TranslatedText;
                    if (translatedText.Contains("[ERROR]") || translatedText.Contains("[WARNING]"))
                        continue;
                    translatedText = RegexPatterns.NoticePrefix().Replace(translatedText, "");

                    messages.InsertRange(1, [
                        new BaseLLMConfig.Message { role = "user", content = $"🔤 {entry.SourceText} 🔤" },
                        new BaseLLMConfig.Message { role = "assistant", content = $"{translatedText}" }
                    ]);
                }
            }
            return messages;
        }

        public static async Task<string> OpenAI(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["OpenAI"] as OpenAIConfig;
            string language = ResolveTargetLanguage(OpenAIConfig.SupportedLanguages);
            var messages = BuildChatMessages(text, language);

            HttpResponseMessage? response = null;
            while (true)
            {
                int fallbackIndex;
                lock (openaiFallbackLock)
                {
                    fallbackIndex = openai_fallback_index;
                }
                string jsonContent = SerializeRequest(LLMRequestDataFactory.Create(fallbackIndex,
                    config.ModelName, messages, config.Temperature));
                using var request = BuildJsonRequest(HttpMethod.Post, config.ApiUrl, jsonContent);
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.ApiKey}");

                var (sent, error) = await TrySendAsync(request, token);
                if (error != null)
                {
                    FileLogger.Info(error);
                    return error;
                }
                response = sent!;
                if (response.StatusCode != HttpStatusCode.BadRequest &&
                    response.StatusCode != HttpStatusCode.UnprocessableEntity)
                    break;
                // Retry with another request format: some OpenAI-compatible providers
                // reject unknown fields with 400/422.
                await Task.Delay(15, token);

                bool exhausted;
                lock (openaiFallbackLock)
                {
                    openai_fallback_index = fallbackIndex + 1;
                    if (openai_fallback_index >= LLMRequestDataFactory.FallbackCount)
                    {
                        openai_fallback_index = 0;
                        exhausted = true;
                    }
                    else
                        exhausted = false;
                }
                if (exhausted)
                    break;
            }

            return await ReadAndParseResponse(response!, body =>
            {
                var responseObj = JsonSerializer.Deserialize<OpenAIConfig.Response>(body);
                return RegexPatterns.ModelThinking().Replace(responseObj.choices[0].message.content, "");
            }, token);
        }

        public static async Task<string> Ollama(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["Ollama"] as OllamaConfig;
            string language = ResolveTargetLanguage(OllamaConfig.SupportedLanguages);
            var messages = BuildChatMessages(text, language);

            var requestData = LLMRequestDataFactory.Create("Ollama", config.ModelName, messages, config.Temperature);
            requestData.keep_alive = config.keep_alive;
            var request = BuildJsonRequest(HttpMethod.Post, config.ApiUrl + "/api/chat",
                SerializeRequest(requestData));

            return await SendTranslationRequest(request, body =>
            {
                var responseObj = JsonSerializer.Deserialize<OllamaConfig.Response>(body);
                return RegexPatterns.ModelThinking().Replace(responseObj.message.content, "");
            }, token);
        }

        public static async Task<string> LMStudio(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["LMStudio"] as LMStudioConfig;
            string language = ResolveTargetLanguage(LMStudioConfig.SupportedLanguages);

            // LMStudio native chat takes a single prompt instead of a message list.
            string input = $"🔤 {text} 🔤";
            if (Translator.Setting.ContextAware)
            {
                var contextLines = new List<string>();
                foreach (var entry in Translator.Caption.AwareContexts)
                {
                    string translatedText = entry.TranslatedText;
                    if (translatedText.Contains("[ERROR]") || translatedText.Contains("[WARNING]"))
                        continue;
                    translatedText = RegexPatterns.NoticePrefix().Replace(translatedText, "");
                    contextLines.Add($"🔤 {entry.SourceText} 🔤 → {translatedText}");
                }
                if (contextLines.Count > 0)
                    input = string.Join("\n", contextLines) + "\n" + input;
            }

            var requestData = new
            {
                model = config.ModelName,
                system_prompt = string.Format(Prompt, language),
                input = input,
                temperature = config.Temperature
            };
            var request = BuildJsonRequest(HttpMethod.Post, config.ApiUrl + "/chat",
                JsonSerializer.Serialize(requestData));

            return await SendTranslationRequest(request, body =>
            {
                // LMStudio native /api/v1/chat response:
                // { "output": [ { "type": "message", "content": "..." }, ... ] }
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("output", out var outputArray) &&
                    outputArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in outputArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var typeProp) &&
                            typeProp.GetString() == "message" &&
                            item.TryGetProperty("content", out var contentProp))
                        {
                            return RegexPatterns.ModelThinking().Replace(contentProp.GetString() ?? "", "");
                        }
                    }
                }
                return "[ERROR] " + LocalizationService.Get("Api.ErrUnexpectedFormat");
            }, token, includeBodyInError: true);
        }

        public static async Task<string> OpenRouter(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["OpenRouter"] as OpenRouterConfig;
            string language = ResolveTargetLanguage(OpenRouterConfig.SupportedLanguages);
            var messages = BuildChatMessages(text, language);

            string jsonContent = SerializeRequest(LLMRequestDataFactory.Create(
                "OpenRouter", config.ModelName, messages, config.Temperature));
            var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config?.ApiKey}");

            return await SendTranslationRequest(request, body =>
            {
                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(body);
                var output = jsonResponse.GetProperty("choices")[0]
                                         .GetProperty("message")
                                         .GetProperty("content")
                                         .GetString() ?? string.Empty;
                return RegexPatterns.ModelThinking().Replace(output, "");
            }, token);
        }

        public static async Task<string> Google(string text, CancellationToken token = default)
        {
            var language = Translator.Setting?.TargetLanguage;

            string encodedText = Uri.EscapeDataString(text);
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=auto&tl={language}&q={encodedText}");

            return await SendTranslationRequest(request, body =>
            {
                var responseObj = JsonSerializer.Deserialize<List<List<string>>>(body);
                return responseObj[0][0];
            }, token);
        }

        // Unofficial endpoint extracted from the Google Dictionary Chrome extension.
        // It works out of the box but may stop working at any time.
        public static async Task<string> Google2(string text, CancellationToken token = default)
        {
            string apiKey = "AIzaSyA6EEtrDCfBkHV8uU2lgGY-N383ZgAOo7Y";
            var language = Translator.Setting?.TargetLanguage;

            string encodedText = Uri.EscapeDataString(text);
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://dictionaryextension-pa.googleapis.com/v1/dictionaryExtensionData?" +
                $"language={language}&key={apiKey}&term={encodedText}&strategy=2");
            request.Headers.TryAddWithoutValidation("x-referer", "chrome-extension://mgijmajocgfcbeboacabfgobmjgjcoja");

            return await SendTranslationRequest(request, body =>
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("translateResponse", out var translateResponse))
                    return translateResponse.GetProperty("translateText").GetString() ?? string.Empty;
                return "[ERROR] " + LocalizationService.Get("Api.ErrUnexpectedFormat");
            }, token);
        }

        public static async Task<string> DeepL(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["DeepL"] as DeepLConfig;
            string language = ResolveTargetLanguage(DeepLConfig.SupportedLanguages);

            var requestData = new
            {
                text = new[] { text },
                target_lang = language
            };
            var request = BuildJsonRequest(HttpMethod.Post, config.ApiUrl, JsonSerializer.Serialize(requestData));
            request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {config?.ApiKey}");

            return await SendTranslationRequest(request, body =>
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("translations", out var translations) &&
                    translations.ValueKind == JsonValueKind.Array && translations.GetArrayLength() > 0)
                {
                    return translations[0].GetProperty("text").GetString() ?? string.Empty;
                }
                return "[ERROR] " + LocalizationService.Get("Api.ErrNoFeedback");
            }, token);
        }

        public static async Task<string> Youdao(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["Youdao"] as YoudaoConfig;
            string language = ResolveTargetLanguage(YoudaoConfig.SupportedLanguages);

            string salt = Guid.NewGuid().ToString("N");
            string sign = ComputeSign($"{config.AppKey}{text}{salt}{config.AppSecret}");

            var parameters = new Dictionary<string, string>
            {
                ["q"] = text,
                ["from"] = "auto",
                ["to"] = language,
                ["appKey"] = config.AppKey,
                ["salt"] = salt,
                ["sign"] = sign
            };
            var request = new HttpRequestMessage(HttpMethod.Post, config.ApiUrl)
            {
                Content = new FormUrlEncodedContent(parameters)
            };

            return await SendTranslationRequest(request, body =>
            {
                var responseObj = JsonSerializer.Deserialize<YoudaoConfig.TranslationResult>(body);

                if (responseObj.errorCode != "0")
                    return "[ERROR] " + string.Format(
                        LocalizationService.Get("Api.ErrYoudao"), responseObj.errorCode);

                return responseObj.translation?.FirstOrDefault()
                    ?? "[ERROR] " + LocalizationService.Get("Api.ErrNoContent");
            }, token);
        }

        public static async Task<string> MTranServer(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["MTranServer"] as MTranServerConfig;
            string targetLanguage = ResolveTargetLanguage(MTranServerConfig.SupportedLanguages);
            string sourceLanguage = config.SourceLanguage;

            var requestData = new
            {
                text = text,
                to = targetLanguage,
                from = sourceLanguage
            };
            var request = BuildJsonRequest(HttpMethod.Post, config.ApiUrl, JsonSerializer.Serialize(requestData));
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config?.ApiKey}");

            return await SendTranslationRequest(request, body =>
            {
                var responseObj = JsonSerializer.Deserialize<MTranServerConfig.Response>(body);
                return responseObj.result;
            }, token);
        }

        public static async Task<string> Baidu(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["Baidu"] as BaiduConfig;
            string language = ResolveTargetLanguage(BaiduConfig.SupportedLanguages);

            string salt = Guid.NewGuid().ToString("N");
            string sign = ComputeSign($"{config.AppId}{text}{salt}{config.AppSecret}");

            var parameters = new Dictionary<string, string>
            {
                ["q"] = text,
                ["from"] = "auto",
                ["to"] = language,
                ["appid"] = config.AppId,
                ["salt"] = salt,
                ["sign"] = sign
            };
            var request = new HttpRequestMessage(HttpMethod.Post, config.ApiUrl)
            {
                Content = new FormUrlEncodedContent(parameters)
            };

            return await SendTranslationRequest(request, body =>
            {
                var responseObj = JsonSerializer.Deserialize<BaiduConfig.TranslationResult>(body);

                if (responseObj.error_code is not null && responseObj.error_code != "0")
                    return "[ERROR] " + string.Format(
                        LocalizationService.Get("Api.ErrBaidu"), responseObj.error_code);

                return responseObj.trans_result?.FirstOrDefault()?.dst
                    ?? "[ERROR] " + LocalizationService.Get("Api.ErrNoContent");
            }, token);
        }

        public static async Task<string> LibreTranslate(string text, CancellationToken token = default)
        {
            var config = Translator.Setting["LibreTranslate"] as LibreTranslateConfig;
            string targetLanguage = ResolveTargetLanguage(LibreTranslateConfig.SupportedLanguages);

            var requestData = new
            {
                q = text,
                target = targetLanguage,
                source = "auto",
                format = "text",
                api_key = config?.ApiKey
            };
            var request = BuildJsonRequest(HttpMethod.Post, config.ApiUrl, JsonSerializer.Serialize(requestData));

            return await SendTranslationRequest(request, body =>
            {
                var responseObj = JsonSerializer.Deserialize<LibreTranslateConfig.Response>(body);
                return responseObj.translatedText;
            }, token);
        }
    }

    public class ConfigDictConverter : JsonConverter<Dictionary<string, List<TranslateAPIConfig>>>
    {
        public override Dictionary<string, List<TranslateAPIConfig>> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Expected a StartObject token.");
            var configs = new Dictionary<string, List<TranslateAPIConfig>>();

            reader.Read();
            while (reader.TokenType == JsonTokenType.PropertyName)
            {
                string key = reader.GetString();
                reader.Read();

                var configType = Type.GetType($"LiveCaptionsTranslator.models.{key}Config");
                TranslateAPIConfig config;

                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    var list = new List<TranslateAPIConfig>();
                    reader.Read();

                    while (reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (configType != null && typeof(TranslateAPIConfig).IsAssignableFrom(configType))
                            config = (TranslateAPIConfig)JsonSerializer.Deserialize(ref reader, configType, options);
                        else
                            config = (TranslateAPIConfig)JsonSerializer.Deserialize(ref reader, typeof(TranslateAPIConfig), options);

                        list.Add(config);
                        reader.Read();
                    }
                    configs[key] = list;
                }
                else
                    throw new JsonException("Expected a StartObject token or a StartArray token.");

                reader.Read();
            }

            if (reader.TokenType != JsonTokenType.EndObject)
                throw new JsonException("Expected an EndObject token.");
            return configs;
        }

        public override void Write(
            Utf8JsonWriter writer, Dictionary<string, List<TranslateAPIConfig>> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var kvp in value)
            {
                writer.WritePropertyName(kvp.Key);
                var configType = Type.GetType($"LiveCaptionsTranslator.models.{kvp.Key}Config");

                if (kvp.Value is IEnumerable<TranslateAPIConfig> configList)
                {
                    writer.WriteStartArray();
                    foreach (var config in configList)
                    {
                        if (configType != null && typeof(TranslateAPIConfig).IsAssignableFrom(configType))
                            JsonSerializer.Serialize(writer, config, configType, options);
                        else
                            JsonSerializer.Serialize(writer, config, typeof(TranslateAPIConfig), options);
                    }
                    writer.WriteEndArray();
                }
                else
                    throw new JsonException($"Unsupported config type: {kvp.Value.GetType()}");
            }
            writer.WriteEndObject();
        }
    }
}
