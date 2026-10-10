using System;

namespace Singleton.Runtime
{
    /// <summary>
    /// Lets the singleton be the node which is already in the scene tree, rather than one of its own.<para/>
    /// The generator writes a search for the first node of the type in front of what makes the instance, so that a
    /// node which was put in a scene by hand is the one <c>Instance</c> answers with and nothing is made beside it.
    /// Without the attribute no search is written at all, and the instance is always the one the singleton makes.
    /// </summary>
    /// <remarks>
    /// Only a <c>Node</c> is in a scene tree, so the attribute applies to a <c>Node</c> alone and is an error on
    /// anything else. A <c>Resource</c> is loaded by a path rather than found in a tree, and what keeps it the same
    /// object is the loader which remembers it rather than a search.<para/>
    /// The search is a walk of the whole tree, which is why it is asked for rather than always done: it happens at most
    /// once per singleton, because what it finds is held in the field from then on.<para/>
    /// The attribute does nothing on its own: what it changes is the property which <see cref="SingletonAttribute"/>
    /// generates, so a type which carries this one and asks for no singleton is one which nothing is generated for. It
    /// decides nothing either where there is no search to write, which is a singleton made by a creator or loaded from
    /// an asset.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ReusableAttribute : Attribute
    {
        /// <summary>
        /// Lets the singleton be the node which is already in the scene tree.
        /// </summary>
        public ReusableAttribute() { }
    }
}
