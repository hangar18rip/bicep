// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Bicep.Core.Registry
{
    public interface IModuleRegistryDispatcher : IModuleRegistry
    {
        IEnumerable<string> AvailableSchemes { get; }
    }
}
