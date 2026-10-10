using Singleton.Runtime;

namespace Singleton.Tests
{
    /// <summary>A plain type which is made by its own parameterless constructor.</summary>
    [Singleton]
    public partial class NewableSingleton
    {
        /// <summary>What a test writes to tell one instance from another.</summary>
        public int Mark;
    }
}