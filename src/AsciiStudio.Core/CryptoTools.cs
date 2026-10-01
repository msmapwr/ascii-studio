using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AsciiStudio.Core;

public static class CryptoTools
{
    public const int InputLimit = 1_048_576;
    public const int OutputLimit = 4_194_304;
    private const int Iterations = 600_000;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static readonly string[] Modern = ["AES-128-GCM", "AES-192-GCM", "AES-256-GCM", "AES-256-CCM", "ChaCha20-Poly1305", "AES-256-CBC + HMAC-SHA256", "RSA-OAEP-SHA256 + AES-256-GCM"];
    public static readonly string[] Digests = ["SHA-256", "SHA-384", "SHA-512", "SHA3-256", "SHA3-384", "SHA3-512", "SHAKE128", "SHAKE256", "HMAC-SHA256", "HMAC-SHA384", "HMAC-SHA512", "PBKDF2-SHA256", "PBKDF2-SHA512", "MD5（旧式）", "SHA-1（旧式）"];
    public static readonly string[] Encodings = ["Base64", "Base64URL", "Base32", "Hex", "URL", "二进制"];
    public static readonly string[] Traditional = ["ROT13", "ROT47", "Caesar", "Atbash", "Vigenere", "Rail Fence", "XOR（教学）"];
    private sealed record Envelope(int Version, string Algorithm, int Iterations, string Salt, string Nonce, string Cipher, string Tag, string WrappedKey = "");
    public static bool IsSupported(string algorithm) => algorithm switch
    {
        "SHA3-256" => SHA3_256.IsSupported,
        "SHA3-384" => SHA3_384.IsSupported,
        "SHA3-512" => SHA3_512.IsSupported,
        "SHAKE128" => Shake128.IsSupported,
        "SHAKE256" => Shake256.IsSupported,
        "ChaCha20-Poly1305" => ChaCha20Poly1305.IsSupported,
        "AES-256-CCM" => AesCcm.IsSupported,
        "AES-128-GCM" or "AES-192-GCM" or "AES-256-GCM" or "RSA-OAEP-SHA256 + AES-256-GCM" => AesGcm.IsSupported,
        _ => true
    };

    public static string Apply(string algorithm, string text, string secret = "", bool decrypt = false, string keyPem = "")
    {
        if (secret.Length > 4096) throw new ArgumentException("口令或密钥最多 4096 字符。");
        if (!IsSupported(algorithm)) throw new PlatformNotSupportedException("当前 Windows 版本不支持此算法，请选择其他方法。");
        if (text.Length > OutputLimit || Utf8.GetByteCount(text) > (decrypt ? OutputLimit : InputLimit)) throw new ArgumentException("文本超过限制：输入最多 1 MB，编码或密文最多 4 MB。");
        string result;
        if (Modern.Contains(algorithm)) result = decrypt ? Decrypt(text, secret, keyPem) : Encrypt(algorithm, text, secret, keyPem);
        else if (Digests.Contains(algorithm))
        {
            if (decrypt) throw new ArgumentException("摘要不可还原。需要还原时请选择可解密加密或编码。");
            result = Digest(algorithm, text, secret);
        }
        else if (Encodings.Contains(algorithm)) result = Encode(algorithm, text, decrypt);
        else if (Traditional.Contains(algorithm)) result = Classical(algorithm, text, secret, decrypt);
        else throw new ArgumentException("未知算法。");
        if (result.Length > OutputLimit) throw new ArgumentException("结果超过 4 MB，请减少输入内容。");
        return result;
    }

    public static (string PublicKey, string PrivateKey) GenerateRsaKeys()
    {
        using var rsa = RSA.Create(3072);
        return (rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem());
    }
    private static byte[] PasswordKey(string secret, byte[] salt, int length)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length > 4096) throw new ArgumentException("请填写口令或密钥（最多 4096 字符）。");
        return Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, length);
    }
    private static byte[] Associated(Envelope value) => Utf8.GetBytes($"AsciiStudio|{value.Version}|{value.Algorithm}|{value.Iterations}|{value.Salt}|{value.Nonce}|{value.WrappedKey}");
    private static string Encrypt(string algorithm, string text, string secret, string keyPem)
    {
        var plain = Utf8.GetBytes(text); var rsaMode = algorithm.StartsWith("RSA-");
        byte[] salt = rsaMode ? [] : RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(algorithm.Contains("CBC") ? 16 : 12);
        var keySize = algorithm.Contains("CBC") ? 64 : algorithm == "AES-128-GCM" ? 16 : algorithm == "AES-192-GCM" ? 24 : 32;
        var key = rsaMode ? RandomNumberGenerator.GetBytes(32) : PasswordKey(secret, salt, keySize);
        try
        {
            var wrapped = "";
            if (rsaMode)
            {
                using var rsa = Rsa(keyPem); wrapped = Convert.ToBase64String(rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA256));
            }
            var envelope = new Envelope(1, algorithm, rsaMode ? 0 : Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(nonce), "", "", wrapped);
            var associated = Associated(envelope); var cipher = new byte[plain.Length]; var tag = new byte[16];
            if (algorithm.Contains("CBC"))
            {
                using var aes = Aes.Create(); aes.Key = key[..32]; cipher = aes.EncryptCbc(plain, nonce, PaddingMode.PKCS7);
                tag = HMACSHA256.HashData(key[32..], (byte[])[.. associated, .. cipher]);
            }
            else if (algorithm.Contains("CCM")) { using var aes = new AesCcm(key); aes.Encrypt(nonce, plain, cipher, tag, associated); }
            else if (algorithm.StartsWith("ChaCha")) { using var chacha = new ChaCha20Poly1305(key); chacha.Encrypt(nonce, plain, cipher, tag, associated); }
            else { using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, plain, cipher, tag, associated); }
            return JsonSerializer.Serialize(envelope with { Cipher = Convert.ToBase64String(cipher), Tag = Convert.ToBase64String(tag) });
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    private static RSA Rsa(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem) || pem.Length > 16_384) throw new ArgumentException("请提供 PEM 格式 RSA 公钥或私钥。");
        var rsa = RSA.Create();
        try { rsa.ImportFromPem(pem); if (rsa.KeySize < 2048) throw new ArgumentException("RSA 密钥至少需要 2048 位。"); return rsa; }
        catch { rsa.Dispose(); throw; }
    }
    private static string Decrypt(string text, string secret, string keyPem)
    {
        var value = JsonSerializer.Deserialize<Envelope>(text) ?? throw new ArgumentException("密文容器为空。");
        if (value.Version != 1 || !Modern.Contains(value.Algorithm)) throw new ArgumentException("不支持的密文格式或算法。");
        var rsaMode = value.Algorithm.StartsWith("RSA-");
        if (value.Iterations != (rsaMode ? 0 : Iterations)) throw new ArgumentException("不支持的密钥派生参数。");
        var salt = Convert.FromBase64String(value.Salt); var nonce = Convert.FromBase64String(value.Nonce);
        var cipher = Convert.FromBase64String(value.Cipher); var tag = Convert.FromBase64String(value.Tag);
        var cbc = value.Algorithm.Contains("CBC");
        if (salt.Length != (rsaMode ? 0 : 16) || nonce.Length != (cbc ? 16 : 12) || tag.Length != (cbc ? 32 : 16) || cipher.Length > InputLimit + (cbc ? 16 : 0))
            throw new ArgumentException("密文参数或长度无效。");
        var keySize = cbc ? 64 : value.Algorithm == "AES-128-GCM" ? 16 : value.Algorithm == "AES-192-GCM" ? 24 : 32;
        byte[] key;
        if (rsaMode) { using var rsa = Rsa(keyPem); key = rsa.Decrypt(Convert.FromBase64String(value.WrappedKey), RSAEncryptionPadding.OaepSHA256); }
        else key = PasswordKey(secret, salt, keySize);
        var plain = new byte[cipher.Length];
        try
        {
            if (key.Length != (rsaMode ? 32 : keySize)) throw new CryptographicException("密钥长度无效。");
            var associated = Associated(value);
            if (cbc)
            {
                var expected = HMACSHA256.HashData(key[32..], (byte[])[.. associated, .. cipher]);
                if (!CryptographicOperations.FixedTimeEquals(expected, tag)) throw new CryptographicException("口令错误或密文已修改。");
                using var aes = Aes.Create(); aes.Key = key[..32]; plain = aes.DecryptCbc(cipher, nonce, PaddingMode.PKCS7);
            }
            else if (value.Algorithm.Contains("CCM")) { using var aes = new AesCcm(key); aes.Decrypt(nonce, cipher, tag, plain, associated); }
            else if (value.Algorithm.StartsWith("ChaCha")) { using var chacha = new ChaCha20Poly1305(key); chacha.Decrypt(nonce, cipher, tag, plain, associated); }
            else { using var aes = new AesGcm(key, 16); aes.Decrypt(nonce, cipher, tag, plain, associated); }
            return Utf8.GetString(plain);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    private static string Digest(string algorithm, string text, string secret)
    {
        var data = Utf8.GetBytes(text);
        if (algorithm.StartsWith("PBKDF2"))
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = algorithm.EndsWith("512") ? HashAlgorithmName.SHA512 : HashAlgorithmName.SHA256;
            var bytes = Rfc2898DeriveBytes.Pbkdf2(data, salt, Iterations, hash, 32);
            return $"{algorithm}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(bytes)}";
        }
        var key = Utf8.GetBytes(secret);
        try
        {
            if (algorithm.StartsWith("HMAC") && key.Length == 0) throw new ArgumentException("HMAC 需要密钥。");
            var bytes = algorithm switch
            {
                "SHA-256" => SHA256.HashData(data),
                "SHA-384" => SHA384.HashData(data),
                "SHA-512" => SHA512.HashData(data),
                "SHA3-256" => SHA3_256.HashData(data),
                "SHA3-384" => SHA3_384.HashData(data),
                "SHA3-512" => SHA3_512.HashData(data),
                "SHAKE128" => Shake128.HashData(data, 32),
                "SHAKE256" => Shake256.HashData(data, 64),
                "HMAC-SHA256" => HMACSHA256.HashData(key, data),
                "HMAC-SHA384" => HMACSHA384.HashData(key, data),
                "HMAC-SHA512" => HMACSHA512.HashData(key, data),
                "MD5（旧式）" => MD5.HashData(data),
                "SHA-1（旧式）" => SHA1.HashData(data),
                _ => throw new ArgumentException("未知摘要算法。")
            };
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(data); }
    }
    private static string Encode(string algorithm, string text, bool decode)
    {
        if (algorithm == "URL") return decode ? Uri.UnescapeDataString(text) : Uri.EscapeDataString(text);
        if (decode)
        {
            var bytes = algorithm switch
            {
                "Base64" => Convert.FromBase64String(text),
                "Base64URL" => Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/').PadRight((text.Length + 3) / 4 * 4, '=')),
                "Base32" => Base32Decode(text),
                "Hex" => Convert.FromHexString(text.Replace(" ", "")),
                "二进制" => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Length == 8 ? Convert.ToByte(s, 2) : throw new ArgumentException("每个二进制字节必须为 8 位。")).ToArray(),
                _ => throw new ArgumentException("未知编码。")
            };
            return Utf8.GetString(bytes);
        }
        var data = Utf8.GetBytes(text);
        if (algorithm == "二进制" && (long)data.Length * 9 > OutputLimit) throw new ArgumentException("二进制输出超过 4 MB，请减少输入内容。");
        return algorithm switch
        {
            "Base64" => Convert.ToBase64String(data),
            "Base64URL" => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            "Base32" => Base32Encode(data),
            "Hex" => Convert.ToHexString(data),
            "二进制" => string.Join(' ', data.Select(b => Convert.ToString(b, 2).PadLeft(8, '0'))),
            _ => throw new ArgumentException("未知编码。")
        };
    }
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private static string Base32Encode(byte[] data)
    {
        var result = new StringBuilder(); var buffer = 0; var bits = 0;
        foreach (var value in data) { buffer = (buffer << 8) | value; bits += 8; while (bits >= 5) { bits -= 5; result.Append(Alphabet[(buffer >> bits) & 31]); } }
        if (bits > 0) result.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        while (result.Length % 8 != 0) result.Append('='); return result.ToString();
    }
    private static byte[] Base32Decode(string text)
    {
        var bytes = new List<byte>(); var buffer = 0; var bits = 0;
        foreach (var value in text.TrimEnd('=').ToUpperInvariant())
        {
            var index = Alphabet.IndexOf(value); if (index < 0) throw new FormatException("Base32 字符无效。");
            buffer = (buffer << 5) | index; bits += 5; if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); }
        }
        if (bits >= 5 || (buffer & ((1 << bits) - 1)) != 0) throw new FormatException("Base32 尾部位无效。");
        return bytes.ToArray();
    }
    private static string Classical(string algorithm, string text, string secret, bool decrypt)
    {
        if (algorithm == "XOR（教学）")
        {
            var key = Utf8.GetBytes(secret); if (key.Length == 0) throw new ArgumentException("XOR 需要密钥。");
            try { var data = decrypt ? Convert.FromBase64String(text) : Utf8.GetBytes(text); for (var i = 0; i < data.Length; i++) data[i] ^= key[i % key.Length]; return decrypt ? Utf8.GetString(data) : Convert.ToBase64String(data); }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        if (algorithm == "Rail Fence")
        {
            if (!int.TryParse(secret, out var rails) || rails is < 2 or > 32) throw new ArgumentException("Rail Fence 密钥为 2–32 的轨道数。");
            var runes = text.EnumerateRunes().ToArray(); var route = Enumerable.Range(0, runes.Length).Select(i => { var p = i % (2 * rails - 2); return p < rails ? p : 2 * rails - 2 - p; }).ToArray();
            var order = Enumerable.Range(0, runes.Length).OrderBy(i => route[i]).ToArray();
            if (!decrypt) return string.Concat(order.Select(i => runes[i].ToString()));
            var restored = new Rune[runes.Length]; for (var i = 0; i < order.Length; i++) restored[order[i]] = runes[i]; return string.Concat(restored.Select(r => r.ToString()));
        }
        var shift = 13; var keyIndex = 0; var vigenere = secret.Where(char.IsAsciiLetter).Select(c => char.ToUpperInvariant(c) - 'A').ToArray();
        if (algorithm == "Caesar" && (!int.TryParse(secret, out shift) || shift is < -1_000_000 or > 1_000_000)) throw new ArgumentException("Caesar 密钥为整数位移。");
        if (algorithm == "Vigenere" && vigenere.Length == 0) throw new ArgumentException("Vigenere 密钥需包含英文字母。");
        var output = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (algorithm == "ROT47") { output.Append(c is >= '!' and <= '~' ? (char)('!' + (c - '!' + 47) % 94) : c); continue; }
            if (!char.IsAsciiLetter(c)) { output.Append(c); continue; }
            var start = char.IsAsciiLetterUpper(c) ? 'A' : 'a'; var offset = c - start;
            if (algorithm == "Atbash") offset = 25 - offset;
            else { var delta = algorithm == "Vigenere" ? vigenere[keyIndex++ % vigenere.Length] : shift; offset = ((offset + (decrypt ? -delta : delta)) % 26 + 26) % 26; }
            output.Append((char)(start + offset));
        }
        return output.ToString();
    }
}
