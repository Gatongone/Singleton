# Singleton

Build a singleton in Unity without a base type.

```csharp
using Singleton.Runtime;
using UnityEngine;

[Singleton]
public partial class GameManager : MonoBehaviour
{
    public int Score;
}
```

`GameManager.Instance` is the instance, and the same one every time it is read.

The type is declared `partial`, and nothing else is asked of it. No base type carries the instance, so a `MonoBehaviour` singleton is still a `MonoBehaviour` and a `ScriptableObject` singleton is still a `ScriptableObject`.

`GameManager.TryGetInstance(out var manager)` answers whether there is one already, and makes none where there is not: a message which must not bring a singleton into being asks this rather than `Instance`.

## Manual

### Attribute

**With nothing**, Unity makes the instance, or the type's own constructor does:

| The type | What `Instance` is |
|----------|--------------------|
| `MonoBehaviour` | the loaded instance, or one made on a new `GameObject` where there is none |
| `ScriptableObject` | the loaded instance, or one made with `CreateInstance` where there is none |
| anything else | `new T()`, so it needs a parameterless constructor of any accessibility |

```csharp
[Singleton]
public partial class Foo { }
```

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
[Singleton(AssetType.Resources, "path/to/asset")]
public partial class ResourceMonoBehaviour : MonoBehaviour { }

[Singleton(AssetType.Resources, "path/to/asset")]
public partial class ResourceScriptableObject : ScriptableObject { }

[Singleton(AssetType.Addressable, "address/of/asset")]
public partial class AddressableMonoBehaviour : MonoBehaviour { }

[Singleton(AssetType.Addressable, "address/of/asset")]
public partial class AddressableScriptableObject : ScriptableObject { }
```

An asset type applies to a `MonoBehaviour` and to a `ScriptableObject`, and to nothing else. A `MonoBehaviour` looks for an instance which is already loaded first, and only then loads the prefab and instantiates it; a `ScriptableObject` loads the asset itself.

The path may be left out for `AssetType.Resources`, and the generator then reads it off the project: the first `.prefab` (for a `MonoBehaviour`) or `.asset` (for a `ScriptableObject`) which names the script which declares the type is the one which is loaded, and a project which holds none is an error. An Addressables **address** cannot be read off a project, so `AssetType.Addressable` asks for it.

### Woven

A `MonoBehaviour` is given an `Awake` and an `OnDestroy`:

```csharp
private void Awake()
{
    if (s_Instance != null && s_Instance != this)
    {
        Destroy(gameObject);
        return;
    }

    s_Instance = this;
    // ... the Awake which was written, if there was one
}

protected virtual void OnDestroy()
{
    if (s_Instance == this) s_Instance = null;
    // ... the OnDestroy which was written, if there was one
}
```

A `ScriptableObject` is given an `OnEnable` and an `OnDisable` instead, and a second one which is enabled is left out of the field rather than destroyed, because Unity loads a `ScriptableObject` only once.

Unity sends a message to the nearest declaration of it alone, and a declaration is what the weaver writes where the type wrote none, so the message a base class wrote is called from the one which was woven rather than left behind. A `private` message of a base class is given `protected` access to make that call, which is the only thing the weaving changes about the declaration a type wrote; a `private` message of an assembly which is not the one being compiled cannot be, and is left behind.

A singleton which derives from a singleton has an instance of its own, and `Instance` is declared `new` where the type it derives from declares one, because a member which is declared beside one of the same name of a type it derives from hides it. `TryGetInstance` is not: it answers with the type it is declared in, which no base type declares alike, so it is an overload rather than a member which hides one. A `MonoBehaviour` is looked for in the scene and a type which derives from another is one of it, so the two answer with the same object where there is one.

### Extra

```csharp
[Persistent, Invisible]
public class Foo : MonoBehaviour
{
    // ...
}
```

* `[Persistent]`: adds `DontDestroyOnLoad(gameObject)` to the `Awake`

* `[Invisible]`: adds `gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.DontSave`.

Both also stand on their own: a type which carries one of them and no `[Singleton]` is still given an `Awake` which does what the attribute asks. Where a type carries a singleton as well, the singleton is settled first and the attributes after it, so a second instance, which is given up, is neither kept nor hidden.

Where `CSHARP_11_OR_NEWER` is defined, the generated half also declares the type as an `ISingleton<T>`, so that the same instance is read from a type argument alone:

```csharp
T Read<T>() where T : class, ISingleton<T> => Singleton<T>.Instance;
bool TryRead<T>(out T instance) where T : class, ISingleton<T> => Singleton<T>.TryGetInstance(out instance);
```

### Diagnostics

| ID | Descriptions |
|----|---|
| SING0001 | The type, or a type which holds it, is not declared `partial`. |
| SING0002 | The type is generic, abstract, static, not a class, or declared inside a generic type. |
| SING0003 | The type declares an `Instance`, an `s_Instance` or a `TryGetInstance` of its own. |
| SING0004 | The type has no parameterless constructor for the generated `Instance` to call. |
| SING0005 | The creator which was named does not implement `ICreator<T>` for the type. |
| SING0006 | The creator has no accessible constructor which the named arguments fit. |
| SING0007 | An asset type was asked for by a type which is neither a `MonoBehaviour` nor a `ScriptableObject`. |
| SING0008 | No prefab or asset under a `Resources` folder holds the type. |
| SING0009 | `AssetType.Addressable` was asked for without an address. |
| SING0010 | The compilation does not see a type which the generated code would name, so the Addressables package is not referenced. |
| SING0011 | `PersistentAttribute` was put on something which is not a `MonoBehaviour`. |
| SING0012 | `InvisibleAttribute` was put on something which is not a `MonoBehaviour`. |
| SING0013 | The Unity project could not be found, so a `Resources` path could not be read off it. |

## Principle

A singleton is written in two halves, because the two halves are not known at the same time:

1. The half which is known before the type is compiled is written by a **source generator**: the `s_Instance` field and the `Instance` property which makes the instance the first time it is read. Nothing more is asked of the type than that it is declared `partial`.

2. The half which is not known then is woven in by a **Mono.Cecil post processor** after the type was compiled. The instance which Unity made is not known to the property until something tells the field about it, and the message which does that is `Awake` for a `MonoBehaviour` and `OnEnable` for a `ScriptableObject` - messages a type may already have written, so they cannot be generated beside it and are woven into it instead. The post processor is a part of the compilation rather than something beside it, so a singleton is one in a player as well as in the editor.

What the generator needs to know about the project, which is the `Resources` path of a type for which no path was given, is written into `obj/Singleton.txt` by the editor half through the `AssetDatabase`. A path is therefore read once per change to the project rather than walked on every compilation.

## Requirement

- **Unity**: $\geqslant$ 2021.3.
- **com.unity.nuget.mono-cecil**: $\geqslant$ 1.11.6

## Install

**From [NPMJS](https://www.npmjs.com/package/com.gatongone.singleton/):**

```json
{
  "scopedRegistries":
  [
    {
      "name": "npmjs",
      "url": "https://registry.npmjs.org",
      "scopes": ["com.gatongone"]
    }
  ],
  "dependencies":
  {
    "com.gatongone.singleton": "0.0.1"
  }
}
```

**From [OpenUPM](https://openupm.com/packages/com.gatongone.singleton/):**

```json
{
  "scopedRegistries":
  [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["com.gatongone"]
    }
  ],
  "dependencies":
  {
    "com.gatongone.singleton": "0.0.1"
  }
}
```

```sh
openupm add com.gatongone.singleton
```

**From [GIT](https://github.com/Gatongone/Singleton):**

```json
{
  "dependencies":
  {
    "com.gatongone.singleton": "https://github.com/Gatongone/Singleton.git#v0.0.1"
  }
}
```

## License

Singleton is released under the [MIT License](LICENSE). Copyright (c) 2026, Gatongone