using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avro.File;
using Avro.Generic;
using SJP.Avro.Tools;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvroTool.Commands;

internal sealed class ToJsonCommand : AsyncCommand<ToJsonCommand.Settings>
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
        [Description("Indent each record's output JSON.")]
        [DefaultValue(false)]
        public bool Pretty { get; set; }
    }

    private readonly IStatusConsole _console;
    private readonly IStandardStreams _streams;

    public ToJsonCommand(IStatusConsole console, IStandardStreams streams)
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

        IFileReader<GenericRecord> reader;
        try
        {
            reader = DataFileReader<GenericRecord>.OpenReader(stream);
        }
        catch (Exception ex)
        {
            _console.MarkupLineInterpolated($"[red]Unable to read '{source}' as an Avro object container file.[/]");
            _console.MarkupLineInterpolated($"[red]    {ex.Message}[/]");
            return ErrorCode.Error;
        }

        using (reader)
        {
            var schema = reader.GetSchema();

            // A container file can hold millions of records, and standard output flushes on every
            // write, so records are buffered and forwarded in blocks. Disposal flushes whatever is
            // left, including when decoding fails part way through.
            using var output = new BufferedTextWriter(_streams.Output);

            try
            {
                while (reader.HasNext())
                {
                    var record = reader.Next();
                    var json = AvroJsonWriter.Encode(schema, record);
                    if (settings.Pretty)
                        json = JsonFormatting.Indent(json);

                    await output.WriteLineAsync(json.AsMemory(), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _console.MarkupLineInterpolated($"[red]Failed to decode records from '{source}'.[/]");
                _console.MarkupLineInterpolated($"[red]    {ex.Message}[/]");
                return ErrorCode.Error;
            }

            return ErrorCode.Success;
        }
    }
}
