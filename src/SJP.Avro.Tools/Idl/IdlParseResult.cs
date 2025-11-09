using System;
using AvroProtocol = Avro.Protocol;
using AvroSchema = Avro.Schema;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// TODO
/// </summary>
public sealed record IdlParseResult
{
    private readonly AvroProtocol? _protocol;
    private readonly AvroSchema? _schema;

    private IdlParseResult(AvroProtocol protocol) => _protocol = protocol;

    private IdlParseResult(AvroSchema schema) => _schema = schema;

    /// <summary>
    /// TODO
    /// </summary>
    public static IdlParseResult Protocol(AvroProtocol protocol) => new IdlParseResult(protocol);

    /// <summary>
    /// TODO
    /// </summary>
    public static IdlParseResult Schema(AvroSchema schema) => new IdlParseResult(schema);

    /// <summary>
    /// TODO
    /// </summary>
    public bool IsProtocol => _protocol != null;

    /// <summary>
    /// TODO
    /// </summary>
    public bool IsSchema => _schema != null;

    /// <summary>
    /// TODO
    /// </summary>
    public void Match(
        Action<AvroProtocol> protocol,
        Action<AvroSchema> schema)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(schema);

        if (IsProtocol)
            protocol.Invoke(_protocol!);

        if (IsSchema)
            schema.Invoke(_schema!);
    }

    /// <summary>
    /// TODO
    /// </summary>
    public T Match<T>(
        Func<AvroProtocol, T> protocol,
        Func<AvroSchema, T> schema)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(schema);

        return IsProtocol
            ? protocol.Invoke(_protocol!)
            : schema.Invoke(_schema!);
    }
}