using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using AvroTool.Completions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvroTool.Commands;

internal sealed class CompletionsCommand : AsyncCommand<CompletionsCommand.Settings>
{
    public enum ShellKind
    {
        Bash,
        Zsh,
        Fish,
        PowerShell
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<SHELL>")]
        [Description("The shell to generate a completion script for (bash, zsh, fish, powershell).")]
        public ShellKind Shell { get; set; }
    }

    private readonly IAnsiConsole _console;

    public CompletionsCommand(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        _console = console;
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var script = CompletionScriptGenerator.Generate(settings.Shell);

            // Write the script verbatim to standard output. It is deliberately not routed
            // through the Spectre console: the script contains markup-significant characters
            // (e.g. '[', ']') and long lines that console markup/word-wrapping would mangle.
            Console.Out.Write(script);

            return Task.FromResult(ErrorCode.Success);
        }
        catch (Exception ex)
        {
            _console.MarkupLineInterpolated($"[red]Failed to generate completion script: {ex.Message}[/]");
            return Task.FromResult(ErrorCode.Error);
        }
    }
}
