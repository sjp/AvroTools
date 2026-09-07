using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SJP.Avro.Tools.Diff;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvroTool.Commands;

internal sealed class DiffCommand : AsyncCommand<DiffCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[SCHEMA_A]")]
        [Description("The earlier/base schema. May be an IDL, protocol or schema file. Omit whichever schema --stdin supplies.")]
        public string SchemaA { get; set; } = "";

        [CommandArgument(1, "[SCHEMA_B]")]
        [Description("The later/candidate schema to compare against SCHEMA_A. May be an IDL, protocol or schema file.")]
        public string SchemaB { get; set; } = "";

        [CommandOption("--stdin")]
        [Description("Read one of the two schemas from standard input instead of a file. Only one positional argument is then given.")]
        [DefaultValue(false)]
        public bool FromStandardInput { get; set; }

        [CommandOption("--stdin-as")]
        [Description("Which schema standard input supplies, as a 1-based position: 1 for SCHEMA_A (the default) or 2 for SCHEMA_B.")]
        public int? StandardInputPosition { get; set; }

        [CommandOption("--json")]
        [Description("Emit machine-readable JSON to standard output instead of a human-readable summary.")]
        [DefaultValue(false)]
        public bool Json { get; set; }

        [CommandOption("--verbose")]
        [Description("Also report documentation, alias and other metadata changes that don't affect the schema's shape.")]
        [DefaultValue(false)]
        public bool Verbose { get; set; }
    }

    private readonly IAnsiConsole _console;
    private readonly IIdlToAvroTranslator _idlTranslator;

    public DiffCommand(
        IAnsiConsole console,
        IIdlToAvroTranslator idlTranslator)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(idlTranslator);

        _console = console;
        _idlTranslator = idlTranslator;
    }

    protected override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (!settings.FromStandardInput)
        {
            if (settings.StandardInputPosition != null)
                return ValidationResult.Error("--stdin-as may only be used together with --stdin.");

            if (string.IsNullOrWhiteSpace(settings.SchemaA))
                return ValidationResult.Error("SCHEMA_A must be provided.");

            if (string.IsNullOrWhiteSpace(settings.SchemaB))
                return ValidationResult.Error("SCHEMA_B must be provided.");

            return ValidateFiles(settings.SchemaA, settings.SchemaB);
        }

        var position = settings.StandardInputPosition ?? 1;
        if (position is not (1 or 2))
            return ValidationResult.Error($"--stdin-as must be 1 (SCHEMA_A) or 2 (SCHEMA_B), not {position}.");

        if (string.IsNullOrWhiteSpace(settings.SchemaA))
            return ValidationResult.Error("The schema that does not come from standard input must be provided.");

        if (!string.IsNullOrWhiteSpace(settings.SchemaB))
            return ValidationResult.Error("Only one schema may be given as a file when --stdin supplies the other.");

        return ValidateFiles(settings.SchemaA);
    }

    private static ValidationResult ValidateFiles(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path))
                return ValidationResult.Error($"A schema file could not be found at: {path}");
        }

        return ValidationResult.Success();
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (pathA, pathB) = ResolveInputPaths(settings);

        var before = await AvroInputResolver.ResolveSingleSchemaAsync(pathA, _idlTranslator, "diff", _console, cancellationToken);
        if (before == null)
            return ErrorCode.Error;

        var after = await AvroInputResolver.ResolveSingleSchemaAsync(pathB, _idlTranslator, "diff", _console, cancellationToken);
        if (after == null)
            return ErrorCode.Error;

        SchemaDiffResult result;
        try
        {
            result = SchemaDiff.Compare(before.Schema, after.Schema, settings.Verbose);
        }
        catch (Exception ex)
        {
            _console.MarkupLine("[red]Failed to compute schema diff.[/]");
            _console.MarkupLineInterpolated($"[red]    {ex.Message}[/]");
            return ErrorCode.Error;
        }

        if (settings.Json)
            await WriteJsonAsync(result, cancellationToken);
        else
            WriteHuman(result);

        return result.IsIdentical ? ErrorCode.Success : ErrorCode.Error;
    }

    /// <summary>
    /// The file paths of the two schemas, in order, where <c>null</c> means standard input. When
    /// standard input supplies one of them the single positional argument fills the other.
    /// </summary>
    private static (string? PathA, string? PathB) ResolveInputPaths(Settings settings)
    {
        if (!settings.FromStandardInput)
            return (settings.SchemaA, settings.SchemaB);

        return (settings.StandardInputPosition ?? 1) == 1
            ? (null, settings.SchemaA)
            : (settings.SchemaA, null);
    }

    private void WriteHuman(SchemaDiffResult result)
    {
        foreach (var change in result.Changes)
        {
            var kind = NamingConventions.ToUpperSnake(change.Kind);
            var color = ChangeColor(change.Kind);
            _console.MarkupLineInterpolated($"[{color}]{kind}[/] at {change.Location}: {change.Message}");
        }

        if (result.IsIdentical)
            _console.MarkupLine("[green]Schemas are identical.[/]");
        else
            _console.MarkupLineInterpolated($"[red]Schemas differ ({result.Changes.Count} change(s)).[/]");
    }

    private static string ChangeColor(ChangeKind kind)
    {
        var name = kind.ToString();
        if (name.EndsWith("Added", StringComparison.Ordinal))
            return "green";

        return name.EndsWith("Removed", StringComparison.Ordinal) ? "red" : "yellow";
    }

    private static async Task WriteJsonAsync(SchemaDiffResult result, CancellationToken cancellationToken)
    {
        var payload = new
        {
            identical = result.IsIdentical,
            changes = result.Changes.Select(change => new
            {
                kind = NamingConventions.ToUpperSnake(change.Kind),
                location = change.Location,
                message = change.Message,
                oldValue = change.OldValue,
                newValue = change.NewValue,
                isValidPromotion = change.IsValidPromotion,
            }),
        };

        var json = JsonSerializer.Serialize(payload, JsonFormatting.IndentedOptions);
        await Console.Out.WriteLineAsync(json.AsMemory(), cancellationToken);
    }
}
