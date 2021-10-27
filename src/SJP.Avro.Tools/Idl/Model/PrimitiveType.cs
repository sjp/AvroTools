using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model
{
    public record PrimitiveType : AvroType
    {
        public PrimitiveType(string name, IEnumerable<Property> properties)
            : base(properties)
        {
            if (name.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(name));

            Name = name;
        }

        public string Name { get; }
    }

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

    public record DecimalType : LogicalType
    {
        public DecimalType(int precision, int scale, IEnumerable<Property> properties)
            : base("bytes", properties)
        {
            Precision = precision;
            Scale = scale;
        }

        public int Precision { get; }

        public int Scale { get; }
    }
}
