using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Singleton.Weaver
{
    /// <summary>
    /// Writes the Godot notifications which keep the field of a singleton true into the assembly of a project, after it
    /// was compiled.
    /// </summary>
    /// <remarks>
    /// The generated half of a singleton is the field and the property which makes an instance where there is none, and
    /// both of those can be written before the type is compiled. What cannot be written is the other direction: the
    /// node Godot made is not known to the property until something tells the field about it, and the notification
    /// which does that is <c>_EnterTree</c> - one a type may already have written, so the code which tells the field
    /// cannot be generated beside it and is woven into it instead.<para/>
    /// What makes this more than an edit of one method is that Godot's own source generator decides which overridable
    /// methods a script class answers for, and it decides it from the files it is handed. A method which is added to
    /// the assembly afterwards is one that generator never saw, so it is in no dispatch table, and a notification which
    /// is in no dispatch table is one the engine never sends. The weaving therefore writes the dispatch as well, in the
    /// shape the generator of the engine being built against wrote it.
    /// </remarks>
    internal static class SingletonWeaver
    {
        /// <summary>The attribute which asks for a singleton.</summary>
        private const string SINGLETON_ATTRIBUTE = "Singleton.Runtime.SingletonAttribute";

        /// <summary>The attribute which asks for the node to be kept across a change of scene.</summary>
        private const string PERSISTENT_ATTRIBUTE = "Singleton.Runtime.PersistentAttribute";

        /// <summary>The type which holds what the woven code calls.</summary>
        private const string RUNTIME = "Singleton.Runtime.SingletonRuntime";

        /// <summary>The field which holds the instance.</summary>
        private const string INSTANCE_FIELD = "s_Instance";

        /// <summary>The type a singleton is given notifications of the tree for.</summary>
        private const string NODE = "Godot.Node";

        /// <summary>The class the generator caches the names of methods in.</summary>
        private const string METHOD_NAME = "MethodName";

        /// <summary>The class the generator of a newer Godot puts the dispatch of a script class in.</summary>
        private const string GODOT_INTERNAL = "GodotInternal";

        /// <summary>What a newer Godot dispatches a script class through.</summary>
        private const string TRAMPOLINES = "GetGodotMethodTrampolines";

        /// <summary>The notification which is sent as a node enters the tree.</summary>
        private const string ENTER_TREE = "_EnterTree";

        /// <summary>The notification which is sent as a node has entered the tree.</summary>
        private const string READY = "_Ready";

        /// <summary>The notification which is sent as a node leaves the tree.</summary>
        private const string EXIT_TREE = "_ExitTree";

        /// <summary>The dispatch which calls the method a name stands for.</summary>
        private const string INVOKE = "InvokeGodotClassMethod";

        /// <summary>The dispatch which answers whether a name stands for a method of the type.</summary>
        private const string HAS = "HasGodotClassMethod";

        /// <summary>
        /// Weave every singleton of an assembly.
        /// </summary>
        /// <param name="path">The path of the assembly.</param>
        /// <returns>How many types were woven into.</returns>
        public static int Weave(string path)
        {
            var directory = Path.GetDirectoryName(path)!;

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(directory);

            var module = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });

            var woven = 0;
            using (module)
            {
                // An assembly which does not name Godot holds no script class of it, and one which does not name the
                // runtime of this package was not built with the package at all. Either way there is nothing here.
                if (Godot.Of(module) is not { } godot) return 0;

                // What is woven is the types which asked for it, and a base type is woven before the types which derive
                // from it: what a notification a type is given is written from is the notification of the type it
                // derives from, and a type which is woven first is one whose notification names a method the type it
                // derives from has not been given yet.
                var marked = Types(module)
                    .Where(type => AsksForASingleton(type) || AsksToBeKept(type))
                    .OrderBy(type => Depth(type))
                    .ToList();

                foreach (var type in marked)
                {
                    // Only a node is in a scene tree. A Resource singleton is made by its own constructor or loaded by
                    // a path, and neither direction needs anything woven: what the loader remembers is the same object,
                    // so the field never has to be told about it.
                    if (!IsDerivedFrom(type, NODE)) continue;

                    if (Weave(type, module, godot)) woven++;
                }

                if (woven == 0) return 0;

                var temporary = path + ".woven";
                module.Write(temporary);
            }

            File.Move(path + ".woven", path, overwrite: true);
            return woven;
        }

        /// <summary>
        /// Weave into a type the notifications which give the field of a singleton the node Godot made, and which do to
        /// the node what the attribute of this package asks of it.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="module">The module the type lies in.</param>
        /// <param name="godot">What the woven code is written against.</param>
        /// <returns>Whether the type was woven into.</returns>
        private static bool Weave(TypeDefinition type, ModuleDefinition module, Godot godot)
        {
            var singleton = AsksForASingleton(type);
            var keep = AsksToBeKept(type);

            // A type whose half was not generated is one the generator refused, and the compilation carries the reason
            // already. There is no field for a notification to keep true, so there is nothing to weave.
            var instance = singleton
                ? type.Fields.FirstOrDefault(field => field.Name == INSTANCE_FIELD && field.IsStatic)
                : null;

            if (singleton && instance == null) return false;
            if (!singleton && !keep) return false;

            RefuseUnsupportedDispatch(type);

            var woven = new List<string>();

            if (singleton)
            {
                var enter = EnsureNotification(module, type, ENTER_TREE, godot.EnterTree);
                Inject(enter, _ => Register(godot, instance!));
                woven.Add(ENTER_TREE);

                var exit = EnsureNotification(module, type, EXIT_TREE, godot.ExitTree);
                Inject(exit, first => GiveUp(instance!, first));
                woven.Add(EXIT_TREE);
            }

            if (keep)
            {
                var ready = EnsureNotification(module, type, READY, godot.Ready);

                // A node which is not the singleton is one which is on its way out, so it is not moved under the root:
                // keeping it would be keeping the second of two, which is the one the singleton is not.
                Inject(ready, first => instance == null
                    ? Keep(godot)
                    : Keep(godot, instance, first));
                woven.Add(READY);
            }

            Dispatch(module, type, godot, woven);
            return true;
        }

        /// <summary>
        /// Refuse an assembly which was built against a Godot whose dispatch is not the one this weaves for.
        /// </summary>
        /// <remarks>
        /// A method which is added to an assembly is one the generator of the engine never saw, so the weaving writes
        /// the entry which makes the engine ask for it. That entry is written in the shape the generator emits, and the
        /// shape is not the same in every Godot: the released engines answer through <c>InvokeGodotClassMethod</c>,
        /// which is what is written here.
        /// <para/>
        /// A newer engine answers through a table of trampolines instead. Writing that table from here would be writing
        /// it unseen - the shape is of an engine which is not released - and an entry which is written wrongly is a
        /// singleton which silently never reaches its field, which is worse than a build which stops. So it is refused,
        /// and said, rather than guessed at.
        /// </remarks>
        /// <param name="type">The type.</param>
        private static void RefuseUnsupportedDispatch(TypeDefinition type)
        {
            var internal_ = type.NestedTypes.FirstOrDefault(nested => nested.Name == GODOT_INTERNAL);
            if (internal_ == null) return;

            if (!internal_.Methods.Any(method => method.Name == TRAMPOLINES)) return;

            throw new NotSupportedException(
                $"'{type.FullName}' was compiled against a Godot whose script dispatch is a table of trampolines " +
                $"({GODOT_INTERNAL}.{TRAMPOLINES}), which this weaver does not write. The singleton would be given a " +
                "notification the engine never sends, so nothing was woven. Use a released Godot, or a version of " +
                "this package which knows that dispatch.");
        }

        /// <summary>
        /// Read the notification to be woven into a type, or make one where the type has none.<para/>
        /// A notification which is made is one the type never wrote, and the engine sends one to the nearest
        /// declaration of it alone, so the one the type it derives from wrote is called from here: it is a body which
        /// ran before anything was woven and which would not run after.
        /// </summary>
        /// <param name="module">The module the type lies in.</param>
        /// <param name="type">The type.</param>
        /// <param name="name">The name of the notification.</param>
        /// <param name="inherited">What the type Godot declares the notification as.</param>
        /// <returns>The notification.</returns>
        private static MethodDefinition EnsureNotification(ModuleDefinition module, TypeDefinition type, string name,
                                                            MethodReference inherited)
        {
            var declared = Inherited(type, name);

            foreach (var method in type.Methods)
            {
                if (method.Name != name) continue;
                if (method.IsStatic || method.Parameters.Count != 0) continue;
                if (method.ReturnType.FullName != "System.Void") continue;

                // A notification which was written is woven into as it stands: what it is, what it returns and who may
                // call it are the author's, and what is woven needs no local of its own so that nothing of the body has
                // to be moved out of the way.
                if (method.IsAbstract || method.IsRuntime) return method;

                if (!method.HasBody) method.Body = Empty(method);
                else if (method.Body.Instructions.Count == 0) method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

                return method;
            }

            // The notification is an override rather than a method beside one: the engine reaches the type through the
            // slot of the type it derives from, so what is made has to take that slot rather than declare a new one.
            var made = new MethodDefinition(name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                module.ImportReference(inherited.ReturnType));

            made.Body = Empty(made);
            type.Methods.Add(made);

            if (declared != null && Reachable(declared, module))
            {
                made.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Call, module.ImportReference(declared)));
                made.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Ldarg_0));
            }

            return made;
        }

        /// <summary>
        /// Read the notification of the nearest type a type derives from which declares one.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="name">The name of the notification.</param>
        /// <returns>The notification, or <c>null</c> where no type declares one.</returns>
        private static MethodDefinition? Inherited(TypeDefinition type, string name)
        {
            for (var current = Base(type); current != null; current = Base(current))
            {
                foreach (var method in current.Methods)
                {
                    if (method.Name != name) continue;
                    if (method.IsStatic || method.Parameters.Count != 0) continue;
                    if (method.ReturnType.FullName != "System.Void") continue;

                    return method;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether a notification of another type may be called from the one being woven, which a private one may not.
        /// </summary>
        /// <param name="method">The notification.</param>
        /// <param name="module">The module being woven.</param>
        /// <returns>Whether it may be called.</returns>
        private static bool Reachable(MethodDefinition method, ModuleDefinition module)
        {
            if (!method.IsPrivate) return true;

            // A private notification of a type of this assembly is widened, because the engine ran it for a type which
            // derived from it until one was woven here. One of another assembly cannot be widened, and is left.
            if (method.Module.Assembly.Name.FullName != module.Assembly.Name.FullName) return false;

            method.Attributes = (method.Attributes & ~MethodAttributes.MemberAccessMask) | MethodAttributes.Family;
            return true;
        }

        /// <summary>
        /// Make sure the engine's dispatch answers for the notifications which were woven, creating the dispatch where
        /// Godot's generator wrote none.
        /// </summary>
        /// <remarks>
        /// This is the part which fails silently. An override which is added to a type is one no dispatch table names,
        /// and a notification which no dispatch table names is one the engine never sends: the field is never told
        /// about the node the engine made, the property makes a second one, and nothing anywhere says so.
        /// </remarks>
        /// <param name="module">The module the type lies in.</param>
        /// <param name="type">The type.</param>
        /// <param name="godot">What the woven code is written against.</param>
        /// <param name="woven">The names of the notifications which were woven.</param>
        private static void Dispatch(ModuleDefinition module, TypeDefinition type, Godot godot, List<string> woven)
        {
            if (woven.Count == 0) return;

            var methodName = MethodNameOf(type)
                ?? throw new NotSupportedException($"'{type.FullName}' has no '{METHOD_NAME}' class, so the names of " +
                                                   "its notifications have nowhere to be cached and the dispatch cannot " +
                                                   "be written. The assembly was not compiled by Godot's generator.");

            var names = new List<(string Name, FieldDefinition Field)>();
            foreach (var name in woven)
            {
                names.Add((name, EnsureName(module, methodName, godot, name)));
            }

            EnsureDispatch(module, type, godot, names, methodName, INVOKE, 3);
            EnsureDispatch(module, type, godot, names, methodName, HAS, 1);
        }

        /// <summary>
        /// Read the class of cached names which a type answers for, which is the nearest one it or the type it derives
        /// from declares.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>The class, or <c>null</c> where there is none.</returns>
        private static TypeDefinition? MethodNameOf(TypeDefinition type)
        {
            for (var current = type; current != null; current = Base(current))
            {
                var nested = current.NestedTypes.FirstOrDefault(candidate => candidate.Name == METHOD_NAME);
                if (nested != null) return nested;
            }

            return null;
        }

        /// <summary>
        /// Make sure the class of cached names holds a name for a notification, and that it is given its value.
        /// </summary>
        /// <param name="module">The module the type lies in.</param>
        /// <param name="methodName">The class of cached names.</param>
        /// <param name="godot">What the woven code is written against.</param>
        /// <param name="name">The name of the notification.</param>
        /// <returns>The field which holds the name.</returns>
        private static FieldDefinition EnsureName(ModuleDefinition module, TypeDefinition methodName, Godot godot, string name)
        {
            foreach (var field in methodName.Fields)
            {
                if (field.Name == name) return field;
            }

            var made = new FieldDefinition(name,
                FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly,
                module.ImportReference(godot.StringName));

            methodName.Fields.Add(made);

            var cctor = methodName.Methods.FirstOrDefault(method => method.Name == ".cctor");
            if (cctor == null)
            {
                cctor = new MethodDefinition(".cctor",
                    MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig |
                    MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                    methodName.Module.TypeSystem.Void);

                cctor.Body = new MethodBody(cctor);
                cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                methodName.Methods.Add(cctor);
            }

            var processor = cctor.Body.GetILProcessor();
            var ret = cctor.Body.Instructions.Last(instruction => instruction.OpCode == OpCodes.Ret);

            processor.InsertBefore(ret, Instruction.Create(OpCodes.Ldstr, name));
            processor.InsertBefore(ret, Instruction.Create(OpCodes.Call, module.ImportReference(godot.StringNameImplicit)));
            processor.InsertBefore(ret, Instruction.Create(OpCodes.Stsfld, made));
            cctor.Body.MaxStackSize = Math.Max(cctor.Body.MaxStackSize, 2);

            return made;
        }

        /// <summary>
        /// Make sure the dispatch which the engine asks answers for the names, creating it where none was written.
        /// </summary>
        /// <param name="module">The module the type lies in.</param>
        /// <param name="type">The type.</param>
        /// <param name="godot">What the woven code is written against.</param>
        /// <param name="names">The names of the notifications which were woven.</param>
        /// <param name="methodName">The class of cached names.</param>
        /// <param name="dispatcher">The name of the dispatch.</param>
        /// <param name="parameters">How many parameters the dispatch takes.</param>
        private static void EnsureDispatch(ModuleDefinition module, TypeDefinition type, Godot godot,
                                           List<(string Name, FieldDefinition Field)> names, TypeDefinition methodName,
                                           string dispatcher, int parameters)
        {
            var inherited = Chain(type)
                .SelectMany(current => current.Methods)
                .FirstOrDefault(method => method.Name == dispatcher && method.Parameters.Count == parameters && !method.IsStatic);

            if (inherited == null)
            {
                throw new NotSupportedException($"'{type.FullName}' has nothing to override for '{dispatcher}', so the " +
                                                "engine has no way to be told that the type answers for the woven " +
                                                "notifications.");
            }

            var dispatch = type.Methods.FirstOrDefault(method => method.Name == dispatcher && !method.IsStatic);

            if (dispatch == null)
            {
                dispatch = new MethodDefinition(dispatcher,
                    MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                    module.ImportReference(inherited.ReturnType));

                foreach (var parameter in inherited.Parameters)
                {
                    dispatch.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes,
                        module.ImportReference(parameter.ParameterType)));
                }

                dispatch.Body = new MethodBody(dispatch);
                var processor = dispatch.Body.GetILProcessor();

                // A dispatch Godot never generated stops at the type it derives from, which is what the engine would
                // have asked had nothing been woven here.
                processor.Append(Instruction.Create(OpCodes.Ldarg_0));
                for (var index = 1; index <= inherited.Parameters.Count; index++) processor.Append(Argument(index));
                processor.Append(Instruction.Create(OpCodes.Call, module.ImportReference(inherited)));
                processor.Append(Instruction.Create(OpCodes.Ret));

                type.Methods.Add(dispatch);
            }

            var target = dispatch.Body.GetILProcessor();
            var first = dispatch.Body.Instructions[0];

            foreach (var (name, field) in names)
            {
                var fieldReference = module.ImportReference(field);
                var skip = Instruction.Create(OpCodes.Nop);

                // if (method == MethodName.<name>) ...
                target.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_1));
                target.InsertBefore(first, Instruction.Create(OpCodes.Ldsfld, fieldReference));
                target.InsertBefore(first, Instruction.Create(OpCodes.Call, module.ImportReference(godot.OpEquality)));
                target.InsertBefore(first, Instruction.Create(OpCodes.Brfalse, skip));

                if (dispatcher == INVOKE)
                {
                    // ... and it was asked for with no arguments, which is how a notification is sent.
                    target.InsertBefore(first, Instruction.Create(OpCodes.Ldarga_S, dispatch.Parameters[1]));
                    target.InsertBefore(first, Instruction.Create(OpCodes.Call, module.ImportReference(godot.GetCount)));
                    target.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4_0));
                    target.InsertBefore(first, Instruction.Create(OpCodes.Ceq));
                    target.InsertBefore(first, Instruction.Create(OpCodes.Brfalse, skip));

                    // this.<name>(); ret = default; return true;
                    var notification = type.Methods.First(method => method.Name == name && method.Parameters.Count == 0);
                    target.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                    target.InsertBefore(first, Instruction.Create(OpCodes.Callvirt, module.ImportReference(notification)));
                    target.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_3));
                    target.InsertBefore(first, Instruction.Create(OpCodes.Initobj, module.ImportReference(godot.Variant)));
                    target.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4_1));
                    target.InsertBefore(first, Instruction.Create(OpCodes.Ret));
                }
                else
                {
                    target.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4_1));
                    target.InsertBefore(first, Instruction.Create(OpCodes.Ret));
                }

                target.InsertBefore(first, skip);
            }

            dispatch.Body.MaxStackSize = Math.Max(dispatch.Body.MaxStackSize, 4);
        }

        /// <summary>
        /// Read what a node which entered the tree does: the first one is the singleton, and the others are given up.
        /// </summary>
        /// <param name="godot">What the woven code is written against.</param>
        /// <param name="instance">The field which holds the instance.</param>
        /// <returns>The messages.</returns>
        private static List<Instruction> Register(Godot godot, FieldDefinition instance)
        {
            // A node which is not the one which is held is a second one, and it is given up here rather than kept: the
            // property answers with the first, and a node which was put in a scene by hand is a node somebody meant.
            var register = Instruction.Create(OpCodes.Ldarg_0);

            return new List<Instruction>
            {
                Instruction.Create(OpCodes.Ldsfld, instance),
                Instruction.Create(OpCodes.Call, godot.Alive),
                Instruction.Create(OpCodes.Brfalse, register),

                Instruction.Create(OpCodes.Ldsfld, instance),
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Beq, register),

                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Callvirt, godot.QueueFree),
                Instruction.Create(OpCodes.Ret),

                // The message above falls through into the body which was written.
                register,
                Instruction.Create(OpCodes.Stsfld, instance)
            };
        }

        /// <summary>
        /// Read what a node which left the tree does: the field gives up the node it holds.
        /// </summary>
        /// <param name="instance">The field which holds the instance.</param>
        /// <param name="after">What the notification does of its own, which is run when the field does not hold it.</param>
        /// <returns>The messages.</returns>
        private static List<Instruction> GiveUp(FieldDefinition instance, Instruction after) => new()
        {
            Instruction.Create(OpCodes.Ldsfld, instance),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Bne_Un, after),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Stsfld, instance)
        };

        /// <summary>
        /// Read what a node which is the singleton does as it becomes ready: it is moved under the root of the tree, so
        /// that a change of scene leaves it where it is.
        /// </summary>
        /// <param name="godot">What the woven code is written against.</param>
        /// <returns>The messages.</returns>
        private static List<Instruction> Keep(Godot godot) => new()
        {
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Call, godot.Keep)
        };

        /// <summary>
        /// Read what a node does as it becomes ready where it has to be the singleton for it to be kept.
        /// </summary>
        /// <param name="godot">What the woven code is written against.</param>
        /// <param name="instance">The field which holds the instance.</param>
        /// <param name="after">What the notification does of its own.</param>
        /// <returns>The messages.</returns>
        private static List<Instruction> Keep(Godot godot, FieldDefinition instance, Instruction after) => new()
        {
            Instruction.Create(OpCodes.Ldsfld, instance),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Bne_Un, after),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Call, godot.Keep)
        };

        /// <summary>
        /// Write a prologue in front of what a notification does of its own.<para/>
        /// The body which was written comes after the prologue rather than inside it, so a notification which is woven
        /// into runs as it was written when the field already holds the node.
        /// </summary>
        /// <param name="notification">The notification which is woven into.</param>
        /// <param name="prologue">What is written in front of it, read against the body which is run after it.</param>
        private static void Inject(MethodDefinition notification, Func<Instruction, List<Instruction>> prologue)
        {
            var body = notification.Body;
            var processor = body.GetILProcessor();
            var first = body.Instructions[0];

            foreach (var instruction in prologue(first)) processor.InsertBefore(first, instruction);

            // What was woven asks two values of the stack at most, and a notification whose stack was already deeper
            // keeps the depth it had.
            body.MaxStackSize = Math.Max(body.MaxStackSize, 8);
        }

        /// <summary>
        /// Read a body which does nothing, which is what a notification a type never wrote begins as.
        /// </summary>
        /// <param name="method">The notification.</param>
        /// <returns>The body.</returns>
        private static MethodBody Empty(MethodDefinition method)
        {
            var body = new MethodBody(method)
            {
                InitLocals = true,
                MaxStackSize = 8
            };

            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            return body;
        }

        /// <summary>
        /// Read the argument of a method at a position, which is what the dispatch hands to the type it derives from.
        /// </summary>
        /// <param name="index">The position of the argument.</param>
        /// <returns>The message which loads it.</returns>
        private static Instruction Argument(int index) => index switch
        {
            1 => Instruction.Create(OpCodes.Ldarg_1),
            2 => Instruction.Create(OpCodes.Ldarg_2),
            3 => Instruction.Create(OpCodes.Ldarg_3),
            _ => Instruction.Create(OpCodes.Ldarg, index)
        };

        /// <summary>
        /// Whether a type asked for a singleton.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>Whether it did.</returns>
        private static bool AsksForASingleton(TypeDefinition type) => Has(type, SINGLETON_ATTRIBUTE);

        /// <summary>
        /// Whether a type asked for its node to be kept across a change of scene.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>Whether it did.</returns>
        private static bool AsksToBeKept(TypeDefinition type) => Has(type, PERSISTENT_ATTRIBUTE);

        /// <summary>
        /// Whether a type carries an attribute.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="name">The name of the attribute, written in full.</param>
        /// <returns>Whether it carries it.</returns>
        private static bool Has(TypeDefinition type, string name) =>
            type.HasCustomAttributes && type.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == name);

        /// <summary>
        /// Whether a type is the named type, or derives from it.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="name">The name of the type which is looked for, written in full.</param>
        /// <returns>Whether it is.</returns>
        private static bool IsDerivedFrom(TypeDefinition type, string name)
        {
            for (var current = type; current != null; current = Base(current))
            {
                if (current.FullName == name) return true;
            }

            return false;
        }

        /// <summary>
        /// Read the type which a type is declared as.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>The type it is declared as, or <c>null</c> where it is declared as none or cannot be read.</returns>
        private static TypeDefinition? Base(TypeDefinition type)
        {
            if (type.BaseType == null) return null;
            if (type.BaseType is TypeDefinition definition) return definition;

            var beside = type.Module.GetType(type.BaseType.FullName);
            if (beside != null) return beside;

            try
            {
                return type.BaseType.Resolve();
            }
            catch (Exception)
            {
                // A type which cannot be read is a chain which ends here, and a chain which ends before the type which
                // was looked for is one which does not hold it.
                return null;
            }
        }

        /// <summary>Every type a type derives from, nearest first.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The types.</returns>
        private static IEnumerable<TypeDefinition> Chain(TypeDefinition type)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var current = type; current != null && seen.Add(current.FullName); current = Base(current))
            {
                yield return current;
            }
        }

        /// <summary>
        /// Read how many types a type derives from, which is what the order of the weaving is read in.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>The number of them.</returns>
        private static int Depth(TypeDefinition type)
        {
            var depth = 0;
            for (var current = Base(type); current != null && depth < 128; current = Base(current)) depth++;
            return depth;
        }

        /// <summary>
        /// Read every type of a module, the ones declared inside another type among them.
        /// </summary>
        /// <param name="module">The module.</param>
        /// <returns>The types.</returns>
        private static IEnumerable<TypeDefinition> Types(ModuleDefinition module)
        {
            var pending = new Stack<TypeDefinition>(module.Types);
            while (pending.Count > 0)
            {
                var type = pending.Pop();
                yield return type;

                foreach (var nested in type.NestedTypes) pending.Push(nested);
            }
        }

        /// <summary>
        /// Find a type by name in the module being woven or in the assemblies it names.
        /// </summary>
        /// <param name="module">The module.</param>
        /// <param name="fullName">The name of the type, written in full.</param>
        /// <returns>The type, or <c>null</c> where it cannot be found.</returns>
        private static TypeDefinition? FindType(ModuleDefinition module, string fullName)
        {
            var here = module.GetType(fullName);
            if (here != null) return here;

            foreach (var reference in module.AssemblyReferences)
            {
                try
                {
                    var assembly = module.AssemblyResolver.Resolve(reference);
                    var found = assembly?.MainModule.GetType(fullName);
                    if (found != null) return found;
                }
                catch (Exception)
                {
                    // A name which cannot be read is one which is left to the next: what is looked for is in one of the
                    // assemblies which were named, and an assembly which cannot be loaded is not that one.
                }
            }

            return null;
        }

        /// <summary>
        /// What the woven code is written against, read once for the module being woven.
        /// </summary>
        /// <remarks>
        /// Everything here is named in full and imported from the assemblies which the module already names, rather
        /// than taken from the types this program runs with: a reference which is written into an image is one Godot
        /// has to resolve when it loads it, and this program runs on a runtime of its own whose copy of an assembly is
        /// not the one the woven project will have.
        /// </remarks>
        private sealed class Godot
        {
            private Godot(TypeDefinition node, MethodReference enterTree, MethodReference ready, MethodReference exitTree,
                          MethodReference queueFree, MethodReference alive, MethodReference keep,
                          MethodReference opEquality, MethodReference getCount, TypeReference stringName,
                          MethodReference stringNameImplicit, TypeReference variant)
            {
                Node = node;
                EnterTree = enterTree;
                Ready = ready;
                ExitTree = exitTree;
                QueueFree = queueFree;
                Alive = alive;
                Keep = keep;
                OpEquality = opEquality;
                GetCount = getCount;
                StringName = stringName;
                StringNameImplicit = stringNameImplicit;
                Variant = variant;
            }

            /// <summary>What a node is.</summary>
            public TypeDefinition Node { get; }

            /// <summary>The notification which is sent as a node enters the tree.</summary>
            public MethodReference EnterTree { get; }

            /// <summary>The notification which is sent as a node has entered the tree.</summary>
            public MethodReference Ready { get; }

            /// <summary>The notification which is sent as a node leaves the tree.</summary>
            public MethodReference ExitTree { get; }

            /// <summary>What gives up a node.</summary>
            public MethodReference QueueFree { get; }

            /// <summary>What tells whether a node which is held is still there.</summary>
            public MethodReference Alive { get; }

            /// <summary>What keeps a node across a change of scene.</summary>
            public MethodReference Keep { get; }

            /// <summary>What a name is compared with a name by.</summary>
            public MethodReference OpEquality { get; }

            /// <summary>What the arguments a dispatch was asked with are counted by.</summary>
            public MethodReference GetCount { get; }

            /// <summary>What a name of a method is held as.</summary>
            public TypeReference StringName { get; }

            /// <summary>What a name of a method is made from a string by.</summary>
            public MethodReference StringNameImplicit { get; }

            /// <summary>What a dispatch answers with.</summary>
            public TypeReference Variant { get; }

            /// <summary>
            /// Read what the woven code is written against, or answer with nothing where the module does not name what
            /// it would take.
            /// </summary>
            /// <param name="module">The module which is woven.</param>
            /// <returns>What the code is written against, or <c>null</c>.</returns>
            public static Godot? Of(ModuleDefinition module)
            {
                var node = FindType(module, NODE);
                var runtime = FindType(module, RUNTIME);
                if (node == null || runtime == null) return null;

                var godot = node.Module;
                var stringName = godot.GetType("Godot.StringName");
                var arguments = godot.GetType("Godot.NativeInterop.NativeVariantPtrArgs");
                var variant = godot.GetType("Godot.NativeInterop.godot_variant");
                if (stringName == null || arguments == null || variant == null) return null;

                // StringName declares several equality overloads; the dispatch compares the engine's raw
                // godot_string_name against a cached StringName, so the one whose first parameter is the raw struct is
                // the one which is written.
                var opEquality = stringName.Methods.FirstOrDefault(method =>
                    method.Name == "op_Equality" && method.Parameters.Count == 2 &&
                    method.Parameters[0].ParameterType.FullName.Contains("godot_string_name", StringComparison.Ordinal));

                var stringNameImplicit = stringName.Methods.FirstOrDefault(method =>
                    method.Name == "op_Implicit" && method.Parameters.Count == 1 &&
                    method.Parameters[0].ParameterType.FullName == "System.String");

                var getCount = arguments.Methods.FirstOrDefault(method => method.Name == "get_Count");
                var queueFree = node.Methods.FirstOrDefault(method => method.Name == "QueueFree" && method.Parameters.Count == 0);
                var alive = runtime.Methods.FirstOrDefault(method => method.Name == "Alive" && method.Parameters.Count == 1);
                var keep = runtime.Methods.FirstOrDefault(method => method.Name == "Keep" && method.Parameters.Count == 1);
                var enterTree = node.Methods.FirstOrDefault(method => method.Name == ENTER_TREE && method.Parameters.Count == 0);
                var ready = node.Methods.FirstOrDefault(method => method.Name == READY && method.Parameters.Count == 0);
                var exitTree = node.Methods.FirstOrDefault(method => method.Name == EXIT_TREE && method.Parameters.Count == 0);

                if (opEquality == null || stringNameImplicit == null || getCount == null || queueFree == null ||
                    alive == null || keep == null || enterTree == null || ready == null || exitTree == null)
                {
                    return null;
                }

                return new Godot(node,
                    module.ImportReference(enterTree), module.ImportReference(ready), module.ImportReference(exitTree),
                    module.ImportReference(queueFree), module.ImportReference(alive), module.ImportReference(keep),
                    module.ImportReference(opEquality), module.ImportReference(getCount),
                    module.ImportReference(stringName), module.ImportReference(stringNameImplicit),
                    module.ImportReference(variant));
            }
        }
    }
}
