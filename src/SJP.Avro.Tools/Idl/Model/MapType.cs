using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace SJP.Avro.Tools.Idl.Model
{
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    public class MapType : AvroType
    {
        public MapType(AvroType nestedType, IEnumerable<Property> properties)
            : base(properties)
        {
            NestedType = nestedType ?? throw new ArgumentNullException(nameof(nestedType));
        }

        public AvroType NestedType { get; }

        /// <summary>
        /// Returns a string that provides a basic string representation of this object.
        /// </summary>
        /// <returns>A <see cref="string"/> that represents this instance.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override string ToString() => DebuggerDisplay;

        private string DebuggerDisplay
        {
            get
            {
                var nestedTypeValue = NestedType.ToString();
                return $"Type: map<{ nestedTypeValue }>";
            }
        }
    }
}
