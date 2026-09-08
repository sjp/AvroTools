using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Antlr4.Runtime;
using Newtonsoft.Json.Linq;
using AvroProtocol = Avro.Protocol;
using AvroSchema = Avro.Schema;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Translates IDL documents to their equivalent JSON-compatible protocol and schema forms.
/// </summary>
public class IdlToAvroTranslator : IIdlToAvroTranslator
{
    private readonly IIdlFileReader _fileReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdlToAvroTranslator"/> class.
    /// </summary>
    /// <param name="fileReader">The reader to use for retrieving imported files.</param>
    public IdlToAvroTranslator(IIdlFileReader fileReader)
    {
        ArgumentNullException.ThrowIfNull(fileReader);

        _fileReader = fileReader;
    }

    /// <summary>
    /// Translates IDL to either Protocol or Schema.
    /// </summary>
    /// <param name="idlContent">A string containing an IDL representing a protocol or a schema.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A parse result that contains either a protocol or a schema.</returns>
    /// <exception cref="ArgumentException"><paramref name="idlContent"/> is <c>null</c>, empty or whitespace.</exception>
    /// <exception cref="IdlTranslationException">The document could not be translated.</exception>
    public Task<IdlParseResult> Translate(string idlContent, CancellationToken cancellationToken = default)
        => Translate(idlContent, null, cancellationToken);

    /// <summary>
    /// Translates IDL to either Protocol or Schema.
    /// </summary>
    /// <param name="idlContent">A string containing an IDL representing a protocol or a schema.</param>
    /// <param name="baseDirectory">The directory that relative import paths are resolved against, typically the directory containing the document. When <c>null</c>, import paths are used exactly as written.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A parse result that contains either a protocol or a schema.</returns>
    /// <exception cref="ArgumentException"><paramref name="idlContent"/> is <c>null</c>, empty or whitespace.</exception>
    /// <exception cref="IdlTranslationException">The document could not be translated.</exception>
    public Task<IdlParseResult> Translate(string idlContent, string? baseDirectory, CancellationToken cancellationToken)
        => Translate(idlContent, baseDirectory, null, cancellationToken);

    /// <summary>
    /// Translates IDL to either Protocol or Schema, resolving relative imports against a directory and
    /// guarding against an import cycle that leads back to the document itself.
    /// </summary>
    /// <param name="idlContent">A string containing an IDL representing a protocol or a schema.</param>
    /// <param name="baseDirectory">The directory that relative import paths are resolved against, typically the directory containing the document. When <c>null</c>, import paths are used exactly as written.</param>
    /// <param name="sourcePath">
    /// The path the document was read from. An import elsewhere in the graph that resolves back to
    /// this path is then recognised as a cycle instead of being parsed again. When <c>null</c>, such
    /// as for a document read from standard input, no such path is known.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A parse result that contains either a protocol or a schema.</returns>
    /// <exception cref="ArgumentException"><paramref name="idlContent"/> is <c>null</c>, empty or whitespace.</exception>
    /// <exception cref="IdlTranslationException">The document could not be translated.</exception>
    public async Task<IdlParseResult> Translate(string idlContent, string? baseDirectory, string? sourcePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idlContent);

        var antlrStream = new AntlrInputStream(idlContent);
        var parsed = ParseIdlContent(antlrStream);
        var context = CreateRootContext(parsed, baseDirectory);

        if (!string.IsNullOrEmpty(sourcePath))
            context.ProcessedImports.Add(Path.GetFullPath(sourcePath));

        return await Translate(parsed.Tree, context, cancellationToken);
    }

    /// <summary>
    /// Translates IDL to either Protocol or Schema.
    /// </summary>
    /// <param name="idlContent">A stream whose contents contain an IDL representing a protocol or a schema.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A parse result that contains either a protocol or a schema.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="idlContent"/> is <c>null</c>.</exception>
    /// <exception cref="IdlTranslationException">The document could not be translated.</exception>
    public Task<IdlParseResult> Translate(Stream idlContent, CancellationToken cancellationToken = default)
        => Translate(idlContent, null, cancellationToken);

    /// <summary>
    /// Translates IDL to either Protocol or Schema.
    /// </summary>
    /// <param name="idlContent">A stream whose contents contain an IDL representing a protocol or a schema.</param>
    /// <param name="baseDirectory">The directory that relative import paths are resolved against, typically the directory containing the document. When <c>null</c>, import paths are used exactly as written.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A parse result that contains either a protocol or a schema.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="idlContent"/> is <c>null</c>.</exception>
    /// <exception cref="IdlTranslationException">The document could not be translated.</exception>
    public async Task<IdlParseResult> Translate(Stream idlContent, string? baseDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idlContent);

        var antlrStream = new AntlrInputStream(idlContent);
        var parsed = ParseIdlContent(antlrStream);
        var context = CreateRootContext(parsed, baseDirectory);
        return await Translate(parsed.Tree, context, cancellationToken);
    }

    private static IdlParsingContext CreateRootContext(ParsedIdlDocument parsed, string? baseDirectory)
    {
        var context = new IdlParsingContext
        {
            BaseDirectory = NormaliseBaseDirectory(baseDirectory),
            DocComments = parsed.DocComments
        };
        context.Warnings.AddRange(parsed.DocComments.Warnings);

        return context;
    }

    private static string? NormaliseBaseDirectory(string? baseDirectory)
    {
        return string.IsNullOrWhiteSpace(baseDirectory)
            ? null
            : Path.GetFullPath(baseDirectory);
    }

    /// <summary>
    /// A parsed IDL document, together with the documentation written against its declarations.
    /// Doc comments are tokenised onto a hidden channel, so they are resolved from the token stream
    /// rather than read out of the parse tree.
    /// </summary>
    private readonly record struct ParsedIdlDocument(IdlParser.IdlFileContext Tree, IdlDocComments DocComments);

    private static ParsedIdlDocument ParseIdlContent(AntlrInputStream inputStream)
    {
        var errorListener = new ThrowingErrorListener();

        var lexer = new IdlLexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);

        var tokenStream = new CommonTokenStream(lexer);
        var parser = new IdlParser(tokenStream);

        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);

        var tree = parser.idlFile();

        return new ParsedIdlDocument(tree, IdlDocComments.Resolve(tokenStream, tree));
    }

    /// <summary>
    /// Attempts to translate to either Protocol or Schema, returning the appropriate type.
    /// </summary>
    private async Task<IdlParseResult> Translate(IdlParser.IdlFileContext context, IdlParsingContext parsingContext, CancellationToken cancellationToken)
    {
        if (context.protocol != null)
        {
            var protocolJson = await TranslateProtocolToJson(context.protocol, parsingContext, cancellationToken);
            var protocol = ParseTranslated(() => AvroProtocol.Parse(protocolJson.ToString()), "protocol");
            return IdlParseResult.Protocol(protocol, protocolJson, parsingContext.NamedSchemas, parsingContext.Warnings);
        }
        else
        {
            var schemaJson = await TranslateSchemaToJson(context, parsingContext, cancellationToken);
            var schema = ParseTranslated(() => AvroSchema.Parse(schemaJson.ToString()), "schema");
            return IdlParseResult.Schema(schema, schemaJson, parsingContext.NamedSchemas, parsingContext.Warnings);
        }
    }

    /// <summary>
    /// Reads the translated JSON back as an Avro protocol or schema, reporting a document that
    /// translates to something Avro will not accept as a translation failure rather than letting
    /// an error from another library's object model escape.
    /// </summary>
    private static T ParseTranslated<T>(Func<T> parse, string kind)
    {
        try
        {
            return parse();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new IdlTranslationException($"The document does not describe a valid Avro {kind}: {ex.Message}", ex);
        }
    }

    private async Task<JToken> TranslateSchemaToJson(IdlParser.IdlFileContext context, IdlParsingContext parsingContext, CancellationToken cancellationToken)
    {
        parsingContext.DefaultNamespace = context.@namespace?.@namespace?.GetName();

        // process imports for schemas
        var importedTypes = new List<JObject>();
        var importedMessages = new JObject();

        foreach (var import in context._imports)
        {
            await ProcessImport(import, importedTypes, importedMessages, parsingContext, cancellationToken);
        }

        // cache imported types and any named schemas in the file for reference resolution
        foreach (var importedType in importedTypes)
        {
            var name = GetSchemaName(importedType, parsingContext);
            if (!string.IsNullOrEmpty(name))
            {
                parsingContext.NamedSchemas[name] = importedType;
            }
        }

        // cache any named schemas defined in this file
        foreach (var namedSchema in context._namedSchemas)
        {
            var schemaJson = TranslateNamedSchema(namedSchema, parsingContext);
            var name = GetSchemaName(schemaJson, parsingContext);
            if (!string.IsNullOrEmpty(name))
            {
                parsingContext.NamedSchemas[name] = schemaJson;
            }
        }

        // now translate with forward reference tracking enabled
        parsingContext.TrackForwardReferences = true;

        JToken mainSchemaJson;
        if (context.mainSchema != null)
        {
            mainSchemaJson = TranslateFullType(context.mainSchema.mainSchema, parsingContext);
        }
        else if (context._namedSchemas.Count > 0)
        {
            var schema = context._namedSchemas[0];

            var fullName = GetDeclaredSchemaFullName(schema, parsingContext);
            if (!string.IsNullOrEmpty(fullName))
                parsingContext.ProcessedSchemas.Add(fullName);

            mainSchemaJson = TranslateNamedSchema(schema, parsingContext);
        }
        else
        {
            throw new IdlTranslationException("The IDL file does not contain a schema.");
        }

        return mainSchemaJson;
    }

    private async Task<JObject> TranslateProtocolToJson(IdlParser.ProtocolDeclarationContext context, IdlParsingContext parsingContext, CancellationToken cancellationToken)
    {
        var (protocolName, protocolOwnNamespace) = SplitDeclaredName(context.name.GetName());
        var doc = parsingContext.DocComments.For(context);
        var properties = TranslateProperties(context._schemaProperties);
        var body = context.body;

        parsingContext.DefaultNamespace = protocolOwnNamespace ?? GetNamespaceFromProperties(properties);

        var importedTypes = new List<JObject>();
        var importedMessages = new JObject();

        foreach (var import in body._imports)
            await ProcessImport(import, importedTypes, importedMessages, parsingContext, cancellationToken);

        foreach (var importedType in importedTypes)
        {
            var name = GetSchemaName(importedType, parsingContext);
            if (!string.IsNullOrEmpty(name))
                parsingContext.ProcessedSchemas.Add(name);
        }

        var types = new List<JObject>();
        types.AddRange(importedTypes); // add imported types first
        types.AddRange(TranslateNamedSchemas(body._namedSchemas, parsingContext));

        var messages = new JObject();

        foreach (var prop in importedMessages.Properties())
            messages[prop.Name] = prop.Value;

        foreach (var message in body._messages)
        {
            var messageName = message.name.GetName();
            var messageJson = TranslateMessage(message, parsingContext);
            messages[messageName] = messageJson;
        }

        var protocolJson = new JObject
        {
            ["protocol"] = protocolName
        };

        if (!string.IsNullOrWhiteSpace(parsingContext.DefaultNamespace))
            protocolJson["namespace"] = parsingContext.DefaultNamespace;

        if (!string.IsNullOrWhiteSpace(doc))
            protocolJson["doc"] = doc;

        protocolJson["types"] = new JArray(types);
        protocolJson["messages"] = messages;

        var nonNamespaceProperties = properties.Where(p => p.Key != "namespace");
        foreach (var prop in nonNamespaceProperties)
        {
            protocolJson[prop.Key] = prop.Value;
        }

        return protocolJson;
    }

    /// <summary>
    /// Translates a set of named schema declarations into the types of a protocol. Declarations may
    /// refer to each other in any order: a type named before it is declared is inlined at the point
    /// of first use, so that every type in the returned list is defined before it is referred to.
    /// </summary>
    private List<JObject> TranslateNamedSchemas(IList<IdlParser.NamedSchemaDeclarationContext> namedSchemas, IdlParsingContext parsingContext)
    {
        // cache all named schemas for forward reference resolution
        foreach (var namedSchema in namedSchemas)
        {
            var schemaJson = TranslateNamedSchema(namedSchema, parsingContext);
            var name = GetSchemaName(schemaJson, parsingContext);
            if (!string.IsNullOrEmpty(name))
                parsingContext.NamedSchemas[name] = schemaJson;
        }

        // now lets check for forward references
        parsingContext.TrackForwardReferences = true;

        var types = new List<JObject>();

        foreach (var namedSchema in namedSchemas)
        {
            var fullName = GetDeclaredSchemaFullName(namedSchema, parsingContext);

            if (!string.IsNullOrEmpty(fullName))
                parsingContext.ProcessedSchemas.Add(fullName);

            var schemaJson = TranslateNamedSchema(namedSchema, parsingContext);
            // only add if it wasn't inlined as a forward reference elsewhere
            if (!string.IsNullOrEmpty(fullName) && !parsingContext.InlinedForwardRefs.Contains(fullName))
                types.Add(schemaJson);
        }

        return types;
    }

    /// <summary>
    /// The fully qualified name a declaration will carry, taking its namespace from an explicit
    /// property when present and from the enclosing document otherwise.
    /// </summary>
    private string GetDeclaredSchemaFullName(IdlParser.NamedSchemaDeclarationContext context, IdlParsingContext parsingContext)
    {
        var (localName, ownNamespace) = SplitDeclaredName(GetNamedSchemaName(context));

        var schemaProperties = context.fixedDeclaration()?._schemaProperties
            ?? context.enumDeclaration()?._schemaProperties
            ?? context.recordDeclaration()?._schemaProperties;
        var schemaProps = schemaProperties != null ? TranslateProperties(schemaProperties) : [];
        var explicitNamespace = schemaProps.TryGetValue("namespace", out var ns)
            ? ns.ToString()
            : null;

        var schemaNamespace = ownNamespace ?? explicitNamespace ?? parsingContext.DefaultNamespace;

        return !string.IsNullOrEmpty(schemaNamespace)
            ? $"{schemaNamespace}.{localName}"
            : localName;
    }

    /// <summary>
    /// Splits a declared name at its last <c>.</c> into the simple name that a schema is registered
    /// under and the namespace that owns it. A declared name carries its own namespace, which
    /// overrides both an explicit <c>@namespace</c> annotation and any namespace inherited from the
    /// enclosing document.
    /// </summary>
    private static (string Name, string? Namespace) SplitDeclaredName(string name)
    {
        var lastSeparator = name.LastIndexOf('.');

        return lastSeparator > 0
            ? (name[(lastSeparator + 1)..], name[..lastSeparator])
            : (name, null);
    }

    private JObject TranslateNamedSchema(IdlParser.NamedSchemaDeclarationContext context, IdlParsingContext parsingContext)
    {
        if (context.fixedDeclaration() != null)
            return TranslateFixed(context.fixedDeclaration(), parsingContext);

        if (context.enumDeclaration() != null)
            return TranslateEnum(context.enumDeclaration(), parsingContext);

        if (context.recordDeclaration() != null)
            return TranslateRecord(context.recordDeclaration(), parsingContext);

        throw Error(context, "Unknown named schema type");
    }

    private JObject TranslateFixed(IdlParser.FixedDeclarationContext context, IdlParsingContext parsingContext)
    {
        var (name, ownNamespace) = SplitDeclaredName(context.name.GetName());
        var size = ParseNumericLiteral(context.size, context.size.Text, IdlNumericLiteral.ParseInt32);
        var doc = parsingContext.DocComments.For(context);
        var properties = TranslateProperties(context._schemaProperties);

        var fixedJson = new JObject
        {
            ["type"] = "fixed",
            ["name"] = name,
            ["size"] = size
        };

        var explicitNamespace = properties.TryGetValue("namespace", out var ns) ? ns.ToString() : null;
        var fixedNamespace = ownNamespace ?? explicitNamespace ?? parsingContext.DefaultNamespace;
        if (!string.IsNullOrWhiteSpace(fixedNamespace))
            fixedJson["namespace"] = fixedNamespace;

        if (!string.IsNullOrWhiteSpace(doc))
            fixedJson["doc"] = doc;

        var nonNamespaceProperties = properties.Where(p => p.Key != "namespace");
        foreach (var prop in nonNamespaceProperties)
        {
            fixedJson[prop.Key] = prop.Value;
        }

        return fixedJson;
    }

    private JObject TranslateEnum(IdlParser.EnumDeclarationContext context, IdlParsingContext parsingContext)
    {
        var (name, ownNamespace) = SplitDeclaredName(context.name.GetName());
        var doc = parsingContext.DocComments.For(context);
        var properties = TranslateProperties(context._schemaProperties);

        var symbols = new JArray();
        foreach (var symbol in context._enumSymbols)
        {
            symbols.Add(symbol.name.GetName());
        }

        var enumJson = new JObject
        {
            ["type"] = "enum",
            ["name"] = name,
            ["symbols"] = symbols
        };

        // a declared name's own namespace takes priority, then an explicit @namespace, then the
        // document's default namespace
        var explicitNamespace = properties.TryGetValue("namespace", out var ns) ? ns.ToString() : null;
        var enumNamespace = ownNamespace ?? explicitNamespace ?? parsingContext.DefaultNamespace;
        if (!string.IsNullOrWhiteSpace(enumNamespace))
            enumJson["namespace"] = enumNamespace;

        if (!string.IsNullOrWhiteSpace(doc))
            enumJson["doc"] = doc;

        if (context.defaultSymbol != null)
            enumJson["default"] = context.defaultSymbol.defaultSymbolName.GetName();

        foreach (var prop in properties)
        {
            if (prop.Key != "namespace")
            {
                enumJson[prop.Key] = prop.Value;
            }
        }

        return enumJson;
    }

    private JObject TranslateRecord(IdlParser.RecordDeclarationContext context, IdlParsingContext parsingContext)
    {
        var (name, ownNamespace) = SplitDeclaredName(context.name.GetName());
        var doc = parsingContext.DocComments.For(context);
        var properties = TranslateProperties(context._schemaProperties);

        var explicitRecordNamespace = properties.TryGetValue("namespace", out var ns) ? ns.ToString() : null;
        var recordNamespace = ownNamespace ?? explicitRecordNamespace ?? parsingContext.DefaultNamespace;

        var previousNamespace = parsingContext.CurrentNamespace;
        parsingContext.CurrentNamespace = recordNamespace;

        JArray fields;
        try
        {
            fields = new JArray();
            foreach (var fieldDecl in context.body._fields)
            {
                foreach (var varDecl in fieldDecl._variableDeclarations)
                {
                    var field = TranslateField(fieldDecl, varDecl, parsingContext);
                    fields.Add(field);
                }
            }
        }
        finally
        {
            parsingContext.CurrentNamespace = previousNamespace;
        }

        var recordJson = new JObject
        {
            ["type"] = context.recordType.Text,
            ["name"] = name,
            ["fields"] = fields
        };

        if (!string.IsNullOrWhiteSpace(recordNamespace))
            recordJson["namespace"] = recordNamespace;

        if (!string.IsNullOrWhiteSpace(doc))
            recordJson["doc"] = doc;

        var nonNamespaceProperties = properties.Where(p => p.Key != "namespace");
        foreach (var prop in nonNamespaceProperties)
        {
            recordJson[prop.Key] = prop.Value;
        }

        return recordJson;
    }

    private JObject TranslateField(
        IdlParser.FieldDeclarationContext fieldDecl,
        IdlParser.VariableDeclarationContext varDecl,
        IdlParsingContext parsingContext)
    {
        var fieldName = varDecl.fieldName.GetName();
        var defaultValue = varDecl.defaultValue != null ? TranslateJsonValue(varDecl.defaultValue) : null;
        var fieldType = TranslateFullType(fieldDecl.fieldType, parsingContext, defaultValue);

        if (defaultValue != null)
            ValidateDefaultValue(fieldType, defaultValue, $"field '{fieldName}'", varDecl, parsingContext);

        // a comment attached to the variable describes that variable alone, so it wins over the
        // comment on the declaration, which is shared by every variable declared with the same type
        var doc = parsingContext.DocComments.For(varDecl)
            ?? parsingContext.DocComments.For(fieldDecl);
        var properties = TranslateProperties(varDecl._schemaProperties);

        var field = new JObject
        {
            ["name"] = fieldName,
            ["type"] = fieldType
        };

        if (!string.IsNullOrWhiteSpace(doc))
            field["doc"] = doc;

        if (defaultValue != null)
            field["default"] = defaultValue;

        foreach (var prop in properties)
        {
            field[prop.Key] = prop.Value;
        }

        return field;
    }

    private JObject TranslateMessage(IdlParser.MessageDeclarationContext context, IdlParsingContext parsingContext)
    {
        var doc = parsingContext.DocComments.For(context);
        var properties = TranslateProperties(context._schemaProperties);
        var isOneway = context.oneway != null;

        if (isOneway && context.returnType.Void() == null)
            throw Error(context, $"One-way message must return void: '{context.name.GetName()}'.");

        var request = new JArray();
        foreach (var param in context._formalParameters)
        {
            var paramName = param.parameter.fieldName.GetName();
            var paramDefault = param.parameter.defaultValue != null ? TranslateJsonValue(param.parameter.defaultValue) : null;
            var paramType = TranslateFullType(param.parameterType, parsingContext, paramDefault);

            if (paramDefault != null)
                ValidateDefaultValue(paramType, paramDefault, $"parameter '{paramName}' of message '{context.name.GetName()}'", param.parameter, parsingContext);

            // a comment written against the parameter name itself is preferred over one written
            // ahead of its type, matching the precedence used for record fields
            var paramDoc = parsingContext.DocComments.For(param.parameter)
                ?? parsingContext.DocComments.For(param);
            var paramProperties = TranslateProperties(param.parameter._schemaProperties);

            var requestParam = new JObject
            {
                ["name"] = paramName,
                ["type"] = paramType
            };

            if (!string.IsNullOrWhiteSpace(paramDoc))
                requestParam["doc"] = paramDoc;

            if (paramDefault != null)
                requestParam["default"] = paramDefault;

            foreach (var paramProp in paramProperties)
            {
                requestParam[paramProp.Key] = paramProp.Value;
            }

            request.Add(requestParam);
        }

        var response = context.returnType.Void() != null
            ? (JToken)"null"
            : TranslatePlainType(context.returnType.plainType(), parsingContext);

        var message = new JObject
        {
            ["request"] = request,
            ["response"] = response
        };

        if (!string.IsNullOrWhiteSpace(doc))
            message["doc"] = doc;

        if (isOneway)
            message["one-way"] = true;

        if (context._errors.Count > 0)
        {
            var errors = new JArray();
            foreach (var error in context._errors)
            {
                errors.Add(error.GetName());
            }
            message["errors"] = errors;
        }

        foreach (var prop in properties)
        {
            message[prop.Key] = prop.Value;
        }

        return message;
    }

    private JToken TranslateFullType(IdlParser.FullTypeContext context, IdlParsingContext parsingContext, JToken? defaultValue = null)
    {
        var properties = TranslateProperties(context._schemaProperties);
        var typeToken = TranslatePlainType(context.plainType(), parsingContext, defaultValue);

        if (properties.Count == 0)
            return typeToken;

        // a reference names a type that is defined elsewhere, and Avro reads only the name from it,
        // so any property written alongside the reference would be silently discarded
        var referenceName = context.plainType().nullableType()?.referenceName;
        if (referenceName != null)
            throw Error(context, $"Annotations cannot be applied to a reference to the named type '{referenceName.GetName()}'; annotate the declaration of the type instead.");

        if (typeToken is JArray unionArray)
        {
            if (context.plainType().unionType() != null)
                throw Error(context, "Annotations cannot be applied to a union type; annotate the individual branches instead.");

            return ApplyAnnotationsToNullableBranch(unionArray, properties);
        }

        if (typeToken is JObject obj)
        {
            foreach (var prop in properties)
            {
                obj[prop.Key] = prop.Value;
            }
            return obj;
        }

        var wrapper = new JObject
        {
            ["type"] = typeToken
        };
        foreach (var prop in properties)
        {
            wrapper[prop.Key] = prop.Value;
        }
        return wrapper;
    }

    private static JArray ApplyAnnotationsToNullableBranch(JArray unionArray, Dictionary<string, JToken> properties)
    {
        var result = new JArray();

        foreach (var branch in unionArray)
        {
            if (branch.Type == JTokenType.String && branch.Value<string>() == "null")
            {
                result.Add(branch);
                continue;
            }

            var branchObj = branch is JObject existing
                ? existing
                : new JObject { ["type"] = branch };

            foreach (var prop in properties)
            {
                branchObj[prop.Key] = prop.Value;
            }

            result.Add(branchObj);
        }

        return result;
    }

    private JToken TranslatePlainType(IdlParser.PlainTypeContext context, IdlParsingContext parsingContext, JToken? defaultValue = null)
    {
        if (context.arrayType() != null)
            return TranslateArrayType(context.arrayType(), parsingContext);

        if (context.mapType() != null)
            return TranslateMapType(context.mapType(), parsingContext);

        if (context.unionType() != null)
            return TranslateUnionType(context.unionType(), parsingContext);

        if (context.nullableType() != null)
            return TranslateNullableType(context.nullableType(), parsingContext, defaultValue);

        throw Error(context, "Unknown plain type");
    }

    private JObject TranslateArrayType(IdlParser.ArrayTypeContext context, IdlParsingContext parsingContext)
    {
        var itemType = TranslateFullType(context.elementType, parsingContext);
        return new JObject
        {
            ["type"] = "array",
            ["items"] = itemType
        };
    }

    private JObject TranslateMapType(IdlParser.MapTypeContext context, IdlParsingContext parsingContext)
    {
        var valueType = TranslateFullType(context.valueType, parsingContext);
        return new JObject
        {
            ["type"] = "map",
            ["values"] = valueType
        };
    }

    private JArray TranslateUnionType(IdlParser.UnionTypeContext context, IdlParsingContext parsingContext)
    {
        var fullTypes = context._types
            .Select(t => TranslateFullType(t, parsingContext))
            .ToList();
        return new JArray(fullTypes);
    }

    private JToken TranslateNullableType(IdlParser.NullableTypeContext context, IdlParsingContext parsingContext, JToken? defaultValue = null)
    {
        var baseType = TranslatePrimitiveOrReference(context, parsingContext);
        if (context.QuestionMark() == null)
            return baseType;

        var hasNonNullDefault = defaultValue != null && defaultValue.Type != JTokenType.Null;
        return hasNonNullDefault
            ? new JArray { baseType, "null" }
            : new JArray { "null", baseType };
    }

    private JToken TranslatePrimitiveOrReference(IdlParser.NullableTypeContext context, IdlParsingContext parsingContext)
    {
        if (context.primitiveType() != null)
            return TranslatePrimitiveType(context.primitiveType());

        if (context.referenceName != null)
            return TranslateReferenceType(context, parsingContext);

        throw Error(context, "Unknown nullable type");
    }

    private JToken TranslateReferenceType(IdlParser.NullableTypeContext context, IdlParsingContext parsingContext)
    {
        var refName = context.referenceName.GetName();
        var fullName = ResolveFullTypeName(refName, parsingContext);

        if (parsingContext.ProcessedSchemas.Contains(fullName) // already been added to the types array
            || parsingContext.InlinedForwardRefs.Contains(fullName)) // has been inlined already
        {
            if (refName.Contains('.'))
                return refName;

            var typeNamespace = fullName.Contains('.')
                ? fullName[..fullName.LastIndexOf('.')]
                : null;

            var enclosingNamespace = parsingContext.CurrentNamespace ?? parsingContext.DefaultNamespace;

            return typeNamespace == enclosingNamespace
                ? refName
                : fullName;
        }
        else if (parsingContext.TrackForwardReferences && parsingContext.NamedSchemas.TryGetValue(fullName, out var schema))
        {
            // type is defined later so should be inlined
            parsingContext.InlinedForwardRefs.Add(fullName);

            var inlinedSchema = (JObject)schema.DeepClone();

            // recursively process the inlined schema to replace any string references
            // with inlined schemas if they are also forward references
            ProcessForwardReferencesInSchema(inlinedSchema, parsingContext);

            return inlinedSchema;
        }

        return refName;
    }

    private static JToken TranslatePrimitiveType(IdlParser.PrimitiveTypeContext context)
    {
        var typeNameToken = context.typeName;
        if (typeNameToken == null)
        {
            throw Error(context, "Primitive type has no type name");
        }

        var logicalType = TranslateLogicalType(context);
        if (logicalType != null)
            return logicalType;

        var text = typeNameToken.Text;

        return text switch
        {
            "void" => new JValue("null"),
            "boolean" => new JValue("boolean"),
            "int" => new JValue("int"),
            "long" => new JValue("long"),
            "float" => new JValue("float"),
            "double" => new JValue("double"),
            "string" => new JValue("string"),
            "bytes" => new JValue("bytes"),
            "null" => new JValue("null"),

            _ => new JValue(text) // just return the value if unknown
        };
    }

    private static JObject? TranslateLogicalType(IdlParser.PrimitiveTypeContext context)
    {
        var typeNameToken = context.typeName
            ?? throw Error(context, "Logical type has no type name");

        var text = typeNameToken.Text;

        // decimal is a special case
        if (text == "decimal")
        {
            var precisionToken = context.precision;
            var scaleToken = context.scale;

            if (precisionToken != null)
            {
                var precision = ParseNumericLiteral(precisionToken, precisionToken.Text, IdlNumericLiteral.ParseInt32);
                var decimalObj = new JObject
                {
                    ["type"] = "bytes",
                    ["logicalType"] = "decimal",
                    ["precision"] = precision
                };

                if (scaleToken != null)
                {
                    var scale = ParseNumericLiteral(scaleToken, scaleToken.Text, IdlNumericLiteral.ParseInt32);
                    decimalObj["scale"] = scale;
                }

                return decimalObj;
            }
        }

        return text switch
        {
            "uuid" => new JObject
            {
                // consider configuring this so that uuid can be 'fixed' with size = 16
                ["type"] = "string",
                ["logicalType"] = "uuid"
            },
            "date" => new JObject
            {
                ["type"] = "int",
                ["logicalType"] = "date"
            },
            "time_ms" => new JObject
            {
                ["type"] = "int",
                ["logicalType"] = "time-millis"
            },
            "timestamp_ms" => new JObject
            {
                ["type"] = "long",
                ["logicalType"] = "timestamp-millis"
            },
            "local_timestamp_ms" => new JObject
            {
                ["type"] = "long",
                ["logicalType"] = "local-timestamp-millis"
            },

            _ => null
        };
    }

    private JToken TranslateJsonValue(IdlParser.JsonValueContext context)
    {
        if (context.jsonLiteral() != null)
            return TranslateJsonLiteral(context.jsonLiteral());

        if (context.jsonObject() != null)
            return TranslateJsonObject(context.jsonObject());

        if (context.jsonArray() != null)
            return TranslateJsonArray(context.jsonArray());

        throw Error(context, "Unknown JSON value type");
    }

    private static JToken TranslateJsonLiteral(IdlParser.JsonLiteralContext context)
    {
        if (context.StringLiteral() != null)
            return IdlStringLiteral.Unescape(context.StringLiteral().GetText());

        if (context.IntegerLiteral() != null)
            return ParseNumericLiteral(context, context.IntegerLiteral().GetText(), IdlNumericLiteral.ParseInteger);

        if (context.FloatingPointLiteral() != null)
        {
            var literalText = context.FloatingPointLiteral().GetText();
            var value = ParseNumericLiteral(context, literalText, IdlNumericLiteral.ParseDouble);
            if (!double.IsFinite(value))
                throw Error(context, $"The numeric literal '{literalText}' has no JSON representation and cannot be used in an Avro schema.");

            return value;
        }

        if (context.BTrue() != null)
            return true;

        if (context.BFalse() != null)
            return false;

        if (context.Null() != null)
            return JValue.CreateNull();

        throw Error(context, "Unknown JSON literal type");
    }

    private JObject TranslateJsonObject(IdlParser.JsonObjectContext context)
    {
        var obj = new JObject();
        foreach (var pair in context._jsonPairs)
        {
            var key = IdlStringLiteral.Unescape(pair.name.Text);
            obj[key] = TranslateJsonValue(pair.value);
        }
        return obj;
    }

    private JArray TranslateJsonArray(IdlParser.JsonArrayContext context)
    {
        var jsonValues = context._jsonValues
            .Select(TranslateJsonValue)
            .ToList();

        return new JArray(jsonValues);
    }

    private async Task ProcessImport(
        IdlParser.ImportStatementContext import,
        List<JObject> importedTypes,
        JObject importedMessages,
        IdlParsingContext parsingContext,
        CancellationToken cancellationToken)
    {
        var importType = import.importType.Text;
        var location = IdlStringLiteral.Unescape(import.location.Text);

        var importPath = ResolveImportPath(location, parsingContext.BaseDirectory);

        // prevent circular imports, comparing resolved paths so that the same file reached by
        // two different spellings is recognised as a single import
        if (!parsingContext.ProcessedImports.Add(importPath))
            return;

        switch (importType.ToLowerInvariant())
        {
            case "idl":
                await ProcessIdlImport(importPath, importedTypes, importedMessages, parsingContext, cancellationToken);
                break;
            case "protocol":
                await ProcessProtocolImport(importPath, importedTypes, importedMessages, parsingContext, cancellationToken);
                break;
            case "schema":
                await ProcessSchemaImport(importPath, importedTypes, parsingContext, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Resolves an import location against the directory holding the importing document. When that
    /// directory is unknown, the location is used exactly as written so that a file reader keyed on
    /// bare names (such as one over embedded resources) still resolves it.
    /// </summary>
    /// <remarks>
    /// A directory that is itself relative belongs to a reader whose paths are relative to a root
    /// of its own, so the two are joined and reduced as text. Only a rooted directory is resolved
    /// against the file system.
    /// </remarks>
    private static string ResolveImportPath(string location, string? baseDirectory)
    {
        if (string.IsNullOrEmpty(baseDirectory))
            return location;

        return Path.IsPathRooted(baseDirectory)
            ? Path.GetFullPath(location, baseDirectory)
            : CombineRelativePath(baseDirectory, location);
    }

    /// <summary>
    /// Joins a location onto a relative directory, reducing the <c>.</c> and <c>..</c> segments
    /// that a resolved path no longer needs. The result keeps <c>/</c> separators, which every
    /// file provider understands.
    /// </summary>
    private static string CombineRelativePath(string baseDirectory, string location)
    {
        if (Path.IsPathRooted(location))
            return location;

        var segments = new List<string>();

        foreach (var segment in $"{baseDirectory}/{location}".Split('/', '\\'))
        {
            switch (segment)
            {
                case "" or ".":
                    break;
                case ".." when segments.Count > 0 && segments[^1] != "..":
                    segments.RemoveAt(segments.Count - 1);
                    break;
                default:
                    segments.Add(segment);
                    break;
            }
        }

        return string.Join('/', segments);
    }

    /// <summary>
    /// The directory that an imported document's own relative imports resolve against, which is
    /// the directory holding the imported document. A document reached by a bare name has none, so
    /// its own imports are looked up by bare name in turn.
    /// </summary>
    private static string? GetImportBaseDirectory(string importPath)
        => Path.IsPathRooted(importPath)
            ? Path.GetDirectoryName(importPath)
            : Path.GetDirectoryName(importPath.Replace('\\', '/'));

    private async Task ProcessIdlImport(
        string importPath,
        List<JObject> importedTypes,
        JObject importedMessages,
        IdlParsingContext parsingContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var idlContent = _fileReader.OpenRead(importPath);
            var antlrInputStream = new AntlrInputStream(idlContent);
            var parsed = ParseIdlContent(antlrInputStream);
            var parseTree = parsed.Tree;

            var nestedContext = new IdlParsingContext
            {
                BaseDirectory = GetImportBaseDirectory(importPath),
                DocComments = parsed.DocComments
            };
            nestedContext.ProcessedImports.UnionWith(parsingContext.ProcessedImports); // Carry forward processed imports
            nestedContext.Warnings.AddRange(parsed.DocComments.Warnings);

            if (parseTree.protocol != null)
            {
                var protocolJson = await TranslateProtocolToJson(parseTree.protocol, nestedContext, cancellationToken);

                if (protocolJson.TryGetValue("types", out var typesToken) && typesToken is JArray typesArray)
                {
                    foreach (var type in typesArray.OfType<JObject>())
                    {
                        PreserveEmptyNamespaceOnImport(type, parsingContext.DefaultNamespace);
                        importedTypes.Add(type);

                        // Cache for reference resolution
                        var name = GetSchemaName(type, parsingContext);
                        if (!string.IsNullOrEmpty(name))
                        {
                            parsingContext.NamedSchemas[name] = type;
                        }
                    }
                }

                if (protocolJson.TryGetValue("messages", out var messagesToken) && messagesToken is JObject messagesObj)
                {
                    foreach (var prop in messagesObj.Properties())
                    {
                        if (prop.Value is JObject message)
                            QualifyMessageReferences(message, nestedContext.DefaultNamespace);

                        importedMessages[prop.Name] = prop.Value;
                    }
                }
            }
            else
            {
                nestedContext.DefaultNamespace = parseTree.@namespace?.@namespace?.GetName();

                // resolve the imported document's own imports first, so that its named schemas can
                // reference types brought in transitively
                var transitiveTypes = new List<JObject>();
                var transitiveMessages = new JObject();
                foreach (var transitiveImport in parseTree._imports)
                {
                    await ProcessImport(transitiveImport, transitiveTypes, transitiveMessages, nestedContext, cancellationToken);
                }

                foreach (var transitiveType in transitiveTypes)
                {
                    PreserveEmptyNamespaceOnImport(transitiveType, parsingContext.DefaultNamespace);
                    importedTypes.Add(transitiveType);

                    var transitiveName = GetSchemaName(transitiveType, parsingContext);
                    if (!string.IsNullOrEmpty(transitiveName))
                    {
                        parsingContext.NamedSchemas[transitiveName] = transitiveType;

                        // mark as already emitted so that this document's own types refer to it by
                        // name instead of inlining it as an apparent forward reference
                        nestedContext.ProcessedSchemas.Add(transitiveName);
                    }
                }

                // the imported file's types resolve references among themselves, so that they may be
                // declared in any order, just as those of a protocol are
                foreach (var schemaJson in TranslateNamedSchemas(parseTree._namedSchemas, nestedContext))
                {
                    if (!string.IsNullOrEmpty(nestedContext.DefaultNamespace) && !schemaJson.ContainsKey("namespace"))
                    {
                        schemaJson["namespace"] = nestedContext.DefaultNamespace;
                    }

                    PreserveEmptyNamespaceOnImport(schemaJson, parsingContext.DefaultNamespace);

                    importedTypes.Add(schemaJson);

                    var name = GetSchemaName(schemaJson, parsingContext);
                    if (!string.IsNullOrEmpty(name))
                    {
                        parsingContext.NamedSchemas[name] = schemaJson;
                    }
                }
            }

            parsingContext.ProcessedImports.UnionWith(nestedContext.ProcessedImports);

            // an imported document's warnings are named by the import they came from, so that the
            // reader can tell which file a comment they never wrote is being reported against
            foreach (var warning in nestedContext.Warnings)
                parsingContext.Warnings.Add($"{importPath}: {warning}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ImportError(importPath, "Failed to import IDL from", ex);
        }
    }

    private async Task ProcessProtocolImport(
        string importPath,
        List<JObject> importedTypes,
        JObject importedMessages,
        IdlParsingContext parsingContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var protocolStream = _fileReader.OpenRead(importPath);
            using var jsonReader = new StreamReader(protocolStream);
            var protocolJson = await jsonReader.ReadToEndAsync(cancellationToken);
            var protocolObj = JObject.Parse(protocolJson);
            var protocolNamespace = GetProtocolNamespace(protocolObj);

            // Import types
            if (protocolObj.TryGetValue("types", out var typesToken) && typesToken is JArray typesArray)
            {
                foreach (var type in typesArray.OfType<JObject>())
                {
                    // types keep the namespace they had in the document they came from, which they
                    // must now state explicitly as they are moving into a differently named protocol
                    QualifyInheritedNamespaces(type, protocolNamespace);
                    PreserveEmptyNamespaceOnImport(type, parsingContext.DefaultNamespace);
                    importedTypes.Add(type);

                    // Cache for reference resolution
                    var name = GetSchemaName(type, parsingContext);
                    if (!string.IsNullOrEmpty(name))
                    {
                        parsingContext.NamedSchemas[name] = type;
                    }
                }
            }

            // Import messages
            if (protocolObj.TryGetValue("messages", out var messagesToken) && messagesToken is JObject messagesObj)
            {
                foreach (var prop in messagesObj.Properties())
                {
                    if (prop.Value is JObject message)
                        QualifyMessageReferences(message, protocolNamespace);

                    importedMessages[prop.Name] = prop.Value;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ImportError(importPath, "Failed to import protocol from", ex);
        }
    }

    private async Task ProcessSchemaImport(
        string importPath,
        List<JObject> importedTypes,
        IdlParsingContext parsingContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var schemaStream = _fileReader.OpenRead(importPath);
            using var schemaReader = new StreamReader(schemaStream);
            var schemaJson = await schemaReader.ReadToEndAsync(cancellationToken);
            var schemaObj = JObject.Parse(schemaJson);

            PreserveEmptyNamespaceOnImport(schemaObj, parsingContext.DefaultNamespace);
            importedTypes.Add(schemaObj);

            // Cache for reference resolution
            var name = GetSchemaName(schemaObj, parsingContext);
            if (!string.IsNullOrEmpty(name))
            {
                parsingContext.NamedSchemas[name] = schemaObj;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ImportError(importPath, "Failed to import schema from", ex);
        }
    }

    private Dictionary<string, JToken> TranslateProperties(IList<IdlParser.SchemaPropertyContext> properties)
    {
        var result = new Dictionary<string, JToken>();

        foreach (var prop in properties)
        {
            var name = prop.name.GetName();
            var value = TranslateJsonValue(prop.value);
            result[name] = value;
        }

        return result;
    }

    private static string? GetNamespaceFromProperties(IReadOnlyDictionary<string, JToken> properties)
    {
        return properties.TryGetValue("namespace", out var ns)
            ? ns.ToString()
            : null;
    }

    private static string? GetSchemaName(JObject schema, IdlParsingContext parsingContext)
    {
        if (!schema.TryGetValue("name", out var name))
            return null;

        // a name may already be fully qualified, in which case it carries its own namespace
        var nameStr = name.ToString();
        if (nameStr.Contains('.'))
            return nameStr;

        var ns = schema.TryGetValue("namespace", out var nsToken)
            ? nsToken.ToString()
            : parsingContext.DefaultNamespace;

        return !string.IsNullOrEmpty(ns)
            ? $"{ns}.{nameStr}"
            : nameStr;
    }

    /// <summary>
    /// The namespace that types declared in a protocol document inherit when they declare none.
    /// </summary>
    private static string? GetProtocolNamespace(JObject protocol)
    {
        if (protocol.TryGetValue("namespace", out var ns))
        {
            var nsText = ns.ToString();
            if (!string.IsNullOrEmpty(nsText))
                return nsText;
        }

        var protocolName = protocol.TryGetValue("protocol", out var name)
            ? name.ToString()
            : string.Empty;
        var lastSeparator = protocolName.LastIndexOf('.');

        return lastSeparator > 0
            ? protocolName[..lastSeparator]
            : null;
    }

    /// <summary>
    /// Writes the namespace a named schema inherits from its enclosing document or type onto the
    /// schema itself, so that it keeps its full name once moved into another document.
    /// </summary>
    private static void QualifyInheritedNamespaces(JToken? schema, string? inheritedNamespace)
    {
        switch (schema)
        {
            case JArray union:
                foreach (var branch in union)
                    QualifyInheritedNamespaces(branch, inheritedNamespace);
                return;

            case JObject obj:
                var nestedNamespace = QualifyNamespace(obj, inheritedNamespace);

                if (obj.TryGetValue("fields", out var fields) && fields is JArray fieldArray)
                {
                    foreach (var field in fieldArray.OfType<JObject>())
                        QualifyInheritedNamespaces(field["type"], nestedNamespace);
                }

                if (obj.TryGetValue("items", out var items))
                    QualifyInheritedNamespaces(items, nestedNamespace);

                if (obj.TryGetValue("values", out var values))
                    QualifyInheritedNamespaces(values, nestedNamespace);

                return;
        }
    }

    /// <summary>
    /// The namespace that schemas nested inside <paramref name="schema"/> inherit, having given the
    /// schema an explicit namespace when it is a named type that was relying on an inherited one.
    /// </summary>
    private static string? QualifyNamespace(JObject schema, string? inheritedNamespace)
    {
        if (!IsNamedSchema(schema))
            return inheritedNamespace;

        var name = schema["name"]!.ToString();
        var lastSeparator = name.LastIndexOf('.');
        if (lastSeparator > 0)
            return name[..lastSeparator];

        if (schema.TryGetValue("namespace", out var ns))
        {
            var nsText = ns.ToString();
            return !string.IsNullOrEmpty(nsText) ? nsText : null;
        }

        if (!string.IsNullOrEmpty(inheritedNamespace))
            schema["namespace"] = inheritedNamespace;

        return inheritedNamespace;
    }

    /// <summary>
    /// Marks a top-level imported named type as having no namespace when it declares none of its own and
    /// the document importing it does, so it keeps its original full name instead of silently inheriting
    /// the importing document's namespace once nested inside it.
    /// </summary>
    private static void PreserveEmptyNamespaceOnImport(JObject type, string? importingNamespace)
    {
        if (!IsNamedSchema(type) || type.ContainsKey("namespace"))
            return;

        var name = type["name"]!.ToString();
        if (name.Contains('.'))
            return;

        if (!string.IsNullOrEmpty(importingNamespace))
            type["namespace"] = "";
    }

    /// <summary>
    /// The keywords that appear as bare strings in a request, response or errors list without naming a
    /// type declared elsewhere: primitive type names and the "type" discriminator of array, map and
    /// named-schema wrappers.
    /// </summary>
    private static readonly FrozenSet<string> ReservedTypeKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "null", "boolean", "int", "long", "float", "double", "string", "bytes",
        "array", "map", "enum", "fixed", "record", "error"
    }.ToFrozenSet();

    /// <summary>
    /// Qualifies the bare type references in an imported message's request, response and errors against
    /// the namespace of the document the message came from, so the message still resolves once it is
    /// copied into a document with a different (or no) namespace.
    /// </summary>
    private static void QualifyMessageReferences(JObject message, string? sourceNamespace)
    {
        if (message.TryGetValue("request", out var requestToken) && requestToken is JArray request)
        {
            foreach (var param in request.OfType<JObject>())
            {
                if (param.TryGetValue("type", out var paramType))
                    param["type"] = QualifyTypeReference(paramType, sourceNamespace);
            }
        }

        if (message.TryGetValue("response", out var response))
            message["response"] = QualifyTypeReference(response, sourceNamespace);

        if (message.TryGetValue("errors", out var errorsToken) && errorsToken is JArray errors)
        {
            for (var i = 0; i < errors.Count; i++)
                errors[i] = QualifyTypeReference(errors[i], sourceNamespace);
        }
    }

    private static JToken QualifyTypeReference(JToken type, string? sourceNamespace)
    {
        switch (type)
        {
            case JArray union:
                for (var i = 0; i < union.Count; i++)
                    union[i] = QualifyTypeReference(union[i], sourceNamespace);
                return union;

            case JObject obj:
                if (obj.TryGetValue("type", out var innerType))
                    obj["type"] = QualifyTypeReference(innerType, sourceNamespace);
                if (obj.TryGetValue("items", out var items))
                    obj["items"] = QualifyTypeReference(items, sourceNamespace);
                if (obj.TryGetValue("values", out var values))
                    obj["values"] = QualifyTypeReference(values, sourceNamespace);
                return obj;

            case JValue value when value.Type == JTokenType.String:
                var name = value.ToString();
                return string.IsNullOrEmpty(sourceNamespace) || name.Contains('.') || ReservedTypeKeywords.Contains(name)
                    ? value
                    : new JValue($"{sourceNamespace}.{name}");

            default:
                return type;
        }
    }

    private static bool IsNamedSchema(JObject schema)
    {
        if (!schema.ContainsKey("name") || !schema.TryGetValue("type", out var type))
            return false;

        return type.ToString() is "record" or "error" or "enum" or "fixed";
    }

    private static string GetNamedSchemaName(IdlParser.NamedSchemaDeclarationContext context)
    {
        if (context.fixedDeclaration() != null)
            return context.fixedDeclaration().name.GetName();

        if (context.enumDeclaration() != null)
            return context.enumDeclaration().name.GetName();

        if (context.recordDeclaration() != null)
            return context.recordDeclaration().name.GetName();

        throw Error(context, "Unknown named schema type");
    }

    /// <summary>
    /// An error describing something in a document that cannot be translated, naming the position
    /// of the declaration it was found on so that it can be located in the source.
    /// </summary>
    private static IdlTranslationException Error(ParserRuleContext context, string message, Exception? innerException = null)
        => Error(context.Start, message, innerException);

    /// <inheritdoc cref="Error(ParserRuleContext, string, Exception)"/>
    private static IdlTranslationException Error(IToken token, string message, Exception? innerException = null)
        => new($"Error at line {token.Line}:{token.Column} - {message}", null, token.Line, token.Column, innerException);

    /// <summary>
    /// Reads a numeric literal, reporting a literal that denotes no number the declared type can
    /// hold against the position it was written at.
    /// </summary>
    private static T ParseNumericLiteral<T>(IToken token, string literalText, Func<string, T> parse)
    {
        try
        {
            return parse(literalText);
        }
        catch (FormatException ex)
        {
            throw Error(token, ex.Message, ex);
        }
    }

    /// <inheritdoc cref="ParseNumericLiteral{T}(IToken, string, Func{string, T})"/>
    private static T ParseNumericLiteral<T>(ParserRuleContext context, string literalText, Func<string, T> parse)
        => ParseNumericLiteral(context.Start, literalText, parse);

    /// <summary>
    /// An error raised while reading an imported document, named by the import it came from. The
    /// position within the imported document, where one is known, is carried up unchanged: it
    /// describes where in that document the error is, not where in this one the import sits.
    /// </summary>
    private static IdlTranslationException ImportError(string importPath, string description, Exception innerException)
    {
        var located = innerException as IdlTranslationException;

        return new IdlTranslationException(
            $"{description} '{importPath}': {innerException.Message}",
            located?.FileName ?? importPath,
            located?.LineNumber,
            located?.ColumnNumber,
            innerException);
    }

    private static string ResolveFullTypeName(string typeName, IdlParsingContext parsingContext)
    {
        if (typeName.Contains('.'))
        {
            // already qualified
            return typeName;
        }

        if (!string.IsNullOrEmpty(parsingContext.CurrentNamespace))
        {
            var enclosingName = $"{parsingContext.CurrentNamespace}.{typeName}";
            if (parsingContext.NamedSchemas.ContainsKey(enclosingName))
            {
                return enclosingName;
            }
        }

        if (!string.IsNullOrEmpty(parsingContext.DefaultNamespace))
        {
            var fullName = $"{parsingContext.DefaultNamespace}.{typeName}";
            if (parsingContext.NamedSchemas.ContainsKey(fullName))
            {
                return fullName;
            }
        }

        return typeName;
    }

    /// <summary>
    /// The definition of a type referred to by name, or <c>null</c> when no type of that name has
    /// been read yet, as is the case for one declared further down the document.
    /// </summary>
    private static JToken? ResolveNamedSchema(string typeName, IdlParsingContext parsingContext)
        => parsingContext.NamedSchemas.GetValueOrDefault(ResolveFullTypeName(typeName, parsingContext));

    /// <summary>
    /// Rejects a default value that the declared type cannot hold, so that it is reported against
    /// the declaration that carries it rather than failing somewhere further downstream.
    /// </summary>
    private static void ValidateDefaultValue(
        JToken type,
        JToken defaultValue,
        string declaration,
        ParserRuleContext context,
        IdlParsingContext parsingContext)
    {
        var reason = IdlDefaultValue.DescribeMismatch(type, defaultValue, name => ResolveNamedSchema(name, parsingContext));
        if (reason != null)
            throw Error(context, $"The default value for {declaration} cannot be used: {reason}.");
    }

    /// <summary>
    /// Recursively processes a schema to replace string references with inlined schemas
    /// for forward references that haven't been processed yet.
    /// </summary>
    private void ProcessForwardReferencesInSchema(JToken schema, IdlParsingContext parsingContext)
    {
        if (schema is JObject obj)
        {
            // process record/error with fields
            if (obj.TryGetValue("fields", out var fieldsToken) && fieldsToken is JArray fields)
            {
                for (var i = 0; i < fields.Count; i++)
                {
                    if (fields[i] is JObject field && field.TryGetValue("type", out var fieldType))
                    {
                        var replacedType = ProcessForwardReferenceInType(fieldType, parsingContext);
                        if (replacedType != fieldType)
                        {
                            field["type"] = replacedType;
                        }
                    }
                }
            }
            // process array with items
            else if (obj.TryGetValue("items", out var itemsToken))
            {
                var replacedItems = ProcessForwardReferenceInType(itemsToken, parsingContext);
                if (replacedItems != itemsToken)
                {
                    obj["items"] = replacedItems;
                }
            }

            // process a map with values
            else if (obj.TryGetValue("values", out var valuesToken))
            {
                var replacedValues = ProcessForwardReferenceInType(valuesToken, parsingContext);
                if (replacedValues != valuesToken)
                {
                    obj["values"] = replacedValues;
                }
            }
        }

        if (schema is JArray arr)
        {
            // Union type - process each element
            for (var i = 0; i < arr.Count; i++)
            {
                var element = arr[i];
                var replacedElement = ProcessForwardReferenceInType(element, parsingContext);
                if (replacedElement != element)
                {
                    arr[i] = replacedElement;
                }
            }
        }
    }

    /// <summary>
    /// Processes a single type reference, potentially replacing it with an inlined schema
    /// if it's a forward reference.
    /// </summary>
    private JToken ProcessForwardReferenceInType(JToken typeToken, IdlParsingContext parsingContext)
    {
        if (typeToken is JValue val && val.Type == JTokenType.String)
        {
            var typeName = val.ToString();
            var fullName = ResolveFullTypeName(typeName, parsingContext);

            // is this is a forward reference that needs inlining?
            if (!parsingContext.ProcessedSchemas.Contains(fullName) &&
                !parsingContext.InlinedForwardRefs.Contains(fullName) &&
                parsingContext.NamedSchemas.TryGetValue(fullName, out var schema))
            {
                parsingContext.InlinedForwardRefs.Add(fullName);
                var inlinedSchema = (JObject)schema.DeepClone();

                // recursively process the inlined schema
                ProcessForwardReferencesInSchema(inlinedSchema, parsingContext);

                return inlinedSchema;
            }
        }

        if (typeToken is JObject || typeToken is JArray)
        {
            ProcessForwardReferencesInSchema(typeToken, parsingContext);
        }

        return typeToken;
    }
}
