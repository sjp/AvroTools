using Spectre.Console;

namespace AvroTool;

/// <summary>
/// The console that a command's human-readable payload is written to.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="IStandardStreams.Output"/> for a payload that is laid out and
/// coloured rather than piped onwards verbatim, and the counterpart of <see cref="IStatusConsole"/>
/// for output that is the answer the user asked for rather than a remark about how the run went.
/// A report written here can be redirected to a file on its own, and drops its colour when it is.
/// </remarks>
internal interface IOutputConsole : IAnsiConsole;

/// <summary>
/// An output console that renders through an existing console.
/// </summary>
/// <param name="console">The console to render through.</param>
internal sealed class OutputConsole(IAnsiConsole console) : ForwardingConsole(console), IOutputConsole;
