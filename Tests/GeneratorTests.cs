using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Singleton.Tests
{
    /// <summary>
    /// What the generated half of a singleton does: which instance each kind of singleton is read as, and that the
    /// property returns the same instance from then on.
    /// </summary>
    public sealed class GeneratorTests
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

        [Test]
        public void A_type_is_made_by_its_own_constructor()
        {
            var first = NewableSingleton.Instance;
            Assert.That(first, Is.Not.Null);

            first.Mark = 7;
            Assert.That(NewableSingleton.Instance, Is.SameAs(first));
            Assert.That(NewableSingleton.Instance.Mark, Is.EqualTo(7));
        }

        [Test]
        public void TryGetInstance_answers_without_making_one()
        {
            Assert.That(NewableSingleton.TryGetInstance(out var none), Is.False, "an instance was answered with before one was made");
            Assert.That(none, Is.Null);
            Assert.That(Singletons.Read(typeof(NewableSingleton)), Is.Null, "the answer made the instance");

            var made = NewableSingleton.Instance;

            Assert.That(NewableSingleton.TryGetInstance(out var found), Is.True);
            Assert.That(found, Is.SameAs(made));
        }

        [Test]
        public void A_type_declared_inside_another_type_is_made_by_its_own_constructor()
        {
            var first = Nest.Inner.Instance;
            Assert.That(first, Is.Not.Null);

            first.Mark = 9;
            Assert.That(Nest.Inner.Instance, Is.SameAs(first));
            Assert.That(Nest.Inner.Instance.Mark, Is.EqualTo(9));
        }

        [Test]
        public void A_creator_of_a_type_declared_inside_another_type_is_made_with_its_arguments()
        {
            var instance = Nest.Made.Instance;

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.Number, Is.EqualTo(7));
            Assert.That(Nest.Made.Instance, Is.SameAs(instance));
        }

        [Test]
        public void A_type_declared_in_no_namespace_is_made_by_its_own_constructor()
        {
            var first = GlobalTestSingleton.Instance;
            Assert.That(first, Is.Not.Null);

            first.Mark = 4;
            Assert.That(GlobalTestSingleton.Instance, Is.SameAs(first));
            Assert.That(GlobalTestSingleton.Instance.Mark, Is.EqualTo(4));
        }

        [Test]
        public void A_creator_is_made_with_the_arguments_the_attribute_named()
        {
            var made = CountingCreator.Made;
            var instance = CountedSingleton.Instance;

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.Name, Is.EqualTo("made"));
            Assert.That(instance.Number, Is.EqualTo(42));
            Assert.That(CountingCreator.Made, Is.GreaterThan(made));

            made = CountingCreator.Made;
            Assert.That(CountedSingleton.Instance, Is.SameAs(instance));
            Assert.That(CountingCreator.Made, Is.EqualTo(made), "the creator is not read a second time");
        }

        [UnityTest]
        public IEnumerator A_MonoBehaviour_is_made_on_an_object_of_its_own_and_given_up_with_it()
        {
            var first = PlainMonoBehaviourSingleton.Instance;
            Assert.That(first, Is.Not.Null);
            Assert.That(first.gameObject, Is.Not.Null);
            Assert.That(first.gameObject.name, Is.EqualTo(nameof(PlainMonoBehaviourSingleton)));

            first.Mark = 11;
            Assert.That(PlainMonoBehaviourSingleton.Instance, Is.SameAs(first));

            Object.Destroy(first.gameObject);
            yield return null;

            var second = PlainMonoBehaviourSingleton.Instance;
            Assert.That(second, Is.Not.Null);
            Assert.That(second, Is.Not.SameAs(first), "the field was given up with the object");
        }

        [UnityTest]
        public IEnumerator A_MonoBehaviour_which_is_already_there_is_the_one_which_is_returned()
        {
            var placed = new GameObject(nameof(PlainMonoBehaviourSingleton));
            var component = placed.AddComponent<PlainMonoBehaviourSingleton>();
            yield return null;

            Assert.That(PlainMonoBehaviourSingleton.Instance, Is.SameAs(component));

#if UNITY_2023_1_OR_NEWER
            var found = Object.FindObjectsByType<PlainMonoBehaviourSingleton>(FindObjectsInactive.Exclude);
#else
            var found = Object.FindObjectsOfType<PlainMonoBehaviourSingleton>();
#endif
            Assert.That(found, Has.Length.EqualTo(1), "a second object was not made");
        }

        [Test]
        public void A_ScriptableObject_is_made_with_CreateInstance()
        {
            var first = PlainScriptableObjectSingleton.Instance;
            Assert.That(first, Is.Not.Null);

            first.Mark = 3;
            Assert.That(PlainScriptableObjectSingleton.Instance, Is.SameAs(first));
            Assert.That(PlainScriptableObjectSingleton.Instance.Mark, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator A_MonoBehaviour_which_wrote_an_Awake_of_its_own_runs_both_halves()
        {
            var instance = AwakeMonoBehaviourSingleton.Instance;
            yield return null;

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.Awoke, Is.True, "the Awake which the type wrote ran");
            Assert.That(AwakeMonoBehaviourSingleton.Instance, Is.SameAs(instance), "what was woven in told the field");
        }

        [UnityTest]
        public IEnumerator A_prefab_is_loaded_by_the_path_the_attribute_named()
        {
            var instance = ExplicitResourcesMonoBehaviourSingleton.Instance;
            yield return null;

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.gameObject, Is.Not.Null);
            Assert.That(instance.Mark, Is.EqualTo(5), "the prefab was loaded, not made");
            Assert.That(instance.Note, Is.EqualTo("explicit"));
            Assert.That(ExplicitResourcesMonoBehaviourSingleton.Instance, Is.SameAs(instance));
        }

        [UnityTest]
        public IEnumerator A_prefab_is_loaded_by_the_path_which_was_read_off_the_project()
        {
            var instance = ResourcesMonoBehaviourSingleton.Instance;
            yield return null;

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.Mark, Is.EqualTo(1), "the prefab was loaded, not made");
            Assert.That(instance.Note, Is.EqualTo("auto"));
            Assert.That(ResourcesMonoBehaviourSingleton.Instance, Is.SameAs(instance));
        }

        [Test]
        public void An_asset_is_loaded_by_the_path_which_was_read_off_the_project()
        {
            var instance = ResourcesScriptableObjectSingleton.Instance;

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.Mark, Is.EqualTo(2), "the asset was loaded, not made");
            Assert.That(instance.Note, Is.EqualTo("auto"));
            Assert.That(ResourcesScriptableObjectSingleton.Instance, Is.SameAs(instance));
        }
    }
}