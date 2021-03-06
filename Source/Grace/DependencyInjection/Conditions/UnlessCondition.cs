﻿namespace Grace.DependencyInjection.Conditions
{
	/// <summary>
	/// Simple condition that exports unless the condition is meet
	/// </summary>
	public class UnlessCondition : IExportCondition
	{
		private readonly ExportConditionDelegate condition;

		/// <summary>
		/// Default constructor
		/// </summary>
		/// <param name="condition"></param>
		public UnlessCondition(ExportConditionDelegate condition)
		{
			this.condition = condition;
		}

		/// <summary>
		/// Called to determine if the export strategy meets the condition to be activated
		/// </summary>
		/// <param name="scope">injection scope that this export exists in</param>
		/// <param name="injectionContext">injection context for this request</param>
		/// <param name="exportStrategy">export strategy being tested</param>
		/// <returns>true if the export meets the condition</returns>
		public bool ConditionMeet(IInjectionScope scope, IInjectionContext injectionContext, IExportStrategy exportStrategy)
		{
			return !condition(scope, injectionContext, exportStrategy);
		}
	}
}