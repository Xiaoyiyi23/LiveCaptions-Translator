using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows.Data;

namespace LiveCaptionsTranslator.utils
{
    /// <summary>
    /// Provides localized UI strings from JSON tables embedded as assembly
    /// resources (src/lang/{language}.json). Raising PropertyChanged for
    /// "Item[]" re-evaluates every Loc binding, so switching languages
    /// applies immediately without restarting.
    /// </summary>
    public class LocalizationService : INotifyPropertyChanged
    {
        public const string DEFAULT_LANGUAGE = "en";
        public static readonly string[] SUPPORTED_LANGUAGES = ["en", "zh-CN"];

        public static readonly LocalizationService Instance = new();

        private Dictionary<string, string> table;
        private string language = DEFAULT_LANGUAGE;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Language => language;

        // Missing keys fall back to the English table, then to the key itself
        // so gaps in a translation are obvious but never crash the UI.
        public string this[string key]
        {
            get
            {
                if (table.TryGetValue(key, out string? value))
                    return value;
                return key;
            }
        }

        private LocalizationService()
        {
            table = LoadTable(DEFAULT_LANGUAGE) ?? new Dictionary<string, string>();
        }

        public static string Get(string key) => Instance[key];

        // null resolves to the system UI language.
        public void ApplyLanguage(string? language)
        {
            string lang = ResolveLanguage(language);
            if (lang == this.language)
                return;

            table = LoadTable(lang) ?? LoadTable(DEFAULT_LANGUAGE) ?? new Dictionary<string, string>();
            this.language = lang;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }

        private static string ResolveLanguage(string? language)
        {
            if (!string.IsNullOrEmpty(language) && SUPPORTED_LANGUAGES.Contains(language))
                return language!;
            try
            {
                return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : DEFAULT_LANGUAGE;
            }
            catch
            {
                return DEFAULT_LANGUAGE;
            }
        }

        private static Dictionary<string, string>? LoadTable(string language)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream($"LiveCaptionsTranslator.lang.{language}.json");
                if (stream == null)
                    return null;
                return JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            }
            catch (Exception ex)
            {
                FileLogger.Error($"Failed to load language '{language}'", ex);
                return null;
            }
        }
    }

    /// <summary>
    /// Markup extension for localized strings in XAML:
    /// Text="{utils:Loc Some.Key}".
    /// </summary>
    public class LocExtension : Binding
    {
        public LocExtension(string key) : base("[" + key + "]")
        {
            Source = LocalizationService.Instance;
            Mode = BindingMode.OneWay;
        }
    }
}
