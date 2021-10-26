using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SJP.Avro.Tools.Idl.Model
{
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    public class ErrorType : TypeDeclaration
    {
        public ErrorType(
            DocComment? comment,
            IEnumerable<Property> properties,
            int position,
            Identifier name,
            IEnumerable<Field> fields
        ) : base(comment, properties, position)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        }

        public Identifier Name { get; }

        public IEnumerable<Field> Fields { get; }

        private string DebuggerDisplay
        {
            get
            {
                return $"Error: {Name.Value}";
            }
        }
    }
}
