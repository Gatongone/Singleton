namespace Singleton.Runtime
{
    /// <summary>
    /// The kind of asset which a singleton is loaded from, where it is not one which is already in memory.
    /// </summary>
    /// <remarks>
    /// An asset type asks for an asset rather than for a constructor, so it applies to the two kinds of type which
    /// Unity keeps assets of - <c>MonoBehaviour</c> and <c>ScriptableObject</c> - and to nothing else.
    /// </remarks>
    public enum AssetType
    {
        /// <summary>
        /// A folder named <c>Resources</c> in the project, read with <c>UnityEngine.Resources</c>.
        /// </summary>
        Resources,

        /// <summary>
        /// A catalog of the Addressables package, read with <c>UnityEngine.AddressableAssets.Addressables</c>.
        /// </summary>
        Addressable
    }
}