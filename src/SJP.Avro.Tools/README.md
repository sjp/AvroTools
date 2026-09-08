# SJP.Avro.Tools

A pure C# implementation of the Avro schema tooling that usually needs a Java runtime:
an [Avro IDL](https://avro.apache.org/docs/1.12.0/idl-language/) compiler, a
schema-evolution compatibility checker, a semantic schema diff, and a JSON encoder for
Avro data.

It is the library behind the [`SJP.AvroTool`](https://www.nuget.org/packages/SJP.AvroTool)
command-line tool, and speaks the `Avro.Schema` and `Avro.Protocol` types from
[Apache.Avro](https://www.nuget.org/packages/Apache.Avro), so results can be handed
straight to the readers and writers that library provides.

```bash
dotnet add package SJP.Avro.Tools
```

## Compiling IDL

`IdlToAvroTranslator` turns an IDL document into either a protocol or a schema. It
resolves `import` statements through an `IIdlFileReader`; `PhysicalIdlFileReader` reads
them from disk, and `FileProviderIdlFileReader` reads them through an
`IFileProvider` — from embedded resources, say, or an in-memory file system.

```csharp
using SJP.Avro.Tools.Idl;

var translator = new IdlToAvroTranslator(new PhysicalIdlFileReader());

var path = Path.GetFullPath("sample.avdl");
var idl = await File.ReadAllTextAsync(path);

// The base directory relative imports resolve against, and the path of the document
// itself, so an import that leads back to it is recognised as a cycle.
var result = await translator.Translate(idl, Path.GetDirectoryName(path), path, default);

foreach (var warning in result.Warnings)
    Console.Error.WriteLine(warning);

// The document is a protocol or a schema; Match handles both.
var name = result.Match(
    protocol => protocol.Name,
    schema => schema.Name);

// The JSON form, as an .avpr or .avsc file would contain it.
Console.WriteLine(result.Json.ToString());

// Each named type reachable from the document, as a self-contained schema.
foreach (var namedType in result.GetNamedTypesJson())
    Console.WriteLine(namedType.ToString());
```

A document that cannot be translated raises an `IdlTranslationException` carrying the
line, column and reason.

## Checking compatibility

`SchemaCompatibility` answers whether data written with one schema can still be read with
another, under Avro's schema-resolution rules.

```csharp
using Avro;
using SJP.Avro.Tools.Compatibility;

var writer = Schema.Parse(await File.ReadAllTextAsync("v1.avsc"));
var reader = Schema.Parse(await File.ReadAllTextAsync("v2.avsc"));

var result = SchemaCompatibility.CheckReaderWriterCompatibility(reader, writer);
foreach (var incompatibility in result.Incompatibilities)
    Console.WriteLine($"{incompatibility.Location}: {incompatibility.Message}");
```

`SchemaCompatibility.Check` applies a whole registry-style mode — `Backward`, `Forward`,
`Full` and their transitive variants — and reports every comparison the mode called for.
The candidate schema comes first, followed by the versions it is checked against, most
recent first; a transitive mode checks it against all of them, a non-transitive mode
against only the one:

```csharp
var modeResult = SchemaCompatibility.Check(
    CompatibilityMode.BackwardTransitive,
    [candidate, v3, v2, v1]);

foreach (var check in modeResult.Checks.Where(c => !c.Result.IsCompatible))
{
    foreach (var incompatibility in check.Result.Incompatibilities)
        Console.WriteLine($"{check.Direction}: {incompatibility.Location}: {incompatibility.Message}");
}
```

## Diffing schemas

`SchemaDiff` reports what changed between two versions of a schema — fields added,
removed and reordered, types promoted, defaults and logical types altered — rather than
the textual difference between two documents.

```csharp
using SJP.Avro.Tools.Diff;

var diff = SchemaDiff.Compare(before, after);

foreach (var change in diff.Changes)
{
    Console.WriteLine($"{change.Kind} {change.Location}: {change.Message}");
    if (change.IsValidPromotion == true)
        Console.WriteLine("  (a type promotion a reader of the new schema accepts)");
}
```

Passing `includeMetadata: true` also reports changes that do not affect how data is read,
such as documentation and aliases.

## Encoding data as JSON

`AvroJsonWriter.Encode` writes a datum in Avro's JSON encoding, in which a union value is
tagged with the branch it came from.

```csharp
using SJP.Avro.Tools;

var json = AvroJsonWriter.Encode(schema, record);
```

## Related packages

* [`SJP.AvroTool`](https://www.nuget.org/packages/SJP.AvroTool) — the same functionality as
  a .NET command-line tool.
* [`SJP.Avro.Tools.CodeGen`](https://www.nuget.org/packages/SJP.Avro.Tools.CodeGen) —
  generates human-readable C# for the schemas and protocols this package produces.

## License

MIT. The project lives at [github.com/sjp/AvroTools](https://github.com/sjp/AvroTools).
