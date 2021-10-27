using System;
using System.Collections.Generic;
using Superpower.Model;

namespace SJP.Avro.Tools.Idl.Model
{
    public record Property
    {
        private const string PropertyPrefix = "@";

        public Property(string name, IEnumerable<Token<IdlToken>> value)
        {
            if (name.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(name));
            if (!name.StartsWith(PropertyPrefix))
                throw new ArgumentException($"The given property name '{ name }' does not start with '{ PropertyPrefix }'.", nameof(name));

            Name = TrimNamespacePrefix(name);
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Name { get; }

        public IEnumerable<Token<IdlToken>> Value { get; }

        private static string TrimNamespacePrefix(string name) => name[PropertyPrefix.Length..];
    }
}
