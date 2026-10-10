using System;

namespace Singleton.Runtime
{
    /// <summary>
    /// Keeps the node of a <c>Node</c> singleton alive across a change of scene.<para/>
    /// The weaver writes <c>SingletonRuntime.Keep(this)</c> into the <c>_Ready</c> of the type, which moves the node
    /// under the root of the scene tree. A node which is a child of the root is not part of the current scene, so
    /// <c>SceneTree.ChangeSceneToFile</c> and <c>ChangeSceneToPacked</c> leave it where it is.
    /// </summary>
    /// <remarks>
    /// Only a <c>Node</c> is in a scene tree, so the attribute is an error on anything else. A <c>Resource</c> is
    /// unloaded only when nothing refers to it, and what keeps it is that the field refers to it.<para/>
    /// The attribute stands on its own and needs no <see cref="SingletonAttribute"/>: a type which carries only this
    /// one is still given a <c>_Ready</c> which keeps its node. Where the two are on the same type the singleton is
    /// settled first, so a node which is given up for not being the singleton is not kept as well.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PersistentAttribute : Attribute
    {
        /// <summary>
        /// Keeps the node of the type alive across a change of scene.
        /// </summary>
        public PersistentAttribute() { }
    }
}
