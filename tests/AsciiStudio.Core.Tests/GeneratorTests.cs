using AsciiStudio.Core;
using Xunit;

namespace AsciiStudio.Core.Tests;

public sealed class GeneratorTests
{
    [Fact(DisplayName = "generator recipes restore all seven sources")]
    public void GeneratorRecipesRestoreAllSevenSources()
    {
        for (var kind = 0; kind < 7; kind++)
        {
            var recipe = new GeneratorRecipe(kind, 2, "first\nsecond", 32, 12, 1234);
            var restored = GeneratorRecipe.FromParameters(recipe.ToParameters(), recipe.Text);
            Assert.True(restored == recipe && restored.Generate() == recipe.Generate(), "Generator source round trip changed output");
            AsciiDocument.FromText(restored.Generate()).Validate();
        }
    }

    [Fact(DisplayName = "generator invalid project parameters rejected")]
    public void GeneratorInvalidProjectParametersRejected()
    {
        void Rejected(Action action) { try { action(); throw new Exception("Invalid generator parameters accepted"); } catch (ArgumentException) { } }
        Rejected(() => GeneratorRecipe.FromParameters(null, ""));
        var parameters = new GeneratorRecipe().ToParameters(); parameters["schema"] = "2";
        Rejected(() => GeneratorRecipe.FromParameters(parameters, ""));
        parameters["schema"] = "1"; parameters["seed"] = "1.5";
        Rejected(() => GeneratorRecipe.FromParameters(parameters, ""));
        Rejected(() => new GeneratorRecipe(Kind: 7).Generate());
        Rejected(() => new GeneratorRecipe(Kind: 2, Width: 101).Generate());
        Rejected(() => new GeneratorRecipe(Text: new string('x', 2001)).Generate());
        Rejected(() => new GeneratorRecipe(Seed: -1).Generate());
        var normal = new GeneratorRecipe().ToParameters();
        Assert.True(GeneratorRecipe.FromParameters(normal, "a\r\nb").Text == "a\nb", "Project line endings were not normalized");
    }
}
