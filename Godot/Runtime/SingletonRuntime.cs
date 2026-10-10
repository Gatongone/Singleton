#nullable enable

using Godot;

namespace Singleton.Runtime
{
    /// <summary>
    /// What the generated half of a singleton and the weaving call, rather than writing the whole of it into every
    /// type: finding the node which is already in the tree, keeping one across a change of scene, and telling whether
    /// the instance which is held is still there.
    /// </summary>
    /// <remarks>
    /// Everything here is written against the types of Godot which the generated code would otherwise have to name in
    /// full, and it is a part of the package rather than of the generated half so that a project woven by one version
    /// of the package and run against another is one thing rather than two.
    /// </remarks>
    public static class SingletonRuntime
    {
        /// <summary>
        /// Whether a node which is held is still there.
        /// </summary>
        /// <remarks>
        /// A node which was freed is not <c>null</c> in the way a reference is: it is a value which throws when it is
        /// read, and this is what asks whether reading it is safe. What the weaver clears on the way out of the tree is
        /// the ordinary case, and this is what covers a node which was freed without the message being run at all.
        /// </remarks>
        /// <param name="node">The node, which may be nothing.</param>
        /// <returns>Whether it is there.</returns>
        public static bool Alive(Node? node) => node != null && GodotObject.IsInstanceValid(node);

        /// <summary>
        /// Find the first node of a type in the scene tree.
        /// </summary>
        /// <remarks>
        /// The walk is the whole tree, because a singleton may be anywhere in it: the searches of Godot are made by the
        /// names of the classes of the engine, and a script class of C# is one of those names rather than a name of its
        /// own, so two script classes which extend the same node are one name to the engine and two types to this.<para/>
        /// Nothing here is cached. The search happens the first time a singleton which asked to be reusable is read and
        /// never again, because what it answers with is put in the field which is read from then on.
        /// </remarks>
        /// <typeparam name="T">The type of the singleton.</typeparam>
        /// <returns>The node, or <c>null</c> where there is none.</returns>
        public static T? Search<T>() where T : Node
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return null;

            var root = tree.Root;
            return root == null ? null : Descend<T>(root);
        }

        /// <summary>
        /// Keep a node across a change of scene by moving it under the root of the scene tree.
        /// </summary>
        /// <remarks>
        /// A node which is a child of the root is not a part of the scene which is current, so the calls which replace
        /// that scene leave it where it is.<para/>
        /// The move is deferred rather than done where the call is made. A node which is moved is one the engine takes
        /// out of the tree and puts back into it, and a message which runs while another message of the same node is
        /// running is one which finds the node half way through what it was doing. Deferring it puts the move after the
        /// message which asked for it, which is what makes it safe to ask from <c>_Ready</c>.
        /// </remarks>
        /// <param name="node">The node to keep.</param>
        public static void Keep(Node node)
        {
            var root = node.GetTree()?.Root;
            if (root == null || node.GetParent() == root) return;

            node.CallDeferred(Node.MethodName.Reparent, root, true);
        }

        /// <summary>
        /// Walk the children of a node for the first one of a type, in the order the tree holds them.
        /// </summary>
        /// <typeparam name="T">The type of the singleton.</typeparam>
        /// <param name="node">The node whose children are walked.</param>
        /// <returns>The node, or <c>null</c> where there is none.</returns>
        private static T? Descend<T>(Node node) where T : Node
        {
            foreach (var child in node.GetChildren())
            {
                if (child is T found) return found;
                if (Descend<T>(child) is { } deeper) return deeper;
            }

            return null;
        }
    }
}
