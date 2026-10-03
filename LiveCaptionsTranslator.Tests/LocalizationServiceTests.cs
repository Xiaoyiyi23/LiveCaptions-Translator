using LiveCaptionsTranslator.utils;

using Xunit;

namespace LiveCaptionsTranslator.Tests
{
    // Kept in a single class: these tests mutate the shared singleton and
    // xUnit runs tests of one class sequentially.
    public class LocalizationServiceTests
    {
        [Fact]
        public void EmbeddedEnglishTable_Loads()
        {
            LocalizationService.Instance.ApplyLanguage("en");
            Assert.Equal("Caption", LocalizationService.Get("Main.Nav.Caption"));
        }

        [Fact]
        public void ApplyLanguage_zhCN_SwitchesTable()
        {
            LocalizationService.Instance.ApplyLanguage("zh-CN");
            Assert.Equal("zh-CN", LocalizationService.Instance.Language);
            Assert.Equal("字幕", LocalizationService.Get("Main.Nav.Caption"));
            Assert.Equal("设置", LocalizationService.Get("Main.Nav.Setting"));
        }

        [Fact]
        public void MissingKey_FallsBackToKeyItself()
        {
            LocalizationService.Instance.ApplyLanguage("en");
            Assert.Equal("No.Such.Key", LocalizationService.Get("No.Such.Key"));
        }

        [Fact]
        public void UnsupportedLanguage_FallsBackToASupportedLanguage()
        {
            LocalizationService.Instance.ApplyLanguage("fr");
            Assert.Contains(LocalizationService.Instance.Language, LocalizationService.SUPPORTED_LANGUAGES);
        }
    }
}
