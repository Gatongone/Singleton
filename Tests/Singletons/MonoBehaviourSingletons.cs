using Singleton.Runtime;
using UnityEngine;

namespace Singleton.Tests
{
    /// <summary>A <c>MonoBehaviour</c> which Unity makes and which nothing destroys.</summary>
    [Singleton]
    public partial class PlainMonoBehaviourSingleton : MonoBehaviour
    {
        /// <summary>What a test writes to tell one instance from another.</summary>
        public int Mark;
    }

    /// <summary>A <c>ScriptableObject</c> which <c>CreateInstance</c> makes.</summary>
    [Singleton]
    public partial class PlainScriptableObjectSingleton : ScriptableObject
    {
        /// <summary>What a test writes to tell one instance from another.</summary>
        public int Mark;
    }

    /// <summary>A <c>MonoBehaviour</c> whose object outlives a scene load.</summary>
    [Singleton, DontDestroyOnLoad]
    public partial class KeptMonoBehaviourSingleton : MonoBehaviour
    {
    }

    /// <summary>
    /// A <c>MonoBehaviour</c> whose object is taken out of the hierarchy, out of the Inspector and out of what a save
    /// writes.
    /// </summary>
    [Singleton, Invisible]
    public partial class HiddenMonoBehaviourSingleton : MonoBehaviour
    {
    }

    /// <summary>
    /// A <c>MonoBehaviour</c> which wrote an <c>Awake</c> of its own, so that a test can tell that what was woven into
    /// it ran as well as what it wrote.
    /// </summary>
    [Singleton]
    public partial class AwakeMonoBehaviourSingleton : MonoBehaviour
    {
        /// <summary>Whether the <c>Awake</c> which the type wrote ran.</summary>
        public bool Awoke;

        private void Awake() => Awoke = true;
    }
}