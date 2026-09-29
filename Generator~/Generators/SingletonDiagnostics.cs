using Microsoft.CodeAnalysis;

namespace Singleton.Generator
{
    /// <summary>
    /// What the generator reports of a type which asked for a singleton and cannot have the one it asked for.
    /// </summary>
    /// <remarks>
    /// Everything here is an error rather than a warning, because each of them is a case of a singleton which would be
    /// answered with something other than what was asked for: a half which was not generated at all, an instance which
    /// is made in another way than the attribute says, or an asset which is not there.
    /// </remarks>
    internal static class SingletonDiagnostics
    {
        private const string CATEGORY = "Singleton";

        /// <summary>A declaration of the type, or of a type which holds it, is not <c>partial</c>.</summary>
        public static readonly DiagnosticDescriptor TypeMustBePartial = new(
            "SING0001",
            "A singleton must be declared partial",
            "'{0}' asks for a singleton and '{1}' is not declared 'partial', so the half which holds 'Instance' cannot be generated there",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>The type is one which a singleton cannot be made of.</summary>
        public static readonly DiagnosticDescriptor UnsupportedType = new(
            "SING0002",
            "A singleton must be a non-generic, non-abstract class",
            "'{0}' asks for a singleton and cannot have one: {1}",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>The type declares a member which the generated half would declare as well.</summary>
        public static readonly DiagnosticDescriptor MemberConflict = new(
            "SING0003",
            "A singleton must not declare 'Instance', 's_Instance' or 'TryGetInstance'",
            "'{0}' asks for a singleton and declares '{1}' of its own, which the generated half would declare as well",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>The type has no constructor which the generated <c>Instance</c> could call.</summary>
        public static readonly DiagnosticDescriptor ParameterlessConstructorMissing = new(
            "SING0004",
            "A singleton must have a parameterless constructor",
            "'{0}' asks for a singleton and has no parameterless constructor for the generated 'Instance' to call",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>The named creator does not make the type which named it.</summary>
        public static readonly DiagnosticDescriptor CreatorDoesNotMakeTheType = new(
            "SING0005",
            "A creator must implement ICreator<T> for the singleton",
            "'{0}' was named as the creator of '{1}' and does not implement 'ICreator<{1}>'",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>The creator has no constructor which the given arguments fit.</summary>
        public static readonly DiagnosticDescriptor CreatorConstructorMissing = new(
            "SING0006",
            "A creator must have a constructor which the arguments fit",
            "'{0}' has no accessible constructor which the arguments fit ({1})",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>An asset type was asked for by a type which Unity keeps no asset of.</summary>
        public static readonly DiagnosticDescriptor AssetTypeUnsupportedType = new(
            "SING0007",
            "An asset type applies to a MonoBehaviour or a ScriptableObject",
            "'{0}' asks for '{1}' and is neither a MonoBehaviour nor a ScriptableObject, so there is no asset to load",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>No asset of the type was found under a <c>Resources</c> folder.</summary>
        public static readonly DiagnosticDescriptor ResourceAssetNotFound = new(
            "SING0008",
            "A Resources singleton must have an asset to load",
            "No {1} under a 'Resources' folder in '{2}' holds '{0}', so the path to load it by is not known",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>An Addressables singleton was given no address.</summary>
        public static readonly DiagnosticDescriptor AddressablePathRequired = new(
            "SING0009",
            "An Addressables singleton must be given its address",
            "'{0}' asks for 'AssetType.Addressable' without an address, and an address is not read off a project",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>The compilation does not see a type which the generated code would name.</summary>
        public static readonly DiagnosticDescriptor AddressablesNotReferenced = new(
            "SING0010",
            "An Addressables singleton needs the Addressables package",
            "'{0}' asks for 'AssetType.Addressable' and the compilation does not refer to '{1}', which the generated code names",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary><c>PersistentAttribute</c> was put on something which has no object to keep.</summary>
        public static readonly DiagnosticDescriptor PersistentRequiresMonoBehaviour = new(
            "SING0011",
            "Persistent applies to a MonoBehaviour",
            "'{0}' asks to be kept across scene loads and is not a MonoBehaviour, so there is nothing to keep",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary><c>InvisibleAttribute</c> was put on something which has no object to hide.</summary>
        public static readonly DiagnosticDescriptor InvisibleRequiresMonoBehaviour = new(
            "SING0012",
            "Invisible applies to a MonoBehaviour",
            "'{0}' asks to be hidden and is not a MonoBehaviour, so there is nothing to hide",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>The Unity project which holds the compiling scripts could not be found.</summary>
        public static readonly DiagnosticDescriptor UnityProjectNotFound = new(
            "SING0013",
            "The Unity project must be found to read a Resources path",
            "'{0}' asks for a 'Resources' singleton and names no path, and the Unity project which holds the scripts being compiled could not be found, so the path could not be read off it",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}