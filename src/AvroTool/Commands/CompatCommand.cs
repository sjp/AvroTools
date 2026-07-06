using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SJP.Avro.Tools.Compatibility;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;
using AvroSchema = Avro.Schema;

namespace AvroTool.Commands;

internal sealed class CompatCommand : AsyncCommand<CompatCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<SCHEMAS>")]
        [Description("Two schema files (reader then writer), or, for the *-transitive modes, a candidate followed by the earlier versions to check it against. Each may be an IDL, protocol or schema file.")]
        public string[] Schemas { get; set; } = [];

        [CommandOption("-m|--mode")]
        [Description("Compatibility mode: backward (default), forward, full, backward-transitive, forward-transitive or full-transitive.")]
        [DefaultValue("backward")]
        public string Mode { get; set; } = "backward";

        [CommandOption("--json")]
        [Description("Emit machine-readable JSON to standard output instead of a human-readable summary.")]
        [DefaultValue(false)]
        public bool Json { get; set; }
    }

    // Accepts hyphen- or underscore-separated spellings, case-insensitively.
    private static readonly Dictionary<string, CompatibilityMode> Modes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["backward"] = CompatibilityMode.Backward,
        ["forward"] = CompatibilityMode.Forward,
        ["full"] = CompatibilityMode.Full,
        ["backwardtransitive"] = CompatibilityMode.BackwardTransitive,
        ["forwardtransitive"] = CompatibilityMode.ForwardTransitive,
        ["fulltransitive"] = CompatibilityMode.FullTransitive,
    };

    private readonly IAnsiConsole _console;
    private readonly IIdlToAvroTranslator _idlTranslator;

    public CompatCommand(
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
        if (!Modes.TryGetValue(Normalise(settings.Mode), out var mode))
            return ValidationResult.Error($"Unknown mode '{settings.Mode}'. Supported: backward, forward, full, backward-transitive, forward-transitive, full-transitive.");

        if (settings.Schemas.Length < 2)
            return ValidationResult.Error("At least two schema files must be provided.");

        if (!IsTransitive(mode) && settings.Schemas.Length != 2)
            return ValidationResult.Error($"The '{settings.Mode}' mode compares exactly two schemas. Use a '*-transitive' mode to check a candidate against a chain of versions.");

        foreach (var schemaFile in settings.Schemas)
        {
            if (string.IsNullOrWhiteSpace(schemaFile))
                return ValidationResult.Error("A schema file must be provided.");

            if (!File.Exists(schemaFile))
                return ValidationResult.Error($"A schema file could not be found at: {schemaFile}");
        }

        return ValidationResult.Success();
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var mode = Modes[Normalise(settings.Mode)];

        var sources = new List<SchemaSource>(settings.Schemas.Length);
        foreach (var schemaFile in settings.Schemas)
        {
            var content = await File.ReadAllTextAsync(schemaFile, cancellationToken);
            var input = await AvroInputResolver.ResolveAsync(content, _idlTranslator, cancellationToken);
            if (input == null)
            {
                _console.MarkupLineInterpolated($"[red]Input '{schemaFile}' unable to be parsed as one of Avro IDL, JSON protocol or JSON schema.[/]");
                return ErrorCode.Error;
            }

            if (input.Schemas.Count != 1)
            {
                _console.MarkupLineInterpolated($"[red]Input '{schemaFile}' resolves to {input.Schemas.Count} named types; compat expects a single schema per input.[/]");
                return ErrorCode.Error;
            }

            sources.Add(new SchemaSource(schemaFile, input.Schemas[0]));
        }

        List<CompatibilityCheck> checks;
        try
        {
            checks = RunChecks(mode, sources);
        }
        catch (Exception ex)
        {
            _console.MarkupLine("[red]Failed to compute schema compatibility.[/]");
            _console.MarkupLineInterpolated($"[red]    {ex.Message}[/]");
            return ErrorCode.Error;
        }

        var compatible = checks.All(c => c.Result.IsCompatible);

        if (settings.Json)
            await WriteJsonAsync(settings.Mode, compatible, checks, cancellationToken);
        else
            WriteHuman(compatible, checks);

        return compatible ? ErrorCode.Success : ErrorCode.Error;
    }

    /// <summary>
    /// Expands the requested mode into the individual reader/writer checks. The first schema is the
    /// anchor: the reader for backward directions and the writer for forward directions. The
    /// remaining schemas are the writers (backward) or readers (forward) it is compared against.
    /// </summary>
    private static List<CompatibilityCheck> RunChecks(CompatibilityMode mode, IReadOnlyList<SchemaSource> sources)
    {
        var anchor = sources[0];
        var others = sources.Skip(1);
        var checks = new List<CompatibilityCheck>();

        var wantBackward = mode is CompatibilityMode.Backward or CompatibilityMode.BackwardTransitive or CompatibilityMode.Full or CompatibilityMode.FullTransitive;
        var wantForward = mode is CompatibilityMode.Forward or CompatibilityMode.ForwardTransitive or CompatibilityMode.Full or CompatibilityMode.FullTransitive;

        foreach (var other in others)
        {
            if (wantBackward)
            {
                // Backward: the anchor is the reader; it must read data written with the other schema.
                var result = SchemaCompatibility.CheckReaderWriterCompatibility(anchor.Schema, other.Schema);
                checks.Add(new CompatibilityCheck("backward", anchor, other, result));
            }

            if (wantForward)
            {
                // Forward: the anchor is the writer; the other schema (an older reader) must read it.
                var result = SchemaCompatibility.CheckReaderWriterCompatibility(other.Schema, anchor.Schema);
                checks.Add(new CompatibilityCheck("forward", other, anchor, result));
            }
        }

        return checks;
    }

    private void WriteHuman(bool compatible, IReadOnlyList<CompatibilityCheck> checks)
    {
        foreach (var check in checks)
        {
            if (check.Result.IsCompatible)
            {
                _console.MarkupLineInterpolated($"[green]COMPATIBLE[/] ({check.Direction}) reader '{check.Reader.Source}' can read writer '{check.Writer.Source}'");
            }
            else
            {
                _console.MarkupLineInterpolated($"[red]INCOMPATIBLE[/] ({check.Direction}) reader '{check.Reader.Source}' cannot read writer '{check.Writer.Source}'");
                foreach (var incompatibility in check.Result.Incompatibilities)
                    _console.MarkupLineInterpolated($"    [yellow]{ToUpperSnake(incompatibility.Type)}[/] at {incompatibility.Location}: {incompatibility.Message}");
            }
        }

        if (compatible)
            _console.MarkupLine("[green]Schemas are compatible.[/]");
        else
            _console.MarkupLine("[red]Schemas are not compatible.[/]");
    }

    private static async Task WriteJsonAsync(string mode, bool compatible, IReadOnlyList<CompatibilityCheck> checks, CancellationToken cancellationToken)
    {
        var payload = new
        {
            mode,
            compatible,
            checks = checks.Select(check => new
            {
                direction = check.Direction,
                reader = check.Reader.Source,
                writer = check.Writer.Source,
                compatible = check.Result.IsCompatible,
                incompatibilities = check.Result.Incompatibilities.Select(incompatibility => new
                {
                    type = ToUpperSnake(incompatibility.Type),
                    location = incompatibility.Location,
                    message = incompatibility.Message,
                }),
            }),
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await Console.Out.WriteLineAsync(json.AsMemory(), cancellationToken);
    }

    private static bool IsTransitive(CompatibilityMode mode) =>
        mode is CompatibilityMode.BackwardTransitive or CompatibilityMode.ForwardTransitive or CompatibilityMode.FullTransitive;

    private static string Normalise(string value) =>
        (value ?? string.Empty).Trim().Replace("-", string.Empty).Replace("_", string.Empty);

    // Renders an incompatibility kind in the UPPER_SNAKE_CASE convention shared with the Java tooling.
    private static string ToUpperSnake(SchemaIncompatibilityType type)
    {
        var name = type.ToString();
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

    private sealed record CompatibilityCheck(string Direction, SchemaSource Reader, SchemaSource Writer, SchemaCompatibilityResult Result);
}
