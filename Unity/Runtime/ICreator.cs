namespace Singleton.Runtime
{
    /// <summary>
    /// Makes the instance which a singleton is first read as.<para/>
    /// A type which no constructor of its own can make names a creator in <see cref="SingletonAttribute"/>. The
    /// generated property then makes an instance of that creator and returns whatever its <see cref="Create"/> method
    /// returns.
    /// </summary>
    /// <remarks>
    /// The creator is made when the singleton is first asked for, and only then, so its constructor runs at most once.
    /// It is not kept afterwards, because what it made is what is kept.
    /// </remarks>
    /// <typeparam name="T">The type of the singleton which the creator makes.</typeparam>
    public interface ICreator<out T>
    {
        /// <summary>
        /// Makes the instance which the singleton is first read as.
        /// </summary>
        /// <returns>The instance.</returns>
        T Create();
    }
}