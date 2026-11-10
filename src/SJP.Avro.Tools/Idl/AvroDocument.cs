using AvroProtocol = Avro.Protocol;
using AvroSchema = Avro.Schema;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// The Avro document an IDL document translates to: either a <see cref="AvroProtocol"/>, from an
/// IDL document that declares a <c>protocol</c>, or a single <see cref="AvroSchema"/>, from a
/// schema IDL document.
/// </summary>
/// <remarks>
/// <para>
/// Inspect the value with pattern matching. A <c>switch</c> expression over the two cases is
/// exhaustive, so it needs no discard arm:
/// </para>
/// <code>
/// var name = result.Document switch
/// {
///     Avro.Protocol protocol => protocol.Name,
///     Avro.Schema schema => schema.Name,
/// };
/// </code>
/// <para>
/// Either case converts implicitly to an <see cref="AvroDocument"/>. The default value of the
/// type holds neither case, and matches neither pattern; every document handed out by
/// <see cref="IdlParseResult"/> holds one.
/// </para>
/// </remarks>
public union AvroDocument(AvroProtocol, AvroSchema);
