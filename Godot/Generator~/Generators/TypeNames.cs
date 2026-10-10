namespace Singleton.Generator
{
    /// <summary>
    /// The metadata names of the types which the generator reads off a compilation.
    /// </summary>
    /// <remarks>
    /// A compilation which does not hold one of the types which are read off it is one which the generator has nothing
    /// to say about, and the parts of the generated code which name a type of Godot are written only where that type is
    /// there to name.
    /// </remarks>
    internal static class TypeNames
    {
        /// <summary>The attribute which asks for a singleton.</summary>
        public const string SINGLETON_ATTRIBUTE = "Singleton.Runtime.SingletonAttribute";

        /// <summary>The creator which the attribute may name.</summary>
        public const string CREATOR = "Singleton.Runtime.ICreator`1";

        /// <summary>Where an asset-backed singleton is loaded from.</summary>
        public const string ASSET_TYPE = "Singleton.Runtime.AssetType";

        /// <summary>The type a creator is named by, which is looked up by name because it has no special type of its own.</summary>
        public const string SYSTEM_TYPE = "System.Type";

        /// <summary>Keeps the node of a singleton across a change of scene.</summary>
        public const string PERSISTENT_ATTRIBUTE = "Singleton.Runtime.PersistentAttribute";

        /// <summary>Lets the singleton be the node which is already in the scene tree.</summary>
        public const string REUSABLE_ATTRIBUTE = "Singleton.Runtime.ReusableAttribute";

        /// <summary>The type a singleton is given the notifications of the tree for.</summary>
        public const string NODE = "Godot.Node";

        /// <summary>The type a singleton is loaded as where it is an asset the loader remembers.</summary>
        public const string RESOURCE = "Godot.Resource";
    }
}
