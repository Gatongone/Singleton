#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;
using UnityEngine;

namespace Singleton.Editor
{
    /// <summary>
    /// Writes the Unity messages which keep the field of a singleton true into the assemblies of a compilation, after
    /// they were compiled.<para/>
    /// The generated half of a singleton is the field and the property which makes an instance where there is none, and
    /// both of those can be written before the type is compiled. What cannot be written is the other direction: the
    /// instance Unity made is not known to the property until something tells the field about it, and the message
    /// which does that is <c>Awake</c> for a <c>MonoBehaviour</c> and <c>OnEnable</c> for a <c>ScriptableObject</c> -
    /// messages a type may already have written, so the code which tells the field cannot be generated beside them and
    /// is woven into them instead.
    /// </summary>
    /// <remarks>
    /// The weaving happens through the IL post processing of the compilation rather than beside it, because a message
    /// which is woven only into an assembly of the editor makes a singleton which is not one in a player. A post
    /// processor is a part of the compilation - it is handed the image an assembly was compiled to and answers with
    /// the image which replaces it - and it is run for the editor and for a player alike.<para/>
    /// Everything which is written into an image is written against the assemblies that image already names, and
    /// nothing is taken from the types the post processor itself runs with: a reference which is written here is one
    /// which the woven assembly has to be able to resolve, and an assembly which names the runtime of the post
    /// processor - <c>System.Private.CoreLib</c>, or the module Unity loaded a type out of - is one which Unity refuses
    /// to load at all.<para/>
    /// The assembly this is declared in is named <c>Unity.Singleton.CodeGen</c> rather than after the package, and it
    /// has to be: Unity hands <c>Unity.CompilationPipeline.Common</c> - the assembly <see cref="ILPostProcessor"/> is
    /// declared in - to an assembly whose name begins with <c>Unity</c> and ends with <c>CodeGen</c>, and to no other,
    /// so a post processor in an assembly named anything else fails to compile with <i>the type or namespace name
    /// 'CompilationPipeline' does not exist in the namespace 'Unity'</i>.
    /// </remarks>
    internal sealed class SingletonILPostProcessor : ILPostProcessor
    {
        /// <summary>The attribute which asks for a singleton.</summary>
        private const string SINGLETON_ATTRIBUTE = "Singleton.Runtime.SingletonAttribute";

        /// <summary>The attribute which asks for the object to be kept across scene loads.</summary>
        private const string PERSISTENT_ATTRIBUTE = "Singleton.Runtime.PersistentAttribute";

        /// <summary>The attribute which asks for the object to be hidden.</summary>
        private const string INVISIBLE_ATTRIBUTE = "Singleton.Runtime.InvisibleAttribute";

        /// <summary>The field which holds the instance.</summary>
        private const string INSTANCE_FIELD = "s_Instance";

        /// <summary>What a <c>MonoBehaviour</c> is.</summary>
        private const string MONO_BEHAVIOUR = "UnityEngine.MonoBehaviour";

        /// <summary>What a <c>ScriptableObject</c> is.</summary>
        private const string SCRIPTABLE_OBJECT = "UnityEngine.ScriptableObject";

        /// <inheritdoc/>
        public override ILPostProcessor GetInstance() => new SingletonILPostProcessor();

        /// <summary>
        /// Whether an assembly declares a singleton, which is the one question worth asking of it.<para/>
        /// The types of the module are read without resolving what they refer to, so that asking the question is cheap
        /// and cannot fail: an assembly which declares no singleton is given <c>false</c> here and is never woven.
        /// </summary>
        /// <param name="assembly">The assembly which was compiled.</param>
        /// <returns>Whether it declares one.</returns>
        public override bool WillProcess(ICompiledAssembly assembly)
        {
            try
            {
                using var stream = new MemoryStream(assembly.InMemoryAssembly.PeData!);
                using var image = AssemblyDefinition.ReadAssembly(stream);
                return Types(image.MainModule).Any(Marks);
            }
            catch (Exception)
            {
                // An image which cannot be read here is one the weaving would fail on as well, and the weaving reports
                // that failure itself. Answering false leaves the assembly as it was compiled.
                return false;
            }
        }

        /// <inheritdoc/>
        public override ILPostProcessResult Process(ICompiledAssembly assembly)
        {
            var diagnostics = new List<DiagnosticMessage>();

            try
            {
                var resolver = new DefaultAssemblyResolver();
                foreach (var reference in assembly.References)
                {
                    var directory = Path.GetDirectoryName(reference);
                    if (!string.IsNullOrEmpty(directory)) resolver.AddSearchDirectory(directory);
                }

                var written = Weave(assembly.InMemoryAssembly.PeData!, assembly.InMemoryAssembly.PdbData, resolver, diagnostics);
                if (written == null) return new ILPostProcessResult(assembly.InMemoryAssembly, diagnostics);

                return new ILPostProcessResult(new InMemoryAssembly(written.Value.Pe, written.Value.Pdb), diagnostics);
            }
            catch (Exception exception)
            {
                // An assembly which could not be woven is left as it was compiled, and what went wrong is reported as
                // an error of the compilation which produced the assembly rather than swallowed.
                diagnostics.Add(Error($"The singletons of '{assembly.Name}' were not woven. {exception}"));
                return new ILPostProcessResult(assembly.InMemoryAssembly, diagnostics);
            }
        }

        /// <summary>
        /// Weave every singleton of an image.
        /// </summary>
        /// <param name="pe">The image which was compiled.</param>
        /// <param name="pdb">The symbols of it, where it was compiled with any.</param>
        /// <param name="resolver">What the types of the assemblies it refers to are read through.</param>
        /// <param name="diagnostics">What is reported.</param>
        /// <returns>The woven image, or <c>null</c> where there was nothing to weave.</returns>
        private static (byte[] Pe, byte[]? Pdb)? Weave(byte[] pe, byte[]? pdb, IAssemblyResolver resolver,
            List<DiagnosticMessage> diagnostics)
        {
            // The symbols are read with the image and woven with it, because the two are one pair: the places a
            // database records are places of the image it names, and the weaving writes an image of its own.
            var symbols = pdb is {Length: > 0};

            using var read = new MemoryStream(pe);
            using var image = Read(read, pdb, symbols, resolver, diagnostics);
            var unity = default(Unity);
            var woven = false;

            // What is woven is the types which asked for it, and a base type is woven before the types which derive
            // from it: what a message which is made is written from is the message of the type the deriving one has,
            // and a type which is woven before the type it derives from is one whose message names a method which that
            // type has not been given yet, and the message of it is then never run.
            var marked = Types(image.MainModule)
                .Where(Marks)
                .OrderBy(type => Depth(type, image.MainModule))
                .ToList();

            foreach (var type in marked)
            {
                // What a message of Unity is written against is read once for the image, and only for an image which
                // holds a type worth weaving: an assembly which holds none is left, and not one of the assemblies it
                // names is read.
                unity ??= Unity.Of(image.MainModule, resolver);
                woven |= Weave(type, image.MainModule, unity, diagnostics);
            }

            if (!woven) return null;

            using var write = new MemoryStream();
            if (symbols)
            {
                try
                {
                    // The symbols of a compilation are portable ones, and their writer is named rather than left to
                    // Cecil to work out from how the module was read: the writer it works out for the symbols of a
                    // machine does not write to a stream at all, so the weaving of a project which was compiled with
                    // symbols would fail.
                    using var symbolStream = new MemoryStream();
                    image.Write(write, new WriterParameters
                    {
                        WriteSymbols         = true,
                        SymbolStream         = symbolStream,
                        SymbolWriterProvider = new PortablePdbWriterProvider()
                    });
                    return (write.ToArray(), symbolStream.ToArray());
                }
                catch (Exception exception)
                {
                    // Symbols which cannot be written with the woven image are given up rather than handed back with
                    // an image they do not describe, which a reader of the two would refuse. What is lost is where the
                    // woven messages are read in a debugger, and what is kept is the assembly itself, so the giving up
                    // is reported as something which went wrong and not as something which failed.
                    diagnostics.Add(Warning($"The symbols of the woven singletons could not be written and were given up. {exception.Message}"));
                    write.SetLength(0);
                }
            }

            image.Write(write, new WriterParameters {WriteSymbols = false});
            return (write.ToArray(), null);
        }

        /// <summary>
        /// Read an image, with the symbols it was compiled with where it was compiled with any.
        /// </summary>
        /// <param name="stream">The image.</param>
        /// <param name="pdb">The symbols of it.</param>
        /// <param name="symbols">Whether there are any.</param>
        /// <param name="resolver">What the types of the assemblies it refers to are read through.</param>
        /// <param name="diagnostics">What is reported.</param>
        /// <returns>The image.</returns>
        private static AssemblyDefinition Read(Stream stream, byte[]? pdb, bool symbols, IAssemblyResolver resolver,
            List<DiagnosticMessage> diagnostics)
        {
            if (!symbols)
            {
                return AssemblyDefinition.ReadAssembly(stream, new ReaderParameters {AssemblyResolver = resolver, InMemory = true});
            }

            try
            {
                return AssemblyDefinition.ReadAssembly(stream, new ReaderParameters
                {
                    AssemblyResolver     = resolver,
                    InMemory             = true,
                    ReadSymbols          = true,
                    SymbolStream         = new MemoryStream(pdb!),
                    SymbolReaderProvider = new PortablePdbReaderProvider()
                });
            }
            catch (Exception exception)
            {
                // An image whose symbols cannot be read is woven without them, which leaves an assembly which works
                // and a debugger which cannot say where in it a woven message is.
                diagnostics.Add(Warning($"The symbols of an image could not be read and were given up. {exception.Message}"));
                stream.Position = 0;
                return AssemblyDefinition.ReadAssembly(stream, new ReaderParameters {AssemblyResolver = resolver, InMemory = true});
            }
        }

        /// <summary>
        /// Weave into a type the messages which give the field of a singleton the instance Unity made, and which do to
        /// the object what the two attributes of the package ask of it.
        /// </summary>
        /// <remarks>
        /// Both are written into one message rather than one message each, and the singleton is written first: the
        /// attributes ask something of the object of the instance which is held, so a second instance, which is given
        /// up before either of them runs, is never kept and never hidden.
        /// </remarks>
        /// <param name="type">The type.</param>
        /// <param name="module">The module the type lies in.</param>
        /// <param name="unity">What a message of Unity is written against.</param>
        /// <returns>Whether the type was woven into.</returns>
        private static bool Weave(TypeDefinition type, ModuleDefinition module, Unity? unity, List<DiagnosticMessage> diagnostics)
        {
            var singleton = AsksForASingleton(type);
            var keep = Has(type, PERSISTENT_ATTRIBUTE);
            var invisible = Has(type, INVISIBLE_ATTRIBUTE);

            if (!singleton && !keep && !invisible) return false;

            // A type whose half was not generated is one the generator refused, and the compilation carries the reason
            // already. There is no field for a message to keep true, so there is nothing to weave.
            var instance = singleton
                ? type.Fields.FirstOrDefault(field => field.Name == INSTANCE_FIELD && field.IsStatic)
                : null;

            if (singleton && instance == null) return false;

            if (IsDerivedFrom(type, module, MONO_BEHAVIOUR))
            {
                // A MonoBehaviour belongs to a GameObject which Unity made or which the property made, so the instance
                // Unity made is the one the field is given, and a second one is destroyed with its object.
                if (unity == null) return false;
                if (Message(type, "Awake", canBeOverridden: false) is not { } awake) return false;

                Inject(awake, _ => Tell(unity, instance, destroyTheOther: singleton, keep: keep, invisible: invisible));

                // A type which asked for no singleton has no field to give up, and the two attributes have nothing to
                // undo when the object is destroyed.
                if (!singleton) return true;

                if (Message(type, "OnDestroy", canBeOverridden: true) is not { } gone) return false;
                Inject(gone, first => GiveUp(first, instance!));
                return true;
            }

            // The two attributes ask something of the GameObject of a MonoBehaviour, and the generator reports an
            // attribute which is on a type which has none. Nothing is woven and nothing is reported here.
            if (!singleton || !IsDerivedFrom(type, module, SCRIPTABLE_OBJECT)) return false;

            // A ScriptableObject is a value which Unity loads, and there is no object of it to destroy: a second one
            // which is enabled is simply left out of the field. The messages of a ScriptableObject name nothing of
            // Unity, so one is woven whether or not the types of Unity could be read.
            if (Message(type, "OnEnable", canBeOverridden: false) is not { } enabled) return false;
            Inject(enabled, _ => Tell(null, instance, destroyTheOther: false, keep: false, invisible: false));

            if (Message(type, "OnDisable", canBeOverridden: false) is not { } disabled) return false;
            Inject(disabled, first => GiveUp(first, instance!));
            return true;
        }

        /// <summary>
        /// Read the messages which give the field the instance Unity made, and which do to the object of that instance
        /// what the two attributes ask of it.
        /// </summary>
        /// <remarks>
        /// What is written is what a type would have written for itself: an instance which is not the one which is held
        /// is given up, and the one which is held is given to the field, along with what the two attributes ask of the
        /// object.<para/>
        /// The instance which is held is written to the field again when the field already holds it, which is not a
        /// message written twice for nothing: the property makes the instance of the default <c>MonoBehaviour</c>
        /// singleton and puts it in the field before Unity runs <c>Awake</c> on it, so a message which skipped the work
        /// when the field holds it would be the one message a <c>Persistent</c> singleton never runs.<para/>
        /// A type which asked for no singleton is one which has no field, and what is written for it is only what the
        /// two attributes ask of the object: the object of a <c>MonoBehaviour</c> is there whether or not the type is a
        /// singleton.
        /// </remarks>
        /// <param name="unity">What a message of Unity is written against, which the messages below need.</param>
        /// <param name="instance">The field which holds the instance, or <c>null</c> where there is none to hold.</param>
        /// <param name="destroyTheOther">Whether an instance which is not the one held is destroyed with its object.</param>
        /// <param name="keep">Whether the object is to be kept across scene loads.</param>
        /// <param name="invisible">Whether the object is to be hidden.</param>
        /// <returns>The messages.</returns>
        private static List<Instruction> Tell(Unity? unity, FieldDefinition? instance,
            bool destroyTheOther, bool keep, bool invisible)
        {
            var instructions = new List<Instruction>();

            if (instance != null)
            {
                var assign = Instruction.Create(OpCodes.Ldarg_0);
                instructions.Add(Instruction.Create(OpCodes.Ldsfld, instance));
                instructions.Add(Instruction.Create(OpCodes.Brfalse, assign));
                instructions.Add(Instruction.Create(OpCodes.Ldsfld, instance));
                instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                instructions.Add(Instruction.Create(OpCodes.Beq, assign));

                if (destroyTheOther)
                {
                    instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                    instructions.Add(Instruction.Create(OpCodes.Callvirt, unity!.GameObject));
                    instructions.Add(Instruction.Create(OpCodes.Call, unity.Destroy));
                }

                // A second instance is given up here, before either attribute is applied: an object which is about to
                // be destroyed is not one to keep across scene loads and not one to hide.
                instructions.Add(Instruction.Create(OpCodes.Ret));

                instructions.Add(assign);
                instructions.Add(Instruction.Create(OpCodes.Stsfld, instance));
            }

            if (keep)
            {
                instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                instructions.Add(Instruction.Create(OpCodes.Callvirt, unity!.GameObject));
                instructions.Add(Instruction.Create(OpCodes.Call, unity.Keep));
            }

            if (invisible)
            {
                instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                instructions.Add(Instruction.Create(OpCodes.Callvirt, unity!.GameObject));
                instructions.Add(Instruction.Create(OpCodes.Ldc_I4, unity.Invisible));
                instructions.Add(Instruction.Create(OpCodes.Callvirt, unity.SetHideFlags));
            }

            // The messages above fall through into the body which was written.
            return instructions;
        }

        /// <summary>
        /// Read the messages which give the field up when the instance is gone.
        /// </summary>
        /// <param name="after">What the message does of its own, which is run when the field does not hold it.</param>
        /// <param name="instance">The field which holds the instance.</param>
        /// <returns>The messages.</returns>
        private static List<Instruction> GiveUp(Instruction after, FieldDefinition instance) => new()
        {
            Instruction.Create(OpCodes.Ldsfld, instance),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Bne_Un, after),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Stsfld, instance)
        };

        /// <summary>
        /// Write a prologue in front of what a message does of its own.<para/>
        /// The body which was written comes after the prologue rather than inside it, so a message which is woven into
        /// runs as it was written when the field already holds the instance.
        /// </summary>
        /// <remarks>
        /// A branch which pointed at the first instruction of the body still points at it and not at the prologue
        /// which was put in front of it: a message which loops back to its own beginning loops back to the body it was
        /// written with, and running the prologue again is neither what was written nor what it needs.
        /// </remarks>
        /// <param name="message">The message which is woven into.</param>
        /// <param name="prologue">What is written in front of it, read against the body which is run after it.</param>
        private static void Inject(MethodDefinition message, Func<Instruction, List<Instruction>> prologue)
        {
            var body = message.Body;
            var processor = body.GetILProcessor();
            var first = body.Instructions[0];

            foreach (var instruction in prologue(first)) processor.InsertBefore(first, instruction);

            // What was woven asks two values of the stack at most, and a message whose stack was already deeper keeps
            // the depth it had.
            body.MaxStackSize = Math.Max(body.MaxStackSize, 8);
        }

        /// <summary>
        /// Read the message to be woven into a type, or make one where the type has none.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="name">The name of the message.</param>
        /// <param name="canBeOverridden">Whether the message which is made is virtual.</param>
        /// <returns>The message, or <c>null</c> where the type declares one which cannot be woven into.</returns>
        private static MethodDefinition? Message(TypeDefinition type, string name, bool canBeOverridden)
        {
            foreach (var method in type.Methods)
            {
                if (method.Name != name) continue;
                if (method.IsStatic || method.Parameters.Count != 0) continue;
                if (method.ReturnType.FullName != "System.Void") continue;

                // A message which was written is woven into as it stands: what it is, what it returns and who may call
                // it are the author's, and what is woven needs no local of its own so that nothing of the body has to
                // be moved out of the way.
                if (method.IsAbstract || method.IsRuntime) return null;

                if (!method.HasBody) method.Body = Empty(method);
                else if (method.Body.Instructions.Count == 0) method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

                return method;
            }

            // A message which Unity sends is one Unity finds by name alone, so what is made here is private like the
            // ones a type writes for itself. The one which gives the instance up may be made virtual instead, because a
            // type which derives from a singleton may have one of its own to write.
            var attributes = canBeOverridden
                ? MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.HideBySig
                : MethodAttributes.Private | MethodAttributes.HideBySig;

            var made = new MethodDefinition(name, attributes, type.Module.ImportReference(type.Module.TypeSystem.Void))
            {
                ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed
            };

            type.Methods.Add(made);
            made.Body = Empty(made);

            // A message which is made is one the type never wrote, and Unity sends a message to the nearest declaration
            // of it alone: a base type which wrote one is a body which ran before anything was woven here and which
            // would not run after, so it is called from the message which was made. What a type wrote itself is left as
            // it is, because a message which a type writes over one of its base is one Unity sent to that message
            // before this package was added at all.
            if (Inherited(type, name) is { } inherited && Reachable(inherited, type.Module))
            {
                made.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Call, type.Module.ImportReference(inherited)));
                made.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Ldarg_0));
            }

            return made;
        }

        /// <summary>
        /// Read the message of the nearest base type which declares one.
        /// </summary>
        /// <remarks>
        /// Unity sends a message to the nearest declaration of it, so what is read here is what Unity would have sent
        /// the message to had nothing been woven into the type. A message which a type further up declares is reached
        /// through the one below it rather than called from here, because it is that one which would have run.<para/>
        /// The chain is walked through <see cref="Base"/>, which is what leaves it where a base type cannot be read: a
        /// chain which cannot be read is then one which no message is called from, rather than one which the weaving of
        /// the whole assembly stops at.
        /// </remarks>
        /// <param name="type">The type.</param>
        /// <param name="name">The name of the message.</param>
        /// <returns>The message, or <c>null</c> where no base type declares one.</returns>
        private static MethodDefinition? Inherited(TypeDefinition type, string name)
        {
            var module = type.Module;
            var current = Base(type, module);
            for (var depth = 0; current != null && depth < 128; depth++, current = Base(current, module))
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
        /// Whether a message of another type may be called from the one being woven, which a private message may not.
        /// </summary>
        /// <remarks>
        /// A private message of a base type is one no other type may call, and it is one Unity ran for a type which
        /// derived from it until a message was woven into that type: what is widened here is the access of a method
        /// which the weaving would otherwise have taken out of the run of it. A private message of an assembly which is
        /// not this one cannot be widened, and is left where it is.
        /// </remarks>
        /// <param name="method">The message.</param>
        /// <param name="module">The module being woven.</param>
        /// <returns>Whether it may be called.</returns>
        private static bool Reachable(MethodDefinition method, ModuleDefinition module)
        {
            if (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly) return true;
            if (method.Module.Assembly.Name.FullName != module.Assembly.Name.FullName) return false;

            if (method.IsPrivate)
            {
                method.Attributes = (method.Attributes & ~MethodAttributes.MemberAccessMask) | MethodAttributes.Family;
            }

            return true;
        }

        /// <summary>
        /// Read a body which does nothing, which is what a message a type never wrote begins as.
        /// </summary>
        /// <param name="method">The message.</param>
        /// <returns>The body.</returns>
        private static MethodBody Empty(MethodDefinition method)
        {
            var body = new MethodBody(method)
            {
                InitLocals   = true,
                MaxStackSize = 8
            };

            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            return body;
        }

        /// <summary>
        /// Whether a type asked for a singleton.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>Whether it did.</returns>
        private static bool AsksForASingleton(TypeDefinition type) => Has(type, SINGLETON_ATTRIBUTE);

        /// <summary>
        /// Whether a type is one which the weaving has something to write into: a singleton, a type which asked to be
        /// kept across scene loads, or a type which asked to be hidden.
        /// </summary>
        /// <remarks>
        /// The two attributes stand on their own. What they ask of is the <c>GameObject</c> of a <c>MonoBehaviour</c>,
        /// which is there whether or not the type is a singleton, and a type which carries one of them and no singleton
        /// is one which the generator wrote nothing for at all.
        /// </remarks>
        /// <param name="type">The type.</param>
        /// <returns>Whether it is.</returns>
        private static bool Marks(TypeDefinition type) =>
            AsksForASingleton(type) ||
            Has(type, PERSISTENT_ATTRIBUTE) ||
            Has(type, INVISIBLE_ATTRIBUTE);

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
        /// <param name="module">The module the type lies in.</param>
        /// <param name="name">The name of the type which is looked for, written in full.</param>
        /// <returns>Whether it is.</returns>
        private static bool IsDerivedFrom(TypeDefinition type, ModuleDefinition module, string name)
        {
            var current = type;
            for (var depth = 0; current != null && depth < 128; depth++)
            {
                if (current.FullName == name) return true;
                current = Base(current, module);
            }

            return false;
        }

        /// <summary>
        /// Read the type which a type is declared as.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="module">The module the type lies in.</param>
        /// <returns>The type it is declared as, or <c>null</c> where it is declared as none.</returns>
        private static TypeDefinition? Base(TypeDefinition type, ModuleDefinition module)
        {
            if (type.BaseType == null) return null;
            if (type.BaseType is TypeDefinition definition) return definition;

            var beside = module.GetType(type.BaseType.FullName);
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
        /// Read how many types a type derives from, which is what the order of the weaving is read in.
        /// </summary>
        /// <remarks>
        /// The chain is walked through <see cref="Base"/>, which reads a base type out of the module it lies beside
        /// before it reads it out of the assembly it names and leaves the chain where that cannot be done: a base type
        /// which is read by resolving it alone ends the whole of the weaving where it cannot be resolved, rather than
        /// the chain it stands in.
        /// </remarks>
        /// <param name="type">The type.</param>
        /// <param name="module">The module the type lies in.</param>
        /// <returns>The number of them.</returns>
        private static int Depth(TypeDefinition type, ModuleDefinition module)
        {
            var depth = 0;
            for (var current = Base(type, module); current != null && depth < 128; current = Base(current, module)) depth++;

            return depth;
        }

        /// <summary>
        /// Whether the chain of base types of a type can be read to the end of it.
        /// </summary>
        /// <remarks>
        /// A chain which ends is one whose last type is declared as none, and a chain which <see cref="Base"/> answered
        /// with nothing about is one which could not be read: the two are told apart by the type which was left holding
        /// a base type, which is the one the reading stopped at.
        /// </remarks>
        /// <param name="type">The type.</param>
        /// <param name="module">The module the type lies in.</param>
        /// <returns>Whether it can be read.</returns>
        private static bool Readable(TypeDefinition type, ModuleDefinition module)
        {
            var current = type;
            for (var depth = 0; current != null && depth < 128; depth++)
            {
                if (current.BaseType == null) return true;

                current = Base(current, module);
            }

            return false;
        }

        /// <summary>
        /// Report a weaving which stopped at a type, and answer with what the weaving of that type answers with.
        /// </summary>
        /// <remarks>
        /// A diagnostic fails the compilation, and the process which runs the weaving prints what it throws and none of
        /// what a processor answers it with: a failure which is not thrown is one no reader of a run finds, so what the
        /// weaving can report it reports and what it cannot it throws.
        /// </remarks>
        /// <param name="message">What is reported.</param>
        /// <param name="diagnostics">What is reported.</param>
        /// <returns><c>false</c>, which is what the weaving of a type answers with.</returns>
        private static bool Stop(string message, List<DiagnosticMessage> diagnostics)
        {
            diagnostics.Add(Error(message));
            return false;
        }

        /// <summary>
        /// Report what went wrong while an assembly was woven.
        /// </summary>
        /// <param name="message">What is reported.</param>
        /// <returns>The report.</returns>
        private static DiagnosticMessage Error(string message) => new()
        {
            DiagnosticType = DiagnosticType.Error,
            MessageData    = message
        };

        /// <summary>
        /// Report something which went wrong while an assembly was woven and which the assembly survives.
        /// </summary>
        /// <param name="message">What is reported.</param>
        /// <returns>The report.</returns>
        private static DiagnosticMessage Warning(string message) => new()
        {
            DiagnosticType = DiagnosticType.Warning,
            MessageData    = message
        };

        /// <summary>
        /// What the messages of a <c>MonoBehaviour</c> are written against, read out of the assemblies which the image
        /// being woven already names.
        /// </summary>
        /// <remarks>
        /// Nothing here is read off the types which the post processor itself runs with. A reference which is written
        /// into an image is one which Unity has to resolve when it loads the image, and the post processor runs on a
        /// runtime of its own: a member of Unity taken from the copy which that runtime loaded is a reference to an
        /// assembly - <c>System.Private.CoreLib</c> for a type of the runtime, or the module Unity happened to be
        /// loaded from - which the woven assembly has no way to resolve and which makes Unity refuse the whole
        /// assembly.
        /// </remarks>
        private sealed class Unity
        {
            private const int INVISIBLE_FLAGS = (int) (HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.DontSave);

            private Unity(MethodReference gameObject, MethodReference destroy, MethodReference keep,
                MethodReference setHideFlags, int invisible)
            {
                GameObject   = gameObject;
                Destroy      = destroy;
                Keep         = keep;
                SetHideFlags = setHideFlags;
                Invisible    = invisible;
            }

            /// <summary>What the object of a component is read by.</summary>
            public MethodReference GameObject { get; }

            /// <summary>What destroys an object.</summary>
            public MethodReference Destroy { get; }

            /// <summary>What takes an object out of what a scene load destroys.</summary>
            public MethodReference Keep { get; }

            /// <summary>What the flags of an object are written by.</summary>
            public MethodReference SetHideFlags { get; }

            /// <summary>The flags which take an object out of the hierarchy, the Inspector and what a save writes.</summary>
            public int Invisible { get; }

            /// <summary>
            /// Read what the messages of a <c>MonoBehaviour</c> are written against, or answer with nothing where the
            /// assembly which holds the types of Unity could not be read.
            /// </summary>
            /// <param name="module">The module which is woven.</param>
            /// <param name="resolver">What the assemblies it names are read through.</param>
            /// <returns>What the messages are written against, or <c>null</c>.</returns>
            public static Unity? Of(ModuleDefinition module, IAssemblyResolver resolver)
            {
                var assembly = Assembly(module, resolver);
                if (assembly == null) return null;

                var component = assembly.MainModule.GetType("UnityEngine.Component");
                var handle = assembly.MainModule.GetType("UnityEngine.Object");
                var flags = assembly.MainModule.GetType("UnityEngine.HideFlags");

                var gameObject = Method(module, component, "get_gameObject", 0);
                var destroy = Method(module, handle, "Destroy", 1);
                var keep = Method(module, handle, "DontDestroyOnLoad", 1);
                var hide = Method(module, handle, "set_hideFlags", 1);

                if (gameObject == null || destroy == null || keep == null || hide == null) return null;

                return new Unity(gameObject, destroy, keep, hide, FlagsOf(flags));
            }

            /// <summary>
            /// Read the assembly which holds the types of Unity.
            /// </summary>
            /// <remarks>
            /// What a <c>MonoBehaviour</c> is declared in is what everything a <c>MonoBehaviour</c> is made of is
            /// declared in, and the name of that assembly is written in the image itself: a <c>MonoBehaviour</c>
            /// singleton names <c>UnityEngine.MonoBehaviour</c> as the type it is declared as, and the scope of that
            /// name is the assembly the messages are written against.
            /// </remarks>
            /// <param name="module">The module which is woven.</param>
            /// <param name="resolver">What the assemblies it names are read through.</param>
            /// <returns>The assembly, or <c>null</c> where it could not be read.</returns>
            private static AssemblyDefinition? Assembly(ModuleDefinition module, IAssemblyResolver resolver)
            {
                foreach (var name in new[] {MONO_BEHAVIOUR, SCRIPTABLE_OBJECT, "UnityEngine.Component", "UnityEngine.Object"})
                {
                    var reference = module.GetTypeReferences().FirstOrDefault(type => type.FullName == name);
                    if (reference?.Scope is not AssemblyNameReference scope) continue;

                    try
                    {
                        var assembly = resolver.Resolve(scope);
                        if (assembly != null) return assembly;
                    }
                    catch (Exception)
                    {
                        // A name which cannot be read is one which is left to the next: one of the names above is
                        // enough, and they are all in the same assembly.
                    }
                }

                return null;
            }

            /// <summary>
            /// Read a message of a type of Unity, written against the module which is woven.
            /// </summary>
            /// <param name="module">The module which is woven.</param>
            /// <param name="type">The type which declares it.</param>
            /// <param name="name">The name of the message.</param>
            /// <param name="parameters">How many parameters it takes.</param>
            /// <returns>The message, or <c>null</c> where the type does not declare one.</returns>
            private static MethodReference? Method(ModuleDefinition module, TypeDefinition? type, string name, int parameters)
            {
                if (type == null) return null;

                foreach (var method in type.Methods)
                {
                    if (method.Name != name || method.Parameters.Count != parameters) continue;
                    return module.ImportReference(method);
                }

                return null;
            }

            /// <summary>
            /// Read the flags which take an object out of the hierarchy, the Inspector and what a save writes.
            /// </summary>
            /// <param name="flags">The enumeration of them.</param>
            /// <returns>The flags.</returns>
            private static int FlagsOf(TypeDefinition? flags)
            {
                if (flags == null) return INVISIBLE_FLAGS;

                var value = 0;
                foreach (var name in new[] {"HideInHierarchy", "HideInInspector", "DontSave"})
                {
                    foreach (var field in flags.Fields.Where(field => field.Name == name && field.HasConstant))
                    {
                        if (field.Constant is int constant) value |= constant;
                    }
                }

                return value == 0 ? INVISIBLE_FLAGS : value;
            }
        }
    }
}