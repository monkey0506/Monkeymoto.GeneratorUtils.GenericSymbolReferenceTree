# GenericSymbolReferenceTree

C# incremental generator utility to produce a tree of closed generic symbols
used in your project's compilation, each paired with a syntax node.

## How to use this project

This project is designed to be used with a C# incremental generator to discover
closed generic symbols that are referenced in your project's compilation.
Accordingly, the first step in using this project is to [add the necessary
references to your `csproj` file](https://github.com/dotnet/roslyn/discussions/47517).

The pre-built binaries of this project are available as a
[NuGet Package](https://www.nuget.org/packages/Monkeymoto.GeneratorUtils.GenericSymbolReferenceTree).
Your `csproj` file may look like this:

````XML
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <PublishAot>false</PublishAot>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
    <PackageReference Include="Monkeymoto.GeneratorUtils.GenericSymbolReferenceTree" Version="1.0.0.2">
      <PrivateAssets>all</PrivateAssets>
      <GeneratePathProperty>true</GeneratePathProperty>
    </PackageReference>
  </ItemGroup>

  <PropertyGroup>
    <GetTargetPathDependsOn>$(GetTargetPathDependsOn);GetDependencyTargetPaths</GetTargetPathDependsOn>
  </PropertyGroup>

  <Target Name="GetDependencyTargetPaths">
    <ItemGroup>
      <TargetPathWithTargetPlatformMoniker Include="$(PKGMonkeymoto_GeneratorUtils_GenericSymbolReferenceTree)\lib\netstandard2.0\Monkeymoto.GeneratorUtils.GenericSymbolReferenceTree.dll" IncludeRuntimeDependency="false" />
    </ItemGroup>
  </Target>

</Project>
````

Once this project has been added to your incremental generator, you can then
use the `GenericSymbolReferenceTree.FromIncrementalGeneratorInitializationContext`
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
            var symbolTree = Monkeymoto.GeneratorUtils.GenericSymbolReferenceTree.FromIncrementalGeneratorInitializationContext(context);
            var symbolToFind = context.CompilationProvider.Select(static (compilation, _) =>
            {
                return compilation.GetTypeByMetadataName("MyProject.MyGenericType`1")!;
            });
            var symbols = symbolTree.Combine(symbolToFind).Select(static (x, cancellationToken) =>
            {
                var (symbolTree, symbolToFind) = x;
                return symbolTree.GetBranchesBySymbol(symbolToFind, cancellationToken);
            });
            // `symbols` is an `IncrementalValueProvider<IEnumerable<GenericSymbolReference>>`
            // this represents each *closed* instance of `MyProject.MyGenericType<T>` in the compilation
            // i.e., `MyProject.MyGenericType<int>`, `MyProject.MyGenericType<double>`, etc.
        }
    }
}
````

The methods `GetBranch` and `GetBranchesBySymbol` are provided by the
`GenericSymbolReferenceTree`, and can be used to retrieve the closed generic
symbols used in your project's compilation, each paired with the relevant
syntax nodes.
