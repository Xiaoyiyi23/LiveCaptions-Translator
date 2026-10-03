using System.Diagnostics;

using LiveCaptionsTranslator.core;
using LiveCaptionsTranslator.models;

using Xunit;

namespace LiveCaptionsTranslator.Tests
{
    public class TranslationEngineTests
    {
        private sealed class FakeApi : ITranslationApi
        {
            public List<(string Text, CancellationToken Token)> Calls = new();
            public Func<string, CancellationToken, Task<string>>? Responder;

            public bool IsLLMBased => false;

            public async Task<string> TranslateAsync(string text, CancellationToken token = default)
            {
                Calls.Add((text, token));
                if (Responder != null)
                    return await Responder(text, token);
                return $"T:{text}";
            }
        }

        private sealed class FakeStore : IHistoryStore
        {
            public List<(string Source, string Translated, bool Overwrite)> Logged = new();
            public List<(string Source, bool Overwrite)> LoggedOnly = new();
            public string? LastSource;
            public TranslationHistoryEntry? LastTranslation;

            public Task LogAsync(string originalText, string translatedText, string targetLanguage,
                string apiName, bool isOverwrite, CancellationToken token = default)
            {
                Logged.Add((originalText, translatedText, isOverwrite));
                return Task.CompletedTask;
            }

            public Task LogOnlyAsync(string originalText, bool isOverwrite, CancellationToken token = default)
            {
                LoggedOnly.Add((originalText, isOverwrite));
                return Task.CompletedTask;
            }

            public Task<string?> LoadLastSourceTextAsync(CancellationToken token = default) =>
                Task.FromResult(LastSource);

            public Task<TranslationHistoryEntry?> LoadLastTranslationAsync(CancellationToken token = default) =>
                Task.FromResult(LastTranslation);
        }

        private readonly FakeApi api = new();
        private readonly FakeStore store = new();
        private readonly TranslationEngine engine;

        public TranslationEngineTests()
        {
            // `Caption` is an app-wide singleton; reset its state per test.
            var caption = Caption.GetInstance();
            caption.OriginalCaption = string.Empty;
            caption.TranslatedCaption = string.Empty;
            caption.DisplayOriginalCaption = string.Empty;
            caption.Contexts.Clear();
            caption.InvalidateContextsCache();

            engine = new TranslationEngine(new Setting(), Caption.GetInstance(), api, store);
        }

        private static bool WaitUntil(Func<bool> condition, int timeoutMs = 3000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition())
                    return true;
                Thread.Sleep(20);
            }
            return false;
        }

        [Fact]
        public void CompleteSentence_IsTranslatedAndLogged()
        {
            engine.ProcessCaptions("Hello world.");
            engine.DispatchPending().Wait();

            Assert.True(WaitUntil(() => engine.Output.translatedText == "T:Hello world."));
            Assert.Contains(("Hello world.", "T:Hello world.", false), store.Logged);
        }

        [Fact]
        public void GrowingSentence_IsForcedAfterMaxSyncInterval()
        {
            // MaxSyncInterval defaults to 3: after 4 distinct partial updates
            // of the same sentence, the partial text is force-enqueued.
            engine.ProcessCaptions("AAAAAAAAAA1");
            engine.ProcessCaptions("AAAAAAAAAA2");
            engine.ProcessCaptions("AAAAAAAAAA3");
            engine.ProcessCaptions("AAAAAAAAAA4");
            engine.DispatchPending().Wait();

            Assert.True(WaitUntil(() => api.Calls.Any(c => c.Text == "AAAAAAAAAA4")));
        }

        [Fact]
        public void StalledSentence_IsForcedAfterMaxIdleInterval()
        {
            // MaxIdleInterval defaults to 50: identical text stops incrementing
            // syncCount and eventually forces the pending sentence through.
            for (int i = 0; i < 51; i++)
                engine.ProcessCaptions("A partial sentence here");
            engine.DispatchPending().Wait();

            Assert.True(WaitUntil(() => api.Calls.Any(c => c.Text == "A partial sentence here")));
        }

        [Fact]
        public async void LogOnlyMode_BypassesApiAndLogsOriginalOnly()
        {
            engine.LogOnlyFlag = true;
            engine.ProcessCaptions("Hello world.");
            await engine.DispatchPending();

            Assert.Empty(api.Calls);
            Assert.Contains(("Hello world.", false), store.LoggedOnly);
            Assert.Empty(store.Logged);
        }

        [Fact]
        public async void LatestWins_CompletingTaskCancelsStuckEarlierOnes()
        {
            var blocked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            api.Responder = (text, token) => text == "slow." ? blocked.Task : Task.FromResult($"T:{text}");

            engine.ProcessCaptions("slow.");
            await engine.DispatchPending();
            engine.ProcessCaptions("fast.");
            await engine.DispatchPending();

            Assert.True(WaitUntil(() => engine.Output.translatedText == "T:fast."));
            Assert.True(WaitUntil(() => api.Calls.First(c => c.Text == "slow.").Token.IsCancellationRequested));
            Assert.True(WaitUntil(() => store.Logged.Any(l => l.Source == "fast.")));
            Assert.DoesNotContain(store.Logged, l => l.Source == "slow.");

            blocked.TrySetCanceled();
        }

        [Fact]
        public async void ContextAwareTraditionalApi_WrapsTextAndExtractsTargetSentence()
        {
            var setting = new Setting { ContextAware = true };
            var engine = new TranslationEngine(setting, Caption.GetInstance(), api, store);
            api.Responder = (_, _) => Task.FromResult("prefix 🔤 Result 🔤 suffix");

            var (translated, isChoke) = await engine.Translate("Hello world.");

            Assert.Equal("Result", translated);
            Assert.True(isChoke);  // ends with EOS punctuation
            Assert.Contains("🔤 Hello world. 🔤", api.Calls.Single().Text);
        }

        [Fact]
        public async void SimilarToLastSource_LogsAsOverwrite()
        {
            store.LastSource = "Hello world. How are you today";
            bool isOverwrite = await engine.IsOverwrite("Hello world. How are you today my friend");
            Assert.True(isOverwrite);

            store.LastSource = "Completely different content";
            isOverwrite = await engine.IsOverwrite("Hello world. How are you today my friend");
            Assert.False(isOverwrite);
        }
    }
}
