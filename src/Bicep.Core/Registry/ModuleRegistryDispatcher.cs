// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Diagnostics;
using Bicep.Core.FileSystem;
using Bicep.Core.Modules;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Bicep.Core.Registry
{
    public class ModuleRegistryDispatcher : IModuleRegistryDispatcher
    {
        private readonly ImmutableDictionary<string, IModuleRegistry> schemeToRegistry;
        private readonly ImmutableDictionary<Type, IModuleRegistry> referenceTypeToRegistry;
        private readonly ImmutableDictionary<Type, string> referenceTypeToScheme;

        public ModuleRegistryDispatcher(IFileResolver fileResolver)
        {
            (this.schemeToRegistry, this.referenceTypeToRegistry, this.referenceTypeToScheme) = Initialize(fileResolver);
            this.AvailableSchemes = this.schemeToRegistry.Keys.OrderBy(s => s).ToImmutableArray();
        }

        public IEnumerable<string> AvailableSchemes { get; }

        public ModuleReference? TryParseModuleReference(string reference, out DiagnosticBuilder.ErrorBuilderDelegate? failureBuilder)
        {
            var parts = reference.Split(':', 2, System.StringSplitOptions.None);
            switch (parts.Length)
            {
                case 1:
                    // local path reference
                    return schemeToRegistry[string.Empty].TryParseModuleReference(parts[0], out failureBuilder);

                case 2:
                    var scheme = parts[0];

                    if (schemeToRegistry.TryGetValue(scheme, out var registry))
                    {
                        // the scheme is recognized
                        var rawValue = parts[1];
                        return registry.TryParseModuleReference(rawValue, out failureBuilder);
                    }

                    // unknown scheme
                    failureBuilder = x => x.UnknownModuleReferenceScheme(scheme, this.AvailableSchemes);
                    return null;

                default:
                    // empty string
                    failureBuilder = x => x.ModulePathHasNotBeenSpecified();
                    return null;
            }
        }

        public string GetFullyQualifiedReference(ModuleReference reference)
        {
            Type refType = reference.GetType();
            if (this.referenceTypeToScheme.TryGetValue(refType, out var scheme))
            {
                return $"{scheme}:{reference}";
            }

            throw new NotImplementedException($"Unexpected module reference type '{refType.Name}'");
        }

        public bool IsModuleInitRequired(ModuleReference reference)
        {
            Type refType = reference.GetType();
            if (this.referenceTypeToRegistry.TryGetValue(refType, out var registry))
            {
                return registry.IsModuleInitRequired(reference);
            }

            throw new NotImplementedException($"Unexpected module reference type '{refType.Name}'");
        }

        public Uri? TryGetLocalModuleEntryPointPath(Uri parentModuleUri, ModuleReference reference, out DiagnosticBuilder.ErrorBuilderDelegate? failureBuilder)
        {
            Type refType = reference.GetType();
            if (this.referenceTypeToRegistry.TryGetValue(refType, out var registry))
            {
                return registry.TryGetLocalModuleEntryPointPath(parentModuleUri, reference, out failureBuilder);
            }

            throw new NotImplementedException($"Unexpected module reference type '{refType.Name}'");
        }

        public void InitModules(IEnumerable<ModuleReference> references)
        {
            var lookup = references.ToLookup(@ref => @ref.GetType());
            foreach (var referenceType in this.referenceTypeToRegistry.Keys.Where(refType => lookup.Contains(refType)))
            {
                this.referenceTypeToRegistry[referenceType].InitModules(lookup[referenceType]);
            }
        }

        // TODO: Once we have some sort of dependency injection in the CLI, this could be simplified
        private static (ImmutableDictionary<string, IModuleRegistry>, ImmutableDictionary<Type, IModuleRegistry>, ImmutableDictionary<Type, string>) Initialize(IFileResolver fileResolver)
        {
            var mapByString = ImmutableDictionary.CreateBuilder<string, IModuleRegistry>();
            var mapByType = ImmutableDictionary.CreateBuilder<Type, IModuleRegistry>();
            var schemeMap = ImmutableDictionary.CreateBuilder<Type, string>();

            void AddRegistry(string scheme, Type moduleRefType, IModuleRegistry instance)
            {
                mapByString.Add(scheme, instance);
                mapByType.Add(moduleRefType, instance);
                schemeMap.Add(moduleRefType, scheme);
            }

            AddRegistry(string.Empty, typeof(LocalModuleReference), new LocalModuleRegistry(fileResolver));
            AddRegistry("oci", typeof(OciArtifactModuleReference), new OciModuleRegistry(fileResolver));

            return (mapByString.ToImmutable(), mapByType.ToImmutable(), schemeMap.ToImmutable());
        }
    }
}
