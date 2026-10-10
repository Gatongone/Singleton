using Singleton.Runtime;
using UnityEngine;

namespace Singleton.Tests
{
    /// <summary>A base class which wrote an <c>Awake</c> of its own and asked for no singleton.</summary>
    public class WakingBase : MonoBehaviour
    {
        /// <summary>Whether the <c>Awake</c> which the base class wrote ran.</summary>
        public bool Awoke;

        private void Awake() => Awoke = true;
    }

    /// <summary>A singleton which derives from a type which wrote an <c>Awake</c> of its own.</summary>
    [Singleton]
    public partial class DerivedFromWakingBase : WakingBase
    {
    }

    /// <summary>A singleton which derives from another singleton, and which wrote an <c>Awake</c> of its own.</summary>
    [Singleton]
    public partial class BaseSingleton : MonoBehaviour
    {
        /// <summary>Whether the <c>Awake</c> which this type wrote ran.</summary>
        public bool Awoke;

        private void Awake() => Awoke = true;
    }

    /// <summary>A singleton which derives from a singleton, and which wrote nothing of its own.</summary>
    [Singleton]
    public partial class DerivedSingleton : BaseSingleton
    {
    }

    /// <summary>A base class which wrote a <c>protected virtual</c> <c>Awake</c> of its own.</summary>
    public class VirtualWakingBase : MonoBehaviour
    {
        /// <summary>Whether the <c>Awake</c> which the base class wrote ran.</summary>
        public bool Awoke;

        protected virtual void Awake() => Awoke = true;
    }

    /// <summary>A singleton which derives from a type which wrote a <c>protected virtual</c> <c>Awake</c>.</summary>
    [Singleton]
    public partial class DerivedFromVirtualWakingBase : VirtualWakingBase
    {
    }

    /// <summary>A type which derives from a base class which wrote an <c>Awake</c>, and asked for no singleton.</summary>
    public class PlainDerivedFromWakingBase : WakingBase
    {
    }

    /// <summary>A singleton which wrote nothing of its own, so that the message it is given is one the weaving made.</summary>
    [Singleton]
    public partial class SilentBaseSingleton : MonoBehaviour
    {
    }

    /// <summary>A singleton which derives from one which wrote nothing of its own.</summary>
    [Singleton]
    public partial class SilentDerivedSingleton : SilentBaseSingleton
    {
    }
}