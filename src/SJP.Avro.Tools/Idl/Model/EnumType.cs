using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace SJP.Avro.Tools.Idl.Model
{
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    public class EnumType : TypeDeclaration
    {
        public EnumType(
            DocComment? comment,
            IEnumerable<Property> properties,
            int position,
            Identifier name,
            IEnumerable<Identifier> members,
            Identifier? defaultValue = null
        ) : base(comment, properties, position)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Members = members ?? throw new ArgumentNullException(nameof(members));
            DefaultValue = defaultValue;
        }

        public Identifier Name { get; }

        public IEnumerable<Identifier> Members { get; }

        public Identifier? DefaultValue { get; }

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
                return $"Enum: { Name.Value }";
            }
        }
    }
}
