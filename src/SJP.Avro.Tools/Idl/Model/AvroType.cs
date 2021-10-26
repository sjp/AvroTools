using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model
{
    public class AvroType : Declaration
    {
        protected AvroType(IEnumerable<Property> properties)
        {
            Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        }

        public IEnumerable<Property> Properties { get; }
    }
}
