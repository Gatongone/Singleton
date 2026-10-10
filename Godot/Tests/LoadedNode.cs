using Godot;
using Singleton.Runtime;

namespace E2E
{
    /// <summary>
    /// A node singleton which named no path, so that the generator has to read one off the project: the first scene
    /// which names this script is the one which is loaded.
    /// </summary>
    [Singleton(AssetType.Resources)]
    public partial class LoadedNode : Node
    {
        public int Mark;
    }
}
