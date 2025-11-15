using System;
using SJP.Avro.Tools.Idl;

namespace AvroTool;

internal sealed record IdlFileParseResult
{
    private readonly Exception? _exception;
    private readonly IdlParseResult? _parseResult;

    private IdlFileParseResult(
        bool success,
        Exception? exception,
        IdlParseResult? parseResult)
    {
        Success = success;
        _exception = exception;
        _parseResult = parseResult;
    }

    public static IdlFileParseResult Ok(IdlParseResult result) => new(true, null, result);

    public static IdlFileParseResult Error(Exception ex) => new(false, ex, null);

    public bool Success { get; }

    public Exception Exception
    {
        get
        {
            if (Success)
                throw new InvalidOperationException($"Attempted to access {nameof(Exception)} when {nameof(Success)} was true");

            return _exception!;
        }
    }

    public IdlParseResult Result
    {
        get
        {
            if (!Success)
                throw new InvalidOperationException($"Attempted to access {nameof(Result)} when {nameof(Success)} was false");

            return _parseResult!;
        }
    }
}
