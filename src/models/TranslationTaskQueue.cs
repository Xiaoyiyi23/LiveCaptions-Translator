namespace LiveCaptionsTranslator.models
{
    /// <summary>
    /// Schedules translations with a "latest wins" policy: when any task
    /// completes, all still-pending earlier tasks are cancelled. Completion
    /// handling is injected so the queue can be tested without the app's
    /// static dependencies.
    /// </summary>
    public class TranslationTaskQueue
    {
        private readonly object _lock = new object();
        private readonly List<TranslationTask> tasks;
        private readonly Func<TranslationTask, Task> onCompletedAsync;

        private sealed class OutputState
        {
            public static readonly OutputState Empty = new(string.Empty, false);

            public OutputState(string translatedText, bool isChoke)
            {
                TranslatedText = translatedText;
                IsChoke = isChoke;
            }

            public string TranslatedText { get; }
            public bool IsChoke { get; }
        }

        // Written by task continuations and read by the display loop on other
        // threads; the volatile reference guarantees the latest result is visible.
        private volatile OutputState output = OutputState.Empty;
        public (string translatedText, bool isChoke) Output =>
            (output.TranslatedText, output.IsChoke);

        public TranslationTaskQueue(Func<TranslationTask, Task> onCompletedAsync)
        {
            tasks = new List<TranslationTask>();
            this.onCompletedAsync = onCompletedAsync;
        }

        public void Enqueue(Func<CancellationToken, Task<(string, bool)>> worker, string originalText)
        {
            var newTranslationTask = new TranslationTask(worker, originalText, new CancellationTokenSource());
            lock (_lock)
            {
                tasks.Add(newTranslationTask);
            }
            // Run `OnTaskCompleted` in a new thread.
            newTranslationTask.Task.ContinueWith(
                task => OnTaskCompleted(newTranslationTask),
                TaskContinuationOptions.OnlyOnRanToCompletion
            );
        }

        private async Task OnTaskCompleted(TranslationTask translationTask)
        {
            lock (_lock)
            {
                var index = tasks.IndexOf(translationTask);
                for (int i = 0; i < index; i++)
                    tasks[i].CTS.Cancel();
                for (int i = index; i >= 0; i--)
                    tasks.RemoveAt(i);
            }

            var result = translationTask.Task.Result;
            output = new OutputState(result.Item1, result.Item2);

            await onCompletedAsync(translationTask);
        }
    }

    public class TranslationTask
    {
        public Task<(string, bool)> Task { get; }
        public string OriginalText { get; }
        public CancellationTokenSource CTS { get; }

        public TranslationTask(Func<CancellationToken, Task<(string, bool)>> worker,
            string originalText, CancellationTokenSource cts)
        {
            Task = worker(cts.Token);
            OriginalText = originalText;
            CTS = cts;
        }
    }
}
