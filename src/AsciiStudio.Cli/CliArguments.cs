using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using AsciiStudio.Services;

namespace AsciiStudio.Cli;

public sealed class CliUsageException(string message) : Exception(message);

public sealed class CliArguments
{
    public string Command { get; private set; } = "";
    public Dictionary<string, List<string>> Values { get; } = new(StringComparer.Ordinal);
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    public bool Has(string name) => Values.ContainsKey(name);
    public string? Get(string name, string? fallback = null) => Values.TryGetValue(name, out var list) ? list[^1] : fallback;
    public string Require(string name) => Get(name) ?? throw new CliUsageException($"Missing --{name}.");
    public bool Flag(string name) => bool.TryParse(Get(name, "false"), out var value) ? value : throw new CliUsageException($"--{name} expects true or false.");
    public int Integer(string name, int fallback) => Get(name) is not { } value ? fallback
        : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : throw new CliUsageException($"--{name} expects an integer.");
    public double Number(string name, double fallback) => Get(name) is not { } value ? fallback
        : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result) ? result : throw new CliUsageException($"--{name} expects a finite number.");

    public static CliArguments Parse(string[] args)
    {
        var result = new CliArguments(); var words = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i] == "-h" ? "--help" : args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (arg.StartsWith('-')) throw new CliUsageException($"Unknown option: {arg}");
                words.Add(arg); continue;
            }
            var split = arg[2..].Split('=', 2); var name = split[0];
            var definition = CliCatalog.AllOptions.FirstOrDefault(o => o.Name == name) ?? throw new CliUsageException($"Unknown option: --{name}");
            var value = split.Length == 2 ? split[1] : definition.IsFlag ? "true"
                : ++i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal) ? args[i] : throw new CliUsageException($"Missing value for --{name}.");
            if (result.Values.TryGetValue(name, out var old))
            {
                if (name != "set") throw new CliUsageException($"Duplicate option: --{name}");
                old.Add(value);
            }
            else result.Values.Add(name, [value]);
            if (definition.IsFlag && !bool.TryParse(value, out _)) throw new CliUsageException($"--{name} expects true or false.");
        }
        result.Command = string.Join(' ', words);
        if (result.Get("language", "system") is not ("system" or "zh-CN" or "en-US")) throw new CliUsageException("--language: system | zh-CN | en-US");
        if (result.Command.Length == 0 && (result.Flag("help") || result.Flag("version") || args.Length == 0)) return result;
        var command = CliCatalog.Commands.FirstOrDefault(c => c.Name == result.Command);
        if (command is null)
        {
            if (result.Flag("help") && CliCatalog.Commands.Any(c => c.Name.StartsWith(result.Command + " ", StringComparison.Ordinal))) return result;
            throw new CliUsageException($"Unknown command: {result.Command}. Use --help.");
        }
        foreach (var option in result.Values.Keys)
            if (!CliCatalog.GlobalOptions.Any(o => o.Name == option) && !command.Options.Any(o => o.Name == option))
                throw new CliUsageException($"--{option} is not supported by {command.Name}.");
        return result;
    }

    public T Model<T>(T defaults, string prefix = "", string? fileOption = null) where T : class
    {
        var model = defaults;
        if (fileOption is not null && Get(fileOption) is { } path)
        {
            var supplied = JsonNode.Parse(BoundedFile.JsonBytes(BoundedFile.Read(path, 1_000_000)).Span) as JsonObject
                ?? throw new CliUsageException("Configuration must be an object.");
            // Validate unknown fields before merging so a typo cannot silently disappear.
            _ = supplied.Deserialize<T>(Json) ?? throw new CliUsageException("Configuration cannot be null.");
            var merged = JsonSerializer.SerializeToNode(defaults, Json)!.AsObject();
            foreach (var field in supplied)
            {
                var existing = merged.Select(p => p.Key).FirstOrDefault(key => key.Equals(field.Key, StringComparison.OrdinalIgnoreCase));
                merged[existing ?? field.Key] = field.Value?.DeepClone();
            }
            model = merged.Deserialize<T>(Json)!;
        }
        if (!Values.TryGetValue("set", out var assignments)) return model;
        foreach (var assignment in assignments)
        {
            var pair = assignment.Split('=', 2);
            if (pair.Length != 2) throw new CliUsageException("--set expects Name=Value or group.Name=Value.");
            var key = pair[0];
            if (prefix.Length > 0)
            {
                if (!key.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)) continue;
                key = key[(prefix.Length + 1)..];
            }
            else if (key.Contains('.')) continue;
            var property = typeof(T).GetProperties().FirstOrDefault(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && p.SetMethod is not null)
                ?? throw new CliUsageException($"Unknown {typeof(T).Name} property: {key}");
            if (property.PropertyType.IsArray) throw new CliUsageException($"{key} cannot be changed using --set.");
            property.SetValue(model, ConvertValue(property.PropertyType, pair[1]));
        }
        return model;
    }
    public void ValidateAssignments(params string[] prefixes)
    {
        if (!Values.TryGetValue("set", out var assignments)) return;
        foreach (var item in assignments)
        {
            var pair = item.Split('=', 2);
            if (pair.Length != 2 || pair[0].Contains('.') && !prefixes.Contains(pair[0].Split('.')[0], StringComparer.OrdinalIgnoreCase))
                throw new CliUsageException($"Unsupported --set assignment: {item}");
        }
    }
    internal static object ConvertValue(Type type, string value)
    {
        try
        {
            if (type == typeof(string)) return value;
            if (type.IsEnum) return Enum.Parse(type, value, true);
            if (type == typeof(uint) && (value.StartsWith('#') || value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)))
            {
                var hex = value.StartsWith('#') ? value[1..] : value[2..];
                if (hex.Length == 6) hex = "FF" + hex;
                return uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            var converted = Convert.ChangeType(value, type, CultureInfo.InvariantCulture)!;
            if (converted is double number && !double.IsFinite(number)) throw new FormatException();
            return converted;
        }
        catch (Exception error) when (error is FormatException or OverflowException or ArgumentException)
        { throw new CliUsageException($"Invalid {type.Name} value: {value}"); }
    }
}
