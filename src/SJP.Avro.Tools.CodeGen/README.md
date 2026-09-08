# SJP.Avro.Tools.CodeGen

Generates human-readable C# for Avro records, errors, enums, fixed types and protocols.
The generator is built on Roslyn and returns a plain C# file as a string, so the output
can be written to disk, compiled in memory, or emitted from a build step.

It is the generator behind the [`SJP.AvroTool`](https://www.nuget.org/packages/SJP.AvroTool)
command-line tool, and takes the `Avro.Schema` and `Avro.Protocol` types from
[Apache.Avro](https://www.nuget.org/packages/Apache.Avro) as its input. A record implements
that library's `ISpecificRecord`, an error derives from `SpecificException`, a fixed type
from `SpecificFixed`, and a protocol is generated as an abstract `ISpecificProtocol` whose
messages dispatch through an `ICallbackRequestor`, so the output drops into that library's
specific readers, writers and requestors.

```bash
dotnet add package SJP.Avro.Tools.CodeGen
```

## Generating a type

`CodeGeneratorResolver` hands back the generator for a given kind of Avro type. Each one
takes the type, the namespace to fall back on when the Avro type declares none, and
optional style settings, and returns the C# file — or `null` when the type has nothing to
generate.

```csharp
using Avro;
using SJP.Avro.Tools.CodeGen;

var schema = (RecordSchema)Schema.Parse(await File.ReadAllTextAsync("Person.avsc"));

var resolver = new CodeGeneratorResolver();
var generator = resolver.Resolve<RecordSchema>()!;

var code = generator.Generate(schema, "Example.Generated");
await File.WriteAllTextAsync("Person.cs", code);
```

`Resolve<T>` covers `RecordSchema` (which generates both records and errors), `EnumSchema`,
`FixedSchema` and `Protocol`. A schema usually names several types, so generating a whole
document means walking the named types it reaches and generating a file for each:

```csharp
foreach (var namedType in schema.GetNamedTypes())
{
    var output = namedType switch
    {
        RecordSchema record => resolver.Resolve<RecordSchema>()!.Generate(record, ns),
        EnumSchema enumeration => resolver.Resolve<EnumSchema>()!.Generate(enumeration, ns),
        FixedSchema fixedSchema => resolver.Resolve<FixedSchema>()!.Generate(fixedSchema, ns),
        _ => null
    };
}
```

`GetNamedTypes()` is an extension in
[`SJP.Avro.Tools`](https://www.nuget.org/packages/SJP.Avro.Tools), which also compiles Avro
IDL to the schemas and protocols this package generates from.

## Output style

`CodeGenOptions` chooses between the property styles C# offers:

```csharp
var options = new CodeGenOptions(RequiredProperties: true, InitOnlyProperties: true);
var code = generator.Generate(schema, "Example.Generated", options);
```

* `InitOnlyProperties` generates `init` accessors rather than `set`, so an instance is
  configured through an object initialiser and not mutated afterwards.
* `RequiredProperties` marks the properties of non-optional fields `required`, so the
  compiler rejects an initialiser that leaves one unset.

Generated files open with `#nullable enable` and are annotated throughout, so they compile
cleanly whether or not the consuming project has nullable reference types switched on. The
output is deterministic: the same input produces the same text, with line feeds and
invariant formatting, regardless of the machine or culture it was generated on.

## Names C# cannot spell

Avro allows names C# does not. A field named after a C# keyword, or after a member the
generated type already has, is renamed in the C# output while keeping its Avro name in the
schema, so the two still agree on the wire. Avro places no restriction at all on a
namespace or on a protocol's own name, though, and generated code spells those exactly as
the definition does. `AvroNameValidation.FindUnusableNames` reports the ones C# has no way
to write — `my-service`, say — so such a definition can be rejected with a message that
names them, rather than generating source that does not compile:

```csharp
var unusable = AvroNameValidation.FindUnusableNames(schema);
if (unusable.Count > 0)
    throw new InvalidOperationException($"Not expressible in C#: {string.Join(", ", unusable)}");
```

A named type whose own name collides with a member its generated class is obliged to
declare — a `fixed` called `Schema`, or an error called `Get` — raises a
`NotSupportedException` for the same reason.

## Related packages

* [`SJP.AvroTool`](https://www.nuget.org/packages/SJP.AvroTool) — the same generator as a
  .NET command-line tool.
* [`SJP.Avro.Tools`](https://www.nuget.org/packages/SJP.Avro.Tools) — the Avro IDL compiler,
  compatibility checker and schema diff.

## License

MIT. The project lives at [github.com/sjp/AvroTools](https://github.com/sjp/AvroTools).
