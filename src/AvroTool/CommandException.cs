using System;

namespace SJP.Avro.AvroTool;

[Serializable]
internal class CommandException : Exception
{
    public CommandException()
    {
    }

    public CommandException(string message)
        : base(message)
    {
    }

    public CommandException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}