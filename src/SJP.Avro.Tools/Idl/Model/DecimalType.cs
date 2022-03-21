using System;
using System.Collections.Generic;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Describes an Avro decimal type, i.e. an arbitrary-precision signed decimal number.
/// </summary>
public record DecimalType : LogicalType
{
    /// <summary>
    /// Constructs a representation of an Avro decimal type definition.
    /// </summary>
    /// <param name="precision">The (maximum) precision of decimals stored in this type.</param>
    /// <param name="scale">The scale of the decimal values in this type (i.e. number of required decimal places).</param>
    /// <param name="properties">A collection of properties to attach to the decimal type. Can be empty.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="precision"/> or <paramref name="scale"/> have a value less than one. Alternatively when <paramref name="scale"/> is greater than <paramref name="precision"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="properties"/> is <c>null</c>.</exception>
    public DecimalType(int precision, int scale, IEnumerable<Property> properties)
        : base("bytes", properties)
    {
        if (precision <= 0)
            throw new ArgumentOutOfRangeException(nameof(precision), $"The precision for a decimal type must a positive integer greater than zero. Given precision: '{ precision }'.");
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), $"The scale for a decimal type must a positive integer greater than zero. Given scale: '{ scale }'.");
        if (scale > precision)
            throw new ArgumentOutOfRangeException(nameof(scale), $"The scale for a decimal type must be no greater than the precision. Given precision: '{ precision }' and scale: '{ scale }'.");

        Precision = precision;
        Scale = scale;
    }

    /// <summary>
    /// The (maximum) precision of decimals stored in this type.
    /// </summary>
    public int Precision { get; }

    /// <summary>
    /// The scale of the decimal values in this type (i.e. number of required decimal places).
    /// </summary>
    public int Scale { get; }
}
