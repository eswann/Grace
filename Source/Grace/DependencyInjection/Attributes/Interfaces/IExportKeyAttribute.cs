﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Grace.DependencyInjection.Attributes.Interfaces
{
	/// <summary>
	/// Attributes that implement this interface will be used to provide an export key
	/// </summary>
	public interface IExportKeyAttribute
	{
		/// <summary>
		/// Provide an export key
		/// </summary>
		/// <param name="attributedType">attributed type</param>
		/// <returns>export key</returns>
		object ProvideKey(Type attributedType);
	}
}
