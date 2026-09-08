using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Avro;
using Avro.File;
using Avro.Generic;
using Spectre.Console;

namespace AvroTool;

/// <summary>
/// Opens an Avro object container file, turning a failure into a reported error rather than an
/// exception that ends the process.
/// </summary>
internal static class AvroContainerFiles
{
    /// <summary>
    /// The compression codecs Apache.Avro 1.12.2 implements without an additional package
    /// reference, keyed by the assembly it fails to load when a container declares one of the
    /// codecs it does not bundle.
    /// </summary>
    private static readonly Dictionary<string, string> UnbundledCodecAssemblies = new(StringComparer.Ordinal)
    {
        ["Avro.File.Snappy"] = "snappy",
        ["Avro.File.BZip2"] = "bzip2",
        ["Avro.File.Zstandard"] = "zstandard",
        ["Avro.File.XZ"] = "xz",
    };

    private const string UnrecognizedCodecPrefix = "Unrecognized codec: ";

    /// <summary>
    /// Opens an Avro object container file for reading, reporting a failure to <paramref name="console"/>
    /// and returning <c>null</c> instead of throwing.
    /// </summary>
    /// <param name="stream">The stream to read the container from.</param>
    /// <param name="source">The name of the input, used when reporting a failure.</param>
    /// <param name="console">The console to write a failure to.</param>
    public static IFileReader<GenericRecord>? TryOpenReader(Stream stream, string source, IStatusConsole console)
    {
        try
        {
            return DataFileReader<GenericRecord>.OpenReader(stream);
        }
        catch (Exception ex)
        {
            console.MarkupLineInterpolated($"[red]Unable to read '{source}' as an Avro object container file.[/]");

            var codec = UnsupportedCodecName(ex);
            if (codec != null)
                console.MarkupLineInterpolated($"[red]    The container uses the '{codec}' codec; only 'null' and 'deflate' are supported.[/]");
            else
                console.MarkupLineInterpolated($"[red]    {ex.Message}[/]");

            return null;
        }
    }

    /// <summary>
    /// Names the codec responsible for a failure to open a container, when the failure was
    /// caused by a codec this build cannot decode; otherwise <c>null</c>.
    /// </summary>
    private static string? UnsupportedCodecName(Exception ex)
    {
        // A codec Apache.Avro implements only as a separate package (snappy, bzip2, zstandard,
        // xz) fails to load its handler assembly, which this tool does not reference.
        if (ex is FileNotFoundException { FileName: { } assemblyQualifiedName })
        {
            var assemblyName = new AssemblyName(assemblyQualifiedName).Name;
            if (assemblyName != null && UnbundledCodecAssemblies.TryGetValue(assemblyName, out var codec))
                return codec;
        }

        // A codec name the library does not recognise at all names itself in the message.
        if (ex is AvroRuntimeException && ex.Message.StartsWith(UnrecognizedCodecPrefix, StringComparison.Ordinal))
            return ex.Message[UnrecognizedCodecPrefix.Length..];

        return null;
    }
}
