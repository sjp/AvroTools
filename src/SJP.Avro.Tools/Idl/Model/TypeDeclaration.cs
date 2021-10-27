using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model
{
    public abstract record TypeDeclaration
    {
        protected TypeDeclaration(DocComment? comment, IEnumerable<Property> properties, int position)
        {
            Comment = comment;
            Properties = properties ?? throw new ArgumentNullException(nameof(properties));
            Position = position;
        }

        public DocComment? Comment { get; }

        public IEnumerable<Property> Properties { get; }

        // the position of the declaration inside the source document
        public int Position { get; }
    }
}
