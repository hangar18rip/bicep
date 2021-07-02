// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Modules;
using System.Collections.Generic;

namespace Bicep.Core.Registry
{
    public interface IModuleRegistryDispatcher : IModuleRegistry
    {
        IEnumerable<string> AvailableSchemes { get; }

        string GetFullyQualifiedReference(ModuleReference reference);
    }
}
