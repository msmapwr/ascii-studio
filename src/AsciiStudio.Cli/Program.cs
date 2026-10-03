using System.Text;
using AsciiStudio.Cli;

Console.InputEncoding = new UTF8Encoding(false, true);
Console.OutputEncoding = new UTF8Encoding(false);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    // Configure isolation before any shared workspace/font static is initialized.
    var parsed = CliArguments.Parse(args);
    if (parsed.Flag("desktop-data") && parsed.Has("data-directory")) throw new CliUsageException("--desktop-data conflicts with --data-directory.");
    var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AsciiStudio");
    Environment.SetEnvironmentVariable("ASCIISTUDIO_DATA_DIRECTORY", parsed.Flag("desktop-data") ? desktop : parsed.Get("data-directory", Path.Combine(desktop, "Cli")));
    Environment.SetEnvironmentVariable("ASCIISTUDIO_FONT_DIRECTORY", parsed.Get("font-directory", Path.Combine(desktop, "fonts")));
    return await CliHost.Run(args, Console.Out, Console.Error, Console.In, cancellation.Token);
}
catch (CliUsageException error)
{
    var json = args.Any(value => value is "--json" or "--json=true");
    await Console.Error.WriteLineAsync(json ? System.Text.Json.JsonSerializer.Serialize(new { ok = false, code = "usage", message = error.Message }) : "usage: " + error.Message);
    return 2;
}
