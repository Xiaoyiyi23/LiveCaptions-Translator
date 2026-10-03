using LiveCaptionsTranslator;

using Xunit;

namespace LiveCaptionsTranslator.Tests
{
    public class TranslatorFacadeTests
    {
        [Fact]
        public void TouchingStaticState_DoesNotLaunchLiveCaptions()
        {
            // Before the static-constructor split, first access to any Translator
            // member launched LiveCaptions.exe and threw TypeInitializationException
            // on machines where it is unavailable (Win10/CI runners).
            Assert.NotNull(Translator.Setting);
            Assert.NotNull(Translator.Caption);
            Assert.Null(Translator.Window);
        }
    }
}
