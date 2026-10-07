using Charloom.Controls;
using Charloom.Core;
using Charloom.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Charloom.Pages;

public sealed class CryptoPage : Grid
{
    private readonly ComboBox category = Ui.Choice(["可解密加密", "不可逆摘要", "编码", "传统密码教学", "字符编码", "Unicode 与转义", "二进制转文本", "压缩", "校验"]);
    private readonly ComboBox algorithm = Ui.Choice(CryptoTools.Modern);
    private readonly TextBox input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 150, MaxLength = CryptoTools.OutputLimit };
    private readonly TextBox output = new() { AcceptsReturn = true, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxLength = CryptoTools.OutputLimit, FontFamily = new("Consolas") };
    private readonly PasswordBox secret = new() { MaxLength = 4096 };
    private readonly ComboBox byteFormat = Ui.Choice(["Hex", "Base64"]);
    private readonly ToggleSwitch bom = new() { Header = "添加 BOM", IsOn = false };
    private readonly StackPanel encodingOptions = Ui.Stack();
    private readonly FrameworkElement secretField;
    private readonly TextBox publicKey = new() { AcceptsReturn = true, Height = 100, MaxLength = 16384, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox privateKey = new() { AcceptsReturn = true, Height = 100, MaxLength = 16384, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock description = Ui.Text("", 12, true);
    private readonly StackPanel keys = Ui.Stack();
    private readonly Button decrypt;
    private readonly Button encrypt;
    private readonly ContentControl inputHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ProgressRing progress = new() { IsActive = false, Width = 24, Height = 24 };
    private bool busy;
    private int operationVersion;
    private string resultText = "";
    public CryptoPage()
    {
        var source = Ui.Stack();
        source.Children.Add(Ui.Field("方法分类", category)); source.Children.Add(Ui.Field("算法", algorithm));
        source.Children.Add(description); source.Children.Add(Ui.Field("文本 / 密文", input));
        source.Children.Add(Ui.Button("使用当前字符画", () => input.Text = WorkspaceService.CurrentArt?.Text ?? ""));
        secretField = Ui.Field("口令 / 密钥 / 教学参数", secret);
        source.Children.Add(secretField);
        encodingOptions.Children.Add(Ui.Field("字节表示（编码与还原使用同一格式）", byteFormat));
        encodingOptions.Children.Add(bom); source.Children.Add(encodingOptions);
        AutomationProperties.SetAutomationId(byteFormat, "CryptoByteFormat");
        AutomationProperties.SetAutomationId(bom, "CryptoBom");
        keys.Children.Add(Ui.Field("RSA 公钥（PEM）", publicKey)); keys.Children.Add(Ui.Field("RSA 私钥（PEM，仅用于解密）", privateKey));
        keys.Children.Add(Ui.AsyncButton("生成 RSA 3072 位密钥", async () =>
        {
            if (busy) return; busy = true; inputHost.IsEnabled = false; progress.IsActive = true;
            var version = ++operationVersion;
            try
            {
                var pair = await Task.Run(CryptoTools.GenerateRsaKeys); if (version != operationVersion) return;
                publicKey.Text = pair.PublicKey; privateKey.Text = pair.PrivateKey;
                App.Window.Message("密钥仅保留在当前页面。公钥用于加密，私钥用于解密，请妥善保存。");
            }
            finally { busy = false; inputHost.IsEnabled = true; progress.IsActive = false; }
        }));
        keys.Children.Add(Ui.AsyncButton("导出公钥", () => ExportKey(false)));
        keys.Children.Add(Ui.AsyncButton("导出私钥", () => ExportKey(true)));
        source.Children.Add(keys);
        encrypt = Ui.AsyncButton("生成 / 加密", () => Process(false), true); decrypt = Ui.AsyncButton("还原 / 解密", () => Process(true));
        AutomationProperties.SetAutomationId(encrypt, "CryptoEncrypt"); AutomationProperties.SetAutomationId(decrypt, "CryptoDecrypt");
        source.Children.Add(Ui.SettingsGrid(encrypt, decrypt));
        source.Children.Add(progress); inputHost.Content = source;
        var target = new Grid { RowSpacing = 12 };
        target.RowDefinitions.Add(new() { Height = GridLength.Auto }); target.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var actions = Ui.Stack(8); actions.Orientation = Orientation.Horizontal;
        actions.Children.Add(Ui.Button("复制结果", () => { var data = new DataPackage(); data.SetText(resultText); Clipboard.SetContent(data); }));
        actions.Children.Add(Ui.Button("结果放入输入", () => input.Text = resultText));
        actions.Children.Add(Ui.Button("清空文本与密钥", () => { operationVersion++; resultText = input.Text = output.Text = publicKey.Text = privateKey.Text = secret.Password = ""; }));
        target.Children.Add(actions); Grid.SetRow(output, 1); target.Children.Add(output);
        Children.Add(Ui.Page(Ui.Heading("加密与编码", ""), Ui.Workspace(Ui.Card(inputHost), Ui.Card(target))));
        AutomationProperties.SetAutomationId(input, "CryptoInput"); AutomationProperties.SetAutomationId(output, "CryptoOutput");
        AutomationProperties.SetAutomationId(category, "CryptoCategory"); AutomationProperties.SetAutomationId(algorithm, "CryptoAlgorithm");
        category.SelectionChanged += (_, _) =>
        {
            algorithm.Items.Clear(); foreach (var name in category.SelectedIndex switch
            {
                1 => CryptoTools.Digests, 2 => CryptoTools.Encodings, 3 => CryptoTools.Traditional,
                4 => TextProcessing.CharacterEncodings, 5 => TextProcessing.Representations,
                6 => TextProcessing.BinaryEncodings, 7 => TextProcessing.Compression,
                8 => TextProcessing.Checksums, _ => CryptoTools.Modern
            }) algorithm.Items.Add(name);
            algorithm.SelectedIndex = 0; UpdateMode();
        };
        algorithm.SelectionChanged += (_, _) => UpdateMode(); UpdateMode();
    }
    private void UpdateMode()
    {
        var name = Ui.ChoiceValue(algorithm) ?? ""; keys.Visibility = name.StartsWith("RSA-") ? Visibility.Visible : Visibility.Collapsed;
        decrypt.IsEnabled = category.SelectedIndex != 1 && TextProcessing.CanReverse(name);
        encodingOptions.Visibility = category.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        bom.Visibility = name is "UTF-8" or "UTF-16LE" or "UTF-16BE" ? Visibility.Visible : Visibility.Collapsed;
        secretField.Visibility = category.SelectedIndex is 0 or 3 || name.StartsWith("HMAC", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        encrypt.Content = category.SelectedIndex switch { 0 => "加密", 1 => "生成摘要", 7 => "压缩", 8 => "计算校验", _ when name is "NFC" or "NFKC" => "规范化", _ => "编码 / 转换" };
        decrypt.Content = category.SelectedIndex switch { 0 => "解密", 7 => "解压", _ => "还原 / 解码" };
        encrypt.IsEnabled = CryptoTools.IsSupported(name);
        decrypt.IsEnabled &= CryptoTools.IsSupported(name);
        description.Text = category.SelectedIndex switch
        {
            1 => "摘要不可还原。SHA/HMAC 用于校验；MD5、SHA-1 仅用于旧格式兼容。PBKDF2 每次使用随机盐。",
            2 => "编码可还原，但不保护机密。输入按 UTF-8 处理。",
            3 => "传统密码仅用于教学。Caesar：整数位移；Vigenere：英文密钥；Rail Fence：2–32 轨道。拉丁字母算法保留其他字符。",
            4 => "按所选字符编码转换为 Hex 或 Base64 字节；还原时选择相同编码和字节表示。不能表示的字符或无效字节会报错。UTF 编码默认无 BOM，还原会识别并移除匹配的 BOM。",
            5 when name is "NFC" or "NFKC" => "Unicode 规范化不可逆。NFC 合并组合字符；NFKC 还会转换兼容字符（如全角字母），结果可能改变字符形式。",
            5 when name == "Punycode" => "RFC 3492 原始 Punycode，保留大小写，不进行 IDNA 映射，不添加 xn-- 前缀。输入最多 4096 字符。",
            5 when name == "JSON 转义" => "生成带双引号的完整 JSON 字符串；还原时也需输入完整字符串。",
            5 => "Unicode 短转义按 UTF-16 单元表示，长转义与数字实体按 Unicode 码点表示；支持中文和 emoji，非法码点会报错。",
            6 when name == "Base58" => "Bitcoin 字母表，不含 0、O、I、l；不附带 Base58Check 校验。适合短文本，输入／解码字节最多 16 KB。",
            6 when name == "Ascii85" => "Adobe Ascii85，输出含 <~ ~> 和零分组 z；还原也接受无边界符格式。字节按 UTF-8 处理。",
            6 => "Base32hex 使用 RFC 4648 字母表；Quoted-printable 使用 MIME 转义和软换行。字节按 UTF-8 处理，编码不保护机密。",
            7 => "文本按 UTF-8 压缩，标准压缩字节以 Base64 展示。还原需选择相同方法，解压结果最多 4 MB；压缩不保护机密。",
            8 => "按 UTF-8 字节计算 8 位十六进制校验值。CRC32 使用 IEEE 多项式，Adler-32 使用标准初始值。只能校验，不能还原，也不是加密或安全摘要。",
            _ => "口令派生密钥，随机盐与 nonce；密文自带算法参数。RSA 加密使用公钥、解密使用私钥。输入最多 1 MB，密文最多 4 MB。"
        };
        if (!CryptoTools.IsSupported(name)) description.Text += " 当前系统不支持此方法，请选择其他算法。";
    }
    private async Task Process(bool reverse)
    {
        if (busy) return;
        busy = true; inputHost.IsEnabled = false; progress.IsActive = true;
        var version = ++operationVersion;
        try
        {
            var name = Ui.ChoiceValue(algorithm) ?? ""; var text = input.Text; var password = secret.Password;
            var pem = reverse ? privateKey.Text : publicKey.Text;
            var options = new TextProcessingOptions(byteFormat.SelectedIndex == 1, bom.IsOn);
            var result = await Task.Run(() => CryptoTools.Apply(name, text, password, reverse, pem, options));
            if (version == operationVersion) { resultText = result; output.Text = result; }
        }
        finally { busy = false; inputHost.IsEnabled = true; progress.IsActive = false; }
    }
    private async Task ExportKey(bool isPrivate)
    {
        var value = isPrivate ? privateKey.Text : publicKey.Text;
        if (string.IsNullOrEmpty(value)) throw new ArgumentException("请先生成或导入密钥。");
        if (isPrivate)
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "导出未加密私钥？", Content = "此 PEM 文件可以解密对应密文。请保存到安全位置，不要分享或提交到 Git。", PrimaryButtonText = "导出私钥", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
            if (await Ui.ShowDialog(dialog) != ContentDialogResult.Primary) return;
        }
        var picker = new FileSavePicker { SuggestedFileName = isPrivate ? "Charloom-private" : "Charloom-public" };
        picker.FileTypeChoices.Add(GuiText.Translate("PEM 密钥"), [".pem"]); App.Window.InitializePicker(picker);
        var file = await picker.PickSaveFileAsync(); if (file is not null) await File.WriteAllTextAsync(file.Path, value);
    }
}
