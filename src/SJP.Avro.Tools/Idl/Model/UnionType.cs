using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace SJP.Avro.Tools.Idl.Model
{
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    public class UnionType : AvroType
    {
        public UnionType(IEnumerable<AvroType> typeOptions, IEnumerable<Property> properties)
            : base(properties)
        {
            TypeOptions = typeOptions ?? throw new ArgumentNullException(nameof(typeOptions));
        }

        public IEnumerable<AvroType> TypeOptions { get; }

        /// <summary>
        /// Returns a string that provides a basic string representation of this object.
        /// </summary>
        /// <returns>A <see cref="string"/> that represents this instance.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override string ToString()
        {
            var pieces = TypeOptions.Select(t => t.ToString() ?? string.Empty).Join(", ");
            return $"{{{ pieces }}}";
        }

        private string DebuggerDisplay
        {
            get
            {
                return $"Union: { this }";
            }
        }
    }
}
