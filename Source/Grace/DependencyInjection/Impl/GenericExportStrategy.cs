﻿using System;
using System.Collections.Generic;
using System.Reflection;
using Grace.DependencyInjection.Impl.CompiledExport;

namespace Grace.DependencyInjection.Impl
{
	/// <summary>
	/// Represents a generic export strategy
	/// </summary>
	public class GenericExportStrategy : CompiledExportStrategy, IGenericExportStrategy
	{
		private readonly List<Type> exportAsTypes = new List<Type>(1);

		/// <summary>
		/// Default constructor
		/// </summary>
		/// <param name="exportType"></param>
		public GenericExportStrategy(Type exportType)
			: base(exportType)
		{
		}

		/// <summary>
		/// Names this strategy should be known as.
		/// </summary>
		public override IEnumerable<string> ExportNames
		{
			get
			{
				List<string> returnValue = new List<string>(exportNames);

				foreach (Type exportAsType in exportAsTypes)
				{
					returnValue.Add(exportAsType.FullName);
				}

				if (returnValue.Count == 0)
				{
					returnValue.Add(exportType.FullName);
				}

				return returnValue;
			}
		}

		/// <summary>
		/// Add an export type for the strategy
		/// </summary>
		/// <param name="exportType"></param>
		public override void AddExportType(Type exportType)
		{
			exportAsTypes.Add(exportType);
		}

		/// <summary>
		/// Activate the export
		/// </summary>
		/// <param name="exportInjectionScope"></param>
		/// <param name="context"></param>
		/// <param name="consider"></param>
		/// <returns></returns>
		public override object Activate(IInjectionScope exportInjectionScope,
			IInjectionContext context,
			ExportStrategyFilter consider)
		{
			throw new Exception("You can't activate a generic type. The type must be closed into an compiled instance export.");
		}

		/// <summary>
		/// Checks to make sure the closing types meet generic constraints
		/// </summary>
		/// <param name="closingTypes"></param>
		/// <returns></returns>
		public bool CheckGenericConstrataints(Type[] closingTypes)
		{
			Type[] exportingTypes = exportType.GetTypeInfo().GenericTypeParameters;

			if (closingTypes.Length != exportingTypes.Length)
			{
				return false;
			}

			bool constraintsMatch = true;

			for (int i = 0; i < exportingTypes.Length && constraintsMatch; i++)
			{
				Type[] constraints = exportingTypes[i].GetTypeInfo().GetGenericParameterConstraints();

				foreach (Type constraint in constraints)
				{
					if (!constraint.GetTypeInfo().IsAssignableFrom(closingTypes[i].GetTypeInfo()))
					{
						constraintsMatch = false;
						break;
					}
				}
			}

			return constraintsMatch;
		}

		/// <summary>
		/// Creates a new closed export strategy that can be activated
		/// </summary>
		/// <param name="closingTypes"></param>
		/// <returns></returns>
		public IExportStrategy CreateClosedStrategy(Type[] closingTypes)
		{
			Type closedType = exportType.MakeGenericType(closingTypes);
			TypeInfo closedTypeInfo = closedType.GetTypeInfo();
			ClosedGenericExportStrategy newExportStrategy = new ClosedGenericExportStrategy(closedType);

			if (delegateInfo.ImportProperties != null)
			{
				foreach (ImportPropertyInfo importPropertyInfo in delegateInfo.ImportProperties)
				{
					PropertyInfo propertyInfo = closedTypeInfo.GetDeclaredProperty(importPropertyInfo.Property.Name);

					ImportPropertyInfo newPropertyInfo = new ImportPropertyInfo
																	 {
																		 Property = propertyInfo,
																		 ComparerObject = importPropertyInfo.ComparerObject,
																		 ExportStrategyFilter = importPropertyInfo.ExportStrategyFilter,
																		 ImportName = importPropertyInfo.ImportName,
																		 IsRequired = importPropertyInfo.IsRequired,
																		 ValueProvider = importPropertyInfo.ValueProvider
																	 };

					newExportStrategy.ImportProperty(newPropertyInfo);
				}
			}

			if (delegateInfo.ImportMethods != null)
			{
				foreach (ImportMethodInfo importMethodInfo in delegateInfo.ImportMethods)
				{
					foreach (MethodInfo declaredMethod in closedTypeInfo.GetDeclaredMethods(importMethodInfo.MethodToImport.Name))
					{
						ParameterInfo[] importMethodParams = importMethodInfo.MethodToImport.GetParameters();
						ParameterInfo[] declaredMethodParams = declaredMethod.GetParameters();

						if (importMethodParams.Length == declaredMethodParams.Length)
						{
							ImportMethodInfo newMethodInfo = new ImportMethodInfo
							                                 {
								                                 MethodToImport = declaredMethod
							                                 };

							// fill in method parameters

							// TODO: match the parameters to make sure they are the same
							newExportStrategy.ImportMethod(newMethodInfo);
						}
					}
				}
			}

			foreach (string exportName in base.ExportNames)
			{
				newExportStrategy.AddExportName(exportName);
			}

			foreach (Type exportAsType in exportAsTypes)
			{
				if (exportAsType.GetTypeInfo().IsGenericTypeDefinition)
				{
					Type closingType = exportAsType.MakeGenericType(closingTypes);

					newExportStrategy.AddExportType(closingType);
				}
				else
				{
					newExportStrategy.AddExportType(exportAsType);
				}
			}

			if (Lifestyle != null)
			{
				newExportStrategy.SetLifestyleContainer(Lifestyle.Clone());
			}

			newExportStrategy.CreatingStrategy = this;

			return newExportStrategy;
		}
	}
}