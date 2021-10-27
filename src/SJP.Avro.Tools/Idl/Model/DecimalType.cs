using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model
{
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
