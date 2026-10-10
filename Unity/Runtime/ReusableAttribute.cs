using System;

namespace Singleton.Runtime
{
    /// <summary>
    /// Lets the singleton be the object which is already in the scene, rather than one of its own.<para/>
    /// The generator writes a search for the first loaded object of the type in front of what makes the instance, so
    /// that a type which was put in a scene by hand is the one <c>Instance</c> answers with and nothing is made beside
    /// it. Without the attribute no search is written at all, and the instance is always the one the singleton makes.
    /// </summary>
    /// <remarks>
    /// The search is <c>Object.FindAnyObjectByType&lt;T&gt;</c>, which answers with the objects of a scene and with no
    /// asset of the project, so the attribute applies to a <c>MonoBehaviour</c> alone and is an error on anything else.
    /// A <c>ScriptableObject</c> is an asset, so the search would never answer with one, and the type would be given a
    /// search which finds nothing rather than the instance it asked to be.<para/>
    /// What the search leaves out as well is the objects which are not active and the objects which were given
    /// <c>HideFlags.DontSave</c>. The flags are what <see cref="InvisibleAttribute"/> writes, so a singleton which
    /// carries both is one which is never found by its own search; the field still holds it, so what is given up is the
    /// search rather than the singleton.<para/>
    /// The attribute does nothing on its own: what it changes is the property which <see cref="SingletonAttribute"/>
    /// generates, so a type which carries this one and asks for no singleton is one which nothing is generated for. It
    /// decides nothing either where there is no search to write, which is a singleton made by a creator and a singleton
    /// loaded from an asset which is not a <c>MonoBehaviour</c>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ReusableAttribute : Attribute
    {
        /// <summary>
        /// Lets the singleton be the object which is already in the scene.
        /// </summary>
        public ReusableAttribute() { }
    }
}
