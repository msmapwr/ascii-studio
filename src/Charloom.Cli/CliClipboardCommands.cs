using Charloom.Core;
using Charloom.Services;

namespace Charloom.Cli;

public static partial class CliHost
{
    private sealed partial class Invocation
    {
        private async Task ClipboardCommand()
        {
            switch (args.Command)
            {
                case "clipboard read":
                    var format = args.Get("format", "TXT")!;
                    if (format is not ("TXT" or "PNG")) throw new CliUsageException("Clipboard read supports TXT or PNG.");
                    if (format == "PNG" && !args.Has("output")) throw new CliUsageException("Clipboard image requires --output.");
                    if (args.Get("output") is { } destination) EnsureWritable(destination);
                    if (format == "PNG")
                    {
                        var bytes = await clipboard.ReadPng(token);
                        if (bytes.Length > WindowsClipboardService.MaximumImageBytes) throw new ClipboardContentException("Clipboard image exceeds 40MB.");
                        await Write(args.Require("output"), bytes);
                        if (Json) await Report(new { path = Path.GetFullPath(args.Require("output")), format, bytes = bytes.Length });
                    }
                    else
                    {
                        var text = await clipboard.ReadText(token); WindowsClipboardService.ValidateText(text);
                        token.ThrowIfCancellationRequested();
                        if (args.Get("output") is { } path) { await Write(path, Utf8.GetBytes(text)); if (Json) await Report(new { path = Path.GetFullPath(path), format }); }
                        else if (Json) await Report(new { format, text }); else await output.WriteAsync(text);
                    }
                    break;
                case "clipboard write":
                    var copied = await InputText(); WindowsClipboardService.ValidateText(copied);
                    await clipboard.WriteText(copied, token);
                    if (Json) await Report(new { format = "TXT", bytes = Utf8.GetByteCount(copied), copied = true });
                    break;
                case "clipboard paste":
                    if (!args.Flag("apply")) throw new CliUsageException("Clipboard paste requires explicit --apply; use clipboard read for a copy.");
                    var project = await TargetPath(); args.Values["project"] = [project];
                    using (var session = await ProjectEditSession.Open(project, token))
                    {
                        var selection = args.Flag("selection") ? session.State.Selection ?? throw new CliUsageException("No saved selection; use edit select.") : null;
                        var pasted = await clipboard.ReadText(token); WindowsClipboardService.ValidateText(pasted);
                        pasted = UnicodeGrid.ExpandTabs(pasted);
                        await session.Edit(selection is null ? pasted : TextEditOperations.Replace(session.Current.Document.Text, selection, pasted), token);
                        await Report(session.Summary());
                    }
                    break;
                default: throw new CliUsageException("Unknown clipboard command.");
            }
        }
    }
}
