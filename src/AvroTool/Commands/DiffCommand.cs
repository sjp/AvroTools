using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SJP.Avro.Tools.Diff;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;
using AvroSchema = Avro.Schema;

namespace AvroTool.Commands;

internal sealed class DiffCommand : AsyncCommand<DiffCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<SCHEMA_A>")]
        [Description("The earlier/base schema. May be an IDL, protocol or schema file.")]
        public string SchemaA { get; set; } = "";

        [CommandArgument(1, "<SCHEMA_B>")]
        [Description("The later/candidate schema to compare against SCHEMA_A. May be an IDL, protocol or schema file.")]
        public string SchemaB { get; set; } = "";

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
        if (string.IsNullOrWhiteSpace(settings.SchemaA))
            return ValidationResult.Error("SCHEMA_A must be provided.");

        if (string.IsNullOrWhiteSpace(settings.SchemaB))
            return ValidationResult.Error("SCHEMA_B must be provided.");

        if (!File.Exists(settings.SchemaA))
            return ValidationResult.Error($"A schema file could not be found at: {settings.SchemaA}");

        if (!File.Exists(settings.SchemaB))
            return ValidationResult.Error($"A schema file could not be found at: {settings.SchemaB}");

        return ValidationResult.Success();
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var before = await ResolveSingleSchema(settings.SchemaA, cancellationToken);
        if (before == null)
            return ErrorCode.Error;

        var after = await ResolveSingleSchema(settings.SchemaB, cancellationToken);
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

    private async Task<SchemaSource?> ResolveSingleSchema(string schemaFile, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(schemaFile, cancellationToken);
        var input = await AvroInputResolver.ResolveAsync(content, _idlTranslator, cancellationToken);
        if (input == null)
        {
            _console.MarkupLineInterpolated($"[red]Input '{schemaFile}' unable to be parsed as one of Avro IDL, JSON protocol or JSON schema.[/]");
            return null;
        }

        if (input.Schemas.Count != 1)
        {
            _console.MarkupLineInterpolated($"[red]Input '{schemaFile}' resolves to {input.Schemas.Count} named types; diff expects a single schema per input.[/]");
            return null;
        }

        return new SchemaSource(schemaFile, input.Schemas[0]);
    }

    private void WriteHuman(SchemaDiffResult result)
    {
        foreach (var change in result.Changes)
        {
            var kind = ToUpperSnake(change.Kind);
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
                kind = ToUpperSnake(change.Kind),
                location = change.Location,
                message = change.Message,
                oldValue = change.OldValue,
                newValue = change.NewValue,
                isValidPromotion = change.IsValidPromotion,
            }),
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await Console.Out.WriteLineAsync(json.AsMemory(), cancellationToken);
    }

    // Renders a change kind in the UPPER_SNAKE_CASE convention shared with the compat command.
    private static string ToUpperSnake(ChangeKind kind)
    {
        var name = kind.ToString();
        var builder = new StringBuilder(name.Length + 6);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                builder.Append('_');
            builder.Append(char.ToUpperInvariant(name[i]));
        }

        return builder.ToString();
    }

    private sealed record SchemaSource(string Source, AvroSchema Schema);
}
