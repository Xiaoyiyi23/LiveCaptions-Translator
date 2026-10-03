using LiveCaptionsTranslator.utils;

using Xunit;

namespace LiveCaptionsTranslator.Tests
{
    public class SecretProtectorTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("sk-test-key-123")]
        public void Protect_Unprotect_RoundTrips(string plainText)
        {
            string stored = SecretProtector.Protect(plainText);

            if (plainText == string.Empty)
            {
                // Empty values are stored as-is.
                Assert.Equal(string.Empty, stored);
                return;
            }

            Assert.StartsWith("dpapi:", stored);
            Assert.DoesNotContain(plainText, stored);
            Assert.Equal(plainText, SecretProtector.Unprotect(stored));
        }

        [Fact]
        public void Unprotect_PlaintextValue_ReturnedAsIs()
        {
            // Legacy plaintext setting.json values must keep working.
            Assert.Equal("my-legacy-key", SecretProtector.Unprotect("my-legacy-key"));
        }

        [Fact]
        public void Protect_AlreadyEncryptedValue_IsNotDoubleEncrypted()
        {
            string stored = SecretProtector.Protect("secret");
            Assert.Equal(stored, SecretProtector.Protect(stored));
        }
    }
}
