using Microsoft.UI.Xaml;

namespace AsciiStudio;

public partial class App : Application
{
    public static MainWindow Window { get; private set; } = null!;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try { Directory.CreateDirectory(Services.WorkspaceService.DataDirectory); File.AppendAllText(Path.Combine(Services.WorkspaceService.DataDirectory, "errors.log"), $"{DateTimeOffset.Now:O} UI unhandled: {e.Message}\n{e.Exception}\n"); } catch (IOException) { }
            System.Diagnostics.Debug.WriteLine($"UI unhandled: {e.Message} {e.Exception}");
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
        _ = Window.Guard(Window.RestoreSessionAsync);
    }
}
