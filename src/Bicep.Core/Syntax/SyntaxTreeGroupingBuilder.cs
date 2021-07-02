// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Bicep.Core.Diagnostics;
using Bicep.Core.FileSystem;
using Bicep.Core.Parsing;
using Bicep.Core.Registry;
using Bicep.Core.Utils;
using Bicep.Core.Workspaces;

namespace Bicep.Core.Syntax
{
    public class SyntaxTreeGroupingBuilder
    {
        private readonly IFileResolver fileResolver;
        private readonly IModuleRegistryDispatcher dispatcher;
        private readonly IReadOnlyWorkspace workspace;

        private readonly Dictionary<ModuleDeclarationSyntax, SyntaxTree> moduleLookup;
        private readonly Dictionary<ModuleDeclarationSyntax, DiagnosticBuilder.ErrorBuilderDelegate> moduleFailureLookup;

        private readonly HashSet<ModuleDeclarationSyntax> modulesRequiringInit;

        // uri -> successfully loaded syntax tree
        private readonly Dictionary<Uri, SyntaxTree> syntaxTrees;

        // uri -> syntax tree load failure 
        private readonly Dictionary<Uri, DiagnosticBuilder.ErrorBuilderDelegate> syntaxTreeLoadFailures;

        private SyntaxTreeGroupingBuilder(IFileResolver fileResolver, IModuleRegistryDispatcher dispatcher, IReadOnlyWorkspace workspace, SyntaxTreeGrouping? current)
        {
            this.fileResolver = fileResolver;
            this.dispatcher = dispatcher;
            this.workspace = workspace;
            if (current is null)
            {
                this.moduleLookup = new();
                this.moduleFailureLookup = new();
                this.modulesRequiringInit = new();
                this.syntaxTrees = new();
                this.syntaxTreeLoadFailures = new();
            }
            else
            {
                this.moduleLookup = new(current.ModuleLookup);
                this.moduleFailureLookup = new(current.ModuleFailureLookup);

                // TODO: Needs to be recoverable
                this.modulesRequiringInit = new();

                this.syntaxTrees = current.SyntaxTrees.ToDictionary(tree => tree.FileUri);

                // TODO: can we recover this info too?
                this.syntaxTreeLoadFailures = new();
            }
        }

        public static SyntaxTreeGrouping Build(IFileResolver fileResolver, IModuleRegistryDispatcher dispatcher, IReadOnlyWorkspace workspace, Uri entryFileUri)
        {
            var builder = new SyntaxTreeGroupingBuilder(fileResolver, dispatcher, workspace, current: null);

            return builder.Build(entryFileUri);
        }

        //public static SyntaxTreeGrouping 

        private SyntaxTreeGrouping Build(Uri entryFileUri)
        {
            var entryPoint = PopulateRecursive(entryFileUri, out var entryPointLoadFailureBuilder);
            if (entryPoint == null)
            {
                // TODO: If we upgrade to netstandard2.1, we should be able to use the following to hint to the compiler that failureBuilder is non-null:
                // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis
                var failureBuilder = entryPointLoadFailureBuilder ?? throw new InvalidOperationException($"Expected {nameof(PopulateRecursive)} to provide failure diagnostics");
                var diagnostic = failureBuilder(DiagnosticBuilder.ForPosition(new TextSpan(0, 0)));

                throw new ErrorDiagnosticException(diagnostic);
            }

            ReportFailuresForCycles();

            return new SyntaxTreeGrouping(
                entryPoint,
                syntaxTrees.Values.ToImmutableHashSet(), 
                moduleLookup.ToImmutableDictionary(),
                moduleFailureLookup.ToImmutableDictionary(),
                fileResolver);
        }

        private SyntaxTree? TryGetSyntaxTree(Uri fileUri, out DiagnosticBuilder.ErrorBuilderDelegate? failureBuilder)
        {
            if (workspace.TryGetSyntaxTree(fileUri, out var syntaxTree))
            {
                failureBuilder = null;
                syntaxTrees[fileUri] = syntaxTree;
                return syntaxTree;
            }

            if (syntaxTrees.TryGetValue(fileUri, out syntaxTree))
            {
                failureBuilder = null;
                return syntaxTree;
            }

            if (syntaxTreeLoadFailures.TryGetValue(fileUri, out failureBuilder))
            {
                return null;
            }

            if (!fileResolver.TryRead(fileUri, out var fileContents, out failureBuilder))
            {
                syntaxTreeLoadFailures[fileUri] = failureBuilder;
                return null;
            }

            failureBuilder = null;
            return AddSyntaxTree(fileUri, fileContents);
        }

        private SyntaxTree AddSyntaxTree(Uri fileUri, string fileContents)
        {
            var syntaxTree = SyntaxTree.Create(fileUri, fileContents);
            syntaxTrees[fileUri] = syntaxTree;

            return syntaxTree;
        }

        private SyntaxTree? PopulateRecursive(Uri fileUri, out DiagnosticBuilder.ErrorBuilderDelegate? failureBuilder)
        {
            var syntaxTree = TryGetSyntaxTree(fileUri, out var getSyntaxTreeFailureBuilder);
            if (syntaxTree is null)
            {
                failureBuilder = getSyntaxTreeFailureBuilder;
                return null;
            }

            foreach (var module in GetModuleSyntaxes(syntaxTree))
            {
                var moduleReference = this.dispatcher.TryGetModuleReference(module, out var parseReferenceFailureBuilder);
                if(moduleReference is null)
                {
                    moduleFailureLookup[module] = parseReferenceFailureBuilder ?? throw new InvalidOperationException($"Expected {nameof(ModuleRegistryDispatcherExtensions.TryGetModuleReference)} to provide failure diagnostics.");
                    continue;
                }

                if(this.dispatcher.IsModuleInitRequired(moduleReference))
                {
                    // module is not cached locally
                    moduleFailureLookup[module] = x => x.ModuleRequiresInit(this.dispatcher.GetFullyQualifiedReference(moduleReference));
                    modulesRequiringInit.Add(module);
                    continue;
                }

                var moduleFileName = this.dispatcher.TryGetLocalModuleEntryPointPath(fileUri, moduleReference, out var moduleGetPathFailureBuilder);
                if (moduleFileName is null)
                {
                    // TODO: If we upgrade to netstandard2.1, we should be able to use the following to hint to the compiler that failureBuilder is non-null:
                    // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis
                    moduleFailureLookup[module] = moduleGetPathFailureBuilder ?? throw new InvalidOperationException($"Expected {nameof(dispatcher.TryGetLocalModuleEntryPointPath)} to provide failure diagnostics.");
                    continue;
                }

                // only recurse if we've not seen this module before - to avoid infinite loops
                if (!syntaxTrees.TryGetValue(moduleFileName, out var moduleSyntaxTree))
                {
                    moduleSyntaxTree = PopulateRecursive(moduleFileName, out var modulePopulateFailureBuilder);
                    
                    if (moduleSyntaxTree is null)
                    {
                        // TODO: If we upgrade to netstandard2.1, we should be able to use the following to hint to the compiler that failureBuilder is non-null:
                        // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis
                        moduleFailureLookup[module] = modulePopulateFailureBuilder ?? throw new InvalidOperationException($"Expected {nameof(PopulateRecursive)} to provide failure diagnostics.");
                        continue;
                    }
                }

                if (moduleSyntaxTree is null)
                {
                    continue;
                }

                moduleLookup[module] = moduleSyntaxTree;
            }

            failureBuilder = null;
            return syntaxTree;
        }

        private void ReportFailuresForCycles()
        {
            var syntaxTreeGraph = syntaxTrees.Values.OfType<SyntaxTree>()
                .SelectMany(tree => GetModuleSyntaxes(tree).Where(moduleLookup.ContainsKey).Select(x => moduleLookup[x]).Distinct().Select(x => (tree, x)))
                .ToLookup(x => x.Item1, x => x.Item2);

            var cycles = CycleDetector<SyntaxTree>.FindCycles(syntaxTreeGraph);
            foreach (var kvp in moduleLookup)
            {
                if (cycles.TryGetValue(kvp.Value, out var cycle))
                {
                    if (cycle.Length == 1)
                    {
                        moduleFailureLookup[kvp.Key] = x => x.CyclicModuleSelfReference();
                    }
                    else
                    {
                        moduleFailureLookup[kvp.Key] = x => x.CyclicModule(cycle.Select(x => x.FileUri.LocalPath));
                    }
                }
            }
        }

        private static IEnumerable<ModuleDeclarationSyntax> GetModuleSyntaxes(SyntaxTree syntaxTree)
            => syntaxTree.ProgramSyntax.Declarations.OfType<ModuleDeclarationSyntax>();
    }
}
