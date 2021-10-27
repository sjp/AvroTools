using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace SJP.Avro.Tools.Idl.Model
{
    public record Protocol
    {
        public Protocol(
            DocComment? doc,
            Identifier name,
            IEnumerable<Property> properties,
            IEnumerable<Record> records,
            IEnumerable<Fixed> fixeds,
            IEnumerable<EnumType> enums,
            IEnumerable<ErrorType> errors,
            IEnumerable<Import> imports,
            IReadOnlyDictionary<Identifier, Message> messages)
        {
            Documentation = doc;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Properties = properties ?? throw new ArgumentNullException(nameof(properties));
            Records = records ?? throw new ArgumentNullException(nameof(records));
            Fixeds = fixeds ?? throw new ArgumentNullException(nameof(fixeds));
            Enums = enums ?? throw new ArgumentNullException(nameof(enums));
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
            Imports = imports ?? throw new ArgumentNullException(nameof(imports));
            Messages = messages ?? throw new ArgumentNullException(nameof(messages));
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
    }
}
