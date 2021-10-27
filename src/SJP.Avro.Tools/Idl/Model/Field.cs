using System;
using System.Collections.Generic;
using System.Diagnostics;
using Superpower.Model;

namespace SJP.Avro.Tools.Idl.Model
{
    public record Field
    {
        public Field(
            DocComment? comment,
            IEnumerable<Property> properties,
            AvroType type,
            Identifier name,
            IEnumerable<Token<IdlToken>> defaultValue
        )
        {
            Comment = comment;
            Properties = properties ?? throw new ArgumentNullException(nameof(properties));
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            DefaultValue = defaultValue ?? throw new ArgumentNullException(nameof(defaultValue));
        }

        public DocComment? Comment { get; }

        public IEnumerable<Property> Properties { get; }

        public AvroType Type { get; }

        public Identifier Name { get; }

        public IEnumerable<Token<IdlToken>> DefaultValue { get; }
    }
}
