namespace AsciiStudio.Cli;

public static partial class CliHost
{
    private sealed partial class Invocation
    {
        private async Task CandidateCommand()
        {
            var path = await TargetPath(); Args.Values["project"] = [path];
            using var session = await ProjectEditSession.Open(path, Token);
            switch (Args.Command)
            {
                case "candidate status": await Report(session.Summary()); return;
                case "candidate create":
                    // Keep the lock through generation: CLI editors cannot race the
                    // candidate against a different source/edited revision.
                    if (session.State.Candidate is not null && !Args.Flag("replace-candidate"))
                        throw new CliConflictException("Candidate already exists; explicit --replace-candidate required.");
                    var generated = await Regenerate(session.Current);
                    await session.SetCandidate(generated.Document, Args.Flag("replace-candidate"), Token); break;
                case "candidate show": await Emit(session.CandidateProject.Document); return;
                case "candidate accept": await session.AcceptCandidate(Token); break;
                case "candidate discard": await session.DiscardCandidate(Token); break;
                case "candidate save":
                    var destination = Args.Require("output"); EnsureWritable(destination);
                    await session.SaveCandidate(destination, Args.Flag("overwrite"), Token);
                    await Report(new { path = Path.GetFullPath(destination), candidateRetained = true }); return;
                default: throw new CliUsageException("Unknown candidate command.");
            }
            await Report(session.Summary());
        }
    }
}
