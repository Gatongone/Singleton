using Singleton.Runtime;
using UnityEngine;

namespace Singleton.Tests
{
    /// <summary>
    /// A <c>MonoBehaviour</c> which asked to be hidden and asked for no singleton, so that what the attribute does can
    /// be tested on its own.<para/>
    /// It is not <c>partial</c> on purpose: nothing is generated for a type which asks for no singleton, so nothing
    /// has to be generated beside it.
    /// </summary>
    [Invisible]
    public class HiddenMonoBehaviour : MonoBehaviour
    {
    }

    /// <summary>A <c>MonoBehaviour</c> which asked to be kept across scene loads and asked for no singleton.</summary>
    [Persistent]
    public class KeptMonoBehaviour : MonoBehaviour
    {
    }

    /// <summary>
    /// A singleton which asked for both attributes and recorded what its object was by the time the <c>Awake</c> which
    /// the type wrote ran.<para/>
    /// What is woven is written in front of that body, so what it recorded is what had already been done to the object
    /// - and what a second instance recorded is what had not.
    /// </summary>
    [Singleton, Persistent, Invisible]
    public partial class OrderedMonoBehaviourSingleton : MonoBehaviour
    {
        /// <summary>The flags of the object when the body of the <c>Awake</c> ran.</summary>
        public HideFlags FlagsWhenAwoke;

        /// <summary>The scene of the object when the body of the <c>Awake</c> ran.</summary>
        public string SceneWhenAwoke;

        private void Awake()
        {
            FlagsWhenAwoke = gameObject.hideFlags;
            SceneWhenAwoke = gameObject.scene.name;
        }
    }
}