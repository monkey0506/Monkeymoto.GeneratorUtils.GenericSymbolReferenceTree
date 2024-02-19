using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Monkeymoto.GeneratorUtils
{
    /// <summary>
    /// Represents a collection of closed generic symbols for use with an incremental source generator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class discovers generic types and generic methods in your compilation and keeps <see cref="ISymbol">symbolic
    /// references</see> to them and the <see cref="SyntaxNode">syntax</see> that produced those references. This may create
    /// pressure on your compilation in terms of memory or time spent discovering those symbols.
    /// </para><para>
    /// Open generic symbols are resolved to closed generic symbols by calling <see cref="GetBranch">GetBranch</see> or
    /// <see cref="GetBranchesBySymbol">GetBranchesBySymbol</see>.
    /// </para>
    /// </remarks>
    public sealed class GenericSymbolReferenceTree
    {
        private readonly Dictionary<GenericSymbolReference, HashSet<GenericSymbolReference>> closedBranches = [];
        private readonly HashSet<GenericSymbolReference> openBranches = [];

        /// <summary>
        /// Creates a new tree from an incremental generator initialization context.
        /// </summary>
        /// <param name="context">The context used to create the new tree.</param>
        /// <returns>An <see cref="IncrementalValueProvider{TValue}"/> which provides the newly created tree.</returns>
        public static IncrementalValueProvider<GenericSymbolReferenceTree> FromIncrementalGeneratorInitializationContext
        (
            IncrementalGeneratorInitializationContext context
        )
        {
            return FromIncrementalGeneratorInitializationContext(context, []);
        }

        /// <inheritdoc cref="FromIncrementalGeneratorInitializationContext(IncrementalGeneratorInitializationContext)"/>
        /// <param name="excludePaths">
        /// File paths to exclude from the tree, for example files your generator added to the compilation that contain only
        /// definitions.
        /// </param>
        public static IncrementalValueProvider<GenericSymbolReferenceTree> FromIncrementalGeneratorInitializationContext
        (
            IncrementalGeneratorInitializationContext context,
            params string[] excludePaths
        )
        {
            static bool Predicate(SyntaxNode node, CancellationToken _) =>
                node is GenericNameSyntax or InvocationExpressionSyntax or IdentifierNameSyntax;

            bool PredicateWithExclusions(SyntaxNode node, CancellationToken cancellationToken)
            {
                if (excludePaths.Any(x => node.SyntaxTree.FilePath == x))
                {
                    return false;
                }
                return Predicate(node, cancellationToken);
            }

            return context.SyntaxProvider.CreateSyntaxProvider
            (
                excludePaths.Length == 0 ? Predicate : PredicateWithExclusions,
                static (context, cancellationToken) =>
                    GenericSymbolReference.FromSyntaxNodeInternal(context.Node, context.SemanticModel, cancellationToken)
            ).Collect().Select
            (
                static (symbolsReference, cancellationToken) =>
                    new GenericSymbolReferenceTree(symbolsReference, cancellationToken)
            );
        }

        private GenericSymbolReferenceTree
        (
            ImmutableArray<GenericSymbolReference?> symbolsReference,
            CancellationToken cancellationToken
        )
        {
            foreach (var symbolReference in symbolsReference)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (symbolReference?.IsClosedTypeOrMethod)
                {
                    case true:
                        closedBranches[symbolReference.Value] = [symbolReference.Value];
                        break;
                    case false:
                        _ = openBranches.Add(symbolReference.Value);
                        break;
                    case null:
                    default:
                        break;
                }
            }
        }

        /// <summary>
        /// Returns a collection which represents the closed generic symbols associated with the given symbol and syntax node.
        /// </summary>
        /// <param name="symbolReference">The generic symbol and syntax node to find in the tree.</param>
        /// <param name="cancellationToken">
        /// The <see cref="CancellationToken"/> that will be observed while searching the tree.
        /// </param>
        /// <returns>
        /// A collection representing the closed generic symbols associated with the given symbol after type substitution. Each
        /// item in the returned collection represents the same syntax node as <paramref name="symbolReference"/>.
        /// </returns>
        /// <seealso cref="GetBranchesBySymbol"/>
        public IEnumerable<GenericSymbolReference> GetBranch
        (
            GenericSymbolReference symbolReference,
            CancellationToken cancellationToken
        )
        {
            if (closedBranches.TryGetValue(symbolReference, out var branch))
            {
                // we have already looked up the closed branches for this symbol/node before
                return branch;
            }
            if (!openBranches.Remove(symbolReference))
            {
                // an unknown symbol/node was found - tree is incomplete or `symbolReference` is `default` constructed
                // we don't want to force a compilation failure here, so the best we can do is report no symbols found
                branch = [];
                closedBranches[symbolReference] = branch;
                return branch;
            }
            // get a list of all possible type argument lists
            var candidateArgumentLists = new List<List<ITypeSymbol>>(symbolReference.TypeArguments.Length);
            for (int i = 0; i < symbolReference.TypeArguments.Length; ++i)
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidateArgumentLists.Add([]);
                var typeArgument = symbolReference.TypeArguments[i];
                if (GenericSymbolReference.IsOpenTypeOrMethodSymbol(typeArgument))
                {
                    // typeArgument is either a type parameter of some other type (`T` from an enclosing `Foo<T>`) OR
                    // it is an open generic type (`Foo<T>`), discover all possible substitutions
                    candidateArgumentLists[i].AddRange
                    (
                        typeArgument switch
                        {
                            ITypeParameterSymbol typeParameter =>
                                GetBranchesBySymbol(typeParameter.ContainingSymbol, cancellationToken)
                                    .Select(x => x.TypeArguments[typeParameter.Ordinal]),
                            _ => GetBranchesBySymbol(typeArgument, cancellationToken).Select(x => (ITypeSymbol)x.Symbol)
                        }
                    );
                }
                else
                {
                    // typeArgument is a closed type (non-generic, or closed generic type)
                    candidateArgumentLists[i].Add(typeArgument);
                }
            }
            IMethodSymbol? methodSymbol = null;
            INamedTypeSymbol? namedTypeSymbol = null;
            Func<ITypeSymbol[], ISymbol>? construct = null;
            switch (symbolReference.Symbol)
            {
                case IMethodSymbol symbol:
                    methodSymbol = symbol;
                    construct = symbol.OriginalDefinition.Construct;
                    break;
                case INamedTypeSymbol symbol:
                    namedTypeSymbol = symbol;
                    construct = symbol.OriginalDefinition.Construct;
                    break;
                default:
                    throw new UnreachableException();
            }
            // construct closed symbols using every combination of the arguments we discovered
            var constructedSymbols = new List<ISymbol>();
            foreach (var closedArgumentList in candidateArgumentLists.CartesianProduct())
            {
                constructedSymbols.Add(construct(closedArgumentList.ToArray()));
            }
            branch = [];
            foreach (var constructedSymbol in constructedSymbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = branch.Add(new(constructedSymbol, symbolReference.SemanticModel, symbolReference.Node));
            }
            closedBranches[symbolReference] = branch;
            return branch.ToImmutableArray(); // ensure returned value can't mutate the tree
        }

        /// <summary>
        /// Returns a collection of all branches in the tree that match the given symbol.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If <paramref name="symbol"/> is an open generic symbol, this method will discover all branches that match
        /// <paramref name="symbol"/> after type substitutions. For example, if <paramref name="symbol"/> is the
        /// <see cref="ISymbol.OriginalDefinition">original symbol definition</see>, this method will discover all branches that
        /// share the same original symbol.
        /// </para><para>
        /// If <paramref name="symbol"/> is a closed generic symbol, then the returned collection will only represent those
        /// syntax nodes which reference this closed symbol.
        /// </para>
        /// </remarks>
        /// <param name="symbol">The generic symbol to find in the tree.</param>
        /// <param name="cancellationToken">
        /// The <see cref="CancellationToken"/> that will be observed while searching the tree.
        /// </param>
        /// <returns>
        /// A flattened collection of all branches in the tree that match <paramref name="symbol"/>, regardless of the syntax
        /// node. The returned collection will only contain closed generic symbols.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="symbol"/> was <see langword="null"/>.</exception>
        /// <seealso cref="GetBranch"/>
        public IEnumerable<GenericSymbolReference> GetBranchesBySymbol
        (
            ISymbol symbol,
            CancellationToken cancellationToken
        )
        {
            ArgumentNullExceptionHelper.ThrowIfNull(symbol);

            bool Match(GenericSymbolReference x) => symbol.IsDefinition ?
                SymbolEqualityComparer.Default.Equals(symbol, x.Symbol.OriginalDefinition) :
                SymbolEqualityComparer.Default.Equals(symbol, x.Symbol);

            var branches = new HashSet<GenericSymbolReference>();
            // the tree branches may change during iteration, make a copy of what we're searching for before iterating
            var searchBranches = closedBranches.Keys.Where(Match).ToImmutableArray();
            foreach (var searchBranch in searchBranches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                branches.UnionWith(closedBranches[searchBranch]);
            }
            searchBranches = openBranches.Where(Match).ToImmutableArray();
            foreach (var searchBranch in searchBranches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                branches.UnionWith(GetBranch(searchBranch, cancellationToken));
            }
            return branches;
        }
    }
}
