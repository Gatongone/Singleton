using Microsoft.CodeAnalysis;

namespace Singleton.Generator
{
    /// <summary>
    /// What the generator reports of a type which asked for a singleton and cannot have the one it asked for.
    /// </summary>
    /// <remarks>
    /// Everything here is an error rather than a warning, because each of them is a case of a singleton which would be
    /// answered with something other than what was asked for: a half which was not generated at all, an instance which
    /// is made in another way than the attribute says, or an asset which is not there.<para/>
    /// The identifiers are the ones the package of Unity uses for the same reasons, so that a reader who knows one of
    /// the packages reads the other. Two of them are missing rather than renumbered, because neither is about anything
    /// a Godot project has: <c>SING0010</c> is a package of assets which is not referenced, and <c>SING0012</c> is the
    /// attribute which hides the object of a <c>MonoBehaviour</c>, which Godot has no flags of that kind for.
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

        /// <summary>An asset type was asked for by a type which Godot keeps no asset of.</summary>
        public static readonly DiagnosticDescriptor AssetTypeUnsupportedType = new(
            "SING0007",
            "An asset type applies to a Node or a Resource",
            "'{0}' asks for '{1}' and is neither a Node nor a Resource, so there is no asset to load",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>No scene or resource of the type was found in the project.</summary>
        public static readonly DiagnosticDescriptor ResourceAssetNotFound = new(
            "SING0008",
            "A Resources singleton must have an asset to load",
            "No {1} in '{2}' names the script of '{0}', so the path to load it by is not known",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>An asset type was asked for by a type which is not a file of the project.</summary>
        public static readonly DiagnosticDescriptor AssetPathRequired = new(
            "SING0009",
            "A singleton loaded from an asset must be given its path",
            "'{0}' asks for '{1}' without a path, and the script which declares it is not a file of the project, so no path could be read for it",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary><c>PersistentAttribute</c> was put on something which is not in a scene tree.</summary>
        public static readonly DiagnosticDescriptor PersistentRequiresNode = new(
            "SING0011",
            "Persistent applies to a Node",
            "'{0}' asks to be kept across a change of scene and is not a Node, so there is no node to keep",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>The Godot project which holds the compiling scripts could not be found.</summary>
        public static readonly DiagnosticDescriptor GodotProjectNotFound = new(
            "SING0013",
            "The Godot project must be found to read an asset path",
            "'{0}' asks for a singleton loaded from an asset and names no path, and the Godot project which holds the scripts being compiled could not be found, so the path could not be read off it",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary><c>ReusableAttribute</c> was put on something which is not in a scene tree.</summary>
        public static readonly DiagnosticDescriptor ReusableRequiresNode = new(
            "SING0014",
            "Reusable applies to a Node",
            "'{0}' asks to be reused and is not a Node, so there is no node of a scene tree for a search to answer with",
            CATEGORY,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
