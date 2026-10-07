using System.Text;
using Charloom.Cli;
using Charloom.Services;

Console.InputEncoding = new UTF8Encoding(false, true);
Console.OutputEncoding = new UTF8Encoding(false);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    // Configure isolation before any shared workspace/font static is initialized.
    var parsed = CliArguments.Parse(args);
    if (parsed.Flag("desktop-data") && parsed.Has("data-directory")) throw CliDiagnostics.Usage("data_directory_conflict");
    var desktop = ProductIdentity.DesktopDataDirectory;
    Environment.SetEnvironmentVariable("CHARLOOM_DATA_DIRECTORY", parsed.Flag("desktop-data") ? desktop : parsed.Get("data-directory", Path.Combine(desktop, "Cli")));
    Environment.SetEnvironmentVariable("CHARLOOM_FONT_DIRECTORY", parsed.Get("font-directory", Path.Combine(desktop, "fonts")));
    return await CliHost.Run(args, Console.Out, Console.Error, Console.In, cancellation.Token);
}
catch (CliUsageException error)
{
    return await CliHost.ReportFailure(args, Console.Error, error);
}
