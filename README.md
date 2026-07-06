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
* Supports additional logical types compared to reference compiler. Note that these may not be usable in practice but can be compiled to compatible Avro Protocol/Schema. The following additional logical types are supported in IDL:
  * `uuid`
  * `time-micros`
  * `timestamp-micros`
  * `local-timestamp-ms`
  * `local-timestamp-micros`
  * `duration`

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

OPTIONS:
    -h, --help       Prints help information
    -v, --version    Prints version information

COMMANDS:
    idl                           Generates a JSON protocol file from an Avro IDL file
    idl2schemata                  Extract JSON schemata of the types from an Avro IDL file
    codegen                       Generates C# code for a given Avro IDL, protocol or schema
    compat <SCHEMAS>              Checks whether two Avro schemas are compatible under Avro's schema-evolution rules
    diff <SCHEMA_A> <SCHEMA_B>    Prints a semantic diff between two Avro schemas
    canonical                     Prints the Parsing Canonical Form of an Avro IDL, protocol or schema
    fingerprint                   Computes a fingerprint (crc-64-avro, md5 or sha-256) of an Avro IDL, protocol or schema
    getschema                     Prints the writer schema embedded in an Avro object container file
    tojson                        Decodes an Avro object container file's records to JSON
    completions <SHELL>           Generates a shell completion script (bash, zsh, fish, powershell)
```

Each of the `idl`, `idl2schemata`, `codegen`, `getschema` and `tojson` commands
can read its input from standard input instead of a file (see
[Standard input and output](#standard-input-and-output)).

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
      "fields": [
        {
          "name": "FirstName",
          "type": "string"
        },
        {
          "name": "LastName",
          "type": "string"
        }
      ],
      "name": "TestRecord"
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
> types that do not declare their own namespace.

Generated types are already `record`s with unconditional nullable (`T?`)
annotations for optional (`["null", ...]`) fields. Two further output styles
are opt-in via flags on `codegen`:

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
lookups. The default algorithm is `crc-64-avro` (the Rabin fingerprint), with
`md5` and `sha-256` also available.

```plain
$ avrotool fingerprint Person.avsc                       # crc-64-avro, lowercase hex
b0e15e3c5393d356
$ avrotool fingerprint Person.avsc --format long         # crc-64-avro as a signed 64-bit integer
6256506293052170672
$ avrotool fingerprint Person.avsc --algorithm sha-256
dfcf26207b59396b32b55e6269a8413e2ba78708cef6d7370a489c84ae151009
```

Both commands accept an IDL, protocol or schema as input (and `--stdin`). When
given a protocol, they emit one line per named type; `fingerprint` labels each
line with the type's full name (`<fingerprint>  <name>`).

#### Compatibility checking

`avrotool compat <READER> <WRITER>` checks whether data written with the
`<WRITER>` schema can be read with the `<READER>` schema under Avro's
schema-evolution rules, and reports every incompatibility it finds.

```plain
$ avrotool compat v2.avsc v1.avsc
COMPATIBLE (backward) reader 'v2.avsc' can read writer 'v1.avsc'
Schemas are compatible.
```

The default mode is `backward`; `--mode` also accepts `forward`, `full`, and
their `-transitive` variants, which take a candidate schema followed by every
earlier version to check it against. `--json` emits a machine-readable list of
incompatibilities (kind, location and message) instead of the summary above.
Exit code `0` means compatible, so `compat` slots directly into CI as a
pre-merge gate.

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

Reordering fields doesn't count as a change (it mirrors canonical-form
thinking), but type changes, default changes, renames (detected via
`aliases`), and enum/fixed/union shape changes are all reported. Pass
`--verbose` to also report `doc`/`aliases` metadata changes that don't affect
the schema's shape. Exit code `0` means the schemas are identical, so `diff`
also works as a CI "did the schema change?" gate.

Like `compat`, each of `<SCHEMA_A>` and `<SCHEMA_B>` must resolve to a single
schema — a protocol with more than one named type is rejected with a clear
error.

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
    { "name": "Name", "type": "string" },
    { "name": "Age", "type": "int" },
    { "name": "Email", "default": "", "type": "string" }
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

### Standard input and output

The `idl`, `idl2schemata`, `codegen`, `getschema` and `tojson` commands can
participate in shell pipelines rather than only reading and writing files on
disk.

- **Reading from standard input:** pass `--stdin` to read the IDL, protocol or
  schema from standard input instead of a file. The `IDL_FILES`/`INPUT_FILES`
  argument is then omitted. For `codegen`, supply the base namespace with
  `--namespace` (`-n`).
- **Writing to standard output:** the `idl` command accepts `--stdout` (`-s`) to
  write the generated JSON to standard output instead of a file.
- **Clean pipelines:** all human-facing status messages (the green
  `Generated ...` lines and any errors) are written to **standard error**, so
  standard output carries only the payload.

```sh
# Compile IDL piped in, and print the JSON protocol to stdout
cat sample.avdl | avrotool idl --stdin --stdout

# Chain commands together: IDL -> protocol JSON -> generated C#
cat sample.avdl | avrotool idl --stdin --stdout \
  | avrotool codegen --stdin --namespace Test.Code.Namespace --output-dir ./generated
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
  the `--output-dir`, so many inputs can safely share one output directory. The
  existing `--overwrite` semantics are respected per file.
- **Duplicate outputs** — two inputs that would generate the same output file are
  detected and reported rather than silently racing.
- **Per-file reporting** — a failure in one input does not abort the rest; the
  exit code is non-zero if *any* input failed. Pass `--fail-fast` to stop on the
  first failure instead.
- Because `codegen` now takes a variable number of input files, its base
  namespace is supplied with `--namespace` (`-n`) rather than as a trailing
  positional argument.
- Directory and glob expansion only pick up recognised extensions (`.avdl` for
  `idl`/`idl2schemata`; `.avdl`, `.avpr`, `.avsc` for `codegen`); an explicitly
  named file is always used regardless of its extension.

### Shell completions

`avrotool completions <shell>` writes a completion script for the given shell
(`bash`, `zsh`, `fish` or `powershell`) to standard output. Redirect it to a
location your shell loads completions from:

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
