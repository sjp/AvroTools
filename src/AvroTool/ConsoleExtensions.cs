using System;
using System.CommandLine;
using System.CommandLine.IO;
using System.Linq;

namespace AvroTool;

internal static class ConsoleExtensions
{
    internal static void SetTerminalForegroundRed(this IConsole console) => console.SetTerminalForeground(ConsoleColor.Red);

    internal static void SetTerminalForegroundYellow(this IConsole console) => console.SetTerminalForeground(ConsoleColor.Yellow);

    internal static void SetTerminalForegroundGreen(this IConsole console) => console.SetTerminalForeground(ConsoleColor.Green);

    internal static void SetTerminalForeground(this IConsole console, ConsoleColor color)
    {
        if (console.GetType().GetInterfaces().Any(static i => string.Equals(i.Name, "ITerminal", StringComparison.Ordinal)))
        {
            ((dynamic)console).ForegroundColor = color;
        }

        if (Platform.IsConsoleRedirectionCheckSupported &&
            !Console.IsOutputRedirected)
        {
            Console.ForegroundColor = color;
        }
        else if (Platform.IsConsoleRedirectionCheckSupported)
        {
            Console.ForegroundColor = color;
        }
    }

    internal static void ResetTerminalForegroundColor(this IConsole console)
    {
        if (console.GetType().GetInterfaces().Any(static i => string.Equals(i.Name, "ITerminal", StringComparison.Ordinal)))
        {
            ((dynamic)console).ForegroundColor = ConsoleColor.Red;
        }

        if (Platform.IsConsoleRedirectionCheckSupported &&
            !Console.IsOutputRedirected)
        {
            Console.ResetColor();
        }
        else if (Platform.IsConsoleRedirectionCheckSupported)
        {
            Console.ResetColor();
        }
    }

    internal static void WriteLine(this IStandardStreamWriter streamWriter)
    {
        streamWriter.Write(Environment.NewLine);
    }

    internal static void WriteLine(this IStandardStreamWriter streamWriter, string message)
    {
        streamWriter.Write(message);
        streamWriter.WriteLine();
    }
}