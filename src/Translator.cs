using System.IO;
using System.Windows.Automation;

using LiveCaptionsTranslator.apis;
using LiveCaptionsTranslator.core;
using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    /// <summary>
    /// Static facade over <see cref="TranslationEngine"/>: keeps the surface the
    /// UI depends on (Setting/Caption/Window, flags, events, loop entry points)
    /// while the pipeline logic lives in the engine, unit-tested in isolation.
    /// </summary>
    public static class Translator
    {
        private static AutomationElement? window = null;
        private static readonly Caption? caption;
        private static readonly Setting? setting;
        private static readonly TranslationEngine engine;

        public static AutomationElement? Window
        {
            get => window;
            set => window = value;
        }
        public static Caption? Caption => caption;
        public static Setting? Setting => setting;

        public static bool LogOnlyFlag
        {
            get => engine.LogOnlyFlag;
            set => engine.LogOnlyFlag = value;
        }
        public static bool FirstUseFlag { get; set; } = false;

        public static event Action? TranslationLogged
        {
            add => engine.TranslationLogged += value;
            remove => engine.TranslationLogged -= value;
        }
        public static event Action? LogOnlyFlagChanged
        {
            add => engine.LogOnlyFlagChanged += value;
            remove => engine.LogOnlyFlagChanged -= value;
        }

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

            engine = new TranslationEngine(setting!, caption!,
                new LiveCaptionsTranslationApi(), new SqliteHistoryStore());
            // The engine stays UI-free; surface logging failures through the snackbar.
            engine.HistoryLogFailed += ex =>
                SnackbarHost.Show("[ERROR] " + LocalizationService.Get("Log.ErrLogging"), ex.Message,
                    SnackbarType.Error, timeout: 2, closeButton: true);
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

        // --- Loop shells: timing and LiveCaptions lifecycle only; all pipeline
        //     logic lives in the engine so it can be unit-tested. ---

        // Event-driven caption capture pushes text into the engine the moment
        // LiveCaptions updates it (idle CPU ~0, lower latency); a 500ms watchdog
        // poll falls back to the 25ms polling path if events never arrive.
        // Flip to true only after Win11 regression: caption continuity, fallback,
        // CPU idle comparison, clean exit with no leftover subscriptions.
        private const bool EnableEventDrivenCapture = false;

        public static void SyncLoop()
        {
            bool eventDriven = EnableEventDrivenCapture;
            bool subscribed = false;

            // Watchdog state: if polling keeps seeing text while the event count
            // does not move, events are not firing for this LiveCaptions build —
            // degrade to pure polling for the rest of the session.
            long eventsAtSubscribe = 0;
            int pollSawText = 0;

            while (true)
            {
                if (window == null)
                {
                    Thread.Sleep(2000);
                    continue;
                }

                if (eventDriven && !subscribed)
                {
                    subscribed = LiveCaptionsHandler.SubscribeCaptions(window, engine.ProcessCaptions);
                    eventsAtSubscribe = LiveCaptionsHandler.CaptionsEventCount;
                    pollSawText = 0;
                    if (!subscribed)
                        eventDriven = false;
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
                    window = null;
                    LiveCaptionsHandler.UnsubscribeCaptions();
                    subscribed = false;
                    continue;
                }

                if (eventDriven)
                {
                    // Watchdog: verify events are actually flowing while text is visible.
                    if (!string.IsNullOrEmpty(fullText))
                    {
                        pollSawText++;
                        if (pollSawText >= 10 &&  // ~5s of visible text at 500ms cadence
                            LiveCaptionsHandler.CaptionsEventCount == eventsAtSubscribe)
                        {
                            FileLogger.Warn("Caption events not observed; falling back to polling.");
                            LiveCaptionsHandler.UnsubscribeCaptions();
                            eventDriven = false;
                            subscribed = false;
                        }
                    }
                    Thread.Sleep(500);
                    continue;
                }

                if (string.IsNullOrEmpty(fullText))
                {
                    // Nothing is being said; wait instead of spinning on UIA calls.
                    Thread.Sleep(100);
                    continue;
                }

                engine.ProcessCaptions(fullText);

                Thread.Sleep(25);
            }
        }

        public static async Task TranslateLoop()
        {
            while (true)
            {
                // Check LiveCaptions.exe still alive
                if (window == null)
                {
                    // Keep the "[WARNING]" marker literal: it is matched when filtering contexts.
                    caption!.DisplayTranslatedCaption =
                        "[WARNING] " + LocalizationService.Get("Caption.WarnRestart");
                    window = LiveCaptionsHandler.LaunchLiveCaptions();
                    caption!.DisplayTranslatedCaption = "";
                }

                await engine.DispatchPending();

                await Task.Delay(40);
            }
        }

        public static async Task DisplayLoop()
        {
            while (true)
            {
                var (translatedText, isChoke) = engine.Output;
                engine.ApplyOutput(translatedText, isChoke);

                // If the original sentence is a complete sentence, choke for better visual experience.
                if (isChoke)
                    await Task.Delay(720);
                await Task.Delay(40);
            }
        }

        public static void ClearContexts() => engine.ClearContexts();
    }
}
