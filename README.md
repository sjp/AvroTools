# Avro Tools

[![License (MIT)](https://img.shields.io/badge/license-MIT-blue.svg)](https://opensource.org/licenses/MIT) [![GitHub Actions](https://github.com/sjp/AvroTools/actions/workflows/ci.yml/badge.svg)](https://github.com/sjp/AvroTools/actions/workflows/ci.yml) [![Code coverage](https://img.shields.io/codecov/c/gh/sjp/AvroTools/master?logo=codecov)](https://codecov.io/gh/sjp/AvroTools)

A collection of tools to work with Apache Avro in C#.

## Description

The intention of this project is to provide a pure C# implementation of an [Avro IDL](https://avro.apache.org/docs/1.12.0/idl-language/) compiler. Additionally, although the [Avro GitHub](https://github.com/apache/avro) project does contain a code generator for C#, it contains rather verbose code. This project generates human-readable output via a Roslyn-based code generator.

One other benefit of this project is avoiding the pre-requisite for a Java runtime.

## Features

* Compile [Avro IDL](https://avro.apache.org/docs/current/idl.html) to an [Avro Protocol](https://avro.apache.org/docs/1.12.0/specification/#protocol-declaration).
* Compile [Avro IDL](https://avro.apache.org/docs/current/idl.html) to [Avro Schema](https://avro.apache.org/docs/1.12.0/specification/#schema-declaration).
* Generate C# classes for protocols and schemas.
* Check whether two Avro schemas are compatible under Avro's schema-evolution rules (`compat`).
* Print a semantic, field-level diff between two Avro schema versions (`diff`).
* Print the Parsing Canonical Form and fingerprint of a schema (`canonical`, `fingerprint`).
* Inspect Avro object container files: print the embedded writer schema or decode records to JSON (`getschema`, `tojson`).
* Compile every Avro logical type from IDL: `date`, `time_ms`, `timestamp_ms`,
  `local_timestamp_ms`, `uuid` and `decimal` have a dedicated keyword, and the rest are
  written as a `@logicalType` annotation on the type that backs them (see
  [Logical types in IDL](#logical-types-in-idl)).

## Installation

Install as a [.NET tool](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install):

```bash
dotnet tool install --global SJP.AvroTool
```

## Usage

Most of the documentation is provided by the tool itself (outside of the language specifications).

```plain
$ avrotool --help

USAGE:
    avrotool [OPTIONS] <COMMAND>

EXAMPLES:
    avrotool completions bash

OPTIONS:
    -h, --help       Prints help information
    -v, --version    Prints version information

COMMANDS:
    idl                    Generates a JSON protocol file from an Avro IDL file
    idl2schemata           Extract JSON schemata of the types from an Avro IDL
                           file
    codegen                Generates C# code for a given Avro IDL, protocol or
                           schema
    compat                 Checks whether two Avro schemas are compatible under
                           Avro's schema-evolution rules
    diff                   Prints a semantic diff between two Avro schemas
    canonical              Prints the Parsing Canonical Form of an Avro IDL,
                           protocol or schema
    fingerprint            Computes a fingerprint (crc-64-avro, md5 or sha-256)
                           of an Avro IDL, protocol or schema
    getschema              Prints the writer schema embedded in an Avro object
                           container file
    tojson                 Decodes an Avro object container file's records to
                           JSON
    completions <SHELL>    Generates a shell completion script (bash, zsh, fish,
                           powershell)
```

Every schema-consuming command can read an input from standard input instead of
a file (see [Standard input and output](#standard-input-and-output)).

### Examples

#### Compile IDL to an Avro Protocol

```plain
$ cat sample.avdl
protocol TestProtocol {
  record TestRecord {
    string FirstName;
    string LastName;
  }

  void Ping();
}
$ avrotool idl sample.avdl
Generated /home/sjp/repos/AvroTools/TestProtocol.avpr
$ cat TestProtocol.avpr
{
  "protocol": "TestProtocol",
  "types": [
    {
      "type": "record",
      "name": "TestRecord",
      "fields": [
        {
          "name": "FirstName",
          "type": "string"
        },
        {
          "name": "LastName",
          "type": "string"
        }
      ]
    }
  ],
  "messages": {
    "Ping": {
      "request": [],
      "response": "null"
    }
  }
}
```

#### Compile IDL to Avro Schema

```plain
$ cat sample.avdl
protocol TestProtocol {
  record TestRecord {
    string FirstName;
    string LastName;
  }

  enum TestEnum {
    A,
    B,
    C
  }

  void Ping();
}
$ avrotool idl2schemata sample.avdl
Generated /home/sjp/repos/AvroTools/TestRecord.avsc
Generated /home/sjp/repos/AvroTools/TestEnum.avsc

$ cat TestRecord.avsc
{
  "type": "record",
  "name": "TestRecord",
  "fields": [
    {
      "name": "FirstName",
      "type": "string"
    },
    {
      "name": "LastName",
      "type": "string"
    }
  ]
}

$ cat TestEnum.avsc
{
  "type": "enum",
  "name": "TestEnum",
  "symbols": [
    "A",
    "B",
    "C"
  ]
}
```

#### Logical types in IDL

Five logical types have a dedicated IDL keyword, and `decimal` has a keyword taking
its precision and scale. Each is written wherever a type is expected:

| IDL keyword | Compiled schema |
|-------------|-----------------|
| `date` | `{ "type": "int", "logicalType": "date" }` |
| `time_ms` | `{ "type": "int", "logicalType": "time-millis" }` |
| `timestamp_ms` | `{ "type": "long", "logicalType": "timestamp-millis" }` |
| `local_timestamp_ms` | `{ "type": "long", "logicalType": "local-timestamp-millis" }` |
| `uuid` | `{ "type": "string", "logicalType": "uuid" }` |
| `decimal(precision)` | `{ "type": "bytes", "logicalType": "decimal", "precision": 10 }` |
| `decimal(precision, scale)` | `{ "type": "bytes", "logicalType": "decimal", "precision": 10, "scale": 2 }` |

Every other logical type — `time-micros`, `timestamp-micros`,
`local-timestamp-micros`, `duration`, or one specific to your own tooling — has no
keyword. It is written as a `@logicalType` annotation on the Avro type that backs it,
which the compiler passes through onto the generated schema:

```plain
$ cat job.avdl
record Job {
  uuid jobid;
  @logicalType("timestamp-micros")
  long finishTime;
}
$ avrotool idl2schemata job.avdl
Generated /home/sjp/repos/AvroTools/Job.avsc
$ cat Job.avsc
{
  "type": "record",
  "name": "Job",
  "fields": [
    {
      "name": "jobid",
      "type": {
        "type": "string",
        "logicalType": "uuid"
      }
    },
    {
      "name": "finishTime",
      "type": {
        "type": "long",
        "logicalType": "timestamp-micros"
      }
    }
  ]
}
```

Choosing the backing type is up to you: the annotation is passed through verbatim and
is not checked against the Avro specification. Pairing a logical type with the
representation the specification gives it — `long` for `timestamp-micros`, a 12-byte
`fixed` for `duration` — is what makes the result readable by other Avro
implementations.

#### Generate C# code for Avro Protocol and Schema


```sh
$ cat sample.avdl
protocol TestProtocol {
  record TestRecord {
    string FirstName;
    string LastName;
  }

  void Ping();
}

$ avrotool codegen sample.avdl --namespace Test.Code.Namespace
Generated /home/sjp/repos/AvroTools/TestProtocol.cs
Generated /home/sjp/repos/AvroTools/TestRecord.cs

// Contents of files omitted for brevity
```

> The base namespace is supplied with `--namespace` (`-n`); it is only used for
> types that do not declare their own namespace. Input whose every type — and
> whose protocol, when one is generated — declares a namespace needs no
> `--namespace` at all. Omitting it for input that does contain a namespace-less
> type is reported up front, naming the types that need one, and that input is
> skipped. Each dot-separated part of the namespace must be a valid C#
> identifier; a part that is a C# keyword has to be escaped, as in
> `--namespace @class.Models`.

A namespace declared by the input itself has to satisfy the same rule, as does a
protocol's own name. Avro constrains neither — `"namespace": "my-ns"` and
`"protocol": "my-service"` are both accepted by an Avro parser — while generated
code spells each name exactly as the input does. A name C# has no way to write is
reported up front, alongside the input it was found in, and that input is skipped
rather than producing source that does not compile. Every other declared name is a
legal Avro name, which is always a legal C# identifier.

Each named type (record, error, enum, fixed) and each protocol is written to
`<fullname>.cs` — its Avro namespace and name joined with a dot, so
`TestRecord` above becomes `org.foo.TestRecord.cs` once it declares
`namespace org.foo`. A type or protocol without its own namespace is written
to `<name>.cs`, even when `--namespace` supplies one for the generated C#
code.

Avro records and protocols are generated as C# `record`s, with unconditional
nullable (`T?`) annotations for optional (`["null", ...]`) fields. Every
generated file opens with `#nullable enable`, so those annotations say what they
mean whether or not the project the file is compiled into has switched nullable
reference types on. Avro `fixed` and `error` types are generated as `class`es
instead: they derive from the `SpecificFixed` and `SpecificException` base
classes in `Apache.Avro`, and a C# record may only inherit from `object` or
another record. Avro enums are generated as C# `enum`s whose members keep the
schema's symbol order and carry the ordinal that order implies, which is the
value Avro encodes on the wire; a schema-declared `default` does not change that
order, because it is applied by schema resolution when a writer uses a symbol the
reader does not know. Two further output styles are opt-in via flags on
`codegen`:

| Option | Effect |
|--------|--------|
| `--required` | Marks properties that have no Avro-declared default and aren't a nullable union with the `required` modifier. |
| `--init-only` | Generates `init`-only properties instead of settable ones. A private backing field is used internally so `ISpecificRecord.Put` can still populate the instance after construction — deserialization is unaffected. |

```sh
$ avrotool codegen sample.avsc --namespace Test.Code.Namespace --required --init-only
```

```csharp
public required string FirstName { get => _FirstName; init => _FirstName = value; }
```

> `required` is a compile-time-only check tied to `new T()` syntax; it has no
> effect on `Activator.CreateInstance`-based deserialization, which is what
> `Apache.Avro` uses. Both flags default to off, so existing output is
> unchanged unless you opt in.

Each Avro type maps onto a C# type as follows:

| Avro type | C# type |
|-----------|---------|
| `boolean` | `bool` |
| `int` | `int` |
| `long` | `long` |
| `float` | `float` |
| `double` | `double` |
| `string` | `string` |
| `bytes` | `byte[]` |
| `null` | `object` |
| `enum` | the generated `enum` |
| `record`, `error`, `fixed` | the generated type |
| `array` | `IList<T>` |
| `map` | `IDictionary<string, T>` |
| `["null", T]` | the mapping of `T`, annotated nullable (`T?`) |
| any other union | `object`, annotated nullable (`object?`) when it has a `null` branch |

An `array` is typed by its interface rather than by `List<T>`. `Apache.Avro` builds
the container for an array nested inside another array, a map or a union out of the
element's interface type — a `List<IList<T>>` or a `Dictionary<string, IList<T>>` —
and generic collections are invariant, so a member typed `List<List<T>>` could be
cast neither to nor from what the runtime hands over. A `List<T>` still satisfies an
`IList<T>` member, so records are constructed the same way as before.

Logical types map onto their natural C# counterparts, whatever type backs them:

| Logical type | C# type |
|--------------|---------|
| `uuid` | `Guid` |
| `date`, `timestamp-millis`, `timestamp-micros`, `local-timestamp-millis`, `local-timestamp-micros` | `DateTime` |
| `time-millis`, `time-micros` | `TimeSpan` |
| `decimal` | `decimal` (`AvroDecimal` where a `decimal` cannot carry the value, see below) |

`Apache.Avro` implements exactly those logical types and hands the value of any other
one — `duration`, a `*-nanos` variant, or one specific to your own tooling — through
untouched. Such a field is generated as the type backing it, which is what the
runtime writes and reads: a `duration` becomes the generated 12-byte `fixed`, and a
`{ "type": "long", "logicalType": "my-thing" }` becomes a `long`. A `decimal` that
omits `scale` is generated with a scale of `0`, as the Avro specification requires.

Because `Apache.Avro` exchanges decimal values as `AvroDecimal`, the generated
`Get` and `Put` convert them. That conversion applies to a decimal field and to
an optional (`["null", ...]`) decimal field, which are exposed as `decimal` and
`decimal?` respectively. Decimals nested inside an array or a map are not
converted element by element: those properties are typed
`IList<AvroDecimal>` and `IDictionary<string, AvroDecimal>` and are handed to
Avro as-is.

Avro writes a decimal at exactly the scale its schema declares, so `Get` pads a
value that has fewer decimal places out to that scale. A value that has more of
them than the schema stores is not written at all: rounding it would drop digits
with nothing to say so, so `Get` throws an `AvroTypeException` naming the field.

A C# `decimal` holds at most 28 decimal places, while Avro admits any scale up
to a decimal's precision. A field whose scale is wider than that is typed
`AvroDecimal` and exchanged unconverted, the same as one inside a collection.

A protocol message carries every value in the representation `Apache.Avro` uses,
because the requestor packs the arguments and unpacks the response with no
generated code in between to convert them. A `decimal` parameter or response is
therefore typed `AvroDecimal`, and an optional one `AvroDecimal?`.

A message that declares `errors` carries an `<exception>` documentation tag for
each of them on its generated method, so the error types a caller has to handle
are visible from the signature. The generated `Request` dispatch throws an
`AvroRuntimeException` naming any message the protocol does not declare, rather
than returning without requesting anything and leaving the caller waiting on a
response that never comes.

A logical type may be backed by a named `fixed` rather than a primitive — a
`duration`, or a `decimal` stored in a `fixed`. The named type is generated
alongside the record that uses it, and `idl2schemata` writes an `.avsc` for it
the same as for any other named type. A `duration` field is typed as that
generated `fixed` and round trips through the specific API as the raw 12 bytes.

A `decimal` stored in a `fixed` is refused, and the input it appears in is
reported as a failure. `Apache.Avro` exchanges such a value as a generic fixed,
which its specific writer rejects and its specific reader cannot hand to a
generated class, so no property type could carry it; storing the decimal in
`bytes` works throughout. Note that the `decimal` itself is what is unsupported —
a plain `fixed`, and any other logical type over one, are generated as usual.

The schema each generated type embeds is written the way the Avro specification
defines a logical type, with `logicalType` and its attributes on the type they
apply to. `Apache.Avro` writes a logical type over a named type as a wrapper
around it — `{ "type": { "type": "fixed", ... }, "logicalType": "duration" }` —
which most Avro implementations cannot parse, so that form is never emitted.

Avro names admit every C# keyword, so a name that is one is emitted verbatim with
an `@` prefix (`@class`, `@event`, `@void`). The prefix is purely lexical: the
generated member still carries the Avro name, and field positions are unchanged.

C# also forbids a member from sharing its name with the type that declares it, or
with another member of that type, and a member that shares its name with an
inherited one hides it. A field or message whose name collides is given an
underscore suffix. Collisions come from the type it is declared in (its own record
or protocol), the members generated alongside it (`Schema`, `Get`, `Put`,
`Protocol`, `Request`), the members every type inherits from `object` (`Equals`,
`GetHashCode`, `ToString`, `GetType` and the rest), the members the compiler
writes into a C# `record` (`Clone`, `EqualityContract`, `PrintMembers`), and, on
an error type, the members inherited from `Exception` (`Message`, `Data`,
`Source`, `HResult`, `StackTrace` and the rest):

```csharp
public record Foo : global::Avro.Specific.ISpecificRecord
{
    public int Foo_ { get; set; }       // Avro field 'Foo'
    public string Message_ { get; set; } // Avro field 'Message'
}
```

The Avro name is untouched: it stays in the embedded schema and in the
`Get`/`Put` mapping, so the wire format is unaffected.

A record or protocol named after one of the members its interface obliges it to
carry — a record named `Schema`, `Get` or `Put`, or a protocol named `Protocol`
or `Request` — keeps its Avro name and implements that one member explicitly, so
it is reached through `ISpecificRecord` or `ISpecificProtocol` rather than on the
type itself. The type name is never changed, because Avro resolves a generated
type by the name its schema gave it whenever it reads a value nested inside
another.

An error type named `Schema`, `Get` or `Put`, or a `fixed` type named `Schema`,
is refused, and the input it appears in is reported as a failure. Those types
derive from `SpecificException` and `SpecificFixed`, whose members are abstract
and so have to be overridden under exactly those names, which a type of the same
name cannot do. Renaming the type in the schema is the way out.

Generated files carry no `using` directives. Every type a generated file refers to
is named in full and rooted in the global namespace — `global::Avro.Specific.ISpecificRecord`,
`global::System.Collections.Generic.IList<T>`, and likewise for types generated from
other Avro namespaces. Avro names are unconstrained, so a schema may declare two types
of the same name in different namespaces, or a field named `AvroDecimal` or `Math`; a
full name binds to the type that was meant whatever else the schema happens to name.

#### Canonical form and fingerprints

`avrotool canonical` prints the [Parsing Canonical Form](https://avro.apache.org/docs/current/specification/#parsing-canonical-form-for-schemas)
of a schema — the normalised form that strips `doc`, `aliases`, defaults and
other non-structural attributes and fully-qualifies names, so two structurally
identical schemas compare equal regardless of formatting.

```plain
$ avrotool canonical Person.avsc
{"name":"ns.Person","type":"record","fields":[{"name":"Name","type":"string"},{"name":"Age","type":"int"}]}
```

`avrotool fingerprint` computes a fingerprint over that canonical form — the same
value the wider Avro ecosystem uses for single-object encoding and registry
lookups. The default algorithm is `crc-64-avro` (the Rabin fingerprint, also
accepted as `crc64` or `rabin`), with `md5` and `sha-256` also available via
`-a`/`--algorithm`. The output format defaults to lowercase hex; `-f`/`--format`
also accepts `base64`, and `long` (crc-64-avro only), as a signed 64-bit integer.

```plain
$ avrotool fingerprint Person.avsc                       # crc-64-avro, lowercase hex
b0e15e3c5393d356
$ avrotool fingerprint Person.avsc --format long         # crc-64-avro as a signed 64-bit integer
6256506293052170672
$ avrotool fingerprint Person.avsc --format base64       # crc-64-avro, base64
sOFePFOT01Y=
$ avrotool fingerprint Person.avsc --algorithm sha-256
dfcf26207b59396b32b55e6269a8413e2ba78708cef6d7370a489c84ae151009
```

Both commands accept an IDL, protocol or schema as input (and `--stdin`). When
given a protocol, they emit one line per named type; if there is more than one,
`fingerprint` labels each line with the type's full name (`<fingerprint>  <name>`).
A protocol with a single named type prints a bare fingerprint, since there is
nothing to disambiguate.

#### Compatibility checking

`avrotool compat <READER> <WRITER>` checks whether data written with the
`<WRITER>` schema can be read with the `<READER>` schema under Avro's
schema-evolution rules, and reports every incompatibility it finds.

```plain
$ avrotool compat v2.avsc v1.avsc
COMPATIBLE (backward) reader 'v2.avsc' can read writer 'v1.avsc'
Schemas are compatible.
```

The default mode is `backward`; `-m`/`--mode` also accepts `forward`, `full`, and
their `-transitive` variants, which take a candidate schema followed by every
earlier version to check it against. `--json` emits a machine-readable list of
incompatibilities (kind, location and message) instead of the summary above.
Both forms of the report go to standard output, so either can be redirected to
a file or piped onwards. Types are named the way a schema document names them —
`int`, `enum`, `string` — and a named type the reader has no branch for is
reported by its full name.

`compat` slots directly into CI as a pre-merge gate: it exits `0` when the
schemas are compatible, `1` when they are not, and `2` when it could not reach
an answer at all — a missing file, a schema that will not parse, an unknown
`--mode`. A gate can therefore fail a build for a broken schema without also
passing an unreadable one off as a breakage.

One of the schemas may come from standard input, which avoids a temporary file
when the other version lives in version control rather than on disk:

```sh
# Check that the working tree's schema can still read data written with main's
git show main:schema.avsc | avrotool compat --stdin --stdin-as 2 schema.avsc
```

`--stdin-as` gives the 1-based position that standard input occupies among the
schemas, defaulting to `1` — the reader, or the candidate in the `-transitive`
modes. The remaining positional arguments fill the other positions in order, so
one fewer of them is given.

#### Schema diff

`avrotool diff <SCHEMA_A> <SCHEMA_B>` prints a semantic, field-level diff
between two versions of a schema — the "what changed" complement to `compat`'s
"is this safe".

```plain
$ avrotool diff v1.avsc v2.avsc
FIELD_ADDED at /fields/Email: field added with a default value
Schemas differ (1 change(s)).
```

`--json` emits the same information as a list of typed change records for
tooling or PR comments:

```plain
$ avrotool diff v1.avsc v2.avsc --json
{
  "identical": false,
  "changes": [
    {
      "kind": "FIELD_ADDED",
      "location": "/fields/Email",
      "message": "field added with a default value",
      "oldValue": null,
      "newValue": null,
      "isValidPromotion": null
    }
  ]
}
```

Reordering fields doesn't count as a change: fields are matched between the two
schemas by name (or by alias, to catch a rename), not by position, the same
way Avro's schema resolution matches a reader's fields against a writer's. A
reordered field's own Parsing Canonical Form and binary encoding do change —
canonical form preserves field order and only strips non-structural attributes
— but data written with either field order still reads back the same way, so
`diff` treats the two as equivalent. Type changes, default changes, renames
(detected via `aliases`), and enum/fixed/union shape changes are all reported. A
union branch that is a named type renamed through an `aliases` link is matched to
the branch it replaces, so the rename and whatever changed inside that branch are
reported rather than one branch removed and an unrelated one added. A
type change also carries `isValidPromotion`, which says whether a reader using
the new type can still read data written with the old one under Avro's type
promotion rules — `int` to `long` is a valid promotion, `long` to `int` is not.
It is `null` for changes that aren't type changes. A change's message, values and
location name a type the way a schema document does — `int`, `enum` — and a named
type by its full name, which is also the segment a union branch contributes to a
location.
Logical types count too: adding, removing or replacing a `logicalType`, or
changing a decimal's `precision` or `scale`, is reported even though the
underlying representation is unchanged, because it changes how the data is
interpreted. A `record` in one version and a protocol `error` in the other hold
the same fields and read each other's data, so the two are compared field by
field rather than reported as a wholesale type change. Pass `--verbose` to also
report metadata changes that don't affect the schema's shape: `doc`, `aliases`,
an enum's default symbol, and a `record`/`error` declaration that changed.

As with `git diff`, the diff itself is the payload, so both forms of it go to
standard output and can be redirected or piped:

```sh
avrotool diff v1.avsc v2.avsc > changes.txt
```

The exit codes follow `diff(1)` too, which makes `diff` a CI "did the schema
change?" gate: `0` when the schemas are resolution-equivalent (identical aside
from field order), `1` when they differ, and `2` when the comparison could not
be made — a missing file, or a schema that will not parse.

Like `compat`, each of `<SCHEMA_A>` and `<SCHEMA_B>` must resolve to a single
schema — a protocol with more than one named type is rejected with a clear
error. `diff` also accepts `--stdin` with the same `--stdin-as 1|2` positioning
as `compat`:

```sh
# Show what the working tree changed relative to main
git show main:schema.avsc | avrotool diff --stdin schema.avsc
```

#### Inspecting Avro data files

`avrotool getschema` and `avrotool tojson` read an Avro **object container
file** (`.avro`) rather than a schema/IDL definition.

```plain
$ avrotool getschema people.avro --pretty
{
  "type": "record",
  "name": "Person",
  "namespace": "ns",
  "fields": [
    {
      "name": "Name",
      "type": "string"
    },
    {
      "name": "Age",
      "type": "int"
    },
    {
      "name": "Email",
      "default": "",
      "type": "string"
    }
  ]
}

$ avrotool tojson people.avro
{"Name":"Alice","Age":30,"Email":"alice@example.com"}
{"Name":"Bob","Age":25,"Email":""}
```

`getschema` prints the writer schema embedded in the file's header;
`tojson` decodes every record to JSON Lines (one record per line). Both accept
`--pretty` for indented output and `--stdin` to read the container file from
standard input instead of a path.

Both commands read the file's compression codec to decode its blocks.
Apache.Avro 1.12.2 (the library this tool is built on) implements the `null`
and `deflate` codecs; a container compressed with `snappy`, `bzip2`,
`zstandard` or `xz` is reported as unreadable, naming the codec responsible:

```plain
$ avrotool getschema snappy-compressed.avro
Unable to read 'snappy-compressed.avro' as an Avro object container file.
    The container uses the 'snappy' codec; only 'null' and 'deflate' are supported.
```

### Imports

An IDL document's `import idl`/`import protocol`/`import schema` statements are
resolved **relative to the directory of the file containing the import**, at every
level of nesting, matching the reference Avro IDL compiler. So a tree such as

```plain
schemas/
  main.avdl        # import idl "common/ids.avdl";
  common/
    ids.avdl       # import schema "uuid.avsc";
    uuid.avsc
```

compiles from anywhere:

```sh
avrotool idl schemas/main.avdl
```

Absolute import paths are used as given. When the document is read from standard
input there is no containing file, so imports resolve against the current working
directory. The same file reached by two different spellings (`ids.avdl` and
`./ids.avdl`, say) is recognised as one import rather than being pulled in twice.

### Standard input and output

The `idl`, `idl2schemata`, `codegen`, `canonical`, `fingerprint`, `compat`,
`diff`, `getschema` and `tojson` commands can participate in shell pipelines
rather than only reading and writing files on disk.

- **Reading from standard input:** pass `--stdin` to read the IDL, protocol or
  schema from standard input instead of a file. The `IDL_FILES`/`INPUT_FILES`
  argument is then omitted; naming files as well is rejected rather than one of
  the two inputs being silently ignored. For `codegen`, supply the base
  namespace with `--namespace` (`-n`) unless the input is fully namespaced.
- **Two-schema commands:** `compat` and `diff` take more than one schema, so
  only one of them may come from standard input. `--stdin-as` picks which, as a
  1-based position among the schemas (default `1`); the positional arguments
  fill the rest in order. Output identifies that schema as `<stdin>`.
- **Writing to standard output:** the `idl` command accepts `--stdout` (`-s`) to
  write the generated JSON to standard output instead of a file. The reports
  from `compat` and `diff` are payloads in the same sense, so they go there too,
  in both their human-readable and `--json` forms.
- **Clean pipelines:** all human-facing status messages (the green
  `Generated ...` lines and any errors) are written to **standard error**, so
  standard output carries only the payload. An unusable command line — an
  unknown command, an argument that cannot be converted, a missing or
  non-existent input — is reported there as a single message, and the tool
  exits with code `1`. `--help` and `--version` are what was asked for rather
  than status, so they go to **standard output** and can be piped or redirected.
- **Exit codes:** `0` on success and `1` on failure, except for `compat` and
  `diff`, which answer a yes/no question about two schemas and follow the
  `diff(1)` convention instead: `0` for compatible/identical, `1` for
  incompatible/different, and `2` for a run that could not reach an answer.

```sh
# Compile IDL piped in, and print the JSON protocol to stdout
cat sample.avdl | avrotool idl --stdin --stdout

# Chain commands together: IDL -> protocol JSON -> generated C#
cat sample.avdl | avrotool idl --stdin --stdout \
  | avrotool codegen --stdin --namespace Test.Code.Namespace --output-dir ./generated

# Gate a pull request on the schema still being readable by the version on main
git show main:schema.avsc | avrotool compat --stdin --stdin-as 2 schema.avsc
```

> Note: a bare `-` is a common convention for "read from standard input", but the
> underlying command-line parser reserves a leading `-` for options, so this tool
> uses an explicit `--stdin` flag instead.

Multi-file commands (`idl2schemata` and `codegen`) still write their generated
files to disk; only their input can come from standard input.

### Multiple inputs, directories and glob patterns

The `idl`, `idl2schemata` and `codegen` commands accept more than one input in a
single invocation, so a whole tree of `*.avdl` / `*.avsc` / `*.avpr` files can be
processed at once instead of one command per file. Any mix of the following is
accepted for the input argument(s):

```sh
# Several explicit files
avrotool codegen a.avdl b.avdl --namespace My.Ns

# A directory — recognised files are found recursively by default
avrotool idl schemas/

# ...or only its top level
avrotool idl schemas/ --no-recursive

# A glob pattern (** descends into subdirectories)
avrotool codegen "schemas/**/*.avsc" --namespace My.Ns
```

Details:

- **Output naming** continues to derive from each schema/protocol's own name and
  the `--output-dir`, so many inputs can safely share one output directory.
  `-o`/`--overwrite` is checked per input: if any of an input's outputs already
  exists on disk, that whole input is refused and none of its outputs are
  written, including ones that don't exist yet.
- **Output directory creation** — `--output-dir` (`-d`) is created if it does not
  already exist, including any missing parent directories, so a generated tree can
  be written straight into a fresh location.
- **Shared outputs** — a type shared through an import belongs to every document
  that imports it, so the same file produced more than once with identical
  content is written once and reported as already generated for the inputs that
  follow. Two outputs that would write *different* content to one path — whether
  from two inputs or from a single one, for example a protocol and a type of the
  same name — are reported as a conflict instead of silently racing.
- **Per-file reporting** — a failure in one input does not abort the rest; the
  exit code is non-zero if *any* input failed. Pass `--fail-fast` to stop on the
  first failure instead.
- Because `codegen` now takes a variable number of input files, its base
  namespace is supplied with `--namespace` (`-n`) rather than as a trailing
  positional argument.
- Directory and glob expansion only pick up recognised extensions (`.avdl` for
  `idl`/`idl2schemata`; `.avdl`, `.avpr`, `.avsc` for `codegen`); an explicitly
  named file is always used regardless of its extension.

The remaining commands do not take a variable number of inputs. `canonical`,
`fingerprint`, `getschema` and `tojson` each accept exactly one file (or `--stdin`),
and `compat` and `diff` accept only the schemas they compare. A directory or a glob
pattern is not expanded for any of them.

### Shell completions

`avrotool completions <shell>` writes a completion script for the given shell
(`bash`, `zsh`, `fish` or `powershell`) to standard output. The scripts cover
every command and option, complete file arguments as paths, `--output-dir` as a
directory, and offer the accepted values for `--mode`, `--algorithm`, `--format`
and the `completions` shell argument. The zsh and fish scripts also show each
command's and option's description. The bash, zsh and fish scripts always use
line feeds, so redirecting them from a Windows shell still produces a script
those shells accept. Redirect the script to a location your shell loads
completions from:

```sh
# bash
avrotool completions bash > ~/.local/share/bash-completion/completions/avrotool

# zsh (a directory on your $fpath)
avrotool completions zsh > ~/.zsh/completions/_avrotool

# fish
avrotool completions fish > ~/.config/fish/completions/avrotool.fish

# PowerShell (add to your $PROFILE)
avrotool completions powershell | Out-String | Invoke-Expression
```
