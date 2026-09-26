using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Singleton.Tests
{
    /// <summary>
    /// What the weaving does: the messages which are written into a singleton keep its field true, and the two
    /// attributes which ask something of the object do what they say.
    /// </summary>
    public sealed class WeaverTests
    {
        /// <summary>Begin every test with no singleton holding an instance.</summary>
        [UnitySetUp]
        public IEnumerator Before()
        {
            Singletons.Destroy();
            yield return null;
        }

        /// <summary>Leave nothing of the test behind for the one after it.</summary>
        [UnityTearDown]
        public IEnumerator After()
        {
            Singletons.Destroy();
            yield return null;
        }

        [UnityTest]
        public IEnumerator An_awake_which_was_written_still_runs()
        {
            var made = new GameObject(nameof(AwakeMonoBehaviourSingleton));
            var component = made.AddComponent<AwakeMonoBehaviourSingleton>();

            Assert.That(component.Awoke, Is.True);

            yield return null;
            Assert.That(AwakeMonoBehaviourSingleton.Instance, Is.SameAs(component));
        }

        [UnityTest]
        public IEnumerator A_second_instance_is_destroyed_with_its_object()
        {
            var first = PlainMonoBehaviourSingleton.Instance;
            yield return null;

            var second = new GameObject("Second");
            second.AddComponent<PlainMonoBehaviourSingleton>();
            yield return null;

            Assert.That(second == null, Is.True, "the object of the second instance was destroyed");
            Assert.That(PlainMonoBehaviourSingleton.Instance, Is.SameAs(first), "the first instance is still the one");
        }

        [UnityTest]
        public IEnumerator An_object_which_is_destroyed_gives_the_field_up()
        {
            var first = PlainMonoBehaviourSingleton.Instance;
            yield return null;

            Object.Destroy(first.gameObject);
            yield return null;

            Assert.That(Singletons.Read(typeof(PlainMonoBehaviourSingleton)), Is.Null, "the field was given up");
        }

        [UnityTest]
        public IEnumerator DontDestroyOnLoad_keeps_the_object()
        {
            var instance = KeptMonoBehaviourSingleton.Instance;
            yield return null;

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.gameObject.scene.name, Is.EqualTo("DontDestroyOnLoad"));
        }

        [UnityTest]
        public IEnumerator Invisible_takes_the_object_out_of_the_hierarchy()
        {
            var instance = HiddenMonoBehaviourSingleton.Instance;
            yield return null;

            var expected = HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.DontSave;
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.gameObject.hideFlags, Is.EqualTo(expected));
        }

        [UnityTest]
        public IEnumerator Invisible_alone_hides_the_object_of_a_MonoBehaviour()
        {
            var made = new GameObject(nameof(HiddenMonoBehaviour));
            made.AddComponent<HiddenMonoBehaviour>();

            var expected = HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.DontSave;
            Assert.That(made.hideFlags, Is.EqualTo(expected), "the attribute asked for no singleton and was woven anyway");

            Object.DestroyImmediate(made);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DontDestroyOnLoad_alone_keeps_the_object_of_a_MonoBehaviour()
        {
            var made = new GameObject(nameof(KeptMonoBehaviour));
            made.AddComponent<KeptMonoBehaviour>();
            yield return null;

            Assert.That(made == null, Is.False);
            Assert.That(made.scene.name, Is.EqualTo("DontDestroyOnLoad"), "the attribute asked for no singleton and was woven anyway");

            Object.DestroyImmediate(made);
        }

        [UnityTest]
        public IEnumerator What_the_attributes_ask_of_the_object_is_done_before_the_Awake_which_was_written_runs()
        {
            var instance = OrderedMonoBehaviourSingleton.Instance;
            yield return null;

            var expected = HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.DontSave;
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.FlagsWhenAwoke, Is.EqualTo(expected), "the object was hidden before the body ran");
            Assert.That(instance.SceneWhenAwoke, Is.EqualTo("DontDestroyOnLoad"), "the object was kept before the body ran");
        }

        [UnityTest]
        public IEnumerator A_second_instance_is_given_up_before_what_the_attributes_ask_is_done()
        {
            var first = OrderedMonoBehaviourSingleton.Instance;
            yield return null;

            var second = new GameObject("Second").AddComponent<OrderedMonoBehaviourSingleton>();

            Assert.That(second.gameObject.hideFlags, Is.EqualTo(HideFlags.None), "a second instance was hidden as well");
            Assert.That(second.gameObject.scene.name, Is.Not.EqualTo("DontDestroyOnLoad"), "a second instance was kept as well");
            Assert.That(OrderedMonoBehaviourSingleton.Instance, Is.SameAs(first));

            yield return null;
        }
    }
}