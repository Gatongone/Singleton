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
    /// property. What cannot be - the <c>_EnterTree</c> which tells the field which node the engine made, and the
    /// <c>_ExitTree</c> which clears it - is woven into the assembly after it was compiled, by the weaver of this
    /// package.<para/>
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
            var locator = new GodotAssetLocator(GodotAssetLocator.FindProjectRoot(compilation, context.AdditionalFiles));
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

                target.Hides = Singleton(type.BaseType, attribute);

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
            if (markers.IsNode(type)) return;

            // What the attribute asks of is the place of a node in the scene tree, so on anything else it asks for
            // something which cannot be done, and saying so here is saying it at the place the attribute was written
            // rather than in an assembly which was already compiled.
            if (markers.Has(type, markers.Persistent))
            {
                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.PersistentRequiresNode, Where(type), type.Name));
            }
        }

        /// <summary>
        /// Read what a type asked for, and answer with what the half of it is written from.
        /// </summary>
        /// <param name="context">What is reported to.</param>
        /// <param name="type">The type which asked for the singleton.</param>
        /// <param name="attribute">The attribute which was put on it.</param>
        /// <param name="markers">The types of the runtime half.</param>
        /// <param name="arguments">What the arguments of a creator are read by.</param>
        /// <param name="locator">What the asset of a singleton is read by.</param>
        /// <returns>The target, or <c>null</c> where the type cannot have the singleton it asked for.</returns>
        private static SingletonTarget? Read(GeneratorExecutionContext context, INamedTypeSymbol type, AttributeData attribute,
                                             Markers markers, ArgumentRenderer arguments, GodotAssetLocator locator)
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
                if (member.Name is not ("Instance" or "s_Instance" or "TryGetInstance")) continue;

                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.MemberConflict, where, type.Name, member.Name));
                return null;
            }

            var isNode = markers.IsNode(type);
            var isResource = markers.IsResource(type);

            // A reusable singleton is given a search of the scene tree, and only a node is in one. A Resource is found
            // by a path rather than by a walk, and what keeps it the same object is the loader which remembers it, so
            // the attribute is refused rather than written and left to find nothing.
            if (markers.Has(type, markers.Reusable) && !isNode)
            {
                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.ReusableRequiresNode, where, type.Name));
                return null;
            }

            var read = attribute.ConstructorArguments.Length == 0
                ? Default(type, isNode, context, where)
                : FromArguments(context, type, attribute, markers, arguments, locator, isNode, isResource, where);

            if (read == null) return null;

            read.Persistent = markers.Has(type, markers.Persistent);
            read.Reusable = markers.Has(type, markers.Reusable);
            return read;
        }

        /// <summary>
        /// Read a type which asked for a singleton and named nothing, whose instance Godot makes or its own
        /// constructor makes.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="isNode">Whether the type is a <c>Node</c>.</param>
        /// <param name="context">What is reported to.</param>
        /// <param name="where">Where the type was declared.</param>
        /// <returns>The target, or <c>null</c> where the type cannot have the singleton it asked for.</returns>
        private static SingletonTarget? Default(INamedTypeSymbol type, bool isNode,
                                                GeneratorExecutionContext context, Location where)
        {
            if (isNode) return new SingletonTarget(type, SingletonInstance.Node);

            // A type which is in no scene tree is made by its own constructor, which the generated property calls from
            // inside the type itself and which therefore may be of any accessibility - but has to be there, and is not
            // there if the author wrote a constructor which takes arguments and no other.
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
        /// <param name="type">The type which asked for the singleton.</param>
        /// <param name="attribute">The attribute which was put on it.</param>
        /// <param name="markers">The types of the runtime half.</param>
        /// <param name="arguments">What the arguments of a creator are read by.</param>
        /// <param name="locator">What the asset of a singleton is read by.</param>
        /// <param name="isNode">Whether the type is a <c>Node</c>.</param>
        /// <param name="isResource">Whether the type is a <c>Resource</c>.</param>
        /// <param name="where">Where the type was declared.</param>
        /// <returns>The target, or <c>null</c> where the type cannot have the singleton it asked for.</returns>
        private static SingletonTarget? FromArguments(GeneratorExecutionContext context, INamedTypeSymbol type, AttributeData attribute,
                                                      Markers markers, ArgumentRenderer arguments, GodotAssetLocator locator,
                                                      bool isNode, bool isResource, Location where)
        {
            var constructor = attribute.ConstructorArguments;
            var first = constructor[0];

            if (first.Type != null && markers.AssetType != null && SymbolEqualityComparer.Default.Equals(first.Type, markers.AssetType))
            {
                return FromAsset(context, type, attribute, markers, locator, isNode, isResource, where);
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
        /// <param name="type">The type which asked for the singleton.</param>
        /// <param name="attribute">The attribute which was put on it.</param>
        /// <param name="markers">The types of the runtime half.</param>
        /// <param name="locator">What the asset of a singleton is read by.</param>
        /// <param name="isNode">Whether the type is a <c>Node</c>.</param>
        /// <param name="isResource">Whether the type is a <c>Resource</c>.</param>
        /// <param name="where">Where the type was declared.</param>
        /// <returns>The target, or <c>null</c> where the type cannot have the singleton it asked for.</returns>
        private static SingletonTarget? FromAsset(GeneratorExecutionContext context, INamedTypeSymbol type, AttributeData attribute,
                                                  Markers markers, GodotAssetLocator locator,
                                                  bool isNode, bool isResource, Location where)
        {
            var name = Name(markers.AssetType!, attribute.ConstructorArguments[0]);
            var path = attribute.ConstructorArguments.Length > 1 ? attribute.ConstructorArguments[1].Value as string : null;

            if (!isNode && !isResource)
            {
                context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.AssetTypeUnsupportedType, where, type.Name, name));
                return null;
            }

            if (path == null)
            {
                if (locator.ProjectRoot == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.GodotProjectNotFound, where, type.Name));
                    return null;
                }

                // A script which is not a file of the project is one no scene of the project could name, and the walk
                // would answer with nothing for a reason which reads as a project which holds no asset of the type.
                if (!locator.IsInProject(type))
                {
                    context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.AssetPathRequired, where, type.Name, name));
                    return null;
                }

                // Nothing named the asset, so the project is read for the first asset which names the script of the
                // type. The path is read here rather than at the place the singleton is used, because a path which
                // changes when an asset is moved is a path which a compilation has to be told about.
                path = locator.Find(type, isNode);
                if (path == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(SingletonDiagnostics.ResourceAssetNotFound, where, type.Name,
                                                               isNode ? "scene" : "resource", locator.ProjectRoot));
                    return null;
                }
            }

            return new SingletonTarget(type, isNode ? SingletonInstance.AssetNode : SingletonInstance.AssetResource)
            {
                AssetPath = path
            };
        }

        /// <summary>
        /// Read a type which asked to be made by a creator.
        /// </summary>
        /// <param name="context">What is reported to.</param>
        /// <param name="type">The type which asked for the singleton.</param>
        /// <param name="attribute">The attribute which was put on it.</param>
        /// <param name="markers">The types of the runtime half.</param>
        /// <param name="arguments">What the arguments of a creator are read by.</param>
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
        /// Whether a type, or one of the types it derives from, asked for a singleton.
        /// </summary>
        /// <remarks>
        /// The half of a singleton is declared beside the type it belongs to, so the half of a type which derives from
        /// one is declared beside the half of the type it derives from, and what the two declare is named alike.
        /// </remarks>
        /// <param name="type">The type it derives from, or <c>null</c> where it derives from none.</param>
        /// <param name="attribute">The attribute which asks for a singleton.</param>
        /// <returns>Whether one of them did.</returns>
        private static bool Singleton(INamedTypeSymbol? type, INamedTypeSymbol attribute)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (Find(current, attribute) != null) return true;
            }

            return false;
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
            Node = compilation.GetTypeByMetadataName(TypeNames.NODE);
            Resource = compilation.GetTypeByMetadataName(TypeNames.RESOURCE);
            AssetType = compilation.GetTypeByMetadataName(TypeNames.ASSET_TYPE);
            Type = compilation.GetTypeByMetadataName(TypeNames.SYSTEM_TYPE);
            ICreator = compilation.GetTypeByMetadataName(TypeNames.CREATOR);
            Persistent = compilation.GetTypeByMetadataName(TypeNames.PERSISTENT_ATTRIBUTE);
            Reusable = compilation.GetTypeByMetadataName(TypeNames.REUSABLE_ATTRIBUTE);
        }

        /// <summary>What a <c>Node</c> is.</summary>
        public INamedTypeSymbol? Node { get; }

        /// <summary>What a <c>Resource</c> is.</summary>
        public INamedTypeSymbol? Resource { get; }

        /// <summary>Where an asset is loaded from.</summary>
        public INamedTypeSymbol? AssetType { get; }

        /// <summary>What a creator is named by.</summary>
        public INamedTypeSymbol? Type { get; }

        /// <summary>What makes the instance of a singleton which a creator makes.</summary>
        public INamedTypeSymbol? ICreator { get; }

        /// <summary>Whether the node is kept across a change of scene.</summary>
        public INamedTypeSymbol? Persistent { get; }

        /// <summary>Whether the instance may be the node which is already in the scene tree.</summary>
        public INamedTypeSymbol? Reusable { get; }

        /// <summary>
        /// Whether a type is a <c>Node</c>.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>Whether it is.</returns>
        public bool IsNode(INamedTypeSymbol type) => Derives(type, Node);

        /// <summary>
        /// Whether a type is a <c>Resource</c>.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>Whether it is.</returns>
        public bool IsResource(INamedTypeSymbol type) => Derives(type, Resource);

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
