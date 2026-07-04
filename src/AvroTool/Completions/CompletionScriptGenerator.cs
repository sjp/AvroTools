using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using AvroTool.Commands;
using Spectre.Console.Cli;

namespace AvroTool.Completions;

/// <summary>
/// Generates static shell completion scripts for the <c>avrotool</c> CLI.
/// </summary>
internal static class CompletionScriptGenerator
{
    private const string AppName = "avrotool";

    /// <summary>
    /// The set of commands exposed by the CLI. Command names are listed once here and must
    /// match the registrations in <c>Program.cs</c>; each command's option flags are reflected
    /// from its settings type so they stay in sync automatically.
    /// </summary>
    private static readonly IReadOnlyList<CommandInfo> Commands =
    [
        new("idl", typeof(IdlCommand.Settings)),
        new("idl2schemata", typeof(IdlToSchemataCommand.Settings)),
        new("codegen", typeof(CodeGenCommand.Settings)),
        new("completions", typeof(CompletionsCommand.Settings)),
    ];

    private static readonly IReadOnlyList<string> GlobalOptions = ["-h", "--help", "-v", "--version"];

    public static string Generate(CompletionsCommand.ShellKind shell) => shell switch
    {
        CompletionsCommand.ShellKind.Bash => GenerateBash(),
        CompletionsCommand.ShellKind.Zsh => GenerateZsh(),
        CompletionsCommand.ShellKind.Fish => GenerateFish(),
        CompletionsCommand.ShellKind.PowerShell => GeneratePowerShell(),
        _ => throw new ArgumentOutOfRangeException(nameof(shell), shell, "Unsupported shell."),
    };

    private static string CommandNames() => string.Join(' ', Commands.Select(c => c.Name));

    private static IReadOnlyList<string> OptionFlags(CommandInfo command)
    {
        var flags = command.SettingsType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<CommandOptionAttribute>())
            .Where(a => a is { IsHidden: false })
            .SelectMany(a => a!.ShortNames.Select(n => "-" + n).Concat(a.LongNames.Select(n => "--" + n)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // The help flag is always available on a subcommand.
        flags.Add("-h");
        flags.Add("--help");

        return flags;
    }

    private static string GenerateBash()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# bash completion for {AppName}");
        sb.AppendLine($"_{AppName}_completions() {{");
        sb.AppendLine("    local cur cmd");
        sb.AppendLine("    cur=\"${COMP_WORDS[COMP_CWORD]}\"");
        sb.AppendLine("    cmd=\"${COMP_WORDS[1]}\"");
        sb.AppendLine();
        sb.AppendLine("    if [ \"$COMP_CWORD\" -eq 1 ]; then");
        sb.AppendLine($"        COMPREPLY=( $(compgen -W \"{CommandNames()} {string.Join(' ', GlobalOptions)}\" -- \"$cur\") )");
        sb.AppendLine("        return");
        sb.AppendLine("    fi");
        sb.AppendLine();
        sb.AppendLine("    case \"$cmd\" in");
        foreach (var command in Commands)
        {
            sb.AppendLine($"        {command.Name})");
            sb.AppendLine($"            COMPREPLY=( $(compgen -W \"{string.Join(' ', OptionFlags(command))}\" -- \"$cur\") )");
            sb.AppendLine("            ;;");
        }
        sb.AppendLine("        *)");
        sb.AppendLine("            ;;");
        sb.AppendLine("    esac");
        sb.AppendLine("}");
        sb.AppendLine($"complete -F _{AppName}_completions {AppName}");
        return sb.ToString();
    }

    private static string GenerateZsh()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"#compdef {AppName}");
        sb.AppendLine($"_{AppName}() {{");
        sb.AppendLine("    local -a commands");
        sb.AppendLine("    commands=(");
        foreach (var command in Commands)
            sb.AppendLine($"        '{command.Name}'");
        sb.AppendLine("    )");
        sb.AppendLine();
        sb.AppendLine("    _arguments -C \\");
        sb.AppendLine("        '1: :->command' \\");
        sb.AppendLine("        '*:: :->args'");
        sb.AppendLine();
        sb.AppendLine("    case $state in");
        sb.AppendLine("        command)");
        sb.AppendLine("            _describe 'command' commands");
        sb.AppendLine("            ;;");
        sb.AppendLine("        args)");
        sb.AppendLine("            case $words[1] in");
        foreach (var command in Commands)
        {
            sb.AppendLine($"                {command.Name})");
            sb.AppendLine($"                    _values 'option' {string.Join(' ', OptionFlags(command).Select(f => $"'{f}'"))}");
            sb.AppendLine("                    ;;");
        }
        sb.AppendLine("            esac");
        sb.AppendLine("            ;;");
        sb.AppendLine("    esac");
        sb.AppendLine("}");
        sb.AppendLine($"compdef _{AppName} {AppName}");
        return sb.ToString();
    }

    private static string GenerateFish()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# fish completion for {AppName}");
        sb.AppendLine($"complete -c {AppName} -f");
        foreach (var command in Commands)
            sb.AppendLine($"complete -c {AppName} -n '__fish_use_subcommand' -a '{command.Name}'");
        sb.AppendLine();
        foreach (var command in Commands)
        {
            foreach (var flag in OptionFlags(command))
            {
                var trimmed = flag.TrimStart('-');
                var kind = flag.StartsWith("--", StringComparison.Ordinal) ? "-l" : "-s";
                sb.AppendLine($"complete -c {AppName} -n '__fish_seen_subcommand_from {command.Name}' {kind} '{trimmed}'");
            }
        }
        return sb.ToString();
    }

    private static string GeneratePowerShell()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# PowerShell completion for {AppName}");
        sb.AppendLine($"Register-ArgumentCompleter -Native -CommandName {AppName} -ScriptBlock {{");
        sb.AppendLine("    param($wordToComplete, $commandAst, $cursorPosition)");
        sb.AppendLine();
        sb.AppendLine("    $commandElements = $commandAst.CommandElements");
        sb.AppendLine("    $command = if ($commandElements.Count -ge 2) { $commandElements[1].Value } else { '' }");
        sb.AppendLine();
        sb.AppendLine("    $completions = switch ($command) {");
        foreach (var command in Commands)
            sb.AppendLine($"        '{command.Name}' {{ @({string.Join(", ", OptionFlags(command).Select(f => $"'{f}'"))}) }}");
        sb.AppendLine($"        default {{ @({string.Join(", ", Commands.Select(c => $"'{c.Name}'").Concat(GlobalOptions.Select(o => $"'{o}'")))}) }}");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    $completions |");
        sb.AppendLine("        Where-Object { $_ -like \"$wordToComplete*\" } |");
        sb.AppendLine("        ForEach-Object { [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_) }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private sealed record CommandInfo(string Name, Type SettingsType);
}
