using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model
{
    public record FixedDeclaration : NamedSchemaDeclaration
    {
        public FixedDeclaration(
            DocComment? comment,
            IEnumerable<Property> properties,
            int position,
            Identifier name,
            int size
        ) : base(comment, properties, position)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Size = size;
        }

        public Identifier Name { get; }

        public int Size { get; }
    }
}
