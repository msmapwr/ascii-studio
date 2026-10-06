using Charloom.Core;

namespace Charloom.Cli;

public static partial class CliHost
{
    private sealed partial class Invocation
    {
        private async Task GeometryCommand()
        {
            var path = await TargetPath(); Args.Values["project"] = [path];
            using var session = await ProjectEditSession.Open(path, Token);
            _ = session.GeometrySummary(); // Validate the source before any mutation.
            switch (Args.Command)
            {
                case "geometry status": break;
                case "geometry set":
                    if (!Args.Has("set") && !Args.Has("geometry")) throw new CliUsageException("Provide --geometry JSON or --set geometry.Name=Value.");
                    Args.ValidateAssignments("geometry");
                    if (Args.Values.TryGetValue("set", out var assignments) && assignments.Any(a => !a.StartsWith("geometry.", StringComparison.OrdinalIgnoreCase)))
                        throw new CliUsageException("Geometry assignments require the geometry. prefix.");
                    // Model uses reflection to assign record properties; clone the
                    // draft so validation cannot mutate a retained history entry.
                    await session.SetGeometry(Args.Model(session.DraftGeometry with { }, "geometry", "geometry"), Token); break;
                case "geometry undo": await session.MoveGeometry(false, Token); break;
                case "geometry redo": await session.MoveGeometry(true, Token); break;
                case "geometry reset": await session.SetGeometry(new ImageGeometry(), Token); break;
                default: throw new CliUsageException("Unknown geometry command.");
            }
            await Report(session.GeometrySummary());
        }
    }
}
