using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace SJP.Avro.Tools.Idl.Model
{
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    public class PrimitiveType : AvroType
    {
        public PrimitiveType(string name, IEnumerable<Property> properties)
            : base(properties)
        {
            if (name.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(name));

            Name = name;
        }

        public string Name { get; }

        /// <summary>
        /// Returns a string that provides a basic string representation of this object.
        /// </summary>
        /// <returns>A <see cref="string"/> that represents this instance.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override string ToString() => Name;

        private string DebuggerDisplay => "Type: " + Name;
    }


    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    public class LogicalType : AvroType
    {
        public LogicalType(string name, IEnumerable<Property> properties)
            : base(properties)
        {
            if (name.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(name));

            Name = name;
        }

        public string Name { get; }

        /// <summary>
        /// Returns a string that provides a basic string representation of this object.
        /// </summary>
        /// <returns>A <see cref="string"/> that represents this instance.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override string ToString() => Name;

        private string DebuggerDisplay => "Type: " + Name;
    }

    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    public class DecimalType : LogicalType
    {
        public DecimalType(int precision, int scale, IEnumerable<Property> properties)
            : base("bytes", properties)
        {
            Precision = precision;
            Scale = scale;
        }

        public int Precision { get; }

        public int Scale { get; }

        /// <summary>
        /// Returns a string that provides a basic string representation of this object.
        /// </summary>
        /// <returns>A <see cref="string"/> that represents this instance.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override string ToString() => Name;

        private string DebuggerDisplay => "Type: " + Name;
    }
}
