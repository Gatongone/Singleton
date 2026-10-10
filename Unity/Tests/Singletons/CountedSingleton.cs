using Singleton.Runtime;

namespace Singleton.Tests
{
    /// <summary>
    /// Makes <see cref="CountedSingleton"/> and counts the instances it makes, so that a test can tell that the
    /// creator ran once and that the arguments the attribute named reached it.
    /// </summary>
    public sealed class CountingCreator : ICreator<CountedSingleton>
    {
        /// <summary>How many instances were made.</summary>
        public static int Made;

        private readonly string m_Name;
        private readonly int m_Number;

        /// <summary>
        /// Make a creator which writes the name and the number it is given into what it makes.
        /// </summary>
        /// <param name="name">The name to write into the instance.</param>
        /// <param name="number">The number to write into the instance.</param>
        public CountingCreator(string name, int number)
        {
            m_Name = name;
            m_Number = number;
        }

        /// <inheritdoc/>
        public CountedSingleton Create()
        {
            Made++;
            return new CountedSingleton { Name = m_Name, Number = m_Number };
        }
    }

    /// <summary>A type which is made by a creator which the attribute names arguments for.</summary>
    [Singleton(typeof(CountingCreator), "made", 42)]
    public partial class CountedSingleton
    {
        /// <summary>The name the creator writes into the instance.</summary>
        public string Name;

        /// <summary>The number the creator writes into the instance.</summary>
        public int Number;
    }
}