using Singleton.Runtime;

/// <summary>
/// A singleton which is declared in no namespace at all, so that the half which is generated for such a type can be
/// read: what is generated is a declaration of the type, and a type which no namespace holds is declared where it was
/// written.
/// </summary>
[Singleton]
public partial class GlobalTestSingleton
{
    /// <summary>What a test writes to tell one instance from another.</summary>
    public int Mark;
}