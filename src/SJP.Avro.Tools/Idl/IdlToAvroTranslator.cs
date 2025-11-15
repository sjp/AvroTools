using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    private readonly string _filePath;
    private readonly IFileProvider _fileProvider;
    private readonly Dictionary<string, JObject> _namedSchemas = [];
    private readonly HashSet<string> _processedImports = [];
    private readonly HashSet<string> _processedSchemas = [];
    private readonly HashSet<string> _inlinedForwardRefs = [];
    private bool _trackForwardReferences = false;
    private string? _defaultNamespace;

    /// <summary>
    /// TODO
    /// </summary>
    public IdlToAvroTranslator(string filePath = "memory", IFileProvider? fileProvider = null)
    {
        _filePath = filePath;
        _fileProvider = fileProvider ?? new PhysicalFileProvider(Directory.GetCurrentDirectory());
    }

    /// <summary>
    /// Attempts to translate to either Protocol or Schema, returning the appropriate type.
    /// </summary>
    public static IdlParseResult ParseIdl(string idlContent, string sourceName = "memory", IFileProvider? fileProvider = null)
    {
        var parseTree = ParseIdl(idlContent);
        var translator = new IdlToAvroTranslator(sourceName, fileProvider);
        return translator.Translate(parseTree);
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

    private AvroSchema TranslateSchema(IdlParser.IdlFileContext context)
    {
        var schemaJson = TranslateSchemaToJson(context);
        return AvroSchema.Parse(schemaJson.ToString());
    }

    private JToken TranslateSchemaToJson(IdlParser.IdlFileContext context)
    {
        _namedSchemas.Clear();
        _processedImports.Clear();
        _processedSchemas.Clear();
        _inlinedForwardRefs.Clear();

        _defaultNamespace = context.@namespace?.@namespace?.GetText();

        // Process imports for schemas
        var importedTypes = new List<JObject>();
        var importedMessages = new JObject();

        foreach (var import in context._imports)
        {
            ProcessImport(import, importedTypes, importedMessages);
        }

        // FIRST PASS: Cache imported types and any named schemas in the file for reference resolution
        _trackForwardReferences = false;

        foreach (var importedType in importedTypes)
        {
            var name = GetSchemaName(importedType);
            if (!string.IsNullOrEmpty(name))
            {
                _namedSchemas[name] = importedType;
            }
        }

        // Cache any named schemas defined in this file
        foreach (var namedSchema in context._namedSchemas)
        {
            var schemaJson = TranslateNamedSchema(namedSchema);
            var name = GetSchemaName(schemaJson);
            if (!string.IsNullOrEmpty(name))
            {
                _namedSchemas[name] = schemaJson;
            }
        }

        // SECOND PASS: Now translate with forward reference tracking enabled
        _trackForwardReferences = true;

        JToken mainSchemaJson;
        if (context.mainSchema != null)
        {
            mainSchemaJson = TranslateFullType(context.mainSchema.mainSchema);
        }
        else if (context._namedSchemas.Count > 0)
        {
            var schema = context._namedSchemas[0];
            mainSchemaJson = TranslateNamedSchema(schema);
        }
        else
        {
            throw new InvalidOperationException(
                "The IDL file does not contain a schema. Use TranslateToProtocol for protocols.");
        }

        return mainSchemaJson;
    }

    /// <summary>
    /// Attempts to translate to either Protocol or Schema, returning the appropriate type.
    /// </summary>
    public IdlParseResult Translate(IdlParser.IdlFileContext context)
    {
        if (context.protocol != null)
        {
            var protocol = TranslateProtocol(context.protocol);
            return IdlParseResult.Protocol(protocol);
        }
        else
        {
            var schema = TranslateSchema(context);
            return IdlParseResult.Schema(schema);
        }
    }

    private AvroProtocol TranslateProtocol(IdlParser.ProtocolDeclarationContext context)
    {
        var protocolJson = TranslateProtocolToJson(context);
        var protocolJsonText = protocolJson.ToString();
        return AvroProtocol.Parse(protocolJsonText);
    }

    private JObject TranslateProtocolToJson(IdlParser.ProtocolDeclarationContext context)
    {
        _namedSchemas.Clear();
        _processedImports.Clear();
        _processedSchemas.Clear();
        _inlinedForwardRefs.Clear();

        var protocolName = context.name.GetText();
        var doc = context.doc.ExtractDocumentation();
        var properties = TranslateProperties(context._schemaProperties);

        // Extract namespace from properties
        _defaultNamespace = GetNamespaceFromProperties(properties);

        var body = context.body;

        // Process imports
        var importedTypes = new List<JObject>();
        var importedMessages = new JObject();

        foreach (var import in body._imports)
        {
            ProcessImport(import, importedTypes, importedMessages);
        }

        // FIRST PASS: Cache all named schemas for forward reference resolution
        // Don't track forward references in this pass
        _trackForwardReferences = false;
        foreach (var namedSchema in body._namedSchemas)
        {
            var schemaJson = TranslateNamedSchema(namedSchema);

            // Cache for reference resolution
            var name = GetSchemaName(schemaJson);
            if (!string.IsNullOrEmpty(name))
            {
                _namedSchemas[name] = schemaJson;
            }
        }

        // SECOND PASS: Regenerate schemas with proper references now that all are cached
        // Enable forward reference tracking for this pass
        _trackForwardReferences = true;

        // Mark imported types as already processed first
        foreach (var importedType in importedTypes)
        {
            var name = GetSchemaName(importedType);
            if (!string.IsNullOrEmpty(name))
            {
                _processedSchemas.Add(name);
            }
        }

        var types = new List<JObject>();
        types.AddRange(importedTypes); // Add imported types first

        foreach (var namedSchema in body._namedSchemas)
        {
            // First, get the schema name from the declaration so we can mark it as processed
            var simpleName = GetNamedSchemaName(namedSchema);

            // Check if this schema has an explicit namespace
            var schemaProperties = namedSchema.fixedDeclaration()?._schemaProperties
                ?? namedSchema.enumDeclaration()?._schemaProperties
                ?? namedSchema.recordDeclaration()?._schemaProperties;
            var schemaProps = schemaProperties != null ? TranslateProperties(schemaProperties) : [];
            var explicitNamespace = schemaProps.TryGetValue("namespace", out var ns) ? ns.ToString() : null;

            // Construct the full name using explicit namespace if provided, otherwise use default
            var schemaNamespace = explicitNamespace ?? _defaultNamespace;
            var fullName = string.IsNullOrEmpty(schemaNamespace) ? simpleName : $"{schemaNamespace}.{simpleName}";

            // Mark this schema as processed BEFORE translating it
            // This ensures self-references work correctly (e.g., array<Node> in Node)
            if (!string.IsNullOrEmpty(fullName))
            {
                _processedSchemas.Add(fullName);
            }

            // Now translate the schema - references to this schema (including self-references)
            // will use name strings since it's already marked as processed
            var schemaJson = TranslateNamedSchema(namedSchema);

            // Only add to types array if it wasn't inlined as a forward reference elsewhere
            if (!string.IsNullOrEmpty(fullName) && !_inlinedForwardRefs.Contains(fullName))
            {
                types.Add(schemaJson);
            }
        }

        // Process messages (combine imported and declared)
        var messages = new JObject();

        // Add imported messages first
        foreach (var prop in importedMessages.Properties())
        {
            messages[prop.Name] = prop.Value;
        }

        // Add declared messages
        foreach (var message in body._messages)
        {
            var messageName = IdlName.EscapeName(message.name.GetText());
            var messageJson = TranslateMessage(message);
            messages[messageName] = messageJson;
        }

        // Build protocol JSON
        var protocolJson = new JObject
        {
            ["protocol"] = protocolName
        };

        if (!string.IsNullOrWhiteSpace(_defaultNamespace))
        {
            protocolJson["namespace"] = _defaultNamespace;
        }

        if (!string.IsNullOrWhiteSpace(doc))
        {
            protocolJson["doc"] = doc;
        }

        if (types.Count > 0)
        {
            protocolJson["types"] = new JArray(types);
        }

        if (messages.Count > 0)
        {
            protocolJson["messages"] = messages;
        }

        var nonNamespaceProperties = properties.Where(p => p.Key != "namespace");
        foreach (var prop in nonNamespaceProperties)
        {
            var propName = IdlName.EscapeName(prop.Key);
            protocolJson[propName] = prop.Value;
        }

        return protocolJson;
    }

    private JObject TranslateNamedSchema(IdlParser.NamedSchemaDeclarationContext context)
    {
        if (context.fixedDeclaration() != null)
        {
            return TranslateFixed(context.fixedDeclaration());
        }
        else if (context.enumDeclaration() != null)
        {
            return TranslateEnum(context.enumDeclaration());
        }
        else if (context.recordDeclaration() != null)
        {
            return TranslateRecord(context.recordDeclaration());
        }

        throw new InvalidOperationException("Unknown named schema type");
    }

    private JObject TranslateFixed(IdlParser.FixedDeclarationContext context)
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
        {
            fixedJson["namespace"] = explicitNamespace;
        }
        else if (!string.IsNullOrWhiteSpace(_defaultNamespace))
        {
            fixedJson["namespace"] = _defaultNamespace;
        }

        if (!string.IsNullOrWhiteSpace(doc))
        {
            fixedJson["doc"] = doc;
        }

        var nonNamespaceProperties = properties.Where(p => p.Key != "namespace");
        foreach (var prop in nonNamespaceProperties)
        {
            var propName = IdlName.EscapeName(prop.Key);
            fixedJson[propName] = prop.Value;
        }

        return fixedJson;
    }

    private JObject TranslateEnum(IdlParser.EnumDeclarationContext context)
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
        {
            enumJson["namespace"] = explicitNamespace;
        }
        else if (!string.IsNullOrWhiteSpace(_defaultNamespace))
        {
            enumJson["namespace"] = _defaultNamespace;
        }

        if (!string.IsNullOrWhiteSpace(doc))
        {
            enumJson["doc"] = doc;
        }

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

    private JObject TranslateRecord(IdlParser.RecordDeclarationContext context)
    {
        var name = IdlName.EscapeName(context.name.GetText());
        var doc = context.doc.ExtractDocumentation();
        var properties = TranslateProperties(context._schemaProperties);

        var fields = new JArray();
        foreach (var fieldDecl in context.body._fields)
        {
            foreach (var varDecl in fieldDecl._variableDeclarations)
            {
                var field = TranslateField(fieldDecl, varDecl);
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
        {
            recordJson["namespace"] = explicitNamespace;
        }
        else if (!string.IsNullOrWhiteSpace(_defaultNamespace))
        {
            recordJson["namespace"] = _defaultNamespace;
        }

        if (!string.IsNullOrWhiteSpace(doc))
        {
            recordJson["doc"] = doc;
        }

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
        IdlParser.VariableDeclarationContext varDecl)
    {
        var fieldName = IdlName.EscapeName(varDecl.fieldName.GetText());
        var fieldType = TranslateFullType(fieldDecl.fieldType);
        var doc = fieldDecl.doc.ExtractDocumentation()
            ?? varDecl.doc.ExtractDocumentation();
        var properties = TranslateProperties(varDecl._schemaProperties);

        var field = new JObject
        {
            ["name"] = fieldName,
            ["type"] = fieldType
        };

        if (!string.IsNullOrWhiteSpace(doc))
        {
            field["doc"] = doc;
        }

        if (varDecl.defaultValue != null)
        {
            field["default"] = TranslateJsonValue(varDecl.defaultValue);
        }

        foreach (var prop in properties)
        {
            var propName = IdlName.EscapeName(prop.Key);
            field[propName] = prop.Value;
        }

        return field;
    }

    private JObject TranslateMessage(IdlParser.MessageDeclarationContext context)
    {
        var doc = context.doc.ExtractDocumentation();
        var properties = TranslateProperties(context._schemaProperties);
        var isOneway = context.oneway != null;

        // Request parameters
        var request = new JArray();
        foreach (var param in context._formalParameters)
        {
            var paramName = IdlName.EscapeName(param.parameter.fieldName.GetText());
            var paramType = TranslateFullType(param.parameterType);
            var paramDoc = param.doc.ExtractDocumentation();

            var requestParam = new JObject
            {
                ["name"] = paramName,
                ["type"] = paramType
            };

            if (!string.IsNullOrWhiteSpace(paramDoc))
            {
                requestParam["doc"] = paramDoc;
            }

            if (param.parameter.defaultValue != null)
            {
                requestParam["default"] = TranslateJsonValue(param.parameter.defaultValue);
            }

            request.Add(requestParam);
        }

        // Response type
        JToken response;
        if (context.returnType.Void() != null || isOneway)
        {
            response = "null";
        }
        else
        {
            response = TranslatePlainType(context.returnType.plainType());
        }

        var message = new JObject
        {
            ["request"] = request,
            ["response"] = response
        };

        if (!string.IsNullOrWhiteSpace(doc))
        {
            message["doc"] = doc;
        }

        if (isOneway)
        {
            message["one-way"] = true;
        }

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

    private JToken TranslateFullType(IdlParser.FullTypeContext context)
    {
        var properties = TranslateProperties(context._schemaProperties);
        var typeToken = TranslatePlainType(context.plainType());

        if (properties.Count == 0)
        {
            return typeToken;
        }

        // If we have properties, we need to wrap in an object
        if (typeToken is JObject obj)
        {
            foreach (var prop in properties)
            {
                var propName = IdlName.EscapeName(prop.Key);
                obj[propName] = prop.Value;
            }
            return obj;
        }
        else
        {
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
    }

    private JToken TranslatePlainType(IdlParser.PlainTypeContext context)
    {
        if (context.arrayType() != null)
        {
            return TranslateArrayType(context.arrayType());
        }
        else if (context.mapType() != null)
        {
            return TranslateMapType(context.mapType());
        }
        else if (context.unionType() != null)
        {
            return TranslateUnionType(context.unionType());
        }
        else if (context.nullableType() != null)
        {
            return TranslateNullableType(context.nullableType());
        }

        throw new InvalidOperationException("Unknown plain type");
    }

    private JObject TranslateArrayType(IdlParser.ArrayTypeContext context)
    {
        var itemType = TranslateFullType(context.elementType);
        return new JObject
        {
            ["type"] = "array",
            ["items"] = itemType
        };
    }

    private JObject TranslateMapType(IdlParser.MapTypeContext context)
    {
        var valueType = TranslateFullType(context.valueType);
        return new JObject
        {
            ["type"] = "map",
            ["values"] = valueType
        };
    }

    private JArray TranslateUnionType(IdlParser.UnionTypeContext context)
    {
        var fullTypes = context._types
            .Select(TranslateFullType)
            .ToList();
        return new JArray(fullTypes);
    }

    private JToken TranslateNullableType(IdlParser.NullableTypeContext context)
    {
        var baseType = TranslatePrimitiveOrReference(context);
        return context.QuestionMark() != null
            ? new JArray { "null", baseType }
            : baseType;
    }

    private JToken TranslatePrimitiveOrReference(IdlParser.NullableTypeContext context)
    {
        if (context.primitiveType() != null)
        {
            return TranslatePrimitiveType(context.primitiveType());
        }
        else if (context.referenceName != null)
        {
            return TranslateReferenceType(context);
        }

        throw new InvalidOperationException("Unknown nullable type");
    }

    private JToken TranslateReferenceType(IdlParser.NullableTypeContext context)
    {
        var refName = context.referenceName.GetText();
        var fullName = ResolveFullTypeName(refName);

        // Check if this type has already been added to the types array
        if (_processedSchemas.Contains(fullName))
        {
            // Type is already in the types array, use a name reference
            // If the refName was already qualified (contains '.'), use it as-is
            // Otherwise, check if it's in the same namespace as the default namespace
            if (refName.Contains('.'))
            {
                return refName;
            }
            else
            {
                // Check if the type's namespace matches the default namespace
                // If so, we can use just the simple name; otherwise use the full name
                var typeNamespace = fullName.Contains('.')
                    ? fullName[..fullName.LastIndexOf('.')]
                    : null;

                if (typeNamespace == _defaultNamespace)
                {
                    return refName; // Same namespace, use simple name
                }
                else
                {
                    return fullName; // Different namespace, use fully-qualified name
                }
            }
        }
        // Check if this type has already been inlined as a forward reference
        else if (_inlinedForwardRefs.Contains(fullName))
        {
            // Already inlined once, use name reference for subsequent uses
            // Apply the same logic as above
            if (refName.Contains('.'))
            {
                return refName;
            }
            else
            {
                var typeNamespace = fullName.Contains('.')
                    ? fullName[..fullName.LastIndexOf('.')]
                    : null;

                if (typeNamespace == _defaultNamespace)
                {
                    return refName;
                }
                else
                {
                    return fullName;
                }
            }
        }
        else if (_trackForwardReferences && _namedSchemas.TryGetValue(fullName, out var schema))
        {
            // Forward reference - type is defined later, inline the full definition
            // Mark it so we don't add it to the types array later and so subsequent
            // references within the same protocol use the name instead of inlining again
            _inlinedForwardRefs.Add(fullName);

            // Clone the schema for inlining
            var inlinedSchema = (JObject)schema.DeepClone();

            // Recursively process the inlined schema to replace any string references
            // with inlined schemas if they are also forward references
            ProcessForwardReferencesInSchema(inlinedSchema);

            return inlinedSchema;
        }
        else
        {
            // Type not found in cache, return the reference name anyway
            return refName;
        }
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

    private static JToken? TranslateLogicalType(IdlParser.PrimitiveTypeContext context)
    {
        var typeNameToken = context.typeName;
        if (typeNameToken == null)
        {
            throw new InvalidOperationException("Logical type has no type name");
        }

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

        // consider enabling alternate encodings for uuid
        // alternate option
        // var uuidFixed = new JObject
        // {
        //     ["type"] = "fixed",
        //     ["size"] = 16,
        //     ["logicalType"] = "uuid"
        // };

        return text switch
        {
            "uuid" => new JObject
            {
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
        {
            return TranslateJsonLiteral(context.jsonLiteral());
        }
        else if (context.jsonObject() != null)
        {
            return TranslateJsonObject(context.jsonObject());
        }
        else if (context.jsonArray() != null)
        {
            return TranslateJsonArray(context.jsonArray());
        }

        throw new InvalidOperationException("Unknown JSON value type");
    }

    private static JToken TranslateJsonLiteral(IdlParser.JsonLiteralContext context)
    {
        if (context.StringLiteral() != null)
        {
            var text = context.StringLiteral().GetText();
            // Remove quotes
            return text[1..^1];
        }
        else if (context.IntegerLiteral() != null)
        {
            return long.Parse(context.IntegerLiteral().GetText());
        }
        else if (context.FloatingPointLiteral() != null)
        {
            return double.Parse(context.FloatingPointLiteral().GetText(), CultureInfo.InvariantCulture);
        }
        else if (context.BTrue() != null)
        {
            return true;
        }
        else if (context.BFalse() != null)
        {
            return false;
        }
        else if (context.Null() != null)
        {
            return JValue.CreateNull();
        }

        throw new InvalidOperationException("Unknown JSON literal type");
    }

    private JObject TranslateJsonObject(IdlParser.JsonObjectContext context)
    {
        var obj = new JObject();
        foreach (var pair in context._jsonPairs)
        {
            var key = pair.name.Text;
            // Remove quotes from key
            if (key.StartsWith('"') && key.EndsWith('"'))
            {
                key = key[1..^1];
            }
            obj[key] = TranslateJsonValue(pair.value);
        }
        return obj;
    }

    private JArray TranslateJsonArray(IdlParser.JsonArrayContext context)
    {
        var array = new JArray();
        foreach (var value in context._jsonValues)
        {
            array.Add(TranslateJsonValue(value));
        }
        return array;
    }

    private void ProcessImport(
        IdlParser.ImportStatementContext import,
        List<JObject> importedTypes,
        JObject importedMessages)
    {
        var importType = import.importType.Text;
        var location = import.location.Text;

        // Remove quotes from location
        if (location.StartsWith('"') && location.EndsWith('"'))
        {
            location = location[1..^1];
        }

        // Prevent circular imports
        var importPath = ResolveRelativePath(_filePath, location);
        if (_processedImports.Contains(importPath))
        {
            return;
        }
        _processedImports.Add(importPath);

        switch (importType.ToLowerInvariant())
        {
            case "idl":
                ProcessIdlImport(importPath, importedTypes, importedMessages);
                break;
            case "protocol":
                ProcessProtocolImport(importPath, importedTypes, importedMessages);
                break;
            case "schema":
                ProcessSchemaImport(importPath, importedTypes);
                break;
        }
    }

    private static string ResolveRelativePath(string basePath, string relativePath)
    {
        // If basePath is "memory" or similar non-file paths, just return the relative path
        if (basePath == "memory" || !Path.IsPathRooted(basePath))
        {
            return relativePath;
        }

        var baseDir = Path.GetDirectoryName(basePath) ?? basePath;
        var combined = Path.Combine(baseDir, relativePath);
        return Path.GetFullPath(combined);
    }

    private string ReadFileContent(string filePath)
    {
        var fileInfo = _fileProvider.GetFileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        using var stream = fileInfo.CreateReadStream();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void ProcessIdlImport(
        string importPath,
        List<JObject> importedTypes,
        JObject importedMessages)
    {
        try
        {
            var idlContent = ReadFileContent(importPath);
            var parseTree = ParseIdl(idlContent);

            var translator = new IdlToAvroTranslator(importPath, _fileProvider);
            translator._processedImports.UnionWith(_processedImports); // Carry forward processed imports

            if (parseTree.protocol != null)
            {
                // Get protocol as JSON without parsing to avoid validation issues with incomplete type references
                var protocolJson = translator.TranslateProtocolToJson(parseTree.protocol);

                // Add imported types
                if (protocolJson.TryGetValue("types", out var typesToken) && typesToken is JArray typesArray)
                {
                    foreach (var type in typesArray.OfType<JObject>())
                    {
                        importedTypes.Add(type);

                        // Cache for reference resolution
                        var name = GetSchemaName(type);
                        if (!string.IsNullOrEmpty(name))
                        {
                            _namedSchemas[name] = type;
                        }
                    }
                }

                // Add imported messages
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
                // Handle standalone schema import
                // The imported file might be a standalone schema with named types
                translator._defaultNamespace = parseTree.@namespace?.@namespace?.GetText();

                // Process any named schemas defined in the imported file
                foreach (var namedSchema in parseTree._namedSchemas)
                {
                    var schemaJson = translator.TranslateNamedSchema(namedSchema);

                    // Ensure the schema has a namespace field if one was set at the file level
                    // and the schema doesn't already have an explicit namespace
                    if (!string.IsNullOrEmpty(translator._defaultNamespace) && !schemaJson.ContainsKey("namespace"))
                    {
                        schemaJson["namespace"] = translator._defaultNamespace;
                    }

                    importedTypes.Add(schemaJson);

                    // Cache for reference resolution
                    var name = GetSchemaName(schemaJson);
                    if (!string.IsNullOrEmpty(name))
                    {
                        _namedSchemas[name] = schemaJson;
                    }
                }
            }

            // Update processed imports from nested translator
            _processedImports.UnionWith(translator._processedImports);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to import IDL from '{importPath}': {ex.Message}", ex);
        }
    }

    private void ProcessProtocolImport(
        string importPath,
        List<JObject> importedTypes,
        JObject importedMessages)
    {
        try
        {
            var protocolJson = ReadFileContent(importPath);
            var protocolObj = JObject.Parse(protocolJson);

            // Import types
            if (protocolObj.TryGetValue("types", out var typesToken) && typesToken is JArray typesArray)
            {
                foreach (var type in typesArray.OfType<JObject>())
                {
                    importedTypes.Add(type);

                    // Cache for reference resolution
                    var name = GetSchemaName(type);
                    if (!string.IsNullOrEmpty(name))
                    {
                        _namedSchemas[name] = type;
                    }
                }
            }

            // Import messages
            if (protocolObj.TryGetValue("messages", out var messagesToken) && messagesToken is JObject messagesObj)
            {
                foreach (var prop in messagesObj.Properties())
                {
                    importedMessages[prop.Name] = prop.Value;
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to import protocol from '{importPath}': {ex.Message}", ex);
        }
    }

    private void ProcessSchemaImport(
        string importPath,
        List<JObject> importedTypes)
    {
        try
        {
            var schemaJson = ReadFileContent(importPath);
            var schemaObj = JObject.Parse(schemaJson);

            importedTypes.Add(schemaObj);

            // Cache for reference resolution
            var name = GetSchemaName(schemaObj);
            if (!string.IsNullOrEmpty(name))
            {
                _namedSchemas[name] = schemaObj;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to import schema from '{importPath}': {ex.Message}", ex);
        }
    }

    private Dictionary<string, JToken> TranslateProperties(
        IList<IdlParser.SchemaPropertyContext> properties)
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

    private string? GetSchemaName(JObject schema)
    {
        if (!schema.TryGetValue("name", out var name))
            return null;

        var nameStr = name.ToString();
        var ns = schema.TryGetValue("namespace", out var nsToken)
            ? nsToken.ToString()
            : _defaultNamespace;

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

    private string ResolveFullTypeName(string typeName)
    {
        if (typeName.Contains('.'))
        {
            // already qualified
            return typeName;
        }

        // Try with default namespace first
        if (!string.IsNullOrEmpty(_defaultNamespace))
        {
            var fullName = $"{_defaultNamespace}.{typeName}";
            if (_namedSchemas.ContainsKey(fullName))
            {
                return fullName;
            }
        }

        // Return the simple name if not found with namespace
        return typeName;
    }

    /// <summary>
    /// Recursively processes a schema to replace string references with inlined schemas
    /// for forward references that haven't been processed yet.
    /// </summary>
    private void ProcessForwardReferencesInSchema(JToken schema)
    {
        if (schema is JObject obj)
        {
            // Check if this is a record/error with fields
            if (obj.TryGetValue("fields", out var fieldsToken) && fieldsToken is JArray fields)
            {
                for (var i = 0; i < fields.Count; i++)
                {
                    if (fields[i] is JObject field && field.TryGetValue("type", out var fieldType))
                    {
                        var replacedType = ProcessForwardReferenceInType(fieldType);
                        if (replacedType != fieldType)
                        {
                            field["type"] = replacedType;
                        }
                    }
                }
            }
            // Check if this is an array with items
            else if (obj.TryGetValue("items", out var itemsToken))
            {
                var replacedItems = ProcessForwardReferenceInType(itemsToken);
                if (replacedItems != itemsToken)
                {
                    obj["items"] = replacedItems;
                }
            }
            // Check if this is a map with values
            else if (obj.TryGetValue("values", out var valuesToken))
            {
                var replacedValues = ProcessForwardReferenceInType(valuesToken);
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
                var replacedElement = ProcessForwardReferenceInType(element);
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
    private JToken ProcessForwardReferenceInType(JToken typeToken)
    {
        // If it's a string reference, check if it needs to be inlined
        if (typeToken is JValue val && val.Type == JTokenType.String)
        {
            var typeName = val.ToString();
            var fullName = ResolveFullTypeName(typeName);

            // Check if this is a forward reference that needs inlining
            if (!_processedSchemas.Contains(fullName) &&
                !_inlinedForwardRefs.Contains(fullName) &&
                _namedSchemas.TryGetValue(fullName, out var schema))
            {
                // This is a forward reference, inline it
                _inlinedForwardRefs.Add(fullName);
                var inlinedSchema = (JObject)schema.DeepClone();

                // Recursively process this inlined schema
                ProcessForwardReferencesInSchema(inlinedSchema);

                return inlinedSchema;
            }
        }
        // If it's a complex type (object or array), recursively process it
        else if (typeToken is JObject || typeToken is JArray)
        {
            ProcessForwardReferencesInSchema(typeToken);
        }

        return typeToken;
    }
}
