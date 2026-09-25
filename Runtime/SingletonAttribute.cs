using System;
using System.Runtime.CompilerServices;
using Singleton.Runtime;
using UnityEngine;

namespace Singleton.Runtime
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class SingletonAttribute : Attribute
    {
        public SingletonAttribute() { }
        public SingletonAttribute(AssetType assetType, string path = null) { }
        public SingletonAttribute(Type creator, params object[] args) { }
    }

    public interface ICreator<out T> where T : class
    {
        T Create();
    }

    public enum AssetType
    {
        Resources,
        Addressable
    }

#if CSHARP_11_OR_NEWER
    public static class Singleton<T> where T : class, ISingleton<T>
    {
        public static T Instance => T.Instance;
    }

    public interface ISingleton<T>
    {
        static abstract T Instance { get; }
    }
#endif
}