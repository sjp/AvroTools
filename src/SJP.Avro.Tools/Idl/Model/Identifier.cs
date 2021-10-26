using System;
using System.Diagnostics;

namespace SJP.Avro.Tools.Idl.Model
{
    [DebuggerDisplay("{" + nameof(Value) + ",nq}")]
    public class Identifier
    {
        public Identifier(string identifier)
        {
            if (identifier.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(identifier));

            Value = StripQuoting(identifier);
        }

        public string Value { get; }

        private static string StripQuoting(string identifier)
        {
            if (identifier.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(identifier));

            var isQuoted = identifier.StartsWith('`') && identifier.EndsWith('`');
            return isQuoted
                ? identifier[1..^1]
                : identifier;
        }
    }
}
