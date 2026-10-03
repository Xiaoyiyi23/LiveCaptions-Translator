using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator.core
{
    /// <summary>
    /// The translation pipeline: caption segmentation, task dispatch and display
    /// updates. Depends only on abstractions (<see cref="ITranslationApi"/>,
    /// <see cref="IHistoryStore"/>) and the data models, so the whole flow is
    /// unit-testable. <c>Translator</c> remains the static facade used by the UI.
    /// </summary>
    public class TranslationEngine
    {
        private readonly Setting setting;
        private readonly Caption caption;
        private readonly ITranslationApi api;
        private readonly IHistoryStore store;

        // Enqueued by `ProcessCaptions`, dequeued by `DispatchPending` (different threads).
        private readonly ConcurrentQueue<string> pendingTextQueue = new();
        private readonly TranslationTaskQueue translationTaskQueue;

        private int idleCount = 0;
        private int syncCount = 0;
        private bool logOnlyFlag = false;

        public event Action? TranslationLogged;
        public event Action? LogOnlyFlagChanged;

        // Raised instead of touching SnackbarHost directly so the engine stays UI-free.
        public event Action<Exception>? HistoryLogFailed;

        public bool LogOnlyFlag
        {
            get => logOnlyFlag;
            set
            {
                if (logOnlyFlag == value)
                    return;
                logOnlyFlag = value;
                LogOnlyFlagChanged?.Invoke();
            }
        }

        public (string translatedText, bool isChoke) Output => translationTaskQueue.Output;

        public TranslationEngine(Setting setting, Caption caption, ITranslationApi api, IHistoryStore store)
        {
            this.setting = setting;
            this.caption = caption;
            this.api = api;
            this.store = store;
            translationTaskQueue = new TranslationTaskQueue(CompleteTaskAsync);
        }

        // --- Caption segmentation (the former SyncLoop body) ---

        public void ProcessCaptions(string fullText)
        {
            // Preprocess
            fullText = TextUtil.PreprocessCaption(fullText);

            // Prevent adding the last sentence from previous running to log cards
            // before the first sentence is completed.
            if (fullText.IndexOfAny(TextUtil.PUNC_EOS) == -1 && caption.Contexts.Count > 0)
                ClearContexts();

            // Get the last sentence.
            var (lastEOSIndex, latestCaption) = TextUtil.GetLastSentence(fullText);

            // `OverlayOriginalCaption`: The sentence to be displayed on Overlay Window.
            caption.OverlayOriginalCaption = latestCaption;
            for (int historyCount = Math.Min(setting.DisplaySentences, caption.Contexts.Count);
                 historyCount > 0 && lastEOSIndex > 0;
                 historyCount--)
            {
                lastEOSIndex = fullText[0..lastEOSIndex].LastIndexOfAny(TextUtil.PUNC_EOS);
                caption.OverlayOriginalCaption = fullText.Substring(lastEOSIndex + 1);
            }

            // `DisplayOriginalCaption`: The sentence to be displayed on Main Window.
            if (string.CompareOrdinal(caption.DisplayOriginalCaption, latestCaption) != 0)
            {
                caption.DisplayOriginalCaption = latestCaption;
                // If the last sentence is too long, truncate it when displayed.
                caption.DisplayOriginalCaption =
                    TextUtil.ShortenDisplaySentence(caption.DisplayOriginalCaption, TextUtil.VERYLONG_THRESHOLD);
            }

            // Prepare for `OriginalCaption`. If Expanded, only retain the complete sentence.
            int lastEOS = latestCaption.LastIndexOfAny(TextUtil.PUNC_EOS);
            if (lastEOS != -1)
                latestCaption = latestCaption.Substring(0, lastEOS + 1);
            // `OriginalCaption`: The sentence to be really translated.
            if (string.CompareOrdinal(caption.OriginalCaption, latestCaption) != 0)
            {
                caption.OriginalCaption = latestCaption;

                idleCount = 0;
                if (Array.IndexOf(TextUtil.PUNC_EOS, caption.OriginalCaption[^1]) != -1)
                {
                    syncCount = 0;
                    pendingTextQueue.Enqueue(caption.OriginalCaption);
                }
                else if (Encoding.UTF8.GetByteCount(caption.OriginalCaption) >= TextUtil.SHORT_THRESHOLD)
                    syncCount++;
            }
            else
                idleCount++;

            // `TranslateFlag` determines whether this sentence should be translated.
            // When `OriginalCaption` remains unchanged, `idleCount` +1; when `OriginalCaption` changes, `MaxSyncInterval` +1.
            if (syncCount > setting.MaxSyncInterval ||
                idleCount == setting.MaxIdleInterval)
            {
                syncCount = 0;
                pendingTextQueue.Enqueue(caption.OriginalCaption);
            }
        }

        // --- Task dispatch (the former TranslateLoop body) ---

        public async Task DispatchPending()
        {
            if (!pendingTextQueue.TryDequeue(out string? originalSnapshot))
                return;

            if (LogOnlyFlag)
            {
                bool isOverwrite = await IsOverwrite(originalSnapshot);
                await LogOnly(originalSnapshot, isOverwrite);
            }
            else
            {
                string snapshot = originalSnapshot;
                translationTaskQueue.Enqueue(token => Task.Run(
                    () => Translate(snapshot, token), token), snapshot);
            }
        }

        // --- Display updates (the former DisplayLoop body) ---

        public void ApplyOutput(string translatedText, bool isChoke)
        {
            if (LogOnlyFlag)
            {
                caption.TranslatedCaption = string.Empty;
                caption.DisplayTranslatedCaption = LocalizationService.Get("Caption.Paused");
                caption.OverlayNoticePrefix = LocalizationService.Get("Caption.Paused");
                caption.OverlayCurrentTranslation = string.Empty;
            }
            else if (!string.IsNullOrEmpty(RegexPatterns.NoticePrefix().Replace(
                         translatedText, string.Empty).Trim()) &&
                     string.CompareOrdinal(caption.TranslatedCaption, translatedText) != 0)
            {
                // Main page
                caption.TranslatedCaption = translatedText;
                caption.DisplayTranslatedCaption =
                    TextUtil.ShortenDisplaySentence(caption.TranslatedCaption, TextUtil.VERYLONG_THRESHOLD);

                // Overlay window
                if (caption.TranslatedCaption.Contains("[ERROR]") || caption.TranslatedCaption.Contains("[WARNING]"))
                    caption.OverlayCurrentTranslation = caption.TranslatedCaption;
                else
                {
                    var match = RegexPatterns.NoticePrefixAndTranslation().Match(caption.TranslatedCaption);
                    caption.OverlayNoticePrefix = match.Groups[1].Value.Trim();
                    caption.OverlayCurrentTranslation = match.Groups[2].Value.Trim();
                }
            }
        }

        // --- Translation (the former Translator.Translate) ---

        public async Task<(string, bool)> Translate(string text, CancellationToken token = default)
        {
            string translatedText;
            bool isChoke = Array.IndexOf(TextUtil.PUNC_EOS, text[^1]) != -1;

            try
            {
                var sw = setting.MainWindow.LatencyShow ? Stopwatch.StartNew() : null;

                if (setting.ContextAware && !api.IsLLMBased)
                {
                    translatedText = await api.TranslateAsync(
                        $"{caption.AwareContextsCaption} 🔤 {text} 🔤", token);
                    translatedText = RegexPatterns.TargetSentence().Match(translatedText).Groups[1].Value;
                }
                else
                {
                    translatedText = await api.TranslateAsync(text, token);
                    translatedText = translatedText.Replace("🔤", "");
                }

                if (sw != null)
                {
                    sw.Stop();
                    translatedText = $"[{sw.ElapsedMilliseconds,4} ms] " + translatedText;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Translation failed", ex);
                return ("[ERROR] " + string.Format(LocalizationService.Get("Api.ErrGeneric"), ex.Message), isChoke);
            }

            return (translatedText, isChoke);
        }

        // --- History & contexts (the former Translator helpers) ---

        private async Task CompleteTaskAsync(TranslationTask translationTask)
        {
            // Log after translation.
            bool isOverwrite = await IsOverwrite(translationTask.OriginalText);
            if (!isOverwrite)
                await AddContexts();
            await Log(translationTask.OriginalText, translationTask.Task.Result.Item1, isOverwrite);
        }

        public async Task Log(string originalText, string translatedText,
            bool isOverwrite = false, CancellationToken token = default)
        {
            try
            {
                await store.LogAsync(originalText, translatedText,
                    setting.TargetLanguage, setting.ApiName, isOverwrite, token);
                TranslationLogged?.Invoke();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                FileLogger.Error("Logging history failed", ex);
                HistoryLogFailed?.Invoke(ex);
            }
        }

        public async Task LogOnly(string originalText,
            bool isOverwrite = false, CancellationToken token = default)
        {
            try
            {
                await store.LogOnlyAsync(originalText, isOverwrite, token);
                TranslationLogged?.Invoke();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                FileLogger.Error("Logging history failed", ex);
                HistoryLogFailed?.Invoke(ex);
            }
        }

        public async Task AddContexts(CancellationToken token = default)
        {
            var lastLog = await store.LoadLastTranslationAsync(token);
            if (lastLog == null)
                return;

            if (caption.Contexts.Count >= Caption.MAX_CONTEXTS)
                caption.Contexts.Dequeue();
            caption.Contexts.Enqueue(lastLog);

            caption.InvalidateContextsCache();
            caption.OnPropertyChanged("DisplayLogCards");
            caption.OnPropertyChanged("OverlayPreviousTranslation");
        }

        public void ClearContexts()
        {
            caption.Contexts.Clear();

            caption.InvalidateContextsCache();
            caption.OnPropertyChanged("DisplayLogCards");
            caption.OnPropertyChanged("OverlayPreviousTranslation");
        }

        // If this text is too similar to the last one, overwrite it when logging.
        public async Task<bool> IsOverwrite(string originalText, CancellationToken token = default)
        {
            string? lastOriginalText = await store.LoadLastSourceTextAsync(token);
            if (string.IsNullOrEmpty(lastOriginalText))
                return false;

            int minLen = Math.Min(originalText.Length, lastOriginalText.Length);
            originalText = originalText.Substring(0, minLen);
            lastOriginalText = lastOriginalText.Substring(0, minLen);

            double similarity = TextUtil.Similarity(originalText, lastOriginalText);
            return similarity > TextUtil.SIM_THRESHOLD;
        }
    }
}
