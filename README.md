# Singleton

Build a singleton in **Unity** or in **Godot** without a base type.

```csharp
[Singleton]
public partial class GameManager : AnyBaseType
{
    public int Score;
}
```

`GameManager.Instance` is the instance, and the same one every time it is read.

The type is declared `partial`, and nothing else is asked of it. No base type carries the instance, so a `MonoBehaviour` singleton is still a `MonoBehaviour`, a `Node` singleton is still a `Node`, and a `ScriptableObject` or a `Resource` singleton is still what it was.

`GameManager.TryGetInstance(out var manager)` answers whether there is one already, and makes none where there is not: a message which must not bring a singleton into being asks this rather than `Instance`.

## Attributes

* `[Singleton]`: Make class as singleton with static `Instance` property and `TryGetInstance` method.
* `[Persistent]`: Don't destory singleton when scene switching.
* `[Reusable]`: The generated `Instance` looks for the object which is already in the scene before making one.

## Requirement

* Godot: $\geqslant$ 4.7.*
* Unity: $\geqslant$ 2021.3.*

## Documents

* Godot: [README.md](./Godot/README.md)
* Unity: [README.md](./Unity/README.md)

## License

Singleton is released under the [MIT License](https://github.com/Gatongone/Singleton/blob/main/LICENSE). Copyright (c) 2026, Gatongone