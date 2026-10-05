using Charloom.Controls;
using Charloom.Core;
using Charloom.Services;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Charloom.Pages;

public sealed class GeneratorPage : Grid, IProjectSessionPage
{
    private readonly ResultPane result = new();
    public ResultPane ResultPane => result;
    public string SessionMode => "generator";
    public event Action<bool>? DirtyChanged { add => result.DirtyChanged += value; remove => result.DirtyChanged -= value; }
    public event Action<AsciiDocument>? DocumentChanged { add => result.DocumentChanged += value; remove => result.DocumentChanged -= value; }
    public void SetSession(string id, string? path) => result.SetSession(id, path);
    public Task<bool> SaveProjectAsync() => result.SaveProjectAsync();
    public Task SaveRecoveryAsync() => result.SaveRecoveryAsync();
    private readonly ComboBox kind = Ui.Choice(["文字边框", "分隔线", "迷宫", "星空", "棋盘", "斜纹", "密度渐变"]);
    private readonly ComboBox style = Ui.Choice(["ASCII / 虚线", "双线 / 点线", "星号 / 等号", "波浪"]);
    private readonly TextBox text = new() { Text = "Hello, ASCII!", AcceptsReturn = true, MinHeight = 100, MaxLength = 2_000_000 };
    private readonly NumberBox width = new() { Value = 60, Minimum = 8, Maximum = 300 };
    private readonly NumberBox height = new() { Value = 20, Minimum = 3, Maximum = 100 };
    private readonly NumberBox seed = new() { Value = 42, Minimum = 0, Maximum = int.MaxValue };
    private readonly TextBlock status = Ui.Text("", 12, true);
    private GeneratorRecipe saved = new();
    private GeneratorRecipe? observed = new();
    private long version;
    private bool applying;

    public GeneratorPage()
    {
        result.RestoreProject = LoadProject;
        var p = Ui.Stack(); p.Children.Add(Ui.Field("生成器", kind));
        p.Children.Add(Ui.Field("边框内容", text));
        var generate = Ui.AsyncButton("生成", Generate, true);
        AutomationProperties.SetAutomationId(generate, "GeneratorGenerate");
        AutomationProperties.SetAutomationId(status, "GeneratorStatus");
        p.Children.Add(generate); p.Children.Add(status);
        var parameters = Ui.Stack();
        parameters.Children.Add(Ui.SettingsGrid(Ui.Field("宽度", width), Ui.Field("高度", height), Ui.Field("边框 / 分隔线样式", style), Ui.Field("随机种子", seed)));
        parameters.Children.Add(Ui.Button("换一个随机种子", () => seed.Value = Random.Shared.Next(int.MaxValue)));
        parameters.Children.Add(Ui.Button("恢复生成器默认参数", Reset));
        result.AddSettings("生成参数", parameters, "GeneratorSettings");
        result.ProjectFactory = document => new(1, document, null, null, saved.Text, "generator", saved.ToParameters());
        result.DraftFactory = document => { var recipe = Capture(); return new(2, document, null, null, recipe.Text, "generator", recipe.ToParameters()); };
        Children.Add(Ui.Page(Ui.Heading("生成器", ""), Ui.Workspace(Ui.Card(p), result)));
        kind.SelectionChanged += (_, _) => { UpdateControls(); Changed(); };
        style.SelectionChanged += (_, _) => Changed();
        text.TextChanged += (_, _) => Changed();
        width.ValueChanged += (_, _) => Changed(); height.ValueChanged += (_, _) => Changed(); seed.ValueChanged += (_, _) => Changed();
        Loaded += (_, _) => WorkspaceService.SettingsChanged += SettingsChanged;
        Unloaded += (_, _) => { version++; WorkspaceService.SettingsChanged -= SettingsChanged; };
        UpdateControls();
    }

    private void SettingsChanged(StudioSettings settings) { if (!settings.AutoConvert) version++; }
    private void UpdateControls()
    {
        var previous = applying; applying = true;
        try
        {
            text.IsEnabled = kind.SelectedIndex == 0; style.IsEnabled = kind.SelectedIndex < 2;
            width.IsEnabled = kind.SelectedIndex != 0; height.IsEnabled = kind.SelectedIndex > 1; seed.IsEnabled = kind.SelectedIndex is 2 or 3;
            width.Maximum = kind.SelectedIndex == 2 ? 100 : 300; height.Maximum = kind.SelectedIndex == 2 ? 80 : 100;
            width.Value = Math.Min(width.Value, width.Maximum); height.Value = Math.Min(height.Value, height.Maximum);
        }
        finally { applying = previous; }
    }

    private GeneratorRecipe Capture()
    {
        int Integer(double value)
        {
            if (!double.IsFinite(value) || value != Math.Truncate(value) || value is < 0 or > int.MaxValue) throw new ArgumentException("尺寸和随机种子必须为有效整数。");
            return (int)value;
        }
        var recipe = new GeneratorRecipe(kind.SelectedIndex, style.SelectedIndex, TextUtilities.Normalize(text.Text), Integer(width.Value), Integer(height.Value), Integer(seed.Value));
        recipe.Validate(); return recipe;
    }

    private void Changed()
    {
        if (applying) return;
        GeneratorRecipe recipe;
        try { recipe = Capture(); }
        catch (ArgumentException error) { version++; observed = null; status.Text = error.Message; return; }
        if (recipe == observed) return;
        observed = recipe;
        if (IsLoaded) result.InputChanged();
        var current = ++version;
        status.Text = "参数已修改，点击“生成”更新结果。";
        if (IsLoaded && WorkspaceService.Settings.AutoConvert) _ = App.Window.Guard(() => GenerateDelayed(current));
    }

    private async Task GenerateDelayed(long current)
    {
        await Task.Delay(WorkspaceService.Settings.ConversionDelay);
        if (current == version && IsLoaded && WorkspaceService.Settings.AutoConvert) await Generate();
    }

    private async Task Generate()
    {
        var current = ++version;
        GeneratorRecipe recipe;
        try { recipe = Capture(); }
        catch (ArgumentException error) { status.Text = error.Message; throw; }
        observed = recipe;
        var family = result.CharacterFontFamily; var metrics = FontCatalog.Measure(family);
        var output = await Task.Run(recipe.Generate);
        if (current != version) return;
        saved = recipe;
        await result.SetDocument(AsciiDocument.FromText(output, kind.SelectedItem?.ToString() ?? "Generator") with { FontFamily = family, CellWidth = metrics.Width, CellHeight = metrics.Height });
        if (current == version) status.Text = $"已生成 · {result.Document!.Width} × {result.Document.Height}";
    }

    private void Apply(GeneratorRecipe recipe)
    {
        applying = true;
        try
        {
            kind.SelectedIndex = recipe.Kind; UpdateControls(); style.SelectedIndex = recipe.Style;
            text.Text = recipe.Text; width.Value = recipe.Width; height.Value = recipe.Height; seed.Value = recipe.Seed;
            observed = recipe;
        }
        finally { applying = false; }
    }

    private void Reset()
    {
        version++; Apply(new(Kind: kind.SelectedIndex)); observed = null; Changed();
    }

    public async Task LoadProject(StudioProject project)
    {
        var recipe = GeneratorRecipe.FromParameters(project.Parameters, project.SourceText);
        version++; saved = recipe; Apply(recipe);
        await result.LoadDocument(project);
        status.Text = "项目已恢复，点击“生成”可用原参数重新生成。";
    }
}
