// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// These are the external versions of the architecture-specific DotNet-Runtime packages.
// They are created from the 3.0.0 version, plus the 2.2 NetCore.App SDK, which is deployed side-by-side
// The latter is required for MSBuild tests

module({
    name: "DotNet-Runtime-5.win-x64",
    projects: [f`DotNet-Runtime.win-x64.dsc`]
});

module({
    name: "DotNet-Runtime-5.osx-x64",
    projects: [f`DotNet-Runtime.osx-x64.dsc`]
});

module({
    name: "DotNet-Runtime-5.linux-x64",
    projects: [f`DotNet-Runtime.linux-x64.dsc`]
});