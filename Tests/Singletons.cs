#nullable enable

using System;
using System.Reflection;
using Object = UnityEngine.Object;

namespace Singleton.Tests
{
    /// <summary>
    /// What the tests do to a singleton which they did not make.<para/>
    /// Every singleton keeps its instance in a field which lives for as long as the process does, and a field which a
    /// test filled before another one ran still holds what that test made. The field is given up before each test,
    /// so that what a test reads is what it made and not what another one left behind.
    /// </summary>
    internal static class Singletons
    {
        /// <summary>The name of the field which holds the instance of every singleton.</summary>
        private const string INSTANCE = "s_Instance";

        /// <summary>The types of the singletons which the tests read.</summary>
        public static readonly Type[] All =
        {
            typeof(NewableSingleton),
            typeof(CountedSingleton),
            typeof(PlainMonoBehaviourSingleton),
            typeof(PlainScriptableObjectSingleton),
            typeof(KeptMonoBehaviourSingleton),
            typeof(HiddenMonoBehaviourSingleton),
            typeof(AwakeMonoBehaviourSingleton),
            typeof(ExplicitResourcesMonoBehaviourSingleton),
            typeof(ResourcesMonoBehaviourSingleton),
            typeof(ResourcesScriptableObjectSingleton),
            typeof(Nest.Inner),
            typeof(Nest.Made),
            typeof(GlobalTestSingleton),
            typeof(OrderedMonoBehaviourSingleton),
            typeof(DerivedFromWakingBase),
            typeof(BaseSingleton),
            typeof(DerivedSingleton),
            typeof(DerivedFromVirtualWakingBase),
            typeof(SilentBaseSingleton),
            typeof(SilentDerivedSingleton)
        };

        /// <summary>
        /// Destroy the object of every singleton which holds one, so that a test begins with the scene it expects.
        /// </summary>
        public static void Destroy()
        {
            foreach (var type in All)
            {
                if (!typeof(UnityEngine.Component).IsAssignableFrom(type)) continue;
                if (Read(type) is not UnityEngine.Component component) continue;

                // An object which Unity destroyed is one which is equal to null and whose members throw, so it is left
                // where it is rather than read.
                if (component == null) continue;

                Object.DestroyImmediate(component.gameObject);
            }

            Clear();
        }

        /// <summary>
        /// Give up the instance of every singleton, which is what a type does when the object goes.
        /// </summary>
        public static void Clear()
        {
            foreach (var type in All) Of(type)?.SetValue(null, null);
        }

        /// <summary>
        /// Read the instance which a singleton holds, through the private field which test code cannot name.
        /// </summary>
        /// <param name="type">The type of the singleton.</param>
        /// <returns>The instance, or <c>null</c> where there is none.</returns>
        public static object? Read(Type type) => Of(type)?.GetValue(null);

        /// <summary>
        /// Read the field which holds the instance of a singleton.
        /// </summary>
        /// <param name="type">The type of the singleton.</param>
        /// <returns>The field.</returns>
        private static FieldInfo? Of(Type type) => type.GetField(INSTANCE, BindingFlags.Static | BindingFlags.NonPublic);
    }
}