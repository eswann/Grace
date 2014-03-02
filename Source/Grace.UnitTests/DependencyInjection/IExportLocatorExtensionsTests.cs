﻿using System;
using Grace.DependencyInjection;
using Grace.UnitTests.Classes.Simple;
using Xunit;

namespace Grace.UnitTests.DependencyInjection
{
	public class IExportLocatorExtensionsTests
	{
		[Fact]
		public void SomeTest()
		{
			DependencyInjectionContainer container = new DependencyInjectionContainer();

			container.Configure(c =>
			                    {
										  c.Export<BasicService>().As<IBasicService>();
				                    c.Export<ConstructorImportService>().AndSingleton();
			                    });

			string whatDoIHaveStr = container.WhatDoIHave(true);

			Console.Write(whatDoIHaveStr);
		}

		[Fact]
		public void WhatDoIHaveTestIncludeParent()
		{
			DependencyInjectionContainer container = new DependencyInjectionContainer();

			container.Configure(c => c.Export<ConstructorImportService>().AndSingleton());

			using (IInjectionScope child = container.CreateChildScope(c => c.Export<BasicService>().As<IBasicService>()))
			{
				string whatDoIHave = child.WhatDoIHave(true);

				Console.Write(whatDoIHave);

				Assert.NotNull(whatDoIHave);

				Assert.Contains("BasicService", whatDoIHave);
				Assert.Contains("ConstructorImportService", whatDoIHave);
				Assert.Contains("Singleton", whatDoIHave);
			}
		}

		[Fact]
		public void WhatDoIHaveTestDoNotIncludeParent()
		{
			DependencyInjectionContainer container = new DependencyInjectionContainer();

			container.Configure(c => c.Export<ConstructorImportService>().AndSingleton());

			using (IInjectionScope child = container.CreateChildScope(c => c.Export<BasicService>().As<IBasicService>()))
			{
				string whatDoIHave = child.WhatDoIHave();

				Console.Write(whatDoIHave);

				Assert.NotNull(whatDoIHave);

				Assert.Contains("BasicService", whatDoIHave);
				Assert.DoesNotContain("ConstructorImportService", whatDoIHave);
				Assert.DoesNotContain("Singleton", whatDoIHave);
			}
		}

		[Fact]
		public void WhatDoIHaveFilteredTest()
		{
			DependencyInjectionContainer container = new DependencyInjectionContainer();

			container.Configure(c => c.Export<ConstructorImportService>().AndSingleton());

			using (IInjectionScope child = container.CreateChildScope(c => c.Export<BasicService>().As<IBasicService>()))
			{
				string whatDoIHave = child.WhatDoIHave(includeParent: true, consider: ExportsThat.AreExportedAs<IBasicService>());

				Console.Write(whatDoIHave);

				Assert.NotNull(whatDoIHave);

				Assert.Contains("BasicService", whatDoIHave);
				Assert.DoesNotContain("ConstructorImportService", whatDoIHave);
				Assert.DoesNotContain("Singleton", whatDoIHave);
			}
		}
	}
}