using Singleton.Runtime;
using UnityEngine;

namespace Singleton.Tests
{
    /// <summary>
    /// A <c>MonoBehaviour</c> which named no path, so that a test can tell that the generator read the path of the first
    /// prefab under a <c>Resources</c> folder which holds the type.
    /// </summary>
    [Singleton(AssetType.Resources)]
    public partial class ResourcesMonoBehaviourSingleton : MonoBehaviour
    {
        /// <summary>What the prefab holds, which a test reads to tell the asset from a made one.</summary>
        public int Mark;

        /// <summary>What the prefab holds, which a test reads to tell the asset from a made one.</summary>
        public string Note;
    }
}