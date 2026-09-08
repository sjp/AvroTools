using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SJP.Avro.Tools.Compatibility;
using SJP.Avro.Tools.Idl;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvroTool.Commands;

internal sealed class CompatCommand : AsyncCommand<CompatCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[SCHEMAS]")]
        [Description("Two schema files (reader then writer), or, for the *-transitive modes, a candidate followed by the earlier versions to check it against. Each may be an IDL, protocol or schema file. Omit whichever schema --stdin supplies.")]
        public string[] Schemas { get; set; } = [];

        [CommandOption("--stdin")]
        [Description("Read one of the schemas from standard input instead of a file. One fewer positional argument is then given.")]
        [DefaultValue(false)]
        public bool FromStandardInput { get; set; }

        [CommandOption("--stdin-as")]
        [Description("Which schema standard input supplies, as a 1-based position among the schemas. Defaults to 1, the reader (or, in the *-transitive modes, the candidate).")]
        public int? StandardInputPosition { get; set; }

        [CommandOption("-m|--mode")]
        [Description("Compatibility mode: backward (default), forward, full, backward-transitive, forward-transitive or full-transitive.")]
        [DefaultValue("backward")]
        public string Mode { get; set; } = "backward";

        [CommandOption("--json")]
        [Description("Emit machine-readable JSON to standard output instead of a human-readable summary.")]
        [DefaultValue(false)]
        public bool Json { get; set; }
    }

    /// <summary>The canonical spellings of the compatibility modes the command accepts.</summary>
    public static readonly IReadOnlyList<string> SupportedModes =
    [
        "backward",
        "forward",
        "full",
        "backward-transitive",
        "forward-transitive",
        "full-transitive",
    ];

    // Accepts hyphen- or underscore-separated spellings, case-insensitively.
    private static readonly FrozenDictionary<string, CompatibilityMode> Modes = new Dictionary<string, CompatibilityMode>(StringComparer.OrdinalIgnoreCase)
    {
        ["backward"] = CompatibilityMode.Backward,
        ["forward"] = CompatibilityMode.Forward,
        ["full"] = CompatibilityMode.Full,
        ["backwardtransitive"] = CompatibilityMode.BackwardTransitive,
        ["forwardtransitive"] = CompatibilityMode.ForwardTransitive,
        ["fulltransitive"] = CompatibilityMode.FullTransitive,
    }.ToFrozenDictionary();

    private readonly IStatusConsole _console;
    private readonly IOutputConsole _output;
    private readonly IStandardStreams _streams;
    private readonly IIdlToAvroTranslator _idlTranslator;

    public CompatCommand(
        IStatusConsole console,
        IOutputConsole output,
        IStandardStreams streams,
        IIdlToAvroTranslator idlTranslator)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(idlTranslator);

        _console = console;
        _output = output;
        _streams = streams;
        _idlTranslator = idlTranslator;
    }

    protected override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (!Modes.TryGetValue(NamingConventions.NormaliseOption(settings.Mode), out var mode))
            return ValidationResult.Error($"Unknown mode '{settings.Mode}'. Supported: {string.Join(", ", SupportedModes)}.");

        if (!settings.FromStandardInput && settings.StandardInputPosition != null)
            return ValidationResult.Error("--stdin-as may only be used together with --stdin.");

        // Standard input contributes one schema, so it counts towards the total the mode requires.
        var total = settings.Schemas.Length + (settings.FromStandardInput ? 1 : 0);

        if (total < 2)
            return ValidationResult.Error("At least two schema files must be provided.");

        if (!SchemaCompatibility.IsTransitive(mode) && total != 2)
            return ValidationResult.Error($"The '{settings.Mode}' mode compares exactly two schemas. Use a '*-transitive' mode to check a candidate against a chain of versions.");

        if (settings.FromStandardInput)
        {
            var position = settings.StandardInputPosition ?? 1;
            if (position < 1 || position > total)
                return ValidationResult.Error($"--stdin-as must be a position between 1 and {total}, not {position}.");
        }

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
        var mode = Modes[NamingConventions.NormaliseOption(settings.Mode)];

        var inputs = ResolveInputPaths(settings);

        var sources = new List<SchemaSource>(inputs.Count);
        foreach (var schemaFile in inputs)
        {
            var source = await AvroInputResolver.ResolveSingleSchemaAsync(schemaFile, _streams, _idlTranslator, "compat", _console, cancellationToken);
            if (source == null)
                return ErrorCode.ComparisonError;

            sources.Add(source);
        }

        CompatibilityModeResult comparison;
        try
        {
            comparison = SchemaCompatibility.Check(mode, sources.ConvertAll(s => s.Schema));
        }
        catch (Exception ex)
        {
            _console.MarkupLine("[red]Failed to compute schema compatibility.[/]");
            _console.MarkupLineInterpolated($"[red]    {ex.Message}[/]");
            return ErrorCode.ComparisonError;
        }

        if (settings.Json)
            await WriteJsonAsync(settings.Mode, comparison, sources, cancellationToken);
        else
            WriteHuman(comparison, sources);

        return comparison.IsCompatible ? ErrorCode.Success : ErrorCode.Difference;
    }

    /// <summary>
    /// The schemas to compare, in order, where <c>null</c> means standard input. The positional
    /// arguments keep their relative order; standard input is inserted at the requested position.
    /// </summary>
    private static IReadOnlyList<string?> ResolveInputPaths(Settings settings)
    {
        var inputs = new List<string?>(settings.Schemas.Length + 1);
        inputs.AddRange(settings.Schemas);

        if (settings.FromStandardInput)
            inputs.Insert((settings.StandardInputPosition ?? 1) - 1, null);

        return inputs;
    }

    /// <summary>
    /// Writes the report of every check that was run. It is the answer the user asked for, so it
    /// goes to standard output where a redirect or a pipeline can pick it up, alongside the
    /// <c>--json</c> form of the same information.
    /// </summary>
    /// <param name="comparison">The checks that were run, and whether all of them passed.</param>
    /// <param name="sources">The schemas that were compared, in the order they were given.</param>
    private void WriteHuman(CompatibilityModeResult comparison, IReadOnlyList<SchemaSource> sources)
    {
        foreach (var check in comparison.Checks)
        {
            var direction = DirectionName(check.Direction);
            var reader = sources[check.ReaderIndex].Source;
            var writer = sources[check.WriterIndex].Source;

            if (check.Result.IsCompatible)
            {
                _output.MarkupLineInterpolated($"[green]COMPATIBLE[/] ({direction}) reader '{reader}' can read writer '{writer}'");
            }
            else
            {
                _output.MarkupLineInterpolated($"[red]INCOMPATIBLE[/] ({direction}) reader '{reader}' cannot read writer '{writer}'");
                foreach (var incompatibility in check.Result.Incompatibilities)
                    _output.MarkupLineInterpolated($"    [yellow]{NamingConventions.ToUpperSnake(incompatibility.Type)}[/] at {incompatibility.Location}: {incompatibility.Message}");
            }
        }

        if (comparison.IsCompatible)
            _output.MarkupLine("[green]Schemas are compatible.[/]");
        else
            _output.MarkupLine("[red]Schemas are not compatible.[/]");
    }

    private async Task WriteJsonAsync(string mode, CompatibilityModeResult comparison, IReadOnlyList<SchemaSource> sources, CancellationToken cancellationToken)
    {
        var payload = new
        {
            mode,
            compatible = comparison.IsCompatible,
            checks = comparison.Checks.Select(check => new
            {
                direction = DirectionName(check.Direction),
                reader = sources[check.ReaderIndex].Source,
                writer = sources[check.WriterIndex].Source,
                compatible = check.Result.IsCompatible,
                incompatibilities = check.Result.Incompatibilities.Select(incompatibility => new
                {
                    type = NamingConventions.ToUpperSnake(incompatibility.Type),
                    location = incompatibility.Location,
                    message = incompatibility.Message,
                }),
            }),
        };

        var json = JsonSerializer.Serialize(payload, JsonFormatting.IndentedOptions);
        await _streams.Output.WriteLineAsync(json.AsMemory(), cancellationToken);
    }

    /// <summary>The direction as it is spelled in the report, matching the mode names the command accepts.</summary>
    private static string DirectionName(CompatibilityDirection direction) =>
        direction.ToString().ToLowerInvariant();
}
