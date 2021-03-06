﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Templates.Core.Gen;
using Microsoft.Templates.Core.Test.Locations;
using Microsoft.Templates.Fakes;
using Xunit;

namespace Microsoft.Templates.Core.Test
{
    public class TemplatesFixture
    {
        public TemplatesRepository Repository { get; private set; }

        public void InitializeFixture(string language)
        {
            var source = new UnitTestsTemplatesSource();

            GenContext.Bootstrap(source, new FakeGenShell(language), language);

            GenContext.ToolBox.Repo.SynchronizeAsync().Wait();

            Repository = GenContext.ToolBox.Repo;
        }
    }

    [CollectionDefinition("Unit Test Templates")]
    public class TemplatesCollection : ICollectionFixture<TemplatesFixture>
    {
    }
}
