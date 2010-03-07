﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Silverlight.Testing.Harness;
using Microsoft.Silverlight.Testing.UnitTesting.Harness;
using Microsoft.Silverlight.Testing.UnitTesting.Metadata;

namespace StatLight.Client.Harness.UnitTestProviders.NUnit
{
    /// <summary>
    /// Assembly metadata for the Visual Studio Team Test unit test framework.
    /// </summary>
    public class UnitTestFrameworkAssembly : IAssembly
    {
        /// <summary>
        /// Assembly reflection object.
        /// </summary>
        private Assembly _assembly;

        /// <summary>
        /// Unit test provider used for the assembly.
        /// </summary>
        private IUnitTestProvider _provider;

        /// <summary>
        /// The unit test harness.
        /// </summary>
        private ITestHarness _harness;

        /// <summary>
        /// Creates a new unit test assembly wrapper.
        /// </summary>
        /// <param name="provider">Unit test metadata provider.</param>
        /// <param name="unitTestHarness">A reference to the unit test harness.</param>
        /// <param name="assembly">Assembly reflection object.</param>
        public UnitTestFrameworkAssembly(IUnitTestProvider provider, ITestHarness unitTestHarness, Assembly assembly)
        {
            _provider = provider;
            _harness = unitTestHarness;
            _assembly = assembly;
        }

        /// <summary>
        /// Gets the name of the test assembly.
        /// </summary>
        public string Name
        {
            get
            {
                string n = _assembly.ToString();
                return (n.Contains(", ") ? n.Substring(0, n.IndexOf(",", StringComparison.Ordinal)) : n);
            }
        }

        /// <summary>
        /// Gets the unit test provider instance.
        /// </summary>
        public IUnitTestProvider Provider
        {
            get { return _provider; }
        }

        /// <summary>
        /// Gets any assembly initialize method.
        /// </summary>
        public MethodInfo AssemblyInitializeMethod
        {
            get { return null; }
        }

        /// <summary>
        /// Gets any assembly cleanup method.
        /// </summary>
        public MethodInfo AssemblyCleanupMethod
        {
            get { return null; }
        }

        /// <summary>
        /// Gets the test harness used to initialize the assembly.
        /// </summary>
        public ITestHarness TestHarness
        {
            get { return _harness; }
        }

        /// <summary>
        /// Gets the test harness as a unit test harness.
        /// </summary>
        public UnitTestHarness UnitTestHarness
        {
            get { return _harness as UnitTestHarness; }
        }

        /// <summary>
        /// Reflect and retrieve the test class metadata wrappers for 
        /// the test assembly.
        /// </summary>
        /// <returns>Returns a collection of test class metadata 
        /// interface objects.</returns>
        public ICollection<ITestClass> GetTestClasses()
        {
            return _assembly.GetTestClasses(type => type.HasAttribute(NUnitAttributes.TestFixture),
                                            type => new TestClass(this, type));
        }
    }
}