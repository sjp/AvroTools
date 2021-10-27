using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model
{
    public record EnumType : TypeDeclaration
    {
        public EnumType(
            DocComment? comment,
            IEnumerable<Property> properties,
            int position,
            Identifier name,
            IEnumerable<Identifier> members,
            Identifier? defaultValue = null
        ) : base(comment, properties, position)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Members = members ?? throw new ArgumentNullException(nameof(members));
            DefaultValue = defaultValue;
        }

        public Identifier Name { get; }

        public IEnumerable<Identifier> Members { get; }

        public Identifier? DefaultValue { get; }
    }
}
