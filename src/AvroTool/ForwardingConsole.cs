using System;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AvroTool;

/// <summary>
/// A console that renders through an existing console.
/// </summary>
/// <remarks>
/// The tool keeps its output channels apart by giving each one its own interface, because the
/// command-line framework reserves <see cref="IAnsiConsole"/> for the console it renders help
/// and version output through and injects that one into commands whatever the container holds.
/// This is the plumbing each of those channels shares.
/// </remarks>
/// <param name="console">The console to render through.</param>
internal abstract class ForwardingConsole(IAnsiConsole console) : IAnsiConsole
{
    /// <inheritdoc />
    public Profile Profile => console.Profile;

    /// <inheritdoc />
    public IAnsiConsoleCursor Cursor => console.Cursor;

    /// <inheritdoc />
    public IAnsiConsoleInput Input => console.Input;

    /// <inheritdoc />
    public IExclusivityMode ExclusivityMode => console.ExclusivityMode;

    /// <inheritdoc />
    public RenderPipeline Pipeline => console.Pipeline;

    /// <inheritdoc />
    public void Clear(bool home) => console.Clear(home);

    /// <inheritdoc />
    public void Write(IRenderable renderable) => console.Write(renderable);

    /// <inheritdoc />
    public void WriteAnsi(Action<AnsiWriter> action) => console.WriteAnsi(action);
}
