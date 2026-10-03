using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Automation;

using LiveCaptionsTranslator.apis;
using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public static class Translator
    {
        private static AutomationElement? window = null;
        private static Caption? caption = null;
        private static Setting? setting = null;

        // Enqueued by `SyncLoop`, dequeued by `TranslateLoop` (different threads).
        private static readonly ConcurrentQueue<string> pendingTextQueue = new();
        private static readonly TranslationTaskQueue translationTaskQueue = new();

        public static AutomationElement? Window
        {
            get => window;
            set => window = value;
        }
        public static Caption? Caption => caption;
        public static Setting? Setting => setting;

        private static bool logOnlyFlag = false;
        private static bool firstUseFlag = false;

        public static bool LogOnlyFlag
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
        public static bool FirstUseFlag { get => firstUseFlag; set => firstUseFlag = value; }

        public static event Action? TranslationLogged;
        public static event Action? LogOnlyFlagChanged;

        static Translator()
        {
            // Side-effect-free initialization only: touching `Translator.Setting`
            // (e.g. from unit tests on machines without LiveCaptions) must never
            // spawn or kill processes. The LiveCaptions window is launched
            // explicitly at startup via `EnsureLiveCaptions`.
            if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), models.Setting.FILENAME)))
                FirstUseFlag = true;

            caption = Caption.GetInstance();
            setting = Setting.Load();
        }

        // Launches (and hides) the system LiveCaptions window. Idempotent.
        public static void EnsureLiveCaptions()
        {
            if (window != null)
                return;
            window = LiveCaptionsHandler.LaunchLiveCaptions();
            LiveCaptionsHandler.FixLiveCaptions(Window);
            LiveCaptionsHandler.HideLiveCaptions(Window);
        }

        public static void SyncLoop()
        {
            int idleCount = 0;
            int syncCount = 0;

            while (true)
            {
                if (Window == null)
                {
                    Thread.Sleep(2000);
                    continue;
                }

                string fullText = string.Empty;
                try
                {
                    // Check LiveCaptions.exe still alive
                    var info = Window.Current;
                    var name = info.Name;
                    // Get the text recognized by LiveCaptions (10-20ms)
                    fullText = LiveCaptionsHandler.GetCaptions(Window);
                }
                catch (ElementNotAvailableException)
                {
                    Window = null;
                    continue;
                }
                if (string.IsNullOrEmpty(fullText))
                {
                    // Nothing is being said; wait instead of spinning on UIA calls.
                    Thread.Sleep(100);
                    continue;
                }

                // Preprocess
                fullText = TextUtil.PreprocessCaption(fullText);

                // Prevent adding the last sentence from previous running to log cards
                // before the first sentence is completed.
                if (fullText.IndexOfAny(TextUtil.PUNC_EOS) == -1 && Caption.Contexts.Count > 0)
                    ClearContexts();

                // Get the last sentence.
                var (lastEOSIndex, latestCaption) = TextUtil.GetLastSentence(fullText);

                // `OverlayOriginalCaption`: The sentence to be displayed on Overlay Window.
                Caption.OverlayOriginalCaption = latestCaption;
                for (int historyCount = Math.Min(Setting.DisplaySentences, Caption.Contexts.Count);
                     historyCount > 0 && lastEOSIndex > 0;
                     historyCount--)
                {
                    lastEOSIndex = fullText[0..lastEOSIndex].LastIndexOfAny(TextUtil.PUNC_EOS);
                    Caption.OverlayOriginalCaption = fullText.Substring(lastEOSIndex + 1);
                }

                // `DisplayOriginalCaption`: The sentence to be displayed on Main Window.
                if (string.CompareOrdinal(Caption.DisplayOriginalCaption, latestCaption) != 0)
                {
                    Caption.DisplayOriginalCaption = latestCaption;
                    // If the last sentence is too long, truncate it when displayed.
                    Caption.DisplayOriginalCaption =
                        TextUtil.ShortenDisplaySentence(Caption.DisplayOriginalCaption, TextUtil.VERYLONG_THRESHOLD);
                }

                // Prepare for `OriginalCaption`. If Expanded, only retain the complete sentence.
                int lastEOS = latestCaption.LastIndexOfAny(TextUtil.PUNC_EOS);
                if (lastEOS != -1)
                    latestCaption = latestCaption.Substring(0, lastEOS + 1);
                // `OriginalCaption`: The sentence to be really translated.
                if (string.CompareOrdinal(Caption.OriginalCaption, latestCaption) != 0)
                {
                    Caption.OriginalCaption = latestCaption;

                    idleCount = 0;
                    if (Array.IndexOf(TextUtil.PUNC_EOS, Caption.OriginalCaption[^1]) != -1)
                    {
                        syncCount = 0;
                        pendingTextQueue.Enqueue(Caption.OriginalCaption);
                    }
                    else if (Encoding.UTF8.GetByteCount(Caption.OriginalCaption) >= TextUtil.SHORT_THRESHOLD)
                        syncCount++;
                }
                else
                    idleCount++;

                // `TranslateFlag` determines whether this sentence should be translated.
                // When `OriginalCaption` remains unchanged, `idleCount` +1; when `OriginalCaption` changes, `MaxSyncInterval` +1.
                if (syncCount > Setting.MaxSyncInterval ||
                    idleCount == Setting.MaxIdleInterval)
                {
                    syncCount = 0;
                    pendingTextQueue.Enqueue(Caption.OriginalCaption);
                }

                Thread.Sleep(25);
            }
        }

        public static async Task TranslateLoop()
        {
            while (true)
            {
                // Check LiveCaptions.exe still alive
                if (Window == null)
                {
                    // Keep the "[WARNING]" marker literal: it is matched when filtering contexts.
                    Caption.DisplayTranslatedCaption =
                        "[WARNING] " + LocalizationService.Get("Caption.WarnRestart");
                    Window = LiveCaptionsHandler.LaunchLiveCaptions();
                    Caption.DisplayTranslatedCaption = "";
                }

                // Translate
                if (pendingTextQueue.TryDequeue(out string? originalSnapshot))
                {
                    if (LogOnlyFlag)
                    {
                        bool isOverwrite = await IsOverwrite(originalSnapshot);
                        await LogOnly(originalSnapshot, isOverwrite);
                    }
                    else
                    {
                        translationTaskQueue.Enqueue(token => Task.Run(
                            () => Translate(originalSnapshot, token), token), originalSnapshot);
                    }
                }

                await Task.Delay(40);
            }
        }

        public static async Task DisplayLoop()
        {
            while (true)
            {
                var (translatedText, isChoke) = translationTaskQueue.Output;

                if (LogOnlyFlag)
                {
                    Caption.TranslatedCaption = string.Empty;
                    Caption.DisplayTranslatedCaption = LocalizationService.Get("Caption.Paused");
                    Caption.OverlayNoticePrefix = LocalizationService.Get("Caption.Paused");
                    Caption.OverlayCurrentTranslation = string.Empty;
                }
                else if (!string.IsNullOrEmpty(RegexPatterns.NoticePrefix().Replace(
                             translatedText, string.Empty).Trim()) &&
                         string.CompareOrdinal(Caption.TranslatedCaption, translatedText) != 0)
                {
                    // Main page
                    Caption.TranslatedCaption = translatedText;
                    Caption.DisplayTranslatedCaption =
                        TextUtil.ShortenDisplaySentence(Caption.TranslatedCaption, TextUtil.VERYLONG_THRESHOLD);

                    // Overlay window
                    if (Caption.TranslatedCaption.Contains("[ERROR]") || Caption.TranslatedCaption.Contains("[WARNING]"))
                        Caption.OverlayCurrentTranslation = Caption.TranslatedCaption;
                    else
                    {
                        var match = RegexPatterns.NoticePrefixAndTranslation().Match(Caption.TranslatedCaption);
                        Caption.OverlayNoticePrefix = match.Groups[1].Value.Trim();
                        Caption.OverlayCurrentTranslation = match.Groups[2].Value.Trim();
                    }
                }

                // If the original sentence is a complete sentence, choke for better visual experience.
                if (isChoke)
                    await Task.Delay(720);
                await Task.Delay(40);
            }
        }

        public static async Task<(string, bool)> Translate(string text, CancellationToken token = default)
        {
            string translatedText;
            bool isChoke = Array.IndexOf(TextUtil.PUNC_EOS, text[^1]) != -1;

            try
            {
                var sw = Setting.MainWindow.LatencyShow ? Stopwatch.StartNew() : null;

                if (Setting.ContextAware && !TranslateAPI.IsLLMBased)
                {
                    translatedText = await TranslateAPI.TranslateFunction($"{Caption.AwareContextsCaption} 🔤 {text} 🔤", token);
                    translatedText = RegexPatterns.TargetSentence().Match(translatedText).Groups[1].Value;
                }
                else
                {
                    translatedText = await TranslateAPI.TranslateFunction(text, token);
                    translatedText = translatedText.Replace("🔤", "");
                }

                if (sw != null)
                {
                    sw.Stop();
                    translatedText = $"[{sw.ElapsedMilliseconds,4} ms] " + translatedText;
                }
            }
            catch (OperationCanceledException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Translation failed", ex);
                return ($"[ERROR] " + string.Format(LocalizationService.Get("Api.ErrGeneric"), ex.Message), isChoke);
            }

            return (translatedText, isChoke);
        }

        public static async Task Log(string originalText, string translatedText,
            bool isOverwrite = false, CancellationToken token = default)
        {
            string targetLanguage, apiName;
            if (Setting != null)
            {
                targetLanguage = Setting.TargetLanguage;
                apiName = Setting.ApiName;
            }
            else
            {
                targetLanguage = "N/A";
                apiName = "N/A";
            }

            try
            {
                if (isOverwrite)
                    await SQLiteHistoryLogger.DeleteLastTranslation(token);
                await SQLiteHistoryLogger.LogTranslation(originalText, translatedText, targetLanguage, apiName);
                TranslationLogged?.Invoke();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                FileLogger.Error("Logging history failed", ex);
                SnackbarHost.Show("[ERROR] " + LocalizationService.Get("Log.ErrLogging"), ex.Message, SnackbarType.Error,
                    timeout: 2, closeButton: true);
            }
        }

        public static async Task LogOnly(string originalText,
            bool isOverwrite = false, CancellationToken token = default)
        {
            try
            {
                if (isOverwrite)
                    await SQLiteHistoryLogger.DeleteLastTranslation(token);
                await SQLiteHistoryLogger.LogTranslation(originalText, "N/A", "N/A", "LogOnly");
                TranslationLogged?.Invoke();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                FileLogger.Error("Logging history failed", ex);
                SnackbarHost.Show("[ERROR] " + LocalizationService.Get("Log.ErrLogging"), ex.Message, SnackbarType.Error,
                    timeout: 2, closeButton: true);
            }
        }

        public static async Task AddContexts(CancellationToken token = default)
        {
            var lastLog = await SQLiteHistoryLogger.LoadLastTranslation(token);
            if (lastLog == null)
                return;

            if (Caption?.Contexts.Count >= Caption.MAX_CONTEXTS)
                Caption.Contexts.Dequeue();
            Caption?.Contexts.Enqueue(lastLog);

            Caption?.InvalidateContextsCache();
            Caption?.OnPropertyChanged("DisplayLogCards");
            Caption?.OnPropertyChanged("OverlayPreviousTranslation");
        }

        public static void ClearContexts()
        {
            Caption?.Contexts.Clear();

            Caption?.InvalidateContextsCache();
            Caption?.OnPropertyChanged("DisplayLogCards");
            Caption?.OnPropertyChanged("OverlayPreviousTranslation");
        }

        // If this text is too similar to the last one, overwrite it when logging.
        public static async Task<bool> IsOverwrite(string originalText, CancellationToken token = default)
        {
            string lastOriginalText = await SQLiteHistoryLogger.LoadLastSourceText(token);
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
