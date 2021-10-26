using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace SJP.Avro.Tools.Idl.Model
{
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    public class Message : TypeDeclaration
    {
        public Message(
            DocComment? comment,
            Identifier name,
            AvroType returnType,
            IEnumerable<Property> properties,
            int position,
            IEnumerable<MessageParameter> parameters,
            bool oneway,
            IEnumerable<Identifier> errors
        ) : base(comment, properties, position)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            OneWay = oneway;
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        public Identifier Name { get; }

        public AvroType ReturnType { get; }

        public IEnumerable<MessageParameter> Parameters { get; }

        public bool OneWay { get; }

        public IEnumerable<Identifier> Errors { get; }

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
                return $"Message: {Name.Value}";
            }
        }
    }
}
