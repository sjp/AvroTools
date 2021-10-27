using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model
{
    public record ReferenceType : AvroType
    {
        public ReferenceType(Identifier name, IEnumerable<Property> properties)
            : base(properties)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public Identifier Name { get; }
    }
}
