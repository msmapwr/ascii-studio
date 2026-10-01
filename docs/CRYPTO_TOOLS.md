# 加密、摘要与编码

所有操作均在本机执行。现代加密输入按 UTF-8 编码，应用界面统一 LF 换行；输入最多 1 MB，密文和编码最多 4 MB。错误不替换上一份结果。

## 方法清单（35 种）

- 现代加密：AES-128/192/256-GCM、AES-256-CCM、ChaCha20-Poly1305、AES-256-CBC + HMAC-SHA256、RSA-OAEP-SHA256 + AES-256-GCM。
- 摘要：SHA-256/384/512、SHA3-256/384/512、SHAKE128/256（输出 32/64 字节）、HMAC-SHA256/384/512、PBKDF2-SHA256/512、MD5、SHA-1。
- 编码：Base64、Base64URL、Base32（RFC 4648）、Hex、URL、二进制。
- 教学：ROT13、ROT47、Caesar、Atbash、Vigenere、Rail Fence、XOR。

摘要不可还原；编码不保护机密；传统密码仅用于教学。MD5/SHA-1 用于旧格式兼容。界面根据系统 API 能力禁用不可用算法。

## 现代加密格式

- 口令通过 PBKDF2-HMAC-SHA256、600000 次迭代派生密钥；每次生成随机 16 字节盐。
- GCM/CCM/ChaCha 使用随机 12 字节 nonce 和 16 字节认证标签。CBC 使用随机 IV，并在解密前校验 HMAC。
- RSA 混合加密生成随机 AES 密钥，以 OAEP-SHA256 包装，因此可处理完整字符画而不受 RSA 直接加密消息长度限制。
- JSON 容器包含版本、算法、盐、nonce、密文、认证标签和必要的包装密钥。元数据参与认证；不可随意删除字段。此为 AsciiStudio 格式，不宣称兼容其他工具的密文格式。
- 不在项目、设置或恢复快照中保存口令或私钥。可自行导入 PEM。生成的 RSA 密钥为 3072 位，导出未加密私钥前会提示确认。
- PBKDF2 摘要以输入文本作为口令，每次返回随机盐和派生结果；HMAC 使用口令栏中的文本作为密钥。

## 简单使用

选择方法 → 粘贴文本或使用当前字符画 → 填写需要的口令/密钥 → 生成。点击“结果放入输入”可继续解码或解密；RSA 解密需私钥。点击“清空文本与密钥”后，不会让尚未完成的任务重新填回结果。

传统参数：Caesar 为整数位移；Vigenere 为英文密钥；Rail Fence 为 2–32 轨道。ROT13、ROT47、Atbash 无需密钥。XOR 用 Base64 表示其字节结果，不提供完整性认证。字母替换保留中文与其他非拉丁字符，Rail Fence 按 Unicode 字符处理。

实现依据：[.NET 加密能力](https://learn.microsoft.com/dotnet/standard/security/cross-platform-cryptography)、[PBKDF2 API](https://learn.microsoft.com/dotnet/api/system.security.cryptography.rfc2898derivebytes.pbkdf2)。
