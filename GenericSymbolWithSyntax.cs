using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Monkeymoto.GeneratorUtils
{
    /// <summary>
    /// Represents a generic <see cref="ISymbol"/> and an associated <see cref="SyntaxNode"/>.
    /// </summary>
    public readonly struct GenericSymbolWithSyntax : IEquatable<GenericSymbolWithSyntax>
    {
        private const string InvalidMethodSymbolError = "Symbol must be a generic method.";
        private const string InvalidNamedTypeSymbolError = "Symbol must be a generic named type.";

        /// <summary>
        /// Represents whether <see cref="Symbol">Symbol</see> is a closed generic type or closed generic method.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> if <see cref="Symbol">Symbol</see> has no unsubstituted type arguments; otherwise,
        /// <see langword="false"/>.
        /// </returns>
        public readonly bool IsClosedTypeOrMethod = false;
        /// <summary>
        /// Represents the <see cref="SyntaxNode"/> associated with <see cref="Symbol">Symbol</see>.
        /// </summary>
        public readonly SyntaxNode Node;
        /// <summary>
        /// Represents the <see cref="ISymbol"/> associated with <see cref="Node">Node</see>.
        /// </summary>
        public readonly ISymbol Symbol;
        /// <summary>
        /// Represents the type arguments for this <see cref="Symbol">Symbol</see>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If <see cref="IsClosedTypeOrMethod">IsClosedTypeOrMethod</see> is <see langword="false"/>, then one or more of these
        /// type arguments is an <see cref="ITypeParameterSymbol"/> or an open generic type. Otherwise, these values represent
        /// the closed type arguments.
        /// </para>
        /// </remarks>
        public readonly ImmutableArray<ITypeSymbol> TypeArguments;

        public static bool operator ==(GenericSymbolWithSyntax left, GenericSymbolWithSyntax right) =>
            left.Equals(right);
        public static bool operator !=(GenericSymbolWithSyntax left, GenericSymbolWithSyntax right) => !(left == right);

        private static ISymbol CheckSymbol
        (
            IMethodSymbol methodSymbol,
            [CallerArgumentExpression(nameof(methodSymbol))] string? paramName = null
        )
        {
            ArgumentNullExceptionHelper.ThrowIfNull(methodSymbol, paramName);
            if (!methodSymbol.IsGenericMethod)
            {
                throw new ArgumentException(InvalidMethodSymbolError, paramName);
            }
            return methodSymbol;
        }

        private static ISymbol CheckSymbol
        (
            INamedTypeSymbol namedTypeSymbol,
            [CallerArgumentExpression(nameof(namedTypeSymbol))] string? paramName = null
        )
        {
            ArgumentNullExceptionHelper.ThrowIfNull(namedTypeSymbol, paramName);
            if (!namedTypeSymbol.IsGenericType)
            {
                throw new ArgumentException(InvalidNamedTypeSymbolError, paramName);
            }
            return namedTypeSymbol;
        }

        private static SyntaxNode CheckSyntax
        (
            SyntaxNode syntaxNode,
            [CallerArgumentExpression(nameof(syntaxNode))] string? paramName = null
        )
        {
            ArgumentNullExceptionHelper.ThrowIfNull(syntaxNode, paramName);
            return syntaxNode;
        }

        internal static GenericSymbolWithSyntax? FromSyntaxNodeInternal
        (
            SyntaxNode syntaxNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken
        )
        {
            var symbol = syntaxNode switch
            {
                GenericNameSyntax genericNameSyntax => GetSymbol(genericNameSyntax, semanticModel, cancellationToken),
                IdentifierNameSyntax or InvocationExpressionSyntax => GetSymbol(syntaxNode, semanticModel, cancellationToken),
                _ => null
            };
            return symbol switch
            {
                null or ISymbol { IsDefinition: true } => null,
                IMethodSymbol methodSymbol => methodSymbol.IsGenericMethod ? new(methodSymbol, syntaxNode) : null,
                INamedTypeSymbol namedTypeSymbol => namedTypeSymbol.IsGenericType ? new(namedTypeSymbol, syntaxNode) : null,
                _ => null
            };
        }

        /// <summary>
        /// Gets a new instance representing the given syntax node and its associated generic symbol.
        /// </summary>
        /// <param name="syntaxNode">The syntax node to associate with a generic symbol.</param>
        /// <param name="semanticModel">
        /// The <see cref="SemanticModel"/> used to get a <see cref="ISymbol">symbolic reference</see> to
        /// <paramref name="syntaxNode"/>.
        /// </param>
        /// <param name="cancellationToken">
        /// The <see cref="CancellationToken"/> that will be observed while retrieving symbolic info from the
        /// <paramref name="semanticModel"/>.
        /// </param>
        /// <returns>
        /// <see langword="null"/>, if <paramref name="semanticModel"/> does not resolve <paramref name="syntaxNode"/> to a
        /// generic symbol or the symbol is the <see cref="ISymbol.OriginalDefinition">original symbol definition</see>;
        /// otherwise, the new instance.
        /// </returns>
        public static GenericSymbolWithSyntax? FromSyntaxNode
        (
            SyntaxNode syntaxNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken
        )
        {
            ArgumentNullExceptionHelper.ThrowIfNull(syntaxNode);
            ArgumentNullExceptionHelper.ThrowIfNull(semanticModel);
            return FromSyntaxNodeInternal(syntaxNode, semanticModel, cancellationToken);
        }

        private static ISymbol? GetSymbol
        (
            GenericNameSyntax genericNameSyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken
        )
        {
            if ((genericNameSyntax.Parent is InvocationExpressionSyntax) ||
                ((genericNameSyntax.Parent is MemberAccessExpressionSyntax memberAccessExpressionSyntax) &&
                (memberAccessExpressionSyntax.Parent is InvocationExpressionSyntax)))
            {
                // generic method invocations with an explicit type argument list produce a GenericNameSyntax node
                // this node is already added to the tree via the (grand)parent InvocationExpressionSyntax node
                return null;
            }
            return semanticModel.GetSymbolInfo(genericNameSyntax, cancellationToken).Symbol;
        }

        private static ISymbol? GetSymbol
        (
            SyntaxNode syntaxNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken
        )
        {
            var operation = semanticModel.GetOperation(syntaxNode, cancellationToken);
            return operation switch
            {
                IInvocationOperation invocationOperation => invocationOperation.TargetMethod,
                IMethodReferenceOperation methodReferenceOperation => methodReferenceOperation.Method,
                _ => null
            };
        }

        internal static bool IsOpenTypeOrMethodSymbol(ISymbol symbol)
        {
            return symbol switch
            {
                ITypeParameterSymbol => true,
                IMethodSymbol methodSymbol => methodSymbol.TypeArguments.Any(IsOpenTypeOrMethodSymbol),
                INamedTypeSymbol namedTypeSymbol => namedTypeSymbol.TypeArguments.Any(IsOpenTypeOrMethodSymbol),
                _ => false
            };
        }

        internal GenericSymbolWithSyntax(ISymbol symbol, SyntaxNode node)
        {
            IsClosedTypeOrMethod = !IsOpenTypeOrMethodSymbol(symbol);
            Node = node;
            Symbol = symbol;
            TypeArguments = symbol switch
            {
                IMethodSymbol sym => sym.TypeArguments,
                INamedTypeSymbol sym => sym.TypeArguments,
                _ => throw new UnreachableException()
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GenericSymbolWithSyntax"/> class with the specified symbol and syntax.
        /// </summary>
        /// <param name="methodSymbol">The symbol to associate with <paramref name="identifierNameSyntax"/>.</param>
        /// <param name="identifierNameSyntax">The syntax to associate with <paramref name="methodSymbol"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="methodSymbol"/> or <paramref name="identifierNameSyntax"/> was <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="methodSymbol"/> was not a generic method.
        /// </exception>
        public GenericSymbolWithSyntax(IMethodSymbol methodSymbol, IdentifierNameSyntax identifierNameSyntax) :
            this(CheckSymbol(methodSymbol), CheckSyntax(identifierNameSyntax))
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="GenericSymbolWithSyntax"/> class with the specified symbol and syntax.
        /// </summary>
        /// <param name="methodSymbol">The symbol to associate with <paramref name="invocationExpressionSyntax"/>.</param>
        /// <param name="invocationExpressionSyntax">The syntax to associate with <paramref name="methodSymbol"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="methodSymbol"/> or <paramref name="invocationExpressionSyntax"/> was <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="methodSymbol"/> was not a generic method.
        /// </exception>
        public GenericSymbolWithSyntax(IMethodSymbol methodSymbol, InvocationExpressionSyntax invocationExpressionSyntax) :
            this(CheckSymbol(methodSymbol), CheckSyntax(invocationExpressionSyntax))
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="GenericSymbolWithSyntax"/> class with the specified symbol and syntax.
        /// </summary>
        /// <param name="namedTypeSymbol">The symbol to associate with <paramref name="genericNameSyntax"/>.</param>
        /// <param name="genericNameSyntax">The syntax to associate with <paramref name="namedTypeSymbol"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="namedTypeSymbol"/> or <paramref name="genericNameSyntax"/> was <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="namedTypeSymbol"/> was not a generic named type.
        /// </exception>
        public GenericSymbolWithSyntax(INamedTypeSymbol namedTypeSymbol, GenericNameSyntax genericNameSyntax) :
            this(CheckSymbol(namedTypeSymbol), CheckSyntax(genericNameSyntax))
        { }

        public override bool Equals(object? obj)
        {
            return obj is GenericSymbolWithSyntax other && Equals(other);
        }

        public bool Equals(GenericSymbolWithSyntax other)
        {
            return SymbolEqualityComparer.Default.Equals(Symbol, other.Symbol) && Node.IsEquivalentTo(other.Node);
        }

        /// <summary>
        /// Gets the hash code for this <see cref="GenericSymbolWithSyntax"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Borrowed from <see href="https://stackoverflow.com/a/1646913">Quick and Simple Hash Code Combinations - Stack
        /// Overflow</see> answer by user <see href="https://stackoverflow.com/users/22656/jon-skeet">Jon Skeet</see>, licensed
        /// under <see href="https://creativecommons.org/licenses/by-sa/2.5/">CC BY-SA 2.5</see>. Changes have been made to
        /// match the fields of this class.
        /// </para>
        /// </remarks>
        /// <returns>The hash value generated for this <see cref="GenericSymbolWithSyntax"/>.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(Symbol);
                hash = hash * 31 + Node.GetHashCode();
                return hash;
            }
        }
    }
}
