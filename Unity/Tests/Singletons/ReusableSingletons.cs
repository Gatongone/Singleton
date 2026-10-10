using Singleton.Runtime;
using UnityEngine;

namespace Singleton.Tests
{
    /// <summary>
    /// A <c>MonoBehaviour</c> which asked to be reusable, so that a test can tell that the object which is already in
    /// the scene is the one the property answers with rather than one which is made beside it.
    /// </summary>
    [Singleton, Reusable]
    public partial class ReusableMonoBehaviourSingleton : MonoBehaviour
    {
        /// <summary>What a test writes to tell one instance from another.</summary>
        public int Mark;
    }
}
