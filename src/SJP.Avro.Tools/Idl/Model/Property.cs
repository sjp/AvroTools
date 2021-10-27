using System;
using System.Collections.Generic;
using Superpower.Model;

namespace SJP.Avro.Tools.Idl.Model
{
    public record Property
    {
        private readonly string _propertyName;

        public Property(string name, IEnumerable<Token<IdlToken>> value)
        {
            if (name.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(name));
            if (!name.StartsWith('@'))
                throw new ArgumentException($"The given property name '{ name }' does not start with a '@'.", nameof(name));

            _propertyName = name;
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Name => _propertyName[1..]; // trim off starting '@'

        public IEnumerable<Token<IdlToken>> Value { get; }
    }
}
