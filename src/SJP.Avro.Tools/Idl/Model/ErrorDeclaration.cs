using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model
{
    public record ErrorDeclaration : NamedSchemaDeclaration
    {
        public ErrorDeclaration(
            DocComment? comment,
            IEnumerable<Property> properties,
            int position,
            Identifier name,
            IEnumerable<FieldDeclaration> fields
        ) : base(comment, properties, position)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        }

        public Identifier Name { get; }

        public IEnumerable<FieldDeclaration> Fields { get; }
    }
}
