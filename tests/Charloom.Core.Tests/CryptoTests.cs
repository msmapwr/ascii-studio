using Charloom.Core;
using Xunit;

namespace Charloom.Core.Tests;

public sealed class CryptoTests
{
    [Fact]
    public void LegacyBrandAuthenticatedCipherRemainsReadable()
    {
        // Independent AES-GCM vector with the original AsciiStudio authentication
        // context. Public test password and deterministic salt/nonce are fixtures only.
        const string cipher = """{"Version":1,"Algorithm":"AES-256-GCM","Iterations":600000,"Salt":"AAECAwQFBgcICQoLDA0ODw==","Nonce":"AAECAwQFBgcICQoL","Cipher":"uyF/4y3WLgwpjVW1Jg==","Tag":"4tRdu9dlilf35h68R86DLQ==","WrappedKey":""}""";
        Assert.Equal("legacy 测试", CryptoTools.Apply("AES-256-GCM", cipher, "public-test-password", decrypt: true));
    }

    [Fact(DisplayName = "authenticated ciphers round trip and reject altered metadata")]
    public void AuthenticatedCiphersRoundTripAndRejectAlteredMetadata()
    {
        const string text = "ASCII\n测试 🙂";
        foreach (var name in CryptoTools.Modern.Where(n => !n.StartsWith("RSA-") && CryptoTools.IsSupported(n)))
        {
            var cipher = CryptoTools.Apply(name, text, "secret");
            Assert.True(CryptoTools.Apply(name, cipher, "secret", true) == text, "Cipher round trip failed: " + name);
            var changed = cipher.Replace("600000", "99999999");
            try { CryptoTools.Apply(name, changed, "secret", true); throw new Exception("Unbounded KDF metadata accepted"); } catch (ArgumentException) { }
        }
        var protectedText = CryptoTools.Apply("AES-256-GCM", text, "secret");
        try { CryptoTools.Apply("AES-256-GCM", protectedText, "wrong", true); throw new Exception("Wrong password accepted"); } catch (System.Security.Cryptography.CryptographicException) { }
    }

    [Fact(DisplayName = "RSA hybrid cipher supports arbitrary Unicode text")]
    public void RSAHybridCipherSupportsArbitraryUnicodeText()
    {
        var keys = CryptoTools.GenerateRsaKeys(); const string name = "RSA-OAEP-SHA256 + AES-256-GCM";
        var cipher = CryptoTools.Apply(name, "测试\nASCII", keyPem: keys.PublicKey);
        Assert.True(CryptoTools.Apply(name, cipher, decrypt: true, keyPem: keys.PrivateKey) == "测试\nASCII", "RSA hybrid round trip failed");
    }

    [Fact(DisplayName = "encoding and traditional methods round trip")]
    public void EncodingAndTraditionalMethodsRoundTrip()
    {
        foreach (var name in CryptoTools.Encodings)
        {
            var encoded = CryptoTools.Apply(name, "abc测试🙂\n");
            Assert.True(CryptoTools.Apply(name, encoded, decrypt: true) == "abc测试🙂\n", "Encoding round trip failed: " + name);
        }
        foreach (var name in CryptoTools.Traditional)
        {
            var key = name == "Caesar" ? "-27" : name == "Rail Fence" ? "3" : "secret";
            Assert.True(CryptoTools.Apply(name, CryptoTools.Apply(name, "Abc 测试🙂!\n", key), key, true) == "Abc 测试🙂!\n", "Traditional round trip failed: " + name);
        }
        Assert.True(CryptoTools.Apply("Base32", "foo") == "MZXW6===", "RFC 4648 Base32 vector failed");
    }

    [Fact(DisplayName = "digest known value and input boundaries")]
    public void DigestKnownValueAndInputBoundaries()
    {
        Assert.True(CryptoTools.Apply("SHA-256", "abc") == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA256 vector failed");
        try { CryptoTools.Apply("SHA-256", "abc", decrypt: true); throw new Exception("Digest was decrypted"); } catch (ArgumentException) { }
        try { CryptoTools.Apply("AES-256-GCM", new string('a', CryptoTools.InputLimit + 1), "secret"); throw new Exception("Oversize input accepted"); } catch (ArgumentException) { }
    }
}
