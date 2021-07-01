// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Diagnostics;
using Bicep.Core.FileSystem;
using Bicep.Core.Modules;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Bicep.Core.Registry
{
    public class OciModuleRegistry : IModuleRegistry
    {
        private readonly IFileResolver fileResolver;

        private readonly OrasClient orasClient;

        public OciModuleRegistry(IFileResolver fileResolver)
        {
            this.fileResolver = fileResolver;
            this.orasClient = new OrasClient(GetArtifactCachePath());
        }

        public ModuleReference? TryParseModuleReference(string reference, out DiagnosticBuilder.ErrorBuilderDelegate? failureBuilder) => OciArtifactModuleReference.TryParse(reference, out failureBuilder);

        public bool IsModuleInitRequired(ModuleReference reference)
        {
            // TODO: implement after rebasing
            return true;
        }

        public Uri? TryGetLocalModuleEntryPointPath(Uri parentModuleUri, ModuleReference reference, out DiagnosticBuilder.ErrorBuilderDelegate? failureBuilder)
        {
            var typed = ConvertReference(reference);
            string localArtifactPath = this.orasClient.GetLocalPackageEntryPointPath(typed);
            if (Uri.TryCreate(localArtifactPath, UriKind.Absolute, out var uri))
            {
                failureBuilder = null;
                return uri;
            }

            throw new NotImplementedException($"Local OCI artifact path is malformed: \"{localArtifactPath}\"");
        }

        public void InitModules(IEnumerable<ModuleReference> references)
        {
            foreach(var reference in references.OfType<OciArtifactModuleReference>())
            {
                this.orasClient.Pull(reference);
            }
        }
        
        private static string GetArtifactCachePath()
        {
            // TODO: Will NOT work if user profile is not loaded on Windows! (Az functions load exes like that)
            string basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return Path.Combine(basePath, ".bicep", "artifacts");
        }

        private static OciArtifactModuleReference ConvertReference(ModuleReference reference)
        {
            if(reference is OciArtifactModuleReference typed)
            {
                return typed;
            }

            throw new ArgumentException($"Reference type '{reference.GetType().Name}' is not supported.");
        }
    }
}
