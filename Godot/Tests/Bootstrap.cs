using System;
using System.Reflection;
using Godot;
using Singleton.Runtime;

namespace E2E
{
    /// <summary>
    /// Exercises the package on the engine: what each kind of singleton reads as, that a second node is given up, that
    /// a node which asked to be kept is moved under the root, and that the field is given up when the node goes.
    /// </summary>
    public partial class Bootstrap : Node
    {
        private static int s_Failed;

        /// <inheritdoc/>
        public override async void _Ready()
        {
            await SearchFindsTheNodeWhichIsAlreadyThere();
            MakingIsTheSameInstanceTwice();
            await ASecondNodeIsGivenUp();
            await AKeptNodeIsMovedUnderTheRoot();
            APlainTypeIsMadeByItsOwnConstructor();
            AResourceIsMadeByItsOwnConstructor();
            ASceneIsLoadedByThePathWhichWasReadOffTheProject();
            await ExitTreeGivesTheFieldUp();

            GD.Print(s_Failed == 0 ? "== ALL PASS ==" : $"== {s_Failed} CHECK(S) FAILED ==");
            GetTree().Quit();
        }

        /// <summary>
        /// A node which is already in the tree is the instance, which is the whole point of asking to be reusable.
        /// </summary>
        private async System.Threading.Tasks.Task SearchFindsTheNodeWhichIsAlreadyThere()
        {
            var placed = new ReusableNode { Name = "Placed", Mark = 5 };
            AddChild(placed);

            // What the woven _EnterTree did is undone, which leaves the node in the tree with nothing holding it: the
            // only state in which the search is what answers rather than the field.
            Forget(typeof(ReusableNode));

            Check(ReusableNode.Instance == placed, "a reusable singleton found the node which was already in the tree");
            Check(ReusableNode.Instance.Mark == 5, "the node which was found is the one which was placed");

            placed.GetParent()?.RemoveChild(placed);
            placed.QueueFree();
            Forget(typeof(ReusableNode));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        /// <summary>A node which asked for no search is made once and answered with from then on.</summary>
        private void MakingIsTheSameInstanceTwice()
        {
            var first = MadeNode.Instance;
            var second = MadeNode.Instance;

            Check(first != null, "a node singleton was made");
            Check(first == second, "a node singleton is the same instance twice");
            Check(first!.GetParent() == GetTree().Root, "a node singleton is under the root of the scene tree");
        }

        /// <summary>A second node of a singleton is given up rather than kept beside the first.</summary>
        private async System.Threading.Tasks.Task ASecondNodeIsGivenUp()
        {
            var held = MadeNode.Instance;
            var extra = new MadeNode { Name = "Extra" };

            GetTree().Root.AddChild(extra);

            Check(extra.IsQueuedForDeletion(), "a second node of a singleton was given up");
            Check(MadeNode.Instance == held, "the second node did not take the field");

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        /// <summary>
        /// A node which asked to be kept is moved under the root, which is what makes a change of scene leave it.
        /// </summary>
        private async System.Threading.Tasks.Task AKeptNodeIsMovedUnderTheRoot()
        {
            var kept = new KeptNode { Name = "Kept" };
            AddChild(kept);

            Check(kept.GetParent() == this, "the node was put where it was asked to be put");

            // The move is deferred, so it is asked for again once the frame which asked for it has ended.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Check(kept.GetParent() == GetTree().Root, "a node which asked to be kept was moved under the root");
            Check(KeptNode.Instance == kept, "the node which was kept is the instance");

            kept.GetParent()?.RemoveChild(kept);
            kept.QueueFree();
            Forget(typeof(KeptNode));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        /// <summary>A type which is in no tree is made by its own constructor.</summary>
        private void APlainTypeIsMadeByItsOwnConstructor()
        {
            var first = PlainThing.Instance;
            first.Mark = 11;

            Check(PlainThing.Instance == first, "a plain singleton is the same instance twice");
            Check(PlainThing.Instance.Mark == 11, "a plain singleton is made by its own constructor");
        }

        /// <summary>A resource which named no asset is made by its own constructor.</summary>
        private void AResourceIsMadeByItsOwnConstructor()
        {
            var first = PlainResource.Instance;
            first.Mark = 3;

            Check(PlainResource.Instance == first, "a resource singleton is the same instance twice");
            Check(PlainResource.Instance.Mark == 3, "a resource singleton is made by its own constructor");
            Check(PlainResource.TryGetInstance(out var held) && held == first, "TryGetInstance answers with the instance");
        }

        /// <summary>
        /// A singleton which named no path is loaded from the scene the generator read off the project, and the node
        /// which comes out of it is the instance.
        /// </summary>
        private void ASceneIsLoadedByThePathWhichWasReadOffTheProject()
        {
            var instance = LoadedNode.Instance;

            Check(instance != null, "a singleton which named no path was loaded from a scene");
            Check(instance!.GetParent() == GetTree().Root, "the node which was loaded is under the root of the scene tree");
            Check(instance.GetType() == typeof(LoadedNode), "the scene which was loaded holds the type which asked for it");
        }

        /// <summary>The field is given up when the node which holds it leaves the tree.</summary>
        private async System.Threading.Tasks.Task ExitTreeGivesTheFieldUp()
        {
            var held = MadeNode.Instance;
            held.GetParent()?.RemoveChild(held);
            held.QueueFree();

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Check(!MadeNode.TryGetInstance(out _), "the field was given up when the node left the tree");
        }

        /// <summary>
        /// Give up the field of a singleton, which is what the tests do to reach the state the search is written for.
        /// </summary>
        /// <param name="type">The type of the singleton.</param>
        private static void Forget(Type type)
        {
            var field = type.GetField("s_Instance", BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException($"'{type.Name}' has no field which holds its instance");

            field.SetValue(null, null);
        }

        /// <summary>Record what a check answered.</summary>
        /// <param name="passed">Whether it held.</param>
        /// <param name="what">What it asked.</param>
        private static void Check(bool passed, string what)
        {
            if (passed)
            {
                GD.Print($"PASS  {what}");
                return;
            }

            s_Failed++;
            GD.Print($"FAIL  {what}");
        }
    }
}
