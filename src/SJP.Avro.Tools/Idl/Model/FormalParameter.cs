using System;
using System.Collections.Generic;
using Superpower.Model;

namespace SJP.Avro.Tools.Idl.Model
{
    public record FormalParameter
    {
        public FormalParameter(
            AvroType type,
            Identifier name,
            IEnumerable<Token<IdlToken>> defaultValue
        )
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            DefaultValue = defaultValue ?? throw new ArgumentNullException(nameof(defaultValue));
        }

        public AvroType Type { get; }

        public Identifier Name { get; }

        public IEnumerable<Token<IdlToken>> DefaultValue { get; }
    }
}
