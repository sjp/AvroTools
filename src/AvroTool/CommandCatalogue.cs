using System;
using System.Collections.Generic;
using System.Linq;
using AvroTool.Commands;
using Spectre.Console.Cli;

namespace AvroTool;

/// <summary>
/// Describes a single command exposed by the CLI.
/// </summary>
internal sealed class CommandDefinition
{
    private readonly Func<IConfigurator, ICommandConfigurator> _register;

    public CommandDefinition(
        string name,
        string description,
        Type settingsType,
        Func<IConfigurator, ICommandConfigurator> register,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? optionValues = null,
        int? failureCode = null)
    {
        Name = name;
        Description = description;
        SettingsType = settingsType;
        OptionValues = optionValues ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        FailureCode = failureCode ?? ErrorCode.Error;
        _register = register;
    }

    /// <summary>The name the command is invoked by.</summary>
    public string Name { get; }

    /// <summary>A one-line description of what the command does.</summary>
    public string Description { get; }

    /// <summary>The settings type whose arguments and options the command accepts.</summary>
    public Type SettingsType { get; }

    /// <summary>
    /// The values accepted by options whose type does not describe them, keyed by the
    /// option's long name (without leading dashes).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> OptionValues { get; }

    /// <summary>
    /// The exit code reported when the command cannot be run at all: an unparseable command
    /// line, a failed validation, or a fault inside the tool. A command that answers a question
    /// through its exit code reserves the usual failure code for the negative answer and reports
    /// a failure separately.
    /// </summary>
    public int FailureCode { get; }

    /// <summary>Registers the command with a Spectre.Console.Cli configurator.</summary>
    public ICommandConfigurator Register(IConfigurator config) => _register(config);
}

/// <summary>
/// The single source of truth for the commands the CLI exposes. Command registration and the
/// generated shell completion scripts are both driven from this list so that they cannot drift
/// apart; each command's arguments and options are reflected from its settings type.
/// </summary>
internal static class CommandCatalogue
{
    public static IReadOnlyList<CommandDefinition> Commands { get; } =
    [
        Define<IdlCommand, IdlCommand.Settings>(
            "idl",
            "Generates a JSON protocol file from an Avro IDL file."),
        Define<IdlToSchemataCommand, IdlToSchemataCommand.Settings>(
            "idl2schemata",
            "Extract JSON schemata of the types from an Avro IDL file."),
        Define<CodeGenCommand, CodeGenCommand.Settings>(
            "codegen",
            "Generates C# code for a given Avro IDL, protocol or schema."),
        Define<CompatCommand, CompatCommand.Settings>(
            "compat",
            "Checks whether two Avro schemas are compatible under Avro's schema-evolution rules.",
            optionValues: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["mode"] = CompatCommand.SupportedModes,
            },
            failureCode: ErrorCode.ComparisonError),
        Define<DiffCommand, DiffCommand.Settings>(
            "diff",
            "Prints a semantic diff between two Avro schemas.",
            failureCode: ErrorCode.ComparisonError),
        Define<CanonicalCommand, CanonicalCommand.Settings>(
            "canonical",
            "Prints the Parsing Canonical Form of an Avro IDL, protocol or schema."),
        Define<FingerprintCommand, FingerprintCommand.Settings>(
            "fingerprint",
            "Computes a fingerprint (crc-64-avro, md5 or sha-256) of an Avro IDL, protocol or schema.",
            optionValues: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["algorithm"] = FingerprintCommand.SupportedAlgorithms,
                ["format"] = FingerprintCommand.SupportedFormats,
            }),
        Define<GetSchemaCommand, GetSchemaCommand.Settings>(
            "getschema",
            "Prints the writer schema embedded in an Avro object container file."),
        Define<ToJsonCommand, ToJsonCommand.Settings>(
            "tojson",
            "Decodes an Avro object container file's records to JSON."),
        Define<CompletionsCommand, CompletionsCommand.Settings>(
            "completions",
            "Generates a shell completion script (bash, zsh, fish, powershell).",
            example: ["completions", "bash"]),
    ];

    /// <summary>
    /// The exit code reported when the command line in <paramref name="args"/> cannot be run,
    /// whether it failed to parse, failed validation, or faulted part-way through.
    /// </summary>
    /// <param name="args">The command line being run. Its first argument names the command.</param>
    /// <returns>The exit code the named command reports a failure with.</returns>
    /// <remarks>
    /// A failure is reported before there is a parsed command line to consult, so the command is
    /// identified from the raw arguments. A line that names no command at all — one asking for
    /// help or the version, or one too malformed to have got that far — takes the general code.
    /// </remarks>
    public static int FailureCodeFor(IReadOnlyList<string> args)
    {
        var name = args.Count > 0 ? args[0] : null;
        var command = Commands.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));

        return command?.FailureCode ?? ErrorCode.Error;
    }

    private static CommandDefinition Define<TCommand, TSettings>(
        string name,
        string description,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? optionValues = null,
        string[]? example = null,
        int? failureCode = null)
        where TCommand : class, ICommand<TSettings>
        where TSettings : CommandSettings
    {
        return new CommandDefinition(
            name,
            description,
            typeof(TSettings),
            config =>
            {
                var command = config.AddCommand<TCommand>(name).WithDescription(description);
                if (example != null)
                    command = command.WithExample(example);

                return command;
            },
            optionValues,
            failureCode);
    }
}
