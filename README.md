# Avro Tools

[![License (MIT)](https://img.shields.io/badge/license-MIT-blue.svg)](https://opensource.org/licenses/MIT) [![GitHub Actions](https://github.com/sjp/AvroTools/actions/workflows/ci.yml/badge.svg)](https://github.com/sjp/AvroTools/actions/workflows/ci.yml) [![Code coverage](https://img.shields.io/codecov/c/gh/sjp/AvroTools/master?logo=codecov)](https://codecov.io/gh/sjp/AvroTools)

A collection of tools to work with Apache Avro in C#.

## Description

The intention of this project is to provide a pure C# implementation of an [Avro IDL](https://avro.apache.org/docs/1.12.0/idl-language/) compiler. Additionally, although the [Avro GitHub](https://github.com/apache/avro) project does contain a code generator for C#, it contains rather verbose code. This project generates human-readable output via a Roslyn-based code generator.

One other benefit of this project is avoiding the pre-requisite for a Java runtime.

## Features

* Compile [Avro IDL](https://avro.apache.org/docs/1.12.0/idl-language/) to an [Avro Protocol](https://avro.apache.org/docs/1.12.0/specification/#protocol-declaration).
* Compile [Avro IDL](https://avro.apache.org/docs/1.12.0/idl-language/) to [Avro Schema](https://avro.apache.org/docs/1.12.0/specification/#schema-declaration).
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

Install as a [.NET tool](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install):

```bash
dotnet tool install --global SJP.AvroTool
```

The same functionality is available to call from code, as two libraries the tool is built
on (see [Using the libraries](#using-the-libraries)):

```bash
dotnet add package SJP.Avro.Tools
dotnet add package SJP.Avro.Tools.CodeGen
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

Every other logical type such as `time-micros`, `timestamp-micros`,
`local-timestamp-micros`, `duration`, or one specific to your own tooling, has no
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

Avro records and protocols are generated as C# `record`s, with unconditional
nullable (`T?`) annotations for optional (`["null", ...]`) fields. Every
generated file opens with `#nullable enable`. Avro `fixed` and `error` types
are generated as `class`es
instead: deriving from the `SpecificFixed` and `SpecificException` base
classes in `Apache.Avro`, whereas a C# record may only inherit from `object` or
another record. Avro enums are generated as C# `enum`s whose members keep the
schema's symbol order and carry the ordinal that order implies, which is the
value Avro encodes on the wire. Two further output styles are opt-in via flags on
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

Logical types map onto their natural C# counterparts, whatever type backs them:

| Logical type | C# type |
|--------------|---------|
| `uuid` | `Guid` |
| `date`, `timestamp-millis`, `timestamp-micros`, `local-timestamp-millis`, `local-timestamp-micros` | `DateTime` |
| `time-millis`, `time-micros` | `TimeSpan` |
| `decimal` | `decimal` (`AvroDecimal` where a `decimal` cannot carry the value, see below) |

#### Canonical form and fingerprints

`avrotool canonical` prints the [Parsing Canonical Form](https://avro.apache.org/docs/1.12.0/specification/#parsing-canonical-form-for-schemas)
of a schema, the normalised form that strips `doc`, `aliases`, defaults and
other non-structural attributes and fully-qualifies names, so two structurally
identical schemas compare equal regardless of formatting.

```plain
$ avrotool canonical Person.avsc
{"name":"ns.Person","type":"record","fields":[{"name":"Name","type":"string"},{"name":"Age","type":"int"}]}
```

`avrotool fingerprint` computes a fingerprint over that canonical form; the same
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

`compat` slots directly into CI as a pre-merge gate: it exits `0` when the
schemas are compatible, `1` when they are not, and `2` when it could not reach
an answer at all.

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
way Avro's schema resolution matches a reader's fields against a writer's.

As with `git diff`, the diff itself is the payload, so both forms of it go to
standard output and can be redirected or piped:

```sh
avrotool diff v1.avsc v2.avsc > changes.txt
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

### Standard input and output

The `idl`, `idl2schemata`, `codegen`, `canonical`, `fingerprint`, `compat`,
`diff`, `getschema` and `tojson` commands can participate in shell pipelines
rather than only reading and writing files on disk.

- **Reading from standard input:** pass `--stdin` to read the IDL, protocol or
  schema from standard input instead of a file.
- **Two-schema commands:** `compat` and `diff` take more than one schema, so
  only one of them may come from standard input. `--stdin-as` picks which, as a
  1-based position among the schemas (default `1`); the positional arguments
  fill the rest in order.
- **Writing to standard output:** the `idl` command accepts `--stdout` (`-s`) to
  write the generated JSON to standard output instead of a file. The reports
  from `compat` and `diff` are payloads in the same sense.
- **Clean pipelines:** all human-facing status messages (the green
  `Generated ...` lines and any errors) are written to **standard error**, so
  standard output carries only the payload.
- **Exit codes:** `0` on success and `1` on failure.

```sh
# Compile IDL piped in, and print the JSON protocol to stdout
cat sample.avdl | avrotool idl --stdin --stdout

# Chain commands together: IDL -> protocol JSON -> generated C#
cat sample.avdl | avrotool idl --stdin --stdout \
  | avrotool codegen --stdin --namespace Test.Code.Namespace --output-dir ./generated

# Gate a pull request on the schema still being readable by the version on main
git show main:schema.avsc | avrotool compat --stdin --stdin-as 2 schema.avsc
```

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

## Using the libraries

Everything the tool does is available to call directly, from two packages that target
.NET 10:

| Package | Contents |
|---------|----------|
| [`SJP.Avro.Tools`](https://www.nuget.org/packages/SJP.Avro.Tools) | The IDL compiler (`IdlToAvroTranslator`), the compatibility checker (`SchemaCompatibility`), the schema diff (`SchemaDiff`) and the JSON encoder (`AvroJsonWriter`). |
| [`SJP.Avro.Tools.CodeGen`](https://www.nuget.org/packages/SJP.Avro.Tools.CodeGen) | The C# generators for records, errors, enums, fixed types and protocols, reached through `CodeGeneratorResolver`. |

Both speak the `Avro.Schema` and `Avro.Protocol` types from
[Apache.Avro](https://www.nuget.org/packages/Apache.Avro), so a schema compiled from IDL
can be handed straight to the code generator, or to that library's readers and writers.

## License

MIT, as set out in [LICENSE](https://github.com/sjp/AvroTools/blob/master/LICENSE).

The Avro IDL grammar, and the lexer and parser generated from it, come from Apache Avro
and are used under the Apache License 2.0. That licence and the accompanying attribution
are reproduced in
[THIRD-PARTY-NOTICES.md](https://github.com/sjp/AvroTools/blob/master/THIRD-PARTY-NOTICES.md),
which also ships in the packages.
