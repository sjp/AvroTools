using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvroTool.Commands;

internal sealed class GetSchemaCommand : AsyncCommand<GetSchemaCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[AVRO_FILE]")]
        [Description("An Avro object container file. Omit and use --stdin to read from standard input.")]
        public string AvroFile { get; set; } = string.Empty;

        [CommandOption("--stdin")]
        [Description("Read the Avro object container file from standard input instead of a file.")]
        [DefaultValue(false)]
        public bool FromStandardInput { get; set; }

        [CommandOption("--pretty")]
        [Description("Indent the output JSON.")]
        [DefaultValue(false)]
        public bool Pretty { get; set; }
    }

    private readonly IStatusConsole _console;
    private readonly IStandardStreams _streams;

    public GetSchemaCommand(IStatusConsole console, IStandardStreams streams)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(streams);

        _console = console;
        _streams = streams;
    }

    protected override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (settings.FromStandardInput)
            return InputValidation.ValidateStandardInputAlone(settings.AvroFile, "An Avro object container file");

        if (string.IsNullOrWhiteSpace(settings.AvroFile))
            return ValidationResult.Error("An Avro object container file must be provided.");

        if (!File.Exists(settings.AvroFile))
            return ValidationResult.Error($"An Avro object container file could not be found at: {settings.AvroFile}");

        return ValidationResult.Success();
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var source = settings.FromStandardInput ? InputSource.StandardInputName : settings.AvroFile;

        using var stream = InputReader.TryOpenRead(_streams, settings.FromStandardInput, settings.AvroFile, source, _console);
        if (stream == null)
            return ErrorCode.Error;

        var reader = AvroContainerFiles.TryOpenReader(stream, source, _console);
        if (reader == null)
            return ErrorCode.Error;

        using (reader)
        {
            var schemaJson = reader.GetSchema().ToString();
            if (settings.Pretty)
                schemaJson = JsonFormatting.Indent(schemaJson);

            await _streams.Output.WriteLineAsync(schemaJson.AsMemory(), cancellationToken);
            return ErrorCode.Success;
        }
    }
}
