using System;

namespace Singleton.Runtime
{
    /// <summary>
    /// Keeps the <c>GameObject</c> of a <c>MonoBehaviour</c> alive across scene loads.<para/>
    /// The weaver writes <c>DontDestroyOnLoad(gameObject)</c> into the <c>Awake</c> of the type, so that the object is
    /// kept from the moment the type wakes rather than later on.
    /// </summary>
    /// <remarks>
    /// Only a <c>MonoBehaviour</c> has a <c>GameObject</c> to keep, so the attribute is an error on anything else.<para/>
    /// The attribute stands on its own and needs no <see cref="SingletonAttribute"/>: a type which carries only this one
    /// is still given an <c>Awake</c> which keeps its object. Where the two are on the same type the singleton is
    /// settled first, so an object which is destroyed for not being the singleton is not kept as well.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PersistentAttribute : Attribute
    {
        /// <summary>
        /// Keeps the <c>GameObject</c> of the type alive across scene loads.
        /// </summary>
        public PersistentAttribute() { }
    }
}