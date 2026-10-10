using System;

namespace Singleton.Weaver
{
    /// <summary>
    /// What the weaver is run with, and what it answers with.
    /// </summary>
    /// <remarks>
    /// The weaver is a program rather than a task of the build, so that what it is asked is a path and what it answers
    /// with is a status: a build runs it after the assembly was written, and a status which is not zero is a build
    /// which stops with what went wrong printed rather than an assembly which is silently not woven.
    /// </remarks>
    internal static class Program
    {
        /// <summary>
        /// Weave the singletons of the assembly which was named.
        /// </summary>
        /// <param name="args">The path of the assembly.</param>
        /// <returns>Zero where the assembly was woven or held nothing to weave.</returns>
        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: Singleton.Weaver <assembly.dll>");
                return 2;
            }

            var path = Path.GetFullPath(args[0]);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Singleton: no assembly at '{path}'.");
                return 2;
            }

            try
            {
                var woven = SingletonWeaver.Weave(path);
                if (woven > 0) Console.WriteLine($"Singleton: wove {woven} type(s) of {Path.GetFileName(path)}.");

                return 0;
            }
            catch (Exception exception)
            {
                // The assembly is left as it was compiled, and what went wrong is printed rather than swallowed: a
                // singleton which is not woven is one whose nodes never reach the field, which reads as a singleton
                // that is made twice rather than as a build which failed.
                Console.Error.WriteLine($"Singleton: the singletons of '{Path.GetFileName(path)}' were not woven, " +
                                        $"and the assembly is left as it was compiled. {exception}");
                return 1;
            }
        }
    }
}
