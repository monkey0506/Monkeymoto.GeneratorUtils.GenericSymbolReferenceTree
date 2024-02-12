# GenericSymbolWithSyntaxTree

C# incremental generator utility to produce a tree of closed generic symbols
used in your project's compilation, each paired with a syntax node.

## How to use this project

This project is designed to be used with a C# incremental generator to discover
closed generic symbols that are referenced in your project's compilation.
Accordingly, the first step in using this project is to [add the necessary
references to your `csproj` file](https://github.com/dotnet/roslyn/discussions/47517)[^1].

[^1]: This project will be built and published as a NuGet package. This README
should then be updated with relevant package info.

Once this project has been added to your incremental generator, you can then
use the `GenericSymbolWithSyntaxTree.FromIncrementalGeneratorInitializationContext`
method to get an `IncrementalValueProvider` which gives you access to the tree.
For example:

````C#
using Microsoft.CodeAnalysis;

namespace MyProjectGenerator
{
    [Generator]
    internal class MyGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var symbolTree = Monkeymoto.GeneratorUtils.GenericSymbolWithSyntaxTree.FromIncrementalGeneratorInitializationContext(context);
            var symbolToFind = context.CompilationProvider.Select(static (compilation, _) =>
            {
                return compilation.GetTypeByMetadataName("MyProject.MyGenericType`1")!;
            });
            var symbols = symbolTree.Combine(symbolToFind).Select(static (x, cancellationToken) =>
            {
                var (symbolTree, symbolToFind) = x;
                return symbolTree.GetBranchesBySymbol(symbolToFind, cancellationToken);
            });
            // `symbols` is an `IncrementalValueProvider<IEnumerable<GenericSymbolWithSyntax>>`
            // this represents each *closed* instance of `MyProject.MyGenericType<T>` in the compilation
            // i.e., `MyProject.MyGenericType<int>`, `MyProject.MyGenericType<double>`, etc.
        }
    }
}
````

The methods `GetBranch` and `GetBranchesBySymbol` are provided by the
`GenericSymbolWithSyntaxTree`, and can be used to retrieve the closed generic
symbols used in your project's compilation, each paired with the relevant
syntax nodes.
