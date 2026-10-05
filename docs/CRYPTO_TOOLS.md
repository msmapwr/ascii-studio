# 加密、摘要与编码

所有操作均在本机执行。现代加密输入按 UTF-8 编码；处理页面不再主动改写输入换行。输入最多 1 MB，密文和编码最多 4 MB。错误不替换上一份结果。

## 方法清单（原有 35 种＋新增 25 种）

- 现代加密：AES-128/192/256-GCM、AES-256-CCM、ChaCha20-Poly1305、AES-256-CBC + HMAC-SHA256、RSA-OAEP-SHA256 + AES-256-GCM。
- 摘要：SHA-256/384/512、SHA3-256/384/512、SHAKE128/256（输出 32/64 字节）、HMAC-SHA256/384/512、PBKDF2-SHA256/512、MD5、SHA-1。
- 编码：Base64、Base64URL、Base32（RFC 4648）、Hex、URL、二进制。
- 教学：ROT13、ROT47、Caesar、Atbash、Vigenere、Rail Fence、XOR。
- 字符编码：ASCII、UTF-8、UTF-16LE、UTF-16BE、GB18030、GBK、Big5、Shift_JIS。
- Unicode 与转义：`\uXXXX`、`\UXXXXXXXX`、HTML 十进制／十六进制实体、JSON 转义、NFC、NFKC、Punycode。
- 二进制转文本：Base32hex、Base58、Ascii85、Quoted-printable。
- 压缩：GZIP、Zlib、Brotli。
- 校验：CRC32、Adler-32。

## 新方法的使用约定

- 字符编码默认输出 Hex，可切换 Base64。还原需选择相同编码和字节表示；Hex 允许空白分隔。字符不能表示或字节序列无效时严格报错，不用问号替换。
- UTF-8／UTF-16 默认不添加 BOM，可开启添加；还原识别并移除匹配的 BOM。UTF-16 明确区分字节序。
- 短 Unicode 转义按 UTF-16 单元表示（emoji 为一对转义）；长转义和数字实体按 Unicode 码点表示。非法代理项或码点报错。
- JSON 转义输出带双引号的完整字符串，反向输入也需要完整字符串；不是整个 JSON 文档序列化功能。
- NFC 合并组合字符，NFKC 还转换兼容字符。两者不可逆；界面禁用还原。
- Punycode 按 RFC 3492 输出原始编码，不做 IDNA 映射或大小写转换，不自动添加 `xn--`。原文上限 4096 字符，反向编码输入上限 16 KB。
- Base32hex 使用 RFC 4648 标准字母表与填充，支持大小写输入及省略填充；校验尾部位。Base58 使用 Bitcoin 字母表，无 Base58Check 校验，原文／解码字节上限 16 KB，适合短文本。
- Ascii85 使用 Adobe 边界符 `<~ ~>`、零分组 `z`，反向接受无边界符格式。Quoted-printable 按 MIME 规则折行，编码空白和原始换行以保留内容。
- GZIP／Zlib／Brotli 将 UTF-8 文本压缩后以 Base64 展示，反向必须选择相同方法。解压按流读取，超过 4 MB 立即停止。没有额外自定义压缩容器，不提供任意二进制文件还原。
- CRC32 为 CRC-32/ISO-HDLC（IEEE），Adler-32 为标准初始值；均按 UTF-8 字节计算，显示 8 位小写 Hex。校验不可逆，不是加密，也不是安全摘要。

实现依据：[RFC 4648](https://www.rfc-editor.org/info/rfc4648/)、[RFC 2045](https://www.rfc-editor.org/info/rfc2045/)、[RFC 3492](https://www.rfc-editor.org/info/rfc3492/)、[.NET 字符编码](https://learn.microsoft.com/dotnet/api/system.text.encoding)。

摘要不可还原；编码不保护机密；传统密码仅用于教学。MD5/SHA-1 用于旧格式兼容。界面根据系统 API 能力禁用不可用算法。

## 现代加密格式

- 口令通过 PBKDF2-HMAC-SHA256、600000 次迭代派生密钥；每次生成随机 16 字节盐。
- GCM/CCM/ChaCha 使用随机 12 字节 nonce 和 16 字节认证标签。CBC 使用随机 IV，并在解密前校验 HMAC。
- RSA 混合加密生成随机 AES 密钥，以 OAEP-SHA256 包装，因此可处理完整字符画而不受 RSA 直接加密消息长度限制。
- JSON 容器包含版本、算法、盐、nonce、密文、认证标签和必要的包装密钥。元数据参与认证；不可随意删除字段。此为 Charloom 格式，不宣称兼容其他工具的密文格式。
- 不在项目、设置或恢复快照中保存口令或私钥。可自行导入 PEM。生成的 RSA 密钥为 3072 位，导出未加密私钥前会提示确认。
- PBKDF2 摘要以输入文本作为口令，每次返回随机盐和派生结果；HMAC 使用口令栏中的文本作为密钥。

## 简单使用

选择方法 → 粘贴文本或使用当前字符画 → 填写需要的口令/密钥 → 生成。点击“结果放入输入”可继续解码或解密；RSA 解密需私钥。点击“清空文本与密钥”后，不会让尚未完成的任务重新填回结果。

传统参数：Caesar 为整数位移；Vigenere 为英文密钥；Rail Fence 为 2–32 轨道。ROT13、ROT47、Atbash 无需密钥。XOR 用 Base64 表示其字节结果，不提供完整性认证。字母替换保留中文与其他非拉丁字符，Rail Fence 按 Unicode 字符处理。

实现依据：[.NET 加密能力](https://learn.microsoft.com/dotnet/standard/security/cross-platform-cryptography)、[PBKDF2 API](https://learn.microsoft.com/dotnet/api/system.security.cryptography.rfc2898derivebytes.pbkdf2)。
