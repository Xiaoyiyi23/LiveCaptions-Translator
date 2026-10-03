using LiveCaptionsTranslator.utils;

using Xunit;

namespace LiveCaptionsTranslator.Tests
{
    public class TextUtilTests
    {
        [Theory]
        [InlineData("Hello , world", "Hello, world")]
        [InlineData("你好 。 世界", "你好。世界")]
        [InlineData("line1\nline2", "line1—line2")]
        [InlineData("A. B. test", "AB. test")]
        [InlineData("A. Bc", "A Bc")]
        public void PreprocessCaption_NormalizesText(string input, string expected)
        {
            Assert.Equal(expected, TextUtil.PreprocessCaption(input));
        }

        [Fact]
        public void PreprocessCaption_LongNewlineBecomesPeriod()
        {
            string input = new string('a', 50) + "\nnext";
            Assert.Equal(new string('a', 50) + ". next", TextUtil.PreprocessCaption(input));
        }

        [Fact]
        public void PreprocessCaption_CJKNewlineUsesCJKDash()
        {
            Assert.Equal("你好——世界", TextUtil.PreprocessCaption("你好\n世界"));
        }

        [Theory]
        [InlineData("Hello world. How are", -1, "Hello world. How are")]
        [InlineData("How are you?", -1, "How are you?")]
        [InlineData("Hi. Ok.", -1, "Hi. Ok.")]
        [InlineData("Hi. Very long sentence here.", 2, " Very long sentence here.")]
        public void GetLastSentence_SplitsCorrectly(string input, int expectedIndex, string expectedCaption)
        {
            var (lastEOSIndex, latestCaption) = TextUtil.GetLastSentence(input);
            Assert.Equal(expectedIndex, lastEOSIndex);
            Assert.Equal(expectedCaption, latestCaption);
        }

        [Theory]
        [InlineData("line1\nline2", "line1—line2")]
        [InlineData("你好\n世界", "你好——世界")]
        public void ReplaceNewlines_JoinsShortLinesWithDash(string input, string expected)
        {
            Assert.Equal(expected, TextUtil.ReplaceNewlines(input, TextUtil.MEDIUM_THRESHOLD));
        }

        [Fact]
        public void ReplaceNewlines_LongLineEndsWithPeriod()
        {
            string input = new string('a', 50) + "\nnext";
            Assert.Equal(new string('a', 50) + ". next", TextUtil.ReplaceNewlines(input, TextUtil.MEDIUM_THRESHOLD));
        }

        [Fact]
        public void ShortenDisplaySentence_TrimsLeadingSentences()
        {
            // The cut lands right after the punctuation, so the leading space remains.
            Assert.Equal(" Three.", TextUtil.ShortenDisplaySentence("One. Two. Three.", 10));
        }

        [Fact]
        public void ShortenDisplaySentence_KeepsShortText()
        {
            Assert.Equal("Hi.", TextUtil.ShortenDisplaySentence("Hi.", 10));
        }

        [Theory]
        [InlineData("abc", "abc", 1.0)]
        [InlineData("abcdef", "abc", 1.0)]
        [InlineData("", "", 1.0)]
        public void Similarity_IdenticalOrPrefix_ReturnsOne(string a, string b, double expected)
        {
            Assert.Equal(expected, TextUtil.Similarity(a, b));
        }

        [Fact]
        public void Similarity_SingleEdit_ReturnsRatio()
        {
            // Levenshtein distance between "abc" and "abd" is 1 out of length 3.
            Assert.Equal(2.0 / 3.0, TextUtil.Similarity("abc", "abd"), 5);
        }

        [Theory]
        [InlineData("http://localhost:11434/", "http://localhost:11434")]
        [InlineData("https://a.com//b", "https://a.com/b")]
        [InlineData("localhost:11434", "localhost:11434")]
        public void NormalizeUrl_CollapsesSlashes(string input, string expected)
        {
            Assert.Equal(expected, TextUtil.NormalizeUrl(input));
        }
    }
}
