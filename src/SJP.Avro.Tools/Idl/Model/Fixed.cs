using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SJP.Avro.Tools.Idl.Model
{
    public record Fixed : TypeDeclaration
    {
        public Fixed(
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
