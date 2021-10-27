using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model
{
    public record ArrayType : AvroType
    {
        public ArrayType(AvroType nestedType, IEnumerable<Property> properties)
            : base(properties)
        {
            NestedType = nestedType ?? throw new ArgumentNullException(nameof(nestedType));
        }

        public AvroType NestedType { get; }
    }
}
