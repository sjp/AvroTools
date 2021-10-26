using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SJP.Avro.Tools.Idl;
using Superpower.Model;

namespace SJP.Avro.Tools
{
    public class IdlCompiler
    {
        private readonly IFileProvider _fileProvider;

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

        public string Compile(string filePath, Idl.Model.Protocol protocol)
        {
            if (filePath.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(filePath));
            if (protocol == null)
                throw new ArgumentNullException(nameof(protocol));

            var protocolNamespace = protocol.Properties
                .Where(p => p.Name == "namespace"
                    && p.Value.Count() == 1
                    && p.Value.Single().Kind == IdlToken.StringLiteral)
                .Select(p => GetUnquotedStringValue(p.Value.Single()))
                .LastOrDefault();

            var idlImports = protocol.Imports
                .Where(i => i.Type == Idl.Model.ImportType.Idl)
                .Select(i => ParseImportedIdl(filePath, i.Path))
                .ToList();

            var parsedMessages = new Dictionary<Idl.Model.Identifier, Idl.Model.Message>(protocol.Messages);
            foreach (var message in idlImports.SelectMany(i => i.Messages))
                parsedMessages[message.Key] = message.Value;

            var messageDtos = parsedMessages
                .Select(kv => new KeyValuePair<string, JObject>(kv.Key.Value, MapToMessageDto(protocol, kv.Value)))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

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
                    props = new List<Idl.Model.Property>();

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

                var resolvedName = !ns.IsNullOrWhiteSpace()
                    ? ns + "." + name
                    : name;

                if (!typePropertyLookup.TryGetValue(resolvedName, out var props))
                    continue;

                AttachProperties(typeDto, props);
            }

            // now remove namespace where it matches the protocol namespace
            // as it's redundant
            if (!protocolNamespace.IsNullOrWhiteSpace())
            {
                foreach (var typeDto in typeDtos)
                {
                    var ns = typeDto["namespace"]?.ToString() ?? string.Empty;
                    if (ns.IsNullOrWhiteSpace())
                        continue;

                    if (ns == protocolNamespace)
                        typeDto.Remove("namespace");
                }
            }

            var dto = new ProtocolDto
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

        private static IEnumerable<Idl.Model.TypeDeclaration> GetOrderedTypeDeclarations(Idl.Model.Protocol protocol)
        {
            return protocol.Imports
                .Where(i => i.Type == Idl.Model.ImportType.Schema || i.Type == Idl.Model.ImportType.Idl)
                .Select(i => i as Idl.Model.TypeDeclaration)
                .Concat(protocol.Enums)
                .Concat(protocol.Fixeds)
                .Concat(protocol.Records)
                .Concat(protocol.Errors)
                .OrderBy(x => x.Position)
                .ToList();
        }

        private IEnumerable<JObject> MapTypeDeclarationToDto(string baseFilePath, Idl.Model.Protocol protocol, Idl.Model.TypeDeclaration typeDeclaration)
        {
            return typeDeclaration switch
            {
                Idl.Model.EnumType e => new[] { MapToEnumDto(e) },
                Idl.Model.Fixed f => new[] { MapToFixedDto(f) },
                Idl.Model.Record r => new[] { MapToRecordDto(protocol, r) },
                Idl.Model.ErrorType e => new[] { MapToErrorDto(protocol, e) },
                Idl.Model.Import i => MapImportDeclarationToDto(baseFilePath, i),
                _ => Array.Empty<JObject>()
            };
        }

        private IEnumerable<JObject> MapImportDeclarationToDto(string baseFilePath, Idl.Model.Import import)
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
                        if (importIdlNs.IsNullOrWhiteSpace() || t["namespace"] != null)
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

                return new[] { parsedSchema };
            }

            return Array.Empty<JObject>();
        }

        private static JObject MapToEnumDto(Idl.Model.EnumType enumType)
        {
            var dto = new EnumDto
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

        private static JObject MapToFixedDto(Idl.Model.Fixed fixedType)
        {
            var dto = new FixedDto
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
                    var decimalObj = JObject.FromObject(new DecimalTypeDto(decimalType.Precision, decimalType.Scale));
                    AttachProperties(decimalObj, avroType.Properties);

                    return decimalObj;
                }

                var result = logicalType.Name switch
                {
                    "date" => new DateTypeDto() as object,
                    "duration" => new DurationTypeDto(),
                    "time_ms" => new TimeMillisTypeDto(),
                    "timestamp_ms" => new TimestampMillisTypeDto(),
                    "local_timestamp_ms" => new LocalTimestampMillisTypeDto(),
                    "uuid" => new UuidTypeDto(),
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
            if (avroType is Idl.Model.UnionType unionType)
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

        private static JObject MapToRecordDto(Idl.Model.Protocol protocol, Idl.Model.Record record)
        {
            var dto = new RecordDto
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

        private static FieldDto MapToFieldDto(Idl.Model.Protocol protocol, Idl.Model.Field field)
        {
            return new FieldDto
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
            if (defaultValueStr.IsNullOrWhiteSpace())
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

        private static JObject MapToErrorDto(Idl.Model.Protocol protocol, Idl.Model.ErrorType error)
        {
            var dto = new ErrorDto
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

        private static JObject MapToMessageDto(Idl.Model.Protocol protocol, Idl.Model.Message message)
        {
            var dto = new MessageDto
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

        private static MessageParameterDto MapToMessageParameterDto(Idl.Model.Protocol protocol, Idl.Model.MessageParameter messageParameter)
        {
            return new MessageParameterDto
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
                ?.ToArray() ?? Array.Empty<string>();
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

    public class ProtocolDto
    {
        [JsonProperty("protocol")]
        public string Name { get; set; } = default!;

        [JsonProperty("namespace", NullValueHandling = NullValueHandling.Ignore)]
        public string? Namespace { get; set; }

        [JsonProperty("doc", NullValueHandling = NullValueHandling.Ignore)]
        public string? Documentation { get; set; }

        [JsonProperty("types", NullValueHandling = NullValueHandling.Ignore)]
        public IEnumerable<JObject>? Types { get; set; }

        [JsonProperty("messages", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, JObject>? Messages { get; set; }
    }

    public class MessageDto
    {
        [JsonProperty("doc", NullValueHandling = NullValueHandling.Ignore)]
        public string? Documentation { get; set; }

        [JsonProperty("request")]
        public IEnumerable<MessageParameterDto> Request { get; set; } = Array.Empty<MessageParameterDto>();

        [JsonProperty("response")]
        public JToken? Response { get; set; } = JToken.FromObject("null");

        [JsonProperty("one-way", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool OneWay { get; set; }

        [JsonProperty("errors", NullValueHandling = NullValueHandling.Ignore)]
        public IEnumerable<string>? Errors { get; set; }
    }

    public class MessageParameterDto
    {
        [JsonProperty("name")]
        public string Name { get; set; } = default!;

        [JsonProperty("type")]
        public JToken Type { get; set; } = default!;

        [JsonProperty("default", NullValueHandling = NullValueHandling.Ignore)]
        public IEnumerable<JToken>? Default { get; set; }
    }

    public abstract class NamedSchemaDto
    {
        [JsonProperty("name")]
        public string Name { get; set; } = default!;

        [JsonProperty("namespace", NullValueHandling = NullValueHandling.Ignore)]
        public string? Namespace { get; set; }

        [JsonProperty("aliases", NullValueHandling = NullValueHandling.Ignore)]
        public IEnumerable<string>? Aliases { get; set; }

        [JsonProperty("doc", NullValueHandling = NullValueHandling.Ignore)]
        public string? Documentation { get; set; }
    }

    public class FixedDto : NamedSchemaDto
    {
        [JsonProperty("type")]
        public string Type { get; } = "fixed";

        [JsonProperty("size")]
        public int Size { get; set; }
    }

    public class RecordDto : NamedSchemaDto
    {
        [JsonProperty("type")]
        public string Type { get; } = "record";

        [JsonProperty("fields")]
        public IEnumerable<FieldDto> Fields { get; set; } = Array.Empty<FieldDto>();
    }

    public class ErrorDto : NamedSchemaDto
    {
        [JsonProperty("type")]
        public string Type { get; } = "error";

        [JsonProperty("fields")]
        public IEnumerable<FieldDto> Fields { get; set; } = Array.Empty<FieldDto>();
    }

    public class EnumDto : NamedSchemaDto
    {
        [JsonProperty("type")]
        public string Type { get; } = "enum";

        [JsonProperty("symbols")]
        public IEnumerable<string> Symbols { get; set; } = Array.Empty<string>();

        [JsonProperty("default", NullValueHandling = NullValueHandling.Ignore)]
        public string? DefaultValue { get; set; }
    }

    public class DecimalTypeDto
    {
        public DecimalTypeDto(int precision, int scale)
        {
            Precision = precision;
            Scale = scale;
        }

        [JsonProperty("type")]
        public string Type { get; } = "bytes";

        [JsonProperty("logicalType")]
        public string LogicalType { get; } = "decimal";

        [JsonProperty("precision")]
        public int Precision { get; }

        [JsonProperty("scale")]
        public int Scale { get; }
    }

    public class TimeMillisTypeDto
    {
        [JsonProperty("type")]
        public string Type { get; } = "int";

        [JsonProperty("logicalType")]
        public string LogicalType { get; } = "time-millis";
    }

    public class TimestampMillisTypeDto
    {
        [JsonProperty("type")]
        public string Type { get; } = "long";

        [JsonProperty("logicalType")]
        public string LogicalType { get; } = "timestamp-millis";
    }

    public class LocalTimestampMillisTypeDto
    {
        [JsonProperty("type")]
        public string Type { get; } = "long";

        [JsonProperty("logicalType")]
        public string LogicalType { get; } = "local-timestamp-millis";
    }

    public class DateTypeDto
    {
        [JsonProperty("type")]
        public string Type { get; } = "int";

        [JsonProperty("logicalType")]
        public string LogicalType { get; } = "date";
    }

    public class UuidTypeDto
    {
        [JsonProperty("type")]
        public string Type { get; } = "string";

        [JsonProperty("logicalType")]
        public string LogicalType { get; } = "uuid";
    }

    public class DurationTypeDto
    {
        [JsonProperty("type")]
        public string Type { get; } = "fixed";

        [JsonProperty("logicalType")]
        public string LogicalType { get; } = "duration";

        [JsonProperty("size")]
        public int Size { get; } = 12;
    }

    public class FieldDto
    {
        /// <summary>
        /// List of aliases for the field name.
        /// </summary>
        [JsonProperty("aliases", NullValueHandling = NullValueHandling.Ignore)]
        public IEnumerable<string>? Aliases { get; set; }

        /// <summary>
        /// Name of the field.
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; } = default!;

        /// <summary>
        /// Position of the field within its record.
        /// </summary>
        [JsonIgnore]
        public int Position { get; set; }

        /// <summary>
        /// Documentation for the field, if any. Null if there is no documentation.
        /// </summary>
        [JsonProperty("doc", NullValueHandling = NullValueHandling.Ignore)]
        public string? Documentation { get; set; }

        /// <summary>
        /// The default value for the field stored as JSON object, if defined. Otherwise, null.
        /// </summary>
        [JsonProperty("default", NullValueHandling = NullValueHandling.Ignore)]
        public JToken? DefaultValue { get; set; }

        /// <summary>
        /// Order of the field
        /// </summary>
        [JsonProperty("order", NullValueHandling = NullValueHandling.Ignore)]
        public string? Ordering { get; set; }

        /// <summary>
        /// Type of the field.
        /// </summary>
        [JsonProperty("type")]
        public JToken Type { get; init; } = default!;
    }
}
