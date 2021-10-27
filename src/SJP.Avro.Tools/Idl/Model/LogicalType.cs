using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model
{
    public record LogicalType : AvroType
    {
        public LogicalType(string name, IEnumerable<Property> properties)
            : base(properties)
        {
            if (name.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(name));

            Name = name;
        }

        public string Name { get; }
    }
}
