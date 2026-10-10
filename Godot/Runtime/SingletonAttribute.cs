#nullable enable

using System;

namespace Singleton.Runtime
{
    /// <summary>
    /// Gives a type a singleton of its own, without a base type.<para/>
    /// The generator answers the attribute with a <c>private static T s_Instance</c> field and a
    /// <c>public static T Instance</c> property, which makes the instance the first time the property is read and
    /// returns that same instance from then on. The weaver adds the Godot notifications which keep the field in step
    /// with the node the engine made, and clears the field when that node leaves the tree.
    /// </summary>
    /// <remarks>
    /// The type is written in two halves, so the half which declares it must be <c>partial</c>: the half which holds the
    /// field and the property is a second declaration of the type rather than a part of the first one. A type which is
    /// not <c>partial</c> is reported as an error, as is one which declares an <c>Instance</c> or an <c>s_Instance</c>
    /// of its own.<para/>
    /// Which instance is made when the field is empty is decided by what the attribute was given. Given nothing, a
    /// <c>Node</c> is made and put under the root of the scene tree, and any other type is made with its parameterless
    /// constructor. A creator makes the instance instead, and an asset type loads it from a path in the project.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class SingletonAttribute : Attribute
    {
        /// <summary>
        /// Gives the type a singleton which Godot makes, or which its own parameterless constructor makes.
        /// </summary>
        public SingletonAttribute() { }

        /// <summary>
        /// Gives the type a singleton which is loaded from an asset.
        /// </summary>
        /// <param name="assetType">The kind of asset which the singleton is loaded from.</param>
        /// <param name="path">
        /// The path the asset is loaded by, as a Godot path such as <c>res://Player.tscn</c>.<para/>
        /// Where it is left out, the generator reads it off the project: the first scene or resource which names the
        /// script which declares the type is the one which is loaded, and a project which holds none is reported as an
        /// error.
        /// </param>
        public SingletonAttribute(AssetType assetType, string? path = null) { }

        /// <summary>
        /// Gives the type a singleton which a creator makes.
        /// </summary>
        /// <param name="creator">
        /// The <see cref="ICreator{T}"/> which makes the singleton. If the named type implements <c>ICreator&lt;T&gt;</c>
        /// for more than one <c>T</c>, the one whose <c>T</c> is the type the attribute is on is the one which is used.
        /// </param>
        /// <param name="args">
        /// The arguments the constructor of the creator is called with. A creator with no constructor which these fit is
        /// reported as an error rather than left to the compiler.
        /// </param>
        public SingletonAttribute(Type creator, params object[] args) { }
    }
}
