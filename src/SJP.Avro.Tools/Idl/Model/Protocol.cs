using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace SJP.Avro.Tools.Idl.Model
{
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    public class Protocol
    {
        public Protocol(
            DocComment? doc,
            Identifier name,
            IEnumerable<Property> properties,
            IEnumerable<Record> records,
            IEnumerable<Fixed> fixeds,
            IEnumerable<EnumType> enums,
            IEnumerable<ErrorType> errors,
            IReadOnlyDictionary<Identifier, Message> messages,
            IEnumerable<Import> imports)
        {
            Documentation = doc;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Properties = properties ?? throw new ArgumentNullException(nameof(properties));
            Records = records ?? throw new ArgumentNullException(nameof(records));
            Fixeds = fixeds ?? throw new ArgumentNullException(nameof(fixeds));
            Enums = enums ?? throw new ArgumentNullException(nameof(enums));
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
            Messages = messages ?? throw new ArgumentNullException(nameof(messages));
            Imports = imports ?? throw new ArgumentNullException(nameof(imports));
        }

        public DocComment? Documentation { get; }

        public Identifier Name { get; }

        public IEnumerable<Property> Properties { get; }

        public IEnumerable<Record> Records { get; }

        public IEnumerable<Fixed> Fixeds { get; }

        public IEnumerable<EnumType> Enums { get; }

        public IEnumerable<ErrorType> Errors { get; }

        public IEnumerable<Import> Imports { get; }

        public IReadOnlyDictionary<Identifier, Message> Messages { get; }

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
                return $"Protocol: {Name.Value}";
            }
        }
    }
}
