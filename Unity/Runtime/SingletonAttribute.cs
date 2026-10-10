using System;

namespace Singleton.Runtime
{
    /// <summary>
    /// Gives a type a singleton of its own, without a base type.<para/>
    /// The generator answers the attribute with a <c>private static T s_Instance</c> field and a
    /// <c>public static T Instance</c> property, which makes the instance the first time the property is read and
    /// returns that same instance from then on. The weaver adds the Unity messages which keep the field in step with the
    /// instance Unity made, and clears the field when that instance is destroyed.
    /// </summary>
    /// <remarks>
    /// The type is written in two halves, so the half which declares it must be <c>partial</c>: the half which holds the
    /// field and the property is a second declaration of the type rather than a part of the first one. A type which is
    /// not <c>partial</c> is reported as an error, as is one which declares an <c>Instance</c> or an <c>s_Instance</c>
    /// of its own.<para/>
    /// Which instance is made when the field is empty is decided by what the attribute was given. Given nothing, a
    /// <c>MonoBehaviour</c> is looked for among the loaded objects and made on a new <c>GameObject</c> where there is none,
    /// a <c>ScriptableObject</c> is looked for the same way and made with <c>CreateInstance</c> where there is none, and
    /// any other type is made with its parameterless constructor. A creator makes the instance instead, and an asset
    /// type loads it from a <c>Resources</c> folder or from an Addressables catalog.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class SingletonAttribute : Attribute
    {
        /// <summary>
        /// Gives the type a singleton which Unity makes, or which its own parameterless constructor makes.
        /// </summary>
        public SingletonAttribute() { }

        /// <summary>
        /// Gives the type a singleton which is loaded from an asset.
        /// </summary>
        /// <param name="assetType">The kind of asset which the singleton is loaded from.</param>
        /// <param name="path">
        /// The path the asset is loaded by: a path within a <c>Resources</c> folder, without the extension, for
        /// <see cref="AssetType.Resources"/>; an address, for <see cref="AssetType.Addressable"/>.<para/>
        /// Where it is left out, the generator reads it off the project, and the first asset under a <c>Resources</c>
        /// folder which holds the type is the one which is loaded. A project which holds none is reported as an error.
        /// An Addressables address cannot be read off a project, so <see cref="AssetType.Addressable"/> requires one.
        /// </param>
        public SingletonAttribute(AssetType assetType, string path = null) { }

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