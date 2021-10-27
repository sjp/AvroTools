using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model
{
    public record Protocol
    {
        public Protocol(
            DocComment? doc,
            Identifier name,
            IEnumerable<Property> properties,
            IEnumerable<RecordDeclaration> records,
            IEnumerable<FixedDeclaration> fixeds,
            IEnumerable<EnumDeclaration> enums,
            IEnumerable<ErrorDeclaration> errors,
            IEnumerable<ImportDeclaration> imports,
            IReadOnlyDictionary<Identifier, MessageDeclaration> messages)
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

        public IEnumerable<RecordDeclaration> Records { get; }

        public IEnumerable<FixedDeclaration> Fixeds { get; }

        public IEnumerable<EnumDeclaration> Enums { get; }

        public IEnumerable<ErrorDeclaration> Errors { get; }

        public IEnumerable<ImportDeclaration> Imports { get; }

        public IReadOnlyDictionary<Identifier, MessageDeclaration> Messages { get; }
    }
}
