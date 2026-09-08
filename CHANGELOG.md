# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

A release is cut by tagging a commit `vX.Y.Z`. Before tagging, give the pending changes
their own version heading here — the release workflow reads the notes for the tagged
version out of this file and fails if there is no section for it.

## Unreleased

## 0.2.0

The first release since the tool grew from an IDL compiler into a general-purpose Avro
command-line tool. It adds seven commands, accepts many inputs at once, reads from
standard input, and fixes a long list of IDL translation and code generation defects.

### Added

- `compat` checks whether a schema can read data written with another, in the backward,
  forward, full and transitive modes, and reports the incompatibilities it finds.
- `diff` prints a semantic, field-level difference between two schemas, including logical
  type and decimal precision/scale changes, and exits with `diff(1)`-style codes.
- `canonical` and `fingerprint` produce the parsing canonical form of a schema and its
  Rabin, MD5 or SHA-256 fingerprint.
- `getschema` prints the writer's schema embedded in an Avro container file, and `tojson`
  prints its records as JSON.
- `completions` generates shell completion scripts for bash, zsh, fish and PowerShell.
- Commands accept several inputs at once, as explicit files, directories searched
  recursively, or glob patterns.
- `--stdin` reads the input from standard input, so commands can be chained in a pipeline.
- Code generation options for init-only properties, required members and the other
  generated-member styles.
- The tool targets .NET 10 and rolls forward to a later major runtime.

### Changed

- `--help` and `--version` are written to standard output rather than standard error.
- A command-line error is reported as a single message and exits non-zero, instead of
  terminating with an unhandled exception.
- Unknown options are rejected rather than silently ignored along with the argument that
  followed them.
- Status and error messages are no longer hard-wrapped at 80 columns when output is
  redirected.
- JSON output writes non-ASCII and HTML-sensitive characters literally instead of escaping
  them.
- Input and output are UTF-8 throughout, and a byte-order mark on standard input is
  stripped rather than being parsed as content.
- Ctrl+C cancels the running command and lets it finish writing before the process exits.
- Outputs are written atomically, the output directory is created when it does not exist,
  and a path written twice within one invocation is reported as a collision.
- An input that cannot be read is reported against that file and the remaining inputs are
  still processed, rather than aborting the run.
- Two inputs that import the same type can be processed in one invocation; the shared
  output is written once instead of failing as a duplicate.
- Positional inputs given alongside `--stdin` are rejected instead of silently ignored.
- `compat` and `diff` report their result on standard output.
- The reason an input failed to parse is reported inline rather than discarded.
- `tojson` buffers its output instead of flushing per record, and exits promptly when the
  reader closes the pipe.
- Completion scripts are generated with LF line endings, option descriptions and grouped
  zsh spellings.
- The tool runs with the invariant globalization mode, so it starts on an image that
  carries no ICU library and behaves the same whatever the machine's locale is.
- The package leaves out the localized resources of its dependencies, none of which the
  tool reads, and carries an icon.

### Fixed

- IDL output is no longer round-tripped through Apache.Avro's object model, which silently
  dropped field order, aliases and custom properties.
- `T? x = <non-null>` emits a union with the default's branch first, rather than an invalid
  schema.
- Annotations on optional (`T?`) and union types are attached to the branch they belong to
  instead of being lost.
- Messages imported from another protocol keep type references qualified against the
  protocol they came from.
- A self- or mutually-recursive record in a schema-syntax file without a `schema` statement
  no longer produces a duplicate definition.
- `import idl` of a schema-syntax file resolves that file's own imports, so transitive
  imports are no longer dropped.
- Bare type references inside a record with its own `@namespace` resolve against that
  namespace before falling back to the document's.
- Dotted declared names (`record a.b.C`, `protocol org.foo.Bar`) keep their namespace
  instead of producing duplicate definitions.
- An import cycle that returns to the root document no longer redeclares the root's types.
- An imported type that declares no namespace keeps its bare name instead of being moved
  into the importing protocol's namespace.
- Hexadecimal and octal literals at or above 2^63 are rejected rather than wrapping to a
  negative value, and the minimum `long` literal parses.
- Backtick-escaped identifiers are unescaped everywhere one is read, including namespace
  declarations, `throws` clauses, type references, enum symbols and defaults.
- A variable's own doc comment takes precedence over the declaration's, message parameters
  keep their annotations, and `oneway` with a non-void return type is an error.
- A doc comment where a declaration does not expect one is reported as an out-of-place
  warning rather than a syntax error, and its indentation and asterisks are preserved when
  they are content.
- Escape sequences in IDL string literals are decoded, and every numeric literal form the
  grammar accepts is parsed.
- Imports resolve relative to the importing file, and characters the lexer cannot tokenise
  fail the translation instead of being skipped.
- Types imported from a protocol keep their source namespace.
- Generated Avro `fixed` and `error` types are classes, so the generated code compiles.
- The `uuid` logical type maps to `Guid`, and decimals are converted in every schema
  position with a missing scale defaulting to zero.
- C# keywords are escaped and clashing member names resolved in generated code.
- A union with several non-null branches is typed as `object`, and field nullability is
  derived from the presence of a `null` branch.
- Compatibility and diff findings memoised under recursion are re-rooted at each use site,
  so their reported locations are correct.

## 0.1.1 and earlier

Released before this changelog was kept. See the commit history for what changed.
