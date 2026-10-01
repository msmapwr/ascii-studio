using AsciiStudio.Controls;
using AsciiStudio.Core;
using AsciiStudio.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace AsciiStudio.Pages;

public sealed class CryptoPage : Grid
{
    private readonly ComboBox category = Ui.Choice(["可解密加密", "不可逆摘要", "编码", "传统密码教学"]);
    private readonly ComboBox algorithm = Ui.Choice(CryptoTools.Modern);
    private readonly TextBox input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 150, MaxLength = CryptoTools.OutputLimit };
    private readonly TextBox output = new() { AcceptsReturn = true, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxLength = CryptoTools.OutputLimit, FontFamily = new("Consolas") };
    private readonly PasswordBox secret = new() { MaxLength = 4096 };
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
        source.Children.Add(Ui.Field("口令 / 密钥 / 教学参数", secret));
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
            algorithm.Items.Clear(); foreach (var name in category.SelectedIndex switch { 1 => CryptoTools.Digests, 2 => CryptoTools.Encodings, 3 => CryptoTools.Traditional, _ => CryptoTools.Modern }) algorithm.Items.Add(name);
            algorithm.SelectedIndex = 0; UpdateMode();
        };
        algorithm.SelectionChanged += (_, _) => UpdateMode(); UpdateMode();
    }
    private void UpdateMode()
    {
        var name = algorithm.SelectedItem?.ToString() ?? ""; keys.Visibility = name.StartsWith("RSA-") ? Visibility.Visible : Visibility.Collapsed;
        decrypt.IsEnabled = category.SelectedIndex != 1;
        encrypt.IsEnabled = CryptoTools.IsSupported(name);
        decrypt.IsEnabled &= CryptoTools.IsSupported(name);
        description.Text = category.SelectedIndex switch
        {
            1 => "摘要不可还原。SHA/HMAC 用于校验；MD5、SHA-1 仅用于旧格式兼容。PBKDF2 每次使用随机盐。",
            2 => "编码可还原，但不保护机密。输入按 UTF-8 处理。",
            3 => "传统密码仅用于教学。Caesar：整数位移；Vigenere：英文密钥；Rail Fence：2–32 轨道。拉丁字母算法保留其他字符。",
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
            var name = algorithm.SelectedItem?.ToString() ?? ""; var text = input.Text.Replace("\r\n", "\n").Replace('\r', '\n'); var password = secret.Password;
            var pem = reverse ? privateKey.Text : publicKey.Text;
            var result = await Task.Run(() => CryptoTools.Apply(name, text, password, reverse, pem));
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
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        var picker = new FileSavePicker { SuggestedFileName = isPrivate ? "AsciiStudio-private" : "AsciiStudio-public" };
        picker.FileTypeChoices.Add("PEM 密钥", [".pem"]); App.Window.InitializePicker(picker);
        var file = await picker.PickSaveFileAsync(); if (file is not null) await File.WriteAllTextAsync(file.Path, value);
    }
}
