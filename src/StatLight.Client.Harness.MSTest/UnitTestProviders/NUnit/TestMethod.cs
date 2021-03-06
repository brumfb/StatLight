﻿using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Silverlight.Testing.UnitTesting.Metadata;

namespace StatLight.Client.Harness.Hosts.MSTest.UnitTestProviders.NUnit
{
    using System.Collections;
    using System.Threading;

    /// <summary>
    /// A provider wrapper for a test method.
    /// </summary>
    public class TestMethod : ITestMethod
    {
        /// <summary>
        /// Default value for methods when no priority attribute is defined.
        /// </summary>
        private const int DefaultPriority = 3;

        /// <summary>
        /// An empty object array.
        /// </summary>
        private static readonly object[] None = { };

        /// <summary>
        /// Method reflection object.
        /// </summary>
        private MethodInfo _methodInfo;

        /// <summary>
        /// Private constructor, the constructor requires the method reflection object.
        /// </summary>
        private TestMethod() { }

        /// <summary>
        /// Creates a new test method wrapper object.
        /// </summary>
        /// <param name="methodInfo">The reflected method.</param>
        public TestMethod(MethodInfo methodInfo)
            : this()
        {
            _methodInfo = methodInfo;
        }

        /// <summary>
        /// Allows the test to perform a string WriteLine.
        /// </summary>
        public event EventHandler<StringEventArgs> WriteLine;

        /// <summary>
        /// Call the WriteLine method.
        /// </summary>
        /// <param name="s">String to WriteLine.</param>
        internal void OnWriteLine(string s)
        {
            StringEventArgs sea = new StringEventArgs(s);
            if (WriteLine != null)
            {
                WriteLine(this, sea);
            }
        }

        /// <summary>
        /// Decorates a test class instance with the unit test framework's 
        /// specific test context capability, if supported.
        /// </summary>
        /// <param name="instance">Instance to decorate.</param>
        public void DecorateInstance(object instance)
        {
        }

        /// <summary>
        /// Gets the underlying reflected method.
        /// </summary>
        public MethodInfo Method
        {
            get { return _methodInfo; }
        }

        /// <summary>
        /// Gets a value indicating whether there is an Ignore attribute.
        /// </summary>
        public bool Ignore
        {
            get
            {
                if (this.Method.HasAttribute(NUnitAttributes.Ignore))
                    return true;

                if (this.Method.HasAttribute(NUnitAttributes.Explicit))
                {
                    if (StatLight.Core.Configuration.ClientTestRunConfiguration.IsTestExplicit(Method))
                        return false;
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Gets any description marked on the test method.
        /// </summary>
        public string Description
        {
            get
            {
                string description = null;

                var descriptionAttribute = this.Method.GetAttribute(NUnitAttributes.Description);

                if (descriptionAttribute != null)
                    description = descriptionAttribute.GetObjectPropertyValue(NUnitAttributes.Description) as string;

                return description;
            }
        }

        /// <summary>
        /// Gets the name of the method.
        /// </summary>
        public virtual string Name
        {
            get { return _methodInfo.Name; }
        }

        /// <summary>
        /// Gets the Category.
        /// </summary>
        public string Category
        {
            get
            {
                var exp = this.Method.GetAttribute(NUnitAttributes.Category);
                var category = exp.GetObjectPropertyValue("Name") as string;
                return category;
            }
        }

        /// <summary>
        /// Gets the owner name of the test.
        /// </summary>
        public string Owner
        {
            get
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets any expected exception attribute information for the test method.
        /// </summary>
        public IExpectedException ExpectedException
        {
            get
            {
                var exp = this.Method.GetAttribute(NUnitAttributes.ExpectedException);

                return exp != null ?
                    new ExpectedException(exp) : null;
            }
        }

        /// <summary>
        /// Gets any timeout.  A Nullable property.
        /// </summary>
        public int? Timeout
        {
            get { return null; }
        }

        /// <summary>
        /// Gets a Collection of test properties.
        /// </summary>
        public ICollection<ITestProperty> Properties
        {
            get { return null; }
        }

        /// <summary>
        /// Gets a collection of test work items.
        /// </summary>
        public ICollection<IWorkItemMetadata> WorkItems
        {
            get { return null; }
        }

        /// <summary>
        /// Gets Priority information.
        /// </summary>
        public IPriority Priority
        {
            get
            {
                return null;
                //                VS.PriorityAttribute pri = ReflectionUtility.GetAttribute(this, ProviderAttributes.Priority, true) as VS.PriorityAttribute;
                //                return new Priority(pri == null ? DefaultPriority : pri.Priority);
            }
        }

        /// <summary>
        /// Get any attribute on the test method that are provided dynamically.
        /// </summary>
        /// <returns>
        /// Dynamically provided attributes on the test method.
        /// </returns>
        public virtual IEnumerable<Attribute> GetDynamicAttributes()
        {
            return new Attribute[] { };
        }

        /// <summary>
        /// Invoke the test method.
        /// </summary>
        /// <param name="instance">Instance of the test class.</param>
        public virtual void Invoke(object instance)
        {
            InvokeInternal(instance, None);
        }

        protected virtual object InvokeInternal(object instance, object[] parameters)
        {
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            var originalUICulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                var culture = GetCultureOverride();
                if (!string.IsNullOrEmpty(culture))
                    Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(culture);

                var cultureUI = GetCultureUIOverride();
                if (!string.IsNullOrEmpty(cultureUI))
                    Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(cultureUI);

                return _methodInfo.Invoke(instance, parameters);

            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
                Thread.CurrentThread.CurrentUICulture = originalUICulture;
            }
        }

        private string GetCultureOverride()
        {
            return GetAttributeProperty("NUnit.Framework.SetCulture", "_SETCULTURE");
        }

        private string GetCultureUIOverride()
        {
            return GetAttributeProperty("NUnit.Framework.SetUICulture", "_SETUICULTURE");
        }

        private string GetAttributeProperty(string attributeString, string propertyKey)
        {
            // First try to get the attribute from the method. Then try the class...
            object attribute = this.Method.GetAttribute(attributeString) ??
                               this.Method.DeclaringType.GetAttribute(attributeString);

            if (attribute == null)
                return null;

            var properties = attribute.GetObjectPropertyValue("Properties") as IDictionary;
            if (properties == null)
                return null;

            if (!properties.Contains(propertyKey))
                return null;

            return properties[propertyKey] as string;
        }
    }
}