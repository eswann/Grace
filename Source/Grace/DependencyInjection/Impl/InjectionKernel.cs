﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Grace.DependencyInjection.Exceptions;
using Grace.Diagnostics;
using Grace.Logging;

namespace Grace.DependencyInjection.Impl
{
	/// <summary>
	/// InjectionKernel keeps a collection of exports to be used for resolving dependencies.
	/// </summary>
	[DebuggerDisplay("{DebugDisplayString,nq}")]
	[DebuggerTypeProxy(typeof(IInjectionScopeDiagnostic))]
	public class InjectionKernel : DisposalScope, IInjectionScope, IMissingExportHandler
	{
		#region Private Static

		private static readonly Dictionary<Type, Type> openGenericStrategyMapping;

		static InjectionKernel()
		{
			openGenericStrategyMapping = new Dictionary<Type, Type>();

			openGenericStrategyMapping[typeof(IEnumerable<>)] = typeof(ListExportStrategy<>);
			openGenericStrategyMapping[typeof(ICollection<>)] = typeof(ListExportStrategy<>);
			openGenericStrategyMapping[typeof(IList<>)] = typeof(ListExportStrategy<>);
			openGenericStrategyMapping[typeof(List<>)] = typeof(ListExportStrategy<>);

			openGenericStrategyMapping[typeof(IReadOnlyCollection<>)] = typeof(ReadOnlyCollectionExportStrategy<>);
			openGenericStrategyMapping[typeof(IReadOnlyList<>)] = typeof(ReadOnlyCollectionExportStrategy<>);
			openGenericStrategyMapping[typeof(ReadOnlyCollection<>)] = typeof(ReadOnlyCollectionExportStrategy<>);

			openGenericStrategyMapping[typeof(Owned<>)] = typeof(OwnedStrategy<>);

			openGenericStrategyMapping[typeof(Lazy<>)] = typeof(LazyExportStrategy<>);

			openGenericStrategyMapping[typeof(Func<>)] = typeof(FuncExportStrategy<>);

			openGenericStrategyMapping[typeof(Func<,>)] = typeof(GenericFuncExportStrategy<,>);
			openGenericStrategyMapping[typeof(Func<,,>)] = typeof(GenericFuncExportStrategy<,,>);
			openGenericStrategyMapping[typeof(Func<,,,>)] = typeof(GenericFuncExportStrategy<,,,>);
			openGenericStrategyMapping[typeof(Func<,,,,>)] = typeof(GenericFuncExportStrategy<,,,,>);
			openGenericStrategyMapping[typeof(Func<,,,,,>)] = typeof(GenericFuncExportStrategy<,,,,,>);
		}

		#endregion

		#region Private Fields

		private readonly ExportStrategyComparer comparer;
		private readonly IDisposalScopeProvider disposalScopeProvider;
		private readonly object exportsLock = new object();
		private readonly object extraDataLock = new object();
		private readonly InjectionKernelManager kernelManager;
		private readonly ILog log = Logger.GetLogger<InjectionKernel>();
		private readonly object secondaryResolversLock = new object();
		private volatile Dictionary<string, ExportStrategyCollection> exports;
		private volatile Dictionary<string, object> extraData;
		private volatile ReadOnlyCollection<ISecondaryExportLocator> secondaryResolvers;

		#endregion

		#region Constructors

		/// <summary>
		/// Default constructor
		/// </summary>
		/// <param name="kernelManager">kernel manager for this kernel</param>
		/// <param name="parentScope"></param>
		/// <param name="scopeProvider">passing a null for scope provider is ok</param>
		/// <param name="scopeName"></param>
		/// <param name="comparer"></param>	
		public InjectionKernel(InjectionKernelManager kernelManager,
			IInjectionScope parentScope,
			IDisposalScopeProvider scopeProvider,
			string scopeName,
			ExportStrategyComparer comparer)
		{
			ScopeId = Guid.NewGuid();

			exports = new Dictionary<string, ExportStrategyCollection>();

			this.kernelManager = kernelManager;
			this.comparer = comparer;
			disposalScopeProvider = scopeProvider;
			ScopeName = scopeName;
			ParentScope = parentScope;
		}

		#endregion

		#region Properties

		/// <summary>
		/// The container this scope was created in
		/// </summary>
		public IDependencyInjectionContainer Container
		{
			get { return kernelManager.Container; }
		}

		/// <summary>
		/// Unique identifier for the instance of the injection scope
		/// </summary>
		public Guid ScopeId { get; private set; }

		/// <summary>
		/// The scopes name
		/// </summary>
		public string ScopeName { get; internal set; }

		/// <summary>
		/// Parent scope, can be null if it's the root scope
		/// </summary>
		public IInjectionScope ParentScope { get; internal set; }

		/// <summary>
		/// The environment for this scope (always inherited from the root scope)
		/// </summary>
		public ExportEnvironment Environment { get; internal set; }

		#endregion

		#region Scope Methods

		/// <summary>
		/// Creates a child scope from this scope
		/// </summary>
		/// <param name="scopeName"></param>
		/// <param name="registrationDelegate"></param>
		/// <param name="disposalScopeProvider"></param>
		/// <returns></returns>
		public IInjectionScope CreateChildScope(ExportRegistrationDelegate registrationDelegate = null,
			string scopeName = null,
			IDisposalScopeProvider disposalScopeProvider = null)
		{
			IInjectionScope returnValue =
				kernelManager.CreateNewKernel(this,
					scopeName,
					registrationDelegate,
					this.disposalScopeProvider,
					disposalScopeProvider);

			return returnValue;
		}

		/// <summary>
		/// Creates a child scope from this scope using a configuration module
		/// </summary>
		/// <param name="scopeName">name of the scope you want to create</param>
		/// <param name="configurationModule"></param>
		/// <param name="disposalScopeProvider"></param>
		/// <returns></returns>
		public IInjectionScope CreateChildScope(IConfigurationModule configurationModule,
			string scopeName = null,
			IDisposalScopeProvider disposalScopeProvider = null)
		{
			return CreateChildScope(configurationModule.Configure, scopeName, disposalScopeProvider);
		}

		/// <summary>
		/// Adds a secondary resolver to the injection scope
		/// </summary>
		/// <param name="newLocator"></param>
		public void AddSecondaryLocator(ISecondaryExportLocator newLocator)
		{
			lock (secondaryResolversLock)
			{
				List<ISecondaryExportLocator> newResolvers = new List<ISecondaryExportLocator>(1);

				if (secondaryResolvers != null)
				{
					newResolvers.AddRange(secondaryResolvers);
				}

				newResolvers.Add(newLocator);

				secondaryResolvers = new ReadOnlyCollection<ISecondaryExportLocator>(newResolvers);
			}
		}

		/// <summary>
		/// List of Export Locators
		/// </summary>
		public IEnumerable<ISecondaryExportLocator> SecondaryExportLocators
		{
			get
			{
				if (secondaryResolvers != null)
				{
					return secondaryResolvers;
				}

				return new ISecondaryExportLocator[0];
			}
		}

		/// <summary>
		/// Add a strategy 
		/// </summary>
		/// <param name="inspector">strategy inspector</param>
		public void AddStrategyInspector(IStrategyInspector inspector)
		{
		}

		/// <summary>
		/// Clone the injection kernel, the rootscope cannot be cloned
		/// </summary>
		/// <param name="parentScope"></param>
		/// <param name="parentScopeProvider"></param>
		/// <param name="scopeProvider"></param>
		/// <returns></returns>
		public InjectionKernel Clone(IInjectionScope parentScope,
			IDisposalScopeProvider parentScopeProvider,
			IDisposalScopeProvider scopeProvider)
		{
			if (ParentScope == null)
			{
				throw new RootScopeCloneException(ScopeName, ScopeId);
			}

			IDisposalScopeProvider newProvider = (scopeProvider ?? disposalScopeProvider) ?? parentScopeProvider;

			InjectionKernel returnValue = new InjectionKernel(kernelManager, parentScope, newProvider, ScopeName, comparer)
			                              {
				                              ParentScope = parentScope,
				                              Environment = Environment
			                              };

			Dictionary<string, ExportStrategyCollection> newExports = returnValue.exports;

			foreach (KeyValuePair<string, ExportStrategyCollection> exportStrategyCollection in exports)
			{
				newExports[exportStrategyCollection.Key] =
					exportStrategyCollection.Value.Clone(returnValue);
			}

			return returnValue;
		}

		#endregion

		#region Configure Methods

		/// <summary>
		/// You can add extra configuration to the scope
		/// </summary>
		/// <param name="registrationDelegate"></param>
		public void Configure(ExportRegistrationDelegate registrationDelegate)
		{
			ExportRegistrationBlock registrationBlock = new ExportRegistrationBlock(this);

			registrationDelegate(registrationBlock);

			List<IExportStrategy> exportStrategyList = new List<IExportStrategy>();
			IExportStrategy[] exportStrategies = registrationBlock.GetExportStrategies().ToArray();

			foreach (IExportStrategy exportStrategy in exportStrategies)
			{
				if (kernelManager.BlackList.IsExportStrategyBlackedOut(exportStrategy))
				{
					continue;
				}

				exportStrategy.OwningScope = this;

				exportStrategy.Initialize();

				exportStrategyList.Add(exportStrategy);

				foreach (IExportStrategy secondaryStrategy in exportStrategy.SecondaryStrategies())
				{
					secondaryStrategy.OwningScope = this;

					secondaryStrategy.Initialize();

					exportStrategyList.Add(secondaryStrategy);
				}
			}

			lock (exportsLock)
			{
				Dictionary<string, ExportStrategyCollection> newExports =
					new Dictionary<string, ExportStrategyCollection>(exports);

				foreach (IExportStrategy exportStrategy in exportStrategyList)
				{
					if (kernelManager.BlackList.IsExportStrategyBlackedOut(exportStrategy))
					{
						continue;
					}

					foreach (string exportName in exportStrategy.ExportNames)
					{
						ExportStrategyCollection currentCollection;

						if (!newExports.TryGetValue(exportName, out currentCollection))
						{
							currentCollection = new ExportStrategyCollection(this, Environment, comparer);

							newExports[exportName] = currentCollection;
						}

						currentCollection.AddExport(exportStrategy);
					}
				}

				exports = newExports;
			}
		}

		/// <summary>
		/// Configure the scope with a configuration module
		/// </summary>
		/// <param name="configurationModule"></param>
		public void Configure(IConfigurationModule configurationModule)
		{
			Configure(configurationModule.Configure);
		}

		#endregion

		#region CreateContext Methods

		/// <summary>
		/// Create an injection context associated with this scope
		/// </summary>
		/// <returns></returns>
		public IInjectionContext CreateContext(IDisposalScope disposalScope = null)
		{
			if (disposalScope == null)
			{
				return disposalScopeProvider == null
					? new InjectionContext(this, this)
					: new InjectionContext(disposalScopeProvider.ProvideDisposalScope(this), this);
			}

			return new InjectionContext(disposalScope, this);
		}

		#endregion

		#region Locate methods

		/// <summary>
		/// Locate an export by type
		/// </summary>
		/// <param name="injectionContext"></param>
		/// <param name="consider"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public T Locate<T>(IInjectionContext injectionContext = null, ExportStrategyFilter consider = null)
		{
			return (T)Locate(typeof(T), injectionContext, consider);
		}

		/// <summary>
		/// Locate an object by type
		/// </summary>
		/// <param name="objectType"></param>
		/// <param name="injectionContext"></param>
		/// <param name="consider"></param>
		/// <returns></returns>
		public object Locate(Type objectType, IInjectionContext injectionContext = null, ExportStrategyFilter consider = null)
		{
			if (objectType == null)
			{
				throw new ArgumentNullException("objectType");
			}

			string fullName = objectType.FullName;

			if (log.IsDebugEnabled)
			{
				log.DebugFormat("Locate by type {0} with injectionContext is null: {1} and consider is null {2}",
					objectType.FullName,
					injectionContext == null,
					consider == null);
			}

			if (injectionContext == null)
			{
				injectionContext = CreateContext();
			}

			try
			{
				object returnValue = null;
				ExportStrategyCollection collection;

				if (exports.TryGetValue(fullName, out collection))
				{
					returnValue = collection.Activate(fullName, null, injectionContext, consider);

					if (returnValue != null)
					{
						return returnValue;
					}
				}

				if (objectType.IsConstructedGenericType)
				{
					IExportStrategy exportStrategy = GetStrategy(objectType, injectionContext);

					// I'm doing a second look up incase two threads are trying to create a generic at the same exact time
					// and they have a singleton you have to use the same export strategy
					if (exportStrategy != null && exports.TryGetValue(fullName, out collection))
					{
						returnValue = collection.Activate(fullName, objectType, injectionContext, consider);
					}
				}

				ReadOnlyCollection<ISecondaryExportLocator> tempSecondaryResolvers = secondaryResolvers;

				if (returnValue == null && tempSecondaryResolvers != null)
				{
					foreach (ISecondaryExportLocator secondaryDependencyResolver in tempSecondaryResolvers)
					{
						returnValue = secondaryDependencyResolver.Locate(this, injectionContext, null, objectType, consider);

						if (returnValue != null)
						{
							break;
						}
					}
				}

				if (returnValue == null && ParentScope != null)
				{
					returnValue = ParentScope.Locate(objectType, injectionContext, consider);
				}

				if (returnValue == null && injectionContext.RequestingScope == this)
				{
					if (objectType.IsConstructedGenericType)
					{
						returnValue = ProcessSpecialGenericType<object>(injectionContext, objectType, consider);
					}
					else if (objectType.IsArray)
					{
						returnValue = ProcessArrayType<object>(injectionContext, objectType, consider);
					}

					if (returnValue == null)
					{
						returnValue = ProcessICollectionType(injectionContext, objectType, consider);
					}

					if (returnValue == null)
					{
						returnValue = ResolveUnknownExport(objectType, null, injectionContext, consider);
					}
				}

				if (returnValue != null)
				{
					return returnValue;
				}
			}
			catch (Exception exp)
			{
				if (kernelManager.Container != null &&
				    kernelManager.Container.ThrowExceptions)
				{
					throw;
				}

				log.Error(
					string.Format("Exception was thrown from Locate by type {0} in scope {1} id {2}",
						objectType.FullName,
						ScopeName,
						ScopeId),
					exp);
			}

			if (kernelManager.Container != null &&
			    kernelManager.Container.ThrowExceptions)
			{
				throw new ExportMissingException(objectType.FullName);
			}

			return null;
		}

		/// <summary>
		/// Locate an export by name
		/// </summary>
		/// <param name="exportName"></param>
		/// <param name="injectionContext"></param>
		/// <param name="consider"></param>
		/// <returns></returns>
		public object Locate(string exportName,
			IInjectionContext injectionContext = null,
			ExportStrategyFilter consider = null)
		{
			if (exportName == null)
			{
				throw new ArgumentNullException("exportName");
			}

			exportName = exportName.ToLowerInvariant();

			if (log.IsDebugEnabled)
			{
				log.DebugFormat("Locate by name {0} with injectionContext is null: {1} and consider is null {2}",
					exportName,
					injectionContext == null,
					consider == null);
			}

			if (injectionContext == null)
			{
				injectionContext = CreateContext();
			}

			try
			{
				object returnValue = null;

				ExportStrategyCollection collection;

				if (exports.TryGetValue(exportName, out collection))
				{
					returnValue = collection.Activate(exportName, null, injectionContext, consider);

					if (returnValue != null)
					{
						return returnValue;
					}
				}

				ReadOnlyCollection<ISecondaryExportLocator> tempSecondaryResolvers = secondaryResolvers;

				if (tempSecondaryResolvers != null)
				{
					foreach (ISecondaryExportLocator secondaryDependencyResolver in tempSecondaryResolvers)
					{
						returnValue = secondaryDependencyResolver.Locate(this, injectionContext, exportName, null, consider);

						if (returnValue != null)
						{
							break;
						}
					}
				}

				if (ParentScope != null)
				{
					returnValue = ParentScope.Locate(exportName, injectionContext, consider);
				}

				if (returnValue == null && injectionContext.RequestingScope == this)
				{
					returnValue = ResolveUnknownExport(typeof(object), exportName, injectionContext, consider);
				}

				if (returnValue != null)
				{
					return returnValue;
				}
			}
			catch (Exception exp)
			{
				if (kernelManager.Container != null &&
				    kernelManager.Container.ThrowExceptions)
				{
					throw;
				}

				log.Error(
					string.Format("Exception was thrown from Locate by name {0} in scope {1} id {2}", exportName, ScopeName, ScopeId),
					exp);
			}

			if (kernelManager.Container != null &&
			    kernelManager.Container.ThrowExceptions)
			{
				throw new ExportMissingException(exportName);
			}

			return null;
		}

		/// <summary>
		/// Locate all export of type T
		/// </summary>
		/// <param name="injectionContext"></param>
		/// <param name="consider"></param>
		/// <param name="comparer"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public List<T> LocateAll<T>(IInjectionContext injectionContext = null, ExportStrategyFilter consider = null, IComparer<T> comparer = null)
		{
			if (log.IsDebugEnabled)
			{
				log.DebugFormat("LocateAll<T> type {0} with injectionContext is null: {1} and consider is null {2}",
					typeof(T).FullName,
					injectionContext == null,
					consider == null);
			}

			if (injectionContext == null)
			{
				injectionContext = CreateContext();
			}

			List<T> returnValue;
			try
			{
				returnValue = InternalLocateAllWithContext<T>(injectionContext, typeof(T).FullName, typeof(T), consider);
			}
			catch (Exception exp)
			{
				if (kernelManager.Container != null &&
				    kernelManager.Container.ThrowExceptions)
				{
					throw;
				}

				log.Error(
					string.Format("Exception was thrown from LocateAll<T> for type {0} in scope {1} id {2}",
						typeof(T).FullName,
						ScopeName,
						ScopeId),
					exp);

				returnValue = new List<T>();
			}

			if (comparer != null)
			{
				returnValue.Sort(comparer);
			}

			return returnValue;
		}

		/// <summary>
		/// Locate All exports by the name provided
		/// </summary>
		/// <param name="name"></param>
		/// <param name="injectionContext"></param>
		/// <param name="consider"></param>
		/// <param name="comparer"></param>
		/// <returns></returns>
		public List<object> LocateAll(string name, IInjectionContext injectionContext = null, ExportStrategyFilter consider = null, IComparer<object> comparer = null)
		{
			if (name == null)
			{
				throw new ArgumentNullException("name");
			}

			name = name.ToLowerInvariant();

			if (log.IsDebugEnabled)
			{
				log.DebugFormat("LocateAll by name {0} with injectionContext is null: {1} and consider is null {2}",
					name,
					injectionContext == null,
					consider == null);
			}

			if (injectionContext == null)
			{
				injectionContext = CreateContext();
			}

			List<object> returnValue;

			try
			{
				returnValue = InternalLocateAllWithContext<object>(injectionContext, name, null, consider);
			}
			catch (Exception exp)
			{
				if (kernelManager.Container != null &&
				    kernelManager.Container.ThrowExceptions)
				{
					throw;
				}

				log.Error(
					string.Format("Exception was thrown from LocateAll by name {0} in scope {1} id {2}", name, ScopeName, ScopeId),
					exp);

				returnValue=  new List<object>();
			}

			if (comparer != null)
			{
				returnValue.Sort(comparer);
			}

			return returnValue;
		}

		/// <summary>
		/// Locate all exports by type
		/// </summary>
		/// <param name="exportType"></param>
		/// <param name="injectionContext"></param>
		/// <param name="consider"></param>
		/// <returns></returns>
		public List<object> LocateAll(Type exportType,
			IInjectionContext injectionContext = null,
			ExportStrategyFilter consider = null)
		{
			if (exportType == null)
			{
				throw new ArgumentNullException("exportType");
			}

			if (log.IsDebugEnabled)
			{
				log.DebugFormat("LocateAll by type {0} with injectionContext is null: {1} and consider is null {2}",
					exportType.FullName,
					injectionContext == null,
					consider == null);
			}

			if (injectionContext == null)
			{
				injectionContext = CreateContext();
			}

			try
			{
				return InternalLocateAllWithContext<object>(injectionContext, exportType.FullName, exportType, consider);
			}
			catch (Exception exp)
			{
				if (kernelManager.Container != null &&
				    kernelManager.Container.ThrowExceptions)
				{
					throw;
				}

				log.Error(
					string.Format("Exception was thrown from LocateAll by type {0} in scope {1} id {2}",
						exportType.FullName,
						ScopeName,
						ScopeId),
					exp);

				return new List<object>();
			}
		}

		#endregion

		#region ExtraData Methods

		/// <summary>
		/// Extra data associated with the injection request. 
		/// </summary>
		/// <param name="dataName"></param>
		/// <returns></returns>
		public object GetExtraData(string dataName)
		{
			object returnValue = null;

			if (extraData != null)
			{
				lock (extraDataLock)
				{
					extraData.TryGetValue(dataName, out returnValue);
				}
			}

			return returnValue;
		}

		/// <summary>
		/// Sets extra data on the injection context
		/// </summary>
		/// <param name="dataName"></param>
		/// <param name="newValue"></param>
		public void SetExtraData(string dataName, object newValue)
		{
			lock (extraDataLock)
			{
				if (extraData == null)
				{
					extraData = new Dictionary<string, object>();
				}

				extraData[dataName] = newValue;
			}
		}

		#endregion

		#region Get Strategy Methods

		/// <summary>
		/// Returns a list of all known strategies.
		/// </summary>
		/// <param name="exportFilter"></param>
		/// <returns>returns all known strategies</returns>
		public IEnumerable<IExportStrategy> GetAllStrategies(ExportStrategyFilter exportFilter)
		{
			IInjectionContext context = null;
			HashSet<IExportStrategy> returnValue = new HashSet<IExportStrategy>();
			Dictionary<string, ExportStrategyCollection> currentExports = exports;

			if (exportFilter != null)
			{
				context = CreateContext();
			}

			foreach (ExportStrategyCollection exportStrategyCollection in currentExports.Values)
			{
				foreach (IExportStrategy exportStrategy in exportStrategyCollection.ExportStrategies)
				{
					if (!returnValue.Contains(exportStrategy) &&
						 (exportFilter == null || exportFilter(context, exportStrategy)))
					{
						returnValue.Add(exportStrategy);
					}
				}
			}

			return returnValue;
		}

		/// <summary>
		/// Finds the best matching strategy exported by the name provided
		/// </summary>
		/// <param name="name"></param>
		/// <param name="injectionContext"></param>
		/// <returns></returns>
		public IExportStrategy GetStrategy(string name, IInjectionContext injectionContext)
		{
			ExportStrategyCollection collection;

			if (exports.TryGetValue(name, out collection))
			{
				foreach (IExportStrategy exportStrategy in collection.ExportStrategies)
				{
					if (exportStrategy.MeetsCondition(injectionContext))
					{
						return exportStrategy;
					}
				}
			}

			return null;
		}

		/// <summary>
		/// Finds the best matching strategy exported by the name provided
		/// </summary>
		/// <param name="exportType"></param>
		/// <param name="injectionContext"></param>
		/// <returns></returns>
		public IExportStrategy GetStrategy(Type exportType, IInjectionContext injectionContext)
		{
			IExportStrategy exportStrategy = null;
			ExportStrategyCollection collection;

			if (exports.TryGetValue(exportType.FullName, out collection))
			{
				foreach (IExportStrategy currentExportStrategy in collection.ExportStrategies)
				{
					if (currentExportStrategy.MeetsCondition(injectionContext))
					{
						return currentExportStrategy;
					}
				}
			}

			if (exportType.IsConstructedGenericType)
			{
				Type genericType = exportType.GetGenericTypeDefinition();

				if (exports.TryGetValue(genericType.FullName, out collection))
				{
					Type[] closingTypes = exportType.GenericTypeArguments;

					foreach (IExportStrategy strategy in collection.ExportStrategies)
					{
						IGenericExportStrategy genericExportStrategy = strategy as IGenericExportStrategy;

						if (genericExportStrategy != null &&
						    genericExportStrategy.MeetsCondition(injectionContext) &&
						    genericExportStrategy.CheckGenericConstrataints(closingTypes))
						{
							if (genericExportStrategy.OwningScope != this)
							{
								exportStrategy = genericExportStrategy.OwningScope.GetStrategy(exportType, injectionContext);
							}
							else
							{
								exportStrategy = genericExportStrategy.CreateClosedStrategy(closingTypes);
							}

							if (exportStrategy != null)
							{
								AddStrategy(exportStrategy);

								break;
							}
						}
					}
				}
			}

			return exportStrategy;
		}

		/// <summary>
		/// Get the list of exported strategies sorted by best option.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="injectionContext"></param>
		/// <param name="exportFilter"></param>
		/// <returns></returns>
		public IEnumerable<IExportStrategy> GetStrategies(string name,
			IInjectionContext injectionContext,
			ExportStrategyFilter exportFilter = null)
		{
			ExportStrategyCollection returnValue;
			IInjectionContext context = injectionContext ?? CreateContext();

			name = name.ToLowerInvariant();

			if (exports.TryGetValue(name, out returnValue))
			{
				foreach (IExportStrategy exportStrategy in returnValue.ExportStrategies)
				{
					if (exportStrategy.MeetsCondition(context) &&
					    (exportFilter == null || exportFilter(injectionContext, exportStrategy)))
					{
						yield return exportStrategy;
					}
				}
			}
		}

		/// <summary>
		/// Get the list of exported strategies sorted by best option.
		/// </summary>
		/// <param name="exportType"></param>
		/// <param name="injectionContext"></param>
		/// <param name="exportFilter"></param>
		/// <returns></returns>
		public IEnumerable<IExportStrategy> GetStrategies(Type exportType,
			IInjectionContext injectionContext,
			ExportStrategyFilter exportFilter = null)
		{
			string name = exportType.FullName;
			ExportStrategyCollection returnValue;
			IInjectionContext context = injectionContext ?? CreateContext();

			if (exports.TryGetValue(name, out returnValue))
			{
				foreach (IExportStrategy exportStrategy in returnValue.ExportStrategies)
				{
					if (exportStrategy.MeetsCondition(context) &&
						 (exportFilter == null || exportFilter(injectionContext, exportStrategy)))
					{
						yield return exportStrategy;
					}
				}
			}
		}

		/// <summary>
		/// Get the export strategy collection
		/// </summary>
		/// <param name="exportName"></param>
		/// <returns>can be null if nothing is registered by that name</returns>
		public IExportStrategyCollection GetStrategyCollection(string exportName)
		{
			ExportStrategyCollection returnValue;

			if (!exports.TryGetValue(exportName, out returnValue) && ParentScope == null)
			{
				lock (exportsLock)
				{
					Dictionary<string, ExportStrategyCollection> newExports =
						new Dictionary<string, ExportStrategyCollection>(exports);

					returnValue = new ExportStrategyCollection(this, Environment, comparer);

					newExports[exportName] = returnValue;

					exports = newExports;
				}
			}

			return returnValue;
		}

		#endregion

		#region Add Remove Methods

		/// <summary>
		/// Adds a new strategy to the container
		/// </summary>
		/// <param name="addStrategy"></param>
		public void AddStrategy(IExportStrategy addStrategy)
		{
			List<IExportStrategy> newExportStrategies = new List<IExportStrategy> { addStrategy };

			addStrategy.OwningScope = this;

			addStrategy.Initialize();

			foreach (IExportStrategy secondaryStrategy in addStrategy.SecondaryStrategies())
			{
				secondaryStrategy.OwningScope = this;

				secondaryStrategy.Initialize();

				newExportStrategies.Add(secondaryStrategy);
			}

			lock (exportsLock)
			{
				Dictionary<string, ExportStrategyCollection> newExports =
					new Dictionary<string, ExportStrategyCollection>(exports);

				foreach (IExportStrategy newExportStrategy in newExportStrategies)
				{
					foreach (string exportName in newExportStrategy.ExportNames)
					{
						ExportStrategyCollection currentCollection;

						if (!newExports.TryGetValue(exportName, out currentCollection))
						{
							currentCollection = new ExportStrategyCollection(this, Environment, comparer);

							newExports[exportName] = currentCollection;
						}

						currentCollection.AddExport(newExportStrategy);
					}
				}

				exports = newExports;
			}
		}

		/// <summary>
		/// Allows the caller to remove a strategy from the container
		/// </summary>
		/// <param name="knownStrategy">strategy to remove</param>
		public void RemoveStrategy(IExportStrategy knownStrategy)
		{
			foreach (string exportName in knownStrategy.ExportNames)
			{
				ExportStrategyCollection collection;

				if (exports.TryGetValue(exportName, out collection))
				{
					collection.RemoveExport(knownStrategy);
				}
			}
		}

		#endregion

		#region LocateMissingExport

		/// <summary>
		/// Locate missing exports, this is an internal method
		/// </summary>
		/// <param name="context"></param>
		/// <param name="exportName"></param>
		/// <param name="exportType"></param>
		/// <param name="consider"></param>
		/// <returns></returns>
		public object LocateMissingExport(IInjectionContext context,
			string exportName,
			Type exportType,
			ExportStrategyFilter consider)
		{
			// skip trynig to locate,only do this from root scope
			if (ParentScope != null)
			{
				return null;
			}

			ReadOnlyCollection<ISecondaryExportLocator> tempSecondaryResolvers = secondaryResolvers;

			// loop through the list of secondary resolvers because this method is only called from ExportStrategyCollection
			// this route will not 
			if (tempSecondaryResolvers != null)
			{
				foreach (ISecondaryExportLocator secondaryDependencyResolver in tempSecondaryResolvers)
				{
					object returnValue = secondaryDependencyResolver.Locate(this, context, exportName, exportType, consider);

					if (returnValue != null)
					{
						return returnValue;
					}
				}
			}

			return ResolveUnknownExport(exportType, exportName, context, consider);
		}

		#endregion

		#region Protected Methods

		/// <summary>
		/// Dispose of this kernel and child kernels
		/// </summary>
		/// <param name="dispose"></param>
		protected override void Dispose(bool dispose)
		{
			if (disposed)
			{
				return;
			}

			if (dispose)
			{
				foreach (ExportStrategyCollection exportStrategyCollection in exports.Values)
				{
					exportStrategyCollection.Dispose();
				}

				exports = null;

				base.Dispose(true);
			}
		}

		#endregion

		#region Private Methods

		private object ResolveUnknownExport(Type resolveType,
			string resolveName,
			IInjectionContext injectionContext,
			ExportStrategyFilter consider)
		{
			if (log.IsDebugEnabled)
			{
				log.DebugFormat("Resolving unknown export with type '{0}' and name '{1}'",
					resolveType != null ? resolveType.FullName : string.Empty,
					resolveName);
			}

			object returnValue = null;

			if (kernelManager.Container != null)
			{
				returnValue = kernelManager.Container.LocateMissingExport(injectionContext, resolveName, resolveType, consider);

				if (returnValue == null &&
				    kernelManager.Container.AutoRegisterUnknown &&
				    resolveType != null &&
				    string.IsNullOrEmpty(resolveName) &&
				    !resolveType.GetTypeInfo().IsAbstract &&
				    !resolveType.GetTypeInfo().IsGenericTypeDefinition)
				{
					ConcreteAttributeExportStrategy strategy =
						new ConcreteAttributeExportStrategy(resolveType,
							resolveType.GetTypeInfo().GetCustomAttributes());

					strategy.AddExportType(resolveType);

					IInjectionScope addScope = this;

					while (addScope.ParentScope != null)
					{
						addScope = addScope.ParentScope;
					}

					addScope.AddStrategy(strategy);

					return strategy.Activate(this, injectionContext, consider);
				}
			}

			return returnValue;
		}

		private object ProcessSpecialGenericType<T>(IInjectionContext injectionContext,
			Type type,
			ExportStrategyFilter consider)
		{
			Type openGenericType = type.GetGenericTypeDefinition();
			Type exportStrategyType;

			if (type == typeof(Func<Type, object>))
			{
				return new Func<Type, object>(inType => Locate(inType, consider: consider));
			}

			if (type == typeof(Func<Type, IInjectionContext, object>))
			{
				return new Func<Type, IInjectionContext, object>((inType, context) => Locate(inType, context, consider));
			}

			if (openGenericStrategyMapping.TryGetValue(openGenericType, out exportStrategyType))
			{
				Type closedExportStrategyType = exportStrategyType.MakeGenericType(type.GenericTypeArguments);

				IExportStrategy exportStrategy = (IExportStrategy)Activator.CreateInstance(closedExportStrategyType);

				AddStrategy(exportStrategy);

				return exportStrategy.Activate(this, injectionContext, consider);
			}

			return null;
		}

		private object ProcessArrayType<T>(IInjectionContext injectionContext, Type tType, ExportStrategyFilter consider)
		{
			Type arrayStrategyType = typeof(ArrayExportStrategy<>).MakeGenericType(tType.GetElementType());

			IExportStrategy exportStrategy = (IExportStrategy)Activator.CreateInstance(arrayStrategyType);

			AddStrategy(exportStrategy);

			return exportStrategy.Activate(this, injectionContext, consider);
		}

		private object ProcessICollectionType(IInjectionContext injectionContext, Type tType, ExportStrategyFilter consider)
		{
			Type icollectionType =
				tType.GetTypeInfo()
					.ImplementedInterfaces.FirstOrDefault(
						t => t.IsConstructedGenericType && t.GetGenericTypeDefinition() == typeof(ICollection<>));

			if (icollectionType != null)
			{
				Type itemType = icollectionType.GenericTypeArguments[0];

				Type collectionStrategyType = typeof(ICollectionExportStrategy<,>).MakeGenericType(tType, itemType);

				IExportStrategy exportStrategy = (IExportStrategy)Activator.CreateInstance(collectionStrategyType);

				AddStrategy(exportStrategy);

				return exportStrategy.Activate(this, injectionContext, consider);
			}

			return null;
		}

		private object LocateOwned(Type tType, IInjectionContext injectionContext, ExportStrategyFilter consider)
		{
			return null;
		}

		private object LocateMeta(Type tType, IInjectionContext injectionContext, ExportStrategyFilter consider)
		{
			throw new NotImplementedException();
		}

		private List<T> InternalLocateAllWithContext<T>(IInjectionContext injectionContext,
			string name,
			Type locateType,
			ExportStrategyFilter exportFilter)
		{
			ExportStrategyCollection exportStrategyCollection;
			List<T> returnValue = new List<T>();

			if (exports.TryGetValue(name, out exportStrategyCollection))
			{
				returnValue.AddRange(exportStrategyCollection.ActivateAll<T>(injectionContext, exportFilter));
			}

			if (locateType != null && locateType.IsConstructedGenericType)
			{
				Type genericType = locateType.GetGenericTypeDefinition();
				Type[] genericArgs = locateType.GetTypeInfo().GenericTypeArguments;
				ExportStrategyCollection genericCollection;

				if (exports.TryGetValue(genericType.FullName, out genericCollection))
				{
					List<IExportStrategy> strategies = new List<IExportStrategy>(genericCollection.ExportStrategies);

					if (exportStrategyCollection != null)
					{
						foreach (IExportStrategy exportStrategy in exportStrategyCollection.ExportStrategies)
						{
							ICompiledExportStrategy compiledExport = exportStrategy as ICompiledExportStrategy;

							if (compiledExport != null && compiledExport.CreatingStrategy != null)
							{
								strategies.Remove(compiledExport.CreatingStrategy);
							}
						}
					}

					foreach (IExportStrategy exportStrategy in strategies)
					{
						GenericExportStrategy genericExportStrategy = exportStrategy as GenericExportStrategy;

						if (genericExportStrategy != null &&
						    genericExportStrategy.MeetsCondition(injectionContext) &&
						    genericExportStrategy.CheckGenericConstrataints(genericArgs))
						{
							IExportStrategy newStrategy =
								genericExportStrategy.CreateClosedStrategy(genericArgs);

							AddStrategy(newStrategy);

							// I'm jumping through these hoops because in a multithreaded app it's possible to 
							// create two closed strategies at the same time and we want to use the strategy that was added.
							if (exports.TryGetValue(name, out exportStrategyCollection))
							{
								IExportStrategy exportStrategyToUse = null;

								foreach (IExportStrategy strategy in exportStrategyCollection.ExportStrategies)
								{
									CompiledExportStrategy compiledExportStrategy = strategy as CompiledExportStrategy;

									if (compiledExportStrategy != null && compiledExportStrategy.CreatingStrategy == genericExportStrategy)
									{
										exportStrategyToUse = compiledExportStrategy;
									}
								}

								if (exportStrategyToUse != null)
								{
									object tempO = exportStrategyToUse.Activate(this, injectionContext, exportFilter);

									if (tempO != null)
									{
										returnValue.Add((T)tempO);
									}
								}
							}
						}
					}
				}
			}

			ReadOnlyCollection<ISecondaryExportLocator> tempResolvers = secondaryResolvers;

			if (tempResolvers != null)
			{
				foreach (ISecondaryExportLocator secondaryDependencyResolver in tempResolvers)
				{
					IEnumerable<object> locatedObjects =
						secondaryDependencyResolver.LocateAll(this,
							injectionContext,
							name,
							locateType,
							returnValue.Count == 0,
							exportFilter);

					foreach (object locatedObject in locatedObjects)
					{
						returnValue.Add((T)locatedObject);
					}
				}
			}

			if (ParentScope != null)
			{
				if (locateType != null)
				{
					foreach (T t in ParentScope.LocateAll(locateType, injectionContext, exportFilter))
					{
						returnValue.Add(t);
					}
				}
				else
				{
					foreach (T t in ParentScope.LocateAll(name, injectionContext: injectionContext, consider: exportFilter))
					{
						returnValue.Add(t);
					}
				}
			}

			return returnValue;
		}

		private string DebugDisplayString
		{
			get { return "Exports: " + GetAllStrategies(null).Count(); }
		}

		#endregion
	}
}