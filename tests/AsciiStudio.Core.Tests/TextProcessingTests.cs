using System.IO.Compression;
using System.Text;
using AsciiStudio.Core;
using Xunit;

namespace AsciiStudio.Core.Tests;

public sealed class TextProcessingTests
{
    public static IEnumerable<object[]> ReversibleMethods => TextProcessing.CharacterEncodings.Select(name => new object[] { name, name switch
    {
        "ASCII" => "ABC\0\r\n", "GBK" or "Big5" => "中文 ABC\r\n", "Shift_JIS" => "日本語 ABC\r\n", _ => "中文🙂e\u0301\0\r\n"
    } }).Concat(TextProcessing.Representations.Where(TextProcessing.CanReverse).Concat(TextProcessing.BinaryEncodings).Concat(TextProcessing.Compression).Select(name => new object[] { name, "Abc 中文🙂e\u0301\0\r\n\\\"&" }));

    [Theory]
    [MemberData(nameof(ReversibleMethods))]
    public void AllReversibleMethodsPreserveExactText(string name, string text)
        => Assert.Equal(text, CryptoTools.Apply(name, CryptoTools.Apply(name, text), decrypt: true));

    [Fact]
    public void ExactlyTwentyFiveMethodsAreAdded()
    {
        var methods = TextProcessing.CharacterEncodings.Concat(TextProcessing.Representations).Concat(TextProcessing.BinaryEncodings).Concat(TextProcessing.Compression).Concat(TextProcessing.Checksums).ToArray();
        Assert.Equal(25, methods.Length); Assert.Equal(25, methods.Distinct().Count());
        Assert.All(methods, name => Assert.True(TextProcessing.Contains(name)));
    }

    [Theory]
    [InlineData("ASCII", "ABC", "414243")]
    [InlineData("UTF-8", "中", "E4B8AD")]
    [InlineData("UTF-16LE", "中", "2D4E")]
    [InlineData("UTF-16BE", "中", "4E2D")]
    [InlineData("GBK", "中", "D6D0")]
    [InlineData("GB18030", "中", "D6D0")]
    [InlineData("Big5", "中", "A4A4")]
    [InlineData("Shift_JIS", "あ", "82A0")]
    [InlineData("Base32hex", "f", "CO======")]
    [InlineData("Base32hex", "foobar", "CPNMUOJ1E8======")]
    [InlineData("Base58", "Hello World", "JxF12TrwUP45BMd")]
    [InlineData("Base58", "\0\0", "11")]
    [InlineData("Ascii85", "Man", "<~9jqo~>")]
    [InlineData("Ascii85", "\0\0\0\0", "<~z~>")]
    [InlineData("Quoted-printable", "é=\r\n", "=C3=A9=3D=0D=0A")]
    [InlineData("Unicode \\uXXXX", "🙂", "\\uD83D\\uDE42")]
    [InlineData("Unicode \\UXXXXXXXX", "🙂", "\\U0001F642")]
    [InlineData("HTML 十进制实体", "M🙂", "&#77;&#128578;")]
    [InlineData("HTML 十六进制实体", "M🙂", "&#x4D;&#x1F642;")]
    [InlineData("Punycode", "bücher", "bcher-kva")]
    [InlineData("Punycode", "ABC", "ABC-")]
    [InlineData("Punycode", "他们为什么不说中文", "ihqwcrb4cv8a8dqg056pqjye")]
    [InlineData("CRC32", "123456789", "cbf43926")]
    [InlineData("CRC32", "", "00000000")]
    [InlineData("Adler-32", "Wikipedia", "11e60398")]
    [InlineData("Adler-32", "", "00000001")]
    public void StandardVectors(string name, string text, string expected) => Assert.Equal(expected, CryptoTools.Apply(name, text));

    [Theory]
    [InlineData("UTF-8", "EFBBBF41")]
    [InlineData("UTF-16LE", "FFFE4100")]
    [InlineData("UTF-16BE", "FEFF0041")]
    public void BomAndBase64BytesRoundTrip(string name, string expected)
    {
        Assert.Equal(expected, CryptoTools.Apply(name, "A", processingOptions: new(IncludeBom: true)));
        Assert.Equal("A", CryptoTools.Apply(name, expected, decrypt: true));
        var options = new TextProcessingOptions(true, true);
        var encoded = CryptoTools.Apply(name, "测试🙂", processingOptions: options);
        Assert.Equal("测试🙂", CryptoTools.Apply(name, encoded, decrypt: true, processingOptions: options));
    }

    [Theory]
    [InlineData("ASCII", "中文")]
    [InlineData("GBK", "🙂")]
    [InlineData("Big5", "🙂")]
    [InlineData("Shift_JIS", "🙂")]
    public void UnrepresentableCharactersAreRejected(string name, string text) => Assert.Throws<EncoderFallbackException>(() => CryptoTools.Apply(name, text));

    [Theory]
    [InlineData("Base32hex", "C")]
    [InlineData("Base32hex", "CP======")]
    [InlineData("Base32hex", "CO=")]
    [InlineData("Base32hex", "CO=====V")]
    [InlineData("Base58", "0OIl")]
    [InlineData("Ascii85", "<~!~>")]
    [InlineData("Ascii85", "<~!z~>")]
    [InlineData("Ascii85", "<~uuuuu~>")]
    [InlineData("Ascii85", "<~9jqo")]
    [InlineData("Quoted-printable", "=G0")]
    [InlineData("Quoted-printable", "=")]
    [InlineData("Unicode \\uXXXX", "\\uD800")]
    [InlineData("Unicode \\UXXXXXXXX", "\\U00110000")]
    [InlineData("HTML 十进制实体", "&#55296;")]
    [InlineData("HTML 十六进制实体", "&#x110000;")]
    [InlineData("JSON 转义", "null")]
    [InlineData("Punycode", "999999999999999999999999999")]
    [InlineData("UTF-8", "FF")]
    [InlineData("UTF-16LE", "41")]
    public void MalformedInputIsRejected(string name, string text) => Assert.ThrowsAny<Exception>(() => CryptoTools.Apply(name, text, decrypt: true));

    [Theory]
    [InlineData("NFC", "e\u0301", "é")]
    [InlineData("NFKC", "Ａ①", "A1")]
    public void NormalizationIsExplicitlyIrreversible(string name, string text, string expected)
    {
        Assert.Equal(expected, CryptoTools.Apply(name, text)); Assert.False(TextProcessing.CanReverse(name));
        Assert.Throws<ArgumentException>(() => CryptoTools.Apply(name, text, decrypt: true));
    }

    [Theory]
    [InlineData("CRC32")]
    [InlineData("Adler-32")]
    public void ChecksumsCannotBeDecoded(string name) => Assert.Throws<ArgumentException>(() => CryptoTools.Apply(name, "abc", decrypt: true));

    [Fact]
    public void QuotedPrintableFoldsAndAcceptsSoftBreaks()
    {
        var result = CryptoTools.Apply("Quoted-printable", new string('中', 100));
        Assert.All(result.Split("\r\n"), line => Assert.InRange(line.Length, 0, 76));
        Assert.Equal("hello world", CryptoTools.Apply("Quoted-printable", "hello=20=\r\nworld", decrypt: true));
    }

    [Theory]
    [InlineData("GZIP")]
    [InlineData("Zlib")]
    [InlineData("Brotli")]
    public void DecompressionRejectsExpansionBeyondBudget(string name)
    {
        using var output = new MemoryStream();
        using (Stream compressor = name switch
        {
            "GZIP" => new GZipStream(output, CompressionLevel.Optimal, true),
            "Zlib" => new ZLibStream(output, CompressionLevel.Optimal, true),
            _ => new BrotliStream(output, CompressionLevel.Optimal, true)
        }) compressor.Write(new byte[CryptoTools.OutputLimit + 1]);
        Assert.Throws<InvalidDataException>(() => CryptoTools.Apply(name, Convert.ToBase64String(output.ToArray()), decrypt: true));
    }

    [Fact]
    public void ExpansionAndSpecialAlgorithmBudgetsAreEnforced()
    {
        Assert.Throws<ArgumentException>(() => CryptoTools.Apply("Unicode \\uXXXX", new string('a', CryptoTools.InputLimit)));
        Assert.Throws<ArgumentException>(() => CryptoTools.Apply("Base58", new string('a', 16385)));
        Assert.Throws<ArgumentException>(() => CryptoTools.Apply("Punycode", new string('a', 4097)));
    }

    [Theory]
    [InlineData("Base32hex")]
    [InlineData("Base58")]
    [InlineData("Ascii85")]
    [InlineData("Quoted-printable")]
    public void AllPartialGroupsAndLeadingZerosRoundTrip(string name)
    {
        for (var length = 0; length < 40; length++)
        {
            var text = "\0\0" + new string('A', length) + "🙂";
            Assert.Equal(text, CryptoTools.Apply(name, CryptoTools.Apply(name, text), decrypt: true));
        }
    }
}
