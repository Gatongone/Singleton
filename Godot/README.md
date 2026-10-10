# Singleton

Build a singleton in Godot without a base type.

```csharp
using Godot;
using Singleton.Runtime;

[Singleton]
public partial class GameManager : Node
{
    public int Score;
}
```

`GameManager.Instance` is the instance, and the same one every time it is read.

The type is declared `partial`, and nothing else is asked of it. No base type carries the instance, so a `Node` singleton is still a `Node` and a `Resource` singleton is still a `Resource`.

`GameManager.TryGetInstance(out var manager)` answers whether there is one already, and makes none where there is not: a message which must not bring a singleton into being asks this rather than `Instance`.

## Manual

### Attribute

**With nothing**, Godot makes the instance, or the type's own constructor does:

| The type | What `Instance` is |
|----------|--------------------|
| `Node` | one made and put under the root of the scene tree, or, with `[Reusable]`, the node which is already there |
| anything else | `new T()`, so it needs a parameterless constructor of any accessibility |

A `Node` singleton is put under `SceneTree.Root` rather than under a scene, so a change of scene leaves it where it is.

**With a creator**, `ICreator<T>` makes it:

```csharp
public sealed class MyCreator : ICreator<MyObject>
{
    private readonly string m_Name;

    public MyCreator(string name) => m_Name = name;

    public MyObject Create() => new MyObject { Name = m_Name };
}

[Singleton(typeof(MyCreator), "MyName")]
public partial class MyObject
{
    public string Name;
}
```

The creator is made once, when the instance is first asked for, and is not held afterwards. The generator reads the arguments against the constructors of the creator, so a creator with no accessible constructor which they fit is reported as an error rather than left to the compiler.

**With an asset type**, the instance is loaded:

```csharp
[Singleton(AssetType.Resources, "res://GameManager.tscn")]
public partial class ResourceNode : Node { }

[Singleton(AssetType.Resources, "res://data/Settings.tres")]
public partial class ResourceSettings : Resource { }
```

An asset type applies to a `Node` and to a `Resource`, and to nothing else. A `Node` is loaded from a scene, which is instantiated and put under the root; a `Resource` is loaded itself.

The path may be left out, and the generator then reads it off the project: the first `.tscn` (for a `Node`) or `.tres` (for a `Resource`) which names the script which declares the type is the one which is loaded, and a project which holds none is an error.

```csharp
[Singleton(AssetType.Resources)]
public partial class ResourceNode : Node { }
```

A scene is a text file, and it names the script it was built from the same way a resource does, so nothing but the files of the project is needed to read the path: there is no index to be written and no editor half to write one.

### Reusable

```csharp
[Singleton, Reusable]
public partial class GameManager : Node { }
```

`Instance` is the `GameManager` which is already in the scene tree where there is one, and a new one where there is not. Without the attribute the property never looks: an object which was put in a scene by hand and which the woven `_EnterTree` did not settle is one which `Instance` makes a second of.

Only a `Node` is in a scene tree, so the attribute is an error on anything else. A `Resource` is found by a path rather than by a walk, and what keeps it the same object is the loader which remembers it.

What the search leaves out is a node which is not the one which is held and a node which is on its way out; the walk happens at most once per singleton, because what it finds is held in the field from then on.

### Persistent

```csharp
[Singleton, Persistent]
public partial class GameManager : Node { }
```

The node is moved under the root of the scene tree as it becomes ready, which is what makes `SceneTree.ChangeSceneToFile` and `ChangeSceneToPacked` leave it where it is.

An `[Singleton]` node is already put under the root when the singleton makes one; what the attribute is for is a node which was placed in a scene by hand.

Only a `Node` is in a scene tree, so the attribute is an error on anything else. It stands on its own and needs no `[Singleton]`: a type which carries only this one is still given a `_Ready` which keeps its node.

### Woven

A `Node` singleton is given an `_EnterTree` and an `_ExitTree`:

```csharp
public override void _EnterTree()
{
    if (SingletonRuntime.Alive(s_Instance) && s_Instance != this)
    {
        QueueFree();
        return;
    }

    s_Instance = this;
    // ... the _EnterTree which was written, if there was one
}

public override void _ExitTree()
{
    if (s_Instance == this) s_Instance = null;
    // ... the _ExitTree which was written, if there was one
}
```

The first node of a singleton is the one which is held, and a second one is given up rather than kept beside it.

What is woven is written in front of the body which was written, so a type which wrote one of these of its own has both halves run. A notification which a base type declares is called from the one which was woven rather than left behind, so a singleton which derives from a singleton has an instance of its own.

Where `Instance` is read is worth knowing: the woven `_EnterTree` settles the field as the node enters the tree, so reading a node singleton from the `_Ready` of a node which entered before it is a read which makes one.

### Diagnostics

| ID | Descriptions |
|----|---|
| SING0001 | The type, or a type which holds it, is not declared `partial`. |
| SING0002 | The type is generic, abstract, static, not a class, or declared inside a generic type. |
| SING0003 | The type declares an `Instance`, an `s_Instance` or a `TryGetInstance` of its own. |
| SING0004 | The type has no parameterless constructor for the generated `Instance` to call. |
| SING0005 | The creator which was named does not implement `ICreator<T>` for the type. |
| SING0006 | The creator has no accessible constructor which the named arguments fit. |
| SING0007 | An asset type was asked for by a type which is neither a `Node` nor a `Resource`. |
| SING0008 | No scene or resource in the project names the script of the type. |
| SING0009 | An asset type was asked for without a path, and the script which declares the type is not a file of the project. |
| SING0011 | `PersistentAttribute` was put on something which is not a `Node`. |
| SING0013 | The Godot project could not be found, so a path could not be read off it. |
| SING0014 | `ReusableAttribute` was put on something which is not a `Node`. |

Two of the identifiers the package of Unity uses are missing rather than renumbered, because neither is about anything a Godot project has: `SING0010` is a package of assets which is not referenced, and `SING0012` is the attribute which hides the object of a `MonoBehaviour`, which Godot has no flags of that kind for.

## Principle

A singleton is written in two halves, because the two halves are not known at the same time:

1. The half which is known before the type is compiled is written by a **source generator**: the `s_Instance` field and the `Instance` property which makes the instance the first time it is read. Nothing more is asked of the type than that it is declared `partial`.

2. The half which is not known then is woven in by a **Mono.Cecil weaver** after the type was compiled. The node Godot made is not known to the property until something tells the field about it, and the notification which does that is `_EnterTree` - one a type may already have written, so it cannot be generated beside it and is woven into it instead.

What makes the second half more than an edit of one method is that Godot's own source generator decides which overridable methods a script class answers for, and it decides it from the files it is handed. A method which is added to the assembly afterwards is one it never saw, so it is in no dispatch table - and a notification which is in no dispatch table is one the engine never sends, silently. The weaving therefore writes the dispatch as well, in the shape the generator of the engine being built against wrote it.

## Requirement

- **Godot**: built with .NET 8. What this was written and verified against is 4.7; an earlier 4.x is untested rather than known to work.
- The weaver writes the dispatch of the released engines, which is `InvokeGodotClassMethod`. A Godot whose script dispatch is a table of trampolines instead is refused with an error rather than woven wrongly.

## Install

There are two ways, and they hand over the same three things - the runtime, the generator and the weaver - by different means. Take whichever suits the project; nothing about a singleton depends on which.

### Nuget

Published to [NuGet](https://www.nuget.org/packages/Singleton.Godot/):

```sh
dotnet add package Singleton.Godot
```

Nothing else has to be wired up. The package adds the runtime to the project which installs it, hands the generator to the compiler, and runs the weaver after every build.

### Addons

For a project which keeps what it uses in its own tree, or one which takes it from the asset library of Godot. Unpack the release archive into the root of the project, so that it lies as `addons/singleton/`, and switch it on in **Project > Project Settings > Plugins**.

Switching it on is what wires the build: the plugin writes a `Directory.Build.props` at the root of the project, which names the generator to the compiler and runs the weaver after the assembly was written. Nothing is put into the project file itself, which the editor rewrites.

The runtime needs no wiring at all here, because it is a folder of the project and the compiler reads it already.

Three things are worth knowing:

* The plugin is a GDScript one rather than a C# one. A plugin of C# can only be loaded out of an assembly which was built, and the build of a project which uses `[Singleton]` cannot succeed before the wiring exists - so a C# plugin would be a circle the editor has no way into. What this does is plumbing of files, which needs no assembly at all.

* It writes the file and does not take it away again. The only message which could take it away is the one the editor sends both when a plugin is switched off and when the editor is closing, and a file removed on the way out is one a build outside the editor - a command line, or a runner - no longer finds. To remove the wiring, delete `Directory.Build.props`.

* A project which already has a `Directory.Build.props` of its own is not written over. The plugin says which two lines to add and leaves the file alone.

Both ways are held to the same suite on every push.

## License

Singleton is released under the [MIT License](https://github.com/Gatongone/Singleton/blob/main/LICENSE). Copyright (c) 2026, Gatongone