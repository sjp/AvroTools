using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SJP.Avro.Tools.Idl;
using SJP.Avro.Tools.Schema.Model;
using Superpower.Model;

namespace SJP.Avro.Tools;

/// <summary>
/// A compiler used to generate a JSON protocol from an Avro IDL protocol.
/// </summary>
public class IdlCompiler : IIdlCompiler
{
    private readonly IFileProvider _fileProvider;

    /// <summary>
    /// Constructs a compiler used to generate a JSON protocol from an Avro IDL protocol.
    /// </summary>
    /// <param name="fileProvider">A file provider, primarily used to construct and access relative paths.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fileProvider"/> is <c>null</c>.</exception>
    public IdlCompiler(IFileProvider fileProvider)
    {
        _fileProvider = fileProvider ?? throw new ArgumentNullException(nameof(fileProvider));
    }

    private Idl.Model.Protocol ParseImportedIdl(string parentFilePath, string idlPath)
    {
        var tokenizer = new IdlTokenizer();

        var input = _fileProvider.GetFileContents(parentFilePath, idlPath);
        var tokenizeResult = tokenizer.TryTokenize(input);
        var tokens = tokenizeResult.Value.ToList();

        var commentFreeTokens = tokens.Where(t => t.Kind != IdlToken.Comment).ToArray();
        var tokenList = new TokenList<IdlToken>(commentFreeTokens);

        var result = IdlTokenParsers.Protocol(tokenList);

        return result.Value;
    }

    private JObject ParseImportedSchema(string parentFilePath, string idlPath)
    {
        var input = _fileProvider.GetFileContents(parentFilePath, idlPath);
        return JObject.Parse(input);
    }

    /// <summary>
    /// Compiles an IDL protocol into a JSON definition of the same protocol.
    /// </summary>
    /// <param name="filePath">A path representing the source location of <paramref name="protocol"/>.</param>
    /// <param name="protocol">A parsed protocol definition from an IDL document.</param>
    /// <returns>A string containing JSON text that represents an Avro protocol.</returns>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="filePath"/> or <paramref name="protocol"/> is <c>null</c>.</exception>
    public string Compile(string filePath, Idl.Model.Protocol protocol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(protocol);

        var protocolNamespace = GetNamespaceFromProperties(protocol.Properties);

        var idlImports = protocol.Imports
            .Where(i => i.Type == Idl.Model.ImportType.Idl)
            .Select(i => ParseImportedIdl(filePath, i.Path))
            .ToList();

        var parsedMessages = new Dictionary<Idl.Model.Identifier, Idl.Model.MessageDeclaration>(protocol.Messages);
        foreach (var message in idlImports.SelectMany(i => i.Messages))
            parsedMessages[message.Key] = message.Value;

        var messageDtos = parsedMessages
            .Select(kv => new KeyValuePair<string, JObject>(kv.Key.Value, MapToMessageDto(protocol, kv.Value)))
            .ToDictionary();

        // finally append the contents of the "messages" of the imported protocols
        var typePropertyLookup = new Dictionary<string, List<Idl.Model.Property>>();
        var referenceTypeFieldProperties = protocol.Records
            .Concat(idlImports.SelectMany(i => i.Records))
            .SelectMany(r => r.Fields)
            .Select(f => f.Type as Idl.Model.ReferenceType)
            .Where(t => t?.Properties.Any() == true)
            .Select(t => t!)
            .ToList();
        foreach (var fieldType in referenceTypeFieldProperties)
        {
            if (!typePropertyLookup.TryGetValue(fieldType.Name.Value, out var props))
                props = [];

            props.AddRange(fieldType.Properties);
            typePropertyLookup[fieldType.Name.Value] = props;
        }

        var protocolImports = protocol.Imports
            .Where(i => i.Type == Idl.Model.ImportType.Protocol)
            .Select(i => ParseImportedSchema(filePath, i.Path))
            .ToList();

        var orderedTypeDeclarations = GetOrderedTypeDeclarations(protocol);
        var typeDtos = orderedTypeDeclarations
            .SelectMany(t => MapTypeDeclarationToDto(filePath, protocol, t))
            .ToList();

        // same for messages via protocol
        foreach (var importedProtocol in protocolImports)
        {
            if (!importedProtocol.TryGetValue("messages", out var protocolMessages))
            {
                continue;
            }

            if (protocolMessages is not JObject messageObj)
            {
                continue;
            }

            var importedNames = messageObj.Properties().Select(p => p.Name).ToList();
            foreach (var name in importedNames)
            {
                var importedValue = messageObj[name];
                if (importedValue is not JObject importedMessage)
                {
                    continue;
                }

                messageDtos[name] = importedMessage;
            }
        }

        foreach (var typeDto in typeDtos)
        {
            var ns = typeDto["namespace"]?.ToString() ?? string.Empty;
            var name = typeDto["name"]?.ToString() ?? string.Empty;

            var resolvedName = !string.IsNullOrWhiteSpace(ns)
                ? ns + "." + name
                : name;

            if (!typePropertyLookup.TryGetValue(resolvedName, out var props))
                continue;

            AttachProperties(typeDto, props);
        }

        // now remove namespace where it matches the protocol namespace
        // as it's redundant
        if (!string.IsNullOrWhiteSpace(protocolNamespace))
        {
            foreach (var typeDto in typeDtos)
            {
                var ns = typeDto["namespace"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(ns))
                    continue;

                if (ns == protocolNamespace)
                    typeDto.Remove("namespace");
            }
        }

        var dto = new Protocol
        {
            Documentation = protocol.Documentation?.Value,
            Types = typeDtos,
            Messages = messageDtos,
            Name = protocol.Name.Value
        };

        var jobj = JObject.FromObject(dto);
        AttachProperties(jobj, protocol.Properties);

        var json = JsonConvert.SerializeObject(jobj, Formatting.Indented);

        return json;
    }

    private static IEnumerable<Idl.Model.NamedSchemaDeclaration> GetOrderedTypeDeclarations(Idl.Model.Protocol protocol)
    {
        return protocol.Imports
            .Where(i => i.Type == Idl.Model.ImportType.Schema || i.Type == Idl.Model.ImportType.Idl)
            .Select(i => i as Idl.Model.NamedSchemaDeclaration)
            .Concat(protocol.Enums)
            .Concat(protocol.Fixeds)
            .Concat(protocol.Records)
            .Concat(protocol.Errors)
            .OrderBy(x => x.Position)
            .ToList();
    }

    private IEnumerable<JObject> MapTypeDeclarationToDto(string baseFilePath, Idl.Model.Protocol protocol, Idl.Model.NamedSchemaDeclaration typeDeclaration)
    {
        return typeDeclaration switch
        {
            Idl.Model.EnumDeclaration e => [MapToEnumDto(e)],
            Idl.Model.FixedDeclaration f => [MapToFixedDto(f)],
            Idl.Model.RecordDeclaration r => [MapToRecordDto(protocol, r)],
            Idl.Model.ErrorDeclaration e => [MapToErrorDto(protocol, e)],
            Idl.Model.ImportDeclaration i => MapImportDeclarationToDto(baseFilePath, i),
            _ => []
        };
    }

    private IEnumerable<JObject> MapImportDeclarationToDto(string baseFilePath, Idl.Model.ImportDeclaration import)
    {
        if (import.Type == Idl.Model.ImportType.Idl)
        {
            var resolvedNewPath = _fileProvider.CreatePath(baseFilePath, import.Path);
            var importedIdl = ParseImportedIdl(baseFilePath, import.Path);
            var importIdlNs = GetNamespaceFromProperties(importedIdl.Properties);

            var orderedTypeDeclarations = GetOrderedTypeDeclarations(importedIdl);
            return orderedTypeDeclarations
                .SelectMany(t => MapTypeDeclarationToDto(resolvedNewPath, importedIdl, t))
                .Select(t =>
                {
                    if (string.IsNullOrWhiteSpace(importIdlNs) || t["namespace"] != null)
                        return t;

                    // explicitly setting a namespace, will be filtered out if needed
                    // during a simplification step later
                    t["namespace"] = importIdlNs;

                    return t;
                })
                .ToList();
        }

        if (import.Type == Idl.Model.ImportType.Schema)
        {
            var parsedSchema = ParseImportedSchema(baseFilePath, import.Path);

            // minor tweak, name can be imported fully qualified, so decompose where possible
            var nameText = parsedSchema["name"]?.ToString() ?? string.Empty;
            var pieces = nameText.Split('.', StringSplitOptions.RemoveEmptyEntries);

            if (parsedSchema["namespace"] == null && pieces.Length > 1)
            {
                var ns = pieces[..^1].Join(".");
                var newName = pieces[^1];

                parsedSchema["namespace"] = ns;
                parsedSchema["name"] = newName;
            }

            return [parsedSchema];
        }

        return [];
    }

    private static JObject MapToEnumDto(Idl.Model.EnumDeclaration enumType)
    {
        var dto = new EnumType
        {
            Name = enumType.Name.Value,
            Documentation = enumType.Comment?.Value,
            DefaultValue = enumType.DefaultValue?.Value,
            Namespace = GetNamespaceFromProperties(enumType.Properties),
            Aliases = GetAliasesFromProperties(enumType.Properties),
            Symbols = enumType.Members.Select(m => m.Value).ToList()
        };

        var jobj = JObject.FromObject(dto);
        AttachProperties(jobj, enumType.Properties);

        return jobj;
    }

    private static JObject MapToFixedDto(Idl.Model.FixedDeclaration fixedType)
    {
        var dto = new FixedType
        {
            Name = fixedType.Name.Value,
            Documentation = fixedType.Comment?.Value,
            Namespace = GetNamespaceFromProperties(fixedType.Properties),
            Aliases = GetAliasesFromProperties(fixedType.Properties),
            Size = fixedType.Size
        };

        var jobj = JObject.FromObject(dto);
        AttachProperties(jobj, fixedType.Properties);

        return jobj;
    }

    private static JToken GenerateTypeTokens(Idl.Model.Protocol protocol, Idl.Model.AvroType avroType)
    {
        // primitive
        if (avroType is Idl.Model.PrimitiveType primitiveType)
        {
            // special case:
            // if we have a 'void', replace with 'null'.
            var primitiveTypeName = primitiveType.Name == "void"
                ? "null"
                : primitiveType.Name;

            if (!avroType.Properties.Any())
            {
                return JToken.FromObject(primitiveTypeName);
            }

            var result = new JObject
            {
                ["type"] = primitiveTypeName
            };

            AttachProperties(result, avroType.Properties);
            return result;
        }

        if (avroType is Idl.Model.LogicalType logicalType)
        {
            if (logicalType is Idl.Model.DecimalType decimalType)
            {
                var decimalObj = JObject.FromObject(new DecimalType(decimalType.Precision, decimalType.Scale));
                AttachProperties(decimalObj, avroType.Properties);

                return decimalObj;
            }

            var result = logicalType.Name switch
            {
                "date" => new DateType() as object,
                "duration" => new DurationType(),
                "time_ms" => new TimeMillisType(),
                "time_micros" => new TimeMicrosType(),
                "timestamp_ms" => new TimestampMillisType(),
                "timestamp_micros" => new TimestampMicrosType(),
                "local_timestamp_ms" => new LocalTimestampMillisType(),
                "local_timestamp_micros" => new LocalTimestampMicrosType(),
                "uuid" => new UuidType(),
                _ => throw new ArgumentOutOfRangeException(logicalType.Name)
            };

            var obj = JObject.FromObject(result);
            AttachProperties(obj, avroType.Properties);

            return obj;
        }

        // reference
        if (avroType is Idl.Model.ReferenceType referenceType)
        {
            // Reference type can't attach any properties
            // to the *reference*, but it can to the actual type
            // declaration.
            //
            // We'll update this elsewhere to attach.
            // e.g.
            // fixed MD5(16);                             // declaration
            // @foo("bar") MD5 hash = "0000000000000000"; // usage
            //
            // In this case the foo property is attached to the declaration.
            return JToken.FromObject(referenceType.Name.Value);
        }

        // array
        if (avroType is Idl.Model.ArrayType arrayType)
        {
            var nestedTypeTokens = GenerateTypeTokens(protocol, arrayType.NestedType);
            var arrayObj = new JObject
            {
                ["type"] = JToken.FromObject("array"),
                ["items"] = JToken.FromObject(nestedTypeTokens)
            };

            AttachProperties(arrayObj, avroType.Properties);
            return arrayObj;
        }

        // map
        if (avroType is Idl.Model.MapType mapType)
        {
            var nestedTypeTokens = GenerateTypeTokens(protocol, mapType.NestedType);
            var mapObj = new JObject
            {
                ["type"] = JToken.FromObject("map"),
                ["values"] = JToken.FromObject(nestedTypeTokens)
            };

            AttachProperties(mapObj, avroType.Properties);
            return mapObj;
        }

        // union
        if (avroType is Idl.Model.UnionDefinition unionType)
        {
            var jarr = new JArray();
            foreach (var unionOption in unionType.TypeOptions)
            {
                var nestedTypeTokens = GenerateTypeTokens(protocol, unionOption);
                jarr.Add(nestedTypeTokens);
            }

            return jarr;
        }

        return default!;
    }

    private static JObject MapToRecordDto(Idl.Model.Protocol protocol, Idl.Model.RecordDeclaration record)
    {
        var dto = new Record
        {
            Name = record.Name.Value,
            Documentation = record.Comment?.Value,
            Namespace = GetNamespaceFromProperties(record.Properties),
            Aliases = GetAliasesFromProperties(record.Properties),
            Fields = record.Fields.Select(f => MapToFieldDto(protocol, f)).ToList()
        };

        var jobj = JObject.FromObject(dto);
        AttachProperties(jobj, record.Properties);

        return jobj;
    }

    private static FieldSchema MapToFieldDto(Idl.Model.Protocol protocol, Idl.Model.FieldDeclaration field)
    {
        return new FieldSchema
        {
            Name = field.Name.Value,
            Documentation = field.Comment?.Value,
            Aliases = GetAliasesFromProperties(field.Properties),
            DefaultValue = MapToDefaultValueToken(field.DefaultValue),
            Ordering = GetOrderFromProperties(field.Properties),
            Type = GenerateTypeTokens(protocol, field.Type)
        };
    }

    private static JToken? MapToDefaultValueToken(IEnumerable<Token<IdlToken>> defaultValueTokens)
    {
        var defaultValueStr = defaultValueTokens
            .Select(d => d.ToStringValue())
            .Join(string.Empty);
        if (string.IsNullOrWhiteSpace(defaultValueStr))
            return null;

        return JToken.Parse(defaultValueStr);
    }

    private static void AttachProperties(JObject jobj, IEnumerable<Idl.Model.Property> props)
    {
        foreach (var p in props)
        {
            var propStr = p.Value.Select(t => t.ToStringValue()).Join(string.Empty);
            jobj[p.Name] = JToken.Parse(propStr);
        }
    }

    private static JObject MapToErrorDto(Idl.Model.Protocol protocol, Idl.Model.ErrorDeclaration error)
    {
        var dto = new Error
        {
            Name = error.Name.Value,
            Documentation = error.Comment?.Value,
            Namespace = GetNamespaceFromProperties(error.Properties),
            Aliases = GetAliasesFromProperties(error.Properties),
            Fields = error.Fields.Select(f => MapToFieldDto(protocol, f)).ToList()
        };

        var jobj = JObject.FromObject(dto);
        AttachProperties(jobj, error.Properties);

        return jobj;
    }

    private static JObject MapToMessageDto(Idl.Model.Protocol protocol, Idl.Model.MessageDeclaration message)
    {
        var dto = new Message
        {
            Documentation = message.Comment?.Value,
            Errors = message.Errors.Any() ? message.Errors.Select(e => e.Value).ToList() : null,
            OneWay = message.OneWay,
            Request = message.Parameters.Select(p => MapToMessageParameterDto(protocol, p)).ToList(),
            Response = GenerateTypeTokens(protocol, message.ReturnType)
        };

        var obj = JObject.FromObject(dto);
        AttachProperties(obj, message.Properties);

        return obj;
    }

    private static MessageParameter MapToMessageParameterDto(Idl.Model.Protocol protocol, Idl.Model.FormalParameter messageParameter)
    {
        return new MessageParameter
        {
            Default = MapToDefaultValueToken(messageParameter.DefaultValue),
            Name = messageParameter.Name.Value,
            Type = GenerateTypeTokens(protocol, messageParameter.Type)
        };
    }

    private static string? GetNamespaceFromProperties(IEnumerable<Idl.Model.Property> properties)
    {
        var nsProp = properties.LastOrDefault(p => p.Name == "namespace");
        var token = nsProp?.Value?.FirstOrDefault();
        return token.HasValue ? GetUnquotedStringValue(token.Value) : null;
    }

    private static string? GetOrderFromProperties(IEnumerable<Idl.Model.Property> properties)
    {
        var orderProp = properties.LastOrDefault(p => p.Name == "order");
        var token = orderProp?.Value?.FirstOrDefault();
        return token.HasValue ? GetUnquotedStringValue(token.Value) : null;
    }

    private static IEnumerable<string>? GetAliasesFromProperties(IEnumerable<Idl.Model.Property> properties)
    {
        var aliasProp = properties.LastOrDefault(p => p.Name == "aliases");
        var aliases = aliasProp
            ?.Value
            ?.Where(t => t.Kind == Idl.IdlToken.StringLiteral)
            ?.Select(GetUnquotedStringValue)
            ?.ToArray() ?? [];
        return aliases.Length > 0 ? aliases : null;
    }

    private static string GetUnquotedStringValue(Token<IdlToken> token)
    {
        var strValue = token.ToStringValue();
        var unescaped = Regex.Unescape(strValue);

        // remove quoting
        return unescaped.StartsWith('"') && unescaped.EndsWith('"')
            ? unescaped[1..^1]
            : unescaped;
    }
}