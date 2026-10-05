using System.Text.Json;
using Charloom.Core;
using Charloom.Services;

namespace Charloom.Cli;

public static partial class CliHost
{
    private sealed partial class Invocation
    {
        private async Task<string> TargetPath()
        {
            if (Args.Get("project") is { } project) return Path.GetFullPath(project);
            using var workspace = await CliWorkspace.Open(Args.Get("workspace"), Token);
            return workspace.State.Active ?? throw new CliUsageException("Specify --project or open a workspace project first.");
        }
        private async Task<string> Replacement()
        {
            var sources = new[] { "text", "input", "stdin" }.Count(name => Args.Has(name) && (name != "stdin" || Args.Flag(name)));
            if (sources != 1) throw new CliUsageException("Editing needs exactly one replacement source: --text, --input, --stdin.");
            var text = Args.Get("text") ?? (Args.Has("input") ? Utf8.GetString(StripBom(await BoundedFile.ReadAsync(Args.Require("input"), 8_000_000, Token))) : await ReadStdin());
            if (Utf8.GetByteCount(text) > 8_000_000) throw new ArgumentException("Replacement exceeds 8MB.");
            return UnicodeGrid.ExpandTabs(text);
        }
        private async Task EditCommand()
        {
            var path = await TargetPath(); Args.Values["project"] = [path];
            if (Args.Command == "project recover")
            {
                var destination = Args.Require("output"); EnsureWritable(destination);
                await Report(await ProjectEditSession.Recover(path, destination, Token)); return;
            }
            if (Args.Command == "project reload")
            {
                if (File.Exists(ProjectEditSession.StatePath(path)) && !Args.Flag("discard-edits")) throw new CliUsageException("Reload discards edit history; explicit --discard-edits required.");
                using var reloaded = await ProjectEditSession.Open(path, Token, true); await reloaded.Persist(Token); await Report(reloaded.Summary()); return;
            }
            using var session = await ProjectEditSession.Open(path, Token);
            var text = session.Current.Document.Text;
            var selected = Args.Flag("selection") ? session.State.Selection ?? throw new CliUsageException("No saved selection; use edit select.") : null;
            switch (Args.Command)
            {
                case "edit show":
                    var original = session.Current.Document;
                    var displayed = selected is null ? original : AsciiDocument.FromText(TextEditOperations.Selected(text, selected), original.Title)
                        with { FontFamily = original.FontFamily, CellWidth = original.CellWidth, CellHeight = original.CellHeight };
                    await Emit(displayed); return;
                case "edit select":
                    await session.Select(new(Args.Integer("row", 0), Args.Integer("column", 0), Args.Integer("end-row", Args.Integer("row", 0)),
                        Args.Integer("end-column", Args.Integer("column", 0)), Args.Flag("rectangle")), Token); break;
                case "edit find":
                    var matches = TextEditOperations.Find(text, Args.Require("find"), Args.Flag("ignore-case"));
                    await Report(new { matches = matches.Select(m => new { row = m.Row, column = m.Column, endRow = m.EndRow, endColumn = m.EndColumn }), maximumMatches = 1000 }); return;
                case "edit replace":
                {
                    if (!Args.Has("find") && (Args.Flag("all") || Args.Flag("ignore-case"))) throw new CliUsageException("--all/--ignore-case require --find.");
                    var replacement = await Replacement();
                    if (Args.Has("find"))
                    {
                        if (selected is not null) throw new CliUsageException("Use either --find or --selection.");
                        var found = TextEditOperations.Find(text, Args.Require("find"), Args.Flag("ignore-case"));
                        foreach (var match in (Args.Flag("all") ? found : found.Take(1)).Reverse())
                            text = TextEditOperations.Replace(text, new(match.Row, match.Column, match.EndRow, match.EndColumn), replacement);
                    }
                    else text = selected is null ? replacement : TextEditOperations.Replace(text, selected, replacement);
                    await session.Edit(text, Token); break;
                }
                case "edit insert":
                    var row = Args.Integer("row", 0); var column = Args.Integer("column", 0);
                    await session.Edit(TextEditOperations.Replace(text, new(row, column, row, column), await Replacement()), Token); break;
                case "edit delete": await session.Edit(selected is null ? "" : TextEditOperations.Replace(text, selected, ""), Token); break;
                case "edit transform":
                    var transformed = TextEditOperations.Transform(selected is null ? text : TextEditOperations.Selected(text, selected), Args.Require("operation"));
                    await session.Edit(selected is null ? transformed : TextEditOperations.Replace(text, selected, transformed), Token); break;
                case "history undo": await session.Move(false, Token); break;
                case "history redo": await session.Move(true, Token); break;
                case "history clear": await session.ClearHistory(Token); break;
                case "history list": break;
                case "project save":
                    var destination = Args.Get("output", path)!; EnsureWritable(destination, true);
                    await session.Save(destination, Args.Flag("overwrite"), Token); break;
                default: throw new CliUsageException("Unknown edit command.");
            }
            await Report(session.Summary());
        }
        private async Task<int> WorkspaceCommand()
        {
            using var workspace = await CliWorkspace.Open(Args.Get("workspace"), Token);
            switch (Args.Command)
            {
                case "workspace open": await workspace.Add(Args.Require("project"), Token); break;
                case "workspace new":
                {
                    if (workspace.State.Projects.Length >= 32) throw new CliUsageException("Workspace allows at most 32 open projects.");
                    var path = Path.GetFullPath(Args.Require("project"));
                    if (File.Exists(path) || File.Exists(ProjectEditSession.StatePath(path))) throw new IOException("New project path already exists.");
                    if (path.Equals(Path.GetFullPath(Args.Get("workspace", CliWorkspace.DefaultPath)!), StringComparison.OrdinalIgnoreCase)) throw new CliUsageException("Project must not replace the workspace manifest.");
                    var document = AsciiDocument.FromText(Args.Get("text", "")!, Path.GetFileNameWithoutExtension(path));
                    await ProjectEditSession.Atomic(path, JsonSerializer.SerializeToUtf8Bytes(new StudioProject(WorkspaceService.CurrentProjectVersion, document, null, null, null, "snapshot")), false, Token);
                    await workspace.Add(path, Token); break;
                }
                case "workspace switch": await workspace.Activate(Args.Require("project"), Token); break;
                case "workspace close": await workspace.Close(Args.Get("project"), Args.Get("action", "cancel")!, Args.Flag("overwrite"), Token); break;
                case "workspace recent": await Report(new { paths = workspace.State.Recent }); return 0;
                case "workspace clear-recent": await workspace.ClearRecent(Token); break;
                case "workspace recovery": await Report(new { paths = workspace.State.Recovery }); return 0;
                case "workspace restore":
                    var restored = await workspace.Restore(Token); await Report(new { failures = restored.Failures, results = restored.Results }); return restored.Failures == 0 ? 0 : 5;
                case "workspace list": break;
                default: throw new CliUsageException("Unknown workspace command.");
            }
            await Report(new { projects = await workspace.Status(Token), workspace.State.Active, workspace.State.Recovery }); return 0;
        }
    }
}
