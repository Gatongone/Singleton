// This project exists to hang what the package carries from, and what it carries is a set of files rather than an
// assembly of its own: the runtime is given to the project which asked for it as source, which that project compiles.
// This file is what gives the compiler of this project something to be handed.
namespace Singleton.Godot
{
    /// <summary>What the package of Godot carries, which is nothing this assembly holds.</summary>
    internal static class Package
    {
    }
}
