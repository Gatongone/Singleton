using Singleton.Runtime;

namespace Singleton.Tests
{
    /// <summary>
    /// A type which holds singletons, so that the half which is generated for a type declared inside another type can
    /// be read: the generated half is a declaration of the singleton, so it is written inside the type which holds it,
    /// and the type which holds it has to be <c>partial</c> for that to be possible.
    /// </summary>
    public partial class Nest
    {
        /// <summary>A singleton which is declared inside another type.</summary>
        [Singleton]
        public partial class Inner
        {
            /// <summary>What a test writes to tell one instance from another.</summary>
            public int Mark;
        }

        /// <summary>A singleton which a creator makes and which is declared inside another type.</summary>
        [Singleton(typeof(InnerCreator), 7)]
        public partial class Made
        {
            /// <summary>The number the creator writes into the instance.</summary>
            public int Number;
        }
    }

    /// <summary>Makes the singleton which is declared inside another type.</summary>
    public sealed class InnerCreator : ICreator<Nest.Made>
    {
        private readonly int m_Number;

        /// <summary>
        /// Make a creator which writes the number it is given into what it makes.
        /// </summary>
        /// <param name="number">The number to write into the instance.</param>
        public InnerCreator(int number) => m_Number = number;

        /// <inheritdoc/>
        public Nest.Made Create() => new Nest.Made { Number = m_Number };
    }
}