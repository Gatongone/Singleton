using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Singleton.Generator
{
    /// <summary>
    /// Gives every type which asked for a singleton the half of it which holds the field and the property.
    /// </summary>
    /// <remarks>
    /// The generator writes the half which can be written before the type is compiled, which is the field and the
    /// property. What cannot be - the <c>Awake</c> or the <c>OnEnable</c> which tells the field which instance Unity
    /// made, and the <c>OnDestroy</c> or the <c>OnDisable</c> which clears the field - is woven into the assembly
    /// after it was compiled, by the post processor of the editor half of this package.<para/>
    /// Whatever stops a type from having a singleton is reported rather than written, because a singleton which is not
    /// the one which was asked for is worse than one which was refused: a creator whose arguments fit no constructor,
    /// an asset which is not there, and a type which declares a member of its own which the generated half declares
    /// are all errors of the compilation.
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public sealed class InstanceGenerator : ISourceGenerator
    {
        /// <inheritdoc/>
        public void Initialize(GeneratorInitializationContext context)
            => context.RegisterForSyntaxNotifications(() => new SingletonSyntaxReceiver());

        /// <inheritdoc/>
        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not SingletonSyntaxReceiver receiver) return;

            var compilation = context.Compilation;
            var cancellation = context.CancellationToken;
            var attribute = compilation.GetTypeByMetadataName(TypeNames.SINGLETON_ATTRIBUTE);
            if (attribute == null) return;

            var markers = new Markers(compilation);
            var arguments = new ArgumentRenderer(compilation);
            var locator = new ResourcesAssetLocator(ResourcesAssetLocator.FindProjectRoot(compilation, context.AdditionalFiles));
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var hints = new HashSet<string>(StringComparer.Ordinal);

            foreach (var declaration in receiver.Types)
            {
                cancellation.ThrowIfCancellationRequested();

                var model = compilation.GetSemanticModel(declaration.SyntaxTree);
                if (model.GetDeclaredSymbol(declaration, cancellation) is not INamedTypeSymbol type) continue;
                if (!seen.Add(type)) continue;

                Report(markers, type, context);

                if (Find(type, attribute) is not { } singleton) continue;

                var target = Read(context, type, singleton, markers, arguments, locator);
                if (target == null) continue;

                context.AddSource(Hint(type, hints), SourceText.From(SingletonEmitter.Emit(target, arguments), Encoding.UTF8));
            }
        }

        /// <summary>
        /// Report an attribute which was put on a type which cannot carry it.
        /// </summary>
        /// <param name="markers">The types of the runtime half.</param>
        /// <param name="type">The type.</param>
        /// <param name="context">What is reported to.</param>
        private static void Report(Markers markers, INamedTypeSymbol type, GeneratorExecutionContext context)
        {
            if (markers.IsMonoBehaviour(type)) return;

            // The two attributes of the weaver name something which only a MonoBehaviour has - a GameObject - so on
            // anything else they ask for something which cannot be done, and saying so here is saying it at the place
            // the attribute was written rather than in an assembly which was already compiled.
            if (markers.Has(type, markers.Persistent))
            {
                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.PersistentRequiresMonoBehaviour, Where(type), type.Name));
            }

            if (markers.Has(type, markers.Invisible))
            {
                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.InvisibleRequiresMonoBehaviour, Where(type), type.Name));
            }
        }

        /// <summary>
        /// Read what a type asked for, and answer with what the half of it is written from.
        /// </summary>
        /// <param name="context">What is reported to.</param>
        /// <param name="type">The type.</param>
        /// <param name="attribute">The attribute which was put on it.</param>
        /// <param name="markers">The types of the runtime half.</param>
        /// <param name="arguments">What the arguments of a creator are read by.</param>
        /// <param name="locator">What the asset of a <c>Resources</c> singleton is read by.</param>
        /// <returns>The target, or <c>null</c> where the type cannot have the singleton it asked for.</returns>
        private static SingletonTarget? Read(GeneratorExecutionContext context, INamedTypeSymbol type, AttributeData attribute,
                                             Markers markers, ArgumentRenderer arguments, ResourcesAssetLocator locator)
        {
            var where = Where(type);

            if (NotPartial(type) is { } notPartial)
            {
                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.TypeMustBePartial, where, type.Name, notPartial.Name));
                return null;
            }

            if (ReasonToRefuse(type) is { } reason)
            {
                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.UnsupportedType, where, type.Name, reason));
                return null;
            }

            foreach (var member in type.GetMembers())
            {
                if (member.Name is not ("Instance" or "s_Instance")) continue;

                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.MemberConflict, where, type.Name, member.Name));
                return null;
            }

            var isMonoBehaviour = markers.IsMonoBehaviour(type);
            var isScriptableObject = markers.IsScriptableObject(type);

            var read = attribute.ConstructorArguments.Length == 0
                ? Default(type, isMonoBehaviour, isScriptableObject, context, where)
                : FromArguments(context, type, attribute, markers, arguments, locator, isMonoBehaviour, isScriptableObject, where);

            if (read == null) return null;

            read.Persistent = markers.Has(type, markers.Persistent);
            read.Invisible = markers.Has(type, markers.Invisible);
            return read;
        }

        /// <summary>
        /// Read a type which asked for a singleton and named nothing, whose instance is made by Unity or by its own
        /// constructor.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="isMonoBehaviour">Whether the type is a <c>MonoBehaviour</c>.</param>
        /// <param name="isScriptableObject">Whether the type is a <c>ScriptableObject</c>.</param>
        /// <param name="context">What is reported to.</param>
        /// <param name="where">Where the type was declared.</param>
        /// <returns>The target, or <c>null</c> where the type cannot have the singleton it asked for.</returns>
        private static SingletonTarget? Default(INamedTypeSymbol type, bool isMonoBehaviour, bool isScriptableObject,
                                                GeneratorExecutionContext context, Location where)
        {
            if (isMonoBehaviour) return new SingletonTarget(type, SingletonInstance.MonoBehaviour);
            if (isScriptableObject) return new SingletonTarget(type, SingletonInstance.ScriptableObject);

            // A type which Unity makes no instance of is made by its own constructor, which the generated property
            // calls from inside the type itself and which therefore may be of any accessibility - but has to be there,
            // and is not there if the author wrote a constructor which takes arguments and no other.
            if (type.InstanceConstructors.All(constructor => constructor.Parameters.Length != 0))
            {
                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.ParameterlessConstructorMissing, where, type.Name));
                return null;
            }

            return new SingletonTarget(type, SingletonInstance.Newable);
        }

        /// <summary>
        /// Read a type which asked for a singleton and named an asset type or a creator.
        /// </summary>
        /// <param name="context">What is reported to.</param>
        /// <param name="type">The type.</param>
        /// <param name="attribute">The attribute which was put on it.</param>
        /// <param name="markers">The types of the runtime half.</param>
        /// <param name="arguments">What the arguments of a creator are read by.</param>
        /// <param name="locator">What the asset of a <c>Resources</c> singleton is read by.</param>
        /// <param name="isMonoBehaviour">Whether the type is a <c>MonoBehaviour</c>.</param>
        /// <param name="isScriptableObject">Whether the type is a <c>ScriptableObject</c>.</param>
        /// <param name="where">Where the type was declared.</param>
        /// <returns>The target, or <c>null</c> where the type cannot have the singleton it asked for.</returns>
        private static SingletonTarget? FromArguments(GeneratorExecutionContext context, INamedTypeSymbol type, AttributeData attribute,
                                                      Markers markers, ArgumentRenderer arguments, ResourcesAssetLocator locator,
                                                      bool isMonoBehaviour, bool isScriptableObject, Location where)
        {
            var constructor = attribute.ConstructorArguments;
            var first = constructor[0];

            if (first.Type != null && markers.AssetType != null && SymbolEqualityComparer.Default.Equals(first.Type, markers.AssetType))
            {
                return FromAsset(context, type, attribute, markers, locator, isMonoBehaviour, isScriptableObject, where);
            }

            if (first.Type != null && markers.Type != null && SymbolEqualityComparer.Default.Equals(first.Type, markers.Type))
            {
                return FromCreator(context, type, attribute, markers, arguments, where);
            }

            // The attribute has three constructors, so this is a use of it which the generator does not know: it is
            // reported as a type which cannot have a singleton rather than left to be answered with nothing.
            context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.UnsupportedType, where, type.Name,
                                                       "the arguments of the attribute are not an asset type or a creator"));
            return null;
        }

        /// <summary>
        /// Read a type which asked to be loaded from an asset.
        /// </summary>
        /// <param name="context">What is reported to.</param>
        /// <param name="type">The type.</param>
        /// <param name="attribute">The attribute which was put on it.</param>
        /// <param name="markers">The types of the runtime half.</param>
        /// <param name="locator">What the asset of a <c>Resources</c> singleton is read by.</param>
        /// <param name="isMonoBehaviour">Whether the type is a <c>MonoBehaviour</c>.</param>
        /// <param name="isScriptableObject">Whether the type is a <c>ScriptableObject</c>.</param>
        /// <param name="where">Where the type was declared.</param>
        /// <returns>The target, or <c>null</c> where the type cannot have the singleton it asked for.</returns>
        private static SingletonTarget? FromAsset(GeneratorExecutionContext context, INamedTypeSymbol type, AttributeData attribute,
                                                  Markers markers, ResourcesAssetLocator locator,
                                                  bool isMonoBehaviour, bool isScriptableObject, Location where)
        {
            var name = Name(markers.AssetType!, attribute.ConstructorArguments[0]);
            var path = attribute.ConstructorArguments.Length > 1 ? attribute.ConstructorArguments[1].Value as string : null;

            if (!isMonoBehaviour && !isScriptableObject)
            {
                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.AssetTypeUnsupportedType, where, type.Name, name));
                return null;
            }

            if (string.Equals(name, "Resources", StringComparison.Ordinal))
            {
                if (path == null)
                {
                    // Nothing named the asset, so the project is read for the first asset which holds the type. The
                    // path is read here rather than at the place the singleton is used, because a path which changes
                    // when an asset is moved is a path which a compilation has to be told about.
                    if (locator.ProjectRoot == null)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.UnityProjectNotFound, where, type.Name));
                        return null;
                    }

                    path = locator.Find(type, isMonoBehaviour);
                    if (path == null)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.ResourceAssetNotFound, where, type.Name,
                                                                   isMonoBehaviour ? "prefab" : "asset file", locator.ProjectRoot));
                        return null;
                    }
                }

                return new SingletonTarget(type, isMonoBehaviour
                    ? SingletonInstance.ResourcesMonoBehaviour
                    : SingletonInstance.ResourcesScriptableObject)
                {
                    AssetPath = path
                };
            }

            // The two types are named by the code which is written, and they live in two assemblies of the package:
            // Addressables itself is one, and the status its loads answer with is in the resource manager which the
            // Addressables assembly is built on. A compilation of scripts which were written inside an assembly
            // definition has to name both of them, so which one is missing is worth saying.
            if (markers.Addressables == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.AddressablesNotReferenced, where, type.Name,
                                                           TypeNames.ADDRESSABLES));
                return null;
            }

            if (markers.AsyncOperationStatus == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.AddressablesNotReferenced, where, type.Name,
                                                           TypeNames.ASYNC_OPERATION_STATUS));
                return null;
            }

            if (path == null)
            {
                // An address is a name an Addressables catalog was told to know an asset by, and nothing in the project
                // being compiled says what those names are, so the address is asked for rather than looked for.
                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.AddressablePathRequired, where, type.Name));
                return null;
            }

            return new SingletonTarget(type, isMonoBehaviour
                ? SingletonInstance.AddressableMonoBehaviour
                : SingletonInstance.AddressableScriptableObject)
            {
                AssetPath = path
            };
        }

        /// <summary>
        /// Read a type which asked to be made by a creator.
        /// </summary>
        /// <param name="context">What is reported to.</param>
        /// <param name="type">The type.</param>
        /// <param name="attribute">The attribute which was put on it.</param>
        /// <param name="markers">The types of the runtime half.</param>
        /// <param name="arguments">What the arguments of the creator are read by.</param>
        /// <param name="where">Where the type was declared.</param>
        /// <returns>The target, or <c>null</c> where the type cannot have the singleton it asked for.</returns>
        private static SingletonTarget? FromCreator(GeneratorExecutionContext context, INamedTypeSymbol type, AttributeData attribute,
                                                    Markers markers, ArgumentRenderer arguments, Location where)
        {
            if (markers.ICreator == null) return null;

            if (attribute.ConstructorArguments[0].Value is not INamedTypeSymbol creator ||
                CreatorOf(creator, type, markers.ICreator) == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.CreatorDoesNotMakeTheType, where,
                                                           attribute.ConstructorArguments[0].Value is ITypeSymbol written ? written.Name : "?",
                                                           type.Name));
                return null;
            }

            var named = Arguments(attribute.ConstructorArguments);
            var chosen = Constructor(creator, type, named, arguments);
            if (chosen == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.CreatorConstructorMissing, where, creator.Name,
                                                           named.Count == 0
                                                               ? "none"
                                                               : string.Join(", ", named.Select(argument => argument.Type?.Name ?? "null"))));
                return null;
            }

            var target = new SingletonTarget(type, SingletonInstance.Creator)
            {
                Creator = creator,
                CreatorConstructor = chosen.Value.Constructor
            };

            target.CreatorConstructorArguments.AddRange(chosen.Value.Arguments);
            return target;
        }

        /// <summary>
        /// Read the interface of a creator which makes a type, which is the <c>ICreator&lt;T&gt;</c> of the named type
        /// whose type argument is the type itself.
        /// </summary>
        /// <param name="creator">The type which was named.</param>
        /// <param name="type">The type which asked for the singleton.</param>
        /// <param name="open">The <c>ICreator&lt;T&gt;</c> which the argument is read against.</param>
        /// <returns>The interface, or <c>null</c> where the named type does not make the type.</returns>
        private static INamedTypeSymbol? CreatorOf(INamedTypeSymbol creator, INamedTypeSymbol type, INamedTypeSymbol open)
        {
            foreach (var candidate in creator.AllInterfaces)
            {
                if (!SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, open)) continue;
                if (candidate.TypeArguments.Length != 1) continue;
                if (SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], type)) return candidate;
            }

            return null;
        }

        /// <summary>
        /// Read the arguments which a creator was named with.
        /// </summary>
        /// <param name="named">The arguments of the attribute.</param>
        /// <returns>The arguments, one to each parameter which is read for.</returns>
        private static List<TypedConstant> Arguments(ImmutableArray<TypedConstant> named)
        {
            var arguments = new List<TypedConstant>();
            if (named.Length < 2) return arguments;

            var rest = named[1];
            if (rest.Kind == TypedConstantKind.Array && !rest.IsNull)
            {
                // The constructor takes the arguments as a params array, and the language packs them into one.
                arguments.AddRange(rest.Values);
                return arguments;
            }

            // A compilation which did not pack them hands them over one by one, which is read the same way.
            for (var index = 1; index < named.Length; index++) arguments.Add(named[index]);
            return arguments;
        }

        /// <summary>
        /// Read the constructor of a creator which the arguments fit.
        /// </summary>
        /// <param name="creator">The creator.</param>
        /// <param name="type">The type which asked for the singleton, from inside which the constructor would be called.</param>
        /// <param name="arguments">The arguments which the creator was named with.</param>
        /// <param name="renderer">What an argument is read against a parameter by.</param>
        /// <returns>The constructor with the arguments, or <c>null</c> where there is none or more than one.</returns>
        private static (IMethodSymbol Constructor, List<TypedConstant> Arguments)? Constructor(
            INamedTypeSymbol creator, INamedTypeSymbol type, List<TypedConstant> arguments, ArgumentRenderer renderer)
        {
            IMethodSymbol? best = null;
            var cost = int.MaxValue;
            var ambiguous = false;

            foreach (var constructor in creator.InstanceConstructors)
            {
                if (constructor.Parameters.Length != arguments.Count) continue;
                if (!Accessible(constructor, type)) continue;

                var total = 0;
                var fits = true;
                for (var index = 0; index < arguments.Count; index++)
                {
                    if (renderer.TryFit(arguments[index], constructor.Parameters[index].Type, out var one)) total += one;
                    else { fits = false; break; }
                }

                if (!fits) continue;
                if (total < cost) { best = constructor; cost = total; ambiguous = false; }
                else if (total == cost) ambiguous = true;
            }

            return best == null || ambiguous ? null : (best, arguments);
        }

        /// <summary>
        /// Whether a constructor can be called from inside a type.
        /// </summary>
        /// <param name="constructor">The constructor.</param>
        /// <param name="from">The type which the call is written inside.</param>
        /// <returns>Whether it can be called.</returns>
        private static bool Accessible(IMethodSymbol constructor, INamedTypeSymbol from)
        {
            var own = SymbolEqualityComparer.Default.Equals(constructor.ContainingAssembly, from.ContainingAssembly);

            switch (constructor.DeclaredAccessibility)
            {
                case Microsoft.CodeAnalysis.Accessibility.Public:
                    return true;
                case Microsoft.CodeAnalysis.Accessibility.Internal:
                    return own;
                case Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal:
                    return own || Derives(from, constructor.ContainingType);
                case Microsoft.CodeAnalysis.Accessibility.Protected:
                    return Derives(from, constructor.ContainingType);
                case Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal:
                    return own && Derives(from, constructor.ContainingType);
                case Microsoft.CodeAnalysis.Accessibility.Private:
                    return SymbolEqualityComparer.Default.Equals(constructor.ContainingType, from);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Read the declaration which stops the generated half from being joined to the type, which is the declaration
        /// of the type itself or of a type which holds it.<para/>
        /// A type declared in more than one place has to be <c>partial</c> in every one of them, and a type declared
        /// inside another one can only be declared again inside that one, so the type which holds it has to be
        /// <c>partial</c> as well. Which of them it is is worth saying, because a nested type which is <c>partial</c>
        /// inside a type which is not is a mistake which reads as the nested one being wrong.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>The declaration which is not <c>partial</c>, or <c>null</c> where every one of them is.</returns>
        private static INamedTypeSymbol? NotPartial(INamedTypeSymbol type)
        {
            for (var current = type; current != null; current = current.ContainingType)
            {
                if (current.DeclaringSyntaxReferences.Length == 0) return current;

                foreach (var reference in current.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax() is not TypeDeclarationSyntax declaration) return current;
                    if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword)) return current;
                }
            }

            return null;
        }

        /// <summary>
        /// Read what makes a type one which a singleton cannot be made of.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>What makes it one, or <c>null</c> where it is a type a singleton can be made of.</returns>
        private static string? ReasonToRefuse(INamedTypeSymbol type)
        {
            if (type.IsStatic) return "it is static, so there is no instance of it to hold";
            if (type.IsAbstract) return "it is abstract, so no instance of it can be made";
            if (type.IsGenericType) return "it is generic, so one field could not hold the instance of each type argument of it";
            if (type.TypeKind != TypeKind.Class) return "it is not a class";

            for (var container = type.ContainingType; container != null; container = container.ContainingType)
            {
                if (container.IsGenericType) return "it is declared inside a generic type, so one field could not hold the instance of each type argument of that type";
            }

            return null;
        }

        /// <summary>
        /// Read the name of the enumeration member which an argument was written as.
        /// </summary>
        /// <param name="type">The enumeration.</param>
        /// <param name="argument">The argument.</param>
        /// <returns>The name of the member.</returns>
        private static string Name(INamedTypeSymbol type, TypedConstant argument)
        {
            if (argument.Value is not int value) return "?";
            if (argument.Type is INamedTypeSymbol written && written.TypeKind == TypeKind.Enum) type = written;

            foreach (var member in type.GetMembers())
            {
                if (member is IFieldSymbol { HasConstantValue: true, ConstantValue: int constant } field && constant == value)
                {
                    return field.Name;
                }
            }

            return "?";
        }

        /// <summary>
        /// Read off a type the first attribute which is the given one.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="attribute">The attribute.</param>
        /// <returns>The attribute, or <c>null</c> where the type does not carry it.</returns>
        private static AttributeData? Find(INamedTypeSymbol type, INamedTypeSymbol attribute)
        {
            foreach (var candidate in type.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute)) return candidate;
            }

            return null;
        }

        /// <summary>
        /// Where a type was declared.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>The place, or nowhere where the type has none.</returns>
        private static Location Where(INamedTypeSymbol type)
        {
            foreach (var location in type.Locations)
            {
                if (location.IsInSource) return location;
            }

            return Location.None;
        }

        /// <summary>
        /// The name which the file of a singleton is added under.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="taken">The names which were added already.</param>
        /// <returns>The name.</returns>
        private static string Hint(INamedTypeSymbol type, HashSet<string> taken)
        {
            var builder = new StringBuilder();
            if (!type.ContainingNamespace.IsGlobalNamespace)
            {
                builder.Append(type.ContainingNamespace.ToDisplayString()).Append('.');
            }

            var containers = new List<string>();
            for (var current = type; current != null; current = current.ContainingType) containers.Add(current.Name);
            containers.Reverse();
            builder.Append(string.Join(".", containers)).Append(".Singleton.g.cs");

            var hint = builder.ToString();
            var unique = hint;
            for (var attempt = 2; !taken.Add(unique); attempt++) unique = hint + attempt;
            return unique;
        }

        /// <summary>
        /// Whether a type is another type, or derives from it.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="baseType">The type which is looked for.</param>
        /// <returns>Whether it is.</returns>
        private static bool Derives(INamedTypeSymbol type, INamedTypeSymbol baseType)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType)) return true;
            }

            return false;
        }
    }

    /// <summary>
    /// The types of the runtime half of the package, read off a compilation once.
    /// </summary>
    internal sealed class Markers
    {
        /// <summary>
        /// Read them off a compilation.
        /// </summary>
        /// <param name="compilation">The compilation.</param>
        public Markers(Compilation compilation)
        {
            MonoBehaviour = compilation.GetTypeByMetadataName(TypeNames.MONO_BEHAVIOUR);
            ScriptableObject = compilation.GetTypeByMetadataName(TypeNames.SCRIPTABLE_OBJECT);
            AssetType = compilation.GetTypeByMetadataName(TypeNames.ASSET_TYPE);
            Type = compilation.GetTypeByMetadataName(TypeNames.SYSTEM_TYPE);
            ICreator = compilation.GetTypeByMetadataName(TypeNames.CREATOR);
            Persistent = compilation.GetTypeByMetadataName(TypeNames.PERSISTENT_ATTRIBUTE);
            Invisible = compilation.GetTypeByMetadataName(TypeNames.INVISIBLE_ATTRIBUTE);
            Addressables = compilation.GetTypeByMetadataName(TypeNames.ADDRESSABLES);
            AsyncOperationStatus = compilation.GetTypeByMetadataName(TypeNames.ASYNC_OPERATION_STATUS);
        }

        /// <summary>What a <c>MonoBehaviour</c> is.</summary>
        public INamedTypeSymbol? MonoBehaviour { get; }

        /// <summary>What a <c>ScriptableObject</c> is.</summary>
        public INamedTypeSymbol? ScriptableObject { get; }

        /// <summary>Where an asset is loaded from.</summary>
        public INamedTypeSymbol? AssetType { get; }

        /// <summary>What a creator is named by.</summary>
        public INamedTypeSymbol? Type { get; }

        /// <summary>What makes the instance of a singleton which a creator makes.</summary>
        public INamedTypeSymbol? ICreator { get; }

        /// <summary>Whether the object is kept across scene loads.</summary>
        public INamedTypeSymbol? Persistent { get; }

        /// <summary>Whether the object is hidden.</summary>
        public INamedTypeSymbol? Invisible { get; }

        /// <summary>What an Addressables asset is loaded with.</summary>
        public INamedTypeSymbol? Addressables { get; }

        /// <summary>What an Addressables load is read by.</summary>
        public INamedTypeSymbol? AsyncOperationStatus { get; }

        /// <summary>
        /// Whether a type is a <c>MonoBehaviour</c>.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>Whether it is.</returns>
        public bool IsMonoBehaviour(INamedTypeSymbol type) => Derives(type, MonoBehaviour);

        /// <summary>
        /// Whether a type is a <c>ScriptableObject</c>.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>Whether it is.</returns>
        public bool IsScriptableObject(INamedTypeSymbol type) => Derives(type, ScriptableObject);

        /// <summary>
        /// Whether a type carries an attribute.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="attribute">The attribute, which may be one the compilation does not hold.</param>
        /// <returns>Whether it carries it.</returns>
        public bool Has(INamedTypeSymbol type, INamedTypeSymbol? attribute)
        {
            if (attribute == null) return false;

            foreach (var candidate in type.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether a type is another type, or derives from it.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="baseType">The type which is looked for.</param>
        /// <returns>Whether it is.</returns>
        private static bool Derives(INamedTypeSymbol type, INamedTypeSymbol? baseType)
        {
            if (baseType == null) return false;

            for (var current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType)) return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Which declarations of a compilation carry attributes, which is every type a singleton could have been asked of.
    /// </summary>
    /// <remarks>
    /// What is collected here is collected by syntax alone, because a receiver is handed the tree and not the
    /// compilation: whether a declaration is one which the attribute was put on is read afterwards, off the symbol.
    /// </remarks>
    internal sealed class SingletonSyntaxReceiver : ISyntaxReceiver
    {
        /// <summary>The declarations which carry attribute lists.</summary>
        public List<TypeDeclarationSyntax> Types { get; } = new();

        /// <inheritdoc/>
        public void OnVisitSyntaxNode(SyntaxNode node)
        {
            if (node is TypeDeclarationSyntax { AttributeLists.Count: > 0 } declaration) Types.Add(declaration);
        }
    }
}