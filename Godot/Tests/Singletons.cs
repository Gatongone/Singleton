using Godot;
using Singleton.Runtime;

namespace E2E
{
    /// <summary>A node singleton which may be one which is already in the scene tree.</summary>
    [Singleton, Reusable]
    public partial class ReusableNode : Node
    {
        public int Mark;
    }

    /// <summary>A node singleton which is always one of its own.</summary>
    [Singleton]
    public partial class MadeNode : Node
    {
        public int Mark;
    }

    /// <summary>A node singleton which is moved under the root so that a change of scene leaves it.</summary>
    [Singleton, Persistent]
    public partial class KeptNode : Node
    {
        public int Mark;
    }

    /// <summary>A type which is in no tree, made by its own constructor.</summary>
    [Singleton]
    public partial class PlainThing
    {
        public int Mark;
    }

    /// <summary>A resource singleton, which is made rather than found.</summary>
    [Singleton]
    public partial class PlainResource : Resource
    {
        public int Mark;
    }
}
