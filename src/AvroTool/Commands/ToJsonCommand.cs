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

    private readonly IAnsiConsole _console;

    public ToJsonCommand(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        _console = console;
    }

    protected override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (settings.FromStandardInput)
            return ValidationResult.Success();

        if (string.IsNullOrWhiteSpace(settings.AvroFile))
            return ValidationResult.Error("An Avro object container file must be provided.");

        if (!File.Exists(settings.AvroFile))
            return ValidationResult.Error($"An Avro object container file could not be found at: {settings.AvroFile}");

        return ValidationResult.Success();
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var source = settings.FromStandardInput ? "<stdin>" : settings.AvroFile;

        using var stream = InputSource.OpenRead(settings.FromStandardInput, settings.AvroFile);

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

            try
            {
                while (reader.HasNext())
                {
                    var record = reader.Next();
                    var json = AvroJsonWriter.Encode(schema, record);
                    if (settings.Pretty)
                        json = JsonFormatting.Indent(json);

                    await Console.Out.WriteLineAsync(json.AsMemory(), cancellationToken);
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
