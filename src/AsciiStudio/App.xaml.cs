using Microsoft.UI.Xaml;

namespace AsciiStudio;

public partial class App : Application
{
    public static MainWindow Window { get; private set; } = null!;
    public App() { InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
    }
}
