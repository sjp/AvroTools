using System;

namespace AvroTool;

internal sealed record Result<T> where T : class
{
    private readonly Exception? _exception;
    private readonly T? _parseResult;

    private Result(
        bool success,
        Exception? exception,
        T? parseResult)
    {
        Success = success;
        _exception = exception;
        _parseResult = parseResult;
    }

    public static Result<T> Ok(T result) => new(true, null, result);

    public static Result<T> Error(Exception ex) => new(false, ex, null);

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

    public T Data
    {
        get
        {
            if (!Success)
                throw new InvalidOperationException($"Attempted to access {nameof(Result<>)} when {nameof(Success)} was false");

            return _parseResult!;
        }
    }
}
