using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Superpower.Model;

namespace SJP.Avro.Tools.Idl.Model
{
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    public class Property
    {
        private readonly string _propertyName;

        public Property(string name, IEnumerable<Token<IdlToken>> value)
        {
            if (name.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(name));

            _propertyName = name;
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Name => _propertyName[1..]; // trim off starting '@'

        public IEnumerable<Token<IdlToken>> Value { get; }

        private string DebuggerDisplay
        {
            get
            {
                var value = Value.Select(v => v.ToStringValue()).Join(string.Empty);
                return $"Property: {Name}, Value: {value}";
            }
        }
    }
}
