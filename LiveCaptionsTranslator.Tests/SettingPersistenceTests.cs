using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

using Xunit;

namespace LiveCaptionsTranslator.Tests
{
    // Same collection as LocalizationServiceTests: loading a Setting applies its
    // Language, which mutates the shared LocalizationService singleton.
    [Collection("Localization")]
    public class SettingPersistenceTests
    {
        [Fact]
        public void MainWindowState_RoundTripsThroughJson()
        {
            string path = Path.Combine(Path.GetTempPath(), $"setting_test_{Guid.NewGuid():N}.json");
            try
            {
                var setting = new Setting();
                setting.MainWindow.CloseToTray = false;
                setting.MainWindow.TrayHintShown = true;
                setting.Language = "zh-CN";
                setting.TargetLanguage = "ja-JP";
                setting.Save(path);

                var loaded = Setting.Load(path);
                Assert.False(loaded.MainWindow.CloseToTray);
                Assert.True(loaded.MainWindow.TrayHintShown);
                Assert.Equal("zh-CN", loaded.Language);
                Assert.Equal("ja-JP", loaded.TargetLanguage);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
