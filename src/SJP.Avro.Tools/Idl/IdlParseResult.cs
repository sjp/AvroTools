using System;
using AvroProtocol = Avro.Protocol;
using AvroSchema = Avro.Schema;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// A convenience class used for enabling access to either a protocol or a schema
/// </summary>
public sealed record IdlParseResult
{
    private readonly AvroProtocol? _protocol;
    private readonly AvroSchema? _schema;

    private IdlParseResult(AvroProtocol protocol) => _protocol = protocol;

    private IdlParseResult(AvroSchema schema) => _schema = schema;

    /// <summary>
    /// Constructs a parse result that contains a <see cref="AvroProtocol"/>.
    /// </summary>
    public static IdlParseResult Protocol(AvroProtocol protocol) => new(protocol);

    /// <summary>
    /// Constructs a parse result that contains a <see cref="AvroSchema"/>.
    /// </summary>
    public static IdlParseResult Schema(AvroSchema schema) => new(schema);

    /// <summary>
    /// Returns <c>true</c> when the value contains a <see cref="AvroProtocol"/>.
    /// </summary>
    public bool IsProtocol => _protocol != null;

    /// <summary>
    /// Returns <c>true</c> when the value contains a <see cref="AvroSchema"/>.
    /// </summary>
    public bool IsSchema => _schema != null;

    /// <summary>
    /// Enables actions to be performed depending on the contained value.
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
    /// Returns different values depending on the contained value.
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