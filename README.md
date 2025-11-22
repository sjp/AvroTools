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
* Supports additional logical types compared to reference compiler. Note that these may not be usable in practice but can be compiled to compatible Avro Protocol/Schema. The following additional logical types are supported in IDL:
  * `uuid`
  * `time-micros`
  * `timestamp-micros`
  * `local-timestamp-ms`
  * `local-timestamp-micros`
  * `duration`

## Usage

The intention is for this to be installable as a [`dotnet tool`](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install), but for the moment usage is possible via `dotnet run`.

Most of the documentation is provided by the tool itself (outside of the language specifications).

```plain
$ dotnet run --project src/AvroTool

USAGE:
    avrotool [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help       Prints help information
    -v, --version    Prints version information

COMMANDS:
    idl <IDL_FILE>                      Generates a JSON protocol file from an Avro IDL file
    idl2schemata <IDL_FILE>             Extract JSON schemata of the types from an Avro IDL file
    codegen <INPUT_FILE> <NAMESPACE>    Generates C# code for a given Avro IDL, protocol or schem
```

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
$ dotnet run --project src/AvroTool idl sample.avdl
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
$ dotnet run --project src/AvroTool idl2schemata sample.avdl
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

$ dotnet run --project src/AvroTool codegen sample.avdl Test.Code.Namespace
Generated /home/sjp/repos/AvroTools/TestProtocol.cs
Generated /home/sjp/repos/AvroTools/TestRecord.cs

// Contents of files omitted for brevity
```