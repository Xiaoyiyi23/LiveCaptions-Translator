using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.core
{
    /// <summary>
    /// Raw translation call. The engine owns context handling, the latency
    /// prefix and error wrapping on top of this.
    /// </summary>
    public interface ITranslationApi
    {
        bool IsLLMBased { get; }

        Task<string> TranslateAsync(string text, CancellationToken token = default);
    }

    /// <summary>
    /// Persistence for the translation history. The engine orchestrates
    /// contexts and overwrite detection on top of this.
    /// </summary>
    public interface IHistoryStore
    {
        Task LogAsync(string originalText, string translatedText, string targetLanguage,
            string apiName, bool isOverwrite, CancellationToken token = default);

        Task LogOnlyAsync(string originalText, bool isOverwrite, CancellationToken token = default);

        Task<string?> LoadLastSourceTextAsync(CancellationToken token = default);

        Task<TranslationHistoryEntry?> LoadLastTranslationAsync(CancellationToken token = default);
    }
}
