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
/// <remarks>
/// Everything the scripts contain is derived from <see cref="CommandCatalogue"/>: the command
/// names and descriptions from the catalogue itself, and each command's arguments and options
/// by reflecting over its settings type. Adding a command or an option therefore updates the
/// completion scripts without any change here.
/// </remarks>
internal static class CompletionScriptGenerator
{
    private const string AppName = "avrotool";

    private const string FileSpec = "<file>";
    private const string DirectorySpec = "<dir>";
    private const string NoneSpec = "<none>";

    private static readonly IReadOnlyList<string> GlobalOptions = ["-h", "--help", "-v", "--version"];

    public static string Generate(CompletionsCommand.ShellKind shell) => shell switch
    {
        CompletionsCommand.ShellKind.Bash => GenerateBash(),
        CompletionsCommand.ShellKind.Zsh => GenerateZsh(),
        CompletionsCommand.ShellKind.Fish => GenerateFish(),
        CompletionsCommand.ShellKind.PowerShell => GeneratePowerShell(),
        _ => throw new ArgumentOutOfRangeException(nameof(shell), shell, "Unsupported shell."),
    };

    private static IReadOnlyList<CommandDefinition> Commands => CommandCatalogue.Commands;

    private static string CommandNames() => string.Join(' ', Commands.Select(c => c.Name));

    /// <summary>
    /// What a shell should offer in place of a value: a set of literal values, a file path,
    /// a directory path, or nothing at all.
    /// </summary>
    private sealed record Completion(string Spec, IReadOnlyList<string> Values)
    {
        public static readonly Completion None = new(NoneSpec, []);
        public static readonly Completion File = new(FileSpec, []);
        public static readonly Completion Directory = new(DirectorySpec, []);

        public static Completion FromValues(IEnumerable<string> values)
        {
            var list = values.ToList();
            return list.Count == 0 ? None : new Completion(string.Join(' ', list), list);
        }

        public bool IsValues => Values.Count > 0;
    }

    // An option, its flags, and what its value (if it takes one) completes to. The name labels
    // the value in shells that show a label.
    private sealed record OptionInfo(string Name, IReadOnlyList<string> Flags, bool TakesValue, Completion Value);

    // A command's positional arguments and what they complete to.
    private sealed record ArgumentInfo(string Name, Completion Value);

    private static readonly OptionInfo HelpOption = new("help", ["-h", "--help"], false, Completion.None);

    private static IReadOnlyList<OptionInfo> Options(CommandDefinition command)
    {
        var options = command.SettingsType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (Property: p, Attribute: p.GetCustomAttribute<CommandOptionAttribute>()))
            .Where(x => x.Attribute is { IsHidden: false })
            .Select(x =>
            {
                var flags = x.Attribute!.ShortNames.Select(n => "-" + n)
                    .Concat(x.Attribute.LongNames.Select(n => "--" + n))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var takesValue = !IsFlag(x.Property.PropertyType);
                var value = takesValue
                    ? ValueCompletion(command, x.Attribute.LongNames, x.Property.PropertyType)
                    : Completion.None;

                var name = x.Attribute.LongNames.FirstOrDefault() ?? x.Attribute.ShortNames.FirstOrDefault() ?? "value";

                return new OptionInfo(name, flags, takesValue, value);
            })
            .ToList();

        // The help flag is always available on a subcommand.
        options.Add(HelpOption);

        return options;
    }

    private static IReadOnlyList<string> OptionFlags(CommandDefinition command)
        => Options(command).SelectMany(o => o.Flags).ToList();

    /// <summary>
    /// What the command's positional arguments complete to. Where a command's arguments do not
    /// agree (none currently disagree) file completion is used, which is never actively wrong.
    /// </summary>
    private static ArgumentInfo Arguments(CommandDefinition command)
    {
        var arguments = command.SettingsType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (Property: p, Attribute: p.GetCustomAttribute<CommandArgumentAttribute>()))
            .Where(x => x.Attribute != null)
            .OrderBy(x => x.Attribute!.Position)
            .Select(x => new ArgumentInfo(
                ArgumentName(x.Attribute!.ValueName),
                ValueCompletion(command, [], x.Property.PropertyType) is var c && c != Completion.None ? c : Completion.File))
            .ToList();

        if (arguments.Count == 0)
            return new ArgumentInfo("value", Completion.None);

        return arguments.DistinctBy(a => a.Value).Count() == 1
            ? arguments[0]
            : new ArgumentInfo("file", Completion.File);
    }

    // Turns an argument's help template (e.g. '<SCHEMA_FILE>' or '[IDL_FILES]') into a label.
    private static string ArgumentName(string valueName)
        => valueName.Trim('<', '>', '[', ']').ToLowerInvariant();

    private static Completion ValueCompletion(CommandDefinition command, IReadOnlyList<string> longNames, Type propertyType)
    {
        foreach (var longName in longNames)
        {
            if (command.OptionValues.TryGetValue(longName, out var values))
                return Completion.FromValues(values);
        }

        var type = UnderlyingType(propertyType);

        if (type == typeof(System.IO.DirectoryInfo))
            return Completion.Directory;

        if (type == typeof(System.IO.FileInfo))
            return Completion.File;

        if (type.IsEnum)
            return Completion.FromValues(Enum.GetNames(type).Select(n => n.ToLowerInvariant()));

        return Completion.None;
    }

    private static bool IsFlag(Type propertyType) => UnderlyingType(propertyType) == typeof(bool);

    private static Type UnderlyingType(Type type)
    {
        if (type.IsArray && type.GetElementType() is { } elementType)
            type = elementType;

        return Nullable.GetUnderlyingType(type) ?? type;
    }

    private static string GenerateBash()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# bash completion for {AppName}");
        sb.AppendLine($"_{AppName}_complete_spec() {{");
        sb.AppendLine("    local spec=\"$1\" cur=\"$2\"");
        sb.AppendLine("    case \"$spec\" in");
        sb.AppendLine($"        '{FileSpec}')");
        sb.AppendLine("            COMPREPLY=( $(compgen -f -- \"$cur\") )");
        sb.AppendLine("            compopt -o filenames 2>/dev/null");
        sb.AppendLine("            ;;");
        sb.AppendLine($"        '{DirectorySpec}')");
        sb.AppendLine("            COMPREPLY=( $(compgen -d -- \"$cur\") )");
        sb.AppendLine("            compopt -o filenames 2>/dev/null");
        sb.AppendLine("            ;;");
        sb.AppendLine($"        '{NoneSpec}')");
        sb.AppendLine("            COMPREPLY=()");
        sb.AppendLine("            ;;");
        sb.AppendLine("        *)");
        sb.AppendLine("            COMPREPLY=( $(compgen -W \"$spec\" -- \"$cur\") )");
        sb.AppendLine("            ;;");
        sb.AppendLine("    esac");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"_{AppName}_completions() {{");
        sb.AppendLine("    local cur prev cmd options args values");
        sb.AppendLine("    cur=\"${COMP_WORDS[COMP_CWORD]}\"");
        sb.AppendLine("    prev=\"${COMP_WORDS[COMP_CWORD-1]}\"");
        sb.AppendLine("    cmd=\"${COMP_WORDS[1]}\"");
        sb.AppendLine("    COMPREPLY=()");
        sb.AppendLine();
        sb.AppendLine("    if [ \"$COMP_CWORD\" -eq 1 ]; then");
        sb.AppendLine($"        COMPREPLY=( $(compgen -W \"{CommandNames()} {string.Join(' ', GlobalOptions)}\" -- \"$cur\") )");
        sb.AppendLine("        return");
        sb.AppendLine("    fi");
        sb.AppendLine();
        sb.AppendLine("    options=\"\"");
        sb.AppendLine($"    args='{NoneSpec}'");
        sb.AppendLine("    values=\"\"");
        sb.AppendLine();
        sb.AppendLine("    case \"$cmd\" in");
        foreach (var command in Commands)
        {
            var options = Options(command);

            sb.AppendLine($"        {command.Name})");
            sb.AppendLine($"            options=\"{string.Join(' ', options.SelectMany(o => o.Flags))}\"");
            sb.AppendLine($"            args='{Arguments(command).Value.Spec}'");

            var valueOptions = options.Where(o => o.TakesValue).ToList();
            if (valueOptions.Count > 0)
            {
                sb.AppendLine("            case \"$prev\" in");
                foreach (var option in valueOptions)
                    sb.AppendLine($"                {string.Join('|', option.Flags)}) values='{option.Value.Spec}' ;;");
                sb.AppendLine("            esac");
            }

            sb.AppendLine("            ;;");
        }
        sb.AppendLine("        *)");
        sb.AppendLine("            ;;");
        sb.AppendLine("    esac");
        sb.AppendLine();
        sb.AppendLine("    if [ -n \"$values\" ]; then");
        sb.AppendLine($"        _{AppName}_complete_spec \"$values\" \"$cur\"");
        sb.AppendLine("        return");
        sb.AppendLine("    fi");
        sb.AppendLine();
        sb.AppendLine("    if [[ \"$cur\" == -* ]]; then");
        sb.AppendLine("        COMPREPLY=( $(compgen -W \"$options\" -- \"$cur\") )");
        sb.AppendLine("        return");
        sb.AppendLine("    fi");
        sb.AppendLine();
        sb.AppendLine($"    _{AppName}_complete_spec \"$args\" \"$cur\"");
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
            sb.AppendLine($"        '{command.Name}:{ZshQuote(command.Description)}'");
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
            var specs = Options(command)
                .SelectMany(o => o.Flags.Select(f => ZshOptionSpec(f, o)))
                .ToList();

            var argument = Arguments(command);
            if (argument.Value != Completion.None)
                specs.Add($"'*:{ZshLabel(argument.Value, argument.Name)}:{ZshAction(argument.Value)}'");

            sb.AppendLine($"                {command.Name})");
            sb.AppendLine("                    _arguments \\");
            for (var i = 0; i < specs.Count; i++)
                sb.AppendLine($"                        {specs[i]}{(i == specs.Count - 1 ? string.Empty : " \\")}");
            sb.AppendLine("                    ;;");
        }
        sb.AppendLine("            esac");
        sb.AppendLine("            ;;");
        sb.AppendLine("    esac");
        sb.AppendLine("}");
        sb.AppendLine($"compdef _{AppName} {AppName}");
        return sb.ToString();
    }

    private static string ZshOptionSpec(string flag, OptionInfo option)
        => option.TakesValue
            ? $"'{flag}:{ZshLabel(option.Value, option.Name)}:{ZshAction(option.Value)}'"
            : $"'{flag}'";

    private static string ZshLabel(Completion completion, string name) => completion.Spec switch
    {
        FileSpec => "file",
        DirectorySpec => "directory",
        _ => name,
    };

    private static string ZshAction(Completion completion) => completion.Spec switch
    {
        FileSpec => "_files",
        DirectorySpec => "_files -/",
        NoneSpec => string.Empty,
        _ => $"({completion.Spec})",
    };

    private static string ZshQuote(string value) => value.Replace("'", @"'\''", StringComparison.Ordinal).Replace(":", @"\:", StringComparison.Ordinal);

    private static string GenerateFish()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# fish completion for {AppName}");
        sb.AppendLine($"complete -c {AppName} -f");
        foreach (var command in Commands)
            sb.AppendLine($"complete -c {AppName} -n '__fish_use_subcommand' -a '{command.Name}' -d '{FishQuote(command.Description)}'");
        sb.AppendLine();
        foreach (var command in Commands)
        {
            var condition = $"-n '__fish_seen_subcommand_from {command.Name}'";

            var argument = Arguments(command).Value;
            if (argument == Completion.File)
                sb.AppendLine($"complete -c {AppName} {condition} -F");
            else if (argument == Completion.Directory)
                sb.AppendLine($"complete -c {AppName} {condition} -a '(__fish_complete_directories)'");
            else if (argument.IsValues)
                sb.AppendLine($"complete -c {AppName} {condition} -a '{argument.Spec}'");

            foreach (var option in Options(command))
            {
                foreach (var flag in option.Flags)
                {
                    var trimmed = flag.TrimStart('-');
                    var kind = flag.StartsWith("--", StringComparison.Ordinal) ? "-l" : "-s";
                    sb.AppendLine($"complete -c {AppName} {condition} {kind} '{trimmed}'{FishValue(option)}");
                }
            }
        }
        return sb.ToString();
    }

    private static string FishValue(OptionInfo option)
    {
        if (!option.TakesValue)
            return string.Empty;

        return option.Value.Spec switch
        {
            FileSpec => " -r -F",
            DirectorySpec => " -r -a '(__fish_complete_directories)'",
            NoneSpec => " -r",
            _ => $" -r -a '{option.Value.Spec}'",
        };
    }

    private static string FishQuote(string value)
        => value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("'", @"\'", StringComparison.Ordinal);

    private static string GeneratePowerShell()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# PowerShell completion for {AppName}");
        sb.AppendLine($"Register-ArgumentCompleter -Native -CommandName {AppName} -ScriptBlock {{");
        sb.AppendLine("    param($wordToComplete, $commandAst, $cursorPosition)");
        sb.AppendLine();
        sb.AppendLine($"    $commands = @({string.Join(", ", Commands.Select(c => $"'{c.Name}'"))})");
        sb.AppendLine($"    $globalOptions = @({string.Join(", ", GlobalOptions.Select(o => $"'{o}'"))})");
        sb.AppendLine();
        sb.AppendLine("    $commandOptions = @{");
        foreach (var command in Commands)
            sb.AppendLine($"        '{command.Name}' = @({string.Join(", ", OptionFlags(command).Select(f => $"'{f}'"))})");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    $optionValues = @{");
        foreach (var command in Commands)
        {
            var valueOptions = Options(command).Where(o => o.TakesValue).ToList();
            sb.AppendLine($"        '{command.Name}' = @{{");
            foreach (var option in valueOptions)
            {
                foreach (var flag in option.Flags)
                    sb.AppendLine($"            '{flag}' = {PowerShellSpec(option.Value)}");
            }
            sb.AppendLine("        }");
        }
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    $argumentValues = @{");
        foreach (var command in Commands)
            sb.AppendLine($"        '{command.Name}' = {PowerShellSpec(Arguments(command).Value)}");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    function Expand-Spec($spec, $word) {");
        sb.AppendLine("        if ($spec -is [array]) {");
        sb.AppendLine("            return @($spec | Where-Object { $_ -like \"$word*\" })");
        sb.AppendLine("        }");
        sb.AppendLine($"        if ($spec -eq '{FileSpec}' -or $spec -eq '{DirectorySpec}') {{");
        sb.AppendLine($"            $items = if ($spec -eq '{DirectorySpec}') {{");
        sb.AppendLine("                Get-ChildItem -Path \"$word*\" -Directory -ErrorAction SilentlyContinue");
        sb.AppendLine("            } else {");
        sb.AppendLine("                Get-ChildItem -Path \"$word*\" -ErrorAction SilentlyContinue");
        sb.AppendLine("            }");
        sb.AppendLine("            return @($items | ForEach-Object {");
        sb.AppendLine("                $path = Resolve-Path -Relative -LiteralPath $_.FullName -ErrorAction SilentlyContinue");
        sb.AppendLine("                if ($path -match '\\s') { \"'$path'\" } else { $path }");
        sb.AppendLine("            })");
        sb.AppendLine("        }");
        sb.AppendLine("        return @()");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    $elements = @($commandAst.CommandElements | ForEach-Object { $_.ToString() })");
        sb.AppendLine("    $command = if ($elements.Count -ge 2) { $elements[1] } else { '' }");
        sb.AppendLine();
        sb.AppendLine("    $previous = ''");
        sb.AppendLine("    if ($elements.Count -ge 2) {");
        sb.AppendLine("        $last = $elements[$elements.Count - 1]");
        sb.AppendLine("        if ($wordToComplete -and $last -eq $wordToComplete) {");
        sb.AppendLine("            if ($elements.Count -ge 3) { $previous = $elements[$elements.Count - 2] }");
        sb.AppendLine("        } else {");
        sb.AppendLine("            $previous = $last");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    $completions = if (-not ($commands -contains $command)) {");
        sb.AppendLine("        @(($commands + $globalOptions) | Where-Object { $_ -like \"$wordToComplete*\" })");
        sb.AppendLine("    } elseif ($optionValues[$command].ContainsKey($previous)) {");
        sb.AppendLine("        Expand-Spec $optionValues[$command][$previous] $wordToComplete");
        sb.AppendLine("    } elseif ($wordToComplete.StartsWith('-')) {");
        sb.AppendLine("        @($commandOptions[$command] | Where-Object { $_ -like \"$wordToComplete*\" })");
        sb.AppendLine("    } else {");
        sb.AppendLine("        Expand-Spec $argumentValues[$command] $wordToComplete");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    $completions |");
        sb.AppendLine("        ForEach-Object { [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_) }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string PowerShellSpec(Completion completion)
        => completion.IsValues
            ? $"@({string.Join(", ", completion.Values.Select(v => $"'{v}'"))})"
            : $"'{completion.Spec}'";
}
