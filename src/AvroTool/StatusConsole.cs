using Spectre.Console;

namespace AvroTool;

/// <summary>
/// The console that status and diagnostic messages are written to.
/// </summary>
/// <remarks>
/// A distinct type rather than <see cref="IAnsiConsole"/> because the command-line framework
/// reserves <see cref="IAnsiConsole"/> for the console it renders help and version output
/// through, and injects that one into commands whatever the container holds. Depending on this
/// interface instead keeps the channels apart: help and version follow convention onto standard
/// output, while a command's progress and failures stay on standard error and out of a
/// pipeline's payload.
/// </remarks>
internal interface IStatusConsole : IAnsiConsole;

/// <summary>
/// A status console that renders through an existing console.
/// </summary>
/// <param name="console">The console to render through.</param>
internal sealed class StatusConsole(IAnsiConsole console) : ForwardingConsole(console), IStatusConsole;
