using Singleton.Runtime;
using UnityEngine;

namespace Singleton.Tests
{
    /// <summary>
    /// A <c>MonoBehaviour</c> which asked for the path of its prefab, so that a test can tell that the path which was
    /// written is the path which the prefab is loaded by.
    /// </summary>
    [Singleton(AssetType.Resources, "Singleton/ExplicitPrefab")]
    public partial class ExplicitResourcesMonoBehaviourSingleton : MonoBehaviour
    {
        /// <summary>What the prefab holds, which a test reads to tell the asset from a made one.</summary>
        public int Mark;

        /// <summary>What the prefab holds, which a test reads to tell the asset from a made one.</summary>
        public string Note;
    }
}