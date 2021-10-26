using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace SJP.Avro.Tools.Idl.Model
{
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    public class ReferenceType : AvroType
    {
        public ReferenceType(Identifier name, IEnumerable<Property> properties)
            : base(properties)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public Identifier Name { get; }

        /// <summary>
        /// Returns a string that provides a basic string representation of this object.
        /// </summary>
        /// <returns>A <see cref="string"/> that represents this instance.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override string ToString() => Name.Value;

        private string DebuggerDisplay
        {
            get
            {
                return $"Type: { Name.Value }";
            }
        }
    }
}
