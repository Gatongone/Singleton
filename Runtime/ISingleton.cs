#if CSHARP_11_OR_NEWER
namespace Singleton.Runtime
{
    /// <summary>
    /// Reads the singleton of a type without naming that type.<para/>
    /// The generated half of a singleton declares this interface beside the type itself, so that a generic method
    /// constrained to <c>ISingleton&lt;T&gt;</c> reads the same instance as <c>T.Instance</c>, without the singleton
    /// having to derive from a base type.
    /// </summary>
    /// <remarks>
    /// A static interface is a compile-time construct rather than a type which a value can have: a singleton implements
    /// it without any instance of the interface existing.
    /// </remarks>
    /// <typeparam name="T">The type of the singleton.</typeparam>
    public interface ISingleton<T> where T : ISingleton<T>
    {
        /// <summary>
        /// The instance of the singleton.
        /// </summary>
        static abstract T Instance { get; }
    }

    /// <summary>
    /// Reads the singleton of a type from a type argument alone.
    /// </summary>
    /// <example>
    /// <code>
    /// var instance = Singleton&lt;MyObject&gt;.Instance;
    /// </code>
    /// </example>
    /// <typeparam name="T">The type of the singleton.</typeparam>
    public static class Singleton<T> where T : class, ISingleton<T>
    {
        /// <summary>
        /// The instance of the singleton.
        /// </summary>
        public static T Instance => T.Instance;
    }
}
#endif