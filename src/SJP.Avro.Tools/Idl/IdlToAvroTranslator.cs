using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Antlr4.Runtime;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json.Linq;
using AvroProtocol = Avro.Protocol;
using AvroSchema = Avro.Schema;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// Translates ANTLR parse tree (IdlFileContext) to Avro.Protocol and Avro.Schema objects.
/// This approach uses the guaranteed-correct ANTLR parser and translates directly to Avro types.
/// </summary>
public class IdlToAvroTranslator
{
    private readonly IFileProvider _fileProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdlToAvroTranslator"/> class.
    /// </summary>
    /// <param name="fileProvider">The file provider to use for reading imported files. If null, uses the current directory.</param>
    public IdlToAvroTranslator(IFileProvider? fileProvider = null)
    {
        _fileProvider = fileProvider ?? new PhysicalFileProvider(Directory.GetCurrentDirectory());
    }

    /// <summary>
    /// Attempts to translate to either Protocol or Schema, returning the appropriate type.
    /// </summary>
    public static async Task<IdlParseResult> ParseIdl(string idlContent, string sourceName = "memory", IFileProvider? fileProvider = null, CancellationToken cancellationToken = default)
    {
        var parseTree = ParseIdl(idlContent);
        var translator = new IdlToAvroTranslator(fileProvider);
        var context = new IdlParsingContext
        {
            FilePath = sourceName
        };
        return await translator.Translate(parseTree, context, cancellationToken);
    }

    /// <summary>
    /// Attempts to translate to either Protocol or Schema, returning the appropriate type.
    /// </summary>
    public async Task<IdlParseResult> Translate(IdlParser.IdlFileContext context, IdlParsingContext parsingContext, CancellationToken cancellationToken)
    {
        if (context.protocol != null)
        {
            var protocol = await TranslateProtocol(context.protocol, parsingContext, cancellationToken);
            return IdlParseResult.Protocol(protocol);
        }
        else
        {
            var schema = await TranslateSchema(context, parsingContext, cancellationToken);
            return IdlParseResult.Schema(schema);
        }
    }

    private static IdlParser.IdlFileContext ParseIdl(string idlContent)
    {
        var inputStream = new AntlrInputStream(idlContent);
        var lexer = new IdlLexer(inputStream);
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new IdlParser(tokenStream);

        parser.RemoveErrorListeners();
        parser.AddErrorListener(new ThrowingErrorListener());

        return parser.idlFile();
    }

    private async Task<AvroSchema> TranslateSchema(IdlParser.IdlFileContext context, IdlParsingContext parsingContext, CancellationToken cancellationToken)
    {
        var schemaJson = await TranslateSchemaToJson(context, parsingContext, cancellationToken);
        return AvroSchema.Parse(schemaJson.ToString());
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
            mainSchemaJson = TranslateNamedSchema(schema, parsingContext);
        }
        else
        {
            throw new InvalidOperationException("The IDL file does not contain a schema.");
        }

        return mainSchemaJson;
    }

    private async Task<AvroProtocol> TranslateProtocol(IdlParser.ProtocolDeclarationContext context, IdlParsingContext parsingContext, CancellationToken cancellationToken)
    {
        var protocolJson = await TranslateProtocolToJson(context, parsingContext, cancellationToken);
        var protocolJsonText = protocolJson.ToString();
        return AvroProtocol.Parse(protocolJsonText);
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

        // cache all named schemas for forward reference resolution
        foreach (var namedSchema in body._namedSchemas)
        {
            var schemaJson = TranslateNamedSchema(namedSchema, parsingContext);
            var name = GetSchemaName(schemaJson, parsingContext);
            if (!string.IsNullOrEmpty(name))
                parsingContext.NamedSchemas[name] = schemaJson;
        }

        // now lets check for forward references
        parsingContext.TrackForwardReferences = true;

        foreach (var importedType in importedTypes)
        {
            var name = GetSchemaName(importedType, parsingContext);
            if (!string.IsNullOrEmpty(name))
                parsingContext.ProcessedSchemas.Add(name);
        }

        var types = new List<JObject>();
        types.AddRange(importedTypes); // add imported types first

        foreach (var namedSchema in body._namedSchemas)
        {
            var localNameName = GetNamedSchemaName(namedSchema);

            var schemaProperties = namedSchema.fixedDeclaration()?._schemaProperties
                ?? namedSchema.enumDeclaration()?._schemaProperties
                ?? namedSchema.recordDeclaration()?._schemaProperties;
            var schemaProps = schemaProperties != null ? TranslateProperties(schemaProperties) : [];
            var explicitNamespace = schemaProps.TryGetValue("namespace", out var ns)
                ? ns.ToString()
                : null;

            var schemaNamespace = explicitNamespace ?? parsingContext.DefaultNamespace;
            var fullName = !string.IsNullOrEmpty(schemaNamespace)
                ? $"{schemaNamespace}.{localNameName}"
                : localNameName;

            if (!string.IsNullOrEmpty(fullName))
                parsingContext.ProcessedSchemas.Add(fullName);

            var schemaJson = TranslateNamedSchema(namedSchema, parsingContext);
            // only add if it wasn't inlined as a forward reference elsewhere
            if (!string.IsNullOrEmpty(fullName) && !parsingContext.InlinedForwardRefs.Contains(fullName))
                types.Add(schemaJson);
        }

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

        if (types.Count > 0)
            protocolJson["types"] = new JArray(types);

        if (messages.Count > 0)
            protocolJson["messages"] = messages;

        var nonNamespaceProperties = properties.Where(p => p.Key != "namespace");
        foreach (var prop in nonNamespaceProperties)
        {
            var propName = IdlName.EscapeName(prop.Key);
            protocolJson[propName] = prop.Value;
        }

        return protocolJson;
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
        var sizeText = context.size.Text.Trim();
        var isHexNumber = sizeText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || sizeText.StartsWith("x", StringComparison.OrdinalIgnoreCase);
        var numberBase = isHexNumber ? 16 : 10;
        var size = Convert.ToInt32(sizeText, numberBase);
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
            symbols.Add(symbol.name.GetText());
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
        {
            // the defaultSymbol will look like '=Example;'
            // but we actually want 'Example'
            enumJson["default"] = context.defaultSymbol.GetText()
                .TrimStart('=')
                .TrimEnd(';');
        }

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
        var fieldType = TranslateFullType(fieldDecl.fieldType, parsingContext);
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

        if (varDecl.defaultValue != null)
            field["default"] = TranslateJsonValue(varDecl.defaultValue);

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
            var paramType = TranslateFullType(param.parameterType, parsingContext);
            var paramDoc = param.doc.ExtractDocumentation();

            var requestParam = new JObject
            {
                ["name"] = paramName,
                ["type"] = paramType
            };

            if (!string.IsNullOrWhiteSpace(paramDoc))
                requestParam["doc"] = paramDoc;

            if (param.parameter.defaultValue != null)
                requestParam["default"] = TranslateJsonValue(param.parameter.defaultValue);

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

    private JToken TranslateFullType(IdlParser.FullTypeContext context, IdlParsingContext parsingContext)
    {
        var properties = TranslateProperties(context._schemaProperties);
        var typeToken = TranslatePlainType(context.plainType(), parsingContext);

        if (properties.Count == 0)
            return typeToken;

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

    private JToken TranslatePlainType(IdlParser.PlainTypeContext context, IdlParsingContext parsingContext)
    {
        if (context.arrayType() != null)
            return TranslateArrayType(context.arrayType(), parsingContext);

        if (context.mapType() != null)
            return TranslateMapType(context.mapType(), parsingContext);

        if (context.unionType() != null)
            return TranslateUnionType(context.unionType(), parsingContext);

        if (context.nullableType() != null)
            return TranslateNullableType(context.nullableType(), parsingContext);

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

    private JToken TranslateNullableType(IdlParser.NullableTypeContext context, IdlParsingContext parsingContext)
    {
        var baseType = TranslatePrimitiveOrReference(context, parsingContext);
        return context.QuestionMark() != null
            ? new JArray { "null", baseType }
            : baseType;
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
                var precision = int.Parse(precisionToken.Text, CultureInfo.InvariantCulture);
                var decimalObj = new JObject
                {
                    ["type"] = "bytes",
                    ["logicalType"] = "decimal",
                    ["precision"] = precision
                };

                if (scaleToken != null)
                {
                    var scale = int.Parse(scaleToken.Text, CultureInfo.InvariantCulture);
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
        {
            var text = context.StringLiteral().GetText();
            // trims quotes
            return text[1..^1];
        }

        if (context.IntegerLiteral() != null)
            return long.Parse(context.IntegerLiteral().GetText());

        if (context.FloatingPointLiteral() != null)
            return double.Parse(context.FloatingPointLiteral().GetText(), CultureInfo.InvariantCulture);

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
            var key = pair.name.Text;

            // trim quotes
            const char QuoteChar = '"';
            if (key.StartsWith(QuoteChar) && key.EndsWith(QuoteChar))
            {
                key = key
                    .TrimStart(QuoteChar)
                    .TrimEnd(QuoteChar);
            }
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
        var location = import.location.Text;

        // trim quotes
        const char QuoteChar = '"';
        if (location.StartsWith(QuoteChar) && location.EndsWith(QuoteChar))
        {
            location = location
                .TrimStart(QuoteChar)
                .TrimEnd(QuoteChar);
        }

        // prevent circular imports
        var importPath = ResolveRelativePath(parsingContext.FilePath, location);
        if (parsingContext.ProcessedImports.Contains(importPath))
            return;

        parsingContext.ProcessedImports.Add(importPath);

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

    private static string ResolveRelativePath(string basePath, string relativePath)
    {
        // If basePath is "memory" or similar non-file paths, just return the relative path
        if (basePath == "memory" || !Path.IsPathRooted(basePath))
            return relativePath;

        var baseDir = Path.GetDirectoryName(basePath) ?? basePath;
        var combined = Path.Combine(baseDir, relativePath);
        return Path.GetFullPath(combined);
    }

    private async Task<string> ReadFileContent(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = _fileProvider.GetFileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"File not found: {filePath}");

        await using var stream = fileInfo.CreateReadStream();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
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
            var idlContent = await ReadFileContent(importPath, cancellationToken);
            var parseTree = ParseIdl(idlContent);

            var nestedContext = new IdlParsingContext
            {
                FilePath = importPath
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
                        importedMessages[prop.Name] = prop.Value;
                    }
                }
            }
            else
            {
                nestedContext.DefaultNamespace = parseTree.@namespace?.@namespace?.GetText();

                foreach (var namedSchema in parseTree._namedSchemas)
                {
                    var schemaJson = TranslateNamedSchema(namedSchema, nestedContext);

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
            var protocolJson = await ReadFileContent(importPath, cancellationToken);
            var protocolObj = JObject.Parse(protocolJson);

            // Import types
            if (protocolObj.TryGetValue("types", out var typesToken) && typesToken is JArray typesArray)
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

            // Import messages
            if (protocolObj.TryGetValue("messages", out var messagesToken) && messagesToken is JObject messagesObj)
            {
                foreach (var prop in messagesObj.Properties())
                    importedMessages[prop.Name] = prop.Value;
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
            var schemaJson = await ReadFileContent(importPath, cancellationToken);
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

        var nameStr = name.ToString();
        var ns = schema.TryGetValue("namespace", out var nsToken)
            ? nsToken.ToString()
            : parsingContext.DefaultNamespace;

        return !string.IsNullOrEmpty(ns)
            ? $"{ns}.{nameStr}"
            : nameStr;
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
