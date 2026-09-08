using System;
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
    /// <returns>A parse result that contains either a protocol or a schema. If parsing fails, an exception is thrown.</returns>
    public Task<IdlParseResult> Translate(string idlContent, CancellationToken cancellationToken = default)
        => Translate(idlContent, null, cancellationToken);

    /// <summary>
    /// Translates IDL to either Protocol or Schema.
    /// </summary>
    /// <param name="idlContent">A string containing an IDL representing a protocol or a schema.</param>
    /// <param name="baseDirectory">The directory that relative import paths are resolved against, typically the directory containing the document. When <c>null</c>, import paths are used exactly as written.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A parse result that contains either a protocol or a schema. If parsing fails, an exception is thrown.</returns>
    public async Task<IdlParseResult> Translate(string idlContent, string? baseDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idlContent);

        var antlrStream = new AntlrInputStream(idlContent);
        var parseTree = ParseIdlContent(antlrStream);
        var context = new IdlParsingContext { BaseDirectory = NormaliseBaseDirectory(baseDirectory) };
        return await Translate(parseTree, context, cancellationToken);
    }

    /// <summary>
    /// Translates IDL to either Protocol or Schema.
    /// </summary>
    /// <param name="idlContent">A stream whose contents contain an IDL representing a protocol or a schema.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A parse result that contains either a protocol or a schema. If parsing fails, an exception is thrown.</returns>
    public Task<IdlParseResult> Translate(Stream idlContent, CancellationToken cancellationToken = default)
        => Translate(idlContent, null, cancellationToken);

    /// <summary>
    /// Translates IDL to either Protocol or Schema.
    /// </summary>
    /// <param name="idlContent">A stream whose contents contain an IDL representing a protocol or a schema.</param>
    /// <param name="baseDirectory">The directory that relative import paths are resolved against, typically the directory containing the document. When <c>null</c>, import paths are used exactly as written.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A parse result that contains either a protocol or a schema. If parsing fails, an exception is thrown.</returns>
    public async Task<IdlParseResult> Translate(Stream idlContent, string? baseDirectory, CancellationToken cancellationToken)
    {
        var antlrStream = new AntlrInputStream(idlContent);
        var parseTree = ParseIdlContent(antlrStream);
        var context = new IdlParsingContext { BaseDirectory = NormaliseBaseDirectory(baseDirectory) };
        return await Translate(parseTree, context, cancellationToken);
    }

    private static string? NormaliseBaseDirectory(string? baseDirectory)
    {
        return string.IsNullOrWhiteSpace(baseDirectory)
            ? null
            : Path.GetFullPath(baseDirectory);
    }

    private static IdlParser.IdlFileContext ParseIdlContent(AntlrInputStream inputStream)
    {
        var errorListener = new ThrowingErrorListener();

        var lexer = new IdlLexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);

        var tokenStream = new CommonTokenStream(lexer);
        var parser = new IdlParser(tokenStream);

        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);

        return parser.idlFile();
    }

    /// <summary>
    /// Attempts to translate to either Protocol or Schema, returning the appropriate type.
    /// </summary>
    private async Task<IdlParseResult> Translate(IdlParser.IdlFileContext context, IdlParsingContext parsingContext, CancellationToken cancellationToken)
    {
        if (context.protocol != null)
        {
            var protocolJson = await TranslateProtocolToJson(context.protocol, parsingContext, cancellationToken);
            var protocol = AvroProtocol.Parse(protocolJson.ToString());
            return IdlParseResult.Protocol(protocol, protocolJson, parsingContext.NamedSchemas);
        }
        else
        {
            var schemaJson = await TranslateSchemaToJson(context, parsingContext, cancellationToken);
            var schema = AvroSchema.Parse(schemaJson.ToString());
            return IdlParseResult.Schema(schema, schemaJson, parsingContext.NamedSchemas);
        }
    }

    private async Task<JToken> TranslateSchemaToJson(IdlParser.IdlFileContext context, IdlParsingContext parsingContext, CancellationToken cancellationToken)
    {
        parsingContext.DefaultNamespace = context.@namespace?.@namespace?.GetText();

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
            throw new InvalidOperationException("The IDL file does not contain a schema.");
        }

        return mainSchemaJson;
    }

    private async Task<JObject> TranslateProtocolToJson(IdlParser.ProtocolDeclarationContext context, IdlParsingContext parsingContext, CancellationToken cancellationToken)
    {
        var protocolName = context.name.GetText();
        var doc = context.doc.ExtractDocumentation();
        var properties = TranslateProperties(context._schemaProperties);
        var body = context.body;

        parsingContext.DefaultNamespace = GetNamespaceFromProperties(properties);

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
            var messageName = IdlName.EscapeName(message.name.GetText());
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
            var propName = IdlName.EscapeName(prop.Key);
            protocolJson[propName] = prop.Value;
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
        var localName = GetNamedSchemaName(context);

        var schemaProperties = context.fixedDeclaration()?._schemaProperties
            ?? context.enumDeclaration()?._schemaProperties
            ?? context.recordDeclaration()?._schemaProperties;
        var schemaProps = schemaProperties != null ? TranslateProperties(schemaProperties) : [];
        var explicitNamespace = schemaProps.TryGetValue("namespace", out var ns)
            ? ns.ToString()
            : null;

        var schemaNamespace = explicitNamespace ?? parsingContext.DefaultNamespace;

        return !string.IsNullOrEmpty(schemaNamespace)
            ? $"{schemaNamespace}.{localName}"
            : localName;
    }

    private JObject TranslateNamedSchema(IdlParser.NamedSchemaDeclarationContext context, IdlParsingContext parsingContext)
    {
        if (context.fixedDeclaration() != null)
            return TranslateFixed(context.fixedDeclaration(), parsingContext);

        if (context.enumDeclaration() != null)
            return TranslateEnum(context.enumDeclaration(), parsingContext);

        if (context.recordDeclaration() != null)
            return TranslateRecord(context.recordDeclaration(), parsingContext);

        throw new InvalidOperationException("Unknown named schema type");
    }

    private JObject TranslateFixed(IdlParser.FixedDeclarationContext context, IdlParsingContext parsingContext)
    {
        var name = IdlName.EscapeName(context.name.GetText());
        var size = IdlNumericLiteral.ParseInt32(context.size.Text);
        var doc = context.doc.ExtractDocumentation();
        var properties = TranslateProperties(context._schemaProperties);

        var fixedJson = new JObject
        {
            ["type"] = "fixed",
            ["name"] = name,
            ["size"] = size
        };

        if (properties.TryGetValue("namespace", out var explicitNamespace))
            fixedJson["namespace"] = explicitNamespace;
        else if (!string.IsNullOrWhiteSpace(parsingContext.DefaultNamespace))
            fixedJson["namespace"] = parsingContext.DefaultNamespace;

        if (!string.IsNullOrWhiteSpace(doc))
            fixedJson["doc"] = doc;

        var nonNamespaceProperties = properties.Where(p => p.Key != "namespace");
        foreach (var prop in nonNamespaceProperties)
        {
            var propName = IdlName.EscapeName(prop.Key);
            fixedJson[propName] = prop.Value;
        }

        return fixedJson;
    }

    private JObject TranslateEnum(IdlParser.EnumDeclarationContext context, IdlParsingContext parsingContext)
    {
        var name = IdlName.EscapeName(context.name.GetText());
        var doc = context.doc.ExtractDocumentation();
        var properties = TranslateProperties(context._schemaProperties);

        var symbols = new JArray();
        foreach (var symbol in context._enumSymbols)
        {
            symbols.Add(IdlName.EscapeName(symbol.name.GetText()));
        }

        var enumJson = new JObject
        {
            ["type"] = "enum",
            ["name"] = name,
            ["symbols"] = symbols
        };

        // Add namespace: use explicit if provided, otherwise use default namespace
        if (properties.TryGetValue("namespace", out var explicitNamespace))
            enumJson["namespace"] = explicitNamespace;
        else if (!string.IsNullOrWhiteSpace(parsingContext.DefaultNamespace))
            enumJson["namespace"] = parsingContext.DefaultNamespace;

        if (!string.IsNullOrWhiteSpace(doc))
            enumJson["doc"] = doc;

        if (context.defaultSymbol != null)
            enumJson["default"] = IdlName.EscapeName(context.defaultSymbol.defaultSymbolName.GetText());

        foreach (var prop in properties)
        {
            if (prop.Key != "namespace")
            {
                var propName = IdlName.EscapeName(prop.Key);
                enumJson[propName] = prop.Value;
            }
        }

        return enumJson;
    }

    private JObject TranslateRecord(IdlParser.RecordDeclarationContext context, IdlParsingContext parsingContext)
    {
        var name = IdlName.EscapeName(context.name.GetText());
        var doc = context.doc.ExtractDocumentation();
        var properties = TranslateProperties(context._schemaProperties);

        var fields = new JArray();
        foreach (var fieldDecl in context.body._fields)
        {
            foreach (var varDecl in fieldDecl._variableDeclarations)
            {
                var field = TranslateField(fieldDecl, varDecl, parsingContext);
                fields.Add(field);
            }
        }

        var recordJson = new JObject
        {
            ["type"] = context.recordType.Text,
            ["name"] = name,
            ["fields"] = fields
        };

        if (properties.TryGetValue("namespace", out var explicitNamespace))
            recordJson["namespace"] = explicitNamespace;
        else if (!string.IsNullOrWhiteSpace(parsingContext.DefaultNamespace))
            recordJson["namespace"] = parsingContext.DefaultNamespace;

        if (!string.IsNullOrWhiteSpace(doc))
            recordJson["doc"] = doc;

        var nonNamespaceProperties = properties.Where(p => p.Key != "namespace");
        foreach (var prop in nonNamespaceProperties)
        {
            var propName = IdlName.EscapeName(prop.Key);
            recordJson[propName] = prop.Value;
        }

        return recordJson;
    }

    private JObject TranslateField(
        IdlParser.FieldDeclarationContext fieldDecl,
        IdlParser.VariableDeclarationContext varDecl,
        IdlParsingContext parsingContext)
    {
        var fieldName = IdlName.EscapeName(varDecl.fieldName.GetText());
        var defaultValue = varDecl.defaultValue != null ? TranslateJsonValue(varDecl.defaultValue) : null;
        var fieldType = TranslateFullType(fieldDecl.fieldType, parsingContext, defaultValue);
        var doc = fieldDecl.doc.ExtractDocumentation()
            ?? varDecl.doc.ExtractDocumentation();
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
            var propName = IdlName.EscapeName(prop.Key);
            field[propName] = prop.Value;
        }

        return field;
    }

    private JObject TranslateMessage(IdlParser.MessageDeclarationContext context, IdlParsingContext parsingContext)
    {
        var doc = context.doc.ExtractDocumentation();
        var properties = TranslateProperties(context._schemaProperties);
        var isOneway = context.oneway != null;

        var request = new JArray();
        foreach (var param in context._formalParameters)
        {
            var paramName = IdlName.EscapeName(param.parameter.fieldName.GetText());
            var paramDefault = param.parameter.defaultValue != null ? TranslateJsonValue(param.parameter.defaultValue) : null;
            var paramType = TranslateFullType(param.parameterType, parsingContext, paramDefault);
            var paramDoc = param.doc.ExtractDocumentation();

            var requestParam = new JObject
            {
                ["name"] = paramName,
                ["type"] = paramType
            };

            if (!string.IsNullOrWhiteSpace(paramDoc))
                requestParam["doc"] = paramDoc;

            if (paramDefault != null)
                requestParam["default"] = paramDefault;

            request.Add(requestParam);
        }

        var response = context.returnType.Void() != null || isOneway
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
                errors.Add(error.GetText());
            }
            message["errors"] = errors;
        }

        foreach (var prop in properties)
        {
            var propName = IdlName.EscapeName(prop.Key);
            message[propName] = prop.Value;
        }

        return message;
    }

    private JToken TranslateFullType(IdlParser.FullTypeContext context, IdlParsingContext parsingContext, JToken? defaultValue = null)
    {
        var properties = TranslateProperties(context._schemaProperties);
        var typeToken = TranslatePlainType(context.plainType(), parsingContext, defaultValue);

        if (properties.Count == 0)
            return typeToken;

        if (typeToken is JArray unionArray)
        {
            if (context.plainType().unionType() != null)
                throw new InvalidOperationException("Annotations cannot be applied to a union type; annotate the individual branches instead.");

            return ApplyAnnotationsToNullableBranch(unionArray, properties);
        }

        if (typeToken is JObject obj)
        {
            foreach (var prop in properties)
            {
                var propName = IdlName.EscapeName(prop.Key);
                obj[propName] = prop.Value;
            }
            return obj;
        }

        var wrapper = new JObject
        {
            ["type"] = typeToken
        };
        foreach (var prop in properties)
        {
            var propName = IdlName.EscapeName(prop.Key);
            wrapper[propName] = prop.Value;
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
                var propName = IdlName.EscapeName(prop.Key);
                branchObj[propName] = prop.Value;
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

        throw new InvalidOperationException("Unknown plain type");
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

        throw new InvalidOperationException("Unknown nullable type");
    }

    private JToken TranslateReferenceType(IdlParser.NullableTypeContext context, IdlParsingContext parsingContext)
    {
        var refName = context.referenceName.GetText();
        var fullName = ResolveFullTypeName(refName, parsingContext);

        if (parsingContext.ProcessedSchemas.Contains(fullName) // already been added to the types array
            || parsingContext.InlinedForwardRefs.Contains(fullName)) // has been inlined already
        {
            if (refName.Contains('.'))
                return refName;

            var typeNamespace = fullName.Contains('.')
                ? fullName[..fullName.LastIndexOf('.')]
                : null;

            return typeNamespace == parsingContext.DefaultNamespace
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
            throw new InvalidOperationException("Primitive type has no type name");
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
            ?? throw new InvalidOperationException("Logical type has no type name");

        var text = typeNameToken.Text;

        // decimal is a special case
        if (text == "decimal")
        {
            var precisionToken = context.precision;
            var scaleToken = context.scale;

            if (precisionToken != null)
            {
                var precision = IdlNumericLiteral.ParseInt32(precisionToken.Text);
                var decimalObj = new JObject
                {
                    ["type"] = "bytes",
                    ["logicalType"] = "decimal",
                    ["precision"] = precision
                };

                if (scaleToken != null)
                {
                    var scale = IdlNumericLiteral.ParseInt32(scaleToken.Text);
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

        throw new InvalidOperationException("Unknown JSON value type");
    }

    private static JToken TranslateJsonLiteral(IdlParser.JsonLiteralContext context)
    {
        if (context.StringLiteral() != null)
            return IdlStringLiteral.Unescape(context.StringLiteral().GetText());

        if (context.IntegerLiteral() != null)
            return IdlNumericLiteral.ParseInteger(context.IntegerLiteral().GetText());

        if (context.FloatingPointLiteral() != null)
        {
            var literalText = context.FloatingPointLiteral().GetText();
            var value = IdlNumericLiteral.ParseDouble(literalText);
            if (!double.IsFinite(value))
                throw new InvalidOperationException($"The numeric literal '{literalText}' has no JSON representation and cannot be used in an Avro schema.");

            return value;
        }

        if (context.BTrue() != null)
            return true;

        if (context.BFalse() != null)
            return false;

        if (context.Null() != null)
            return JValue.CreateNull();

        throw new InvalidOperationException("Unknown JSON literal type");
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
    private static string ResolveImportPath(string location, string? baseDirectory)
    {
        return string.IsNullOrEmpty(baseDirectory)
            ? location
            : Path.GetFullPath(location, baseDirectory);
    }

    /// <summary>
    /// The directory that an imported document's own relative imports resolve against.
    /// </summary>
    private static string? GetImportBaseDirectory(string importPath, string? baseDirectory)
    {
        return string.IsNullOrEmpty(baseDirectory)
            ? null
            : Path.GetDirectoryName(importPath);
    }

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
            var parseTree = ParseIdlContent(antlrInputStream);

            var nestedContext = new IdlParsingContext
            {
                BaseDirectory = GetImportBaseDirectory(importPath, parsingContext.BaseDirectory)
            };
            nestedContext.ProcessedImports.UnionWith(parsingContext.ProcessedImports); // Carry forward processed imports

            if (parseTree.protocol != null)
            {
                var protocolJson = await TranslateProtocolToJson(parseTree.protocol, nestedContext, cancellationToken);

                if (protocolJson.TryGetValue("types", out var typesToken) && typesToken is JArray typesArray)
                {
                    foreach (var type in typesArray.OfType<JObject>())
                    {
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
                nestedContext.DefaultNamespace = parseTree.@namespace?.@namespace?.GetText();

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

                    importedTypes.Add(schemaJson);

                    var name = GetSchemaName(schemaJson, parsingContext);
                    if (!string.IsNullOrEmpty(name))
                    {
                        parsingContext.NamedSchemas[name] = schemaJson;
                    }
                }
            }

            parsingContext.ProcessedImports.UnionWith(nestedContext.ProcessedImports);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to import IDL from '{importPath}': {ex.Message}", ex);
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
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to import protocol from '{importPath}': {ex.Message}", ex);
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

            importedTypes.Add(schemaObj);

            // Cache for reference resolution
            var name = GetSchemaName(schemaObj, parsingContext);
            if (!string.IsNullOrEmpty(name))
            {
                parsingContext.NamedSchemas[name] = schemaObj;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to import schema from '{importPath}': {ex.Message}", ex);
        }
    }

    private Dictionary<string, JToken> TranslateProperties(IList<IdlParser.SchemaPropertyContext> properties)
    {
        var result = new Dictionary<string, JToken>();

        foreach (var prop in properties)
        {
            var name = IdlName.EscapeName(prop.name.GetText());
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
    /// The keywords that appear as bare strings in a request, response or errors list without naming a
    /// type declared elsewhere: primitive type names and the "type" discriminator of array, map and
    /// named-schema wrappers.
    /// </summary>
    private static readonly HashSet<string> ReservedTypeKeywords = new(StringComparer.Ordinal)
    {
        "null", "boolean", "int", "long", "float", "double", "string", "bytes",
        "array", "map", "enum", "fixed", "record", "error"
    };

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
            return context.fixedDeclaration().name.GetText();

        if (context.enumDeclaration() != null)
            return context.enumDeclaration().name.GetText();

        if (context.recordDeclaration() != null)
            return context.recordDeclaration().name.GetText();

        throw new InvalidOperationException("Unknown named schema type");
    }

    private static string ResolveFullTypeName(string typeName, IdlParsingContext parsingContext)
    {
        if (typeName.Contains('.'))
        {
            // already qualified
            return typeName;
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
