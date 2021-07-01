// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Registry;
using Bicep.Core.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Bicep.Core.Modules
{
    public class ExternalReferenceCollector
    {
        public static ImmutableHashSet<ModuleReference> Collect(SyntaxTreeGrouping grouping, IModuleRegistryDispatcher dispatcher)
        {
            var moduleReferences = new HashSet<ModuleReference>();
            var visitor = new ExternalModuleReferenceCollectorVisitor(moduleReferences, dispatcher);

            visitor.Visit(grouping.EntryPoint.ProgramSyntax);
            foreach(var tree in grouping.SyntaxTrees)
            {
                visitor.Visit(tree.ProgramSyntax);
            }

            return moduleReferences.ToImmutableHashSet();
        }

        private class ExternalModuleReferenceCollectorVisitor: SyntaxVisitor
        {
            private readonly HashSet<ModuleReference> moduleReferences;

            private readonly IModuleRegistryDispatcher dispatcher;

            public ExternalModuleReferenceCollectorVisitor(HashSet<ModuleReference> moduleReferences, IModuleRegistryDispatcher dispatcher)
            {
                this.moduleReferences = moduleReferences;
                this.dispatcher = dispatcher;
            }

            public override void VisitModuleDeclarationSyntax(ModuleDeclarationSyntax syntax)
            {
                var moduleReference = this.dispatcher.TryGetModuleReference(syntax, out _);
                if (moduleReference is null || moduleReference is LocalModuleReference)
                {
                    // module ref is not valid or is a local ref
                    return;
                }

                this.moduleReferences.Add(moduleReference);

                // since modules can't be nested, there's no need to visit children
            }
        }
    }
}
