using System;

namespace Singleton.Runtime
{
    /// <summary>
    /// Takes the <c>GameObject</c> of a <c>MonoBehaviour</c> out of the hierarchy, out of the Inspector and out of what
    /// a save writes.<para/>
    /// The weaver writes
    /// <c>gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.DontSave</c> into the
    /// <c>Awake</c> of the type.
    /// </summary>
    /// <remarks>
    /// Only a <c>MonoBehaviour</c> has a <c>GameObject</c> to hide, so the attribute is an error on anything else.<para/>
    /// The attribute stands on its own and needs no <see cref="SingletonAttribute"/>: a type which carries only this one
    /// is still given an <c>Awake</c> which hides its object. Where the two are on the same type the singleton is
    /// settled first, so an object which is destroyed for not being the singleton is not hidden as well.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InvisibleAttribute : Attribute
    {
        /// <summary>
        /// Takes the <c>GameObject</c> of the type out of the hierarchy, out of the Inspector and out of what a save
        /// writes.
        /// </summary>
        public InvisibleAttribute() { }
    }
}