using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator.core
{
    /// <summary>
    /// Bridges the engine abstractions to the real API dispatcher and SQLite storage.
    /// </summary>
    public class LiveCaptionsTranslationApi : ITranslationApi
    {
        public bool IsLLMBased => apis.TranslateAPI.IsLLMBased;

        public Task<string> TranslateAsync(string text, CancellationToken token = default) =>
            apis.TranslateAPI.TranslateFunction(text, token);
    }

    public class SqliteHistoryStore : IHistoryStore
    {
        public async Task LogAsync(string originalText, string translatedText, string targetLanguage,
            string apiName, bool isOverwrite, CancellationToken token = default)
        {
            if (isOverwrite)
                await SQLiteHistoryLogger.DeleteLastTranslation(token);
            await SQLiteHistoryLogger.LogTranslation(originalText, translatedText, targetLanguage, apiName);
        }

        public async Task LogOnlyAsync(string originalText, bool isOverwrite, CancellationToken token = default)
        {
            if (isOverwrite)
                await SQLiteHistoryLogger.DeleteLastTranslation(token);
            await SQLiteHistoryLogger.LogTranslation(originalText, "N/A", "N/A", "LogOnly");
        }

        public Task<string?> LoadLastSourceTextAsync(CancellationToken token = default) =>
            SQLiteHistoryLogger.LoadLastSourceText(token);

        public Task<TranslationHistoryEntry?> LoadLastTranslationAsync(CancellationToken token = default) =>
            SQLiteHistoryLogger.LoadLastTranslation(token);
    }
}
