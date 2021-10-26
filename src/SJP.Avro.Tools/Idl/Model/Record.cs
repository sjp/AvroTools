using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace SJP.Avro.Tools.Idl.Model
{
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    public class Record : TypeDeclaration
    {
        public Record(
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
                return $"Record: {Name.Value}";
            }
        }
    }
}
