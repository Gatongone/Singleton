namespace Singleton.Runtime
{
    /// <summary>
    /// The kind of asset which a singleton is loaded from, where it is not one which is made.
    /// </summary>
    /// <remarks>
    /// A Godot project holds one kind of asset path and one loader for it: <c>res://</c> is read with
    /// <c>ResourceLoader</c>, which remembers what it loaded, so the same path answers with the same object every time
    /// it is asked. The enumeration is here for the shape the attribute is written in rather than for a choice which a
    /// Godot project has to make today.
    /// </remarks>
    public enum AssetType
    {
        /// <summary>
        /// A path in the project, read with <c>ResourceLoader</c>.
        /// </summary>
        Resources
    }
}
