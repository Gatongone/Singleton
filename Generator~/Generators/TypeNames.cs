namespace Singleton.Generator
{
    /// <summary>
    /// The metadata names of the types which the generator reads off a compilation.
    /// </summary>
    /// <remarks>
    /// A compilation which does not hold one of the types which are read off it is one which the generator has nothing
    /// to say about, and the parts of the generated code which name a type of Unity are written only where that type is
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

        /// <summary>Keeps the object of a singleton across scene loads.</summary>
        public const string DONT_DESTROY_ON_LOAD_ATTRIBUTE = "Singleton.Runtime.DontDestroyOnLoadAttribute";

        /// <summary>Takes the object of a singleton out of the hierarchy, the Inspector and what a save writes.</summary>
        public const string INVISIBLE_ATTRIBUTE = "Singleton.Runtime.InvisibleAttribute";

        /// <summary>The type a singleton is given an <c>Awake</c> for.</summary>
        public const string MONO_BEHAVIOUR = "UnityEngine.MonoBehaviour";

        /// <summary>The type a singleton is given an <c>OnEnable</c> for.</summary>
        public const string SCRIPTABLE_OBJECT = "UnityEngine.ScriptableObject";

        /// <summary>The type an <c>Addressable</c> singleton is loaded with.</summary>
        public const string ADDRESSABLES = "UnityEngine.AddressableAssets.Addressables";

        /// <summary>The enumeration an <c>Addressable</c> load is read by.</summary>
        public const string ASYNC_OPERATION_STATUS = "UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus";
    }
}