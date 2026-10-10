using System;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Singleton.Generator
{
    /// <summary>
    /// Reads the arguments which a creator was named with: which types they fit, and what text writes them into the
    /// generated call.
    /// </summary>
    /// <remarks>
    /// An argument of an attribute is a constant and not an expression, so what a constructor can be chosen by is the
    /// type each constant was written as and the conversions which the language makes from it. The choice is made here
    /// rather than left to the compiler, because a creator whose arguments fit no constructor is a singleton which
    /// would fail the compilation of a file the author never wrote.
    /// </remarks>
    internal sealed class ArgumentRenderer
    {
        private readonly Compilation m_Compilation;

        /// <summary>
        /// Read the arguments of an attribute against a compilation.
        /// </summary>
        /// <param name="compilation">The compilation which the special types are read off.</param>
        public ArgumentRenderer(Compilation compilation) => m_Compilation = compilation;

        /// <summary>
        /// The type an argument was written as.<para/>
        /// An argument of a <c>params object[]</c> is an <c>object</c> to the attribute constructor and holds its own
        /// type only in what it carries, so the type is read off the value where the written one says nothing.
        /// </summary>
        /// <param name="argument">The argument.</param>
        /// <returns>The type, or <c>null</c> where there is none to read.</returns>
        public ITypeSymbol? EffectiveType(TypedConstant argument)
        {
            switch (argument.Kind)
            {
                case TypedConstantKind.Type:
                    // A typeof is a System.Type, which is a type the language has rather than one the metadata marks.
                    return m_Compilation.GetTypeByMetadataName("System.Type");
                case TypedConstantKind.Enum:
                case TypedConstantKind.Array:
                    return argument.Type;
                case TypedConstantKind.Error:
                    return null;
            }

            var written = argument.Type;
            if (written != null && written.TypeKind != TypeKind.Error && written.SpecialType != SpecialType.System_Object)
            {
                return written;
            }

            return argument.Value switch
            {
                null => null,
                bool => m_Compilation.GetSpecialType(SpecialType.System_Boolean),
                char => m_Compilation.GetSpecialType(SpecialType.System_Char),
                string => m_Compilation.GetSpecialType(SpecialType.System_String),
                sbyte => m_Compilation.GetSpecialType(SpecialType.System_SByte),
                byte => m_Compilation.GetSpecialType(SpecialType.System_Byte),
                short => m_Compilation.GetSpecialType(SpecialType.System_Int16),
                ushort => m_Compilation.GetSpecialType(SpecialType.System_UInt16),
                int => m_Compilation.GetSpecialType(SpecialType.System_Int32),
                uint => m_Compilation.GetSpecialType(SpecialType.System_UInt32),
                long => m_Compilation.GetSpecialType(SpecialType.System_Int64),
                ulong => m_Compilation.GetSpecialType(SpecialType.System_UInt64),
                float => m_Compilation.GetSpecialType(SpecialType.System_Single),
                double => m_Compilation.GetSpecialType(SpecialType.System_Double),
                _ => null
            };
        }

        /// <summary>
        /// Write an argument as the C# which passes it to a constructor.
        /// </summary>
        /// <param name="argument">The argument.</param>
        /// <returns>The expression.</returns>
        public string Render(TypedConstant argument)
        {
            if (argument.IsNull || argument.Value == null && argument.Kind != TypedConstantKind.Type)
            {
                return "null";
            }

            if (argument.Kind == TypedConstantKind.Type && argument.Value is ITypeSymbol type)
            {
                return $"typeof({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})";
            }

            if (argument.Kind == TypedConstantKind.Enum && argument.Type != null)
            {
                return $"({argument.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){Literal(argument.Value)}";
            }

            return Literal(argument.Value);
        }

        /// <summary>
        /// Whether an argument fits a parameter, and how much the fit costs.<para/>
        /// How much the fit costs is what tells one constructor of a creator from another where more than one of them
        /// takes the arguments: the one which converts least is the one which is written.
        /// </summary>
        /// <param name="argument">The argument.</param>
        /// <param name="parameter">The parameter.</param>
        /// <param name="cost">What the fit costs, where it fits.</param>
        /// <returns>Whether the argument fits the parameter.</returns>
        public bool TryFit(TypedConstant argument, ITypeSymbol parameter, out int cost)
        {
            cost = -1;
            if (parameter.TypeKind == TypeKind.Error) return false;

            if (argument.IsNull || argument.Value == null && argument.Kind != TypedConstantKind.Type)
            {
                if (!parameter.IsReferenceType && parameter.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T) return false;
                cost = 1;
                return true;
            }

            var written = EffectiveType(argument);
            if (written == null) return false;

            if (SymbolEqualityComparer.Default.Equals(written, parameter))
            {
                cost = 0;
                return true;
            }

            // A parameter which asked to be given anything is given anything, which is a box where the argument is not
            // one already.
            if (parameter.SpecialType == SpecialType.System_Object)
            {
                cost = 4;
                return true;
            }

            // A typeof is a System.Type, which the two cases above have already answered for.
            if (argument.Kind == TypedConstantKind.Type) return false;

            // A constant written as an int is read as any type it fits, which is what the language does with it.
            if (written.SpecialType == SpecialType.System_Int32 && argument.Value is int constant &&
                FitsIntConstant(constant, parameter.SpecialType))
            {
                cost = 2;
                return true;
            }

            if (IsNumeric(written) && IsNumeric(parameter) &&
                HasImplicitNumericConversion(written.SpecialType, parameter.SpecialType))
            {
                cost = 2;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Write a primitive value as a literal of its own type.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The literal.</returns>
        private static string Literal(object? value)
        {
            switch (value)
            {
                case null:
                    return "null";
                case string text:
                    return SymbolDisplay.FormatLiteral(text, quote: true);
                case char character:
                    return SymbolDisplay.FormatLiteral(character, quote: true);
                case bool flag:
                    return flag ? "true" : "false";
                case float single when float.IsNaN(single):
                    return "float.NaN";
                case float single when float.IsPositiveInfinity(single):
                    return "float.PositiveInfinity";
                case float single when float.IsNegativeInfinity(single):
                    return "float.NegativeInfinity";
                case float single:
                    return single.ToString("R", CultureInfo.InvariantCulture) + "f";
                case double number when double.IsNaN(number):
                    return "double.NaN";
                case double number when double.IsPositiveInfinity(number):
                    return "double.PositiveInfinity";
                case double number when double.IsNegativeInfinity(number):
                    return "double.NegativeInfinity";
                case double number:
                    return number.ToString("R", CultureInfo.InvariantCulture) + "d";
                case decimal number:
                    return number.ToString(CultureInfo.InvariantCulture) + "m";
                case long number:
                    return number.ToString(CultureInfo.InvariantCulture) + "L";
                case ulong number:
                    return number.ToString(CultureInfo.InvariantCulture) + "UL";
                case uint number:
                    return number.ToString(CultureInfo.InvariantCulture) + "U";
                case int number:
                    return number.ToString(CultureInfo.InvariantCulture);
                case short number:
                    return "(short)" + number.ToString(CultureInfo.InvariantCulture);
                case ushort number:
                    return "(ushort)" + number.ToString(CultureInfo.InvariantCulture);
                case byte number:
                    return "(byte)" + number.ToString(CultureInfo.InvariantCulture);
                case sbyte number:
                    return "(sbyte)" + number.ToString(CultureInfo.InvariantCulture);
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
            }
        }

        /// <summary>
        /// Whether a type is one of the types a numeric conversion is made between.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>Whether it is one of them.</returns>
        private static bool IsNumeric(ITypeSymbol type) => type.SpecialType switch
        {
            SpecialType.System_SByte or SpecialType.System_Byte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double or
            SpecialType.System_Decimal or SpecialType.System_Char => true,
            _ => false
        };

        /// <summary>
        /// Whether a constant written as an <c>int</c> fits a type it would not be converted to as a variable.
        /// </summary>
        /// <param name="value">The constant.</param>
        /// <param name="target">The type it is read as.</param>
        /// <returns>Whether it fits.</returns>
        private static bool FitsIntConstant(int value, SpecialType target) => target switch
        {
            SpecialType.System_SByte => value >= sbyte.MinValue && value <= sbyte.MaxValue,
            SpecialType.System_Byte => value >= byte.MinValue && value <= byte.MaxValue,
            SpecialType.System_Int16 => value >= short.MinValue && value <= short.MaxValue,
            SpecialType.System_UInt16 => value >= ushort.MinValue && value <= ushort.MaxValue,
            SpecialType.System_UInt32 => value >= 0,
            SpecialType.System_UInt64 => value >= 0,
            _ => false
        };

        /// <summary>
        /// Whether the language converts one numeric type to another without being asked to.
        /// </summary>
        /// <param name="from">The type which was written.</param>
        /// <param name="to">The type it is read as.</param>
        /// <returns>Whether the conversion is made.</returns>
        private static bool HasImplicitNumericConversion(SpecialType from, SpecialType to)
        {
            switch (from)
            {
                case SpecialType.System_SByte:
                    return to is SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64
                        or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
                case SpecialType.System_Byte:
                    return to is SpecialType.System_Int16 or SpecialType.System_UInt16
                        or SpecialType.System_Int32 or SpecialType.System_UInt32
                        or SpecialType.System_Int64 or SpecialType.System_UInt64
                        or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
                case SpecialType.System_Int16:
                    return to is SpecialType.System_Int32 or SpecialType.System_Int64
                        or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
                case SpecialType.System_UInt16:
                case SpecialType.System_Char:
                    return to is SpecialType.System_Int32 or SpecialType.System_UInt32
                        or SpecialType.System_Int64 or SpecialType.System_UInt64
                        or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
                case SpecialType.System_Int32:
                    return to is SpecialType.System_Int64
                        or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
                case SpecialType.System_UInt32:
                    return to is SpecialType.System_Int64 or SpecialType.System_UInt64
                        or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                    return to is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
                case SpecialType.System_Single:
                    return to == SpecialType.System_Double;
                default:
                    return false;
            }
        }
    }
}