// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Diagnostics;
using Bicep.Core.FileSystem;
using Bicep.Core.Modules;
using System;
using System.Collections.Generic;

namespace Bicep.Core.Registry
{
    public class LocalModuleRegistry : IModuleRegistry
    {
        private readonly IFileResolver fileResolver;

        public LocalModuleRegistry(IFileResolver fileResolver)
        {
            this.fileResolver = fileResolver;
        }

        public ModuleReference? TryParseModuleReference(string reference, out DiagnosticBuilder.ErrorBuilderDelegate? failureBuilder) => LocalModuleReference.TryParse(reference, out failureBuilder);

        public Uri? TryGetLocalModuleEntryPointPath(Uri parentModuleUri, ModuleReference reference, out DiagnosticBuilder.ErrorBuilderDelegate? failureBuilder)
        {
            var typed = ConvertReference(reference);

            var localUri = fileResolver.TryResolveFilePath(parentModuleUri, typed.Path);
            if (localUri is not null)
            {
                failureBuilder = null;
                return localUri;
            }

            failureBuilder = x => x.FilePathCouldNotBeResolved(typed.Path, parentModuleUri.LocalPath);
            return null;
        }

        public void InitModules(IEnumerable<ModuleReference> reference)
        {
            // local modules are already present on the file system
            // and do not require init
        }

        public bool IsModuleInitRequired(ModuleReference reference) => false;

        private static LocalModuleReference ConvertReference(ModuleReference reference)
        {
            if (reference is LocalModuleReference typed)
            {
                return typed;
            }

            throw new ArgumentException($"Reference type '{reference.GetType().Name}' is not supported.");
        }
    }
}